// V2f: the CCTV picture (LundunCctvFeed): green-grey, grain, scanlines, a little line jitter, dark corners.
Shader "Lundun/CctvFeed"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "white" {}
        _Grain ("Grain", Float) = 0.14
    }
    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off ZWrite Off ZTest Always
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float _Grain;

            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed4 col : COLOR; };

            v2f vert (appdata_full v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.texcoord.xy;
                o.col = v.color;
                return o;
            }

            float hash (float2 p) { return frac(sin(dot(p, float2(12.9898, 78.233))) * 43758.5453); }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float row = floor(uv.y * 216.0);
                float jitter = (hash(float2(row, floor(_Time.y * 12.0))) - 0.5) * 0.004;
                float3 c = tex2D(_MainTex, float2(uv.x + jitter, uv.y)).rgb;
                float g = dot(c, float3(0.3, 0.59, 0.11));
                g = saturate((g - 0.03) * 1.3);
                float3 col = g * float3(0.84, 1.0, 0.86);
                col += (hash(uv * float2(640.0, 360.0) + floor(_Time.y * 24.0)) - 0.5) * _Grain;
                col *= 0.86 + 0.14 * sin(uv.y * 216.0 * 3.14159);
                float2 d = uv - 0.5;
                col *= saturate(1.2 - dot(d, d) * 1.8);
                return fixed4(col, i.col.a);
            }
            ENDCG
        }
    }
}
