// PersonDetector.cs — Detect and track people in the real world for D-Space overlays
// In the Daemon, HUD glasses detect people and overlay their darknet identities.
// This uses the device camera + ML-based person detection to find humans in view.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using DaemonVision.Core;

namespace DaemonVision.Detection
{
    public class PersonDetector : SubsystemBase
    {
        public override string Name => "PersonDetector";

        [Header("Detection Settings")]
        [SerializeField] private float detectionInterval = 0.2f;  // 5 FPS detection
        [SerializeField] private float maxDetectionRange = 30f;   // meters
        [SerializeField] private float personLostTimeout = 3f;    // seconds before marking lost
        [SerializeField] private float positionSmoothingFactor = 0.3f;
        [SerializeField] private bool useARKitPeopleOcclusion;

        private Camera arCamera;
        private ARCameraManager arCameraManager;
        private float detectionTimer;

        private readonly Dictionary<string, DetectedPerson> trackedPeople
            = new Dictionary<string, DetectedPerson>();
        private int nextTrackingId;

        public event Action<DetectedPerson> OnPersonDetected;
        public event Action<DetectedPerson> OnPersonUpdated;
        public event Action<string> OnPersonLost;

        public IReadOnlyDictionary<string, DetectedPerson> TrackedPeople => trackedPeople;
        public int TrackedCount => trackedPeople.Count;

        protected override Task OnInitialize()
        {
            arCamera = Manager.ARCamera;
            arCameraManager = FindObjectOfType<ARCameraManager>();

            // In production: initialize ML model for person detection
            // Options: MediaPipe Pose, Unity Barracuda + custom model, ARKit body tracking
            InitializeDetectionModel();

            return Task.CompletedTask;
        }

        public override void Tick(float deltaTime)
        {
            detectionTimer += deltaTime;

            // Update tracking timers
            var lostPeople = new List<string>();
            foreach (var kvp in trackedPeople)
            {
                kvp.Value.TimeSinceLastUpdate += deltaTime;
                if (kvp.Value.TimeSinceLastUpdate > personLostTimeout)
                    lostPeople.Add(kvp.Key);
            }

            foreach (var id in lostPeople)
            {
                trackedPeople.Remove(id);
                OnPersonLost?.Invoke(id);
            }

            // Run detection at specified interval
            if (detectionTimer >= detectionInterval)
            {
                detectionTimer = 0f;
                RunDetection();
            }
        }

        private void RunDetection()
        {
            if (arCamera == null) return;

            // In production, this would:
            // 1. Grab camera frame from ARCameraManager
            // 2. Run through ML person detection model (YOLO, MediaPipe, etc.)
            // 3. For each detected person bounding box:
            //    a. Estimate depth from AR depth buffer or stereo camera
            //    b. Project to world position
            //    c. Match with existing tracked person or create new
            //    d. Attempt identity match via mesh network + face features

            // Platform-specific detection
#if UNITY_ANDROID && !UNITY_EDITOR
            RunAndroidDetection();
#elif UNITY_IOS && !UNITY_EDITOR
            RuniOSDetection();
#else
            RunSimulatedDetection();
#endif
        }

        /// <summary>
        /// Register a detection from the platform-specific detector.
        /// </summary>
        public void RegisterDetection(Vector3 worldPosition, Rect screenBounds,
            float confidence, string matchedId = null)
        {
            if (confidence < 0.5f) return;

            // Try to match with existing tracked person
            string trackingId = matchedId ?? MatchExistingPerson(worldPosition);

            if (trackingId != null && trackedPeople.TryGetValue(trackingId, out var existing))
            {
                // Update existing
                existing.WorldPosition = Vector3.Lerp(
                    existing.WorldPosition, worldPosition, positionSmoothingFactor);
                existing.ScreenBounds = screenBounds;
                existing.Confidence = confidence;
                existing.TimeSinceLastUpdate = 0f;
                existing.DetectionCount++;

                OnPersonUpdated?.Invoke(existing);
            }
            else
            {
                // New person detected
                trackingId = $"person_{nextTrackingId++}";
                var person = new DetectedPerson
                {
                    TrackingId = trackingId,
                    WorldPosition = worldPosition,
                    ScreenBounds = screenBounds,
                    Confidence = confidence,
                    FirstDetectedTime = Time.time,
                    TimeSinceLastUpdate = 0f,
                    DetectionCount = 1
                };

                trackedPeople[trackingId] = person;
                OnPersonDetected?.Invoke(person);
            }
        }

        private string MatchExistingPerson(Vector3 position)
        {
            float closestDist = float.MaxValue;
            string closestId = null;

            foreach (var kvp in trackedPeople)
            {
                float dist = Vector3.Distance(kvp.Value.WorldPosition, position);
                if (dist < 1.5f && dist < closestDist) // Within 1.5m = same person
                {
                    closestDist = dist;
                    closestId = kvp.Key;
                }
            }

            return closestId;
        }

        private void InitializeDetectionModel()
        {
            // Placeholder — in production:
            // - Load ONNX model via Unity Barracuda/Sentis
            // - Or use platform ML Kit (Android ML Kit, iOS Vision)
            Log("Person detection model initialized.");
        }

        private void RunAndroidDetection()
        {
            // Use Android ML Kit Pose Detection or CameraX + TFLite
            // Integrate via Android native plugin
        }

        private void RuniOSDetection()
        {
            // Use ARKit body tracking (ARBodyTrackingConfiguration)
            // Provides 3D body position directly
        }

        private void RunSimulatedDetection()
        {
            // Simulation mode for editor testing
            // Raycast from camera center to find simulated people
            if (Physics.Raycast(arCamera.transform.position, arCamera.transform.forward,
                out RaycastHit hit, maxDetectionRange))
            {
                if (hit.collider.CompareTag("SimulatedPerson"))
                {
                    Vector3 screenPos = arCamera.WorldToViewportPoint(hit.point);
                    RegisterDetection(hit.point,
                        new Rect(screenPos.x - 0.05f, screenPos.y - 0.1f, 0.1f, 0.2f),
                        0.95f);
                }
            }
        }
    }

    [Serializable]
    public class DetectedPerson
    {
        public string TrackingId;
        public Vector3 WorldPosition;
        public Rect ScreenBounds;     // Normalized viewport coordinates
        public float Confidence;       // 0.0 - 1.0
        public float FirstDetectedTime;
        public float TimeSinceLastUpdate;
        public int DetectionCount;
        public string MatchedDarknetAddress; // Set if matched to a known operative
    }
}
