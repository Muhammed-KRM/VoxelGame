#ifndef STRATOCRAFT_VAT_MOB_SHADER_INCLUDED
#define STRATOCRAFT_VAT_MOB_SHADER_INCLUDED

// ============================================================================
// Stratocraft 500 Mob Gece Dalgasi GPU Vertex Animation Texture (VAT) Shader
// - Kemik (SkinnedMeshRenderer) maliyeti olmadan 500+ yaratigi tek DrawMeshInstancedIndirect
//   cagrisiyla GPU'da canlandirir.
// - T_NightWave_500Mob_VAT_Pos.exr (RGBA16F) ve T_NightWave_500Mob_VAT_Norm.exr (RGBA16F)
//   dokularindan vertexId ve normalize animasyon zamani ile ornekleme yapar.
// ============================================================================

TEXTURE2D(_VatPositionTex);
SAMPLER(sampler_VatPositionTex);

TEXTURE2D(_VatNormalTex);
SAMPLER(sampler_VatNormalTex);

float4 _VatTexelSize; // x: 1/width (vertexCount), y: 1/height (frameCount), z: width, w: height

struct VatSampleResult
{
    float3 localPositionOffset;
    float3 localNormal;
};

VatSampleResult SampleStratocraftVatAnimation(uint vertexId, float normalizedAnimTime, float startFrame, float frameCount)
{
    VatSampleResult result;
    float u = ((float)vertexId + 0.5) * _VatTexelSize.x;
    float frameIndex = startFrame + frac(normalizedAnimTime) * max(1.0, frameCount - 1.0);
    float v = (frameIndex + 0.5) * _VatTexelSize.y;

    float4 posSample = SAMPLE_TEXTURE2D_LOD(_VatPositionTex, sampler_VatPositionTex, float2(u, v), 0);
    float4 normSample = SAMPLE_TEXTURE2D_LOD(_VatNormalTex, sampler_VatNormalTex, float2(u, v), 0);

    result.localPositionOffset = posSample.xyz;
    result.localNormal = normalize(normSample.xyz);
    return result;
}

#endif // STRATOCRAFT_VAT_MOB_SHADER_INCLUDED
