// Shared lighting helpers for Bloodfall's URP shaders: fog-of-war sampling, stylised-PBR lighting (main light +
// additional lights + shadows + SH ambient), rim light and hit flash.
#ifndef BLOODFALL_COMMON_INCLUDED
#define BLOODFALL_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

// Fog of war (set globally by the match client). R = visible now (smoothed), G = explored.
TEXTURE2D(_BF_FogTex); SAMPLER(sampler_BF_FogTex);
float4 _BF_FogParams;      // x = 1/mapWidth, y = 1/mapHeight, z = enabled (0/1), w = fogged brightness
float4 _BF_FogColor;       // tint for unseen areas

half BF_FogVisibility(float3 positionWS)
{
    if (_BF_FogParams.z < 0.5) return 1;
    float2 uv = float2(positionWS.x * _BF_FogParams.x, positionWS.z * _BF_FogParams.y);
    half2 f = SAMPLE_TEXTURE2D(_BF_FogTex, sampler_BF_FogTex, uv).rg;
    // Unexplored areas are darker than remembered (explored) ones.
    half fogged = lerp(_BF_FogParams.w * 0.55, _BF_FogParams.w, f.g);
    return lerp(fogged, 1, f.r);
}

half3 BF_ApplyFog(half3 color, float3 positionWS)
{
    half v = BF_FogVisibility(positionWS);
    half lum = dot(color, half3(0.3, 0.59, 0.11));
    half3 desat = lerp(half3(lum, lum, lum), color, 0.45) * _BF_FogColor.rgb;
    return lerp(desat * v, color, saturate((v - _BF_FogParams.w) / max(1e-3, 1 - _BF_FogParams.w)));
}

struct BFSurface
{
    half3 albedo;
    half3 normalWS;
    half smoothness;
    half metallic;
    half occlusion;
    half3 emission;
    half translucency;
};

half3 BF_LightTerm(Light light, BFSurface s, half3 viewDirWS)
{
    half ndl = dot(s.normalWS, light.direction);
    half diffuse = saturate(ndl);
    // Wrap-around for translucent surfaces (foliage, cloth).
    diffuse = lerp(diffuse, saturate((ndl + 0.5) / 1.5), s.translucency);
    half3 h = SafeNormalize(light.direction + viewDirWS);
    half ndh = saturate(dot(s.normalWS, h));
    half specPow = exp2(10 * s.smoothness + 1);
    half3 specColor = lerp(half3(0.04, 0.04, 0.04), s.albedo, s.metallic);
    half3 spec = specColor * pow(ndh, specPow) * (specPow + 8) / 8 * s.smoothness;
    half atten = light.distanceAttenuation * light.shadowAttenuation;
    return (s.albedo * (1 - s.metallic * 0.8) * diffuse + spec * diffuse) * light.color * atten;
}

half3 BF_Lighting(BFSurface s, float3 positionWS, float4 positionCS, half3 viewDirWS)
{
    float4 shadowCoord = TransformWorldToShadowCoord(positionWS);
    Light mainLight = GetMainLight(shadowCoord);
    half3 color = BF_LightTerm(mainLight, s, viewDirWS);

    #if defined(_ADDITIONAL_LIGHTS) || USE_FORWARD_PLUS
    InputData inputData = (InputData)0;
    inputData.positionWS = positionWS;
    inputData.normalWS = s.normalWS;
    inputData.viewDirectionWS = viewDirWS;
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(positionCS);
    uint pixelLightCount = GetAdditionalLightsCount();
    #if USE_FORWARD_PLUS
    for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        Light l = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
        color += BF_LightTerm(l, s, viewDirWS);
    }
    #endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, positionWS, half4(1, 1, 1, 1));
        color += BF_LightTerm(light, s, viewDirWS);
    LIGHT_LOOP_END
    #endif

    half3 ambient = SampleSH(s.normalWS) * s.albedo * s.occlusion;
    return color + ambient + s.emission;
}

half3 BF_Rim(half3 normalWS, half3 viewDirWS, half4 rimColor, half power)
{
    half r = pow(1 - saturate(dot(normalWS, viewDirWS)), power);
    return rimColor.rgb * rimColor.a * r;
}

// Cheap value noise for water / wind.
float BF_Hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float BF_Noise(float2 p)
{
    float2 i = floor(p), f = frac(p);
    float2 u = f * f * (3 - 2 * f);
    return lerp(lerp(BF_Hash(i), BF_Hash(i + float2(1, 0)), u.x), lerp(BF_Hash(i + float2(0, 1)), BF_Hash(i + float2(1, 1)), u.x), u.y);
}

#endif
