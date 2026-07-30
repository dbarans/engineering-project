// Sprite shader for objects that must be masked by the player's vision (enemies, furniture).
// Renders like a normal unlit sprite, but fades with the global vision mask (see VisionMask.hlsl):
// a pixel no light reaches is not drawn at all, so half-covered objects show only their visible
// half, and objects at the rim of a light fade out together with the ground under them.
//
// Wall edges stay hard on purpose: the mask geometry ends abruptly at a wall's shadow, so only the
// outer rim of a light — where the mask itself is a gradient — produces a soft edge.
Shader "Custom/SpriteFovMasked"
{
    Properties
    {
        _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1, 1, 1, 1)
        // Defaults to 0 so the sprite renders normally in the Editor (Scene view, Prefab view,
        // Project thumbnails), where no vision mask has ever been rendered.
        // Materials/SpriteFovMaskedClipped.mat sets this to 1; FovMaskedSpriteRuntime.cs swaps a
        // renderer onto that material at Awake, so masking only applies during Play.
        [ToggleUI] _MaskEnabled("Apply Vision Mask", Float) = 0
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
            Name "SpriteFovMasked"
            Tags { "LightMode" = "Universal2D" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "VisionMask.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
                float _MaskEnabled;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                half4  color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 screenPos   : TEXCOORD1;
                half4  color       : COLOR;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.screenPos = VisionMaskScreenPos(OUT.positionHCS);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv) * IN.color;

                half visibility = SampleVisionMask(IN.screenPos);
                col.a *= lerp(1.0h, visibility, _MaskEnabled);

                return col;
            }
            ENDHLSL
        }
    }
}
