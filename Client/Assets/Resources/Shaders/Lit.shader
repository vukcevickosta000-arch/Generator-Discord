// Bloodfall lit shader for heroes, creeps, structures and props: albedo/normal/mask maps, emission, rim light (team /
// hover outline), hit flash, dissolve (death / spawn), fog of war for static objects.
Shader "Bloodfall/Lit"
{
    Properties
    {
        _BaseMap ("Albedo", 2D) = "white" {}
        _BaseColor ("Color", Color) = (1,1,1,1)
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Float) = 1
        _MaskMap ("Mask (R metal, G occlusion, A smooth)", 2D) = "white" {}
        _Metallic ("Metallic", Range(0,1)) = 0
        _Smoothness ("Smoothness", Range(0,1)) = 0.35
        _EmissionMap ("Emission", 2D) = "white" {}
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,1)
        _RimColor ("Rim Color (a = strength)", Color) = (1,0.2,0.2,0)
        _RimPower ("Rim Power", Float) = 3
        _HitFlash ("Hit Flash", Range(0,1)) = 0
        _Dissolve ("Dissolve", Range(0,1)) = 0
        [HDR] _DissolveEdge ("Dissolve Edge", Color) = (4,0.6,0.2,1)
        _FogAffected ("Fog of war (static objects)", Float) = 0
        _Translucency ("Translucency (cloth)", Range(0,1)) = 0
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "BloodfallCommon.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap); TEXTURE2D(_MaskMap); TEXTURE2D(_EmissionMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; half4 _BaseColor; half _BumpScale; half _Metallic; half _Smoothness; half4 _EmissionColor;
            half4 _RimColor; half _RimPower; half _HitFlash; half _Dissolve; half4 _DissolveEdge; half _FogAffected; half _Translucency; half _Cutoff;
        CBUFFER_END

        half BF_DissolveClip(float2 uv, float3 positionWS)
        {
            half n = BF_Noise(uv * 18) * 0.6 + BF_Noise(positionWS.xz * 3 + positionWS.y) * 0.4;
            return n - _Dissolve * 1.05;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog

            struct Attributes
            {
                float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 tangentOS : TANGENT; float2 uv : TEXCOORD0; float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; half3 normalWS : TEXCOORD2;
                half4 tangentWS : TEXCOORD3; half fogFactor : TEXCOORD4; half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert (Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                VertexNormalInputs n = GetVertexNormalInputs(v.normalOS, v.tangentOS);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.normalWS = n.normalWS;
                o.tangentWS = half4(n.tangentWS, v.tangentOS.w * GetOddNegativeScale());
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                o.color = v.color;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor;
                clip(albedo.a - _Cutoff);
                half dissolve = 1;
                if (_Dissolve > 0.001)
                {
                    dissolve = BF_DissolveClip(i.uv, i.positionWS);
                    clip(dissolve);
                }
                half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_BaseMap, i.uv);
                half3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BaseMap, i.uv), _BumpScale);
                half3 bitangent = i.tangentWS.w * cross(i.normalWS, i.tangentWS.xyz);
                half3 normalWS = normalize(mul(nTS, half3x3(i.tangentWS.xyz, bitangent, i.normalWS)));

                BFSurface s;
                s.albedo = albedo.rgb * i.color.rgb;
                s.normalWS = normalWS;
                s.metallic = _Metallic * mask.r;
                s.smoothness = _Smoothness * mask.a;
                s.occlusion = mask.g;
                s.emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_BaseMap, i.uv).rgb * _EmissionColor.rgb;
                s.translucency = _Translucency;
                half3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half3 col = BF_Lighting(s, i.positionWS, i.positionCS, viewDir);
                col += BF_Rim(normalWS, viewDir, _RimColor, _RimPower);
                col = lerp(col, half3(1.6, 1.5, 1.4), _HitFlash * 0.6);
                if (_Dissolve > 0.001) col += _DissolveEdge.rgb * saturate(1 - dissolve * 12);
                if (_FogAffected > 0.5) col = BF_ApplyFog(col, i.positionWS);
                col = MixFog(col, i.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "BloodfallShadow.hlsl"
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1; };
            Varyings vert (Attributes v)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(v);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = BF_ShadowClip(posWS, TransformObjectToWorldNormal(v.normalOS));
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.positionWS = posWS;
                return o;
            }
            half4 frag (Varyings i) : SV_Target
            {
                if (_Cutoff > 0) clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * _BaseColor.a - _Cutoff);
                if (_Dissolve > 0.001) clip(BF_DissolveClip(i.uv, i.positionWS));
                return 0;
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
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert (Attributes v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); return TransformObjectToHClip(v.positionOS.xyz); }
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
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; half3 normalWS : TEXCOORD0; };
            Varyings vert (Attributes v) { Varyings o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag (Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
