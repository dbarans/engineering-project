using UnityEngine;

[System.Serializable]
public class RecipeIngredient
{
    public ItemData item;
    public int count = 1;
}

[CreateAssetMenu(fileName = "New Recipe", menuName = "Items/Recipe")]
public class RecipeData : ScriptableObject
{
    public RecipeIngredient[] ingredients = new RecipeIngredient[0];
    public ItemData outputItem;
}
