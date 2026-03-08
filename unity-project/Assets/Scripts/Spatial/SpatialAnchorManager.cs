// SpatialAnchorManager.cs — GPS-anchored D-Space objects
// In the Daemon, ALL virtual objects are anchored to GPS coordinates on the world grid.
// D-Space constructs persist at their real-world locations for all operatives to see.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using DaemonVision.Core;

namespace DaemonVision.Spatial
{
    /// <summary>
    /// Manages persistent, GPS-anchored AR objects in D-Space.
    /// Objects are shared across all operatives and persist at world coordinates.
    /// </summary>
    public class SpatialAnchorManager : SubsystemBase
    {
        public override string Name => "SpatialAnchors";

        [Header("Anchor Settings")]
        [SerializeField] private float anchorUpdateRadius = 500f; // meters
        [SerializeField] private int maxActiveAnchors = 100;
        [SerializeField] private float anchorCullDistance = 1000f;
        [SerializeField] private float gpsUpdateInterval = 2f;

        private GPSLocationProvider gpsProvider;
        private ARAnchorManager arAnchorManager;

        private readonly Dictionary<string, DSpaceAnchor> activeAnchors
            = new Dictionary<string, DSpaceAnchor>();
        private readonly List<DSpaceAnchorData> pendingAnchors = new List<DSpaceAnchorData>();

        // Earth radius in meters for coordinate conversion
        private const double EarthRadius = 6378137.0;

        // Reference point for local coordinate system (set on first GPS fix)
        private double refLatitude;
        private double refLongitude;
        private double refAltitude;
        private bool hasReference;

        public event Action<DSpaceAnchor> OnAnchorCreated;
        public event Action<string> OnAnchorRemoved;

        protected override Task OnInitialize()
        {
            arAnchorManager = FindObjectOfType<ARAnchorManager>();
            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            gpsProvider = GetSubsystem<GPSLocationProvider>();
            if (gpsProvider != null)
            {
                gpsProvider.OnLocationUpdated += HandleLocationUpdate;
            }
        }

        /// <summary>
        /// Create a persistent D-Space object at a GPS coordinate.
        /// This is how the Daemon places virtual architecture, signs, and markers in the real world.
        /// </summary>
        public DSpaceAnchor CreateAnchor(DSpaceAnchorData data)
        {
            if (activeAnchors.ContainsKey(data.AnchorId))
            {
                Warn($"Anchor {data.AnchorId} already exists.");
                return activeAnchors[data.AnchorId];
            }

            if (!hasReference)
            {
                pendingAnchors.Add(data);
                return null;
            }

            Vector3 localPos = GPSToLocal(data.Latitude, data.Longitude, data.Altitude);

            var anchorGO = new GameObject($"DSpaceAnchor_{data.AnchorId}");
            anchorGO.transform.SetParent(Manager.WorldAnchorRoot);
            anchorGO.transform.position = localPos;
            anchorGO.transform.rotation = Quaternion.Euler(0, data.YawDegrees, 0);

            var anchor = anchorGO.AddComponent<DSpaceAnchor>();
            anchor.Initialize(data);

            activeAnchors[data.AnchorId] = anchor;
            OnAnchorCreated?.Invoke(anchor);

            Log($"Anchor created: {data.AnchorType} at ({data.Latitude:F6}, {data.Longitude:F6})");
            return anchor;
        }

        public void RemoveAnchor(string anchorId)
        {
            if (activeAnchors.TryGetValue(anchorId, out var anchor))
            {
                activeAnchors.Remove(anchorId);
                if (anchor != null && anchor.gameObject != null)
                    Destroy(anchor.gameObject);
                OnAnchorRemoved?.Invoke(anchorId);
            }
        }

        public DSpaceAnchor GetAnchor(string anchorId)
        {
            activeAnchors.TryGetValue(anchorId, out var anchor);
            return anchor;
        }

        public IEnumerable<DSpaceAnchor> GetAnchorsInRadius(Vector3 center, float radius)
        {
            float sqrRadius = radius * radius;
            foreach (var anchor in activeAnchors.Values)
            {
                if ((anchor.transform.position - center).sqrMagnitude <= sqrRadius)
                    yield return anchor;
            }
        }

