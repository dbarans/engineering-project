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
