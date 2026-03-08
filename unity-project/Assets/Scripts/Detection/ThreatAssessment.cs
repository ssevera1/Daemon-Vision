// ThreatAssessment.cs — D-Space threat detection and assessment
// In the Daemon, hostiles are highlighted with red outlines. The system
// assesses threats based on behavior, proximity, speed, and network intel.
// This is NOT weapons detection — it's behavioral threat assessment.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Identity;

namespace DaemonVision.Detection
{
    public class ThreatAssessment : SubsystemBase
    {
        public override string Name => "ThreatAssessment";

        [Header("Assessment Settings")]
        [SerializeField] private float assessmentInterval = 1f;
        [SerializeField] private float approachSpeedThreshold = 3f;    // m/s — running toward you
        [SerializeField] private float proximityAlertDistance = 5f;      // meters
        [SerializeField] private float threatDecayRate = 0.1f;           // per second

        private PersonDetector personDetector;
        private DarknetIdentityManager identityManager;

        private readonly Dictionary<string, ThreatInfo> activeThreatTable
            = new Dictionary<string, ThreatInfo>();

        // Position history for velocity calculation
        private readonly Dictionary<string, Queue<TimestampedPosition>> positionHistory
            = new Dictionary<string, Queue<TimestampedPosition>>();

        private float assessmentTimer;

        public event Action<ThreatInfo> OnThreatDetected;
        public event Action<ThreatInfo> OnThreatUpdated;
        public event Action<string> OnThreatCleared;

        protected override Task OnInitialize()
        {
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            personDetector = GetSubsystem<PersonDetector>();
            identityManager = GetSubsystem<DarknetIdentityManager>();

            if (personDetector != null)
            {
                personDetector.OnPersonUpdated += TrackPosition;
                personDetector.OnPersonLost += HandlePersonLost;
            }
        }

