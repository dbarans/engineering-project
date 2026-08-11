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
    /// <summary>The item this drop represents.</summary>
    public ItemData Item { get; private set; }

    /// <summary>How many units are lying here.</summary>
    public int Count { get; private set; }

    /// <summary>
    /// Remaining durability the drop carries, <c>-1</c> meaning full/undamaged (same
    /// sentinel as <see cref="ItemStack.durability"/>). Without it the ground would be a
    /// free repair bench: dropping a worn weapon and picking it straight back up would
    /// hand it back at full durability.
    /// </summary>
    public int Durability { get; private set; } = -1;

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
        Item = item;
        Count = Mathf.Max(0, count);
        Durability = durability;
    }

    /// <summary>Prevents pickup for <paramref name="delay"/> seconds from now.</summary>
    public void ArmPickupDelay(float delay)
    {
        _pickableAt = Time.time + Mathf.Max(0f, delay);
    }
}
