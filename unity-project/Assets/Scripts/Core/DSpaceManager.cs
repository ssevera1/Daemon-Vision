// DSpaceManager.cs — Central coordinator for all D-Space subsystems
// Inspired by the Daemon's distributed game engine that overlays virtual space on the GPS grid

using System;
using System.Collections.Generic;
using UnityEngine;

namespace DaemonVision.Core
{
    /// <summary>
    /// The master orchestrator for D-Space — the augmented reality darknet layer.
    /// Manages lifecycle of all subsystems: identity, spatial anchoring, HUD rendering,
    /// quest threads, reputation, mesh networking, and economy.
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

        public event Action<DSpaceState> OnStateChanged;
        public event Action OnDSpaceReady;
        public event Action<string> OnDSpaceError;

        private readonly List<IDSpaceSubsystem> subsystems = new List<IDSpaceSubsystem>();
        private readonly Dictionary<Type, IDSpaceSubsystem> subsystemLookup = new Dictionary<Type, IDSpaceSubsystem>();

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
        }

        private void Start()
        {
            if (autoInitialize)
                InitializeDSpace();
        }

        /// <summary>
        /// Boot sequence for D-Space — mirrors the Daemon's distributed initialization.
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
            Debug.Log("[D-Space] === DAEMON VISION BOOT SEQUENCE ===");
            Debug.Log("[D-Space] Initializing D-Space overlay...");

            try
            {
                // Phase 1: Core services
                Debug.Log("[D-Space] Phase 1: Core services...");
                await ServiceLocator.Instance.InitializeCoreServices();

                // Phase 2: Register and initialize all subsystems in dependency order
                Debug.Log("[D-Space] Phase 2: Subsystem initialization...");
                foreach (var subsystem in subsystems)
                {
                    Debug.Log($"[D-Space] Initializing: {subsystem.Name}");
                    await subsystem.Initialize(this);
                }

                // Phase 3: Cross-subsystem linking
                Debug.Log("[D-Space] Phase 3: Cross-subsystem linking...");
                foreach (var subsystem in subsystems)
                {
                    subsystem.OnAllSubsystemsReady();
                }

                SetState(DSpaceState.Online);
                Debug.Log("[D-Space] === D-SPACE ONLINE ===");
                Debug.Log($"[D-Space] {subsystems.Count} subsystems active.");
                OnDSpaceReady?.Invoke();
            }
            catch (Exception ex)
            {
                SetState(DSpaceState.Error);
                Debug.LogError($"[D-Space] Boot failure: {ex.Message}");
                OnDSpaceError?.Invoke(ex.Message);
            }
        }

        public void RegisterSubsystem(IDSpaceSubsystem subsystem)
        {
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
            Debug.Log("[D-Space] Shutting down...");
            for (int i = subsystems.Count - 1; i >= 0; i--)
            {
                subsystems[i].Shutdown();
            }
            SetState(DSpaceState.Offline);
            Debug.Log("[D-Space] === D-SPACE OFFLINE ===");
        }

        private void SetState(DSpaceState newState)
        {
            if (State == newState) return;
            State = newState;
            OnStateChanged?.Invoke(newState);
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
    /// Interface for all D-Space subsystems — follows the Daemon's modular architecture
    /// where each capability is a discrete, independently-operable module.
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
