// ServiceLocator.cs — Lightweight dependency injection for D-Space services
// The Daemon's architecture is distributed and modular — this mirrors that pattern

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace DaemonVision.Core
{
    public class ServiceLocator : MonoBehaviour
    {
        public static ServiceLocator Instance { get; private set; }

        private readonly Dictionary<Type, object> services = new Dictionary<Type, object>();
        private readonly Dictionary<Type, Func<object>> factories = new Dictionary<Type, Func<object>>();
        private bool coreServicesInitialized;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public void Register<T>(T service) where T : class
        {
            var type = typeof(T);
            services[type] = service;
        }

        public void RegisterFactory<T>(Func<T> factory) where T : class
        {
            factories[typeof(T)] = () => factory();
        }

        public T Get<T>() where T : class
        {
            var type = typeof(T);

            if (services.TryGetValue(type, out var service))
                return service as T;

            if (factories.TryGetValue(type, out var factory))
            {
                var instance = factory() as T;
                services[type] = instance;
                return instance;
            }

            // Try finding MonoBehaviour in scene
            if (typeof(MonoBehaviour).IsAssignableFrom(type))
            {
                var found = FindObjectOfType(type) as T;
                if (found != null)
                {
                    services[type] = found;
                    return found;
                }
            }

            Debug.LogWarning($"[ServiceLocator] Service not found: {type.Name}");
            return null;
        }

        public bool Has<T>() where T : class
        {
            return services.ContainsKey(typeof(T)) || factories.ContainsKey(typeof(T));
        }

        public void Unregister<T>() where T : class
        {
            var type = typeof(T);
            services.Remove(type);
            factories.Remove(type);
        }

        public Task InitializeCoreServices()
        {
            if (coreServicesInitialized) return Task.CompletedTask;

            // Register core platform services
            Register<ILogger>(new DSpaceLogger());
            Register<ITimeProvider>(new UnityTimeProvider());
            Register<IPlatformInfo>(new PlatformInfo());

            coreServicesInitialized = true;
            Debug.Log("[ServiceLocator] Core services initialized.");
            return Task.CompletedTask;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                services.Clear();
                factories.Clear();
                Instance = null;
            }
        }
    }

    // Core service interfaces
    public interface ILogger
    {
        void Log(string message);
        void Warn(string message);
        void Error(string message);
    }

    public interface ITimeProvider
    {
        float Time { get; }
        float DeltaTime { get; }
        double UnixTimestamp { get; }
    }

    public interface IPlatformInfo
    {
        string DeviceModel { get; }
        RuntimePlatform Platform { get; }
        bool HasGPS { get; }
        bool HasCamera { get; }
        bool HasDepthSensor { get; }
    }

    // Implementations
    public class DSpaceLogger : ILogger
    {
        public void Log(string message) => Debug.Log($"[DSpace] {message}");
        public void Warn(string message) => Debug.LogWarning($"[DSpace] {message}");
        public void Error(string message) => Debug.LogError($"[DSpace] {message}");
    }

    public class UnityTimeProvider : ITimeProvider
    {
        public float Time => UnityEngine.Time.time;
        public float DeltaTime => UnityEngine.Time.deltaTime;
        public double UnixTimestamp => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0;
    }

    public class PlatformInfo : IPlatformInfo
    {
        public string DeviceModel => SystemInfo.deviceModel;
        public RuntimePlatform Platform => Application.platform;
        public bool HasGPS => UnityEngine.Input.location.isEnabledByUser;
        public bool HasCamera => WebCamTexture.devices.Length > 0;
        public bool HasDepthSensor => false; // Set per-device in GlassesProfile
    }
}
