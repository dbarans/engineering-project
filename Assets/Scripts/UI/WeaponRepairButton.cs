using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The crafting table's "repair weapon" action. Shown by <see cref="CraftingBoxUI"/> while a
/// <see cref="CraftingTable"/> is in range. Clicking it restores a damaged durable weapon (a
/// melee weapon whose <see cref="ItemData.maxDurability"/> is positive) back to full, at a cost
/// of one scrap for every <see cref="durabilityPerScrap"/> durability points restored (rounded
/// up) — but only when the player is actually carrying that many scrap. With too little scrap,
/// or nothing to repair, it does nothing (and never spends scrap).
/// </summary>
[RequireComponent(typeof(Button))]
public class WeaponRepairButton : MonoBehaviour
{
    [Tooltip("Inventory the scrap is spent from and the weapon repaired in. Auto-resolved at runtime if left unset.")]
    [SerializeField] private SlotInventory inventory;
    [Tooltip("The scrap item consumed by a repair. Auto-resolved from the item database (by name) if left unset.")]
    [SerializeField] private ItemData scrapItem;
    [Tooltip("Durability points restored per scrap item. A repair costs one scrap for every this " +
             "many points of missing durability, rounded up (e.g. 3 = 1 scrap per 3 points).")]
    [SerializeField, Min(1)] private int durabilityPerScrap = 3;

    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnClicked);
    }

    private void OnClicked()
    {
        if (inventory == null) inventory = FindFirstObjectByType<SlotInventory>();
        if (scrapItem == null) scrapItem = ResolveScrap();

        if (inventory == null || scrapItem == null)
        {
            Debug.LogWarning("[WeaponRepair] Cannot repair — inventory or scrap item unresolved.");
            return;
        }

        if (!TryFindDamagedWeapon(out ItemContainer container, out int index))
        {
            Notify("No damaged weapon to repair.");
            return;
        }

        // Cost scales with the damage: one scrap per durabilityPerScrap points restored,
        // rounded up. A damaged weapon is always missing at least one point, so cost >= 1.
        var weapon = container.Get(index);
        int missing = weapon.MaxDurability - weapon.CurrentDurability;
        int cost = Mathf.CeilToInt(missing / (float)durabilityPerScrap);

        int scrapHeld = inventory.Hotbar.Count(scrapItem) + inventory.Backpack.Count(scrapItem);
        if (scrapHeld < cost)
        {
            Notify($"Need {cost} scrap to repair (have {scrapHeld}).");
            return;
        }

        // Spend the scrap (hotbar first, then backpack) and restore the weapon to full.
        int remaining = cost;
        remaining -= inventory.Hotbar.TryRemove(scrapItem, remaining);
        if (remaining > 0) remaining -= inventory.Backpack.TryRemove(scrapItem, remaining);

        container.SetDurability(index, weapon.MaxDurability);
        Notify($"Weapon repaired (-{cost} scrap).");
    }

    /// <summary>First damaged durable item found in the hotbar, then the backpack.</summary>
    private bool TryFindDamagedWeapon(out ItemContainer container, out int index)
    {
        if (TryFindDamagedIn(inventory.Hotbar, out index)) { container = inventory.Hotbar; return true; }
        if (TryFindDamagedIn(inventory.Backpack, out index)) { container = inventory.Backpack; return true; }
        container = null;
        index = -1;
        return false;
    }

    private static bool TryFindDamagedIn(ItemContainer container, out int index)
    {
        for (int i = 0; i < container.SlotCount; i++)
        {
            var stack = container.Get(i);
            if (stack != null && !stack.IsEmpty && stack.HasDurability &&
                stack.CurrentDurability < stack.MaxDurability)
            {
                index = i;
                return true;
            }
        }
        index = -1;
        return false;
    }

    /// <summary>Falls back to matching the scrap asset by name when no reference is wired.</summary>
    private static ItemData ResolveScrap()
    {
        var db = ItemDatabase.Instance;
        if (db == null) return null;
        foreach (var item in db.Items)
            if (item != null && string.Equals(item.itemName, "Scrap", StringComparison.OrdinalIgnoreCase))
                return item;
        return null;
    }

    private static void Notify(string message)
    {
        Debug.Log($"[WeaponRepair] {message}");
        FindFirstObjectByType<ToastUI>()?.Show(message);
    }
}
