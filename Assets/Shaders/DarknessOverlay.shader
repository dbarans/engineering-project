// Full-scene darkness overlay. Darkens each pixel by how little light reaches it, read from the
// global vision mask (see VisionMask.hlsl / VisionMaskRenderer.cs): fully lit pixels stay clear,
// unlit ones get the full darkness, and the rim of every light fades between the two.
//
// It also carries the light's colour onto the scene. The mask's RGB holds the colour each light
// casts, and only the part of it that is *not* neutral grey is added back here — so plain eyesight
// (which casts black) and a plain white lamp both leave the scene exactly as it was, while an oil
// lamp or a torch washes the ground around it amber, with no second light pass and no per-object
// shader.
Shader "Custom/DarknessOverlay"
{
    Properties
    {
        _Darkness("Darkness Alpha", Range(0, 1)) = 0.97
        _TintStrength("Light Tint Strength", Range(0, 2)) = 0.4
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

            // Premultiplied alpha rather than the usual SrcAlpha blend, so this one pass can both
            // darken (through alpha) and add the light's colour (through RGB). With RGB at zero it
            // behaves exactly like the plain black overlay it replaces.
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "VisionMask.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Darkness;
                float _TintStrength;
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
                half4 light = SampleVisionMaskLight(IN.screenPos);
                half darkness = _Darkness * (1.0h - light.a);

                // Only the coloured part of the light is added: subtracting the neutral grey
                // leaves an uncoloured light contributing nothing and a warm one contributing its
                // amber, which keeps unlit-but-seen ground the reference tone to read warmth against.
                half neutral = min(light.r, min(light.g, light.b));
                half3 tint = saturate((light.rgb - neutral) * _TintStrength);

                return half4(tint, darkness);
            }
            ENDHLSL
        }
    }
}
