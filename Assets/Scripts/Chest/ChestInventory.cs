using UnityEngine;

/// <summary>
/// Source of truth for a single chest's storage. Mirrors SlotInventory's pattern:
/// owns one ItemContainer, created lazily so it's ready regardless of script
/// execution order.
/// </summary>
public class ChestInventory : MonoBehaviour
{
    [Header("Chest (columns x rows)")]
    [SerializeField, Min(0)] private int columns = 4;
    [SerializeField, Min(0)] private int rows = 4;

    private ItemContainer _container;

    /// <summary>This chest's storage (default 4 x 4 = 16 slots).</summary>
    public ItemContainer Container => _container ??= new ItemContainer(columns * rows);
}
