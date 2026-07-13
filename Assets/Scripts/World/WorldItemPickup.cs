using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Scene-level manager for items on the ground. Two responsibilities:
///
/// 1. <see cref="Drop"/> — spawns a <see cref="WorldItem"/> in front of the player.
///    Called by <see cref="HeldItemController"/> when the player clicks the world while
///    carrying an item on the cursor.
///
/// 2. Pick-up — every frame it collects the <see cref="WorldItem"/>s under the cursor,
///    reveals the name of the currently selected one, lets the player cycle the
///    selection with <see cref="cycleKey"/> (E) when several are stacked, and picks the
///    selected one up into the <see cref="SlotInventory.Backpack"/> on left-click.
///
/// The selection is cycled with E (read directly off the keyboard rather than the
/// Interact action, so ordinary world interaction is unaffected) and only consumes the
/// key while a stack of two or more is under the cursor.
/// </summary>
public class WorldItemPickup : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private SlotInventory slotInventory;
    [SerializeField] private WorldItem worldItemPrefab;
    [SerializeField] private Camera worldCamera;
    [Tooltip("Cursor that displays the hovered item's name. Auto-resolved if left unset.")]
    [SerializeField] private CursorController cursor;

    [Header("Drop placement")]
    [Tooltip("Transform the drop is measured from (the player).")]
    [SerializeField] private Transform player;
    [Tooltip("Transform whose local +X (right) axis is the player's facing/front. " +
             "Usually the aimed torso; defaults to the player if unset.")]
    [SerializeField] private Transform facingSource;
    [Tooltip("How far in front of the player a dropped item lands.")]
    [SerializeField] private float dropDistance = 1.5f;
    [Tooltip("Small random spread so items dropped on the same spot fan out instead of perfectly overlapping.")]
    [SerializeField] private float dropScatter = 0.25f;
    [Tooltip("Seconds a freshly dropped item cannot be picked up, so the drop click doesn't instantly re-grab it.")]
    [SerializeField] private float pickupDelay = 0.5f;

    [Header("Pick-up")]
    [Tooltip("Radius (world units) around the cursor that counts as hovering an item.")]
    [SerializeField] private float hoverRadius = 0.3f;
    [Tooltip("Layers searched for world items under the cursor. Non-item colliders are ignored anyway.")]
    [SerializeField] private LayerMask itemLayerMask = ~0;
    [Tooltip("Key that cycles the selection when several items are stacked under the cursor.")]
    [SerializeField] private Key cycleKey = Key.E;

    // Items under the cursor this frame, sorted for a stable cycling order.
    private readonly List<WorldItem> _underCursor = new List<WorldItem>();
    private readonly List<Collider2D> _hits = new List<Collider2D>();
    private ContactFilter2D _filter;

    private WorldItem _selected;   // the item that will be picked up / whose name is shown
    private WorldItem _shownItem;  // the item currently displaying its name (to hide it later)

    private void Awake()
    {
        if (worldCamera == null) worldCamera = Camera.main;
        if (slotInventory == null) slotInventory = FindFirstObjectByType<SlotInventory>();
        if (cursor == null) cursor = FindFirstObjectByType<CursorController>();

        _filter = new ContactFilter2D { useTriggers = true, useLayerMask = true };
        _filter.SetLayerMask(itemLayerMask);
    }

    /// <summary>
    /// Spawns <paramref name="count"/> units of <paramref name="item"/> as a single drop
    /// on the ground in front of the player. Returns <c>false</c> if it could not spawn
    /// (no prefab or nothing to drop).
    /// </summary>
    public bool Drop(ItemData item, int count)
    {
        var drop = SpawnAt(item, count, ComputeDropPosition());
        if (drop == null) return false;

        drop.ArmPickupDelay(pickupDelay);
        return true;
    }

    /// <summary>
    /// Spawns a drop of <paramref name="count"/> × <paramref name="item"/> at the given
    /// world position, without a pickup delay. Used by <see cref="Drop"/> (which arms
    /// the delay on top) and by <see cref="WorldItemsSaveable"/> when restoring ground
    /// items from a save. Returns null when it could not spawn (no prefab, no item or
    /// nothing to drop).
    /// </summary>
    public WorldItem SpawnAt(ItemData item, int count, Vector2 position)
    {
        if (item == null || count <= 0 || worldItemPrefab == null) return null;

        var drop = Instantiate(
            worldItemPrefab, new Vector3(position.x, position.y, 0f), Quaternion.identity);
        drop.SetStack(item, count);
        return drop;
    }

    private Vector3 ComputeDropPosition()
    {
        Transform origin = player != null ? player : transform;
        Transform rot = facingSource != null ? facingSource : origin;

        Vector3 dir = rot.right; // +X is "front" per PlayerAim's aiming convention
        if (dir.sqrMagnitude < 0.0001f) dir = Vector3.right;

        Vector3 pos = origin.position + dir.normalized * dropDistance;
        if (dropScatter > 0f)
            pos += (Vector3)(Random.insideUnitCircle * dropScatter);
        pos.z = 0f;
        return pos;
    }

    private void Update()
    {
        RefreshUnderCursor();
        UpdateSelection();
        ShowSelectedName();

        if (_selected != null && LeftClickPressedThisFrame())
            PickUp(_selected);
    }

    /// <summary>Hover text for a drop: its name, plus a "×N" suffix when more than one is stacked.</summary>
    private static string NameOf(WorldItem item)
    {
        if (item == null || item.Item == null) return string.Empty;
        return item.Count > 1 ? $"{item.Item.itemName} ×{item.Count}" : item.Item.itemName;
    }

    /// <summary>Rebuilds <see cref="_underCursor"/> from the colliders around the cursor.</summary>
    private void RefreshUnderCursor()
    {
        _underCursor.Clear();

        if (worldCamera == null || Mouse.current == null) return;

        Vector3 world = worldCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        Vector2 point = world; // z ignored by 2D physics

        int count = Physics2D.OverlapCircle(point, hoverRadius, _filter, _hits);
        for (int i = 0; i < count; i++)
        {
            var col = _hits[i];
            if (col == null) continue;
            var item = col.GetComponentInParent<WorldItem>();
            if (item != null && !_underCursor.Contains(item))
                _underCursor.Add(item);
        }

        // Stable order so E cycles predictably regardless of physics query ordering.
        _underCursor.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
    }

    /// <summary>Keeps the selection on the same item across frames, and cycles it on the key.</summary>
    private void UpdateSelection()
    {
        if (_underCursor.Count == 0)
        {
            _selected = null;
            return;
        }

        int index = _selected != null ? _underCursor.IndexOf(_selected) : -1;
        if (index < 0)
        {
            index = 0;
            _selected = _underCursor[0];
        }

        if (_underCursor.Count > 1 && CycleKeyPressedThisFrame())
        {
            index = (index + 1) % _underCursor.Count;
            _selected = _underCursor[index];
        }
    }

    /// <summary>Shows the selected item's name on the cursor, updating it as the selection changes.</summary>
    private void ShowSelectedName()
    {
        if (_shownItem == _selected) return;

        _shownItem = _selected;
        if (cursor != null) cursor.SetHoverText(NameOf(_selected)); // empty name clears the label
    }

    /// <summary>Moves the drop into the backpack, keeping any overflow on the ground.</summary>
    private void PickUp(WorldItem item)
    {
        if (item == null || slotInventory == null) return;
        if (!item.CanPickUp) return; // still in its post-drop delay

        int remainder = slotInventory.Backpack.TryAddItem(item.Item, item.Count);
        if (remainder <= 0)
        {
            if (_selected == item) _selected = null;
            if (_shownItem == item)
            {
                _shownItem = null;
                if (cursor != null) cursor.ClearHoverText();
            }
            Destroy(item.gameObject);
        }
        else
        {
            // Backpack filled up — leave the remainder on the ground, still hovered.
            item.SetStack(item.Item, remainder);
            if (cursor != null && _shownItem == item) cursor.SetHoverText(NameOf(item)); // refresh the count
        }
    }

    private static bool LeftClickPressedThisFrame()
    {
        var mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
    }

    private bool CycleKeyPressedThisFrame()
    {
        var keyboard = Keyboard.current;
        return keyboard != null && keyboard[cycleKey].wasPressedThisFrame;
    }
}
