#ifndef STRATOCRAFT_UBER_SHADER_INCLUDED
#define STRATOCRAFT_UBER_SHADER_INCLUDED

// ==============================================================================
// Stratocraft UV-Free Procedural Uber-Shader Cekirdegi (URP 17 Forward+ & DOTS)
// - Color32 Sozlesmesi: R=MaterialID (nointerpolation uint), G=Temperature (T^4),
//   B=Sharpness/FacetContrast, A=CavityAO
// - Astroneer / Deep Rock Galactic ddx/ddy Flat-Faceted Low-Poly Normaller
// - Kuresel 256x16 LUT + 16x MatCap + 2-Tap IQ Biplanar MicroDetail + SSAO + Golge
// - SRP Batcher & Entities Graphics (BatchRendererGroup) %100 Uyumlu
// ==============================================================================

#include "StratocraftInput.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

struct StratocraftAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 color      : COLOR;     // R=MaterialID (veya Direct RGB), G=Temp, B=Sharpness/FacetTone, A=CavityAO
    float4 uv1        : TEXCOORD1; // Opsiyonel yedek metadata
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct StratocraftVaryings
{
    float4 positionCS               : SV_POSITION;
    float3 positionWS               : TEXCOORD0;
    float3 normalWS                 : TEXCOORD1;
    float3 positionOS               : TEXCOORD2;
    nointerpolation uint matId      : TEXCOORD3;
    float3 tempSharpAo              : TEXCOORD4;
    float4 directColorAndFog        : TEXCOORD5; // rgb = vertexCol.rgb, a = fogFactor
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

/// sRGB (0..1) tablo rengini aktif renk uzayina (Linear ise dogrusal uzaya) cevirir.
float3 ConvertLutSrgbToActiveColorSpace(float3 srgbCol)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return srgbCol;
#else
    // Exact piecewise sRGB -> Linear
    float3 lo = srgbCol / 12.92;
    float3 hi = pow(max((srgbCol + 0.055) / 1.055, 0.0), 2.4);
    return (srgbCol <= 0.04045) ? lo : hi;
#endif
}

/// Unity Linear Color Space modunda GPU Input Assembler Color32.rgb (UInt8 UNORM_SRGB) kanallarini
/// otomatik Linear uzaya cevirir. Ters parcali donusum ile 0..255 tam sayilari %100 kayipsiz geri kazanilir.
float3 RecoverRawColor32Bytes01(float3 gpuColorRgb)
{
#if defined(UNITY_COLORSPACE_GAMMA)
    return saturate(gpuColorRgb);
#else
    float3 c = max(gpuColorRgb, 0.0);
    float3 lo = c * 12.92;
    float3 hi = 1.055 * pow(c, 1.0 / 2.4) - 0.055;
    return saturate((c <= 0.0031308) ? lo : hi);
#endif
}

/// 256x16 LUT dokusundan MaterialID (0..255) ve satir (0..15) texel'ini filtre/mip olmadan tam okur.
/// Not: Unity PNG yukleyicisi dikey ekseni ters cevirdigi icin PIL lut[0] satiri GPU bellekte y = 15 satirindadir.
float4 LoadMaterialLutExact(uint matId, uint rowIdx)
{
    int flippedY = 15 - (int)min(rowIdx, 15u);
    int2 coord = int2((int)(matId & 255u), flippedY);
    return LOAD_TEXTURE2D(_MaterialPaletteLUT, coord);
}

/// 4x4 (16 kure) MatCap atlasindan gorunum-uzayi normaline gore sifir-isik maliyetli yansima okur.
float3 SampleMatCap16x(float3 normalWS, uint matCapIndex)
{
    float3 normalVS = normalize(mul((float3x3)UNITY_MATRIX_V, normalWS));
    float2 localUv = clamp(normalVS.xy * 0.48 + 0.5, 0.02, 0.98);
    uint tileX = matCapIndex & 3u;
    uint tileY = (matCapIndex >> 2u) & 3u;
    float2 atlasUv = (float2((float)tileX, (float)(3u - tileY)) + localUv) * 0.25;
    float3 rawMc = SAMPLE_TEXTURE2D(_MatCapAtlas16x, sampler_MatCapAtlas16x, atlasUv).rgb;
    return rawMc;
}

