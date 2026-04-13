using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;
using UnityEngine.UI;
using TMPro;
using UnityEngine.XR.Management;
using UnityEngine.Rendering;

namespace SavitGame.AR {
    public class ARPlacementManager : MonoBehaviour {
        [Header("Referências AR")]
        public ARRaycastManager raycastManager;
        public ARPlaneManager planeManager;
        [Tooltip("Câmera AR usada para raycast/rotação do ghost (arraste a AR Camera do XR Origin)")]
        public Camera placementCamera;

        [Header("Prefabs")]
        [Tooltip("Prefab real da cena — spawnado após confirmar posição")]
        public GameObject gameScenePrefab;

        [Tooltip("Prefab ghost (duplicata do GameScenePrefab + componente GhostPreview)")]
        public GameObject ghostScenePrefab;

        [Header("Performance / Estabilidade")]
        [Tooltip("Se true, reaproveita o ghost (GameScenePreviewPrefab) como a cena final ao confirmar. Isso evita instanciar um segundo prefab pesado no Android, reduzindo pico de memória e risco de o app ser encerrado pelo sistema.")]
        public bool reuseGhostAsPlacedScene = true;

        [Header("UI")]
        public GameObject tapToPlaceUI;      // texto "Aponte para uma superfície"
        public GameObject confirmButton;     // botão "Colocar aqui" — aparece com o ghost
        [Tooltip("Botão opcional abaixo do 'Colocar aqui' para alternar pré-visualização do ghost (sólido/translúcido).")]
        public GameObject previewToggleButton;
        [Tooltip("Slider opcional para rotacionar no eixo Y durante o placement (yaw).")]
        public GameObject yawSlider;

        [Header("UI - Textos")]
        public string previewText = "Ver como está";
        public string backToPlaceText = "Voltar a colocar";

        [Header("Ajustes do Ghost")]
        [Tooltip("Velocidade de suavização do ghost ao acompanhar o pose")]
        public float ghostFollowSpeed = 12f;

        [Tooltip("Offset vertical para evitar z-fighting com o plano")]
        public float ghostVerticalOffset = 0.005f;

        [Header("Ajuste na Superfície")]
        [Tooltip("Se true, ajusta a altura para o objeto ficar 'em cima' do plano (evita ficar dentro da mesa/chão).")]
        public bool snapToSurface = true;

        [Tooltip("Folga extra (metros) ao assentar na superfície. Ex: 0.001 = 1mm")]
        public float surfaceSnapPadding = 0.0015f;

        [Tooltip("Recentra instância usando bounds dos renderers (corrige prefabs com offsets grandes)")]
        public bool recenterByRenderBounds = true;

        [Tooltip("Quando não houver plano, usa pose temporária à frente da câmera para não travar o fluxo")]
        public bool useFallbackPoseWhenNoPlane = true;

        [Tooltip("Se true, só mostra ghost/botão e só permite ConfirmPlacement quando houver plano (fallback não libera placement).")]
        public bool requirePlaneForPlacement = true;

        [Tooltip("Distância da pose de fallback à frente da câmera")]
        public float fallbackDistance = 1.2f;

        [Header("Escala")]
        [Tooltip("Escala aplicada ao prefab ao instanciar. Se o prefab veio de uma cena real em escala 1:1 (objetos a 60m de distância), use valores pequenos como 0.02.")]
        public float instanceScale = 1f;

        [Header("Rotação (Placement)")]
        [Tooltip("Offset de rotação em Y (graus) aplicado durante o placement (ghost e cena final).")]
        public float yawOffsetDegrees = 0f;

        [Tooltip("Passo (graus) usado pelos botões de girar.")]
        public float yawStepDegrees = 15f;

        [Header("Física")]
        [Tooltip("Após posicionar a cena, força todos Rigidbody do prefab a ficarem kinematic (evita peças 'voando' por instabilidade de física/teleportes).")]
        public bool makePlacedRigidbodiesKinematic = true;

        [Header("Debug")]
        [Tooltip("Liga logs throttled para diagnosticar tracking/centro de tela/alinhamento do ghost no device")]
        public bool debugLogs = false;

        [Tooltip("Intervalo (segundos) entre logs quando debugLogs=true")]
        public float debugLogInterval = 0.25f;

        [Tooltip("Intervalo (segundos) entre logs após o placement (root/peças).")]
        public float placedDebugLogInterval = 1.0f;

        [Header("Eventos")]
        [Tooltip("Chamado quando o objeto é posicionado. Conecte ao ARGameManager.OnScenePlaced()")]
        public UnityEvent onScenePlaced;

        private GameObject spawnedScene;
        private GameObject ghostInstance;
        private bool isPlaced = false;
        private bool placementAllowed = false;
        private bool confirmInProgress = false;
        private bool cameraWarningLogged = false;
        private bool confirmButtonWarningLogged = false;
        private Pose placementPose;
        private Pose lastValidPlacementPose;
        private bool hasLastValidPlacementPose = false;
        private bool placementPoseIsValid = false;
        // Offset entre o root do prefab e o centro visual (bounds) em ESPAÇO LOCAL da pose.
        // Precisa ser local para rotacionar junto com o ghost e não “derivar” quando a rotação muda.
        private Vector3 ghostRootOffsetLocal;
        private bool hasGhostRootOffsetLocal = false;
        private bool usingFallbackPose = false;
        private bool planePoseIsValid = false;
        private Pose lastValidPlanePose;
        private bool hasLastValidPlanePose = false;
        private TrackableId lastPlaneHitTrackableId;
        private bool hasLastPlaneHitTrackableId = false;
        private static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

        // Guards globais (caso existam 2 ARPlacementManager ativos ou 2 listeners no botão).
        private static int s_lastConfirmFrame = -1;
        // Lock atômico (0=livre, 1=ocupado) para bloquear reentrância/duplicidade no mesmo tap.
        private static int s_globalConfirmLock = 0;
        private static float s_lastConfirmTime = -999f;
        private const float k_confirmDebounceSeconds = 0.25f;
        private static bool s_globalPlacementDone = false;
        private static int s_activeInstanceCount = 0;

        private ARAnchorManager anchorManager;
        private ARAnchor spawnedAnchor;

        private AROcclusionManager occlusionManager;
        private ARShaderOcclusion shaderOcclusion;

        private float nextDebugLogTime = 0f;
        private Vector2 lastRaycastScreenCenter;
        private float nextPlacedDebugLogTime = 0f;

        private bool isGhostFrozenForPreview = false;
        private bool hasFrozenGhostPose = false;
        private Vector3 frozenGhostCenterPos;
        private Quaternion frozenGhostBaseRotation;
        private float frozenYawOffsetAtFreeze = 0f;

        private Slider yawSliderComponent;
        private bool yawSliderListenerAttached = false;

        private void Awake() {
            s_activeInstanceCount++;
            if (s_activeInstanceCount > 1) {
                Debug.LogWarning($"ARPlacementManager: detectadas {s_activeInstanceCount} instâncias ativas. Isso pode causar ConfirmPlacement duplicado.");
            }

            ResolveManagersIfNeeded();
            ResolveAnchorManagerIfNeeded();
            ResolveOcclusionIfNeeded();
            ResolvePlacementCamera();
            ResolveUIRefsIfNeeded();

            if (debugLogs) {
                Debug.Log($"[ARPlacementDebug] Awake instanceId={GetInstanceID()} go='{gameObject.name}'");
            }
        }

        private void OnDestroy() {
            s_activeInstanceCount = Mathf.Max(0, s_activeInstanceCount - 1);
        }

