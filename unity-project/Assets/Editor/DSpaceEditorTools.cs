using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;

namespace DaemonVision.Editor
{
    /// <summary>
    /// Editor tools for Daemon Vision development and testing.
    /// Provides menu items under DaemonVision/Tools/ for spawning test data,
    /// simulating GPS, managing identity, and inspecting subsystem state.
    /// </summary>
    public static class DSpaceEditorTools
    {
        // ──────────────────────────────────────────────
        // Constants
        // ──────────────────────────────────────────────
        private const string MenuRoot = "DaemonVision/Tools/";
        private const string OperativeTag = "DSpace_TestOperative";
        private const string AnchorTag = "DSpace_Anchor";

        private static readonly string[] FirstNames =
        {
            "Spectre", "Cipher", "Wraith", "Phantom", "Nomad",
            "Viper", "Ghost", "Raven", "Echo", "Onyx",
            "Shade", "Flux", "Drift", "Pulse", "Zero"
        };

        private static readonly string[] LastNames =
        {
            "Alpha", "Bravo", "Charlie", "Delta", "Echo",
            "Foxtrot", "Golf", "Hotel", "India", "Juliet"
        };

        private static readonly Color[] OperativeColors =
        {
            new Color(0f, 1f, 1f, 1f),       // Cyan
            new Color(1f, 0.2f, 0.4f, 1f),   // Red
            new Color(0.4f, 1f, 0.4f, 1f),   // Green
            new Color(1f, 0.8f, 0.1f, 1f),   // Gold
            new Color(0.6f, 0.4f, 1f, 1f)    // Purple
        };

