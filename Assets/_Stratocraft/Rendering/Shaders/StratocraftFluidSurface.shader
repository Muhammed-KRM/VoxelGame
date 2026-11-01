Shader "Stratocraft/FluidSurface"
{
    Properties
    {
        _WaterColorShallow ("Water Shallow Color", Color) = (0.16, 0.58, 0.92, 0.55)
        _WaterColorDeep ("Water Deep (Abyssal) Color", Color) = (0.02, 0.12, 0.32, 0.95)
        _WaterExtinction ("Water Extinction (RGB Sigma)", Vector) = (0.45, 0.14, 0.06, 0.0)
        _WaterWaveSpeed ("Wave Speed", Float) = 1.2
        _WaterWaveFrequency ("Wave Frequency", Float) = 0.8
        _WaterWaveAmplitude ("Wave Amplitude", Float) = 0.04
        _ShorelineFoamDistance ("Foam Band Distance (Meters)", Float) = 0.32
        
        _LavaCrustColor ("Lava Basaltic Crust Color", Color) = (0.12, 0.08, 0.08, 1.0)
        _LavaCoreColor ("Lava Core Glow Color", Color) = (1.0, 0.36, 0.08, 1.0)
        _LavaEmissionMultiplier ("Lava T^4 Emission Multiplier", Float) = 4.5
        _LavaCrustSpeed ("Lava Flow Speed", Float) = 0.25
        
        _AcidColorShallow ("Acid Shallow Color", Color) = (0.24, 0.86, 0.16, 0.70)
        _AcidColorDeep ("Acid Deep Color", Color) = (0.08, 0.42, 0.04, 0.98)
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
            Cull Back

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

                // Su ve Asit icin prosedurel Gerstner dalgasi
                if (fType == 1 || fType == 3)
                {
                    float time = _Time.y * _WaterWaveSpeed;
                    float wave = sin((posWS.x + flux.x) * _WaterWaveFrequency + time) *
                                 cos((posWS.z + flux.y) * _WaterWaveFrequency * 1.3 + time * 0.8);
                    posWS.y += wave * _WaterWaveAmplitude;
                }

                output.positionWS = posWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
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

            float4 FluidFrag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);
                float NdotV = max(0.0, dot(normalWS, viewDirWS));

                // Derinlik tamponundan optik yol uzunlugu (d_optic)
                float2 screenUV = input.screenPos.xy / max(1e-5, input.screenPos.w);
                #if UNITY_REVERSED_Z
                float rawDepth = SampleSceneDepth(screenUV);
                #else
                float rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, SampleSceneDepth(screenUV));
                #endif
                float sceneDepthWS = LinearEyeDepth(rawDepth, _ZBufferParams);
                float surfaceDepthWS = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float opticalDepth = max(0.0, sceneDepthWS - surfaceDepthWS);

                // Ana Isik
                Light mainLight = GetMainLight();
                float NdotL = max(0.0, dot(normalWS, mainLight.direction));
                float3 lightCol = mainLight.color * (NdotL * 0.75 + 0.25);

                // ==================== 1. SU (FluidType == 1) ====================
                if (input.fluidType == 1)
                {
                    // Beer-Lambert Derinlik Sogurmasi
                    float3 extinction = _WaterExtinction.rgb;
                    float3 absorb = exp(-extinction * opticalDepth);
                    float3 waterColor = lerp(_WaterColorDeep.rgb, _WaterColorShallow.rgb, absorb);

                    // Schlick Fresnel
                    float fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, 5.0);
                    float3 skyReflection = lightCol * 1.25;
                    float3 finalColor = lerp(waterColor * lightCol, skyReflection, fresnel * 0.65);

                    // Shoreline Foam Band (Kiyi Etegi Kopugu)
                    float foamFactor = saturate(1.0 - (opticalDepth / max(0.05, _ShorelineFoamDistance)));
                    float foamNoise = Hash21(input.positionWS.xz * 6.0 + _Time.y * 0.4);
                    float foam = step(0.45, foamFactor * foamNoise);
                    finalColor = lerp(finalColor, float3(0.95, 0.98, 1.0) * lightCol * 1.4, foam * 0.85);

                    float alpha = saturate(_WaterColorShallow.a + (opticalDepth * 0.15) + (foam * 0.35));
                    return float4(MixFog(finalColor, input.fogFactor), alpha);
                }

                // ==================== 2. LAV (FluidType == 2) ====================
                if (input.fluidType == 2)
                {
                    // Voronoi bazaltik kabuk ve catlaklar
                    float2 lavaUV = input.positionWS.xz * 0.35 + input.fluxVelocity * (_Time.y * _LavaCrustSpeed);
                    float vDist = Voronoi2D(lavaUV);
                    float crackMask = 1.0 - smoothstep(0.05, 0.28, vDist);

                    // Stefan-Boltzmann T^4 akkor turuncu-sari emisyon
                    float3 lavaGlow = _LavaCoreColor.rgb * _LavaEmissionMultiplier;
                    float3 crust = _LavaCrustColor.rgb * lightCol;
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
