using UnityEngine;

/// <summary>
/// Draws the broken-glass patches (GU-0073) procedurally, so the prop has real art instead of a
/// tinted placeholder square. Used by <see cref="BrokenGlassSetup"/>, which writes the PNGs and
/// builds the prefab around them.
///
/// The drawing is a fracture rather than a scatter of random triangles: a pane is broken at one
/// point, cracks radiate out from it, and the wedges between them are then pulled apart a little
/// and shrunk away from their own edges. That is what makes a patch read as one thing that broke
/// — the pieces still line up across the gaps — where independently placed shards read as litter.
///
/// Every shape is decided in <see cref="BuildShards"/> and every pixel in
/// <see cref="PixelColor"/>: both are pure and take a <see cref="DeterministicRandom"/>, so the
/// same variant name always draws the same PNG (no repository churn on a re-run) and the drawing
/// can be rendered and inspected outside the editor.
/// </summary>
public static class BrokenGlassArt
{
    /// <summary>Texture side in pixels. With <see cref="PixelsPerUnit"/> this is the patch size.</summary>
    public const int TexturePixels = 96;

    /// <summary>
    /// PPU chosen so the texture covers <see cref="BrokenGlassSetup.PatchSize"/> world units
    /// exactly, which is what keeps the drawn glass the same size as the trigger the player
    /// steps into.
    /// </summary>
    public const int PixelsPerUnit = 80;

    /// <summary>How many wedges the pane breaks into. More reads as finer, thinner splinters.</summary>
    private const int MinWedges = 7;
    private const int MaxWedges = 10;

    /// <summary>
    /// Where the crack ring between the inner and outer shards sits, as a fraction of a wedge's
    /// length. Real glass breaks into both stubby pieces near the impact and long ones further
    /// out, and one ring is enough to get both without the patch turning into gravel.
    /// </summary>
    private const float RingFraction = 0.45f;

    /// <summary>How far a shard is pulled away from its own edges, around its centre. Below 1, so
    /// the cracks open into visible gaps instead of the pieces still touching.</summary>
    private const float ShardShrink = 0.88f;

    /// <summary>Chance a piece was kicked clear of the rest instead of settling where it broke.
    /// Without a few of these the patch reads as an intact pane with cracks drawn on it.</summary>
    private const float OutlierChance = 0.22f;

    /// <summary>How far a kicked piece slides, in pixels.</summary>
    private const float MinOutlierSlide = 6f;
    private const float MaxOutlierSlide = 16f;

    /// <summary>How much smaller a kicked piece is: the ones that travel are the small ones.</summary>
    private const float OutlierShrink = 0.7f;

    /// <summary>How far from a piece the powder reaches, in pixels.</summary>
    private const float DustReach = 9f;

    /// <summary>Chance of a speck of powder right beside a piece, falling off to none at
    /// <see cref="DustReach"/>.</summary>
    private const float DustChance = 0.16f;

    /// <summary>Direction the light falls from, in texture space. Every shard's lit edge faces it.</summary>
    private static readonly Vector2 LightDirection = new Vector2(-0.5f, 0.87f);

    /// <summary>Glass seen against the dungeon's blue-grey floor: pale, cold and barely opaque.</summary>
    private static readonly Color GlassColor = new Color(0.72f, 0.87f, 0.93f, 0.22f);

    /// <summary>An edge catching the light. Nearly white and nearly solid — this is the glint that
    /// makes a shard readable at all on a dark floor.</summary>
    private static readonly Color GlintColor = new Color(0.94f, 0.99f, 1f, 0.92f);

    /// <summary>An edge facing away from the light: the shard's own thickness, in shadow.</summary>
    private static readonly Color EdgeShadowColor = new Color(0.15f, 0.21f, 0.24f, 0.6f);

    /// <summary>Powdered glass around the pieces, the part that is too small to have shape.</summary>
    private static readonly Color DustColor = new Color(0.82f, 0.92f, 0.96f, 0.5f);

    /// <summary>Fully transparent; the floor shows through everywhere the glass is not.</summary>
    private static readonly Color Nothing = new Color(0f, 0f, 0f, 0f);

    /// <summary>How wide an edge is drawn, in pixels.</summary>
    private const float EdgeWidth = 1.3f;

    /// <summary>One piece of the broken pane: a convex polygon of three or four points.</summary>
    public struct Shard
    {
        /// <summary>Corners in order around the piece. Only the first <see cref="Count"/> are used.</summary>
        public Vector2 A, B, C, D;

        /// <summary>3 for a wedge tip, 4 for a piece cut out of the ring beyond it.</summary>
        public int Count;

        /// <summary>Brightness of this piece, around 1. Pieces lie at slightly different angles,
        /// and it is that difference — not the outline — that separates two shards sharing a crack.</summary>
        public float Tone;

        /// <summary>The corner at <paramref name="index"/>, wrapping around the polygon.</summary>
        public Vector2 Corner(int index)
        {
            switch (index % Count)
            {
                case 0: return A;
                case 1: return B;
                case 2: return C;
                default: return D;
            }
        }
    }

