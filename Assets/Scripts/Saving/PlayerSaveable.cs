using UnityEngine;

/// <summary>
/// Captures and restores the player: vitals, position and inventory (plan Phase 2,
/// §5.5). Lives on the Player prefab next to the systems it saves; the hotbar UI is
/// resolved from the scene because it lives in the UI canvas, not the player prefab.
/// The two containers go through <see cref="ContainerSaveUtility"/>, shared with the
/// chests. Restore runs after Start() (SaveManager guarantees this), so the setters win
/// over the max-value resets in the vitals systems.
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
            hotbar = ContainerSaveUtility.Capture(inventory != null ? inventory.Hotbar : null),
            backpack = ContainerSaveUtility.Capture(inventory != null ? inventory.Backpack : null),
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
            ContainerSaveUtility.Restore(inventory.Hotbar, saved.hotbar, this);
            ContainerSaveUtility.Restore(inventory.Backpack, saved.backpack, this);
        }

        // After the containers, so the selection highlight lands on restored content.
        var hotbarUI = FindFirstObjectByType<HotbarUI>();
        if (hotbarUI != null) hotbarUI.SetSelectedIndex(saved.selectedHotbarIndex);
    }

}
