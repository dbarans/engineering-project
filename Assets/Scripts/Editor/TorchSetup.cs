using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// One-click setup for the craftable torch (GU-0076): draws its icon with
/// <see cref="TorchArt"/>, creates the <see cref="ItemData"/> that carries the light, creates the
/// wood-and-alcohol <see cref="RecipeData"/> that makes it, adds that recipe to the crafting
/// table, and makes sure the player prefab actually holds a lit torch when one is selected
/// (<see cref="HeldTorch"/> plus a <see cref="FlameFlicker"/> for it to burn by).
///
/// Idempotent: every asset is rewritten in place, so the item's id — which save files store —
/// survives a re-run, and neither the recipe nor the player's components are ever duplicated.
///
/// What makes the item a light source is its <see cref="ItemData.lightRadius"/>, not its name:
/// any item given a radius lights the player's way while it is the selected hotbar item.
/// </summary>
public static class TorchSetup
{
    private const string IconFolder = "Assets/Art/Generated";
    private const string IconPath = IconFolder + "/TorchIcon.png";
    private const string ItemPath = "Assets/Items/Item 18 - Torch.asset";
    private const string RecipePath = "Assets/Items/Recipes/Recipe 7 - Torch.asset";
    private const string TablePrefabPath = "Assets/Prefabs/World/CraftingTable.prefab";
    private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";

    private const string WoodPath = "Assets/Items/Item 3 - Wood.asset";
    private const string AlcoholPath = "Assets/Items/Item 14 - Alcohol.asset";

    /// <summary>
    /// The torch's permanent item id. Fixed rather than generated, so the same torch in a save
    /// file resolves to this asset on every machine and after every re-run of this setup.
    /// </summary>
    private const string TorchItemId = "c4b17f2a90d64e8ab5137e6c92f4a3d1";

    /// <summary>
    /// How far a held torch lights the ground around the player, in world units. Comfortably
    /// short of the player's view radius: a torch is meant to show what is at their feet and
    /// behind them, not to replace looking where they are going.
    /// </summary>
    private const float TorchLightRadius = 4.5f;

    /// <summary>The flame's colour. Warmer and yellower than a lamp's, being an open flame.</summary>
    private static readonly Color TorchLightColor = new Color(1f, 0.74f, 0.34f, 1f);

    /// <summary>How much the flame's brightness wavers, and how fast. Livelier than a lamp's
    /// steady burn — it is the wavering that tells the player the light is on fire.</summary>
    private const float FlickerStrength = 0.18f;
    private const float FlickerSpeed = 3.5f;

    [MenuItem("Tools/Items/Build Torch")]
    public static void Build()
    {
        Sprite icon = BuildIcon();
        ItemData torch = BuildItem(icon);
        RecipeData recipe = BuildRecipe(torch);

        bool onTable = AddToCraftingTable(recipe);
        bool onPlayer = WirePlayerPrefab();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ItemDatabaseBuilder.Rebuild();

        Debug.Log($"[Torch] Built '{ItemPath}' and '{RecipePath}'.", torch);
        EditorUtility.DisplayDialog("Torch",
            "Setup complete.\n\n" +
            $"• Icon drawn to '{IconPath}'\n" +
            $"• Torch item (lights {TorchLightRadius} units while held)\n" +
            "• Recipe: 1 Wood + 1 Alcohol → 1 Torch\n" +
            (onTable ? "• Added to the crafting table's recipes\n" : "• Crafting table not found — add the recipe to one by hand\n") +
            (onPlayer ? "• Player prefab carries HeldTorch + FlameFlicker\n" : "• Player prefab not found — add HeldTorch to it by hand\n") +
            "\nCraft one at the table, put it in the hotbar and select it: the circle of light " +
            "around the player grows and everything in view turns to firelight.",
            "OK");

        Selection.activeObject = torch;
    }

    /// <summary>
    /// Draws the icon and imports it the way the project's other item icons are imported.
    /// Rewritten on every run — the drawing is deterministic, so this only ever produces the
    /// same file again unless <see cref="TorchArt"/> itself changed.
    /// </summary>
    private static Sprite BuildIcon()
    {
        Directory.CreateDirectory(IconFolder);
        File.WriteAllBytes(IconPath, TorchArt.BuildTexture());
        AssetDatabase.ImportAsset(IconPath, ImportAssetOptions.ForceSynchronousImport);

        var importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = TorchArt.PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.alphaIsTransparency = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.SaveAndReimport();
        }

