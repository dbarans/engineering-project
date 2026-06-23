using UnityEngine;

[CreateAssetMenu(fileName = "New Item", menuName = "Items/Item Data")]
public class ItemData : ScriptableObject
{
    public Sprite icon;
    public string itemName;

    public int value;

    [Tooltip("Maximum number of this item that can occupy a single slot. 1 = non-stackable.")]
    [Min(1)]
    public int maxStack = 1;
}
