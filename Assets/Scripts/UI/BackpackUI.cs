using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Builds a fixed grid of <see cref="SlotView"/>s over the backpack container and
/// toggles its panel (default: the Tab key). Slots are created once and bound to
/// the container; the container's <see cref="ItemContainer.SlotChanged"/> event
/// drives per-slot refresh.
/// </summary>
public class BackpackUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform slotsContainer;
    [SerializeField] private GameObject slotPrefab;
    [SerializeField] private SlotInventory slotInventory;
    [SerializeField] private HeldItemController heldItem;

    private readonly List<SlotView> _slots = new List<SlotView>();
    private ItemContainer _container;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Start()
    {
        if (slotInventory == null) slotInventory = FindFirstObjectByType<SlotInventory>();
        if (heldItem == null) heldItem = FindFirstObjectByType<HeldItemController>();
        BuildSlots();
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard[KeyBindings.Instance.toggleBackpack].wasPressedThisFrame)
            Toggle();
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
        _container = slotInventory.Backpack;

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
    }

    /// <summary>Raised when the backpack opens (<c>true</c>) or closes (<c>false</c>).</summary>
    public event Action<bool> OpenStateChanged;

    /// <summary>Whether the backpack panel is currently visible.</summary>
    public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

    /// <summary>Opens the backpack panel if closed, closes it if open.</summary>
    public void Toggle() => SetOpen(!IsOpen);

    /// <summary>Shows the backpack panel.</summary>
    public void Show() => SetOpen(true);

    /// <summary>Hides the backpack panel.</summary>
    public void Hide() => SetOpen(false);

    private void SetOpen(bool open)
    {
        if (panelRoot == null || panelRoot.activeSelf == open) return;
        panelRoot.SetActive(open);
        OpenStateChanged?.Invoke(open);
    }
}
