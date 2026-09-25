using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click setup for <see cref="DoorBarricade"/>: creates a placeholder Plank item,
/// adds the component to the door prefab with three placeholder stage visuals, points it
/// at the plank item, and upgrades any door already sitting in the open scene.
///
/// The component has to land on the same GameObject as <see cref="SimpleDoor"/>, which in
/// the door prefab is the <c>Door_Visual</c> child rather than the root — so the door is
/// located by searching for the component instead of by hierarchy path, and the tool keeps
/// working if the prefab is ever restructured.
///
/// The prefab is resolved through the <see cref="PrefabRegistry"/> entry the dungeon
/// generator already spawns doors from, falling back to the known asset path, so generated
/// dungeons and hand-placed doors end up with the same setup.
///
/// Idempotent: re-running touches nothing that is already wired — real art dropped in by
/// hand over the placeholders survives, same convention as <see cref="ChestSetup"/>.
/// </summary>
public static class DoorBarricadeSetup
{
    private const string DoorPrefabId = "world.door";
    private const string DoorPrefabPath = "Assets/Prefabs/Door_System.prefab";
    private const string RegistryPath = "Assets/Resources/" + PrefabRegistry.ResourcesPath + ".asset";

    private const string PlankItemPath = "Assets/Items/Item 10 - Plank.asset";
    private const string PlankIconFolder = "Assets/Generation";
    private const string PlankIconPath = PlankIconFolder + "/Plank.png";
    private const int PlankIconPixels = 32;

    private const int StageCount = 3;
    private const string StageVisualPrefix = "Barricade_Stage";

    [MenuItem("Tools/Doors/Add Barricades to Doors")]
    public static void AddBarricades()
    {
        ItemData plank = EnsurePlankItem();

        bool prefabDone = SetUpPrefab(plank, out string prefabPath);
        int sceneDoors = UpgradeSceneDoors(plank);

        AssetDatabase.SaveAssets();
        if (sceneDoors > 0)
        {
            EditorSceneManager.SaveOpenScenes();
        }

        string prefabLine = prefabDone
            ? $"• Door prefab at '{prefabPath}' can be barricaded, with 3 placeholder stage visuals, and saves/restores its barricade"
            : "• Door prefab NOT found — only scene doors were set up";

        Debug.Log($"[Door Barricade] Setup complete. {prefabLine}. " +
                  $"{sceneDoors} door(s) in the open scene upgraded. Plank item: '{PlankItemPath}' (white-square placeholder icon).");

        EditorUtility.DisplayDialog("Door Barricade",
            "Setup complete.\n\n" +
            prefabLine + "\n" +
            $"• {sceneDoors} door(s) in the open scene upgraded\n" +
            $"• Plank item created at '{PlankItemPath}' with a white-square placeholder icon — " +
            "drop real art onto it whenever it's ready\n\n" +
            "In game: stand next to a closed door and press F to nail a stage on, G to pry one off.",
            "OK");
    }

    /// <summary>
    /// Loads the Plank item, creating it with a white-square placeholder icon the first
    /// time. Only ever fills in fields on first creation — a plank item edited by hand
    /// afterwards (real icon, different stack size) is left alone on re-run.
    /// </summary>
    private static ItemData EnsurePlankItem()
    {
        var existing = AssetDatabase.LoadAssetAtPath<ItemData>(PlankItemPath);
        if (existing != null) return existing;

        Sprite icon = EnsurePlankIcon();

        var plank = ScriptableObject.CreateInstance<ItemData>();
        var so = new SerializedObject(plank);
        so.FindProperty("itemName").stringValue = "Plank";
        so.FindProperty("icon").objectReferenceValue = icon;
        so.FindProperty("maxStack").intValue = 20;
        so.FindProperty("value").intValue = 1;
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.CreateAsset(plank, PlankItemPath);
        // CreateAsset fires ItemData.OnValidate, which assigns the stable id — reload so
        // the returned reference reflects it rather than racing the import.
        AssetDatabase.SaveAssets();
        return AssetDatabase.LoadAssetAtPath<ItemData>(PlankItemPath);
    }

    /// <summary>Loads the plank sprite, drawing it the first time it is asked for.</summary>
    private static Sprite EnsurePlankIcon()
    {
        Directory.CreateDirectory(PlankIconFolder);

        if (!File.Exists(PlankIconPath)) WritePlankIcon();

        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(PlankIconPath);
        if (sprite == null)
            Debug.LogWarning($"[Door Barricade] No sprite imported from '{PlankIconPath}' — item will have no icon.");
        return sprite;
    }

    /// <summary>
    /// Redraws the plank over whatever is on disk, for when the drawing changes.
    /// <see cref="EnsurePlankIcon"/> deliberately leaves an existing file alone so real art
    /// survives a re-run, which also means it can never update its own placeholder.
    ///
    /// One sprite does two jobs: the Plank item's inventory icon and all three barricade
    /// stage visuals on the door prefab, which is why redrawing it here re-boards every
    /// barricade in the game without touching a prefab.
    /// </summary>
    public static void RegeneratePlankIcon()
    {
        Directory.CreateDirectory(PlankIconFolder);
        WritePlankIcon();
    }

