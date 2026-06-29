using UnityEngine;

/// <summary>
/// Source of truth for the slot-based inventory: owns a hotbar container and a
/// backpack container. Independent from the legacy <see cref="PlayerInventory"/>
/// (which still backs crafting). Containers are created lazily so they are ready
/// regardless of script execution order.
/// </summary>
public class SlotInventory : MonoBehaviour
{
    [Header("Hotbar")]
    [SerializeField, Min(0)] private int hotbarSize = 5;

    [Header("Backpack (columns x rows)")]
    [SerializeField, Min(0)] private int backpackColumns = 4;
    [SerializeField, Min(0)] private int backpackRows = 5;

    private ItemContainer _hotbar;
    private ItemContainer _backpack;

    /// <summary>The hotbar container (default 5 slots).</summary>
    public ItemContainer Hotbar => _hotbar ??= new ItemContainer(hotbarSize);

    /// <summary>The backpack container (default 4 x 5 = 20 slots).</summary>
    public ItemContainer Backpack => _backpack ??= new ItemContainer(backpackColumns * backpackRows);
}
