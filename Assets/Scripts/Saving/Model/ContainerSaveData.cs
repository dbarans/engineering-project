using System;

/// <summary>One inventory slot: item id (via <see cref="ItemDatabase"/>) + count. Null/empty id = empty slot.</summary>
[Serializable]
public class SlotSaveData
{
    public string itemId;
    public int count;

    /// <summary>
    /// Remaining durability for a durable item (see <see cref="ItemStack.durability"/>).
    /// Defaults to <c>-1</c> ("full/undamaged") so saves written before durability existed
    /// restore weapons at full durability rather than broken.
    /// </summary>
    public int durability = -1;
}

/// <summary>Fixed-size item container (hotbar, backpack); array index = slot index.</summary>
[Serializable]
public class ContainerSaveData
{
    public SlotSaveData[] slots;
}
