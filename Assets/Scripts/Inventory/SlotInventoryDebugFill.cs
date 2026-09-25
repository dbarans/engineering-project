using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional helper that seeds the slot inventory with example stacks on Start so
/// the hotbar/backpack show content (and quantity badges) without picking items up.
/// Safe to remove for production.
/// </summary>
public class SlotInventoryDebugFill : MonoBehaviour
{
    [System.Serializable]
    public class Entry
    {
        public ItemData item;
        [Min(1)] public int count = 1;
        [Tooltip("Place in the backpack instead of the hotbar.")]
        public bool toBackpack;
    }

    [SerializeField] private SlotInventory slotInventory;
    [SerializeField] private List<Entry> entries = new List<Entry>();

    private void Start()
    {
        if (slotInventory == null) slotInventory = GetComponent<SlotInventory>();
        if (slotInventory == null) slotInventory = FindFirstObjectByType<SlotInventory>();
        if (slotInventory == null) return;

        foreach (var e in entries)
        {
            if (e?.item == null) continue;
            var container = e.toBackpack ? slotInventory.Backpack : slotInventory.Hotbar;
            container.TryAddItem(e.item, Mathf.Max(1, e.count));
        }
    }
}