    /// <summary>
    /// Draws one glass patch as a PNG. The seed is the variant's own name, so re-running the
    /// setup produces byte-identical files and does not churn the repository.
    /// </summary>
    /// <param name="seed">Name of the variant being drawn.</param>
    public static byte[] BuildTexture(string seed)
    {
        var random = new DeterministicRandom(seed);
        Shard[] shards = BuildShards(random);

        // Its own stream, so changing how many pieces the pane breaks into does not also
        // rearrange every speck of dust around them.
        var dust = random.Derive("dust");

        var texture = new Texture2D(TexturePixels, TexturePixels, TextureFormat.RGBA32, false);
        try
        {
            for (int y = 0; y < TexturePixels; y++)
            {
                for (int x = 0; x < TexturePixels; x++)
                {
                    Color color = PixelColor(shards, x, y);
                    if (color.a <= 0f) color = DustPixel(dust, shards, x, y);

                    texture.SetPixel(x, y, color);
                }
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
    /// Breaks one pane and returns the pieces. The impact point is off-centre and the cracks are
    /// unevenly spaced, because a fracture radiating evenly from the middle reads as a drawn
    /// asterisk rather than as damage.
    /// </summary>
    /// <param name="random">Stream deciding this variant's fracture.</param>
    public static Shard[] BuildShards(DeterministicRandom random)
    {
        float half = TexturePixels / 2f;
        var impact = new Vector2(
            half + (random.NextFloat() * 2f - 1f) * TexturePixels * 0.12f,
            half + (random.NextFloat() * 2f - 1f) * TexturePixels * 0.12f);

        int wedges = random.RangeInclusive(MinWedges, MaxWedges);
        var directions = new Vector2[wedges];
        var reach = new float[wedges];

        for (int i = 0; i < wedges; i++)
        {
            // Evenly spaced angles wobbled by up to half a step: the wedges stay in order around
            // the impact (so the pieces still tile the pane) while none of them match in width.
            float step = 360f / wedges;
            float angle = (i + (random.NextFloat() - 0.5f) * 0.7f) * step;
            float radians = angle * Mathf.Deg2Rad;

            directions[i] = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
            reach[i] = TexturePixels * (0.24f + random.NextFloat() * 0.18f);
        }

        var shards = new Shard[wedges * 2];
        for (int i = 0; i < wedges; i++)
        {
            int next = (i + 1) % wedges;

            Vector2 innerA = impact + directions[i] * reach[i] * RingFraction;
            Vector2 innerB = impact + directions[next] * reach[next] * RingFraction;
            Vector2 outerA = impact + directions[i] * reach[i];
            Vector2 outerB = impact + directions[next] * reach[next];

            shards[i * 2] = Shrink(new Shard
            {
                A = impact, B = innerA, C = innerB, Count = 3,
                Tone = 0.85f + random.NextFloat() * 0.35f,
            });

            shards[i * 2 + 1] = Shrink(new Shard
            {
                A = innerA, B = outerA, C = outerB, D = innerB, Count = 4,
                Tone = 0.85f + random.NextFloat() * 0.35f,
            });
        }

        for (int i = 0; i < shards.Length; i++)
        {
            if (!random.Chance(OutlierChance)) continue;

            // Slid outward along the direction it broke away in, so the gap it left behind is
            // still where it came from and the patch keeps reading as one impact.
            Vector2 centre = Centre(shards[i]);
            Vector2 away = (centre - impact).normalized;
            float slide = MinOutlierSlide + random.NextFloat() * (MaxOutlierSlide - MinOutlierSlide);

            shards[i] = Translate(Resize(shards[i], OutlierShrink), away * slide);
        }

        return shards;
    }

    /// <summary>
    /// Pulls a piece in towards its own centre, which is what opens the cracks between the
    /// pieces into gaps the floor shows through.
    /// </summary>
    private static Shard Shrink(Shard shard) => Resize(shard, ShardShrink);

    /// <summary>The middle of a piece: the average of its corners.</summary>
    private static Vector2 Centre(Shard shard)
    {
        Vector2 centre = Vector2.zero;
        for (int i = 0; i < shard.Count; i++) centre += shard.Corner(i);
        return centre / shard.Count;
    }

    /// <summary>Resizes a piece around its own centre, leaving it where it is.</summary>
    private static Shard Resize(Shard shard, float factor)
    {
        Vector2 centre = Centre(shard);

        shard.A = centre + (shard.A - centre) * factor;
        shard.B = centre + (shard.B - centre) * factor;
        shard.C = centre + (shard.C - centre) * factor;
        shard.D = centre + (shard.D - centre) * factor;
        return shard;
    }

    /// <summary>Moves a piece bodily by an offset.</summary>
    private static Shard Translate(Shard shard, Vector2 offset)
    {
        shard.A += offset;
        shard.B += offset;
        shard.C += offset;
        shard.D += offset;
        return shard;
    }

    /// <summary>
    /// Colour of one pixel of the patch: transparent outside every piece, a faint wash of glass
    /// inside one, and a bright or shadowed line along whichever edge it sits on.
    ///
    /// The nearest edge decides the outline, and which side of the light it faces decides whether
    /// that outline is a glint or a shadow — the same piece is therefore drawn lit on one side and
    /// dark on the other, which is what gives a flat polygon the look of a piece with thickness.
    /// </summary>
    /// <param name="shards">Pieces from <see cref="BuildShards"/>.</param>
    /// <param name="x">Pixel column.</param>
    /// <param name="y">Pixel row.</param>
    public static Color PixelColor(Shard[] shards, int x, int y)
    {
        var point = new Vector2(x + 0.5f, y + 0.5f);

        for (int i = 0; i < shards.Length; i++)
        {
            Shard shard = shards[i];
            if (!Contains(shard, point)) continue;

            float distance = DistanceToEdge(shard, point, out Vector2 outward);
            if (distance > EdgeWidth) return Scale(GlassColor, shard.Tone);

            // Fades over the outermost pixel so an edge is a drawn line rather than a jagged
            // staircase; the sprite is drawn small and the stepping would be the loudest thing on it.
            float edge = Mathf.Clamp01(EdgeWidth - distance);
            bool lit = Vector2.Dot(outward, LightDirection) > 0.1f;
            Color edgeColor = lit ? Scale(GlintColor, shard.Tone) : EdgeShadowColor;

            return Blend(Scale(GlassColor, shard.Tone), edgeColor, edge);
        }

        return Nothing;
    }

    /// <summary>
    /// Powdered glass: single specks around the pieces, densest right against them and gone by
    /// <see cref="DustReach"/> pixels away. Returns transparent where no speck falls.
    ///
    /// Keying the powder to the pieces rather than to the middle of the texture is what keeps it
    /// looking like glass that shattered — it follows the shape of the break, including out to a
    /// piece that slid away — instead of like grain sprinkled over the whole square.
    /// </summary>
    /// <param name="random">Stream deciding where the powder falls and how bright it is.</param>
    /// <param name="shards">Pieces from <see cref="BuildShards"/>.</param>
    /// <param name="x">Pixel column.</param>
    /// <param name="y">Pixel row.</param>
    public static Color DustPixel(DeterministicRandom random, Shard[] shards, int x, int y)
    {
        var point = new Vector2(x + 0.5f, y + 0.5f);

        float nearest = float.MaxValue;
        for (int i = 0; i < shards.Length; i++)
            nearest = Mathf.Min(nearest, DistanceToEdge(shards[i], point, out _));

        // Squared, so the powder crowds the pieces instead of spreading evenly to the cutoff.
        float density = Mathf.Clamp01(1f - nearest / DustReach);
        if (!random.Chance(DustChance * density * density)) return Nothing;

        return Scale(DustColor, 0.6f + random.NextFloat() * 0.6f);
    }

    /// <summary>True when the point lies inside the convex piece.</summary>
    private static bool Contains(Shard shard, Vector2 point)
    {
        bool positive = false;
        bool negative = false;

        for (int i = 0; i < shard.Count; i++)
        {
            Vector2 from = shard.Corner(i);
            Vector2 to = shard.Corner(i + 1);
            float side = Cross(to - from, point - from);

            if (side > 0f) positive = true;
            else if (side < 0f) negative = true;

            // Corners on both sides of an edge means the point is outside a convex polygon.
            if (positive && negative) return false;
        }

        return true;
    }

    /// <summary>
    /// Distance from the point to the closest edge of the piece, with that edge's outward normal.
    /// </summary>
    private static float DistanceToEdge(Shard shard, Vector2 point, out Vector2 outward)
    {
        float best = float.MaxValue;
        outward = Vector2.up;

        for (int i = 0; i < shard.Count; i++)
        {
            Vector2 from = shard.Corner(i);
            Vector2 to = shard.Corner(i + 1);
            Vector2 edge = to - from;
            float length = edge.magnitude;
            if (length < 0.0001f) continue;

            float along = Mathf.Clamp01(Vector2.Dot(point - from, edge) / (length * length));
            float distance = Vector2.Distance(point, from + edge * along);
            if (distance >= best) continue;

            best = distance;
            // The polygons are wound consistently by BuildShards, so one perpendicular of the
            // edge always points out of the piece.
            outward = new Vector2(edge.y, -edge.x).normalized;
        }

        return best;
    }

    /// <summary>Cross product of two 2D vectors: positive when b turns left of a.</summary>
    private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

    /// <summary>Brightens or dims a colour by a factor, leaving its alpha alone.</summary>
    private static Color Scale(Color color, float factor)
    {
        return new Color(
            Mathf.Clamp01(color.r * factor),
            Mathf.Clamp01(color.g * factor),
            Mathf.Clamp01(color.b * factor),
            color.a);
    }

    /// <summary>Mixes two colours, alpha included, by <paramref name="amount"/> of the second.</summary>
    private static Color Blend(Color from, Color to, float amount)
    {
        return Color.Lerp(from, to, Mathf.Clamp01(amount));
    }
}
