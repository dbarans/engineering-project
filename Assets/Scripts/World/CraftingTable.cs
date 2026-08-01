using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A workbench the player walks up to. While the player stands in range, this table
/// adds its own <see cref="recipes"/> to the shared <see cref="CraftingBoxUI"/> (the
/// grid opened with Tab), so the box offers more recipes near a table than away from
/// one. Walking out of range removes them again.
///
/// The <see cref="recipes"/> list <b>is</b> the "which recipes are visible on this
/// table" marking: only the recipes assigned here appear when this table is in range.
/// Basic recipes that should always be craftable stay on the <see cref="CraftingBoxUI"/>
/// itself; table-gated recipes go here.
///
/// Detection follows the <see cref="ChestInteractable"/> convention: a trigger
/// <see cref="Collider2D"/> sized to the approach zone, firing on the player collider
/// (tagged "Player"). Requires the player to carry a <see cref="Rigidbody2D"/>, which it
/// does.
///
/// The table also blocks movement, set up like the Barrel prefab (see CraftingTableSetup):
/// the ObstaclePathOnly layer, a solid collider that stops the player, and the FOV-masked
/// material so the vision system clips it at runtime. The padded approach trigger lives on
/// the same object and still fires, since that layer collides with the player.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class CraftingTable : MonoBehaviour
{
    [Tooltip("Recipes this table adds to the crafting box while the player is in range. " +
             "This is the per-table \"visible recipes\" marking.")]
    [SerializeField] private List<RecipeData> recipes = new List<RecipeData>();

    [Tooltip("Crafting box these recipes are added to. Auto-resolved if left unset.")]
    [SerializeField] private CraftingBoxUI craftingBox;

    private bool _playerInRange;

    /// <summary>The recipes this table exposes while in range.</summary>
    public IReadOnlyList<RecipeData> Recipes => recipes;

    private void Awake()
    {
        if (craftingBox == null)
            craftingBox = FindFirstObjectByType<CraftingBoxUI>();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_playerInRange || !other.CompareTag("Player")) return;
        _playerInRange = true;
        if (craftingBox != null) craftingBox.AddTable(this);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!_playerInRange || !other.CompareTag("Player")) return;
        _playerInRange = false;
        if (craftingBox != null) craftingBox.RemoveTable(this);
    }

    // Leaving the table's recipes registered after it is disabled/destroyed would keep
    // them in the box with no way to remove them, so pull them back out defensively.
    private void OnDisable()
    {
        if (!_playerInRange) return;
        _playerInRange = false;
        if (craftingBox != null) craftingBox.RemoveTable(this);
    }
}
