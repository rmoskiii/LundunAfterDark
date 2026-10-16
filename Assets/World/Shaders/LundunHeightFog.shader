// Lundun height fog (V3a): thick near the ground, thinning with height, so streets get depth and the sky stays clear.
// Drawn by a Full Screen Pass renderer feature (added by Lundun → 18) before post-processing.
// Its settings are sent every frame by LundunAtmosphere (per time of day, and later the weather).
Shader "Lundun/HeightFog"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always Blend Off

        Pass
        {
            Name "LundunHeightFog"

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            float4 _LundunFogColor;      // rgb = colour, a = overall strength (0 = off)
            float4 _LundunFogParams;     // x = density, y = falloff (per metre), z = base height, w = start distance
            float4 _LundunFogSky;        // x = how much fog the sky gets (0..1), y = distance used for the sky

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                if (_LundunFogColor.a <= 0.0001) return col;

                #if UNITY_REVERSED_Z
                    float depth = SampleSceneDepth(uv);
                    bool sky = depth <= 0.000001;
                #else
                    float depth = lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
                    bool sky = depth >= 0.999999;
                #endif

                float3 wpos = ComputeWorldSpacePosition(uv, depth, UNITY_MATRIX_I_VP);
                float3 cam = _WorldSpaceCameraPos;
                float3 ray = wpos - cam;
                float dist = length(ray);
                float3 dir = ray / max(dist, 0.0001);
                if (sky) dist = _LundunFogSky.y;

                // fog along the ray: density falls off exponentially with height (exact integral)
                float d = max(0.0, dist - _LundunFogParams.w);
                float k = max(_LundunFogParams.y, 0.0001);
                float h0 = cam.y - _LundunFogParams.z;
                float dy = dir.y * d;
                float startDensity = _LundunFogParams.x * exp(-k * h0);
                float f = (abs(dy) > 0.01) ? (1.0 - exp(-k * dy)) / (k * dy) : 1.0;
                float amount = 1.0 - exp(-startDensity * d * f);
                amount = saturate(amount * _LundunFogColor.a);
                if (sky) amount *= _LundunFogSky.x;

                col.rgb = lerp(col.rgb, _LundunFogColor.rgb, amount);
                return col;
            }
            ENDHLSL
        }
    }
}
