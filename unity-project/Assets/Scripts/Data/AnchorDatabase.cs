// AnchorDatabase.cs — Persistent storage for D-Space spatial anchors
// Stores all known DSpaceAnchorData objects with spatial indexing for
// fast area queries. Supports import/export for mesh network synchronization.
// Anchors are GPS-positioned virtual objects shared across all operatives.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using DaemonVision.Core;
using DaemonVision.Spatial;

namespace DaemonVision.Data
{
    /// <summary>
    /// Persistent database of D-Space anchors. Uses grid-based spatial indexing
    /// for efficient area queries and persists through DataPersistence.
    /// Anchors are the foundation of the shared D-Space world — virtual signs,
    /// quest givers, caches, and structures all live here.
    /// </summary>
    public class AnchorDatabase : SubsystemBase
    {
        public override string Name => "AnchorDatabase";

        [Header("Database Settings")]
        [SerializeField] private double gridCellSizeDegrees = 0.001;  // ~111m at equator
        [SerializeField] private float maxAnchorAgeDays = 90f;
        [SerializeField] private float cleanupIntervalMinutes = 30f;
        [SerializeField] private int maxAnchorsTotal = 10000;

        private DataPersistence persistence;

        // Master anchor store: anchorId -> data
        private readonly Dictionary<string, DSpaceAnchorData> anchors
            = new Dictionary<string, DSpaceAnchorData>();

        // Spatial index: grid cell key -> set of anchor IDs in that cell
        private readonly Dictionary<string, HashSet<string>> spatialGrid
            = new Dictionary<string, HashSet<string>>();

        // Index by creator address for social queries
        private readonly Dictionary<string, HashSet<string>> creatorIndex
            = new Dictionary<string, HashSet<string>>();

        // Index by anchor type for filtering
        private readonly Dictionary<DSpaceAnchorType, HashSet<string>> typeIndex
            = new Dictionary<DSpaceAnchorType, HashSet<string>>();

        private float cleanupTimer;

        private const string PersistenceKey = "anchors/anchor_database";

        public int AnchorCount => anchors.Count;

        public event Action<DSpaceAnchorData> OnAnchorStored;
        public event Action<string> OnAnchorRemoved;
        public event Action<int> OnAnchorsImported;

        // ─────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────────────────

        protected override Task OnInitialize()
        {
            // Initialize type index buckets
            foreach (DSpaceAnchorType type in Enum.GetValues(typeof(DSpaceAnchorType)))
            {
                typeIndex[type] = new HashSet<string>();
            }

            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            persistence = GetSubsystem<DataPersistence>();

            if (persistence != null)
            {
                LoadFromDisk();
            }
            else
            {
                Warn("DataPersistence not available. Anchor data will not be persisted.");
            }
        }

        public override void Tick(float deltaTime)
        {
            cleanupTimer += deltaTime;

            if (cleanupTimer >= cleanupIntervalMinutes * 60f)
            {
                cleanupTimer = 0f;
                CleanupOldAnchors();
            }
        }

        protected override void OnShutdown()
        {
            PersistToDisk();
        }

        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Store a new anchor or update an existing one in the database.
        /// Automatically indexes by location, creator, and type.
        /// </summary>
        public void StoreAnchor(DSpaceAnchorData anchor)
        {
            if (anchor == null || string.IsNullOrEmpty(anchor.AnchorId))
            {
                Warn("Attempted to store anchor with null/empty ID.");
                return;
            }

            // Remove existing entry if updating (cleans up old index entries)
            if (anchors.ContainsKey(anchor.AnchorId))
            {
                RemoveFromIndices(anchor.AnchorId);
            }

            // Enforce capacity limit
            if (!anchors.ContainsKey(anchor.AnchorId) && anchors.Count >= maxAnchorsTotal)
            {
                EvictOldestAnchor();
            }

            // Store
            anchors[anchor.AnchorId] = anchor;

            // Add to spatial index
            string gridKey = GetGridKey(anchor.Latitude, anchor.Longitude);
            if (!spatialGrid.TryGetValue(gridKey, out var cell))
            {
                cell = new HashSet<string>();
                spatialGrid[gridKey] = cell;
            }
            cell.Add(anchor.AnchorId);

            // Add to creator index
            if (!string.IsNullOrEmpty(anchor.CreatorAddress))
            {
                if (!creatorIndex.TryGetValue(anchor.CreatorAddress, out var creatorSet))
                {
                    creatorSet = new HashSet<string>();
                    creatorIndex[anchor.CreatorAddress] = creatorSet;
                }
                creatorSet.Add(anchor.AnchorId);
            }

            // Add to type index
            if (typeIndex.TryGetValue(anchor.AnchorType, out var typeSet))
            {
                typeSet.Add(anchor.AnchorId);
            }

            OnAnchorStored?.Invoke(anchor);
            MarkDirty();
        }

