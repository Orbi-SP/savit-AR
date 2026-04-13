using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace SavitGame.Rendering {
    /// <summary>
    /// Writes to the camera depth buffer wherever a global person mask texture indicates "foreground".
    ///
    /// This is designed to be fed by an ML segmentation mask (person/body). It intentionally writes
    /// an "always in front" depth value for masked pixels, so virtual content behind the person is occluded.
    ///
    /// Required globals (set by PersonOcclusionMaskSource):
    /// - _Savit_PersonMaskTex (Texture)
    /// - _Savit_PersonMaskThreshold (float)
    /// - _Savit_PersonMaskInvert (float 0/1)
    /// - _Savit_PersonMaskFlipY (float 0/1)
    /// - _Savit_PersonMaskFlipX (float 0/1)
    /// - _Savit_PersonMaskRotate (float 0..3 quarter-turns clockwise)
    /// - _Savit_PersonMaskUseUvTransform (float 0/1)
    /// - _Savit_PersonMaskUvT0 (Vector4; packed as (m00, m10, m20, unused))
    /// - _Savit_PersonMaskUvT1 (Vector4; packed as (m01, m11, m21, unused))
    /// </summary>
    public sealed class PersonOcclusionRendererFeature : ScriptableRendererFeature {
        [System.Serializable]
        public sealed class Settings {
            [Tooltip("Material using the Savit/PersonOcclusionDepthOnly shader.")]
            public Material material;

            [Tooltip("When to write depth. BeforeRenderingOpaques is usually correct.")]
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;

            [Tooltip("Skip in SceneView/GameView when not playing.")]
            public bool onlyWhenPlaying = true;

            [Header("Debug")]
            [Tooltip("Logs (throttled) when the pass runs and whether depth target is valid.")]
            public bool debugPassLogging = false;

            [Range(0.2f, 5f)]
            public float debugLogIntervalSeconds = 1.0f;

            [Tooltip("Draw the person mask as a red overlay for debugging. Requires shader pass 1.")]
            public bool debugDrawMaskOverlay = false;

            [Tooltip("Optional override material for the debug overlay. If set, pass index 0 will be used.")]
            public Material debugOverlayMaterial;

            [Tooltip("When to draw the debug overlay. AfterRendering is usually easiest to see.")]
            public RenderPassEvent debugOverlayPassEvent = RenderPassEvent.AfterRendering;
        }

        [SerializeField] private Settings settings = new Settings();

        private sealed class Pass : ScriptableRenderPass {
            private readonly ProfilingSampler sampler = new ProfilingSampler("Savit Person Occlusion Mask");
            private static Mesh s_FullscreenTriangleMesh;

            private Material material;
            private static readonly int PersonMaskTexId = Shader.PropertyToID("_Savit_PersonMaskTex");
            private static readonly int PersonMaskThresholdId = Shader.PropertyToID("_Savit_PersonMaskThreshold");
            private static readonly int PersonMaskInvertId = Shader.PropertyToID("_Savit_PersonMaskInvert");
            private static readonly int PersonMaskFlipYId = Shader.PropertyToID("_Savit_PersonMaskFlipY");
            private static readonly int PersonMaskFlipXId = Shader.PropertyToID("_Savit_PersonMaskFlipX");
            private static readonly int PersonMaskRotateId = Shader.PropertyToID("_Savit_PersonMaskRotate");
            private static readonly int PersonMaskUseUvTransformId = Shader.PropertyToID("_Savit_PersonMaskUseUvTransform");
            private static readonly int PersonMaskUvT0Id = Shader.PropertyToID("_Savit_PersonMaskUvT0");
            private static readonly int PersonMaskUvT1Id = Shader.PropertyToID("_Savit_PersonMaskUvT1");

            private int materialPassIndex;
            private bool bindDepthTarget;

            private bool debugLogging;
            private float debugLogIntervalSeconds;
            private double nextDebugLogTime;
            private double nextExecuteDebugLogTime;
            private double nextMaterialDebugLogTime;
            private bool loggedDeviceInfo;

            private static Mesh GetFullscreenTriangleMesh() {
                if (s_FullscreenTriangleMesh != null)
                    return s_FullscreenTriangleMesh;

                var mesh = new Mesh {
                    name = "Savit_FullscreenTriangle",
                    hideFlags = HideFlags.HideAndDontSave
                };

                // Classic fullscreen triangle in clip space.
                // Positions are already in clip space; we render with identity view/projection.
                mesh.SetVertices(new System.Collections.Generic.List<Vector3> {
                    new Vector3(-1f, -1f, 0f),
                    new Vector3(-1f,  3f, 0f),
                    new Vector3( 3f, -1f, 0f),
                });

                mesh.SetUVs(0, new System.Collections.Generic.List<Vector2> {
                    new Vector2(0f, 0f),
                    new Vector2(0f, 2f),
                    new Vector2(2f, 0f),
                });

                mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false);
                mesh.UploadMeshData(true);

                s_FullscreenTriangleMesh = mesh;
                return s_FullscreenTriangleMesh;
            }

            private void SyncMaterialFromGlobals(Texture mask) {
                if (material == null) return;

                // Always bind a texture (even a fallback) so the shader never samples "null".
                // This also lets the debug overlay run even while the ML pipeline hasn't produced a mask yet.
                material.SetTexture(PersonMaskTexId, mask);

                // Mirror the same globals onto material-local properties so RenderGraph passes
                // do not depend on global state propagation timing.
                material.SetFloat(PersonMaskThresholdId, Shader.GetGlobalFloat(PersonMaskThresholdId));
                material.SetFloat(PersonMaskInvertId, Shader.GetGlobalFloat(PersonMaskInvertId));
                material.SetFloat(PersonMaskFlipYId, Shader.GetGlobalFloat(PersonMaskFlipYId));
                material.SetFloat(PersonMaskFlipXId, Shader.GetGlobalFloat(PersonMaskFlipXId));
                material.SetFloat(PersonMaskRotateId, Shader.GetGlobalFloat(PersonMaskRotateId));

                material.SetFloat(PersonMaskUseUvTransformId, Shader.GetGlobalFloat(PersonMaskUseUvTransformId));
                material.SetVector(PersonMaskUvT0Id, Shader.GetGlobalVector(PersonMaskUvT0Id));
                material.SetVector(PersonMaskUvT1Id, Shader.GetGlobalVector(PersonMaskUvT1Id));
            }

            private void MaybeLogMaterialInfo(string prefix) {
                if (!debugLogging) return;

                if (!loggedDeviceInfo) {
                    loggedDeviceInfo = true;
                    Debug.Log($"[PersonOcclusionPass] DeviceInfo: gfxType={SystemInfo.graphicsDeviceType} gfxName='{SystemInfo.graphicsDeviceName}' gfxVer='{SystemInfo.graphicsDeviceVersion}'");
                }

                var now = Time.unscaledTimeAsDouble;
                if (now < nextMaterialDebugLogTime) return;
                nextMaterialDebugLogTime = now + Mathf.Max(1.0f, debugLogIntervalSeconds);

                if (material == null) {
                    Debug.Log($"{prefix} material=null");
                    return;
                }

                var shader = material.shader;
                if (shader == null) {
                    Debug.Log($"{prefix} shader=null");
                    return;
                }

                Debug.Log($"{prefix} shader='{shader.name}' supported={shader.isSupported} passCount={material.passCount} thisPass={materialPassIndex}");
            }

            public void Setup(Settings settings, Material material, int materialPassIndex, bool bindDepthTarget) {
                this.material = material;
                this.materialPassIndex = Mathf.Max(0, materialPassIndex);
                this.bindDepthTarget = bindDepthTarget;
                debugLogging = settings != null && settings.debugPassLogging;
                debugLogIntervalSeconds = settings != null ? Mathf.Clamp(settings.debugLogIntervalSeconds, 0.2f, 5f) : 1.0f;
            }

            private void MaybeLog(string message) {
                if (!debugLogging) return;
                var now = Time.unscaledTimeAsDouble;
                if (now < nextDebugLogTime) return;
                nextDebugLogTime = now + debugLogIntervalSeconds;
                Debug.Log(message);
            }

            private void MaybeLogExecute(string message) {
                if (!debugLogging) return;
                var now = Time.unscaledTimeAsDouble;
                if (now < nextExecuteDebugLogTime) return;
                nextExecuteDebugLogTime = now + debugLogIntervalSeconds;
                Debug.Log(message);
            }

            [System.Obsolete("Compatibility mode path (URP RenderGraph disabled).")]
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData) {
                if (material == null)
                    return;

                var mask = Shader.GetGlobalTexture(PersonMaskTexId);
                if (mask == null) {
                    // Depth pass can't do anything without a mask.
                    if (bindDepthTarget)
                        return;

                    // Debug overlay can still run as a pure fullscreen validation step.
                    mask = Texture2D.whiteTexture;
                }

                SyncMaterialFromGlobals(mask);
                MaybeLogMaterialInfo("[PersonOcclusionPass] MatInfo (compat):");

                #pragma warning disable CS0618 // URP compatibility mode API (RenderGraph disabled)
                var renderer = renderingData.cameraData.renderer;
                var colorTarget = renderer.cameraColorTargetHandle;
                var depthTarget = renderer.cameraDepthTargetHandle;
                #pragma warning restore CS0618

                if (colorTarget == null || (bindDepthTarget && depthTarget == null)) {
                    MaybeLog("[PersonOcclusionPass] Skipped: target handle is null.");
                    return;
                }

                #pragma warning disable CS0618 // URP compatibility mode API (RenderGraph disabled)
                if (bindDepthTarget) {
                    ConfigureTarget(colorTarget, depthTarget);
                } else {
                    ConfigureTarget(colorTarget);
                }
                ConfigureClear(ClearFlag.None, Color.clear);
                #pragma warning restore CS0618

                MaybeLog($"[PersonOcclusionPass] Configured. cam='{renderingData.cameraData.camera?.name}' mask={mask.width}x{mask.height} evt={(int)renderPassEvent}");
            }

            private class PassData {
                internal Material material;
                internal int materialPassIndex;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
                if (material == null)
                    return;

                var mask = Shader.GetGlobalTexture(PersonMaskTexId);
                if (mask == null) {
                    if (bindDepthTarget) {
                        MaybeLog("[PersonOcclusionPass] RG skipped: global mask is null.");
                        return;
                    }

                    // Debug overlay can still run to validate that the pass affects the final frame.
                    mask = Texture2D.whiteTexture;
                    MaybeLog("[PersonOcclusionPass] RG debug overlay: global mask is null, using fallback texture.");
                }

                SyncMaterialFromGlobals(mask);
                MaybeLogMaterialInfo("[PersonOcclusionPass] MatInfo (RG):");

                UniversalResourceData resourcesData = frameData.Get<UniversalResourceData>();
                if (bindDepthTarget && !resourcesData.activeDepthTexture.IsValid()) {
                    MaybeLog("[PersonOcclusionPass] RG skipped: activeDepthTexture is invalid.");
                    return;
                }

                if (!resourcesData.activeColorTexture.IsValid()) {
                    MaybeLog("[PersonOcclusionPass] RG skipped: activeColorTexture is invalid.");
                    return;
                }

                MaybeLog($"[PersonOcclusionPass] RG record. cam='{frameData.Get<UniversalCameraData>().camera?.name}' mask={mask.width}x{mask.height} pass={materialPassIndex} bindDepth={bindDepthTarget} evt={(int)renderPassEvent}");
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Savit Person Occlusion Mask", out var passData, sampler)) {
                    passData.material = material;
                    passData.materialPassIndex = materialPassIndex;

                    // Bind active color to ensure the raster pass is well-formed on all platforms.
                    // (Some drivers/pipelines don't like depth-only raster passes.)
                    builder.SetRenderAttachment(resourcesData.activeColorTexture, 0, AccessFlags.ReadWrite);

                    if (bindDepthTarget) {
                        builder.SetRenderAttachmentDepth(resourcesData.activeDepthTexture, AccessFlags.Write);
                    }

                    // We rely on globally-bound textures/params coming from PersonOcclusionMaskSource.
                    builder.AllowGlobalStateModification(true);

                    // This pass is side-effect driven (writes depth / debug overlay). Avoid accidental culling.
                    builder.AllowPassCulling(false);

                    builder.SetRenderFunc((PassData data, RasterGraphContext rgContext) => {
                        MaybeLogExecute($"[PersonOcclusionPass] RG execute. pass={data.materialPassIndex} bindDepth={bindDepthTarget}");
                        rgContext.cmd.DrawMesh(GetFullscreenTriangleMesh(), Matrix4x4.identity, data.material, 0, data.materialPassIndex);
                    });
                }
            }

            [System.Obsolete("Compatibility mode path (URP RenderGraph disabled).")]
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData) {
                if (material == null)
                    return;

                var mask = Shader.GetGlobalTexture(PersonMaskTexId);
                if (mask == null) {
                    if (bindDepthTarget)
                        return;
                    mask = Texture2D.whiteTexture;
                }

                SyncMaterialFromGlobals(mask);
                MaybeLogMaterialInfo("[PersonOcclusionPass] MatInfo (Execute):");

                var cmd = CommandBufferPool.Get("Savit Person Occlusion Mask");
                try {
                    using (new ProfilingScope(cmd, sampler)) {
#pragma warning disable CS0618 // URP compatibility mode API (RenderGraph disabled)
                        var renderer = renderingData.cameraData.renderer;
                        var colorTarget = renderer.cameraColorTargetHandle;
                        if (bindDepthTarget) {
                            // Be explicit about the RTs we draw into. This avoids cases where the pass
                            // executes but writes depth into a non-active attachment (platform/URP dependent).
                            var depthTarget = renderer.cameraDepthTargetHandle;
                            cmd.SetRenderTarget(colorTarget, depthTarget);
                        } else {
                            cmd.SetRenderTarget(colorTarget);
                        }
#pragma warning restore CS0618
                        cmd.DrawMesh(GetFullscreenTriangleMesh(), Matrix4x4.identity, material, 0, materialPassIndex);
                    }
                    context.ExecuteCommandBuffer(cmd);
                } finally {
                    CommandBufferPool.Release(cmd);
                }

                MaybeLog($"[PersonOcclusionPass] Executed. cam='{renderingData.cameraData.camera?.name}' mask={mask.width}x{mask.height}");
            }
        }

        private Pass depthPass;
        private Pass debugOverlayPass;

        public override void Create() {
            depthPass = new Pass();
            debugOverlayPass = new Pass();
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
            if (settings.onlyWhenPlaying && !Application.isPlaying)
                return;

            if (settings.material == null)
                return;

            depthPass.renderPassEvent = settings.renderPassEvent;
            depthPass.Setup(settings, settings.material, materialPassIndex: 0, bindDepthTarget: true);
            renderer.EnqueuePass(depthPass);

            if (settings.debugDrawMaskOverlay) {
                debugOverlayPass.renderPassEvent = settings.debugOverlayPassEvent;

                var overlayMaterial = settings.debugOverlayMaterial != null ? settings.debugOverlayMaterial : settings.material;
                var overlayPassIndex = settings.debugOverlayMaterial != null ? 0 : 1;
                debugOverlayPass.Setup(settings, overlayMaterial, materialPassIndex: overlayPassIndex, bindDepthTarget: false);
                renderer.EnqueuePass(debugOverlayPass);
            }
        }
    }
}
