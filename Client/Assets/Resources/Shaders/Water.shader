// River of Velmoragh: dark blood-tinted water with procedural ripples, moonlit specular, fresnel sky reflection,
// depth-based shoreline foam and transparency (requires the camera depth texture, enabled by ProjectSetup).
Shader "Bloodfall/Water"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.32, 0.06, 0.07, 0.55)
        _DeepColor ("Deep", Color) = (0.08, 0.01, 0.03, 0.92)
        _SkyColor ("Reflection", Color) = (0.35, 0.22, 0.3, 1)
        _DepthRange ("Depth Range (m)", Float) = 1.2
        _Ripple ("Ripple (scale, speed, strength)", Vector) = (1.4, 0.35, 0.35, 0)
        _Smoothness ("Smoothness", Range(0,1)) = 0.92
        _FoamColor ("Foam", Color) = (0.8, 0.55, 0.5, 0.6)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-100" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
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
            #pragma multi_compile_fog
            #include "BloodfallCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor; half4 _DeepColor; half4 _SkyColor; float _DepthRange; float4 _Ripple; half _Smoothness; half4 _FoamColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float4 screenPos : TEXCOORD1; half fogFactor : TEXCOORD2; };
            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.fogFactor = ComputeFogFactor(p.positionCS.z);
                return o;
            }
            float Height(float2 xz)
            {
                float t = _Time.y * _Ripple.y;
                return BF_Noise(xz * _Ripple.x + float2(t, t * 0.6)) * 0.6 + BF_Noise(xz * _Ripple.x * 2.3 - float2(t * 0.8, -t)) * 0.4;
            }
            half4 frag (Varyings i) : SV_Target
            {
                float2 xz = i.positionWS.xz;
                float e = 0.05;
                float h = Height(xz);
                float3 n = normalize(float3((h - Height(xz + float2(e, 0))) / e * _Ripple.z, 1, (h - Height(xz + float2(0, e))) / e * _Ripple.z));
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float waterDepth = LinearEyeDepth(i.positionCS.z, _ZBufferParams);
                float depth = saturate((sceneDepth - waterDepth) / _DepthRange);
                half4 col = lerp(_ShallowColor, _DeepColor, depth);
                half3 viewDir = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half fresnel = pow(1 - saturate(dot(n, viewDir)), 4);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half3 hv = SafeNormalize(mainLight.direction + viewDir);
                half spec = pow(saturate(dot(n, hv)), exp2(10 * _Smoothness + 1)) * _Smoothness * 2;
                half3 rgb = col.rgb * (SampleSH(n) + mainLight.color * 0.25) + _SkyColor.rgb * fresnel + mainLight.color * spec * mainLight.shadowAttenuation;
                half foam = saturate(1 - (sceneDepth - waterDepth) / 0.25) * (0.6 + 0.4 * BF_Noise(xz * 6 + _Time.y));
                rgb = lerp(rgb, _FoamColor.rgb, foam * _FoamColor.a);
                rgb = BF_ApplyFog(rgb, i.positionWS);
                rgb = MixFog(rgb, i.fogFactor);
                return half4(rgb, saturate(col.a + fresnel * 0.3 + foam * 0.3));
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
