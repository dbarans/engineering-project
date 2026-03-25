using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Displays crafting recipes and required ingredient counts for a crafting station.
/// </summary>
public class CraftingStationUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform recipeListContent;
    [SerializeField] private GameObject recipeRowPrefab;
    [SerializeField] private Transform requiredIngredientsContent;
    [SerializeField] private GameObject requiredIngredientRowPrefab;
    [SerializeField] private Button craftButton;

    /// <summary>
    /// Fired when the crafting panel is hidden.
    /// </summary>
    public event Action OnHidden;

    private int _selectedIndex;
    private readonly List<RecipeData> _recipes = new List<RecipeData>();
    private Func<RecipeData, IInventory, bool> _tryCraft;
    private IInventory _inventory;
    private readonly List<GameObject> _spawnedRecipeRows = new List<GameObject>();
    private readonly List<GameObject> _spawnedIngredientRows = new List<GameObject>();

    private void Awake()
    {
        // Fallback if panelRoot isn't assigned.
        if (panelRoot == null) panelRoot = gameObject;
        if (panelRoot != null) panelRoot.SetActive(false);
        if (craftButton != null) craftButton.onClick.AddListener(OnCraftClicked);
    }

    private void Update()
    {
        if (panelRoot != null && panelRoot.activeSelf && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            Hide();
    }

    /// <summary>
    /// Shows the list of recipes and wires ingredient/craft validation.
    /// </summary>
    public void Show(IReadOnlyList<RecipeData> recipes, IInventory inventory, Func<RecipeData, IInventory, bool> tryCraft)
    {
        _tryCraft = tryCraft;
        _inventory = inventory;
        gameObject.SetActive(true);
        _recipes.Clear();
        if (recipes != null)
            foreach (var r in recipes)
                if (r != null) _recipes.Add(r);

        ClearRecipeRows();
        if (recipeListContent != null && recipeRowPrefab != null)
        {
            for (int i = 0; i < _recipes.Count; i++)
            {
                var recipe = _recipes[i];
                var row = Instantiate(recipeRowPrefab, recipeListContent);
                _spawnedRecipeRows.Add(row);
                var rowScript = row.GetComponent<RecipeRow>();
                if (rowScript != null)
                    rowScript.Setup(recipe);
                var btn = row.GetComponent<Button>();
                if (btn != null)
                {
                    int index = i;
                    btn.onClick.AddListener(() => SelectRecipe(index));
                }
            }
        }

        _selectedIndex = -1;
        RefreshDetail();

        if (panelRoot != null) panelRoot.SetActive(true);
    }

    private void ClearRecipeRows()
    {
        foreach (var go in _spawnedRecipeRows)
        {
            if (go != null) Destroy(go);
        }
        _spawnedRecipeRows.Clear();
    }

    /// <summary>
    /// Hides the crafting panel and clears spawned rows.
    /// </summary>
    public void Hide()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
        else gameObject.SetActive(false);
        _tryCraft = null;
        _inventory = null;
        ClearRecipeRows();
        ClearRequiredIngredientsPanel();
        OnHidden?.Invoke();
    }

    private void SelectRecipe(int index)
    {
        if (index >= 0 && index < _recipes.Count)
        {
            _selectedIndex = index;
            RefreshDetail();
        }
    }

    private void RefreshDetail()
    {
        var inv = _inventory;
        ClearRequiredIngredientsPanel();

        if (_selectedIndex < 0 || _selectedIndex >= _recipes.Count)
        {
            if (craftButton != null) craftButton.interactable = false;
            return;
        }

        var recipe = _recipes[_selectedIndex];
        if (recipe == null) return;

        var required = GetRequiredCounts(recipe);
        if (requiredIngredientsContent != null && requiredIngredientRowPrefab != null)
        {
            foreach (var (ingredientItem, requiredCount) in required)
            {
                var row = Instantiate(requiredIngredientRowPrefab, requiredIngredientsContent);
                _spawnedIngredientRows.Add(row);
                var rowScript = row.GetComponent<RequiredIngredientRow>();
                if (rowScript != null)
                    rowScript.Setup(ingredientItem, requiredCount, inv != null ? inv.Count(ingredientItem) : 0);
            }
        }

        if (craftButton != null)
            craftButton.interactable = inv != null && CraftingManager.HasAllIngredients(recipe, inv);
    }

    private void ClearRequiredIngredientsPanel()
    {
        foreach (var go in _spawnedIngredientRows)
        {
            if (go != null) Destroy(go);
        }
        _spawnedIngredientRows.Clear();
    }

    private static List<(ItemData item, int count)> GetRequiredCounts(RecipeData recipe)
    {
        var list = new List<(ItemData, int)>();
        if (recipe?.ingredients == null) return list;
        foreach (var ing in recipe.ingredients)
        {
            if (ing?.item == null) continue;
            list.Add((ing.item, ing.count));
        }
        return list;
    }

    private void OnCraftClicked()
    {
        if (_tryCraft == null || _selectedIndex < 0 || _selectedIndex >= _recipes.Count) return;
        var inv = _inventory;
        if (inv == null) return;
        var recipe = _recipes[_selectedIndex];
        if (!CraftingManager.HasAllIngredients(recipe, inv)) return;
        if (_tryCraft(recipe, inv))
            RefreshDetail();
    }
}
