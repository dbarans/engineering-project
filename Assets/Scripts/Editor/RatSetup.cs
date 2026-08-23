using UnityEditor;
using UnityEngine;
using static ProceduralArt;

/// <summary>
/// Draws the live rat and builds the prefab the dungeon scatters it as.
///
/// The rat used to exist only as <c>DecalRat</c>, painted flat on the floor tilemap. A
/// dead rat is still worth having and is still drawn there — see
/// <see cref="InkedTileGenerator"/> — but a rat is the one thing a cellar has that moves,
/// and painting it into the floor threw away the only reason to draw it. This one is an
/// object with <see cref="WanderingRat"/> on it, and the decal was redrawn as an
/// unmistakably dead one so the two can never be confused for each other.
///
/// Drawn in the inked idiom of <see cref="ProceduralArt"/> at 128 pixels, like the decals,
/// because it lies on the same hand-drawn floor at the same resolution. Seen from directly
/// above and nose along +X, which is the facing <see cref="WanderingRat"/> rotates from.
/// </summary>
public static class RatSetup
{
    private const string SpritePath = "Assets/Generation/Props/Rat.png";
    private const string PrefabPath = "Assets/Prefabs/Rat.prefab";
    private const string RegistryPath = "Assets/Resources/" + PrefabRegistry.ResourcesPath + ".asset";

    /// <summary>Id the dungeon spawns it under. Matches <c>RoomContentSettings.ratPrefabId</c>.</summary>
    private const string PrefabId = "critter.rat";

    private const int Pixels = 128;

    /// <summary>
    /// How wide the sprite is in the prefab's own units. Spawned content is parented to the
    /// generator's content root, which carries the dungeon's 2× scale, so this is half of
    /// what the rat measures in the world: a little over half a 2-unit cell, nose to tail.
    /// </summary>
    private const float SpriteWorldUnits = 0.6f;

    // The same fur the decal is drawn in, so the live rat and the dead one are the same
    // animal rather than two unrelated drawings that happen to share a name.
    private static readonly Color FurColor = new Color32(0x4E, 0x47, 0x40, 0xFF);
    private static readonly Color SkinColor = new Color32(0x6B, 0x5C, 0x54, 0xFF);

