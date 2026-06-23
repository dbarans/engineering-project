using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The cursor "hand". One per Canvas. Follows the mouse and applies the
/// pick / place / merge / swap rules when a <see cref="SlotView"/> is clicked.
///
/// The carried item is a real, persistent <see cref="InventoryItem"/> GameObject:
/// it is re-parented (slot → cursor → slot) rather than recreated, so any per-item
/// state on it survives a move. The cursor never spawns entities — it only adopts
/// one detached from a slot.
/// </summary>
public class HeldItemController : MonoBehaviour
{
    [SerializeField] private RectTransform followTarget;

    [Header("Rendering")]
    [Tooltip("Sorting order of the held item's own canvas. Kept high so the carried " +
             "item always draws above the HUD and other UI.")]
    [SerializeField] private int sortingOrder = 1000;

    private InventoryItem _heldItem;

    /// <summary>True while the cursor is carrying an item entity.</summary>
    public bool IsHolding => _heldItem != null;

    private void Awake()
    {
        if (followTarget == null) followTarget = transform as RectTransform;
        EnsureTopMostCanvas();
    }

    /// <summary>
    /// Puts the held item on its own nested <see cref="Canvas"/> with a high sorting
    /// order so it renders above every other UI element (including the HUD).
    /// </summary>
    private void EnsureTopMostCanvas()
    {
        var canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.overrideSorting = true;
        canvas.sortingOrder = sortingOrder;
    }

    private void Update()
    {
        if (followTarget != null && Mouse.current != null)
            followTarget.position = Mouse.current.position.ReadValue();
    }

    /// <summary>Routes a click on <paramref name="slot"/> through the interaction rules.</summary>
    public void HandleSlotClick(SlotView slot)
    {
        if (slot == null) return;
        var container = slot.Container;
        int index = slot.Index;
        var stack = container?.Get(index);
        if (stack == null) return;

        // Capture slot data up front — container.Clear/Set mutates the live stack in place.
        ItemData slotItem = stack.item;
        int slotCount = stack.count;
        bool slotEmpty = stack.IsEmpty;

        if (_heldItem == null)
        {
            // Empty hand + slot has item -> take the slot's entity onto the cursor.
            if (slotEmpty) return;
            var picked = slot.Detach();
            container.Clear(index);            // Refresh sees empty + detached -> no-op
            Adopt(picked);
            if (_heldItem != null) _heldItem.SetStack(slotItem, slotCount);
            return;
        }

        ItemData heldItem = _heldItem.Item;
        int heldCount = _heldItem.Count;

        if (slotEmpty)
        {
            // Holding + empty slot -> drop the cursor's entity into the slot.
            var dropped = _heldItem;
            _heldItem = null;
            slot.Attach(dropped);
            container.Set(index, heldItem, heldCount); // Refresh updates the now-attached entity
            return;
        }

        if (ItemStack.IsSameItem(slotItem, heldItem))
        {
            int max = Mathf.Max(1, slotItem.maxStack);
            int moved = Mathf.Min(Mathf.Max(0, max - slotCount), heldCount);
            if (moved > 0)
            {
                // Merge into the slot's existing entity; the cursor keeps any remainder.
                container.Set(index, slotItem, slotCount + moved);
                int remainder = heldCount - moved;
                if (remainder <= 0)
                {
                    Destroy(_heldItem.gameObject);
                    _heldItem = null;
                }
                else
                {
                    _heldItem.SetStack(heldItem, remainder);
                }
                return;
            }
            // Slot is full -> fall through to a swap so the user can relocate it.
        }

        // Holding + different item (or full same-item) -> swap the two entities.
        var fromSlot = slot.Detach();
        var fromHand = _heldItem;
        slot.Attach(fromHand);
        container.Set(index, heldItem, heldCount);     // Refresh updates the slot's new entity
        Adopt(fromSlot);
        if (_heldItem != null) _heldItem.SetStack(slotItem, slotCount);
    }

    /// <summary>Re-parents an entity onto the cursor.</summary>
    private void Adopt(InventoryItem item)
    {
        _heldItem = item;
        if (item == null || followTarget == null) return;
        item.transform.SetParent(followTarget, false);
        SlotView.StretchToParent(item.transform as RectTransform);
    }
}
