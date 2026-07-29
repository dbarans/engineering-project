using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click setup for the Phase 5 save/load flow (docs/save-system-plan.md):
/// builds the typewriter <see cref="SaveStation"/> prefab (sprite + trigger collider,
/// "Save game" on cursor hover, opened by left-clicking it), drops an instance into the scene next to the player
/// spawn, generates the full-screen <see cref="SaveLoadUI"/> (5 slots, Save/Load tabs,
/// overwrite confirmation) plus the bottom-right "Game saved" <see cref="ToastUI"/> on
/// the main canvas and wires everything together. Idempotent: re-running regenerates
/// the UI in place and keeps an already-placed station where it is.
///
/// Requires the canvas from <b>Tools ▸ Slot Inventory ▸ Build UI &amp; Wire Scene</b>.
/// </summary>
public static class SaveStationSetup
{
    private const string TypewriterSpritePath = "Assets/Art/FURNITURE_pngy_maszyna.png";
    private const string StationPrefabPath = "Assets/Prefabs/World/SaveStation.prefab";
    private const string SlotBgSpritePath = "Assets/Art/GDS/Sprites/ui/backgrounds/bg-slot.png";
    private const string WindowBgSpritePath = "Assets/Art/GDS/Sprites/ui/backgrounds/bg-window.png";
    private const string InkItemPath = "Assets/Items/Item 6 - Ink.asset";  // ink = the save cost

    private const int InteractableLayer = 10;      // "Interactable" — semantic grouping of usable props
    private const float StationScale = 0.35f;      // 362px sprite @100ppu → ~1.3 world units

    private const float SlotWidth = 640f;
    private const float SlotHeight = 84f;
    private const float SlotSpacing = 12f;

