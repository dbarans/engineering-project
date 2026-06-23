using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A single inventory item entity: one icon + a quantity badge representing one
/// <see cref="ItemStack"/>. It lives as a child of a <see cref="SlotView"/> (while
/// sitting in a slot) or of the <see cref="HeldItemController"/> (while carried on
/// the cursor). Its graphics do not block raycasts, so clicks pass through to the
/// slot frame underneath.
/// </summary>
public class InventoryItem : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text countLabel;

    /// <summary>The item this entity represents.</summary>
    public ItemData Item { get; private set; }

    /// <summary>How many units this entity represents.</summary>
    public int Count { get; private set; }

    /// <summary>Sets the stack this entity shows and refreshes its visuals.</summary>
    public void SetStack(ItemData item, int count)
    {
        Item = item;
        Count = count;
        Refresh();
    }

    private void Refresh()
    {
        bool hasIcon = Item != null && Item.icon != null;
        if (iconImage != null)
        {
            iconImage.sprite = hasIcon ? Item.icon : null;
            iconImage.enabled = hasIcon;
        }

        if (countLabel != null)
        {
            bool showCount = Item != null && Count > 1;
            countLabel.text = showCount ? Count.ToString() : string.Empty;
            countLabel.enabled = showCount;
        }
    }
}
