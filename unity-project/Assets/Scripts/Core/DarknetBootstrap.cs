// DarknetBootstrap.cs — The Daemon's boot sequence
// "The Daemon is listening. The Daemon is watching. The Daemon has awakened."
// This is the entry point that brings D-Space online.

using UnityEngine;
using UnityEngine.XR.ARFoundation;
using DaemonVision.Identity;
using DaemonVision.Spatial;
using DaemonVision.HUD;
using DaemonVision.Social;
using DaemonVision.Quest;
using DaemonVision.Economy;
using DaemonVision.Network;
using DaemonVision.Detection;
using DaemonVision.Communication;
using DaemonVision.Input;

namespace DaemonVision.Core
{
    /// <summary>
    /// Bootstrap sequence — creates and registers all D-Space subsystems.
    /// Attach to the root DaemonVision GameObject in the scene.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class DarknetBootstrap : MonoBehaviour
    {
        [Header("AR Foundation")]
        [SerializeField] private ARSession arSession;
        [SerializeField] private ARSessionOrigin arSessionOrigin;
        [SerializeField] private ARCameraManager arCameraManager;
        [SerializeField] private ARAnchorManager arAnchorManager;
        [SerializeField] private ARPlaneManager arPlaneManager;
        [SerializeField] private ARRaycastManager arRaycastManager;

        [Header("Subsystem Prefabs")]
        [SerializeField] private GameObject nameplatePrefab;
        [SerializeField] private GameObject waypointPrefab;
        [SerializeField] private GameObject questThreadPrefab;
        [SerializeField] private GameObject dspaceObjectPrefab;

        private void Awake()
        {
            Debug.Log("=== DAEMON VISION ===");
            Debug.Log("\"The Daemon is awakening...\"");
            Debug.Log("Bootstrapping D-Space subsystems...");

            // Ensure core singletons exist
            EnsureComponent<ServiceLocator>();
            var dspace = EnsureComponent<DSpaceManager>();

            // Register all subsystems in dependency order
            RegisterSubsystems(dspace);
        }

        private void RegisterSubsystems(DSpaceManager dspace)
        {
            // Layer 0: Platform & Configuration
            dspace.RegisterSubsystem(EnsureComponent<GlassesProfileManager>());

            // Layer 1: Identity (must be first — everything depends on who you are)
            dspace.RegisterSubsystem(EnsureComponent<DarknetIdentityManager>());
            dspace.RegisterSubsystem(EnsureComponent<BiometricAuth>());

            // Layer 2: Spatial Awareness
            dspace.RegisterSubsystem(EnsureComponent<SpatialAnchorManager>());
            dspace.RegisterSubsystem(EnsureComponent<GPSLocationProvider>());
            dspace.RegisterSubsystem(EnsureComponent<WorldMeshManager>());

            // Layer 3: Detection (people, objects, threats)
            dspace.RegisterSubsystem(EnsureComponent<PersonDetector>());
            dspace.RegisterSubsystem(EnsureComponent<ThreatAssessment>());

            // Layer 4: Network (mesh networking — the darknet itself)
            dspace.RegisterSubsystem(EnsureComponent<MeshNetworkManager>());
            dspace.RegisterSubsystem(EnsureComponent<PeerDiscovery>());
            dspace.RegisterSubsystem(EnsureComponent<DarknetProtocol>());

            // Layer 5: Social Systems (reputation, factions, classes)
            dspace.RegisterSubsystem(EnsureComponent<ReputationSystem>());
            dspace.RegisterSubsystem(EnsureComponent<FactionManager>());
            dspace.RegisterSubsystem(EnsureComponent<ClassSystem>());
            dspace.RegisterSubsystem(EnsureComponent<LevelProgression>());

            // Layer 6: Economy
            dspace.RegisterSubsystem(EnsureComponent<DarknetEconomy>());

            // Layer 7: Quest System
            dspace.RegisterSubsystem(EnsureComponent<QuestManager>());

            // Layer 8: Communication
            dspace.RegisterSubsystem(EnsureComponent<ChatSystem>());
            dspace.RegisterSubsystem(EnsureComponent<VoiceChannelManager>());

            // Layer 9: Input
            dspace.RegisterSubsystem(EnsureComponent<GazeInputManager>());
            dspace.RegisterSubsystem(EnsureComponent<GestureRecognizer>());
            dspace.RegisterSubsystem(EnsureComponent<VoiceCommandProcessor>());

            // Layer 10: HUD Rendering (last — depends on everything else)
            dspace.RegisterSubsystem(EnsureComponent<HUDManager>());
            dspace.RegisterSubsystem(EnsureComponent<NameplateRenderer>());
            dspace.RegisterSubsystem(EnsureComponent<ThreatIndicatorRenderer>());
            dspace.RegisterSubsystem(EnsureComponent<CompassOverlay>());
            dspace.RegisterSubsystem(EnsureComponent<StatusBarRenderer>());
            dspace.RegisterSubsystem(EnsureComponent<QuestHUDRenderer>());
            dspace.RegisterSubsystem(EnsureComponent<MinimapRenderer>());

            Debug.Log($"[Bootstrap] Registered {dspace.GetType().Name} subsystems.");
        }

        private T EnsureComponent<T>() where T : Component
        {
            var component = GetComponent<T>();
            if (component == null)
                component = gameObject.AddComponent<T>();
            return component;
        }
    }
}
