// Velmoragh terrain: 8 tiling layers blended by two splat maps (see Tools/mapgen), automatic cliff rock on steep
// slopes, per-pixel bump from blended layer heights (surface-gradient method), wet specular on blood mud, fog of war.
Shader "Bloodfall/Terrain"
{
    Properties
    {
        _Splat0 ("Splat 0 (grass, dirt, road, rock)", 2D) = "red" {}
        _Splat1 ("Splat 1 (blood mud, ash, leaves, paving)", 2D) = "black" {}
        _L0 ("Grass/Moss", 2D) = "gray" {}
        _L1 ("Dirt/Mud", 2D) = "gray" {}
        _L2 ("Cobblestone", 2D) = "gray" {}
        _L3 ("Rock", 2D) = "gray" {}
        _L4 ("Blood Mud", 2D) = "gray" {}
        _L5 ("Bone/Ash", 2D) = "gray" {}
        _L6 ("Leaf Litter", 2D) = "gray" {}
        _L7 ("Sun Paving", 2D) = "gray" {}
        _Tiling ("Layer tiling (m per repeat) 0-3", Vector) = (6, 6, 4, 8)
        _Tiling2 ("Layer tiling (m per repeat) 4-7", Vector) = (5, 6, 5, 4)
        _MapSize ("Map size (m)", Vector) = (192, 192, 0, 0)
        _BumpScale ("Bump Strength", Float) = 0.9
        _CliffStart ("Cliff slope start", Range(0,1)) = 0.78
        _Tint ("Tint", Color) = (1,1,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry-10" }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #include "BloodfallCommon.hlsl"

            TEXTURE2D(_Splat0); SAMPLER(sampler_Splat0);
            TEXTURE2D(_Splat1);
            TEXTURE2D(_L0); SAMPLER(sampler_L0);
            TEXTURE2D(_L1); TEXTURE2D(_L2); TEXTURE2D(_L3); TEXTURE2D(_L4); TEXTURE2D(_L5); TEXTURE2D(_L6); TEXTURE2D(_L7);

            CBUFFER_START(UnityPerMaterial)
                float4 _Tiling; float4 _Tiling2; float4 _MapSize; float _BumpScale; float _CliffStart; half4 _Tint;
                float4 _Splat0_ST; float4 _Splat1_ST;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
            };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            half4 Layer(TEXTURE2D_PARAM(tex, smp), float2 xz, float tiling) { return SAMPLE_TEXTURE2D(tex, smp, xz / tiling); }

            half4 frag (Varyings i) : SV_Target
            {
                float2 xz = i.positionWS.xz;
                float2 mapUV = xz / _MapSize.xy;
                half4 s0 = SAMPLE_TEXTURE2D(_Splat0, sampler_Splat0, mapUV);
                half4 s1 = SAMPLE_TEXTURE2D(_Splat1, sampler_Splat0, mapUV);
                half3 n = normalize(i.normalWS);
                // Steep slopes become rock regardless of paint.
                half cliff = smoothstep(_CliffStart, _CliffStart - 0.2, n.y);
                s0 = lerp(s0, half4(0, 0, 0, 1), cliff);
                s1 *= 1 - cliff;

                half4 l0 = Layer(TEXTURE2D_ARGS(_L0, sampler_L0), xz, _Tiling.x);
                half4 l1 = Layer(TEXTURE2D_ARGS(_L1, sampler_L0), xz, _Tiling.y);
                half4 l2 = Layer(TEXTURE2D_ARGS(_L2, sampler_L0), xz, _Tiling.z);
                half4 l3 = Layer(TEXTURE2D_ARGS(_L3, sampler_L0), xz, _Tiling.w);
                half4 l4 = Layer(TEXTURE2D_ARGS(_L4, sampler_L0), xz, _Tiling2.x);
                half4 l5 = Layer(TEXTURE2D_ARGS(_L5, sampler_L0), xz, _Tiling2.y);
                half4 l6 = Layer(TEXTURE2D_ARGS(_L6, sampler_L0), xz, _Tiling2.z);
                half4 l7 = Layer(TEXTURE2D_ARGS(_L7, sampler_L0), xz, _Tiling2.w);

                // Height-aware blend: layers with taller detail win at transitions (crisper edges than linear).
                half w[8] = { s0.r, s0.g, s0.b, s0.a, s1.r, s1.g, s1.b, s1.a };
                half hgt[8] = { l0.a, l1.a, l2.a, l3.a, l4.a, l5.a, l6.a, l7.a };
                half maxH = 0;
                [unroll] for (int k = 0; k < 8; k++) maxH = max(maxH, w[k] + hgt[k] * 0.5);
                half total = 0;
                half ww[8];
                [unroll] for (int k2 = 0; k2 < 8; k2++) { ww[k2] = max(0, w[k2] + hgt[k2] * 0.5 - maxH + 0.25) * step(0.001, w[k2]); total += ww[k2]; }
                total = max(total, 1e-4);
                half3 albedo = (l0.rgb * ww[0] + l1.rgb * ww[1] + l2.rgb * ww[2] + l3.rgb * ww[3] + l4.rgb * ww[4] + l5.rgb * ww[5] + l6.rgb * ww[6] + l7.rgb * ww[7]) / total;
                half height = (l0.a * ww[0] + l1.a * ww[1] + l2.a * ww[2] + l3.a * ww[3] + l4.a * ww[4] + l5.a * ww[5] + l6.a * ww[6] + l7.a * ww[7]) / total;
                half wet = ww[4] / total;

                // Surface-gradient bump from the blended height.
                float3 dpdx = ddx(i.positionWS), dpdy = ddy(i.positionWS);
                float dhdx = ddx(height), dhdy = ddy(height);
                float3 r1 = cross(dpdy, n), r2 = cross(n, dpdx);
                float det = dot(dpdx, r1);
                float3 grad = sign(det) * (dhdx * r1 + dhdy * r2);
                n = normalize(abs(det) * n - grad * _BumpScale * 0.05);

                BFSurface s;
                s.albedo = albedo * _Tint.rgb;
                s.normalWS = n;
                s.smoothness = 0.12 + wet * 0.6 + cliff * 0.05;
                s.metallic = 0;
                s.occlusion = lerp(0.55, 1, height);
                s.emission = 0;
                s.translucency = 0;
                half3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half3 col = BF_Lighting(s, i.positionWS, i.positionCS, viewDir);
                col = BF_ApplyFog(col, i.positionWS);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; };
            float4 vert (Attributes v) : SV_POSITION { return TransformObjectToHClip(v.positionOS.xyz); }
            half frag () : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            Varyings vert (Attributes v) { Varyings o; o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag (Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
