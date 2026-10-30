Shader "Stratocraft/UberShader_UVFree"
{
    Properties
    {
        [NoScaleOffset] _MaterialPaletteLUT ("Material Palette LUT (256x16)", 2D) = "white" {}
        [NoScaleOffset] _MatCapAtlas16x ("MatCap 16x Sphere Atlas", 2D) = "gray" {}
        [NoScaleOffset] _MicroDetailArray ("Micro-Detail Array (32 Layers)", 2DArray) = "" {}
        _BiplanarTileScale ("Biplanar Tile Scale", Range(0.1, 8.0)) = 1.15
        _DamascusFrequency ("Damascus 3D Wave Frequency", Range(2.0, 32.0)) = 14.0
        _BlackbodyIntensity ("Blackbody T^4 Glow Intensity", Range(0.0, 8.0)) = 3.2
        _RimLightStrength ("Sharpness Rim Light Strength", Range(0.0, 1.0)) = 0.20
        _MatCapBlendWeight ("MatCap Metallic Blend Weight", Range(0.0, 1.0)) = 0.65
        _SmoothFacetBlend ("Smooth vs Facet Normal Blend (0=Crisp LowPoly)", Range(0.0, 1.0)) = 0.12
        _AmbientStrength ("Ambient Light Strength", Range(0.1, 2.0)) = 0.85
        _MicroDetailStrength ("Biplanar Micro-Detail Strength", Range(0.0, 1.0)) = 0.35
        [Toggle] _UsePaletteLUT ("Decode R=MaterialID via 256x16 LUT", Float) = 1.0
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _STRATOCRAFT_LOWSPEC_1TAP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            #include "StratocraftInput.hlsl"
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
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "StratocraftInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 4.5
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #pragma vertex StratocraftDepthNormalsVert
            #pragma fragment StratocraftDepthNormalsFrag

            #include "StratocraftUberShader.hlsl"
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
