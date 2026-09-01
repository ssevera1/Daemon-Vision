// MLPersonDetector.cs — ML-based person detection pipeline using Unity Sentis
// Loads a pre-trained model (MobileNet SSD / YOLO) for real-time person
// detection. Captures frames from ARCameraManager, runs GPU inference via Sentis,
// filters for the "person" class (COCO class 0), estimates depth, and projects
// detections to world space for the PersonDetector to consume.
//
// UNITY_SENTIS is defined by DaemonVision.asmdef only when com.unity.sentis 2.x
// is installed. Sentis 2.x requires Unity 6; on Unity 2022.3 this subsystem
// compiles to a stub that logs once and stays inactive.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using DaemonVision.Core;

#if UNITY_SENTIS
using Unity.Sentis;
#endif

namespace DaemonVision.Detection
{
    /// <summary>
    /// ML inference pipeline for detecting people in camera frames.
    /// Pipeline: Capture -> Resize -> Infer -> Parse -> NMS -> Depth -> WorldProject -> Register
    /// Runs at a configurable FPS (default 5) to balance accuracy with battery life.
    /// </summary>
    public class MLPersonDetector : SubsystemBase
    {
        public override string Name => "MLPersonDetector";

        [Header("Model Settings")]
#if UNITY_SENTIS
        [Tooltip("Imported ONNX model. Preferred over the StreamingAssets fallback.")]
        [SerializeField] private ModelAsset modelAsset;
#endif
        [Tooltip("Fallback: a serialized .sentis file under StreamingAssets/Models/. " +
                 "Raw .onnx files cannot be loaded at runtime.")]
        [SerializeField] private string modelFileName = "person_detection.sentis";
        [SerializeField] private int modelInputWidth = 320;
        [SerializeField] private int modelInputHeight = 320;
        [SerializeField] private string modelInputName = "input";
        [SerializeField] private string modelOutputName = "output";

        [Header("Detection Settings")]
        [SerializeField] private float targetFPS = 5f;
        [SerializeField] private float confidenceThreshold = 0.5f;
        [SerializeField] private float nmsIoUThreshold = 0.45f;
        [SerializeField] private int personClassId = 0;  // COCO: person = class 0
        [SerializeField] private int maxDetectionsPerFrame = 10;

        [Header("Depth Estimation")]
        [SerializeField] private float averagePersonHeightMeters = 1.7f;
        [SerializeField] private float maxDepthMeters = 30f;
        [SerializeField] private float minDepthMeters = 0.5f;

        private ARCameraManager arCameraManager;
        private Camera arCamera;
        private PersonDetector personDetector;
        private DepthEstimator depthEstimator;

        private float inferenceTimer;
        private float inferenceInterval;
        private bool modelLoaded;
        private bool isProcessingFrame;

        // Reusable textures to avoid GC allocation per frame
        private Texture2D cameraTexture;
        private Texture2D resizedTexture;

#if UNITY_SENTIS
        private Model runtimeModel;
        private Worker worker;
#endif

        // Detection results buffer
        private readonly List<DetectionResult> frameDetections = new List<DetectionResult>();
        private readonly List<DetectionResult> nmsResults = new List<DetectionResult>();

        public bool ModelLoaded => modelLoaded;
        public int DetectionsLastFrame => nmsResults.Count;

        public event Action<List<DetectionResult>> OnDetectionsProcessed;

        // ─────────────────────────────────────────────────────────────────
        //  Lifecycle
        // ─────────────────────────────────────────────────────────────────

        protected override Task OnInitialize()
        {
            inferenceInterval = targetFPS > 0 ? 1f / targetFPS : 0.2f;
            arCamera = Manager.ARCamera;
            arCameraManager = FindObjectOfType<ARCameraManager>();

            resizedTexture = new Texture2D(modelInputWidth, modelInputHeight, TextureFormat.RGB24, false);

            LoadModel();

            return Task.CompletedTask;
        }

        public override void OnAllSubsystemsReady()
        {
            personDetector = GetSubsystem<PersonDetector>();
            depthEstimator = GetSubsystem<DepthEstimator>();
        }

        public override void Tick(float deltaTime)
        {
            if (!modelLoaded || personDetector == null)
                return;

            inferenceTimer += deltaTime;

            if (inferenceTimer >= inferenceInterval && !isProcessingFrame)
            {
                inferenceTimer = 0f;
                ProcessFrame();
            }
        }

