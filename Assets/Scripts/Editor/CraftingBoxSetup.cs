using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click setup for the slot-based crafting box that sits beside the backpack.
/// Builds the crafting-slot prefab, the crafting panel (a grid of one slot per recipe),
/// bakes the slots into the scene, loads the example recipes, and wires
/// <see cref="CraftingBoxUI"/> to the existing <see cref="SlotInventory"/> /
/// <see cref="BackpackUI"/> / <see cref="HeldItemController"/>. Idempotent.
///
/// Run <b>Tools ▸ Slot Inventory ▸ Build UI &amp; Wire Scene</b> first (it creates the
/// backpack + cursor), then <b>Tools ▸ Slot Inventory ▸ Build Crafting Box</b>.
/// </summary>
public static class CraftingBoxSetup
{
    private const string CraftSlotPrefabPath = "Assets/Prefabs/UI/CraftingSlot.prefab";
    private const string SlotBgSpritePath = "Assets/Art/GDS/Sprites/ui/backgrounds/bg-slot.png";
    private const string WindowBgSpritePath = "Assets/Art/GDS/Sprites/ui/backgrounds/bg-window.png";

    // Grid layout — cells are left-aligned, at most MaxColumns per row, and the panel
    // height grows with the number of rows. These must match the GridLayoutGroup below.
    private const int MaxColumns = 4;
    private const float CellSize = 64f;
    private const float CellSpacing = 6f;
    private const float GridPadding = 4f;   // GridLayoutGroup inner padding (all sides)
    private const float SlotsInset = 8f;     // gap from panel edge to the slots area (L/R/B)
    private const float TitleHeight = 40f;   // reserved strip at the panel top for the title
    private const float FooterHeight = 44f;  // reserved strip at the panel bottom for the repair button
    private const float RepairButtonWidth = 190f;
    private const float RepairButtonHeight = 34f;

    // Recipes always available in the box, even away from a crafting table. The
    // table-gated ones (Bullet, Shell, Ink, Plank) live on the CraftingTable instead —
    // see CraftingTableSetup — so they only show up when the player stands at a table.
    private static readonly string[] RecipePaths =
    {
        "Assets/Items/Recipes/Recipe 5 - Bandage.asset",
        "Assets/Items/Recipes/Recipe 6 - Door Key.asset",
    };

    [MenuItem("Tools/Slot Inventory/Build Crafting Box")]
    public static void BuildAndWire()
    {
        var slotInventory = Object.FindFirstObjectByType<SlotInventory>();
        var backpack = Object.FindFirstObjectByType<BackpackUI>();
        // Resolve the canvas from the backpack, NOT FindFirstObjectByType<Canvas>():
        // the HeldItem cursor has its own nested canvas, and picking that would parent
        // the crafting box under HeldItem.
        var canvas = backpack != null ? backpack.GetComponentInParent<Canvas>() : null;

        if (canvas == null || slotInventory == null || backpack == null)
        {
            EditorUtility.DisplayDialog("Crafting Box",
                "Missing pieces. Run 'Tools ▸ Slot Inventory ▸ Build UI & Wire Scene' first so the " +
                "Canvas, SlotInventory and Backpack exist, then run this.", "OK");
            return;
        }

        var recipes = LoadRecipes();
        GameObject slotPrefab = BuildCraftingSlotPrefab();
        CraftingBoxUI box = EnsureCraftingPanel(canvas, backpack, slotPrefab, recipes.Count);

        // Wire references + recipes.
        SetRef(box, "slotInventory", slotInventory);
        SetRef(box, "backpack", backpack);
        SetRef(box, "slotPrefab", slotPrefab);
        SetRecipeList(box, recipes);
        WireRepairButton(box, slotInventory);

        // Bake one crafting slot per recipe under the box's grid.
        var slotsContainer = new SerializedObject(box).FindProperty("slotsContainer").objectReferenceValue as Transform;
        PopulateSlots(slotsContainer, recipes.Count, slotPrefab);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log($"[CraftingBox] Setup complete. {recipes.Count} recipe slot(s) baked beside the backpack; " +
                  "opens with the backpack (Tab).");
        EditorUtility.DisplayDialog("Crafting Box",
            $"Setup complete.\n\n• CraftingSlot prefab\n• Crafting panel beside the backpack\n• {recipes.Count} recipe slots (baked)\n• Wired to SlotInventory / Backpack\n\n" +
            "Open the backpack (Tab) in Play mode. Uncraftable recipes appear dimmed; " +
            "click a craftable one to consume its ingredients — the result drops into the backpack.", "OK");
    }

