// Rendered on the FOV mesh. Single pass (URP 2D Renderer draws only one Universal2D pass):
//   1. Writes stencil = 1 across the visible area so DarknessOverlay skips it.
//   2. Fades darkness in near the view radius, softening the line between view and shadow.
// UV.x (set by FieldOfView.cs) is the normalized distance from the player:
// 0 at the center, 1 at the view radius. Points cut short by walls have UV.x < 1,
// so wall shadows stay sharp while the outer range edge fades smoothly.
Shader "Custom/FovMaskWriter"
{
    Properties
    {
        _Darkness("Darkness Alpha (match DarknessOverlay)", Range(0, 1)) = 0.97
        _EdgeSoftness("Cone Edge Softness", Range(0, 1)) = 0.35
        _NearEdgeSoftness("Near Circle Edge Softness", Range(0, 1)) = 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+1"
        }

        Pass
        {
            Name "FovStencilAndFalloff"
            Tags { "LightMode" = "Universal2D" }

            Stencil
            {
                Ref 1
                Comp Always
                Pass Replace
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
                float _EdgeSoftness;
                float _NearEdgeSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Softness depends on the region (UV.y): the wide cone edge vs the narrow
                // near-circle edge. A narrow near edge keeps the circle clear so objects
                // behind the player stay visible instead of being darkened away.
                float softness = lerp(_EdgeSoftness, _NearEdgeSoftness, saturate(IN.uv.y));
                float edgeStart = 1.0 - saturate(softness);
                float falloff = smoothstep(edgeStart, 1.0, saturate(IN.uv.x));
                return half4(0, 0, 0, falloff * _Darkness);
            }
            ENDHLSL
        }
    }
}
