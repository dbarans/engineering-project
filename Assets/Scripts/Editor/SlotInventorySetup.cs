using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One-click setup for the slot-based hotbar &amp; backpack. Builds the slot prefab,
/// the backpack panel, and the held-item cursor in the open scene, then wires
/// <see cref="SlotInventory"/> / <see cref="HotbarUI"/> / <see cref="BackpackUI"/> /
/// <see cref="HeldItemController"/>. Idempotent: re-running reuses what already exists.
///
/// Run via <b>Tools ▸ Slot Inventory ▸ Build UI &amp; Wire Scene</b>.
/// </summary>
public static class SlotInventorySetup
{
    private const string SlotPrefabPath = "Assets/Prefabs/UI/InventorySlot.prefab";
    private const string ItemPrefabPath = "Assets/Prefabs/UI/InventoryItem.prefab";
    private const string SlotBgSpritePath = "Assets/Art/GDS/Sprites/ui/backgrounds/bg-slot.png";
    private const string WindowBgSpritePath = "Assets/Art/GDS/Sprites/ui/backgrounds/bg-window.png";

    private const string Item1Path = "Assets/Items/Item 1 - Sword.asset";
    private const string Item2Path = "Assets/Items/Item 2 - Mana Potion.asset";
    private const string Item3Path = "Assets/Items/Item 3 - Wood.asset";
    private const string Item4Path = "Assets/Items/Item 4 - Axe.asset";
    private const string Item5Path = "Assets/Items/Item 5 - Pistol.asset";

