using UnityEngine;

/// <summary>
/// Draws the torch's inventory icon procedurally, so the craftable torch has art of its own
/// rather than borrowing another item's. Used by <see cref="TorchSetup"/>, which writes the PNG
/// and builds the item and recipe around it.
///
/// Drawn to match the icons the project already uses: one object filling most of the square, flat
/// colours with a single light direction, and a dark outline around everything that is not on
/// fire — the flame is outlined in its own darker orange instead, because a black line around a
/// flame reads as a cut-out sticker.
///
/// <see cref="IconPixel"/> is pure: it takes only the pixel's coordinates and returns its colour,
/// with every wobble hashed from the position rather than drawn from a stream. That makes the
/// icon byte-identical on every re-run and lets the drawing be rendered and looked at outside the
/// editor.
/// </summary>
public static class TorchArt
{
    /// <summary>Icon side in pixels — the size the project's other item icons are authored at.</summary>
    public const int IconPixels = 128;

    /// <summary>PPU the icon is imported at, matching the rest of the item icons.</summary>
    public const int PixelsPerUnit = 100;

    // Geometry, in icon pixels with the origin at the bottom left. The torch stands upright:
    // it is read at 40 pixels across in a hotbar slot, and a diagonal one loses its flame there.
    private const float CentreX = 64f;
    private const float ShaftBottom = 10f;
    private const float ShaftTop = 78f;
    private const float ShaftHalfWidthBottom = 7.5f;
    private const float ShaftHalfWidthTop = 6f;
    private const float WrapBottom = 62f;
    private const float WrapTop = 92f;
    private const float WrapHalfWidth = 12f;
    private const float FlameBottom = 84f;
    private const float FlameTip = 122f;

    /// <summary>Where the flame is at its widest, and how wide that is.</summary>
    private const float FlameWaist = 96f;
    private const float FlameHalfWidth = 16.5f;

    /// <summary>How wide the dark outline around the wood and cloth is, in pixels.</summary>
    private const float OutlineWidth = 2.5f;

    // The palette. Warm wood, drab cloth and a flame that goes from a deep orange rim to an
    // almost-white core, which is the part that has to survive being shrunk into a slot.
    private static readonly Color Outline = new Color32(0x17, 0x11, 0x0D, 0xFF);
    private static readonly Color Wood = new Color32(0x7A, 0x53, 0x30, 0xFF);
    private static readonly Color WoodShadow = new Color32(0x5A, 0x3B, 0x21, 0xFF);
    private static readonly Color WoodHighlight = new Color32(0x9C, 0x6E, 0x43, 0xFF);
    private static readonly Color Cloth = new Color32(0x5B, 0x45, 0x36, 0xFF);
    private static readonly Color ClothShadow = new Color32(0x3A, 0x2C, 0x22, 0xFF);
    private static readonly Color ClothHighlight = new Color32(0x75, 0x5A, 0x46, 0xFF);
    private static readonly Color FlameRim = new Color32(0x8C, 0x3A, 0x10, 0xFF);
    private static readonly Color FlameOuter = new Color32(0xD9, 0x64, 0x1C, 0xFF);
    private static readonly Color FlameMid = new Color32(0xF0, 0x98, 0x2E, 0xFF);
    private static readonly Color FlameCore = new Color32(0xFF, 0xDC, 0x7A, 0xFF);

    /// <summary>Fully transparent: everything the torch does not cover.</summary>
    private static readonly Color Nothing = new Color(0f, 0f, 0f, 0f);

