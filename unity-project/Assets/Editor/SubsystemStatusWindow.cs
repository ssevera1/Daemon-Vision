// SubsystemStatusWindow.cs - Editor window showing the live state of every D-Space subsystem
// Reads DSpaceManager.Subsystems directly, so the list always matches what
// DarknetBootstrap registered, in dependency order.

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using DaemonVision.Core;

namespace DaemonVision.Editor
{
    public class SubsystemStatusWindow : EditorWindow
    {
        private static readonly Color ActiveColor = new Color(0.2f, 0.9f, 0.3f, 1f);
        private static readonly Color InactiveColor = new Color(0.9f, 0.2f, 0.2f, 1f);
        private static readonly Color PendingColor = new Color(0.9f, 0.7f, 0.1f, 1f);
        private static readonly Color HeaderBgColor = new Color(0.15f, 0.15f, 0.2f, 1f);

        private Vector2 scrollPosition;
        private bool autoRefresh = true;
        private double lastRefreshTime;
        private float refreshInterval = 1.0f;

        private readonly List<SubsystemEntry> cachedSubsystems = new List<SubsystemEntry>();
        private DSpaceState cachedState = DSpaceState.Offline;

        private struct SubsystemEntry
        {
            public int Order;
            public string Name;
            public string TypeName;
            public bool IsActive;
            public bool Failed;
        }

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
                RefreshSubsystems();
            else if (state == PlayModeStateChange.ExitingPlayMode)
                cachedSubsystems.Clear();
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
            DrawHeader();

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
                    EditorApplication.isPlaying = true;
                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
                return;
            }

            DrawToolbar();

            EditorGUILayout.Space(4);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            if (cachedSubsystems.Count == 0)
            {
                EditorGUILayout.Space(20);
                EditorGUILayout.HelpBox(
                    "No subsystems found.\n\n" +
                    "DSpaceManager is not in the scene, or DarknetBootstrap has not registered anything yet. " +
                    "Click Refresh to try again.",
                    MessageType.Info);
            }
            else
            {
                for (int i = 0; i < cachedSubsystems.Count; i++)
                    DrawSubsystemEntry(cachedSubsystems[i], i);
            }

            EditorGUILayout.EndScrollView();
            DrawFooter();
        }

        private void DrawHeader()
        {
            Rect headerRect = EditorGUILayout.BeginVertical();
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, position.width, 42), HeaderBgColor);
            EditorGUILayout.Space(8);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(8);
            var titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                normal = { textColor = new Color(0f, 0.9f, 0.9f, 1f) }
            };
            EditorGUILayout.LabelField("D-SPACE SUBSYSTEM STATUS", titleStyle);
            GUILayout.FlexibleSpace();
            if (Application.isPlaying)
                GUILayout.Label($"State: {cachedState}", EditorStyles.miniLabel, GUILayout.Width(120));
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(8);
            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                RefreshSubsystems();

            GUILayout.Space(8);
            autoRefresh = GUILayout.Toggle(autoRefresh, "Auto-Refresh", EditorStyles.toolbarButton, GUILayout.Width(90));

            if (autoRefresh)
            {
                GUILayout.Space(4);
                GUILayout.Label("Interval:", GUILayout.Width(55));
                refreshInterval = EditorGUILayout.Slider(refreshInterval, 0.25f, 5f, GUILayout.Width(120));
            }

            GUILayout.FlexibleSpace();

            int activeCount = 0;
            int failedCount = 0;
            foreach (var sub in cachedSubsystems)
            {
                if (sub.IsActive) activeCount++;
                if (sub.Failed) failedCount++;
            }

            var countStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            GUILayout.Label($"Active: {activeCount}  |  Failed: {failedCount}  |  Total: {cachedSubsystems.Count}", countStyle);
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

            GUILayout.Space(8);
            Rect dotRect = GUILayoutUtility.GetRect(12, 12, GUILayout.Width(12));
            dotRect.y += 2;
            Color dotColor = entry.Failed ? InactiveColor : (entry.IsActive ? ActiveColor : PendingColor);
            EditorGUI.DrawRect(new Rect(dotRect.x + 2, dotRect.y + 2, 8, 8), dotColor);

            GUILayout.Label($"{entry.Order:00}", EditorStyles.miniLabel, GUILayout.Width(24));

            var nameStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            EditorGUILayout.LabelField(entry.Name, nameStyle, GUILayout.Width(200));

            var statusStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = dotColor },
                fontStyle = FontStyle.Bold
            };
            string status = entry.Failed ? "FAILED" : (entry.IsActive ? "ACTIVE" : "INACTIVE");
            EditorGUILayout.LabelField(status, statusStyle, GUILayout.Width(80));

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(52);
            var typeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f, 0.7f) }
            };
            EditorGUILayout.LabelField($"Type: {entry.TypeName}", typeStyle);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);
            EditorGUILayout.EndVertical();
        }

        private void DrawFooter()
        {
            EditorGUILayout.Space(4);
            Rect footerRect = EditorGUILayout.BeginHorizontal();
            EditorGUI.DrawRect(new Rect(footerRect.x, footerRect.y, position.width, 22), HeaderBgColor);

            var footerStyle = new GUIStyle(EditorStyles.miniLabel)
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

        private void RefreshSubsystems()
        {
            lastRefreshTime = EditorApplication.timeSinceStartup;
            cachedSubsystems.Clear();

            var manager = DSpaceManager.Instance;
            if (manager == null)
            {
                cachedState = DSpaceState.Offline;
                return;
            }

            cachedState = manager.State;
            var failed = new HashSet<string>(manager.FailedSubsystems);

            int order = 0;
            foreach (var subsystem in manager.Subsystems)
            {
                cachedSubsystems.Add(new SubsystemEntry
                {
                    Order = ++order,
                    Name = subsystem.Name,
                    TypeName = subsystem.GetType().FullName,
                    IsActive = subsystem.IsActive,
                    Failed = failed.Contains(subsystem.Name)
                });
            }
        }
    }
}
