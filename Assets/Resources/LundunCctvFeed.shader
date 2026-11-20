// V2f / Phase 5: the CCTV picture (LundunCctvFeed), made to look like real council CCTV at night: black and white
// with a faint cold cast, a hard contrast curve (crushed blacks, bright lights blooming), heavy grain, interlace
// lines, a slow rolling bar, the odd horizontal tear, a slight wide-angle bulge and dark corners.
Shader "Lundun/CctvFeed"
{
    Properties
    {
        _MainTex ("Picture", 2D) = "white" {}
        _Grain ("Grain", Float) = 0.11
        _Bulge ("Lens bulge", Float) = 0.09
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
            float4 _MainTex_TexelSize;
            float _Grain, _Bulge;

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
            float luma (float3 c) { return dot(c, float3(0.299, 0.587, 0.114)); }

            fixed4 frag (v2f i) : SV_Target
            {
                // a wide-angle lens: the picture bulges a little; outside it is black
                float2 cc = i.uv - 0.5;
                float r2 = dot(cc, cc);
                float2 uv = 0.5 + cc * (1.0 - _Bulge + _Bulge * 4.0 * r2);
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0) return fixed4(0, 0, 0, i.col.a);

                float frame = floor(_Time.y * 12.0);
                float row = floor(uv.y * 270.0);
                // line jitter, and now and then a band of lines torn sideways
                float jitter = (hash(float2(row, frame)) - 0.5) * 0.0025;
                float band = floor(uv.y * 30.0);
                float tear = step(0.975, hash(float2(band, frame))) * (hash(float2(frame, band)) - 0.5) * 0.03;
                uv.x += jitter + tear;
                // interlace: odd lines a hair behind
                uv.x += (fmod(row, 2.0) - 0.5) * 0.0012;

                float2 px = _MainTex_TexelSize.xy;
                float l = luma(tex2D(_MainTex, uv).rgb);
                // lights bloom: a soft halo from the bright neighbours
                float halo = 0.0;
                halo += luma(tex2D(_MainTex, uv + float2(px.x * 2.0, 0)).rgb);
                halo += luma(tex2D(_MainTex, uv - float2(px.x * 2.0, 0)).rgb);
                halo += luma(tex2D(_MainTex, uv + float2(0, px.y * 2.0)).rgb);
                halo += luma(tex2D(_MainTex, uv - float2(0, px.y * 2.0)).rgb);
                halo *= 0.25;
                l += max(halo - 0.5, 0.0) * 0.9;

                // auto-gain night camera: lift the darks a touch, then a hard S-curve
                l = saturate(l * 1.18 + 0.02);
                l = lerp(l, l * l * (3.0 - 2.0 * l), 0.65);

                // grain (changes every frame), interlace lines, a slow rolling bar
                l += (hash(uv * float2(960.0, 540.0) + frame * 1.37) - 0.5) * _Grain;
                l *= 0.9 + 0.1 * sin(uv.y * 270.0 * 3.14159);
                l *= 0.94 + 0.06 * sin((uv.y + _Time.y * 0.09) * 6.28318);

                // dark corners
                l *= saturate(1.18 - r2 * 2.1);

                float3 col = saturate(l) * float3(0.92, 0.96, 1.0);          // a faint cold cast, like a real monochrome sensor
                return fixed4(col, i.col.a);
            }
            ENDCG
        }
    }
}
