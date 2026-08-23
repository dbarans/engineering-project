using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Shared drawing toolkit for the smooth generated art — the item icons and the UI bars.
///
/// Deliberately a different technique from the tile and prop generators. Those decide each
/// pixel with a chain of hard tests, which is right for 32-pixel tiles that have to sit on a
/// grid and stay crisp under point filtering. An inventory icon has none of those
/// constraints: it is 128 pixels, it is never tiled, and it is drawn over a dark panel where
/// a stair-stepped edge is the first thing the eye finds.
///
/// The look it is aiming at is the project's hand-drawn furniture — the barrel, the table,
/// the typewriter: thick uneven ink around every silhouette, flat desaturated fills with a
/// blotchy wash over them, and detail drawn as strokes rather than modelled with light.
/// Explicitly not a rendered look; there are no specular highlights or smooth gradients in
/// the reference and there should be none here.
///
/// So shapes here are <b>signed distance fields</b>. Every primitive answers "how far is this
/// point from my edge, negative inside", which gives three things a per-pixel switch cannot:
/// an anti-aliased edge for free (coverage is a smoothstep over the last pixel and a half),
/// an outline for free (the band just outside the edge), and shading that knows how deep
/// inside the shape it is, which is what makes a form look round rather than flat.
///
/// Nothing here touches <see cref="Texture2D"/> until <see cref="ArtCanvas.EncodePng"/>, so
/// every drawing built on it can be rendered and looked at outside Unity.
/// </summary>
public static class ProceduralArt
{
    /// <summary>
    /// Width of the anti-aliased band at a shape's edge, in pixels. A pixel and a half:
    /// under one and the edge still steps visibly, much over two and small shapes go soft
    /// enough to look out of focus.
    /// </summary>
    public const float EdgeSoftness = 1.5f;

    /// <summary>Fully transparent.</summary>
    public static readonly Color Nothing = new Color(0f, 0f, 0f, 0f);

    // ---------------------------------------------------------------- canvas

    /// <summary>
    /// A plain RGBA buffer with alpha compositing. Row-major from the bottom, matching
    /// <see cref="Texture2D"/>'s convention so an encoded canvas comes out the right way up.
    /// </summary>
    public sealed class ArtCanvas
    {
        public readonly int Width;
        public readonly int Height;
        public readonly Color[] Pixels;

        public ArtCanvas(int width, int height)
        {
            Width = width;
            Height = height;
            Pixels = new Color[width * height];
        }

        /// <summary>
        /// Composites a colour over what is already there, at the given coverage. Straight
        /// (non-premultiplied) alpha, because that is what Unity's importer expects and what
        /// <c>alphaIsTransparency</c> assumes.
        /// </summary>
        public void Blend(int x, int y, Color color, float alpha)
        {
            if (x < 0 || y < 0 || x >= Width || y >= Height) return;

            alpha = Mathf.Clamp01(alpha) * color.a;
            if (alpha <= 0f) return;

            Color under = Pixels[y * Width + x];
            float outAlpha = alpha + under.a * (1f - alpha);
            if (outAlpha <= 0f)
            {
                Pixels[y * Width + x] = Nothing;
                return;
            }

            // Un-premultiplied blend: colours are weighted by how much of the result each
            // one owns, which is not the same as weighting by coverage alone once the
            // surface underneath is itself partly transparent.
            float weight = alpha / outAlpha;
            Pixels[y * Width + x] = new Color(
                Mathf.Lerp(under.r, color.r, weight),
                Mathf.Lerp(under.g, color.g, weight),
                Mathf.Lerp(under.b, color.b, weight),
                outAlpha);
        }

        /// <summary>The pixel's centre in canvas coordinates, which is where a field is sampled.</summary>
        public Vector2 Point(int x, int y) => new Vector2(x + 0.5f, y + 0.5f);

        /// <summary>The canvas centre, as the origin most shapes are placed relative to.</summary>
        public Vector2 Centre => new Vector2(Width * 0.5f, Height * 0.5f);

        public byte[] EncodePng()
        {
            var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels(Pixels);
                texture.Apply();
                return texture.EncodeToPNG();
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
        }
    }

    // ---------------------------------------------------------------- coverage

    /// <summary>
    /// How much of a pixel a shape covers, from its signed distance. One well inside, zero
    /// well outside, smooth across the edge — this is the whole anti-aliasing story.
    /// </summary>
    public static float Coverage(float distance)
    {
        return Mathf.Clamp01(0.5f - distance / EdgeSoftness);
    }

    /// <summary>
    /// Coverage of the outline band hugging a shape's edge: inside the shape but within
    /// <paramref name="width"/> of leaving it. Drawn as a band rather than as a larger shape
    /// underneath, so an outline never bleeds past a neighbouring form drawn later.
    /// </summary>
    public static float EdgeBand(float distance, float width)
    {
        return Mathf.Min(Coverage(distance), Coverage(-distance - width));
    }

