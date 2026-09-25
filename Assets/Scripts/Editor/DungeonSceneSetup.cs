using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
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

    /// <summary>How many variants of the exit room's floor are generated.</summary>
    private const int ExitFloorVariantCount = 3;

    /// <summary>
    /// Radius of the pillar's column in pixels, of the 16 a tile's half-width gives. Short
    /// of the full cell so there is floor visible around the column and it reads as
    /// something standing in the room. It is also the radius of the column's collider —
    /// see <see cref="AssignCircularPhysicsShape"/> — so the shadow it casts matches the
    /// stone that is drawn.
    /// </summary>
    private const float PillarRadius = 12.5f;

    /// <summary>
    /// How many sides the pillar's collider polygon has. Enough that a linecast grazing the
    /// column cannot tell it from a circle at the size it is drawn (25 pixels across), few
    /// enough that a colonnade stays cheap to merge into the composite collider.
    /// </summary>
    private const int PillarColliderSegments = 16;

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
    private static readonly Color ExitFloorColor = new Color32(0x6A, 0x74, 0x6F, 0xFF);
    private static readonly Color ExitFloorJointColor = new Color32(0x3F, 0x47, 0x44, 0xFF);
    private static readonly Color RubbleColor = new Color32(0x39, 0x41, 0x3F, 0xFF);
    private static readonly Color RubbleChunkColor = new Color32(0x4E, 0x58, 0x55, 0xFF);
    private static readonly Color ThresholdColor = new Color32(0x4A, 0x54, 0x51, 0xFF);
    private static readonly Color JambColor = new Color32(0x6B, 0x76, 0x72, 0xFF);
    private static readonly Color CrackColor = new Color32(0x0F, 0x13, 0x12, 0xFF);
    private static readonly Color StainColor = new Color32(0x16, 0x1A, 0x19, 0xFF);
    private static readonly Color GritColor = new Color32(0x76, 0x82, 0x7E, 0xFF);

    // The dungeon's growing and rotting things. Kept at the same low saturation as the
    // stone — except the moss, which gets the only real hue in the palette, because a
    // patch of damp growth is the one thing down here that is meant to look alive.
    private static readonly Color MushroomCapColor = new Color32(0xB4, 0xAE, 0x93, 0xFF);
    private static readonly Color MushroomStemColor = new Color32(0x8C, 0x87, 0x72, 0xFF);
    private static readonly Color MushroomShadeColor = new Color32(0x4E, 0x4A, 0x3D, 0xFF);
    private static readonly Color BoneColor = new Color32(0xAE, 0xA8, 0x93, 0xFF);
    private static readonly Color BoneShadeColor = new Color32(0x5C, 0x57, 0x49, 0xFF);
    private static readonly Color MossColor = new Color32(0x4F, 0x6B, 0x4A, 0xFF);
    private static readonly Color MossLightColor = new Color32(0x68, 0x84, 0x5C, 0xFF);
    private static readonly Color PuddleColor = new Color32(0x1C, 0x26, 0x28, 0xFF);
    private static readonly Color PuddleSheenColor = new Color32(0x7E, 0x8C, 0x8A, 0xFF);

    /// <summary>Fully transparent; the floor underneath shows through wherever a decal has nothing to say.</summary>
    private static readonly Color Nothing = new Color(0f, 0f, 0f, 0f);

    /// <summary>
    /// Seed for the wall's stonework. A constant rather than the tile's own name, so all
    /// sixteen autotile variants share one course of masonry and a long wall run reads as
    /// continuous stone instead of as sixteen unrelated swatches butted together.
    /// </summary>
    private const uint MasonrySeed = 0x5A17E501u;

    /// <summary>How many decal variants are generated.</summary>
    private const int DecalVariantCount = 8;

    // ---------------------------------------------------------------- hand-drawn art

    private const string ArtFolder = "Assets/Art";

    /// <summary>Cobblestone ground. One large slab, not a seamless tile — see <see cref="EnsureFloorMosaic"/>.</summary>
    private const string FloorArtPath = ArtFolder + "/FURNITURE_pngy_podloga.png";

    /// <summary>Loose paper sheets, drawn as separate groups on one transparent sheet.</summary>
    private const string PaperArtPath = ArtFolder + "/FURNITURE_pngy_papier.png";

    /// <summary>
    /// Ground clutter cut whole, one sprite per source — unlike the paper art, each of these
    /// files is already exactly one thing to place, with nothing overlapping to separate out.
    /// <see cref="NatureDecalWeight"/> is the frequency the painter's uniform pick gives it,
    /// realised as how many times its tile is repeated in the decal array rather than as a
    /// second selection step, so the existing uniform picker in <c>DungeonPainter.PaintDecals</c>
    /// does not need to learn about weights at all.
    /// </summary>
    private static readonly (string name, string artPath, int weight)[] NatureDecals =
    {
        ("DecalMushroomA", ArtFolder + "/mushroom.png", 3),
        ("DecalMushroomB", ArtFolder + "/mushroom02.png", 3),
        // A dropped sleeping bag reads as a small scene — someone was here — which is worth
        // more if it stays uncommon than if it turns up as often as a mushroom.
        ("DecalSleepingBag", ArtFolder + "/sleepingbag01.png", 1),
    };

    /// <summary>
    /// Resolution of a tile cut from hand-drawn art, with its PPU set to match so it still
    /// covers exactly one world unit. Higher than <see cref="TilePixels"/> because the
    /// generated placeholders are flat noise that survives being tiny, while the real art has
    /// detail worth keeping; the two can coexist on the same tilemap precisely because each
    /// texture carries its own PPU.
    /// </summary>
    private const int ArtTilePixels = 128;

    /// <summary>
    /// Edge of the floor mosaic, in tiles. The floor art is cut into this many pieces per side
    /// and painted by position, so its artwork runs continuously across cell borders and only
    /// repeats every <see cref="FloorMosaicSize"/> cells. Must stay in sync with the painter's
    /// own <c>floorMosaicSize</c>, which this setup writes.
    /// </summary>
    private const int FloorMosaicSize = 4;

    /// <summary>
    /// Edge of the wall mosaic, in tiles. Same idea as <see cref="FloorMosaicSize"/>, but the
    /// masonry is drawn straight into the block rather than cut out of a source image: each of
    /// the sixteen exposure masks gets this many tiles squared, and the painter picks between
    /// them by cell position. Must stay in sync with the painter's <c>wallMosaicSize</c>.
    ///
    /// The point is that one 32px tile only holds eight stones, and stamping the same eight
    /// down every cell is what made a wall run read as wallpaper — no amount of noise inside
    /// a single tile fixes that, because the noise repeats with it. Two doubles the period in
    /// both directions for four times the files; three would be better still and costs sixteen
    /// times nine tiles, which is more placeholder art than this is worth.
    /// </summary>
    private const int WallMosaicSize = 2;

    /// <summary>
    /// How many sheets each paper decal variant is built from. One entry per variant, so this
    /// sets both the number of variants and the mix — mostly lone dropped pages, with a couple
    /// of small scatters. Under a uniform pick by the painter, that is also how often each
    /// shows up.
    /// </summary>
    private static readonly int[] PaperSheetCounts = { 1, 1, 1, 2, 2, 3 };

    /// <summary>
    /// How much of a tile one sheet's longest side covers. Well under 1 so a page reads as
    /// something dropped on the floor with ground visible around it, and so several can share
    /// a tile without covering the cell.
    /// </summary>
    private const float PaperSheetFill = 0.42f;

    /// <summary>How much sheets vary in size, either side of <see cref="PaperSheetFill"/>.</summary>
    private const float PaperScaleJitter = 0.12f;

    /// <summary>How far a lone sheet drifts from the middle of its cell, as a fraction of it.</summary>
    private const float PaperLoneSpread = 0.06f;

    /// <summary>
    /// How far the sheets of a group spread from the middle of their cell. Enough that they
    /// read as separate pages rather than one blob, small enough that the group stays inside
    /// its own cell — a rotated sheet already reaches about 0.3 of a tile from its centre.
    /// </summary>
    private const float PaperGroupSpread = 0.17f;

    /// <summary>
    /// How much of a tile a nature decal's longest side covers. Larger than
    /// <see cref="PaperSheetFill"/> — these are meant to read as ground cover you notice, not
    /// litter you glance past.
    /// </summary>
    private const float NatureDecalFill = 0.8f;

    /// <summary>How much a nature decal's size jitters, either side of <see cref="NatureDecalFill"/>.</summary>
    private const float NatureScaleJitter = 0.1f;

    /// <summary>How far a nature decal drifts from the middle of its cell, as a fraction of it.</summary>
    private const float NatureSpread = 0.04f;

    /// <summary>Alpha at or above which a source pixel counts as solid when hunting for a crop.</summary>
    private const byte OpaqueAlpha = 250;

    /// <summary>
    /// Green minus blue at or above which a pixel counts as moss rather than stone. Measured
    /// against this art: stone sits at 8 and reaches 27 at the 99th percentile, moss runs 59
    /// to 65, so anything in the thirties separates them with room to spare on both sides.
    /// </summary>
    private const int MossGreenOverBlue = 30;

    /// <summary>
    /// How much a percent of moss in a candidate crop counts against it, in units of the edge
    /// tone mismatch it is traded off against. High because the two artefacts are not
    /// comparable in kind: an uneven edge is a soft step in brightness, while moss is a shape
    /// the eye recognises and then notices again in every block.
    /// </summary>
    private const float MossPenalty = 40f;

    [MenuItem("Tools/Dungeon/Setup Scene Tilemaps")]
    public static void Setup()
    {
        EnsureFolders();

        // The real floor art when it is present, the generated placeholders when it is not.
        // A mosaic is positional, so the painter has to be told which of the two it got.
        Tile[] floorMosaic = EnsureFloorMosaic();
        Tile[] floorTiles = floorMosaic ?? EnsureFloorTiles();
        int floorMosaicSize = floorMosaic != null ? FloorMosaicSize : 1;

        Tile wallTile = EnsureTile("WallTile", TileStyle.WallTop, Tile.ColliderType.Grid);
        Tile wallFaceTile = EnsureTile("WallFaceTile", TileStyle.WallFace, Tile.ColliderType.Grid);
        // Sprite rather than Grid, and that is the whole point of the round plan: the
        // collider follows the drawn column, so a colonnade casts one shadow per column
        // with light between them instead of one unbroken band. FieldOfView linecasts
        // against these colliders and PathfindingGrid overlap-tests a circle at the cell
        // centre, which the column still covers, so the cell stays unwalkable.
        //
        // Only free-standing columns get this tile — DungeonPainter gives the wall's
        // full-cell tile to any Pillar cell touching the structure, so doorway jambs do
        // not develop holes.
        Tile pillarTile = EnsureTile("PillarTile", TileStyle.Pillar, Tile.ColliderType.Sprite);
        AssignCircularPhysicsShape($"{TilesFolder}/PillarTile.png", PillarRadius);
        // Both of these used to be drawn here at 32 pixels in the cool placeholder palette.
        // They now come from InkedTileGenerator instead: 128 pixels, keyed to the hand-drawn
        // floor's own colour. The old assets are left on disk untouched, simply unreferenced.
        Tile rubbleTile = FirstOrNull(InkedTileGenerator.EnsureRubble());
        Tile[] exitFloorTiles = InkedTileGenerator.EnsureExitFloor();
        Tile[] natureDecals = EnsureNatureDecals();
        Tile[] inkedDecals = InkedTileGenerator.EnsureInkedDecals();
        Tile[] decalTiles = CombineDecals(EnsureDecalTiles(), EnsurePaperDecals(), natureDecals,
            inkedDecals);

        // Also guaranteed once in the hub, on top of being rare scatter everywhere else via
        // decalTiles above — see DungeonPainter.hubGuaranteedDecalTile. Found by name in the
        // already-composited array rather than composited a second time, so this stays the
        // same Tile reference decalTiles carries and never risks the two disagreeing.
        Tile sleepingBagTile = FindTileByName(natureDecals, "DecalSleepingBag");
        Tile[] wallAutotiles = EnsureWallAutotiles();
        // Before the registry entries below, which bind ids to prefabs that already exist:
        // the rat is generated rather than authored, so on a fresh checkout there is no
        // prefab at its path until this has run.
        RatSetup.EnsurePrefab();

        PrefabRegistry registry = EnsureRegistry();
        EnsureRegistryEntries(registry);
        DungeonGenerationSettings settings = EnsureSettings();
        RoomContentSettings contentSettings = EnsureContentSettings();

        Grid grid = EnsureGrid();
        Tilemap floor = EnsureFloorTilemap(grid);
        Tilemap decals = EnsureDecalTilemap(grid);
        Tilemap walls = EnsureWallTilemap(grid);

        WarnOnCellSizeMismatch(grid);
        WireGenerator(grid, floor, decals, walls, floorTiles, floorMosaicSize, wallTile,
            wallFaceTile, pillarTile, rubbleTile, exitFloorTiles, decalTiles, sleepingBagTile,
            wallAutotiles, settings, contentSettings, registry);

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
        Tile[] floorTiles, int floorMosaicSize, Tile wallTile, Tile wallFaceTile,
        Tile pillarTile, Tile rubbleTile, Tile[] exitFloorTiles,
        Tile[] decalTiles, Tile hubGuaranteedDecalTile, Tile[] wallAutotiles,
        DungeonGenerationSettings settings,
        RoomContentSettings contentSettings, PrefabRegistry registry)
    {
        var root = grid.gameObject;

        var painter = EditorSetupUtility.EnsureComponent<DungeonPainter>(root);
        SetRef(painter, "floorTilemap", floor);
        SetRef(painter, "decalTilemap", decals);
        SetRef(painter, "wallTilemap", walls);
        SetArray(painter, "decalTiles", decalTiles);
        SetRef(painter, "hubGuaranteedDecalTile", hubGuaranteedDecalTile);
        SetArray(painter, "wallAutotiles", wallAutotiles);
        SetInt(painter, "wallMosaicSize", WallMosaicSize);
        SetArray(painter, "floorTiles", floorTiles);
        SetInt(painter, "floorMosaicSize", floorMosaicSize);
        SetRef(painter, "wallTile", wallTile);
        SetRef(painter, "wallFaceTile", wallFaceTile);
        SetRef(painter, "pillarTile", pillarTile);
        SetRef(painter, "rubbleTile", rubbleTile);
        SetArray(painter, "exitFloorTiles", exitFloorTiles);

        var builder = EditorSetupUtility.EnsureComponent<DungeonBuilder>(root);
        SetRef(builder, "settings", settings);
        SetRef(builder, "painter", painter);
        SetRef(builder, "pathfindingGrid", Object.FindFirstObjectByType<PathfindingGrid>());
        WirePlayerSpawnMarker(builder);

        var populator = EditorSetupUtility.EnsureComponent<DungeonPopulator>(root);
        SetRef(populator, "builder", builder);
        SetRef(populator, "content", contentSettings);
        SetRef(populator, "registry", registry);
    }

    /// <summary>
    /// Points <see cref="DungeonBuilder"/>'s spawn marker at whatever Transform
    /// <see cref="GameManager"/> in this scene teleports the player to at game start.
    ///
    /// This is what makes the fix self-installing rather than a manual inspector step
    /// someone has to remember: without it, this scene's own <c>GameManager</c> and
    /// <c>DungeonBuilder</c> would each keep working correctly in isolation, and the
    /// player would still end up in the wrong place, because nothing told the builder
    /// which Transform is the one <c>GameManager</c> actually reads at startup.
    /// </summary>
    private static void WirePlayerSpawnMarker(DungeonBuilder builder)
    {
        var gameManager = Object.FindFirstObjectByType<GameManager>();
        if (gameManager == null) return;

        var serialized = new SerializedObject(gameManager);
        SerializedProperty property = serialized.FindProperty("playerSpawnPoint");
        if (property == null || property.objectReferenceValue == null) return;

        SetRef(builder, "playerSpawnMarker", property.objectReferenceValue);
    }

    /// <summary>
    /// The generator maps grid cells 1:1 onto pathfinding cells, so a Grid cell size
    /// other than the PathfindingGrid's would offset every spawn from its tile.
    ///
    /// Compared in *world* units — <c>grid.cellSize</c> alone is the Grid component's own
    /// local-space value and excludes the GameObject's transform scale, so a scaled
    /// DungeonRoot (this project has one at 2×) would otherwise make this warning fire
    /// on a scene that is actually fine. <see cref="PathfindingGrid.Configure"/> now reads
    /// this same world-space size from <see cref="DungeonPainter.CellSize"/> on every
    /// build and corrects itself regardless, so this check is only ever a heads-up before
    /// the first Generate — not something a mismatch here can leave broken.
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

        Vector3 scale = grid.transform.lossyScale;
        float worldCellSizeX = grid.cellSize.x * scale.x;
        float worldCellSizeY = grid.cellSize.y * scale.y;

        if (!Mathf.Approximately(worldCellSizeX, pathfinding.CellSize) ||
            !Mathf.Approximately(worldCellSizeY, pathfinding.CellSize))
        {
            Debug.LogWarning(
                $"[DungeonSetup] Tilemap cell size {worldCellSizeX}x{worldCellSizeY} world units " +
                $"differs from PathfindingGrid.cellSize {pathfinding.CellSize}. Generate will " +
                "correct this on the next build; flagged here only so it is not a surprise before then.",
                grid);
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
        /// Free-standing pillar: the wall's masonry cut to a round plan and edged like an
        /// exposed wall side, so it reads as a column of the same stone standing in the
        /// room. Transparent outside the column, so the floor shows through around it.
        /// </summary>
        Pillar,

        /// <summary>
        /// Floor of the exit room: cut flagstones, lighter than the rough ground
        /// elsewhere, so the room the run ends in does not look like every other room.
        /// </summary>
        ExitFloor,

        /// <summary>Collapsed masonry: scattered lighter chunks over a dark bed.</summary>
        Rubble,

        /// <summary>
        /// Doorway threshold: a dark slab framed by two lit jamb blocks.
        ///
        /// No longer painted — <see cref="DungeonPainter"/> runs the ordinary floor through
        /// doorways now, so the stone stays continuous under the door. Kept as a style
        /// because the drawing routine is the record of what the threshold looked like, and
        /// it costs nothing sitting here; delete it if the placeholder floor goes too.
        /// </summary>
        Doorway,

        /// <summary>Decal: a fracture running across the tile, with the odd branch off it.</summary>
        Crack,

        /// <summary>Decal: an irregular dark patch, as of damp or old spillage.</summary>
        Stain,

        /// <summary>Decal: loose chippings, lighter than the floor they lie on.</summary>
        Grit,

        /// <summary>Decal: a clump of pale mushrooms grown out of the joints in the floor.</summary>
        Mushrooms,

        /// <summary>Decal: a few scattered bones, picked clean.</summary>
        Bones,

        /// <summary>Decal: a patch of damp moss, the one green thing in the dungeon.</summary>
        Moss,

        /// <summary>Decal: standing water, dark, with the light catching one edge of it.</summary>
        Puddle,

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
    /// Which cell of the mosaic block the wall tile currently being drawn occupies, and how
    /// wide the block is in pixels. Together these turn the tile's local pixel coordinates
    /// into block coordinates, which is what lets the stonework run on across cell borders:
    /// the courses, the stone tones and the noise are all keyed on the block position, so
    /// neighbouring tiles of the same block continue each other instead of restarting.
    ///
    /// A block of one — the plain <see cref="TileStyle.WallTop"/> fallback tile — is the same
    /// drawing with the block reduced to a single cell, so it still works on its own.
    /// </summary>
    private static int _wallCellX, _wallCellY;
    private static int _wallBlockPixels = TilePixels;

    /// <summary>
    /// Decals are drawn as a whole rather than pixel by pixel, because a crack is a path
    /// across the tile and a path cannot be decided from one pixel's coordinates alone.
    /// </summary>
    private static bool IsDecal(TileStyle style)
    {
        return style == TileStyle.Crack || style == TileStyle.Stain || style == TileStyle.Grit ||
               style == TileStyle.Mushrooms || style == TileStyle.Bones ||
               style == TileStyle.Moss || style == TileStyle.Puddle;
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
                var pixels = new Color[TilePixels * TilePixels];
                DrawDecal(pixels, style, random);
                texture.SetPixels(pixels);
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

                // The rim's depth wanders by a pixel along its length, so the outline reads
                // as stone that has been knocked about rather than as a drawn border. Keyed
                // on the block position like the stonework, so the tiles of one block do not
                // all wander in the same places.
                int rimX = _wallCellX * TilePixels + x;
                int rimY = _wallCellY * TilePixels + y;

                bool onRim =
                    (north && y >= TilePixels - RimDepth(rimX, 0)) ||
                    (east && x >= TilePixels - RimDepth(rimY, 1)) ||
                    (west && x < RimDepth(rimY, 2)) ||
                    (south && y < RimDepth(rimX, 3));

                if (onRim) return Jitter(WallJointColor, 0.02f, random);

                goto case TileStyle.WallTop;
            }

            case TileStyle.WallTop:
            {
                // Horizontal courses with staggered vertical joints, so the wall reads as
                // masonry instead of a single slab.
                //
                // Flat colour on a ruled grid was the problem: a wall run read as graph
                // paper. Four things break that up, and every one of them is keyed on the
                // pixel's position rather than drawn from the stream, so the pattern lines
                // up across neighbouring cells instead of restarting at each tile edge —
                // the joints wander a pixel, each stone carries its own tone, each stone
                // is bevelled top and bottom, and a slow mottle with fine pitting over it
                // runs across the whole face.
                const int courseHeight = TilePixels / 4;
                const int brickWidth = TilePixels / 2;

                // Everything below works in block coordinates, not tile ones. There are eight
                // stones in a 32px tile; a block of N by N tiles holds N*N times as many, and
                // that is the only thing that actually lengthens the period of the wall.
                int blockX = _wallCellX * TilePixels + x;
                int blockY = _wallCellY * TilePixels + y;
                int block = _wallBlockPixels;

                int course = blockY / courseHeight;
                int column = blockX + (course % 2) * (brickWidth / 2);

                // Nearest joint rather than a modulo test, because the wobble has to be
                // able to push a joint off where the grid would have put it.
                int jointY = Mathf.RoundToInt(blockY / (float)courseHeight) * courseHeight;
                int jointX = Mathf.RoundToInt(column / (float)brickWidth) * brickWidth;

                bool jointRow = blockY == jointY + Wobble(blockX, jointY, 0x9E3779B9u, block);
                bool jointColumn = column == jointX + Wobble(course, jointX, 0x85EBCA6Bu, block);
                if (jointRow || jointColumn) return Jitter(WallJointColor, 0.03f, random);

                // Wrapped to the block, so the half-stone at one edge and its other half at
                // the far edge are the same stone and come out the same tone.
                int stoneX = (column / brickWidth) % (block / brickWidth);
                int stoneY = course % (block / courseHeight);

                // A corner knocked off a quarter of the stones. Cheap, and it does more for
                // the wall than any amount of grain: it gives the eye irregular silhouettes
                // to read the courses by instead of a run of identical rectangles.
                uint stoneHash = DeterministicRandom.Hash(MasonrySeed ^ 0x7A1CE5u, stoneX, stoneY);
                if ((stoneHash & 3) == 0)
                {
                    int localX = column - (column / brickWidth) * brickWidth;
                    int localY = blockY - course * courseHeight;
                    int corner = (int)((stoneHash >> 2) & 3);
                    int chip = 2 + (int)((stoneHash >> 4) & 1);

                    int fromX = (corner & 1) == 0 ? localX : brickWidth - 1 - localX;
                    int fromY = (corner & 2) == 0 ? localY : courseHeight - 1 - localY;
                    if (fromX + fromY < chip) return Jitter(WallJointColor, 0.03f, random);
                }

                // Tone spread by the golden ratio over the eight stones in a tile rather
                // than drawn at random. There are only eight, and a random draw kept
                // handing a whole course two dark stones, which — with the same texture
                // stamped down every cell — came out as a black band running the length of
                // the wall every four courses. Stepping by the golden ratio guarantees the
                // eight tones are spread, so no course is uniformly dark.
                int stone = stoneY * (block / brickWidth) + stoneX;
                float tone = stone * 0.6180339887f;
                tone -= Mathf.Floor(tone);
                Color color = Shade(WallTopColor, (tone - 0.5f) * 0.13f);

                // One lit pixel at the top of each stone and one shadowed at its foot. That
                // is all the relief 32 pixels will carry, and it is what stops the courses
                // reading as printed lines.
                int intoCourse = blockY - course * courseHeight;
                if (intoCourse == courseHeight - 1) color = Shade(color, 0.045f);
                else if (intoCourse == 1) color = Shade(color, -0.04f);

                // Damp and soot across the stone, then pitting picked out of a finer octave.
                color = Shade(color, Fbm(blockX, blockY, MasonrySeed ^ 0x27D4EB2Fu, block) * 0.14f);
                // A quarter of the block as the wrap rather than a scaled coordinate: same
                // effect — a finer field — but it still repeats on the block boundary.
                if (Fbm(blockX, blockY, MasonrySeed ^ 0xC2B2AE35u, block / 4) > 0.18f)
                    color = Shade(color, -0.05f);

                // Less stream jitter than before: the position-keyed detail above is now
                // carrying the texture, and grain on top of it only muddies the bevels.
                return Jitter(color, 0.012f, random);
            }

            case TileStyle.WallFace:
            {
                // The top rows catch the light; everything below falls away into shadow.
                const int capHeight = 6;
                if (y >= TilePixels - capHeight) return Jitter(WallCapColor, 0.02f, random);

                float depth = 1f - y / (float)(TilePixels - capHeight);
                Color color = Color.Lerp(WallFaceColor, WallFaceShadowColor, depth * 0.6f);

                // Same mottle as the wall top, so the two surfaces look like the same stone.
                color = Shade(color, Fbm(x, y, MasonrySeed ^ 0x165667B1u) * 0.08f);
                return Jitter(color, 0.02f, random);
            }

            case TileStyle.Pillar:
            {
                // The wall's own masonry, cut to a round plan. A pillar is a piece of the
                // same structure standing free in the room, so it has to be built out of
                // the same stone: the disc this replaces was two shades lighter than any
                // wall, smooth where the wall is coursed, and shaded from the top in a
                // game drawn from directly above — three separate reasons for it to read
                // as a different material sitting next to the masonry rather than as part
                // of it. Doorway jambs are pillars too (DoorwayNormalizer), which put the
                // mismatch in the one place the player looks at head-on.
                //
                // Everything outside the column is left transparent rather than filled
                // with FloorColor. The painter floors pillar cells like any other open
                // cell, so what shows through is the real floor — including the hand-drawn
                // art, which the old placeholder grey was painting over.
                float dx = x - (TilePixels - 1) * 0.5f;
                float dy = y - (TilePixels - 1) * 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);

                // The silhouette wanders a pixel, the way the wall's rim does along its
                // length, so the column reads as knocked-about stone and not as a compass
                // circle. Keyed on position rather than drawn from the stream, so it stays
                // put between runs like the rest of the stonework.
                float radius = PillarRadius +
                    (Fbm(x, y, MasonrySeed ^ 0x51E7A2C3u) > 0.05f ? 0.5f : -0.5f);

                if (distance > radius) return Nothing;

                // A free-standing column is exposed on all four sides, so it gets the drawn
                // edge an exposed wall side gets — same colour, same two-to-three pixels.
                // Measured around the circumference rather than across the tile, so the
                // depth varies along the edge instead of mirroring across the column.
                // One pixel shallower than the wall's, because the column is a quarter of a
                // wall run's width: at the wall's full depth the rim eats most of the disc and
                // the masonry inside it never shows.
                int along = Mathf.RoundToInt(Mathf.Atan2(dy, dx) * radius);
                if (distance > radius - (RimDepth(along, 0) - 1))
                    return Jitter(WallJointColor, 0.02f, random);

                goto case TileStyle.WallTop;
            }

            case TileStyle.ExitFloor:
            {
                // No longer painted — InkedTileGenerator draws the exit floor now, at 128
                // pixels and in the hand-drawn floor's own colour. Kept for the same reason
                // TileStyle.Doorway is: the routine is the record of what the placeholder
                // looked like, and it costs nothing sitting here.
                // Cut flagstones instead of the rough ground everywhere else. The exit is
                // the room the whole run is looking for, and worked stone underfoot is what
                // says it has been found — a change of surface the player reads before they
                // have seen anything else in the room.
                //
                // Lighter than the ordinary floor rather than darker, for the reason the
                // palette above gives: the floor is the lightest thing on screen, and the
                // exit has to sit at the top of that range to register at all inside the
                // vision cone.
                const int slab = TilePixels / 2;

                // Nearest joint rather than a modulo test, so the wobble can push a joint
                // off where the grid would have put it — the wall's trick, and the joints
                // still line up across cell borders because the noise wraps on the tile.
                int slabX = Mathf.RoundToInt(x / (float)slab) * slab;
                int slabY = Mathf.RoundToInt(y / (float)slab) * slab;

                bool joint =
                    x == slabX + Wobble(y, slabX, 0xB5297A4Du, TilePixels) ||
                    y == slabY + Wobble(x, slabY, 0x68E31DA4u, TilePixels);
                if (joint) return Jitter(ExitFloorJointColor, 0.03f, random);

                // One tone per slab, spread by the golden ratio for the same reason the
                // wall's stones are: there are only four of them per tile, and a random
                // draw kept handing two neighbours the same shade.
                int stoneIndex = (y / slab) * (TilePixels / slab) + (x / slab);
                float slabTone = stoneIndex * 0.6180339887f;
                slabTone -= Mathf.Floor(slabTone);
                Color slabColor = Shade(ExitFloorColor, (slabTone - 0.5f) * 0.07f);

                // The same mottle the wall carries, so the two surfaces are recognisably
                // the same stone worked to different ends.
                slabColor = Shade(slabColor, Fbm(x, y, MasonrySeed ^ 0x1B873593u) * 0.10f);
                return Jitter(slabColor, 0.02f, random);
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
                // No longer painted either, and for the same reason: this filled the cell
                // with speckle on a four-pixel lattice in a palette two shades lighter and
                // colder than the floor, so a collapse read as a rectangle of static.
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
    /// <summary>Writes one pixel of a decal buffer, which is laid out row-major from the bottom.</summary>
    private static void SetPixel(Color[] pixels, int x, int y, Color color)
    {
        pixels[y * TilePixels + x] = color;
    }

    private static void DrawDecal(Color[] pixels, TileStyle style, DeterministicRandom random)
    {
        for (int y = 0; y < TilePixels; y++)
        {
            for (int x = 0; x < TilePixels; x++)
                SetPixel(pixels, x, y, Nothing);
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

                    PlotCrack(pixels, horizontal, i, across, random);

                    // A short spur every so often, so the fracture forks instead of
                    // running the whole width as one unbroken stroke.
                    if (!random.Chance(0.07f)) continue;

                    int spur = random.RangeInclusive(2, 5);
                    int direction = random.Chance(0.5f) ? 1 : -1;
                    for (int s = 1; s <= spur; s++)
                        PlotCrack(pixels, horizontal, i + s * direction, across + s * direction, random);
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

                            Color existing = pixels[(y) * TilePixels + (x)];
                            if (alpha <= existing.a) continue;

                            SetPixel(pixels, x, y, new Color(StainColor.r, StainColor.g, StainColor.b, alpha));
                        }
                    }
                }
                return;
            }

            case TileStyle.Mushrooms:
            {
                // A clump rather than a scatter: mushrooms come up together out of one patch
                // of damp, and three drawn in a group read as growth where three spread
                // evenly over the tile read as three unrelated dots.
                float clumpX = random.RangeInclusive(11, TilePixels - 12);
                float clumpY = random.RangeInclusive(11, TilePixels - 12);
                int caps = random.RangeInclusive(3, 6);

                for (int c = 0; c < caps; c++)
                {
                    int capX = Mathf.RoundToInt(clumpX + (random.NextFloat() * 2f - 1f) * 6f);
                    int capY = Mathf.RoundToInt(clumpY + (random.NextFloat() * 2f - 1f) * 6f);
                    DrawMushroom(pixels, capX, capY, random.RangeInclusive(2, 4), random);
                }
                return;
            }

            case TileStyle.Bones:
            {
                // Long bones only, no skull. A skull at 32 pixels is four pixels of eye
                // socket and reads as a smudge, while a shaft with knuckles at both ends
                // reads as a bone at any size — which is the whole job of a decal.
                int bones = random.RangeInclusive(3, 5);
                for (int b = 0; b < bones; b++)
                {
                    float angle = random.NextFloat() * Mathf.PI;
                    float length = random.RangeInclusive(10, 15);
                    float cx = random.RangeInclusive(9, TilePixels - 10);
                    float cy = random.RangeInclusive(9, TilePixels - 10);

                    DrawBone(pixels, cx, cy, angle, length, random);
                }
                return;
            }

            case TileStyle.Moss:
            {
                // Grown out from one point rather than laid down as a shape: every pixel is
                // kept on a falling probability with distance, so the patch frays at its
                // edge the way something spreading does instead of ending on a line.
                float centreX = random.RangeInclusive(10, TilePixels - 11);
                float centreY = random.RangeInclusive(10, TilePixels - 11);
                float reach = random.RangeInclusive(11, 15);

                for (int y = 0; y < TilePixels; y++)
                {
                    for (int x = 0; x < TilePixels; x++)
                    {
                        float dx = x - centreX;
                        float dy = y - centreY;
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        if (distance > reach) continue;

                        float density = Mathf.InverseLerp(reach, 0f, distance);
                        if (!random.Chance(Mathf.Pow(density, 0.45f) * 0.95f)) continue;

                        Color color = Jitter(random.Chance(0.3f) ? MossLightColor : MossColor, 0.05f, random);
                        color.a = Mathf.Clamp01(0.6f + density * 0.4f);
                        SetPixel(pixels, x, y, color);
                    }
                }
                return;
            }

            case TileStyle.Puddle:
            {
                // One body of water, not a union of blobs like the stain: a puddle has a
                // surface, and the thing that says so is the sheen along one edge of it.
                // The lit edge is fixed, not random, so every puddle in a room agrees about
                // where the light is.
                float centreX = random.RangeInclusive(13, TilePixels - 14);
                float centreY = random.RangeInclusive(13, TilePixels - 14);
                float radiusX = random.RangeInclusive(8, 12);
                float radiusY = random.RangeInclusive(6, 10);

                for (int y = 0; y < TilePixels; y++)
                {
                    for (int x = 0; x < TilePixels; x++)
                    {
                        float nx = (x - centreX) / radiusX;
                        float ny = (y - centreY) / radiusY;

                        // The outline wanders, so the puddle is not an ellipse anybody can
                        // name. Keyed on position rather than drawn from the stream, so the
                        // edge stays put instead of dissolving into per-pixel noise.
                        float edge = Mathf.Sqrt(nx * nx + ny * ny) - Fbm(x, y, MasonrySeed ^ 0x2545F491u) * 0.35f;
                        if (edge > 1f) continue;

                        // The sheen: a band just inside the upper edge, where a wet surface
                        // returns the light instead of swallowing it.
                        bool sheen = edge > 0.62f && ny > 0.1f;

                        Color color = Jitter(sheen ? PuddleSheenColor : PuddleColor, 0.03f, random);
                        color.a = sheen
                            ? 0.85f
                            : Mathf.Clamp01(0.55f + Mathf.InverseLerp(1f, 0f, edge) * 0.4f);
                        SetPixel(pixels, x, y, color);
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
                            SetPixel(pixels, x + dx, y + dy, color);
                    }
                }
                return;
            }
        }
    }

    /// <summary>
    /// One mushroom: a cap with a lit crown and a shaded underside, over a short stem.
    /// Drawn as a half-disc rather than a full one, because from above what is visible of a
    /// mushroom is the cap and a sliver of stem below it, and a full disc reads as a pebble.
    /// </summary>
    private static void DrawMushroom(Color[] pixels, int capX, int capY, int radius,
        DeterministicRandom random)
    {
        for (int y = capY - radius; y <= capY + radius; y++)
        {
            for (int x = capX - radius; x <= capX + radius; x++)
            {
                if (x < 0 || y < 0 || x >= TilePixels || y >= TilePixels) continue;

                float dx = x - capX;
                float dy = y - capY;
                if (dx * dx + dy * dy > radius * radius) continue;

                // The lower third of the cap is its own shadow, which is what gives the
                // shape any depth at all at four pixels across.
                bool underside = dy < -radius * 0.35f;
                Color color = Jitter(underside ? MushroomShadeColor : MushroomCapColor, 0.04f, random);
                color.a = 1f;
                SetPixel(pixels, x, y, color);
            }
        }

        // The stem, showing below the cap.
        for (int y = capY - radius - 2; y < capY - radius; y++)
        {
            if (y < 0 || y >= TilePixels || capX < 0 || capX >= TilePixels) continue;

            Color stem = Jitter(MushroomStemColor, 0.04f, random);
            stem.a = 1f;
            SetPixel(pixels, capX, y, stem);
        }
    }

    /// <summary>
    /// One long bone: a shaft walked from end to end with a knuckle at each end. Walked
    /// rather than rasterised from an equation, because a bone is two pixels wide and any
    /// anti-aliasing at that size turns it into a grey smear.
    /// </summary>
    private static void DrawBone(Color[] pixels, float cx, float cy, float angle,
        float length, DeterministicRandom random)
    {
        float dx = Mathf.Cos(angle);
        float dy = Mathf.Sin(angle);

        for (float t = -length * 0.5f; t <= length * 0.5f; t += 0.5f)
        {
            int x = Mathf.RoundToInt(cx + dx * t);
            int y = Mathf.RoundToInt(cy + dy * t);

            // A knuckle at each end, one pixel wider than the shaft.
            bool knuckle = Mathf.Abs(t) > length * 0.5f - 1.5f;
            int half = knuckle ? 1 : 0;

            for (int oy = -half; oy <= half; oy++)
            {
                for (int ox = -half; ox <= half; ox++)
                {
                    int px = x + ox;
                    int py = y + oy;
                    if (px < 0 || py < 0 || px >= TilePixels || py >= TilePixels) continue;

                    // The far side of the shaft is shaded, so two bones crossing still read
                    // as two bones rather than as one forked one.
                    bool shaded = oy < 0 && knuckle;
                    Color color = Jitter(shaded ? BoneShadeColor : BoneColor, 0.04f, random);
                    color.a = 1f;
                    SetPixel(pixels, px, py, color);
                }
            }
        }
    }

    /// <summary>Plots one cell of a crack, given the run/across coordinates and its axis.</summary>
    private static void PlotCrack(Color[] pixels, bool horizontal, int along, int across,
        DeterministicRandom random)
    {
        if (along < 0 || along >= TilePixels || across < 0 || across >= TilePixels) return;

        int x = horizontal ? along : across;
        int y = horizontal ? across : along;

        Color color = Jitter(CrackColor, 0.03f, random);
        color.a = 0.65f + random.NextFloat() * 0.3f;
        SetPixel(pixels, x, y, color);
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

    /// <summary>How deep the drawn edge of a wall runs at one point along it: 2px or 3px.</summary>
    private static int RimDepth(int along, int side)
    {
        const int rim = 3;
        // No scaling of the coordinate: multiplying it would slide the noise's own wrap
        // off the block's, and the field has to repeat exactly where the block does.
        return Fbm(along, side * 9f, MasonrySeed ^ 0x3C6EF372u, _wallBlockPixels) > 0.06f
            ? rim - 1
            : rim;
    }

    /// <summary>
    /// Whether a joint line steps a pixel off the grid at this point along it. Binary
    /// rather than a continuous offset: at 32 pixels a joint is one pixel wide, so a
    /// fractional displacement would only blur it.
    /// </summary>
    private static int Wobble(int along, int joint, uint salt, int wrapPixels)
    {
        return Fbm(along, joint * 3.1f, salt, wrapPixels) > 0.05f ? 1 : 0;
    }

    /// <summary>
    /// Three octaves of value noise, returning roughly <c>[-0.5, 0.5]</c> centred on zero.
    ///
    /// Every octave's lattice wraps over <paramref name="wrapPixels"/>, so the result is
    /// continuous where one tile meets the next. That is the whole reason for hand-rolling
    /// this rather than reaching for <see cref="Mathf.PerlinNoise"/>: the same block of
    /// textures is stamped across every cell of a wall, and noise that does not wrap puts a
    /// visible seam on every cell boundary.
    /// </summary>
    private static float Fbm(float x, float y, uint salt, int wrapPixels = TilePixels)
    {
        float sum = 0f;
        float total = 0f;
        float amplitude = 1f;
        int period = 4;

        for (int octave = 0; octave < 3; octave++)
        {
            float frequency = period / (float)wrapPixels;
            sum += amplitude * (ValueNoise(x * frequency, y * frequency, period,
                salt + (uint)octave * 0x9E3779B9u) - 0.5f);
            total += amplitude;
            amplitude *= 0.5f;
            period *= 2;
        }

        return sum / total;
    }

    /// <summary>Bilinear value noise on a lattice that repeats every <paramref name="period"/> cells.</summary>
    private static float ValueNoise(float x, float y, int period, uint salt)
    {
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float fx = x - x0;
        float fy = y - y0;

        // Smoothstep the weights; linear interpolation alone leaves the lattice visible
        // as a grid of creases.
        fx = fx * fx * (3f - 2f * fx);
        fy = fy * fy * (3f - 2f * fy);

        float bottom = Mathf.Lerp(Lattice(x0, y0, period, salt), Lattice(x0 + 1, y0, period, salt), fx);
        float top = Mathf.Lerp(Lattice(x0, y0 + 1, period, salt), Lattice(x0 + 1, y0 + 1, period, salt), fx);
        return Mathf.Lerp(bottom, top, fy);
    }

    /// <summary>One lattice corner's value in <c>[0, 1)</c>, wrapped so the field is periodic.</summary>
    private static float Lattice(int x, int y, int period, uint salt)
    {
        int wrappedX = ((x % period) + period) % period;
        int wrappedY = ((y % period) + period) % period;
        return (DeterministicRandom.Hash(salt, wrappedX, wrappedY) >> 8) / (float)(1 << 24);
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

    /// <summary>
    /// PPU = the texture's own tile size, so one tile covers exactly one world unit whatever
    /// resolution it was authored at — which is what lets the higher-resolution tiles cut from
    /// hand-drawn art sit on the same tilemap as the 32px placeholders without either being
    /// scaled to suit the other.
    ///
    /// Filtering follows the same split. Point keeps the placeholders' deliberate pixel grain
    /// crisp, but on downsampled photographic art it aliases the stonework into shimmering
    /// speckle as the camera moves, so those get bilinear.
    /// </summary>
    private static void ConfigureTextureImporter(string path, int pixelsPerUnit = TilePixels)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixelsPerUnit;
        importer.filterMode = pixelsPerUnit > TilePixels ? FilterMode.Bilinear : FilterMode.Point;

        // Decals are mostly transparent; without this Unity premultiplies them and the
        // soft edges of a stain come out ringed in black.
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }

    /// <summary>
    /// Gives a sprite an explicit physics shape: a regular polygon of
    /// <see cref="PillarColliderSegments"/> sides approximating a circle of
    /// <paramref name="radiusPixels"/> around the middle of the sprite.
    ///
    /// This is what actually makes <see cref="Tile.ColliderType.Sprite"/> mean "the shape that
    /// was drawn". A sprite with no authored physics shape falls back to one Unity generates
    /// itself, and that fallback is a box the size of the whole sprite — so the round pillar was
    /// colliding, and cutting the player's field of view, as a full square cell, casting a
    /// shadow with corners the drawn column plainly does not have.
    ///
    /// Written through the sprite data provider API rather than the importer, because a physics
    /// shape is sprite data (the Sprite Editor's Custom Physics Shape) and not an import setting.
    /// </summary>
    /// <param name="texturePath">Asset path of the sprite's texture.</param>
    /// <param name="radiusPixels">Radius of the collider, in the texture's own pixels.</param>
    private static void AssignCircularPhysicsShape(string texturePath, float radiusPixels)
    {
        var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
        if (importer == null)
        {
            Debug.LogWarning($"[DungeonSetup] No texture at '{texturePath}' — its collider shape " +
                             "was left as Unity's generated fallback.");
            return;
        }

        var factories = new SpriteDataProviderFactories();
        factories.Init();

        ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
        if (provider == null) return;

        provider.InitSpriteEditorDataProvider();
        var physicsOutlines = provider.GetDataProvider<ISpritePhysicsOutlineDataProvider>();
        if (physicsOutlines == null) return;

        // Outline vertices are in pixels measured from the middle of the sprite's rect, which for
        // a whole-texture tile sprite is the middle of the texture — where the column is drawn.
        var circle = new Vector2[PillarColliderSegments];
        for (int i = 0; i < PillarColliderSegments; i++)
        {
            float angle = i / (float)PillarColliderSegments * Mathf.PI * 2f;
            circle[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radiusPixels;
        }

        foreach (SpriteRect spriteRect in provider.GetSpriteRects())
            physicsOutlines.SetOutlines(spriteRect.spriteID, new List<Vector2[]> { circle });

        provider.Apply();
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
            ("prop.statue01", "Assets/Prefabs/Statue01.prefab"),
            ("prop.statue02", "Assets/Prefabs/Statue02.prefab"),
            ("critter.rat", "Assets/Prefabs/Rat.prefab"),
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
        AddLoot(settings.loot, "Assets/Items/Item 13 - Rags.asset", 1f, 1, 4);
        AddLoot(settings.loot, "Assets/Items/Item 15 - Gunpowder.asset", 0.9f, 1, 3);
        AddLoot(settings.loot, "Assets/Items/Item 14 - Alcohol.asset", 0.7f, 1, 2);
        AddLoot(settings.loot, "Assets/Items/Item 8 - Scrap.asset", 1f, 1, 4);
        AddLoot(settings.loot, "Assets/Items/Item 12 - Shell.asset", 0.9f, 1, 3);

        // Survival resources, not valuables. The treasure room is optional — it is off the
        // key-to-exit path — so the only thing that can justify the detour is loot that
        // changes the player's odds: saves, and something to shoot with. It used to hold a
        // pistol, a sword and a pile of coins, and coins buy nothing because the game has
        // no vendor of any kind; the room was a detour that paid in dead weight.
        AddLoot(settings.treasureLoot, "Assets/Items/Item 6 - Ink.asset", 1.5f, 1, 1);
        AddLoot(settings.treasureLoot, "Assets/Items/Item 7 - Bullet.asset", 1f, 6, 12);
        AddLoot(settings.treasureLoot, "Assets/Items/Item 12 - Shell.asset", 0.8f, 4, 8);

        // Chests are the only source of loot now that nothing is scattered on the floor,
        // and this table was never filled in by this tool at all — a scene built from
        // scratch got chests that were empty by construction. Ink is weighted high on
        // purpose: it used to be guaranteed as floor drops, and with those gone the chests
        // are the only thing standing between a run and being unsaveable.
        AddLoot(settings.chestLoot, "Assets/Items/Item 7 - Bullet.asset", 1f, 3, 8);
        AddLoot(settings.chestLoot, "Assets/Items/Item 3 - Wood.asset", 1f, 2, 5);
        AddLoot(settings.chestLoot, "Assets/Items/Item 8 - Scrap.asset", 0.8f, 1, 3);
        AddLoot(settings.chestLoot, "Assets/Items/Item 13 - Rags.asset", 0.6f, 2, 4);
        AddLoot(settings.chestLoot, "Assets/Items/Item 14 - Alcohol.asset", 0.5f, 1, 2);
        AddLoot(settings.chestLoot, "Assets/Items/Item 15 - Gunpowder.asset", 0.7f, 1, 4);
        AddLoot(settings.chestLoot, "Assets/Items/Item 6 - Ink.asset", 1.4f, 1, 2);
        AddLoot(settings.chestLoot, "Assets/Items/Item 12 - Shell.asset", 0.8f, 2, 5);

        AddLoot(settings.treasureChestLoot, "Assets/Items/Item 6 - Ink.asset", 1.6f, 1, 1);
        AddLoot(settings.treasureChestLoot, "Assets/Items/Item 7 - Bullet.asset", 1f, 8, 16);
        AddLoot(settings.treasureChestLoot, "Assets/Items/Item 12 - Shell.asset", 0.9f, 6, 10);
        AddLoot(settings.treasureChestLoot, "Assets/Items/Item 16 - Bandage.asset", 0.8f, 1, 2);

        // Nothing is scattered on the floor. Everything the player finds is in a chest, so
        // looting is opening something rather than walking over things — and thirty rooms
        // of loose pickups read as litter, not as reward.
        //
        // The tables above are left populated on purpose: they are the content, and the
        // counts are the switch. Turning floor loot back on is raising these, not
        // re-authoring what would drop.
        settings.minLootPerRoom = 0;
        settings.maxLootPerRoom = 0;
        settings.treasureLootCount = 0;

        // Ink is what saving costs, so a run with none in it cannot be saved at all. The
        // guaranteed drops were the floor under that, and they were floor loot, so they
        // are gone with the rest; chests carry the ink now — see its weight in chestLoot,
        // raised to compensate. Set this back above zero if runs turn out short of saves.
        var ink = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Items/Item 6 - Ink.asset");
        if (ink != null) settings.guaranteedItemId = ink.Id;
        settings.guaranteedItemDrops = 0;

        // The one key the run cannot be finished without. It is deliberately NOT the
        // craftable Door Key: that one opens ordinary locked doors and costs three scrap,
        // so pointing the exit at it would let the player craft their way out and skip the
        // key chest entirely. The golden key has no recipe and exactly one instance per
        // dungeon.
        var goldenKey = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Items/Item 17 - Golden Key.asset");
        if (goldenKey != null) settings.exitKeyItemId = goldenKey.Id;

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
    /// The wall variants: one per combination of exposed sides, times one per cell of the
    /// mosaic block, laid out mask-major so the painter can index it as
    /// <c>mask * size * size + row * size + column</c>.
    ///
    /// Generated rather than authored because the alternative is a hand-cut wall sheet,
    /// and that has been the blocker on making outlines readable since Stage 5. These are
    /// placeholders in the same sense as the rest: enough for the geometry to be legible
    /// and to be judged, not a substitute for real art. When the real sheet arrives it can
    /// either fill these same slots or be swapped for a Rule Tile — the painter only asks
    /// for a tile per mask and position and does not care which.
    ///
    /// The block's first cell keeps the bare <c>WallTile_NN</c> name the earlier setup runs
    /// created, so a scene wired before the mosaic existed keeps its references and simply
    /// paints that one cell everywhere until it is wired again.
    /// </summary>
    private static Tile[] EnsureWallAutotiles(bool overwrite = false)
    {
        const int cells = WallMosaicSize * WallMosaicSize;
        var tiles = new Tile[16 * cells];

        _wallBlockPixels = TilePixels * WallMosaicSize;

        for (int mask = 0; mask < 16; mask++)
        {
            _wallMask = mask;
            for (int row = 0; row < WallMosaicSize; row++)
            {
                for (int column = 0; column < WallMosaicSize; column++)
                {
                    _wallCellX = column;
                    _wallCellY = row;

                    string name = row == 0 && column == 0
                        ? $"WallTile_{mask:00}"
                        : $"WallTile_{mask:00}_{column}{row}";

                    tiles[mask * cells + row * WallMosaicSize + column] = EnsureTile(
                        name, TileStyle.WallAutotile, Tile.ColliderType.Grid, overwrite);
                }
            }
        }

        // Back to a single-cell block, so anything drawn after this — the plain WallTile
        // fallback, on a regenerate — is not left keyed on a block it is not part of.
        _wallCellX = _wallCellY = 0;
        _wallBlockPixels = TilePixels;

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
            ("DecalGrit", TileStyle.Grit),
            ("DecalMushrooms", TileStyle.Mushrooms),
            ("DecalBones", TileStyle.Bones),
            ("DecalMoss", TileStyle.Moss),
            ("DecalPuddle", TileStyle.Puddle)
        };

        var tiles = new Tile[DecalVariantCount];
        for (int i = 0; i < styles.Length && i < tiles.Length; i++)
            tiles[i] = EnsureTile(styles[i].name, styles[i].style, Tile.ColliderType.None, overwrite);

        return tiles;
    }

    // ------------------------------------------------------- tiles cut from hand-drawn art

    /// <summary>
    /// Reads a PNG straight off disk into a texture whose pixels can be sampled.
    ///
    /// Deliberately not <c>AssetDatabase.LoadAssetAtPath</c> plus <c>GetPixels</c>: the art is
    /// imported with <c>isReadable: 0</c>, which is the right setting for a shipped sprite and
    /// makes the imported texture unreadable from script. Decoding the file ourselves sidesteps
    /// that without editing the artist's import settings underneath them.
    /// </summary>
    private static Texture2D LoadArtTexture(string path)
    {
        if (!File.Exists(path))
        {
            Debug.LogWarning(
                $"[DungeonSetup] Art '{path}' not found; falling back to generated placeholders.");
            return null;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        if (!texture.LoadImage(File.ReadAllBytes(path)))
        {
            Debug.LogWarning($"[DungeonSetup] Could not decode '{path}'.");
            Object.DestroyImmediate(texture);
            return null;
        }
        return texture;
    }

    /// <summary>
    /// The floor, cut from <see cref="FloorArtPath"/> into an N by N mosaic.
    ///
    /// The art is one large slab of cobbles with torn edges, not a tile: cutting independent
    /// per-cell variants out of it would put a hard discontinuity at every cell border, and no
    /// amount of variants hides that, because the mismatch is at every edge rather than
    /// occasionally. Cutting one region into a block of adjacent pieces instead means the
    /// pieces line up with each other exactly — they were adjacent in the source — so the
    /// stones run unbroken across every border inside the block and only the block seam
    /// repeats, every <see cref="FloorMosaicSize"/> cells.
    ///
    /// The source region is found by searching for a fully opaque square nearest the middle of
    /// the art, so the torn edges and their semi-transparent fringe stay out of the floor. The
    /// search is deterministic, so re-running writes byte-identical PNGs and does not churn the
    /// repository — the same property the generated placeholders have.
    /// </summary>
    private static Tile[] EnsureFloorMosaic(bool overwrite = false)
    {
        Texture2D art = LoadArtTexture(FloorArtPath);
        if (art == null) return null;

        try
        {
            int blockPixels = ArtTilePixels * FloorMosaicSize;

            // Whole multiples of the output only, so the downsample below is an exact box
            // average over equal-sized source squares rather than a resample with seams of
            // its own. Largest first: more source pixels per output pixel is more of the
            // artist's detail retained, and fewer, larger stones per cell.
            int source = 0;
            var origin = Vector2Int.zero;
            for (int scale = 4; scale >= 1; scale--)
            {
                int candidate = blockPixels * scale;
                if (candidate > art.width || candidate > art.height) continue;
                if (!TryFindOpaqueSquare(art, candidate, out origin)) continue;
                source = candidate;
                break;
            }

            if (source == 0)
            {
                Debug.LogWarning(
                    $"[DungeonSetup] No fully opaque {blockPixels}px square in '{FloorArtPath}' " +
                    $"({art.width}x{art.height}); falling back to generated floor tiles.");
                return null;
            }

            Color32[] block = Resample(
                art.GetPixels32(), art.width, art.height,
                new RectInt(origin.x, origin.y, source, source), blockPixels, blockPixels);

            var tiles = new Tile[FloorMosaicSize * FloorMosaicSize];
            for (int row = 0; row < FloorMosaicSize; row++)
            {
                for (int column = 0; column < FloorMosaicSize; column++)
                {
                    Color32[] piece = Crop(block, blockPixels, blockPixels,
                        new RectInt(column * ArtTilePixels, row * ArtTilePixels,
                            ArtTilePixels, ArtTilePixels));

                    // Row-major, matching DungeonPainter.PickFloor's row * size + column.
                    int index = row * FloorMosaicSize + column;
                    tiles[index] = WriteArtTile($"FloorMosaic_{index:00}", piece,
                        ArtTilePixels, ArtTilePixels, Tile.ColliderType.None, overwrite);
                }
            }
            return tiles;
        }
        finally
        {
            Object.DestroyImmediate(art);
        }
    }

    /// <summary>
    /// Paper litter, composed from a single sheet cut out of <see cref="PaperArtPath"/>.
    ///
    /// The art holds four sheets on one transparent canvas, three of them overlapping into one
    /// clump. Taking each clump as a decal is the cheap reading of that image, and it is wrong
    /// twice over: every pile then lands at the one angle and arrangement the artist happened
    /// to draw, and the sheets inside it cannot be told apart or spread out. Splitting the
    /// clump apart is not an option either — an overlapped sheet is drawn with a bite taken out
    /// of it by the one on top, so extracting it yields a notched shape, not a sheet.
    ///
    /// So exactly one sheet is taken — the isolated one — and every decal is <b>composed</b>
    /// from it: each sheet placed at its own angle, its own slight size jitter and its own
    /// offset, and a group is simply a tile with several of them on it. The variants run from
    /// a single dropped page to a scatter of three, per <see cref="PaperSheetCounts"/>.
    ///
    /// The one sheet is identified as the <b>smallest</b> group: a clump contains two or more
    /// sheets and is therefore larger than any one of them. That holds for this art and for any
    /// redraw that keeps at least one sheet clear of the others; if a redraw ever overlapped
    /// every sheet, the check below is what says so rather than silently cutting up a pile.
    /// </summary>
    private static Tile[] EnsurePaperDecals(bool overwrite = false)
    {
        Texture2D art = LoadArtTexture(PaperArtPath);
        if (art == null) return null;

        try
        {
            Color32[] pixels = art.GetPixels32();
            List<OpaqueGroup> groups = FindOpaqueGroups(pixels, art.width, art.height);
            if (groups.Count == 0)
            {
                Debug.LogWarning($"[DungeonSetup] No paper groups found in '{PaperArtPath}'.");
                return null;
            }

            // Smallest by area — see the summary. FindOpaqueGroups sorts largest first.
            OpaqueGroup sheet = groups[groups.Count - 1];
            if (groups.Count > 1 && sheet.Area > groups[0].Area * 0.75f)
            {
                Debug.LogWarning(
                    $"[DungeonSetup] Every clump in '{PaperArtPath}' is a similar size, so the " +
                    "single-sheet one could not be told apart; paper decals will be cut from " +
                    "whichever is smallest and may be a pile rather than one sheet.");
            }

            Color32[] sheetPixels = ExtractGroup(pixels, art.width, sheet);
            int sheetWidth = sheet.Bounds.width, sheetHeight = sheet.Bounds.height;

            var tiles = new List<Tile>(PaperSheetCounts.Length);
            for (int variant = 0; variant < PaperSheetCounts.Length; variant++)
            {
                string name = $"DecalPaper{(char)('A' + variant)}";

                // Seeded by name, so the angles are scattered but a re-run reproduces the same
                // PNGs byte for byte and does not churn the repository — the same property the
                // generated placeholder tiles have.
                var random = new DeterministicRandom(name);
                var tile = new Color32[ArtTilePixels * ArtTilePixels];
                int count = PaperSheetCounts[variant];

                for (int i = 0; i < count; i++)
                {
                    float longestSide = Mathf.Max(sheetWidth, sheetHeight);
                    float scale = PaperSheetFill * ArtTilePixels / longestSide
                                * Mathf.Lerp(1f - PaperScaleJitter, 1f + PaperScaleJitter,
                                    random.NextFloat());

                    // A lone sheet sits near the middle of its cell; several have to spread out
                    // or they stack into something that reads as one blob again.
                    float spread = count == 1 ? PaperLoneSpread : PaperGroupSpread;
                    var centre = new Vector2(
                        ArtTilePixels * (0.5f + (random.NextFloat() * 2f - 1f) * spread),
                        ArtTilePixels * (0.5f + (random.NextFloat() * 2f - 1f) * spread));

                    CompositeRotated(sheetPixels, sheetWidth, sheetHeight, tile, ArtTilePixels,
                        centre, scale, random.NextFloat() * 360f);
                }

                tiles.Add(WriteArtTile(name, tile, ArtTilePixels, ArtTilePixels,
                    Tile.ColliderType.None, overwrite));
            }
            return tiles.ToArray();
        }
        finally
        {
            Object.DestroyImmediate(art);
        }
    }

    /// <summary>
    /// Ground clutter cut from <see cref="NatureDecals"/>: mushroom clusters and a dropped
    /// sleeping bag, each its own source file with exactly one thing drawn on it.
    ///
    /// Unlike <see cref="EnsurePaperDecals"/>, there is no clump to pick apart — the whole
    /// opaque region of the file <i>is</i> the decal — so this crops to it directly rather than
    /// separating groups first. It is still found via <see cref="FindOpaqueGroups"/> and not a
    /// plain alpha bounding box, because that rejects stray flecks under
    /// <c>minimumArea</c> the same way it does for paper, instead of letting a speck of anti-
    /// aliasing outside the real drawing widen the crop.
    ///
    /// Composed once per tile with a random rotation and a small scale jitter, the same
    /// building blocks <see cref="EnsurePaperDecals"/> uses for a lone sheet — ground cover
    /// lying at a fixed angle every time would read as stamped rather than dropped.
    ///
    /// Rarity is not a second weighted-pick system: <see cref="NatureDecals"/>' weight is
    /// realised by writing the same <see cref="Tile"/> reference into the returned array that
    /// many times, so <c>DungeonPainter.PaintDecals</c>' existing uniform pick over the array is
    /// the only selection logic that ever runs.
    /// </summary>
    private static Tile[] EnsureNatureDecals(bool overwrite = false)
    {
        var tiles = new List<Tile>();

        foreach (var (name, artPath, weight) in NatureDecals)
        {
            Texture2D art = LoadArtTexture(artPath);
            if (art == null) continue;

            Tile tile;
            try
            {
                Color32[] pixels = art.GetPixels32();
                List<OpaqueGroup> groups = FindOpaqueGroups(pixels, art.width, art.height);
                if (groups.Count == 0)
                {
                    Debug.LogWarning($"[DungeonSetup] No opaque artwork found in '{artPath}'.");
                    continue;
                }

                // Largest group: the drawing itself, as opposed to any fleck too small to
                // clear minimumArea and therefore already excluded, or — if the source ever
                // gained a second, smaller mark on the same canvas — that mark rather than
                // the subject.
                OpaqueGroup subject = groups[0];
                Color32[] subjectPixels = ExtractGroup(pixels, art.width, subject);
                int subjectWidth = subject.Bounds.width, subjectHeight = subject.Bounds.height;

                var random = new DeterministicRandom(name);
                var composed = new Color32[ArtTilePixels * ArtTilePixels];

                float longestSide = Mathf.Max(subjectWidth, subjectHeight);
                float scale = NatureDecalFill * ArtTilePixels / longestSide
                            * Mathf.Lerp(1f - NatureScaleJitter, 1f + NatureScaleJitter,
                                random.NextFloat());
                var centre = new Vector2(
                    ArtTilePixels * (0.5f + (random.NextFloat() * 2f - 1f) * NatureSpread),
                    ArtTilePixels * (0.5f + (random.NextFloat() * 2f - 1f) * NatureSpread));

                CompositeRotated(subjectPixels, subjectWidth, subjectHeight, composed, ArtTilePixels,
                    centre, scale, random.NextFloat() * 360f);

                tile = WriteArtTile(name, composed, ArtTilePixels, ArtTilePixels,
                    Tile.ColliderType.None, overwrite);
            }
            finally
            {
                Object.DestroyImmediate(art);
            }

            if (tile == null) continue;
            for (int i = 0; i < weight; i++) tiles.Add(tile);
        }

        return tiles.Count > 0 ? tiles.ToArray() : null;
    }

    /// <summary>
    /// Copies one connected group's pixels out into its own bounding-box-sized block, dropping
    /// anything inside that box belonging to a different group. The mask matters even when the
    /// group looks isolated: a bounding box is rectangular and artwork is not, so a neighbour's
    /// corner can intrude into the box without touching the group itself.
    /// </summary>
    private static Color32[] ExtractGroup(Color32[] pixels, int sourceWidth, OpaqueGroup group)
    {
        RectInt bounds = group.Bounds;
        var result = new Color32[bounds.width * bounds.height];

        for (int y = 0; y < bounds.height; y++)
        {
            for (int x = 0; x < bounds.width; x++)
            {
                int local = y * bounds.width + x;
                if (!group.Mask[local]) continue;
                result[local] = pixels[(bounds.y + y) * sourceWidth + bounds.x + x];
            }
        }
        return result;
    }

    /// <summary>
    /// Draws a sheet onto a tile, rotated and scaled about its own centre, blended over
    /// whatever is already there.
    ///
    /// Walks the <i>destination</i> pixels and maps each back into the sheet, rather than
    /// walking the sheet and scattering its pixels forwards. A forward map leaves unwritten
    /// gaps wherever rounding sends two source pixels to the same destination, which on a
    /// rotated sprite shows up as a dusting of pinholes across it.
    ///
    /// Sampling and blending are both done in premultiplied alpha, for the same reason
    /// <see cref="Resample"/> is: interpolating straight RGB across the sheet's edge drags in
    /// the colour of fully transparent pixels and rings the paper in black.
    /// </summary>
    private static void CompositeRotated(Color32[] sheet, int sheetWidth, int sheetHeight,
        Color32[] tile, int tileSize, Vector2 centre, float scale, float angleDegrees)
    {
        float radians = angleDegrees * Mathf.Deg2Rad;
        float cos = Mathf.Cos(radians), sin = Mathf.Sin(radians);

        // Bounding box of the transformed sheet, clipped to the tile, so we only touch pixels
        // that can actually be covered.
        float reach = 0.5f * scale * Mathf.Sqrt(sheetWidth * (float)sheetWidth
                                              + sheetHeight * (float)sheetHeight) + 2f;
        int minX = Mathf.Max(0, Mathf.FloorToInt(centre.x - reach));
        int maxX = Mathf.Min(tileSize - 1, Mathf.CeilToInt(centre.x + reach));
        int minY = Mathf.Max(0, Mathf.FloorToInt(centre.y - reach));
        int maxY = Mathf.Min(tileSize - 1, Mathf.CeilToInt(centre.y + reach));

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                // Destination -> sheet: undo the translate, the rotation and the scale.
                float dx = (x + 0.5f) - centre.x;
                float dy = (y + 0.5f) - centre.y;
                float sx = (dx * cos + dy * sin) / scale + sheetWidth * 0.5f;
                float sy = (-dx * sin + dy * cos) / scale + sheetHeight * 0.5f;

                if (!TrySampleBilinear(sheet, sheetWidth, sheetHeight, sx, sy,
                        out float sr, out float sg, out float sb, out float sa))
                    continue;
                if (sa <= 0.0001f) continue;

                int index = y * tileSize + x;
                Color32 under = tile[index];
                float ua = under.a / 255f;

                // Source over destination, in premultiplied terms.
                float outAlpha = sa + ua * (1f - sa);
                if (outAlpha <= 0.0001f)
                {
                    tile[index] = new Color32(0, 0, 0, 0);
                    continue;
                }

                float keep = ua * (1f - sa);
                tile[index] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt((sr + under.r / 255f * keep) / outAlpha * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt((sg + under.g / 255f * keep) / outAlpha * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt((sb + under.b / 255f * keep) / outAlpha * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(outAlpha * 255f), 0, 255));
            }
        }
    }

    /// <summary>
    /// Bilinear sample, returning colour premultiplied by alpha. False when the point falls
    /// outside the image.
    /// </summary>
    private static bool TrySampleBilinear(Color32[] pixels, int width, int height,
        float x, float y, out float r, out float g, out float b, out float a)
    {
        r = g = b = a = 0f;
        x -= 0.5f;
        y -= 0.5f;
        if (x < -1f || y < -1f || x > width || y > height) return false;

        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;

        for (int corner = 0; corner < 4; corner++)
        {
            int cx = x0 + (corner & 1);
            int cy = y0 + (corner >> 1);
            if (cx < 0 || cy < 0 || cx >= width || cy >= height) continue;

            float weight = ((corner & 1) == 0 ? 1f - fx : fx) * ((corner >> 1) == 0 ? 1f - fy : fy);
            if (weight <= 0f) continue;

            Color32 pixel = pixels[cy * width + cx];
            float alpha = pixel.a / 255f;
            r += pixel.r / 255f * alpha * weight;
            g += pixel.g / 255f * alpha * weight;
            b += pixel.b / 255f * alpha * weight;
            a += alpha * weight;
        }
        return true;
    }

    /// <summary>
    /// Writes one tile's pixels to a PNG and binds a <see cref="Tile"/> to it, mirroring
    /// <see cref="EnsureTile"/>'s contract: the texture is only written when missing, so art
    /// swapped in by hand survives a routine re-run.
    /// </summary>
    private static Tile WriteArtTile(string name, Color32[] pixels, int width, int height,
        Tile.ColliderType colliderType, bool overwrite)
    {
        string texturePath = $"{TilesFolder}/{name}.png";
        string tilePath = $"{TilesFolder}/{name}.asset";

        if (overwrite || !File.Exists(texturePath))
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                texture.SetPixels32(pixels);
                texture.Apply();
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextureImporter(texturePath, ArtTilePixels);
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

    /// <summary>
    /// Finds the best square of the given size to cut the floor mosaic from.
    ///
    /// "Best" is three things, in order of how loudly they show up once the block is repeated
    /// across a room:
    /// <list type="number">
    /// <item><b>Fully opaque.</b> The art is a slab with torn edges and a semi-transparent
    /// fringe; a crop that catches any of it puts see-through notches in the floor.</item>
    /// <item><b>As little moss as possible.</b> Cobble is uniform noise and repeats invisibly,
    /// but a tuft of moss is a landmark, and a landmark on a fixed grid is precisely the
    /// wallpaper effect the mosaic exists to avoid. Weighted heavily for that reason.</item>
    /// <item><b>Matching tone on opposite edges.</b> The slab is lit unevenly, so a crop
    /// straddling that gradient is brighter down one side than the other and every block
    /// boundary becomes a visible step.</item>
    /// </list>
    ///
    /// Moss is told from stone by green minus blue, not by green against both other channels:
    /// this moss is olive — measured at green only about 10 above red, but 59 to 65 above blue,
    /// against 8 for stone. A test phrased the intuitive way, as green dominating red as well,
    /// matches none of it.
    ///
    /// Scanned on a coarse stride: neighbouring offsets differ by a pixel of pattern, so
    /// testing every position costs time and buys nothing.
    /// </summary>
    private static bool TryFindOpaqueSquare(Texture2D art, int size, out Vector2Int origin)
    {
        origin = Vector2Int.zero;
        if (size > art.width || size > art.height) return false;

        Color32[] pixels = art.GetPixels32();
        int width = art.width, height = art.height;

        // Summed-area tables, so testing a square is four lookups instead of size*size — the
        // difference between a moment and a minute on a multi-megapixel source.
        var sheerTable = new int[(width + 1) * (height + 1)];
        var mossTable = new int[(width + 1) * (height + 1)];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Color32 pixel = pixels[y * width + x];
                int sheer = pixel.a < OpaqueAlpha ? 1 : 0;
                int moss = pixel.g - pixel.b >= MossGreenOverBlue && pixel.g >= pixel.r ? 1 : 0;

                int here = (y + 1) * (width + 1) + x + 1;
                int up = y * (width + 1) + x + 1;
                int left = (y + 1) * (width + 1) + x;
                int diagonal = y * (width + 1) + x;

                sheerTable[here] = sheer + sheerTable[up] + sheerTable[left] - sheerTable[diagonal];
                mossTable[here] = moss + mossTable[up] + mossTable[left] - mossTable[diagonal];
            }
        }

        float best = float.MaxValue;
        bool found = false;
        int stride = Mathf.Max(1, size / 16);

        for (int y = 0; y + size <= height; y += stride)
        {
            for (int x = 0; x + size <= width; x += stride)
            {
                if (BoxSum(sheerTable, width, x, y, size) != 0) continue;

                float mossFraction = BoxSum(mossTable, width, x, y, size) / (float)(size * size);
                float score = mossFraction * 100f * MossPenalty
                            + EdgeToneMismatch(pixels, width, x, y, size);
                if (score >= best) continue;

                best = score;
                origin = new Vector2Int(x, y);
                found = true;
            }
        }
        return found;
    }

    /// <summary>Sum over a square in a summed-area table.</summary>
    private static int BoxSum(int[] table, int width, int x, int y, int size)
    {
        return table[(y + size) * (width + 1) + x + size]
             - table[y * (width + 1) + x + size]
             - table[(y + size) * (width + 1) + x]
             + table[y * (width + 1) + x];
    }

    /// <summary>
    /// How badly the tone of a candidate's left edge disagrees with its right, plus top against
    /// bottom. These are the edges that end up adjacent when the block repeats, so a difference
    /// here is exactly what the eye reads as a seam.
    /// </summary>
    private static float EdgeToneMismatch(Color32[] pixels, int width, int x, int y, int size)
    {
        int band = Mathf.Max(4, size / 64);

        float MeanLuma(int x0, int y0, int x1, int y1)
        {
            float sum = 0f;
            int count = 0;
            for (int yy = y0; yy < y1; yy++)
            {
                for (int xx = x0; xx < x1; xx++)
                {
                    Color32 pixel = pixels[yy * width + xx];
                    sum += 0.299f * pixel.r + 0.587f * pixel.g + 0.114f * pixel.b;
                    count++;
                }
            }
            return count == 0 ? 0f : sum / count;
        }

        float horizontal = Mathf.Abs(
            MeanLuma(x, y, x + band, y + size) - MeanLuma(x + size - band, y, x + size, y + size));
        float vertical = Mathf.Abs(
            MeanLuma(x, y, x + size, y + band) - MeanLuma(x, y + size - band, x + size, y + size));
        return horizontal + vertical;
    }

    /// <summary>One connected clump of opaque pixels: where it is, and which pixels are its.</summary>
    private sealed class OpaqueGroup
    {
        public RectInt Bounds;
        public int Area;

        /// <summary>
        /// Membership over the bounding box, row-major. Carried rather than recomputed because
        /// a bounding box can also contain pixels of a <i>different</i> group, and only this
        /// says which are which.
        /// </summary>
        public bool[] Mask;
    }

    /// <summary>
    /// Each connected clump of opaque pixels, largest first. Used to pick the paper art apart
    /// without hand-measuring it. Iterative rather than recursive: a clump can run to tens of
    /// thousands of pixels and a recursive flood fill would blow the stack on the larger ones.
    /// </summary>
    private static List<OpaqueGroup> FindOpaqueGroups(Color32[] pixels, int width, int height,
        byte alphaThreshold = 40, int minimumArea = 2000)
    {
        var seen = new bool[width * height];
        var groups = new List<OpaqueGroup>();
        var stack = new Stack<int>();
        var members = new List<int>();

        for (int start = 0; start < pixels.Length; start++)
        {
            if (seen[start] || pixels[start].a < alphaThreshold) continue;

            int minX = start % width, maxX = minX;
            int minY = start / width, maxY = minY;
            members.Clear();

            seen[start] = true;
            stack.Push(start);
            while (stack.Count > 0)
            {
                int index = stack.Pop();
                int x = index % width, y = index / width;
                members.Add(index);
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;

                TryPushNeighbour(x - 1, y, x > 0);
                TryPushNeighbour(x + 1, y, x < width - 1);
                TryPushNeighbour(x, y - 1, y > 0);
                TryPushNeighbour(x, y + 1, y < height - 1);
            }

            if (members.Count < minimumArea) continue;

            var bounds = new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
            var mask = new bool[bounds.width * bounds.height];
            foreach (int index in members)
            {
                int x = index % width - bounds.x;
                int y = index / width - bounds.y;
                mask[y * bounds.width + x] = true;
            }

            groups.Add(new OpaqueGroup { Bounds = bounds, Area = members.Count, Mask = mask });
        }

        groups.Sort((a, b) => b.Area.CompareTo(a.Area));
        return groups;

        void TryPushNeighbour(int x, int y, bool inBounds)
        {
            if (!inBounds) return;
            int index = y * width + x;
            if (seen[index] || pixels[index].a < alphaThreshold) return;
            seen[index] = true;
            stack.Push(index);
        }
    }

    /// <summary>
    /// Area-averaging resample of a sub-rectangle down to the requested size.
    ///
    /// Averages in <b>premultiplied</b> alpha and divides back out at the end. Averaging raw
    /// RGB would drag the colour of fully transparent pixels — black, in a PNG's unused
    /// regions — into every edge pixel, which is what puts a dark fringe around a cut-out
    /// sprite. Irrelevant for the floor, which is opaque throughout; essential for paper,
    /// which is nearly all edge.
    /// </summary>
    private static Color32[] Resample(Color32[] source, int sourceWidth, int sourceHeight,
        RectInt region, int targetWidth, int targetHeight)
    {
        var result = new Color32[targetWidth * targetHeight];

        for (int y = 0; y < targetHeight; y++)
        {
            int y0 = region.y + region.height * y / targetHeight;
            int y1 = Mathf.Max(y0 + 1, region.y + region.height * (y + 1) / targetHeight);
            y1 = Mathf.Min(y1, sourceHeight);

            for (int x = 0; x < targetWidth; x++)
            {
                int x0 = region.x + region.width * x / targetWidth;
                int x1 = Mathf.Max(x0 + 1, region.x + region.width * (x + 1) / targetWidth);
                x1 = Mathf.Min(x1, sourceWidth);

                float r = 0f, g = 0f, b = 0f, a = 0f;
                int count = 0;
                for (int sy = y0; sy < y1; sy++)
                {
                    for (int sx = x0; sx < x1; sx++)
                    {
                        Color32 pixel = source[sy * sourceWidth + sx];
                        float alpha = pixel.a / 255f;
                        r += pixel.r * alpha;
                        g += pixel.g * alpha;
                        b += pixel.b * alpha;
                        a += alpha;
                        count++;
                    }
                }

                if (count == 0) continue;

                float meanAlpha = a / count;
                if (meanAlpha <= 0f)
                {
                    result[y * targetWidth + x] = new Color32(0, 0, 0, 0);
                    continue;
                }

                result[y * targetWidth + x] = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(r / count / meanAlpha), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(g / count / meanAlpha), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(b / count / meanAlpha), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(meanAlpha * 255f), 0, 255));
            }
        }
        return result;
    }

    /// <summary>Copies a sub-rectangle out of a pixel block.</summary>
    private static Color32[] Crop(Color32[] source, int sourceWidth, int sourceHeight, RectInt region)
    {
        var result = new Color32[region.width * region.height];
        for (int y = 0; y < region.height; y++)
        {
            int sourceY = Mathf.Clamp(region.y + y, 0, sourceHeight - 1);
            for (int x = 0; x < region.width; x++)
            {
                int sourceX = Mathf.Clamp(region.x + x, 0, sourceWidth - 1);
                result[y * region.width + x] = source[sourceY * sourceWidth + sourceX];
            }
        }
        return result;
    }

    /// <summary>
    /// Joins every decal source into one array, skipping any that is absent (art not on disk,
    /// nothing found in it). The painter picks between decals uniformly, so this array's
    /// composition <i>is</i> the mix — including within one source, for callers like
    /// <see cref="EnsureNatureDecals"/> that repeat a tile reference to weight it.
    /// </summary>
    /// <summary>
    /// The first tile of a generated set, for the painter slots that take a single tile
    /// rather than an array. <see cref="DungeonPainter"/>'s rubble slot is one tile, so the
    /// other two variants are drawn and left for whenever that slot becomes an array.
    /// </summary>
    private static Tile FirstOrNull(Tile[] tiles)
    {
        return tiles != null && tiles.Length > 0 ? tiles[0] : null;
    }

    private static Tile[] CombineDecals(params Tile[][] sources)
    {
        var all = new List<Tile>();
        foreach (Tile[] source in sources)
        {
            if (source == null) continue;
            foreach (Tile tile in source) if (tile != null) all.Add(tile);
        }
        return all.Count > 0 ? all.ToArray() : null;
    }

    /// <summary>
    /// Finds one already-composited tile by its asset name, e.g. picking the sleeping bag
    /// back out of <see cref="EnsureNatureDecals"/>' weighted array for
    /// <see cref="DungeonPainter"/>'s separate guaranteed-hub-decal slot. A Tile asset's
    /// <c>name</c> is the asset's file name once loaded via <c>AssetDatabase</c>, which is
    /// exactly the name it was written under in <see cref="WriteArtTile"/>.
    /// </summary>
    private static Tile FindTileByName(Tile[] tiles, string name)
    {
        if (tiles == null) return null;
        foreach (Tile tile in tiles) if (tile != null && tile.name == name) return tile;
        return null;
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
        EnsureTile("PillarTile", TileStyle.Pillar, Tile.ColliderType.Sprite, overwrite: true);
        AssignCircularPhysicsShape($"{TilesFolder}/PillarTile.png", PillarRadius);
        InkedTileGenerator.EnsureRubble(overwrite: true);
        InkedTileGenerator.EnsureExitFloor(overwrite: true);
        EnsureDecalTiles(overwrite: true);
        InkedTileGenerator.EnsureInkedDecals(overwrite: true);
        EnsureWallAutotiles(overwrite: true);

        // Recut from the source art as well. These are not placeholders, but they are just as
        // much generated output, and leaving them stale after a redraw is how the floor ends
        // up cut to a version of the art that is no longer on disk.
        EnsureFloorMosaic(overwrite: true);
        EnsurePaperDecals(overwrite: true);
        EnsureNatureDecals(overwrite: true);

        AssetDatabase.SaveAssets();
        // Redrawing does not rewire anything. A scene wired before the wall mosaic existed
        // still holds sixteen wall tiles and paints the block's first cell everywhere, which
        // looks like the redraw half worked rather than like a scene that needs rewiring.
        Debug.Log($"[DungeonSetup] Placeholder tiles redrawn in '{TilesFolder}'. " +
                  "Run Tools > Dungeon > Setup Scene Tilemaps to pick up tiles the scene " +
                  "does not reference yet.");
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

    /// <summary>Sets a private serialized int field by name.</summary>
    private static void SetInt(Object component, string field, int value)
    {
        var serialized = new SerializedObject(component);
        var property = serialized.FindProperty(field);
        if (property == null)
        {
            Debug.LogWarning($"[DungeonSetup] Field '{field}' not found on {component.GetType().Name}.");
            return;
        }
        property.intValue = value;
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
