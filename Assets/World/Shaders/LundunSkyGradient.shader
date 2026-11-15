// Simple three-colour sky. Colours are set by LundunLighting from the lighting preset.
Shader "Lundun/SkyGradient"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.03, 0.04, 0.10, 1)
        _HorizonColor ("Horizon (also the fog colour)", Color) = (0.26, 0.17, 0.21, 1)
        _BottomColor ("Below horizon", Color) = (0.06, 0.05, 0.06, 1)
        _HorizonSharpness ("Horizon glow height", Range(1, 12)) = 4
        _Stars ("Stars (0 = none; set by LundunLighting on clear nights)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _TopColor, _HorizonColor, _BottomColor;
            half _HorizonSharpness;
            half _Stars;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float y = normalize(i.dir).y;
                float up = 1.0 - pow(1.0 - saturate(y), _HorizonSharpness);   // 0 at the horizon, 1 overhead
                fixed4 above = lerp(_HorizonColor, _TopColor, up);
                fixed4 below = lerp(_HorizonColor, _BottomColor, saturate(-y * 6.0));
                // V2b: a few stars on clear nights (London: not many, none near the horizon's glow)
                float3 d = normalize(i.dir);
                float3 sp = d * 190.0;
                float3 cell = floor(sp);
                float3 inCell = frac(sp) - 0.5;
                float h = frac(sin(dot(cell, float3(127.1, 311.7, 74.7))) * 43758.5453);
                float star = step(0.9978, h) * saturate(1.0 - length(inCell) * 3.0);
                above.rgb += star * _Stars * saturate(d.y * 2.5 - 0.15) * (0.45 + 0.55 * frac(h * 91.7)) * fixed3(0.85, 0.9, 1.0);
                return y >= 0 ? above : below;
            }
            ENDCG
        }
    }
    Fallback Off
}
