using System;
using UnityEditor;
using UnityEngine;
using static ProceduralArt;

/// <summary>
/// Draws inventory icons for the crafting items and points their <see cref="ItemData"/>
/// assets at the result.
///
/// The items shipped on icons from the GDS pack, which are competent but belong to a
/// brighter, cleaner game than this one. These are drawn to match the project's own
/// hand-drawn furniture instead — <c>FURNITURE_pngy_beczka</c>, <c>_stol</c>, <c>_maszyna</c>
/// — because those are what the player actually sees in a room, and an icon in a different
/// idiom reads as borrowed the moment the two are on screen together.
///
/// Four things carry that idiom, and every icon here is built out of them:
/// <list type="bullet">
/// <item>a thick ink line round every silhouette, <b>uneven along its length</b>, the way a
/// brush pen is pressed harder in some places than others;</item>
/// <item>flat desaturated fills with a blotchy wash over them — no gradients;</item>
/// <item>detail drawn as ink strokes (plank seams, cracks, wrap lines) rather than modelled
/// with light;</item>
/// <item>contours that wander, so nothing is a true circle or a straight edge.</item>
/// </list>
///
/// There is deliberately no specular highlight and no rounded shading anywhere in this file.
/// An earlier pass had both and came out looking like small 3D renders — technically smooth,
/// and completely foreign to the barrel standing next to them.
///
/// <b>Nothing is deleted.</b> The GDS icons stay where they are; this only repoints the
/// <c>icon</c> field on each item, so reverting is a matter of dragging the old sprite back.
///
/// Every drawing is seeded from a fixed salt, so re-running produces byte-identical files
/// and does not churn the repository.
/// </summary>
public static class ItemIconGenerator
{
    private const string IconFolder = "Assets/Generation/Icons";

    /// <summary>Icon side in pixels, matching the GDS sheet it replaces.</summary>
    private const int IconPixels = 128;

    /// <summary>Same pixels-per-unit as the GDS icons, so a dropped item keeps its world size.</summary>
    private const float IconPixelsPerUnit = 100f;

    // The ink. Not pure black — the reference art's line is a very dark warm brown, which is
    // what stops it looking printed.
    private static readonly Color Ink = new Color32(0x1B, 0x17, 0x13, 0xFF);

    /// <summary>
    /// Ink line width in pixels, wandering between these along a contour. Thick: at 128
    /// pixels the reference line is five or six pixels, and an icon drawn with a hairline
    /// border does not read as belonging to it however good the fill is.
    /// </summary>
    private const float InkMin = 4.5f;
    private const float InkMax = 8.5f;

    /// <summary>How far a silhouette is allowed to wander off its ideal shape, in pixels.</summary>
    private const float ContourWobble = 2.1f;

    private static readonly Color GlassColor = new Color32(0x3A, 0x40, 0x42, 0xFF);
    private static readonly Color InkFluidColor = new Color32(0x1E, 0x20, 0x26, 0xFF);
    private static readonly Color CorkColor = new Color32(0x8A, 0x6E, 0x46, 0xFF);

    private static readonly Color LinenColor = new Color32(0xC0, 0xB4, 0x98, 0xFF);
    private static readonly Color LinenShadow = new Color32(0x8E, 0x82, 0x69, 0xFF);

    private static readonly Color IronColor = new Color32(0x6E, 0x71, 0x70, 0xFF);
    private static readonly Color IronDark = new Color32(0x44, 0x47, 0x47, 0xFF);
    private static readonly Color RustColor = new Color32(0x7C, 0x54, 0x36, 0xFF);
    private static readonly Color EdgeColor = new Color32(0xAE, 0xB2, 0xB0, 0xFF);

    private static readonly Color LeadColor = new Color32(0x7C, 0x7E, 0x80, 0xFF);
    private static readonly Color PaperColor = new Color32(0xCB, 0xC0, 0xA4, 0xFF);
    private static readonly Color CordColor = new Color32(0x4E, 0x3E, 0x2A, 0xFF);

    private static readonly Color BrassColor = new Color32(0xA8, 0x92, 0x5A, 0xFF);
    private static readonly Color HullColor = new Color32(0x7A, 0x39, 0x30, 0xFF);