/// Inigo Quilez 2-Tap Biplanar Mapping (textureGrad ile eksen gecisinde mip dikis hatasini onler).
float4 SampleBiplanarMicroDetail(float3 posWS, float3 normalWS, float layerIdx, float2 screenPosPx)
{
    float3 p = posWS * max(_BiplanarTileScale, 0.05);
    float3 dpdx = ddx(p);
    float3 dpdy = ddy(p);
    float3 n = abs(normalWS);

    uint3 ma = (n.x > n.y && n.x > n.z) ? uint3(0, 1, 2) :
               ((n.y > n.z)             ? uint3(1, 2, 0) : uint3(2, 0, 1));
    uint3 mi = (n.x < n.y && n.x < n.z) ? uint3(0, 1, 2) :
               ((n.y < n.z)             ? uint3(1, 2, 0) : uint3(2, 0, 1));
    uint3 me = uint3(3, 3, 3) - mi - ma;

    float2 uvMajor  = float2(p[ma.y], p[ma.z]);
    float2 dxMajor  = float2(dpdx[ma.y], dpdx[ma.z]);
    float2 dyMajor  = float2(dpdy[ma.y], dpdy[ma.z]);

    float2 uvMedian = float2(p[me.y], p[me.z]);
    float2 dxMedian = float2(dpdx[me.y], dpdx[me.z]);
    float2 dyMedian = float2(dpdy[me.y], dpdy[me.z]);

#if defined(_STRATOCRAFT_LOWSPEC_1TAP)
    float dither = frac(dot(screenPosPx, float2(0.375, 0.125)));
    float majorW = saturate((n[ma.x] - 0.5773) / (1.0 - 0.5773));
    bool pickMajor = majorW > dither;
    float2 chosenUv = pickMajor ? uvMajor : uvMedian;
    float2 chosenDx = pickMajor ? dxMajor : dxMedian;
    float2 chosenDy = pickMajor ? dyMajor : dyMedian;
    return SAMPLE_TEXTURE2D_ARRAY_GRAD(_MicroDetailArray, sampler_MicroDetailArray, chosenUv, layerIdx, chosenDx, chosenDy);
#else
    float2 w = float2(n[ma.x], n[me.x]);
    w = saturate((w - 0.5773) / (1.0 - 0.5773));
    w = w * w * w;
    w = w * w;
    w /= max(w.x + w.y, 1e-4);

    float4 s1 = SAMPLE_TEXTURE2D_ARRAY_GRAD(_MicroDetailArray, sampler_MicroDetailArray, uvMajor, layerIdx, dxMajor, dyMajor);
    float4 s2 = SAMPLE_TEXTURE2D_ARRAY_GRAD(_MicroDetailArray, sampler_MicroDetailArray, uvMedian, layerIdx, dxMedian, dyMedian);
    return s1 * w.x + s2 * w.y;
#endif
}

/// 3B Prosedurel Kati Doku: 1 = Sam Celigi (Damascus Sine-Wave), 2 = Ahsap Yas Halkasi, 3 = Kristal Faset
float3 ApplyProceduralSolidPattern(float3 baseAlbedo, float3 posOS, uint procMode)
{
    if (procMode == 1u)
    {
        float3 sp = posOS * max(_DamascusFrequency, 8.0);
        float fold = sin(sp.x * 2.3 + cos(sp.y * 3.7)) + cos(sp.z * 2.9 + sin(sp.x * 3.1));
        float band = 0.5 + 0.5 * sin(sp.y * 9.0 + fold * 2.6);
        return baseAlbedo * lerp(0.80, 1.20, smoothstep(0.25, 0.75, band));
    }
    if (procMode == 2u)
    {
        float3 wp = posOS * 12.0;
        float radial = length(wp.xz) + 0.18 * sin(wp.y * 1.7 + wp.x * 2.1);
        float grain = 0.5 + 0.5 * sin(radial * 8.5);
        return baseAlbedo * lerp(0.84, 1.14, grain);
    }
    if (procMode == 3u)
    {
        float3 cp = posOS * 16.0;
        float sparkle = abs(sin(cp.x) * cos(cp.y) * sin(cp.z));
        return baseAlbedo * (0.92 + 0.30 * sparkle);
    }
    return baseAlbedo;
}

