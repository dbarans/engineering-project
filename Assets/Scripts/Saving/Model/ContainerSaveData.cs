using System;

/// <summary>One inventory slot: item id (via <see cref="ItemDatabase"/>) + count. Null/empty id = empty slot.</summary>
[Serializable]
public class SlotSaveData
{
    public string itemId;
    public int count;
}

/// <summary>Fixed-size item container (hotbar, backpack); array index = slot index.</summary>
[Serializable]
public class ContainerSaveData
{
    public SlotSaveData[] slots;
}
