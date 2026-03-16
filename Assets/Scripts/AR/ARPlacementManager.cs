using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.InputSystem;
using Unity.XR.CoreUtils;
using UnityEngine.UI;

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

        [Header("UI")]
        public GameObject tapToPlaceUI;      // texto "Aponte para uma superfície"
        public GameObject confirmButton;     // botão "Colocar aqui" — aparece com o ghost

        [Header("Ajustes do Ghost")]
        [Tooltip("Velocidade de suavização do ghost ao acompanhar o pose")]
        public float ghostFollowSpeed = 12f;

        [Tooltip("Offset vertical para evitar z-fighting com o plano")]
        public float ghostVerticalOffset = 0.005f;

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

        [Header("Eventos")]
        [Tooltip("Chamado quando o objeto é posicionado. Conecte ao ARGameManager.OnScenePlaced()")]
        public UnityEvent onScenePlaced;

        private GameObject spawnedScene;
        private GameObject ghostInstance;
        private bool isPlaced = false;
        private bool placementAllowed = false;
        private bool cameraWarningLogged = false;
        private Pose placementPose;
        private Pose lastValidPlacementPose;
        private bool hasLastValidPlacementPose = false;
        private bool placementPoseIsValid = false;
        private Vector3 ghostRootOffset;
        private bool usingFallbackPose = false;
        private bool planePoseIsValid = false;
        private Pose lastValidPlanePose;
        private bool hasLastValidPlanePose = false;
        private static readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();

        private void Awake() {
            ResolveManagersIfNeeded();
            ResolvePlacementCamera();
            ResolveUIRefsIfNeeded();
        }

        private void Start() {
            ResolveManagersIfNeeded();
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

            // Por padrão, o placement só fica ativo quando o ARGameManager entrar no estado Placing.
            placementAllowed = false;
        }

        private void ResolveUIRefsIfNeeded() {
            // Se não estiver ligado no Inspector, tenta nomes comuns da cena.
            if (confirmButton == null) {
                var byName = GameObject.Find("PlaceHereButton");
                if (byName != null) confirmButton = byName;
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
                if (ghostInstance != null) ghostInstance.SetActive(false);
                if (confirmButton != null) confirmButton.SetActive(false);
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
            if (isPlaced) return;
            if (!placementAllowed) return;

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
                var screenCenter3 = placementCamera.ViewportToScreenPoint(new Vector3(0.5f, 0.5f, 0f));
                var screenCenter = new Vector2(screenCenter3.x, screenCenter3.y);
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
                lastValidPlacementPose = placementPose;
                usingFallbackPose = false;
                return;
            }

            if (useFallbackPoseWhenNoPlane) {
                // Fallback para não travar o fluxo (não depende de ARRaycastManager).
                Vector3 camForward = placementCamera.transform.forward;
                Vector3 fallbackPos = placementCamera.transform.position + camForward * Mathf.Max(0.3f, fallbackDistance);
                fallbackPos += Vector3.up * ghostVerticalOffset;

                Vector3 bearing = new Vector3(camForward.x, 0f, camForward.z);
                if (bearing.sqrMagnitude < 0.0001f) bearing = placementCamera.transform.up;

                placementPose = new Pose(fallbackPos, Quaternion.LookRotation(bearing.normalized));
                placementPoseIsValid = true;
                usingFallbackPose = true;
            } else {
                placementPoseIsValid = false;
            }
        }

        private void UpdateGhost() {
            if (ghostScenePrefab == null) {
                if (tapToPlaceUI != null) tapToPlaceUI.SetActive(true);
                if (confirmButton != null) confirmButton.SetActive(false);
                return;
            }

            // UX desejado: só mostra ghost/botão quando houver PLANO.
            bool canShowPlacementUI = requirePlaneForPlacement ? planePoseIsValid : placementPoseIsValid;

            if (canShowPlacementUI) {
                // Spawn do ghost na primeira vez que encontrar superfície
                if (ghostInstance == null) {
                    ghostInstance = Instantiate(ghostScenePrefab, placementPose.position, placementPose.rotation);
                    ghostInstance.SetActive(true);
                    if (instanceScale != 1f)
                        ghostInstance.transform.localScale = Vector3.one * instanceScale;
                    PrepareGhostInstance(ghostInstance);
                    if (recenterByRenderBounds) {
                        RecenterInstanceToPose(ghostInstance, placementPose);
                        ghostRootOffset = ghostInstance.transform.position - placementPose.position;
                    } else {
                        ghostRootOffset = Vector3.zero;
                    }
                    if (confirmButton != null) confirmButton.SetActive(true);
                    if (tapToPlaceUI != null)  tapToPlaceUI.SetActive(false);
                    Debug.Log($"👻 Ghost preview spawnado. scale={instanceScale} offset={ghostRootOffset}");
                }

                // Ghost segue a superfície — aplica o mesmo offset de recenter calculado no spawn
                ghostInstance.transform.rotation = placementPose.rotation;
                ghostInstance.transform.position = placementPose.position + ghostRootOffset;
            } else {
                // Superfície perdida — oculta ghost e botão
                if (ghostInstance != null) ghostInstance.SetActive(false);
                if (confirmButton != null) confirmButton.SetActive(false);
                if (tapToPlaceUI != null)  tapToPlaceUI.SetActive(true);
            }

            // Reativa o ghost se a superfície voltou
            if (canShowPlacementUI && ghostInstance != null && !ghostInstance.activeSelf) {
                ghostInstance.SetActive(true);
                if (confirmButton != null) confirmButton.SetActive(true);
                if (tapToPlaceUI != null)  tapToPlaceUI.SetActive(false);
            }
        }

        /// <summary>
        /// Chamado pelo botão "Colocar aqui" na UI.
        /// </summary>
        public void ConfirmPlacement() {
            if (!placementAllowed) {
                Debug.LogWarning("ARPlacementManager: ConfirmPlacement chamado enquanto placementAllowed=false.");
                return;
            }

            Pose finalPose;

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

            if (gameScenePrefab == null) {
                Debug.LogWarning("ARPlacementManager: gameScenePrefab não atribuído!");
                return;
            }

            // Remove o ghost
            if (ghostInstance != null) {
                Destroy(ghostInstance);
                ghostInstance = null;
            }

            // Spawna a cena real
            spawnedScene = Instantiate(gameScenePrefab, finalPose.position, finalPose.rotation);
            if (spawnedScene == null) {
                Debug.LogError("ARPlacementManager: falha ao instanciar gameScenePrefab.");
                return;
            }

            if (instanceScale != 1f)
                spawnedScene.transform.localScale = Vector3.one * instanceScale;

            // Importante: prepare/ativa a hierarquia renderizável ANTES de recentrar,
            // senão bounds podem não existir (peças desativadas) e o prefab fica com offsets grandes.
            PreparePlacedInstance(spawnedScene);

            if (recenterByRenderBounds) {
                RecenterInstanceToPose(spawnedScene, finalPose);
            }

            var partsManager = spawnedScene.GetComponent<ARPartsManager>();
            if (partsManager == null) {
                partsManager = spawnedScene.AddComponent<ARPartsManager>();
                Debug.LogWarning("ARPlacementManager: ARPartsManager ausente no prefab; componente adicionado em runtime.");
            }
            partsManager.ShowAllParts();
            isPlaced = true;
            placementAllowed = false;

            if (tapToPlaceUI != null)  tapToPlaceUI.SetActive(false);
            if (confirmButton != null) confirmButton.SetActive(false);

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

            if (ghostInstance != null) {
                Destroy(ghostInstance);
                ghostInstance = null;
            }

            isPlaced = false;
            placementPoseIsValid = false;
            planePoseIsValid = false;
            if (planeManager != null)
                planeManager.enabled = true;

            if (tapToPlaceUI != null)  tapToPlaceUI.SetActive(true);
            if (confirmButton != null) confirmButton.SetActive(false);

            // O ARGameManager decide quando o placement deve voltar a ficar ativo (estado Placing).
            placementAllowed = false;
        }
    }
}