/// Cevher, komur, kukurt ve mucevher damari araligi (30..49 ve 52..59).
bool IsOreOrGemVeinMat(uint matId)
{
    return (matId >= 30u && matId <= 49u) || (matId >= 52u && matId <= 59u);
}

/// 0 MB ek doku VRAM ile 3B alan-carpitmali sirt gurultusu uzerinden ana kayac matrisi (veinMask01=0)
/// ve parlak cevher/kristal damari (veinMask01=1) + oksit halkasi ayrimini uretir.
/// Floating-Origin (K * 32m) gecislerinde %100 dikissiz ve kaymasiz kalmasi icin 2*PI/32 harmonikleri kullanir.
void ApplyProceduralOreVeinAndGlint(
    uint matId, float3 posWS, float sharpnessOrTone,
    inout float3 albedo, inout float roughness, inout float metallic, out float veinMask01)
{
    const float kStep = 0.19634954; // 2 * PI / 32.0 (32m Floating Origin periyodik kilidi)
    float3 p = posWS - floor(posWS * (1.0 / 32.0)) * 32.0;

    float3 warp = 0.46 * sin(p.yzx * (19.0 * kStep)) + 0.22 * cos(p.zxy * (31.0 * kStep));
    float3 q1 = p * (29.0 * kStep) + warp * 2.45;
    float ridge1 = 1.0 - abs(sin(q1.x + cos(q1.y)) * cos(q1.z - sin(q1.x)));

    float3 q2 = p * (53.0 * kStep) - warp.yzx * 1.90;
    float ridge2 = 1.0 - abs(sin(q2.y + sin(q2.z)) * cos(q2.x + cos(q2.y)));

    float pocket = 0.5 + 0.5 * (sin(p.x * (17.0 * kStep) + warp.y) * cos(p.y * (23.0 * kStep) - warp.z));
    float rawVein = saturate(ridge1 * 0.66 + ridge2 * 0.34 + (pocket - 0.45) * 0.22 + (sharpnessOrTone - 0.5) * 0.18);

    veinMask01 = smoothstep(0.45, 0.69, rawVein);
    float haloMask01 = saturate(smoothstep(0.32, 0.50, rawVein) - veinMask01);

    // Yan kayac matrisi (host rock): sig magarada koyu kirec/silikat tasi, derin katmanda koyu bazalt
    float deepBasaltBlend = saturate((52.0 - posWS.y) / 28.0);
    float3 stoneHost = ConvertLutSrgbToActiveColorSpace(float3(0.33, 0.35, 0.39));
    float3 basaltHost = ConvertLutSrgbToActiveColorSpace(float3(0.22, 0.24, 0.28));
    float3 hostAlbedo = lerp(stoneHost, basaltHost, deepBasaltBlend) * lerp(0.88, 1.08, sharpnessOrTone);
    float hostRoughness = 0.84;
    float hostMetallic = 0.04;

    float3 oreAlbedo = albedo;
    float3 haloAlbedo = hostAlbedo * 0.85;
    float oreRoughness = roughness;
    float oreMetallic = metallic;

    if (matId == 30u) // Bakir Cevheri: kizil-turuncu kalkopirit damar + hafif malakit yesil oksit kenar
    {
        oreAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.88, 0.42, 0.20));
        haloAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.20, 0.50, 0.40));
        oreRoughness = 0.23;
        oreMetallic = 0.90;
    }
    else if (matId == 31u) // Kalay Cevheri: kalay-gumus kasiterit + kahve oksit kenar
    {
        oreAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.76, 0.79, 0.81));
        haloAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.42, 0.30, 0.22));
        oreRoughness = 0.27;
        oreMetallic = 0.86;
    }
    else if (matId == 32u) // Demir Cevheri: parlak celik manyetit damar + pasli kizil hematit oksit halkasi
    {
        oreAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.72, 0.75, 0.80));
        haloAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.48, 0.22, 0.14));
        oreRoughness = 0.25;
        oreMetallic = 0.90;
    }
    else if (matId == 33u) // Boksit / Aluminyum
    {
        oreAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.82, 0.56, 0.46));
        haloAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.54, 0.34, 0.22));
        oreRoughness = 0.34;
        oreMetallic = 0.74;
    }
    else if (matId == 34u) // Gumus Cevheri
    {
        oreAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.93, 0.95, 0.98));
        haloAlbedo = hostAlbedo * 0.75;
        oreRoughness = 0.15;
        oreMetallic = 0.96;
    }
    else if (matId == 35u) // Altin Cevheri: yuksek yansiticilikli saf altin + acik kuvars damar yatagi
    {
        oreAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.98, 0.79, 0.22));
        haloAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.70, 0.68, 0.62));
        oreRoughness = 0.14;
        oreMetallic = 0.98;
    }
    else if (matId == 36u) // Titanyum Cevheri
    {
        oreAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.80, 0.86, 0.94));
        haloAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.30, 0.36, 0.46));
        oreRoughness = 0.19;
        oreMetallic = 0.94;
    }
    else if (matId == 37u) // Mithril Cevheri
    {
        oreAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.44, 0.86, 0.98));
        haloAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.22, 0.48, 0.64));
        oreRoughness = 0.14;
        oreMetallic = 0.97;
    }
    else if (matId == 44u) // Komur Damari: mat tas icinde kirikli antrasit-siyah camsi (vitreous) seritler
    {
        oreAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.06, 0.06, 0.07));
        haloAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.16, 0.16, 0.18));
        oreRoughness = 0.22;
        oreMetallic = 0.18;
    }
    else if (matId == 52u) // Kukurt Cevheri: parlak kanarya sarisi kristal kukurt
    {
        oreAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.92, 0.88, 0.18));
        haloAlbedo = ConvertLutSrgbToActiveColorSpace(float3(0.48, 0.46, 0.18));
        oreRoughness = 0.32;
        oreMetallic = 0.14;
    }
    else if (matId == 41u || (matId >= 46u && matId <= 49u) || (matId >= 53u && matId <= 59u))
    {
        // Kuvars, Ametist, Safir, Zumrut, Yakut, Elmas, Kizil Elmas, Astral & Yildiz Kristalleri
        float prism = 0.5 + 0.5 * sin(q2.x * 1.7 + q1.y * 1.3);
        oreAlbedo = albedo * lerp(1.08, 1.38, prism);
        haloAlbedo = lerp(hostAlbedo, oreAlbedo, 0.35);
        oreRoughness = 0.11;
        oreMetallic = max(metallic, 0.56);
    }
    else // Orichalcum, Cehennem Demiri, Ilahi Metal, Adamantite, Hiclik Metali, Karanlik Madde (38..43, 45)
    {
        oreAlbedo = albedo * 1.18;
        haloAlbedo = lerp(hostAlbedo, oreAlbedo, 0.30);
        oreRoughness = clamp(roughness * 0.55, 0.14, 0.26);
        oreMetallic = max(metallic, 0.92);
    }

    float3 baseWithHalo = lerp(hostAlbedo, haloAlbedo, haloMask01 * 0.85);
    albedo = lerp(baseWithHalo, oreAlbedo, veinMask01);
    roughness = lerp(hostRoughness, oreRoughness, veinMask01);
    metallic = lerp(hostMetallic, oreMetallic, veinMask01);
}

