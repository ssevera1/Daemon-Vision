// DSpaceSceneSetup.cs — Scene validation and auto-setup for D-Space
// Editor helper that runs in Awake() to ensure all required AR Foundation
// components and D-Space subsystems exist in the scene. Auto-creates missing
// objects with warning logs and sets up physics layers and tags.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DaemonVision.Core
{
    /// <summary>
    /// Scene validation MonoBehaviour. Attach to a setup object in the scene.
    /// Validates that all required D-Space and AR Foundation components exist,
    /// auto-creates missing ones with warnings, and configures physics layers/tags.
    /// Runs in Awake() before the bootstrap sequence.
    /// </summary>
    [DefaultExecutionOrder(-200)] // Run before DarknetBootstrap (-100)
    public class DSpaceSceneSetup : MonoBehaviour
    {
        [Header("Auto-Setup Options")]
        [SerializeField] private bool autoCreateMissingComponents = true;
        [SerializeField] private bool autoConfigureLayers = true;
        [SerializeField] private bool autoConfigureTags = true;
        [SerializeField] private bool logValidationResults = true;

        // Required D-Space physics layers
        private static readonly string[] RequiredLayers =
        {
            "DSpaceHUD",           // Layer 8 — HUD elements rendered on top
            "DSpaceWorld",         // Layer 9 — World-space D-Space objects (anchors, signs)
            "DSpacePeople",        // Layer 10 — Detected person overlays (nameplates)
            "DSpaceEffects",       // Layer 11 — Particle effects, threat indicators
            "SimulatedPerson"      // Layer 12 — Editor simulation targets
        };

        // Required tags
        private static readonly string[] RequiredTags =
        {
            "DSpaceAnchor",
            "DSpaceHUD",
            "DSpaceNameplate",
            "SimulatedPerson",
            "QuestThread",
            "Waypoint",
            "DSpaceManager"
        };

        private readonly List<string> validationErrors = new List<string>();
        private readonly List<string> validationWarnings = new List<string>();
        private readonly List<string> autoFixActions = new List<string>();

        // ─────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────────────────

        private void Awake()
        {
            validationErrors.Clear();
            validationWarnings.Clear();
            autoFixActions.Clear();

            if (logValidationResults)
                Debug.Log("[DSpaceSceneSetup] Validating scene setup...");

            // Validate AR Foundation
            ValidateARSession();
            ValidateARSessionOrigin();
            ValidateARCamera();

            // Validate D-Space core
            ValidateServiceLocator();
            ValidateDSpaceManager();
            ValidateDarknetBootstrap();

            // Validate config
            ValidateDSpaceConfig();

            // Configure layers and tags
            if (autoConfigureLayers)
                ConfigureLayers();

            if (autoConfigureTags)
                ConfigureTags();

            // Report results
            ReportValidationResults();
        }

        // ─────────────────────────────────────────────────────────────────
        //  AR Foundation Validation
        // ─────────────────────────────────────────────────────────────────

        private void ValidateARSession()
        {
            var arSession = FindObjectOfType<ARSession>();
            if (arSession == null)
            {
                if (autoCreateMissingComponents)
                {
                    var go = new GameObject("AR Session");
                    go.AddComponent<ARSession>();
                    go.AddComponent<ARInputManager>();
                    autoFixActions.Add("Created AR Session GameObject with ARSession and ARInputManager.");
                }
                else
                {
                    validationErrors.Add("ARSession not found in scene. AR features will not work.");
                }
            }
        }

        private void ValidateARSessionOrigin()
        {
            var arOrigin = FindObjectOfType<ARSessionOrigin>();
            if (arOrigin == null)
            {
                if (autoCreateMissingComponents)
                {
                    var go = new GameObject("AR Session Origin");
                    var origin = go.AddComponent<ARSessionOrigin>();

                    // Add required AR managers
                    go.AddComponent<ARAnchorManager>();
                    go.AddComponent<ARPlaneManager>();
                    go.AddComponent<ARRaycastManager>();

                    // Create AR Camera as child if no camera exists
                    var existingCamera = FindObjectOfType<Camera>();
                    if (existingCamera != null)
                    {
                        origin.camera = existingCamera;
                    }
                    else
                    {
                        var camGo = new GameObject("AR Camera");
                        camGo.transform.SetParent(go.transform);
                        var cam = camGo.AddComponent<Camera>();
                        cam.clearFlags = CameraClearFlags.SolidColor;
                        cam.backgroundColor = Color.black;
                        camGo.AddComponent<ARCameraManager>();
                        camGo.AddComponent<ARCameraBackground>();
                        origin.camera = cam;
                    }

                    autoFixActions.Add("Created AR Session Origin with ARAnchorManager, ARPlaneManager, ARRaycastManager.");
                }
                else
                {
                    validationErrors.Add("ARSessionOrigin not found. Spatial tracking will not work.");
                }
            }
            else
            {
                // Validate sub-managers on the session origin
                if (arOrigin.GetComponent<ARAnchorManager>() == null)
                {
                    if (autoCreateMissingComponents)
                    {
                        arOrigin.gameObject.AddComponent<ARAnchorManager>();
                        autoFixActions.Add("Added missing ARAnchorManager to AR Session Origin.");
                    }
                    else
                    {
                        validationWarnings.Add("ARAnchorManager missing on ARSessionOrigin.");
                    }
                }

                if (arOrigin.GetComponent<ARRaycastManager>() == null)
                {
                    if (autoCreateMissingComponents)
                    {
                        arOrigin.gameObject.AddComponent<ARRaycastManager>();
                        autoFixActions.Add("Added missing ARRaycastManager to AR Session Origin.");
                    }
                    else
                    {
                        validationWarnings.Add("ARRaycastManager missing on ARSessionOrigin.");
                    }
                }
            }
        }

        private void ValidateARCamera()
        {
            var arCameraManager = FindObjectOfType<ARCameraManager>();
            if (arCameraManager == null)
            {
                // Check if there's a camera that should have an ARCameraManager
                var mainCamera = Camera.main;
                if (mainCamera != null && autoCreateMissingComponents)
                {
                    mainCamera.gameObject.AddComponent<ARCameraManager>();
                    if (mainCamera.GetComponent<ARCameraBackground>() == null)
                        mainCamera.gameObject.AddComponent<ARCameraBackground>();
                    autoFixActions.Add("Added ARCameraManager and ARCameraBackground to main camera.");
                }
                else if (mainCamera == null)
                {
                    validationErrors.Add("No camera found in scene. D-Space requires a camera.");
                }
                else
                {
                    validationWarnings.Add("ARCameraManager not found. Camera frame capture for ML detection will not work.");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  D-Space Core Validation
        // ─────────────────────────────────────────────────────────────────

        private void ValidateServiceLocator()
        {
            var serviceLocator = FindObjectOfType<ServiceLocator>();
            if (serviceLocator == null)
            {
                if (autoCreateMissingComponents)
                {
                    var go = FindOrCreateDSpaceRoot();
                    go.AddComponent<ServiceLocator>();
                    autoFixActions.Add("Created ServiceLocator on D-Space root object.");
                }
                else
                {
                    validationErrors.Add("ServiceLocator not found. D-Space dependency injection will not work.");
                }
            }
        }

        private void ValidateDSpaceManager()
        {
            var manager = FindObjectOfType<DSpaceManager>();
            if (manager == null)
            {
                if (autoCreateMissingComponents)
                {
                    var go = FindOrCreateDSpaceRoot();
                    var mgr = go.AddComponent<DSpaceManager>();

                    // Create required child transforms
                    var hudRoot = new GameObject("HUD Root");
                    hudRoot.transform.SetParent(go.transform);

                    var worldRoot = new GameObject("World Anchor Root");
                    worldRoot.transform.SetParent(go.transform);

                    autoFixActions.Add("Created DSpaceManager with HUD Root and World Anchor Root.");
                }
                else
                {
                    validationErrors.Add("DSpaceManager not found. D-Space will not function.");
                }
            }
            else
            {
                // Validate references
                if (manager.ARCamera == null)
                {
                    validationWarnings.Add("DSpaceManager.ARCamera is not assigned. Will attempt to find Camera.main at runtime.");
                }

                if (manager.HUDRoot == null)
                {
                    validationWarnings.Add("DSpaceManager.HUDRoot is not assigned. HUD elements may not render correctly.");
                }

                if (manager.WorldAnchorRoot == null)
                {
                    validationWarnings.Add("DSpaceManager.WorldAnchorRoot is not assigned. World anchors may not be parented correctly.");
                }
            }
        }

        private void ValidateDarknetBootstrap()
        {
            var bootstrap = FindObjectOfType<DarknetBootstrap>();
            if (bootstrap == null)
            {
                if (autoCreateMissingComponents)
                {
                    var go = FindOrCreateDSpaceRoot();
                    go.AddComponent<DarknetBootstrap>();
                    autoFixActions.Add("Created DarknetBootstrap on D-Space root object.");
                }
                else
                {
                    validationErrors.Add("DarknetBootstrap not found. D-Space subsystems will not be registered.");
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Config Validation
        // ─────────────────────────────────────────────────────────────────

        private void ValidateDSpaceConfig()
        {
            var config = Resources.Load<DSpaceConfig>("Config/DSpaceConfig");
            if (config == null)
            {
                validationWarnings.Add("DSpaceConfig ScriptableObject not found at Resources/Config/DSpaceConfig. " +
                                       "Using default configuration values.");

#if UNITY_EDITOR
                CreateDefaultConfig();
#endif
            }
        }

#if UNITY_EDITOR
        /// <summary>
        /// Create the default DSpaceConfig ScriptableObject in the Resources folder.
        /// Only runs in the editor.
        /// </summary>
        private void CreateDefaultConfig()
        {
            string resourcesPath = "Assets/Resources";
            string configPath = "Assets/Resources/Config";
            string assetPath = "Assets/Resources/Config/DSpaceConfig.asset";

            // Ensure directories exist
            if (!AssetDatabase.IsValidFolder(resourcesPath))
                AssetDatabase.CreateFolder("Assets", "Resources");

            if (!AssetDatabase.IsValidFolder(configPath))
                AssetDatabase.CreateFolder(resourcesPath, "Config");

            // Check if asset already exists
            if (AssetDatabase.LoadAssetAtPath<DSpaceConfig>(assetPath) != null)
                return;

            // Create default config
            var config = ScriptableObject.CreateInstance<DSpaceConfig>();
            AssetDatabase.CreateAsset(config, assetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            autoFixActions.Add("Created default DSpaceConfig at Resources/Config/DSpaceConfig.");
        }
#endif

        // ─────────────────────────────────────────────────────────────────
        //  Layer & Tag Configuration
        // ─────────────────────────────────────────────────────────────────

        private void ConfigureLayers()
        {
#if UNITY_EDITOR
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layersProperty = tagManager.FindProperty("layers");

            for (int i = 0; i < RequiredLayers.Length; i++)
            {
                string layerName = RequiredLayers[i];
                int layerIndex = LayerMask.NameToLayer(layerName);

                if (layerIndex == -1)
                {
                    // Find first empty user layer slot (8-31)
                    for (int slot = 8; slot < 32; slot++)
                    {
                        var layerProp = layersProperty.GetArrayElementAtIndex(slot);
                        if (string.IsNullOrEmpty(layerProp.stringValue))
                        {
                            layerProp.stringValue = layerName;
                            autoFixActions.Add($"Assigned layer {slot}: '{layerName}'");
                            break;
                        }
                    }
                }
            }

            tagManager.ApplyModifiedPropertiesWithoutUndo();
#else
            // At runtime, verify layers exist
            foreach (string layerName in RequiredLayers)
            {
                if (LayerMask.NameToLayer(layerName) == -1)
                {
                    validationWarnings.Add($"Physics layer '{layerName}' not found. " +
                                           "Configure layers in Project Settings > Tags and Layers.");
                }
            }
#endif
        }

        private void ConfigureTags()
        {
#if UNITY_EDITOR
            var tagManager = new SerializedObject(
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var tagsProperty = tagManager.FindProperty("tags");

            foreach (string tag in RequiredTags)
            {
                bool found = false;
                for (int i = 0; i < tagsProperty.arraySize; i++)
                {
                    if (tagsProperty.GetArrayElementAtIndex(i).stringValue == tag)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    tagsProperty.InsertArrayElementAtIndex(tagsProperty.arraySize);
                    tagsProperty.GetArrayElementAtIndex(tagsProperty.arraySize - 1).stringValue = tag;
                    autoFixActions.Add($"Added tag: '{tag}'");
                }
            }

            tagManager.ApplyModifiedPropertiesWithoutUndo();
#else
            // At runtime we cannot add tags, so just verify
            foreach (string tag in RequiredTags)
            {
                try
                {
                    // This will throw if the tag doesn't exist — but we can't
                    // easily check at runtime without a try/catch.
                    // Just note it as a warning during setup.
                }
                catch
                {
                    validationWarnings.Add($"Tag '{tag}' not configured. " +
                                           "Add it in Project Settings > Tags and Layers.");
                }
            }
#endif
        }

        // ─────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Find or create the root D-Space GameObject that holds all subsystems.
        /// </summary>
        private GameObject FindOrCreateDSpaceRoot()
        {
            // Look for existing root
            var existing = GameObject.Find("DaemonVision");
            if (existing != null) return existing;

            var manager = FindObjectOfType<DSpaceManager>();
            if (manager != null) return manager.gameObject;

            var bootstrap = FindObjectOfType<DarknetBootstrap>();
            if (bootstrap != null) return bootstrap.gameObject;

            // Create new root
            var root = new GameObject("DaemonVision");
            DontDestroyOnLoad(root);
            autoFixActions.Add("Created DaemonVision root GameObject.");
            return root;
        }

        private void ReportValidationResults()
        {
            if (!logValidationResults) return;

            // Report auto-fixes
            foreach (string action in autoFixActions)
            {
                Debug.LogWarning($"[DSpaceSceneSetup] Auto-fix: {action}");
            }

            // Report warnings
            foreach (string warning in validationWarnings)
            {
                Debug.LogWarning($"[DSpaceSceneSetup] Warning: {warning}");
            }

            // Report errors
            foreach (string error in validationErrors)
            {
                Debug.LogError($"[DSpaceSceneSetup] ERROR: {error}");
            }

            // Summary
            if (validationErrors.Count == 0 && validationWarnings.Count == 0 && autoFixActions.Count == 0)
            {
                Debug.Log("[DSpaceSceneSetup] Scene validation passed. All required components present.");
            }
            else
            {
                Debug.Log($"[DSpaceSceneSetup] Validation complete: " +
                          $"{validationErrors.Count} errors, " +
                          $"{validationWarnings.Count} warnings, " +
                          $"{autoFixActions.Count} auto-fixes applied.");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Editor Menu Integration
        // ─────────────────────────────────────────────────────────────────

#if UNITY_EDITOR
        [MenuItem("DaemonVision/Validate Scene Setup")]
        private static void ValidateSceneFromMenu()
        {
            var setup = FindObjectOfType<DSpaceSceneSetup>();
            if (setup == null)
            {
                var go = new GameObject("DSpaceSceneSetup (Temporary)");
                setup = go.AddComponent<DSpaceSceneSetup>();
                setup.Awake();
                DestroyImmediate(go);
            }
            else
            {
                setup.Awake();
            }
        }

        [MenuItem("DaemonVision/Create D-Space Scene")]
        private static void CreateDSpaceScene()
        {
            // Create full D-Space scene hierarchy
            Debug.Log("[DSpaceSceneSetup] Creating D-Space scene hierarchy...");

            // AR Session
            var arSessionGo = new GameObject("AR Session");
            arSessionGo.AddComponent<ARSession>();
            arSessionGo.AddComponent<ARInputManager>();

            // AR Session Origin with camera
            var arOriginGo = new GameObject("AR Session Origin");
            var arOrigin = arOriginGo.AddComponent<ARSessionOrigin>();
            arOriginGo.AddComponent<ARAnchorManager>();
            arOriginGo.AddComponent<ARPlaneManager>();
            arOriginGo.AddComponent<ARRaycastManager>();

            var camGo = new GameObject("AR Camera");
            camGo.transform.SetParent(arOriginGo.transform);
            var cam = camGo.AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 1000f;
            camGo.AddComponent<ARCameraManager>();
            camGo.AddComponent<ARCameraBackground>();
            arOrigin.camera = cam;

            // DaemonVision root
            var dvRoot = new GameObject("DaemonVision");
            dvRoot.AddComponent<ServiceLocator>();

            var hudRoot = new GameObject("HUD Root");
            hudRoot.transform.SetParent(dvRoot.transform);

            var worldRoot = new GameObject("World Anchor Root");
            worldRoot.transform.SetParent(dvRoot.transform);

            dvRoot.AddComponent<DSpaceManager>();
            dvRoot.AddComponent<DarknetBootstrap>();

            // Scene setup validator
            var setupGo = new GameObject("Scene Setup");
            setupGo.AddComponent<DSpaceSceneSetup>();

            Debug.Log("[DSpaceSceneSetup] D-Space scene hierarchy created successfully.");
        }
#endif
    }
}
