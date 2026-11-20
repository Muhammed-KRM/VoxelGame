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
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
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

            // Paket 4B: GPU Alt-Tik Enterpolasyonu ([0.0 .. 1.0]), Beaufort Ruzgar ve Teshis Gorunum Modu (0=Normal, 1..6=Debug)
            float _FluidSubTickAlpha;
            float _GlobalWindBeaufort;
            float _FluidDebugView;

            // Global Oyuncu-Su Etkilesimi (SRP Batcher uyumlu, 0 B doku VRAM):
            float4 _StratocraftSwimmerPos;
            float4 _StratocraftSwimmerVel;
            float _StratocraftCameraSubmerged;

            // Kuresel Ruzgar Vektoru, Bulut Golgesi ve Tuzluluk Parametreleri
            float4 _GlobalWindVector;
            float4 _GlobalCloudGridParams;
            float4 _GlobalWaterSalinityParams;

            struct Attributes
            {
                float4 positionOS   : POSITION;
                float3 normalOS     : NORMAL;
                float4 color        : COLOR;     // R=FluidId(1,12,2,3), G=SubTickDy(128=0), B=VelX(128=0), A=VelZ(128=0)
                float2 auxUv0       : TEXCOORD0; // x=FluidDepthMeters, y=WaterfallFoam01
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS   : SV_POSITION;
                float3 positionWS   : TEXCOORD0;
                float3 normalWS     : TEXCOORD1;
                float4 screenPos    : TEXCOORD2;
                nointerpolation uint fluidType : TEXCOORD3;
                float4 flowAndFoam  : TEXCOORD4; // xy=VelXZ (m/s), z=Depth (m), w=WaterfallFoam01
                float fogFactor     : TEXCOORD5;
                float2 salinityAndDy : TEXCOORD6; // x=salinity01, y=subTickDy (m)
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ==================== 0.0 MB Doku VRAM Prosedurel Gurultu & Vlachos Dual-Phase ====================

            float Hash21(float2 p)
            {
                p = frac(p * float2(234.34, 435.345));
                p += dot(p, p + 34.23);
                return frac(p.x * p.y);
            }

            float2 Hash22Fluid(float2 p)
            {
                float3 p3 = frac(float3(p.xyx) * float3(0.1031, 0.1030, 0.0973));
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.xx + p3.yz) * p3.zy);
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

            // Cikti: x = yukseklik [-1..1], yz = analitik turev (dh/dx, dh/dz)
            float3 ProceduralRippleHeightAndGrad(float2 uv, float timeSec)
            {
                float2 i = floor(uv);
                float2 f = frac(uv);
                float2 u = f * f * (3.0 - 2.0 * f);
                float2 du = 6.0 * f * (1.0 - f);

                float cs = cos(timeSec);
                float sn = sin(timeSec);
                float2 rot = float2(cs, sn);

                float a = dot(Hash22Fluid(i + float2(0.0, 0.0)) - 0.5, rot);
                float b = dot(Hash22Fluid(i + float2(1.0, 0.0)) - 0.5, rot);
                float c = dot(Hash22Fluid(i + float2(0.0, 1.0)) - 0.5, rot);
                float d = dot(Hash22Fluid(i + float2(1.0, 1.0)) - 0.5, rot);

                float k0 = a;
                float k1 = b - a;
                float k2 = c - a;
                float k3 = a - b - c + d;

                float val = k0 + k1 * u.x + k2 * u.y + k3 * u.x * u.y;
                float2 grad = du * (float2(k1, k2) + k3 * u.yx);
                return float3(val, grad);
            }

            // Alex Vlachos (SIGGRAPH 2010) Cift-Fazli Prosedurel Akis Adveksiyonu (flowPeriod = 1.60 s, 0.0 MB VRAM)
            // Not: floor(worldXZ * 0.25) kare voksel sinirlarinda kesikli sicrama urettigi icin surekli sinüs fazi kullanilir!
            float3 SampleDualPhaseAdvectedRipples(float2 worldXZ, float2 flowVelXZ, float freqScale, float timeSec)
            {
                float flowPeriod = 1.60;
                float spatialJitter = (sin(worldXZ.x * 0.37 + worldXZ.y * 0.29) * 0.5 + 0.5) * 0.16;

                float phase0 = frac((timeSec / flowPeriod) + spatialJitter);
                float phase1 = frac((timeSec / flowPeriod) + 0.50 + spatialJitter);
                // Kanonik Alex Vlachos (SIGGRAPH 2010) ucgen dalga agirligi:
                // phase0 = 0/1 (p0 geri sararken) -> blend1 = 1.0 (yalnizca r1 gorunur, %0 ziplama!);
                // phase0 = 0.5 (p1 geri sararken) -> blend1 = 0.0 (yalnizca r0 gorunur, %0 ziplama!).
                float blend1 = abs(1.0 - 2.0 * phase0);

                float2 p0 = (worldXZ - flowVelXZ * (phase0 * flowPeriod)) * freqScale;
                float2 p1 = (worldXZ - flowVelXZ * (phase1 * flowPeriod)) * freqScale;

                float3 r0 = ProceduralRippleHeightAndGrad(p0, timeSec * 1.35);
                float3 r1 = ProceduralRippleHeightAndGrad(p1, timeSec * 1.35 + 1.57);
                return lerp(r0, r1, blend1);
            }

            // ==================== Vertex Shader ====================

            Varyings FluidVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                // Color.r: 1=FreshWater, 12=SaltWater/SeaWater, 2/13=Lava, 3/15/19=Acid
                uint rawId = (uint)round(input.color.r * 255.0);
                uint fType = 1u;
                if (rawId == 2u || rawId == 13u)
                {
                    fType = 2u; // Lava
                }
                else if (rawId == 3u || rawId == 15u || rawId == 19u)
                {
                    fType = 3u; // Acid
                }
                else
                {
                    fType = 1u; // Water (Tatli veya Tuzlu Su)
                }
                output.fluidType = fType;

                // #L135 Tamiri: rawId == 1u kesinlikle Tatli Su (0.0), rawId == 12u Tuzlu Deniz Suyu (1.0)
                float isSaltVertex = (rawId == 12u || rawId == 4u) ? 1.0 : 0.0;
                float salinity01 = (rawId == 1u) ? 0.0 : saturate(max(isSaltVertex, _GlobalWaterSalinityParams.x));

                // 1) Alt-Tik Yukseklik Farki Dekodlamasi: COLOR.g == 128 -> dy = 0.0m
                float encodedDy = round(input.color.g * 255.0);
                float subTickDy = (encodedDy > 0.5) ? clamp((encodedDy - 128.0) / 127.0, -1.0, 1.0) : 0.0;
                output.salinityAndDy = float2(salinity01, subTickDy);

                // 2) Kose Yatay Hiz Vektoru Dekodlamasi: COLOR.ba == 128 -> v = 0.0 m/s
                float rawVx = round(input.color.b * 255.0);
                float rawVz = round(input.color.a * 255.0);
                float2 velXZ = float2(
                    (rawVx > 0.5) ? clamp((rawVx - 128.0) / 127.0, -1.0, 1.0) * 4.0 : 0.0,
                    (rawVz > 0.5) ? clamp((rawVz - 128.0) / 127.0, -1.0, 1.0) * 4.0 : 0.0);

                float depthMeters = max(0.0, input.auxUv0.x);
                float rawFoamParam = input.auxUv0.y;
                // Paket 4B.2 Deplasman Kilidi: auxUv0.y >= 2.0 ise bu tepe noktasi ust su yuzeyine kilitli bir
                // savak/kenar ust kosesidir; nrmWS dikeye yakin olsa bile ust yuzeyle birebir ayni dalga ve alt-tik
                // yukseklik deplasmanini alir (0.000m yirtilmasiz dikis!).
                float seamLockedTopVertex = step(1.95, rawFoamParam);
                float waterfallFoam01 = saturate(seamLockedTopVertex > 0.5 ? (rawFoamParam - 2.0) : rawFoamParam);

                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 nrmWS = normalize(TransformObjectToWorldNormal(input.normalOS));

                // 3) GPU Alt-Tik Kesintisiz Yukseklik Enterpolasyonu
                float alphaSub = saturate(_FluidSubTickAlpha);
                float upwardSurfaceMask = max(seamLockedTopVertex, saturate(nrmWS.y * 1.25));
                posWS.y -= (1.0 - alphaSub) * subTickDy * upwardSurfaceMask;

                // 4) WMO Beaufort 0..12 Derinlik-Sinirli Gerstner Dalga + Zaman-Carpanli Akis Adveksiyonu (#L149 Tamiri)
                if (fType == 1u || fType == 3u)
                {
                    float2 windXZ = _GlobalWindVector.xz;
                    float windSpeed = max(_GlobalWindVector.y, length(windXZ));
                    float windLenXZ = length(windXZ);
                    float2 windDir = (windLenXZ > 0.1) ? (windXZ / windLenXZ) : float2(1.0, 0.0);
                    float windAmpScale = 1.0 + clamp(windSpeed * 0.11, 0.0, 2.25);

                    float depthWaveGate = smoothstep(0.15, 2.40, depthMeters) * upwardSurfaceMask;
                    float beaufortAmp = clamp(0.012 + 0.00065 * windSpeed * windSpeed, 0.012, 0.14) * depthWaveGate;
                    float shallowAmp = (_WaterWaveAmplitude * windAmpScale) * upwardSurfaceMask * smoothstep(0.04, 0.40, depthMeters);
                    float waveAmp = min(0.16, max(shallowAmp, beaufortAmp));

                    float time = _Time.y * (_WaterWaveSpeed + clamp(windSpeed * 0.08, 0.0, 1.4));
                    // Devir #069 P1-3: Kose-basina degisen velXZ'yi sinirsiz _Time.y ile carpmak 2-3 dakika sonra
                    // komsu tepe noktalarinda rastgele ±16cm kirisik faz patlamasi uretir -> akis adveksiyonu fragment
                    // shaderdaki cift-fazli Vlachos fonksiyonunda (SampleDualPhaseAdvectedRipples) yapilir!
                    float2 advectedXZ = posWS.xz + windXZ * (_Time.y * 0.06);
                    float wave1 = sin(advectedXZ.x * _WaterWaveFrequency + time) *
                                  cos(advectedXZ.y * _WaterWaveFrequency * 1.3 + time * 0.8);
                    float wave2 = sin(dot(posWS.xz, windDir * 0.55) - (_Time.y * (1.6 + windSpeed * 0.12)));
                    posWS.y += (wave1 * 0.45 + wave2 * 0.55) * waveAmp;

                    // Oyuncu yuzme dairesel dalga halkasi (yalnizca gercek derinligi olan sularda; ince yagmurda 0!)
                    float topMask = saturate(nrmWS.y * 1.5);
                    float swimDepthGate = smoothstep(0.12, 0.45, depthMeters);
                    float swimWeight = saturate(_StratocraftSwimmerPos.w) * swimDepthGate;
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
                output.flowAndFoam = float4(velXZ, depthMeters, waterfallFoam01);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);

                return output;
            }

            // ==================== Fragment Shader ====================

            float4 FluidFrag(Varyings input, bool isFrontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Paket 4B.3 Tek-Yuzlu Hacim Korumasi:
                // Kamera su disindayken (_StratocraftCameraSubmerged < 0.05) arka yuzleri at (Cull Back esdegeri!) ->
                // 3B selale sutunlarinda ve zemin muhur eteklerinde cift-duvar parlamasi / ters mavi levha %0!
                // Kamera suya daldiginda yukari bakan su-hava ust yuzeyinde (normalWS.y >= 0.35) Snell gokyuzu penceresi acilir.
                if (!isFrontFace)
                {
                    if (input.fluidType != 1u || _StratocraftCameraSubmerged < 0.05 || input.normalWS.y < 0.35)
                    {
                        discard;
                    }
                }

                float3 geomNormalWS = normalize(isFrontFace ? input.normalWS : -input.normalWS);
                float3 rawFaceCross = cross(ddx(input.positionWS), ddy(input.positionWS));
                float3 faceNormalWS = dot(rawFaceCross, rawFaceCross) > 1e-10 ? normalize(rawFaceCross) : geomNormalWS;
                if (dot(faceNormalWS, geomNormalWS) < 0.0)
                {
                    faceNormalWS = -faceNormalWS;
                }
                // Devir #069 P1-4: Duz ucgen faset normali (ddx/ddy) yerine yumusak paylasilan kose normali kullan
                // (yalnizca gercek dikey selale duvarlarinda |faceNormalWS.y| < 0.25 iken yan duvar maskesi acilir -> 0 faset kirigi!)
                float smoothSideMask = 1.0 - smoothstep(0.28, 0.62, abs(geomNormalWS.y));
                float hardWallMask = 1.0 - smoothstep(0.18, 0.34, abs(faceNormalWS.y));
                float sideWallMask = lerp(smoothSideMask, hardWallMask, saturate(input.flowAndFoam.w * 4.0));
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);

                float2 flowVel = input.flowAndFoam.xy;
                float flowSpeed = length(flowVel);
                float depthMeters = input.flowAndFoam.z;
                float waterfallAttrFoam = input.flowAndFoam.w;
                float windSpeed = max(_GlobalWindVector.y, length(_GlobalWindVector.xz));

                // Derinlik tamponundan optik yol uzunlugu (d_optic)
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
                float opticalDepth = (validDepth && rawDiff >= -0.06 && rawDiff < 24.0) ? max(0.0, rawDiff) : max(0.35, depthMeters);

                // Gercek Arazi/Dag Golge Haritasi + Bulut Golgesi Zayiflatmasi (Faz 4B.4-D)
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float cloudShadowAtten = 1.0;
                if (_GlobalCloudGridParams.w > 0.001 && _GlobalCloudGridParams.x > 0.005 && mainLight.direction.y > -0.05)
                {
                    float cloudBaseY = (_GlobalCloudGridParams.y > 1.0) ? _GlobalCloudGridParams.y : 180.0;
                    float rayT = max(0.0, (cloudBaseY - input.positionWS.y) / max(0.08, mainLight.direction.y));
                    float2 cloudXZ = (input.positionWS.xz + mainLight.direction.xz * rayT - _GlobalWindVector.xz * _Time.y) * 0.025;
                    float cn = saturate(0.5 + 0.28 * sin(cloudXZ.x * 1.7 + cos(cloudXZ.y * 1.3)) + 0.22 * cos(cloudXZ.y * 2.9 - sin(cloudXZ.x * 2.1)));
                    float rawCloudAtten = clamp(exp(-_GlobalCloudGridParams.x * (0.48 + 0.32 * cn)), 0.32, 1.0);
                    cloudShadowAtten = lerp(1.0, rawCloudAtten, saturate(_GlobalCloudGridParams.w));
                }
                float totalShadowAtten = lerp(0.45, 1.0, mainLight.shadowAttenuation) * cloudShadowAtten;

                // Faz 4B.4-D: Yumusak Hareket Kapisi (hiz + alt-tik + yamac egimi -> egimli derede donuk/yamali alan 0!)
                float subTickDyFrag = input.salinityAndDy.y;
                float flowMotion01 = smoothstep(0.04, 0.28, flowSpeed);
                float dyMotion01 = smoothstep(0.025, 0.14, abs(subTickDyFrag));
                float slopeSin = saturate(length(geomNormalWS.xz));
                float slopeMotion01 = smoothstep(0.16, 0.36, slopeSin) * (1.0 - sideWallMask);
                float activeMotionGate = saturate(max(max(flowMotion01, dyMotion01), slopeMotion01 * 0.85));

                float slopeFlowMask = smoothstep(0.12, 0.38, slopeSin) * (1.0 - sideWallMask) * activeMotionGate;
                float2 downhillDirXZ = (slopeSin > 0.05) ? (geomNormalWS.xz / slopeSin) : float2(0.0, 0.0);
                float2 gravitySlopeVel = downhillDirXZ * (slopeFlowMask * lerp(0.95, 2.60, slopeSin));
                float2 activeStreamVel = (flowSpeed > 0.25) ? (flowVel + gravitySlopeVel * 0.50) : (flowVel + gravitySlopeVel);
                float activeFlowSpeed = length(activeStreamVel);

                float windFetchGate = smoothstep(0.10, 0.90, depthMeters);
                float2 effectiveFlow = activeStreamVel + _GlobalWindVector.xz * (0.015 + 0.045 * windFetchGate);
                float3 rippleCoarse = SampleDualPhaseAdvectedRipples(input.positionWS.xz, effectiveFlow, 1.65, _Time.y);
                float3 rippleFine = SampleDualPhaseAdvectedRipples(input.positionWS.xz + float2(7.3, 11.1), effectiveFlow * 1.15, 3.30, _Time.y * 1.25);
                float rippleHeight01 = saturate(0.5 + 0.5 * (0.60 * rippleCoarse.x + 0.40 * rippleFine.x));
                float2 analyticalGrad = rippleCoarse.yz * (1.65 * 0.60) + rippleFine.yz * (3.30 * 0.40);

                // Dikey 3B selale ve zemin muhur duvarlari icin asagi akan animasyonlu akis dalgalari
                // (Yalnizca gercek selalelerde waterfallAttrFoam > 0; durgun zemin muhur eteklerinde dikey dalga/cizgi 0!)
                float waterfallWallGate = saturate(waterfallAttrFoam * 5.0);
                float activeWallMask = sideWallMask * waterfallWallGate;
                float wallU = abs(faceNormalWS.x) > abs(faceNormalWS.z) ? input.positionWS.z : input.positionWS.x;
                float2 wallFlowUV = float2(wallU * 2.4, input.positionWS.y * 0.90 + _Time.y * 4.6);
                float3 wallRipple = ProceduralRippleHeightAndGrad(wallFlowUV, _Time.y * 2.2);

                // Faz 4B.4-D: faceNormalWS yerine yumusak paylasilan geomNormalWS -> ucgen faset kiriklari 0!
                float upwardMask = saturate(abs(geomNormalWS.y) * 1.25);
                float calmBaseBump = lerp(0.06, 0.16, windFetchGate);
                float bumpStrength = lerp(calmBaseBump, 0.46, saturate(activeFlowSpeed * 0.42 + slopeFlowMask * 0.55 + windSpeed * 0.025 * windFetchGate)) * upwardMask;
                float3 horizontalPerturb = float3(-analyticalGrad.x * bumpStrength, 0.0, -analyticalGrad.y * bumpStrength);
                float3 wallPerturb = float3(0.0, -wallRipple.z * 0.24 * activeWallMask, 0.0);
                float3 normalWS = normalize(geomNormalWS + horizontalPerturb + wallPerturb);

                float NdotV = max(0.0, dot(normalWS, viewDirWS));
                float NdotL = max(0.0, dot(normalWS, mainLight.direction)) * totalShadowAtten;
                float3 lightCol = max(float3(0.45, 0.48, 0.52), mainLight.color * (NdotL * 0.75 + 0.35));

                if (_FluidDebugView > 0.5)
                {
                    int dbgMode = (int)(_FluidDebugView + 0.5);
                    if (dbgMode == 1) return half4(1.0 - activeMotionGate, flowMotion01, dyMotion01, 1.0);
                    if (dbgMode == 2) return half4(saturate(abs(subTickDyFrag) * 4.0), saturate(geomNormalWS.y), waterfallAttrFoam, 1.0);
                    if (dbgMode == 3)
                    {
                        float nrmDisc = saturate((1.0 - dot(geomNormalWS, faceNormalWS)) * 4.0);
                        return half4(nrmDisc, saturate(geomNormalWS.y), saturate(length(geomNormalWS.xz)), 1.0);
                    }
                    if (dbgMode == 4) return half4(opticalDepth < 0.05 ? 1.0 : 0.0, saturate(opticalDepth * 0.5), saturate(depthMeters * 0.5), 1.0);
                    if (dbgMode == 5) return half4(sideWallMask, waterfallAttrFoam, saturate(length(flowVel) * 0.5), 1.0);
                    return half4(input.fluidType == 2u ? 1.0 : 0.0, input.fluidType == 3u ? 1.0 : (input.fluidType == 1u ? 0.8 : 0.0), input.fluidType == 1u ? 1.0 : 0.0, 1.0);
                }

                // ==================== 1. SU (FluidType == 1: Tatli Su vs Tuzlu Deniz Suyu) ====================
                if (input.fluidType == 1u)
                {
                    float salt01 = saturate(input.salinityAndDy.x);
                    // Tatli nehir/gol suyu berrak turkuaz-camgobegi; tuzlu deniz suyu daha derin zumrut-kobalt
                    float3 shallowTint = lerp(_WaterColorShallow.rgb, float3(0.15, 0.62, 0.86), salt01 * 0.45);
                    float3 deepTint = lerp(_WaterColorDeep.rgb, float3(0.05, 0.30, 0.66), salt01 * 0.48);

                    if (!isFrontFace)
                    {
                        float snellWindow = smoothstep(0.42, 0.78, NdotV);
                        float causticWave = SmoothValueNoise2D(input.positionWS.xz * 2.1 + _Time.y * 0.45);
                        float3 tirDeep = deepTint * lightCol * 0.92;
                        float3 windowBright = lerp(shallowTint, float3(0.68, 0.92, 1.0), 0.45 + 0.20 * causticWave) * lightCol * 1.15;
                        float3 underColor = lerp(tirDeep, windowBright, snellWindow);
                        float underAlpha = saturate(lerp(0.84, 0.58, snellWindow));
                        return float4(MixFog(underColor, input.fogFactor), underAlpha);
                    }

                    // Beer-Lambert Derinlik Sogurmasi:
                    // Gercek selaleler (activeWallMask) dolgun mavi; durgun zemin muhur etekleri gercek derinlik + yumusak erimeyle
                    // kar/topraga sert akvaryum cami gibi degil dogal su kenari gibi oturur!
                    float flowingContrastBoost = max(slopeFlowMask * 0.34, saturate((activeFlowSpeed - 0.30) * 0.35) * 0.24) * activeMotionGate;
                    float horizontalDepth = max(min(opticalDepth, max(depthMeters * 1.65, 0.02)), flowingContrastBoost);
                    float effectiveColumnDepth = lerp(horizontalDepth, max(depthMeters, 0.72), activeWallMask);
                    float3 extinction = _WaterExtinction.rgb * lerp(1.0, 1.16, salt01);
                    float3 absorb = exp(-extinction * clamp(effectiveColumnDepth + activeWallMask * 0.22 + flowingContrastBoost * 1.15, 0.03, 6.0));
                    float3 waterColor = lerp(deepTint, shallowTint, absorb);
                    // Buz/Kar zeminle kamufle olmamasi icin akan derede hafif safir-kobalt doygunluk vurgusu
                    waterColor = lerp(waterColor, float3(0.08, 0.44, 0.80), saturate(flowingContrastBoost * 0.85));

                    // Schlick Fresnel + Gunes/Gok Yansimasi + Canli Gunes Specular Pariltisi
                    float fresnel = 0.06 + 0.94 * pow(saturate(1.0 - NdotV), 3.8);
                    float3 skyReflection = float3(0.42, 0.70, 0.96) * lightCol * 1.14;
                    float3 finalColor = lerp(waterColor * lightCol, skyReflection, fresnel * lerp(0.52, 0.10, sideWallMask));
                    float3 halfDir = normalize(mainLight.direction + viewDirWS);
                    float sunSpecPower = lerp(150.0, 60.0, slopeFlowMask);
                    float sunSpec = pow(saturate(dot(normalWS, halfDir)), sunSpecPower) *
                                    (0.65 + 0.50 * slopeFlowMask) * totalShadowAtten * (1.0 - sideWallMask * 0.65);
                    finalColor += mainLight.color * sunSpec;

                    // --- Birlesik Fiziksel Beyaz Kopuk (0.0 MB VRAM) ---
                    // A. Superkritik Froude Sayisi Kopugu
                    float froude = activeFlowSpeed / sqrt(9.81 * max(depthMeters, 0.10));
                    float froudeDepthGate = smoothstep(0.08, 0.34, depthMeters);
                    float froudeFoam = smoothstep(0.95, 2.15, froude) * smoothstep(0.36, 0.76, rippleHeight01) * froudeDepthGate;

                    // B. Minecraft Tarzi 3B Hacimli Selale Akis Cizgileri:
                    // Yuzey eksenine hizali (wallU) asagi kayan yari-seffaf parlak turkuaz-beyaz akis damarlari
                    // (Yalnizca gercek selalelerde waterfallAttrFoam > 0; durgun zemin muhur etekleri berrak su olarak kalir!)
                    float2 waterfallStreakUV = float2(wallU * 2.6, input.positionWS.y * 0.92 + _Time.y * 4.8);
                    float waterfallNoise = SmoothValueNoise2D(waterfallStreakUV * 2.4);
                    float fineStreaks = 0.5 + 0.5 * sin(wallU * 16.0 + waterfallNoise * 3.8 + input.positionWS.y * 1.2 + _Time.y * 5.2);
                    float flowVeins = smoothstep(0.38, 0.88, fineStreaks * (0.45 + 0.55 * waterfallNoise));
                    float activeFlowIntensity = waterfallAttrFoam;
                    float waterfallFoam = activeFlowIntensity * (0.14 + 0.68 * flowVeins);

                    // B2. Egimli Yamac / Tasan Akinti Seritleri (71bfe469.jpg Tamiri):
                    // Durgun golde 0.0; egimli yamacta ve hizli akan suda akis yonunde kayan canli beyaz-turkuaz akinti damarlari!
                    float slopeRapidFoam = 0.0;
                    float slopeRapidGate = saturate(max(
                        slopeFlowMask * smoothstep(0.012, 0.065, depthMeters),
                        smoothstep(0.45, 1.35, activeFlowSpeed) * smoothstep(0.018, 0.085, depthMeters))) * (1.0 - sideWallMask) * activeMotionGate;
                    if (slopeRapidGate > 0.01 && activeFlowSpeed > 0.05)
                    {
                        float2 flowDir = activeStreamVel / max(activeFlowSpeed, 0.001);
                        float2 flowPerp = float2(-flowDir.y, flowDir.x);
                        float alongFlow = dot(input.positionWS.xz, flowDir);
                        float acrossFlow = dot(input.positionWS.xz, flowPerp);
                        float scrollSpeed = 2.0 + activeFlowSpeed * 1.45;
                        float2 rapidUV = float2(acrossFlow * 3.4, alongFlow * 1.05 - _Time.y * scrollSpeed);
                        float rapidNoise = SmoothValueNoise2D(rapidUV * 1.9);
                        float rapidWave = 0.5 + 0.5 * sin(acrossFlow * 13.5 + rapidNoise * 4.0 + alongFlow * 1.4 - _Time.y * (scrollSpeed * 1.35));
                        float rapidStreaks = smoothstep(0.42, 0.88, rapidWave * (0.45 + 0.55 * rapidNoise));
                        slopeRapidFoam = slopeRapidGate * (0.12 + 0.58 * rapidStreaks);
                    }

                    // C. Kiyi ve Engel Carpma Kopugu: yalnizca derinligi olan gol/nehir/deniz kiyisinda (2-8 cm yagmur filminde 0!)
                    float shoreDist = max(0.05, _ShorelineFoamDistance);
                    float bodyOfWaterGate = smoothstep(0.22, 0.68, depthMeters) * (1.0 - sideWallMask);
                    float shoreFoam = smoothstep(shoreDist, 0.0, opticalDepth) * smoothstep(0.42, 0.80, rippleHeight01) * bodyOfWaterGate;

                    // D. WMO Beaufort >= 6 Ruzgar Dalga Tepesi Kopugu (Whitecaps)
                    float beaufortWhitecap = smoothstep(10.8, 22.0, windSpeed) *
                                             smoothstep(0.55, 0.84, rippleHeight01) *
                                             smoothstep(0.30, 1.50, depthMeters);

                    // E. Oyuncu Yuzme Calkanti Halkasi ve V-Iz Kopugu
                    float playerFoam = 0.0;
                    float swimWeight = saturate(_StratocraftSwimmerPos.w);
                    if (swimWeight > 0.01 && abs(input.positionWS.y - _StratocraftSwimmerPos.y) < 2.2)
                    {
                        float2 relXZ = input.positionWS.xz - _StratocraftSwimmerPos.xz;
                        float r = length(relXZ);
                        float splashPulse = saturate(_StratocraftSwimmerVel.w);
                        float ringWave = 0.5 + 0.5 * sin(r * 9.0 - _Time.y * 10.5);
                        float churnNoise = SmoothValueNoise2D(input.positionWS.xz * 5.2 - _Time.yy * 1.4);
                        float bodyRing = smoothstep(1.35 + splashPulse * 0.65, 0.18, r) * (0.45 + 0.55 * ringWave * churnNoise);

                        float2 swimVelXZ = _StratocraftSwimmerVel.xz;
                        float swimSpeedXZ = length(swimVelXZ);
                        float wakeFoam = 0.0;
                        if (swimSpeedXZ > 0.25)
                        {
                            float2 backDir = -swimVelXZ / swimSpeedXZ;
                            float2 sideDir = float2(-backDir.y, backDir.x);
                            float alongBack = dot(relXZ, backDir);
                            float acrossSide = abs(dot(relXZ, sideDir));
                            if (alongBack > 0.0 && alongBack < 3.6)
                            {
                                float wakeHalfWidth = 0.22 + alongBack * 0.34;
                                float vEdge = 1.0 - smoothstep(wakeHalfWidth * 0.55, wakeHalfWidth, acrossSide);
                                float wakeFade = (1.0 - alongBack / 3.6) * saturate(swimSpeedXZ * 0.28);
                                wakeFoam = vEdge * wakeFade * (0.55 + 0.45 * churnNoise);
                            }
                        }

                        playerFoam = saturate((bodyRing * (0.65 + 0.55 * splashPulse) + wakeFoam) * swimWeight);
                    }

                    float foam = saturate(max(max(froudeFoam, max(waterfallFoam, slopeRapidFoam)), max(shoreFoam, max(beaufortWhitecap, playerFoam))));
                    finalColor = lerp(finalColor, float3(0.92, 0.97, 1.0) * lightCol * 1.12, foam * lerp(0.78, 0.46, sideWallMask));

                    // Fiziksel kalinliga + akisa duyarli seffaflik (71bfe469.jpg Tamiri):
                    // - Durgun ince yagmur filmi berrak islak tabaka (~0.24..0.38 alpha),
                    // - Kar/buz ustunde akan dere ve tasan akinti net gorunur (0.50..0.82 alpha),
                    // - Dikey 3B selale/zemin muhru dolgun yari-seffaf su (0.74..0.84)!
                    float depthVis01 = sqrt(saturate((effectiveColumnDepth - 0.006) / 0.24));
                    float slopeAlphaFloor = max(slopeFlowMask, saturate((activeFlowSpeed - 0.35) * 0.85)) *
                                            smoothstep(0.008, 0.050, depthMeters) * 0.58;
                    float columnThicknessAlpha = max(slopeAlphaFloor, lerp(0.24, max(0.76, _WaterColorShallow.a), depthVis01));
                    float deepVolumeBonus = clamp(effectiveColumnDepth, 0.0, 4.0) * 0.05;
                    float edgeContactFeather = validDepth ? lerp(smoothstep(0.0, 0.045, opticalDepth), 1.0, max(activeWallMask, slopeFlowMask * 0.85)) : 1.0;
                    float thinRimFeather = lerp(smoothstep(0.004, 0.040, depthMeters), 1.0, max(activeWallMask, slopeFlowMask * 0.70));
                    float baseAlpha = saturate(columnThicknessAlpha + deepVolumeBonus + fresnel * lerp(0.16, 0.0, sideWallMask));
                    float foamTargetAlpha = activeWallMask > 0.5 ? 0.84 : (waterfallAttrFoam > 0.05 ? 0.78 : 0.90);
                    float alpha = lerp(baseAlpha, foamTargetAlpha, foam) * saturate(thinRimFeather * edgeContactFeather);
                    return float4(MixFog(finalColor, input.fogFactor), alpha);
                }

                // ==================== 2. LAV (FluidType == 2: Cift-Fazli Viskoz Kabuk + T^4 Akkor) ====================
                if (input.fluidType == 2u)
                {
                    float lavaPeriod = 3.20;
                    float lavaSpatialJitter = (sin(input.positionWS.x * 0.29 + input.positionWS.z * 0.31) * 0.5 + 0.5) * 0.22;
                    float lavaP0 = frac((_Time.y / lavaPeriod) + lavaSpatialJitter);
                    float lavaP1 = frac((_Time.y / lavaPeriod) + 0.50 + lavaSpatialJitter);
                    float lavaBlend = abs(1.0 - 2.0 * lavaP0);

                    float2 baseLavaUV = (input.positionWS.xz + float2(input.positionWS.y * 0.55, -input.positionWS.y * 0.45)) * 0.35;
                    float2 lavaDrift = flowVel * _LavaCrustSpeed;
                    float v0 = Voronoi2D(baseLavaUV - lavaDrift * (lavaP0 * lavaPeriod));
                    float v1 = Voronoi2D(baseLavaUV - lavaDrift * (lavaP1 * lavaPeriod));
                    float vDist = lerp(v0, v1, lavaBlend);

                    float flowGlowBoost = 1.0 + saturate(flowSpeed * 0.45);
                    float crackMask = saturate((1.0 - smoothstep(0.06, 0.34, vDist)) * flowGlowBoost);

                    float3 lavaGlow = _LavaCoreColor.rgb * _LavaEmissionMultiplier;
                    float3 crust = max(float3(0.28, 0.11, 0.06), _LavaCrustColor.rgb * lightCol);
                    float3 finalColor = lerp(crust, lavaGlow, crackMask);

                    return float4(MixFog(finalColor, input.fogFactor), 1.0);
                }

                // ==================== 3. ASIT (FluidType == 3) ====================
                float3 acidAbsorb = exp(-float3(0.12, 0.04, 0.35) * opticalDepth);
                float3 acidColor = lerp(_AcidColorDeep.rgb, _AcidColorShallow.rgb, acidAbsorb);
                float acidFresnel = 0.04 + 0.96 * pow(saturate(1.0 - NdotV), 4.0);
                float3 finalAcid = lerp(acidColor * lightCol, lightCol * 1.1, acidFresnel * 0.5);

                return float4(MixFog(finalAcid, input.fogFactor), 0.85);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