        protected override void OnShutdown()
        {
            DisposeModel();

            if (cameraTexture != null)
            {
                UnityEngine.Object.Destroy(cameraTexture);
                cameraTexture = null;
            }

            if (resizedTexture != null)
            {
                UnityEngine.Object.Destroy(resizedTexture);
                resizedTexture = null;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Model Loading
        // ─────────────────────────────────────────────────────────────────

        private void LoadModel()
        {
#if UNITY_SENTIS
            try
            {
                if (modelAsset != null)
                {
                    runtimeModel = ModelLoader.Load(modelAsset);
                    Log($"ML model loaded from asset: {modelAsset.name}");
                }
                else
                {
                    // Note: on Android, StreamingAssets lives inside the APK and is not
                    // a plain file path. Assign a ModelAsset instead for device builds.
                    string modelPath = System.IO.Path.Combine(
                        Application.streamingAssetsPath, "Models", modelFileName);
                    if (!System.IO.File.Exists(modelPath))
                        throw new System.IO.FileNotFoundException("No model asset assigned and no file at path.", modelPath);

                    runtimeModel = ModelLoader.Load(modelPath);
                    Log($"ML model loaded from file: {modelFileName}");
                }

                worker = new Worker(runtimeModel, BackendType.GPUCompute);
                modelLoaded = true;
                Log($"ML inference ready ({modelInputWidth}x{modelInputHeight}, {targetFPS:F0} fps target)");
            }
            catch (Exception ex)
            {
                Warn($"Failed to load ML model: {ex.Message}. Person detection is disabled " +
                     "until a ModelAsset is assigned on MLPersonDetector.");
                modelLoaded = false;
            }
#else
            Warn("Unity Sentis 2.x is not installed (it requires Unity 6). " +
                 "ML person detection is disabled; the simulated detector still runs in the Editor.");
            modelLoaded = false;
#endif
        }

        private void DisposeModel()
        {
#if UNITY_SENTIS
            worker?.Dispose();
            worker = null;
            runtimeModel = null;
#endif
            modelLoaded = false;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Frame Processing Pipeline
        // ─────────────────────────────────────────────────────────────────

        private void ProcessFrame()
        {
            isProcessingFrame = true;

            try
            {
                // Step 1: Capture frame from AR camera
                if (!CaptureFrame())
                {
                    isProcessingFrame = false;
                    return;
                }

                // Step 2: Resize to model input dimensions
                ResizeTexture(cameraTexture, modelInputWidth, modelInputHeight, resizedTexture);

                // Step 3: Run inference
                RunInference(resizedTexture);

                // Step 4: Parse detections and filter for persons
                // (populated in RunInference -> ParseOutput)

                // Step 5: Non-maximum suppression
                nmsResults.Clear();
                NonMaxSuppression(frameDetections, nmsIoUThreshold, nmsResults);

                // Step 6: Estimate depth and project to world space
                for (int i = 0; i < nmsResults.Count && i < maxDetectionsPerFrame; i++)
                {
                    var detection = nmsResults[i];

                    // Estimate depth
                    detection.EstimatedDepth = EstimateDepth(detection);
                    nmsResults[i] = detection;

                    // Project bounding box center to world position
                    Vector2 screenCenter = detection.BoundingBox.center;
                    Vector3 worldPos = ScreenToWorld(screenCenter, detection.EstimatedDepth, arCamera);

                    // Step 7: Register with PersonDetector
                    if (personDetector != null && worldPos != Vector3.zero)
                    {
                        personDetector.RegisterDetection(
                            worldPos,
                            detection.BoundingBox,
                            detection.Confidence);
                    }
                }

                OnDetectionsProcessed?.Invoke(nmsResults);
            }
            catch (Exception ex)
            {
                Error($"Frame processing failed: {ex.Message}");
            }
            finally
            {
                isProcessingFrame = false;
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Step 1: Camera Frame Capture
        // ─────────────────────────────────────────────────────────────────

        private bool CaptureFrame()
        {
            if (arCameraManager == null)
                return false;

            // Try to acquire CPU image from AR camera subsystem
            if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
                return false;

            try
            {
                // Convert to RGBA32 for processing
                var conversionParams = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                    outputDimensions = new Vector2Int(cpuImage.width, cpuImage.height),
                    outputFormat = TextureFormat.RGBA32,
                    transformation = XRCpuImage.Transformation.MirrorY
                };

                // Create or resize camera texture
                if (cameraTexture == null ||
                    cameraTexture.width != cpuImage.width ||
                    cameraTexture.height != cpuImage.height)
                {
                    if (cameraTexture != null)
                        UnityEngine.Object.Destroy(cameraTexture);

                    cameraTexture = new Texture2D(
                        cpuImage.width, cpuImage.height,
                        TextureFormat.RGBA32, false);
                }

                // Copy image data to texture
                var rawData = cameraTexture.GetRawTextureData<byte>();
                cpuImage.Convert(conversionParams, rawData);
                cameraTexture.Apply();

                return true;
            }
            finally
            {
                cpuImage.Dispose();
            }
        }

        // ─────────────────────────────────────────────────────────────────
        //  Step 2: Texture Resize
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Resize a source texture to the target dimensions using bilinear filtering.
        /// Writes into the provided destination texture (must already be created at target size).
        /// </summary>
        public static void ResizeTexture(Texture2D source, int targetWidth, int targetHeight, Texture2D destination)
        {
            if (source == null || destination == null)
                return;

            // Use RenderTexture for GPU-accelerated resize
            RenderTexture rt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
            rt.filterMode = FilterMode.Bilinear;

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = rt;

            Graphics.Blit(source, rt);

            destination.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            destination.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
        }

        /// <summary>
        /// Convenience overload that creates and returns a new resized texture.
        /// Caller is responsible for destroying the returned texture.
        /// </summary>
        public static Texture2D ResizeTexture(Texture2D source, int targetWidth, int targetHeight)
        {
            var result = new Texture2D(targetWidth, targetHeight, TextureFormat.RGB24, false);
            ResizeTexture(source, targetWidth, targetHeight, result);
            return result;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Step 3: ML Inference
        // ─────────────────────────────────────────────────────────────────

        private void RunInference(Texture2D inputTexture)
        {
            frameDetections.Clear();

#if UNITY_SENTIS
            if (worker == null || inputTexture == null)
                return;

            try
            {
                // Create input tensor from texture
                // Normalize pixels to [0, 1] range as expected by most detection models
                using (var inputTensor = TextureConverter.ToTensor(inputTexture, new TextureTransform()
                    .SetDimensions(modelInputWidth, modelInputHeight, 3)))
                {
                    // Execute inference
                    worker.Schedule(inputTensor);

                    // PeekOutput returns a tensor the worker still owns (do not dispose it),
                    // and its data lives on the GPU. ReadbackAndClone() gives a CPU copy
                    // that the indexer can read; that copy is ours to dispose.
                    var outputTensor = worker.PeekOutput(modelOutputName) as Tensor<float>;
                    if (outputTensor != null)
                    {
                        using (var cpuTensor = outputTensor.ReadbackAndClone())
                        {
                            ParseModelOutput(cpuTensor);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Warn($"Inference failed: {ex.Message}");
            }
#endif
        }

#if UNITY_SENTIS
        /// <summary>
        /// Parse the model output tensor into detection results.
        /// Supports SSD MobileNet output format: [1, numDetections, 7]
        /// where each detection is [batch, classId, confidence, x1, y1, x2, y2]
        /// Also supports YOLO format: [1, numBoxes, 5+numClasses]
        /// </summary>
        private void ParseModelOutput(Tensor<float> output)
        {
            var shape = output.shape;

            if (shape.rank < 2)
                return;

            // SSD MobileNet format: [1, N, 7]
            if (shape.rank == 3 && shape[2] == 7)
            {
                ParseSSDOutput(output, shape);
            }
            // YOLO format: [1, N, 5+C] where C = num classes
            else if (shape.rank == 3 && shape[2] > 5)
            {
                ParseYOLOOutput(output, shape);
            }
            // Single detection array [N, 6] — some models output flat
            else if (shape.rank == 2 && shape[1] >= 6)
            {
                ParseFlatOutput(output, shape);
            }
        }

        private void ParseSSDOutput(Tensor<float> output, TensorShape shape)
        {
            int numDetections = shape[1];

            for (int i = 0; i < numDetections; i++)
            {
                int classId = (int)output[0, i, 1];
                float confidence = output[0, i, 2];

                if (classId != personClassId || confidence < confidenceThreshold)
                    continue;

                float x1 = output[0, i, 3];
                float y1 = output[0, i, 4];
                float x2 = output[0, i, 5];
                float y2 = output[0, i, 6];

                frameDetections.Add(new DetectionResult
                {
                    BoundingBox = new Rect(x1, y1, x2 - x1, y2 - y1),
                    Confidence = confidence,
                    ClassId = classId,
                    EstimatedDepth = 0f
                });
            }
        }

        private void ParseYOLOOutput(Tensor<float> output, TensorShape shape)
        {
            int numBoxes = shape[1];
            int numValues = shape[2];

            for (int i = 0; i < numBoxes; i++)
            {
                float objectness = output[0, i, 4];
                if (objectness < confidenceThreshold)
                    continue;

                // Find best class
                int bestClass = -1;
                float bestScore = 0f;

                for (int c = 5; c < numValues; c++)
                {
                    float score = output[0, i, c] * objectness;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestClass = c - 5;
                    }
                }

                if (bestClass != personClassId || bestScore < confidenceThreshold)
                    continue;

                // YOLO outputs center_x, center_y, width, height
                float cx = output[0, i, 0];
                float cy = output[0, i, 1];
                float w = output[0, i, 2];
                float h = output[0, i, 3];

                // Convert to normalized [0,1] coordinates
                float x1 = (cx - w / 2f) / modelInputWidth;
                float y1 = (cy - h / 2f) / modelInputHeight;
                float bw = w / modelInputWidth;
                float bh = h / modelInputHeight;

                frameDetections.Add(new DetectionResult
                {
                    BoundingBox = new Rect(x1, y1, bw, bh),
                    Confidence = bestScore,
                    ClassId = bestClass,
                    EstimatedDepth = 0f
                });
            }
        }

        private void ParseFlatOutput(Tensor<float> output, TensorShape shape)
        {
            int numDetections = shape[0];

            for (int i = 0; i < numDetections; i++)
            {
                int classId = (int)output[i, 0];
                float confidence = output[i, 1];

                if (classId != personClassId || confidence < confidenceThreshold)
                    continue;

                float x1 = output[i, 2];
                float y1 = output[i, 3];
                float x2 = output[i, 4];
                float y2 = output[i, 5];

                frameDetections.Add(new DetectionResult
                {
                    BoundingBox = new Rect(x1, y1, x2 - x1, y2 - y1),
                    Confidence = confidence,
                    ClassId = classId,
                    EstimatedDepth = 0f
                });
            }
        }
#endif

        // ─────────────────────────────────────────────────────────────────
        //  Step 5: Non-Maximum Suppression
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Apply Non-Maximum Suppression to remove overlapping detections.
        /// Keeps the highest-confidence detection when boxes overlap beyond the IoU threshold.
        /// </summary>
        public static void NonMaxSuppression(List<DetectionResult> detections, float iouThreshold,
            List<DetectionResult> results)
        {
            if (detections == null || detections.Count == 0)
                return;

            // Sort by confidence (descending)
            detections.Sort((a, b) => b.Confidence.CompareTo(a.Confidence));

            var suppressed = new bool[detections.Count];

            for (int i = 0; i < detections.Count; i++)
            {
                if (suppressed[i]) continue;

                results.Add(detections[i]);

                for (int j = i + 1; j < detections.Count; j++)
                {
                    if (suppressed[j]) continue;

                    float iou = CalculateIoU(detections[i].BoundingBox, detections[j].BoundingBox);
                    if (iou > iouThreshold)
                    {
                        suppressed[j] = true;
                    }
                }
            }
        }

        /// <summary>
        /// Convenience overload that returns a new list.
        /// </summary>
        public static List<DetectionResult> NonMaxSuppression(List<DetectionResult> detections, float iouThreshold)
        {
            var results = new List<DetectionResult>();
            NonMaxSuppression(detections, iouThreshold, results);
            return results;
        }

        /// <summary>
        /// Calculate Intersection over Union (IoU) between two rectangles.
        /// </summary>
        private static float CalculateIoU(Rect a, Rect b)
        {
            float x1 = Mathf.Max(a.xMin, b.xMin);
            float y1 = Mathf.Max(a.yMin, b.yMin);
            float x2 = Mathf.Min(a.xMax, b.xMax);
            float y2 = Mathf.Min(a.yMax, b.yMax);

            float intersectionWidth = Mathf.Max(0, x2 - x1);
            float intersectionHeight = Mathf.Max(0, y2 - y1);
            float intersectionArea = intersectionWidth * intersectionHeight;

            float areaA = a.width * a.height;
            float areaB = b.width * b.height;
            float unionArea = areaA + areaB - intersectionArea;

            if (unionArea <= 0) return 0f;
            return intersectionArea / unionArea;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Step 6: Depth Estimation
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Estimate the depth (distance) to a detected person. Uses the
        /// DepthEstimator subsystem if available, otherwise falls back to
        /// bounding box height heuristic.
        /// </summary>
        private float EstimateDepth(DetectionResult detection)
        {
            // Try DepthEstimator if available
            if (depthEstimator != null)
            {
                float depth = depthEstimator.EstimateDepthAtScreenPoint(
                    detection.BoundingBox.center);

                if (depth > 0)
                    return Mathf.Clamp(depth, minDepthMeters, maxDepthMeters);
            }

            // Fallback: bounding box height heuristic
            // If a person is ~1.7m tall, their apparent height in normalized
            // screen coordinates tells us roughly how far away they are.
            return EstimateDepthFromBBoxHeight(detection.BoundingBox.height);
        }

        /// <summary>
        /// Estimate depth from the bounding box height using the pinhole camera model.
        /// depth = (realHeight * focalLength) / (apparentHeight * sensorHeight)
        /// Simplified: depth ~ realHeight / (2 * tan(vFOV/2) * normalizedHeight)
        /// </summary>
        private float EstimateDepthFromBBoxHeight(float normalizedBoxHeight)
        {
            if (normalizedBoxHeight <= 0.01f)
                return maxDepthMeters;

            float vFOV = arCamera != null ? arCamera.fieldOfView : 60f;
            float halfFOVRad = vFOV * 0.5f * Mathf.Deg2Rad;
            float viewHeight = 2f * Mathf.Tan(halfFOVRad);

            // apparentHeight = normalizedBoxHeight * viewHeight (at distance 1m)
            // realHeight = depth * apparentHeight
            // depth = realHeight / apparentHeight = realHeight / (normalizedBoxHeight * viewHeight)
            float depth = averagePersonHeightMeters / (normalizedBoxHeight * viewHeight);

            return Mathf.Clamp(depth, minDepthMeters, maxDepthMeters);
        }

        // ─────────────────────────────────────────────────────────────────
        //  Step 7: Screen to World Projection
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Project a normalized screen-space point to a world-space position
        /// at the estimated depth from the camera.
        /// </summary>
        public static Vector3 ScreenToWorld(Vector2 normalizedScreenPoint, float estimatedDepth, Camera camera)
        {
            if (camera == null || estimatedDepth <= 0)
                return Vector3.zero;

            // Convert normalized [0,1] coordinates to pixel coordinates
            Vector3 screenPoint = new Vector3(
                normalizedScreenPoint.x * camera.pixelWidth,
                normalizedScreenPoint.y * camera.pixelHeight,
                estimatedDepth);

            // Use camera to convert screen to world
            Vector3 worldPoint = camera.ScreenToWorldPoint(screenPoint);

            return worldPoint;
        }

        // ─────────────────────────────────────────────────────────────────
        //  Configuration
        // ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Change the inference FPS at runtime (e.g., lower when on battery).
        /// </summary>
        public void SetTargetFPS(float fps)
        {
            targetFPS = Mathf.Max(0.5f, fps);
            inferenceInterval = 1f / targetFPS;
            Log($"Detection FPS set to {targetFPS:F1}");
        }

        /// <summary>
        /// Set the confidence threshold for accepting detections.
        /// </summary>
        public void SetConfidenceThreshold(float threshold)
        {
            confidenceThreshold = Mathf.Clamp01(threshold);
        }
    }

    // ─────────────────────────────────────────────────────────────────
    //  Detection Result
    // ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Result from a single ML detection. Contains bounding box in normalized
    /// screen coordinates [0,1], confidence score, class ID, and estimated depth.
    /// </summary>
    [Serializable]
    public struct DetectionResult
    {
        /// <summary>
        /// Bounding box in normalized screen coordinates [0,1].
        /// (x, y) is top-left corner; (width, height) are extents.
        /// </summary>
        public Rect BoundingBox;

        /// <summary>
        /// Detection confidence score [0, 1].
        /// </summary>
        public float Confidence;

        /// <summary>
        /// COCO class ID. Person = 0.
        /// </summary>
        public int ClassId;

        /// <summary>
        /// Estimated distance from camera in meters.
        /// Set during depth estimation phase.
        /// </summary>
        public float EstimatedDepth;
    }
}
