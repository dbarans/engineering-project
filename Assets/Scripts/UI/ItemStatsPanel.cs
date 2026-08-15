using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Mouse-hover info panel for an item: its name, its icon and — when the item is a
/// crafting result — the recipe it is made from, one row per ingredient showing that
/// ingredient's icon, name and required count.
///
/// The ingredient rows are built at runtime under <see cref="recipeContainer"/> (a
/// <see cref="GridLayoutGroup"/> places them), so no per-ingredient prefab has to be
/// authored; they inherit their font and colour from <see cref="itemNameText"/>.
/// </summary>
public class ItemStatsPanel : MonoBehaviour
{
    public static ItemStatsPanel Instance { get; private set; }

    [SerializeField] private TMP_Text itemNameText;
    [SerializeField] private Image iconImage;

    [Header("Recipe")]
    [Tooltip("Parent the ingredient rows are spawned under; its GridLayoutGroup lays them out.")]
    [SerializeField] private RectTransform recipeContainer;

    [Tooltip("Caption above the ingredient list. Hidden when the item has no recipe.")]
    [SerializeField] private TMP_Text recipeHeaderText;

    [SerializeField] private float ingredientIconSize = 32f;
    [SerializeField] private float ingredientFontSize = 30f;

    [SerializeField] private float marginFromCursor = 12f;

    /// <summary>Horizontal inset of a row's icon and count, matching the panel's other rows.</summary>
    private const float RowPadding = 20f;
    private const float IconTextGap = 12f;
    private const float CountWidth = 120f;

    private readonly List<GameObject> _ingredientRows = new List<GameObject>();
    private RectTransform _rect;
    private Canvas _canvas;

    private void Awake()
    {
        Instance = this;
        _rect = GetComponent<RectTransform>();
        _canvas = GetComponentInParent<Canvas>();
        Hide();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Updates the panel content for the provided item and the recipe that produces it
    /// (<c>null</c> when the item is not crafted, which just leaves the recipe list empty).
    /// </summary>
    public void Setup(ItemData item, RecipeData recipe = null)
    {
        if (item == null)
        {
            if (itemNameText != null) itemNameText.text = "";
            if (iconImage != null) iconImage.enabled = false;
            BuildRecipe(null);
            return;
        }

        if (itemNameText != null) itemNameText.text = item.itemName ?? "";
        if (iconImage != null)
        {
            iconImage.enabled = item.icon != null;
            if (item.icon != null) iconImage.sprite = item.icon;
        }
        BuildRecipe(recipe);
    }

    /// <summary>
    /// Shows the panel for the provided item and positions it near the cursor.
    /// </summary>
    public void ShowAt(ItemData item, RecipeData recipe, Vector2 screenPosition)
    {
        if (item == null) { Hide(); return; }
        if (_rect == null) _rect = GetComponent<RectTransform>();
        if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
        if (_canvas == null || _rect == null) return;

        Setup(item, recipe);
        gameObject.SetActive(true);

        float scale = _canvas.rootCanvas.scaleFactor;
        float panelW = _rect.rect.width * scale;
        float panelH = _rect.rect.height * scale;
        float margin = marginFromCursor;

        bool cursorLeftHalf = screenPosition.x < Screen.width * 0.5f;
        bool cursorTopHalf = screenPosition.y > Screen.height * 0.5f;

        float centerX = cursorLeftHalf
            ? screenPosition.x + margin + panelW * 0.5f
            : screenPosition.x - margin - panelW * 0.5f;
        float centerY = cursorTopHalf
            ? screenPosition.y - margin - panelH * 0.5f
            : screenPosition.y + margin + panelH * 0.5f;

        centerX = Mathf.Clamp(centerX, panelW * 0.5f, Screen.width - panelW * 0.5f);
        centerY = Mathf.Clamp(centerY, panelH * 0.5f, Screen.height - panelH * 0.5f);

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _canvas.rootCanvas.transform as RectTransform,
            new Vector2(centerX, centerY),
            _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera,
            out Vector2 localPoint);
        _rect.anchoredPosition = localPoint;
    }

    /// <summary>Shows the panel for an item whose recipe is unknown.</summary>
    public void ShowAt(ItemData item, Vector2 screenPosition) => ShowAt(item, null, screenPosition);

    /// <summary>Hides the stats panel.</summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Rebuilds the ingredient list for <paramref name="recipe"/>, hiding the caption when
    /// there is nothing to show.
    /// </summary>
    private void BuildRecipe(RecipeData recipe)
    {
        ClearIngredientRows();

        var ingredients = recipe != null ? recipe.ingredients : null;
        if (recipeContainer != null && ingredients != null)
        {
            foreach (var ing in ingredients)
            {
                if (ing?.item == null || ing.count <= 0) continue;
                _ingredientRows.Add(CreateIngredientRow(ing));
            }
        }

        if (recipeHeaderText != null)
            recipeHeaderText.gameObject.SetActive(_ingredientRows.Count > 0);
    }

    private void ClearIngredientRows()
    {
        foreach (var row in _ingredientRows)
        {
            if (row == null) continue;
            // Detach first: Destroy is deferred, and a still-parented row would be
            // counted by the grid layout for one more rebuild.
            row.transform.SetParent(null, false);
            Destroy(row);
        }
        _ingredientRows.Clear();
    }

    private GameObject CreateIngredientRow(RecipeIngredient ing)
    {
        var row = new GameObject($"Ingredient ({ing.item.itemName})", typeof(RectTransform));
        row.layer = recipeContainer.gameObject.layer;
        var rowRect = (RectTransform)row.transform;
        rowRect.SetParent(recipeContainer, false);

        var iconGo = new GameObject("Icon", typeof(RectTransform));
        iconGo.layer = row.layer;
        var iconRect = (RectTransform)iconGo.transform;
        iconRect.SetParent(rowRect, false);
        iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
        iconRect.pivot = new Vector2(0f, 0.5f);
        iconRect.anchoredPosition = new Vector2(RowPadding, 0f);
        iconRect.sizeDelta = new Vector2(ingredientIconSize, ingredientIconSize);

        var icon = iconGo.AddComponent<Image>();
        icon.raycastTarget = false;
        icon.preserveAspect = true;
        icon.sprite = ing.item.icon;
        icon.enabled = ing.item.icon != null;

        var nameLabel = CreateLabel("Name", rowRect, ing.item.itemName ?? "", TextAlignmentOptions.MidlineLeft);
        var nameRect = nameLabel.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.offsetMin = new Vector2(RowPadding + ingredientIconSize + IconTextGap, 0f);
        nameRect.offsetMax = new Vector2(-(RowPadding + CountWidth), 0f);

        var countLabel = CreateLabel("Count", rowRect, $"x{ing.count}", TextAlignmentOptions.MidlineRight);
        var countRect = countLabel.rectTransform;
        countRect.anchorMin = new Vector2(1f, 0f);
        countRect.anchorMax = new Vector2(1f, 1f);
        countRect.pivot = new Vector2(1f, 0.5f);
        countRect.sizeDelta = new Vector2(CountWidth, 0f);
        countRect.anchoredPosition = new Vector2(-RowPadding, 0f);

        return row;
    }

    private TMP_Text CreateLabel(string name, Transform parent, string text, TextAlignmentOptions alignment)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);

        var label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.alignment = alignment;
        label.fontSize = ingredientFontSize;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        if (itemNameText != null)
        {
            if (itemNameText.font != null) label.font = itemNameText.font;
            label.color = itemNameText.color;
        }
        return label;
    }
}
