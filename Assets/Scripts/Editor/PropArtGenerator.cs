using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Draws the placeholder props procedurally and points their prefabs at the result.
///
/// The props shipped on two stand-ins: a magenta checker (sacks and statues) and a UI
/// swirl borrowed from the GDS pack (candle and lamp). Neither says anything about what
/// the object is, which makes a room impossible to judge — the whole reason the tiles in
/// <see cref="DungeonSceneSetup"/> are generated rather than left flat.
///
/// These are placeholders in exactly that sense: enough for a prop to be recognisable at
/// a glance and to sit in the dungeon's palette, not a substitute for real art. Drawn from
/// directly above, like the walls and for the same reason — the game is seen from
/// overhead, and anything drawn at an angle reads as leaning.
///
/// Sizing is the part that lets this be a drop-in. Every sprite's pixels-per-unit is set so
/// it covers exactly the same world size as the stand-in it replaces, so no prefab transform
/// has to be touched and nothing already placed in a scene changes size. The door leaf is the
/// one exception — its stand-in was a slice of table art squashed by a non-uniform scale, so
/// a sprite of the right shape has to have that scale recomputed rather than inherited.
///
/// The noise is seeded from each prop's name, so re-running produces byte-identical files
/// and does not churn the repository.
/// </summary>
public static class PropArtGenerator
{
    private const string GeneratedFolder = "Assets/Generation/Props";

    /// <summary>Sprite side in pixels. Matches the tiles, so props and floor share a grain.</summary>
    private const int Pixels = 32;

    // The dungeon's stone, carried over from DungeonSceneSetup so a statue's plinth is
    // recognisably cut from the same rock as the walls around it.
    private static readonly Color StoneLight = new Color32(0x6E, 0x7A, 0x76, 0xFF);
    private static readonly Color StoneMid = new Color32(0x4A, 0x54, 0x4F, 0xFF);
    private static readonly Color StoneDark = new Color32(0x27, 0x2E, 0x2D, 0xFF);

    // Everything that is not stone is kept warm and desaturated: warm enough to separate
    // from the floor inside the vision cone, desaturated enough not to read as a pickup.
    private static readonly Color ClothLight = new Color32(0x7C, 0x71, 0x5C, 0xFF);
    private static readonly Color ClothDark = new Color32(0x4C, 0x44, 0x37, 0xFF);
    private static readonly Color CordColor = new Color32(0x33, 0x2C, 0x23, 0xFF);
    private static readonly Color GrainColor = new Color32(0x9A, 0x8C, 0x6A, 0xFF);

    private static readonly Color IronColor = new Color32(0x4C, 0x54, 0x50, 0xFF);
    private static readonly Color IronDark = new Color32(0x22, 0x27, 0x25, 0xFF);
    private static readonly Color DoorWoodColor = new Color32(0x6B, 0x51, 0x33, 0xFF);
    private static readonly Color DoorWoodDark = new Color32(0x4A, 0x37, 0x22, 0xFF);
    private static readonly Color DoorEdgeColor = new Color32(0x24, 0x1B, 0x11, 0xFF);

    private static readonly Color WaxColor = new Color32(0xC6, 0xBE, 0xA4, 0xFF);
    private static readonly Color WaxShadow = new Color32(0x8E, 0x87, 0x71, 0xFF);
    private static readonly Color FlameColor = new Color32(0xE8, 0xA9, 0x4A, 0xFF);

    private static readonly Color FleshColor = new Color32(0x6B, 0x5B, 0x55, 0xFF);
    private static readonly Color FleshShadow = new Color32(0x3D, 0x33, 0x30, 0xFF);
    private static readonly Color BloodColor = new Color32(0x54, 0x24, 0x22, 0xFF);

    private static readonly Color Nothing = new Color(0f, 0f, 0f, 0f);

    /// <summary>Which prop to draw.</summary>
    private enum PropStyle
    {
        /// <summary>A tied sack slumped on the floor, seen from above.</summary>
        Sack,

