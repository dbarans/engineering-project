using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click setup for the broken-glass surface (GU-0073): builds the
/// <c>BrokenGlass</c> prefab (sprite + trigger box + <see cref="NoisySurface"/>), registers
/// it in the <see cref="PrefabRegistry"/> as <c>prop.brokenglass</c> so the generator can
/// spawn it, and adds it to the prop table in <c>RoomContentSettings</c> so it actually
/// turns up in a dungeon.
///
/// Idempotent: the prefab is rebuilt in place (its GUID survives, so scene instances stay
/// linked), and both the registry entry and the prop entry are repointed rather than
/// duplicated — the id may already appear in save files.
///
/// The prefab uses the placeholder prop sprite, tinted, because the project has no glass
/// art yet. Dropping a real sprite onto the prefab later needs no code change; only the
/// tint here would go.
/// </summary>
public static class BrokenGlassSetup
{
    private const string PrefabPath = "Assets/Prefabs/World/BrokenGlass.prefab";
    private const string SpritePath = "Assets/Art/PlaceholderProp.png";
    private const string RegistryPath = "Assets/Resources/" + PrefabRegistry.ResourcesPath + ".asset";
    private const string ContentSettingsPath = "Assets/Generation/RoomContentSettings.asset";
    private const string NoiseSettingsPath = "Assets/Settings/NoiseSettings.asset";

    private const string GlassPrefabId = "prop.brokenglass";

    /// <summary>Half a floor tile of shards, so a patch is stepped over rather than around.</summary>
    private const float PatchSize = 1.2f;

    /// <summary>How often a room's prop budget lands on glass, relative to barrels and tables.</summary>
    private const float GlassWeight = 0.25f;

    [MenuItem("Tools/World/Build Broken Glass Prefab")]
    public static void Build()
    {
        GameObject prefab = BuildPrefab();
        Register(prefab);
        AddToPropTable();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[BrokenGlass] Built '{PrefabPath}', registered as '{GlassPrefabId}' and " +
                  "added to the prop table.");
        EditorUtility.DisplayDialog("Broken Glass",
            "Setup complete.\n\n" +
            "• BrokenGlass prefab (trigger patch, NoisySurface)\n" +
            $"• Registered as '{GlassPrefabId}'\n" +
            "• Added to RoomContentSettings ▸ Props\n\n" +
            "Walking over a patch emits noise an enemy can hear, scaled by movement mode — " +
            "sneaking is quiet but, unlike on ordinary floor, no longer silent. Regenerate " +
            "the dungeon to see patches placed.",
            "OK");

        Selection.activeObject = prefab;
    }

    private static GameObject BuildPrefab()
    {
        var sprite = AssetDatabase.LoadAllAssetsAtPath(SpritePath).OfType<Sprite>().FirstOrDefault();
        if (sprite == null)
            Debug.LogWarning($"[BrokenGlass] No sprite at '{SpritePath}' — the patch will be invisible.");

        var root = new GameObject("BrokenGlass");
        try
        {
            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            // Pale and translucent so the placeholder reads as debris on the floor rather
            // than as another solid prop the player might try to walk around.
            renderer.color = new Color(0.72f, 0.86f, 0.9f, 0.65f);
            // Behind everything that stands on the floor: glass is part of the floor.
            renderer.sortingOrder = -1;

            var box = root.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(PatchSize, PatchSize);

            var surface = root.AddComponent<NoisySurface>();
            AssignBaseNoiseSettings(surface);

            Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>
    /// Points the surface at the project's shared noise ranges. Without this the surface's
    /// multipliers have nothing to multiply, so it would fall back to silence — the field is
    /// serialized so a hand-authored patch can still override it, but the generated prefab
    /// should never ship unassigned.
    /// </summary>
    private static void AssignBaseNoiseSettings(NoisySurface surface)
    {
        var settings = AssetDatabase.LoadAssetAtPath<NoiseSettings>(NoiseSettingsPath);
        if (settings == null)
        {
            Debug.LogWarning($"[BrokenGlass] No NoiseSettings at '{NoiseSettingsPath}' — " +
                              "glass will log a warning and stay silent until one is assigned by hand.");
            return;
        }

        var serialized = new SerializedObject(surface);
        serialized.FindProperty("baseNoiseSettings").objectReferenceValue = settings;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>
    /// Binds the prefab to its permanent id. An existing entry is repointed rather than
    /// duplicated, because that id may already appear in save files.
    /// </summary>
    private static void Register(GameObject prefab)
    {
        if (prefab == null) return;

        var registry = AssetDatabase.LoadAssetAtPath<PrefabRegistry>(RegistryPath);
        if (registry == null)
        {
            Debug.LogWarning(
                $"[BrokenGlass] No PrefabRegistry at '{RegistryPath}' — '{GlassPrefabId}' not " +
                "registered, so the generator cannot spawn glass. Run " +
                "Tools ▸ Dungeon ▸ Build Dungeon Scene first.");
            return;
        }

        var serialized = new SerializedObject(registry);
        SerializedProperty entries = serialized.FindProperty("entries");

        SerializedProperty entry = FindById(entries, "id", GlassPrefabId) ?? Append(entries);
        entry.FindPropertyRelative("id").stringValue = GlassPrefabId;
        entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(registry);
    }

    /// <summary>
    /// Puts glass into the room prop table. Glass is scatter, not cover, so it is not
    /// solitary — a cluster of patches is exactly the debris field this wants to be.
    /// An existing entry keeps its tuned weight; only a missing one is added.
    /// </summary>
    private static void AddToPropTable()
    {
        var content = AssetDatabase.LoadAssetAtPath<RoomContentSettings>(ContentSettingsPath);
        if (content == null)
        {
            Debug.LogWarning(
                $"[BrokenGlass] No RoomContentSettings at '{ContentSettingsPath}' — glass is " +
                "registered but nothing will spawn it. Add it to a prop table by hand.");
            return;
        }

        var serialized = new SerializedObject(content);
        SerializedProperty props = serialized.FindProperty("props");

        if (FindById(props, "prefabId", GlassPrefabId) != null) return;

        SerializedProperty entry = Append(props);
        entry.FindPropertyRelative("prefabId").stringValue = GlassPrefabId;
        entry.FindPropertyRelative("weight").floatValue = GlassWeight;
        entry.FindPropertyRelative("minDepth").intValue = 0;
        entry.FindPropertyRelative("solitary").boolValue = false;

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(content);
    }

    /// <summary>The array element whose <paramref name="idField"/> equals <paramref name="id"/>, or null.</summary>
    private static SerializedProperty FindById(SerializedProperty array, string idField, string id)
    {
        for (int i = 0; i < array.arraySize; i++)
        {
            SerializedProperty element = array.GetArrayElementAtIndex(i);
            if (element.FindPropertyRelative(idField).stringValue == id) return element;
        }

        return null;
    }

    private static SerializedProperty Append(SerializedProperty array)
    {
        int index = array.arraySize;
        array.InsertArrayElementAtIndex(index);
        return array.GetArrayElementAtIndex(index);
    }
}
