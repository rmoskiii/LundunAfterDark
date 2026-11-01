// Simple three-colour sky. Colours are set by LundunLighting from the lighting preset.
Shader "Lundun/SkyGradient"
{
    Properties
    {
        _TopColor ("Top", Color) = (0.03, 0.04, 0.10, 1)
        _HorizonColor ("Horizon (also the fog colour)", Color) = (0.26, 0.17, 0.21, 1)
        _BottomColor ("Below horizon", Color) = (0.06, 0.05, 0.06, 1)
        _HorizonSharpness ("Horizon glow height", Range(1, 12)) = 4
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
                return y >= 0 ? above : below;
            }
            ENDCG
        }
    }
    Fallback Off
}
