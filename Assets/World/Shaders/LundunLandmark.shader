// E7: landmarks seen from across the city (the Lundun Wheel's far version). Plain colour from the building kit's
// atlas, no lighting (LundunWheel tints it for the time of day), its own glow (lights at night), and the distance fog
// held back so it still reads as a silhouette on the skyline. The height fog (a full-screen pass) still applies, so it
// thins towards the top like everything else. Drawn on the Landmark layer, which the camera sees further than the rest.
Shader "Lundun/Landmark"
{
    Properties
    {
        _MainTex ("Atlas", 2D) = "white" {}
        _Color ("Tint (time of day)", Color) = (1, 1, 1, 1)
        _EmissionColor ("Glow", Color) = (0, 0, 0, 0)
        _MinVisible ("Always at least this visible through fog", Range(0, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _EmissionColor;
            float _MinVisible;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float fog : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                float z = UNITY_Z_0_FAR_FROM_CLIPSPACE(o.pos.z);
                #if defined(FOG_LINEAR)
                    o.fog = saturate(z * unity_FogParams.z + unity_FogParams.w);
                #elif defined(FOG_EXP)
                    o.fog = saturate(exp2(-unity_FogParams.y * z));
                #elif defined(FOG_EXP2)
                    float e = unity_FogParams.x * z; o.fog = saturate(exp2(-e * e));
                #else
                    o.fog = 1;
                #endif
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                float seen = max(i.fog, _MinVisible);
                c.rgb = lerp(unity_FogColor.rgb, c.rgb, seen);
                c.rgb += _EmissionColor.rgb * max(seen, 0.75);
                c.a = 1;
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
