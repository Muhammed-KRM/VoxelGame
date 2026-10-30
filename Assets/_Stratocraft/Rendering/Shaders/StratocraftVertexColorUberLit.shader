Shader "Stratocraft/VertexColorUberLit"
{
    Properties
    {
        _SmoothFacetBlend ("Smooth vs Facet Normal Blend", Range(0.0, 1.0)) = 0.45
        _AmbientStrength ("Hemisphere Ambient Strength", Range(0.1, 1.5)) = 0.52
        _RimLightIntensity ("Rim Light Intensity", Range(0.0, 1.0)) = 0.24
        _ThermalGlowIntensity ("Thermal T4 Glow Intensity", Range(0.0, 6.0)) = 2.6
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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _SmoothFacetBlend;
                float _AmbientStrength;
                float _RimLightIntensity;
                float _ThermalGlowIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float4 uv1        : TEXCOORD1; // x=MatIdNorm, y=Temperature, z=Sharpness, w=CavityAO
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 color      : COLOR;
                float4 metaUv1    : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

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
                output.metaUv1 = input.uv1;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Astroneer / Deep Rock Galactic stilize Low-Poly Shading:
                // Ekran uzayi turevlerinden (ddx/ddy) gercek ucgen faset normalini hesapla ve yumusak vertex normaliyle harmanla
                float3 dpdx = ddx(input.positionWS);
                float3 dpdy = ddy(input.positionWS);
                float3 geoNormalWS = normalize(cross(dpdy, dpdx));
                float3 vtxNormalWS = normalize(input.normalWS);
                if (dot(geoNormalWS, vtxNormalWS) < 0.0)
                {
                    geoNormalWS = -geoNormalWS;
                }
                float3 normalWS = normalize(lerp(geoNormalWS, vtxNormalWS, saturate(_SmoothFacetBlend)));
                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);

                // Yari-kuresel (Hemisphere Sky-Ground) yumusak ortam aydinlatmasi + Gunesten gelen NdotL golgesi
                float ndotl = dot(normalWS, mainLight.direction);
                float wrappedDiffuse = saturate(ndotl * 0.65 + 0.35);
                float directSun = saturate(ndotl) * mainLight.shadowAttenuation;

                float upFactor = normalWS.y * 0.5 + 0.5;
                float3 skyAmbient = float3(0.56, 0.66, 0.82);
                float3 groundAmbient = float3(0.24, 0.21, 0.19);
                float3 hemiAmbient = lerp(groundAmbient, skyAmbient, upFactor) * _AmbientStrength;

                // Yukseklik bazli hafif derinlik gradyani (alt kaya katmanlarini daha tok gosterir)
                float heightBoost = saturate((input.positionWS.y + 1.0) / 11.0);
                float depthShade = lerp(0.74, 1.06, heightBoost);

                float3 baseAlbedo = input.color.rgb * depthShade;
                float cavityAo = (input.metaUv1.w > 0.01) ? lerp(0.55, 1.0, saturate(input.metaUv1.w)) : 1.0;
                float sharpness = saturate(input.metaUv1.z);
                float heatNorm = saturate(input.metaUv1.y);

                // Spekuler parilti + Fresnel kenar aydinlatmasi (Low-Poly siluet vurgusu)
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float specPow = lerp(16.0, 64.0, sharpness);
                float spec = pow(saturate(dot(normalWS, halfDir)), specPow) * (0.12 + 0.38 * sharpness) * mainLight.shadowAttenuation;
                float rim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), 3.0) * _RimLightIntensity;

                float3 litColor = baseAlbedo * (hemiAmbient + mainLight.color * (0.28 * wrappedDiffuse + 0.82 * directSun));
                litColor = (litColor + mainLight.color * spec + rim * skyAmbient) * cavityAo;

                // Magma / Kor Ates T^4 Blackbody Emisyonu
                if (heatNorm > 0.05)
                {
                    float3 magmaEmission = lerp(float3(1.0, 0.28, 0.02), float3(1.0, 0.85, 0.25), heatNorm);
                    litColor = lerp(litColor, magmaEmission, saturate(heatNorm * 0.65))
                             + magmaEmission * (heatNorm * heatNorm * _ThermalGlowIntensity);
                }

                return half4(litColor, 1.0h);
            }
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
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }
    }
}
