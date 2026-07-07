// Renders the FOV mesh to the stencil buffer (stencil = 1) without writing any color.
// Must render BEFORE DarknessOverlay — set sorting order lower than darkness quad.
Shader "Custom/FovMaskWriter"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+1"
        }

        Pass
        {
            Name "FovStencilWrite"
            Tags { "LightMode" = "Universal2D" }

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
            }

            ColorMask 0
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings   { float4 positionHCS : SV_POSITION; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target { return half4(0, 0, 0, 0); }
            ENDHLSL
        }
    }
}
