Shader "Stratocraft/SmoothProceduralSky"
{
    // PT-09 & PT-10: 0.0 MB VRAM per-pixel Rayleigh + Mie gokyuzu shader'i.
    // - Unity'nin yerlesik Skybox/Procedural shader'indaki per-vertex kaba izgara / kare-kare banding artefaktlarini
    //   %100 per-pixel analitik sacilma + Interleaved Gradient Noise (IGN) ucgen dithering ile kokunden giderir.
    // - Biyom FogAndSkyTint + ColoredShadowTint + gece yildizlari + kuzey/hiclik aurora isiltisi destekler.
    Properties
    {
        _SunSize ("Sun Size", Range(0.005, 0.25)) = 0.042
        _SunSizeConvergence ("Sun Size Convergence", Range(1.0, 20.0)) = 5.0
        _AtmosphereThickness ("Atmosphere Thickness", Range(0.2, 3.0)) = 1.0
        _SkyTint ("Sky Tint", Color) = (0.50, 0.54, 0.62, 1.0)
        _GroundColor ("Ground Color", Color) = (0.28, 0.29, 0.32, 1.0)
        _Exposure ("Exposure", Range(0.02, 4.0)) = 1.2
        _HorizonTint ("Horizon Tint", Color) = (0.76, 0.84, 0.94, 1.0)
        _ZenithTint ("Zenith Tint", Color) = (0.20, 0.44, 0.82, 1.0)
        _BiomeSkyTint ("Biome Sky Tint", Color) = (0.54, 0.82, 0.92, 0.0)
        _NightFactor ("Night Factor", Range(0.0, 1.0)) = 0.0
        _CloudOvercast ("Cloud Overcast", Range(0.0, 1.0)) = 0.0
        _AuroraStrength ("Aurora Strength", Range(0.0, 1.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
            "RenderPipeline" = "UniversalPipeline"
        }
        Cull Off
        ZWrite Off

        Pass
        {
            Name "StratocraftSmoothSkyForward"
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex SkyVert
            #pragma fragment SkyFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _SunSize;
                float _SunSizeConvergence;
                float _AtmosphereThickness;
                float4 _SkyTint;
                float4 _GroundColor;
                float _Exposure;
                float4 _HorizonTint;
                float4 _ZenithTint;
                float4 _BiomeSkyTint;
                float _NightFactor;
                float _CloudOvercast;
                float _AuroraStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 viewDirWS  : TEXCOORD0;
            };

            // Jimenez (2014) Interleaved Gradient Noise — 8-bit gokyuzu renk basamaklanmasini (banding) sifirlar
            float InterleavedGradientNoise(float2 screenPx)
            {
                float3 magic = float3(0.06711056, 0.00583715, 52.9829189);
                return frac(magic.z * frac(dot(screenPx, magic.xy)));
            }

            float Hash31Sky(float3 p)
            {
                p = frac(p * float3(443.8975, 397.2973, 491.1871));
                p += dot(p, p.yxz + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            Varyings SkyVert(Attributes input)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                o.viewDirWS = input.positionOS.xyz;
                return o;
            }

            half4 SkyFrag(Varyings input) : SV_Target
            {
                float3 dir = normalize(input.viewDirWS);
                Light mainLight = GetMainLight();
                float3 sunDir = normalize(mainLight.direction);
                float3 lightCol = mainLight.color;

                float up = dir.y;
                float absUp = max(up, 0.0);

                // 1) Per-pixel puruzsuz Rayleigh gokyuzu kubbesi gradyani (0 vertex-mesh banding!)
                float zenithCurve = pow(absUp, lerp(0.52, 0.72, saturate(_AtmosphereThickness * 0.5)));
                float horizonGlow = pow(1.0 - absUp, 4.2);

                float3 zenithCol = _ZenithTint.rgb * (_SkyTint.rgb * 1.9);
                float3 horizonCol = _HorizonTint.rgb;

                // Biyom gokyuzu-sis harmani (PT-10)
                float biomeWeight = saturate(_BiomeSkyTint.a);
                horizonCol = lerp(horizonCol, lerp(horizonCol, _BiomeSkyTint.rgb, 0.52), biomeWeight);
                zenithCol = lerp(zenithCol, lerp(zenithCol, _BiomeSkyTint.rgb * 0.62, 0.28), biomeWeight);

                // Kapali/bulutlu havada kubbeyi yumusak gumus-griye harmanla
                float3 overcastCol = lerp(float3(0.42, 0.46, 0.52), float3(0.05, 0.06, 0.09), saturate(_NightFactor));
                zenithCol = lerp(zenithCol, overcastCol * 0.88, saturate(_CloudOvercast * 0.65));
                horizonCol = lerp(horizonCol, overcastCol * 1.05, saturate(_CloudOvercast * 0.65));

                float3 skyCol = lerp(horizonCol, zenithCol, zenithCurve);

                // 2) Mie gunes/ay halesi ve ufuk kizilligi (tamamen per-pixel analitik)
                float sunCos = dot(dir, sunDir);
                float twilightBand = saturate(1.0 - abs(sunDir.y) * 2.8);
                float mieWide = pow(saturate(sunCos * 0.5 + 0.5), 5.0) * (0.28 + 0.45 * twilightBand) * (1.0 - _CloudOvercast * 0.7);
                float mieTight = pow(saturate(sunCos), 48.0) * 0.45 * (1.0 - _CloudOvercast * 0.8);
                float3 sunHaloTint = lerp(lightCol, float3(1.0, 0.48, 0.24), twilightBand * 0.65);
                skyCol += sunHaloTint * (mieWide * (0.45 + 0.55 * horizonGlow) + mieTight) * saturate(1.0 - _NightFactor * 0.75);

                // 3) Gunes / Ay diski (puruzsuz anti-aliased kenar)
                float diskRadius = max(0.004, _SunSize * 0.45);
                float angDist = acos(clamp(sunCos, -1.0, 1.0));
                float diskEdge = max(0.002, diskRadius * (0.35 / max(1.0, _SunSizeConvergence * 0.2)));
                float diskMask = 1.0 - smoothstep(diskRadius - diskEdge, diskRadius + diskEdge, angDist);
                float diskVis = saturate(1.0 - _CloudOvercast * 0.85) * smoothstep(-0.04, 0.04, up + 0.03);
                float3 diskCol = lerp(lightCol * 2.8, float3(0.75, 0.84, 1.0) * 1.4, saturate(_NightFactor));
                skyCol += diskCol * diskMask * diskVis;

                // 4) Gece yildizlari ve kutup/hiclik aurora isiltisi
                float nightClear = saturate(_NightFactor) * saturate(1.0 - _CloudOvercast * 0.9) * smoothstep(0.04, 0.26, up);
                if (nightClear > 0.01)
                {
                    float3 starCell = floor(dir * 260.0);
                    float sh = Hash31Sky(starCell);
                    float starMask = smoothstep(0.9945, 0.9995, sh);
                    float twinkle = 0.72 + 0.28 * sin(_Time.y * 2.5 + sh * 120.0);
                    skyCol += float3(0.84, 0.90, 1.0) * (starMask * twinkle * nightClear * 1.15);

                    if (_AuroraStrength > 0.01)
                    {
                        float band = sin(dir.x * 7.5 + _Time.y * 0.22 + sin(dir.z * 5.2 - _Time.y * 0.14) * 1.4);
                        float curtain = smoothstep(0.15, 0.85, band * 0.5 + 0.5) * smoothstep(0.12, 0.48, up) * smoothstep(0.92, 0.45, up);
                        float3 auroraCol = lerp(float3(0.0, 0.96, 0.83), float3(0.92, 0.24, 0.88), saturate(dir.x * 0.5 + 0.5));
                        skyCol += auroraCol * (curtain * nightClear * _AuroraStrength * 0.42);
                    }
                }

                // 5) Ufuk altinda puruzsuz zemin gecisi
                float groundBlend = smoothstep(0.02, -0.14, up);
                skyCol = lerp(skyCol, _GroundColor.rgb, groundBlend);

                skyCol *= max(0.02, _Exposure);

                // 6) Ucgen (triangular) IGN dithering: 8-bit renk gecis basamaklarini (kare-kare banding) %100 yok eder!
                float n1 = InterleavedGradientNoise(input.positionCS.xy);
                float n2 = InterleavedGradientNoise(input.positionCS.xy + float2(17.0, 31.0));
                float triDither = (n1 + n2 - 1.0) * (0.75 / 255.0);
                skyCol = max(0.0, skyCol + triDither);

                return half4(skyCol, 1.0h);
            }
            ENDHLSL
        }
    }
    FallBack "Skybox/Procedural"
}
