#ifndef STRATOCRAFT_UBER_SHADER_INCLUDED
#define STRATOCRAFT_UBER_SHADER_INCLUDED

// ==============================================================================
// Stratocraft UV-Free Procedural Uber-Shader Cekirdegi (Bolum 3.4 & 4.13.16)
// Toplam Kaplama VRAM Butcesi < 16.0 MB (Faz 0 Olcumu: ~3.13 MB)
// SRP Batcher & GPU Resident Drawer %100 Uyumlu
// ==============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_MaterialPaletteLUT);
SAMPLER(sampler_MaterialPaletteLUT);

TEXTURE2D(_MatCapAtlas16x);
SAMPLER(sampler_MatCapAtlas16x);

TEXTURE2D_ARRAY(_MicroDetailArray);
SAMPLER(sampler_MicroDetailArray);

CBUFFER_START(UnityPerMaterial)
    float _BiplanarTileScale;
    float _DamascusFrequency;
    float _BlackbodyIntensity;
    float _RimLightStrength;
    float _MatCapBlendWeight;
CBUFFER_END

struct StratocraftAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 color      : COLOR; // R=MaterialID, G=Temperature(T^4), B=Sharpness, A=CavityAO
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct StratocraftVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    float3 positionOS : TEXCOORD2;
    float4 vertexCol  : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

/// 256x16 LUT dokusundan MaterialID (0..255) ve satir (0..15) pikselini tam merkezden okur.
float4 SampleMaterialLutExact(uint matId, uint rowIdx)
{
    float u = ( (float)(matId & 255u) + 0.5 ) / 256.0;
    float v = ( (float)min(rowIdx, 15u) + 0.5 ) / 16.0;
    return SAMPLE_TEXTURE2D_LOD(_MaterialPaletteLUT, sampler_MaterialPaletteLUT, float2(u, v), 0.0);
}

/// 4x4 (16 kure) MatCap atlasindan gorunum-uzayi normaline gore sifir-isik maliyetli yansima okur.
float3 SampleMatCap16x(float3 normalWS, uint matCapIndex)
{
    float3 normalVS = normalize(mul((float3x3)UNITY_MATRIX_V, normalWS));
    float2 localUv = clamp(normalVS.xy * 0.48 + 0.5, 0.02, 0.98);
    uint tileX = matCapIndex & 3u;
    uint tileY = (matCapIndex >> 2u) & 3u;
    float2 atlasUv = (float2((float)tileX, (float)(3u - tileY)) + localUv) * 0.25;
    return SAMPLE_TEXTURE2D(_MatCapAtlas16x, sampler_MatCapAtlas16x, atlasUv).rgb;
}

/// Inigo Quilez 2-Tap Biplanar Mapping (8 GB RAM / iGPU dusuk donanim modunda 1-Tap Dithered dalina gecer).
float4 SampleBiplanarMicroDetail(float3 posWS, float3 normalWS, float layerIdx, float2 screenPosPx)
{
    float3 p = posWS * max(_BiplanarTileScale, 0.05);
    float3 n = abs(normalWS);

    // En baskin ve ikinci baskin eksenlerin siralanmasi
    uint3 ma = (n.x > n.y && n.x > n.z) ? uint3(0, 1, 2) :
               ((n.y > n.z)             ? uint3(1, 2, 0) : uint3(2, 0, 1));
    uint3 mi = (n.x < n.y && n.x < n.z) ? uint3(0, 1, 2) :
               ((n.y < n.z)             ? uint3(1, 2, 0) : uint3(2, 0, 1));
    uint3 me = uint3(3, 3, 3) - mi - ma;

    float2 uvMajor = float2(p[ma.y], p[ma.z]);
    float2 uvMedian = float2(p[me.y], p[me.z]);

#if defined(_STRATOCRAFT_LOWSPEC_1TAP)
    // iGPU dostu tek dokunuslu (1-Tap) Bayer benzeri titremeli gecis
    float dither = frac(dot(screenPosPx, float2(0.375, 0.125)));
    float majorW = saturate((n[ma.x] - 0.5773) / (1.0 - 0.5773));
    float2 chosenUv = (majorW > dither) ? uvMajor : uvMedian;
    return SAMPLE_TEXTURE2D_ARRAY(_MicroDetailArray, sampler_MicroDetailArray, chosenUv, layerIdx);
#else
    float2 w = float2(n[ma.x], n[me.x]);
    w = saturate((w - 0.5773) / (1.0 - 0.5773));
    w = pow(w, 6.0);
    w /= max(w.x + w.y, 1e-4);

    float4 s1 = SAMPLE_TEXTURE2D_ARRAY(_MicroDetailArray, sampler_MicroDetailArray, uvMajor, layerIdx);
    float4 s2 = SAMPLE_TEXTURE2D_ARRAY(_MicroDetailArray, sampler_MicroDetailArray, uvMedian, layerIdx);
    return s1 * w.x + s2 * w.y;
#endif
}

