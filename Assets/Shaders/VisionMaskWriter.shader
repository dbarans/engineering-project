// Rendered on every light mesh (the player's FOV mesh and each lamp's lit circle) into the
// offscreen vision mask, never to the screen. Outputs how lit each pixel is: 1 inside the light,
// falling off to 0 across the outer rim.
//
// UV.x (set by OcclusionMeshBuilder) is the normalized distance from the light's origin: 0 at the
// center, 1 at its reach. Points cut short by a wall have UV.x < 1, so wall shadows stay hard
// while the outer range edge fades smoothly.
// UV.y is the region flag selecting the edge softness: 1 for the player's near-vision circle
// (narrow edge, so the circle around the player stays clear), 0 for a cone or a lamp.
//
// BlendOp Max is what makes multiple lights combine correctly: overlapping lights take the
// brighter contribution instead of accumulating, so a lamp cannot paint its dim rim over ground
// the player already sees.
//
// _Intensity scales the whole light down (e.g. a flickering lamp) without touching the mesh, so a
// light source can flicker every frame via a MaterialPropertyBlock instead of re-raycasting.
Shader "Custom/VisionMaskWriter"
{
    Properties
    {
        _EdgeSoftness("Cone Edge Softness", Range(0, 1)) = 0.35
        _NearEdgeSoftness("Near Circle Edge Softness", Range(0, 1)) = 0.15
        _Intensity("Intensity", Range(0, 1)) = 1
    }

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
            Name "VisionMaskWriter"
            Tags { "LightMode" = "Universal2D" }

            BlendOp Max
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _EdgeSoftness;
                float _NearEdgeSoftness;
                float _Intensity;
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
                float softness = lerp(_EdgeSoftness, _NearEdgeSoftness, saturate(IN.uv.y));
                float edgeStart = 1.0 - saturate(softness);
                half visibility = (1.0h - smoothstep(edgeStart, 1.0, saturate(IN.uv.x))) * saturate(_Intensity);
                return half4(visibility, visibility, visibility, visibility);
            }
            ENDHLSL
        }
    }
}