    // Straight off the barrel and the table: a warm desaturated brown, dark and dusty.
    private static readonly Color BarkColor = new Color32(0x54, 0x44, 0x33, 0xFF);
    private static readonly Color WoodColor = new Color32(0x7E, 0x6A, 0x51, 0xFF);
    private static readonly Color WoodDark = new Color32(0x5A, 0x4A, 0x38, 0xFF);

    /// <summary>One generated icon: what to draw and which item asset receives it.</summary>
    private readonly struct IconSpec
    {
        public readonly string Name;
        public readonly string ItemPath;
        public readonly Action<ArtCanvas> Draw;

        public IconSpec(string name, string itemPath, Action<ArtCanvas> draw)
        {
            Name = name;
            ItemPath = itemPath;
            Draw = draw;
        }
    }

    private static readonly IconSpec[] Icons =
    {
        new IconSpec("IconInk", "Assets/Items/Item 6 - Ink.asset", DrawInk),
        new IconSpec("IconBandage", "Assets/Items/Item 16 - Bandage.asset", DrawBandage),
        new IconSpec("IconScrap", "Assets/Items/Item 8 - Scrap.asset", DrawScrap),
        new IconSpec("IconBullet", "Assets/Items/Item 7 - Bullet.asset", DrawBullet),
        new IconSpec("IconShell", "Assets/Items/Item 12 - Shell.asset", DrawShell),
        new IconSpec("IconWood", "Assets/Items/Item 3 - Wood.asset", DrawWood),
        new IconSpec("IconAxe", "Assets/Items/Item 4 - Axe.asset", DrawAxe)
    };

    /// <summary>
    /// Draws every icon, imports it and assigns it to its item. Idempotent and safe to
    /// re-run: the drawings are deterministic and the only thing written to an item is the
    /// sprite reference.
    /// </summary>
    [MenuItem("Tools/Art/Regenerate Item Icons")]
    public static void RegenerateIcons()
    {
        int assigned = 0;
        foreach (IconSpec spec in Icons)
        {
            var canvas = new ArtCanvas(IconPixels, IconPixels);
            spec.Draw(canvas);

            Sprite sprite = WriteSprite(canvas, $"{IconFolder}/{spec.Name}.png", IconPixelsPerUnit);
            if (sprite == null) continue;
            if (AssignToItem(spec.ItemPath, sprite)) assigned++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[ItemIcons] Redrew {Icons.Length} icons in '{IconFolder}' " +
                  $"({assigned} items re-pointed). The GDS icons they replace are untouched.");
    }

    /// <summary>Points an item's serialized icon field at the sprite. Returns whether it changed.</summary>
    private static bool AssignToItem(string itemPath, Sprite sprite)
    {
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(itemPath);
        if (item == null)
        {
            Debug.LogWarning($"[ItemIcons] No item at '{itemPath}' — icon drawn but not assigned.");
            return false;
        }

        var serialized = new SerializedObject(item);
        SerializedProperty icon = serialized.FindProperty("icon");
        if (icon == null)
        {
            Debug.LogWarning($"[ItemIcons] '{itemPath}' has no 'icon' field — nothing assigned.");
            return false;
        }

        if (icon.objectReferenceValue == sprite) return false;

        icon.objectReferenceValue = sprite;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(item);
        return true;
    }

    // ---------------------------------------------------------------- the two primitives

