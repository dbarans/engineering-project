using System.Collections.Generic;

/// <summary>
/// Basic inventory contract used by crafting and UI.
/// </summary>
public interface IInventory
{
    IReadOnlyList<ItemData> Items { get; }
    int Count(ItemData item);
    bool HasItem(ItemData item);
    bool RemoveItem(ItemData item);
    void AddItem(ItemData item);
}
