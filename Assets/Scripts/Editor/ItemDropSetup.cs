using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One-click setup for dropping items on the ground and picking them back up.
/// Builds the <c>DroppedItem</c> prefab (unknown-icon sprite + trigger collider +
/// hidden floating name), drops a <see cref="WorldItemPickup"/> manager into the scene,
/// and wires it to the existing <see cref="SlotInventory"/>, player and
/// <see cref="HeldItemController"/>. Idempotent.
///
/// Run <b>Tools ▸ Slot Inventory ▸ Build UI &amp; Wire Scene</b> first (it creates the
/// backpack + cursor), then <b>Tools ▸ Slot Inventory ▸ Build Item Drops</b>.
/// </summary>
public static class ItemDropSetup
{
    private const string DropPrefabPath = "Assets/Prefabs/World/DroppedItem.prefab";
    private const string UnknownIconPath = "Assets/Art/GDS/Sprites/Items/unknown-icon.png";

    [MenuItem("Tools/Slot Inventory/Build Item Drops")]
    public static void BuildAndWire()
    {
        var slotInventory = Object.FindFirstObjectByType<SlotInventory>();
        var held = Object.FindFirstObjectByType<HeldItemController>();

        if (slotInventory == null || held == null)
        {
            EditorUtility.DisplayDialog("Item Drops",
                "Missing pieces. Run 'Tools ▸ Slot Inventory ▸ Build UI & Wire Scene' first so the " +
                "SlotInventory and HeldItem cursor exist, then run this.", "OK");
            return;
        }

        WorldItem dropPrefab = BuildDropPrefab();
        WorldItemPickup manager = EnsureManager();
        CursorController cursor = EnsureCursor(held);

        ResolvePlayer(out Transform player, out Transform facing);

        SetRef(manager, "slotInventory", slotInventory);
        SetRef(manager, "worldItemPrefab", dropPrefab);
        SetRef(manager, "worldCamera", Camera.main);
        SetRef(manager, "player", player);
        SetRef(manager, "facingSource", facing);
        SetRef(manager, "cursor", cursor);

        SetRef(held, "dropTarget", manager);

        EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();

        Debug.Log("[ItemDrops] Setup complete. Hold an item on the cursor and left-click the world " +
                  "(outside the backpack) to drop it; hover a drop to see its name above the cursor, " +
                  "E to cycle a stack, left-click to pick up.");
        EditorUtility.DisplayDialog("Item Drops",
            "Setup complete.\n\n• DroppedItem prefab (unknown-icon)\n• Hover-name label on the cursor\n" +
            "• WorldItemPickup manager wired to SlotInventory / player / cursor\n\n" +
            "In Play mode: pick an item onto the cursor, left-click the world outside the backpack to drop it. " +
            "Hover a drop to reveal its name above the cursor, press E to cycle a stack, left-click to pick it up.", "OK");
    }

    // ---------------------------------------------------------------- prefab

    /// <summary>
    /// (Re)builds the dropped-item prefab: a SpriteRenderer showing the generic unknown
    /// icon and a trigger circle collider for cursor detection. Overwrites in place so
    /// its GUID stays stable.
    /// </summary>
    private static WorldItem BuildDropPrefab()
    {
        var root = new GameObject("DroppedItem");

        // Drops are parented to the generator's content root, which the Dungeon scene
        // scales 2×. At the prefab's own scale of 1 they came out twice the size of the
        // icons they represent; half here lands them where they used to sit before the
        // dungeon was built at run time.
        root.transform.localScale = new Vector3(0.5f, 0.5f, 1f);

        var sprite = root.AddComponent<SpriteRenderer>();
        sprite.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(UnknownIconPath);
        sprite.sortingOrder = 5;

        var collider = root.AddComponent<CircleCollider2D>();
        collider.isTrigger = true;
        collider.radius = 0.4f;

        root.AddComponent<WorldItem>();

        Directory.CreateDirectory(Path.GetDirectoryName(DropPrefabPath));
        var prefab = PrefabUtility.SaveAsPrefabAsset(root, DropPrefabPath);
        Object.DestroyImmediate(root);
        return prefab.GetComponent<WorldItem>();
    }

