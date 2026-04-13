using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace SavitGame.AR {
    /// <summary>
    /// Controla a visualização dos planos detectados (chão, mesa, etc.)
    /// </summary>
    [RequireComponent(typeof(ARPlaneManager))]
    public class ARPlaneVisualizer : MonoBehaviour {
        [Header("Configurações")]
        [Tooltip("Mostrar planos detectados (desmarque para deixar invisível)")]
        public bool showPlanes = true;

        [Tooltip("Material dos planos detectados (opcional)")]
        public Material planeMaterial;

        private ARPlaneManager planeManager;
        private Material runtimePlaneMaterial;

        private void Awake() {
            planeManager = GetComponent<ARPlaneManager>();

            // Se nenhum material foi atribuído no Inspector, cria um semitransparente em runtime
            // para evitar o rosa/magenta do material padrão ausente.
            if (planeMaterial == null) {
                Shader shader = Shader.Find("Standard");
                if (shader != null) {
                    runtimePlaneMaterial = new Material(shader) { name = "ARPlane_Runtime" };
                    runtimePlaneMaterial.SetFloat("_Mode", 3f);
                    runtimePlaneMaterial.SetInt("_SrcBlend", 5);
                    runtimePlaneMaterial.SetInt("_DstBlend", 10);
                    runtimePlaneMaterial.SetInt("_ZWrite", 0);
                    runtimePlaneMaterial.EnableKeyword("_ALPHABLEND_ON");
                    runtimePlaneMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                    runtimePlaneMaterial.color = new Color(0.2f, 0.8f, 1f, 0.15f);
                }
            }
        }

        private void OnEnable() {
            planeManager.trackablesChanged.AddListener(OnPlanesChanged);

            // Aplica o estado atual também aos planos que já existiam antes
            // do listener ser registrado (evita aparecer um mesh/material errado).
            foreach (var plane in planeManager.trackables) {
                ConfigurePlane(plane);
            }
        }

        private void OnDisable() {
            planeManager.trackablesChanged.RemoveListener(OnPlanesChanged);
        }

        private void OnPlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args) {
            foreach (var plane in args.added) {
                ConfigurePlane(plane);
            }

            foreach (var plane in args.updated) {
                ConfigurePlane(plane);
            }
        }

        private void ConfigurePlane(ARPlane plane) {
            var renderer = plane.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            renderer.enabled = showPlanes;

            if (planeMaterial != null) {
                renderer.material = planeMaterial;
            } else if (runtimePlaneMaterial != null) {
                renderer.material = runtimePlaneMaterial;
            }
        }

        public void SetPlanesVisible(bool visible) {
            showPlanes = visible;
            foreach (var plane in planeManager.trackables) {
                var renderer = plane.GetComponent<MeshRenderer>();
                if (renderer != null)
                    renderer.enabled = visible;
            }
        }
    }
}
