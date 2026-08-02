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

    private const int ObstacleStaticLayer = 8; // matches the obstacle masks on FieldOfView/PathfindingGrid

    // World objects sit at sorting order 0..1, so both tilemaps must draw below them.
    private const int FloorSortingOrder = -100;
    private const int WallSortingOrder = -50;

    /// <summary>Tile texture side in pixels; imported at the same PPU so one tile is one world unit.</summary>
    private const int TilePixels = 32;

    private static readonly Color FloorColor = new Color32(0x2A, 0x26, 0x22, 0xFF);
    private static readonly Color FloorEdgeColor = new Color32(0x22, 0x1F, 0x1B, 0xFF);
    private static readonly Color WallColor = new Color32(0x4A, 0x44, 0x3C, 0xFF);
    private static readonly Color WallEdgeColor = new Color32(0x30, 0x2C, 0x26, 0xFF);

    [MenuItem("Tools/Dungeon/Setup Scene Tilemaps")]
    public static void Setup()
    {
        EnsureFolders();

        Tile floorTile = EnsureTile("FloorTile", FloorColor, FloorEdgeColor, Tile.ColliderType.None);
        Tile wallTile = EnsureTile("WallTile", WallColor, WallEdgeColor, Tile.ColliderType.Grid);
        PrefabRegistry registry = EnsureRegistry();

        Grid grid = EnsureGrid();
        Tilemap floor = EnsureFloorTilemap(grid);
        Tilemap walls = EnsureWallTilemap(grid);

        WarnOnCellSizeMismatch(grid);

        EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[DungeonSetup] Ready. Floor='{floor.name}', Walls='{walls.name}' " +
            $"(layer {LayerMask.LayerToName(ObstacleStaticLayer)}), tiles in '{TilesFolder}', " +
            $"registry at '{RegistryPath}' ({registry.Entries.Count} entries).");

        EditorUtility.DisplayDialog("Dungeon Setup",
            "Scene prepared for procedural generation.\n\n" +
            "• DungeonRoot ▸ Floor / Walls tilemaps\n" +
            "• Walls: TilemapCollider2D merged into a CompositeCollider2D, layer ObstacleStatic\n" +
            "• Placeholder FloorTile / WallTile assets\n" +
            "• Empty PrefabRegistry in Resources\n\n" +
            "Verify Stage 0 by hand: paint a few wall tiles with the Tile Palette, then check " +
            "in Play mode that they block the player's field of view and that an enemy paths " +
            "around them. Rebuild the PathfindingGrid after painting — it samples physics once " +
            "on Awake.",
            "OK");

        Selection.activeGameObject = grid.gameObject;
    }

    // ---------------------------------------------------------------- scene objects

    private static Grid EnsureGrid()
    {
        var existing = GameObject.Find("DungeonRoot");
        if (existing != null)
        {
            var found = existing.GetComponent<Grid>();
            if (found != null) return found;
            return Undo.AddComponent<Grid>(existing);
        }

        var root = new GameObject("DungeonRoot");
        Undo.RegisterCreatedObjectUndo(root, "Create Dungeon Root");
        var grid = root.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);
        return grid;
    }

    private static Tilemap EnsureFloorTilemap(Grid grid)
    {
        Tilemap tilemap = EnsureTilemap(grid, "Floor");
        tilemap.GetComponent<TilemapRenderer>().sortingOrder = FloorSortingOrder;
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
        go.GetComponent<TilemapRenderer>().sortingOrder = WallSortingOrder;

        var body = go.GetComponent<Rigidbody2D>() ?? Undo.AddComponent<Rigidbody2D>(go);
        body.bodyType = RigidbodyType2D.Static;

        var composite = go.GetComponent<CompositeCollider2D>() ?? Undo.AddComponent<CompositeCollider2D>(go);
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

        var collider = go.GetComponent<TilemapCollider2D>() ?? Undo.AddComponent<TilemapCollider2D>(go);
        collider.compositeOperation = Collider2D.CompositeOperation.Merge;

        return tilemap;
    }

    private static Tilemap EnsureTilemap(Grid grid, string name)
    {
        Transform child = grid.transform.Find(name);
        if (child != null)
        {
            var found = child.GetComponent<Tilemap>();
            if (found != null) return found;
            Debug.LogWarning(
                $"[DungeonSetup] '{name}' exists under DungeonRoot but has no Tilemap — adding one.", child);
            child.gameObject.AddComponent<TilemapRenderer>();
            return child.gameObject.AddComponent<Tilemap>();
        }

        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, $"Create {name} Tilemap");
        go.transform.SetParent(grid.transform, false);
        var tilemap = go.AddComponent<Tilemap>();
        go.AddComponent<TilemapRenderer>();
        return tilemap;
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
    /// Placeholder tile: a flat square with a darker one-pixel border so tile seams stay
    /// readable while iterating on layouts. Replaced by Rule Tiles in Stage 5.
    /// </summary>
    private static Tile EnsureTile(string name, Color fill, Color edge, Tile.ColliderType colliderType)
    {
        string texturePath = $"{TilesFolder}/{name}.png";
        string tilePath = $"{TilesFolder}/{name}.asset";

        if (!File.Exists(texturePath))
        {
            File.WriteAllBytes(texturePath, BuildTileTexture(fill, edge));
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

    private static byte[] BuildTileTexture(Color fill, Color edge)
    {
        var texture = new Texture2D(TilePixels, TilePixels, TextureFormat.RGBA32, false);
        try
        {
            for (int y = 0; y < TilePixels; y++)
            {
                for (int x = 0; x < TilePixels; x++)
                {
                    bool border = x == 0 || y == 0 || x == TilePixels - 1 || y == TilePixels - 1;
                    texture.SetPixel(x, y, border ? edge : fill);
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

    /// <summary>Point filtering and PPU = tile size, so one tile covers exactly one world unit.</summary>
    private static void ConfigureTextureImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = TilePixels;
        importer.filterMode = FilterMode.Point;
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
