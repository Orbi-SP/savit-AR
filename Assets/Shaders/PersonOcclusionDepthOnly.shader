Shader "Savit/PersonOcclusionDepthOnly" {
    Properties {
        _Savit_PersonMaskThreshold ("Mask Threshold", Range(0,1)) = 0.5
        _Savit_PersonMaskInvert ("Invert Mask", Float) = 0
        _Savit_PersonMaskFlipY ("Flip Mask Y", Float) = 1
        _Savit_PersonMaskFlipX ("Flip Mask X", Float) = 0
        _Savit_PersonMaskRotate ("Mask Rotate (0=0,1=90,2=180,3=270)", Float) = 0
    }

    SubShader {
        Tags { "RenderPipeline"="UniversalRenderPipeline" }

        Pass {
            Name "PersonOcclusionDepthOnly"
            Tags { "LightMode"="UniversalForward" }

            Cull Off
            ZWrite On
            ZTest Always
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Savit_PersonMaskTex);
            SAMPLER(sampler_Savit_PersonMaskTex);

            float _Savit_PersonMaskThreshold;
            float _Savit_PersonMaskInvert;
            float _Savit_PersonMaskFlipY;
            float _Savit_PersonMaskFlipX;
            float _Savit_PersonMaskRotate;
            float _Savit_PersonMaskUseUvTransform;
            float4 _Savit_PersonMaskUvT0;
            float4 _Savit_PersonMaskUvT1;

            float2 ApplyMaskUv(float2 uv) {
                if (_Savit_PersonMaskUseUvTransform > 0.5) {
                    uv = float2(
                        uv.x * _Savit_PersonMaskUvT0.x + uv.y * _Savit_PersonMaskUvT0.y + _Savit_PersonMaskUvT0.z,
                        uv.x * _Savit_PersonMaskUvT1.x + uv.y * _Savit_PersonMaskUvT1.y + _Savit_PersonMaskUvT1.z
                    );
                }

                // Rotation is expressed as quarter-turns clockwise: 0,1,2,3.
                float r = _Savit_PersonMaskRotate;
                if (r > 0.5 && r < 1.5) {
                    uv = float2(uv.y, 1.0 - uv.x);
                } else if (r >= 1.5 && r < 2.5) {
                    uv = float2(1.0 - uv.x, 1.0 - uv.y);
                } else if (r >= 2.5) {
                    uv = float2(1.0 - uv.y, uv.x);
                }

                if (_Savit_PersonMaskFlipY > 0.5)
                    uv.y = 1.0 - uv.y;
                if (_Savit_PersonMaskFlipX > 0.5)
                    uv.x = 1.0 - uv.x;

                return uv;
            }

            struct Attributes {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input) {
                Varyings output;

                // Input positions are already in clip space (fullscreen triangle).
                // Put geometry at the near plane so masked pixels always occlude.
                // Using vertex depth avoids SV_Depth / gl_FragDepth paths that are less reliable on mobile.
                output.positionCS = float4(input.positionOS.xy, UNITY_NEAR_CLIP_VALUE, 1.0);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target {
                float2 uv = ApplyMaskUv(input.uv);

                float mask = SAMPLE_TEXTURE2D(_Savit_PersonMaskTex, sampler_Savit_PersonMaskTex, uv).r;
                if (_Savit_PersonMaskInvert > 0.5)
                    mask = 1.0 - mask;

                // If the UV transform maps outside the mask texture, treat it as background.
                float2 inMin = step(0.0, uv);
                float2 inMax = step(uv, 1.0);
                float inside01 = inMin.x * inMin.y * inMax.x * inMax.y;
                mask *= inside01;

                clip(mask - _Savit_PersonMaskThreshold);

                // Color is masked out (ColorMask 0). Returning anything is fine.
                return 0;
            }
            ENDHLSL
        }

        Pass {
            Name "PersonOcclusionDebugOverlay"
            Tags { "LightMode"="UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDebug
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Savit_PersonMaskTex);
            SAMPLER(sampler_Savit_PersonMaskTex);

            float _Savit_PersonMaskThreshold;
            float _Savit_PersonMaskInvert;
            float _Savit_PersonMaskFlipY;
            float _Savit_PersonMaskFlipX;
            float _Savit_PersonMaskRotate;
            float _Savit_PersonMaskUseUvTransform;
            float4 _Savit_PersonMaskUvT0;
            float4 _Savit_PersonMaskUvT1;

            float2 ApplyMaskUv(float2 uv) {
                if (_Savit_PersonMaskUseUvTransform > 0.5) {
                    uv = float2(
                        uv.x * _Savit_PersonMaskUvT0.x + uv.y * _Savit_PersonMaskUvT0.y + _Savit_PersonMaskUvT0.z,
                        uv.x * _Savit_PersonMaskUvT1.x + uv.y * _Savit_PersonMaskUvT1.y + _Savit_PersonMaskUvT1.z
                    );
                }

                float r = _Savit_PersonMaskRotate;
                if (r > 0.5 && r < 1.5) {
                    uv = float2(uv.y, 1.0 - uv.x);
                } else if (r >= 1.5 && r < 2.5) {
                    uv = float2(1.0 - uv.x, 1.0 - uv.y);
                } else if (r >= 2.5) {
                    uv = float2(1.0 - uv.y, uv.x);
                }

                if (_Savit_PersonMaskFlipY > 0.5)
                    uv.y = 1.0 - uv.y;
                if (_Savit_PersonMaskFlipX > 0.5)
                    uv.x = 1.0 - uv.x;

                return uv;
            }

            struct Attributes {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                // Input positions are already in clip space (fullscreen triangle).
                output.positionCS = float4(input.positionOS.xy, 0.0, 1.0);
                output.uv = input.uv;
                return output;
            }

            float4 FragDebug(Varyings input) : SV_Target {
                float2 uv = ApplyMaskUv(input.uv);

                float mask = SAMPLE_TEXTURE2D(_Savit_PersonMaskTex, sampler_Savit_PersonMaskTex, uv).r;
                if (_Savit_PersonMaskInvert > 0.5)
                    mask = 1.0 - mask;

                float2 inMin = step(0.0, uv);
                float2 inMax = step(uv, 1.0);
                float inside01 = inMin.x * inMin.y * inMax.x * inMax.y;
                mask *= inside01;

                // Red = foreground (person), Green = background.
                float isPerson = step(_Savit_PersonMaskThreshold, mask);
                float3 rgb = lerp(float3(0.0, 1.0, 0.0), float3(1.0, 0.0, 0.0), isPerson);
                return float4(rgb, 0.35);
            }
            ENDHLSL
        }
    }

    // Fallback SubShader for devices/backends where the URP Core.hlsl path ends up unsupported.
    // Targeted at OpenGLES3 (your current Android Graphics API).
    SubShader {
        Tags { "RenderPipeline"="UniversalRenderPipeline" }

        Pass {
            Name "PersonOcclusionDepthOnly"
            Tags { "LightMode"="UniversalForward" }

            Cull Off
            ZWrite On
            ZTest Always
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma only_renderers gles3

            Texture2D _Savit_PersonMaskTex;
            SamplerState sampler_Savit_PersonMaskTex;

            float _Savit_PersonMaskThreshold;
            float _Savit_PersonMaskInvert;
            float _Savit_PersonMaskFlipY;
            float _Savit_PersonMaskFlipX;
            float _Savit_PersonMaskRotate;
            float _Savit_PersonMaskUseUvTransform;
            float4 _Savit_PersonMaskUvT0;
            float4 _Savit_PersonMaskUvT1;

            float2 ApplyMaskUv(float2 uv) {
                if (_Savit_PersonMaskUseUvTransform > 0.5) {
                    uv = float2(
                        uv.x * _Savit_PersonMaskUvT0.x + uv.y * _Savit_PersonMaskUvT0.y + _Savit_PersonMaskUvT0.z,
                        uv.x * _Savit_PersonMaskUvT1.x + uv.y * _Savit_PersonMaskUvT1.y + _Savit_PersonMaskUvT1.z
                    );
                }

                float r = _Savit_PersonMaskRotate;
                if (r > 0.5 && r < 1.5) {
                    uv = float2(uv.y, 1.0 - uv.x);
                } else if (r >= 1.5 && r < 2.5) {
                    uv = float2(1.0 - uv.x, 1.0 - uv.y);
                } else if (r >= 2.5) {
                    uv = float2(1.0 - uv.y, uv.x);
                }

                if (_Savit_PersonMaskFlipY > 0.5)
                    uv.y = 1.0 - uv.y;
                if (_Savit_PersonMaskFlipX > 0.5)
                    uv.x = 1.0 - uv.x;

                return uv;
            }

            struct Attributes {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                // For OpenGL-style clip space (GLES), near plane is z = -1.
                output.positionCS = float4(input.positionOS.xy, -1.0, 1.0);
                output.uv = input.uv;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target {
                float2 uv = ApplyMaskUv(input.uv);

                float mask = _Savit_PersonMaskTex.Sample(sampler_Savit_PersonMaskTex, uv).r;
                if (_Savit_PersonMaskInvert > 0.5)
                    mask = 1.0 - mask;

                float2 inMin = step(0.0, uv);
                float2 inMax = step(uv, 1.0);
                float inside01 = inMin.x * inMin.y * inMax.x * inMax.y;
                mask *= inside01;

                clip(mask - _Savit_PersonMaskThreshold);
                return 0;
            }
            ENDHLSL
        }

        Pass {
            Name "PersonOcclusionDebugOverlay"
            Tags { "LightMode"="UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragDebug
            #pragma target 3.0
            #pragma only_renderers gles3

            Texture2D _Savit_PersonMaskTex;
            SamplerState sampler_Savit_PersonMaskTex;

            float _Savit_PersonMaskThreshold;
            float _Savit_PersonMaskInvert;
            float _Savit_PersonMaskFlipY;
            float _Savit_PersonMaskFlipX;
            float _Savit_PersonMaskRotate;
            float _Savit_PersonMaskUseUvTransform;
            float4 _Savit_PersonMaskUvT0;
            float4 _Savit_PersonMaskUvT1;

            float2 ApplyMaskUv(float2 uv) {
                if (_Savit_PersonMaskUseUvTransform > 0.5) {
                    uv = float2(
                        uv.x * _Savit_PersonMaskUvT0.x + uv.y * _Savit_PersonMaskUvT0.y + _Savit_PersonMaskUvT0.z,
                        uv.x * _Savit_PersonMaskUvT1.x + uv.y * _Savit_PersonMaskUvT1.y + _Savit_PersonMaskUvT1.z
                    );
                }

                float r = _Savit_PersonMaskRotate;
                if (r > 0.5 && r < 1.5) {
                    uv = float2(uv.y, 1.0 - uv.x);
                } else if (r >= 1.5 && r < 2.5) {
                    uv = float2(1.0 - uv.x, 1.0 - uv.y);
                } else if (r >= 2.5) {
                    uv = float2(1.0 - uv.y, uv.x);
                }

                if (_Savit_PersonMaskFlipY > 0.5)
                    uv.y = 1.0 - uv.y;
                if (_Savit_PersonMaskFlipX > 0.5)
                    uv.x = 1.0 - uv.x;

                return uv;
            }

            struct Attributes {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input) {
                Varyings output;
                output.positionCS = float4(input.positionOS.xy, 0.0, 1.0);
                output.uv = input.uv;
                return output;
            }

            float4 FragDebug(Varyings input) : SV_Target {
                float2 uv = ApplyMaskUv(input.uv);

                float mask = _Savit_PersonMaskTex.Sample(sampler_Savit_PersonMaskTex, uv).r;
                if (_Savit_PersonMaskInvert > 0.5)
                    mask = 1.0 - mask;

                float2 inMin = step(0.0, uv);
                float2 inMax = step(uv, 1.0);
                float inside01 = inMin.x * inMin.y * inMax.x * inMax.y;
                mask *= inside01;

                float isPerson = step(_Savit_PersonMaskThreshold, mask);
                float3 rgb = lerp(float3(0.0, 1.0, 0.0), float3(1.0, 0.0, 0.0), isPerson);
                return float4(rgb, 0.35);
            }
            ENDHLSL
        }
    }
}