    // ---------------------------------------------------------------- recipes

    private static List<RecipeData> LoadRecipes()
    {
        var list = new List<RecipeData>();
        foreach (var path in RecipePaths)
        {
            var r = AssetDatabase.LoadAssetAtPath<RecipeData>(path);
            if (r != null) list.Add(r);
            else Debug.LogWarning($"[CraftingBox] Recipe not found at '{path}'.");
        }
        return list;
    }

    /// <summary>
    /// Wires the repair button's scrap item + inventory so a re-run keeps them authored.
    /// The <see cref="WeaponRepairButton"/> also resolves both at runtime, so this is only a
    /// convenience for inspecting the wiring in the editor.
    /// </summary>
    private static void WireRepairButton(CraftingBoxUI box, SlotInventory slotInventory)
    {
        var buttonRoot = new SerializedObject(box).FindProperty("repairButtonRoot").objectReferenceValue as GameObject;
        var repair = buttonRoot != null ? buttonRoot.GetComponent<WeaponRepairButton>() : null;
        if (repair == null) return;

        var scrap = AssetDatabase.LoadAssetAtPath<ItemData>("Assets/Items/Item 8 - Scrap.asset");
        if (scrap != null) SetRef(repair, "scrapItem", scrap);
        SetRef(repair, "inventory", slotInventory);
    }