    [MenuItem("Tools/Slot Inventory/Build UI & Wire Scene")]
    public static void BuildAndWire()
    {
        var canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Slot Inventory",
                "No Canvas found in the open scene. Open the scene with your UI Canvas and try again.", "OK");
            return;
        }

        GameObject itemPrefab = BuildItemPrefab();
        GameObject slotPrefab = BuildSlotPrefab(itemPrefab);
        EnsureEventSystem();
        EnsureRaycaster(canvas);

        SlotInventory slotInventory = EnsureSlotInventory(out GameObject ownerForDebug);
        HeldItemController held = EnsureHeldItem(canvas);
        BackpackUI backpack = EnsureBackpackPanel(canvas, slotPrefab);

        // Wire backpack.
        SetRef(backpack, "slotInventory", slotInventory);
        SetRef(backpack, "heldItem", held);
        SetRef(backpack, "slotPrefab", slotPrefab);

        // Wire hotbar (keeps its existing slotsContainer).
        var hotbar = Object.FindFirstObjectByType<HotbarUI>();
        Transform hotbarContainer = null;
        if (hotbar != null)
        {
            SetRef(hotbar, "slotPrefab", slotPrefab);
            SetRef(hotbar, "slotInventory", slotInventory);
            SetRef(hotbar, "heldItem", held);
            hotbarContainer = new SerializedObject(hotbar).FindProperty("slotsContainer").objectReferenceValue as Transform;
            if (hotbarContainer == null)
                Debug.LogWarning("[SlotInventory] HotbarUI.slotsContainer is empty — assign the hotbar slots parent transform manually.");
        }
        else
        {
            Debug.LogWarning("[SlotInventory] No HotbarUI found in scene; only the backpack was wired.");
        }

        // Bake the slot instances into the scene at edit time so they exist before Play.
        ReadSlotCounts(slotInventory, out int hotbarCount, out int backpackCount);
        PopulateSlots(hotbarContainer, hotbarCount, slotPrefab);
        var backpackContainer = new SerializedObject(backpack).FindProperty("slotsContainer").objectReferenceValue as Transform;
        PopulateSlots(backpackContainer, backpackCount, slotPrefab);

        SeedDebugItems(ownerForDebug, slotInventory);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log("[SlotInventory] Setup complete. Hotbar + backpack slots baked into the scene; " +
                  "backpack opens on Tab.");
        EditorUtility.DisplayDialog("Slot Inventory",
            "Setup complete.\n\nCreated/last wired:\n• InventoryItem prefab (item entity)\n• InventorySlot prefab (slot frame)\n• Hotbar + backpack slots (baked into the scene)\n• Backpack panel (4×5)\n• HeldItem cursor\n• SlotInventory on the player\n\n" +
            "Press Tab in Play mode to open the backpack.", "OK");
    }

    // ---------------------------------------------------------------- prefabs

    /// <summary>
    /// (Re)builds the item-entity prefab: one icon + a red count badge, both with
    /// raycasts off so clicks pass through to the slot frame. Overwrites in place to
    /// keep its GUID (and existing references) stable.
    /// </summary>
    private static GameObject BuildItemPrefab()
    {
        var root = NewUI("InventoryItem", null);
        root.sizeDelta = new Vector2(64, 64);

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
        if (TMP_Settings.defaultFontAsset != null) countText.font = TMP_Settings.defaultFontAsset;

        var item = root.gameObject.AddComponent<InventoryItem>();
        SetRef(item, "iconImage", iconImg);
        SetRef(item, "countLabel", countText);

        Directory.CreateDirectory(Path.GetDirectoryName(ItemPrefabPath));
        var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, ItemPrefabPath);
        Object.DestroyImmediate(root.gameObject);
        return prefab;
    }

    /// <summary>
    /// (Re)builds the slot-frame prefab: a clickable background + selection highlight,
    /// wired to spawn the item entity from <paramref name="itemPrefab"/>. Overwrites in
    /// place to keep its GUID stable so baked slot instances stay linked.
    /// </summary>
    private static GameObject BuildSlotPrefab(GameObject itemPrefab)
    {
        var root = NewUI("InventorySlot", null);
        root.sizeDelta = new Vector2(64, 64);
        var bg = root.gameObject.AddComponent<Image>();
        bg.color = new Color(0.13f, 0.13f, 0.13f, 0.9f);
        bg.raycastTarget = true; // must receive clicks
        var bgSprite = AssetDatabase.LoadAssetAtPath<Sprite>(SlotBgSpritePath);
        if (bgSprite != null) { bg.sprite = bgSprite; bg.type = Image.Type.Sliced; }

        var slotView = root.gameObject.AddComponent<SlotView>();

        var highlight = NewUI("SelectionHighlight", root);
        Stretch(highlight, 0);
        var hlImg = highlight.gameObject.AddComponent<Image>();
        hlImg.raycastTarget = false;
        hlImg.color = new Color(1f, 0.85f, 0f, 0.45f);
        hlImg.enabled = false;

        SetRef(slotView, "itemPrefab", itemPrefab.GetComponent<InventoryItem>());
        SetRef(slotView, "itemAnchor", root);
        SetRef(slotView, "selectionHighlight", hlImg);

        Directory.CreateDirectory(Path.GetDirectoryName(SlotPrefabPath));
        var prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, SlotPrefabPath);
        Object.DestroyImmediate(root.gameObject);
        return prefab;
    }

    // ---------------------------------------------------------------- scene pieces

    private static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem));
        // Project uses the new Input System for UI input.
        go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
    }

    private static void EnsureRaycaster(Canvas canvas)
    {
        if (canvas.GetComponent<GraphicRaycaster>() == null)
            Undo.AddComponent<GraphicRaycaster>(canvas.gameObject);
    }

    private static SlotInventory EnsureSlotInventory(out GameObject owner)
    {
        var existing = Object.FindFirstObjectByType<SlotInventory>();
        if (existing != null) { owner = existing.gameObject; return existing; }

        var player = Object.FindFirstObjectByType<PlayerInventory>();
        owner = player != null ? player.gameObject
                               : Object.FindFirstObjectByType<PlayerInputHandler>()?.gameObject;
        if (owner == null)
        {
            owner = new GameObject("SlotInventory");
            Undo.RegisterCreatedObjectUndo(owner, "Create SlotInventory host");
        }
        return Undo.AddComponent<SlotInventory>(owner);
    }

    private static HeldItemController EnsureHeldItem(Canvas canvas)
    {
        var existing = Object.FindFirstObjectByType<HeldItemController>();
        if (existing != null)
        {
            ConfigureTopMostCanvas(existing.gameObject);
            existing.transform.SetAsLastSibling();
            return existing;
        }

        var root = NewUI("HeldItem", canvas.transform);
        root.sizeDelta = new Vector2(64, 64);

        var held = root.gameObject.AddComponent<HeldItemController>();
        SetRef(held, "followTarget", root);

        ConfigureTopMostCanvas(root.gameObject); // render above the HUD, never clipped
        root.SetAsLastSibling();
        Undo.RegisterCreatedObjectUndo(root.gameObject, "Create HeldItem");
        return held;
    }

    /// <summary>Gives a UI object its own high-sorting canvas so it draws above all other UI.</summary>
    private static void ConfigureTopMostCanvas(GameObject go)
    {
        var c = go.GetComponent<Canvas>();
        if (c == null) c = go.AddComponent<Canvas>();
        c.overrideSorting = true;
        c.sortingOrder = 1000;
    }

    private static BackpackUI EnsureBackpackPanel(Canvas canvas, GameObject slotPrefab)
    {
        var existing = Object.FindFirstObjectByType<BackpackUI>();
        if (existing != null) return existing;

        // Always-active root that holds the BackpackUI behaviour.
        var rootRt = NewUI("Backpack", canvas.transform);
        Stretch(rootRt, 0);
        var backpack = rootRt.gameObject.AddComponent<BackpackUI>();

        // The toggled visual window.
        var panelRt = NewUI("Panel", rootRt);
        panelRt.anchorMin = panelRt.anchorMax = new Vector2(0.5f, 0.5f);
        panelRt.pivot = new Vector2(0.5f, 0.5f);
        panelRt.sizeDelta = new Vector2(300, 430);
        var panelImg = panelRt.gameObject.AddComponent<Image>();
        panelImg.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);
        var windowSprite = AssetDatabase.LoadAssetAtPath<Sprite>(WindowBgSpritePath);
        if (windowSprite != null) { panelImg.sprite = windowSprite; panelImg.type = Image.Type.Sliced; }

        var titleRt = NewUI("Title", panelRt);
        titleRt.anchorMin = new Vector2(0, 1);
        titleRt.anchorMax = new Vector2(1, 1);
        titleRt.pivot = new Vector2(0.5f, 1);
        titleRt.sizeDelta = new Vector2(0, 40);
        titleRt.anchoredPosition = new Vector2(0, -4);
        var title = titleRt.gameObject.AddComponent<TextMeshProUGUI>();
        title.text = "Backpack";
        title.alignment = TextAlignmentOptions.Center;
        title.fontSize = 26;
        title.fontStyle = FontStyles.Bold;
        if (TMP_Settings.defaultFontAsset != null) title.font = TMP_Settings.defaultFontAsset;

        var slotsRt = NewUI("Slots", panelRt);
        slotsRt.anchorMin = Vector2.zero;
        slotsRt.anchorMax = Vector2.one;
        slotsRt.offsetMin = new Vector2(8, 8);
        slotsRt.offsetMax = new Vector2(-8, -44);
        var grid = slotsRt.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(64, 64);
        grid.spacing = new Vector2(6, 6);
        grid.padding = new RectOffset(4, 4, 4, 4);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 4;
        grid.childAlignment = TextAnchor.UpperCenter;

        SetRef(backpack, "panelRoot", panelRt.gameObject);
        SetRef(backpack, "slotsContainer", slotsRt);
        SetRef(backpack, "slotPrefab", slotPrefab);

        Undo.RegisterCreatedObjectUndo(rootRt.gameObject, "Create Backpack panel");
        return backpack;
    }

    private static void ReadSlotCounts(SlotInventory slotInventory, out int hotbar, out int backpack)
    {
        var so = new SerializedObject(slotInventory);
        hotbar = so.FindProperty("hotbarSize").intValue;
        backpack = so.FindProperty("backpackColumns").intValue * so.FindProperty("backpackRows").intValue;
    }

    /// <summary>
    /// Bakes exactly <paramref name="count"/> slot-prefab instances under
    /// <paramref name="container"/> so the slots exist in the scene before Play.
    /// Idempotent: clears any previously generated slot views first.
    /// </summary>
    private static void PopulateSlots(Transform container, int count, GameObject slotPrefab)
    {
        if (container == null || slotPrefab == null || count <= 0) return;

        var stale = new System.Collections.Generic.List<GameObject>();
        foreach (Transform child in container)
            if (child.GetComponent<SlotView>() != null) stale.Add(child.gameObject);
        foreach (var go in stale) Object.DestroyImmediate(go);

        for (int i = 0; i < count; i++)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(slotPrefab, container);
            instance.name = $"Slot {i}";
            Undo.RegisterCreatedObjectUndo(instance, "Create inventory slot");
        }
    }

    private static void SeedDebugItems(GameObject owner, SlotInventory slotInventory)
    {
        if (owner == null) return;
        var sword = AssetDatabase.LoadAssetAtPath<ItemData>(Item1Path);
        var mana = AssetDatabase.LoadAssetAtPath<ItemData>(Item2Path);
        var wood = AssetDatabase.LoadAssetAtPath<ItemData>(Item3Path);
        var axe = AssetDatabase.LoadAssetAtPath<ItemData>(Item4Path);
        var pistol = AssetDatabase.LoadAssetAtPath<ItemData>(Item5Path);
        if (sword == null && mana == null && wood == null && axe == null && pistol == null) return;

        var fill = owner.GetComponent<SlotInventoryDebugFill>() ?? Undo.AddComponent<SlotInventoryDebugFill>(owner);
        var so = new SerializedObject(fill);
        so.FindProperty("slotInventory").objectReferenceValue = slotInventory;
        var entries = so.FindProperty("entries");
        entries.ClearArray();
        AddEntry(entries, sword, 1, false);
        AddEntry(entries, mana, 12, false);
        AddEntry(entries, axe, 1, false);
        AddEntry(entries, pistol, 1, false);
        AddEntry(entries, wood, 64, true);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    // ---------------------------------------------------------------- helpers

    private static void AddEntry(SerializedProperty array, ItemData item, int count, bool backpack)
    {
        if (item == null) return;
        int i = array.arraySize;
        array.InsertArrayElementAtIndex(i);
        var el = array.GetArrayElementAtIndex(i);
        el.FindPropertyRelative("item").objectReferenceValue = item;
        el.FindPropertyRelative("count").intValue = count;
        el.FindPropertyRelative("toBackpack").boolValue = backpack;
    }

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
            Debug.LogWarning($"[SlotInventory] Field '{field}' not found on {component.GetType().Name}.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
