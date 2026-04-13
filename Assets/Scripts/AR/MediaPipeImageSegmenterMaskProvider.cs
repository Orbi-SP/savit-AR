using System;
using System.Collections;
using Mediapipe;
using Mediapipe.Tasks.Core;
using Mediapipe.Tasks.Vision.Core;
using Mediapipe.Tasks.Vision.ImageSegmenter;
using Mediapipe.Unity;
using Unity.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace SavitGame.AR {
    /// <summary>
    /// Runs MediaPipe Tasks ImageSegmenter on AR camera frames and publishes a person mask texture
    /// into <see cref="PersonOcclusionMaskSource"/>.
    ///
    /// Notes:
    /// - Requires the segmentation model under Assets/StreamingAssets (copied from the MediaPipe package resources).
    /// - Runs at a configurable frequency to keep AR framerate high.
    /// </summary>
    public sealed class MediaPipeImageSegmenterMaskProvider : MonoBehaviour {
        public enum ModelVariant {
            SelfieSquare,
            SelfieLandscape,
        }

        [Header("Scene References")]
        public ARCameraManager arCameraManager;
        public PersonOcclusionMaskSource maskSource;

        [Header("Model")]
        public ModelVariant modelVariant = ModelVariant.SelfieSquare;

        [Header("Inference")]
        [Tooltip("Use GPU delegate when available. If you see crashes/black mask on device, switch to CPU.")]
        public BaseOptions.Delegate delegateType =
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            BaseOptions.Delegate.CPU;
#else
            BaseOptions.Delegate.GPU;
#endif

        [Tooltip("How many times per second to run segmentation. 10-15 is usually enough for occlusion.")]
        [Range(1f, 60f)]
        public float inferenceHz = 12f;

        [Tooltip("Downscale the AR camera frame before inference. 256 is a good starting point for realtime occlusion.")]
        [Range(64, 1024)]
        public int maxInputDimension = 256;

        [Tooltip("Category index for person mask in the selfie segmentation model.")]
        public int categoryIndex = 0;

        [Header("Debug")]
        public bool debugMaskLogging = false;

        [Range(0.1f, 5f)]
        public float debugMaskLogIntervalSeconds = 1.0f;

        [Header("Mask Output")]
        [Tooltip("If true, mask is horizontally mirrored when reading from MediaPipe.")]
        public bool mirrorMaskHorizontally;

        [Tooltip("Force Y flip in our occlusion shader globals.")]
        public bool forceFlipY = true;

        [Tooltip("Force X flip in our occlusion shader globals.")]
        public bool forceFlipX = false;

        [Tooltip("Rotate mask sampling in shader (use this to fix ~90° misalignment).")]
        public PersonOcclusionMaskSource.MaskRotation rotation = PersonOcclusionMaskSource.MaskRotation.Deg0;

        [Header("AR Background Alignment")]
        [Tooltip("If true, publishes ARFoundation frameReceived displayMatrix as a UV transform for sampling the mask (fixes crop/scale/offset).")]
        public bool useArFrameDisplayUvTransform = true;

        [Tooltip("If true, publishes the inverse of ARFoundation's displayMatrix instead. Use this only if the mask becomes fully red/green due to incorrect UV mapping direction.")]
        public bool invertArFrameDisplayUvTransform = false;

        [Tooltip("If true, automatically chooses between displayMatrix and its inverse based on which one maps screen UV corners closer to [0..1]. Recommended to leave enabled.")]
        public bool autoDetectArDisplayUvDirection = true;

        [Tooltip("If true, resets manual rotation/flip when AR display UV transform is available (recommended).")]
        public bool preferArDisplayUvTransformOverManual = true;

        [Tooltip("Log AR display UV transform once for debugging.")]
        public bool debugDisplayUvLogging = false;

        public enum ArDisplayUvTranslationSource {
            M02_M12,
            M03_M13,
        }

        public enum ArDisplayUvMatrixPacking {
            // Treat displayMatrix as a 3x3 used like: uv' = M * float3(uv, 1)
            // Uses: x' = m00*x + m01*y + tX, y' = m10*x + m11*y + tY
            MatrixTimesVector,

            // Treat displayMatrix as a 3x3 used like: uv' = float3(uv, 1) * M
            // Uses: x' = m00*x + m10*y + tX, y' = m01*x + m11*y + tY
            // Translation then typically lives in m20/m21 (depending on how it's packed).
            VectorTimesMatrix,
        }

        [Tooltip("Which displayMatrix elements to treat as UV translation. Most ARFoundation UV affine matrices use m02/m12.")]
        public ArDisplayUvTranslationSource arDisplayUvTranslationSource = ArDisplayUvTranslationSource.M02_M12;

        [Tooltip("How to interpret the ARFoundation displayMatrix when packing it for shader UV mapping. Try VectorTimesMatrix if crop still doesn't match.")]
        public ArDisplayUvMatrixPacking arDisplayUvMatrixPacking = ArDisplayUvMatrixPacking.MatrixTimesVector;

        private IResourceManager _resourceManager;
        private ImageSegmenter _segmenter;

        private double _nextInferenceTime;

        private double _nextDebugLogTime;
        private bool _loggedDisplayUv;

        private NativeArray<byte> _rgbaBuffer;
        private int _rgbaWidth;
        private int _rgbaHeight;

        private float[] _maskFloats;
        private byte[] _maskBytes;
        private Texture2D _maskTexture;

        private bool _started;
        private bool _isRunning;

        private string ModelFileName => modelVariant == ModelVariant.SelfieLandscape
            ? "selfie_segmentation_landscape.bytes"
            : "selfie_segmentation.bytes";

        private void Reset() {
            arCameraManager = FindFirstObjectByType<ARCameraManager>();
            maskSource = FindFirstObjectByType<PersonOcclusionMaskSource>();
        }

        private void OnEnable() {
            if (!_started) {
                StartCoroutine(StartMediaPipe());
            } else {
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
                _segmenter?.Close();
            } catch {
                // ignore
            }

            if (_rgbaBuffer.IsCreated) {
                _rgbaBuffer.Dispose();
            }

            if (_maskTexture != null) {
                Destroy(_maskTexture);
            }

            if (delegateType == BaseOptions.Delegate.GPU) {
                GpuManager.Shutdown();
            }

            try {
                Glog.Shutdown();
            } catch {
                // ignore
            }

            Protobuf.ResetLogHandler();
        }

        private IEnumerator StartMediaPipe() {
            if (arCameraManager == null) {
                Debug.LogError("[PersonOcclusion] MediaPipeImageSegmenterMaskProvider: ARCameraManager reference is missing.");
                yield break;
            }
            if (maskSource == null) {
                Debug.LogError("[PersonOcclusion] MediaPipeImageSegmenterMaskProvider: PersonOcclusionMaskSource reference is missing.");
                yield break;
            }

            _started = true;

            Protobuf.SetLogHandler(Protobuf.DefaultLogHandler);
            try {
                Glog.Initialize("Savit-AR");
            } catch (Exception e) {
                Debug.LogWarning($"[PersonOcclusion] Glog.Initialize failed (continuing): {e.Message}");
            }

            // Prepare model file
            _resourceManager = new StreamingAssetsResourceManager();

            IEnumerator prepare;
            try {
                prepare = _resourceManager.PrepareAssetAsync(ModelFileName, ModelFileName, overwriteDestination: false);
            } catch (Exception e) {
                Debug.LogError($"[PersonOcclusion] Failed to prepare model '{ModelFileName}'. Make sure it exists in Assets/StreamingAssets. Error: {e}");
                yield break;
            }
            yield return prepare;

            if (delegateType == BaseOptions.Delegate.GPU) {
                yield return GpuManager.Initialize();
                if (!GpuManager.IsInitialized) {
                    Debug.LogWarning("[PersonOcclusion] GPU delegate requested but GPU resources failed to initialize; consider switching to CPU.");
                }
            }

            try {
                var options = new ImageSegmenterOptions(
                    new BaseOptions(delegateType, modelAssetPath: ModelFileName),
                    runningMode: Mediapipe.Tasks.Vision.Core.RunningMode.VIDEO
                );

                _segmenter = ImageSegmenter.CreateFromOptions(options, GpuManager.GpuResources);
            } catch (Exception e) {
                Debug.LogError($"[PersonOcclusion] Failed to create ImageSegmenter: {e}");
                yield break;
            }

            AutoConfigureMaskIndexFromLabels();

            maskSource.flipY = forceFlipY;
            maskSource.flipX = forceFlipX;
            maskSource.rotation = rotation;

            arCameraManager.frameReceived += OnCameraFrameReceived;
            _isRunning = true;
            _nextInferenceTime = 0;

            Debug.Log($"[PersonOcclusion] MediaPipe ImageSegmenter started (model={ModelFileName}, delegate={delegateType}, hz={inferenceHz}).");
        }

        private void AutoConfigureMaskIndexFromLabels() {
            if (_segmenter == null || maskSource == null) {
                return;
            }

            try {
                var labels = _segmenter.labels;
                if (labels == null || labels.Count == 0) {
                    return;
                }

                int personIndex = -1;
                int backgroundIndex = -1;

                for (int i = 0; i < labels.Count; i++) {
                    var label = labels[i];
                    if (string.IsNullOrEmpty(label)) continue;

                    if (label.IndexOf("person", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        label.IndexOf("foreground", StringComparison.OrdinalIgnoreCase) >= 0) {
                        personIndex = i;
                    }

                    if (label.IndexOf("background", StringComparison.OrdinalIgnoreCase) >= 0) {
                        backgroundIndex = i;
                    }
                }

                if (personIndex >= 0) {
                    categoryIndex = personIndex;
                    maskSource.invert = false;
                } else if (backgroundIndex >= 0) {
                    // If we only have a background confidence mask, invert it.
                    categoryIndex = backgroundIndex;
                    maskSource.invert = true;
                } else if (labels.Count == 2) {
                    // Common segmentation convention: [0]=background, [1]=person.
                    categoryIndex = 1;
                    maskSource.invert = false;
                } else {
                    categoryIndex = Mathf.Clamp(categoryIndex, 0, labels.Count - 1);
                }

                if (debugMaskLogging) {
                    Debug.Log($"[PersonOcclusion] Segmenter labels=[{string.Join(",", labels)}] -> categoryIndex={categoryIndex}, invert={maskSource.invert}");
                }
            } catch (Exception e) {
                Debug.LogWarning($"[PersonOcclusion] Failed to auto-configure labels: {e.Message}");
            }
        }

        private void OnCameraFrameReceived(ARCameraFrameEventArgs args) {
            if (!_isRunning || _segmenter == null) {
                return;
            }

            if (maskSource != null) {
                bool hasDisplayUv = useArFrameDisplayUvTransform && args.displayMatrix.HasValue;
                bool preferAuto = hasDisplayUv && preferArDisplayUvTransformOverManual;

                // If available, use ARFoundation's displayMatrix to match AR background UV mapping.
                // This tends to fix scale/offset mismatch (mask appears smaller or off-center).
                if (hasDisplayUv) {
                    // IMPORTANT:
                    // AR Foundation / ARCore background shaders treat the display matrix as row-major
                    // and apply it like: uv' = mul(float4(uv.x, uv.y, 1, 0), _UnityDisplayTransform).xy
                    // That implies:
                    //   x' = uv.x*m00 + uv.y*m10 + 1*m20
                    //   y' = uv.x*m01 + uv.y*m11 + 1*m21
                    // so we pack (m00, m10, m20) and (m01, m11, m21).
                    //
                    // We keep older inspector fields (translation source / packing) for compatibility,
                    // but the runtime always uses the ARFoundation convention above.

                    static Vector2 MapUvRowVectorTimesMatrix(Vector2 uv, Matrix4x4 m) {
                        return new Vector2(
                            uv.x * m.m00 + uv.y * m.m10 + 1f * m.m20,
                            uv.x * m.m01 + uv.y * m.m11 + 1f * m.m21
                        );
                    }

                    static int ScoreInRange(Matrix4x4 m) {
                        // Prefer transforms that keep corners within [0..1] (with small tolerance).
                        const float lo = -0.01f;
                        const float hi = 1.01f;

                        int score = 0;
                        Vector2[] corners = {
                            new Vector2(0f, 0f),
                            new Vector2(1f, 0f),
                            new Vector2(0f, 1f),
                            new Vector2(1f, 1f),
                        };
                        for (int i = 0; i < corners.Length; i++) {
                            var p = MapUvRowVectorTimesMatrix(corners[i], m);
                            if (p.x >= lo && p.x <= hi && p.y >= lo && p.y <= hi) {
                                score++;
                            }
                        }
                        return score;
                    }

                    var mForward = args.displayMatrix.Value;
                    var mInverse = mForward.inverse;

                    Matrix4x4 mChosen;
                    if (autoDetectArDisplayUvDirection) {
                        var sF = ScoreInRange(mForward);
                        var sI = ScoreInRange(mInverse);
                        mChosen = sI > sF ? mInverse : mForward;
                    } else {
                        mChosen = invertArFrameDisplayUvTransform ? mInverse : mForward;
                    }

                    var m = mChosen;

                    float a = m.m00;
                    float b = m.m10;
                    float d = m.m01;
                    float e = m.m11;
                    float tx = m.m20;
                    float ty = m.m21;

                    maskSource.useArDisplayUvTransform = true;
                    maskSource.arDisplayUvTransform0 = new Vector4(a, b, tx, 0f);
                    maskSource.arDisplayUvTransform1 = new Vector4(d, e, ty, 0f);

                    if (preferArDisplayUvTransformOverManual) {
                        // Let displayMatrix handle orientation/aspect mapping.
                        maskSource.flipY = false;
                        maskSource.flipX = false;
                        maskSource.rotation = PersonOcclusionMaskSource.MaskRotation.Deg0;
                        // Avoid double-mirroring: keep MediaPipe read as-is.
                        mirrorMaskHorizontally = false;
                    }

                    if (debugDisplayUvLogging && !_loggedDisplayUv) {
                        _loggedDisplayUv = true;
                        Debug.Log($"[PersonOcclusion] DisplayUV: screen={UnityEngine.Screen.width}x{UnityEngine.Screen.height} orient={UnityEngine.Screen.orientation} autoDir={autoDetectArDisplayUvDirection} invertFlag={invertArFrameDisplayUvTransform} " +
                                  $"(legacy tSrc={arDisplayUvTranslationSource} pack={arDisplayUvMatrixPacking})");

                        void LogMatrix(string label, Matrix4x4 mm) {
                            Debug.Log($"[PersonOcclusion] DisplayUV {label}: m00={mm.m00:F4} m01={mm.m01:F4} m02={mm.m02:F4} m03={mm.m03:F4} | " +
                                      $"m10={mm.m10:F4} m11={mm.m11:F4} m12={mm.m12:F4} m13={mm.m13:F4} | " +
                                      $"m20={mm.m20:F4} m21={mm.m21:F4} m22={mm.m22:F4} m23={mm.m23:F4} | " +
                                      $"m30={mm.m30:F4} m31={mm.m31:F4} m32={mm.m32:F4} m33={mm.m33:F4}");
                        }

                        Vector2[] corners = {
                            new Vector2(0f, 0f),
                            new Vector2(1f, 0f),
                            new Vector2(0f, 1f),
                            new Vector2(1f, 1f),
                        };

                        void LogCorners(string label, Matrix4x4 mm) {
                            var p00 = MapUvRowVectorTimesMatrix(corners[0], mm);
                            var p10 = MapUvRowVectorTimesMatrix(corners[1], mm);
                            var p01 = MapUvRowVectorTimesMatrix(corners[2], mm);
                            var p11 = MapUvRowVectorTimesMatrix(corners[3], mm);
                            Debug.Log($"[PersonOcclusion] DisplayUV corners {label}: (0,0)->({p00.x:F3},{p00.y:F3}) (1,0)->({p10.x:F3},{p10.y:F3}) (0,1)->({p01.x:F3},{p01.y:F3}) (1,1)->({p11.x:F3},{p11.y:F3})");
                        }

                        LogMatrix("forward", mForward);
                        LogCorners("forward", mForward);
                        LogMatrix("inverse", mInverse);
                        LogCorners("inverse", mInverse);
                        LogMatrix("chosen", m);
                        LogCorners("chosen", m);
                    }
                } else {
                    maskSource.useArDisplayUvTransform = false;
                }

                // Keep manual globals in sync even if you tweak them live while running.
                // When preferAuto=true, we intentionally ignore these to avoid double transforms.
                if (!preferAuto) {
                    maskSource.flipY = forceFlipY;
                    maskSource.flipX = forceFlipX;
                    maskSource.rotation = rotation;
                }
            }

            var now = Time.unscaledTimeAsDouble;
            if (now < _nextInferenceTime) {
                return;
            }
            _nextInferenceTime = now + (1.0 / Mathf.Max(1f, inferenceHz));

            if (!arCameraManager.TryAcquireLatestCpuImage(out var cpuImage)) {
                return;
            }

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

                // Build Mediapipe Image (CPU)
                using var mpImage = new Image(TextureFormat.RGBA32.ToImageFormat(), outW, outH, outW * 4, _rgbaBuffer);

                var result = ImageSegmenterResult.Alloc();
                try {
                    if (_segmenter.TrySegmentForVideo(mpImage, GetTimestampMillis(), new ImageProcessingOptions(rotationDegrees: 0), ref result)) {
                        if (debugMaskLogging) {
                            MaybeLogMaskResult(result);
                        }
                        PublishMask(result);
                    }
                } finally {
                    // Dispose all masks (mirrors sample behavior)
                    if (result.confidenceMasks != null) {
                        foreach (var mask in result.confidenceMasks) {
                            mask?.Dispose();
                        }
                    }
                    result.categoryMask?.Dispose();
                }
            } catch (Exception e) {
                Debug.LogWarning($"[PersonOcclusion] Segmentation failed: {e.Message}");
            } finally {
                cpuImage.Dispose();
            }
        }

        private (int width, int height) ComputeOutputDimensions(int inW, int inH) {
            var maxDim = Mathf.Clamp(maxInputDimension, 64, 1024);
            var srcMax = Mathf.Max(inW, inH);
            if (srcMax <= maxDim) {
                return (inW, inH);
            }

            var scale = (float)maxDim / srcMax;
            var w = Mathf.Max(2, Mathf.RoundToInt(inW * scale));
            var h = Mathf.Max(2, Mathf.RoundToInt(inH * scale));
            // Keep even sizes (some conversion paths prefer it)
            if ((w & 1) == 1) w -= 1;
            if ((h & 1) == 1) h -= 1;
            return (w, h);
        }

        private void PublishMask(ImageSegmenterResult result) {
            if (result.confidenceMasks == null || result.confidenceMasks.Count <= categoryIndex) {
                if (debugMaskLogging) {
                    var count = result.confidenceMasks == null ? -1 : result.confidenceMasks.Count;
                    Debug.Log($"[PersonOcclusion] No confidence mask available (count={count}, categoryIndex={categoryIndex}).");
                }
                return;
            }

            var mask = result.confidenceMasks[categoryIndex];
            if (mask == null) {
                return;
            }

            var w = mask.Width();
            var h = mask.Height();
            var pixelCount = w * h;

            if (_maskFloats == null || _maskFloats.Length != pixelCount) {
                _maskFloats = new float[pixelCount];
                _maskBytes = new byte[pixelCount];
            }

            // Read channel 0 into float array (0..1)
            var ok = mask.TryReadChannelNormalized(0, _maskFloats, isHorizontallyFlipped: mirrorMaskHorizontally, isVerticallyFlipped: false);
            if (!ok) {
                if (debugMaskLogging) {
                    Debug.LogWarning("[PersonOcclusion] TryReadChannelNormalized failed; mask not published.");
                }
                return;
            }

            if (debugMaskLogging && maskSource != null) {
                // Lightweight stats to validate the mask isn't constant/empty and to help decide invert/threshold.
                float min = 1f;
                float max = 0f;
                double sum = 0.0;

                int aboveRaw = 0;
                int aboveInv = 0;
                float t = Mathf.Clamp01(maskSource.threshold);

                for (var i = 0; i < pixelCount; i++) {
                    var v = _maskFloats[i];
                    if (v < min) min = v;
                    if (v > max) max = v;
                    sum += v;
                    if (v > t) aboveRaw++;
                    if ((1f - v) > t) aboveInv++;
                }

                var mean = (float)(sum / Math.Max(1, pixelCount));
                var pctRaw = 100f * aboveRaw / Math.Max(1, pixelCount);
                var pctInv = 100f * aboveInv / Math.Max(1, pixelCount);

                Debug.Log($"[PersonOcclusion] Mask stats {w}x{h} min={min:F3} max={max:F3} mean={mean:F3} thr={t:F2} pct>thr raw={pctRaw:F1}% inv={pctInv:F1}% invert={maskSource.invert}");
            }

            for (var i = 0; i < pixelCount; i++) {
                var v = _maskFloats[i];
                if (v < 0f) v = 0f;
                if (v > 1f) v = 1f;
                _maskBytes[i] = (byte)(v * 255f);
            }

            if (_maskTexture == null || _maskTexture.width != w || _maskTexture.height != h) {
                if (_maskTexture != null) {
                    Destroy(_maskTexture);
                }
                _maskTexture = new Texture2D(w, h, TextureFormat.R8, mipChain: false, linear: true) {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
            }

            _maskTexture.LoadRawTextureData(_maskBytes);
            _maskTexture.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            maskSource.maskTexture = _maskTexture;
        }

        private void MaybeLogMaskResult(ImageSegmenterResult result) {
            var now = Time.unscaledTimeAsDouble;
            if (now < _nextDebugLogTime) return;
            _nextDebugLogTime = now + Mathf.Clamp(debugMaskLogIntervalSeconds, 0.1f, 5f);

            int count = result.confidenceMasks == null ? 0 : result.confidenceMasks.Count;
            string sizeInfo = "n/a";
            if (count > 0 && result.confidenceMasks[0] != null) {
                var m = result.confidenceMasks[0];
                sizeInfo = $"{m.Width()}x{m.Height()}";
            }

            var invert = maskSource != null && maskSource.invert;
            var thr = maskSource != null ? maskSource.threshold : -1f;
            var flipX = maskSource != null && maskSource.flipX;
            var rot = maskSource != null ? (int)maskSource.rotation : -1;
            Debug.Log($"[PersonOcclusion] Segment ok. confidenceMasks={count} firstSize={sizeInfo} categoryIndex={categoryIndex} mirrorX={mirrorMaskHorizontally} flipY={forceFlipY} flipX={flipX} rot={rot} invert={invert} thr={thr:F2}");
        }

        private void EnsureRgbaBuffer(int width, int height) {
            if (_rgbaBuffer.IsCreated && _rgbaWidth == width && _rgbaHeight == height) {
                return;
            }

            if (_rgbaBuffer.IsCreated) {
                _rgbaBuffer.Dispose();
            }

            _rgbaWidth = width;
            _rgbaHeight = height;
            _rgbaBuffer = new NativeArray<byte>(width * height * 4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        }

        private static long GetTimestampMillis() {
            // MediaPipe expects a monotonically increasing timestamp.
            return (long)(Time.realtimeSinceStartupAsDouble * 1000.0);
        }
    }
}
