// ThreatIndicatorRenderer.cs — Red outlines around hostiles in D-Space
// In the Daemon, hostiles are highlighted with red outlines visible through walls,
// giving operatives tactical awareness. This is the "threat overlay" system.

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Detection;
using DaemonVision.Identity;

namespace DaemonVision.HUD
{
    public class ThreatIndicatorRenderer : SubsystemBase
    {
        public override string Name => "ThreatIndicator";

        [Header("Threat Visualization")]
        [SerializeField] private Material threatOutlineMaterial;
        [SerializeField] private Material friendlyOutlineMaterial;
        [SerializeField] private float outlineWidth = 3f;
        [SerializeField] private float pulseSpeed = 2f;
        [SerializeField] private float maxIndicatorDistance = 100f;
        [SerializeField] private bool showThroughWalls = true;

        [Header("Direction Indicators")]
        [SerializeField] private float directionIndicatorRadius = 0.4f; // Screen-space
        [SerializeField] private bool showOffscreenIndicators = true;

        private ThreatAssessment threatSystem;
        private HUDManager hudManager;

        private readonly Dictionary<string, ThreatIndicator> activeIndicators
            = new Dictionary<string, ThreatIndicator>();

        protected override Task OnInitialize()
        {
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            threatSystem = GetSubsystem<ThreatAssessment>();
            hudManager = GetSubsystem<HUDManager>();

            if (threatSystem != null)
            {
                threatSystem.OnThreatDetected += HandleThreatDetected;
                threatSystem.OnThreatUpdated += HandleThreatUpdated;
                threatSystem.OnThreatCleared += HandleThreatCleared;
            }
        }

        public override void Tick(float deltaTime)
        {
            var camera = Manager.ARCamera;
            if (camera == null) return;

            var colors = hudManager?.Colors;

            foreach (var kvp in activeIndicators)
            {
                var indicator = kvp.Value;

                // Pulse effect for active threats
                float pulse = (Mathf.Sin(Time.time * pulseSpeed * (int)indicator.Level) + 1f) * 0.5f;

                // Update screen-space position
                Vector3 screenPos = camera.WorldToViewportPoint(indicator.WorldPosition);
                bool isOnScreen = screenPos.z > 0 && screenPos.x > 0 && screenPos.x < 1
                                  && screenPos.y > 0 && screenPos.y < 1;

                indicator.IsOnScreen = isOnScreen;

                // Update outline color based on threat level
                Color outlineColor = GetThreatColor(indicator.Level, colors);
                outlineColor.a = Mathf.Lerp(0.5f, 1f, pulse);
                indicator.CurrentColor = outlineColor;

                // Off-screen directional indicator
                if (!isOnScreen && showOffscreenIndicators)
                {
                    Vector3 dir = (indicator.WorldPosition - camera.transform.position).normalized;
                    Vector3 forward = camera.transform.forward;
                    float angle = Mathf.Atan2(
                        Vector3.Dot(dir, camera.transform.right),
                        Vector3.Dot(dir, forward));
                    indicator.OffScreenAngle = angle;
                }

                // Distance
                indicator.Distance = Vector3.Distance(camera.transform.position, indicator.WorldPosition);
            }
        }

        private void HandleThreatDetected(ThreatInfo threat)
        {
            if (activeIndicators.ContainsKey(threat.TargetId))
                return;

            var indicator = new ThreatIndicator
            {
                TargetId = threat.TargetId,
                Level = threat.Level,
                WorldPosition = threat.Position,
                Label = threat.Label,
                Distance = 0f
            };

            activeIndicators[threat.TargetId] = indicator;
            Log($"Threat indicator: {threat.Label} [{threat.Level}] at {threat.Position}");
        }

        private void HandleThreatUpdated(ThreatInfo threat)
        {
            if (activeIndicators.TryGetValue(threat.TargetId, out var indicator))
            {
                indicator.Level = threat.Level;
                indicator.WorldPosition = threat.Position;
            }
        }

        private void HandleThreatCleared(string targetId)
        {
            activeIndicators.Remove(targetId);
        }

        public IEnumerable<ThreatIndicator> GetActiveIndicators() => activeIndicators.Values;

        private Color GetThreatColor(ThreatLevel level, HUDColorScheme colors)
        {
            if (colors == null) colors = new HUDColorScheme();

            return level switch
            {
                ThreatLevel.Low => colors.Warning,
                ThreatLevel.Moderate => new Color(1f, 0.5f, 0f), // Orange
                ThreatLevel.High => colors.Danger,
                ThreatLevel.Critical => new Color(1f, 0f, 0f),   // Pure red
                _ => colors.Neutral
            };
        }

        protected override void OnShutdown()
        {
            if (threatSystem != null)
            {
                threatSystem.OnThreatDetected -= HandleThreatDetected;
                threatSystem.OnThreatUpdated -= HandleThreatUpdated;
                threatSystem.OnThreatCleared -= HandleThreatCleared;
            }
            activeIndicators.Clear();
        }
    }

    public class ThreatIndicator
    {
        public string TargetId;
        public ThreatLevel Level;
        public Vector3 WorldPosition;
        public string Label;
        public float Distance;
        public bool IsOnScreen;
        public float OffScreenAngle;
        public Color CurrentColor;
    }
}