        /// <summary>A sack that has split, with its contents spilled to one side.</summary>
        SpilledSack,

        /// <summary>A standing figure on a round stone plinth.</summary>
        Statue,

        /// <summary>The same statue with its head gone and the break left rough.</summary>
        BrokenStatue,

        /// <summary>A lit candle stub in its own pool of wax.</summary>
        Candle,

        /// <summary>A caged lantern: iron ring, barred glass, lit from within.</summary>
        Lamp,

        /// <summary>A body face down, arms out, with what it left on the floor.</summary>
        Corpse,

        /// <summary>A door leaf seen edge-on from above: boarded, banded and ringed.</summary>
        Door
    }

    /// <summary>
    /// One generated prop: what to draw, where the sprite goes, which prefab receives it,
    /// and how wide the sprite it replaces was in world units.
    /// </summary>
    private readonly struct PropSpec
    {
        public readonly string Name;
        public readonly PropStyle Style;
        public readonly string PrefabPath;

        /// <summary>
        /// Which child of the prefab receives the sprite. Empty means the first
        /// <see cref="SpriteRenderer"/> found, which is all most of these prefabs have; the
        /// door has four, and picking the first would board over the wrong one.
        /// </summary>
        public readonly string ChildName;

        /// <summary>Sprite size in pixels. Square for everything but the door leaf.</summary>
        public readonly int Width;
        public readonly int Height;

        /// <summary>
        /// World size the sprite must end up at, or zero to leave the receiving transform
        /// alone. Only the door needs it: its stand-in was a slice of table art with a
        /// compensating non-uniform scale on top, so a sprite of any other shape has to have
        /// that scale recomputed rather than inherited.
        /// </summary>
        public readonly Vector2 TargetSize;

        /// <summary>
        /// World size the finished sprite should cover. The importer's pixels-per-unit is
        /// derived from it, so a prop's size is set here rather than by editing a transform.
        ///
        /// For every prop but the corpse this is the size of the stand-in it replaces, which
        /// is what keeps the swap invisible; the corpse's stand-in was simply the wrong size
        /// and is corrected here.
        /// </summary>
        public readonly float WorldUnits;

        public PropSpec(string name, PropStyle style, string prefabPath, float worldUnits,
            string childName = "", int width = Pixels, int height = Pixels, Vector2 targetSize = default)
        {
            Name = name;
            Style = style;
            PrefabPath = prefabPath;
            WorldUnits = worldUnits;
            ChildName = childName;
            Width = width;
            Height = height;
            TargetSize = targetSize;
        }
    }

    // 2.56 is the magenta checker: 256 pixels at 100 PPU. 1.28 is the borrowed UI swirl,
    // 128 pixels at the same PPU.
    private static readonly PropSpec[] Props =
    {
        new PropSpec("Sack01", PropStyle.Sack, "Assets/Prefabs/Sack01.prefab", 2.56f),
        new PropSpec("Sack02", PropStyle.SpilledSack, "Assets/Prefabs/Sack02.prefab", 2.56f),
        new PropSpec("Statue01", PropStyle.Statue, "Assets/Prefabs/Statue01.prefab", 2.56f),
        new PropSpec("Statue02", PropStyle.BrokenStatue, "Assets/Prefabs/Statue02.prefab", 2.56f),
        new PropSpec("Candle01", PropStyle.Candle, "Assets/Prefabs/Candle01.prefab", 1.28f),
        new PropSpec("Lamp", PropStyle.Lamp, "Assets/Prefabs/Lamp.prefab", 1.28f),
        // The one prop deliberately *not* kept at the size it replaces. Its sheet was one
        // world unit, so the drawn body came out 0.84 units long — against a skull guy whose
        // own art measures 1.66 by 2.38, that is a corpse under a third the length of the
        // thing it is the corpse of. 2.6 puts the body at roughly 2.2 units: a shade shorter
        // than the enemy stood up, which is right for something lying spread out.
        new PropSpec("EnemyCorpse", PropStyle.Corpse, "Assets/Prefabs/EnemyCorpse.prefab", 2.6f),

        // The door leaf. Its stand-in was FURNITURE_pngy_stol_0 — a 984 by 520 slice of the
        // table art at 100 PPU, so 9.84 by 5.20 world units, squashed to a leaf by a scale
        // of 0.102 by 0.22 on the sprite and another 0.2 by 1 on its parent. That works out
        // at 0.201 by 1.144, which is the size the leaf has to come back at, and why this is
        // the one prop whose transform is recomputed instead of preserved.
        //
        // The parent carries the door's BoxCollider2D, so its scale is left alone and the
        // whole correction lands on the sprite child.
        new PropSpec("Door", PropStyle.Door, "Assets/Prefabs/Door_System.prefab", 1f,
            childName: "Visual", width: 16, height: 64, targetSize: new Vector2(0.201f, 1.144f))
    };

