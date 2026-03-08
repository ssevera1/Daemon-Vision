using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace DaemonVision.Editor
{
    /// <summary>
    /// Custom EditorWindow that displays the status of all registered DSpace subsystems.
    /// Only functional in Play Mode — subsystems are not initialized outside of runtime.
    /// </summary>
    public class SubsystemStatusWindow : EditorWindow
    {
        // ──────────────────────────────────────────────
        // Styles & Colors
        // ──────────────────────────────────────────────
        private static readonly Color ActiveColor = new Color(0.2f, 0.9f, 0.3f, 1f);
        private static readonly Color InactiveColor = new Color(0.9f, 0.2f, 0.2f, 1f);
        private static readonly Color PendingColor = new Color(0.9f, 0.7f, 0.1f, 1f);
        private static readonly Color HeaderBgColor = new Color(0.15f, 0.15f, 0.2f, 1f);

        private Vector2 scrollPosition;
        private bool autoRefresh = true;
        private double lastRefreshTime;
        private float refreshInterval = 1.0f;

        private List<SubsystemEntry> cachedSubsystems = new();

        // ──────────────────────────────────────────────
        // Subsystem entry data
        // ──────────────────────────────────────────────
        private struct SubsystemEntry
        {
            public string name;
            public string typeName;
            public bool isActive;
            public string initializationStatus; // "Running", "Stopped", "Error"
            public string details;
        }

        // ──────────────────────────────────────────────
        // Known DSpace subsystem type names (used for discovery)
        // ──────────────────────────────────────────────
        private static readonly string[] KnownSubsystemNames =
        {
            "IdentitySubsystem",
            "MeshNetworkSubsystem",
            "GPSSubsystem",
            "SpatialAnchorSubsystem",
            "QuestSubsystem",
            "PerceptionSubsystem",
            "CommunicationSubsystem",
            "VisionSubsystem",
            "CompanionRelaySubsystem"
        };

        public static void ShowWindow()
        {
            var window = GetWindow<SubsystemStatusWindow>("Subsystem Status");
            window.minSize = new Vector2(460, 400);
        }

        private void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                RefreshSubsystems();
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                cachedSubsystems.Clear();
            }
            Repaint();
        }

        private void Update()
        {
            if (autoRefresh && Application.isPlaying &&
                EditorApplication.timeSinceStartup - lastRefreshTime > refreshInterval)
            {
                RefreshSubsystems();
                Repaint();
            }
        }

        private void OnGUI()
        {
            // Header
            DrawHeader();

            // Play mode gate
            if (!Application.isPlaying)
            {
                EditorGUILayout.Space(40);
                EditorGUILayout.HelpBox(
                    "Subsystem status is only available in Play Mode.\n\n" +
                    "Enter Play Mode to inspect registered subsystems and their current state.",
                    MessageType.Warning);

                EditorGUILayout.Space(12);
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Enter Play Mode", GUILayout.Width(160), GUILayout.Height(30)))
                {
                    EditorApplication.isPlaying = true;
                }
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                return;
            }

            // Toolbar
            DrawToolbar();

            // Subsystem list
            EditorGUILayout.Space(4);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (cachedSubsystems.Count == 0)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.HelpBox(
                    "No subsystems found.\n\n" +
                    "This may mean:\n" +
                    "  - Subsystems have not been registered yet\n" +
                    "  - The DSpace bootstrap has not run\n" +
                    "  - Click Refresh to try again",
                    MessageType.Info);
            }
            else
            {
                for (int i = 0; i < cachedSubsystems.Count; i++)
                {
                    DrawSubsystemEntry(cachedSubsystems[i], i);
                }
            }

            EditorGUILayout.EndScrollView();

            // Footer
            DrawFooter();
        }

        // ──────────────────────────────────────────────
        // Drawing Helpers
        // ──────────────────────────────────────────────

        private void DrawHeader()
        {
            Rect headerRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, position.width, 42), HeaderBgColor);
            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            GUIStyle titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = new Color(0f, 0.9f, 0.9f, 1f) }
            };
            EditorGUILayout.LabelField("D-SPACE SUBSYSTEM STATUS", titleStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                RefreshSubsystems();
            }

            GUILayout.Space(8);
            autoRefresh = GUILayout.Toggle(autoRefresh, "Auto-Refresh", EditorStyles.toolbarButton, GUILayout.Width(90));

            if (autoRefresh)
            {
                GUILayout.Space(4);
                GUILayout.Label("Interval:", GUILayout.Width(55));
                refreshInterval = EditorGUILayout.Slider(refreshInterval, 0.25f, 5f, GUILayout.Width(120));
            }

            GUILayout.FlexibleSpace();

            // Summary counts
            int activeCount = 0;
            int inactiveCount = 0;
            foreach (var sub in cachedSubsystems)
            {
                if (sub.isActive) activeCount++;
                else inactiveCount++;
            }

            GUIStyle countStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight
            };
            GUILayout.Label($"Active: {activeCount}  |  Inactive: {inactiveCount}  |  Total: {cachedSubsystems.Count}", countStyle);
            GUILayout.Space(4);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSubsystemEntry(SubsystemEntry entry, int index)
        {
            Color bgColor = index % 2 == 0
                ? new Color(0.22f, 0.22f, 0.22f, 0.3f)
                : new Color(0.18f, 0.18f, 0.18f, 0.3f);

            Rect entryRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(entryRect, bgColor);

            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();

            // Status indicator dot
            GUILayout.Space(8);
            Rect dotRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
            dotRect.y += 2;
            Color dotColor = entry.isActive ? ActiveColor : InactiveColor;
            if (entry.initializationStatus == "Initializing") dotColor = PendingColor;
            EditorGUI.DrawRect(new Rect(dotRect.x + 2, dotRect.y + 2, 8, 8), dotColor);

            // Name
            GUIStyle nameStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12
            };
            EditorGUILayout.LabelField(entry.name, nameStyle, GUILayout.Width(200));

            // Status label
            GUIStyle statusStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = dotColor },
                fontStyle = FontStyle.Bold
            };
            EditorGUILayout.LabelField(entry.isActive ? "ACTIVE" : "INACTIVE", statusStyle, GUILayout.Width(70));

            // Initialization status
            GUILayout.Label($"[{entry.initializationStatus}]", GUILayout.Width(100));

            EditorGUILayout.EndHorizontal();

            // Details row
            if (!string.IsNullOrEmpty(entry.details))
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(28);
                GUIStyle detailStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    normal = { textColor = new Color(0.6f, 0.6f, 0.6f, 1f) },
                    wordWrap = true
                };
                EditorGUILayout.LabelField(entry.details, detailStyle);
                EditorGUILayout.EndHorizontal();
            }

            // Type name
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(28);
            GUIStyle typeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f, 0.7f) }
            };
            EditorGUILayout.LabelField($"Type: {entry.typeName}", typeStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(4);
            Rect footerRect = EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(new Rect(footerRect.x, footerRect.y, position.width, 22), HeaderBgColor);

            GUIStyle footerStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f, 1f) },
                alignment = TextAnchor.MiddleLeft
            };

            GUILayout.Space(8);
            string timeStr = DateTime.Now.ToString("HH:mm:ss");
            GUILayout.Label($"Last refresh: {timeStr}  |  Uptime: {Time.realtimeSinceStartup:F0}s", footerStyle);
            GUILayout.FlexibleSpace();

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        // ──────────────────────────────────────────────
        // Subsystem Discovery
        // ──────────────────────────────────────────────

        private void RefreshSubsystems()
        {
            lastRefreshTime = EditorApplication.timeSinceStartup;
            cachedSubsystems.Clear();

            // Strategy 1: Search for MonoBehaviours with "Subsystem" in name
            MonoBehaviour[] allBehaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            HashSet<string> foundNames = new HashSet<string>();

            foreach (var behaviour in allBehaviours)
            {
                if (behaviour == null) continue;

                Type type = behaviour.GetType();
                string typeName = type.Name;

                // Match known subsystem patterns
                if (typeName.Contains("Subsystem") || typeName.Contains("Manager") || typeName.Contains("Service"))
                {
                    if (type.Namespace != null && type.Namespace.StartsWith("DaemonVision"))
                    {
                        if (foundNames.Add(typeName))
                        {
                            bool isActive = behaviour.enabled && behaviour.gameObject.activeInHierarchy;

                            // Try to get initialization status via reflection
                            string initStatus = "Unknown";
                            PropertyInfo initProp = type.GetProperty("IsInitialized");
                            if (initProp != null)
                            {
                                try
                                {
                                    bool initialized = (bool)initProp.GetValue(behaviour);
                                    initStatus = initialized ? "Running" : "Initializing";
                                }
                                catch
                                {
                                    initStatus = "Error";
                                }
                            }
                            else
                            {
                                initStatus = isActive ? "Running" : "Stopped";
                            }

                            // Try to get details via reflection
                            string details = "";
                            MethodInfo statusMethod = type.GetMethod("GetStatusString");
                            if (statusMethod != null)
                            {
                                try
                                {
                                    details = (string)statusMethod.Invoke(behaviour, null);
                                }
                                catch { /* Ignore reflection failures */ }
                            }

                            cachedSubsystems.Add(new SubsystemEntry
                            {
                                name = FormatSubsystemName(typeName),
                                typeName = type.FullName ?? typeName,
                                isActive = isActive,
                                initializationStatus = initStatus,
                                details = details
                            });
                        }
                    }
                }
            }

            // Strategy 2: Check for known subsystem names that may not be MonoBehaviours
            // This covers subsystems registered via a service locator or static registry
            foreach (string knownName in KnownSubsystemNames)
            {
                if (!foundNames.Contains(knownName))
                {
                    // Try to find the type in loaded assemblies
                    Type subsystemType = null;
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        subsystemType = assembly.GetType($"DaemonVision.{knownName}") ??
                                        assembly.GetType($"DaemonVision.Subsystems.{knownName}");
                        if (subsystemType != null) break;
                    }

                    if (subsystemType != null)
                    {
                        // Check static Instance property
                        PropertyInfo instanceProp = subsystemType.GetProperty("Instance",
                            BindingFlags.Public | BindingFlags.Static);

                        bool isActive = false;
                        string initStatus = "Not Instantiated";
                        string details = "";

                        if (instanceProp != null)
                        {
                            try
                            {
                                object instance = instanceProp.GetValue(null);
                                if (instance != null)
                                {
                                    isActive = true;
                                    initStatus = "Running (Singleton)";
                                }
                            }
                            catch { /* Not instantiated */ }
                        }

                        cachedSubsystems.Add(new SubsystemEntry
                        {
                            name = FormatSubsystemName(knownName),
                            typeName = subsystemType.FullName ?? knownName,
                            isActive = isActive,
                            initializationStatus = initStatus,
                            details = details
                        });
                    }
                    else
                    {
                        // Type not found in any assembly — show as missing
                        cachedSubsystems.Add(new SubsystemEntry
                        {
                            name = FormatSubsystemName(knownName),
                            typeName = $"DaemonVision.{knownName} (not loaded)",
                            isActive = false,
                            initializationStatus = "Not Found",
                            details = "Assembly containing this subsystem is not loaded."
                        });
                    }
                }
            }

            // Sort: active first, then alphabetical
            cachedSubsystems.Sort((a, b) =>
            {
                if (a.isActive != b.isActive)
                    return a.isActive ? -1 : 1;
                return string.Compare(a.name, b.name, StringComparison.Ordinal);
            });
        }

        private static string FormatSubsystemName(string typeName)
        {
            // "MeshNetworkSubsystem" -> "Mesh Network"
            string name = typeName
                .Replace("Subsystem", "")
                .Replace("Manager", "")
                .Replace("Service", "");

            // Insert spaces before capitals
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                {
                    result.Append(' ');
                }
                result.Append(name[i]);
            }

            return result.ToString().Trim();
        }
    }
}