    // ---------------------------------------------------------------- signed distance fields

    /// <summary>Distance to a circle's edge, negative inside.</summary>
    public static float Circle(Vector2 point, Vector2 centre, float radius)
    {
        return (point - centre).magnitude - radius;
    }

    /// <summary>
    /// Distance to an axis-aligned box with rounded corners, negative inside. The workhorse:
    /// a bottle body, a bandage roll, a bar track and an axe haft are all this shape with
    /// different corner radii.
    /// </summary>
    public static float RoundedBox(Vector2 point, Vector2 centre, Vector2 halfSize, float radius)
    {
        radius = Mathf.Min(radius, Mathf.Min(halfSize.x, halfSize.y));
        Vector2 q = new Vector2(
            Mathf.Abs(point.x - centre.x) - (halfSize.x - radius),
            Mathf.Abs(point.y - centre.y) - (halfSize.y - radius));

        float outside = new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude;
        float inside = Mathf.Min(Mathf.Max(q.x, q.y), 0f);
        return outside + inside - radius;
    }

    /// <summary>
    /// Distance to a thick line segment with round caps, negative inside. Everything long and
    /// thin is drawn with this — an axe haft, a bandage's loose end, a bone.
    /// </summary>
    public static float Segment(Vector2 point, Vector2 from, Vector2 to, float radius)
    {
        Vector2 pa = point - from;
        Vector2 ba = to - from;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude - radius;
    }

    /// <summary>
    /// Approximate distance to an axis-aligned ellipse, negative inside. Approximate on
    /// purpose: the exact field needs a root solve per pixel, and the error here is well
    /// under the anti-aliasing width for the mild aspect ratios anything is drawn at.
    /// </summary>
    public static float Ellipse(Vector2 point, Vector2 centre, Vector2 radii)
    {
        Vector2 d = new Vector2((point.x - centre.x) / radii.x, (point.y - centre.y) / radii.y);
        float length = d.magnitude;
        if (length <= 0f) return -Mathf.Min(radii.x, radii.y);

        // Scale the normalised overshoot back by the local radius, which is what turns a
        // ratio into something measured in pixels.
        float scale = Mathf.Min(radii.x, radii.y);
        return (length - 1f) * scale;
    }

    /// <summary>The union of two shapes: whichever edge is nearer.</summary>
    public static float Union(float a, float b) => Mathf.Min(a, b);

    /// <summary>The part of <paramref name="a"/> that is not in <paramref name="b"/>.</summary>
    public static float Subtract(float a, float b) => Mathf.Max(a, -b);

    /// <summary>The overlap of two shapes.</summary>
    public static float Intersect(float a, float b) => Mathf.Max(a, b);

