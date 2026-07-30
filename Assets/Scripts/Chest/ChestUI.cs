using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Single shared UI panel that displays whichever chest is currently open.
/// Structured like BackpackUI (reuse pre-authored SlotViews, instantiate any
/// shortfall, bind once) rather than BackpackUI's single-fixed-container
/// version, because this panel is shared across multiple chest instances and
/// must rebind to a new container each time a different chest opens.
///
/// Assumes every chest shares the same SlotCount (e.g. all chests are a fixed
/// 3x3) so the slot views built for the first chest can be reused for every
/// later one. If chests vary in size, this needs a rebuild-on-open approach
/// instead.
/// </summary>
public class ChestUI : MonoBehaviour
{
    public static ChestUI Instance { get; private set; }

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform slotsContainer;
    [SerializeField] private GameObject slotPrefab;   // must carry a SlotView, e.g. InventorySlot
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

    public void Show(ItemContainer container)
    {
        Bind(container);
        if (panelRoot != null) panelRoot.SetActive(true);
    }

    public void Hide()
    {
        if (_container != null)
            _container.SlotChanged -= OnSlotChanged;
        _container = null;
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Bind(ItemContainer container)
    {
        if (_container != null)
            _container.SlotChanged -= OnSlotChanged;
        _container = container;

        // Prefer slot views already authored under the container (mirrors
        // BackpackUI); only instantiate to make up any shortfall. Since this
        // panel is reused across chests, this effectively only happens once,
        // on the first chest opened.
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