    /// <summary>
    /// Draws every prop, imports it and assigns it to its prefab's <see cref="SpriteRenderer"/>.
    /// Idempotent, and safe to re-run: the textures are deterministic and the only thing
    /// written to a prefab is the sprite reference.
    /// </summary>
    [MenuItem("Tools/Dungeon/Regenerate Prop Art")]
    public static void RegeneratePropArt()
    {
        Directory.CreateDirectory(GeneratedFolder);
        AssetDatabase.Refresh();

        int assigned = 0;
        foreach (PropSpec spec in Props)
        {
            Sprite sprite = WriteSprite(spec);
            if (sprite == null) continue;
            if (AssignToPrefab(spec, sprite)) assigned++;
        }

        // The chest and the barricade plank are drawn by the tools that own their paths and
        // their prefabs; redrawing them from here would put two writers on one asset.
        ChestSetup.RegenerateChestSprite();
        DoorBarricadeSetup.RegeneratePlankIcon();

        AssetDatabase.SaveAssets();
        Debug.Log($"[PropArt] Redrew {Props.Length} props in '{GeneratedFolder}' " +
                  $"({assigned} prefabs re-pointed), the chest sprite and the barricade plank.");
    }

    /// <summary>Writes one prop's PNG, importing it with the settings that keep its world size.</summary>
    private static Sprite WriteSprite(PropSpec spec)
    {
        string path = $"{GeneratedFolder}/{spec.Name}.png";
        File.WriteAllBytes(path, BuildTexture(spec));
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        ConfigureImporter(path, spec.Height / spec.WorldUnits);

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (sprite == null) Debug.LogError($"[PropArt] No sprite imported from '{path}'.");
        return sprite;
    }

