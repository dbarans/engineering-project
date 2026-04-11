using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World interaction point for crafting stations.
/// </summary>
public class CraftStation : MonoBehaviour, IInteractable
{
    [SerializeField] private List<RecipeData> recipes = new List<RecipeData>();
    [SerializeField] private CraftingManager craftingManager;
    [SerializeField] private CraftingStationUI craftingUI;
    [SerializeField] private InventoryUI inventoryUI;

    private bool _isCrafting;

    private void OnEnable()
    {
        if (craftingUI != null)
            craftingUI.OnHidden += HandleCraftingUIHidden;
    }

    private void OnDisable()
    {
        if (craftingUI != null)
            craftingUI.OnHidden -= HandleCraftingUIHidden;
    }

    private void HandleCraftingUIHidden()
    {
        if (inventoryUI != null)
            inventoryUI.Hide();
    }

    /// <summary>
    /// Opens the crafting UI and prepares ingredient validation.
    /// </summary>
    public virtual void Interact(InteractContext context)
    {
        if (_isCrafting) return;
        if (craftingManager == null) return;

        var inv = craftingManager.PlayerInventory;
        if (inv == null) return;

        if (craftingUI != null)
        {
            craftingUI.Show(recipes, inv, TryStartCraft);
            if (inventoryUI != null) inventoryUI.Show(inv);
        }
    }

    /// <summary>
    /// Validates and consumes ingredients for the provided recipe.
    /// </summary>
    public bool TryStartCraft(RecipeData recipe, IInventory playerInv)
    {
        if (_isCrafting) return false;
        if (recipe == null || recipe.outputItem == null || playerInv == null) return false;
        if (!CraftingManager.HasAllIngredients(recipe, playerInv)) return false;

        _isCrafting = true;
        CraftingManager.ConsumeIngredients(recipe, playerInv);
        playerInv.AddItem(recipe.outputItem);
        _isCrafting = false;
        return true;
    }
}
