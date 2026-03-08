// CompassOverlay.cs — Compass and bearing indicator for D-Space navigation
// The Daemon's HUD includes directional awareness — operatives can see
// bearings to quest objectives, other operatives, and points of interest.

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DaemonVision.Core;

namespace DaemonVision.HUD
{
    public class CompassOverlay : SubsystemBase
    {
        public override string Name => "Compass";

        [Header("Compass Settings")]
        [SerializeField] private float compassWidth = 600f;
        [SerializeField] private float markerScale = 1f;
        [SerializeField] private bool showBearingText = true;
        [SerializeField] private bool showDistanceToMarkers = true;

        private Camera arCamera;
        private readonly List<CompassMarker> markers = new List<CompassMarker>();

        // Cardinal directions
        private static readonly string[] Cardinals = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

        protected override Task OnInitialize()
        {
            arCamera = Manager.ARCamera;
            return Task.CompletedTask;
        }

        /// <summary>
        /// Add a marker to the compass (quest waypoint, operative, POI).
        /// </summary>
        public void AddMarker(CompassMarker marker)
        {
            if (!markers.Contains(marker))
                markers.Add(marker);
        }

        public void RemoveMarker(string markerId)
        {
            markers.RemoveAll(m => m.Id == markerId);
        }

        public void ClearMarkers() => markers.Clear();

        public override void Tick(float deltaTime)
        {
            if (arCamera == null) return;

            float heading = arCamera.transform.eulerAngles.y;

            // Update each marker's compass position
            foreach (var marker in markers)
            {
                if (marker.WorldPosition.HasValue)
                {
                    Vector3 dir = marker.WorldPosition.Value - arCamera.transform.position;
                    marker.Bearing = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
                    if (marker.Bearing < 0) marker.Bearing += 360f;

                    marker.Distance = dir.magnitude;
                }

                // Calculate position on compass strip
                float relativeBearing = Mathf.DeltaAngle(heading, marker.Bearing);
                marker.CompassX = (relativeBearing / 180f) * (compassWidth * 0.5f);
                marker.IsVisible = Mathf.Abs(relativeBearing) < 90f;
            }
        }

        /// <summary>
        /// Get the current cardinal direction string.
        /// </summary>
        public string GetCardinalDirection()
        {
            if (arCamera == null) return "N";
            float heading = arCamera.transform.eulerAngles.y;
            int index = Mathf.RoundToInt(heading / 45f) % 8;
            return Cardinals[index];
        }

        public float GetHeading()
        {
            return arCamera != null ? arCamera.transform.eulerAngles.y : 0f;
        }

        public IReadOnlyList<CompassMarker> GetMarkers() => markers;
    }

    [System.Serializable]
    public class CompassMarker
    {
        public string Id;
        public string Label;
        public Color Color;
        public CompassMarkerType Type;
        public Vector3? WorldPosition;
        public float Bearing;        // Degrees from north
        public float Distance;       // Meters
        public float CompassX;       // Screen-space X on compass strip
        public bool IsVisible;
    }

    public enum CompassMarkerType
    {
        QuestObjective,
        Operative,
        Waypoint,
        Threat,
        Cache,
        Custom
    }
}
