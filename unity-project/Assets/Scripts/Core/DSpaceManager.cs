// DSpaceManager.cs - Central coordinator for all D-Space subsystems
// Inspired by the Daemon's distributed game engine that overlays virtual space on the GPS grid

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DaemonVision.Core
{
    /// <summary>
    /// The master orchestrator for D-Space, the augmented reality darknet layer.
    /// Manages lifecycle of all subsystems: identity, spatial anchoring, HUD rendering,
    /// quest threads, reputation, mesh networking, and economy.
    ///
    /// Subsystems are isolated from each other during boot: one that throws during
    /// Initialize() is logged and shut down, and the rest still come online.
    /// That is what ADR-002 promises ("any subsystem can be disabled without
    /// affecting others"), and it is what lets a headset without GPS still run.
    /// </summary>
    public class DSpaceManager : MonoBehaviour
    {
        public static DSpaceManager Instance { get; private set; }

        [Header("D-Space Configuration")]
        [SerializeField] private DSpaceConfig config;
        [SerializeField] private bool autoInitialize = true;

        [Header("Subsystem References")]
        [SerializeField] private Transform hudRoot;
        [SerializeField] private Transform worldAnchorRoot;
        [SerializeField] private Camera arCamera;

        public DSpaceConfig Config => config;
        public Camera ARCamera => arCamera;
        public Transform HUDRoot => hudRoot;
        public Transform WorldAnchorRoot => worldAnchorRoot;

        public DSpaceState State { get; private set; } = DSpaceState.Offline;

        /// <summary>All registered subsystems in registration (dependency) order.</summary>
        public IReadOnlyList<IDSpaceSubsystem> Subsystems => subsystems;

        /// <summary>Names of subsystems whose Initialize() threw during the last boot.</summary>
        public IReadOnlyList<string> FailedSubsystems => failedSubsystems;

        public event Action<DSpaceState> OnStateChanged;
        public event Action OnDSpaceReady;
        public event Action<string> OnDSpaceError;

        private readonly List<IDSpaceSubsystem> subsystems = new List<IDSpaceSubsystem>();
        private readonly Dictionary<Type, IDSpaceSubsystem> subsystemLookup = new Dictionary<Type, IDSpaceSubsystem>();
        private readonly List<string> failedSubsystems = new List<string>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            if (config == null)
                config = Resources.Load<DSpaceConfig>("Config/DSpaceConfig");

            if (arCamera == null)
                arCamera = Camera.main;

            if (hudRoot == null)
                hudRoot = FindOrCreateChild("HUD Root");

            if (worldAnchorRoot == null)
                worldAnchorRoot = FindOrCreateChild("World Anchor Root");
        }

        private void Start()
        {
            if (autoInitialize)
                InitializeDSpace();
        }

        /// <summary>
        /// Boot sequence for D-Space, mirroring the Daemon's distributed initialization.
        /// Each subsystem comes online in dependency order.
        /// </summary>
        public async void InitializeDSpace()
        {
            if (State != DSpaceState.Offline)
            {
                Debug.LogWarning("[D-Space] Already initialized or initializing.");
                return;
            }

            SetState(DSpaceState.Booting);
            failedSubsystems.Clear();
            Debug.Log("[D-Space] === DAEMON VISION BOOT SEQUENCE ===");
            Debug.Log("[D-Space] Initializing D-Space overlay...");

            try
            {
                // Phase 1: Core services
                Debug.Log("[D-Space] Phase 1: Core services...");
                if (ServiceLocator.Instance == null)
                    throw new InvalidOperationException("ServiceLocator is missing from the scene.");
                await ServiceLocator.Instance.InitializeCoreServices();

                // Phase 2: Initialize all subsystems in dependency order
                Debug.Log("[D-Space] Phase 2: Subsystem initialization...");
                foreach (var subsystem in subsystems)
                {
                    Debug.Log($"[D-Space] Initializing: {subsystem.Name}");
                    try
                    {
                        await subsystem.Initialize(this);
                    }
                    catch (Exception ex)
                    {
                        RecordFailure(subsystem, "initialize", ex);
                    }
                }

                // Phase 3: Cross-subsystem linking
                Debug.Log("[D-Space] Phase 3: Cross-subsystem linking...");
                foreach (var subsystem in subsystems)
                {
                    if (!subsystem.IsActive) continue;
                    try
                    {
                        subsystem.OnAllSubsystemsReady();
                    }
                    catch (Exception ex)
                    {
                        RecordFailure(subsystem, "link", ex);
                    }
                }

                int active = 0;
                foreach (var subsystem in subsystems)
                    if (subsystem.IsActive) active++;

                if (subsystems.Count > 0 && active == 0)
                    throw new InvalidOperationException("Every subsystem failed to initialize.");

                SetState(DSpaceState.Online);
                Debug.Log("[D-Space] === D-SPACE ONLINE ===");
                Debug.Log($"[D-Space] {active} of {subsystems.Count} subsystems active" +
                          (failedSubsystems.Count > 0 ? $" (failed: {string.Join(", ", failedSubsystems)})" : "") + ".");
                OnDSpaceReady?.Invoke();
            }
            catch (Exception ex)
            {
                SetState(DSpaceState.Error);
                Debug.LogError($"[D-Space] Boot failure: {ex}");
                OnDSpaceError?.Invoke(ex.Message);
            }
        }

        public void RegisterSubsystem(IDSpaceSubsystem subsystem)
        {
            if (subsystem == null) return;

            if (!subsystemLookup.ContainsKey(subsystem.GetType()))
            {
                subsystems.Add(subsystem);
                subsystemLookup[subsystem.GetType()] = subsystem;
            }
        }

        public T GetSubsystem<T>() where T : class, IDSpaceSubsystem
        {
            if (subsystemLookup.TryGetValue(typeof(T), out var subsystem))
                return subsystem as T;
            return null;
        }

        public void ShutdownDSpace()
        {
            if (State == DSpaceState.Offline) return;

            Debug.Log("[D-Space] Shutting down...");
            for (int i = subsystems.Count - 1; i >= 0; i--)
            {
                if (!subsystems[i].IsActive) continue;
                try
                {
                    subsystems[i].Shutdown();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[D-Space] {subsystems[i].Name} threw during shutdown: {ex.Message}");
                }
            }
            SetState(DSpaceState.Offline);
            Debug.Log("[D-Space] === D-SPACE OFFLINE ===");
        }

        private void RecordFailure(IDSpaceSubsystem subsystem, string phase, Exception ex)
        {
            failedSubsystems.Add(subsystem.Name);
            Debug.LogError($"[D-Space] {subsystem.Name} failed to {phase}: {ex}");
            OnDSpaceError?.Invoke($"{subsystem.Name}: {ex.Message}");

            // Initialize() flips IsActive before running the subsystem's own setup,
            // so shut it down to keep a half-initialized subsystem out of Tick().
            try
            {
                if (subsystem.IsActive) subsystem.Shutdown();
            }
            catch (Exception shutdownEx)
            {
                Debug.LogWarning($"[D-Space] {subsystem.Name} also threw during cleanup: {shutdownEx.Message}");
            }
        }

        private void SetState(DSpaceState newState)
        {
            if (State == newState) return;
            State = newState;
            OnStateChanged?.Invoke(newState);
        }

        private Transform FindOrCreateChild(string childName)
        {
            var existing = transform.Find(childName);
            if (existing != null) return existing;

            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            return go.transform;
        }

        private void Update()
        {
            if (State != DSpaceState.Online) return;

            foreach (var subsystem in subsystems)
            {
                if (subsystem.IsActive)
                    subsystem.Tick(Time.deltaTime);
            }
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                ShutdownDSpace();
                Instance = null;
            }
        }
    }

    public enum DSpaceState
    {
        Offline,
        Booting,
        Online,
        Error,
        Suspended
    }

    /// <summary>
    /// Interface for all D-Space subsystems, following the Daemon's modular architecture
    /// where each capability is a discrete, independently operable module.
    /// </summary>
    public interface IDSpaceSubsystem
    {
        string Name { get; }
        bool IsActive { get; }
        System.Threading.Tasks.Task Initialize(DSpaceManager manager);
        void OnAllSubsystemsReady();
        void Tick(float deltaTime);
        void Shutdown();
    }
}