        /// <summary>
        /// Find all anchors within a radius (in meters) of a GPS coordinate.
        /// Uses the spatial grid for fast initial filtering, then precise
        /// Haversine distance for final results.
        /// </summary>
        public List<DSpaceAnchorData> GetAnchorsInArea(double lat, double lon, double radiusMeters)
        {
            var results = new List<DSpaceAnchorData>();

            // Calculate the grid cell range to search.
            // At the equator, 1 degree ~= 111,320 meters.
            double radiusDegrees = radiusMeters / 111320.0;

            // Account for longitude scaling at the given latitude
            double cosLat = Math.Cos(lat * Math.PI / 180.0);
            double lonRadiusDeg = cosLat > 0.001 ? radiusDegrees / cosLat : radiusDegrees;

            double minLat = lat - radiusDegrees;
            double maxLat = lat + radiusDegrees;
            double minLon = lon - lonRadiusDeg;
            double maxLon = lon + lonRadiusDeg;

            // Iterate over all grid cells in the bounding box
            var candidateIds = new HashSet<string>();

            double cellLat = Math.Floor(minLat / gridCellSizeDegrees) * gridCellSizeDegrees;
            while (cellLat <= maxLat)
            {
                double cellLon = Math.Floor(minLon / gridCellSizeDegrees) * gridCellSizeDegrees;
                while (cellLon <= maxLon)
                {
                    string key = GetGridKey(cellLat, cellLon);
                    if (spatialGrid.TryGetValue(key, out var cell))
                    {
                        foreach (string id in cell)
                            candidateIds.Add(id);
                    }
                    cellLon += gridCellSizeDegrees;
                }
                cellLat += gridCellSizeDegrees;
            }

            // Precise distance filter using Haversine
            foreach (string id in candidateIds)
            {
                if (anchors.TryGetValue(id, out var anchor))
                {
                    float distance = GPSLocationProvider.DistanceBetween(
                        lat, lon, anchor.Latitude, anchor.Longitude);

                    if (distance <= radiusMeters)
                    {
                        results.Add(anchor);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Get all anchors created by a specific darknet address.
        /// </summary>
        public List<DSpaceAnchorData> GetAnchorsByCreator(string darknetAddress)
        {
            var results = new List<DSpaceAnchorData>();
            if (string.IsNullOrEmpty(darknetAddress)) return results;

            if (creatorIndex.TryGetValue(darknetAddress, out var ids))
            {
                foreach (string id in ids)
                {
                    if (anchors.TryGetValue(id, out var anchor))
                        results.Add(anchor);
                }
            }

            return results;
        }

        /// <summary>
        /// Get all anchors of a specific type.
        /// </summary>
        public List<DSpaceAnchorData> GetAnchorsByType(DSpaceAnchorType anchorType)
        {
            var results = new List<DSpaceAnchorData>();

            if (typeIndex.TryGetValue(anchorType, out var ids))
            {
                foreach (string id in ids)
                {
                    if (anchors.TryGetValue(id, out var anchor))
                        results.Add(anchor);
                }
            }

            return results;
        }

        /// <summary>
        /// Remove an anchor from the database and all indices.
        /// </summary>
        public bool RemoveAnchor(string anchorId)
        {
            if (string.IsNullOrEmpty(anchorId)) return false;

            if (!anchors.ContainsKey(anchorId))
                return false;

            RemoveFromIndices(anchorId);
            anchors.Remove(anchorId);

            OnAnchorRemoved?.Invoke(anchorId);
            MarkDirty();

            return true;
        }

        /// <summary>
        /// Get all anchors in the database.
        /// </summary>
        public List<DSpaceAnchorData> GetAllAnchors()
        {
            return new List<DSpaceAnchorData>(anchors.Values);
        }

        /// <summary>
        /// Get a single anchor by its ID.
        /// </summary>
        public DSpaceAnchorData GetAnchor(string anchorId)
        {
            if (string.IsNullOrEmpty(anchorId)) return null;
            anchors.TryGetValue(anchorId, out var anchor);
            return anchor;
        }

        /// <summary>
        /// Import anchors received from mesh network peers. Merges with existing
        /// data — newer timestamps overwrite older ones for the same anchor ID.
        /// </summary>
        public int ImportAnchors(List<DSpaceAnchorData> importedAnchors)
        {
            if (importedAnchors == null || importedAnchors.Count == 0)
                return 0;

            int imported = 0;

            foreach (var anchor in importedAnchors)
            {
                if (anchor == null || string.IsNullOrEmpty(anchor.AnchorId))
                    continue;

                // Merge strategy: keep the newer version
                if (anchors.TryGetValue(anchor.AnchorId, out var existing))
                {
                    if (anchor.CreatedTimestamp <= existing.CreatedTimestamp)
                        continue; // Our version is newer or same age
                }

                StoreAnchor(anchor);
                imported++;
            }

            if (imported > 0)
            {
                Log($"Imported {imported} anchors from mesh network.");
                OnAnchorsImported?.Invoke(imported);
            }

            return imported;
        }

        /// <summary>
        /// Export all anchors for mesh network sharing. Returns a serializable
        /// list that can be transmitted to peers.
        /// </summary>
        public List<DSpaceAnchorData> ExportAnchors()
        {
            return GetAllAnchors();
        }

        /// <summary>
        /// Export anchors within a specific area for targeted sync.
        /// </summary>
        public List<DSpaceAnchorData> ExportAnchorsInArea(double lat, double lon, double radiusMeters)
        {
            return GetAnchorsInArea(lat, lon, radiusMeters);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Spatial Grid
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Compute the grid cell key for a GPS coordinate.
        /// Each cell is gridCellSizeDegrees x gridCellSizeDegrees.
        /// </summary>
        private string GetGridKey(double lat, double lon)
        {
            long latCell = (long)Math.Floor(lat / gridCellSizeDegrees);
            long lonCell = (long)Math.Floor(lon / gridCellSizeDegrees);
            return $"{latCell}_{lonCell}";
        }

        /// <summary>
        /// Remove an anchor from all secondary indices (spatial grid, creator, type).
        /// </summary>
        private void RemoveFromIndices(string anchorId)
        {
            if (!anchors.TryGetValue(anchorId, out var anchor))
                return;

            // Remove from spatial grid
            string gridKey = GetGridKey(anchor.Latitude, anchor.Longitude);
            if (spatialGrid.TryGetValue(gridKey, out var cell))
            {
                cell.Remove(anchorId);
                if (cell.Count == 0)
                    spatialGrid.Remove(gridKey);
            }

            // Remove from creator index
            if (!string.IsNullOrEmpty(anchor.CreatorAddress))
            {
                if (creatorIndex.TryGetValue(anchor.CreatorAddress, out var creatorSet))
                {
                    creatorSet.Remove(anchorId);
                    if (creatorSet.Count == 0)
                        creatorIndex.Remove(anchor.CreatorAddress);
                }
            }

            // Remove from type index
            if (typeIndex.TryGetValue(anchor.AnchorType, out var typeSet))
            {
                typeSet.Remove(anchorId);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Cleanup
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Remove anchors older than the configured max age.
        /// </summary>
        private void CleanupOldAnchors()
        {
            if (maxAnchorAgeDays <= 0) return;

            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            long maxAgeSeconds = (long)(maxAnchorAgeDays * 86400);
            var toRemove = new List<string>();

            foreach (var kvp in anchors)
            {
                if (kvp.Value.CreatedTimestamp > 0 &&
                    (now - kvp.Value.CreatedTimestamp) > maxAgeSeconds)
                {
                    toRemove.Add(kvp.Key);
                }
            }

            foreach (string id in toRemove)
            {
                RemoveAnchor(id);
            }

            if (toRemove.Count > 0)
            {
                Log($"Cleaned up {toRemove.Count} expired anchors.");
            }
        }

        /// <summary>
        /// Evict the oldest anchor when the database is at capacity.
        /// </summary>
        private void EvictOldestAnchor()
        {
            string oldestId = null;
            long oldestTimestamp = long.MaxValue;

            foreach (var kvp in anchors)
            {
                if (kvp.Value.CreatedTimestamp < oldestTimestamp)
                {
                    oldestTimestamp = kvp.Value.CreatedTimestamp;
                    oldestId = kvp.Key;
                }
            }

            if (oldestId != null)
            {
                RemoveAnchor(oldestId);
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Persistence
        // ─────────────────────────────────────────────────────────────────

        private bool isDirty;

        private void MarkDirty()
        {
            isDirty = true;
        }

        private void PersistToDisk()
        {
            if (persistence == null || !isDirty) return;

            try
            {
                var wrapper = new AnchorDatabaseSnapshot
                {
                    Anchors = new List<DSpaceAnchorData>(anchors.Values),
                    SavedTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                };

                persistence.Save(PersistenceKey, wrapper);
                isDirty = false;
            }
            catch (Exception ex)
            {
                Error($"Failed to persist anchor database: {ex.Message}");
            }
        }

        private void LoadFromDisk()
        {
            if (persistence == null) return;

            try
            {
                var snapshot = persistence.Load<AnchorDatabaseSnapshot>(PersistenceKey);
                if (snapshot != null && snapshot.Anchors != null)
                {
                    foreach (var anchor in snapshot.Anchors)
                    {
                        StoreAnchor(anchor);
                    }

                    isDirty = false; // Data is clean — just loaded
                    Log($"Loaded {anchors.Count} anchors from disk (saved at {snapshot.SavedTimestamp}).");
                }
            }
            catch (Exception ex)
            {
                Warn($"Failed to load anchor database: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Serialization Wrapper
        // ─────────────────────────────────────────────────────────────────

        [Serializable]
        private class AnchorDatabaseSnapshot
        {
            public List<DSpaceAnchorData> Anchors = new List<DSpaceAnchorData>();
            public long SavedTimestamp;
        }
    }
}
