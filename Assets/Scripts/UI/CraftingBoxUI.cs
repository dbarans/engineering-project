using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a fixed grid of <see cref="CraftingSlotView"/>s — one per recipe — beside the
/// backpack. Each slot shows a recipe's output, dims while its ingredients are missing,
/// and on click crafts the output (consuming the ingredients from the backpack and
/// putting the result back into the backpack). Opens and closes together with the
/// <see cref="BackpackUI"/>. Reads from and writes to the slot-based
/// <see cref="SlotInventory.Backpack"/>.
/// </summary>
public class CraftingBoxUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform slotsContainer;
    [SerializeField] private GameObject slotPrefab;
    [SerializeField] private SlotInventory slotInventory;
    [SerializeField] private BackpackUI backpack;

    [Tooltip("Recipes offered by the crafting box (each: output + 1-2 ingredients).")]
    [SerializeField] private List<RecipeData> recipes = new List<RecipeData>();

    private readonly List<CraftingSlotView> _slots = new List<CraftingSlotView>();
    private ItemContainer _source;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Start()
    {
        if (slotInventory == null) slotInventory = FindFirstObjectByType<SlotInventory>();
        if (backpack == null) backpack = FindFirstObjectByType<BackpackUI>();

        BuildSlots();

        if (backpack != null) backpack.OpenStateChanged += OnBackpackToggled;
    }

    private void OnDestroy()
    {
        if (_source != null) _source.SlotChanged -= OnBackpackChanged;
        if (backpack != null) backpack.OpenStateChanged -= OnBackpackToggled;
    }

    private void BuildSlots()
    {
        _slots.Clear();
        if (slotInventory == null) return;
        _source = slotInventory.Backpack;

        // Prefer slot views already baked under the container; instantiate the shortfall.
        if (slotsContainer != null)
        {
            foreach (Transform child in slotsContainer)
            {
                var view = child.GetComponent<CraftingSlotView>();
                if (view != null) _slots.Add(view);
            }
        }

        while (_slots.Count < recipes.Count && slotPrefab != null && slotsContainer != null)
        {
            var go = Instantiate(slotPrefab, slotsContainer);
            var view = go.GetComponent<CraftingSlotView>();
            if (view == null) break;
            _slots.Add(view);
        }

        for (int i = 0; i < _slots.Count; i++)
        {
            var recipe = i < recipes.Count ? recipes[i] : null;
            _slots[i].Bind(recipe, _source);
        }

        // Crafting and ordinary item moves both raise SlotChanged -> re-dim the box.
        _source.SlotChanged += OnBackpackChanged;
    }

    private void OnBackpackChanged(int index) => RefreshAll();

    private void RefreshAll()
    {
        for (int i = 0; i < _slots.Count; i++)
            _slots[i].Refresh();
    }

    private void OnBackpackToggled(bool open)
    {
        if (panelRoot != null) panelRoot.SetActive(open);
        if (open) RefreshAll();
    }
}
