using UnityEngine;
using UnityEngine.UI;

public class HotbarSlotUI : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private Image selectionHighlight;

    public void Setup(ItemData item)
    {
        if (iconImage == null) return;
        bool hasIcon = item != null && item.icon != null;
        iconImage.sprite = hasIcon ? item.icon : null;
        iconImage.enabled = hasIcon;
    }

    public void SetSelected(bool selected)
    {
        if (selectionHighlight != null)
            selectionHighlight.enabled = selected;
    }
}
