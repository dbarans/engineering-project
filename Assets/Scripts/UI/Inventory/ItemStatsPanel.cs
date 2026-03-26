using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Mouse-hover stats panel for item details.
/// </summary>
public class ItemStatsPanel : MonoBehaviour
    {
        public static ItemStatsPanel Instance { get; private set; }


        [SerializeField] private TMP_Text itemNameText;
        [SerializeField] private Image iconImage;

        [SerializeField] private TMP_Text valueValueText;

        [SerializeField] private float marginFromCursor = 12f;

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

        private static void SetStatRow(TMP_Text valueField, int value)
        {
            if (valueField == null) return;
            GameObject row = valueField.transform.parent != null ? valueField.transform.parent.gameObject : valueField.gameObject;
            bool show = value != 0;
            row.SetActive(show);
            if (show)
                valueField.text = value.ToString();
        }

        /// <summary>
        /// Updates the panel content for the provided item.
        /// </summary>
        public void Setup(ItemData item)
        {
            if (item == null)
            {
                if (itemNameText != null) itemNameText.text = "";
                if (iconImage != null) iconImage.enabled = false;
                if (valueValueText != null && valueValueText.transform.parent != null) valueValueText.transform.parent.gameObject.SetActive(false);
                return;
            }

            if (itemNameText != null) itemNameText.text = item.itemName ?? "";
            if (iconImage != null)
            {
                iconImage.enabled = item.icon != null;
                if (item.icon != null) iconImage.sprite = item.icon;
            }
            SetStatRow(valueValueText, item.value);
        }

        /// <summary>
        /// Shows the panel for the provided item and positions it near the cursor.
        /// </summary>
        public void ShowAt(ItemData item, Vector2 screenPosition)
        {
            if (item == null) { Hide(); return; }
            if (_rect == null) _rect = GetComponent<RectTransform>();
            if (_canvas == null) _canvas = GetComponentInParent<Canvas>();
            if (_canvas == null || _rect == null) return;

            Setup(item);
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

        /// <summary>
        /// Hides the stats panel.
        /// </summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }
    }
