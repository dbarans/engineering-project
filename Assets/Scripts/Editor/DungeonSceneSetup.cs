using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Stage 0 of the procedural generation plan (Assets/Scripts/GENERATION_NOTES.md):
/// prepares the currently open scene to host a generated dungeon. Generates nothing —
/// it only builds the container the later stages paint into.
///
/// Creates, in the open scene:
/// <list type="bullet">
/// <item>a <c>DungeonRoot</c> <see cref="Grid"/> with <c>Floor</c> and <c>Walls</c> tilemaps;</item>
/// <item>the walls collider stack (<see cref="TilemapCollider2D"/> merged into a
/// <see cref="CompositeCollider2D"/> on a static body) on the <c>ObstacleStatic</c>
/// layer, which is what both <see cref="PathfindingGrid"/> and <see cref="FieldOfView"/>
/// already raycast against;</item>
/// </list>
/// and, in the project: placeholder floor/wall <see cref="Tile"/> assets, plus an empty
/// <see cref="PrefabRegistry"/> under Resources.
///
/// Idempotent: re-running reuses whatever already exists and leaves painted tiles alone.
/// </summary>
public static class DungeonSceneSetup
{
    private const string GenerationFolder = "Assets/Generation";
    private const string TilesFolder = GenerationFolder + "/Tiles";
    private const string ResourcesFolder = "Assets/Resources";
    private const string RegistryPath = ResourcesFolder + "/" + PrefabRegistry.ResourcesPath + ".asset";
    private const string SettingsPath = GenerationFolder + "/DungeonGenerationSettings.asset";
    private const string ContentSettingsPath = GenerationFolder + "/RoomContentSettings.asset";

    private const int ObstacleStaticLayer = 8; // matches the obstacle masks on FieldOfView/PathfindingGrid

    // World objects sit at sorting order 0..1, so both tilemaps must draw below them.
    private const int FloorSortingOrder = -100;
    private const int DecalSortingOrder = -75;

    /// <summary>
    /// Above <see cref="DarknessOverlayQuad"/>'s sorting order (6 by default, see the
    /// scene instance), so the black overlay never draws over structure — the same trick
    /// as its own doc comment ("must be higher than the sprites it darkens"), aimed the
    /// other way. Walls, pillars and rubble share this tilemap, so all of them get it.
    ///
    /// This is the Darkwood read of the vision system: architecture stays legible at its
    /// own (dark, desaturated) colour everywhere, the way a ruin's silhouette does even
    /// outside a flashlight beam; only the floor, loot and anything standing on it are
    /// actually fog-of-war'd. It is a deliberate asymmetry, not an oversight — occupants
    /// still vanish outside the FOV stencil exactly as before, this only exempts the
    /// static tilemap from the separate darkening pass.
    ///
    /// Known interaction: it also puts walls above ordinary sprites (order ~0-1), which
    /// includes the door leaf. A door leaf wider than its own doorway cell will draw
    /// partly *behind* the wall/jamb tiles it swings across. Not addressed here — the
    /// door prefab is owned by GU-0054-wall-rule-tiles work, not this pass.
    /// </summary>
    private const int WallSortingOrder = 7;

    /// <summary>Tile texture side in pixels; imported at the same PPU so one tile is one world unit.</summary>
    private const int TilePixels = 32;

    /// <summary>How many floor variants are generated; more of them hides the grid better.</summary>
    private const int FloorVariantCount = 3;

    // Cool and desaturated, and — the part that matters — the floor is the *lightest*
    // thing on screen and the stone is darker than it.
    //
    // The first pass had this the other way round, which put a bright mass of masonry
    // around small dark rooms: the eye read the wall as the subject and the room as a
    // hole in it. The concept art does the opposite, and it has to, because the only
    // thing the player ever sees is the inside of their own vision cone. Everything
    // outside is taken to black by DarknessOverlay regardless of what colour it is, so
    // all the tonal range there is has to be spent on the lit floor.
    private static readonly Color FloorColor = new Color32(0x5E, 0x6A, 0x66, 0xFF);
    private static readonly Color FloorSpeckleColor = new Color32(0x53, 0x5E, 0x5B, 0xFF);
    private static readonly Color WallTopColor = new Color32(0x3B, 0x44, 0x42, 0xFF);
    private static readonly Color WallJointColor = new Color32(0x27, 0x2E, 0x2D, 0xFF);
    private static readonly Color WallCapColor = new Color32(0x6E, 0x7A, 0x76, 0xFF);
    private static readonly Color WallFaceColor = new Color32(0x2E, 0x36, 0x34, 0xFF);
    private static readonly Color WallFaceShadowColor = new Color32(0x17, 0x1B, 0x1A, 0xFF);
    private static readonly Color PillarColor = new Color32(0x6E, 0x7A, 0x76, 0xFF);
    private static readonly Color PillarShadowColor = new Color32(0x25, 0x2B, 0x2A, 0xFF);
    private static readonly Color RubbleColor = new Color32(0x39, 0x41, 0x3F, 0xFF);
    private static readonly Color RubbleChunkColor = new Color32(0x4E, 0x58, 0x55, 0xFF);
    private static readonly Color ThresholdColor = new Color32(0x4A, 0x54, 0x51, 0xFF);
    private static readonly Color JambColor = new Color32(0x6B, 0x76, 0x72, 0xFF);
    private static readonly Color CrackColor = new Color32(0x0F, 0x13, 0x12, 0xFF);
    private static readonly Color StainColor = new Color32(0x16, 0x1A, 0x19, 0xFF);
    private static readonly Color GritColor = new Color32(0x76, 0x82, 0x7E, 0xFF);

