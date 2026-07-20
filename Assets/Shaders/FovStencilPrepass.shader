// Rendered on the FOV mesh BEFORE regular sprites (negative sorting order, see FieldOfView.cs).
// Writes stencil = 1 across the visible area and nothing else (ColorMask 0), so that sprites
// using Custom/SpriteFovMasked can stencil-test against it and be clipped pixel-perfectly at
// the vision boundary. FovMaskWriter still re-writes the same stencil later for DarknessOverlay.
Shader "Custom/FovStencilPrepass"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "FovStencilPrepass"
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

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                return half4(0, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}
