using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class HotbarUI : MonoBehaviour
{
    [SerializeField] private int slotCount = 5;
    [SerializeField] private GameObject slotPrefab;
    [SerializeField] private Transform slotsContainer;
    [SerializeField] private PlayerInventory inventory;

    private readonly List<HotbarSlotUI> _slots = new List<HotbarSlotUI>();
    private readonly List<ItemData> _hotbarItems = new List<ItemData>();
    private int _selectedIndex;

    public ItemData SelectedItem =>
        _hotbarItems.Count > 0 && _selectedIndex < _hotbarItems.Count
            ? _hotbarItems[_selectedIndex]
            : null;

    public int SelectedIndex => _selectedIndex;

    private void Awake()
    {
        BuildSlots();
    }

    private void OnEnable()
    {
        if (inventory == null)
            inventory = FindFirstObjectByType<PlayerInventory>();

        if (inventory != null)
            inventory.ContentsChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        if (inventory != null)
            inventory.ContentsChanged -= Refresh;
    }

    private void BuildSlots()
    {
        foreach (var s in _slots)
            if (s != null) Destroy(s.gameObject);
        _slots.Clear();

        for (int i = 0; i < slotCount; i++)
        {
            var go = Instantiate(slotPrefab, slotsContainer);
            var slot = go.GetComponent<HotbarSlotUI>();
            if (slot != null)
                _slots.Add(slot);
        }
    }

    private void Update()
    {
        if (Mouse.current == null) return;
        float scroll = Mouse.current.scroll.ReadValue().y;
        if (scroll > 0f) ChangeSelection(-1);
        else if (scroll < 0f) ChangeSelection(1);
    }

    private void ChangeSelection(int delta)
    {
        _selectedIndex = (_selectedIndex + delta + slotCount) % slotCount;
        UpdateHighlights();
    }

    public void Refresh()
    {
        _hotbarItems.Clear();

        if (inventory != null)
        {
            var seen = new HashSet<string>();
            foreach (var item in inventory.Items)
            {
                if (item == null) continue;
                if (!seen.Add(item.itemName)) continue;
                _hotbarItems.Add(item);
                if (_hotbarItems.Count >= slotCount) break;
            }
        }

        for (int i = 0; i < _slots.Count; i++)
            _slots[i].Setup(i < _hotbarItems.Count ? _hotbarItems[i] : null);

        _selectedIndex = Mathf.Clamp(_selectedIndex, 0, Mathf.Max(0, slotCount - 1));
        UpdateHighlights();
    }

    private void UpdateHighlights()
    {
        for (int i = 0; i < _slots.Count; i++)
            _slots[i].SetSelected(i == _selectedIndex);
    }
}