        var icon = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        if (icon == null) Debug.LogError($"[Torch] No sprite imported from '{IconPath}'.");
        return icon;
    }

    /// <summary>
    /// Creates or updates the torch item. The light it casts lives here rather than on a
    /// component, so the same fields describe any future light source — a lantern, a flare —
    /// without a line of new code.
    /// </summary>
    private static ItemData BuildItem(Sprite icon)
    {
        var item = AssetDatabase.LoadAssetAtPath<ItemData>(ItemPath);
        if (item == null)
        {
            item = ScriptableObject.CreateInstance<ItemData>();
            AssetDatabase.CreateAsset(item, ItemPath);
        }

        var serialized = new SerializedObject(item);
        // Only ever set when missing: an id that changed would orphan every torch already in a
        // save file.
        SerializedProperty id = serialized.FindProperty("id");
        if (string.IsNullOrEmpty(id.stringValue)) id.stringValue = TorchItemId;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        item.itemName = "Torch";
        item.icon = icon;
        item.value = 6;
        // One to a slot: a torch is a bulky thing to carry, and stacking them would make the
        // choice of whether to spend a slot on light no choice at all.
        item.maxStack = 1;
        item.weaponType = WeaponType.None;
        item.lightRadius = TorchLightRadius;
        item.lightColor = TorchLightColor;

        EditorUtility.SetDirty(item);
        return item;
    }

    /// <summary>
    /// Creates or updates the recipe: a stick of wood and a splash of alcohol to soak it in.
    /// Both are ordinary floor loot, so the light in a run is limited by what the player finds
    /// rather than by a currency of its own.
    /// </summary>
    private static RecipeData BuildRecipe(ItemData torch)
    {
        var wood = AssetDatabase.LoadAssetAtPath<ItemData>(WoodPath);
        var alcohol = AssetDatabase.LoadAssetAtPath<ItemData>(AlcoholPath);

        if (wood == null || alcohol == null)
        {
            Debug.LogWarning("[Torch] Wood or Alcohol item missing — the recipe was written " +
                             "with an empty ingredient and will not craft until it is filled in.");
        }

        var recipe = AssetDatabase.LoadAssetAtPath<RecipeData>(RecipePath);
        if (recipe == null)
        {
            recipe = ScriptableObject.CreateInstance<RecipeData>();
            AssetDatabase.CreateAsset(recipe, RecipePath);
        }

        recipe.ingredients = new[]
        {
            new RecipeIngredient { item = wood, count = 1 },
            new RecipeIngredient { item = alcohol, count = 1 },
        };
        recipe.outputItem = torch;

        EditorUtility.SetDirty(recipe);
        return recipe;
    }

    /// <summary>
    /// Adds the recipe to the crafting table prefab, so a torch is made at the hub rather than
    /// wherever the player happens to run out of light. That is the point of gating it: torches
    /// are something to walk back for and stock up on, which is what makes going out with two of
    /// them a decision.
    ///
    /// Returns false when there is no table prefab to add it to.
    /// </summary>
    private static bool AddToCraftingTable(RecipeData recipe)
    {
        if (!File.Exists(TablePrefabPath)) return false;

        GameObject contents = PrefabUtility.LoadPrefabContents(TablePrefabPath);
        try
        {
            var table = contents.GetComponent<CraftingTable>();
            if (table == null) return false;

            var serialized = new SerializedObject(table);
            SerializedProperty recipes = serialized.FindProperty("recipes");

            for (int i = 0; i < recipes.arraySize; i++)
            {
                if (recipes.GetArrayElementAtIndex(i).objectReferenceValue == recipe) return true;
            }

            recipes.InsertArrayElementAtIndex(recipes.arraySize);
            recipes.GetArrayElementAtIndex(recipes.arraySize - 1).objectReferenceValue = recipe;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(contents, TablePrefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }

    /// <summary>
    /// Makes sure the player can actually hold a torch: <see cref="HeldTorch"/> to turn the
    /// selected item into light, and a <see cref="FlameFlicker"/> next to it for the flame to
    /// waver by. Both are left alone when already there, so tuned flicker values survive.
    ///
    /// Returns false when there is no player prefab to wire.
    /// </summary>
    private static bool WirePlayerPrefab()
    {
        if (!File.Exists(PlayerPrefabPath)) return false;

        GameObject contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            bool changed = false;

            if (contents.GetComponent<HeldTorch>() == null)
            {
                contents.AddComponent<HeldTorch>();
                changed = true;
            }

            if (contents.GetComponent<FlameFlicker>() == null)
            {
                var flicker = contents.AddComponent<FlameFlicker>();
                var serialized = new SerializedObject(flicker);
                serialized.FindProperty("strength").floatValue = FlickerStrength;
                serialized.FindProperty("speed").floatValue = FlickerSpeed;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }

            if (changed) PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(contents);
        }
    }
}
