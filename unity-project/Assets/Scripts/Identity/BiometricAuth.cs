// BiometricAuth.cs — Biometric authentication for D-Space access
// In the Daemon, HUD glasses are biometrically keyed — retinal scan, fingerprint,
// and in some cases fMRI-based authentication to prevent coerced access.

using System;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;

namespace DaemonVision.Identity
{
    /// <summary>
    /// Manages biometric authentication for D-Space access.
    /// On real hardware, integrates with device biometric APIs (fingerprint, face, iris).
    /// The Daemon required multi-factor biometric auth to prevent unauthorized access.
    /// </summary>
    public class BiometricAuth : SubsystemBase
    {
        public override string Name => "BiometricAuth";

        public bool IsAuthenticated { get; private set; }
        public AuthMethod LastAuthMethod { get; private set; }

        public event Action OnAuthSuccess;
        public event Action<string> OnAuthFailed;
        public event Action OnAuthRevoked;

        [Header("Auth Settings")]
        [SerializeField] private float sessionTimeoutSeconds = 3600f; // 1 hour
        [SerializeField] private bool requirePeriodicReauth = true;
        [SerializeField] private float reauthIntervalSeconds = 1800f; // 30 min

        private float timeSinceLastAuth;
        private float sessionStartTime;

        protected override Task OnInitialize()
        {
            IsAuthenticated = false;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Request authentication using the best available method on this device.
        /// </summary>
        public async Task<bool> Authenticate()
        {
            Log("Requesting biometric authentication...");

            // Check platform capabilities and attempt auth
            bool success = false;
            AuthMethod method = AuthMethod.None;

#if UNITY_ANDROID && !UNITY_EDITOR
            success = await AuthenticateAndroid();
            method = AuthMethod.DeviceBiometric;
#elif UNITY_IOS && !UNITY_EDITOR
            success = await AuthenticateIOS();
            method = AuthMethod.DeviceBiometric;
#else
            // In editor or unsupported platform, use PIN fallback
            success = await AuthenticateWithPIN();
            method = AuthMethod.PIN;
#endif

            if (success)
            {
                IsAuthenticated = true;
                LastAuthMethod = method;
                timeSinceLastAuth = 0f;
                sessionStartTime = Time.time;
                Log($"Authentication successful via {method}");
                OnAuthSuccess?.Invoke();
            }
            else
            {
                Log("Authentication failed.");
                OnAuthFailed?.Invoke("Biometric verification failed");
            }

            return success;
        }

        /// <summary>
        /// Revoke authentication — locks D-Space access.
        /// In the Daemon, removing glasses or detecting coercion triggers this.
        /// </summary>
        public void RevokeAuth()
        {
            if (!IsAuthenticated) return;

            IsAuthenticated = false;
            LastAuthMethod = AuthMethod.None;
            Log("Authentication revoked. D-Space locked.");
            OnAuthRevoked?.Invoke();
        }

        public override void Tick(float deltaTime)
        {
            if (!IsAuthenticated) return;

            timeSinceLastAuth += deltaTime;

            // Session timeout
            if (Time.time - sessionStartTime > sessionTimeoutSeconds)
            {
                Warn("Session timeout. Re-authentication required.");
                RevokeAuth();
                return;
            }

            // Periodic re-auth check
            if (requirePeriodicReauth && timeSinceLastAuth > reauthIntervalSeconds)
            {
                Warn("Periodic re-authentication required.");
                // Don't revoke — just flag for re-auth on next sensitive action
                timeSinceLastAuth = 0f;
            }
        }

        // Platform-specific auth implementations

        private Task<bool> AuthenticateAndroid()
        {
            // Uses Android BiometricPrompt API via Unity plugin
            // In production, call into AndroidJavaObject for BiometricPrompt
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                {
                    // Call companion app's biometric bridge
                    using (var biometricHelper = new AndroidJavaClass("com.daemon.vision.companion.BiometricBridge"))
                    {
                        return Task.FromResult(biometricHelper.CallStatic<bool>("authenticate", activity));
                    }
                }
            }
            catch (Exception ex)
            {
                Error($"Android biometric auth error: {ex.Message}");
                return Task.FromResult(false);
            }
        }

        private Task<bool> AuthenticateIOS()
        {
            // Uses iOS LocalAuthentication framework via native plugin
            // Placeholder — requires native Objective-C plugin
            Log("iOS biometric auth not yet implemented. Using PIN fallback.");
            return AuthenticateWithPIN();
        }

        private Task<bool> AuthenticateWithPIN()
        {
            // Fallback PIN authentication — for development/testing
            // In production, this would show a PIN entry UI
            Log("Using development bypass authentication.");
            return Task.FromResult(true);
        }
    }

    public enum AuthMethod
    {
        None,
        DeviceBiometric,  // Fingerprint, face, iris via device API
        PIN,              // Numeric PIN fallback
        Pattern,          // Gesture pattern
        VoicePrint,       // Voice biometric (advanced)
        RetinalScan       // The Daemon's preferred method
    }
}
