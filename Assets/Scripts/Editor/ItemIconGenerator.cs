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

    // Chalky and neutral on purpose: this is a "we don't know what this is" mark, not an
    // object with a material of its own. Kept light so it reads against a dark inventory
    // slot instead of sinking into it.
    private static readonly Color UnknownColor = new Color32(0xF2, 0xEA, 0xD8, 0xFF);

    private static readonly Color AlcoholColor = new Color32(0x8A, 0x5A, 0x28, 0xFF);
    private static readonly Color PowderColor = new Color32(0x2A, 0x28, 0x26, 0xFF);
    private static readonly Color GoldColor = new Color32(0xC9, 0xA2, 0x27, 0xFF);

    // The one other strong colour in the set besides the shell's red — a double barrel's
    // stock is the warm orange-brown wood everyone pictures, not the desaturated tones
    // everything else here is drawn in.
    private static readonly Color StockColor = new Color32(0xAD, 0x71, 0x36, 0xFF);

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
        new IconSpec("IconAxe", "Assets/Items/Item 4 - Axe.asset", DrawAxe),
        new IconSpec("IconPistol", "Assets/Items/Item 5 - Pistol.asset", DrawPistol),
        new IconSpec("IconShotgun", "Assets/Items/Item 11 - Shotgun.asset", DrawShotgun),
        new IconSpec("IconRags", "Assets/Items/Item 13 - Rags.asset", DrawRags),
        new IconSpec("IconAlcohol", "Assets/Items/Item 14 - Alcohol.asset", DrawAlcohol),
        new IconSpec("IconGunpowder", "Assets/Items/Item 15 - Gunpowder.asset", DrawGunpowder),
        new IconSpec("IconGoldenKey", "Assets/Items/Item 17 - Golden Key.asset", DrawGoldenKey),
        new IconSpec("IconDoorKey", "Assets/Items/Item 9 - Key_door.asset", DrawDoorKey),
        new IconSpec("IconPlank", "Assets/Items/Item 10 - Plank.asset", DrawPlank)
    };

    /// <summary>Prefab whose default (no-item-assigned) sprite is the GDS question mark.</summary>
    private const string DroppedItemPrefabPath = "Assets/Prefabs/World/DroppedItem.prefab";

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

        var unknownCanvas = new ArtCanvas(IconPixels, IconPixels);
        DrawUnknown(unknownCanvas);
        Sprite unknownSprite = WriteSprite(unknownCanvas, $"{IconFolder}/IconUnknown.png", IconPixelsPerUnit);
        if (unknownSprite != null && AssignToDroppedItemPlaceholder(unknownSprite)) assigned++;

        AssetDatabase.SaveAssets();
        Debug.Log($"[ItemIcons] Redrew {Icons.Length + 1} icons in '{IconFolder}' " +
                  $"({assigned} targets re-pointed). The GDS icons they replace are untouched.");
    }

    /// <summary>
    /// Points DroppedItem's SpriteRenderer — the placeholder shown before <c>WorldItem.item</c>
    /// assigns a real icon — at the sprite. A prefab asset, not an <see cref="ItemData"/>, so it
    /// needs its own load/save path rather than <see cref="AssignToItem"/>.
    /// </summary>
    private static bool AssignToDroppedItemPlaceholder(Sprite sprite)
    {
        GameObject prefab = PrefabUtility.LoadPrefabContents(DroppedItemPrefabPath);
        try
        {
            var renderer = prefab.GetComponent<SpriteRenderer>();
            if (renderer == null)
            {
                Debug.LogWarning($"[ItemIcons] '{DroppedItemPrefabPath}' has no SpriteRenderer — nothing assigned.");
                return false;
            }

            if (renderer.sprite == sprite) return false;

            renderer.sprite = sprite;
            PrefabUtility.SaveAsPrefabAsset(prefab, DroppedItemPrefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(prefab);
        }
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

    private static void DrawPistol(ArtCanvas canvas)
    {
        // A Luger P08, and three things carry that at this size: a barrel noticeably slimmer
        // than what it steps into, a grip raked far further back than any modern pistol's,
        // and the receiver tail falling away above it instead of squaring off. Built like the
        // shotgun otherwise, so the two firearms read as a pair.
        const float Tilt = 27f;
        const float GripTilt = -38f;

        // Same reason as the shotgun: drawn out to within a few pixels of the frame, because
        // the slot it is shown in insets it again. The ink line is left unscaled so the
        // weight still matches the rest of the set.
        const float Scale = 1.05f;
        var origin = new Vector2(10.5f, 48.5f);
        Vector2 along = new Vector2(Mathf.Cos(Tilt * Mathf.Deg2Rad), Mathf.Sin(Tilt * Mathf.Deg2Rad));
        Vector2 off = new Vector2(-along.y, along.x);

        Vector2 At(float t, float s) => origin + (along * t + off * s) * Scale;
        Vector2 Size(float length, float depth) => new Vector2(length, depth) * Scale;
        float R(float radius) => radius * Scale;
        Vector2 Bore(Vector2 p, Vector2 centre) => Rotate(p, centre, -Tilt);

        // Barrel and receiver as one steel form, with the tail cut away where it would
        // otherwise sit square above the grip. The barrel is drawn thin enough that the ink
        // takes most of it — right for a Luger, whose barrel is the slimmest thing on it, and
        // the step where it meets the receiver is most of what identifies the gun.
        var barrelCentre = At(24f, 0f);
        var receiverCentre = At(62f, -5f);
        var tailCentre = At(78f, 28f);
        float Metal(Vector2 p) => Subtract(
            SmoothUnion(
                RoundedBox(Bore(p, barrelCentre), barrelCentre, Size(24f, 11f), R(3f)),
                RoundedBox(Bore(p, receiverCentre), receiverCentre, Size(17f, 18f), R(5f)),
                R(4f)),
            Circle(p, tailCentre, R(20f)));

        Fill(canvas, Metal, Wash(IronColor, 0xB1u, 0.06f), 0xB1u);
        Stroke(canvas, Metal, At(5f, 0f), At(36f, 0f), 2f, 0xB2u, 0.4f);

        // The toggle joint, drawn as the line across the receiver rather than modelled as a
        // hump on top of it — a hump that reads at all would be thinner than its own ink.
        Stroke(canvas, Metal, At(66f, 9f), At(66f, -12f), 2.2f, 0xB5u, 0.5f);

        // Nothing is subtracted from the guard: cutting the neighbour out of a ring removes
        // the overlap holding the two together and leaves the loop floating.
        var guardCentre = At(52f, -28f);
        float Guard(Vector2 p) => Subtract(Circle(p, guardCentre, R(12.5f)), Circle(p, guardCentre, R(7.5f)));
        Fill(canvas, Guard, Wash(IronDark, 0xB3u, 0.05f), 0xB3u);

        // Raked back at the angle the Luger is known for, and buried well up into the
        // receiver so the wood-to-steel seam is one ink line rather than a gap.
        Vector2 gripAlong = new Vector2(Mathf.Cos(GripTilt * Mathf.Deg2Rad), Mathf.Sin(GripTilt * Mathf.Deg2Rad));
        Vector2 gripCentre = At(70f, -12f) + gripAlong * 20f * Scale;
        float Grip(Vector2 p) => RoundedBox(Rotate(p, gripCentre, -GripTilt), gripCentre, Size(20f, 15f), R(6f));
        Fill(canvas, Grip, Wash(StockColor, 0xB4u, 0.06f), 0xB4u);
    }

    private static void DrawShotgun(ArtCanvas canvas)
    {
        // Two constraints decide this drawing, and every earlier attempt failed one of them.
        //
        // First, the ink is 4.5-8.5px wide on *each* side of a contour, so anything under
        // about thirty pixels across is eaten from both edges and comes out a black sliver.
        // Barrels drawn as two separate tubes, a stock pinched in at the wrist: all sliver.
        //
        // Second, each Fill is its own silhouette with its own outline, so two parts that
        // merely touch end up as two islands with a gap between them — the wobble alone moves
        // a contour by two pixels. Parts have to overlap by ten pixels or more, and a part
        // must never be subtracted from the neighbour it is supposed to hang off.
        //
        // What is left after both is a small number of fat shapes that overlap heavily, so
        // the gun reads by its gesture: a long lean barrel against a short deep stock, broken
        // at the wrist. Steep enough that the barrel gets the length a long gun needs — a
        // square canvas is much longer on the diagonal, and shallower layouts left the barrel
        // as stubby as the stock, which is what made the whole thing read as a hand tool.
        const float Tilt = 45f;
        const float StockTilt = 30f;

        // Drawn to within about six pixels of the frame. The icons are shown inside a slot
        // that already insets them, so the margin left here is margin lost twice over; the
        // ink line is not scaled with it, which keeps the line weight matching the rest.
        const float Scale = 1.05f;
        var origin = new Vector2(14.5f, 31.5f);
        Vector2 along = new Vector2(Mathf.Cos(Tilt * Mathf.Deg2Rad), Mathf.Sin(Tilt * Mathf.Deg2Rad));
        Vector2 off = new Vector2(-along.y, along.x);

        // Parts are placed by how far down the gun they sit and how far off the bore line.
        Vector2 At(float t, float s) => origin + (along * t + off * s) * Scale;
        Vector2 Size(float length, float depth) => new Vector2(length, depth) * Scale;
        float R(float radius) => radius * Scale;

        // Rotate turns the sample point, which lands the shape at the negative of the angle
        // passed — the axe passes -40 to stand its head up at +40.
        Vector2 Bore(Vector2 p, Vector2 centre) => Rotate(p, centre, -Tilt);

        // Barrels and receiver as one form. Both are the same steel, so merging them costs
        // nothing and buys a continuous outline with the receiver reading as a bulge in it,
        // instead of two boxes that drift apart. The seam down the middle is what says there
        // are two barrels — drawing them as two shapes only ever produced a black tangle.
        var barrelCentre = At(32f, 0f);
        var receiverCentre = At(70f, -5f);
        float Metal(Vector2 p) => SmoothUnion(
            RoundedBox(Bore(p, barrelCentre), barrelCentre, Size(32f, 14f), R(4f)),
            RoundedBox(Bore(p, receiverCentre), receiverCentre, Size(15f, 18f), R(5f)),
            R(9f));

        Fill(canvas, Metal, Wash(IronColor, 0xC1u, 0.05f), 0xC1u);
        Stroke(canvas, Metal, At(6f, 0f), At(60f, 0f), 2.2f, 0xC2u, 0.5f);

        // The fore-end, well up into the barrel so wood meets steel along one ink line.
        var foreCentre = At(28f, -17f);
        float Fore(Vector2 p) => RoundedBox(Bore(p, foreCentre), foreCentre, Size(21f, 12f), R(5f));
        Fill(canvas, Fore, Wash(StockColor, 0xC3u, 0.06f), 0xC3u);

        // An open dark loop under the receiver: the clearest "this is a gun" mark in the set,
        // and what makes the pistol read at a glance. Nothing is subtracted from it — the
        // previous version cut the receiver out of the ring, which removed the very overlap
        // holding the two together and left the guard floating.
        var guardCentre = At(68f, -27f);
        float Guard(Vector2 p) => Subtract(Circle(p, guardCentre, R(12f)), Circle(p, guardCentre, R(7f)));
        Fill(canvas, Guard, Wash(IronDark, 0xC4u, 0.05f), 0xC4u);
        Fill(canvas, p => Segment(p, At(68f, -15f), At(68f, -26f), R(2.6f)), Wash(IronDark, 0xC5u, 0.05f), 0xC5u);

        // The stock runs at its own shallower angle, so the gun breaks at the wrist instead
        // of continuing as one straight sausage. One fat block with a bite taken out of its
        // underside, which sweeps the belly up towards the trigger guard — carving the
        // silhouette is how the axe gets its shape, and it costs no thickness.
        Vector2 stockAlong = new Vector2(Mathf.Cos(StockTilt * Mathf.Deg2Rad), Mathf.Sin(StockTilt * Mathf.Deg2Rad));
        Vector2 stockOff = new Vector2(-stockAlong.y, stockAlong.x);
        Vector2 stockCentre = At(74f, -5f) + (stockAlong * 21f - stockOff * 7f) * Scale;
        Vector2 bellyCentre = stockCentre - (stockAlong * 21f + stockOff * 30f) * Scale;

        float Stock(Vector2 p) => Subtract(
            RoundedBox(Rotate(p, stockCentre, -StockTilt), stockCentre, Size(21f, 18f), R(7f)),
            Circle(p, bellyCentre, R(20f)));

        Fill(canvas, Stock, Wash(StockColor, 0xC6u, 0.06f), 0xC6u);
    }

    private static void DrawRags(ArtCanvas canvas)
    {
        // Two overlapping strips bitten into at one corner — the same torn-corner idiom the
        // scrap metal uses. Thick strips, same reason as everything else on this page.
        float Strip(Vector2 p)
        {
            float a = RoundedBox(Rotate(p, new Vector2(52f, 66f), 20f), new Vector2(52f, 66f), new Vector2(36f, 16f), 6f);
            float b = RoundedBox(Rotate(p, new Vector2(70f, 40f), -16f), new Vector2(70f, 40f), new Vector2(32f, 15f), 6f);
            return Union(a, b);
        }

        float Torn(Vector2 p) => Subtract(Strip(p), Circle(p, new Vector2(98f, 76f), 20f));
        Fill(canvas, Torn, Wash(LinenShadow, 0xD1u, 0.09f), 0xD1u);

        // Frayed threads along one edge.
        Stroke(canvas, Torn, new Vector2(22f, 58f), new Vector2(40f, 82f), 1.8f, 0xD2u, 0.5f);
        Stroke(canvas, Torn, new Vector2(38f, 26f), new Vector2(56f, 48f), 1.8f, 0xD3u, 0.45f);
    }

    private static void DrawAlcohol(ArtCanvas canvas)
    {
        // The ink bottle's own shape (body + neck, smooth-unioned) reused with a different
        // proportion and colour — the same construction, a different bottle.
        var body = new Vector2(62f, 46f);
        var neck = new Vector2(62f, 82f);
        float Bottle(Vector2 p) => SmoothUnion(
            RoundedBox(p, body, new Vector2(26f, 26f), 12f),
            RoundedBox(p, neck, new Vector2(11f, 16f), 5f),
            9f);

        Fill(canvas, Bottle, Wash(AlcoholColor, 0xD4u, 0.05f), 0xD4u);

        // The liquid line, well below the shoulder — a flask kept half-full reads clearer
        // than one drawn brim-full.
        Stroke(canvas, Bottle, new Vector2(38f, 56f), new Vector2(86f, 56f), 2f, 0xD5u, 0.55f);

        var cork = new Vector2(62f, 98f);
        Fill(canvas, p => RoundedBox(p, cork, new Vector2(9f, 8f), 3f), Wash(CorkColor, 0xD6u, 0.07f), 0xD6u);
    }

    private static void DrawGunpowder(ArtCanvas canvas)
    {
        var pouch = new Vector2(62f, 48f);
        float Pouch(Vector2 p) => SmoothUnion(
            RoundedBox(p, pouch, new Vector2(30f, 28f), 15f),
            RoundedBox(p, pouch + new Vector2(0f, 32f), new Vector2(12f, 14f), 6f),
            9f);

        Fill(canvas, Pouch, (point, distance) =>
        {
            Color color = Wash(PowderColor, 0xD7u, 0.05f)(point, distance);

            // Grains catching the light, scattered rather than smooth — the one place in
            // the set a texture reads as granular instead of as dust.
            float grain = Mathf.SmoothStep(0.05f, 0.22f, Noise(point, 0.22f, 0xD7u, 2));
            return Color.Lerp(color, EdgeColor, grain * 0.4f);
        }, 0xD7u);

        // The drawstring cinching the neck shut.
        Fill(canvas, p => RoundedBox(p, pouch + new Vector2(0f, 24f), new Vector2(17f, 6f), 3f),
            Wash(CordColor, 0xD8u, 0.05f), 0xD8u);
    }

    /// <summary>
    /// Shared key silhouette — a ring bow, a shaft and two teeth cut into its tip — used by
    /// both keys in the set. Only the metal and the highlight differ between them, so the
    /// shape lives once rather than being copy-pasted with a different colour.
    /// </summary>
    private static void DrawKey(ArtCanvas canvas, Color metal, uint saltBase, bool ornate)
    {
        var bow = new Vector2(46f, 90f);
        float Bow(Vector2 p) => Subtract(Circle(p, bow, 26f), Circle(p, bow, 6f));

        var shaftTo = new Vector2(92f, 34f);
        float Shaft(Vector2 p) => Segment(p, bow, shaftTo, 9f);

        // Teeth in the shaft's own local frame (rotated to its angle, same trick the axe's
        // head uses), so they read as cut into the blade rather than as blocks glued on.
        float shaftAngle = Mathf.Atan2(shaftTo.y - bow.y, shaftTo.x - bow.x) * Mathf.Rad2Deg;
        float Teeth(Vector2 p)
        {
            Vector2 local = Rotate(p, shaftTo, -shaftAngle);
            float a = RoundedBox(local, shaftTo + new Vector2(-14f, -10f), new Vector2(8f, 4f), 1.5f);
            float b = RoundedBox(local, shaftTo + new Vector2(-4f, -10f), new Vector2(6f, 4f), 1.5f);
            return Union(a, b);
        }

        float Key(Vector2 p) => Union(Union(Bow(p), Shaft(p)), Teeth(p));
        Fill(canvas, Key, Wash(metal, saltBase, 0.05f), saltBase);

        if (ornate)
        {
            // The one bright accent in the set: a flat highlight band, the same device the
            // shell's brass head uses to say "this catches light" without a gradient.
            Stroke(canvas, Key, bow + new Vector2(-12f, 14f), bow + new Vector2(12f, 14f), 2.5f, saltBase ^ 0x1u, 0.4f);
        }
    }

    private static void DrawGoldenKey(ArtCanvas canvas) => DrawKey(canvas, GoldColor, 0xE1u, true);

    private static void DrawDoorKey(ArtCanvas canvas) => DrawKey(canvas, IronColor, 0xE3u, false);

    private static void DrawPlank(ArtCanvas canvas)
    {
        // A single board, cut on the diagonal like the split logs' sawn faces so it doesn't
        // read as a tile lying flat on the page. Replaces DoorBarricadeSetup's white-square
        // placeholder — that tool still creates the item on first run, this only redraws
        // its icon to match the rest of the set.
        var from = new Vector2(18f, 40f);
        var to = new Vector2(112f, 88f);
        float Board(Vector2 p) => Segment(p, from, to, 20f);
        Fill(canvas, Board, Wash(WoodColor, 0xF1u, 0.06f), 0xF1u);

        Vector2 axis = (to - from).normalized;
        Vector2 across = new Vector2(-axis.y, axis.x);

        // Grain running the length of the board, off-centre on both sides so it doesn't
        // read as a seam down the middle — the log's bark splits use the same trick.
        Stroke(canvas, Board, from + across * 6f + axis * 8f, to + across * 4f - axis * 10f, 2f, 0xF2u, 0.5f);
        Stroke(canvas, Board, from - across * 7f + axis * 14f, to - across * 5f - axis * 16f, 1.8f, 0xF3u, 0.4f);

        // A knot, the one thing that says "wood" faster than the grain lines do.
        var knot = from + axis * 42f + across * 2f;
        Fill(canvas, p => Circle(p, knot, 6f), Wash(WoodDark, 0xF4u, 0.05f), 0xF4u);
    }

    private static void DrawUnknown(ArtCanvas canvas)
    {
        // The loop: a ring bitten open on its lower-left so it reads as a hook rather than a
        // closed "o" — the same bite-a-circle-out idiom the axe's eye is carved with. Thick
        // on purpose: at ring-thickness-11 the ink line from both edges met in the middle and
        // ate the fill entirely, whatever colour it was set to.
        var hookCentre = new Vector2(64f, 90f);
        float Hook(Vector2 p) => Subtract(
            Subtract(Circle(p, hookCentre, 28f), Circle(p, hookCentre, 8f)),
            Circle(p, hookCentre + new Vector2(-17f, -23f), 25f));

        var stemTop = new Vector2(82f, 62f);
        var stemBottom = new Vector2(64f, 44f);
        float HookAndStem(Vector2 p) => SmoothUnion(Hook(p), Segment(p, stemTop, stemBottom, 8f), 6f);

        Fill(canvas, HookAndStem, Wash(UnknownColor, 0xA1u, 0.08f), 0xA1u);

        // The dot, drawn as its own separate fill rather than unioned with the stem — the gap
        // between them is the one thing that says "question mark" instead of "fishhook".
        var dot = new Vector2(60f, 24f);
        Fill(canvas, p => Circle(p, dot, 11f), Wash(UnknownColor, 0xA2u, 0.08f), 0xA2u);
    }
}
