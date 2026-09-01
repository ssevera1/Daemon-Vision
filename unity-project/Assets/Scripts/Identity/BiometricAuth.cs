// BiometricAuth.cs - Biometric authentication for D-Space access
// In the Daemon, HUD glasses are biometrically keyed: retinal scan, fingerprint,
// and in some cases fMRI-based authentication to prevent coerced access.
//
// On Android the prompt is driven by Assets/Plugins/Android/DSpaceBiometric.java,
// which wraps the platform BiometricPrompt so it works from UnityPlayerActivity
// without the androidx dependency the companion app uses.

using System;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;

namespace DaemonVision.Identity
{
    /// <summary>
    /// Manages biometric authentication for D-Space access.
    /// On real hardware, integrates with device biometric APIs (fingerprint, face, iris).
    /// </summary>
    public class BiometricAuth : SubsystemBase
    {
        public override string Name => "BiometricAuth";

        private const string AndroidBridgeClass = "com.daemon.vision.dspace.DSpaceBiometric";
        private const int BridgeStateInProgress = 1;
        private const int BridgeStateSuccess = 2;
        private const int BridgeStateFailed = 3;

        public bool IsAuthenticated { get; private set; }
        public AuthMethod LastAuthMethod { get; private set; }

        public event Action OnAuthSuccess;
        public event Action<string> OnAuthFailed;
        public event Action OnAuthRevoked;

        [Header("Auth Settings")]
        [SerializeField] private float sessionTimeoutSeconds = 3600f; // 1 hour
        [SerializeField] private bool requirePeriodicReauth = true;
        [SerializeField] private float reauthIntervalSeconds = 1800f; // 30 min
        [SerializeField] private float promptTimeoutSeconds = 60f;

        [Tooltip("When no biometric hardware is usable, succeed anyway. Keep this on for " +
                 "Editor and phone testing; turn it off for a hardened build.")]
        [SerializeField] private bool allowDevelopmentBypass = true;

        private float timeSinceLastAuth;
        private float sessionStartTime;
        private bool authInProgress;

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
            if (authInProgress)
            {
                Warn("Authentication already in progress.");
                return false;
            }

            authInProgress = true;
            Log("Requesting biometric authentication...");

            bool success;
            AuthMethod method;

            try
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                (success, method) = await AuthenticateAndroid();
#elif UNITY_IOS && !UNITY_EDITOR
                (success, method) = await AuthenticateIOS();
#else
                (success, method) = await AuthenticateWithFallback("no platform biometrics in this build");
#endif
            }
            finally
            {
                authInProgress = false;
            }

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
        /// Revoke authentication and lock D-Space access.
        /// In the Daemon, removing the glasses or detecting coercion triggers this.
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

            if (Time.time - sessionStartTime > sessionTimeoutSeconds)
            {
                Warn("Session timeout. Re-authentication required.");
                RevokeAuth();
                return;
            }

            if (requirePeriodicReauth && timeSinceLastAuth > reauthIntervalSeconds)
            {
                Warn("Periodic re-authentication required.");
                // Don't revoke; flag for re-auth on the next sensitive action
                timeSinceLastAuth = 0f;
            }
        }

        // ----------------------------------------------------------------
        //  Platform implementations
        // ----------------------------------------------------------------

#if UNITY_ANDROID && !UNITY_EDITOR
        private async Task<(bool, AuthMethod)> AuthenticateAndroid()
        {
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var bridge = new AndroidJavaClass(AndroidBridgeClass))
                {
                    if (!bridge.CallStatic<bool>("isAvailable", activity))
                        return await AuthenticateWithFallback("no enrolled biometrics or screen lock");

                    bridge.CallStatic("reset");
                    bridge.CallStatic("authenticate", activity,
                        "D-Space Authentication", "Verify identity to access the darknet");

                    float deadline = Time.realtimeSinceStartup + promptTimeoutSeconds;
                    while (Time.realtimeSinceStartup < deadline)
                    {
                        int state = bridge.CallStatic<int>("getState");
                        if (state == BridgeStateSuccess)
                            return (true, AuthMethod.DeviceBiometric);
                        if (state == BridgeStateFailed)
                        {
                            Warn($"Biometric prompt failed: {bridge.CallStatic<string>("getLastError")}");
                            return (false, AuthMethod.DeviceBiometric);
                        }
                        if (state != BridgeStateInProgress)
                            break;

                        await Task.Delay(100);
                    }

                    Warn("Biometric prompt timed out.");
                    return (false, AuthMethod.DeviceBiometric);
                }
            }
            catch (Exception ex)
            {
                Error($"Android biometric auth error: {ex.Message}");
                return await AuthenticateWithFallback("bridge error");
            }
        }
#endif

#if UNITY_IOS && !UNITY_EDITOR
        private Task<(bool, AuthMethod)> AuthenticateIOS()
        {
            // LocalAuthentication needs a native Objective-C plugin; not wired up yet.
            return AuthenticateWithFallback("iOS LocalAuthentication plugin not implemented");
        }
#endif

        private Task<(bool, AuthMethod)> AuthenticateWithFallback(string reason)
        {
            if (allowDevelopmentBypass)
            {
                Warn($"Biometric auth unavailable ({reason}). Development bypass is ON; granting access.");
                return Task.FromResult((true, AuthMethod.DevelopmentBypass));
            }

            Warn($"Biometric auth unavailable ({reason}) and development bypass is OFF.");
            return Task.FromResult((false, AuthMethod.None));
        }
    }

    public enum AuthMethod
    {
        None,
        DeviceBiometric,    // Fingerprint, face, iris via device API
        PIN,                // Numeric PIN fallback (UI not implemented yet)
        Pattern,            // Gesture pattern
        VoicePrint,         // Voice biometric (advanced)
        RetinalScan,        // The Daemon's preferred method
        DevelopmentBypass   // Editor and test builds only
    }
}
