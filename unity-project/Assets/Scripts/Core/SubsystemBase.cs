// SubsystemBase.cs — Base class for all D-Space subsystems

using System.Threading.Tasks;
using UnityEngine;

namespace DaemonVision.Core
{
    /// <summary>
    /// Abstract base for D-Space subsystems. Provides common lifecycle management,
    /// logging, and access to the DSpaceManager.
    /// </summary>
    public abstract class SubsystemBase : MonoBehaviour, IDSpaceSubsystem
    {
        public abstract string Name { get; }
        public bool IsActive { get; protected set; }

        protected DSpaceManager Manager { get; private set; }

        public virtual async Task Initialize(DSpaceManager manager)
        {
            Manager = manager;
            IsActive = true;
            await OnInitialize();
            Log($"initialized.");
        }

        public virtual void OnAllSubsystemsReady() { }

        public virtual void Tick(float deltaTime) { }

        public virtual void Shutdown()
        {
            IsActive = false;
            OnShutdown();
            Log("shut down.");
        }

        protected virtual Task OnInitialize() => Task.CompletedTask;
        protected virtual void OnShutdown() { }

        protected T GetSubsystem<T>() where T : class, IDSpaceSubsystem
        {
            return Manager?.GetSubsystem<T>();
        }

        protected void Log(string message) =>
            Debug.Log($"[{Name}] {message}");

        protected void Warn(string message) =>
            Debug.LogWarning($"[{Name}] {message}");

        protected void Error(string message) =>
            Debug.LogError($"[{Name}] {message}");
    }
}