    [MenuItem("Tools/Art/Regenerate Rat")]
    public static void Build()
    {
        Sprite sprite = DrawSprite();
        if (sprite == null) return;

        GameObject prefab = BuildPrefab(sprite);
        Register(prefab);

        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[Rat] Drew '{SpritePath}', built '{PrefabPath}' and registered it as " +
            $"'{PrefabId}'. Regenerate a dungeon to see them; the dead rat stays a floor " +
            "decal and is redrawn by Tools ▸ Dungeon ▸ Redraw Inked Decals.");
    }

    /// <summary>
    /// Draws and builds the rat only when its prefab is missing, so an ordinary scene setup
    /// picks it up on a fresh checkout without overwriting art anyone has replaced by hand
    /// — the same rule the generated tiles follow.
    /// </summary>
    public static void EnsurePrefab()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null) return;
        Build();
    }

    /// <summary>
    /// A rat seen from above, walking: body, head, tail and four feet, nose along +X.
    ///
    /// The tail is the whole silhouette — without it the body is an anonymous lump, and
    /// with it the shape is unmistakable at any size. It is drawn curved rather than
    /// straight for the same reason the decal's is: a straight tail reads as a stick the
    /// animal is standing on.
    /// </summary>
    private static Sprite DrawSprite()
    {
        var canvas = new ArtCanvas(Pixels, Pixels);
        DrawRat(canvas);

        // Pixels-per-unit set from the world size wanted rather than left at the canvas
        // side, so redrawing at another resolution never resizes the animal.
        return WriteSprite(canvas, SpritePath, Pixels / SpriteWorldUnits);
    }

    /// <summary>
    /// The drawing itself, kept apart from writing the asset so it can be rendered and
    /// looked at outside the editor — see GENERATION_NOTES.md on checking generated art
    /// headlessly. Nothing in here touches the asset database.
    /// </summary>
    private static void DrawRat(ArtCanvas canvas)
    {
        var body = new Vector2(56f, 64f);
        var head = new Vector2(88f, 64f);

        // Tail first, so the body sits on top of where it joins.
        Fill(canvas, p => Union(
                Segment(p, new Vector2(32f, 64f), new Vector2(16f, 56f), 3.4f),
                Segment(p, new Vector2(16f, 56f), new Vector2(8f, 40f), 2.8f)),
            SkinColor, 0xB1u, 0.05f);

        // Feet, splayed off the sides — small enough that they only register as an
        // outline, which is all they need to do while the thing is moving.
        foreach (var foot in new[]
                 {
                     new Vector2(46f, 47f), new Vector2(70f, 48f),
                     new Vector2(46f, 81f), new Vector2(70f, 80f)
                 })
        {
            Fill(canvas, p => Ellipse(p, foot, new Vector2(5f, 3.5f)), SkinColor, 0xB2u, 0.05f);
        }

        Fill(canvas, p => SmoothUnion(
                Ellipse(p, body, new Vector2(26f, 17f)),
                Ellipse(p, head, new Vector2(14f, 11f)),
                9f),
            FurColor, 0xB3u, 0.08f);

        // Two ears and the snout, as small forms breaking the head's outline. The decal
        // gets away with one ear because it is lying on the other; this one is upright,
        // and a single ear on a symmetrical body reads as damage.
        Fill(canvas, p => Circle(p, new Vector2(86f, 76f), 6.5f), SkinColor, 0xB4u, 0.05f);
        Fill(canvas, p => Circle(p, new Vector2(86f, 52f), 6.5f), SkinColor, 0xB5u, 0.05f);
        Fill(canvas, p => Circle(p, new Vector2(103f, 64f), 5f), SkinColor, 0xB6u, 0.05f);
    }

    /// <summary>
    /// Renders one shape with a wobbling contour and an uneven ink line — the same
    /// treatment <see cref="InkedTileGenerator"/> gives the floor decals, so the rat sits
    /// in the same drawing as the things around it.
    /// </summary>
    private static void Fill(ArtCanvas canvas, System.Func<Vector2, float> field,
        Color baseColor, uint salt, float blotch)
    {
        var ink = new Color32(0x1B, 0x17, 0x13, 0xFF);

        for (int y = 0; y < canvas.Height; y++)
        {
            for (int x = 0; x < canvas.Width; x++)
            {
                Vector2 point = canvas.Point(x, y);
                float distance = Wobble(field(point), point, 1.8f, 0.05f, salt);

                float coverage = Coverage(distance);
                if (coverage <= 0f) continue;

                Color color = Shade(baseColor, Noise(point, 0.03f, salt, 2) * blotch * 2f);
                color = Shade(color, Noise(point, 0.12f, salt ^ 0x9E3779B9u, 2) * blotch);
                color = Shade(color, -Mathf.Clamp01(1f + distance / 10f) * 0.08f);

                color = Color.Lerp(color, ink,
                    EdgeBand(distance, InkWidth(point, 3f, 5.5f, salt, 0.04f)));
                canvas.Blend(x, y, color, coverage);
            }
        }
    }

    /// <summary>
    /// (Re)builds the prefab in place, so a rat already standing in a saved scene keeps
    /// its link. No collider and nothing on an obstacle layer: the rat is scenery, and
    /// scenery in this project never blocks movement or sight.
    /// </summary>
    private static GameObject BuildPrefab(Sprite sprite)
    {
        var root = new GameObject("Rat");
        try
        {
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;

            // The order the props use. Above the floor and its decals, below the walls —
            // a rat that draws over a wall reads as running along the top of it.
            renderer.sortingOrder = 0;

            root.AddComponent<WanderingRat>();

            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Binds the prefab to <see cref="PrefabId"/> in the registry. Additive and idempotent,
    /// like <c>DungeonSceneSetup.EnsureRegistryEntries</c>: an id already bound is
    /// re-pointed rather than duplicated, and nothing else in the registry is touched.
    /// </summary>
    private static void Register(GameObject prefab)
    {
        var registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(RegistryPath);
        if (registry == null || prefab == null)
        {
            Debug.LogWarning(
                $"[Rat] No PrefabRegistry at '{RegistryPath}' — the prefab was built but " +
                "nothing will spawn it. Run Tools ▸ Dungeon ▸ Setup Scene Tilemaps.");
            return;
        }

        var serialized = new SerializedObject(registry);
        SerializedProperty entries = serialized.FindProperty("entries");

        SerializedProperty entry = null;
        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty candidate = entries.GetArrayElementAtIndex(i);
            if (candidate.FindPropertyRelative("id").stringValue != PrefabId) continue;

            entry = candidate;
            break;
        }

        if (entry == null)
        {
            int index = entries.arraySize;
            entries.InsertArrayElementAtIndex(index);
            entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("id").stringValue = PrefabId;
        }

        entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(registry);
    }
}