    [MenuItem("Tools/Save System/Build Save Station & UI")]
    public static void BuildAndWire()
    {
        var backpack = Object.FindFirstObjectByType<BackpackUI>();
        // Canvas via the backpack, not FindFirstObjectByType<Canvas>: the HeldItem
        // cursor has its own nested canvas (same pitfall as CraftingBoxSetup).
        var canvas = backpack != null ? backpack.GetComponentInParent<Canvas>() : null;
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Save Station",
                "No main canvas found. Run 'Tools ▸ Slot Inventory ▸ Build UI & Wire Scene' " +
                "first, then run this again.", "OK");
            return;
        }

        SaveLoadUI ui = BuildSaveLoadUI(canvas);
        ToastUI toast = BuildToast(canvas);
        SetRef(ui, "toast", toast);
        SaveStation station = EnsureStation(ui);

        EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log("[SaveStation] Setup complete: typewriter station + full-screen save/load UI + toast wired.");
        EditorUtility.DisplayDialog("Save Station",
            "Setup complete.\n\n• SaveStation prefab (typewriter) + scene instance\n" +
            "• Full-screen Save/Load screen (5 slots) on the main canvas\n" +
            "• Ink cost: each save spends one Ink item; the screen shows how many\n" +
            "  saves the carried ink allows and greys out when out of ink\n" +
            "• 'Game saved' toast in the lower-right corner\n\n" +
            "In Play mode left-click the typewriter. Saving needs Ink in the inventory " +
            "and consumes one per save; saving over an occupied slot asks for confirmation; " +
            "after a save the screen closes and the toast confirms it; Load is clickable " +
            "only on occupied slots.",
            "OK");

        if (station != null)
            Selection.activeGameObject = station.gameObject;
    }

    // ---------------------------------------------------------------- station

    private static SaveStation EnsureStation(SaveLoadUI ui)
    {
        var sprite = AssetDatabase.LoadAllAssetsAtPath(TypewriterSpritePath)
            .OfType<Sprite>().FirstOrDefault();
        if (sprite == null)
            Debug.LogWarning($"[SaveStation] No sprite at '{TypewriterSpritePath}' — prefab will have none.");

        GameObject prefab = BuildStationPrefab(sprite);

        var existing = Object.FindFirstObjectByType<SaveStation>();
        if (existing == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = DefaultStationPosition();
            Undo.RegisterCreatedObjectUndo(instance, "Create Save Station");
            existing = instance.GetComponent<SaveStation>();
        }

        SetRef(existing, "ui", ui);
        // Cursor lives on the inventory canvas; when it isn't in the scene yet the
        // station resolves it itself at Awake.
        var cursor = Object.FindFirstObjectByType<CursorController>();
        if (cursor != null) SetRef(existing, "cursor", cursor);
        return existing;
    }

    /// <summary>
    /// (Re)builds the station prefab in place (stable GUID keeps scene instances
    /// linked): typewriter sprite, trigger box on the Interactable layer sized to the
    /// sprite, and the <see cref="SaveStation"/> behaviour.
    /// </summary>
    private static GameObject BuildStationPrefab(Sprite sprite)
    {
        var root = new GameObject("SaveStation");
        try
        {
            root.layer = InteractableLayer;
            root.transform.localScale = Vector3.one * StationScale;

            var renderer = root.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;

            var collider = root.AddComponent<BoxCollider2D>();
            collider.isTrigger = true; // interactable, but never blocks movement/pathfinding
            if (sprite != null)
            {
                collider.size = sprite.bounds.size;
                collider.offset = sprite.bounds.center;
            }

            root.AddComponent<SaveStation>();

            Directory.CreateDirectory(Path.GetDirectoryName(StationPrefabPath));
            return PrefabUtility.SaveAsPrefabAsset(root, StationPrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    /// <summary>Next to the player spawn point when one is wired, origin otherwise.</summary>
    private static Vector3 DefaultStationPosition()
    {
        var gameManager = Object.FindFirstObjectByType<GameManager>();
        if (gameManager != null)
        {
            var spawn = new SerializedObject(gameManager)
                .FindProperty("playerSpawnPoint").objectReferenceValue as Transform;
            if (spawn != null)
                return spawn.position + new Vector3(2f, 1f, 0f);
        }
        return Vector3.zero;
    }

    // ---------------------------------------------------------------- UI

    /// <summary>
    /// Generates the full-screen save/load screen from scratch; a previously generated
    /// one is deleted first so re-runs always produce the current layout.
    /// </summary>
    private static SaveLoadUI BuildSaveLoadUI(Canvas canvas)
    {
        var old = Object.FindFirstObjectByType<SaveLoadUI>(FindObjectsInactive.Include);
        if (old != null)
        {
            Debug.Log("[SaveStation] Existing SaveLoadUI regenerated.");
            Object.DestroyImmediate(old.gameObject);
        }

        var slotBg = AssetDatabase.LoadAssetAtPath<Sprite>(SlotBgSpritePath);
        var windowBg = AssetDatabase.LoadAssetAtPath<Sprite>(WindowBgSpritePath);

        // Always-active root holding the behaviour; the fullscreen panel is toggled.
        var rootRt = NewUI("SaveLoadUI", canvas.transform);
        Stretch(rootRt, 0);
        rootRt.SetAsLastSibling(); // must draw over the backpack/crafting windows
        var ui = rootRt.gameObject.AddComponent<SaveLoadUI>();

        // Ink cost: each save spends one Ink item from the inventory.
        var saveCost = rootRt.gameObject.AddComponent<SaveCost>();
        var inkItem = AssetDatabase.LoadAssetAtPath<ItemData>(InkItemPath);
        if (inkItem == null)
            Debug.LogWarning(
                $"[SaveStation] Ink item not found at '{InkItemPath}' — saving will be free " +
                "until the asset exists. Run Tools ▸ Save System ▸ Rebuild Item Database if it does.");
        SetRef(saveCost, "saveItem", inkItem);
        SetRef(ui, "saveCost", saveCost);

        var panelRt = NewUI("Panel", rootRt);
        Stretch(panelRt, 0);
        var panelImg = panelRt.gameObject.AddComponent<Image>();
        panelImg.color = new Color(0.05f, 0.045f, 0.04f, 0.98f); // blocks clicks to the world UI below

        var title = MakeLabel("Title", panelRt, "Save Game", 44, FontStyles.Bold,
            TextAlignmentOptions.Center);
        var titleRt = (RectTransform)title.transform;
        titleRt.anchorMin = titleRt.anchorMax = new Vector2(0.5f, 1f);
        titleRt.pivot = new Vector2(0.5f, 1f);
        titleRt.sizeDelta = new Vector2(600, 60);
        titleRt.anchoredPosition = new Vector2(0, -50);

        Button saveTab = MakeButton("SaveTab", panelRt, "Save", slotBg, new Vector2(150, 46));
        Button loadTab = MakeButton("LoadTab", panelRt, "Load", slotBg, new Vector2(150, 46));
        PlaceTopCenter((RectTransform)saveTab.transform, new Vector2(-85, -130));
        PlaceTopCenter((RectTransform)loadTab.transform, new Vector2(85, -130));
        StyleTab(saveTab);
        StyleTab(loadTab);

        // Ink counter under the tabs: how many saves the carried ink allows.
        var cost = MakeLabel("CostLabel", panelRt, "Ink: 0", 24, FontStyles.Bold,
            TextAlignmentOptions.Center);
        var costRt = (RectTransform)cost.transform;
        costRt.anchorMin = costRt.anchorMax = new Vector2(0.5f, 1f);
        costRt.pivot = new Vector2(0.5f, 1f);
        costRt.sizeDelta = new Vector2(600, 34);
        costRt.anchoredPosition = new Vector2(0, -186);

        // Slot rows in a fixed centered column.
        int slotCount = SaveManager.MaxSlots;
        var slotsRt = NewUI("Slots", panelRt);
        slotsRt.anchorMin = slotsRt.anchorMax = new Vector2(0.5f, 0.5f);
        slotsRt.pivot = new Vector2(0.5f, 0.5f);
        slotsRt.sizeDelta = new Vector2(
            SlotWidth, slotCount * SlotHeight + (slotCount - 1) * SlotSpacing);
        slotsRt.anchoredPosition = new Vector2(0, -20);
        var layout = slotsRt.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.spacing = SlotSpacing;
        layout.childControlWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;

        var slotViews = new SaveSlotView[slotCount];
        for (int i = 0; i < slotCount; i++)
            slotViews[i] = BuildSlotRow(slotsRt, i, slotBg);

        Button close = MakeButton("CloseButton", panelRt, "Close", slotBg, new Vector2(200, 50));
        var closeRt = (RectTransform)close.transform;
        closeRt.anchorMin = closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0, 50);

        // Overwrite confirmation dialog on top of everything.
        var confirmRt = NewUI("Confirm", panelRt);
        Stretch(confirmRt, 0);
        var dimImg = confirmRt.gameObject.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.6f); // swallows clicks under the dialog

        var boxRt = NewUI("Box", confirmRt);
        boxRt.anchorMin = boxRt.anchorMax = new Vector2(0.5f, 0.5f);
        boxRt.pivot = new Vector2(0.5f, 0.5f);
        boxRt.sizeDelta = new Vector2(520, 220);
        var boxImg = boxRt.gameObject.AddComponent<Image>();
        boxImg.color = new Color(0.1f, 0.09f, 0.08f, 0.98f);
        if (windowBg != null) { boxImg.sprite = windowBg; boxImg.type = Image.Type.Sliced; }

        var message = MakeLabel("Message", boxRt, "Overwrite the save in slot 1?", 26,
            FontStyles.Normal, TextAlignmentOptions.Center);
        var messageRt = (RectTransform)message.transform;
        messageRt.anchorMin = new Vector2(0, 0.45f);
        messageRt.anchorMax = new Vector2(1, 1);
        messageRt.offsetMin = new Vector2(20, 0);
        messageRt.offsetMax = new Vector2(-20, -16);

        Button accept = MakeButton("AcceptButton", boxRt, "Overwrite", slotBg, new Vector2(170, 46));
        Button cancel = MakeButton("CancelButton", boxRt, "Cancel", slotBg, new Vector2(170, 46));
        PlaceBottomCenter((RectTransform)accept.transform, new Vector2(-95, 24));
        PlaceBottomCenter((RectTransform)cancel.transform, new Vector2(95, 24));

        confirmRt.gameObject.SetActive(false);
        panelRt.gameObject.SetActive(false);

        // Wire the behaviour.
        SetRef(ui, "panelRoot", panelRt.gameObject);
        SetRef(ui, "titleLabel", title);
        SetRef(ui, "costLabel", cost);
        SetRef(ui, "saveTabButton", saveTab);
        SetRef(ui, "loadTabButton", loadTab);
        SetRef(ui, "closeButton", close);
        SetRef(ui, "confirmRoot", confirmRt.gameObject);
        SetRef(ui, "confirmLabel", message);
        SetRef(ui, "confirmAcceptButton", accept);
        SetRef(ui, "confirmCancelButton", cancel);
        SetRef(ui, "gameManager", Object.FindFirstObjectByType<GameManager>());
        SetArray(ui, "slotViews", slotViews);

        Undo.RegisterCreatedObjectUndo(rootRt.gameObject, "Create Save/Load UI");
        return ui;
    }

    private static SaveSlotView BuildSlotRow(RectTransform parent, int index, Sprite slotBg)
    {
        var rowRt = NewUI($"Slot {index}", parent);
        rowRt.sizeDelta = new Vector2(SlotWidth, SlotHeight);

        var bg = rowRt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.16f, 0.15f, 0.13f, 0.95f);
        if (slotBg != null) { bg.sprite = slotBg; bg.type = Image.Type.Sliced; }

        var button = rowRt.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;

        var view = rowRt.gameObject.AddComponent<SaveSlotView>();

        var title = MakeLabel("Title", rowRt, $"Slot {index + 1}", 26, FontStyles.Bold,
            TextAlignmentOptions.MidlineLeft);
        var titleRt = (RectTransform)title.transform;
        titleRt.anchorMin = new Vector2(0, 0);
        titleRt.anchorMax = new Vector2(0, 1);
        titleRt.pivot = new Vector2(0, 0.5f);
        titleRt.sizeDelta = new Vector2(130, 0);
        titleRt.anchoredPosition = new Vector2(24, 0);

        var detail = MakeLabel("Detail", rowRt, "Empty", 20, FontStyles.Normal,
            TextAlignmentOptions.MidlineLeft);
        detail.color = new Color(0.75f, 0.72f, 0.66f, 1f);
        var detailRt = (RectTransform)detail.transform;
        detailRt.anchorMin = new Vector2(0, 0);
        detailRt.anchorMax = new Vector2(1, 1);
        detailRt.offsetMin = new Vector2(170, 0);
        detailRt.offsetMax = new Vector2(-16, 0);

        SetRef(view, "button", button);
        SetRef(view, "titleLabel", title);
        SetRef(view, "detailLabel", detail);
        return view;
    }

    /// <summary>
    /// Builds the bottom-right HUD toast ("Game saved"): a small always-active panel
    /// whose CanvasGroup starts invisible; <see cref="ToastUI"/> drives the fade.
    /// Never a raycast target, so it can't swallow clicks even while visible.
    /// </summary>
    private static ToastUI BuildToast(Canvas canvas)
    {
        var old = Object.FindFirstObjectByType<ToastUI>(FindObjectsInactive.Include);
        if (old != null)
            Object.DestroyImmediate(old.gameObject);

        var slotBg = AssetDatabase.LoadAssetAtPath<Sprite>(SlotBgSpritePath);

        var rootRt = NewUI("Toast", canvas.transform);
        rootRt.SetAsLastSibling(); // above the save screen, so "Save failed" is visible too
        rootRt.anchorMin = rootRt.anchorMax = new Vector2(1f, 0f);
        rootRt.pivot = new Vector2(1f, 0f);
        rootRt.sizeDelta = new Vector2(300, 52);
        rootRt.anchoredPosition = new Vector2(-24, 24);

        var bg = rootRt.gameObject.AddComponent<Image>();
        bg.color = new Color(0.12f, 0.11f, 0.1f, 0.92f);
        bg.raycastTarget = false;
        if (slotBg != null) { bg.sprite = slotBg; bg.type = Image.Type.Sliced; }

        var group = rootRt.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;

        var label = MakeLabel("Label", rootRt, "Game saved", 24, FontStyles.Bold,
            TextAlignmentOptions.Center);
        Stretch((RectTransform)label.transform, 4);

        var toast = rootRt.gameObject.AddComponent<ToastUI>();
        SetRef(toast, "group", group);
        SetRef(toast, "label", label);

        Undo.RegisterCreatedObjectUndo(rootRt.gameObject, "Create Toast UI");
        return toast;
    }

    // ---------------------------------------------------------------- helpers

    private static Button MakeButton(string name, RectTransform parent, string label,
        Sprite bgSprite, Vector2 size)
    {
        var rt = NewUI(name, parent);
        rt.sizeDelta = size;

        var img = rt.gameObject.AddComponent<Image>();
        img.color = new Color(0.22f, 0.2f, 0.17f, 0.95f);
        if (bgSprite != null) { img.sprite = bgSprite; img.type = Image.Type.Sliced; }

        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;

        var text = MakeLabel("Label", rt, label, 24, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch((RectTransform)text.transform, 2);
        return button;
    }

    /// <summary>The active tab is shown disabled — make that read as "lit", not broken.</summary>
    private static void StyleTab(Button tab)
    {
        var colors = tab.colors;
        colors.disabledColor = new Color(1f, 0.85f, 0.5f, 1f);
        tab.colors = colors;
    }

    private static TextMeshProUGUI MakeLabel(string name, RectTransform parent, string text,
        float size, FontStyles style, TextAlignmentOptions alignment)
    {
        var rt = NewUI(name, parent);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = alignment;
        label.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) label.font = TMP_Settings.defaultFontAsset;
        return label;
    }

    private static void PlaceTopCenter(RectTransform rt, Vector2 anchoredPosition)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = anchoredPosition;
    }

    private static void PlaceBottomCenter(RectTransform rt, Vector2 anchoredPosition)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = anchoredPosition;
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
            Debug.LogWarning($"[SaveStation] Field '{field}' not found on {component.GetType().Name}.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetArray(Object component, string field, Object[] values)
    {
        var so = new SerializedObject(component);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[SaveStation] Field '{field}' not found on {component.GetType().Name}.");
            return;
        }
        prop.ClearArray();
        for (int i = 0; i < values.Length; i++)
        {
            prop.InsertArrayElementAtIndex(i);
            prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
