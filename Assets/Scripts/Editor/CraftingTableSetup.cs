using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click setup for the crafting table: builds a small FURNITURE_pngy_stol prefab with
/// a trigger "approach" zone and the <see cref="CraftingTable"/> behaviour, assigns the
/// table-gated recipes, drops an instance into the scene next to the player spawn and
/// wires it to the scene's <see cref="CraftingBoxUI"/>. Idempotent: re-running regenerates
/// the prefab in place and keeps an already-placed table where it is.
///
/// Run <b>Tools ▸ Slot Inventory ▸ Build Crafting Box</b> first so the crafting box the
/// table feeds exists, then this.
/// </summary>
public static class CraftingTableSetup
{
    private const string StolSpritePath = "Assets/Art/FURNITURE_pngy_stol.png";
    private const string TablePrefabPath = "Assets/Prefabs/World/CraftingTable.prefab";

    // The plain Table prefab (same sprite) sits at scale 0.25; the crafting table is
    // deliberately smaller.
    private const float TableScale = 0.15f;
    // Extra "in range" margin (world units) around the sprite so the player triggers the
    // table by standing next to it, not only on top of it.
    private const float ApproachPadding = 0.75f;

    // Blocking follows the Barrel prefab (this project's furniture-obstacle): a solid collider
    // on the ObstaclePathOnly layer. Rendering does NOT — the table keeps a plain sprite so it
    // stays visible and is not clipped away by the vision system the way the Barrel is.
    private const string ObstacleLayerName = "ObstaclePathOnly";

    // Table-gated recipes: shown in the crafting box only while the player is at the table.
    // Basic recipes stay on the CraftingBoxUI itself (see CraftingBoxSetup). This list is
    // the default "which recipes are visible on the table" marking; edit the CraftingTable
    // component in the Inspector to change it per table.
    private static readonly string[] TableRecipePaths =
    {
        "Assets/Items/Recipes/Recipe 1 - Sword.asset",
        "Assets/Items/Recipes/Recipe 2 - Mana Potion.asset",
    };

