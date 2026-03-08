// GPSLocationProvider.cs — GPS location services for D-Space
// The Daemon's D-Space is fundamentally built on the GPS grid.
// All spatial operations depend on accurate positioning.

using System;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;

namespace DaemonVision.Spatial
{
    public class GPSLocationProvider : SubsystemBase
    {
        public override string Name => "GPS";

        [Header("GPS Settings")]
        [SerializeField] private float desiredAccuracyMeters = 1f;
        [SerializeField] private float updateDistanceMeters = 0.5f;
        [SerializeField] private float pollIntervalSeconds = 1f;
        [SerializeField] private bool useGPSSmoothing = true;
        [SerializeField] private float smoothingFactor = 0.3f;

        public GPSLocation CurrentLocation { get; private set; }
        public bool HasFix { get; private set; }
        public float Accuracy { get; private set; }

        public event Action<GPSLocation> OnLocationUpdated;
        public event Action<string> OnGPSError;

        private float pollTimer;
        private GPSLocation smoothedLocation;

        protected override async Task OnInitialize()
        {
            CurrentLocation = new GPSLocation();

#if UNITY_ANDROID && !UNITY_EDITOR
            // Request location permission on Android
            if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(
                    UnityEngine.Android.Permission.FineLocation))
            {
                UnityEngine.Android.Permission.RequestUserPermission(
                    UnityEngine.Android.Permission.FineLocation);
            }
#endif

            // Start location services
            if (!UnityEngine.Input.location.isEnabledByUser)
            {
                Warn("GPS not enabled by user. D-Space spatial features limited.");
                OnGPSError?.Invoke("Location services disabled");
                return;
            }

            UnityEngine.Input.location.Start(desiredAccuracyMeters, updateDistanceMeters);

            // Wait for initialization (up to 20 seconds)
            int maxWait = 20;
            while (UnityEngine.Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
            {
                await Task.Delay(1000);
                maxWait--;
            }

            if (UnityEngine.Input.location.status == LocationServiceStatus.Failed)
            {
                Error("GPS initialization failed.");
                OnGPSError?.Invoke("GPS initialization failed");
                return;
            }

            if (UnityEngine.Input.location.status == LocationServiceStatus.Running)
            {
                HasFix = true;
                UpdateLocation();
                Log($"GPS online. Position: ({CurrentLocation.Latitude:F6}, {CurrentLocation.Longitude:F6})");
            }
        }

        public override void Tick(float deltaTime)
        {
            if (!HasFix && UnityEngine.Input.location.status != LocationServiceStatus.Running)
                return;

            pollTimer += deltaTime;
            if (pollTimer >= pollIntervalSeconds)
            {
                pollTimer = 0f;
                UpdateLocation();
            }
        }

        private void UpdateLocation()
        {
            if (UnityEngine.Input.location.status != LocationServiceStatus.Running)
            {
                HasFix = false;
                return;
            }

            var data = UnityEngine.Input.location.lastData;
            HasFix = true;
            Accuracy = data.horizontalAccuracy;

            var newLocation = new GPSLocation
            {
                Latitude = data.latitude,
                Longitude = data.longitude,
                Altitude = data.altitude,
                HorizontalAccuracy = data.horizontalAccuracy,
                VerticalAccuracy = data.verticalAccuracy,
                Timestamp = data.timestamp
            };

            if (useGPSSmoothing && smoothedLocation.Timestamp > 0)
            {
                // Exponential smoothing to reduce GPS jitter
                smoothedLocation.Latitude = Lerp(smoothedLocation.Latitude, newLocation.Latitude, smoothingFactor);
                smoothedLocation.Longitude = Lerp(smoothedLocation.Longitude, newLocation.Longitude, smoothingFactor);
                smoothedLocation.Altitude = Lerp(smoothedLocation.Altitude, newLocation.Altitude, smoothingFactor);
                smoothedLocation.HorizontalAccuracy = newLocation.HorizontalAccuracy;
                smoothedLocation.Timestamp = newLocation.Timestamp;
                CurrentLocation = smoothedLocation;
            }
            else
            {
                smoothedLocation = newLocation;
                CurrentLocation = newLocation;
            }

            OnLocationUpdated?.Invoke(CurrentLocation);
        }

        private static double Lerp(double a, double b, float t)
        {
            return a + (b - a) * t;
        }

        /// <summary>
        /// Calculate distance in meters between two GPS coordinates using Haversine formula.
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
            if (UnityEngine.Input.location.status == LocationServiceStatus.Running)
                UnityEngine.Input.location.Stop();
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
