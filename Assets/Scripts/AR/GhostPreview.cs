using UnityEngine;
using System.Collections.Generic;

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

        private readonly Dictionary<Renderer, Material[]> originalSharedMaterials = new Dictionary<Renderer, Material[]>();
        private Material ghostMat;
        private bool ghostEnabled = true;

        private void Awake() {
            // Ativa todos os filhos antes de aplicar materiais
            // (necessário pois ARPartsManager pode ter desativado os objetos)
            ActivateAllChildren();

            CacheOriginalMaterials();
            SetGhostEnabled(true);
        }

        private void ActivateAllChildren() {
            // Ativa todos os GameObjects filhos (inclusive os que ARPartsManager escondeu)
            foreach (Transform child in GetComponentsInChildren<Transform>(includeInactive: true)) {
                if (child != this.transform)
                    child.gameObject.SetActive(true);
            }
        }

        public void SetGhostEnabled(bool enabled) {
            ghostEnabled = enabled;

            if (enabled) {
                ApplyGhostMaterials();
            } else {
                RestoreOriginalMaterials();
            }
        }

        public bool IsGhostEnabled() => ghostEnabled;

        private void CacheOriginalMaterials() {
            originalSharedMaterials.Clear();

            // Cacheia todos os Renderers relevantes (mesh + skinned) do prefab.
            var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (var r in renderers) {
                if (r == null) continue;
                if (r is MeshRenderer || r is SkinnedMeshRenderer) {
                    originalSharedMaterials[r] = r.sharedMaterials;
                }
            }
        }

        private void ApplyGhostMaterials() {
            if (ghostMat == null) ghostMat = CreateGhostMaterial();

            int applied = 0;
            foreach (var kv in originalSharedMaterials) {
                var rend = kv.Key;
                if (rend == null) continue;

                var original = kv.Value;
                if (original == null) continue;

                // Substitui todos os slots de material pelo ghost.
                var mats = new Material[original.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = ghostMat;
                rend.sharedMaterials = mats;
                applied++;
            }

            Debug.Log($"[GhostPreview] Material ghost aplicado em {applied} renderers.");
        }

        private void RestoreOriginalMaterials() {
            int restored = 0;
            foreach (var kv in originalSharedMaterials) {
                var rend = kv.Key;
                if (rend == null) continue;

                rend.sharedMaterials = kv.Value;
                restored++;
            }

            Debug.Log($"[GhostPreview] Materiais originais restaurados em {restored} renderers.");
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