    private static void SetRecipeList(CraftingBoxUI box, List<RecipeData> recipes)
    {
        var so = new SerializedObject(box);
        var arr = so.FindProperty("recipes");
        arr.ClearArray();
        for (int i = 0; i < recipes.Count; i++)
        {
            arr.InsertArrayElementAtIndex(i);
            arr.GetArrayElementAtIndex(i).objectReferenceValue = recipes[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------------------------------------------------------- prefab

    /// <summary>
    /// (Re)builds the crafting-slot prefab: a clickable background + an output icon
    /// (raycasts off) + a hidden count badge, with <see cref="CraftingSlotView"/> wired.
    /// Overwrites in place to keep its GUID stable so baked instances stay linked.
    /// </summary>
    private static GameObject BuildCraftingSlotPrefab()
    {
        var root = NewUI("CraftingSlot", null);
        root.sizeDelta = new Vector2(64, 64);
        var bg = root.gameObject.AddComponent<Image>();
        bg.color = new Color(0.13f, 0.13f, 0.13f, 0.9f);
        bg.raycastTarget = true; // must receive clicks
        var bgSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SlotBgSpritePath);
        if (bgSprite != null) { bg.sprite = bgSprite; bg.type = Image.Type.Sliced; }

        var view = root.gameObject.AddComponent<CraftingSlotView>();

        var icon = NewUI("Icon", root);
        Stretch(icon, 7);
        var iconImg = icon.gameObject.AddComponent<Image>();
        iconImg.raycastTarget = false;
        iconImg.preserveAspect = true;

        var count = NewUI("Count", root);
        Stretch(count, 2);
        var countText = count.gameObject.AddComponent<TextMeshProUGUI>();
        countText.raycastTarget = false;
        countText.alignment = TextAlignmentOptions.BottomRight;
        countText.fontSize = 22;
        countText.fontStyle = FontStyles.Bold;
        countText.color = new Color(0.95f, 0.2f, 0.2f, 1f);
        countText.text = string.Empty;
        countText.enabled = false;
        if (TMP_Settings.defaultFontAsset != null) countText.font = TMP_Settings.defaultFontAsset;

        SetRef(view, "iconImage", iconImg);
        SetRef(view, "countLabel", countText);

        Directory.CreateDirectory(Path.GetDirectoryName(CraftSlotPrefabPath));
        var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, CraftSlotPrefabPath);
        Object.DestroyImmediate(root.gameObject);
        return prefab;
    }

    // ---------------------------------------------------------------- scene pieces

    private static CraftingBoxUI EnsureCraftingPanel(Canvas canvas, BackpackUI backpack, GameObject slotPrefab, int recipeCount)
    {
        var existing = Object.FindFirstObjectByType<CraftingBoxUI>();
        if (existing != null)
        {
            // Repair earlier runs that parented the box under the wrong canvas
            // (e.g. the HeldItem cursor canvas) by moving it onto the main canvas.
            if (existing.transform.parent != canvas.transform)
                existing.transform.SetParent(canvas.transform, false);
            ConfigureGrid(GetSlotsGrid(existing));            // refresh columns/alignment
            EnsureRepairFooter(existing);                     // footer strip + repair button
            ApplyPanelLayout(existing, backpack, recipeCount); // keep size/position in sync
            return existing;
        }

        // Always-active root holding the behaviour (mirrors the backpack root).
        var rootRt = NewUI("CraftingBox", canvas.transform);
        Stretch(rootRt, 0);
        var box = rootRt.gameObject.AddComponent<CraftingBoxUI>();

        // Toggled visual window; sized to the backpack panel and placed beside it
        // (see ApplyPanelLayout, called once the references are wired).
        var panelRt = NewUI("Panel", rootRt);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
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
        title.text = "Crafting";
        title.alignment = TextAlignmentOptions.Center;
        title.fontSize = 24;
        title.fontStyle = FontStyles.Bold;
        if (TMP_Settings.defaultFontAsset != null) title.font = TMP_Settings.defaultFontAsset;

        var slotsRt = NewUI("Slots", panelRt);
        slotsRt.anchorMin = Vector2.zero;
        slotsRt.anchorMax = Vector2.one;
        slotsRt.offsetMin = new Vector2(SlotsInset, SlotsInset + FooterHeight); // leave the footer strip
        slotsRt.offsetMax = new Vector2(-SlotsInset, -TitleHeight);
        ConfigureGrid(slotsRt.gameObject.AddComponent<GridLayoutGroup>());

        SetRef(box, "panelRoot", panelRt.gameObject);
        SetRef(box, "slotsContainer", slotsRt);
        SetRef(box, "slotPrefab", slotPrefab);

        EnsureRepairFooter(box);                        // footer strip + repair button
        ApplyPanelLayout(box, backpack, recipeCount);   // size to rows, place beside backpack

        Undo.RegisterCreatedObjectUndo(rootRt.gameObject, "Create Crafting Box");
        return box;
    }

    /// <summary>
    /// Sizes the crafting panel to fit <paramref name="recipeCount"/> left-aligned cells
    /// (at most <see cref="MaxColumns"/> per row, height growing with the row count) and
    /// positions it just to the backpack's right, top-aligned. Applied on both create and
    /// re-run so the box always reflects the current recipe count.
    /// </summary>
    private static void ApplyPanelLayout(CraftingBoxUI box, BackpackUI backpack, int recipeCount)
    {
        var panel = GetPanelRect(box);
        if (panel == null) return;

        int rows = Mathf.Max(1, Mathf.CeilToInt(recipeCount / (float)MaxColumns));

        // A full MaxColumns-wide grid sets the width so rows stay left-aligned within it.
        float gridWidth = MaxColumns * CellSize + (MaxColumns - 1) * CellSpacing + 2 * GridPadding;
        float gridHeight = rows * CellSize + (rows - 1) * CellSpacing + 2 * GridPadding;
        float width = gridWidth + 2 * SlotsInset;
        float height = gridHeight + TitleHeight + SlotsInset + FooterHeight; // title on top, footer + inset below
        var size = new Vector2(width, height);

        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = size;

        const float gap = 24f;
        var bpRt = GetBackpackPanel(backpack);
        if (bpRt == null)
        {
            panel.anchoredPosition = new Vector2(size.x + gap, 0f);
            return;
        }
        // Place to the backpack's right with their top edges aligned.
        float x = bpRt.anchoredPosition.x + bpRt.sizeDelta.x * 0.5f + gap + size.x * 0.5f;
        float backpackTop = bpRt.anchoredPosition.y + bpRt.sizeDelta.y * 0.5f;
        float y = backpackTop - size.y * 0.5f;
        panel.anchoredPosition = new Vector2(x, y);
    }

    /// <summary>
    /// Ensures the panel reserves a bottom footer strip and holds the weapon-repair
    /// button, then wires it to the box. Idempotent: fixes the Slots inset and reuses an
    /// existing button on re-runs. <see cref="CraftingBoxUI"/> shows/hides the button at
    /// runtime depending on whether a crafting table is in range.
    /// </summary>
    private static void EnsureRepairFooter(CraftingBoxUI box)
    {
        var panel = GetPanelRect(box);
        if (panel == null) return;

        // Reserve the footer band inside the Slots rect (idempotent; upgrades older panels).
        var slots = new SerializedObject(box).FindProperty("slotsContainer").objectReferenceValue as RectTransform;
        if (slots != null)
            slots.offsetMin = new Vector2(SlotsInset, SlotsInset + FooterHeight);

        var existing = panel.Find("RepairButton") as RectTransform;
        GameObject buttonGo = existing != null ? existing.gameObject : BuildRepairButton(panel);

        SetRef(box, "repairButtonRoot", buttonGo);
    }

    /// <summary>
    /// Builds the "Repair Weapon" button, centered in the panel's bottom footer strip:
    /// a clickable background + label, carrying the placeholder <see cref="WeaponRepairButton"/>.
    /// </summary>
    private static GameObject BuildRepairButton(RectTransform panel)
    {
        var rt = NewUI("RepairButton", panel);
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.sizeDelta = new Vector2(RepairButtonWidth, RepairButtonHeight);
        // Sit in the footer band, above the SlotsInset bottom margin.
        rt.anchoredPosition = new Vector2(0f, SlotsInset + (FooterHeight - RepairButtonHeight) * 0.5f);

        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.22f, 0.2f, 0.17f, 0.95f);
        var bgSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SlotBgSpritePath);
        if (bgSprite != null) { img.sprite = bgSprite; img.type = Image.Type.Sliced; }

        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        rt.gameObject.AddComponent<WeaponRepairButton>();

