using UnityEngine;

namespace SavitGame.AR {
    /// <summary>
    /// Publishes a person segmentation mask as global shader parameters for URP occlusion.
    ///
    /// Expected mask format:
    /// - single channel (R8) preferred, but any texture with a meaningful red channel works
    /// - 0 = background, 1 = person (or invert=true)
    ///
    /// Alignment controls (flip/rotate) are applied in the occlusion shader when sampling.
    /// </summary>
    public sealed class PersonOcclusionMaskSource : MonoBehaviour {
        public enum MaskRotation {
            Deg0 = 0,
            Deg90 = 1,
            Deg180 = 2,
            Deg270 = 3,
        }

        [Header("Mask Input")]
        [Tooltip("Segmentation mask texture (0=background, 1=person). Typically output of ML.")]
        public Texture maskTexture;

        [Range(0f, 1f)]
        public float threshold = 0.5f;

        public bool invert = false;

        [Tooltip("Flip Y when the mask arrives upside-down compared to the camera feed.")]
        public bool flipY = true;

        [Tooltip("Flip X when the mask is mirrored compared to the camera feed.")]
        public bool flipX = false;

        [Tooltip("Rotate the mask sampling to match the displayed AR background (0/90/180/270).")]
        public MaskRotation rotation = MaskRotation.Deg0;

        [Header("Alignment (AR Background)")]
        [Tooltip("If enabled, applies AR camera displayMatrix-style UV transform before rotate/flip when sampling the mask. This corrects crop/scale/offset differences.")]
        public bool useArDisplayUvTransform = false;

        [Tooltip("Packed as (m00, m10, m20, unused) so shader can compute x' = u*m00 + v*m10 + m20 (ARFoundation row-vector * matrix convention).")]
        public Vector4 arDisplayUvTransform0 = new Vector4(1, 0, 0, 0);

        [Tooltip("Packed as (m01, m11, m21, unused) so shader can compute y' = u*m01 + v*m11 + m21 (ARFoundation row-vector * matrix convention).")]
        public Vector4 arDisplayUvTransform1 = new Vector4(0, 1, 0, 0);

        [Header("Runtime")]
        public bool enableOcclusion = true;

        private static readonly int PersonMaskTexId = Shader.PropertyToID("_Savit_PersonMaskTex");
        private static readonly int PersonMaskThresholdId = Shader.PropertyToID("_Savit_PersonMaskThreshold");
        private static readonly int PersonMaskInvertId = Shader.PropertyToID("_Savit_PersonMaskInvert");
        private static readonly int PersonMaskFlipYId = Shader.PropertyToID("_Savit_PersonMaskFlipY");
        private static readonly int PersonMaskFlipXId = Shader.PropertyToID("_Savit_PersonMaskFlipX");
        private static readonly int PersonMaskRotateId = Shader.PropertyToID("_Savit_PersonMaskRotate");
        private static readonly int PersonMaskUseUvTransformId = Shader.PropertyToID("_Savit_PersonMaskUseUvTransform");
        private static readonly int PersonMaskUvT0Id = Shader.PropertyToID("_Savit_PersonMaskUvT0");
        private static readonly int PersonMaskUvT1Id = Shader.PropertyToID("_Savit_PersonMaskUvT1");

        private void OnEnable() {
            ApplyGlobals();
        }

        private void OnDisable() {
            // Clear so we don't accidentally occlude when switching scenes.
            Shader.SetGlobalTexture(PersonMaskTexId, null);
        }

        private void Update() {
            ApplyGlobals();
        }

        private void ApplyGlobals() {
            if (!enableOcclusion || maskTexture == null) {
                Shader.SetGlobalTexture(PersonMaskTexId, null);
                return;
            }

            Shader.SetGlobalTexture(PersonMaskTexId, maskTexture);
            Shader.SetGlobalFloat(PersonMaskThresholdId, threshold);
            Shader.SetGlobalFloat(PersonMaskInvertId, invert ? 1f : 0f);
            Shader.SetGlobalFloat(PersonMaskFlipYId, flipY ? 1f : 0f);
            Shader.SetGlobalFloat(PersonMaskFlipXId, flipX ? 1f : 0f);
            Shader.SetGlobalFloat(PersonMaskRotateId, (float)rotation);

            Shader.SetGlobalFloat(PersonMaskUseUvTransformId, useArDisplayUvTransform ? 1f : 0f);
            Shader.SetGlobalVector(PersonMaskUvT0Id, arDisplayUvTransform0);
            Shader.SetGlobalVector(PersonMaskUvT1Id, arDisplayUvTransform1);
        }
    }
}