/// MagmaBlock (MaterialId=11, Temp=220) icin koyu soguyan volkanik bazalt kabugu ve
/// catlaklarindan (magmaCrack01 -> 1) sicak turuncu-kirmizi kor isinimi ureten 3B maske.
float EvaluateMagmaBlockCrustFissure01(float3 posWS)
{
    const float kStep = 0.19634954; // 2 * PI / 32.0
    float3 p = posWS - floor(posWS * (1.0 / 32.0)) * 32.0;
    float3 mw = 0.38 * sin(p.yzx * (23.0 * kStep)) + 0.19 * cos(p.zxy * (41.0 * kStep));
    float3 mq1 = p * (37.0 * kStep) + mw * 2.6;
    float3 mq2 = p * (61.0 * kStep) - mw.yzx * 2.1;
    float crackA = 1.0 - abs(sin(mq1.x + cos(mq1.z)) * cos(mq1.y - sin(mq1.x)));
    float crackB = 1.0 - abs(sin(mq2.y + sin(mq2.x)) * cos(mq2.z + cos(mq2.y)));
    return saturate(smoothstep(0.60, 0.88, crackA) + 0.55 * smoothstep(0.68, 0.92, crackB));
}

float3 ComputeFacetedWorldNormal(float3 posWS, float3 interpNormalWS)
{
    float3 dpdx = ddx(posWS);
    float3 dpdy = ddy(posWS);
    float3 crossN = cross(dpdy, dpdx);
    float lenSq = dot(crossN, crossN);
    float3 vtxN = normalize(interpNormalWS);
    if (lenSq < 1e-12)
    {
        return vtxN;
    }

    float3 geoN = crossN * rsqrt(lenSq);
    if (dot(geoN, vtxN) < 0.0)
    {
        geoN = -geoN;
    }
    return normalize(lerp(geoN, vtxN, saturate(_SmoothFacetBlend)));
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

    // VertexAttributeFormat.UNorm8 (DXGI_FORMAT_R8G8B8A8_UNORM) baytlari dogrudan byte / 255.0f olarak gecirir
    uint rawMatId = (uint)round(saturate(input.color.r) * 255.0);
    output.matId = (rawMatId == 0u && input.uv1.x > 0.001)
        ? (uint)round(saturate(input.uv1.x) * 255.0)
        : rawMatId;

    output.tempSharpAo = float3(
        saturate(input.color.g),
        saturate(input.color.b),
        saturate(input.color.a));

    output.directColorAndFog = float4(
        input.color.rgb,
        ComputeFogFactor(posInputs.positionCS.z));

    return output;
}

