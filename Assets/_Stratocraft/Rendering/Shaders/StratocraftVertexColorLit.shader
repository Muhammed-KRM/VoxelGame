Shader "Stratocraft/VertexColorUberLit"
{
    Properties
    {
        _BaseColor ("Base Tint", Color) = (1, 1, 1, 1)
        _AmbientStrength ("Ambient Strength", Range(0, 1)) = 0.38
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200
        Cull Back
        ZWrite On

        Pass
        {
            Name "ForwardLit"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                fixed4 color  : COLOR;
                float4 uv2    : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos       : SV_POSITION;
                float3 worldNorm : TEXCOORD0;
                float3 worldPos  : TEXCOORD1;
                fixed4 color     : COLOR;
                float4 matMeta   : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            fixed4 _BaseColor;
            float _AmbientStrength;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.worldNorm = UnityObjectToWorldNormal(v.normal);
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.color = v.color * _BaseColor;
                o.matMeta = v.uv2;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 N = normalize(i.worldNorm);
                float3 L = normalize(float3(0.45, 0.82, -0.35));
                float ndotl = saturate(dot(N, L));

                // Hemisphere sky/ground ambient + directional sun light
                float3 skyAmb = float3(0.68, 0.78, 0.92);
                float3 gndAmb = float3(0.28, 0.24, 0.20);
                float3 hemi = lerp(gndAmb, skyAmb, N.y * 0.5 + 0.5) * _AmbientStrength;

                // Subtle triplanar voxel grid micro-detail from world position
                float3 gridSteps = abs(frac(i.worldPos * 2.0) - 0.5);
                float microEdge = smoothstep(0.46, 0.50, max(gridSteps.x, max(gridSteps.y, gridSteps.z)));
                float microFactor = 1.0 - microEdge * 0.08;

                float3 albedo = i.color.rgb * microFactor;
                float ao = lerp(0.55, 1.0, i.color.a);
                float3 lit = albedo * (hemi + ndotl * float3(1.0, 0.96, 0.88) * 0.78) * ao;

                // Thermal glow if matMeta.y (Temperature) > 0.55
                float tempHeat = saturate((i.matMeta.y - 0.55) * 2.2);
                float3 emission = tempHeat * float3(1.6, 0.45, 0.08);

                return fixed4(lit + emission, 1.0);
            }
            ENDCG
        }
    }
    FallBack "Diffuse"
}