        /// <summary>
        /// Convert GPS coordinates to Unity local space relative to the reference point.
        /// Uses equirectangular approximation — accurate enough for D-Space's operational range.
        /// </summary>
        public Vector3 GPSToLocal(double lat, double lon, double alt)
        {
            if (!hasReference)
                return Vector3.zero;

            double latRad = lat * Math.PI / 180.0;
            double refLatRad = refLatitude * Math.PI / 180.0;

            double dLat = (lat - refLatitude) * Math.PI / 180.0;
            double dLon = (lon - refLongitude) * Math.PI / 180.0;

            // Meters north/east from reference
            double north = dLat * EarthRadius;
            double east = dLon * EarthRadius * Math.Cos(refLatRad);
            double up = alt - refAltitude;

            // Unity: X=east, Y=up, Z=north
            return new Vector3((float)east, (float)up, (float)north);
        }

        public (double lat, double lon, double alt) LocalToGPS(Vector3 localPos)
        {
            if (!hasReference)
                return (0, 0, 0);

            double refLatRad = refLatitude * Math.PI / 180.0;

            double dLat = localPos.z / EarthRadius;
            double dLon = localPos.x / (EarthRadius * Math.Cos(refLatRad));

            double lat = refLatitude + dLat * 180.0 / Math.PI;
            double lon = refLongitude + dLon * 180.0 / Math.PI;
            double alt = refAltitude + localPos.y;

            return (lat, lon, alt);
        }

        private void HandleLocationUpdate(GPSLocation location)
        {
            if (!hasReference)
            {
                refLatitude = location.Latitude;
                refLongitude = location.Longitude;
                refAltitude = location.Altitude;
                hasReference = true;
                Log($"GPS reference set: ({refLatitude:F6}, {refLongitude:F6})");

                // Process pending anchors
                foreach (var data in pendingAnchors)
                    CreateAnchor(data);
                pendingAnchors.Clear();
            }

            CullDistantAnchors(location);
        }

        private void CullDistantAnchors(GPSLocation location)
        {
            var toRemove = new List<string>();
            foreach (var kvp in activeAnchors)
            {
                float distance = Vector3.Distance(
                    kvp.Value.transform.position,
                    GPSToLocal(location.Latitude, location.Longitude, location.Altitude));

                if (distance > anchorCullDistance)
                    toRemove.Add(kvp.Key);
            }

            foreach (var id in toRemove)
                RemoveAnchor(id);
        }

        protected override void OnShutdown()
        {
            if (gpsProvider != null)
                gpsProvider.OnLocationUpdated -= HandleLocationUpdate;
        }
    }

    /// <summary>
    /// A persistent GPS-anchored object in D-Space.
    /// </summary>
    public class DSpaceAnchor : MonoBehaviour
    {
        public DSpaceAnchorData Data { get; private set; }

        public void Initialize(DSpaceAnchorData data)
        {
            Data = data;
        }
    }

    /// <summary>
    /// Serializable data for a D-Space anchor — can be stored and transmitted.
    /// </summary>
    [Serializable]
    public class DSpaceAnchorData
    {
        public string AnchorId;
        public double Latitude;
        public double Longitude;
        public double Altitude;
        public float YawDegrees;
        public DSpaceAnchorType AnchorType;
        public string CreatorAddress;   // Darknet address of the operative who placed it
        public long CreatedTimestamp;
        public string Payload;          // JSON payload specific to the anchor type
        public int RequiredLevel;       // Minimum level to see this anchor (level-gating)
        public string RequiredFaction;  // Faction restriction (empty = visible to all)
    }

    public enum DSpaceAnchorType
    {
        Marker,             // Simple position marker
        Sign,               // Text sign / billboard
        Waypoint,           // Navigation waypoint
        QuestGiver,         // Quest start location
        Cache,              // Resource cache / drop point
        Portal,             // Transition point between D-Space zones
        Structure,          // Virtual architecture (buildings, walls, etc.)
        Hazard,             // Danger zone warning
        MeetingPoint,       // Designated gathering location
        Broadcast,          // Area-effect information broadcast
        Geofence,           // Boundary marker for a D-Space zone
        Custom              // User-defined type
    }
}
