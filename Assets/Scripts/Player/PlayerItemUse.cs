using UnityEngine;

/// <summary>
/// Hold-to-use for consumable hotbar items. While an item with a positive
/// <see cref="ItemData.healAmount"/> — the bandage — sits under the hotbar selection,
/// holding the prepare button (right mouse) for <see cref="holdSeconds"/> spends one unit
/// from that slot and heals the player for that item's amount.
///
/// The press arrives from <see cref="PlayerInputHandler"/> rather than from the controls asset
/// directly, so the same pause / backpack-open / sprint gating that guards weapon charging
/// guards healing too. A consumable is never a weapon (<see cref="WeaponType.None"/> leaves
/// <see cref="PlayerWeaponManager"/> with nothing equipped), so the two never contend for the
/// button: the input handler offers the press here first and falls through to charging only
/// when no use started.
///
/// The hold is tied to the slot it started on, not just to the item: scrolling away, dropping
/// the stack or having it consumed elsewhere mid-hold cancels instead of healing off the
/// wrong slot. Progress runs on scaled time, so pausing freezes it.
/// </summary>
public class PlayerItemUse : MonoBehaviour
{
    [Tooltip("Auto-resolved from this GameObject if left unset.")]
    [SerializeField] private PlayerHealthSystem health;

    [Tooltip("Auto-resolved from this GameObject if left unset.")]
    [SerializeField] private SlotInventory slotInventory;

    [Tooltip("Auto-resolved at runtime if left unset (the hotbar lives in the UI canvas, not the player prefab).")]
    [SerializeField] private HotbarUI hotbar;

    [Tooltip("How long the prepare button must be held before the selected consumable is spent.")]
    [Min(0f)]
    [SerializeField] private float holdSeconds = 1f;

    /// <summary>Slot the current hold started on, or -1 when nothing is being used.</summary>
    private int usedSlot = -1;

    private ItemData usedItem;
    private float holdStartTime;

    /// <summary>True while a consumable is being held down.</summary>
    public bool IsUsing => usedItem != null;

    /// <summary>How far the current hold has come, 0..1 (0 when nothing is being used).</summary>
    public float UseProgress
    {
        get
        {
            if (!IsUsing) return 0f;
            if (holdSeconds <= 0f) return 1f;
            return Mathf.Clamp01((Time.time - holdStartTime) / holdSeconds);
        }
    }

    /// <summary>Whether holding the button on this item would do anything at all.</summary>
    public static bool IsConsumable(ItemData item) => item != null && item.healAmount > 0;

    private void Awake()
    {
        if (health == null) health = GetComponent<PlayerHealthSystem>();
        if (slotInventory == null) slotInventory = GetComponent<SlotInventory>();
    }

    private void Start()
    {
        if (hotbar == null) hotbar = FindFirstObjectByType<HotbarUI>();
    }

    /// <summary>
    /// Starts using the selected hotbar item, if it is a consumable worth using right now.
    /// Returns true when the press was taken (the caller must not also start a weapon charge).
    /// </summary>
    public bool TryBeginUse()
    {
        CancelUse();

        if (hotbar == null || slotInventory == null || health == null) return false;

        ItemData selected = hotbar.SelectedItem;
        if (!IsConsumable(selected)) return false;

        if (health.IsFull)
        {
            // Taking the press but spending nothing: the bandage is the player's scarcest
            // resource, so a full-health hold has to say why it did nothing rather than
            // silently eat the item.
            Notify("Health already full");
            return true;
        }

        usedSlot = hotbar.SelectedIndex;
        usedItem = selected;
        holdStartTime = Time.time;
        return true;
    }

    /// <summary>Aborts the current hold without consuming anything. Safe to call when idle.</summary>
    public void CancelUse()
    {
        if (!IsUsing) return;

        usedSlot = -1;
        usedItem = null;
    }

    private void Update()
    {
        if (!IsUsing) return;

        if (!StillHoldingUsedItem())
        {
            CancelUse();
            return;
        }

        if (UseProgress >= 1f) Consume();
    }

    /// <summary>
    /// Whether the slot the hold started on still carries the same item — the hotbar can
    /// change underneath a hold (scrolling, a drag onto that slot, crafting away the stack).
    /// </summary>
    private bool StillHoldingUsedItem()
    {
        if (hotbar != null && hotbar.SelectedIndex != usedSlot) return false;

        var stack = slotInventory.Hotbar.Get(usedSlot);
        return stack != null && !stack.IsEmpty && ItemStack.IsSameItem(stack.item, usedItem);
    }

    /// <summary>Spends one unit from the slot and applies its effect.</summary>
    private void Consume()
    {
        var container = slotInventory.Hotbar;
        var stack = container.Get(usedSlot);
        int healed = usedItem.healAmount;
        string label = string.IsNullOrEmpty(usedItem.itemName) ? "Item" : usedItem.itemName;

        // count - 1 clears the slot at zero and raises SlotChanged either way, so the
        // hotbar icon and its quantity badge follow along.
        container.Set(usedSlot, stack.item, stack.count - 1);
        health.Heal(healed);
        Notify($"{label} used (+{healed} HP)");

        CancelUse();
    }

    private void Notify(string message)
    {
        FindFirstObjectByType<ToastUI>()?.Show(message);
    }
}