    private static void WritePlankIcon()
    {
        File.WriteAllBytes(PlankIconPath, BuildPlankTexture(PlankIconPixels));
        AssetDatabase.ImportAsset(PlankIconPath, ImportAssetOptions.ForceSynchronousImport);
        ConfigureIconImporter(PlankIconPath, PlankIconPixels);
    }

    /// <summary>
    /// Draws a sawn board: grain along its length, a darker end grain at each end and two
    /// nail heads driven through it. It replaces a plain white square, which said nothing
    /// about what it was in the inventory and turned every barricade stage into a white
    /// block laid across the door.
    ///
    /// Deliberately square rather than long and thin. The board is used at three different
    /// scales on the door prefab and its parent applies a non-uniform scale of its own, so a
    /// sprite with a strong aspect ratio of its own would come out stretched differently at
    /// every stage; a square one is scaled the same way whichever axis it lands on.
    ///
    /// The grain is seeded from a fixed string, so re-running produces byte-identical files
    /// and does not churn the repository.
    /// </summary>
    private static byte[] BuildPlankTexture(int size)
    {
        var random = new DeterministicRandom("plank");
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

        try
        {
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                    texture.SetPixel(x, y, PlankPixel(x, y, size, random));
            }

            texture.Apply();
            return texture.EncodeToPNG();
        }
        finally
        {
            Object.DestroyImmediate(texture);
        }
    }

    /// <summary>One pixel of the plank sprite. Pure, so the drawing can be judged on its own.</summary>
    private static Color PlankPixel(int x, int y, int size, DeterministicRandom random)
    {
        var edge = new Color32(0x24, 0x1B, 0x11, 0xFF);
        var endGrain = new Color32(0x3B, 0x2C, 0x1C, 0xFF);
        var wood = new Color32(0x6B, 0x51, 0x33, 0xFF);
        var woodDark = new Color32(0x50, 0x3C, 0x25, 0xFF);
        var nail = new Color32(0x3A, 0x40, 0x3D, 0xFF);
        var nailLit = new Color32(0x77, 0x80, 0x7C, 0xFF);

        // A margin all round, so two boards crossing on a barricade read as two boards.
        int margin = size / 16;
        int endDepth = size / 8;

        if (x < margin || x >= size - margin || y < margin || y >= size - margin) return Color.clear;

        Color color;
        if (x == margin || x == size - margin - 1 || y == margin || y == size - margin - 1)
        {
            color = edge;
        }
        else if (x < margin + endDepth || x >= size - margin - endDepth)
        {
            // Sawn ends, darker than the face: end grain always is, and it is what stops the
            // board reading as a painted rectangle.
            color = endGrain;
        }
        else
        {
            // Grain running the length of the board, wandering a pixel so the lines do not
            // read as ruling.
            int band = (y + x / 7) % 5;
            color = band == 0 ? woodDark : wood;
        }

        // Two nails, driven at the quarter points where a board would be fixed.
        int nailX = size / 4;
        bool onNail = (Mathf.Abs(x - nailX) <= 1 || Mathf.Abs(x - (size - nailX)) <= 1) &&
                      Mathf.Abs(y - size / 2) <= 1;
        if (onNail) color = x % 2 == 0 && y == size / 2 ? nailLit : nail;

        return Jitter(color, 0.03f, random);
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

    private static void ConfigureIconImporter(string path, int pixels)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = pixels;
        importer.filterMode = FilterMode.Point;
        importer.alphaIsTransparency = true;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();
    }

    /// <summary>
    /// Adds the barricade to the door prefab asset. Returns false when no door prefab could
    /// be resolved, which is not fatal — scene doors are still worth upgrading on their own.
    /// </summary>
    private static bool SetUpPrefab(ItemData plank, out string prefabPath)
    {
        prefabPath = ResolveDoorPrefabPath();
        if (string.IsNullOrEmpty(prefabPath)) return false;

        // LoadPrefabContents rather than editing the asset through a loaded GameObject:
        // it opens an isolated copy, so a half-finished edit can be thrown away without
        // touching the asset, and instances in open scenes update once it is saved.
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null) return false;

        try
        {
            var door = root.GetComponentInChildren<SimpleDoor>(true);
            if (door == null)
            {
                Debug.LogWarning(
                    $"[Door Barricade] '{prefabPath}' holds no SimpleDoor — nothing to barricade.");
                return false;
            }

            var barricade = EditorSetupUtility.EnsureComponent<DoorBarricade>(door.gameObject);
            AssignPlankItem(barricade, plank, overwrite: false);
            EnsureStageVisuals(barricade);
            EnsureSaveSupport(root);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Gives doors in the open scene a barricade. Prefab instances inherit theirs from the
    /// asset above, so this mostly catches doors that were placed and unpacked by hand.
    /// </summary>
    private static int UpgradeSceneDoors(ItemData plank)
    {
        var doors = Object.FindObjectsByType<SimpleDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int upgraded = 0;

        foreach (var door in doors)
        {
            if (door == null) continue;
            if (door.GetComponent<DoorBarricade>() != null) continue;

            var barricade = Undo.AddComponent<DoorBarricade>(door.gameObject);
            AssignPlankItem(barricade, plank, overwrite: false);
            EnsureStageVisuals(barricade);
            EnsureSaveSupport(door.transform.root.gameObject);

            EditorUtility.SetDirty(door.gameObject);
            EditorUtility.SetDirty(door.transform.root.gameObject);
            EditorSceneManager.MarkSceneDirty(door.gameObject.scene);
            upgraded++;
        }

        return upgraded;
    }

    /// <summary>
    /// Writes the plank reference through SerializedObject — the field is private and
    /// <c>[SerializeField]</c>, so there is no public setter to go through, and this is
    /// also what makes the change survive as a proper serialized edit.
    /// </summary>
    private static void AssignPlankItem(DoorBarricade barricade, ItemData plank, bool overwrite)
    {
        if (barricade == null) return;

        var so = new SerializedObject(barricade);
        SerializedProperty property = so.FindProperty("plankItem");
        if (property == null) return;

        // Without this an author who deliberately switched a door over to Scrap would have
        // it silently reset to Wood every time the tool is re-run.
        if (!overwrite && property.objectReferenceValue != null) return;

        property.objectReferenceValue = plank;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Builds three placeholder stage children (growing white squares, stacked in front of
    /// the door) and wires them into <see cref="DoorBarricade.stageVisuals"/> — otherwise
    /// the mechanic is invisible in play, since the array is empty by default. Only creates
    /// what is missing: a door whose Stage Visuals were already hand-authored with real art
    /// is left untouched, and reruns fill in nothing but empty array slots.
    /// </summary>
    private static void EnsureStageVisuals(DoorBarricade barricade)
    {
        if (barricade == null) return;

        var so = new SerializedObject(barricade);
        SerializedProperty array = so.FindProperty("stageVisuals");
        if (array == null) return;

        if (array.arraySize != StageCount)
        {
            array.arraySize = StageCount;
        }

        Sprite icon = AssetDatabase.LoadAssetAtPath<Sprite>(PlankIconPath);
        Transform doorTransform = barricade.transform;
        var doorRenderer = barricade.GetComponent<SpriteRenderer>();
        int sortingOrder = (doorRenderer != null ? doorRenderer.sortingOrder : 0) + 1;

        bool changed = false;
        for (int i = 0; i < StageCount; i++)
        {
            SerializedProperty slot = array.GetArrayElementAtIndex(i);
            if (slot.objectReferenceValue != null) continue; // real art already placed

            string childName = $"{StageVisualPrefix}{i + 1}";
            Transform existingChild = doorTransform.Find(childName);
            GameObject stage = existingChild != null ? existingChild.gameObject : new GameObject(childName);
            stage.transform.SetParent(doorTransform, worldPositionStays: false);

            // Each stage a little larger than the last so three stacked squares read as
            // "more barricaded" at a glance, not as three identical decals.
            float scale = 0.35f + i * 0.18f;
            stage.transform.localPosition = Vector3.zero;
            stage.transform.localScale = new Vector3(scale, scale, 1f);

            var renderer = EditorSetupUtility.EnsureComponent<SpriteRenderer>(stage);
            renderer.sprite = icon;
            renderer.color = Color.white; // plain placeholder — real art replaces this later
            renderer.sortingOrder = sortingOrder + i;

            stage.SetActive(false); // DoorBarricade.RefreshVisuals turns these on per stage

            slot.objectReferenceValue = stage;
            changed = true;
        }

        if (changed)
        {
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// Adds <see cref="SaveableEntity"/> and <see cref="DoorBarricadeSaveable"/> to the
    /// door's root GameObject, so a barricade built during play survives a save/load.
    /// Both <see cref="PrefabRegistry.Spawn"/> and <see cref="SaveableEntity"/>'s own
    /// dispatch look for these on the same GameObject via <c>GetComponent</c> (not
    /// <c>GetComponentInChildren</c>), which is why this targets the door root
    /// (<c>Door_System</c>) rather than the <c>Door_Visual</c> child the barricade and
    /// <see cref="SimpleDoor"/> itself live on.
    /// </summary>
    private static void EnsureSaveSupport(GameObject doorRoot)
    {
        if (doorRoot == null) return;

        EditorSetupUtility.EnsureComponent<SaveableEntity>(doorRoot);
        EditorSetupUtility.EnsureComponent<DoorBarricadeSaveable>(doorRoot);
    }

    /// <summary>
    /// Prefers whatever the generator actually spawns (the registry entry) over the
    /// hardcoded path, so a project that moved its door prefab still gets set up.
    /// </summary>
    private static string ResolveDoorPrefabPath()
    {
        var registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(RegistryPath);
        if (registry != null)
        {
            GameObject fromRegistry = registry.Resolve(DoorPrefabId);
            if (fromRegistry != null)
            {
                string path = AssetDatabase.GetAssetPath(fromRegistry);
                if (!string.IsNullOrEmpty(path)) return path;
            }
        }

        return AssetDatabase.LoadAssetAtPath<GameObject>(DoorPrefabPath) != null ? DoorPrefabPath : null;
    }
}
