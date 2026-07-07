// Full-scene darkness overlay. Renders black only where stencil != 1.
// Stencil = 1 is written by FovMaskWriter (the FOV mesh), so the FOV area stays visible.
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

            Stencil
            {
                Ref 1
                Comp NotEqual
                Pass Keep
            }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Darkness;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                return half4(0, 0, 0, _Darkness);
            }
            ENDHLSL
        }
    }
}
