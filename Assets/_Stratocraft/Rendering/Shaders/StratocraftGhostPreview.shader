Shader "Stratocraft/GhostPreviewHologram"
{
    Properties
    {
        _GhostTint ("Ghost Stability Tint", Color) = (0.11, 0.78, 0.38, 0.58)
        _RimBoost ("Fresnel Rim Boost", Range(0.0, 2.0)) = 0.95
        _PreviewStateMode ("Preview Stress Mode (0=Green,1=Amber,2=Orange,3=Red)", Range(0.0, 3.0)) = 0.0
        _NaturalPreserveWeight ("Natural Material Preserve Weight", Range(0.0, 1.0)) = 0.70
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+20"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _GhostTint;
                half _RimBoost;
                half _PreviewStateMode;
                half _NaturalPreserveWeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                half4 color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                half4 color       : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // DOK5 §2.2: 4 kademeli stres rengi — Mavi renk TAMAMEN kaldirildi!
            // 0 = Tok Zumrut Yesili (Guvenli), 1 = Kehribar Sarisi, 2 = Nabiz Atan Turuncu, 3 = Kesik Kirmizi
            half3 ResolveStressRimColor(half mode, float3 posWS, out half rimPulse)
            {
                rimPulse = 1.0h;
                if (mode < 0.5h)
                {
                    return half3(0.11h, 0.78h, 0.38h);
                }
                if (mode < 1.5h)
                {
                    return half3(0.95h, 0.75h, 0.15h);
                }
                if (mode < 2.5h)
                {
                    rimPulse = 0.78h + 0.22h * (half)sin(_Time.y * 7.5);
                    return half3(0.98h, 0.45h, 0.09h);
                }

                // Kesik Kirmizi (Gecersiz / Havada / Asiri Yuk >= %95)
                half stripe = step(0.35h, frac((half)((posWS.x + posWS.y + posWS.z) * 2.2 - _Time.y * 1.8)));
                rimPulse = 0.72h + 0.48h * stripe;
                return half3(0.95h, 0.14h, 0.12h);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrmInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = NormalizeNormalPerVertex(nrmInputs.normalWS);
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 nrm = NormalizeNormalPerPixel(input.normalWS);
                float3 viewDir = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half ndv = saturate(dot(nrm, viewDir));
                half fresnel = pow(1.0h - ndv, 2.4h);
                half hemi = 0.64h + 0.36h * saturate(nrm.y * 0.5h + 0.5h);

                half rimPulse;
                half3 stressCol = ResolveStressRimColor(_PreviewStateMode, input.positionWS, rimPulse);

                // %70 dogal malzeme rengini koru + %30 ince stres yuzey tonu + Fresnel kenar stres vurgusu
                half3 vCol = max(input.color.rgb, half3(0.20h, 0.20h, 0.20h));
                half keepNatural = saturate(_NaturalPreserveWeight);
                half3 baseRgb = lerp(stressCol * hemi, vCol * hemi, keepNatural);
                half3 finalRgb = baseRgb + stressCol * (fresnel * _RimBoost * rimPulse);
                half baseAlpha = (_GhostTint.a > 0.05h) ? _GhostTint.a : 0.58h;
                half finalAlpha = saturate(baseAlpha + fresnel * 0.28h);
                return half4(finalRgb, finalAlpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