    /// <summary>
    /// Draws the icon as a PNG.
    /// </summary>
    public static byte[] BuildTexture()
    {
        var texture = new Texture2D(IconPixels, IconPixels, TextureFormat.RGBA32, false);
        try
        {
            for (int y = 0; y < IconPixels; y++)
            {
                for (int x = 0; x < IconPixels; x++)
                    texture.SetPixel(x, y, IconPixel(x, y));
            }

            texture.Apply();
            return texture.EncodeToPNG();
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    /// <summary>
    /// Colour of one pixel of the icon.
    ///
    /// Painted front to back: the flame first, because it overlaps the cloth it burns on, then
    /// the cloth, then the stick, and finally the outline — which is drawn wherever a pixel is
    /// just outside one of those shapes rather than as a separate stroke, so it follows every
    /// wobble in the silhouette for free.
    /// </summary>
    /// <param name="x">Pixel column.</param>
    /// <param name="y">Pixel row, counted from the bottom.</param>
    public static Color IconPixel(int x, int y)
    {
        float px = x + 0.5f;
        float py = y + 0.5f;

        if (InsideFlame(px, py, 0f)) return FlameColor(px, py);
        if (InsideFlame(px, py, 2f)) return FlameRim;

        if (InsideWrap(px, py, 0f)) return ClothColor(px, py);
        if (InsideShaft(px, py, 0f)) return ShaftColor(px, py);

        if (InsideWrap(px, py, OutlineWidth) || InsideShaft(px, py, OutlineWidth)) return Outline;

        return Nothing;
    }

    // ---------------------------------------------------------------- shapes

    /// <summary>
    /// The stick: a slightly tapered column, its sides nicked here and there so it reads as
    /// split wood rather than as a dowel.
    /// </summary>
    /// <param name="grow">Pixels to widen the shape by, used to draw its outline.</param>
    private static bool InsideShaft(float x, float y, float grow)
    {
        if (y < ShaftBottom - grow || y > ShaftTop + grow) return false;

        float along = Mathf.InverseLerp(ShaftBottom, ShaftTop, y);
        float halfWidth = Mathf.Lerp(ShaftHalfWidthBottom, ShaftHalfWidthTop, along);
        halfWidth -= Noise(0f, y * 0.35f) * 1.2f;

        return Mathf.Abs(x - CentreX) <= halfWidth + grow;
    }

    /// <summary>
    /// The rag wound around the head: a stubby barrel shape, wider than the stick and rounded at
    /// both ends, which is what makes the head read as cloth wrapped on rather than as a block.
    /// </summary>
    /// <param name="grow">Pixels to widen the shape by, used to draw its outline.</param>
    private static bool InsideWrap(float x, float y, float grow)
    {
        if (y < WrapBottom - grow || y > WrapTop + grow) return false;

        // Fullest in the middle and drawn in at both ends, by the same curve on each.
        float middle = (WrapBottom + WrapTop) * 0.5f;
        float fromMiddle = Mathf.Abs(y - middle) / ((WrapTop - WrapBottom) * 0.5f);
        float halfWidth = WrapHalfWidth * Mathf.Sqrt(Mathf.Max(0f, 1f - fromMiddle * fromMiddle * 0.55f));

        return Mathf.Abs(x - CentreX) <= halfWidth + grow;
    }

    /// <summary>
    /// The flame: a teardrop, round where it sits on the cloth and drawn out to a tip, leaning a
    /// little from side to side on the way up. The lean is what stops it reading as a leaf.
    /// </summary>
    /// <param name="grow">Pixels to widen the shape by, used to draw its rim.</param>
    private static bool InsideFlame(float x, float y, float grow)
    {
        if (y < FlameBottom - grow || y > FlameTip + grow) return false;

        return Mathf.Abs(x - FlameAxis(y)) <= FlameHalfWidthAt(y) + grow;
    }

    /// <summary>Where the middle of the flame sits at this height: it sways as it rises.</summary>
    private static float FlameAxis(float y)
    {
        float along = Mathf.InverseLerp(FlameBottom, FlameTip, y);
        return CentreX + Mathf.Sin(along * 4.2f) * 3.4f * along;
    }

    /// <summary>
    /// Half the flame's width at this height: swelling from its base to the waist, then tapering
    /// away to nothing at the tip.
    /// </summary>
    private static float FlameHalfWidthAt(float y)
    {
        if (y <= FlameWaist)
        {
            float rising = Mathf.InverseLerp(FlameBottom, FlameWaist, y);
            return FlameHalfWidth * Mathf.Sqrt(Mathf.Clamp01(rising * (2f - rising)));
        }

        float falling = Mathf.InverseLerp(FlameWaist, FlameTip, y);
        return FlameHalfWidth * Mathf.Pow(Mathf.Clamp01(1f - falling), 0.75f);
    }

    // ---------------------------------------------------------------- fills

    /// <summary>
    /// Wood: a flat base tone with a lit edge down one side, a shadow down the other and a few
    /// darker grain lines running the length of the stick.
    /// </summary>
    private static Color ShaftColor(float x, float y)
    {
        float across = (x - CentreX) / ShaftHalfWidthBottom;

        if (across < -0.55f) return WoodHighlight;
        if (across > 0.5f) return WoodShadow;

        // Grain: long, thin, slightly wandering streaks, sparse enough to be texture rather
        // than stripes.
        return Noise(x * 0.9f, y * 0.12f) > 0.72f ? WoodShadow : Wood;
    }

    /// <summary>
    /// Cloth: the same lighting as the wood, plus the dark line of each turn of the binding
    /// across it.
    /// </summary>
    private static Color ClothColor(float x, float y)
    {
        // Three bindings, drawn as bands rather than as lines so they survive the icon being
        // shown small.
        float band = Mathf.Repeat(y - WrapBottom, 9f);
        if (band < 2.2f) return ClothShadow;

        float across = (x - CentreX) / WrapHalfWidth;
        if (across < -0.5f) return ClothHighlight;
        if (across > 0.45f) return ClothShadow;

        return Cloth;
    }

    /// <summary>
    /// Flame: three bands from the rim inwards. The core is drawn low and narrow — the hottest
    /// part of a flame sits just above what is burning, not in the middle of the shape.
    /// </summary>
    private static Color FlameColor(float x, float y)
    {
        float halfWidth = Mathf.Max(0.001f, FlameHalfWidthAt(y));
        float across = Mathf.Abs(x - FlameAxis(y)) / halfWidth;

        // How far up the flame this pixel is, with the inner bands pulled towards its base.
        float along = Mathf.InverseLerp(FlameBottom, FlameTip, y);

        if (across < 0.42f && along < 0.45f) return FlameCore;
        if (across < 0.68f && along < 0.72f) return FlameMid;

        return FlameOuter;
    }

    // ---------------------------------------------------------------- noise

    /// <summary>
    /// Value noise in 0..1 from a position, hashed rather than drawn from a random stream so the
    /// drawing stays a pure function of the pixel. Smoothed between lattice points, which is what
    /// makes the grain read as fibre running along the stick instead of as static.
    /// </summary>
    private static float Noise(float x, float y)
    {
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float fx = Smooth(x - x0);
        float fy = Smooth(y - y0);

        float bottom = Mathf.Lerp(Lattice(x0, y0), Lattice(x0 + 1, y0), fx);
        float top = Mathf.Lerp(Lattice(x0, y0 + 1), Lattice(x0 + 1, y0 + 1), fx);
        return Mathf.Lerp(bottom, top, fy);
    }

    /// <summary>One lattice point's value, hashed from its coordinates.</summary>
    private static float Lattice(int x, int y)
    {
        return DeterministicRandom.Hash($"torch:{x}:{y}") / (float)uint.MaxValue;
    }

    /// <summary>Smoothstep, so the noise has no visible lattice corners.</summary>
    private static float Smooth(float t) => t * t * (3f - 2f * t);
}