        private void Start() {
            ResolveManagersIfNeeded();
            ResolveAnchorManagerIfNeeded();
            ResolveOcclusionIfNeeded();
            ResolvePlacementCamera();
            ResolveUIRefsIfNeeded();

            // Proteção: se alguém arrastar prefabs como objetos na Hierarchy,
            // oculta para evitar peça/ghost fixo na cena antes do placement.
            HideSceneTemplateIfNeeded(gameScenePrefab, "gameScenePrefab");
            HideSceneTemplateIfNeeded(ghostScenePrefab, "ghostScenePrefab");

            if (tapToPlaceUI != null)
                tapToPlaceUI.SetActive(false); // ARGameManager controla quando mostrar

            if (confirmButton != null)
                confirmButton.SetActive(false);

            if (previewToggleButton != null)
                previewToggleButton.SetActive(false);

            if (yawSlider != null)
                yawSlider.SetActive(false);

            EnsureYawSliderSetup();

            if (debugLogs) {
                // Dá tempo do subsistema + SRP inicializarem e permite ver se o RP troca após alguns frames.
                StartCoroutine(LogOcclusionStatusLoop(durationSeconds: 12f, intervalSeconds: 2f));
            }

            if (confirmButton == null) {
                Debug.LogWarning("ARPlacementManager: confirmButton (Colocar aqui) não está atribuído/encontrado. Arraste o GameObject do botão no campo 'Confirm Button'.");
            }

            // Por padrão, o placement só fica ativo quando o ARGameManager entrar no estado Placing.
            placementAllowed = false;
        }

        private IEnumerator LogOcclusionStatusLoop(float durationSeconds, float intervalSeconds) {
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start <= durationSeconds) {
                LogOcclusionStatus();
                yield return new WaitForSeconds(intervalSeconds);
            }
        }

        private void ResolveOcclusionIfNeeded() {
            if (occlusionManager == null)
                occlusionManager = FindFirstObjectByType<AROcclusionManager>();

            // Preferência: colocar na AR Camera (onde normalmente ficam ARCameraManager/ARCameraBackground).
            if (occlusionManager == null) {
                var camMgr = FindFirstObjectByType<ARCameraManager>();
                if (camMgr != null) {
                    occlusionManager = camMgr.GetComponent<AROcclusionManager>();
                    if (occlusionManager == null)
                        occlusionManager = camMgr.gameObject.AddComponent<AROcclusionManager>();
                }
            }

            // Fallback: XROrigin
            if (occlusionManager == null) {
                var xrOrigin = FindFirstObjectByType<XROrigin>();
                if (xrOrigin != null) {
                    occlusionManager = xrOrigin.GetComponent<AROcclusionManager>();
                    if (occlusionManager == null)
                        occlusionManager = xrOrigin.gameObject.AddComponent<AROcclusionManager>();
                }
            }

            if (occlusionManager == null) {
                if (debugLogs) Debug.LogWarning("[ARPlacementDebug] AROcclusionManager não encontrado e não foi possível criar (sem ARCameraManager/XROrigin)." );
                return;
            }

            // Configurações recomendadas para ARCore Depth (Android).
            occlusionManager.requestedOcclusionPreferenceMode = OcclusionPreferenceMode.PreferEnvironmentOcclusion;
            occlusionManager.requestedEnvironmentDepthMode = EnvironmentDepthMode.Best;
            occlusionManager.environmentDepthTemporalSmoothingRequested = true;

            // Em Android, human segmentation normalmente não existe; mantém desligado.
            occlusionManager.requestedHumanDepthMode = HumanSegmentationDepthMode.Disabled;
            occlusionManager.requestedHumanStencilMode = HumanSegmentationStencilMode.Disabled;

            // Importante (ARCore/URP): ARShaderOcclusion NÃO "liga" oclusão por si só.
            // Além disso, quando habilitado, ele pode fazer o ARCameraBackground ignorar updates
            // de occlusion frame dependendo do modo de render, quebrando a oclusão de depth.
            // Para ARCore, preferimos o caminho padrão: AROcclusionManager + ARCameraBackground
            // (o shader do background escreve SV_Depth quando env depth está habilitado).
            shaderOcclusion = occlusionManager.GetComponent<ARShaderOcclusion>();
            if (shaderOcclusion != null && IsAndroidWithARCoreActive()) {
                if (shaderOcclusion.enabled) {
                    shaderOcclusion.enabled = false;
                    if (debugLogs) Debug.LogWarning("[ARPlacementDebug] ARShaderOcclusion desabilitado em runtime (Android/ARCore) para não bloquear o path de Environment Depth via ARCameraBackground.");
                }
            }

            // Também garante no GO da ARCameraBackground (é onde o ARCameraBackground checa esse componente).
            if (IsAndroidWithARCoreActive()) {
                var camBg = FindFirstObjectByType<ARCameraBackground>();
                if (camBg != null) {
                    var shaderOccOnCam = camBg.GetComponent<ARShaderOcclusion>();
                    if (shaderOccOnCam != null && shaderOccOnCam.enabled) {
                        shaderOccOnCam.enabled = false;
                        if (debugLogs) Debug.LogWarning("[ARPlacementDebug] ARShaderOcclusion desabilitado no GO da ARCameraBackground (Android/ARCore)." );
                    }
                }
            }

            if (debugLogs) {
                Debug.Log($"[ARPlacementDebug] Occlusion setup go='{occlusionManager.gameObject.name}' reqEnvDepth={occlusionManager.requestedEnvironmentDepthMode} reqPref={occlusionManager.requestedOcclusionPreferenceMode} smoothingReq={occlusionManager.environmentDepthTemporalSmoothingRequested}");
            }
        }

