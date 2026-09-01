// GPSLocationProvider.cs - GPS location services for D-Space
// The Daemon's D-Space is fundamentally built on the GPS grid.
// All spatial operations depend on accurate positioning.
//
// Three sources feed this provider, in priority order:
//   1. The device's own location service (phones, Galaxy XR, Vuzix, RayNeo)
//   2. The companion phone app relaying over UDP (Quest, XREAL, Magic Leap)
//   3. A simulated position in the Editor (GPS Simulator window or DSpaceConfig)
// Startup never blocks the boot sequence: the device fix is polled from Tick().

using System;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Network;

namespace DaemonVision.Spatial
{
    public enum GPSSource
    {
        None,
        Device,
        Companion,
        Simulated
    }

    public class GPSLocationProvider : SubsystemBase
    {
        public override string Name => "GPS";

        // PlayerPrefs keys written by the Editor GPS Simulator window
        public const string SimActiveKey = "dspace_sim_active";
        public const string SimLatKey = "dspace_sim_lat";
        public const string SimLonKey = "dspace_sim_lon";
        public const string SimAltKey = "dspace_sim_alt";
        public const string SimAccuracyKey = "dspace_sim_accuracy";

        [Header("GPS Settings")]
        [SerializeField] private float desiredAccuracyMeters = 1f;
        [SerializeField] private float updateDistanceMeters = 0.5f;
        [SerializeField] private float pollIntervalSeconds = 1f;
        [SerializeField] private float deviceStartupTimeoutSeconds = 20f;
        [SerializeField] private bool useGPSSmoothing = true;
        [SerializeField] private float smoothingFactor = 0.3f;

        [Header("Companion Relay")]
        [SerializeField] private bool acceptCompanionRelay = true;
        [SerializeField] private int companionRelayPort = CompanionLocationReceiver.DefaultPort;
        [SerializeField] private float companionStaleSeconds = 10f;

        [Header("Editor Simulation")]
        [SerializeField] private bool simulateInEditor = true;

        public GPSLocation CurrentLocation { get; private set; }
        public bool HasFix { get; private set; }
        public float Accuracy { get; private set; }
        public GPSSource Source { get; private set; } = GPSSource.None;
        public bool CompanionLinkActive { get; private set; }

        public event Action<GPSLocation> OnLocationUpdated;
        public event Action<string> OnGPSError;

        private float pollTimer;
        private GPSLocation smoothedLocation;
        private bool deviceServiceStarted;
        private bool deviceServiceReady;
        private float deviceStartTime;
        private CompanionLocationReceiver companionReceiver;
        private MeshNetworkManager meshNetwork;

        protected override Task OnInitialize()
        {
            CurrentLocation = new GPSLocation();
            HasFix = false;
            Source = GPSSource.None;

            if (acceptCompanionRelay)
                StartCompanionReceiver();

#if UNITY_EDITOR
            if (simulateInEditor)
            {
                Log("Editor: device GPS is not available; using simulated or companion location.");
                return Task.CompletedTask;
            }
#endif

            StartDeviceLocationService();
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            meshNetwork = GetSubsystem<MeshNetworkManager>();
            if (companionReceiver != null)
            {
                var mesh = meshNetwork;
                companionReceiver.PeerCountProvider = () => mesh != null ? mesh.ConnectedPeerCount : 0;
            }
        }

        public override void Tick(float deltaTime)
        {
            if (deviceServiceStarted && !deviceServiceReady)
                PollDeviceStartup();

            pollTimer += deltaTime;
            if (pollTimer >= pollIntervalSeconds)
            {
                pollTimer = 0f;
                UpdateLocation();
            }
        }

        // ----------------------------------------------------------------
        //  Sources
        // ----------------------------------------------------------------

        private void StartCompanionReceiver()
        {
            try
            {
                companionReceiver = new CompanionLocationReceiver(companionRelayPort);
                companionReceiver.Start();
                Log($"Companion GPS relay listening on UDP port {companionRelayPort}");
            }
            catch (Exception ex)
            {
                Warn($"Companion GPS relay could not start on port {companionRelayPort}: {ex.Message}");
                companionReceiver?.Dispose();
                companionReceiver = null;
            }
        }

        private void StartDeviceLocationService()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.FineLocation))
            {
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.FineLocation);
            }
