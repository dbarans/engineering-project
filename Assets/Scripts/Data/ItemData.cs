using UnityEngine;

public enum WeaponType
{
    None,
    Melee,
    Ranged
}

[CreateAssetMenu(fileName = "New Item", menuName = "Items/Item Data")]
public class ItemData : ScriptableObject
{
    [Tooltip("Stable identifier used by the save system and item equality. Auto-generated; never edit or reuse.")]
    [SerializeField] private string id;

    public Sprite icon;
    public string itemName;

    public int value;

    /// <summary>
    /// Stable unique id of this item. Saves store this instead of asset references;
    /// <see cref="ItemDatabase.Resolve"/> maps it back to the asset.
    /// </summary>
    public string Id => id;

    [Tooltip("Maximum number of this item that can occupy a single slot. 1 = non-stackable.")]
    [Min(1)]
    public int maxStack = 1;

    [Tooltip("Which combat style this item activates while selected in the hotbar. None = not a weapon.")]
    public WeaponType weaponType = WeaponType.None;

    [Tooltip("Health restored when one unit of this item is used from the hotbar (hold the prepare " +
             "button, see PlayerItemUse). 0 = not a healing item and holding the button does nothing.")]
    [Min(0)]
    public int healAmount = 0;

    [Tooltip("How far this item lights the ground around the player while it is the selected " +
             "hotbar item, in world units. 0 = not a light source. This is what marks an item as " +
             "a torch; HeldTorch reads it and feeds the player's field of view.")]
    [Min(0f)]
    public float lightRadius = 0f;

    [Tooltip("Colour the light cast by this item burns with. Only used when Light Radius is " +
             "above 0.")]
    public Color lightColor = new Color(1f, 0.72f, 0.36f, 1f);

    [Tooltip("Durability ceiling for a wearable item such as a melee weapon: how many hits it lasts " +
             "before it is depleted. 0 = no durability (the item never wears out). Each connecting melee " +
             "hit spends one point; a depleted weapon still swings but does much less damage until repaired.")]
    [Min(0)]
    public int maxDurability = 0;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // New assets get their id here; the database builder repairs duplicates
        // (OnValidate alone cannot see them — a duplicated asset copies its id).
        if (string.IsNullOrEmpty(id))
        {
            id = System.Guid.NewGuid().ToString("N");
            UnityEditor.EditorUtility.SetDirty(this);
        }
    }
#endif
}