        private static bool IsAndroidWithARCoreActive() {
            if (Application.platform != RuntimePlatform.Android)
                return false;

            // XR Management loader name check (robusto o suficiente para distinguir ARCore em runtime).
            var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
            if (loader == null)
                return true; // Android build AR: assume ARCore se loader ainda não está pronto.

            var fullName = loader.GetType().FullName;
            return !string.IsNullOrEmpty(fullName) && fullName.IndexOf("ARCore", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void LogOcclusionStatus() {
            if (!debugLogs) return;
            if (occlusionManager == null) occlusionManager = FindFirstObjectByType<AROcclusionManager>();
            if (occlusionManager == null) {
                Debug.LogWarning("[ARPlacementDebug] Occlusion status: sem AROcclusionManager na cena.");
                return;
            }

            // URP note: em URP 7+ o ARCameraBackground requer ARBackgroundRendererFeature no Renderer.
            // Sem esse feature, o background pode até aparecer (dependendo do setup), mas a oclusão por depth
            // frequentemente não escreve no depth buffer e "parece que não funciona".
            int qIndex = -1;
            string qName = "n/a";
            try {
                qIndex = QualitySettings.GetQualityLevel();
                var names = QualitySettings.names;
                if (names != null && qIndex >= 0 && qIndex < names.Length) qName = names[qIndex];
            }
            catch { }

            RenderPipelineAsset rpCurrent = null;
            RenderPipelineAsset rpDefault = null;
            RenderPipelineAsset rpQuality = null;
            string rpInfo = "n/a";
            string rpSources = "";
            string arBgFeatureInfo = "n/a";
            try {
                rpCurrent = GraphicsSettings.currentRenderPipeline;
                rpDefault = GraphicsSettings.defaultRenderPipeline;

                // QualitySettings.renderPipeline existe em versões SRP, mas usa reflection pra ser resiliente.
                var rpProp = typeof(QualitySettings).GetProperty("renderPipeline", BindingFlags.Static | BindingFlags.Public);
                if (rpProp != null) {
                    rpQuality = rpProp.GetValue(null) as RenderPipelineAsset;
                }

                rpInfo = (rpCurrent ?? rpQuality ?? rpDefault) == null ? "Built-in" : (rpCurrent ?? rpQuality ?? rpDefault).GetType().Name;
                rpSources = $"rpCur={(rpCurrent == null ? "null" : rpCurrent.GetType().Name)} rpQual={(rpQuality == null ? "null" : rpQuality.GetType().Name)} rpDef={(rpDefault == null ? "null" : rpDefault.GetType().Name)}";

                bool hasArBackgroundFeature = false;

                var rpForIntrospection = rpCurrent ?? rpQuality ?? rpDefault;
                if (rpForIntrospection != null) {
                    // UniversalRenderPipelineAsset.GetRenderer(int) existe em URP, mas não vamos depender de referência direta.
                    var getRendererMethod = rpForIntrospection.GetType().GetMethod(
                        "GetRenderer",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        binder: null,
                        types: new[] { typeof(int) },
                        modifiers: null
                    );

                    object rendererObj = null;
                    if (getRendererMethod != null) {
                        rendererObj = getRendererMethod.Invoke(rpForIntrospection, new object[] { 0 });
                    }

                    if (rendererObj != null) {
                        // ScriptableRenderer.rendererFeatures (public) existe em versões recentes, mas pode variar.
                        var featuresProp = rendererObj.GetType().GetProperty(
                            "rendererFeatures",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
                        );

                        if (featuresProp != null) {
                            var featuresEnumerable = featuresProp.GetValue(rendererObj) as System.Collections.IEnumerable;
                            if (featuresEnumerable != null) {
                                foreach (var feature in featuresEnumerable) {
                                    if (feature == null) continue;
                                    var t = feature.GetType();
                                    var fullName = t.FullName ?? t.Name;
                                    if (fullName.IndexOf("ARBackgroundRendererFeature", System.StringComparison.OrdinalIgnoreCase) >= 0) {
                                        hasArBackgroundFeature = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }

                arBgFeatureInfo = hasArBackgroundFeature ? "present" : "missing";
            }
            catch (System.Exception ex) {
                arBgFeatureInfo = $"err:{ex.GetType().Name}";
            }

            Texture envTex;
            bool hasEnvTex = occlusionManager.TryGetEnvironmentDepthTexture(out envTex);
            if (!hasEnvTex) envTex = null;
            string envTexInfo = envTex == null ? "null" : $"{envTex.width}x{envTex.height} fmt={envTex.graphicsFormat}";

            // Se a textura GPU não aparece, tenta adquirir a CPU image só pra diagnosticar se o depth está vindo do subsistema.
            string envCpuInfo = "n/a";
            if (envTex == null) {
                try {
                    if (occlusionManager.TryAcquireEnvironmentDepthCpuImage(out var cpuImage)) {
                        envCpuInfo = $"cpuDepth={cpuImage.width}x{cpuImage.height} fmt={cpuImage.format}";
                        cpuImage.Dispose();
                    }
                    else {
                        envCpuInfo = "cpuDepth=null";
                    }
                }
                catch (System.Exception ex) {
                    envCpuInfo = $"cpuDepth=err:{ex.GetType().Name}";
                }
            }

            var camMgr = FindFirstObjectByType<ARCameraManager>();
            var mat = camMgr != null ? camMgr.cameraMaterial : null;
            string matInfo = mat == null ? "null" : $"'{mat.shader?.name}'";
            var camBg = FindFirstObjectByType<ARCameraBackground>();
            string renderModeInfo = camMgr == null ? "n/a" : camMgr.currentRenderingMode.ToString();
            string bgRenderModeInfo = camBg == null ? "n/a" : camBg.currentRenderingMode.ToString();
            bool kwDepth = mat != null && mat.IsKeywordEnabled("ARCORE_ENVIRONMENT_DEPTH_ENABLED");
            var envDepthOnMat = mat != null && mat.HasTexture("_EnvironmentDepth") ? mat.GetTexture("_EnvironmentDepth") : null;
            string envDepthTexOnMatInfo = envDepthOnMat == null ? "null" : $"{envDepthOnMat.width}x{envDepthOnMat.height} (mat _EnvironmentDepth)";

            var shaderOcc = occlusionManager.GetComponent<ARShaderOcclusion>();
            string shaderOccInfo = shaderOcc == null ? "none" : (shaderOcc.enabled ? $"enabled mode={shaderOcc.occlusionShaderMode}" : "disabled");

            Debug.Log(
                $"[ARPlacementDebug] Occlusion status currentEnvDepth={occlusionManager.currentEnvironmentDepthMode} " +
                $"currentPref={occlusionManager.currentOcclusionPreferenceMode} smoothingEnabled={occlusionManager.environmentDepthTemporalSmoothingEnabled} " +
                $"envDepthTex={envTexInfo} renderMode(camMgr)={renderModeInfo} renderMode(camBg)={bgRenderModeInfo} cameraMat={matInfo} kw(ARCORE_ENVIRONMENT_DEPTH_ENABLED)={kwDepth} matEnvDepth={envDepthTexOnMatInfo} shaderOcc={shaderOccInfo} rp={rpInfo} arBgFeature={arBgFeatureInfo}"
                + $" q={qIndex}:{qName} {rpSources} {envCpuInfo}"
            );
        }

        private Slider GetYawSliderComponent() {
            if (yawSliderComponent != null) return yawSliderComponent;
            if (yawSlider == null) return null;

            yawSliderComponent = yawSlider.GetComponent<Slider>();
            if (yawSliderComponent != null) return yawSliderComponent;

            yawSliderComponent = yawSlider.GetComponentInChildren<Slider>(includeInactive: true);
            return yawSliderComponent;
        }

        private void EnsureYawSliderSetup() {
            var slider = GetYawSliderComponent();
            if (slider == null) return;

            // Se o slider estiver mal configurado (range 0), ele sempre vai disparar 0.0.
            float range = slider.maxValue - slider.minValue;
            if (range < 1f) {
                slider.minValue = -180f;
                slider.maxValue = 180f;
            }

            slider.wholeNumbers = false;
            slider.interactable = true;

            // Auto-bind: não depende do wiring manual no Inspector.
            // (Em runtime, isso não remove PersistentListeners; apenas garante que funcione.)
            if (!yawSliderListenerAttached) {
                slider.onValueChanged.AddListener(OnYawSliderValueChanged);
                yawSliderListenerAttached = true;
            }

            UpdateYawSliderValue();

            if (debugLogs) {
                Debug.Log($"[ARPlacementDebug] yawSlider setup min={slider.minValue:F1} max={slider.maxValue:F1} whole={slider.wholeNumbers} interactable={slider.interactable}");
            }
        }

        private void OnYawSliderValueChanged(float value) {
            SetYawOffsetDegrees(value);
        }

        private void ResolveAnchorManagerIfNeeded() {
            if (anchorManager != null) return;

            anchorManager = FindFirstObjectByType<ARAnchorManager>();
            if (anchorManager != null) return;

            // Se a cena não tiver ARAnchorManager, cria em runtime para habilitar anchoring.
            // Preferência: anexar ao XROrigin (onde já ficam Raycast/Plane managers).
            var xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null) {
                anchorManager = xrOrigin.GetComponent<ARAnchorManager>();
                if (anchorManager == null) {
                    anchorManager = xrOrigin.gameObject.AddComponent<ARAnchorManager>();
                    if (debugLogs) Debug.Log("[ARPlacementDebug] ARAnchorManager criado em runtime no XROrigin.");
                }
                return;
            }

            // Fallback: cria um GO dedicado (menos ideal, mas melhor do que não ancorar).
            var go = new GameObject("ARAnchorManager(Runtime)");
            anchorManager = go.AddComponent<ARAnchorManager>();
            if (debugLogs) Debug.Log("[ARPlacementDebug] ARAnchorManager criado em runtime (GO dedicado).");
        }

        private void ResolveUIRefsIfNeeded() {
            // Se não estiver ligado no Inspector, tenta nomes comuns da cena.
            if (confirmButton == null) {
                var byName = GameObject.Find("PlaceHereButton");
                if (byName != null) confirmButton = byName;
            }

            // Tenta encontrar pelo texto do botão (TextMeshPro ou legacy Text).
            if (confirmButton == null) {
                var buttons = FindObjectsByType<Button>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var b in buttons) {
                    if (b == null) continue;
                    string n = b.gameObject.name;
                    if (n.IndexOf("colocar", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                        n.IndexOf("place", System.StringComparison.OrdinalIgnoreCase) >= 0) {
                        confirmButton = b.gameObject;
                        break;
                    }

                    var tmp = b.GetComponentInChildren<TMP_Text>(includeInactive: true);
                    if (tmp != null) {
                        var t = tmp.text;
                        if (!string.IsNullOrWhiteSpace(t) &&
                            (t.IndexOf("colocar aqui", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                             t.IndexOf("place here", System.StringComparison.OrdinalIgnoreCase) >= 0)) {
                            confirmButton = b.gameObject;
                            break;
                        }
                    }

                    var legacy = b.GetComponentInChildren<Text>(includeInactive: true);
                    if (legacy != null) {
                        var t = legacy.text;
                        if (!string.IsNullOrWhiteSpace(t) &&
                            (t.IndexOf("colocar aqui", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                             t.IndexOf("place here", System.StringComparison.OrdinalIgnoreCase) >= 0)) {
                            confirmButton = b.gameObject;
                            break;
                        }
                    }
                }
            }

            // Também aceita referência ao componente Button (se alguém arrastou o Button e não o GameObject pai).
            if (confirmButton == null) {
                var btn = FindFirstObjectByType<Button>();
                if (btn != null && btn.gameObject.name.IndexOf("Place", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    confirmButton = btn.gameObject;
            }
        }

        public void SetPlacementAllowed(bool allowed) {
            placementAllowed = allowed;

            if (!placementAllowed) {
                isGhostFrozenForPreview = false;
                if (ghostInstance != null) ghostInstance.SetActive(false);
                if (confirmButton != null) confirmButton.SetActive(false);
                if (previewToggleButton != null) previewToggleButton.SetActive(false);
                if (yawSlider != null) yawSlider.SetActive(false);
            }
        }

        /// <summary>
        /// Chamado pelo botão "Ver como está" / "Voltar a colocar".
        /// Congela o ghost na pose atual para o usuário inspecionar como ficaria se tivesse colocado.
        /// Ao clicar novamente, volta a acompanhar a superfície.
        /// </summary>
        public void ToggleGhostLook() {
            if (isPlaced) return;
            if (!placementAllowed) return;
            if (ghostInstance == null) return;

            isGhostFrozenForPreview = !isGhostFrozenForPreview;

            if (isGhostFrozenForPreview) {
                CacheFrozenGhostPose();
                ApplyFrozenYawIfNeeded();
            } else {
                hasFrozenGhostPose = false;
            }
            UpdatePreviewToggleButtonLabel();

            if (debugLogs) {
                Debug.Log($"[ARPlacementDebug] ghostPreviewFreeze toggled frozen={isGhostFrozenForPreview}");
            }
        }

        private void UpdatePreviewToggleButtonLabel() {
            if (previewToggleButton == null) return;

            string text = isGhostFrozenForPreview ? backToPlaceText : previewText;

            var tmp = previewToggleButton.GetComponentInChildren<TMP_Text>(includeInactive: true);
            if (tmp != null) {
                tmp.text = text;
                return;
            }

            var legacy = previewToggleButton.GetComponentInChildren<Text>(includeInactive: true);
            if (legacy != null) {
                legacy.text = text;
            }
        }

        private void HideSceneTemplateIfNeeded(GameObject obj, string fieldName) {
            if (obj == null) return;

            // scene.IsValid() == true => referência é objeto da cena (Hierarchy), não asset prefab.
            if (obj.scene.IsValid()) {
                obj.SetActive(false);
                Debug.LogWarning($"ARPlacementManager: '{fieldName}' aponta para objeto da Hierarchy. " +
                                 "Esse objeto foi ocultado para não ficar fixo na cena. " +
                                 "Use o prefab asset da janela Project nesse campo.");
            }
        }

        private void ResolvePlacementCamera() {
            if (placementCamera != null) return;

            // 1) Preferência: câmera do XR Origin
            var xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin != null && xrOrigin.Camera != null) {
                placementCamera = xrOrigin.Camera;
                return;
            }

            // 2) Fallback: câmera com ARCameraManager
            var arCamManager = FindFirstObjectByType<ARCameraManager>();
            if (arCamManager != null)
                placementCamera = arCamManager.GetComponent<Camera>();
        }

        private void ResolveManagersIfNeeded() {
            if (raycastManager != null && planeManager != null) return;

            var xrOrigin = FindFirstObjectByType<XROrigin>();

            if (raycastManager == null) {
                if (xrOrigin != null) {
                    raycastManager = xrOrigin.GetComponent<ARRaycastManager>();
                    if (raycastManager == null)
                        raycastManager = xrOrigin.GetComponentInChildren<ARRaycastManager>(includeInactive: true);
                }
                if (raycastManager == null) raycastManager = FindFirstObjectByType<ARRaycastManager>();
            }

            if (planeManager == null) {
                if (xrOrigin != null) {
                    planeManager = xrOrigin.GetComponent<ARPlaneManager>();
                    if (planeManager == null)
                        planeManager = xrOrigin.GetComponentInChildren<ARPlaneManager>(includeInactive: true);
                }
                if (planeManager == null) planeManager = FindFirstObjectByType<ARPlaneManager>();
            }
        }

        private void Update() {
            if (isPlaced) {
                MaybeLogPlacedState();
                return;
            }
            if (!placementAllowed) return;

            // Modo "Ver como está": mantém o ghost fixo, sem raycast/smoothing.
            if (isGhostFrozenForPreview) {
                if (ghostInstance != null && !ghostInstance.activeSelf) ghostInstance.SetActive(true);
                if (confirmButton != null && !confirmButton.activeSelf) confirmButton.SetActive(true);
                if (previewToggleButton != null && !previewToggleButton.activeSelf) previewToggleButton.SetActive(true);
                if (yawSlider != null && !yawSlider.activeSelf) {
                    yawSlider.SetActive(true);
                    EnsureYawSliderSetup();
                }
                SetTapToPlaceUIVisible(false);

                // Permite que slider/botões de rotação alterem o yaw mesmo com o ghost congelado.
                ApplyFrozenYawIfNeeded();
                return;
            }

            UpdatePlacementPose();
            UpdateGhost();
        }

        private void UpdatePlacementPose() {
            ResolvePlacementCamera();
            ResolveManagersIfNeeded();

            if (placementCamera == null) {
                placementPoseIsValid = false;
                if (!cameraWarningLogged) {
                    Debug.LogWarning("ARPlacementManager: câmera AR não encontrada. Atribua Placement Camera no Inspector.");
                    cameraWarningLogged = true;
                }
                return;
            }

            cameraWarningLogged = false;

            bool gotPlaneHit = false;
            if (raycastManager != null) {
                // Use o centro real da tela (mais robusto do que viewport da câmera em casos de rect/aspect).
                var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                lastRaycastScreenCenter = screenCenter;
                gotPlaneHit = raycastManager.Raycast(
                    screenCenter,
                    hits,
                    TrackableType.PlaneWithinPolygon
                );
            }

            planePoseIsValid = gotPlaneHit;

            if (gotPlaneHit) {
                placementPoseIsValid = true;
                placementPose = hits[0].pose;
                lastPlaneHitTrackableId = hits[0].trackableId;
                hasLastPlaneHitTrackableId = true;
                placementPose.position += Vector3.up * ghostVerticalOffset;
                lastValidPlacementPose = placementPose;
                hasLastValidPlacementPose = true;

                lastValidPlanePose = placementPose;
                hasLastValidPlanePose = true;

                // Girar para ficar na direção da câmera
                var cameraForward = placementCamera.transform.forward;
                var cameraBearing = new Vector3(cameraForward.x, 0, cameraForward.z);
                if (cameraBearing.sqrMagnitude < 0.0001f) cameraBearing = placementCamera.transform.forward;
                placementPose.rotation = Quaternion.LookRotation(cameraBearing.normalized);
                placementPose.rotation = ApplyYawOffset(placementPose.rotation);
                lastValidPlacementPose = placementPose;
                usingFallbackPose = false;

                MaybeLogDebug("plane", extra: $"hitCount={hits.Count} trackableId={hits[0].trackableId}");
                return;
            }

            if (useFallbackPoseWhenNoPlane) {
                // Fallback para não travar o fluxo (não depende de ARRaycastManager).
                Vector3 camForward = placementCamera.transform.forward;
                Vector3 fallbackPos = placementCamera.transform.position + camForward * Mathf.Max(0.3f, fallbackDistance);
                fallbackPos += Vector3.up * ghostVerticalOffset;

                Vector3 bearing = new Vector3(camForward.x, 0f, camForward.z);
                if (bearing.sqrMagnitude < 0.0001f) bearing = placementCamera.transform.up;

                placementPose = new Pose(fallbackPos, ApplyYawOffset(Quaternion.LookRotation(bearing.normalized)));
                placementPoseIsValid = true;
                usingFallbackPose = true;

                MaybeLogDebug("fallback", extra: "noPlaneHit");
            } else {
                placementPoseIsValid = false;
                MaybeLogDebug("invalid", extra: "noPlaneHit");
            }
        }

        private Quaternion ApplyYawOffset(Quaternion baseRotation) {
            if (Mathf.Abs(yawOffsetDegrees) < 0.0001f) return baseRotation;
            return Quaternion.AngleAxis(yawOffsetDegrees, Vector3.up) * baseRotation;
        }

        private void NormalizeYawOffset() {
            // Mantém valores controlados para evitar overflow ao girar muitas vezes.
            yawOffsetDegrees = Mathf.Repeat(yawOffsetDegrees + 180f, 360f) - 180f;
        }

        // ── UI hooks (botões/slider) ─────────────────────────────────────────

        public void RotateYawLeft() {
            if (isPlaced) return;
            yawOffsetDegrees -= yawStepDegrees;
            NormalizeYawOffset();
            ApplyFrozenYawIfNeeded();
        }

        public void RotateYawRight() {
            if (isPlaced) return;
            yawOffsetDegrees += yawStepDegrees;
            NormalizeYawOffset();
            ApplyFrozenYawIfNeeded();
        }

        /// <summary>
        /// Para usar com Slider (OnValueChanged). Sugestão: slider -180..180.
        /// </summary>
        public void SetYawOffsetDegrees(float degrees) {
            if (isPlaced) return;
            yawOffsetDegrees = degrees;
            NormalizeYawOffset();
            ApplyFrozenYawIfNeeded();

            UpdateYawSliderValue();

            if (debugLogs) {
                Debug.Log($"[ARPlacementDebug] SetYawOffsetDegrees called value={degrees:F1} normalized={yawOffsetDegrees:F1}");
            }
        }

        private void UpdateYawSliderValue() {
            var slider = GetYawSliderComponent();
            if (slider == null) return;

            if (Mathf.Abs(slider.value - yawOffsetDegrees) > 0.01f) {
                slider.SetValueWithoutNotify(yawOffsetDegrees);
            }
        }

        private void CacheFrozenGhostPose() {
            if (ghostInstance == null) return;
            if (!hasGhostRootOffsetLocal) return;

            frozenGhostBaseRotation = ghostInstance.transform.rotation;
            frozenYawOffsetAtFreeze = yawOffsetDegrees;

            // Centro visual = rootPos - rot * offsetLocal
            frozenGhostCenterPos = ghostInstance.transform.position - (frozenGhostBaseRotation * ghostRootOffsetLocal);
            hasFrozenGhostPose = true;
        }

        private void ApplyFrozenYawIfNeeded() {
            if (!isGhostFrozenForPreview) return;
            if (!hasFrozenGhostPose) return;
            if (ghostInstance == null) return;
            if (!hasGhostRootOffsetLocal) return;

            float delta = yawOffsetDegrees - frozenYawOffsetAtFreeze;
            Quaternion rot = Quaternion.AngleAxis(delta, Vector3.up) * frozenGhostBaseRotation;
            Vector3 rootPos = frozenGhostCenterPos + (rot * ghostRootOffsetLocal);

            ghostInstance.transform.SetPositionAndRotation(rootPos, rot);
        }

        private void UpdateGhost() {
            if (ghostScenePrefab == null) {
                SetTapToPlaceUIVisible(true);
                if (confirmButton != null) confirmButton.SetActive(false);
                if (previewToggleButton != null) previewToggleButton.SetActive(false);
                if (yawSlider != null) yawSlider.SetActive(false);
                return;
            }

            // UX desejado: só mostra ghost/botão quando houver PLANO.
            bool canShowPlacementUI = requirePlaneForPlacement ? planePoseIsValid : placementPoseIsValid;

            if (canShowPlacementUI) {
                if (confirmButton == null && !confirmButtonWarningLogged) {
                    confirmButtonWarningLogged = true;
                    Debug.LogWarning("ARPlacementManager: ghost ativo mas 'confirmButton' está null. O botão 'Colocar aqui' não vai aparecer até você atribuir/renomear corretamente.");
                }

                // Spawn do ghost na primeira vez que encontrar superfície
                if (ghostInstance == null) {
                    ghostInstance = Instantiate(ghostScenePrefab, placementPose.position, placementPose.rotation);
                    ghostInstance.SetActive(true);
                    if (instanceScale != 1f)
                        ghostInstance.transform.localScale = Vector3.one * instanceScale;
                    PrepareGhostInstance(ghostInstance);

                    if (recenterByRenderBounds) {
                        RecenterInstanceToPose(ghostInstance, placementPose);
                    }

                    if (snapToSurface) {
                        // Assenta o ghost no Y do plano (placementPose já inclui ghostVerticalOffset).
                        LiftRootToSurfaceY(ghostInstance, placementPose.position.y);
                    }

                    // Salva offset em espaço local da pose para que ele rotacione junto com o ghost.
                    ghostRootOffsetLocal = Quaternion.Inverse(placementPose.rotation) * (ghostInstance.transform.position - placementPose.position);
                    hasGhostRootOffsetLocal = true;

                    if (confirmButton != null) confirmButton.SetActive(true);
                    if (previewToggleButton != null) {
                        previewToggleButton.SetActive(true);
                        isGhostFrozenForPreview = false;
                        UpdatePreviewToggleButtonLabel();
                    }
                    if (yawSlider != null) {
                        yawSlider.SetActive(true);
                        EnsureYawSliderSetup();
                        UpdateYawSliderValue();
                    }
                    SetTapToPlaceUIVisible(false);
                    Debug.Log($"👻 Ghost preview spawnado. scale={instanceScale} offsetLocal={ghostRootOffsetLocal}");
                }

                // Ghost segue a superfície — aplica offset local rotacionado para não "derivar" ao virar.
                var targetRot = placementPose.rotation;
                var targetPos = placementPose.position + (hasGhostRootOffsetLocal ? (targetRot * ghostRootOffsetLocal) : Vector3.zero);

                float t = ghostFollowSpeed <= 0f ? 1f : (1f - Mathf.Exp(-ghostFollowSpeed * Time.deltaTime));
                ghostInstance.transform.rotation = Quaternion.Slerp(ghostInstance.transform.rotation, targetRot, t);
                ghostInstance.transform.position = Vector3.Lerp(ghostInstance.transform.position, targetPos, t);

                if (debugLogs) {
                    Vector3 posErr = ghostInstance.transform.position - targetPos;
                    float yawGhost = ghostInstance.transform.rotation.eulerAngles.y;
                    float yawTarget = targetRot.eulerAngles.y;
                    MaybeLogDebug(
                        "ghost",
                        extra: $"posErr=({posErr.x:F3},{posErr.y:F3},{posErr.z:F3}) yawGhost={yawGhost:F1} yawTarget={yawTarget:F1}"
                    );
                }
            } else {
                // Superfície perdida — oculta ghost e botão
                if (ghostInstance != null) ghostInstance.SetActive(false);
                if (confirmButton != null) confirmButton.SetActive(false);
                if (previewToggleButton != null) previewToggleButton.SetActive(false);
                if (yawSlider != null) yawSlider.SetActive(false);
                SetTapToPlaceUIVisible(true);

                MaybeLogDebug("hidden", extra: "noPlacementUI");
            }

            // Reativa o ghost se a superfície voltou
            if (canShowPlacementUI && ghostInstance != null && !ghostInstance.activeSelf) {
                ghostInstance.SetActive(true);
                if (confirmButton != null) confirmButton.SetActive(true);
                if (previewToggleButton != null) {
                    previewToggleButton.SetActive(true);
                    UpdatePreviewToggleButtonLabel();
                }
                if (yawSlider != null) {
                    yawSlider.SetActive(true);
                    EnsureYawSliderSetup();
                    UpdateYawSliderValue();
                }
                SetTapToPlaceUIVisible(false);

                MaybeLogDebug("shown", extra: "surfaceBack");
            }
        }

        private void SetTapToPlaceUIVisible(bool visible) {
            if (tapToPlaceUI == null) return;

            // Se os botões estiverem dentro do tapToPlaceUI, não podemos desativar o container.
            // Caso contrário o botão some mesmo com SetActive(true) nele.
            bool hasChildButtons =
                (confirmButton != null && IsDescendant(confirmButton.transform, tapToPlaceUI.transform)) ||
                (previewToggleButton != null && IsDescendant(previewToggleButton.transform, tapToPlaceUI.transform)) ||
                (yawSlider != null && IsDescendant(yawSlider.transform, tapToPlaceUI.transform));

            if (visible) {
                if (!tapToPlaceUI.activeSelf) tapToPlaceUI.SetActive(true);
                SetAllTapToPlaceUITextEnabled(true);
                return;
            }

            if (!hasChildButtons) {
                if (tapToPlaceUI.activeSelf) tapToPlaceUI.SetActive(false);
                return;
            }

            // Mantém o container ativo, mas esconde só os textos.
            if (!tapToPlaceUI.activeSelf) tapToPlaceUI.SetActive(true);
            SetAllTapToPlaceUITextEnabled(false);
        }

        private void SetAllTapToPlaceUITextEnabled(bool enabled) {
            if (tapToPlaceUI == null) return;

            var tmps = tapToPlaceUI.GetComponentsInChildren<TMP_Text>(includeInactive: true);
            for (int i = 0; i < tmps.Length; i++) {
                if (tmps[i] == null) continue;
                var tr = tmps[i].transform;
                if (confirmButton != null && IsDescendant(tr, confirmButton.transform)) continue;
                if (previewToggleButton != null && IsDescendant(tr, previewToggleButton.transform)) continue;
                if (yawSlider != null && IsDescendant(tr, yawSlider.transform)) continue;
                tmps[i].enabled = enabled;
            }

            var legacy = tapToPlaceUI.GetComponentsInChildren<Text>(includeInactive: true);
            for (int i = 0; i < legacy.Length; i++) {
                if (legacy[i] == null) continue;
                var tr = legacy[i].transform;
                if (confirmButton != null && IsDescendant(tr, confirmButton.transform)) continue;
                if (previewToggleButton != null && IsDescendant(tr, previewToggleButton.transform)) continue;
                if (yawSlider != null && IsDescendant(tr, yawSlider.transform)) continue;
                legacy[i].enabled = enabled;
            }
        }

        private bool IsDescendant(Transform possibleChild, Transform possibleAncestor) {
            if (possibleChild == null || possibleAncestor == null) return false;
            Transform current = possibleChild;
            while (current != null) {
                if (current == possibleAncestor) return true;
                current = current.parent;
            }
            return false;
        }

        private void MaybeLogDebug(string phase, string extra) {
            if (!debugLogs) return;
            float now = Time.unscaledTime;
            float interval = Mathf.Max(0.05f, debugLogInterval);
            if (now < nextDebugLogTime) return;
            nextDebugLogTime = now + interval;

            string cam = placementCamera != null
                ? $"camPos=({placementCamera.transform.position.x:F3},{placementCamera.transform.position.y:F3},{placementCamera.transform.position.z:F3}) camYaw={placementCamera.transform.rotation.eulerAngles.y:F1}"
                : "cam=null";

            string pose = $"planeValid={planePoseIsValid} poseValid={placementPoseIsValid} usingFallback={usingFallbackPose} " +
                          $"screenCenter=({lastRaycastScreenCenter.x:F0},{lastRaycastScreenCenter.y:F0}) screen=({Screen.width},{Screen.height}) " +
                          $"posePos=({placementPose.position.x:F3},{placementPose.position.y:F3},{placementPose.position.z:F3}) poseYaw={placementPose.rotation.eulerAngles.y:F1} " +
                          $"offsetLocal=({ghostRootOffsetLocal.x:F3},{ghostRootOffsetLocal.y:F3},{ghostRootOffsetLocal.z:F3})";

            Debug.Log($"[ARPlacementDebug] phase={phase} {cam} {pose} {extra}");
        }

        private void MaybeLogDebug(string phase) {
            MaybeLogDebug(phase, extra: "");
        }

        /// <summary>
        /// Chamado pelo botão "Colocar aqui" na UI.
        /// </summary>
        public void ConfirmPlacement() {
            if (isPlaced) {
                if (debugLogs) Debug.Log("[ARPlacementDebug] ConfirmPlacement ignorado: já está colocado.");
                return;
            }

            if (s_globalPlacementDone) {
                if (debugLogs) Debug.Log("[ARPlacementDebug] ConfirmPlacement ignorado: placement global já concluído.");
                return;
            }

            float now = Time.unscaledTime;
            if (now - s_lastConfirmTime < k_confirmDebounceSeconds) {
                if (debugLogs) Debug.Log($"[ARPlacementDebug] ConfirmPlacement ignorado: debounce global ({now - s_lastConfirmTime:F3}s < {k_confirmDebounceSeconds:F2}s)." );
                return;
            }

            if (Time.frameCount == s_lastConfirmFrame) {
                if (debugLogs) Debug.Log("[ARPlacementDebug] ConfirmPlacement ignorado: guard global (mesmo frame).");
                return;
            }

            // Lock global atômico: evita duplicidade mesmo se existirem 2 instâncias ou 2 listeners.
            if (Interlocked.CompareExchange(ref s_globalConfirmLock, 1, 0) != 0) {
                if (debugLogs) Debug.Log("[ARPlacementDebug] ConfirmPlacement ignorado: lock global ocupado.");
                return;
            }

            // A partir daqui, precisamos garantir liberação do lock em qualquer retorno.
            // Captura refs cedo para poder reabilitar no finally, se necessário.
            var btn = confirmButton != null ? confirmButton.GetComponent<Button>() : null;
            try {
                s_lastConfirmFrame = Time.frameCount;
                s_lastConfirmTime = now;

                if (confirmInProgress) {
                    if (debugLogs) Debug.Log("[ARPlacementDebug] ConfirmPlacement ignorado: confirmação já em andamento.");
                    return;
                }

                if (!placementAllowed) {
                    Debug.LogWarning("ARPlacementManager: ConfirmPlacement chamado enquanto placementAllowed=false.");
                    return;
                }

                confirmInProgress = true;

                // Bloqueia o botão imediatamente para reduzir chance de duplo clique/evento.
                if (btn != null) btn.interactable = false;

                if (debugLogs) {
                    Debug.Log($"[ARPlacementDebug] ConfirmPlacement BEGIN instanceId={GetInstanceID()} frame={Time.frameCount} time={now:F3} activeInstances={s_activeInstanceCount}");
                }

                Pose finalPose;
                bool poseCameFromGhost = false;

                // Se o ghost estiver ativo, use a pose VISÍVEL no momento do clique.
                // Isso evita discrepância quando o ghost está suavizando (atrasado) mas o raycast já avançou.
                if (ghostInstance != null && ghostInstance.activeInHierarchy && hasGhostRootOffsetLocal) {
                    var ghostRot = ghostInstance.transform.rotation;
                    // Converte posição do ROOT do ghost para o "centro visual" (pose desejada = centro dos bounds)
                    // rootPos = centerPos + rot * offsetLocal  =>  centerPos = rootPos - rot * offsetLocal
                    var ghostCenterPos = ghostInstance.transform.position - (ghostRot * ghostRootOffsetLocal);
                    finalPose = new Pose(ghostCenterPos, ghostRot);
                    poseCameFromGhost = true;
                } else {

                    bool canPlaceNow = requirePlaneForPlacement ? planePoseIsValid : placementPoseIsValid;

                    if (canPlaceNow) {
                        finalPose = placementPose;
                    } else if (requirePlaneForPlacement && hasLastValidPlanePose) {
                        finalPose = lastValidPlanePose;
                    } else if (!requirePlaneForPlacement && hasLastValidPlacementPose) {
                        finalPose = lastValidPlacementPose;
                    } else {
                        Debug.LogWarning("ARPlacementManager: ConfirmPlacement sem pose válida (plano) e sem última pose de plano.");
                        return;
                    }
                }

                if (gameScenePrefab == null) {
                    Debug.LogWarning("ARPlacementManager: gameScenePrefab não atribuído!");
                    return;
                }

                // Remove o ghost
                // Spawna a cena real
                // Se a pose veio do ghost, instancia o ROOT no mesmo offset do ghost para garantir 1:1.
                Vector3 spawnRootPos = finalPose.position;
                Quaternion spawnRootRot = finalPose.rotation;
                if (poseCameFromGhost && hasGhostRootOffsetLocal) {
                    spawnRootPos = finalPose.position + (spawnRootRot * ghostRootOffsetLocal);
                }

                // Importante (Android): evitar um pico de memória instanciando um segundo prefab pesado.
                // Quando possível, reaproveita o ghost como instância final.
                bool canReuseGhost = reuseGhostAsPlacedScene && ghostInstance != null;
                if (canReuseGhost) {
                    spawnedScene = ghostInstance;
                    ghostInstance = null;

                    // Garante pose final do root.
                    spawnedScene.transform.SetPositionAndRotation(spawnRootPos, spawnRootRot);

                    // Restaura materiais originais (remove aparência translúcida) e remove o componente.
                    var ghostPreview = spawnedScene.GetComponent<GhostPreview>();
                    if (ghostPreview != null) {
                        ghostPreview.SetGhostEnabled(false);
                        Destroy(ghostPreview);
                    }

                    // O ghost desativa vários behaviours para virar apenas visual. Aqui reabilitamos para gameplay.
                    EnableAllBehavioursForPlacedScene(spawnedScene);

                    if (debugLogs) {
                        Debug.Log("[ARPlacementDebug] Reutilizando ghost como cena final (evita Instantiate do prefab real).");
                    }
                } else {
                    // Remove ghost antigo antes de instanciar a cena real.
                    if (ghostInstance != null) {
                        Destroy(ghostInstance);
                        ghostInstance = null;
                    }

                    spawnedScene = Instantiate(gameScenePrefab, spawnRootPos, spawnRootRot);
                    if (spawnedScene == null) {
                        Debug.LogError("ARPlacementManager: falha ao instanciar gameScenePrefab.");
                        return;
                    }
                }

                if (instanceScale != 1f)
                    spawnedScene.transform.localScale = Vector3.one * instanceScale;

            // Importante: prepare/ativa a hierarquia renderizável ANTES de recentrar,
            // senão bounds podem não existir (peças desativadas) e o prefab fica com offsets grandes.
                PreparePlacedInstance(spawnedScene);

            // Se já instanciamos usando o offset do ghost, NÃO recentra de novo
            // (isso pode reintroduzir um pequeno deslocamento por bounds diferentes entre ghost/real).
                if (recenterByRenderBounds && !poseCameFromGhost) {
                    RecenterInstanceToPose(spawnedScene, finalPose);
                }

                if (snapToSurface) {
                    // Assenta a instância na superfície. (finalPose.position já inclui ghostVerticalOffset.)
                    LiftRootToSurfaceY(spawnedScene, finalPose.position.y);
                }

                if (makePlacedRigidbodiesKinematic) {
                    MakeAllRigidbodiesKinematic(spawnedScene);
                }

                // Tenta ancorar a cena para reduzir "swim"/drift de tracking.
                TryAnchorPlacedScene(spawnedScene);

                var partsManager = spawnedScene.GetComponent<ARPartsManager>();
                if (partsManager == null) {
                    partsManager = spawnedScene.AddComponent<ARPartsManager>();
                    Debug.LogWarning("ARPlacementManager: ARPartsManager ausente no prefab; componente adicionado em runtime.");
                }
                partsManager.ShowAllParts();
                isPlaced = true;
                s_globalPlacementDone = true;
                placementAllowed = false;
                isGhostFrozenForPreview = false;

                if (tapToPlaceUI != null)  tapToPlaceUI.SetActive(false);
                if (confirmButton != null) confirmButton.SetActive(false);
                if (previewToggleButton != null) previewToggleButton.SetActive(false);
                if (yawSlider != null) yawSlider.SetActive(false);

                // Desativar detecção de novos planos para performance
                if (planeManager != null) {
                    planeManager.enabled = false;
                    SetAllPlanesActive(false);
                } else {
                    Debug.LogWarning("ARPlacementManager: planeManager não atribuído.");
                }

                onScenePlaced?.Invoke();
                Debug.Log($"✅ Cena posicionada no AR! (fallbackPose={usingFallbackPose})");
            }
            finally {
                // Se a confirmação falhou (não ficou colocado), libera para tentar de novo.
                if (!isPlaced) {
                    confirmInProgress = false;

                    // Reabilita botão se existia.
                    if (btn != null) btn.interactable = true;
                }

                Interlocked.Exchange(ref s_globalConfirmLock, 0);
            }
        }

        private void EnableAllBehavioursForPlacedScene(GameObject root) {
            if (root == null) return;

            var behaviours = root.GetComponentsInChildren<Behaviour>(includeInactive: true);
            foreach (var b in behaviours) {
                if (b == null) continue;

                // Não reabilita câmeras/canvases/listeners internos do prefab para não conflitar com AR.
                if (b is Camera || b is AudioListener || b is Canvas) {
                    b.enabled = false;
                    continue;
                }

                // GhostPreview será removido no placement.
                if (b is GhostPreview) {
                    b.enabled = false;
                    continue;
                }

                // Scripts que já causaram crash/log spam no pacote original.
                string typeName = b.GetType().Name;
                if (typeName == "SampleScene" || typeName == "CameraFeed") {
                    b.enabled = false;
                    continue;
                }

                b.enabled = true;
            }
        }

        private void MakeAllRigidbodiesKinematic(GameObject root) {
            if (root == null) return;

            var rbs = root.GetComponentsInChildren<Rigidbody>(includeInactive: true);
            int changed = 0;
            for (int i = 0; i < rbs.Length; i++) {
                var rb = rbs[i];
                if (rb == null) continue;

                if (!rb.isKinematic || rb.useGravity) {
                    changed++;
                }

                // Zera velocidades ANTES de tornar kinematic (senão Unity loga warning no Android).
                if (!rb.isKinematic) {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }

                rb.isKinematic = true;
                rb.useGravity = false;
            }

            if (debugLogs) {
                Debug.Log($"[ARPlacementDebug] rigidbodiesKinematic total={rbs.Length} changed={changed}");
            }
        }

        private void TryAnchorPlacedScene(GameObject root) {
            if (root == null) return;

            ResolveAnchorManagerIfNeeded();
            if (anchorManager == null) {
                if (debugLogs) Debug.Log("[ARPlacementDebug] anchorManager não encontrado; cena não ancorada.");
                return;
            }

            if (!hasLastPlaneHitTrackableId || planeManager == null) {
                if (debugLogs) Debug.Log("[ARPlacementDebug] sem TrackableId de plano; cena não ancorada.");
                return;
            }

            ARPlane plane = null;
            foreach (var p in planeManager.trackables) {
                if (p != null && p.trackableId == lastPlaneHitTrackableId) {
                    plane = p;
                    break;
                }
            }

            if (plane == null) {
                if (debugLogs) Debug.Log("[ARPlacementDebug] plano não encontrado pelo trackableId; cena não ancorada.");
                return;
            }

            var pose = new Pose(root.transform.position, root.transform.rotation);
            spawnedAnchor = anchorManager.AttachAnchor(plane, pose);
            if (spawnedAnchor == null) {
                Debug.LogWarning("ARPlacementManager: falha ao anexar ARAnchor ao plano; cena não ancorada.");
                return;
            }

            root.transform.SetParent(spawnedAnchor.transform, worldPositionStays: true);

            if (debugLogs) {
                Debug.Log($"[ARPlacementDebug] anchorCreated instanceId={GetInstanceID()} anchorGo='{spawnedAnchor.gameObject.name}'");
            }
        }

        private void MaybeLogPlacedState() {
            if (!debugLogs) return;
            if (spawnedScene == null) return;

            float now = Time.unscaledTime;
            float interval = Mathf.Max(0.2f, placedDebugLogInterval);
            if (now < nextPlacedDebugLogTime) return;
            nextPlacedDebugLogTime = now + interval;

            var root = spawnedScene.transform;
            string rootInfo = $"rootPos=({root.position.x:F3},{root.position.y:F3},{root.position.z:F3}) rootYaw={root.rotation.eulerAngles.y:F1} " +
                              $"rootScale=({root.lossyScale.x:F3},{root.lossyScale.y:F3},{root.lossyScale.z:F3}) parent={(root.parent != null ? root.parent.name : "null")}";

            var parts = spawnedScene.GetComponentInChildren<ARPartsManager>(includeInactive: true);
            string partsInfo = parts != null
                ? $"parts(gab={(parts.gabinete != null ? parts.gabinete.activeSelf.ToString() : "null")}, ram={(parts.ram != null ? parts.ram.activeSelf.ToString() : "null")}, mb={(parts.motherboard != null ? parts.motherboard.activeSelf.ToString() : "null")}, mon={(parts.monitor != null ? parts.monitor.activeSelf.ToString() : "null")})"
                : "parts=null";

            var ramModules = spawnedScene.GetComponentsInChildren<RAMModule>(includeInactive: true);
            var motherboardPlacers = spawnedScene.GetComponentsInChildren<MotherboardPlacer>(includeInactive: true);

            Debug.Log($"[ARPlacementDebug] placedState instanceId={GetInstanceID()} {rootInfo} {partsInfo} ramModules={ramModules.Length} motherboardPlacers={motherboardPlacers.Length}");

            if (ramModules.Length > 0) {
                var t = ramModules[0].transform;
                Debug.Log($"[ARPlacementDebug] part RAMModule name='{t.name}' world=({t.position.x:F3},{t.position.y:F3},{t.position.z:F3}) local=({t.localPosition.x:F3},{t.localPosition.y:F3},{t.localPosition.z:F3})");
            }
            if (motherboardPlacers.Length > 0) {
                var t = motherboardPlacers[0].transform;
                Debug.Log($"[ARPlacementDebug] part MotherboardPlacer name='{t.name}' world=({t.position.x:F3},{t.position.y:F3},{t.position.z:F3}) local=({t.localPosition.x:F3},{t.localPosition.y:F3},{t.localPosition.z:F3})");
            }
        }

        private void SetAllPlanesActive(bool active) {
            if (planeManager == null) return;
            foreach (var plane in planeManager.trackables) {
                plane.gameObject.SetActive(active);
            }
        }

        private void PrepareGhostInstance(GameObject root) {
            if (root == null) return;

            ActivateRenderableHierarchy(root);
            DisableProblematicBehaviours(root, keepInteractiveGameplay: false);
        }

        private void PreparePlacedInstance(GameObject root) {
            if (root == null) return;

            ActivateRenderableHierarchy(root);
            DisableProblematicBehaviours(root, keepInteractiveGameplay: true);
        }

        private void ActivateRenderableHierarchy(GameObject root) {
            if (root == null) return;
            root.SetActive(true);
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);

            foreach (var rend in renderers) {
                Transform current = rend.transform;
                while (current != null) {
                    current.gameObject.SetActive(true);
                    if (current == root.transform) break;
                    current = current.parent;
                }

                // Alguns assets vêm com Renderer desabilitado no prefab.
                if (rend != null) rend.enabled = true;
            }
        }

        private void DisableProblematicBehaviours(GameObject root, bool keepInteractiveGameplay) {
            var behaviours = root.GetComponentsInChildren<Behaviour>(includeInactive: true);

            foreach (var b in behaviours) {
                if (b == null) continue;

                // Mantém somente os scripts AR de visibilidade.
                if (b is ARPartsManager || b is GhostPreview) continue;

                // Evita câmeras/canvases/listeners internos do prefab antigo afetarem a cena AR.
                if (b is Camera || b is AudioListener || b is Canvas) {
                    b.enabled = false;
                    continue;
                }

                string typeName = b.GetType().Name;

                // Scripts que já causaram crash/log spam no pacote original.
                if (typeName == "SampleScene" || typeName == "CameraFeed") {
                    b.enabled = false;
                    continue;
                }

                // No ghost, desativa qualquer script de gameplay/OS para virar apenas visual.
                if (!keepInteractiveGameplay) {
                    b.enabled = false;
                }
            }
        }

        private void RecenterInstanceToPose(GameObject root, Pose desiredPose) {
            if (root == null) return;

            // Primeiro aplica pose desejada
            root.transform.SetPositionAndRotation(desiredPose.position, desiredPose.rotation);

            // Depois corrige offset usando centro dos renderers visuais
            if (!TryGetCombinedRendererBounds(root, out Bounds bounds)) {
                return;
            }

            Vector3 delta = desiredPose.position - bounds.center;
            root.transform.position += delta;
        }

        private void LiftRootToSurfaceY(GameObject root, float surfaceY) {
            if (root == null) return;
            if (!TryGetCombinedRendererBounds(root, out Bounds bounds)) return;

            // Se o objeto está "entrando" na superfície, levanta até o minY encostar no surfaceY.
            float delta = surfaceY - bounds.min.y;
            if (delta > 0f) {
                root.transform.position += Vector3.up * (delta + Mathf.Max(0f, surfaceSnapPadding));
            }
        }

        private bool TryGetCombinedRendererBounds(GameObject root, out Bounds combined) {
            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            bool hasBounds = false;
            combined = default;

            foreach (var rend in renderers) {
                if (rend == null || !rend.enabled) continue;
                if (!rend.gameObject.activeInHierarchy) continue;
                if (!hasBounds) {
                    combined = rend.bounds;
                    hasBounds = true;
                } else {
                    combined.Encapsulate(rend.bounds);
                }
            }

            return hasBounds;
        }

        private void EnsureVisibleMarkerIfNeeded(GameObject root, bool isGhost) {
            if (root == null) return;

            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++) {
                var r = renderers[i];
                if (r == null) continue;
                if (r.enabled && r.gameObject.activeInHierarchy) return;
            }

            // Sem renderers ativos: cria marcador simples para visualização
            var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            marker.name = isGhost ? "GhostFallbackMarker" : "PlacedFallbackMarker";
            marker.transform.SetParent(root.transform, false);
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localRotation = Quaternion.identity;
            marker.transform.localScale = new Vector3(0.25f, 0.25f, 0.25f);

            var col = marker.GetComponent<Collider>();
            if (col != null) col.enabled = false;

            var renderer = marker.GetComponent<Renderer>();
            if (renderer != null) {
                var mat = new Material(Shader.Find("Standard"));
                if (isGhost) {
                    mat.SetFloat("_Mode", 3f);
                    mat.SetInt("_SrcBlend", 5);
                    mat.SetInt("_DstBlend", 10);
                    mat.SetInt("_ZWrite", 0);
                    mat.EnableKeyword("_ALPHABLEND_ON");
                    mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    mat.color = new Color(0.2f, 0.9f, 1f, 0.35f);
                } else {
                    mat.color = new Color(0.1f, 1f, 0.1f, 1f);
                }
                renderer.material = mat;
            }

            Debug.LogWarning($"ARPlacementManager: nenhum renderer ativo no prefab instanciado. Marcador '{marker.name}' criado.");
        }

        public void ResetPlacement() {
            if (spawnedScene != null) {
                Destroy(spawnedScene);
                spawnedScene = null;
            }

            if (spawnedAnchor != null) {
                Destroy(spawnedAnchor.gameObject);
                spawnedAnchor = null;
            }

            if (ghostInstance != null) {
                Destroy(ghostInstance);
                ghostInstance = null;
            }

            isPlaced = false;
            placementPoseIsValid = false;
            planePoseIsValid = false;
            hasGhostRootOffsetLocal = false;

            isGhostFrozenForPreview = false;
            hasFrozenGhostPose = false;

            yawOffsetDegrees = 0f;

            confirmInProgress = false;
            s_globalPlacementDone = false;
            s_lastConfirmFrame = -1;
            s_lastConfirmTime = -999f;
            Interlocked.Exchange(ref s_globalConfirmLock, 0);
            if (planeManager != null)
                planeManager.enabled = true;

            if (tapToPlaceUI != null)  tapToPlaceUI.SetActive(true);
            if (confirmButton != null) confirmButton.SetActive(false);
            if (previewToggleButton != null) previewToggleButton.SetActive(false);
            if (yawSlider != null) yawSlider.SetActive(false);

            UpdatePreviewToggleButtonLabel();

            // O ARGameManager decide quando o placement deve voltar a ficar ativo (estado Placing).
            placementAllowed = false;
        }
    }
}