/// 3B Prosedurel Kati Doku: 1 = Sam Celigi (Damascus Sine-Wave), 2 = Ahsap Yas Halkasi, 3 = Kristal Faset
float3 ApplyProceduralSolidPattern(float3 baseAlbedo, float3 posOS, uint procMode)
{
    if (procMode == 1u)
    {
        // 3B Katlanmis Sam Celigi (Damascus Steel) sinuzoidal dalga deseni
        float3 sp = posOS * max(_DamascusFrequency, 8.0);
        float fold = sin(sp.x * 2.3 + cos(sp.y * 3.7)) + cos(sp.z * 2.9 + sin(sp.x * 3.1));
        float band = 0.5 + 0.5 * sin(sp.y * 9.0 + fold * 2.6);
        return baseAlbedo * lerp(0.76, 1.22, smoothstep(0.25, 0.75, band));
    }
    if (procMode == 2u)
    {
        // 3B Silindirik Ahsap Yas Halkalari
        float3 wp = posOS * 14.0;
        float radial = length(wp.xz) + 0.18 * sin(wp.y * 1.7 + wp.x * 2.1);
        float grain = 0.5 + 0.5 * sin(radial * 8.5);
        return baseAlbedo * lerp(0.80, 1.15, grain);
    }
    if (procMode == 3u)
    {
        // 3B Kristal Ic Kirinim Pariltisi
        float3 cp = posOS * 18.0;
        float sparkle = abs(sin(cp.x) * cos(cp.y) * sin(cp.z));
        return baseAlbedo * (0.90 + 0.35 * sparkle);
    }
    return baseAlbedo;
}

StratocraftVaryings StratocraftUberVert(StratocraftAttributes input)
{
    StratocraftVaryings output = (StratocraftVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs nrmInputs = GetVertexNormalInputs(input.normalOS);

    output.positionCS = posInputs.positionCS;
    output.positionWS = posInputs.positionWS;
    output.normalWS = NormalizeNormalPerVertex(nrmInputs.normalWS);
    output.positionOS = input.positionOS.xyz;
    output.vertexCol = input.color;
    return output;
}

half4 StratocraftUberFrag(StratocraftVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float3 normalWS = normalize(input.normalWS);
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

    // 1) Color32 kanallarini coz: R=MaterialID, G=Temperature, B=Sharpness, A=CavityAO
    uint matId = (uint)round(saturate(input.vertexCol.r) * 255.0);
    float heatNorm = saturate(input.vertexCol.g);
    float bladeSharpness = saturate(input.vertexCol.b);
    float cavityAo = lerp(0.35, 1.0, saturate(input.vertexCol.a));

    // 2) Kuresel 256x16 LUT okuma
    float4 row0 = SampleMaterialLutExact(matId, 0u);
    float4 row1 = SampleMaterialLutExact(matId, 1u);

    float3 albedo = row0.rgb;
    float roughness = saturate(row0.a - bladeSharpness * 0.35);
    float metallic = row1.r;
    uint procMode = (uint)round(row1.g * 3.0);
    float microLayer = floor(row1.b * 31.0 + 0.5);
    uint matCapIdx = (uint)round(row1.a * 15.0);

    // 3) 3B Prosedurel Sam Celigi / Ahsap / Kristal dokusu
    albedo = ApplyProceduralSolidPattern(albedo, input.positionOS, procMode);

    // 4) Biplanar Mikro-Detay ve Cavity birlesimi
    float4 microSample = SampleBiplanarMicroDetail(input.positionWS, normalWS, microLayer, input.positionCS.xy);
    float microCavity = lerp(0.75, 1.15, microSample.a);
    albedo *= microCavity;

    // 5) Ana gunes isigi + MatCap + Kenar Pariltisi (Sharpness Rim)
    Light mainLight = GetMainLight();
    float ndotl = saturate(dot(normalWS, mainLight.direction));
    float3 halfDir = normalize(mainLight.direction + viewDirWS);
    float specPow = lerp(8.0, 96.0, 1.0 - roughness);
    float specTerm = pow(saturate(dot(normalWS, halfDir)), specPow) * (0.25 + 0.95 * bladeSharpness);

    float3 matCapCol = SampleMatCap16x(normalWS, matCapIdx);
    float3 ambientSh = SampleSH(normalWS);

    float3 diffuseLit = albedo * (ambientSh + mainLight.color * (0.25 + 0.75 * ndotl));
    float3 metallicLit = lerp(diffuseLit, albedo * matCapCol * 1.35, metallic * _MatCapBlendWeight);
    float fresnelRim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 3.2) * _RimLightStrength * (0.3 + bladeSharpness);
    float3 finalRgb = (metallicLit + mainLight.color * specTerm + fresnelRim) * cavityAo;

    // 6) Demirci Orsunde T^4 Blackbody Termal Kizarma Emisyonu
    if (heatNorm > 0.02)
    {
        uint heatRow = 2u + (uint)round(heatNorm * 13.0);
        float3 heatColor = SampleMaterialLutExact(matId, heatRow).rgb;
        float blackbodyT4 = (heatNorm * heatNorm) * (heatNorm * heatNorm);
        finalRgb = lerp(finalRgb, heatColor, saturate(heatNorm * 0.75)) + heatColor * (blackbodyT4 * _BlackbodyIntensity);
    }

    return half4(finalRgb, 1.0h);
}

#endif // STRATOCRAFT_UBER_SHADER_INCLUDED
