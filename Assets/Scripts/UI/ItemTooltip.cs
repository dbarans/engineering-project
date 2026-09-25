using UnityEngine;
using TMPro;

/// <summary>
/// Singleton UI panel that displays detailed item information (name and description)
/// at the cursor's screen position when hovering over inventory or crafting slots.
/// </summary>
public class ItemTooltip : MonoBehaviour
{
    private static ItemTooltip _instance;

    /// <summary>
    /// Global access to the active or inactive <see cref="ItemTooltip"/> instance in the scene.
    /// </summary>
    public static ItemTooltip Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<ItemTooltip>(FindObjectsInactive.Include);
            }
            return _instance;
        }
    }

    [Tooltip("Text component used to display the item's display name.")]
    [SerializeField] private TMP_Text nameLabel;

    [Tooltip("Text component used to display the item's optional description.")]
    [SerializeField] private TMP_Text descriptionLabel;

    [Tooltip("Screen-space pixel offset applied relative to the cursor position.")]
    [SerializeField] private Vector2 offset = new Vector2(25f, -25f);

    private RectTransform _rectTransform;
    private Canvas _canvas;

    private void Awake()
    {
        _instance = this;
        InitComponents();
    }

    private void InitComponents()
    {
        if (_rectTransform == null) _rectTransform = GetComponent<RectTransform>();
        if (_canvas == null) _canvas = GetComponentInParent<Canvas>(true);
    }

    /// <summary>
    /// Populates and displays the tooltip for the specified item at the given screen position.
    /// </summary>
    /// <param name="data">The item data asset containing display values.</param>
    /// <param name="screenPos">The current screen-space pointer position.</param>
    public void Show(ItemData data, Vector2 screenPos)
    {
        if (data == null) return;

        InitComponents();

        if (nameLabel != null)
            nameLabel.text = data.itemName ?? "";

        if (descriptionLabel != null)
        {
            bool hasDesc = !string.IsNullOrWhiteSpace(data.description);
            descriptionLabel.text = hasDesc ? data.description : "";
            descriptionLabel.gameObject.SetActive(hasDesc);
        }

        gameObject.SetActive(true);
        transform.SetAsLastSibling();

        if (_canvas != null && _rectTransform != null)
        {
            Vector2 targetPos = screenPos + offset;
            Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _canvas.transform as RectTransform, targetPos, cam, out Vector2 localPoint))
            {
                _rectTransform.localPosition = localPoint;
            }
        }
    }

    /// <summary>
    /// Hides and deactivates the tooltip panel.
    /// </summary>
    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void OnDisable()
    {
        Hide();
    }
}