#endif

            if (!UnityEngine.Input.location.isEnabledByUser)
            {
                Warn("Device location is disabled or unavailable. Waiting for a companion relay.");
                OnGPSError?.Invoke("Location services disabled");
                return;
            }

            UnityEngine.Input.location.Start(desiredAccuracyMeters, updateDistanceMeters);
            deviceServiceStarted = true;
            deviceServiceReady = false;
            deviceStartTime = Time.realtimeSinceStartup;
            Log("Device location service starting...");
        }

        private void PollDeviceStartup()
        {
            var status = UnityEngine.Input.location.status;
            switch (status)
            {
                case LocationServiceStatus.Running:
                    deviceServiceReady = true;
                    Log("Device GPS online.");
                    break;

                case LocationServiceStatus.Failed:
                    deviceServiceStarted = false;
                    Error("Device GPS initialization failed.");
                    OnGPSError?.Invoke("GPS initialization failed");
                    break;

                case LocationServiceStatus.Initializing:
                    if (Time.realtimeSinceStartup - deviceStartTime > deviceStartupTimeoutSeconds)
                    {
                        deviceServiceStarted = false;
                        Warn($"Device GPS did not start within {deviceStartupTimeoutSeconds:F0}s.");
                        OnGPSError?.Invoke("GPS startup timed out");
                    }
                    break;

                default:
                    deviceServiceStarted = false;
                    break;
            }
        }

        private void UpdateLocation()
        {
            if (TryReadSimulated(out var simulated))
            {
                Apply(simulated, GPSSource.Simulated);
                return;
            }

            if (deviceServiceReady && TryReadDevice(out var device))
            {
                Apply(device, GPSSource.Device);
                return;
            }

            if (TryReadCompanion(out var companion))
            {
                Apply(companion, GPSSource.Companion);
                return;
            }

            if (HasFix)
            {
                HasFix = false;
                Source = GPSSource.None;
                Warn("Lost GPS fix.");
            }
        }

        private bool TryReadSimulated(out GPSLocation location)
        {
            location = default;
#if UNITY_EDITOR
            if (!simulateInEditor) return false;

            var config = Manager != null ? Manager.Config : null;
            bool prefsActive = PlayerPrefs.GetInt(SimActiveKey, 0) == 1;
            if (!prefsActive && (config == null || !config.SimulateGPS))
                return false;

            double lat = prefsActive ? PlayerPrefs.GetFloat(SimLatKey) : config.SimulatedLatitude;
            double lon = prefsActive ? PlayerPrefs.GetFloat(SimLonKey) : config.SimulatedLongitude;
            double alt = prefsActive ? PlayerPrefs.GetFloat(SimAltKey, 10f) : 10.0;
            float acc = prefsActive ? PlayerPrefs.GetFloat(SimAccuracyKey, 5f) : 5f;

            location = new GPSLocation
            {
                Latitude = lat,
                Longitude = lon,
                Altitude = alt,
                HorizontalAccuracy = acc,
                VerticalAccuracy = acc,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0
            };
            return true;
#else
            return false;
#endif
        }

        private bool TryReadDevice(out GPSLocation location)
        {
            location = default;
            if (UnityEngine.Input.location.status != LocationServiceStatus.Running)
            {
                deviceServiceReady = false;
                return false;
            }

            var data = UnityEngine.Input.location.lastData;
            if (data.timestamp <= 0) return false;

            location = new GPSLocation
            {
                Latitude = data.latitude,
                Longitude = data.longitude,
                Altitude = data.altitude,
                HorizontalAccuracy = data.horizontalAccuracy,
                VerticalAccuracy = data.verticalAccuracy,
                Timestamp = data.timestamp
            };
            return true;
        }

        private bool TryReadCompanion(out GPSLocation location)
        {
            location = default;
            if (companionReceiver == null) return false;

            bool fresh = companionReceiver.TryGetLatest(companionStaleSeconds, out var fix);
            if (fresh != CompanionLinkActive)
            {
                CompanionLinkActive = fresh;
                Log(fresh ? "Companion GPS link established." : "Companion GPS link lost.");
            }
            if (!fresh) return false;

            location = new GPSLocation
            {
                Latitude = fix.Latitude,
                Longitude = fix.Longitude,
                Altitude = fix.Altitude,
                HorizontalAccuracy = fix.Accuracy,
                VerticalAccuracy = fix.Accuracy,
                Timestamp = fix.TimestampMs / 1000.0
            };
            return true;
        }

        private void Apply(GPSLocation newLocation, GPSSource source)
        {
            bool sourceChanged = source != Source;
            Source = source;
            HasFix = true;
            Accuracy = newLocation.HorizontalAccuracy;

            if (useGPSSmoothing && !sourceChanged && smoothedLocation.Timestamp > 0)
            {
                // Exponential smoothing to reduce GPS jitter
                smoothedLocation.Latitude = Lerp(smoothedLocation.Latitude, newLocation.Latitude, smoothingFactor);
                smoothedLocation.Longitude = Lerp(smoothedLocation.Longitude, newLocation.Longitude, smoothingFactor);
                smoothedLocation.Altitude = Lerp(smoothedLocation.Altitude, newLocation.Altitude, smoothingFactor);
                smoothedLocation.HorizontalAccuracy = newLocation.HorizontalAccuracy;
                smoothedLocation.VerticalAccuracy = newLocation.VerticalAccuracy;
                smoothedLocation.Timestamp = newLocation.Timestamp;
                CurrentLocation = smoothedLocation;
            }
            else
            {
                smoothedLocation = newLocation;
                CurrentLocation = newLocation;
            }

            if (sourceChanged)
                Log($"Position source: {source} ({CurrentLocation.Latitude:F6}, {CurrentLocation.Longitude:F6})");

            OnLocationUpdated?.Invoke(CurrentLocation);
        }

        private static double Lerp(double a, double b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// Distance in meters between two GPS coordinates (Haversine formula).
        /// </summary>
        public static float DistanceBetween(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6378137.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return (float)(R * c);
        }

        protected override void OnShutdown()
        {
            if (deviceServiceStarted && UnityEngine.Input.location.status == LocationServiceStatus.Running)
                UnityEngine.Input.location.Stop();
            deviceServiceStarted = false;
            deviceServiceReady = false;

            companionReceiver?.Dispose();
            companionReceiver = null;
        }
    }

    [Serializable]
    public struct GPSLocation
    {
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public float HorizontalAccuracy;
        public float VerticalAccuracy;
        public double Timestamp;

        public bool IsValid => Timestamp > 0 && HorizontalAccuracy < 100f;
    }
}
