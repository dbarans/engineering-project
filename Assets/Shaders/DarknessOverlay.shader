// Full-scene darkness overlay. Darkens each pixel by how little light reaches it, read from the
// global vision mask (see VisionMask.hlsl / VisionMaskRenderer.cs): fully lit pixels stay clear,
// unlit ones get the full darkness, and the rim of every light fades between the two.
Shader "Custom/DarknessOverlay"
{
    Properties
    {
        _Darkness("Darkness Alpha", Range(0, 1)) = 0.97
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+2"
        }

        Pass
        {
            Name "DarknessOverlay"
            Tags { "LightMode" = "Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "VisionMask.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Darkness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float4 screenPos   : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.screenPos = VisionMaskScreenPos(OUT.positionHCS);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half visibility = SampleVisionMask(IN.screenPos);
                return half4(0, 0, 0, _Darkness * (1.0h - visibility));
            }
            ENDHLSL
        }
    }
}
