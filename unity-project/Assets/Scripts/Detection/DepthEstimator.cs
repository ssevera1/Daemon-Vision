// DepthEstimator.cs — Multi-strategy depth estimation for D-Space
// Estimates distance to detected people using the best available method:
// AR depth buffer, bounding box height heuristic, or stereo camera disparity.
// Auto-selects strategy based on the active GlassesProfile's sensor capabilities.

using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using DaemonVision.Core;

namespace DaemonVision.Detection
{
    /// <summary>
    /// Estimates depth (distance in meters) to detected objects/people.
    /// Chooses the most accurate strategy available on the current hardware:
    /// 1. AR depth buffer (best — direct ToF or LiDAR measurement)
    /// 2. Stereo camera disparity (good — triangulation from two cameras)
    /// 3. Bounding box height heuristic (fallback — assumes average person height)
    /// </summary>
    public class DepthEstimator : SubsystemBase
    {
        public override string Name => "DepthEstimator";

        [Header("Depth Settings")]
        [SerializeField] private float maxDepthMeters = 30f;
        [SerializeField] private float minDepthMeters = 0.3f;
        [SerializeField] private float averagePersonHeightMeters = 1.7f;
        [SerializeField] private float depthSmoothingFactor = 0.3f;

        [Header("Stereo Settings")]
        [SerializeField] private float stereoBaseline = 0.065f;  // meters between cameras
        [SerializeField] private float stereoFocalLength = 500f;  // pixels

        private ARCameraManager arCameraManager;
        private AROcclusionManager occlusionManager;
        private Camera arCamera;
        private GlassesProfileManager profileManager;

        private DepthStrategy activeStrategy = DepthStrategy.BoundingBoxHeuristic;
        private Texture2D depthTexture;
        private bool depthBufferAvailable;

        // Smoothed depth cache: screen region hash -> smoothed depth
        private readonly float[] depthCache = new float[64];  // 8x8 grid cache
        private const int DepthGridSize = 8;

        public DepthStrategy ActiveStrategy => activeStrategy;

        public event Action<DepthStrategy> OnStrategyChanged;

        // ─────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────────────────

        protected override Task OnInitialize()
        {
            arCamera = Manager.ARCamera;
            arCameraManager = FindObjectOfType<ARCameraManager>();
            occlusionManager = FindObjectOfType<AROcclusionManager>();

            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            profileManager = GetSubsystem<GlassesProfileManager>();
            SelectBestStrategy();
        }

        public override void Tick(float deltaTime)
        {
            // Update depth buffer availability
            if (activeStrategy == DepthStrategy.ARDepthBuffer)
            {
                UpdateDepthBuffer();
            }
        }