half4 StratocraftUberFrag(StratocraftVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float3 normalWS = ComputeFacetedWorldNormal(input.positionWS, input.normalWS);
    float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    float2 normalizedScreenUV = GetNormalizedScreenSpaceUV(input.positionCS);

    float heatNorm = input.tempSharpAo.x;
    float sharpnessOrTone = input.tempSharpAo.y;
    float cavityAo = lerp(0.42, 1.0, input.tempSharpAo.z);

    // Dogal emici zemin malzemeleri (Dirt=2, Grass=3, Sand=4, Gravel=9, LooseRubble=15, Clay=16) icin
    // Temp kanalinin kor olmayan alt bandi (1..120 -> 0.003..0.50) Toprak Nem/Camur Doygunlugunu tasir!
    bool isWettableSoilMat = (input.matId == 2u || input.matId == 3u || input.matId == 4u ||
                              input.matId == 9u || input.matId == 15u || input.matId == 16u);
    float soilWetness01 = 0.0;
    if (isWettableSoilMat && heatNorm > 0.002 && heatNorm <= 0.50)
    {
        soilWetness01 = saturate(heatNorm / (120.0 / 255.0));
        heatNorm = 0.0;
    }

    float3 albedo;
    float roughness = 0.72;
    float metallic = 0.0;
    float veinMask01 = 0.0;
    float magmaCrack01 = 1.0;
    uint procMode = 0u;
    float microLayer = 0.0;
    uint matCapIdx = 0u;

    if (_UsePaletteLUT > 0.5)
    {
        uint safeMatId = max(input.matId, 1u);
        float4 row0 = LoadMaterialLutExact(safeMatId, 0u);
        float4 row1 = LoadMaterialLutExact(safeMatId, 1u);

        albedo = ConvertLutSrgbToActiveColorSpace(row0.rgb);
        // B kanali (Sharpness / FacetContrast): ufak ucgen fasetler arasinda dogal DRG/Astroneer kontrast varyasyonu saglar
        float facetContrast = lerp(0.86, 1.14, sharpnessOrTone);
        albedo *= facetContrast;

        roughness = saturate(row0.a - sharpnessOrTone * 0.28);
        metallic = row1.r;
        procMode = (uint)round(row1.g * 3.0);
        microLayer = floor(row1.b * 31.0 + 0.5);
        matCapIdx = (uint)round(row1.a * 15.0);

        albedo = ApplyProceduralSolidPattern(albedo, input.positionOS, procMode);

        if (_MicroDetailStrength > 0.01)
        {
            float4 microSample = SampleBiplanarMicroDetail(input.positionWS, normalWS, microLayer, input.positionCS.xy);
            float microMod = lerp(1.0, lerp(0.84, 1.16, microSample.a), saturate(_MicroDetailStrength));
            albedo *= microMod;
        }

        if (IsOreOrGemVeinMat(safeMatId))
        {
            ApplyProceduralOreVeinAndGlint(
                safeMatId, input.positionWS, sharpnessOrTone,
                albedo, roughness, metallic, veinMask01);
        }
        else if (safeMatId == 11u) // MagmaBlock: koyu bazalt kabuk + kor turuncu-kirmizi catlaklar
        {
            magmaCrack01 = EvaluateMagmaBlockCrustFissure01(input.positionWS);
            float3 crustCol = ConvertLutSrgbToActiveColorSpace(float3(0.15, 0.13, 0.14)) * facetContrast;
            float3 fissureCol = ConvertLutSrgbToActiveColorSpace(float3(0.98, 0.30, 0.04));
            albedo = lerp(crustCol, fissureCol, magmaCrack01);
            roughness = lerp(0.86, 0.28, magmaCrack01);
            metallic = lerp(0.05, 0.02, magmaCrack01);
        }
    }
    else
    {
        albedo = input.directColorAndFog.rgb;
        roughness = saturate(0.75 - sharpnessOrTone * 0.35);
    }

    // Toprak islakligi ve camurlasma kararmasi: nem arttikca gozenekli renk koyulasir, doygunlukta koyu kahve camur ve islak parlama olusur
    if (soilWetness01 > 0.005)
    {
        float dampFactor = saturate(soilWetness01 * 2.35);
        albedo *= lerp(1.0, 0.52, dampFactor);

        float3 richMudLinear = ConvertLutSrgbToActiveColorSpace(float3(0.21, 0.125, 0.065));
        float mudColorShift = smoothstep(0.14, 0.72, soilWetness01);
        albedo = lerp(albedo, richMudLinear, mudColorShift * 0.92);

        roughness = lerp(roughness, 0.11, smoothstep(0.08, 0.80, soilWetness01));
    }

    // Yukseklik bazli hafif jeolojik derinlik gradyani (magara ve falez tabanlarina hacim katar)
    float heightFactor = saturate((input.positionWS.y + 4.0) / 18.0);
    albedo *= lerp(0.78, 1.06, heightFactor);

    // SSAO (Screen Space Ambient Occlusion) entegrasyonu
    float ssaoDirect = 1.0;
    float ssaoIndirect = 1.0;
#if defined(_SCREEN_SPACE_OCCLUSION)
    AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(normalizedScreenUV);
    ssaoDirect = aoFactor.directAmbientOcclusion;
    ssaoIndirect = aoFactor.indirectAmbientOcclusion;
#endif
    float combinedIndirectAo = cavityAo * ssaoIndirect;

    // Ana yonlu gunes isigi ve kaskad golgeleri
    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
    Light mainLight = GetMainLight(shadowCoord);
    mainLight.shadowAttenuation *= ssaoDirect;

    float rawNdotL = dot(normalWS, mainLight.direction);
    float ndotl = saturate(rawNdotL);
    float wrappedFill = saturate(rawNdotL * 0.55 + 0.45) * 0.18;

    float3 halfDir = normalize(mainLight.direction + viewDirWS);
    float specPow = lerp(12.0, 96.0, 1.0 - roughness);
    float specStrength = lerp(0.10, 0.75, 1.0 - roughness);
    float specTerm = pow(saturate(dot(normalWS, halfDir)), specPow) * specStrength * mainLight.shadowAttenuation;

    // Kuresel SH + Gemi/Gokyuzu-Zemin yari-kure ortam aydinlatmasi
    float3 shAmbient = SampleSH(normalWS);
    float upFactor = normalWS.y * 0.5 + 0.5;
    float3 skyHemi = float3(0.46, 0.56, 0.74);
    float3 groundHemi = float3(0.18, 0.15, 0.14);
    float3 ambientTerm = max(shAmbient, lerp(groundHemi, skyHemi, upFactor)) * max(_AmbientStrength, 0.25) * combinedIndirectAo;

    float3 directDiffuse = mainLight.color * (ndotl * mainLight.shadowAttenuation + wrappedFill * combinedIndirectAo);
    float3 totalDiffuse = albedo * (ambientTerm + directDiffuse);
    float3 totalSpecular = mainLight.color * specTerm;
    float3 additionalLightIrradiance = float3(0.0, 0.0, 0.0);

    float3 glintMicroNormalWS = normalWS;
    if (veinMask01 > 0.35)
    {
        const float kGlintFreq = 11.97732; // 61 * (2*PI / 32.0)
        float3 pGlint = input.positionWS - floor(input.positionWS * (1.0 / 32.0)) * 32.0;
        glintMicroNormalWS = normalize(normalWS + 0.14 * sin(pGlint.yzx * kGlintFreq));
        float mainGlintDot = max(saturate(dot(normalWS, halfDir)), saturate(dot(glintMicroNormalWS, halfDir)));
        float mainGlintSpec = pow(saturate(mainGlintDot), 96.0) * (veinMask01 * 1.45) * mainLight.shadowAttenuation;
        float3 mainGlintTint = lerp(float3(1.0, 0.98, 0.94), max(albedo * 1.45, float3(0.35, 0.35, 0.35)), saturate(metallic * 0.75));
        totalSpecular += mainLight.color * mainGlintSpec * mainGlintTint;
    }

    // URP 17 Forward+ (_CLUSTER_LIGHT_LOOP) ve Ek Nokta/Spot Isiklar (Mesale, Kamp Atesi, Magma)
#if defined(_ADDITIONAL_LIGHTS) || defined(_CLUSTER_LIGHT_LOOP)
    InputData inputData = (InputData)0;
    inputData.positionWS = input.positionWS;
    inputData.normalWS = normalWS;
    inputData.viewDirectionWS = viewDirWS;
    inputData.normalizedScreenSpaceUV = normalizedScreenUV;

    uint pixelLightCount = GetAdditionalLightsCount();
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light addLight = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
        float addAtten = addLight.distanceAttenuation * addLight.shadowAttenuation;
        float addNdotL = saturate(dot(normalWS, addLight.direction));
        float3 addHalf = normalize(addLight.direction + viewDirWS);
        float addSpec = pow(saturate(dot(normalWS, addHalf)), specPow) * specStrength;
        float3 addIrradiance = addLight.color * (addNdotL * addAtten);
        additionalLightIrradiance += addIrradiance;
        totalDiffuse += albedo * addIrradiance;
        totalSpecular += addLight.color * (addSpec * addAtten);

        if (veinMask01 > 0.35)
        {
            float glintDot = max(saturate(dot(normalWS, addHalf)), saturate(dot(glintMicroNormalWS, addHalf)));
            float glintSpec = pow(saturate(glintDot), 96.0) * (veinMask01 * 2.45);
            float3 glintTint = lerp(float3(1.0, 0.98, 0.94), max(albedo * 1.45, float3(0.35, 0.35, 0.35)), saturate(metallic * 0.75));
            totalSpecular += addLight.color * (glintSpec * addAtten) * glintTint;
        }
    LIGHT_LOOP_END
#endif

    // Metalik MatCap yansima karisimi: ortam + gunes + mesale/nokta isik enerjisiyle module edilir ki
    // karanlik magarada yapay parlamasin ve mesale isigi MatCap tarafindan ezilmesin!
    float3 litRgb = totalDiffuse;
    if (_UsePaletteLUT > 0.5 && metallic > 0.05)
    {
        float3 matCapCol = SampleMatCap16x(normalWS, matCapIdx);
        float3 sceneLightEnergy = ambientTerm + directDiffuse + additionalLightIrradiance;
        float addLightLuma = dot(additionalLightIrradiance, float3(0.2126, 0.7152, 0.0722));
        float torchPreserveDamp = 1.0 / (1.0 + addLightLuma * 0.55);
        float3 metalTint = albedo * matCapCol * (sceneLightEnergy * 1.14 + 0.04);
        litRgb = lerp(litRgb, metalTint, saturate(metallic * _MatCapBlendWeight * torchPreserveDamp));
    }

    float fresnelRim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 3.2) * _RimLightStrength * combinedIndirectAo;
    float3 finalRgb = litRgb + totalSpecular * cavityAo + fresnelRim * float3(0.62, 0.74, 0.95);

    // Stefan-Boltzmann T^4 Kara Cisim (MagmaBlock Catlak Kor Isinimi / Lav / Demirci Orsu) Emisyonu
    float effectiveHeatNorm = (input.matId == 11u)
        ? (heatNorm * lerp(0.14, 1.06, magmaCrack01))
        : heatNorm;

    if (effectiveHeatNorm > 0.03)
    {
        float hClamped = saturate(effectiveHeatNorm);
        float3 heatColor;
        if (input.matId == 11u)
        {
            // Sicak Volkanik Kaya (MagmaBlock, Temp=220): catlaklardan sicak turuncu-kirmizi magma kor isinimi
            heatColor = ConvertLutSrgbToActiveColorSpace(
                lerp(float3(0.78, 0.10, 0.01), float3(1.0, 0.48, 0.06), saturate(magmaCrack01)));
        }
        else if (_UsePaletteLUT > 0.5)
        {
            uint heatRow = 2u + (uint)round(hClamped * 13.0);
            heatColor = ConvertLutSrgbToActiveColorSpace(LoadMaterialLutExact(max(input.matId, 1u), heatRow).rgb);
        }
        else
        {
            heatColor = lerp(float3(1.0, 0.24, 0.02), float3(1.0, 0.86, 0.28), hClamped);
        }
        float blackbodyT4 = (hClamped * hClamped) * (0.35 + 0.65 * hClamped * hClamped);
        finalRgb = lerp(finalRgb, heatColor, saturate(hClamped * 0.70)) + heatColor * (blackbodyT4 * _BlackbodyIntensity);
    }

    finalRgb = MixFog(finalRgb, input.directColorAndFog.a);
    return half4(finalRgb, 1.0h);
}

// ==============================================================================
// SSAO (Screen Space Ambient Occlusion) Icin Faset Uyumlu DepthNormals Pass
// ==============================================================================
struct StratocraftDepthNormalsAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct StratocraftDepthNormalsVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

StratocraftDepthNormalsVaryings StratocraftDepthNormalsVert(StratocraftDepthNormalsAttributes input)
{
    StratocraftDepthNormalsVaryings output = (StratocraftDepthNormalsVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs nrmInputs = GetVertexNormalInputs(input.normalOS);

    output.positionCS = posInputs.positionCS;
    output.positionWS = posInputs.positionWS;
    output.normalWS = NormalizeNormalPerVertex(nrmInputs.normalWS);
    return output;
}

half4 StratocraftDepthNormalsFrag(StratocraftDepthNormalsVaryings input) : SV_Target0
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float3 facetNormalWS = ComputeFacetedWorldNormal(input.positionWS, input.normalWS);
    return half4(NormalizeNormalPerPixel(facetNormalWS), 0.0h);
}

#endif // STRATOCRAFT_UBER_SHADER_INCLUDED