    // ---------------------------------------------------------------- scene pieces

    /// <summary>
    /// Adds a <see cref="CursorController"/> to the held-item cursor object and builds its
    /// floating hover label (a small text chip above the cursor) if missing. The cursor is
    /// the single object that both carries the held item and shows hovered names.
    /// </summary>
    private static CursorController EnsureCursor(HeldItemController held)
    {
        var go = held.gameObject;
        var cursor = EditorSetupUtility.EnsureComponent<CursorController>(go);

        var existing = new SerializedObject(cursor).FindProperty("labelRoot").objectReferenceValue as GameObject;
        if (existing == null)
            BuildHoverLabel(cursor, go.transform as RectTransform);

        return cursor;
    }

    /// <summary>Builds a self-sizing text chip anchored just above the cursor, hidden by default.</summary>
    private static void BuildHoverLabel(CursorController cursor, RectTransform cursorRt)
    {
        var rootRt = NewUI("HoverLabel", cursorRt);
        rootRt.anchorMin = rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 0f);           // grows upward from a point above the cursor
        rootRt.anchoredPosition = new Vector2(0f, 24f);

        var bg = rootRt.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0.75f);
        bg.raycastTarget = false;

        var fitter = rootRt.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var layout = rootRt.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(8, 8, 4, 4);
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        var textRt = NewUI("Text", rootRt);
        var text = textRt.gameObject.AddComponent<TextMeshProUGUI>();
        text.text = string.Empty;
        text.alignment = TextAlignmentOptions.Center;
        text.fontSize = 20;
        text.color = Color.white;
        text.raycastTarget = false;
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;

        SetRef(cursor, "labelRoot", rootRt.gameObject);
        SetRef(cursor, "label", text);

        rootRt.gameObject.SetActive(false);
    }

    /// <summary>
    /// Finds or creates the drop manager, and makes sure it carries the component that
    /// saves items lying on the ground.
    ///
    /// That component is added here rather than only living on the WorldItemPickup prefab,
    /// because the manager in the Dungeon scene was built by hand and never got it: drops
    /// were then never written to a save at all, since the save system scans the scene for
    /// <see cref="ISaveable"/> and finds nothing to ask. Ensuring it on every run of this
    /// tool keeps a scene rebuilt from scratch from losing it again.
    /// </summary>
    private static WorldItemPickup EnsureManager()
    {
        var existing = Object.FindFirstObjectByType<WorldItemPickup>();
        if (existing != null)
        {
            EditorSetupUtility.EnsureComponent<WorldItemsSaveable>(existing.gameObject);
            return existing;
        }

        var go = new GameObject("WorldItemPickup");
        var manager = go.AddComponent<WorldItemPickup>();
        EditorSetupUtility.EnsureComponent<WorldItemsSaveable>(go);
        Undo.RegisterCreatedObjectUndo(go, "Create WorldItemPickup");
        return manager;
    }

    /// <summary>
    /// Finds the player transform (position source) and its facing transform (the aimed
    /// torso, whose +X axis points where the player faces). Falls back gracefully.
    /// </summary>
    private static void ResolvePlayer(out Transform player, out Transform facing)
    {
        player = null;
        facing = null;

        var aim = Object.FindFirstObjectByType<PlayerAim>();
        if (aim != null)
        {
            player = aim.transform;
            facing = new SerializedObject(aim).FindProperty("torsoTransform").objectReferenceValue as Transform;
        }

        if (player == null)
        {
            var input = Object.FindFirstObjectByType<PlayerInputHandler>();
            if (input != null) player = input.transform;
        }

        if (facing == null) facing = player;
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

    private static void SetRef(Object component, string field, Object value)
    {
        var so = new SerializedObject(component);
        var prop = so.FindProperty(field);
        if (prop == null)
        {
            Debug.LogWarning($"[ItemDrops] Field '{field}' not found on {component.GetType().Name}.");
            return;
        }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