        var labelRt = NewUI("Label", rt);
        Stretch(labelRt, 2);
        var text = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = "Repair Weapon";
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 20;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

        return rt.gameObject;
    }

    /// <summary>
    /// Applies the shared grid settings: 64px cells, at most <see cref="MaxColumns"/> per
    /// row, packed to the upper-left. Used on both create and re-run.
    /// </summary>
    private static void ConfigureGrid(GridLayoutGroup grid)
    {
        if (grid == null) return;
        grid.cellSize = new Vector2(CellSize, CellSize);
        grid.spacing = new Vector2(CellSpacing, CellSpacing);
        grid.padding = new RectOffset((int)GridPadding, (int)GridPadding, (int)GridPadding, (int)GridPadding);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = MaxColumns;            // at most 4 cells per row
        grid.childAlignment = TextAnchor.UpperLeft;   // pack cells to the top-left
    }

    private static GridLayoutGroup GetSlotsGrid(CraftingBoxUI box)
    {
        var slots = new SerializedObject(box).FindProperty("slotsContainer").objectReferenceValue as Transform;
        return slots != null ? slots.GetComponent<GridLayoutGroup>() : null;
    }

    private static RectTransform GetPanelRect(CraftingBoxUI box)
    {
        var panel = new SerializedObject(box).FindProperty("panelRoot").objectReferenceValue as GameObject;
        return panel != null ? panel.transform as RectTransform : null;
    }

    private static RectTransform GetBackpackPanel(BackpackUI backpack)
    {
        var panel = new SerializedObject(backpack).FindProperty("panelRoot").objectReferenceValue as GameObject;
        return panel != null ? panel.transform as RectTransform : null;
    }

    /// <summary>
    /// Bakes exactly <paramref name="count"/> crafting-slot instances under
    /// <paramref name="container"/>. Idempotent: clears previously generated ones first.
    /// </summary>
    private static void PopulateSlots(Transform container, int count, GameObject slotPrefab)
    {
        if (container == null || slotPrefab == null) return;

        var stale = new List<GameObject>();
        foreach (Transform child in container)
            if (child.GetComponent<CraftingSlotView>() != null) stale.Add(child.gameObject);
        foreach (var go in stale) Object.DestroyImmediate(go);

        for (int i = 0; i < count; i++)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, container);
            instance.name = $"Recipe {i}";
            Undo.RegisterCreatedObjectUndo(instance, "Create crafting slot");
        }
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
            Debug.LogWarning($"[CraftingBox] Field '{field}' not found on {component.GetType().Name}.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