        // ──────────────────────────────────────────────
        // Spawn Test Operatives
        // ──────────────────────────────────────────────
        [MenuItem(MenuRoot + "Spawn Test Operatives", false, 100)]
        public static void SpawnTestOperatives()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Daemon Vision",
                    "Enter Play Mode before spawning test operatives.",
                    "OK");
                return;
            }

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                Debug.LogError("[DSpace] No main camera found. Cannot spawn operatives.");
                return;
            }

            // Clean up any existing test operatives
            ClearTestOperatives();

            Vector3 camPos = mainCam.transform.position;
            Vector3 camFwd = mainCam.transform.forward;

            for (int i = 0; i < 5; i++)
            {
                // Distribute operatives in a semicircle in front of the camera
                float angle = -60f + (i * 30f);
                float distance = UnityEngine.Random.Range(2f, 6f);

                Quaternion rotation = Quaternion.AngleAxis(angle, Vector3.up);
                Vector3 direction = rotation * camFwd;
                Vector3 spawnPos = camPos + direction * distance;
                spawnPos.y = camPos.y + UnityEngine.Random.Range(-0.5f, 0.5f);

                // Create operative visual
                GameObject operative = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                operative.name = $"TestOperative_{i}";
                operative.tag = "Untagged"; // Unity requires tags to be defined first
                operative.transform.position = spawnPos;
                operative.transform.LookAt(camPos);

                // Apply color
                Renderer renderer = operative.GetComponent<Renderer>();
                if (renderer != null)
                {
                    Material mat = new Material(Shader.Find("Standard"));
                    Color color = OperativeColors[i % OperativeColors.Length];
                    mat.color = color;
                    mat.SetColor("_EmissionColor", color * 0.3f);
                    mat.EnableKeyword("_EMISSION");
                    renderer.material = mat;
                }

                // Add identity label
                GameObject labelObj = new GameObject("Label");
                labelObj.transform.SetParent(operative.transform);
                labelObj.transform.localPosition = new Vector3(0f, 1.2f, 0f);

                // Store identity data as a component
                OperativeTestData data = operative.AddComponent<OperativeTestData>();
                data.operativeName = $"{FirstNames[UnityEngine.Random.Range(0, FirstNames.Length)]}_{LastNames[UnityEngine.Random.Range(0, LastNames.Length)]}";
                data.operativeId = Guid.NewGuid().ToString("N").Substring(0, 12);
                data.spawnTime = Time.time;
                data.threatLevel = UnityEngine.Random.Range(0, 4);

                Debug.Log($"[DSpace] Spawned operative: {data.operativeName} (ID: {data.operativeId}) at {spawnPos}");
            }

            Debug.Log("[DSpace] 5 test operatives spawned successfully.");
        }

        private static void ClearTestOperatives()
        {
            OperativeTestData[] existing = UnityEngine.Object.FindObjectsByType<OperativeTestData>(FindObjectsSortMode.None);
            foreach (var op in existing)
            {
                UnityEngine.Object.DestroyImmediate(op.gameObject);
            }
        }

        // ──────────────────────────────────────────────
        // Create D-Space Anchor
        // ──────────────────────────────────────────────
        [MenuItem(MenuRoot + "Create D-Space Anchor", false, 200)]
        public static void CreateDSpaceAnchor()
        {
            DSpaceAnchorWindow.ShowWindow();
        }

        // ──────────────────────────────────────────────
        // Simulate GPS Location
        // ──────────────────────────────────────────────
        [MenuItem(MenuRoot + "Simulate GPS Location", false, 300)]
        public static void SimulateGPSLocation()
        {
            GPSSimulatorWindow.ShowWindow();
        }

        // ──────────────────────────────────────────────
        // Reset Local Identity
        // ──────────────────────────────────────────────
        [MenuItem(MenuRoot + "Reset Local Identity", false, 400)]
        public static void ResetLocalIdentity()
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Reset Local Identity",
                "This will clear all identity-related PlayerPrefs.\n\n" +
                "Keys to be removed:\n" +
                "  - dspace_operative_id\n" +
                "  - dspace_operative_name\n" +
                "  - dspace_callsign\n" +
                "  - dspace_public_key\n" +
                "  - dspace_private_key\n" +
                "  - dspace_identity_created\n\n" +
                "Are you sure?",
                "Reset Identity",
                "Cancel");

            if (!confirm) return;

            string[] identityKeys =
            {
                "dspace_operative_id",
                "dspace_operative_name",
                "dspace_callsign",
                "dspace_public_key",
                "dspace_private_key",
                "dspace_identity_created",
                "dspace_appearance_hash",
                "dspace_trust_score"
            };

            foreach (string key in identityKeys)
            {
                if (PlayerPrefs.HasKey(key))
                {
                    PlayerPrefs.DeleteKey(key);
                    Debug.Log($"[DSpace] Removed PlayerPref: {key}");
                }
            }

            PlayerPrefs.Save();
            Debug.Log("[DSpace] Local identity has been reset. A new identity will be generated on next launch.");
        }

        // ──────────────────────────────────────────────
        // Clear All Data
        // ──────────────────────────────────────────────
        [MenuItem(MenuRoot + "Clear All Data", false, 500)]
        public static void ClearAllData()
        {
            bool confirm = EditorUtility.DisplayDialog(
                "Clear All Data",
                "WARNING: This will delete ALL PlayerPrefs and cached data.\n\n" +
                "This includes:\n" +
                "  - Local identity\n" +
                "  - Cached anchors\n" +
                "  - Quest progress\n" +
                "  - Peer history\n" +
                "  - All saved settings\n\n" +
                "This action cannot be undone. Continue?",
                "Clear Everything",
                "Cancel");

            if (!confirm) return;

            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();

            // Also clear test operatives if in play mode
            if (Application.isPlaying)
            {
                ClearTestOperatives();
            }

            Debug.Log("[DSpace] All local data has been cleared.");
        }

        // ──────────────────────────────────────────────
        // Show Subsystem Status
        // ──────────────────────────────────────────────
        [MenuItem(MenuRoot + "Show Subsystem Status", false, 600)]
        public static void ShowSubsystemStatus()
        {
            SubsystemStatusWindow.ShowWindow();
        }

        // ──────────────────────────────────────────────
        // Generate Test Quest
        // ──────────────────────────────────────────────
        [MenuItem(MenuRoot + "Generate Test Quest", false, 700)]
        public static void GenerateTestQuest()
        {
            if (!Application.isPlaying)
            {
                EditorUtility.DisplayDialog(
                    "Daemon Vision",
                    "Enter Play Mode before generating a test quest.",
                    "OK");
                return;
            }

            Camera mainCam = Camera.main;
            if (mainCam == null)
            {
                Debug.LogError("[DSpace] No main camera found. Cannot generate quest.");
                return;
            }

            Vector3 origin = mainCam.transform.position;
            string questId = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper();

            // Create quest container
            GameObject questRoot = new GameObject($"TestQuest_{questId}");
            questRoot.transform.position = origin;

            // Quest metadata
            QuestTestData questData = questRoot.AddComponent<QuestTestData>();
            questData.questId = questId;
            questData.questTitle = GenerateQuestTitle();
            questData.questDescription = $"Navigate to all waypoints and complete objectives. Quest ID: {questId}";
            questData.createdAt = DateTime.UtcNow.ToString("o");
            questData.waypointCount = 5;
            questData.difficulty = UnityEngine.Random.Range(1, 6);

            Debug.Log($"[DSpace] ── Quest Generated ──");
            Debug.Log($"[DSpace]   ID:         {questData.questId}");
            Debug.Log($"[DSpace]   Title:      {questData.questTitle}");
            Debug.Log($"[DSpace]   Difficulty: {questData.difficulty}/5");
            Debug.Log($"[DSpace]   Waypoints:  {questData.waypointCount}");

            // Generate waypoints in a rough path around the camera
            for (int i = 0; i < questData.waypointCount; i++)
            {
                float angle = (360f / questData.waypointCount) * i;
                float radius = UnityEngine.Random.Range(3f, 10f);
                float heightOffset = UnityEngine.Random.Range(-1f, 2f);

                Vector3 waypointPos = origin + new Vector3(
                    Mathf.Cos(angle * Mathf.Deg2Rad) * radius,
                    heightOffset,
                    Mathf.Sin(angle * Mathf.Deg2Rad) * radius
                );

                // Visual marker
                GameObject waypoint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                waypoint.name = $"Waypoint_{i}_{questId}";
                waypoint.transform.SetParent(questRoot.transform);
                waypoint.transform.position = waypointPos;
                waypoint.transform.localScale = Vector3.one * 0.4f;

                // Remove collider to avoid physics interference
                UnityEngine.Object.Destroy(waypoint.GetComponent<Collider>());

                // Color: gradient from cyan to magenta along the path
                Renderer rend = waypoint.GetComponent<Renderer>();
                if (rend != null)
                {
                    Material mat = new Material(Shader.Find("Standard"));
                    float t = (float)i / (questData.waypointCount - 1);
                    Color wpColor = Color.Lerp(
                        new Color(0f, 1f, 1f, 1f),
                        new Color(1f, 0f, 1f, 1f),
                        t
                    );
                    mat.color = wpColor;
                    mat.SetColor("_EmissionColor", wpColor * 0.5f);
                    mat.EnableKeyword("_EMISSION");
                    rend.material = mat;
                }

                // Draw a line connecting to the next waypoint
                LineRenderer line = waypoint.AddComponent<LineRenderer>();
                line.startWidth = 0.02f;
                line.endWidth = 0.02f;
                line.material = new Material(Shader.Find("Sprites/Default"));
                line.startColor = new Color(0f, 1f, 1f, 0.4f);
                line.endColor = new Color(1f, 0f, 1f, 0.4f);

                if (i < questData.waypointCount - 1)
                {
                    float nextAngle = (360f / questData.waypointCount) * (i + 1);
                    float nextRadius = UnityEngine.Random.Range(3f, 10f);
                    Vector3 nextPos = origin + new Vector3(
                        Mathf.Cos(nextAngle * Mathf.Deg2Rad) * nextRadius,
                        UnityEngine.Random.Range(-1f, 2f),
                        Mathf.Sin(nextAngle * Mathf.Deg2Rad) * nextRadius
                    );
                    line.positionCount = 2;
                    line.SetPosition(0, waypointPos);
                    line.SetPosition(1, nextPos);
                }
                else
                {
                    line.positionCount = 0;
                }

                Debug.Log($"[DSpace]   Waypoint {i}: ({waypointPos.x:F1}, {waypointPos.y:F1}, {waypointPos.z:F1})");
            }

            Debug.Log($"[DSpace] ── Quest Ready ──");
            Selection.activeGameObject = questRoot;
        }

        private static string GenerateQuestTitle()
        {
            string[] prefixes = { "Operation", "Mission", "Protocol", "Directive", "Task" };
            string[] codenames =
            {
                "Shadow Gate", "Crimson Dawn", "Silent Storm", "Dark Horizon",
                "Ghost Protocol", "Iron Veil", "Neon Pulse", "Frozen Signal",
                "Binary Eclipse", "Quantum Drift", "Obsidian Key", "Violet Surge"
            };

            string prefix = prefixes[UnityEngine.Random.Range(0, prefixes.Length)];
            string codename = codenames[UnityEngine.Random.Range(0, codenames.Length)];
            return $"{prefix}: {codename}";
        }
    }

    // ──────────────────────────────────────────────
    // Runtime data components for test objects
    // ──────────────────────────────────────────────

    /// <summary>
    /// Attached to test operative GameObjects to store identity metadata.
    /// </summary>
    public class OperativeTestData : MonoBehaviour
    {
        [Header("Operative Identity")]
        public string operativeName;
        public string operativeId;
        public float spawnTime;

        [Header("Status")]
        [Range(0, 3)]
        public int threatLevel; // 0=Unknown, 1=Friendly, 2=Neutral, 3=Hostile

        public string ThreatString => threatLevel switch
        {
            0 => "UNKNOWN",
            1 => "FRIENDLY",
            2 => "NEUTRAL",
            3 => "HOSTILE",
            _ => "ERROR"
        };

        private void OnDrawGizmos()
        {
            Color gizmoColor = threatLevel switch
            {
                0 => Color.gray,
                1 => Color.green,
                2 => Color.yellow,
                3 => Color.red,
                _ => Color.white
            };

            Gizmos.color = gizmoColor;
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 1.5f, 0.3f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 2f,
                $"{operativeName}\n[{ThreatString}]\nID: {operativeId}");
#endif
        }
    }

    /// <summary>
    /// Attached to test quest root GameObjects to store quest metadata.
    /// </summary>
    public class QuestTestData : MonoBehaviour
    {
        [Header("Quest Info")]
        public string questId;
        public string questTitle;
        [TextArea(2, 4)]
        public string questDescription;
        public string createdAt;

        [Header("Parameters")]
        public int waypointCount;
        [Range(1, 5)]
        public int difficulty;

        private void OnDrawGizmos()
        {
#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 3f,
                $"QUEST: {questTitle}\nDifficulty: {difficulty}/5\nWaypoints: {waypointCount}");
#endif
        }
    }

    // ──────────────────────────────────────────────
    // D-Space Anchor Window
    // ──────────────────────────────────────────────

    /// <summary>
    /// Editor window for placing D-Space spatial anchors at specified positions.
    /// </summary>
    public class DSpaceAnchorWindow : EditorWindow
    {
        private Vector3 anchorPosition;
        private string anchorLabel = "Unnamed Anchor";
        private string anchorDescription = "";
        private int anchorType = 0;
        private readonly string[] anchorTypes = { "Generic", "Waypoint", "Message Drop", "Cache", "Portal", "Hazard" };
        private bool useSceneViewPosition = true;
        private float anchorRadius = 1.0f;

        public static void ShowWindow()
        {
            var window = GetWindow<DSpaceAnchorWindow>("Create D-Space Anchor");
            window.minSize = new Vector2(350, 320);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("D-Space Anchor Placement", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            anchorLabel = EditorGUILayout.TextField("Label", anchorLabel);
            anchorDescription = EditorGUILayout.TextField("Description", anchorDescription);
            anchorType = EditorGUILayout.Popup("Anchor Type", anchorType, anchorTypes);
            anchorRadius = EditorGUILayout.Slider("Trigger Radius", anchorRadius, 0.1f, 20f);

            EditorGUILayout.Space(8);
            useSceneViewPosition = EditorGUILayout.Toggle("Use Scene View Position", useSceneViewPosition);

            if (!useSceneViewPosition)
            {
                anchorPosition = EditorGUILayout.Vector3Field("Position", anchorPosition);
            }
            else
            {
                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView != null)
                {
                    anchorPosition = sceneView.camera.transform.position +
                                     sceneView.camera.transform.forward * 2f;
                    EditorGUILayout.HelpBox(
                        $"Position: ({anchorPosition.x:F2}, {anchorPosition.y:F2}, {anchorPosition.z:F2})\n" +
                        "Based on current Scene View camera.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("No active Scene View found.", MessageType.Warning);
                }
            }

            EditorGUILayout.Space(12);

            if (GUILayout.Button("Place Anchor", GUILayout.Height(32)))
            {
                PlaceAnchor();
            }
        }

        private void PlaceAnchor()
        {
            string anchorId = Guid.NewGuid().ToString("N").Substring(0, 10).ToUpper();

            GameObject anchorObj = new GameObject($"DSpaceAnchor_{anchorId}");
            anchorObj.transform.position = anchorPosition;

            // Visual indicator — small diamond shape
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visual.name = "AnchorVisual";
            visual.transform.SetParent(anchorObj.transform);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localScale = Vector3.one * 0.15f;
            visual.transform.localRotation = Quaternion.Euler(45f, 45f, 0f);

            Renderer rend = visual.GetComponent<Renderer>();
            if (rend != null)
            {
                Material mat = new Material(Shader.Find("Standard"));
                mat.color = new Color(0f, 1f, 1f, 0.8f);
                mat.SetColor("_EmissionColor", new Color(0f, 1f, 1f) * 0.6f);
                mat.EnableKeyword("_EMISSION");
                rend.material = mat;
            }

            // Radius indicator
            GameObject radiusIndicator = new GameObject("RadiusIndicator");
            radiusIndicator.transform.SetParent(anchorObj.transform);
            radiusIndicator.transform.localPosition = Vector3.zero;

            // Store metadata via PlayerPrefs (for persistence across sessions)
            string prefKey = $"dspace_anchor_{anchorId}";
            string json = JsonUtility.ToJson(new AnchorData
            {
                id = anchorId,
                label = anchorLabel,
                description = anchorDescription,
                type = anchorTypes[anchorType],
                radius = anchorRadius,
                posX = anchorPosition.x,
                posY = anchorPosition.y,
                posZ = anchorPosition.z,
                createdAt = DateTime.UtcNow.ToString("o")
            });
            PlayerPrefs.SetString(prefKey, json);
            PlayerPrefs.Save();

            Debug.Log($"[DSpace] Anchor placed: {anchorLabel} ({anchorTypes[anchorType]}) at {anchorPosition} [ID: {anchorId}]");

            Selection.activeGameObject = anchorObj;
            SceneView.lastActiveSceneView?.Frame(new Bounds(anchorPosition, Vector3.one * 3f), false);
        }

        [Serializable]
        private struct AnchorData
        {
            public string id;
            public string label;
            public string description;
            public string type;
            public float radius;
            public float posX, posY, posZ;
            public string createdAt;
        }
    }

    // ──────────────────────────────────────────────
    // GPS Simulator Window
    // ──────────────────────────────────────────────

    /// <summary>
    /// Editor window for simulating GPS coordinates in the editor.
    /// Sets PlayerPrefs values consumed by the runtime GPS subsystem.
    /// </summary>
    public class GPSSimulatorWindow : EditorWindow
    {
        private double latitude = 37.7749;
        private double longitude = -122.4194;
        private double altitude = 10.0;
        private float accuracy = 5.0f;
        private bool autoUpdate = false;
        private float driftSpeed = 0.00001f;
        private float driftAngle = 0f;

        private static readonly Dictionary<string, (double lat, double lon, double alt)> Presets = new()
        {
            { "San Francisco (Market St)", (37.7749, -122.4194, 10.0) },
            { "New York (Times Square)", (40.7580, -73.9855, 5.0) },
            { "London (Trafalgar Sq)", (51.5074, -0.1278, 15.0) },
            { "Tokyo (Shibuya)", (35.6595, 139.7004, 35.0) },
            { "Sydney (Opera House)", (-33.8568, 151.2153, 3.0) },
            { "Null Island (0, 0)", (0.0, 0.0, 0.0) }
        };

        private int selectedPreset = -1;
        private string[] presetNames;

        public static void ShowWindow()
        {
            var window = GetWindow<GPSSimulatorWindow>("GPS Simulator");
            window.minSize = new Vector2(380, 360);
        }

        private void OnEnable()
        {
            presetNames = new string[Presets.Count + 1];
            presetNames[0] = "-- Select Preset --";
            int idx = 1;
            foreach (var kvp in Presets)
            {
                presetNames[idx++] = kvp.Key;
            }

            // Load last values
            latitude = PlayerPrefs.GetFloat("dspace_sim_lat", 37.7749f);
            longitude = PlayerPrefs.GetFloat("dspace_sim_lon", -122.4194f);
            altitude = PlayerPrefs.GetFloat("dspace_sim_alt", 10f);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("GPS Location Simulator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Simulated GPS values are stored in PlayerPrefs and read by the " +
                "DSpace GPS subsystem when running in the Editor.",
                MessageType.Info);
            EditorGUILayout.Space(4);

            // Preset selector
            int newPreset = EditorGUILayout.Popup("Location Preset", selectedPreset < 0 ? 0 : selectedPreset, presetNames);
            if (newPreset != selectedPreset && newPreset > 0)
            {
                selectedPreset = newPreset;
                int i = 0;
                foreach (var kvp in Presets)
                {
                    i++;
                    if (i == selectedPreset)
                    {
                        latitude = kvp.Value.lat;
                        longitude = kvp.Value.lon;
                        altitude = kvp.Value.alt;
                        break;
                    }
                }
            }

            EditorGUILayout.Space(4);

            latitude = EditorGUILayout.DoubleField("Latitude", latitude);
            longitude = EditorGUILayout.DoubleField("Longitude", longitude);
            altitude = EditorGUILayout.DoubleField("Altitude (m)", altitude);
            accuracy = EditorGUILayout.Slider("Accuracy (m)", accuracy, 0.5f, 100f);

            EditorGUILayout.Space(8);
            autoUpdate = EditorGUILayout.Toggle("Enable GPS Drift", autoUpdate);
            if (autoUpdate)
            {
                driftSpeed = EditorGUILayout.Slider("Drift Speed", driftSpeed, 0.000001f, 0.001f);
                driftAngle = EditorGUILayout.Slider("Drift Bearing", driftAngle, 0f, 360f);
                EditorGUILayout.HelpBox("GPS drift simulates slow movement for testing.", MessageType.None);
            }

            EditorGUILayout.Space(12);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply Location", GUILayout.Height(28)))
            {
                ApplySimulatedLocation();
            }
            if (GUILayout.Button("Clear", GUILayout.Height(28)))
            {
                PlayerPrefs.DeleteKey("dspace_sim_lat");
                PlayerPrefs.DeleteKey("dspace_sim_lon");
                PlayerPrefs.DeleteKey("dspace_sim_alt");
                PlayerPrefs.DeleteKey("dspace_sim_accuracy");
                PlayerPrefs.DeleteKey("dspace_sim_active");
                PlayerPrefs.Save();
                Debug.Log("[DSpace] Simulated GPS location cleared.");
            }
            EditorGUILayout.EndHorizontal();
        }

        private void Update()
        {
            if (autoUpdate && Application.isPlaying)
            {
                float rad = driftAngle * Mathf.Deg2Rad;
                latitude += driftSpeed * Mathf.Cos(rad) * Time.deltaTime;
                longitude += driftSpeed * Mathf.Sin(rad) * Time.deltaTime;
                ApplySimulatedLocation();
                Repaint();
            }
        }

        private void ApplySimulatedLocation()
        {
            PlayerPrefs.SetFloat("dspace_sim_lat", (float)latitude);
            PlayerPrefs.SetFloat("dspace_sim_lon", (float)longitude);
            PlayerPrefs.SetFloat("dspace_sim_alt", (float)altitude);
            PlayerPrefs.SetFloat("dspace_sim_accuracy", accuracy);
            PlayerPrefs.SetInt("dspace_sim_active", 1);
            PlayerPrefs.Save();

            Debug.Log($"[DSpace] Simulated GPS: lat={latitude:F6}, lon={longitude:F6}, alt={altitude:F1}m, accuracy={accuracy:F1}m");
        }
    }
}
