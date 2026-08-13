using System.Collections.Generic;
using UnityEngine;
using TMPro;

/// <summary>
/// Manages the user interface window for viewing and looting corpse inventories.
/// Instantiates, binds, and refreshes UI slot views based on the active container.
/// </summary>
public class CorpseUI : MonoBehaviour
{
    public static CorpseUI Instance { get; private set; }

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform slotsContainer;
    [SerializeField] private GameObject slotPrefab;
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private HeldItemController heldItem;

    private readonly List<SlotView> _slots = new List<SlotView>();
    private ItemContainer _container;

    private void Awake()
    {
        Instance = this;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Start()
    {
        if (heldItem == null) heldItem = FindFirstObjectByType<HeldItemController>();
    }

    /// <summary>
    /// Displays the corpse UI panel, sets the window title, and binds the provided item container.
    /// </summary>
    /// <param name="container">The corpse inventory container to display.</param>
    /// <param name="title">Optional window title header.</param>
    public void Show(ItemContainer container, string title = "Corpse")
    {
        if (titleText != null) titleText.text = title;
        Bind(container);
        if (panelRoot != null) panelRoot.SetActive(true);
    }

    /// <summary>
    /// Unbinds the active container and hides the corpse UI panel.
    /// </summary>
    public void Hide()
    {
        if (_container != null)
            _container.SlotChanged -= OnSlotChanged;
        _container = null;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    /// <summary>
    /// Binds the UI slots to the container, instantiating additional slot prefabs if required.
    /// </summary>
    /// <param name="container">The container whose slots will be populated in the UI.</param>
    private void Bind(ItemContainer container)
    {
        if (_container != null)
            _container.SlotChanged -= OnSlotChanged;
        _container = container;

        if (_slots.Count == 0 && slotsContainer != null)
        {
            foreach (Transform child in slotsContainer)
            {
                var view = child.GetComponent<SlotView>();
                if (view != null) _slots.Add(view);
            }
        }

        while (_slots.Count < _container.SlotCount && slotPrefab != null && slotsContainer != null)
        {
            var go = Instantiate(slotPrefab, slotsContainer);
            var view = go.GetComponent<SlotView>();
            if (view == null) break;
            _slots.Add(view);
        }

        for (int i = 0; i < _slots.Count; i++)
        {
            bool inRange = i < _container.SlotCount;
            _slots[i].gameObject.SetActive(inRange);
            if (inRange) _slots[i].Bind(_container, i, heldItem);
        }

        _container.SlotChanged += OnSlotChanged;
    }

    private void OnSlotChanged(int index)
    {
        if (index >= 0 && index < _slots.Count)
            _slots[index].Refresh();
    }
}