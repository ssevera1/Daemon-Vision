// WorldMeshManager.cs — Spatial mesh and plane understanding for D-Space
// In the Daemon, D-Space constructs interact with real-world geometry.
// Virtual walls align with real walls, signs attach to surfaces, etc.

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using DaemonVision.Core;

namespace DaemonVision.Spatial
{
    public class WorldMeshManager : SubsystemBase
    {
        public override string Name => "WorldMesh";

        [Header("Mesh Settings")]
        [SerializeField] private bool enableMeshVisualization;
        [SerializeField] private Material meshVisualizationMaterial;
        [SerializeField] private bool enablePlaneDetection = true;
        [SerializeField] private float meshUpdateInterval = 0.5f;

        private ARPlaneManager planeManager;
        private ARMeshManager meshManager;

        private readonly List<ARPlane> detectedPlanes = new List<ARPlane>();
        private readonly Dictionary<TrackableId, SurfaceInfo> surfaces
            = new Dictionary<TrackableId, SurfaceInfo>();

        public IReadOnlyList<ARPlane> DetectedPlanes => detectedPlanes;

        protected override Task OnInitialize()
        {
            planeManager = FindObjectOfType<ARPlaneManager>();
            meshManager = FindObjectOfType<ARMeshManager>();

            if (planeManager != null)
            {
                planeManager.planesChanged += OnPlanesChanged;

                if (enablePlaneDetection)
                {
                    planeManager.requestedDetectionMode = PlaneDetectionMode.Everything;
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Perform a raycast against the world mesh to find real-world surfaces.
        /// Used for placing D-Space objects on walls, floors, etc.
        /// </summary>
        public bool RaycastWorld(Ray ray, out RaycastHit hit, float maxDistance = 10f)
        {
            // First try AR raycast
            var arRaycastManager = FindObjectOfType<ARRaycastManager>();
            if (arRaycastManager != null)
            {
                var hits = new List<ARRaycastHit>();
                var screenPoint = Manager.ARCamera.WorldToScreenPoint(ray.origin + ray.direction);
                if (arRaycastManager.Raycast(new Vector2(screenPoint.x, screenPoint.y), hits, TrackableType.AllTypes))
                {
                    hit = new RaycastHit();
                    // Use the closest AR hit point
                    return true;
                }
            }

            // Fallback to physics raycast against mesh colliders
            return Physics.Raycast(ray, out hit, maxDistance);
        }

        /// <summary>
        /// Find the nearest horizontal surface (floor/table) at a position.
        /// Used for placing D-Space objects that should sit on surfaces.
        /// </summary>
        public bool FindNearestHorizontalSurface(Vector3 position, float searchRadius, out Vector3 surfacePoint)
        {
            surfacePoint = position;
            float closestDist = float.MaxValue;
            bool found = false;

            foreach (var plane in detectedPlanes)
            {
                if (plane.alignment != PlaneAlignment.HorizontalUp &&
                    plane.alignment != PlaneAlignment.HorizontalDown)
                    continue;

                float dist = Vector3.Distance(position, plane.center);
                if (dist < searchRadius && dist < closestDist)
                {
                    closestDist = dist;
                    surfacePoint = plane.center;
                    surfacePoint.y = plane.center.y;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Find the nearest vertical surface (wall) at a position.
        /// Used for attaching D-Space signs, markers, and displays to walls.
        /// </summary>
        public bool FindNearestVerticalSurface(Vector3 position, float searchRadius,
            out Vector3 surfacePoint, out Vector3 surfaceNormal)
        {
            surfacePoint = position;
            surfaceNormal = Vector3.forward;
            float closestDist = float.MaxValue;
            bool found = false;

            foreach (var plane in detectedPlanes)
            {
                if (plane.alignment != PlaneAlignment.Vertical)
                    continue;

                float dist = Vector3.Distance(position, plane.center);
                if (dist < searchRadius && dist < closestDist)
                {
                    closestDist = dist;
                    surfacePoint = plane.center;
                    surfaceNormal = plane.normal;
                    found = true;
                }
            }

            return found;
        }

        private void OnPlanesChanged(ARPlanesChangedEventArgs args)
        {
            foreach (var plane in args.added)
            {
                detectedPlanes.Add(plane);
                surfaces[plane.trackableId] = new SurfaceInfo
                {
                    TrackableId = plane.trackableId,
                    Center = plane.center,
                    Normal = plane.normal,
                    Size = plane.size,
                    Alignment = plane.alignment
                };
            }

            foreach (var plane in args.updated)
            {
                if (surfaces.ContainsKey(plane.trackableId))
                {
                    surfaces[plane.trackableId] = new SurfaceInfo
                    {
                        TrackableId = plane.trackableId,
                        Center = plane.center,
                        Normal = plane.normal,
                        Size = plane.size,
                        Alignment = plane.alignment
                    };
                }
            }

            foreach (var plane in args.removed)
            {
                detectedPlanes.Remove(plane);
                surfaces.Remove(plane.trackableId);
            }
        }

        protected override void OnShutdown()
        {
            if (planeManager != null)
                planeManager.planesChanged -= OnPlanesChanged;
        }
    }

    public struct SurfaceInfo
    {
        public TrackableId TrackableId;
        public Vector3 Center;
        public Vector3 Normal;
        public Vector2 Size;
        public PlaneAlignment Alignment;
    }
}
