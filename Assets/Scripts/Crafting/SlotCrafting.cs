/// <summary>
/// Crafting rules for the slot-based backpack. Operates directly on an
/// <see cref="ItemContainer"/> (the player's backpack) instead of the legacy
/// <see cref="IInventory"/>, so it is fully independent from the existing
/// <see cref="CraftingManager"/> / <see cref="CraftStation"/> flow. Recipes are the
/// same <see cref="RecipeData"/> assets, used here purely as data.
/// </summary>
public static class SlotCrafting
{
    /// <summary>
    /// True when <paramref name="source"/> holds every ingredient (in the required
    /// quantity) and the recipe produces an output.
    /// </summary>
    public static bool CanCraft(RecipeData recipe, ItemContainer source)
    {
        if (recipe == null || recipe.outputItem == null || recipe.ingredients == null || source == null)
            return false;

        foreach (var ing in recipe.ingredients)
        {
            if (ing?.item == null) continue;
            if (source.Count(ing.item) < ing.count) return false;
        }
        return true;
    }

    /// <summary>
    /// Removes the recipe's ingredients from <paramref name="source"/>. Returns
    /// <c>false</c> (removing nothing) when not all ingredients are present, so the
    /// check and the consumption never disagree.
    /// </summary>
    public static bool Consume(RecipeData recipe, ItemContainer source)
    {
        if (!CanCraft(recipe, source)) return false;

        foreach (var ing in recipe.ingredients)
        {
            if (ing?.item == null) continue;
            source.TryRemove(ing.item, ing.count);
        }
        return true;
    }
}
