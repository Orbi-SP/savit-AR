using System;
using System.Collections;
using System.Collections.Generic;
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.HandLandmarker;
using Mediapipe.Unity;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace SavitGame.AR {
    /// <summary>
    /// Runs MediaPipe HandLandmarker on AR camera frames to detect hand gestures on-device.
    /// Detects open/closed hand (grab/release) and continuous hand position.
    ///
    /// Follows the same architecture as MediaPipeImageSegmenterMaskProvider.cs.
    /// Place on a GameObject in the AR scene and assign the ARCameraManager.
    /// </summary>
    public sealed class MediaPipeHandTracker : MonoBehaviour {

        [Header("Scene References")]
        public ARCameraManager arCameraManager;

        [Header("Model")]
        [Tooltip("Nome do modelo HandLandmarker no StreamingAssets (sem path).")]
        public string modelFileName = "hand_landmarker.bytes";

        [Header("Inference")]
        [Tooltip("Use GPU delegate when available. Switch to CPU if you see crashes on device.")]
        public BaseOptions.Delegate delegateType =
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            BaseOptions.Delegate.CPU;
#else
            BaseOptions.Delegate.GPU;
#endif

        [Tooltip("How many times per second to run hand detection.")]
        [Range(1f, 30f)]
        public float inferenceHz = 15f;

        [Tooltip("Downscale the AR camera frame before inference for performance.")]
        [Range(64, 512)]
        public int maxInputDimension = 256;

        [Tooltip("Maximum number of hands to detect.")]
        [Range(1, 2)]
        public int maxNumHands = 1;

        [Tooltip("Minimum confidence for hand detection.")]
        [Range(0f, 1f)]
        public float minDetectionConfidence = 0.5f;

        [Tooltip("Minimum confidence for hand tracking.")]
        [Range(0f, 1f)]
        public float minTrackingConfidence = 0.5f;

        [Header("Gesture Detection")]
        [Tooltip("Minimum number of extended fingers to consider 'Free' (open hand). If fewer, it's 'Hold'.")]
        [Range(1, 5)]
        public int minFingersForFree = 2;

        [Header("Debug")]
        public bool debugLogs = false;

        // ──── Public API (read by Api.cs and gameplay scripts) ────────────────

        /// <summary>True when at least one hand is detected in the current frame.</summary>
        public bool IsHandDetected { get; private set; }

        /// <summary>True when the hand is closed (Hold gesture = grabbing).</summary>
        public bool IsHolding { get; private set; }

        /// <summary>Normalized X position of the palm (0 = left edge, 1 = right edge).</summary>
        public float HandPositionX { get; private set; } = 0.5f;

        /// <summary>Normalized Y position of the palm (0 = top edge, 1 = bottom edge).</summary>
        public float HandPositionY { get; private set; } = 0.5f;

        /// <summary>Discrete side: "left", "center", or "right" (for backward compatibility).</summary>
        public string CurrentSide { get; private set; } = "center";

        /// <summary>Number of extended fingers detected in the last frame.</summary>
        public int ExtendedFingers { get; private set; }

        // ──── Internals ──────────────────────────────────────────────────────

        private IResourceManager _resourceManager;
        private HandLandmarker _handLandmarker;

        private double _nextInferenceTime;
        private bool _started;
        private bool _isRunning;

        private NativeArray<byte> _rgbaBuffer;
        private int _rgbaWidth;
        private int _rgbaHeight;

        // Smoothing (moving average over last N frames)
        private const int SmoothingFrames = 3;
        private readonly Queue<float> _xHistory = new Queue<float>();
        private readonly Queue<float> _yHistory = new Queue<float>();

        // ──── Lifecycle ──────────────────────────────────────────────────────

        private void Reset() {
            arCameraManager = FindFirstObjectByType<ARCameraManager>();
        }

        private void OnEnable() {
            if (!_started) {
                StartCoroutine(StartMediaPipe());
            } else if (arCameraManager != null) {
                arCameraManager.frameReceived += OnCameraFrameReceived;
                _isRunning = true;
            }
        }

        private void OnDisable() {
            if (arCameraManager != null) {
                arCameraManager.frameReceived -= OnCameraFrameReceived;
            }
            _isRunning = false;
        }

        private void OnDestroy() {
            try {
                _handLandmarker?.Close();
            } catch {
                // ignore
            }

            if (_rgbaBuffer.IsCreated) {
                _rgbaBuffer.Dispose();
            }
        }

        // ──── Initialization ─────────────────────────────────────────────────

        private IEnumerator StartMediaPipe() {
            if (arCameraManager == null) {
                Debug.LogError("[HandTracker] ARCameraManager reference is missing.");
                yield break;
            }

            _started = true;

            Protobuf.SetLogHandler(Protobuf.DefaultLogHandler);
            try {
                Glog.Initialize("Savit-HandTracker");
            } catch (Exception e) {
                Debug.LogWarning($"[HandTracker] Glog.Initialize failed (continuing): {e.Message}");
            }

            // Prepare model file
            _resourceManager = new StreamingAssetsResourceManager();

            IEnumerator prepare;
            try {
                prepare = _resourceManager.PrepareAssetAsync(modelFileName, modelFileName, overwriteDestination: false);
            } catch (Exception e) {
                Debug.LogError($"[HandTracker] Failed to prepare model '{modelFileName}'. Make sure it exists in Assets/StreamingAssets. Error: {e}");
                yield break;
            }
            yield return prepare;

            if (delegateType == BaseOptions.Delegate.GPU) {
                yield return GpuManager.Initialize();
                if (!GpuManager.IsInitialized) {
                    Debug.LogWarning("[HandTracker] GPU delegate requested but GPU resources failed to initialize; falling back to CPU.");
                }
            }

            try {
                var baseOptions = new BaseOptions(delegateType, modelAssetPath: modelFileName);

                var options = new HandLandmarkerOptions(
                    baseOptions,
                    runningMode: RunningMode.VIDEO,
                    numHands: maxNumHands,
                    minHandDetectionConfidence: minDetectionConfidence,
                    minHandPresenceConfidence: minDetectionConfidence,
                    minTrackingConfidence: minTrackingConfidence
                );

                _handLandmarker = HandLandmarker.CreateFromOptions(options, GpuManager.GpuResources);
            } catch (Exception e) {
                Debug.LogError($"[HandTracker] Failed to create HandLandmarker: {e}");
                yield break;
            }

            arCameraManager.frameReceived += OnCameraFrameReceived;
            _isRunning = true;
            _nextInferenceTime = 0;

            Debug.Log($"[HandTracker] MediaPipe HandLandmarker started (model={modelFileName}, delegate={delegateType}, hz={inferenceHz}).");
        }

        // ──── Frame Processing ───────────────────────────────────────────────

        private void OnCameraFrameReceived(ARCameraFrameEventArgs args) {
            if (!_isRunning || _handLandmarker == null) return;

            var now = Time.unscaledTimeAsDouble;
            if (now < _nextInferenceTime) return;
            _nextInferenceTime = now + (1.0 / Mathf.Max(1f, inferenceHz));

            if (!arCameraManager.TryAcquireLatestCpuImage(out var cpuImage)) return;

            try {
                var (outW, outH) = ComputeOutputDimensions(cpuImage.width, cpuImage.height);
                EnsureRgbaBuffer(outW, outH);

                var conversionParams = new XRCpuImage.ConversionParams {
                    inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                    outputDimensions = new Vector2Int(outW, outH),
                    outputFormat = TextureFormat.RGBA32,
                    transformation = XRCpuImage.Transformation.None,
                };

                cpuImage.Convert(conversionParams, _rgbaBuffer);

                using var mpImage = new Image(TextureFormat.RGBA32.ToImageFormat(), outW, outH, outW * 4, _rgbaBuffer);

                var result = HandLandmarkerResult.Alloc(maxNumHands);
                try {
                    if (_handLandmarker.TryDetectForVideo(mpImage, GetTimestampMillis(), null, ref result)) {
                        ProcessResult(result);
                    } else {
                        // No detection this frame
                        IsHandDetected = false;
                    }
                } finally {
                    // Cleanup result resources
                }
            } catch (Exception e) {
                if (debugLogs) {
                    Debug.LogWarning($"[HandTracker] Inference failed: {e.Message}");
                }
            } finally {
                cpuImage.Dispose();
            }
        }

        // ──── Result Processing ──────────────────────────────────────────────

        private void ProcessResult(HandLandmarkerResult result) {
            if (result.handLandmarks == null || result.handLandmarks.Count == 0) {
                IsHandDetected = false;
                return;
            }

            IsHandDetected = true;

            // Use the first detected hand
            var landmarks = result.handLandmarks[0].landmarks;
            if (landmarks == null || landmarks.Count < 21) {
                IsHandDetected = false;
                return;
            }

            // Calculate palm position (average of WRIST=0 and MIDDLE_FINGER_MCP=9)
            float rawX = (landmarks[0].x + landmarks[9].x) * 0.5f;
            float rawY = (landmarks[0].y + landmarks[9].y) * 0.5f;

            // Apply smoothing
            _xHistory.Enqueue(rawX);
            _yHistory.Enqueue(rawY);
            while (_xHistory.Count > SmoothingFrames) _xHistory.Dequeue();
            while (_yHistory.Count > SmoothingFrames) _yHistory.Dequeue();

            float smoothX = 0f, smoothY = 0f;
            foreach (var v in _xHistory) smoothX += v;
            foreach (var v in _yHistory) smoothY += v;
            smoothX /= _xHistory.Count;
            smoothY /= _yHistory.Count;

            HandPositionX = Mathf.Clamp01(smoothX);
            HandPositionY = Mathf.Clamp01(smoothY);

            // Discrete side (backward compatibility)
            if (HandPositionX < 0.33f)
                CurrentSide = "left";
            else if (HandPositionX > 0.66f)
                CurrentSide = "right";
            else
                CurrentSide = "center";

            // Count extended fingers
            int extended = 0;

            // Index finger: tip (8) higher than PIP (6)
            if (landmarks[8].y < landmarks[6].y) extended++;
            // Middle finger: tip (12) higher than PIP (10)
            if (landmarks[12].y < landmarks[10].y) extended++;
            // Ring finger: tip (16) higher than PIP (14)
            if (landmarks[16].y < landmarks[14].y) extended++;
            // Pinky: tip (20) higher than PIP (18)
            if (landmarks[20].y < landmarks[18].y) extended++;
            // Thumb: tip (4) lateral distance from CMC (2)
            // Check if thumb is extended by comparing x-distance
            float thumbTipX = landmarks[4].x;
            float thumbCmcX = landmarks[2].x;
            if (Mathf.Abs(thumbTipX - thumbCmcX) > 0.04f) extended++;

            ExtendedFingers = extended;
            IsHolding = extended < minFingersForFree;

            if (debugLogs) {
                Debug.Log($"[HandTracker] pos=({HandPositionX:F2},{HandPositionY:F2}) side={CurrentSide} fingers={ExtendedFingers} holding={IsHolding}");
            }
        }

        // ──── Helpers ────────────────────────────────────────────────────────

        private (int width, int height) ComputeOutputDimensions(int inW, int inH) {
            var maxDim = Mathf.Clamp(maxInputDimension, 64, 512);
            var srcMax = Mathf.Max(inW, inH);
            if (srcMax <= maxDim) return (inW, inH);

            var scale = (float)maxDim / srcMax;
            var w = Mathf.Max(2, Mathf.RoundToInt(inW * scale));
            var h = Mathf.Max(2, Mathf.RoundToInt(inH * scale));
            if ((w & 1) == 1) w -= 1;
            if ((h & 1) == 1) h -= 1;
            return (w, h);
        }

        private void EnsureRgbaBuffer(int width, int height) {
            if (_rgbaBuffer.IsCreated && _rgbaWidth == width && _rgbaHeight == height) return;

            if (_rgbaBuffer.IsCreated) _rgbaBuffer.Dispose();

            _rgbaWidth = width;
            _rgbaHeight = height;
            _rgbaBuffer = new NativeArray<byte>(width * height * 4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static long GetTimestampMillis() {
            return (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
        }
    }
}
