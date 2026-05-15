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
    /// Detecta mão via MediaPipe HandLandmarker direto no celular.
    /// Fechar mão = IsHolding, posição normalizada em HandPositionX/Y.
    /// Auto-encontra ARCameraManager. Zero configuração no Inspector.
    /// </summary>
    public sealed class MediaPipeHandTracker : MonoBehaviour {

        [Header("Scene References")]
        public ARCameraManager arCameraManager;

        [Header("Model")]
        public string modelFileName = "hand_landmarker.bytes";

        [Header("Inference")]
        public BaseOptions.Delegate delegateType =
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
            BaseOptions.Delegate.CPU;
#else
            BaseOptions.Delegate.GPU;
#endif

        [Range(1f, 30f)]
        public float inferenceHz = 15f;

        [Range(64, 512)]
        public int maxInputDimension = 256;

        [Range(1, 2)]
        public int maxNumHands = 1;

        [Range(0f, 1f)]
        public float minDetectionConfidence = 0.5f;

        [Range(0f, 1f)]
        public float minTrackingConfidence = 0.5f;

        [Header("Gesture Detection")]
        [Range(1, 5)]
        public int minFingersForFree = 2;

        [Header("Debug")]
        public bool debugLogs = true; // ativado por padrão para facilitar diagnóstico

        // ──── Public API ────────────────────────────────────────────────────

        /// <summary>True quando o MediaPipe está inicializado e processando frames.</summary>
        public bool IsReady => _isRunning && _handLandmarker != null;

        /// <summary>True quando pelo menos uma mão é detectada.</summary>
        public bool IsHandDetected { get; private set; }

        /// <summary>True quando a mão está fechada (segurando).</summary>
        public bool IsHolding { get; private set; }

        /// <summary>Posição X normalizada da palma (0=esquerda, 1=direita).</summary>
        public float HandPositionX { get; private set; } = 0.5f;

        /// <summary>Posição Y normalizada da palma (0=topo, 1=baixo).</summary>
        public float HandPositionY { get; private set; } = 0.5f;

        /// <summary>Lado discreto: "left", "center", "right".</summary>
        public string CurrentSide { get; private set; } = "center";

        /// <summary>Número de dedos estendidos.</summary>
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

        private const int SmoothingFrames = 3;
        private readonly Queue<float> _xHistory = new Queue<float>();
        private readonly Queue<float> _yHistory = new Queue<float>();

        // ──── Lifecycle ──────────────────────────────────────────────────────

        private void Reset() {
            arCameraManager = FindFirstObjectByType<ARCameraManager>();
        }

        private void OnEnable() {
            if (arCameraManager == null)
                arCameraManager = FindFirstObjectByType<ARCameraManager>();

            if (!_started) {
                StartCoroutine(StartMediaPipe());
            } else if (_isRunning && arCameraManager != null) {
                arCameraManager.frameReceived += OnCameraFrameReceived;
            }
        }

        private void OnDisable() {
            if (arCameraManager != null)
                arCameraManager.frameReceived -= OnCameraFrameReceived;
            _isRunning = false;
        }

        private void OnDestroy() {
            try { _handLandmarker?.Close(); } catch { }
            if (_rgbaBuffer.IsCreated) _rgbaBuffer.Dispose();
        }

        // ──── Initialization ─────────────────────────────────────────────────

        private IEnumerator StartMediaPipe() {
            if (arCameraManager == null) {
                Debug.LogError("[HandTracker] ❌ ARCameraManager não encontrado! Não pode iniciar.");
                yield break;
            }

            _started = true;
            Debug.Log("[HandTracker] 🚀 Iniciando MediaPipe HandLandmarker...");

            // Protobuf / Glog (já pode ter sido inicializado pelo segmenter)
            try { Protobuf.SetLogHandler(Protobuf.DefaultLogHandler); } catch { }
            try { Glog.Initialize("Savit"); } catch { }

            // Preparar modelo
            _resourceManager = new StreamingAssetsResourceManager();
            IEnumerator prepare;
            try {
                prepare = _resourceManager.PrepareAssetAsync(modelFileName, modelFileName, overwriteDestination: false);
            } catch (Exception e) {
                Debug.LogError($"[HandTracker] ❌ Modelo '{modelFileName}' não encontrado no StreamingAssets: {e.Message}");
                yield break;
            }
            yield return prepare;
            Debug.Log("[HandTracker] ✅ Modelo carregado.");

            // GPU (compartilha com segmenter se já inicializado)
            if (delegateType == BaseOptions.Delegate.GPU) {
                if (!GpuManager.IsInitialized) {
                    yield return GpuManager.Initialize();
                }
                if (!GpuManager.IsInitialized) {
                    Debug.LogWarning("[HandTracker] ⚠️ GPU indisponível — usando CPU.");
                    delegateType = BaseOptions.Delegate.CPU;
                }
            }

            // Criar HandLandmarker — tenta GPU, se falhar tenta CPU
            if (!TryCreateHandLandmarker(delegateType)) {
                if (delegateType == BaseOptions.Delegate.GPU) {
                    Debug.LogWarning("[HandTracker] ⚠️ GPU falhou — tentando CPU...");
                    if (!TryCreateHandLandmarker(BaseOptions.Delegate.CPU)) {
                        Debug.LogError("[HandTracker] ❌ Falha ao criar HandLandmarker (GPU e CPU). IA desativada.");
                        yield break;
                    }
                } else {
                    Debug.LogError("[HandTracker] ❌ Falha ao criar HandLandmarker com CPU. IA desativada.");
                    yield break;
                }
            }

            arCameraManager.frameReceived += OnCameraFrameReceived;
            _isRunning = true;
            _nextInferenceTime = 0;

            Debug.Log($"[HandTracker] ✅ HandLandmarker PRONTO (delegate={delegateType}, hz={inferenceHz})");
        }

        private bool TryCreateHandLandmarker(BaseOptions.Delegate del) {
            try {
                var baseOpts = new BaseOptions(del, modelAssetPath: modelFileName);
                var options = new HandLandmarkerOptions(
                    baseOpts,
                    runningMode: RunningMode.VIDEO,
                    numHands: maxNumHands,
                    minHandDetectionConfidence: minDetectionConfidence,
                    minHandPresenceConfidence: minDetectionConfidence,
                    minTrackingConfidence: minTrackingConfidence
                );

                var gpuRes = (del == BaseOptions.Delegate.GPU) ? GpuManager.GpuResources : null;
                _handLandmarker = HandLandmarker.CreateFromOptions(options, gpuRes);
                Debug.Log($"[HandTracker] ✅ HandLandmarker criado com {del}");
                return true;
            } catch (Exception e) {
                Debug.LogError($"[HandTracker] ❌ Erro ao criar HandLandmarker ({del}): {e.Message}");
                return false;
            }
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
                if (_handLandmarker.TryDetectForVideo(mpImage, GetTimestampMillis(), null, ref result)) {
                    ProcessResult(result);
                } else {
                    IsHandDetected = false;
                }
            } catch (Exception e) {
                if (debugLogs)
                    Debug.LogWarning($"[HandTracker] Erro na inferência: {e.Message}");
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
            var landmarks = result.handLandmarks[0].landmarks;
            if (landmarks == null || landmarks.Count < 21) {
                IsHandDetected = false;
                return;
            }

            // Posição da palma (média entre WRIST=0 e MIDDLE_FINGER_MCP=9)
            float rawX = (landmarks[0].x + landmarks[9].x) * 0.5f;
            float rawY = (landmarks[0].y + landmarks[9].y) * 0.5f;

            // Suavização
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

            // Lado discreto
            if (HandPositionX < 0.33f) CurrentSide = "left";
            else if (HandPositionX > 0.66f) CurrentSide = "right";
            else CurrentSide = "center";

            // Contagem de dedos estendidos
            int extended = 0;
            if (landmarks[8].y < landmarks[6].y) extended++;   // indicador
            if (landmarks[12].y < landmarks[10].y) extended++; // médio
            if (landmarks[16].y < landmarks[14].y) extended++; // anelar
            if (landmarks[20].y < landmarks[18].y) extended++; // mindinho
            // polegar: distância lateral
            if (Mathf.Abs(landmarks[4].x - landmarks[2].x) > 0.04f) extended++;

            ExtendedFingers = extended;
            IsHolding = extended < minFingersForFree;

            if (debugLogs) {
                Debug.Log($"[HandTracker] ✋ pos=({HandPositionX:F2},{HandPositionY:F2}) dedos={ExtendedFingers} holding={IsHolding}");
            }
        }

        // ──── Helpers ────────────────────────────────────────────────────────

        private (int w, int h) ComputeOutputDimensions(int inW, int inH) {
            var maxDim = Mathf.Clamp(maxInputDimension, 64, 512);
            var srcMax = Mathf.Max(inW, inH);
            if (srcMax <= maxDim) return (inW, inH);
            var scale = (float)maxDim / srcMax;
            var w = Mathf.Max(2, Mathf.RoundToInt(inW * scale));
            var h = Mathf.Max(2, Mathf.RoundToInt(inH * scale));
            if ((w & 1) == 1) w--;
            if ((h & 1) == 1) h--;
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
