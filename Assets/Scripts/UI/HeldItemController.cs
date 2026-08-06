using UnityEngine;
using UnityEngine.EventSystems;
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

    [Header("Dropping")]
    [Tooltip("Receives items dropped into the world. A left-click that lands on the world " +
             "(outside any UI, e.g. beyond the backpack panel) while carrying an item drops it. " +
             "Auto-resolved at runtime if left unset.")]
    [SerializeField] private WorldItemPickup dropTarget;

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

        // Carrying an item + a left-click that lands on the world (not on a slot or any
        // other UI, e.g. outside the backpack panel) -> drop it in front of the player.
        if (_heldItem != null && WasWorldClickThisFrame())
            DropHeldToWorld();
    }

    /// <summary>
    /// True on the frame the left mouse button is pressed while the pointer is over the
    /// world rather than any UI element. Slot clicks (which sit inside the panel) are
    /// reported as "over UI" and so are left to <see cref="SlotView.OnPointerClick"/>.
    /// </summary>
    private static bool WasWorldClickThisFrame()
    {
        var mouse = Mouse.current;
        if (mouse == null || !mouse.leftButton.wasPressedThisFrame) return false;

        var es = EventSystem.current;
        return es == null || !es.IsPointerOverGameObject();
    }

    /// <summary>Hands the carried stack to the world dropper and empties the cursor.</summary>
    private void DropHeldToWorld()
    {
        if (dropTarget == null) dropTarget = FindFirstObjectByType<WorldItemPickup>();
        if (dropTarget == null) return;

        if (!dropTarget.Drop(_heldItem.Item, _heldItem.Count)) return;

        Destroy(_heldItem.gameObject);
        _heldItem = null;
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
        int slotDurability = stack.CurrentDurability;
        bool slotEmpty = stack.IsEmpty;

        // Ctrl -> take a single unit from the clicked stack onto the cursor (repeatable).
        if (IsTakeOneModifierHeld())
        {
            TakeOne(slot, container, index, slotItem, slotCount, slotDurability, slotEmpty);
            return;
        }

        if (_heldItem == null)
        {
            // Empty hand + slot has item -> take it (or half of it with Shift) onto the cursor.
            if (slotEmpty) return;

            if (IsSplitModifierHeld() && slotCount > 1)
            {
                int take = (slotCount + 1) / 2;       // ceil half -> cursor
                int leave = slotCount - take;
                var half = slot.Detach();
                container.Set(index, slotItem, leave); // Refresh spawns a fresh entity for the remainder
                Adopt(half);
                if (_heldItem != null) _heldItem.SetStack(slotItem, take, slotDurability);
                return;
            }

            var picked = slot.Detach();
            container.Clear(index);            // Refresh sees empty + detached -> no-op
            Adopt(picked);
            if (_heldItem != null) _heldItem.SetStack(slotItem, slotCount, slotDurability);
            return;
        }

        ItemData heldItem = _heldItem.Item;
        int heldCount = _heldItem.Count;
        int heldDurability = _heldItem.Durability;

        if (slotEmpty)
        {
            // Holding + empty slot -> drop the cursor's entity into the slot.
            var dropped = _heldItem;
            _heldItem = null;
            slot.Attach(dropped);
            container.Set(index, heldItem, heldCount, heldDurability); // Refresh updates the now-attached entity
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
                    _heldItem.SetStack(heldItem, remainder, heldDurability);
                }
                return;
            }
            // Slot is full -> fall through to a swap so the user can relocate it.
        }

        // Holding + different item (or full same-item) -> swap the two entities.
        var fromSlot = slot.Detach();
        var fromHand = _heldItem;
        slot.Attach(fromHand);
        container.Set(index, heldItem, heldCount, heldDurability); // Refresh updates the slot's new entity
        Adopt(fromSlot);
        if (_heldItem != null) _heldItem.SetStack(slotItem, slotCount, slotDurability);
    }

    /// <summary>True while a Shift key is held (used to split a stack in half on pick-up).</summary>
    private static bool IsSplitModifierHeld()
    {
        var k = Keyboard.current;
        return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
    }

    /// <summary>True while a Ctrl key is held (used to take one unit at a time).</summary>
    private static bool IsTakeOneModifierHeld()
    {
        var k = Keyboard.current;
        return k != null && (k.leftCtrlKey.isPressed || k.rightCtrlKey.isPressed);
    }

    /// <summary>
    /// Moves a single unit from the clicked slot onto the cursor. With an empty hand it
    /// takes the slot's entity (set to 1); while already holding the same item it adds one
    /// more (up to <see cref="ItemData.maxStack"/>). No-op for an empty slot or a different
    /// held item.
    /// </summary>
    private void TakeOne(SlotView slot, ItemContainer container, int index,
        ItemData slotItem, int slotCount, int slotDurability, bool slotEmpty)
    {
        if (slotEmpty) return;

        if (_heldItem == null)
        {
            var entity = slot.Detach();
            container.Set(index, slotItem, slotCount - 1); // 0 -> clears; >0 -> respawns remainder
            Adopt(entity);
            if (_heldItem != null) _heldItem.SetStack(slotItem, 1, slotDurability);
            return;
        }

        if (!ItemStack.IsSameItem(_heldItem.Item, slotItem)) return;
        if (_heldItem.Count >= Mathf.Max(1, slotItem.maxStack)) return; // cursor full

        container.Set(index, slotItem, slotCount - 1);
        _heldItem.SetStack(slotItem, _heldItem.Count + 1, _heldItem.Durability);
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
