#ifndef STRATOCRAFT_INPUT_INCLUDED
#define STRATOCRAFT_INPUT_INCLUDED

// ==============================================================================
// Stratocraft Tekil SRP-Batcher & Entities Graphics (DOTS) Uyumlu CBUFFER
// ForwardLit, ShadowCaster, DepthOnly ve DepthNormals pass'larinin tamami
// birebir ayni UnityPerMaterial duzenini paylasir (%100 SRP Batcher).
// ==============================================================================

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
    float _SmoothFacetBlend;
    float _AmbientStrength;
    float _MicroDetailStrength;
    float _UsePaletteLUT;
CBUFFER_END

#ifdef UNITY_DOTS_INSTANCING_ENABLED
UNITY_DOTS_INSTANCING_START(MaterialPropertyMetadata)
    UNITY_DOTS_INSTANCED_PROP(float, _BiplanarTileScale)
    UNITY_DOTS_INSTANCED_PROP(float, _DamascusFrequency)
    UNITY_DOTS_INSTANCED_PROP(float, _BlackbodyIntensity)
    UNITY_DOTS_INSTANCED_PROP(float, _RimLightStrength)
    UNITY_DOTS_INSTANCED_PROP(float, _MatCapBlendWeight)
    UNITY_DOTS_INSTANCED_PROP(float, _SmoothFacetBlend)
    UNITY_DOTS_INSTANCED_PROP(float, _AmbientStrength)
    UNITY_DOTS_INSTANCED_PROP(float, _MicroDetailStrength)
    UNITY_DOTS_INSTANCED_PROP(float, _UsePaletteLUT)
UNITY_DOTS_INSTANCING_END(MaterialPropertyMetadata)
#endif

#endif // STRATOCRAFT_INPUT_INCLUDED
