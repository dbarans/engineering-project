using UnityEngine;

/// <summary>
/// An item lying on the ground in the world. Spawned by <see cref="WorldItemPickup"/>
/// when the player drops something out of the cursor. Its icon is deliberately the
/// generic "unknown" sprite (baked onto the prefab's <see cref="SpriteRenderer"/>),
/// so every drop looks the same; the item's real identity is only revealed as a name
/// on the cursor (via <see cref="CursorController"/>) while it is hovered.
///
/// The entity carries a single <see cref="ItemData"/> + count. It is a plain world
/// object (SpriteRenderer + trigger Collider2D), independent from the slot UI.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class WorldItem : MonoBehaviour
{
    // Serialized fields behind read-only properties, and the serialisation is the point.
    // These were auto-properties, which Unity does not serialise at all: the drop showed an
    // empty component in the inspector, and — the part that actually broke things — a drop
    // baked into a scene came back with no item after a domain reload. The Dungeon scene is
    // authored by baking, so every piece of generated floor loot in it was an empty pickup
    // that WorldItemsSaveable then skipped for having no item.
    [Tooltip("What is lying here. Set through SetStack; shown for inspection and baked into " +
             "the scene with it.")]
    [SerializeField] private ItemData item;

    [SerializeField] private int count;

    [SerializeField] private int durability = -1;

    /// <summary>The item this drop represents.</summary>
    public ItemData Item => item;

    /// <summary>How many units are lying here.</summary>
    public int Count => count;

    /// <summary>
    /// Remaining durability the drop carries, <c>-1</c> meaning full/undamaged (same
    /// sentinel as <see cref="ItemStack.durability"/>). Without it the ground would be a
    /// free repair bench: dropping a worn weapon and picking it straight back up would
    /// hand it back at full durability.
    /// </summary>
    public int Durability => durability;

    // Earliest time this drop may be picked up; blocks the same click that dropped it
    // from instantly scooping it back up.
    private float _pickableAt;

    /// <summary>True once the drop's pickup delay (if any) has elapsed.</summary>
    public bool CanPickUp => Time.time >= _pickableAt;

    /// <summary>
    /// Sets the item/count this drop holds, plus the remaining durability that travels
    /// with it (<c>-1</c> = full, the right value for anything that does not wear).
    /// </summary>
    public void SetStack(ItemData item, int count, int durability = -1)
    {
        this.item = item;
        this.count = Mathf.Max(0, count);
        this.durability = durability;

#if UNITY_EDITOR
        // The generator stocks drops while baking the Dungeon scene in edit mode, and a
        // change made then is only written to the scene file if the object is marked dirty
        // — the same trap ChestInventory.SetStartingItems and SimpleDoor.RequireKey hit.
        if (!Application.isPlaying) UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    /// <summary>Prevents pickup for <paramref name="delay"/> seconds from now.</summary>
    public void ArmPickupDelay(float delay)
    {
        _pickableAt = Time.time + Mathf.Max(0f, delay);
    }
}