    /// <summary>
    /// Renders one shape: contour wobbled off its ideal, filled flat, and inked round the
    /// edge with a line whose width wanders.
    ///
    /// Every form on every icon goes through here, which is what holds the set together —
    /// the same ink weight, the same wobble, the same compositing.
    /// </summary>
    private static void Fill(ArtCanvas canvas, Func<Vector2, float> field,
        Func<Vector2, float, Color> wash, uint salt, bool inked = true)
    {
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                Vector2 point = canvas.Point(x, y);
                float distance = Wobble(field(point), point, ContourWobble, 0.06f, salt);

                float coverage = Coverage(distance);
                if (coverage <= 0f) continue;

                Color color = wash(point, distance);
                if (inked)
                {
                    float width = InkWidth(point, InkMin, InkMax, salt ^ 0x5BD1E995u);
                    color = Color.Lerp(color, Ink, EdgeBand(distance, width));
                }

                canvas.Blend(x, y, color, coverage);
            }
        }
    }

    /// <summary>
    /// Draws an ink stroke — a plank seam, a crack, a wrap line — clipped to the form it is
    /// drawn on. Tapered at both ends and wobbling along its length, because a straight line
    /// of constant width is the one thing that would give the whole set away as generated.
    /// </summary>
    private static void Stroke(ArtCanvas canvas, Func<Vector2, float> clip, Vector2 from,
        Vector2 to, float width, uint salt, float alpha = 0.85f)
    {
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                Vector2 point = canvas.Point(x, y);

                // Only on the form, and pulled in from its edge so a seam never runs into
                // the ink line round the outside.
                if (clip(point) > -InkMin) continue;

                Vector2 axis = to - from;
                float along = Mathf.Clamp01(Vector2.Dot(point - from, axis) / Vector2.Dot(axis, axis));

                // Thickest in the middle, tapering to nothing at both ends.
                float taper = Mathf.Sin(along * Mathf.PI);
                float here = width * Mathf.Lerp(0.35f, 1f, taper);

                float distance = Wobble(Segment(point, from, to, here), point, 1.3f, 0.09f, salt);
                float coverage = Coverage(distance);
                if (coverage <= 0f) continue;

                canvas.Blend(x, y, Ink, coverage * alpha);
            }
        }
    }

    /// <summary>
    /// A flat fill with a blotchy wash and a slight darkening towards the contour. This is
    /// the whole shading model — the reference art has no light source, only a dusty wash
    /// that pools at the edges of a form.
    /// </summary>
    private static Func<Vector2, float, Color> Wash(Color baseColor, uint salt, float blotch = 0.09f)
    {
        return (point, distance) =>
        {
            // Two octaves at very different scales: broad patches of dust, and a finer
            // mottle over them.
            Color color = Shade(baseColor, Noise(point, 0.028f, salt, 2) * blotch * 2f);
            color = Shade(color, Noise(point, 0.13f, salt ^ 0x9E3779B9u, 2) * blotch);

            // Dirt gathering along the edge. Distance is negative inside, so this is at full
            // strength on the contour and gone a dozen pixels in.
            float edge = Mathf.Clamp01(1f + distance / 14f);
            return Shade(color, -edge * 0.085f);
        };
    }

    // ---------------------------------------------------------------- the icons

    private static void DrawInk(ArtCanvas canvas)
    {
        var body = new Vector2(64f, 48f);
        var neck = new Vector2(64f, 84f);

        float Bottle(Vector2 p) => SmoothUnion(
            RoundedBox(p, body, new Vector2(28f, 26f), 11f),
            RoundedBox(p, neck, new Vector2(11f, 14f), 5f),
            10f);

        Fill(canvas, Bottle, (point, distance) =>
        {
            // The ink inside, seen through the glass, up to a level. A hard line rather than
            // a gradient — the wash in the reference never fades one colour into another.
            Color glass = point.y < 54f ? InkFluidColor : GlassColor;
            return Wash(glass, 0x51E7A2C3u, 0.04f)(point, distance);
        }, 0x51E7A2C3u);

        // The fill line, drawn as a stroke because that is how the reference marks a change
        // of material: a drawn line, not a change of shade.
        Stroke(canvas, Bottle, new Vector2(40f, 54f), new Vector2(88f, 54f), 2.2f, 0x11u, 0.6f);

        var cork = new Vector2(64f, 100f);
        Fill(canvas, p => RoundedBox(p, cork, new Vector2(10f, 9f), 4f), Wash(CorkColor, 0x22u, 0.07f), 0x22u);
    }

    private static void DrawBandage(ArtCanvas canvas)
    {
        // The loose end first, so the roll sits on top of it. A flat strip, not a cord: a
        // bandage is a ribbon, and a capsule with round caps reads as rope.
        float Tail(Vector2 p) => Union(
            RoundedBox(Rotate(p, new Vector2(90f, 42f), 14f), new Vector2(90f, 42f), new Vector2(24f, 8f), 3f),
            RoundedBox(Rotate(p, new Vector2(104f, 64f), 74f), new Vector2(104f, 64f), new Vector2(16f, 8f), 3f));

        Fill(canvas, Tail, Wash(LinenShadow, 0x33u), 0x33u);

        var roll = new Vector2(52f, 66f);
        float Roll(Vector2 p) => RoundedBox(p, roll, new Vector2(38f, 30f), 16f);

        Fill(canvas, Roll, Wash(LinenColor, 0x44u), 0x44u);

        // The wrap, drawn as three ink lines across the roll on a slant. Shadowed bands
        // instead were what made the earlier version read as a blank oval.
        Stroke(canvas, Roll, new Vector2(30f, 38f), new Vector2(46f, 94f), 2.6f, 0x45u, 0.7f);
        Stroke(canvas, Roll, new Vector2(52f, 36f), new Vector2(68f, 92f), 2.6f, 0x46u, 0.7f);
        Stroke(canvas, Roll, new Vector2(74f, 40f), new Vector2(86f, 88f), 2.6f, 0x47u, 0.7f);
    }

    private static void DrawScrap(ArtCanvas canvas)
    {
        // Three plates at different angles unioned into one torn sheet. A single rotated
        // rectangle reads as a tile; the overlap is what gives it a broken silhouette.
        float Plate(Vector2 p)
        {
            float a = RoundedBox(Rotate(p, new Vector2(58f, 62f), 18f), new Vector2(58f, 62f),
                new Vector2(36f, 21f), 4f);
            float b = RoundedBox(Rotate(p, new Vector2(78f, 76f), -32f), new Vector2(78f, 76f),
                new Vector2(23f, 16f), 3f);
            float c = RoundedBox(Rotate(p, new Vector2(44f, 44f), 52f), new Vector2(44f, 44f),
                new Vector2(19f, 13f), 3f);
            return Union(a, Union(b, c));
        }

        float Torn(Vector2 p) => Subtract(Plate(p), Circle(p, new Vector2(100f, 42f), 18f));

        Fill(canvas, Torn, (point, distance) =>
        {
            Color color = Wash(IronColor, 0x55u)(point, distance);

            // Rust in blooms rather than everywhere. The field runs about plus or minus a
            // quarter, so the threshold has to sit inside that range to catch anything.
            float rust = Mathf.SmoothStep(-0.14f, 0.06f, Noise(point, 0.03f, 0x27D4EB2Fu));
            return Color.Lerp(color, RustColor, rust * 0.85f);
        }, 0x55u);

        // Two creases where the sheet has been bent, which is what says thin metal.
        Stroke(canvas, Torn, new Vector2(32f, 54f), new Vector2(74f, 72f), 2.4f, 0x56u, 0.7f);
        Stroke(canvas, Torn, new Vector2(60f, 40f), new Vector2(84f, 84f), 2f, 0x57u, 0.55f);
    }

    private static void DrawBullet(ArtCanvas canvas)
    {
        // A paper cartridge, which is what a musket is loaded with: the ball at the nose,
        // the powder charge in the tube below it, and the paper twisted shut at the tail. A
        // modern brass round would be the wrong century for the guns in this game.
        //
        // Nose up, which was the fix that made it readable — with the ball at the bottom the
        // silhouette read as a flask with a handle rather than as ammunition.
        var ball = new Vector2(64f, 88f);
        var tube = new Vector2(64f, 58f);

        float Cartridge(Vector2 p) => SmoothUnion(
            Circle(p, ball, 21f),
            RoundedBox(p, tube, new Vector2(19f, 24f), 6f),
            8f);

        Fill(canvas, Cartridge, (point, distance) =>
        {
            Color material = point.y > 74f ? LeadColor : PaperColor;
            return Wash(material, 0x66u, 0.07f)(point, distance);
        }, 0x66u);

        // Where the paper is folded over the ball, and the seam down the tube.
        Stroke(canvas, Cartridge, new Vector2(46f, 74f), new Vector2(82f, 74f), 2.6f, 0x67u, 0.75f);
        Stroke(canvas, Cartridge, new Vector2(64f, 40f), new Vector2(64f, 66f), 1.8f, 0x68u, 0.4f);

        // The twist below the tube: paper gathered and folded to one side. Drawn before
        // the cord, so the cord reads as tied round it.
        Fill(canvas, p => SmoothUnion(
                Segment(p, new Vector2(64f, 34f), new Vector2(68f, 22f), 8f),
                Segment(p, new Vector2(68f, 22f), new Vector2(80f, 18f), 6f),
                6f),
            Wash(PaperColor, 0x6Au, 0.06f), 0x6Au);

        // The cord. Wider than the tube on purpose: a tie that stops at the edges of what it
        // is tying reads as a painted stripe, and this one has to read as pinching the paper
        // shut — it is the detail that says "cartridge" rather than "tube".
        var cord = new Vector2(64f, 36f);
        Fill(canvas, p => RoundedBox(p, cord, new Vector2(22f, 7f), 3f), Wash(CordColor, 0x69u, 0.06f), 0x69u);
    }

    private static void DrawShell(ArtCanvas canvas)
    {
        var hull = new Vector2(64f, 78f);
        var head = new Vector2(64f, 40f);

        float Hull(Vector2 p) => RoundedBox(p, hull, new Vector2(22f, 32f), 7f);
        float Head(Vector2 p) => RoundedBox(p, head, new Vector2(24f, 20f), 6f);

        // Paper hull. The one strong colour in the set, and it earns it: a shell is the item
        // the player most needs to spot at a glance in a full inventory.
        Fill(canvas, Hull, Wash(HullColor, 0x77u, 0.05f), 0x77u);

        // The star crimp at the mouth, drawn as three creases meeting at the centre.
        Stroke(canvas, Hull, new Vector2(50f, 104f), new Vector2(78f, 96f), 2.2f, 0x78u, 0.65f);
        Stroke(canvas, Hull, new Vector2(78f, 104f), new Vector2(50f, 96f), 2.2f, 0x79u, 0.65f);

        Fill(canvas, Head, Wash(BrassColor, 0x7Au, 0.06f), 0x7Au);

        // The extractor groove across the head, and the primer set into it.
        Stroke(canvas, Head, new Vector2(42f, 30f), new Vector2(86f, 30f), 2.6f, 0x7Bu, 0.7f);
        Fill(canvas, p => Circle(p, new Vector2(64f, 45f), 8f), Wash(IronDark, 0x7Cu, 0.04f), 0x7Cu);
    }

    private static void DrawWood(ArtCanvas canvas)
    {
        // Two split logs, the near one lying across the far one. One log alone reads as a
        // rolling pin; two reads as firewood.
        DrawLog(canvas, new Vector2(28f, 86f), new Vector2(96f, 98f), 16f, 0x81u);
        DrawLog(canvas, new Vector2(24f, 44f), new Vector2(102f, 36f), 19f, 0x82u);
    }

    /// <summary>
    /// One log: a barrel of bark with a sawn face on the end, growth rings drawn as ink arcs.
    /// The face is a shape of its own rather than a slice of the log's field — as a slice it
    /// came out a thin crescent with no room for rings, and the rings are the whole reason a
    /// log reads as a log from the side.
    /// </summary>
    private static void DrawLog(ArtCanvas canvas, Vector2 from, Vector2 to, float radius, uint salt)
    {
        Vector2 axis = (to - from).normalized;
        Vector2 across = new Vector2(-axis.y, axis.x);

        float Log(Vector2 p) => Segment(p, from, to, radius);
        Fill(canvas, Log, Wash(BarkColor, salt, 0.07f), salt);

        // Bark splits running the length of the log — two strokes, off-centre so they do not
        // read as a seam down the middle.
        Stroke(canvas, Log, from + across * 5f, to + across * 3f, 2.2f, salt ^ 0xAu, 0.5f);
        Stroke(canvas, Log, from - across * 7f + axis * 12f, to - across * 5f - axis * 14f, 1.8f, salt ^ 0xBu, 0.4f);

        // The sawn face: an ellipse on the end, narrow along the log's axis so it reads as a
        // circle seen at a slant.
        Vector2 face = to - axis * 2f;
        float squash = 0.45f;

        float Face(Vector2 p)
        {
            Vector2 d = p - face;
            var local = new Vector2(Vector2.Dot(d, axis) / squash, Vector2.Dot(d, across));
            return (local.magnitude - radius) * squash;
        }

        Fill(canvas, Face, Wash(WoodColor, salt ^ 0xCu, 0.05f), salt ^ 0xCu);

        // Growth rings as ink arcs, spaced closer towards the outside the way a tree's are.
        for (int ring = 1; ring <= 3; ring++)
        {
            float r = radius * (0.28f + ring * 0.22f);
            DrawRing(canvas, Face, face, axis, across, r, squash, WoodDark, salt ^ (uint)(0x20 + ring));
        }
    }

    /// <summary>One growth ring: a squashed circle of darker wood, drawn as a thin band.</summary>
    private static void DrawRing(ArtCanvas canvas, Func<Vector2, float> clip, Vector2 centre,
        Vector2 axis, Vector2 across, float radius, float squash, Color color, uint salt)
    {
        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                Vector2 point = canvas.Point(x, y);
                if (clip(point) > -2f) continue;

                Vector2 d = point - centre;
                var local = new Vector2(Vector2.Dot(d, axis) / squash, Vector2.Dot(d, across));
                float distance = Mathf.Abs((local.magnitude - radius) * squash) - 1.9f;
                distance = Wobble(distance, point, 0.9f, 0.12f, salt);

                float coverage = Coverage(distance);
                if (coverage > 0f) canvas.Blend(x, y, color, coverage);
            }
        }
    }

    private static void DrawAxe(ArtCanvas canvas)
    {
        var haftFrom = new Vector2(28f, 24f);
        var haftTo = new Vector2(86f, 94f);

        float Haft(Vector2 p) => Segment(p, haftFrom, haftTo, 8f);
        Fill(canvas, Haft, Wash(WoodDark, 0x91u, 0.07f), 0x91u);

        // Grain along the haft.
        Stroke(canvas, Haft, haftFrom + new Vector2(3f, -2f), haftTo + new Vector2(2f, -3f), 1.8f, 0x92u, 0.45f);

        var head = new Vector2(92f, 92f);

        float Head(Vector2 p)
        {
            // The classic axe silhouette, and it comes entirely from where the bites are
            // taken. A blunt eye at the haft, a tall blade in front of it, then a circle
            // carved out above and below *near the eye* — that pinches the blade in at the
            // back and leaves the front edge bulging, which is the shape the eye reads as an
            // axe. Biting the front instead (the earlier attempt) just produces a knife.
            Vector2 local = Rotate(p, head, -40f);
            float eye = RoundedBox(local, head + new Vector2(-18f, 0f), new Vector2(11f, 12f), 3f);
            float blade = RoundedBox(local, head + new Vector2(10f, 0f), new Vector2(18f, 25f), 9f);
            float wedge = SmoothUnion(eye, blade, 7f);

            wedge = Subtract(wedge, Circle(local, head + new Vector2(-6f, 36f), 24f));
            wedge = Subtract(wedge, Circle(local, head + new Vector2(-6f, -36f), 24f));
            return wedge;
        }

        Fill(canvas, Head, (point, distance) =>
        {
            Color color = Wash(IronColor, 0x93u)(point, distance);

            // The honed edge: a flat lighter band along the outer third of the blade only.
            // Flat, not a gradient — the reference marks a change of material with an area
            // of different colour, never with a fade.
            float outward = Vector2.Dot(Rotate(point, head, -40f) - head, Vector2.right);
            if (outward > 17f) color = Color.Lerp(color, EdgeColor, 0.6f);

            float rust = Mathf.SmoothStep(-0.02f, 0.18f, Noise(point, 0.04f, 0x68E31DA4u));
            return Color.Lerp(color, RustColor, rust * 0.3f);
        }, 0x93u);

        // The line where the honed edge starts, drawn rather than shaded.
        Stroke(canvas, Head, Rotate(head + new Vector2(17f, -22f), head, 40f),
            Rotate(head + new Vector2(17f, 22f), head, 40f), 2.2f, 0x94u, 0.6f);
    }
}
