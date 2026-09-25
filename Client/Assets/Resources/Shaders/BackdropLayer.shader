// Menu backdrop parallax layer: unlit, alpha blended, optional horizontal UV scroll, emissive flicker layer and a
// global lightning flash term.
Shader "Bloodfall/BackdropLayer"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _EmissionTex ("Emission (additive)", 2D) = "black" {}
        _EmissionStrength ("Emission Strength", Float) = 0
        _ScrollX ("Scroll X (uv/s)", Float) = 0
        _Flash ("Lightning Flash", Float) = 0
        _FlashResponse ("Flash Response", Float) = 1
        _Flicker ("Flicker", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; fixed4 _Color;
            sampler2D _EmissionTex; float _EmissionStrength; float _ScrollX; float _Flash; float _FlashResponse; float _Flicker;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 uvE : TEXCOORD1; };
            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex) + float2(_Time.y * _ScrollX, 0);
                o.uvE = v.uv;
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.rgb *= 1.0 + _Flash * _FlashResponse * 2.5;
                c.rgb += _Flash * _FlashResponse * float3(0.35, 0.32, 0.45) * c.a;
                fixed4 e = tex2D(_EmissionTex, i.uvE);
                // Candle flicker: per-window variation from the emission alpha pattern + time.
                float flick = 1.0 - _Flicker * (0.5 + 0.5 * sin(_Time.y * 7.3 + i.uvE.x * 91.0) * sin(_Time.y * 3.1 + i.uvE.y * 57.0));
                c.rgb += e.rgb * e.a * _EmissionStrength * flick;
                c.a = saturate(c.a + e.a * _EmissionStrength * 0.2);
                return c;
            }
            ENDCG
        }
    }
}
