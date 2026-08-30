// InstancedPrimitive_URP.shader
//
// Shared by AtomRenderer and BondRenderer — both just need "a mesh with a
// per-instance transform and a per-instance color," and the shader itself
// doesn't care whether the bound mesh is a sphere (atoms) or a cylinder
// (bonds). One shader, two meshes, instead of two near-identical shaders.
//
// Dual-path #ifdef structure and the reasoning behind it copied directly
// from MidManStudio_Unity's InstancedProjectile_URP.shader — that file's
// own header comment documents a real bug this avoids: without the
// UNITY_INSTANCING_ENABLED branch, UNITY_ACCESS_INSTANCED_PROP in the
// non-instanced (combined-mesh) path reads the MATERIAL's default
// property value instead of any real per-instance data (there isn't any
// in that path), silently corrupting color for every instance in the
// batch. The fix is the same here: the combined-mesh path uses per-vertex
// color baked directly into the mesh by AtomRenderer/BondRenderer, never
// touching the instanced-property accessor at all.
//
// Simple fixed-direction "fake lit" shading (a diffuse-style dot product
// against a hardcoded light direction, no real lighting/shadow pipeline
// involved) rather than a full PBR/Lit setup — matches the low-poly art
// direction, and keeps this shader small enough to reason about
// correctness without a Unity Editor to visually verify it in (this was
// written and reviewed without one — flagging that plainly rather than
// pretending otherwise).

Shader "MidManStudio/Alembic/InstancedPrimitive_URP"
{
    Properties
    {
        _Color        ("Tint Color (combined-mesh path only)", Color) = (1, 1, 1, 1)
        _LightDir     ("Fake Light Direction (object space)",  Vector) = (0.5, 0.8, -0.3, 0)
        _AmbientLevel ("Ambient Floor", Range(0, 1))                  = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
            "RenderType"     = "Opaque"
        }

        Cull Back
        ZWrite On
        Lighting Off

        Pass
        {
            Name "Unlit"

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float4 _LightDir;
            float  _AmbientLevel;

            UNITY_INSTANCING_BUFFER_START(Props)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(Props)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float4 col        : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS   = TransformObjectToWorldNormal(input.normalOS);

#ifdef UNITY_INSTANCING_ENABLED
                // ── Instanced path (DrawMeshInstanced) ─────────────────────────
                // Per-instance _Color from the MaterialPropertyBlock (element
                // colour for atoms, strain colour for bonds). Vertex color on
                // the source mesh (a plain primitive) is white, so this is the
                // real color, not a multiply against baked-in vertex data.
                output.col = UNITY_ACCESS_INSTANCED_PROP(Props, _Color);
#else
                // ── Combined-mesh path (DrawMesh) ───────────────────────────────
                // Color is pre-baked per-vertex by AtomRenderer/BondRenderer —
                // pass straight through. The _Color material default is
                // intentionally NOT read here, same reasoning as the reference
                // shader this is modeled on: a misconfigured material default
                // must never be able to corrupt what's actually drawn.
                output.col = input.color;
#endif
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 n = normalize(input.normalWS);
                float3 l = normalize(_LightDir.xyz);
                float  ndotl = saturate(dot(n, l));
                float  shade = lerp(_AmbientLevel, 1.0, ndotl);

                half4 result;
                result.rgb = input.col.rgb * shade;
                result.a   = input.col.a;
                return result;
            }
            ENDHLSL
        }
    }

    Fallback Off
}
