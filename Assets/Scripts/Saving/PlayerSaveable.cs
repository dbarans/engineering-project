using UnityEngine;

/// <summary>
/// Captures and restores the player: vitals, position and inventory (plan Phase 2,
/// §5.5). Lives on the Player prefab next to the systems it saves; the hotbar UI is
/// resolved from the scene because it lives in the UI canvas, not the player prefab.
/// Containers are stored as item ids via <see cref="ItemDatabase"/> — never asset
/// references. Restore runs after Start() (SaveManager guarantees this), so the
/// setters win over the max-value resets in the vitals systems.
/// </summary>
public class PlayerSaveable : MonoBehaviour, ISaveable
{
    public void Capture(GameSaveData data)
    {
        var health = GetComponent<PlayerHealthSystem>();
        var stamina = GetComponent<PlayerStaminaSystem>();
        var inventory = GetComponent<SlotInventory>();
        var hotbarUI = FindFirstObjectByType<HotbarUI>();

        data.player = new PlayerSaveData
        {
            position = new[] { transform.position.x, transform.position.y },
            health = health != null ? health.GetCurrentHealth() : 0,
            stamina = stamina != null ? stamina.CurrentStamina : 0f,
            hotbar = Capture(inventory != null ? inventory.Hotbar : null),
            backpack = Capture(inventory != null ? inventory.Backpack : null),
            selectedHotbarIndex = hotbarUI != null ? hotbarUI.SelectedIndex : 0
        };
    }

    public void Restore(GameSaveData data)
    {
        var saved = data.player;
        if (saved == null) return; // save predates player data — leave the fresh scene alone

        if (saved.position != null && saved.position.Length >= 2)
        {
            transform.position = new Vector3(
                saved.position[0], saved.position[1], transform.position.z);
            var body = GetComponent<Rigidbody2D>();
            if (body != null) body.linearVelocity = Vector2.zero;
        }

        var health = GetComponent<PlayerHealthSystem>();
        if (health != null) health.SetHealth(saved.health);

        var stamina = GetComponent<PlayerStaminaSystem>();
        if (stamina != null) stamina.SetStamina(saved.stamina);

        var inventory = GetComponent<SlotInventory>();
        if (inventory != null)
        {
            Restore(inventory.Hotbar, saved.hotbar);
            Restore(inventory.Backpack, saved.backpack);
        }

        // After the containers, so the selection highlight lands on restored content.
        var hotbarUI = FindFirstObjectByType<HotbarUI>();
        if (hotbarUI != null) hotbarUI.SetSelectedIndex(saved.selectedHotbarIndex);
    }

    private static ContainerSaveData Capture(ItemContainer container)
    {
        if (container == null) return null;

        var slots = new SlotSaveData[container.SlotCount];
        for (int i = 0; i < slots.Length; i++)
        {
            var stack = container.Get(i);
            bool occupied = stack != null && !stack.IsEmpty;
            slots[i] = new SlotSaveData
            {
                itemId = occupied ? stack.item.Id : null,
                count = occupied ? stack.count : 0,
                durability = occupied && stack.HasDurability ? stack.CurrentDurability : -1
            };
        }
        return new ContainerSaveData { slots = slots };
    }

    /// <summary>
    /// The save is the source of truth for the whole container: every slot is
    /// overwritten, so items seeded after scene load (e.g. debug fill) don't leak
    /// into a restored game.
    /// </summary>
    private void Restore(ItemContainer container, ContainerSaveData saved)
    {
        if (container == null || saved?.slots == null) return;

        var database = ItemDatabase.Instance;
        for (int i = 0; i < container.SlotCount; i++)
        {
            var slot = i < saved.slots.Length ? saved.slots[i] : null;

            ItemData item = null;
            if (slot != null && !string.IsNullOrEmpty(slot.itemId))
            {
                item = database != null ? database.Resolve(slot.itemId) : null;
                if (item == null)
                    Debug.LogWarning(
                        $"[PlayerSaveable] Item id '{slot.itemId}' from the save is not in " +
                        "the ItemDatabase (asset removed?) — slot left empty.", this);
            }

            container.Set(i, item, item != null ? slot.count : 0, item != null ? slot.durability : -1);
        }
    }
}
