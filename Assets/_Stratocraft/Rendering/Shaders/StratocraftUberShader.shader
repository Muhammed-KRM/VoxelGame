Shader "Stratocraft/UberShader_UVFree"
{
    Properties
    {
        [NoScaleOffset] _MaterialPaletteLUT ("Material Palette LUT (256x16)", 2D) = "white" {}
        [NoScaleOffset] _MatCapAtlas16x ("MatCap 16x Sphere Atlas", 2D) = "gray" {}
        [NoScaleOffset] _MicroDetailArray ("Micro-Detail Array (32 Layers)", 2DArray) = "" {}
        _BiplanarTileScale ("Biplanar Tile Scale", Range(0.1, 8.0)) = 1.25
        _DamascusFrequency ("Damascus 3D Wave Frequency", Range(2.0, 32.0)) = 14.0
        _BlackbodyIntensity ("Blackbody T^4 Glow Intensity", Range(0.0, 8.0)) = 3.2
        _RimLightStrength ("Sharpness Rim Light Strength", Range(0.0, 1.0)) = 0.22
        _MatCapBlendWeight ("MatCap Metallic Blend Weight", Range(0.0, 1.0)) = 0.75
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
            "UniversalMaterialType" = "Lit"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _STRATOCRAFT_LOWSPEC_1TAP

            #pragma vertex StratocraftUberVert
            #pragma fragment StratocraftUberFrag

            #include "StratocraftUberShader.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile_instancing
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