        public override void Tick(float deltaTime)
        {
            assessmentTimer += deltaTime;

            // Decay existing threat scores
            var toRemove = new List<string>();
            foreach (var kvp in activeThreatTable)
            {
                kvp.Value.ThreatScore -= threatDecayRate * deltaTime;
                if (kvp.Value.ThreatScore <= 0)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (var id in toRemove)
            {
                activeThreatTable.Remove(id);
                OnThreatCleared?.Invoke(id);
            }

            // Run assessment
            if (assessmentTimer >= assessmentInterval)
            {
                assessmentTimer = 0f;
                RunAssessment();
            }
        }

        private void RunAssessment()
        {
            if (personDetector == null) return;

            var camera = Manager.ARCamera;
            if (camera == null) return;

            Vector3 myPosition = camera.transform.position;

            foreach (var kvp in personDetector.TrackedPeople)
            {
                var person = kvp.Value;
                float threatScore = 0f;

                // Factor 1: Proximity (closer = higher threat if approaching)
                float distance = Vector3.Distance(myPosition, person.WorldPosition);
                if (distance < proximityAlertDistance)
                {
                    threatScore += (1f - distance / proximityAlertDistance) * 30f;
                }

                // Factor 2: Approach speed
                float approachSpeed = CalculateApproachSpeed(person.TrackingId, myPosition);
                if (approachSpeed > approachSpeedThreshold)
                {
                    threatScore += (approachSpeed / approachSpeedThreshold) * 20f;
                }

                // Factor 3: Network intelligence — check if flagged by other operatives
                if (!string.IsNullOrEmpty(person.MatchedDarknetAddress))
                {
                    var identity = identityManager?.GetIdentity(person.MatchedDarknetAddress);
                    if (identity != null)
                    {
                        // Low reputation = higher suspicion
                        if (identity.ReputationStars < 2f && identity.ReputationCount > 5)
                            threatScore += 25f;

                        // Already flagged as threat by network
                        if (identity.LocalThreatLevel >= ThreatLevel.High)
                            threatScore += 50f;
                    }
                }

                // Factor 4: Erratic movement patterns
                float movementVariance = CalculateMovementVariance(person.TrackingId);
                if (movementVariance > 2f) // High variance in movement
                    threatScore += movementVariance * 5f;

                // Update threat table
                ThreatLevel level = ClassifyThreat(threatScore);
                if (level > ThreatLevel.None)
                {
                    UpdateThreat(person.TrackingId, person.WorldPosition, threatScore, level);
                }
            }
        }

        private void UpdateThreat(string targetId, Vector3 position, float score, ThreatLevel level)
        {
            bool isNew = !activeThreatTable.ContainsKey(targetId);

            var threat = new ThreatInfo
            {
                TargetId = targetId,
                Position = position,
                ThreatScore = score,
                Level = level,
                Label = $"Threat [{level}]",
                LastUpdated = Time.time
            };

            activeThreatTable[targetId] = threat;

            if (isNew)
            {
                OnThreatDetected?.Invoke(threat);
                Log($"Threat detected: {targetId} [{level}] score={score:F1}");
            }
            else
            {
                OnThreatUpdated?.Invoke(threat);
            }
        }

        /// <summary>
        /// Manually flag a target as a threat (operative-initiated).
        /// In the Daemon, Sorcerers can "curse" operatives to flag them.
        /// </summary>
        public void FlagThreat(string targetId, ThreatLevel level, string reason)
        {
            var person = personDetector?.TrackedPeople.GetValueOrDefault(targetId);
            Vector3 pos = person?.WorldPosition ?? Vector3.zero;

            UpdateThreat(targetId, pos, level == ThreatLevel.Critical ? 100f : 60f, level);
            Log($"Manual threat flag: {targetId} [{level}] — {reason}");
        }

        private ThreatLevel ClassifyThreat(float score)
        {
            if (score >= 80f) return ThreatLevel.Critical;
            if (score >= 50f) return ThreatLevel.High;
            if (score >= 25f) return ThreatLevel.Moderate;
            if (score >= 10f) return ThreatLevel.Low;
            return ThreatLevel.None;
        }

        private void TrackPosition(DetectedPerson person)
        {
            if (!positionHistory.TryGetValue(person.TrackingId, out var history))
            {
                history = new Queue<TimestampedPosition>();
                positionHistory[person.TrackingId] = history;
            }

            history.Enqueue(new TimestampedPosition
            {
                Position = person.WorldPosition,
                Time = Time.time
            });

            // Keep last 30 positions
            while (history.Count > 30)
                history.Dequeue();
        }

        private float CalculateApproachSpeed(string trackingId, Vector3 myPosition)
        {
            if (!positionHistory.TryGetValue(trackingId, out var history) || history.Count < 2)
                return 0f;

            var positions = history.ToArray();
            var latest = positions[positions.Length - 1];
            var previous = positions[Mathf.Max(0, positions.Length - 5)];

            float dt = latest.Time - previous.Time;
            if (dt <= 0) return 0f;

            float distNow = Vector3.Distance(latest.Position, myPosition);
            float distBefore = Vector3.Distance(previous.Position, myPosition);

            // Positive = approaching, negative = retreating
            return (distBefore - distNow) / dt;
        }

        private float CalculateMovementVariance(string trackingId)
        {
            if (!positionHistory.TryGetValue(trackingId, out var history) || history.Count < 5)
                return 0f;

            var positions = history.ToArray();
            var velocities = new List<float>();

            for (int i = 1; i < positions.Length; i++)
            {
                float dt = positions[i].Time - positions[i - 1].Time;
                if (dt > 0)
                {
                    float speed = Vector3.Distance(positions[i].Position, positions[i - 1].Position) / dt;
                    velocities.Add(speed);
                }
            }

            if (velocities.Count < 2) return 0f;

            // Calculate variance
            float mean = 0f;
            foreach (float v in velocities) mean += v;
            mean /= velocities.Count;

            float variance = 0f;
            foreach (float v in velocities)
                variance += (v - mean) * (v - mean);
            variance /= velocities.Count;

            return Mathf.Sqrt(variance);
        }

        private void HandlePersonLost(string trackingId)
        {
            positionHistory.Remove(trackingId);
            if (activeThreatTable.Remove(trackingId))
                OnThreatCleared?.Invoke(trackingId);
        }

        protected override void OnShutdown()
        {
            if (personDetector != null)
            {
                personDetector.OnPersonUpdated -= TrackPosition;
                personDetector.OnPersonLost -= HandlePersonLost;
            }
        }
    }

    public class ThreatInfo
    {
        public string TargetId;
        public Vector3 Position;
        public float ThreatScore;
        public ThreatLevel Level;
        public string Label;
        public float LastUpdated;
    }

    public struct TimestampedPosition
    {
        public Vector3 Position;
        public float Time;
    }
}
