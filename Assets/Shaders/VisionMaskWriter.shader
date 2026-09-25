// Rendered on every light mesh (the player's FOV mesh and each lamp's lit circle) into the
// offscreen vision mask, never to the screen. Outputs how lit each pixel is — 1 inside the light,
// falling off to 0 across the outer rim — in the alpha channel, and in RGB the colour this light
// casts on what it lights.
//
// "Casts" is the exact word: RGB is not how bright the light is, it is how much it colours the
// scene. The player's plain eyesight casts nothing (a black tint) and leaves the world its own
// colours; a lamp or a torch casts amber. That separation is what lets the two combine — see the
// blend note below — since a light with nothing to say about colour cannot out-max one that has.
//
// UV.x (set by OcclusionMeshBuilder) is the normalized distance from the light's origin: 0 at the
// center, 1 at its reach. Points cut short by a wall have UV.x < 1, so wall shadows stay hard
// while the outer range edge fades smoothly.
// UV.y is the region flag selecting the edge softness: 1 for the player's near-vision circle
// (narrow edge, so the circle around the player stays clear), 0 for a cone or a lamp.
//
// BlendOp Max is what makes multiple lights combine correctly: overlapping lights take the
// brighter contribution instead of accumulating, so a lamp cannot paint its dim rim over ground
// the player already sees. It applies per channel, so alpha still carries the brightest
// visibility while RGB carries the strongest colour cast reaching that pixel — and an uncoloured
// light, writing black, never overrides a coloured one.
//
// _Intensity scales the whole light down (e.g. a flickering lamp) without touching the mesh, so a
// light source can flicker every frame via a MaterialPropertyBlock instead of re-raycasting.
// _LightTint, _EdgeSoftness and _NearEdgeSoftness are set the same way, which is what lets every
// light share one material and still burn its own colour and fade over its own distance.
Shader "Custom/VisionMaskWriter"
{
    Properties
    {
        _EdgeSoftness("Cone Edge Softness", Range(0, 1)) = 0.35
        _NearEdgeSoftness("Near Circle Edge Softness", Range(0, 1)) = 0.15
        _Intensity("Intensity", Range(0, 1)) = 1
        // Black: a light that reveals without colouring. Set per light through a property block.
        _LightTint("Light Tint", Color) = (0, 0, 0, 1)
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
                half4 _LightTint;
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

                // RGB is the light's colour weighted by how strongly it reaches this pixel, so a
                // dim rim contributes a dim colour and cannot out-max a nearer, brighter light.
                return half4(saturate(_LightTint.rgb) * visibility, visibility);
            }
            ENDHLSL
        }
    }
}