    /// <summary>
    /// Union with the seam between the two shapes rounded off over <paramref name="radius"/>,
    /// so they read as one moulded form rather than as two things touching. What makes a
    /// bottle's neck grow out of its shoulder instead of being stuck on it.
    /// </summary>
    public static float SmoothUnion(float a, float b, float radius)
    {
        float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / radius);
        return Mathf.Lerp(b, a, h) - radius * h * (1f - h);
    }

    /// <summary>Rotates a point about a centre, for shapes that are not axis-aligned.</summary>
    public static Vector2 Rotate(Vector2 point, Vector2 centre, float degrees)
    {
        float radians = degrees * Mathf.Deg2Rad;
        float sin = Mathf.Sin(radians);
        float cos = Mathf.Cos(radians);
        Vector2 d = point - centre;
        return centre + new Vector2(d.x * cos - d.y * sin, d.x * sin + d.y * cos);
    }

    // ---------------------------------------------------------------- hand-drawn ink

    /// <summary>
    /// Perturbs a field with low-frequency noise, so a contour is never geometrically
    /// perfect. The hand-drawn art this set has to sit with (the barrel, the table, the
    /// typewriter) has no straight edges and no true circles anywhere in it, and a shape
    /// that does reads as a different asset the moment the two are next to each other.
    /// </summary>
    public static float Wobble(float distance, Vector2 point, float amount, float scale, uint salt)
    {
        return distance + Noise(point, scale, salt, 2) * amount * 2f;
    }

    /// <summary>
    /// How wide the ink line is at this point along a contour. Varies like a brush pen
    /// pressed harder in some places than others, which is the single most recognisable
    /// thing about the reference art — a border of constant width reads as a stroke applied
    /// by a program, however dark it is.
    /// </summary>
    public static float InkWidth(Vector2 point, float min, float max, uint salt, float scale = 0.045f)
    {
        return Mathf.Lerp(min, max, Mathf.Clamp01(Noise(point, scale, salt, 2) * 2f + 0.5f));
    }

    // ---------------------------------------------------------------- shading

    /// <summary>
    /// Light direction, pointing towards the light. Up and slightly left, and the same for
    /// every icon — a set of icons lit from different directions reads as a set of icons
    /// borrowed from different places.
    /// </summary>
    public static readonly Vector2 LightDirection = new Vector2(-0.45f, 1f).normalized;

    /// <summary>
    /// Shades a point on a form as though it were rounded: the surface normal is inferred
    /// from how far inside the shape the point is, so the lit side comes up and the far side
    /// falls into shadow.
    ///
    /// <paramref name="depth"/> is how far the form reads as extending back from its edge —
    /// small for something flat like a scrap of metal, large for something round like a
    /// bottle.
    /// </summary>
    public static Color Round(Color baseColor, Vector2 point, Vector2 centre, float distance,
        float depth, float strength = 0.5f)
    {
        // Inside the shape distance is negative; how far in, relative to depth, is how much
        // the surface has turned to face the viewer.
        float inward = Mathf.Clamp01(-distance / depth);
        float facing = Mathf.Sqrt(inward);

        Vector2 outward = (point - centre).sqrMagnitude > 0.0001f
            ? (point - centre).normalized
            : Vector2.zero;

        float lit = Vector2.Dot(outward, LightDirection) * (1f - facing);
        return Shade(baseColor, lit * strength);
    }

    /// <summary>
    /// A straight top-to-bottom ramp across a shape, for forms whose roundness runs one way
    /// only — a bar fill, a bandage roll, the shaft of a haft.
    /// </summary>
    public static Color Ramp(Color top, Color bottom, float t)
    {
        return Color.Lerp(bottom, top, Mathf.Clamp01(t));
    }

    /// <summary>Lightens (positive amount) or darkens (negative) a colour, keeping its alpha.</summary>
    public static Color Shade(Color color, float amount)
    {
        return new Color(
            Mathf.Clamp01(color.r + amount),
            Mathf.Clamp01(color.g + amount),
            Mathf.Clamp01(color.b + amount),
            color.a);
    }

    // ---------------------------------------------------------------- noise

    /// <summary>
    /// Smooth fractal noise in roughly <c>[-0.5, 0.5]</c>, for material texture — rust on
    /// iron, grain in wood, weave in cloth.
    ///
    /// Applied as a shade rather than as per-pixel jitter, which is the difference between
    /// texture and grain: jitter at this resolution reads as noise laid over a smooth image,
    /// while a field this size reads as the surface itself.
    /// </summary>
    public static float Noise(Vector2 point, float scale, uint salt, int octaves = 3)
    {
        float sum = 0f;
        float total = 0f;
        float amplitude = 1f;
        float frequency = scale;

        for (int octave = 0; octave < octaves; octave++)
        {
            sum += amplitude * (ValueNoise(point * frequency, salt + (uint)octave * 0x9E3779B9u) - 0.5f);
            total += amplitude;
            amplitude *= 0.5f;
            frequency *= 2f;
        }

        return sum / total;
    }

    private static float ValueNoise(Vector2 point, uint salt)
    {
        int x0 = Mathf.FloorToInt(point.x);
        int y0 = Mathf.FloorToInt(point.y);
        float fx = point.x - x0;
        float fy = point.y - y0;

        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);

        float bottom = Mathf.Lerp(Lattice(x0, y0, salt), Lattice(x0 + 1, y0, salt), fx);
        float top = Mathf.Lerp(Lattice(x0, y0 + 1, salt), Lattice(x0 + 1, y0 + 1, salt), fx);
        return Mathf.Lerp(bottom, top, fy);
    }

    private static float Lattice(int x, int y, uint salt)
    {
        return (DeterministicRandom.Hash(salt, x, y) >> 8) / (float)(1 << 24);
    }

    // ---------------------------------------------------------------- asset writing

    /// <summary>
    /// Writes a canvas to disk and imports it as a sprite.
    ///
    /// Bilinear filtering, unlike everything in <see cref="DungeonSceneSetup"/>: these are
    /// drawn with soft edges and displayed at sizes that have nothing to do with their pixel
    /// grid, so point filtering would throw away the anti-aliasing they are made of.
    /// </summary>
    public static Sprite WriteSprite(ArtCanvas canvas, string path, float pixelsPerUnit,
        Vector4 border = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllBytes(path, canvas.EncodePng());
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);

        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = pixelsPerUnit;
            importer.spriteBorder = border;
            importer.filterMode = FilterMode.Bilinear;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null)
        {
            // First write of a file the database has never seen: SaveAndReimport can leave
            // the sub-asset not yet queryable, and the load comes back null even though the
            // import succeeded. One refresh and a second look settles it.
            AssetDatabase.Refresh();
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }

        if (sprite == null) Debug.LogError($"[ProceduralArt] No sprite imported from '{path}'.");
        return sprite;
    }
}
