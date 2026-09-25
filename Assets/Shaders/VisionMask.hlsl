#ifndef GRAVE_VISION_MASK_INCLUDED
#define GRAVE_VISION_MASK_INCLUDED

// Shared access to the vision mask: an offscreen texture where every light source (the player's
// field of view and every lamp) has written how lit each pixel is — 1 fully lit, 0 pitch dark,
// with a gradient over the outer rim of each light — in alpha, and in RGB the colour that light
// casts on what it lights (black for plain eyesight, amber for a flame). Written by
// VisionMaskWriter.shader, rendered and published as a global texture by VisionMaskRenderer.cs.
//
// Consumers sample it in screen space, so the darkness overlay and masked sprites always agree
// on how visible a given pixel is. Overlapping lights combine by max (see VisionMaskWriter), so
// two lights never darken each other where they meet.

TEXTURE2D(_VisionMask);
SAMPLER(sampler_VisionMask);

/// Screen-space UV for sampling the mask, from a clip-space position (use in the vertex stage).
float4 VisionMaskScreenPos(float4 positionHCS)
{
    return ComputeScreenPos(positionHCS);
}

/// The whole light reaching this pixel: RGB the colour it casts, weighted by how strongly it
/// reaches, alpha how lit the pixel is. Pass the interpolated value from VisionMaskScreenPos.
half4 SampleVisionMaskLight(float4 screenPos)
{
    float2 uv = screenPos.xy / max(screenPos.w, 1e-5);
    return SAMPLE_TEXTURE2D(_VisionMask, sampler_VisionMask, uv);
}

/// How lit this pixel is, 0..1 — the light's colour is ignored, so a dim white light and a dim
/// amber one hide an object equally well.
half SampleVisionMask(float4 screenPos)
{
    return SampleVisionMaskLight(screenPos).a;
}

#endif
