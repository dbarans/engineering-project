using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click setup for lootable chests: builds the Chest prefab (placeholder sprite,
/// solid collider + interaction trigger, <see cref="ChestInventory"/>,
/// <see cref="ChestInteractable"/> and the save components), registers it in the
/// <see cref="PrefabRegistry"/> as <c>world.chest</c> so the dungeon generator can spawn
/// it, generates the shared <see cref="ChestUI"/> panel on the main canvas, and upgrades
/// any chest already sitting in the open scene so its contents start being saved.
///
/// The prefab is what makes chests scale to a whole map: every instance carries its own
/// <see cref="SaveableEntity"/> guid and its own <see cref="ItemContainer"/>, so chests
/// are completely independent of one another and each one's contents are saved
/// separately.
///
/// Requires the canvas from <b>Tools ▸ Slot Inventory ▸ Build UI &amp; Wire Scene</b>.
/// Idempotent: re-running rebuilds the prefab and UI in place and leaves placed chests
/// where they are.
/// </summary>
public static class ChestSetup
{
    private const string GeneratedArtFolder = "Assets/Generation";
    private const string ChestSpritePath = GeneratedArtFolder + "/Chest.png";
    private const string ChestPrefabPath = "Assets/Prefabs/World/Chest.prefab";
    private const string ResourcesFolder = "Assets/Resources";
    private const string RegistryPath = ResourcesFolder + "/" + PrefabRegistry.ResourcesPath + ".asset";
    private const string SlotPrefabPath = "Assets/Prefabs/UI/InventorySlot.prefab";
    private const string SlotBgSpritePath = "Assets/Art/GDS/Sprites/ui/backgrounds/bg-slot.png";
    private const string WindowBgSpritePath = "Assets/Art/GDS/Sprites/ui/backgrounds/bg-window.png";

    /// <summary>Registry id written into save files. Permanent — never change it.</summary>
    private const string ChestPrefabId = "world.chest";

    /// <summary>Sprite side in pixels, imported at the same PPU so a chest is one world unit.</summary>
    private const int ChestPixels = 32;

    // Blocking follows the Barrel/CraftingTable convention: a solid collider on the
    // ObstaclePathOnly layer, plus a padded trigger the player has to stand in to open it.
    private const string ObstacleLayerName = "ObstaclePathOnly";
    private const float ChestScale = 0.9f;
    private const float ReachPadding = 0.6f;

    // Chest grid — must match ChestInventory's columns × rows, since ChestUI sizes itself
    // from the panel and reuses the same slot views for every chest.
    private const int Columns = 4;
    private const int Rows = 4;

    private const float CellSize = 64f;
    private const float CellSpacing = 6f;
    private const float GridPadding = 4f;
    private const float SlotsInset = 8f;
    private const float TitleHeight = 40f;

    [MenuItem("Tools/Slot Inventory/Build Chest & UI")]
    public static void BuildAndWire()
    {
        var backpack = Object.FindFirstObjectByType<BackpackUI>();
        // Canvas via the backpack, not FindFirstObjectByType<Canvas>: the HeldItem cursor
        // has its own nested canvas (same pitfall as CraftingBoxSetup).
        var canvas = backpack != null ? backpack.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Chest",
                "No main canvas found. Run 'Tools ▸ Slot Inventory ▸ Build UI & Wire Scene' " +
                "first, then run this again.", "OK");
            return;
        }

        Sprite sprite = EnsureChestSprite();
        GameObject prefab = BuildChestPrefab(sprite);
        RegisterChest(prefab);
        ChestUI ui = EnsureChestPanel(canvas);
        int upgraded = UpgradeSceneChests(prefab);
        bool placed = EnsureSceneChest(prefab);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log(
            $"[Chest] Setup complete: prefab at '{ChestPrefabPath}', registered as " +
            $"'{ChestPrefabId}', {Columns}x{Rows} UI panel, {upgraded} existing chest(s) upgraded.");
        EditorUtility.DisplayDialog("Chest",
            "Setup complete.\n\n" +
            $"• Chest prefab ({Columns}×{Rows} slots) with SaveableEntity + ChestSaveable\n" +
            $"• Registered as '{ChestPrefabId}' — set it as the chest prefab id on the " +
            "RoomContentSettings asset and the dungeon generator will scatter chests\n" +
            "• Shared chest UI panel on the main canvas\n" +
            (upgraded > 0
                ? $"• {upgraded} chest(s) already in this scene upgraded so their contents save\n"
                : string.Empty) +
            (placed ? "• A chest placed next to the player spawn\n" : string.Empty) +
            "\nIn Play mode walk up to a chest and press E. Each chest has its own " +
            "contents, and each is saved and restored independently.\n\n" +
            "The sprite is a generated placeholder — overwrite " + ChestSpritePath +
            " with real art whenever it exists.",
            "OK");

        if (ui != null) Selection.activeGameObject = ui.gameObject;
    }

    // ---------------------------------------------------------------- prefab

    /// <summary>
    /// (Re)builds the chest prefab in place — a stable asset GUID is what keeps existing
    /// scene instances linked to it. Carries the four components a saveable chest needs:
    /// storage, interaction, identity (<see cref="SaveableEntity"/>) and the payload
    /// provider (<see cref="ChestSaveable"/>).
    /// </summary>
    private static GameObject BuildChestPrefab(Sprite sprite)
    {
        var root = new GameObject("Chest");
        try
        {
            root.transform.localScale = Vector3.one * ChestScale;

            int obstacleLayer = LayerMask.NameToLayer(ObstacleLayerName);
            if (obstacleLayer >= 0) root.layer = obstacleLayer;
            else
                Debug.LogWarning(
                    $"[Chest] Layer '{ObstacleLayerName}' not found — chests won't block movement.");

            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 1; // above the floor, like the Table/CraftingTable prefabs

            var solid = root.AddComponent<BoxCollider2D>();
            solid.isTrigger = false;
            if (sprite != null)
            {
                solid.size = sprite.bounds.size;
                solid.offset = sprite.bounds.center;
            }

            // Reach zone: ChestInteractable's trigger. Collider sizes are local, so the
            // world-space margin has to be divided by the prefab's scale.
            var reach = root.AddComponent<BoxCollider2D>();
            reach.isTrigger = true;
            if (sprite != null)
            {
                float margin = ReachPadding / ChestScale;
                reach.size = (Vector2)sprite.bounds.size + new Vector2(margin * 2f, margin * 2f);
                reach.offset = sprite.bounds.center;
            }

            root.AddComponent<ChestInventory>();
            root.AddComponent<ChestInteractable>();
            root.AddComponent<SaveableEntity>();
            root.AddComponent<ChestSaveable>();

            Directory.CreateDirectory(Path.GetDirectoryName(ChestPrefabPath));
            return PrefabUtility.SaveAsPrefabAsset(root, ChestPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Adds the chest to the prefab registry under its permanent id. Additive: an
    /// existing entry is repointed rather than duplicated, because that id may already
    /// appear in save files.
    /// </summary>
    private static void RegisterChest(GameObject prefab)
    {
        if (prefab == null) return;

        var registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(RegistryPath);
        if (registry == null)
        {
            Debug.LogWarning(
                $"[Chest] No PrefabRegistry at '{RegistryPath}' — '{ChestPrefabId}' not " +
                "registered, so generated chests cannot be respawned from a save. Run " +
                "Tools ▸ Dungeon ▸ Build Dungeon Scene first.");
            return;
        }

        var serialized = new SerializedObject(registry);
        SerializedProperty entries = serialized.FindProperty("entries");

        for (int i = 0; i < entries.arraySize; i++)
        {
            SerializedProperty existing = entries.GetArrayElementAtIndex(i);
            if (existing.FindPropertyRelative("id").stringValue != ChestPrefabId) continue;

            existing.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(registry);
            return;
        }

        int index = entries.arraySize;
        entries.InsertArrayElementAtIndex(index);
        SerializedProperty entry = entries.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("id").stringValue = ChestPrefabId;
        entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(registry);
    }

    // ---------------------------------------------------------------- scene

    /// <summary>
    /// Gives chests already placed in the scene by hand the components they need to be
    /// saved. Without this an older chest keeps working but silently loses its contents
    /// on every load. Returns how many were upgraded.
    /// </summary>
    private static int UpgradeSceneChests(GameObject prefab)
    {
        int upgraded = 0;

        foreach (var chest in Object.FindObjectsByType<ChestInventory>(
                     FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            bool missing = chest.GetComponent<SaveableEntity>() == null ||
                           chest.GetComponent<ChestSaveable>() == null;
            if (!missing) continue;

            // A prefab instance gets the components from the prefab itself once it is
            // reimported; adding them here as instance overrides would shadow that.
            if (PrefabUtility.GetCorrespondingObjectFromSource(chest.gameObject) == prefab) continue;

            EditorSetupUtility.EnsureComponent<SaveableEntity>(chest.gameObject);
            EditorSetupUtility.EnsureComponent<ChestSaveable>(chest.gameObject);
            EditorUtility.SetDirty(chest.gameObject);
            upgraded++;

            Debug.Log(
                $"[Chest] Upgraded hand-placed chest '{chest.name}' — its contents are now " +
                "saved. Consider replacing it with an instance of the Chest prefab.", chest);
        }

        return upgraded;
    }

    /// <summary>Drops one chest next to the player spawn when the scene has none. Returns whether it did.</summary>
    private static bool EnsureSceneChest(GameObject prefab)
    {
        if (prefab == null) return false;
        if (Object.FindFirstObjectByType<ChestInventory>() != null) return false;

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = DefaultChestPosition();
        Undo.RegisterCreatedObjectUndo(instance, "Create Chest");
        return true;
    }

    /// <summary>Next to the player spawn, origin otherwise.</summary>
    private static Vector3 DefaultChestPosition()
    {
        var gameManager = Object.FindFirstObjectByType<GameManager>();
        if (gameManager != null)
        {
            var spawn = new SerializedObject(gameManager)
                .FindProperty("playerSpawnPoint").objectReferenceValue as Transform;
            if (spawn != null) return spawn.position + new Vector3(2.5f, 0f, 0f);
        }
        return Vector3.zero;
    }

    // ---------------------------------------------------------------- UI

    /// <summary>
    /// Builds (or refreshes) the single shared chest panel. One panel serves every chest
    /// on the map — <see cref="ChestUI"/> rebinds it to whichever container is open — so
    /// this is a scene-level object, not something on the chest prefab.
    /// </summary>
    private static ChestUI EnsureChestPanel(Canvas canvas)
    {
        GameObject slotPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SlotPrefabPath);
        if (slotPrefab == null)
            Debug.LogWarning(
                $"[Chest] No slot prefab at '{SlotPrefabPath}' — the chest panel will have " +
                "no slots. Run 'Tools ▸ Slot Inventory ▸ Build UI & Wire Scene' first.");

        var held = Object.FindFirstObjectByType<HeldItemController>();

        var existing = Object.FindFirstObjectByType<ChestUI>();
        if (existing != null)
        {
            if (existing.transform.parent != canvas.transform)
                existing.transform.SetParent(canvas.transform, false);

            ConfigureGrid(GetSlotsGrid(existing));
            ApplyPanelLayout(GetPanelRect(existing));
            SetRef(existing, "slotPrefab", slotPrefab);
            SetRef(existing, "heldItem", held);
            return existing;
        }

        var rootRt = NewUI("ChestUI", canvas.transform);
        Stretch(rootRt, 0);
        var ui = rootRt.gameObject.AddComponent<ChestUI>();

        var panelRt = NewUI("Panel", rootRt);
        var panelImg = panelRt.gameObject.AddComponent<Image>();
        panelImg.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);
        var windowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(WindowBgSpritePath);
        if (windowSprite != null) { panelImg.sprite = windowSprite; panelImg.type = Image.Type.Sliced; }

        var titleRt = NewUI("Title", panelRt);
        titleRt.anchorMin = new Vector2(0, 1);
        titleRt.anchorMax = new Vector2(1, 1);
        titleRt.pivot = new Vector2(0.5f, 1);
        titleRt.sizeDelta = new Vector2(0, 36);
        titleRt.anchoredPosition = new Vector2(0, -4);
        var title = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "Chest";
        title.alignment = TextAlignmentOptions.Center;
        title.fontSize = 24;
        title.fontStyle = FontStyles.Bold;
        title.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) title.font = TMP_Settings.defaultFontAsset;

        var slotsRt = NewUI("Slots", panelRt);
        slotsRt.anchorMin = Vector2.zero;
        slotsRt.anchorMax = Vector2.one;
        slotsRt.offsetMin = new Vector2(SlotsInset, SlotsInset);
        slotsRt.offsetMax = new Vector2(-SlotsInset, -TitleHeight);
        ConfigureGrid(slotsRt.gameObject.AddComponent<GridLayoutGroup>());

        SetRef(ui, "panelRoot", panelRt.gameObject);
        SetRef(ui, "slotsContainer", slotsRt);
        SetRef(ui, "slotPrefab", slotPrefab);
        SetRef(ui, "heldItem", held);

        ApplyPanelLayout(panelRt);

        Undo.RegisterCreatedObjectUndo(rootRt.gameObject, "Create Chest UI");
        return ui;
    }

    /// <summary>Sizes the panel to the chest grid and centres it, slightly above the middle.</summary>
    private static void ApplyPanelLayout(RectTransform panel)
    {
        if (panel == null) return;

        float gridWidth = Columns * CellSize + (Columns - 1) * CellSpacing + 2 * GridPadding;
        float gridHeight = Rows * CellSize + (Rows - 1) * CellSpacing + 2 * GridPadding;

        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(
            gridWidth + 2 * SlotsInset, gridHeight + TitleHeight + SlotsInset);
        // Above centre, so the panel does not sit on top of the hotbar along the bottom.
        panel.anchoredPosition = new Vector2(0f, 80f);
    }

    private static void ConfigureGrid(GridLayoutGroup grid)
    {
        if (grid == null) return;
        grid.cellSize = new Vector2(CellSize, CellSize);
        grid.spacing = new Vector2(CellSpacing, CellSpacing);
        grid.padding = new RectOffset(
            (int)GridPadding, (int)GridPadding, (int)GridPadding, (int)GridPadding);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = Columns;
        grid.childAlignment = TextAnchor.UpperLeft;
    }

    private static RectTransform GetPanelRect(ChestUI ui)
    {
        var root = new SerializedObject(ui).FindProperty("panelRoot").objectReferenceValue as GameObject;
        return root != null ? root.GetComponent<RectTransform>() : null;
    }

    private static GridLayoutGroup GetSlotsGrid(ChestUI ui)
    {
        var slots = new SerializedObject(ui)
            .FindProperty("slotsContainer").objectReferenceValue as Transform;
        return slots != null ? slots.GetComponent<GridLayoutGroup>() : null;
    }

    // ---------------------------------------------------------------- placeholder art

    /// <summary>
    /// Loads the chest sprite, generating a placeholder the first time. The texture is
    /// only written when missing, so real art dropped in by hand survives a re-run.
    /// </summary>
    private static Sprite EnsureChestSprite()
    {
        Directory.CreateDirectory(GeneratedArtFolder);

        if (!File.Exists(ChestSpritePath))
        {
            File.WriteAllBytes(ChestSpritePath, BuildChestTexture());
            AssetDatabase.ImportAsset(ChestSpritePath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureTextureImporter(ChestSpritePath);
        }

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(ChestSpritePath);
        if (sprite == null)
            Debug.LogWarning($"[Chest] No sprite imported from '{ChestSpritePath}' — prefab will have none.");
        return sprite;
    }

    /// <summary>
    /// Draws a placeholder chest: a dark-framed body, a lid band across the top and a
    /// lit clasp, so it reads as a chest rather than as another crate at a glance. The
    /// grain is seeded from a fixed string, so re-running produces byte-identical files
    /// and does not churn the repository.
    /// </summary>
    private static byte[] BuildChestTexture()
    {
        var random = new DeterministicRandom("chest");
        var texture = new Texture2D(ChestPixels, ChestPixels, TextureFormat.RGBA32, false);

        var frame = new Color(0.10f, 0.08f, 0.06f, 1f);
        var body = new Color(0.42f, 0.28f, 0.16f, 1f);
        var lid = new Color(0.52f, 0.35f, 0.20f, 1f);
        var metal = new Color(0.72f, 0.64f, 0.36f, 1f);

        const int margin = 3;              // transparent border, so chests don't touch when adjacent
        int lidTop = ChestPixels - margin;
        int lidBottom = ChestPixels - margin - 9;
        int claspLeft = ChestPixels / 2 - 2;

        try
        {
            for (int y = 0; y < ChestPixels; y++)
            {
                for (int x = 0; x < ChestPixels; x++)
                {
                    bool inside = x >= margin && x < ChestPixels - margin &&
                                  y >= margin && y < lidTop;
                    if (!inside)
                    {
                        texture.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    bool edge = x == margin || x == ChestPixels - margin - 1 ||
                                y == margin || y == lidTop - 1 ||
                                y == lidBottom;                     // the lid seam
                    bool clasp = x >= claspLeft && x < claspLeft + 4 &&
                                 y >= lidBottom - 3 && y <= lidBottom + 3;

                    Color color = edge ? frame : (y > lidBottom ? lid : body);
                    if (clasp && !edge) color = metal;

                    texture.SetPixel(x, y, Jitter(color, 0.03f, random));
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

    /// <summary>Point filtering and PPU = sprite size, so one chest covers one world unit.</summary>
    private static void ConfigureTextureImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = ChestPixels;
        importer.filterMode = FilterMode.Point;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }

    // ---------------------------------------------------------------- helpers

    private static RectTransform NewUI(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rt = go.GetComponent<RectTransform>();
        if (parent != null) rt.SetParent(parent, false);
        return rt;
    }

    private static void Stretch(RectTransform rt, float padding)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(padding, padding);
        rt.offsetMax = new Vector2(-padding, -padding);
    }

    private static void SetRef(Object component, string field, Object value)
    {
        var so = new SerializedObject(component);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[Chest] Field '{field}' not found on {component.GetType().Name}.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