    /// <summary>
    /// Points a prefab's first <see cref="SpriteRenderer"/> at the generated sprite.
    /// Returns whether anything was written.
    /// </summary>
    private static bool AssignToPrefab(PropSpec spec, Sprite sprite)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) == null)
        {
            Debug.LogWarning($"[PropArt] No prefab at '{spec.PrefabPath}' — sprite drawn but not assigned.");
            return false;
        }

        GameObject contents = PrefabUtility.LoadPrefabContents(spec.PrefabPath);
        try
        {
            SpriteRenderer renderer = FindRenderer(contents, spec.ChildName);
            if (renderer == null)
            {
                Debug.LogWarning($"[PropArt] '{spec.PrefabPath}' has no SpriteRenderer" +
                                 (string.IsNullOrEmpty(spec.ChildName) ? "" : $" on a child named '{spec.ChildName}'") +
                                 " — nothing assigned.");
                return false;
            }

            renderer.sprite = sprite;
            if (spec.TargetSize != Vector2.zero) FitToTarget(renderer, sprite, spec.TargetSize);

            PrefabUtility.SaveAsPrefabAsset(contents, spec.PrefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>The named child's renderer, or the first one in the prefab when unnamed.</summary>
    private static SpriteRenderer FindRenderer(GameObject contents, string childName)
    {
        if (string.IsNullOrEmpty(childName)) return contents.GetComponentInChildren<SpriteRenderer>(true);

        foreach (SpriteRenderer candidate in contents.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (candidate.name == childName) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Scales the renderer's own transform so the sprite lands at the target world size,
    /// dividing out whatever its parents already apply. Only the sprite's transform moves —
    /// the parents are left alone, because on the door prefab the parent carries the
    /// collider that blocks the opening.
    /// </summary>
    private static void FitToTarget(SpriteRenderer renderer, Sprite sprite, Vector2 target)
    {
        Vector2 spriteSize = sprite.bounds.size;
        if (spriteSize.x <= 0f || spriteSize.y <= 0f) return;

        Transform transform = renderer.transform;
        Transform parent = transform.parent;
        Vector3 parentScale = parent != null ? parent.lossyScale : Vector3.one;
        if (Mathf.Approximately(parentScale.x, 0f) || Mathf.Approximately(parentScale.y, 0f)) return;

        transform.localScale = new Vector3(
            target.x / (spriteSize.x * parentScale.x),
            target.y / (spriteSize.y * parentScale.y),
            1f);
    }

    /// <summary>
    /// Point filtering, no compression and no mip maps, so 32 pixels stay 32 crisp pixels.
    /// The pixels-per-unit is passed in rather than fixed, because it is what holds each
    /// prop at the size it already is in the scenes.
    /// </summary>
    private static void ConfigureImporter(string path, float pixelsPerUnit)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>
    /// Size of the sprite currently being drawn. A field rather than a parameter threaded
    /// through every shape, following the same pattern the wall autotiles use for their
    /// mask: the drawing routines are a switch over one signature, and widening it for the
    /// one prop that is not square would cost every other case a parameter it ignores.
    /// </summary>
    private static int _width = Pixels, _height = Pixels;

    private static byte[] BuildTexture(PropSpec spec)
    {
        var random = new DeterministicRandom(spec.Name);
        _width = spec.Width;
        _height = spec.Height;

        var texture = new Texture2D(_width, _height, TextureFormat.RGBA32, false);

        try
        {
            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                    texture.SetPixel(x, y, PixelColor(spec.Style, x, y, random));
            }

            texture.Apply();
            return texture.EncodeToPNG();
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    private static Color PixelColor(PropStyle style, int x, int y, DeterministicRandom random)
    {
        float cx = (_width - 1) * 0.5f;
        float cy = (_height - 1) * 0.5f;
        float dx = x - cx;
        float dy = y - cy;

        switch (style)
        {
            case PropStyle.Sack:
            case PropStyle.SpilledSack:
            {
                bool spilled = style == PropStyle.SpilledSack;

                // Grain spilled from the split, drawn before the sack so the sack's own
                // outline stays on top of it and the pile reads as coming out from under.
                if (spilled)
                {
                    float spillX = dx - 9f;
                    float spillY = dy + 6f;
                    float spill = spillX * spillX / 36f + spillY * spillY / 16f;
                    if (spill < 1f + Wobble(x, y, 0x9E3779B9u) * 0.5f)
                        return Jitter(spill < 0.45f ? GrainColor : Shade(GrainColor, -0.08f), 0.05f, random);
                }

                // The body is an ellipse rather than a circle: a sack that has been put
                // down slumps, and a perfect disc reads as a barrel seen from above.
                float bodyX = dx / (spilled ? 11.5f : 10.5f);
                float bodyY = (dy + 1.5f) / 9.5f;
                float body = Mathf.Sqrt(bodyX * bodyX + bodyY * bodyY) - Wobble(x, y, 0x85EBCA6Bu) * 0.06f;
                if (body > 1f) return Nothing;

                // Outline first: without a dark edge the cloth dissolves into the floor,
                // which is the same colour family one step darker.
                if (body > 0.88f) return Jitter(Shade(ClothDark, -0.06f), 0.03f, random);

                // The neck, bunched and tied. Its cord is what makes the shape a sack
                // rather than a cushion, so it gets the strongest contrast on the sprite.
                float neckX = dx + (spilled ? 2.5f : 0f);
                float neckY = dy - 6.5f;
                float neck = Mathf.Sqrt(neckX * neckX + neckY * neckY);
                if (neck < 4.2f)
                {
                    if (neck > 2.6f && neck < 3.6f) return Jitter(CordColor, 0.03f, random);
                    return Jitter(Shade(ClothLight, 0.05f), 0.04f, random);
                }

                // Folds running out from the neck. Keyed on the angle, so they radiate the
                // way cloth gathered at one point actually creases.
                float angle = Mathf.Atan2(neckY, neckX);
                float fold = Mathf.Sin(angle * 5f + Wobble(x, y, 0xC2B2AE35u) * 2f);
                Color cloth = Color.Lerp(ClothDark, ClothLight, Mathf.InverseLerp(1f, 0.15f, body));
                if (fold > 0.72f) cloth = Shade(cloth, -0.06f);

                return Jitter(cloth, 0.03f, random);
            }

            case PropStyle.Statue:
            case PropStyle.BrokenStatue:
            {
                bool broken = style == PropStyle.BrokenStatue;
                float plinth = Mathf.Sqrt(dx * dx + dy * dy);

                if (plinth > 14f + Wobble(x, y, 0x27D4EB2Fu) * 0.8f) return Nothing;

                // The figure, seen from directly above. From overhead a standing person is
                // three masses — head, shoulders, arms — told apart by the gaps between
                // them, so the gaps are drawn as hard dark edges rather than left to
                // shading. Without them the three merge into one lozenge, which is exactly
                // what the first pass at this looked like.
                float head = Ellipse(dx, dy, 0f, 4.4f, 3.3f, 3.3f);
                float shoulders = Ellipse(dx, dy, 0f, -1.8f, 6.4f, 4.2f);
                float leftArm = Ellipse(dx, dy, -5.6f, -3.4f, 2.6f, 3.4f);
                float rightArm = Ellipse(dx, dy, 5.6f, -3.4f, 2.6f, 3.4f);

                float figure = Mathf.Min(shoulders, Mathf.Min(leftArm, rightArm));
                if (!broken) figure = Mathf.Min(figure, head);

                if (figure <= 1f)
                {
                    // The lit top of each mass falling away to a dark edge. This is the one
                    // place drawn relief is right: these are curved surfaces seen from
                    // above, not vertical faces pretending to be seen from the side.
                    if (figure > 0.82f) return Jitter(StoneDark, 0.03f, random);
                    return Jitter(Color.Lerp(Shade(StoneLight, 0.06f), StoneMid, figure * 0.3f), 0.035f, random);
                }

                // The break, on the statue that has lost its head: a rough dark stump where
                // the neck was, standing proud of the shoulders.
                if (broken && Ellipse(dx, dy, 0f, 1.6f, 2.6f, 2.2f) <= 1f + Wobble(x, y, 0x165667B1u) * 0.15f)
                    return Jitter(Shade(StoneDark, 0.04f), 0.05f, random);

                // The plinth is the wall's stone: a rim two or three pixels deep and then
                // masonry, so a statue reads as cut from the same rock as the room.
                if (plinth > 11.6f) return Jitter(StoneDark, 0.03f, random);
                if (plinth > 10.6f) return Jitter(StoneMid, 0.04f, random);

                // Fallen chips scattered over the broken one's plinth.
                if (broken && random.Chance(0.06f)) return Jitter(StoneMid, 0.05f, random);

                return Jitter(Shade(StoneMid, -0.08f), 0.03f, random);
            }

            case PropStyle.Candle:
            {
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                // The prop lands about a third of a tile across on screen, so it is drawn
                // as hard bands rather than with a gradient: at that size anything softer
                // turns into one grey smudge.
                float pool = 9f + Wobble(x, y, 0x9E3779B9u) * 1.4f;
                if (distance > pool) return Nothing;

                // The pool of wax it has burnt down into, kept translucent so the floor
                // still reads through it.
                if (distance > 5.4f)
                {
                    Color spill = Jitter(WaxShadow, 0.03f, random);
                    spill.a = Mathf.InverseLerp(pool, 5.4f, distance) * 0.75f + 0.15f;
                    return spill;
                }

                if (distance > 4.4f) return Jitter(Shade(WaxShadow, -0.12f), 0.03f, random);
                if (distance > 2.6f) return Jitter(WaxColor, 0.03f, random);
                if (distance > 2f) return Jitter(IronDark, 0.02f, random);

                // The flame, with a hot core. It is the smallest thing on the sprite and
                // the only thing that says "light", so it gets the widest contrast step.
                if (distance > 1f) return Jitter(FlameColor, 0.05f, random);
                return Color.Lerp(FlameColor, Color.white, 0.65f);
            }

            case PropStyle.Lamp:
            {
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                // The carrying handle, a bar laid across the top of the cage. Tested before
                // the ring so it can stand out past the cage on both sides.
                if (dy > 8.6f && dy < 11.2f && Mathf.Abs(dx) < 6.5f)
                    return Jitter(dy > 10.4f ? IronDark : IronColor, 0.03f, random);

                if (distance > 11.4f + Wobble(x, y, 0x85EBCA6Bu) * 0.4f) return Nothing;
                if (distance > 9.4f) return Jitter(IronDark, 0.03f, random);
                if (distance > 7.6f) return Jitter(IronColor, 0.04f, random);

                // Three straight bars of the cage crossing the glass, sixty degrees apart.
                // Keying them on the angle instead draws a pinwheel: a bar is a line at a
                // fixed distance from the centre, not a function of the angle to it.
                const float halfBar = 1.1f;
                bool bar = Mathf.Abs(dx) < halfBar ||
                           Mathf.Abs(dx * 0.5f - dy * 0.866f) < halfBar ||
                           Mathf.Abs(dx * 0.5f + dy * 0.866f) < halfBar;
                if (bar) return Jitter(IronColor, 0.03f, random);

                // Glass, hot in the middle and falling away outwards.
                float heat = Mathf.InverseLerp(7.6f, 0f, distance);
                Color glow = Color.Lerp(Shade(FlameColor, -0.26f), Color.Lerp(FlameColor, Color.white, 0.4f), heat);
                return Jitter(glow, 0.03f, random);
            }

            case PropStyle.Corpse:
            {
                // The body first and the pool under whatever it does not cover. Face down
                // with the arms thrown out: from above that is a head, a torso, two arms
                // and two legs, and the silhouette has to carry all of it, because there is
                // no shading at this size that would.
                float head = Ellipse(dx, dy, 0f, 9.5f, 3.6f, 3.6f);
                float torso = Ellipse(dx, dy, 0f, 1.5f, 4.8f, 6.8f);
                float leftArm = Ellipse(dx, dy, -7f, 3.5f, 4.6f, 2.2f);
                float rightArm = Ellipse(dx, dy, 7f, 2f, 4.6f, 2.2f);
                float leftLeg = Ellipse(dx, dy, -2.8f, -8.5f, 2.3f, 4.6f);
                float rightLeg = Ellipse(dx, dy, 3.4f, -8.5f, 2.3f, 4.6f);

                float body = Mathf.Min(Mathf.Min(head, torso),
                    Mathf.Min(Mathf.Min(leftArm, rightArm), Mathf.Min(leftLeg, rightLeg)));

                if (body <= 1f)
                {
                    if (body > 0.84f) return Jitter(Shade(FleshShadow, -0.06f), 0.03f, random);
                    return Jitter(Color.Lerp(FleshColor, FleshShadow, body * 0.6f), 0.035f, random);
                }

                // What it left on the floor.
                float pool = Ellipse(dx, dy, 0f, 2f, 13f, 11f) - Wobble(x, y, 0x27D4EB2Fu) * 0.1f;
                if (pool > 1f) return Nothing;

                Color blood = Shade(BloodColor, (1f - pool) * 0.07f);
                blood.a = Mathf.Clamp01(0.4f + (1f - pool) * 0.6f);
                return Jitter(blood, 0.03f, random);
            }

            case PropStyle.Door:
            {
                // A door leaf seen from above is a long thin rectangle, and everything that
                // makes it a door rather than a plank is drawn across it: the boards run its
                // length, two iron bands cross them, and a ring hangs at the free end. The
                // ring is what tells the player which end opens.
                if (x == 0 || x == _width - 1 || y == 0 || y == _height - 1)
                    return Jitter(DoorEdgeColor, 0.02f, random);

                // The ring, at the end away from the hinge. Drawn first so the bands and
                // boards do not run through it.
                float ringX = x - (_width - 1) * 0.5f;
                float ringY = y - (_height - 9);
                float ring = Mathf.Sqrt(ringX * ringX + ringY * ringY);
                if (ring < 3.6f)
                    return ring > 2.1f ? Jitter(IronColor, 0.04f, random) : Jitter(DoorWoodDark, 0.03f, random);

                // Two bands, in from the ends where the hinges and the lock would be.
                bool band = Mathf.Abs(y - 12) <= 2 || Mathf.Abs(y - (_height - 22)) <= 2;
                if (band)
                {
                    bool bandEdge = Mathf.Abs(y - 12) == 2 || Mathf.Abs(y - (_height - 22)) == 2;
                    return Jitter(bandEdge ? IronDark : IronColor, 0.03f, random);
                }

                // Three boards running the length of the leaf, with the seams between them
                // dark, plus grain wandering along each board.
                bool seam = x == _width / 3 || x == _width - _width / 3 - 1;
                if (seam) return Jitter(DoorEdgeColor, 0.03f, random);

                bool grain = (y + x * 3) % 7 == 0;
                return Jitter(grain ? DoorWoodDark : DoorWoodColor, 0.03f, random);
            }

            default:
                return Nothing;
        }
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>
    /// How far a point lies from an ellipse's centre as a fraction of its radius: under one
    /// is inside it, one is exactly on its edge. Shapes are built by taking the minimum over
    /// several of these, which unions them and leaves the smallest value — how deep inside
    /// the nearest mass the point is — to shade with.
    /// </summary>
    private static float Ellipse(float dx, float dy, float cx, float cy, float rx, float ry)
    {
        float nx = (dx - cx) / rx;
        float ny = (dy - cy) / ry;
        return Mathf.Sqrt(nx * nx + ny * ny);
    }

    /// <summary>
    /// A value in roughly <c>[-1, 1]</c> that varies smoothly across the sprite, used to
    /// take the machine-drawn regularity off an outline. Cheaper than the tiles' noise
    /// because nothing here has to tile: a prop is one sprite with an edge, not a surface
    /// that continues into its neighbour.
    /// </summary>
    private static float Wobble(int x, int y, uint salt)
    {
        uint hash = DeterministicRandom.Hash(salt, x / 4, y / 4);
        return (hash >> 8) / (float)(1 << 23) - 1f;
    }

    /// <summary>Lightens (positive amount) or darkens (negative) a colour, keeping its alpha.</summary>
    private static Color Shade(Color color, float amount)
    {
        return new Color(
            Mathf.Clamp01(color.r + amount),
            Mathf.Clamp01(color.g + amount),
            Mathf.Clamp01(color.b + amount),
            color.a);
    }

    /// <summary>Nudges a colour by a symmetric random amount, keeping it in range and its alpha.</summary>
    private static Color Jitter(Color color, float amount, DeterministicRandom random)
    {
        float delta = (random.NextFloat() * 2f - 1f) * amount;
        return new Color(
            Mathf.Clamp01(color.r + delta),
            Mathf.Clamp01(color.g + delta),
            Mathf.Clamp01(color.b + delta),
            color.a);
    }
}
