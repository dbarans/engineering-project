using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// A single inventory item entity: one icon + a quantity badge representing one
/// <see cref="ItemStack"/>. It lives as a child of a <see cref="SlotView"/> (while
/// sitting in a slot) or of the <see cref="HeldItemController"/> (while carried on
/// the cursor). Its graphics do not block raycasts, so clicks pass through to the
/// slot frame underneath.
///
/// Durable items (a melee weapon) also show their wear as a red bar drawn
/// <i>behind</i> the icon: it grows vertically from the bottom as hits are spent,
/// filling the whole icon square once every hit has been used (durability 0). The
/// value travels with the entity as it moves between slots and the cursor.
/// </summary>
public class InventoryItem : MonoBehaviour
{
    // Opacity of the red wear bar behind the icon.
    private const float FillAlpha = 0.85f;

    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text countLabel;
    [Tooltip("Red wear bar drawn behind the icon for durable items. " +
             "Created automatically at runtime when left unassigned.")]
    [SerializeField] private Image durabilityFill;

    /// <summary>The item this entity represents.</summary>
    public ItemData Item { get; private set; }

    /// <summary>How many units this entity represents.</summary>
    public int Count { get; private set; }

    /// <summary>Remaining durability carried with this entity (see <see cref="ItemStack.CurrentDurability"/>).</summary>
    public int Durability { get; private set; }

    /// <summary>Sets the stack this entity shows and refreshes its visuals.</summary>
    public void SetStack(ItemData item, int count, int durability)
    {
        Item = item;
        Count = count;
        Durability = durability;
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

        RefreshDurability();
    }

    /// <summary>
    /// Sizes the wear bar to the fraction of durability already spent: 0 (hidden) at full
    /// durability, the full icon square once every hit is used. Anchored to the bottom so it
    /// grows upward, and independent of the current slot size.
    /// </summary>
    private void RefreshDurability()
    {
        bool durable = Item != null && Item.maxDurability > 0;
        int max = durable ? Mathf.Max(1, Item.maxDurability) : 0;
        float usedFraction = durable ? Mathf.Clamp01((max - Durability) / (float)max) : 0f;
        bool show = durable && usedFraction > 0f;

        if (show && durabilityFill == null) durabilityFill = CreateDurabilityFill();
        if (durabilityFill == null) return;

        durabilityFill.enabled = show;
        if (!show) return;

        var rt = durabilityFill.rectTransform;
        rt.anchorMin = new Vector2(0f, 0f);
        rt.anchorMax = new Vector2(1f, usedFraction); // height = spent fraction of the icon
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// Builds the red wear bar as the first child so it draws behind the icon. Only created
    /// the first time a durable item is shown.
    /// </summary>
    private Image CreateDurabilityFill()
    {
        var go = new GameObject("DurabilityFill", typeof(RectTransform));
        go.layer = gameObject.layer;
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(transform, false);
        rt.SetAsFirstSibling(); // behind the icon, so it reads as a background bar

        var img = go.AddComponent<Image>();
        img.raycastTarget = false;
        img.color = new Color(1f, 0.12f, 0.1f, FillAlpha);
        return img;
    }
}
