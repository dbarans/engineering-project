// Full-screen "sick CRT" pass, drawn after post-processing by HorrorFullScreenFeature.
//
// Everything here is the kind of distortion a Volume cannot do: the image is warped and torn
// in UV space before it is sampled, which no colour-grading override can express. The colour
// work (vignette, grain, grading, bloom) deliberately lives in the Volume profile instead —
// see POSTFX_NOTES.md for which effect belongs where and why.
//
// One uniform is driven from script (HorrorPostProcessing) rather than set on the material, so it
// is declared OUTSIDE the UnityPerMaterial CBUFFER and read as a global:
//   _HorrorIntensity — master 0..1 dial, 0 leaves the image untouched.
Shader "Custom/HorrorFullScreen"
{
    Properties
    {
        _ScanlineIntensity("Scanline Intensity", Range(0, 0.5)) = 0.07
        _ScanlineCount("Scanline Count", Range(50, 2000)) = 700
        _ScanlineScrollSpeed("Scanline Scroll Speed", Range(-20, 20)) = -2
        _WarpAmount("Barrel Warp", Range(0, 0.3)) = 0.03
        _BreathAmount("Breathing Zoom", Range(0, 0.02)) = 0.0025
        _BreathSpeed("Breathing Speed", Range(0, 2)) = 0.22
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "HorrorFullScreen"

            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Supplies Vert (fullscreen triangle from vertexID), _BlitTexture and sampler_LinearClamp.
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _ScanlineIntensity;
                float _ScanlineCount;
                float _ScanlineScrollSpeed;
                float _WarpAmount;
                float _BreathAmount;
                float _BreathSpeed;
            CBUFFER_END

            // Script-driven global — must stay out of UnityPerMaterial or the material's own
            // (absent) value would shadow the global and it would always read 0.
            float _HorrorIntensity;

            #define SAMPLE_SCENE(uv) SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv)

            half4 Frag(Varyings IN) : SV_Target
            {
                float intensity = saturate(_HorrorIntensity);

                float2 uv = IN.texcoord;
                float2 centered = uv - 0.5;

                // Barrel warp + a slow breathing zoom. Both scale the centred UV, so they cost
                // one multiply together; the breath is a sine so it never settles on a beat the
                // player can consciously read.
                float radiusSq = dot(centered, centered);
                float breath = sin(_Time.y * _BreathSpeed * TWO_PI) * _BreathAmount;
                centered *= 1.0 + (_WarpAmount * radiusSq + breath) * intensity;

                float2 warpedUV = centered + 0.5;

                half3 color = SAMPLE_SCENE(warpedUV).rgb;

                // The warp can pull UVs off-screen, where a clamp sampler would smear the edge
                // pixel into a streak. Black is what a CRT shows past the tube edge anyway.
                float2 inside = step(0.0, warpedUV) * step(warpedUV, 1.0);
                color *= inside.x * inside.y;

                // Scanlines, scrolling slowly so the pattern crawls instead of sitting still and
                // aliasing against the pixel grid.
                float scan = 0.5 + 0.5 * sin(warpedUV.y * _ScanlineCount * PI + _Time.y * _ScanlineScrollSpeed);
                color *= 1.0 - _ScanlineIntensity * intensity * scan;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
