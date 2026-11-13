Shader "Stratocraft/ProceduralClouds"
{
    // Paket 4A canli PlayMode duzeltmesi (v2):
    // - Tek bir buyuk bulut "slab" hacmi + tek bir yagmur hacmi cizilir (81 ayri kutu yerine) -> kutu kenari izgara
    //   cizgileri yok, overdraw = 1.
    // - Hucre basina bulut suyu ve yagis 9x9 AOI dizileri olarak (_CloudWater81 / _CloudPrecip81) GPU'ya yazilir ve
    //   dunya XZ'sinde bilinear orneklenir (izgara disinda kenar hucreleri ufka kadar uzatilir).
    // - Kamera isini hacimle (AABB) kesistirilir, sahne derinligiyle kirpilir (ZTest Always + _CameraDepthTexture),
    //   boylece alttan, icinden veya agac/arazi onunden bakildiginda dogru gorunur.
    // - 0 MB doku VRAM: tum gurultu ALU ile uretilir.
    Properties
    {
        _CloudBaseColor ("Cloud Illuminated Color", Color) = (0.97, 0.98, 1.0, 1.0)
        _CloudShadowColor ("Cloud Storm Underbelly Color", Color) = (0.36, 0.40, 0.48, 1.0)
        _CloudExtinctionSigma ("Beer-Lambert Extinction Sigma", Float) = 0.11
        _HenyeyForwardG ("Henyey-Greenstein Forward G", Range(0.0, 0.92)) = 0.60
        _HenyeyBackG ("Henyey-Greenstein Backscatter G", Range(-0.85, 0.0)) = -0.25
        _CloudNoiseScale ("Cloud Noise Scale", Float) = 0.0065
        _RainStreakColor ("Local Rain Streak Tint", Color) = (0.72, 0.80, 0.90, 0.60)
        _RainFallSpeed ("Rain Fall Speed (m/s)", Float) = 14.0
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
            ZTest Always
            Cull Front

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex CloudRainVert
            #pragma fragment CloudRainFrag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

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

            // Kuresel atmosfer degiskenleri (Stage1AtmosphereCloudAndRainRenderer surer)
            float4 _GlobalCloudGridParams;     // x = ort. bulut suyu, y = taban Y, z = tepe Y, w = golge siddeti
            float4 _GlobalWindVector;          // x = ruzgar X, y = hiz, z = ruzgar Z, w = firtina kararma (0..1)
            float4 _GlobalHorizonOpticsParams; // x/y = bati/dogu nem, z = basinc, w = kizil ufuk siddeti
            float4 _CloudGridOrigin;           // x = izgara min X, y = izgara min Z, z = hucre boyu (64), w = kenar hucre sayisi (9)
            float4 _CloudSlabParams;           // x = bulut taban Y, y = bulut tepe Y, z = yagmur zemin Y, w = genel ortu (0..1)
            float _CloudWater81[81];
            float _CloudPrecip81[81];

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR; // R >= 0.5: yagmur hacmi, R < 0.5: bulut hacmi
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 vtxColor   : TEXCOORD1;
                float3 boxMinWS   : TEXCOORD2;
                float3 boxMaxWS   : TEXCOORD3;
            };

            // ------------------------------------------------------------------
            // Ucuz ALU gurultusu
            // ------------------------------------------------------------------
            float Hash31(float3 p)
            {
                p = frac(p * float3(443.8975, 397.2973, 491.1871));
                p += dot(p, p.yxz + 19.19);
                return frac((p.x + p.y) * p.z);
            }

            float ValueNoise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                float3 u = f * f * (3.0 - 2.0 * f);

                float n000 = Hash31(i);
                float n100 = Hash31(i + float3(1, 0, 0));
                float n010 = Hash31(i + float3(0, 1, 0));
                float n110 = Hash31(i + float3(1, 1, 0));
                float n001 = Hash31(i + float3(0, 0, 1));
                float n101 = Hash31(i + float3(1, 0, 1));
                float n011 = Hash31(i + float3(0, 1, 1));
                float n111 = Hash31(i + float3(1, 1, 1));

                return lerp(
                    lerp(lerp(n000, n100, u.x), lerp(n010, n110, u.x), u.y),
                    lerp(lerp(n001, n101, u.x), lerp(n011, n111, u.x), u.y),
                    u.z);
            }

            float CloudFbm(float3 p)
            {
                float v = ValueNoise3D(p) * 0.56;
                v += ValueNoise3D(p * 2.13 + 17.3) * 0.28;
                v += ValueNoise3D(p * 4.71 + 41.7) * 0.16;
                return v;
            }

            int GridIndex(int cx, int cz)
            {
                int side = (int)_CloudGridOrigin.w;
                cx = clamp(cx, 0, side - 1);
                cz = clamp(cz, 0, side - 1);
                return cz * side + cx;
            }

            float SampleWaterGrid(float2 xz)
            {
                float2 g = (xz - _CloudGridOrigin.xy) / _CloudGridOrigin.z - 0.5;
                int2 i0 = (int2)floor(g);
                float2 t = saturate(g - (float2)i0);
                float a = _CloudWater81[GridIndex(i0.x, i0.y)];
                float b = _CloudWater81[GridIndex(i0.x + 1, i0.y)];
                float c = _CloudWater81[GridIndex(i0.x, i0.y + 1)];
                float d = _CloudWater81[GridIndex(i0.x + 1, i0.y + 1)];
                return lerp(lerp(a, b, t.x), lerp(c, d, t.x), t.y);
            }

            float SamplePrecipGrid(float2 xz)
            {
                float2 g = (xz - _CloudGridOrigin.xy) / _CloudGridOrigin.z - 0.5;
                int2 i0 = (int2)floor(g);
                float2 t = saturate(g - (float2)i0);
                float a = _CloudPrecip81[GridIndex(i0.x, i0.y)];
                float b = _CloudPrecip81[GridIndex(i0.x + 1, i0.y)];
                float c = _CloudPrecip81[GridIndex(i0.x, i0.y + 1)];
                float d = _CloudPrecip81[GridIndex(i0.x + 1, i0.y + 1)];
                return lerp(lerp(a, b, t.x), lerp(c, d, t.x), t.y);
            }

            bool RayBox(float3 ro, float3 rd, float3 bmin, float3 bmax, out float tNear, out float tFar)
            {
                float3 inv = 1.0 / (abs(rd) > 1e-5 ? rd : (rd >= 0 ? 1e-5 : -1e-5));
                float3 t0 = (bmin - ro) * inv;
                float3 t1 = (bmax - ro) * inv;
                float3 tmin = min(t0, t1);
                float3 tmax = max(t0, t1);
                tNear = max(max(tmin.x, tmin.y), tmin.z);
                tFar = min(min(tmax.x, tmax.y), tmax.z);
                return tFar > max(tNear, 0.0);
            }

            float HenyeyGreenstein(float cosTheta, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (4.0 * 3.14159265 * pow(max(1e-4, 1.0 + g2 - 2.0 * g * cosTheta), 1.5));
            }

            Varyings CloudRainVert(Attributes input)
            {
                Varyings o = (Varyings)0;
                float3 posWS = TransformObjectToWorld(input.positionOS.xyz);
                o.positionWS = posWS;
                o.positionCS = TransformWorldToHClip(posWS);
                o.vtxColor = input.color;
                o.boxMinWS = TransformObjectToWorld(float3(-0.5, -0.5, -0.5));
                o.boxMaxWS = TransformObjectToWorld(float3(0.5, 0.5, 0.5));
                return o;
            }

            half4 CloudRainFrag(Varyings input) : SV_Target
            {
                float3 ro = _WorldSpaceCameraPos;
                float3 rd = normalize(input.positionWS - ro);
                float3 bmin = min(input.boxMinWS, input.boxMaxWS);
                float3 bmax = max(input.boxMinWS, input.boxMaxWS);

                float tNear, tFar;
                if (!RayBox(ro, rd, bmin, bmax, tNear, tFar))
                {
                    discard;
                }

                // Sahne derinligi ile kirp (agac/arazi/cati onundeki kisim cizilir, arkasindaki gizlenir)
                float2 screenUv = input.positionCS.xy / _ScaledScreenParams.xy;
                float rawDepth = SampleSceneDepth(screenUv);
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 camFwd = -UNITY_MATRIX_V[2].xyz;
                float sceneDist = eyeDepth / max(1e-3, dot(rd, camFwd));

                float tEnter = max(tNear, 0.0);
                float tExit = min(tFar, sceneDist);
                if (tExit <= tEnter)
                {
                    discard;
                }

                Light mainLight = GetMainLight();
                float3 sunDir = normalize(mainLight.direction);
                float3 lightCol = mainLight.color;
                float3 ambientUp = max(SampleSH(float3(0.0, 1.0, 0.0)), 0.02);
                float3 wind = float3(_GlobalWindVector.x, 0.0, _GlobalWindVector.z);
                float storm01 = saturate(_GlobalWindVector.w);
                float jitter = Hash31(float3(input.positionCS.xy, _Time.y * 13.0));

                // ==============================================================
                // YAGMUR HACMI (COLOR.r >= 0.5)
                // ==============================================================
                if (input.vtxColor.r >= 0.5)
                {
                    float segLen = min(tExit - tEnter, 220.0);
                    float haze = 0.0;
                    [unroll]
                    for (int s = 0; s < 5; s++)
                    {
                        float tt = tEnter + segLen * ((s + jitter) / 5.0);
                        float3 p = ro + rd * tt;
                        float precipMm = SamplePrecipGrid(p.xz);
                        haze += saturate(precipMm / 10.0) * (segLen / 5.0);
                    }
                    float hazeAlpha = 1.0 - exp(-haze * 0.0065);

                    // Yakin dusen yagmur cizgileri (2 katman, ruzgarla egik)
                    float streak = 0.0;
                    [unroll]
                    for (int k = 0; k < 2; k++)
                    {
                        float dist = (k == 0 ? 2.5 : 6.0);
                        float tt = tEnter + dist;
                        if (tt < tExit)
                        {
                            float3 p = ro + rd * tt;
                            float precip01 = saturate(SamplePrecipGrid(p.xz) / 8.0);
                            float3 tp = p;
                            tp.xz -= wind.xz * (p.y * 0.03);
                            float fall = tp.y * 0.35 + _Time.y * max(_RainFallSpeed, 4.0) * 0.35;
                            float2 cell = tp.xz * (k == 0 ? 9.0 : 5.0);
                            float h = Hash31(float3(floor(cell), k * 7.0));
                            float2 fr = frac(cell) - 0.5;
                            float core = smoothstep(0.10, 0.0, abs(fr.x)) * smoothstep(0.45, 0.2, abs(fr.y));
                            float pulse = smoothstep(0.6, 0.95, frac(fall + h * 5.3));
                            streak += core * pulse * step(0.55, h) * precip01;
                        }
                    }

                    float3 rainCol = _RainStreakColor.rgb * (lightCol * 0.55 + ambientUp * 0.9);
                    float alpha = saturate(hazeAlpha * 0.75 + streak * 0.45) * _RainStreakColor.a * 1.4;
                    if (alpha <= 0.003) discard;
                    return half4(rainCol, (half)alpha);
                }

                // ==============================================================
                // BULUT SLAB HACMI (COLOR.r < 0.5)
                // ==============================================================
                float baseY = _CloudSlabParams.x;
                float topY = max(_CloudSlabParams.y, baseY + 8.0);
                float thickness = topY - baseY;

                tExit = min(tExit, tEnter + 900.0);
                const int STEPS = 12;
                float stepLen = (tExit - tEnter) / STEPS;
                float transmittance = 1.0;
                float3 scattered = 0.0;
                float cosTheta = dot(rd, sunDir);
                float phase = lerp(HenyeyGreenstein(cosTheta, _HenyeyBackG), HenyeyGreenstein(cosTheta, _HenyeyForwardG), 0.7) * 4.0 * 3.14159265;
                float noiseScale = max(_CloudNoiseScale, 0.001);
                float3 windOffset = wind * _Time.y * 0.6;
                float sigma = max(_CloudExtinctionSigma, 0.005);

                [loop]
                for (int i = 0; i < STEPS; i++)
                {
                    float tt = tEnter + stepLen * (i + jitter);
                    float3 p = ro + rd * tt;
                    float h01 = saturate((p.y - baseY) / thickness);
                    float heightProfile = smoothstep(0.0, 0.12, h01) * smoothstep(1.0, 0.45, h01);
                    float water = SampleWaterGrid(p.xz);
                    float coverage = saturate(water / 1.8);
                    if (water < 0.06 || heightProfile < 0.01)
                    {
                        continue;
                    }

                    float n = CloudFbm((p - windOffset) * noiseScale);
                    float threshold = lerp(0.66, 0.34, coverage);
                    float density = saturate((n - threshold) * 4.0) * heightProfile;
                    if (density < 0.002)
                    {
                        continue;
                    }

                    float ext = density * sigma;
                    float stepT = exp(-ext * stepLen);

                    // Isik: tepeye ve gunese bakan kisim parlak beyaz, taban (ozellikle firtinada) koyu kursuni
                    float sunLit = saturate(0.45 + h01 * 0.75) * (0.6 + 0.4 * saturate(sunDir.y + 0.2));
                    float3 underbelly = lerp(_CloudShadowColor.rgb, float3(0.16, 0.18, 0.22), storm01);
                    float3 lit = _CloudBaseColor.rgb * (lightCol * sunLit * (0.95 + 0.30 * phase) + ambientUp * 1.15);
                    float3 shade = underbelly * (ambientUp * 1.35 + lightCol * 0.30);
                    float3 col = lerp(shade, lit, saturate(h01 * 0.85 + 0.35 - density * 0.30));

                    scattered += transmittance * (1.0 - stepT) * col;
                    transmittance *= stepT;
                    if (transmittance < 0.02)
                    {
                        break;
                    }
                }

                float alphaC = 1.0 - transmittance;
                // Uzak ufukta sise karis
                float fade = saturate(1.0 - (tEnter - 1400.0) / 1200.0);
                alphaC *= fade;
                if (alphaC <= 0.003)
                {
                    discard;
                }

                float3 outCol = scattered / max(1.0 - transmittance, 1e-3);
                float fogFactor = ComputeFogFactor(TransformWorldToHClip(ro + rd * tEnter).z);
                outCol = MixFog(outCol, fogFactor);
                return half4(outCol, (half)alphaC);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
