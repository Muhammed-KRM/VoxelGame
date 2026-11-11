Shader "Stratocraft/FluidSurface"
{
    Properties
    {
        _WaterColorShallow ("Water Shallow Color", Color) = (0.22, 0.72, 0.96, 0.72)
        _WaterColorDeep ("Water Deep (Abyssal) Color", Color) = (0.10, 0.42, 0.78, 0.88)
        _WaterExtinction ("Water Extinction (RGB Sigma)", Vector) = (0.22, 0.08, 0.03, 0.0)
        _WaterWaveSpeed ("Wave Speed", Float) = 1.2
        _WaterWaveFrequency ("Wave Frequency", Float) = 0.8
        _WaterWaveAmplitude ("Wave Amplitude", Float) = 0.04
        _ShorelineFoamDistance ("Foam Band Distance (Meters)", Float) = 0.32
        
        _LavaCrustColor ("Lava Basaltic Crust Color", Color) = (0.32, 0.14, 0.08, 1.0)
        _LavaCoreColor ("Lava Core Glow Color", Color) = (1.0, 0.42, 0.10, 1.0)
        _LavaEmissionMultiplier ("Lava T^4 Emission Multiplier", Float) = 4.5
        _LavaCrustSpeed ("Lava Flow Speed", Float) = 0.25
        
        _AcidColorShallow ("Acid Shallow Color", Color) = (0.28, 0.88, 0.20, 0.75)
        _AcidColorDeep ("Acid Deep Color", Color) = (0.14, 0.56, 0.10, 0.92)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-10"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 300

        Pass
        {
            Name "FluidForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex FluidVert
            #pragma fragment FluidFrag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS
            #pragma multi_compile _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _WaterColorShallow;
                float4 _WaterColorDeep;
                float4 _WaterExtinction;
                float _WaterWaveSpeed;
                float _WaterWaveFrequency;
                float _WaterWaveAmplitude;
                float _ShorelineFoamDistance;
                
                float4 _LavaCrustColor;
                float4 _LavaCoreColor;
                float _LavaEmissionMultiplier;
                float _LavaCrustSpeed;

                float4 _AcidColorShallow;
                float4 _AcidColorDeep;
            CBUFFER_END

            // Global Oyuncu-Su Etkilesimi (SRP Batcher uyumlu, 0 B doku VRAM):
            // xyz = yuzucu dunya konumu, w = etkilesim siddeti (0..1)
            float4 _StratocraftSwimmerPos;
            // xyz = yuzucu hiz vektoru (m/s), w = suya giris sicrama darbesi (0..1)
            float4 _StratocraftSwimmerVel;

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 color        : COLOR;     // R=FluidType (1=Water, 2=Lava, 3=Acid), G=FluidLevel (0..255), B=FluxX, A=FluxZ
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float4 screenPos    : TEXCOORD2;
                nointerpolation uint fluidType : TEXCOORD3;
                float2 fluxVelocity : TEXCOORD4;
                float fogFactor     : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings FluidVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                
                // Color.r: 1/12=Water, 2/13=Lava, 3/15=Acid
                uint rawId = (uint)round(input.color.r * 255.0);
                uint fType = 1;
                if (rawId == 2 || rawId == 13)
                {
                    fType = 2; // Lava
                }
                else if (rawId == 3 || rawId == 15)
                {
                    fType = 3; // Acid
                }
                else
                {
                    fType = 1; // Water
                }
                output.fluidType = fType;

                float2 flux = float2(input.color.b * 2.0 - 1.0, input.color.a * 2.0 - 1.0);
                output.fluxVelocity = flux;

                // Su ve Asit icin prosedurel Gerstner dalgasi + Oyuncu Yuzme Dairesel Dalga Halkasi
                float3 nrmWS = TransformObjectToWorldNormal(input.normalOS);
                if (fType == 1 || fType == 3)
                {
                    float time = _Time.y * _WaterWaveSpeed;
                    float wave = sin((posWS.x + flux.x) * _WaterWaveFrequency + time) *
                                 cos((posWS.z + flux.y) * _WaterWaveFrequency * 1.3 + time * 0.8);
                    float topMask = saturate(nrmWS.y * 1.5);
                    posWS.y += wave * _WaterWaveAmplitude * topMask;

                    float swimWeight = saturate(_StratocraftSwimmerPos.w);
                    if (swimWeight > 0.01 && topMask > 0.25 && abs(posWS.y - _StratocraftSwimmerPos.y) < 2.4)
                    {
                        float2 toVert = posWS.xz - _StratocraftSwimmerPos.xz;
                        float dist = length(toVert);
                        float splashPulse = saturate(_StratocraftSwimmerVel.w);
                        float rippleAmp = (0.042 * swimWeight + 0.068 * splashPulse) * exp(-dist * 0.58);
                        float phase = dist * 7.8 - _Time.y * 9.6;
                        float ripple = sin(phase) * rippleAmp;
                        posWS.y += ripple * topMask;

                        if (dist > 0.04)
                        {
                            float dRippleDr = cos(phase) * 7.8 * rippleAmp;
                            float2 radialDir = toVert / dist;
                            nrmWS = normalize(nrmWS + float3(-radialDir.x * dRippleDr, 0.0, -radialDir.y * dRippleDr) * 0.55);
                        }
                    }
                }

                output.positionWS = posWS;
                output.normalWS = nrmWS;
                output.positionCS = TransformWorldToHClip(posWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            // Prosedurel 2D Voronoi / Gosterge Gurultusu (Sifir doku VRAM)
            float Hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float SmoothValueNoise2D(float2 uv)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            float Voronoi2D(float2 uv)
            {
                float2 g = floor(uv);
                float2 f = frac(uv);
                float minDist = 1.0;
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 lattice = float2(x, y);
                        float2 offset = float2(Hash21(g + lattice), Hash21(g + lattice + 15.3));
                        float d = distance(lattice + offset, f);
                        minDist = min(minDist, d);
                    }
                }
                return minDist;
            }

            float4 FluidFrag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(isFrontFace ? input.normalWS : -input.normalWS);
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);
                float NdotV = max(0.0, dot(normalWS, viewDirWS));

                // Derinlik tamponundan optik yol uzunlugu (d_optic)
                // SceneView veya derinlik dokusu aktif olmayan kameralarda rawDepth 0 (uzak duzlem) dondugunde
                // opticalDepth'in 4300m olup suyu simsiyah (0,0,0) yapmasini engellemek icin 1.4m varsayilana sabitle!
                float2 screenUV = input.screenPos.xy / max(1e-5, input.screenPos.w);
                #if UNITY_REVERSED_Z
                float rawDepth = SampleSceneDepth(screenUV);
                bool validDepth = (rawDepth > 1e-4) && (rawDepth < 0.9999);
                #else
                float rawSample = SampleSceneDepth(screenUV);
                bool validDepth = (rawSample > 1e-4) && (rawSample < 0.9999);
                float rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawSample);
                #endif
                float sceneDepthWS = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceDepthWS = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float rawDiff = sceneDepthWS - surfaceDepthWS;
                float opticalDepth = (validDepth && rawDiff >= 0.0 && rawDiff < 24.0) ? rawDiff : 1.4;

                // Ana Isik
                Light mainLight = GetMainLight();
                float NdotL = max(0.0, dot(normalWS, mainLight.direction));
                float3 lightCol = max(float3(0.45, 0.48, 0.52), mainLight.color * (NdotL * 0.75 + 0.35));

                // ==================== 1. SU (FluidType == 1) ====================
                if (input.fluidType == 1)
                {
                    if (!isFrontFace)
                    {
                        // Su icinden yukari bakis: Snell penceresi (dik acida aydinlik turkuaz, yatay acida tam ic yansima laciverti)
                        float snellWindow = smoothstep(0.42, 0.78, NdotV);
                        float causticWave = SmoothValueNoise2D(input.positionWS.xz * 2.1 + _Time.y * 0.45);
                        float3 tirDeep = _WaterColorDeep.rgb * lightCol * 0.92;
                        float3 windowBright = lerp(_WaterColorShallow.rgb, float3(0.68, 0.92, 1.0), 0.45 + 0.20 * causticWave) * lightCol * 1.15;
                        float3 underColor = lerp(tirDeep, windowBright, snellWindow);
                        float underAlpha = saturate(lerp(0.84, 0.58, snellWindow));
                        return float4(MixFog(underColor, input.fogFactor), underAlpha);
                    }

                    // Beer-Lambert Derinlik Sogurmasi + 3B Prizma Yan Duvar Kontrasti
                    float sideWallMask = 1.0 - saturate(abs(normalWS.y) * 1.35);
                    float3 extinction = _WaterExtinction.rgb;
                    float3 absorb = exp(-extinction * clamp(opticalDepth + sideWallMask * 0.85, 0.18, 6.0));
                    float3 waterColor = lerp(_WaterColorDeep.rgb, _WaterColorShallow.rgb, absorb);
                    waterColor = lerp(waterColor, _WaterColorDeep.rgb * 1.15, sideWallMask * 0.38);

                    // Schlick Fresnel + Ust-Yan Kenar Meniskus Okunurlugu
                    float fresnel = 0.08 + 0.92 * pow(1.0 - NdotV, 3.6);
                    float3 skyReflection = float3(0.42, 0.70, 0.96) * lightCol * 1.16;
                    float3 finalColor = lerp(waterColor * lightCol, skyReflection, fresnel * (0.52 - sideWallMask * 0.22));

                    // Shoreline Foam Band + Oyuncu Yuzme Calkanti Halkasi ve V-Iz Kopugu
                    float shoreDist = max(0.05, _ShorelineFoamDistance);
                    float foamFactor = smoothstep(shoreDist, 0.0, opticalDepth);
                    float foamWave = SmoothValueNoise2D(input.positionWS.xz * 2.4 + _Time.y * 0.35);
                    float foam = smoothstep(0.56, 0.82, foamFactor * (0.65 + 0.35 * foamWave));

                    float swimWeight = saturate(_StratocraftSwimmerPos.w);
                    if (swimWeight > 0.01 && abs(input.positionWS.y - _StratocraftSwimmerPos.y) < 2.2)
                    {
                        float2 relXZ = input.positionWS.xz - _StratocraftSwimmerPos.xz;
                        float r = length(relXZ);
                        float splashPulse = saturate(_StratocraftSwimmerVel.w);
                        float ringWave = 0.5 + 0.5 * sin(r * 9.0 - _Time.y * 10.5);
                        float churnNoise = SmoothValueNoise2D(input.positionWS.xz * 5.2 - _Time.yy * 1.4);
                        float bodyRing = smoothstep(1.35 + splashPulse * 0.65, 0.18, r) * (0.45 + 0.55 * ringWave * churnNoise);

                        float2 velXZ = _StratocraftSwimmerVel.xz;
                        float speedXZ = length(velXZ);
                        float wakeFoam = 0.0;
                        if (speedXZ > 0.25)
                        {
                            float2 backDir = -velXZ / speedXZ;
                            float2 sideDir = float2(-backDir.y, backDir.x);
                            float alongBack = dot(relXZ, backDir);
                            float acrossSide = abs(dot(relXZ, sideDir));
                            if (alongBack > 0.0 && alongBack < 3.6)
                            {
                                float wakeHalfWidth = 0.22 + alongBack * 0.34;
                                float vEdge = 1.0 - smoothstep(wakeHalfWidth * 0.55, wakeHalfWidth, acrossSide);
                                float wakeFade = (1.0 - alongBack / 3.6) * saturate(speedXZ * 0.28);
                                wakeFoam = vEdge * wakeFade * (0.55 + 0.45 * churnNoise);
                            }
                        }

                        float playerFoam = saturate((bodyRing * (0.65 + 0.55 * splashPulse) + wakeFoam) * swimWeight);
                        foam = max(foam, playerFoam);
                    }

                    finalColor = lerp(finalColor, float3(0.92, 0.97, 1.0) * lightCol * 1.15, foam * 0.54);

                    float shoreFeather = validDepth ? smoothstep(0.0, 0.04, opticalDepth) : 1.0;
                    float baseAlpha = saturate(max(0.74, _WaterColorShallow.a) + (clamp(opticalDepth, 0.0, 4.0) * 0.06) + (sideWallMask * 0.08) + (foam * 0.12));
                    float alpha = baseAlpha * lerp(0.76, 1.0, shoreFeather);
                    return float4(MixFog(finalColor, input.fogFactor), alpha);
                }

                // ==================== 2. LAV (FluidType == 2) ====================
                if (input.fluidType == 2)
                {
                    // 3B dunya koordinatli Voronoi bazaltik kabuk ve catlaklar (dikey yuzeylerde cizgi uzamasi yapmaz)
                    float2 lavaUV = (input.positionWS.xz + float2(input.positionWS.y * 0.55, -input.positionWS.y * 0.45)) * 0.35
                                  + input.fluxVelocity * (_Time.y * _LavaCrustSpeed);
                    float vDist = Voronoi2D(lavaUV);
                    float crackMask = 1.0 - smoothstep(0.06, 0.34, vDist);

                    // Stefan-Boltzmann T^4 akkor turuncu-sari emisyon
                    float3 lavaGlow = _LavaCoreColor.rgb * _LavaEmissionMultiplier;
                    float3 crust = max(float3(0.28, 0.11, 0.06), _LavaCrustColor.rgb * lightCol);
                    float3 finalColor = lerp(crust, lavaGlow, crackMask);

                    return float4(MixFog(finalColor, input.fogFactor), 1.0);
                }

                // ==================== 3. ASIT (FluidType == 3) ====================
                float3 acidAbsorb = exp(-float3(0.12, 0.04, 0.35) * opticalDepth);
                float3 acidColor = lerp(_AcidColorDeep.rgb, _AcidColorShallow.rgb, acidAbsorb);
                float acidFresnel = 0.04 + 0.96 * pow(1.0 - NdotV, 4.0);
                float3 finalAcid = lerp(acidColor * lightCol, lightCol * 1.1, acidFresnel * 0.5);

                return float4(MixFog(finalAcid, input.fogFactor), 0.85);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
