using UnityEngine;

namespace SavitGame.AR {
    /// <summary>
    /// Aplica materiais translúcidos em TODOS os MeshRenderers do objeto e filhos.
    /// Funciona em qualquer modelo, inclusive FBX com materiais embedded (somente leitura).
    /// 
    /// Uso: adicione ao GameScenePreviewPrefab (duplicata do GameScenePrefab).
    /// </summary>
    public class GhostPreview : MonoBehaviour {
        [Header("Aparência")]
        [Tooltip("Cor do ghost — o alpha controla a transparência (0.4 = 40%)")]
        public Color ghostColor = new Color(0.4f, 0.7f, 1f, 0.4f);

        private void Awake() {
            // Ativa todos os filhos antes de aplicar materiais
            // (necessário pois ARPartsManager pode ter desativado os objetos)
            ActivateAllChildren();
            ApplyGhostMaterials();
        }

        private void ActivateAllChildren() {
            // Ativa todos os GameObjects filhos (inclusive os que ARPartsManager escondeu)
            foreach (Transform child in GetComponentsInChildren<Transform>(includeInactive: true)) {
                if (child != this.transform)
                    child.gameObject.SetActive(true);
            }
        }

        private void ApplyGhostMaterials() {
            Material ghostMat = CreateGhostMaterial();

            // Pega todos os MeshRenderers (agora todos já estão ativos)
            var renderers = GetComponentsInChildren<MeshRenderer>(includeInactive: false);
            foreach (var rend in renderers) {
                // Substitui todos os slots de material pelo ghost
                var mats = new Material[rend.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++)
                    mats[i] = ghostMat;
                rend.materials = mats;
            }

            Debug.Log($"[GhostPreview] Material ghost aplicado em {renderers.Length} renderers.");
        }

        private Material CreateGhostMaterial() {
            // Tenta URP primeiro; se não encontrar (projeto BRP), usa Standard
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            bool isURP = shader != null;
            if (!isURP) shader = Shader.Find("Standard");

            var mat = new Material(shader) { name = "Ghost_Runtime" };

            if (isURP) {
                // Modo transparente no URP
                mat.SetFloat("_Surface", 1f);      // Transparent
                mat.SetFloat("_Blend", 0f);        // Alpha
                mat.SetFloat("_SrcBlend", 5f);     // SrcAlpha
                mat.SetFloat("_DstBlend", 10f);    // OneMinusSrcAlpha
                mat.SetFloat("_ZWrite", 0f);
                mat.SetFloat("_AlphaClip", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            } else {
                // Standard shader (fallback BRP)
                mat.SetFloat("_Mode", 3f);         // Transparent
                mat.SetInt("_SrcBlend", 5);
                mat.SetInt("_DstBlend", 10);
                mat.SetInt("_ZWrite", 0);
                mat.DisableKeyword("_ALPHATEST_ON");
                mat.EnableKeyword("_ALPHABLEND_ON");
                mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }

            mat.color = ghostColor;
            return mat;
        }
    }
}