    [MenuItem("Tools/Slot Inventory/Build Crafting Table")]
    public static void BuildAndWire()
    {
        var box = Object.FindFirstObjectByType<CraftingBoxUI>();
        if (box == null)
        {
            EditorUtility.DisplayDialog("Crafting Table",
                "No CraftingBoxUI in the scene. Run 'Tools ▸ Slot Inventory ▸ Build Crafting Box' " +
                "first, then run this again.", "OK");
            return;
        }

        var sprite = AssetDatabase.LoadAllAssetsAtPath(StolSpritePath).OfType<Sprite>().FirstOrDefault();
        if (sprite == null)
            Debug.LogWarning($"[CraftingTable] No sprite at '{StolSpritePath}' — prefab will have none.");

        var recipes = LoadRecipes();
        GameObject prefab = BuildTablePrefab(sprite, recipes);

        var existing = Object.FindFirstObjectByType<CraftingTable>();
        if (existing == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = DefaultTablePosition();
            Undo.RegisterCreatedObjectUndo(instance, "Create Crafting Table");
            existing = instance.GetComponent<CraftingTable>();
        }

        SetRef(existing, "craftingBox", box);

        EditorSceneManager.MarkSceneDirty(existing.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log($"[CraftingTable] Setup complete. {recipes.Count} table-gated recipe(s); " +
                  "walk up to it and open the backpack (Tab) to see them plus the Repair Weapon button.");
        EditorUtility.DisplayDialog("Crafting Table",
            "Setup complete.\n\n" +
            "• CraftingTable prefab (FURNITURE_pngy_stol, smaller than the Table prefab)\n" +
            "• Obstacle like the Barrel: ObstaclePathOnly layer + solid collider (but a plain, always-drawn sprite)\n" +
            "• Scene instance next to the player spawn\n" +
            $"• {recipes.Count} table-gated recipe(s) — edit the CraftingTable's Recipes list to " +
            "mark which recipes this table shows\n" +
            "• Wired to the crafting box (Repair Weapon button appears while in range)\n\n" +
            "In Play mode walk up to the table and press Tab: the extra recipes and the " +
            "Repair Weapon button (placeholder) appear; walk away and they disappear.",
            "OK");

        Selection.activeGameObject = existing.gameObject;
    }

    private static List<RecipeData> LoadRecipes()
    {
        var list = new List<RecipeData>();
        foreach (var path in TableRecipePaths)
        {
            var r = AssetDatabase.LoadAssetAtPath<RecipeData>(path);
            if (r != null) list.Add(r);
            else Debug.LogWarning($"[CraftingTable] Recipe not found at '{path}'.");
        }
        return list;
    }

    /// <summary>
    /// (Re)builds the crafting-table prefab in place (stable GUID keeps scene instances
    /// linked): the stol sprite scaled small, a solid collider on the ObstaclePathOnly layer
    /// that blocks the player like the Barrel, a padded approach trigger driving recipe
    /// visibility, and the <see cref="CraftingTable"/> behaviour carrying the recipes. Unlike
    /// the Barrel it keeps a plain sprite, so the vision system does not clip it away.
    /// </summary>
    private static GameObject BuildTablePrefab(Sprite sprite, List<RecipeData> recipes)
    {
        var root = new GameObject("CraftingTable");
        try
        {
            root.transform.localScale = Vector3.one * TableScale;

            int obstacleLayer = LayerMask.NameToLayer(ObstacleLayerName);
            if (obstacleLayer >= 0) root.layer = obstacleLayer;
            else Debug.LogWarning($"[CraftingTable] Layer '{ObstacleLayerName}' not found — the table won't block movement.");

            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 1; // plain sprite, drawn above the ground like the Table prefab

            // Solid footprint: blocks the player (and enemy pathing where the grid's obstacle
            // mask includes this layer), exactly like the Barrel's collider.
            var solid = root.AddComponent<BoxCollider2D>();
            solid.isTrigger = false;
            if (sprite != null)
            {
                solid.size = (Vector2)sprite.bounds.size; // the table's visual footprint
                solid.offset = sprite.bounds.center;
            }

            // Approach zone: a padded trigger driving recipe visibility. On the same object
            // (like the Barrel is one object); it still fires because this layer collides
            // with the player. Collider sizes are local, so scale the world-space margin.
            var approach = root.AddComponent<BoxCollider2D>();
            approach.isTrigger = true;
            if (sprite != null)
            {
                float margin = ApproachPadding / TableScale;
                approach.size = (Vector2)sprite.bounds.size + new Vector2(margin * 2f, margin * 2f);
                approach.offset = sprite.bounds.center;
            }

            var table = root.AddComponent<CraftingTable>();
            SetRecipeArray(table, "recipes", recipes);

            Directory.CreateDirectory(Path.GetDirectoryName(TablePrefabPath));
            return PrefabUtility.SaveAsPrefabAsset(root, TablePrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>Next to the player spawn (opposite the save station), origin otherwise.</summary>
    private static Vector3 DefaultTablePosition()
    {
        var gameManager = Object.FindFirstObjectByType<GameManager>();
        if (gameManager != null)
        {
            var spawn = new SerializedObject(gameManager)
                .FindProperty("playerSpawnPoint").objectReferenceValue as Transform;
            if (spawn != null)
                return spawn.position + new Vector3(-2.5f, 0f, 0f);
        }
        return Vector3.zero;
    }

    private static void SetRef(Object component, string field, Object value)
    {
        var so = new SerializedObject(component);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[CraftingTable] Field '{field}' not found on {component.GetType().Name}.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetRecipeArray(Object component, string field, List<RecipeData> values)
    {
        var so = new SerializedObject(component);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[CraftingTable] Field '{field}' not found on {component.GetType().Name}.");
            return;
        }
        prop.ClearArray();
        for (int i = 0; i < values.Count; i++)
        {
            prop.InsertArrayElementAtIndex(i);
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
