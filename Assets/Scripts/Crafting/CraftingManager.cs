using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Provides crafting validation and ingredient consumption for recipes.
/// </summary>
public class CraftingManager : MonoBehaviour
{
    [SerializeField] private PlayerInventory playerInventory;

    /// <summary>
    /// Player inventory instance used as the crafting data source.
    /// </summary>
    public IInventory PlayerInventory => playerInventory;

    /// <summary>
    /// Checks whether the inventory contains all ingredients required by the recipe.
    /// </summary>
    public static bool HasAllIngredients(RecipeData recipe, IInventory inv)
    {
        if (recipe == null || recipe.ingredients == null || inv == null) return false;
        foreach (var ing in recipe.ingredients)
        {
            if (ing?.item == null) continue;
            if (inv.Count(ing.item) < ing.count) return false;
        }
        return true;
    }

    /// <summary>
    /// Removes (consumes) all ingredients required by the recipe from the inventory.
    /// </summary>
    public static void ConsumeIngredients(RecipeData recipe, IInventory inv)
    {
        if (recipe?.ingredients == null || inv == null) return;
        foreach (var ing in recipe.ingredients)
        {
            if (ing?.item == null) continue;
            for (int i = 0; i < ing.count; i++)
                inv.RemoveItem(ing.item);
        }
    }

    /// <summary>
    /// Returns the first recipe that the inventory can craft, or <c>null</c> if none match.
    /// </summary>
    public static RecipeData GetFirstCraftable(IReadOnlyList<RecipeData> recipes, IInventory inv)
    {
        if (inv == null || recipes == null) return null;
        foreach (var r in recipes)
            if (r != null && HasAllIngredients(r, inv)) return r;
        return null;
    }
}
