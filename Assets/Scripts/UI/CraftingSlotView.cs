using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

/// <summary>
/// One crafting recipe shown in the crafting box. Draws the recipe's output icon,
/// dims it when the backpack lacks the ingredients, and on click crafts the output —
/// consuming the ingredients from the backpack and placing the result back into the
/// backpack.
///
/// Mirrors <see cref="SlotView"/> but is recipe-driven, not storage-driven: nothing is
/// ever dropped *into* a crafting slot, so it uses its own icon directly rather than the
/// re-parented <see cref="InventoryItem"/> entity that backpack/hotbar slots host.
/// </summary>
[RequireComponent(typeof(Image))]
public class CraftingSlotView : MonoBehaviour, IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text countLabel;

    [Tooltip("Icon alpha used when the recipe cannot currently be crafted.")]
    [SerializeField, Range(0f, 1f)] private float dimmedAlpha = 0.35f;

    private RecipeData _recipe;
    private ItemContainer _source;

    /// <summary>The recipe this slot crafts, or <c>null</c> when unused.</summary>
    public RecipeData Recipe => _recipe;

    /// <summary>Binds this slot to a recipe and the backpack it crafts from/into.</summary>
    public void Bind(RecipeData recipe, ItemContainer source)
    {
        _recipe = recipe;
        _source = source;
        SetupVisuals();
        Refresh();
    }

    private void SetupVisuals()
    {
        var output = _recipe != null ? _recipe.outputItem : null;
        if (iconImage != null)
        {
            bool hasIcon = output != null && output.icon != null;
            iconImage.sprite = hasIcon ? output.icon : null;
            iconImage.enabled = hasIcon;
        }
        if (countLabel != null)
        {
            // Single-output recipes: no quantity badge to show.
            countLabel.text = string.Empty;
            countLabel.enabled = false;
        }
    }

    /// <summary>Re-evaluates craftability and dims the icon when ingredients are missing.</summary>
    public void Refresh()
    {
        if (iconImage == null) return;
        bool craftable = SlotCrafting.CanCraft(_recipe, _source);
        var c = iconImage.color;
        c.a = craftable ? 1f : dimmedAlpha;
        iconImage.color = c;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_recipe == null || _recipe.outputItem == null || _source == null) return;
        if (!SlotCrafting.CanCraft(_recipe, _source)) return;          // missing ingredients
        if (!_source.CanAdd(_recipe.outputItem, 1)) return;            // no room for the result

        if (SlotCrafting.Consume(_recipe, _source))
            _source.TryAddItem(_recipe.outputItem, 1);

        // The backpack's SlotChanged drives a box-wide refresh, but refresh self
        // immediately so the dim state can't lag a frame behind the consumed stack.
        Refresh();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        var output = _recipe != null ? _recipe.outputItem : null;
        ItemStatsPanel.Instance?.ShowAt(output, _recipe, eventData.position);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        ItemStatsPanel.Instance?.Hide();
    }

    private void OnDisable()
    {
        ItemStatsPanel.Instance?.Hide();
    }
}
