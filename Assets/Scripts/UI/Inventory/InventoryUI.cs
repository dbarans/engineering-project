using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders an inventory into UI slots.
/// </summary>
public class InventoryUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform content;
    [SerializeField] private GameObject slotPrefab;
    [SerializeField] private float refreshInterval = 0.15f;

    private IInventory _inventory;
    private float _nextRefresh;
    private readonly List<GameObject> _spawnedSlots = new List<GameObject>();

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Update()
    {
        if (_inventory == null) return;
        if (Time.time < _nextRefresh) return;
        _nextRefresh = Time.time + refreshInterval;
        Refresh();
    }

    private void OnEnable()
    {
        _nextRefresh = 0f;
    }

    private void ClearSlots()
    {
        foreach (var go in _spawnedSlots)
        {
            if (go != null) Destroy(go);
        }
        _spawnedSlots.Clear();
    }

    /// <summary>
    /// Re-renders the inventory content based on the currently bound inventory.
    /// </summary>
    public void Refresh()
    {
        if (content == null || slotPrefab == null)
            return;
        if (_inventory == null)
        {
            ClearSlots();
            return;
        }

        var counts = new Dictionary<ItemData, int>();
        foreach (var item in _inventory.Items)
        {
            if (item == null) continue;
            counts[item] = counts.GetValueOrDefault(item, 0) + 1;
        }

        ClearSlots();
        foreach (var kv in counts)
        {
            var row = Instantiate(slotPrefab, content);
            _spawnedSlots.Add(row);
            var slot = row.GetComponent<RequiredIngredientRow>();
            if (slot != null)
                slot.Setup(kv.Key, kv.Value);
        }
    }

    /// <summary>
    /// Binds an inventory as the data source for this UI.
    /// </summary>
    public void Bind(IInventory inventory)
    {
        _inventory = inventory;
        _nextRefresh = 0f;
        Refresh();
    }

    /// <summary>
    /// Binds an inventory and shows the UI.
    /// </summary>
    public void Show(IInventory inventory)
    {
        Bind(inventory);
        if (panelRoot != null) panelRoot.SetActive(true);
    }

    /// <summary>
    /// Shows the UI panel using the currently bound inventory.
    /// </summary>
    public void Show()
    {
        if (panelRoot != null) panelRoot.SetActive(true);
    }

    /// <summary>
    /// Hides the UI panel.
    /// </summary>
    public void Hide() { if (panelRoot != null) panelRoot.SetActive(false); }
}