        protected override void OnShutdown()
        {
            if (depthTexture != null)
            {
                UnityEngine.Object.Destroy(depthTexture);
                depthTexture = null;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Strategy Selection
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Auto-detect the best available depth estimation strategy based
        /// on the active hardware profile and available AR subsystems.
        /// </summary>
        private void SelectBestStrategy()
        {
            var profile = profileManager?.ActiveProfile;

            // Priority 1: AR depth buffer (LiDAR, ToF sensor)
            if (profile != null && profile.HasDepthSensor && occlusionManager != null)
            {
                if (CheckDepthSubsystem())
                {
                    SetStrategy(DepthStrategy.ARDepthBuffer);
                    return;
                }
            }

            // Priority 2: Stereo camera disparity
            if (profile != null && HasStereoCapability(profile))
            {
                SetStrategy(DepthStrategy.StereoDisparity);
                return;
            }

            // Priority 3: Bounding box height heuristic (always available)
            SetStrategy(DepthStrategy.BoundingBoxHeuristic);
        }

        /// <summary>
        /// Override the automatic strategy selection. Useful for testing or
        /// when the auto-detection doesn't match the hardware correctly.
        /// </summary>
        public void SetStrategy(DepthStrategy strategy)
        {
            if (activeStrategy == strategy) return;

            activeStrategy = strategy;
            Log($"Depth strategy: {strategy}");
            OnStrategyChanged?.Invoke(strategy);
        }

        private bool CheckDepthSubsystem()
        {
            // Check if the AR environment depth subsystem is actually available
            if (occlusionManager == null) return false;

            try
            {
                // Attempt to check if environment depth is supported
                return occlusionManager.descriptor?.supportsEnvironmentDepthImage == true;
            }
            catch
            {
                return false;
            }
        }

        private bool HasStereoCapability(GlassesProfile profile)
        {
            // Devices with stereo cameras or passthrough stereo
            if (profile.HasCamera && profile.DisplayType == DisplayType.VideoPassthrough)
                return true;

            // Vuzix Shield has stereo cameras
            if (profile.Id == "vuzix_shield")
                return true;

            return false;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Estimate the depth at a normalized screen point [0,1].
        /// Returns distance in meters. Returns -1 if estimation fails.
        /// </summary>
        public float EstimateDepthAtScreenPoint(Vector2 normalizedScreenPoint)
        {
            float depth;

            switch (activeStrategy)
            {
                case DepthStrategy.ARDepthBuffer:
                    depth = GetARDepthAtPoint(normalizedScreenPoint);
                    if (depth > 0) return SmoothDepth(normalizedScreenPoint, depth);
                    // Fall through to heuristic if depth buffer miss
                    goto case DepthStrategy.BoundingBoxHeuristic;

                case DepthStrategy.StereoDisparity:
                    depth = GetStereoDepthAtPoint(normalizedScreenPoint);
                    if (depth > 0) return SmoothDepth(normalizedScreenPoint, depth);
                    goto case DepthStrategy.BoundingBoxHeuristic;

                case DepthStrategy.BoundingBoxHeuristic:
                default:
                    return -1f; // Heuristic needs bounding box, not just a point
            }
        }

        /// <summary>
        /// Estimate depth for a full detection bounding box. This is the preferred
        /// method as it can use all strategies including the bbox heuristic.
        /// </summary>
        public float EstimateDepthForDetection(Rect boundingBox)
        {
            float depth;

            switch (activeStrategy)
            {
                case DepthStrategy.ARDepthBuffer:
                    depth = GetARDepthForRegion(boundingBox);
                    if (depth > 0) return Mathf.Clamp(depth, minDepthMeters, maxDepthMeters);
                    // Fall through
                    goto case DepthStrategy.StereoDisparity;

                case DepthStrategy.StereoDisparity:
                    depth = GetStereoDepthAtPoint(boundingBox.center);
                    if (depth > 0) return Mathf.Clamp(depth, minDepthMeters, maxDepthMeters);
                    // Fall through
                    goto case DepthStrategy.BoundingBoxHeuristic;

                case DepthStrategy.BoundingBoxHeuristic:
                default:
                    depth = EstimateDepthFromBBoxHeight(boundingBox.height);
                    return Mathf.Clamp(depth, minDepthMeters, maxDepthMeters);
            }
        }

        /// <summary>
        /// Get the confidence level of the current depth estimate.
        /// AR depth buffer = high, stereo = medium, heuristic = low.
        /// </summary>
        public float GetDepthConfidence()
        {
            switch (activeStrategy)
            {
                case DepthStrategy.ARDepthBuffer:
                    return depthBufferAvailable ? 0.9f : 0.3f;
                case DepthStrategy.StereoDisparity:
                    return 0.7f;
                case DepthStrategy.BoundingBoxHeuristic:
                    return 0.4f;
                default:
                    return 0.1f;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Strategy 1: AR Depth Buffer
        // ─────────────────────────────────────────────────────────────────

        private void UpdateDepthBuffer()
        {
            if (occlusionManager == null)
            {
                depthBufferAvailable = false;
                return;
            }

            // Try to get the environment depth texture
            if (occlusionManager.TryAcquireEnvironmentDepthCpuImage(out XRCpuImage depthImage))
            {
                try
                {
                    depthBufferAvailable = true;

                    // Convert to a texture we can sample
                    if (depthTexture == null ||
                        depthTexture.width != depthImage.width ||
                        depthTexture.height != depthImage.height)
                    {
                        if (depthTexture != null)
                            UnityEngine.Object.Destroy(depthTexture);

                        depthTexture = new Texture2D(
                            depthImage.width, depthImage.height,
                            TextureFormat.RFloat, false);
                    }

                    var conversionParams = new XRCpuImage.ConversionParams
                    {
                        inputRect = new RectInt(0, 0, depthImage.width, depthImage.height),
                        outputDimensions = new Vector2Int(depthImage.width, depthImage.height),
                        outputFormat = TextureFormat.RFloat,
                        transformation = XRCpuImage.Transformation.MirrorY
                    };

                    var rawData = depthTexture.GetRawTextureData<byte>();
                    depthImage.Convert(conversionParams, rawData);
                    depthTexture.Apply();
                }
                finally
                {
                    depthImage.Dispose();
                }
            }
            else
            {
                depthBufferAvailable = false;
            }
        }

        /// <summary>
        /// Sample the AR depth buffer at a normalized screen point.
        /// Returns depth in meters, or -1 if unavailable.
        /// </summary>
        private float GetARDepthAtPoint(Vector2 normalizedPoint)
        {
            if (!depthBufferAvailable || depthTexture == null)
                return -1f;

            int x = Mathf.Clamp((int)(normalizedPoint.x * depthTexture.width), 0, depthTexture.width - 1);
            int y = Mathf.Clamp((int)(normalizedPoint.y * depthTexture.height), 0, depthTexture.height - 1);

            Color pixel = depthTexture.GetPixel(x, y);
            float depth = pixel.r; // RFloat format — red channel contains depth in meters

            if (depth <= 0 || float.IsNaN(depth) || float.IsInfinity(depth))
                return -1f;

            return depth;
        }

        /// <summary>
        /// Sample the AR depth buffer across a bounding box region and return
        /// the median depth. Using median avoids background depth bleeding
        /// around the person's silhouette edges.
        /// </summary>
        private float GetARDepthForRegion(Rect boundingBox)
        {
            if (!depthBufferAvailable || depthTexture == null)
                return -1f;

            // Sample a grid of points within the bounding box center region
            // (use inner 60% to avoid edge contamination from background)
            float inset = 0.2f;
            float x0 = boundingBox.x + boundingBox.width * inset;
            float y0 = boundingBox.y + boundingBox.height * inset;
            float dx = boundingBox.width * (1f - 2f * inset);
            float dy = boundingBox.height * (1f - 2f * inset);

            const int samples = 9; // 3x3 grid
            var depths = new float[samples];
            int validCount = 0;

            for (int gy = 0; gy < 3; gy++)
            {
                for (int gx = 0; gx < 3; gx++)
                {
                    Vector2 samplePoint = new Vector2(
                        x0 + dx * (gx / 2f),
                        y0 + dy * (gy / 2f));

                    float d = GetARDepthAtPoint(samplePoint);
                    if (d > 0)
                    {
                        depths[validCount++] = d;
                    }
                }
            }

            if (validCount == 0)
                return -1f;

            // Return median of valid samples
            Array.Sort(depths, 0, validCount);
            return depths[validCount / 2];
        }

        // ─────────────────────────────────────────────────────────────────
        //  Strategy 2: Stereo Disparity
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Estimate depth from stereo camera disparity.
        /// depth = (baseline * focalLength) / disparity
        /// This is a placeholder — real implementation requires stereo matching
        /// from the device's stereo camera pair.
        /// </summary>
        private float GetStereoDepthAtPoint(Vector2 normalizedPoint)
        {
            // In production, this would:
            // 1. Capture synchronized frames from left and right cameras
            // 2. Run stereo matching (block matching, semi-global matching, or ML-based)
            // 3. Compute disparity map
            // 4. Sample disparity at the requested point
            // 5. Convert disparity to depth: depth = baseline * focalLength / disparity

            // For devices with passthrough cameras (Quest 3, Galaxy XR),
            // the runtime may provide a depth map directly.
            // Check the XR depth subsystem first.
            if (occlusionManager != null && depthBufferAvailable)
            {
                return GetARDepthAtPoint(normalizedPoint);
            }

            // Stereo depth estimation would be implemented via a native plugin
            // or the device's SDK. Return -1 to fall through to heuristic.
            return -1f;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Strategy 3: Bounding Box Height Heuristic
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Estimate depth from the bounding box height using the pinhole camera model.
        /// Assumes the detected person is approximately averagePersonHeightMeters tall.
        /// depth = realHeight / (normalizedHeight * 2 * tan(vFOV/2))
        /// </summary>
        private float EstimateDepthFromBBoxHeight(float normalizedBoxHeight)
        {
            if (normalizedBoxHeight <= 0.01f)
                return maxDepthMeters;

            float vFOV = arCamera != null ? arCamera.fieldOfView : 60f;
            float halfFOVRad = vFOV * 0.5f * Mathf.Deg2Rad;
            float viewHeight = 2f * Mathf.Tan(halfFOVRad);

            float depth = averagePersonHeightMeters / (normalizedBoxHeight * viewHeight);
            return depth;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Depth Smoothing
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Smooth depth readings over time to reduce jitter. Uses a grid-based
        /// cache indexed by screen region.
        /// </summary>
        private float SmoothDepth(Vector2 normalizedPoint, float rawDepth)
        {
            int gx = Mathf.Clamp((int)(normalizedPoint.x * DepthGridSize), 0, DepthGridSize - 1);
            int gy = Mathf.Clamp((int)(normalizedPoint.y * DepthGridSize), 0, DepthGridSize - 1);
            int index = gy * DepthGridSize + gx;

            float prev = depthCache[index];
            if (prev <= 0)
            {
                depthCache[index] = rawDepth;
                return rawDepth;
            }

            float smoothed = Mathf.Lerp(prev, rawDepth, depthSmoothingFactor);
            depthCache[index] = smoothed;
            return smoothed;
        }

        /// <summary>
        /// Clear the depth smoothing cache (e.g., after a teleport or scene change).
        /// </summary>
        public void ClearDepthCache()
        {
            Array.Clear(depthCache, 0, depthCache.Length);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Depth Strategy Enum
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Depth estimation strategies, ordered by accuracy.
    /// </summary>
    public enum DepthStrategy
    {
        /// <summary>
        /// Direct depth from AR hardware (LiDAR, ToF sensor).
        /// Most accurate but requires specific hardware.
        /// </summary>
        ARDepthBuffer,

        /// <summary>
        /// Depth from stereo camera triangulation.
        /// Good accuracy for devices with dual cameras.
        /// </summary>
        StereoDisparity,

        /// <summary>
        /// Depth estimated from bounding box size relative to
        /// known average person height. Always available but
        /// least accurate.
        /// </summary>
        BoundingBoxHeuristic
    }
}
