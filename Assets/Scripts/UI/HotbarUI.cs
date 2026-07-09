using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Builds the always-present hotbar slots over <see cref="SlotInventory.Hotbar"/>,
/// keeps scroll-wheel selection + highlight, and routes clicks through the shared
/// <see cref="SlotView"/> / <see cref="HeldItemController"/> pipeline.
/// </summary>
public class HotbarUI : MonoBehaviour
{
    [SerializeField] private GameObject slotPrefab;
    [SerializeField] private Transform slotsContainer;
    [SerializeField] private SlotInventory slotInventory;
    [SerializeField] private HeldItemController heldItem;

    private readonly List<SlotView> _slots = new List<SlotView>();
    private ItemContainer _container;
    private int _selectedIndex;

    /// <summary>The item in the currently selected hotbar slot, or <c>null</c> if empty.</summary>
    public ItemData SelectedItem
    {
        get
        {
            var stack = _container?.Get(_selectedIndex);
            return stack != null && !stack.IsEmpty ? stack.item : null;
        }
    }

    public int SelectedIndex => _selectedIndex;

    /// <summary>
    /// Raised whenever the item under the selection highlight may have changed:
    /// scrolling to another slot, or the selected slot's content changing.
    /// </summary>
    public event Action SelectedItemChanged;

    private void Start()
    {
        if (slotInventory == null) slotInventory = FindFirstObjectByType<SlotInventory>();
        if (heldItem == null) heldItem = FindFirstObjectByType<HeldItemController>();
        BuildSlots();
        UpdateHighlights();
        SelectedItemChanged?.Invoke();
    }

    private void OnDestroy()
    {
        if (_container != null)
            _container.SlotChanged -= OnSlotChanged;
    }

    private void BuildSlots()
    {
        _slots.Clear();

        if (slotInventory == null) return;
        _container = slotInventory.Hotbar;

        // Prefer slot views already authored under the container (they exist before
        // play); only instantiate to make up any shortfall.
        if (slotsContainer != null)
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

        for (int i = 0; i < _slots.Count && i < _container.SlotCount; i++)
            _slots[i].Bind(_container, i, heldItem);

        _container.SlotChanged += OnSlotChanged;
    }

    private void OnSlotChanged(int index)
    {
        if (index >= 0 && index < _slots.Count)
            _slots[index].Refresh();
        if (index == _selectedIndex)
            SelectedItemChanged?.Invoke();
    }

    private void Update()
    {
        if (Mouse.current == null || _slots.Count == 0) return;
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (scroll > 0f) ChangeSelection(-1);
        else if (scroll < 0f) ChangeSelection(1);
    }

    private void ChangeSelection(int delta)
    {
        int count = _slots.Count;
        _selectedIndex = (_selectedIndex + delta + count) % count;
        UpdateHighlights();
        SelectedItemChanged?.Invoke();
    }

    private void UpdateHighlights()
    {
        for (int i = 0; i < _slots.Count; i++)
            _slots[i].SetSelected(i == _selectedIndex);
    }
}