    /// <summary>Fully transparent; the floor underneath shows through wherever a decal has nothing to say.</summary>
    private static readonly Color Nothing = new Color(0f, 0f, 0f, 0f);

    /// <summary>How many decal variants are generated.</summary>
    private const int DecalVariantCount = 4;

    [MenuItem("Tools/Dungeon/Setup Scene Tilemaps")]
    public static void Setup()
    {
        EnsureFolders();

        Tile[] floorTiles = EnsureFloorTiles();
        Tile wallTile = EnsureTile("WallTile", TileStyle.WallTop, Tile.ColliderType.Grid);
        Tile wallFaceTile = EnsureTile("WallFaceTile", TileStyle.WallFace, Tile.ColliderType.Grid);
        Tile pillarTile = EnsureTile("PillarTile", TileStyle.Pillar, Tile.ColliderType.Grid);
        Tile rubbleTile = EnsureTile("RubbleTile", TileStyle.Rubble, Tile.ColliderType.Grid);
        Tile doorwayTile = EnsureTile("DoorwayTile", TileStyle.Doorway, Tile.ColliderType.None);
        Tile[] decalTiles = EnsureDecalTiles();
        Tile[] wallAutotiles = EnsureWallAutotiles();
        PrefabRegistry registry = EnsureRegistry();
        EnsureRegistryEntries(registry);
        DungeonGenerationSettings settings = EnsureSettings();
        RoomContentSettings contentSettings = EnsureContentSettings();

        Grid grid = EnsureGrid();
        Tilemap floor = EnsureFloorTilemap(grid);
        Tilemap decals = EnsureDecalTilemap(grid);
        Tilemap walls = EnsureWallTilemap(grid);

        WarnOnCellSizeMismatch(grid);
        WireGenerator(grid, floor, decals, walls, floorTiles, wallTile, wallFaceTile,
            pillarTile, rubbleTile, doorwayTile, decalTiles, wallAutotiles,
            settings, contentSettings, registry);

        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[DungeonSetup] Ready. Floor='{floor.name}', Walls='{walls.name}' " +
            $"(layer {LayerMask.LayerToName(ObstacleStaticLayer)}), tiles in '{TilesFolder}', " +
            $"registry at '{RegistryPath}' ({registry.Entries.Count} entries).");

        EditorUtility.DisplayDialog("Dungeon Setup",
            "Scene prepared for procedural generation.\n\n" +
            "• DungeonRoot ▸ Floor / Decals / Walls tilemaps\n" +
            "• Walls: TilemapCollider2D merged into a CompositeCollider2D, layer ObstacleStatic\n" +
            "• DungeonPainter + DungeonBuilder wired to the tilemaps and the PathfindingGrid\n" +
            "• Placeholder FloorTile / WallTile assets and a settings asset\n" +
            "• PrefabRegistry in Resources\n\n" +
            "To generate: select DungeonRoot and use the DungeonBuilder context menu " +
            "(right-click the component header) ▸ Generate. Tools ▸ Dungeon ▸ Layout Preview " +
            "renders layouts as ASCII without touching the scene.",
            "OK");

