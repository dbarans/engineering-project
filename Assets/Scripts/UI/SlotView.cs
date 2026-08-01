using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// A fixed inventory slot frame (background + selection highlight) that hosts at
/// most one <see cref="InventoryItem"/> entity as a child. The entity is a real,
/// persistent GameObject: it is <see cref="Detach"/>ed and <see cref="Attach"/>ed
/// (re-parented) as it moves between slots and the cursor, never recreated. New
/// entities are only spawned here when items arrive in the data from elsewhere.
/// Routes clicks to <see cref="HeldItemController"/>.
/// </summary>
public class SlotView : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private InventoryItem itemPrefab;
    [Tooltip("Parent for the item entity. Defaults to this slot's transform.")]
    [SerializeField] private RectTransform itemAnchor;
    [SerializeField] private Image selectionHighlight;

    private ItemContainer _container;
    private int _index = -1;
    private HeldItemController _held;
    private InventoryItem _item;

    /// <summary>The container this slot reads from.</summary>
    public ItemContainer Container => _container;

    /// <summary>This slot's index within <see cref="Container"/>.</summary>
    public int Index => _index;

    /// <summary>The entity currently sitting in this slot, or <c>null</c>.</summary>
    public InventoryItem Item => _item;

    /// <summary>Binds this slot to slot <paramref name="index"/> of <paramref name="container"/>.</summary>
    public void Bind(ItemContainer container, int index, HeldItemController held)
    {
        _container = container;
        _index = index;
        _held = held;
        Refresh();
    }

    /// <summary>
    /// Reconciles the entity with the bound data: spawns one if data arrived and none
    /// exists, updates an existing one's count, or destroys it if the data emptied.
    /// During a click move the controller detaches/attaches the entity *before*
    /// mutating the data, so this never fights the re-parenting.
    /// </summary>
    public void Refresh()
    {
        var stack = _container?.Get(_index);
        bool hasItem = stack != null && !stack.IsEmpty;

        if (hasItem)
        {
            if (_item == null) _item = SpawnItem();
            if (_item != null) _item.SetStack(stack.item, stack.count, stack.CurrentDurability);
        }
        else if (_item != null)
        {
            Destroy(_item.gameObject);
            _item = null;
        }
    }

    /// <summary>Re-parents an existing entity into this slot and adopts it as the slot's item.</summary>
    public void Attach(InventoryItem item)
    {
        _item = item;
        if (item == null) return;
        var parent = itemAnchor != null ? itemAnchor : (RectTransform)transform;
        item.transform.SetParent(parent, false);
        StretchToParent(item.transform as RectTransform);
    }

    /// <summary>Gives up this slot's entity (without destroying it) for re-parenting elsewhere.</summary>
    public InventoryItem Detach()
    {
        var item = _item;
        _item = null;
        return item;
    }

    private InventoryItem SpawnItem()
    {
        if (itemPrefab == null) return null;
        var parent = itemAnchor != null ? itemAnchor : (RectTransform)transform;
        var inst = Instantiate(itemPrefab, parent);
        StretchToParent(inst.transform as RectTransform);
        return inst;
    }

    /// <summary>Toggles the selection highlight (used by the hotbar).</summary>
    public void SetSelected(bool selected)
    {
        if (selectionHighlight != null)
            selectionHighlight.enabled = selected;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_held != null)
            _held.HandleSlotClick(this);
    }

    /// <summary>Makes a freshly parented item entity fill its holder.</summary>
    internal static void StretchToParent(RectTransform rt)
    {
        if (rt == null) return;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }
}
