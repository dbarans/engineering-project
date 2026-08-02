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
        EnsureRegistryEntries(registry);
        DungeonGenerationSettings settings = EnsureSettings();
        RoomContentSettings contentSettings = EnsureContentSettings();

        Grid grid = EnsureGrid();
        Tilemap floor = EnsureFloorTilemap(grid);
        Tilemap walls = EnsureWallTilemap(grid);

        WarnOnCellSizeMismatch(grid);
        WireGenerator(grid, floor, walls, floorTile, wallTile, settings, contentSettings, registry);

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
    private static void WireGenerator(Grid grid, Tilemap floor, Tilemap walls,
        Tile floorTile, Tile wallTile, DungeonGenerationSettings settings,
        RoomContentSettings contentSettings, PrefabRegistry registry)
    {
        var root = grid.gameObject;

        var painter = EditorSetupUtility.EnsureComponent<DungeonPainter>(root);
        SetRef(painter, "floorTilemap", floor);
        SetRef(painter, "wallTilemap", walls);
        SetRef(painter, "floorTile", floorTile);
        SetRef(painter, "wallTile", wallTile);

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
