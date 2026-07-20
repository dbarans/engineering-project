// Sprite shader for objects that must be clipped hard at the player's vision boundary
// (enemies, furniture). Renders like a normal unlit sprite, but only where the FOV mesh
// has written stencil = 1 (see FovStencilPrepass.shader) — pixels outside the field of
// view are not drawn at all, so half-covered objects show only their visible half.
Shader "Custom/SpriteFovMasked"
{
    Properties
    {
        _MainTex("Sprite Texture", 2D) = "white" {}
        _Color("Tint", Color) = (1, 1, 1, 1)
        // Defaults to Always (8) so the sprite renders normally in the Editor (Scene view,
        // Prefab view, Project thumbnails) where no FieldOfView has ever written the stencil.
        // Materials/SpriteFovMaskedClipped.mat bakes this to Equal (3); FovMaskedSpriteRuntime.cs
        // swaps a renderer onto that material at Awake, so clipping only applies during Play.
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comparison", Int) = 8
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

            Stencil
            {
                Ref 1
                Comp [_StencilComp]
                Pass Keep
            }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Color;
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
                half4  color       : COLOR;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.color = IN.color * _Color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return tex * IN.color;
            }
            ENDHLSL
        }
    }
}
