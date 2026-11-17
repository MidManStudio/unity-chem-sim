// ============================================================================
// NOTICE: Full documentation, design decisions, and fix history for this file
// live in docs/com.midmanstudio.proceduralterrain.md, section "Runtime/Unity/Shaders/TerraVertexColor.shader"
// ============================================================================
// A lit shader for the Universal Render Pipeline that takes its colour from the vertex colours of the chunk meshes.
// The Core writes colours in gamma space (the colours as picked); this shader turns them into linear space in the
// vertex stage when the project uses the Linear colour space, and leaves them alone in Gamma.
//
// What it does: the main light with its shadows, additional lights (per pixel or per vertex, as the pipeline asset
// says), ambient light from the spherical harmonics, and fog. What it does not do: Forward+ additional lights,
// screen space shadows, the DepthNormals pass (use the Depth source for SSAO), lightmaps, light probes beyond
// the ambient term. It has not been compiled by Unity yet, see the docs.
Shader "MidManStudio/ProceduralTerrain/Vertex Color Lit"
{
    Properties
    {
        [MainColor] _Tint ("Tint", Color) = (1, 1, 1, 1)
        _AmbientBoost ("Ambient Boost", Range(0, 2)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }
        LOD 100

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // The same buffer in every pass, so the SRP Batcher can batch the material.
        CBUFFER_START(UnityPerMaterial)
            half4 _Tint;
            half _AmbientBoost;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half4 fogAndVertexLight : TEXCOORD3; // x: fog factor, yzw: additional lights per vertex
            };

            // The polynomial Unity itself uses for a fast sRGB to linear conversion.
            half3 TerraToLinear(half3 c)
            {
                return c * (c * (c * 0.305306011h + 0.682171111h) + 0.012522878h);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normals = GetVertexNormalInputs(input.normalOS);

                output.positionCS = positions.positionCS;
                output.positionWS = positions.positionWS;
                output.normalWS = normals.normalWS;

                half3 albedo = input.color.rgb;
                #if !defined(UNITY_COLORSPACE_GAMMA)
                albedo = TerraToLinear(albedo);
                #endif
                output.color = half4(albedo, input.color.a);

                half3 vertexLight = VertexLighting(positions.positionWS, normals.normalWS);
                half fogFactor = ComputeFogFactor(positions.positionCS.z);
                output.fogAndVertexLight = half4(fogFactor, vertexLight);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 albedo = input.color.rgb * _Tint.rgb;
                half3 normalWS = normalize(input.normalWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                half3 lighting = mainLight.color
                    * (mainLight.distanceAttenuation * mainLight.shadowAttenuation * saturate(dot(normalWS, mainLight.direction)));

                #if defined(_ADDITIONAL_LIGHTS)
                uint pixelLightCount = GetAdditionalLightsCount();
                for (uint lightIndex = 0u; lightIndex < pixelLightCount; ++lightIndex)
                {
                    Light light = GetAdditionalLight(lightIndex, input.positionWS);
                    lighting += light.color * (light.distanceAttenuation * saturate(dot(normalWS, light.direction)));
                }
                #endif

                #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                lighting += input.fogAndVertexLight.yzw;
                #endif

                half3 ambient = SampleSH(normalWS) * _AmbientBoost;
                half3 color = albedo * (lighting + ambient);
                color = MixFog(color, input.fogAndVertexLight.x);
                return half4(color, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
