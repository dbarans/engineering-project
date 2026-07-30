using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds a grid of <see cref="CraftingSlotView"/>s — one per active recipe — beside the
/// backpack. Each slot shows a recipe's output, dims while its ingredients are missing,
/// and on click crafts the output (consuming the ingredients from the backpack and
/// putting the result back into the backpack). Opens and closes together with the
/// <see cref="BackpackUI"/>. Reads from and writes to the slot-based
/// <see cref="SlotInventory.Backpack"/>.
///
/// The active recipe set is the always-available <see cref="recipes"/> plus the recipes
/// of every <see cref="CraftingTable"/> the player currently stands near (registered via
/// <see cref="AddTable"/> / <see cref="RemoveTable"/>). The grid grows and shrinks with
/// that set, the panel resizes to fit, and the "repair weapon" button
/// (<see cref="repairButtonRoot"/>) is shown only while at least one table is in range.
/// </summary>
public class CraftingBoxUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Transform slotsContainer;
    [SerializeField] private GameObject slotPrefab;
    [SerializeField] private SlotInventory slotInventory;
    [SerializeField] private BackpackUI backpack;

    [Tooltip("Recipes always offered by the crafting box, even away from a crafting table.")]
    [SerializeField] private List<RecipeData> recipes = new List<RecipeData>();

    [Header("Crafting table")]
    [Tooltip("Weapon-repair button shown only while a crafting table is in range. " +
             "Does nothing yet (see WeaponRepairButton).")]
    [SerializeField] private GameObject repairButtonRoot;

    private readonly List<CraftingSlotView> _slots = new List<CraftingSlotView>();
    private readonly List<CraftingTable> _tables = new List<CraftingTable>();
    private readonly List<RecipeData> _active = new List<RecipeData>();
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

    /// <summary>
    /// Registers a crafting table the player has walked up to: its recipes join the box
    /// (and the repair button appears) until <see cref="RemoveTable"/> is called.
    /// </summary>
    public void AddTable(CraftingTable table)
    {
        if (table == null || _tables.Contains(table)) return;
        _tables.Add(table);
        RebuildActive();
    }

    /// <summary>Unregisters a crafting table the player has walked away from.</summary>
    public void RemoveTable(CraftingTable table)
    {
        if (!_tables.Remove(table)) return;
        RebuildActive();
    }

    private void BuildSlots()
    {
        _slots.Clear();
        if (slotInventory == null) return;
        _source = slotInventory.Backpack;

        // Adopt slot views already baked under the container; the shortfall is
        // instantiated on demand in EnsureSlotCount.
        if (slotsContainer != null)
        {
            foreach (Transform child in slotsContainer)
            {
                var view = child.GetComponent<CraftingSlotView>();
                if (view != null) _slots.Add(view);
            }
        }

        // Crafting and ordinary item moves both raise SlotChanged -> re-dim the box.
        _source.SlotChanged += OnBackpackChanged;

        RebuildActive();
    }

    /// <summary>
    /// Recomputes the active recipe set (base recipes + every in-range table's recipes,
    /// de-duplicated), binds one slot per recipe, hides the rest, resizes the panel to
    /// fit and toggles the repair button.
    /// </summary>
    private void RebuildActive()
    {
        if (_source == null) return;

        _active.Clear();
        AddRecipes(_active, recipes);
        for (int i = 0; i < _tables.Count; i++)
            if (_tables[i] != null) AddRecipes(_active, _tables[i].Recipes);

        EnsureSlotCount(_active.Count);

        for (int i = 0; i < _slots.Count; i++)
        {
            bool used = i < _active.Count;
            _slots[i].gameObject.SetActive(used);
            if (used) _slots[i].Bind(_active[i], _source);
        }

        ResizePanel(_active.Count);

        if (repairButtonRoot != null)
            repairButtonRoot.SetActive(_tables.Count > 0);
    }

    private static void AddRecipes(List<RecipeData> target, IEnumerable<RecipeData> source)
    {
        if (source == null) return;
        foreach (var recipe in source)
            if (recipe != null && !target.Contains(recipe)) target.Add(recipe);
    }

    private void EnsureSlotCount(int count)
    {
        while (_slots.Count < count && slotPrefab != null && slotsContainer != null)
        {
            var go = Instantiate(slotPrefab, slotsContainer);
            var view = go.GetComponent<CraftingSlotView>();
            if (view == null) { Destroy(go); break; }
            _slots.Add(view);
        }
    }

    /// <summary>
    /// Grows/shrinks the panel to hold <paramref name="count"/> cells. Cell metrics come
    /// from the live <see cref="GridLayoutGroup"/> and the title/footer strips from the
    /// Slots rect's own insets, so this stays in step with whatever the editor setup
    /// baked. The panel grows downward, keeping its top edge where it was placed.
    /// </summary>
    private void ResizePanel(int count)
    {
        var panelRt = panelRoot != null ? panelRoot.transform as RectTransform : null;
        var slotsRt = slotsContainer as RectTransform;
        var grid = slotsContainer != null ? slotsContainer.GetComponent<GridLayoutGroup>() : null;
        if (panelRt == null || slotsRt == null || grid == null) return;

        int columns = grid.constraint == GridLayoutGroup.Constraint.FixedColumnCount
            ? Mathf.Max(1, grid.constraintCount)
            : 4;
        int rows = Mathf.CeilToInt(Mathf.Max(1, count) / (float)columns);

        float gridWidth = columns * grid.cellSize.x + (columns - 1) * grid.spacing.x
                          + grid.padding.left + grid.padding.right;
        float gridHeight = rows * grid.cellSize.y + (rows - 1) * grid.spacing.y
                           + grid.padding.top + grid.padding.bottom;

        // Slots is anchored stretch; its offsets encode the insets to the panel edges.
        // The bottom inset includes the reserved repair-button footer, the top inset the
        // title strip.
        float leftInset = slotsRt.offsetMin.x;
        float bottomInset = slotsRt.offsetMin.y;
        float rightInset = -slotsRt.offsetMax.x;
        float topInset = -slotsRt.offsetMax.y;

        float width = gridWidth + leftInset + rightInset;
        float height = gridHeight + topInset + bottomInset;

        float topEdge = panelRt.anchoredPosition.y + panelRt.sizeDelta.y * 0.5f;
        panelRt.sizeDelta = new Vector2(width, height);
        panelRt.anchoredPosition = new Vector2(panelRt.anchoredPosition.x, topEdge - height * 0.5f);
    }

    private void OnBackpackChanged(int index) => RefreshAll();

    private void RefreshAll()
    {
        for (int i = 0; i < _slots.Count; i++)
            if (_slots[i].gameObject.activeSelf) _slots[i].Refresh();
    }

    private void OnBackpackToggled(bool open)
    {
        if (panelRoot != null) panelRoot.SetActive(open);
        if (open) RefreshAll();
    }
}
