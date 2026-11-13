Shader "Stratocraft/ProceduralClouds"
{
    Properties
    {
        _CloudBaseColor ("Cloud Illuminated Color", Color) = (0.96, 0.97, 0.99, 1.0)
        _CloudShadowColor ("Cloud Storm Underbelly Color", Color) = (0.24, 0.28, 0.35, 1.0)
        _CloudExtinctionSigma ("Beer-Lambert Extinction Sigma", Float) = 0.14
        _HenyeyForwardG ("Henyey-Greenstein Forward G", Range(0.0, 0.92)) = 0.68
        _HenyeyBackG ("Henyey-Greenstein Backscatter G", Range(-0.85, 0.0)) = -0.28
        _CloudNoiseScale ("3D Perlin-Worley Scale", Float) = 0.022
        _RainStreakColor ("Local Rain Streak Tint", Color) = (0.72, 0.82, 0.94, 0.55)
        _RainFallSpeed ("Rain Fall Speed (m/s)", Float) = 18.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+20"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ProceduralCloudsAndLocalRainForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex CloudRainVert
            #pragma fragment CloudRainFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CloudBaseColor;
                float4 _CloudShadowColor;
                float _CloudExtinctionSigma;
                float _HenyeyForwardG;
                float _HenyeyBackG;
                float _CloudNoiseScale;
                float4 _RainStreakColor;
                float _RainFallSpeed;
            CBUFFER_END

            // Kuresel Atmosfer Shader Degiskenleri (0 B Doku VRAM, Stage1AtmosphereCloudAndRainRenderer tarafindan surulur)
            // x = ortalama bulut sivi suyu (kg/m2), y = CloudBaseY (m), z = CloudTopY (m), w = aktiflik/golge siddeti (0..1)
            float4 _GlobalCloudGridParams;
            // x = WindVelX (m/s), y = WindSpeed (m/s), z = WindVelZ (m/s), w = Firtina Kararma Carpani (0..1)
            float4 _GlobalWindVector;
            // x = WestHumidity01, y = EastHumidity01, z = AirPressureHpa, w = Rayleigh Kizil Ufuk Siddeti (0..1)
            float4 _GlobalHorizonOpticsParams;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                // COLOR sozlesmesi:
                // R: < 0.5 ise 3B Bulut Hacmi, >= 0.5 ise Yerel Yagmur Perdesi/Cizgisi
                // G: Bulut yogunlugu (qc normalize 0..1) veya Yagis siddeti (mm/sa normalize 0..1)
                // B: Tavan kesme kotu (skyOcclusionHeightY / 255.0) veya firtina kararmasi (0..1)
                // A: Kenar yumusatma / opaklik agirligi (0..1)
                float4 color      : COLOR;
                float4 uv0        : TEXCOORD0; // x = CloudBaseY, y = CloudTopY, z = MinOccluderY, w = PrecipMmPerHour
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float4 vtxColor   : TEXCOORD3;
                float4 cellParams : TEXCOORD4;
                float  fogFactor  : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ==========================================================================
            // %100 GPU ALU 3D Hash-Gradient Perlin & Worley FBM (0.0 MB Doku VRAM)
            // ==========================================================================
            float Hash31(float3 p)
            {
                p = frac(p * float3(443.8975, 397.2973, 491.1871));
                p += dot(p, p.yxz + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            float3 Hash33(float3 p)
            {
                p = float3(
                    dot(p, float3(127.1, 311.7, 74.7)),
                    dot(p, float3(269.5, 183.3, 246.1)),
                    dot(p, float3(113.5, 271.9, 124.6)));
                return -1.0 + 2.0 * frac(sin(p) * 43758.5453123);
            }

            float GradientPerlin3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);

                float n000 = dot(Hash33(i + float3(0.0, 0.0, 0.0)), f - float3(0.0, 0.0, 0.0));
                float n100 = dot(Hash33(i + float3(1.0, 0.0, 0.0)), f - float3(1.0, 0.0, 0.0));
                float n010 = dot(Hash33(i + float3(0.0, 1.0, 0.0)), f - float3(0.0, 1.0, 0.0));
                float n110 = dot(Hash33(i + float3(1.0, 1.0, 0.0)), f - float3(1.0, 1.0, 0.0));
                float n001 = dot(Hash33(i + float3(0.0, 0.0, 1.0)), f - float3(0.0, 0.0, 1.0));
                float n101 = dot(Hash33(i + float3(1.0, 0.0, 1.0)), f - float3(1.0, 0.0, 1.0));
                float n011 = dot(Hash33(i + float3(0.0, 1.0, 1.0)), f - float3(0.0, 1.0, 1.0));
                float n111 = dot(Hash33(i + float3(1.0, 1.0, 1.0)), f - float3(1.0, 1.0, 1.0));

                float nx00 = lerp(n000, n100, u.x);
                float nx10 = lerp(n010, n110, u.x);
                float nx01 = lerp(n001, n101, u.x);
                float nx11 = lerp(n011, n111, u.x);

                float nxy0 = lerp(nx00, nx10, u.y);
                float nxy1 = lerp(nx01, nx11, u.y);

                return 0.5 + 0.5 * lerp(nxy0, nxy1, u.z);
            }

            float WorleyCellular3D(float3 p)
            {
                float3 id = floor(p);
                float3 fd = frac(p);
                float minDistSq = 1.0;

                [unroll]
                for (int z = -1; z <= 1; z++)
                {
                    [unroll]
                    for (int y = -1; y <= 1; y++)
                    {
                        [unroll]
                        for (int x = -1; x <= 1; x++)
                        {
                            float3 offset = float3((float)x, (float)y, (float)z);
                            float3 cellSeed = id + offset;
                            float3 featurePt = offset + (0.5 + 0.45 * Hash33(cellSeed));
                            float3 diff = featurePt - fd;
                            minDistSq = min(minDistSq, dot(diff, diff));
                        }
                    }
                }
                return 1.0 - saturate(sqrt(minDistSq));
            }

            // 3-Oktav 3B Perlin-Worley FBM (Cumulus/Cumulonimbus karnabahar kabartisi)
            float SamplePerlinWorleyFbm3D(float3 worldPos, float3 windOffset)
            {
                float3 p = (worldPos - windOffset) * max(_CloudNoiseScale, 0.002);
                float p0 = GradientPerlin3D(p);
                float w0 = WorleyCellular3D(p * 1.35);
                float w1 = WorleyCellular3D(p * 2.75 + float3(13.7, 7.1, 29.3));
                float p2 = GradientPerlin3D(p * 5.20 - windOffset * 0.03);

                float worleyFbm = w0 * 0.62 + w1 * 0.38;
                // Inigo Quilez / Schneider (Horizon Zero Dawn) Perlin-Worley remap
                float baseCloud = saturate((p0 - (1.0 - worleyFbm) * 0.45) / max(0.15, worleyFbm));
                return saturate(baseCloud * 0.78 + p2 * 0.22);
            }

            // ==========================================================================
            // Beer-Lambert Isik Sogurumu + Powdered-Sugar & Henyey-Greenstein Sacilim
            // ==========================================================================
            float BeerLambertPowderEnergy(float opticalDepth, float cosTheta)
            {
                float beer = exp(-opticalDepth);
                float powder = 1.0 - exp(-opticalDepth * 2.0);
                return lerp(beer, 2.0 * beer * powder, saturate(0.5 + 0.5 * (-cosTheta)));
            }

            float HenyeyGreensteinLobe(float cosTheta, float g)
            {
                float g2 = g * g;
                float denom = max(1e-4, pow(1.0 + g2 - 2.0 * g * cosTheta, 1.5));
                return (1.0 - g2) / (4.0 * 3.14159265 * denom);
            }

            float DualLobeHenyeyGreensteinPhase(float cosTheta, float forwardG, float backG)
            {
                float fwd = HenyeyGreensteinLobe(cosTheta, forwardG);
                float bck = HenyeyGreensteinLobe(cosTheta, backG);
                return lerp(bck, fwd, 0.72);
            }

            // ==========================================================================
            // Rayleigh (lambda^-4) + Mie Kizil Ufuk Optigi (Denizci Kizil Gokyuzu Kurali)
            // ==========================================================================
            float3 EvaluateRayleighMieHorizonTint(float3 sunDir, float3 baseLightCol)
            {
                // Gunes ufka yaklastiginda (sunDir.y -> 0) optik hava kutlesi (air mass) uzar
                float horizonFactor = saturate(1.0 - abs(sunDir.y) * 2.65);
                horizonFactor *= horizonFactor;

                float westHumidity = saturate(_GlobalHorizonOpticsParams.x);
                float eastHumidity = saturate(_GlobalHorizonOpticsParams.y);
                float pressureHpa  = (_GlobalHorizonOpticsParams.z > 100.0) ? _GlobalHorizonOpticsParams.z : 1013.25;
                float explicitWeight = saturate(_GlobalHorizonOpticsParams.w);

                // Aksam (gunes batida, sunDir.x < 0): bati kuruysa parlak sicak mercan/kizil ("Aksam kizilligi gune iyilik")
                // Sabah (gunes doguda, sunDir.x >= 0): dogu/bati nemli ve alcak basinc varsa koyu kan-kizili firtina uyarisi
                bool isSunrise = (sunDir.x >= 0.0);
                float dryWestGoodWeather = saturate(1.0 - westHumidity * 0.85);
                float stormMorningWarning = saturate(eastHumidity * 0.65 + (1015.0 - pressureHpa) * 0.025);

                float3 eveningWarmCoral = float3(1.00, 0.44, 0.20);
                float3 morningStormCrimson = float3(0.96, 0.24, 0.16);
                float3 horizonCrimson = isSunrise
                    ? lerp(eveningWarmCoral, morningStormCrimson, stormMorningWarning)
                    : lerp(float3(0.58, 0.60, 0.66), eveningWarmCoral, dryWestGoodWeather);

                float blendWeight = saturate(max(horizonFactor * 0.85, explicitWeight));
                return lerp(baseLightCol, horizonCrimson, blendWeight);
            }

            Varyings CloudRainVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 nrmWS = TransformObjectToWorldNormal(input.normalOS);

                output.positionWS = posWS;
                output.normalWS = nrmWS;
                output.positionOS = input.positionOS.xyz;
                output.vtxColor = input.color;
                output.cellParams = input.uv0;
                output.positionCS = TransformWorldToHClip(posWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            half4 CloudRainFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 viewDirWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light mainLight = GetMainLight();
                float3 sunDir = normalize(mainLight.direction);
                float3 windVec = float3(_GlobalWindVector.x, 0.0, _GlobalWindVector.z);
                float3 windDrift = windVec * _Time.y;

                // ======================================================================
                // MOD 2: YEREL YAGMUR PERDESI VE DUSEN YAGMUR CIZGILERI (COLOR.r >= 0.5)
                // Yalnizca yagisli 64m x 64m bulut hucresinin izdusumu altinda ve
                // cati/magara tavan kotunun (minOccluderY) ustunde cizilir!
                // ======================================================================
                if (input.vtxColor.r >= 0.5)
                {
                    float minOccluderY = (input.cellParams.z > 0.001)
                        ? input.cellParams.z
                        : (input.vtxColor.b * 255.0);
                    // Cati veya magara tavani altindaysa yagmuru kesinlikle kirp (AK-4 & AK-8)
                    if (input.positionWS.y < minOccluderY)
                    {
                        discard;
                    }

                    float precipIntensity01 = saturate(max(input.vtxColor.g, input.cellParams.w * 0.08));
                    if (precipIntensity01 <= 0.005)
                    {
                        discard;
                    }

                    // Ruzgarla egilen prosedurel 3B yagmur cizgisi (streak) + uzaktan gorunen yagmur safti
                    float3 tiltedPos = input.positionWS;
                    tiltedPos.xz -= windVec.xz * (input.positionWS.y * 0.035);

                    float fallPhase = tiltedPos.y * 0.42 + _Time.y * max(_RainFallSpeed, 4.0);
                    float3 streakCell = float3(tiltedPos.x * 3.8, fallPhase * 0.18, tiltedPos.z * 3.8);
                    float streakHash = Hash31(floor(streakCell));
                    float2 cellFract = frac(streakCell.xz) - 0.5;
                    float radialDist = length(cellFract);

                    float streakCore = smoothstep(0.24, 0.03, radialDist) * step(0.42, streakHash);
                    float verticalPulse = smoothstep(0.15, 0.85, frac(fallPhase + streakHash * 6.1));
                    float streakMask = streakCore * verticalPulse;

                    // Uzaktan gorunen yagmur perdesi (rain shaft) yogunlugu
                    float camDist = distance(_WorldSpaceCameraPos, input.positionWS);
                    float distantShaft = saturate(camDist / 110.0) * 0.38;
                    float edgeFade = saturate(1.0 - dot(input.positionOS.xz, input.positionOS.xz) * 3.2);
                    float verticalFade = smoothstep(0.0, 0.12, input.positionOS.y + 0.5) *
                                         smoothstep(1.0, 0.82, input.positionOS.y + 0.5);

                    float3 horizonTint = EvaluateRayleighMieHorizonTint(sunDir, mainLight.color);
                    float3 rainRgb = _RainStreakColor.rgb * lerp(float3(0.55, 0.62, 0.72), horizonTint, 0.45);
                    float rainAlpha = saturate((streakMask * 0.65 + distantShaft + 0.14) * precipIntensity01 * edgeFade * verticalFade * _RainStreakColor.a);

                    return half4(MixFog(rainRgb, input.fogFactor), (half)rainAlpha);
                }

                // ======================================================================
                // MOD 1: FIZIKSEL 3B DUNYA-UZAYI BULUT HACMI (COLOR.r < 0.5)
                // ======================================================================
                float cloudWater01 = saturate(max(input.vtxColor.g, _GlobalCloudGridParams.x * 0.35));
                float stormDarkening01 = saturate(max(input.vtxColor.b, _GlobalWindVector.w));

                // 4-Adimli (4-Tap) 3B Dunya-Uzayi Hacimsel Ilerleme (Ray-Marching ALU)
                float3 rayDir = -viewDirWS;
                float stepLen = 3.5;
                float accumDensity = 0.0;
                float sunOpticalDepth = 0.0;

                [unroll]
                for (int s = 0; s < 4; s++)
                {
                    float3 sampleWS = input.positionWS + rayDir * ((float)s * stepLen);
                    float fbm = SamplePerlinWorleyFbm3D(sampleWS, windDrift);

                    // Yukseklik profili: yassilasmis cumulus tabani + yuvarlak karnabahar tepe (anvil/dome)
                    float localY01 = saturate(input.positionOS.y + 0.5);
                    float heightShape = smoothstep(0.0, 0.18, localY01) * smoothstep(1.0, 0.62, localY01);
                    float radialBoxFade = saturate(1.0 - max(abs(input.positionOS.x), abs(input.positionOS.z)) * 1.85);

                    float sampleDensity = saturate((fbm - lerp(0.48, 0.18, cloudWater01)) * 2.15) * heightShape * radialBoxFade;
                    accumDensity += sampleDensity * 0.35;

                    // Gunes yonunde Beer-Lambert optik derinlik orneklemesi
                    float3 sunSampleWS = sampleWS + sunDir * 6.0;
                    float sunFbm = SamplePerlinWorleyFbm3D(sunSampleWS, windDrift);
                    sunOpticalDepth += max(0.0, sunFbm * cloudWater01) * max(_CloudExtinctionSigma, 0.02) * 4.5;
                }

                float finalAlpha = saturate(accumDensity * input.vtxColor.a * lerp(0.55, 0.98, cloudWater01));
                if (finalAlpha <= 0.01)
                {
                    discard;
                }

                float cosTheta = dot(rayDir, sunDir);
                float hgPhase = DualLobeHenyeyGreensteinPhase(cosTheta, _HenyeyForwardG, _HenyeyBackG);
                float lightEnergy = BeerLambertPowderEnergy(sunOpticalDepth, cosTheta) * (0.65 + 0.95 * hgPhase);

                float3 skyHorizonLight = EvaluateRayleighMieHorizonTint(sunDir, mainLight.color);
                float3 illuminatedCol = _CloudBaseColor.rgb * skyHorizonLight;
                // Firtina bulutu (Cumulonimbus) yogunlastikca tabani koyu kursuni-lacivert olur
                float3 darkUnderbelly = lerp(_CloudShadowColor.rgb, float3(0.11, 0.13, 0.18), stormDarkening01);

                float heightAmbient01 = saturate(input.positionOS.y + 0.5);
                float3 cloudRgb = lerp(darkUnderbelly, illuminatedCol, saturate(lightEnergy * 0.78 + heightAmbient01 * 0.28));

                return half4(MixFog(cloudRgb, input.fogFactor), (half)finalAlpha);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