        Selection.activeGameObject = grid.gameObject;
    }

    // ---------------------------------------------------------------- scene objects

    private static Grid EnsureGrid()
    {
        var existing = GameObject.Find("DungeonRoot");
        if (existing != null) return EditorSetupUtility.EnsureComponent<Grid>(existing);

        var root = new GameObject("DungeonRoot");
        Undo.RegisterCreatedObjectUndo(root, "Create Dungeon Root");
        var grid = root.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);
        return grid;
    }

    private static Tilemap EnsureFloorTilemap(Grid grid)
    {
        Tilemap tilemap = EnsureTilemap(grid, "Floor");
        EditorSetupUtility.EnsureComponent<TilemapRenderer>(tilemap.gameObject).sortingOrder = FloorSortingOrder;
        return tilemap;
    }

    /// <summary>
    /// Wear layer. Sits between the floor and the walls and carries no collider, so a
    /// crack painted across a doorway is scenery and nothing more.
    /// </summary>
    private static Tilemap EnsureDecalTilemap(Grid grid)
    {
        Tilemap tilemap = EnsureTilemap(grid, "Decals");
        EditorSetupUtility.EnsureComponent<TilemapRenderer>(tilemap.gameObject).sortingOrder = DecalSortingOrder;
        return tilemap;
    }

    /// <summary>
    /// Walls tilemap plus the collider stack. The composite is what makes this cheap:
    /// it merges thousands of per-tile boxes into a handful of polygons, which matters
    /// because every FOV rebuild raycasts against them each frame.
    /// </summary>
    private static Tilemap EnsureWallTilemap(Grid grid)
    {
        Tilemap tilemap = EnsureTilemap(grid, "Walls");
        var go = tilemap.gameObject;

        go.layer = ObstacleStaticLayer;
        EditorSetupUtility.EnsureComponent<TilemapRenderer>(go).sortingOrder = WallSortingOrder;

        // Order matters: the composite needs a body, and the tilemap collider needs the
        // composite to merge into.
        EditorSetupUtility.EnsureComponent<Rigidbody2D>(go).bodyType = RigidbodyType2D.Static;
        EditorSetupUtility.EnsureComponent<CompositeCollider2D>(go).geometryType = CompositeCollider2D.GeometryType.Polygons;
        EditorSetupUtility.EnsureComponent<TilemapCollider2D>(go).compositeOperation = Collider2D.CompositeOperation.Merge;

        return tilemap;
    }

    private static Tilemap EnsureTilemap(Grid grid, string name)
    {
        Transform child = grid.transform.Find(name);
        if (child != null)
        {
            EditorSetupUtility.EnsureComponent<TilemapRenderer>(child.gameObject);
            return EditorSetupUtility.EnsureComponent<Tilemap>(child.gameObject);
        }

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, $"Create {name} Tilemap");
        go.transform.SetParent(grid.transform, false);
        var tilemap = go.AddComponent<Tilemap>();
        go.AddComponent<TilemapRenderer>();
        return tilemap;
    }

    /// <summary>
    /// Adds and wires the painter and the builder. Wiring here rather than by hand
    /// means the collider/grid ordering the builder depends on cannot be broken by a
    /// missing inspector reference.
    /// </summary>
    private static void WireGenerator(Grid grid, Tilemap floor, Tilemap decals, Tilemap walls,
        Tile[] floorTiles, Tile wallTile, Tile wallFaceTile, Tile pillarTile, Tile rubbleTile,
        Tile doorwayTile, Tile[] decalTiles, Tile[] wallAutotiles,
        DungeonGenerationSettings settings,
        RoomContentSettings contentSettings, PrefabRegistry registry)
    {
        var root = grid.gameObject;

        var painter = EditorSetupUtility.EnsureComponent<DungeonPainter>(root);
        SetRef(painter, "floorTilemap", floor);
        SetRef(painter, "decalTilemap", decals);
        SetRef(painter, "wallTilemap", walls);
        SetArray(painter, "decalTiles", decalTiles);
        SetArray(painter, "wallAutotiles", wallAutotiles);
        SetArray(painter, "floorTiles", floorTiles);
        SetRef(painter, "wallTile", wallTile);
        SetRef(painter, "wallFaceTile", wallFaceTile);
        SetRef(painter, "pillarTile", pillarTile);
        SetRef(painter, "rubbleTile", rubbleTile);
        SetRef(painter, "doorwayTile", doorwayTile);

        var builder = EditorSetupUtility.EnsureComponent<DungeonBuilder>(root);
        SetRef(builder, "settings", settings);
        SetRef(builder, "painter", painter);
        SetRef(builder, "pathfindingGrid", Object.FindFirstObjectByType<PathfindingGrid>());

        var populator = EditorSetupUtility.EnsureComponent<DungeonPopulator>(root);
        SetRef(populator, "builder", builder);
        SetRef(populator, "content", contentSettings);
        SetRef(populator, "registry", registry);
    }

    /// <summary>
    /// The generator maps grid cells 1:1 onto pathfinding cells, so a Grid cell size
    /// other than the PathfindingGrid's would offset every spawn from its tile.
    /// </summary>
    private static void WarnOnCellSizeMismatch(Grid grid)
    {
        var pathfinding = Object.FindFirstObjectByType<PathfindingGrid>();
        if (pathfinding == null)
        {
            Debug.LogWarning(
                "[DungeonSetup] No PathfindingGrid in this scene — the generator needs one. " +
                "Copy it from a scene that has it (e.g. Dominik 04).");
            return;
        }

        if (!Mathf.Approximately(grid.cellSize.x, pathfinding.CellSize) ||
            !Mathf.Approximately(grid.cellSize.y, pathfinding.CellSize))
        {
            Debug.LogWarning(
                $"[DungeonSetup] Tilemap cell size {grid.cellSize.x}x{grid.cellSize.y} differs from " +
                $"PathfindingGrid.cellSize {pathfinding.CellSize}. Make them equal or generated " +
                "content will not line up with its tiles.", grid);
        }
    }

    // ---------------------------------------------------------------- assets

    /// <summary>
    /// Creates (or reuses) one placeholder tile asset. The texture is only written when
    /// missing, so art swapped in by hand survives a re-run; <see cref="RegenerateTiles"/>
    /// is the explicit way to get the generated art back.
    /// </summary>
    private static Tile EnsureTile(string name, TileStyle style, Tile.ColliderType colliderType,
        bool overwrite = false)
    {
        string texturePath = $"{TilesFolder}/{name}.png";
        string tilePath = $"{TilesFolder}/{name}.asset";

        if (overwrite || !File.Exists(texturePath))
        {
            File.WriteAllBytes(texturePath, BuildTileTexture(style, name));
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextureImporter(texturePath);
        }

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(texturePath);
        if (sprite == null)
        {
            Debug.LogError($"[DungeonSetup] No sprite imported from '{texturePath}'.");
            return null;
        }

        var tile = AssetDatabase.LoadAssetAtPath<Tile>(tilePath);
        if (tile == null)
        {
            tile = ScriptableObject.CreateInstance<Tile>();
            AssetDatabase.CreateAsset(tile, tilePath);
        }

        tile.sprite = sprite;
        tile.colliderType = colliderType;
        EditorUtility.SetDirty(tile);
        return tile;
    }

    /// <summary>Which of the three placeholder looks to draw.</summary>
    private enum TileStyle
    {
        /// <summary>Flat ground: base colour plus grain and the odd darker speckle.</summary>
        Floor,

        /// <summary>Wall seen from above: lighter stone, broken into courses.</summary>
        WallTop,

        /// <summary>Wall seen face-on: darker, with a lit cap along the top edge.</summary>
        WallFace,

        /// <summary>
        /// Free-standing pillar: a lit round column on a dark base, so it reads as an
        /// object standing in the room rather than as a piece of the wall.
        /// </summary>
        Pillar,

        /// <summary>Collapsed masonry: scattered lighter chunks over a dark bed.</summary>
        Rubble,

        /// <summary>
        /// Doorway threshold: a dark slab framed by two lit jamb blocks. The frame is the
        /// whole point — an opening painted as plain floor reads as a hole knocked in a
        /// wall, and the concept art's doorways are unmistakably built.
        /// </summary>
        Doorway,

        /// <summary>Decal: a fracture running across the tile, with the odd branch off it.</summary>
        Crack,

        /// <summary>Decal: an irregular dark patch, as of damp or old spillage.</summary>
        Stain,

        /// <summary>Decal: loose chippings, lighter than the floor they lie on.</summary>
        Grit,

        /// <summary>
        /// One of the sixteen wall autotile variants. Which one is carried separately in
        /// <see cref="_wallMask"/> rather than as sixteen enum members, because the drawing
        /// is one routine parameterised by the mask — the whole point of doing it this way.
        /// </summary>
        WallAutotile
    }

    /// <summary>Exposure mask for the wall variant currently being drawn: 1 N, 2 E, 4 S, 8 W.</summary>
    private static int _wallMask;

    /// <summary>
    /// Decals are drawn as a whole rather than pixel by pixel, because a crack is a path
    /// across the tile and a path cannot be decided from one pixel's coordinates alone.
    /// </summary>
    private static bool IsDecal(TileStyle style)
    {
        return style == TileStyle.Crack || style == TileStyle.Stain || style == TileStyle.Grit;
    }

    /// <summary>
    /// Draws a placeholder tile as a PNG.
    ///
    /// Flat colour was enough to judge layouts but reads as a spreadsheet on screen. Two
    /// cheap tricks do most of the work: per-pixel grain, which kills the flatness, and
    /// a lit cap on the wall face, which is what makes a top-down scene read as rooms
    /// with height rather than as a floor plan.
    ///
    /// The noise is seeded from the tile name, so re-running produces byte-identical
    /// files and does not churn the repository.
    /// </summary>
    private static byte[] BuildTileTexture(TileStyle style, string seed)
    {
        var random = new DeterministicRandom(seed);
        var texture = new Texture2D(TilePixels, TilePixels, TextureFormat.RGBA32, false);

        try
        {
            if (IsDecal(style))
            {
                DrawDecal(texture, style, random);
            }
            else
            {
                for (int y = 0; y < TilePixels; y++)
                {
                    for (int x = 0; x < TilePixels; x++)
                        texture.SetPixel(x, y, PixelColor(style, x, y, random));
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

    private static Color PixelColor(TileStyle style, int x, int y, DeterministicRandom random)
    {
        switch (style)
        {
            case TileStyle.WallAutotile:
            {
                // Masonry on top, and a drawn edge on every exposed side, flat and uniform
                // across all four — no faked height on any side. This is a top-down game;
                // a lit cap and a falling shadow on the south side read as a wall viewed at
                // an angle, which is exactly the wrong read for a plan seen from directly
                // above. The edges alone are what makes the outline legible: without them a
                // corner, a straight run and a one-cell buttress are the same twelve pixels
                // of stone and the whole structure reads as one undifferentiated slab.
                bool north = (_wallMask & 1) != 0;
                bool east = (_wallMask & 2) != 0;
                bool south = (_wallMask & 4) != 0;
                bool west = (_wallMask & 8) != 0;

                const int rim = 3;
                bool onRim =
                    (north && y >= TilePixels - rim) ||
                    (east && x >= TilePixels - rim) ||
                    (west && x < rim) ||
                    (south && y < rim);

                if (onRim) return Jitter(WallJointColor, 0.02f, random);

                goto case TileStyle.WallTop;
            }

            case TileStyle.WallTop:
            {
                // Horizontal courses with staggered vertical joints, so the wall reads as
                // masonry instead of a single slab.
                int course = y / (TilePixels / 4);
                bool jointRow = y % (TilePixels / 4) == 0;
                bool jointColumn = (x + (course % 2) * (TilePixels / 4)) % (TilePixels / 2) == 0;

                Color color = jointRow || jointColumn ? WallJointColor : WallTopColor;
                return Jitter(color, 0.02f, random);
            }

            case TileStyle.WallFace:
            {
                // The top rows catch the light; everything below falls away into shadow.
                const int capHeight = 6;
                if (y >= TilePixels - capHeight) return Jitter(WallCapColor, 0.02f, random);

                float depth = 1f - y / (float)(TilePixels - capHeight);
                Color color = Color.Lerp(WallFaceColor, WallFaceShadowColor, depth * 0.6f);
                return Jitter(color, 0.025f, random);
            }

            case TileStyle.Pillar:
            {
                // A disc rather than a square, so a colonnade reads as columns instead of
                // as a grid of wall stubs. Outside the disc is floor-dark, which is what
                // makes the pillar look like it is standing on the floor.
                const float radius = 12f;
                float dx = x - (TilePixels - 1) * 0.5f;
                float dy = y - (TilePixels - 1) * 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                if (distance > radius) return Jitter(FloorColor, 0.02f, random);

                // Lit from the top: the shading is what stops a flat disc looking like a hole.
                float lit = Mathf.InverseLerp(radius, -radius * 0.4f, dy - distance * 0.3f);
                Color color = Color.Lerp(PillarShadowColor, PillarColor, lit);
                return Jitter(color, 0.025f, random);
            }

            case TileStyle.Doorway:
            {
                // Jambs down the left and right edges; the slab between them is darker
                // than the surrounding floor, so the threshold reads as a step through.
                const int jambWidth = 5;
                if (x < jambWidth || x >= TilePixels - jambWidth)
                {
                    // A dark seam where the jamb meets the threshold, so the two blocks
                    // do not merge into one bar at small zoom.
                    bool seam = x == jambWidth - 1 || x == TilePixels - jambWidth;
                    return Jitter(seam ? WallJointColor : JambColor, 0.02f, random);
                }

                return Jitter(ThresholdColor, 0.025f, random);
            }

            case TileStyle.Rubble:
            {
                // Chunks on a 4px lattice with jittered membership: regular enough to read
                // as broken masonry, irregular enough not to read as a pattern.
                bool chunk = (x / 4 + y / 4) % 2 == 0 ? random.Chance(0.75f) : random.Chance(0.25f);
                Color color = chunk ? RubbleChunkColor : RubbleColor;
                return Jitter(color, 0.04f, random);
            }

            default:
            {
                // Occasional darker speckles read as grit and break up the grain.
                Color color = random.Chance(0.04f) ? FloorSpeckleColor : FloorColor;
                return Jitter(color, 0.03f, random);
            }
        }
    }

    /// <summary>
    /// Draws one decal over a transparent tile. Everything not marked stays transparent,
    /// which is what lets the same decal sit on any of the floor variants.
    /// </summary>
    private static void DrawDecal(Texture2D texture, TileStyle style, DeterministicRandom random)
    {
        for (int y = 0; y < TilePixels; y++)
        {
            for (int x = 0; x < TilePixels; x++)
                texture.SetPixel(x, y, Nothing);
        }

        switch (style)
        {
            case TileStyle.Crack:
            {
                // A drunk walk from one edge towards the far one. The wander is what makes
                // it read as a fracture; a straight line reads as a seam between tiles.
                bool horizontal = random.Chance(0.5f);
                float drift = random.RangeInclusive(6, TilePixels - 7);

                for (int i = 0; i < TilePixels; i++)
                {
                    drift += (random.NextFloat() * 2f - 1f) * 1.4f;
                    drift = Mathf.Clamp(drift, 2f, TilePixels - 3f);
                    int across = Mathf.RoundToInt(drift);

                    PlotCrack(texture, horizontal, i, across, random);

                    // A short spur every so often, so the fracture forks instead of
                    // running the whole width as one unbroken stroke.
                    if (!random.Chance(0.07f)) continue;

                    int spur = random.RangeInclusive(2, 5);
                    int direction = random.Chance(0.5f) ? 1 : -1;
                    for (int s = 1; s <= spur; s++)
                        PlotCrack(texture, horizontal, i + s * direction, across + s * direction, random);
                }
                return;
            }

            case TileStyle.Stain:
            {
                // Overlapping discs rather than one circle: a single disc reads as a dot,
                // and the union of a few reads as something that soaked outwards.
                int blobs = random.RangeInclusive(4, 7);
                for (int b = 0; b < blobs; b++)
                {
                    float cx = random.RangeInclusive(8, TilePixels - 9);
                    float cy = random.RangeInclusive(8, TilePixels - 9);
                    float radius = random.RangeInclusive(4, 9);

                    for (int y = 0; y < TilePixels; y++)
                    {
                        for (int x = 0; x < TilePixels; x++)
                        {
                            float dx = x - cx;
                            float dy = y - cy;
                            if (dx * dx + dy * dy > radius * radius) continue;

                            // Edges fade, so the patch has no hard outline to give away
                            // that it is a 32-pixel square laid over the floor.
                            float edge = 1f - Mathf.Sqrt(dx * dx + dy * dy) / radius;
                            float alpha = Mathf.Clamp01(edge * 0.75f);

                            Color existing = texture.GetPixel(x, y);
                            if (alpha <= existing.a) continue;

                            texture.SetPixel(x, y, new Color(StainColor.r, StainColor.g, StainColor.b, alpha));
                        }
                    }
                }
                return;
            }

            default:
            {
                // Chippings: mostly single pixels with the occasional two-by-two lump.
                int chips = random.RangeInclusive(18, 34);
                for (int c = 0; c < chips; c++)
                {
                    int x = random.RangeInclusive(1, TilePixels - 3);
                    int y = random.RangeInclusive(1, TilePixels - 3);
                    int size = random.Chance(0.25f) ? 2 : 1;

                    Color color = Jitter(GritColor, 0.05f, random);
                    color.a = 0.55f + random.NextFloat() * 0.35f;

                    for (int dy = 0; dy < size; dy++)
                    {
                        for (int dx = 0; dx < size; dx++)
                            texture.SetPixel(x + dx, y + dy, color);
                    }
                }
                return;
            }
        }
    }

    /// <summary>Plots one cell of a crack, given the run/across coordinates and its axis.</summary>
    private static void PlotCrack(Texture2D texture, bool horizontal, int along, int across,
        DeterministicRandom random)
    {
        if (along < 0 || along >= TilePixels || across < 0 || across >= TilePixels) return;

        int x = horizontal ? along : across;
        int y = horizontal ? across : along;

        Color color = Jitter(CrackColor, 0.03f, random);
        color.a = 0.65f + random.NextFloat() * 0.3f;
        texture.SetPixel(x, y, color);
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

    /// <summary>Point filtering and PPU = tile size, so one tile covers exactly one world unit.</summary>
    private static void ConfigureTextureImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = TilePixels;
        importer.filterMode = FilterMode.Point;

        // Decals are mostly transparent; without this Unity premultiplies them and the
        // soft edges of a stain come out ringed in black.
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }

    private static PrefabRegistry EnsureRegistry()
    {
        var registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(RegistryPath);
        if (registry != null) return registry;

        registry = ScriptableObject.CreateInstance<PrefabRegistry>();
        AssetDatabase.CreateAsset(registry, RegistryPath);
        Debug.Log(
            $"[DungeonSetup] Created an empty PrefabRegistry at '{RegistryPath}'. " +
            "Populate it in Stage 3, when the generator starts spawning content.");
        return registry;
    }

    private static DungeonGenerationSettings EnsureSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<DungeonGenerationSettings>(SettingsPath);
        if (settings != null) return settings;

        settings = ScriptableObject.CreateInstance<DungeonGenerationSettings>();
        AssetDatabase.CreateAsset(settings, SettingsPath);
        return settings;
    }

    /// <summary>
    /// Registers the project's existing prefabs under stable ids. Additive: ids already
    /// present are left alone, because those ids may already appear in save files.
    /// </summary>
    private static void EnsureRegistryEntries(PrefabRegistry registry)
    {
        var wanted = new (string id, string path)[]
        {
            ("world.door", "Assets/Prefabs/Door_System.prefab"),
            ("world.savestation", "Assets/Prefabs/World/SaveStation.prefab"),
            ("world.lamp", "Assets/Prefabs/Lamp.prefab"),
            ("world.craftingtable", "Assets/Prefabs/World/CraftingTable.prefab"),
            ("world.chest", "Assets/Prefabs/World/Chest.prefab"),
            ("prop.barrel", "Assets/Prefabs/Barrel.prefab"),
            ("prop.table", "Assets/Prefabs/Table.prefab"),
            ("enemy.skullguy", "Assets/Prefabs/SkullGuyEnemy.prefab"),
            ("enemy.blindlistener", "Assets/Prefabs/BlindListenerEnemy.prefab")
        };

        var serialized = new SerializedObject(registry);
        SerializedProperty entries = serialized.FindProperty("entries");

        var known = new HashSet<string>();
        for (int i = 0; i < entries.arraySize; i++)
            known.Add(entries.GetArrayElementAtIndex(i).FindPropertyRelative("id").stringValue);

        foreach (var (id, path) in wanted)
        {
            if (known.Contains(id)) continue;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning($"[DungeonSetup] No prefab at '{path}' — id '{id}' not registered.");
                continue;
            }

            int index = entries.arraySize;
            entries.InsertArrayElementAtIndex(index);
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("id").stringValue = id;
            entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
        }

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(registry);
    }

    /// <summary>
    /// Creates the content settings asset with a starting spawn table. Item ids are read
    /// off the assets rather than hard-coded, because <see cref="ItemData.Id"/> is an
    /// opaque guid that says nothing about which item it belongs to.
    /// </summary>
    private static RoomContentSettings EnsureContentSettings()
    {
        var settings = AssetDatabase.LoadAssetAtPath<RoomContentSettings>(ContentSettingsPath);
        if (settings != null) return settings;

        settings = ScriptableObject.CreateInstance<RoomContentSettings>();

        settings.enemies.Add(new RoomContentSettings.PrefabChoice
        { prefabId = "enemy.skullguy", weight = 1f });
        settings.enemies.Add(new RoomContentSettings.PrefabChoice
        { prefabId = "enemy.blindlistener", weight = 0.7f, minDepth = 2 });

        settings.props.Add(new RoomContentSettings.PrefabChoice { prefabId = "prop.barrel", weight = 1f });
        settings.props.Add(new RoomContentSettings.PrefabChoice { prefabId = "prop.table", weight = 0.6f });

        AddLoot(settings.loot, "Assets/Items/Item 7 - Bullet.asset", 1.2f, 2, 6);
        AddLoot(settings.loot, "Assets/Items/Item 3 - Wood.asset", 1f, 1, 3);
        AddLoot(settings.loot, "Assets/Items/Item 2 - Mana Potion.asset", 0.5f, 1, 1);

        AddLoot(settings.treasureLoot, "Assets/Items/Item 5 - Pistol.asset", 1f, 1, 1);
        AddLoot(settings.treasureLoot, "Assets/Items/Item 1 - Sword.asset", 1f, 1, 1);
        AddLoot(settings.treasureLoot, "Assets/Items/Coins.asset", 1.5f, 5, 15);

        // Ink is what saving costs, so a run with none in it cannot be saved at all.
        var ink = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Items/Item 6 - Ink.asset");
        if (ink != null) settings.guaranteedItemId = ink.Id;

        AssetDatabase.CreateAsset(settings, ContentSettingsPath);
        return settings;
    }

    private static void AddLoot(List<RoomContentSettings.ItemChoice> pool,
        string assetPath, float weight, int minCount, int maxCount)
    {
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(assetPath);
        if (item == null)
        {
            Debug.LogWarning($"[DungeonSetup] No item asset at '{assetPath}' — left out of the loot table.");
            return;
        }

        pool.Add(new RoomContentSettings.ItemChoice
        {
            itemId = item.Id,
            weight = weight,
            minCount = minCount,
            maxCount = maxCount
        });
    }

    /// <summary>
    /// The floor variants. Cells pick between them from the dungeon seed, which is what
    /// stops a large room looking like tiled wallpaper.
    /// </summary>
    private static Tile[] EnsureFloorTiles()
    {
        var tiles = new Tile[FloorVariantCount];
        for (int i = 0; i < tiles.Length; i++)
        {
            // Suffixed rather than numbered from 0, so the first one keeps the name the
            // earlier setup runs already created.
            string name = i == 0 ? "FloorTile" : $"FloorTile{(char)('A' + i)}";
            tiles[i] = EnsureTile(name, TileStyle.Floor, Tile.ColliderType.None);
        }
        return tiles;
    }

    /// <summary>
    /// The sixteen wall variants, one per combination of exposed sides.
    ///
    /// Generated rather than authored because the alternative is a hand-cut wall sheet,
    /// and that has been the blocker on making outlines readable since Stage 5. These are
    /// placeholders in the same sense as the rest: enough for the geometry to be legible
    /// and to be judged, not a substitute for real art. When the real sheet arrives it can
    /// either fill these same sixteen slots or be swapped for a Rule Tile — the painter
    /// only asks for a tile per mask and does not care which.
    /// </summary>
    private static Tile[] EnsureWallAutotiles(bool overwrite = false)
    {
        var tiles = new Tile[16];
        for (int mask = 0; mask < tiles.Length; mask++)
        {
            _wallMask = mask;
            tiles[mask] = EnsureTile($"WallTile_{mask:00}", TileStyle.WallAutotile,
                Tile.ColliderType.Grid, overwrite);
        }
        return tiles;
    }

    /// <summary>
    /// The decal variants: two fractures, a stain and a scatter of chippings. Four is
    /// enough that a room does not visibly repeat, and few enough that each one stays
    /// recognisable rather than dissolving into general texture.
    /// </summary>
    private static Tile[] EnsureDecalTiles(bool overwrite = false)
    {
        var styles = new (string name, TileStyle style)[]
        {
            ("DecalCrackA", TileStyle.Crack),
            ("DecalCrackB", TileStyle.Crack),
            ("DecalStain", TileStyle.Stain),
            ("DecalGrit", TileStyle.Grit)
        };

        var tiles = new Tile[DecalVariantCount];
        for (int i = 0; i < styles.Length && i < tiles.Length; i++)
            tiles[i] = EnsureTile(styles[i].name, styles[i].style, Tile.ColliderType.None, overwrite);

        return tiles;
    }

    /// <summary>
    /// Redraws the placeholder textures over the existing assets. Separate from
    /// <see cref="Setup"/>, which never overwrites, so that art replaced by hand is not
    /// silently thrown away by a routine re-run.
    /// </summary>
    [MenuItem("Tools/Dungeon/Regenerate Placeholder Tiles")]
    public static void RegenerateTiles()
    {
        if (!EditorUtility.DisplayDialog("Regenerate Placeholder Tiles",
                "Redraw the generated placeholder art, overwriting the PNGs in " +
                TilesFolder + ".\n\nAny tile art you replaced by hand will be lost.",
                "Regenerate", "Cancel"))
            return;

        EnsureFolders();

        for (int i = 0; i < FloorVariantCount; i++)
        {
            string name = i == 0 ? "FloorTile" : $"FloorTile{(char)('A' + i)}";
            EnsureTile(name, TileStyle.Floor, Tile.ColliderType.None, overwrite: true);
        }
        EnsureTile("WallTile", TileStyle.WallTop, Tile.ColliderType.Grid, overwrite: true);
        EnsureTile("WallFaceTile", TileStyle.WallFace, Tile.ColliderType.Grid, overwrite: true);
        EnsureTile("PillarTile", TileStyle.Pillar, Tile.ColliderType.Grid, overwrite: true);
        EnsureTile("RubbleTile", TileStyle.Rubble, Tile.ColliderType.Grid, overwrite: true);
        EnsureTile("DoorwayTile", TileStyle.Doorway, Tile.ColliderType.None, overwrite: true);
        EnsureDecalTiles(overwrite: true);
        EnsureWallAutotiles(overwrite: true);

        AssetDatabase.SaveAssets();
        Debug.Log($"[DungeonSetup] Placeholder tiles redrawn in '{TilesFolder}'.");
    }

    /// <summary>Sets a private serialized field by name, the way the other setup tools do.</summary>
    private static void SetRef(Object component, string field, Object value)
    {
        var serialized = new SerializedObject(component);
        var property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning($"[DungeonSetup] Field '{field}' not found on {component.GetType().Name}.");
            return;
        }
        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Fills a private serialized object array by name.</summary>
    private static void SetArray(Object component, string field, Object[] values)
    {
        var serialized = new SerializedObject(component);
        var property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning($"[DungeonSetup] Field '{field}' not found on {component.GetType().Name}.");
            return;
        }

        property.ClearArray();
        for (int i = 0; i < values.Length; i++)
        {
            property.InsertArrayElementAtIndex(i);
            property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureFolders()
    {
        EnsureFolder(GenerationFolder);
        EnsureFolder(TilesFolder);
        EnsureFolder(ResourcesFolder);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;

        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        string leaf = Path.GetFileName(path);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }
}
