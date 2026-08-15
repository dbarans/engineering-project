using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manages enemy corpse loot containers, UI interaction state, and persistence.
/// Disables interactions permanently once emptied and closed.
/// </summary>
[RequireComponent(typeof(SaveableEntity))]
public class EnemyCorpse : MonoBehaviour, ISaveableComponent
{
    [Header("Corpse Inventory Settings")]
    [SerializeField, Min(1)] private int columns = 2;
    [SerializeField, Min(1)] private int rows = 2;

    [SerializeField] private float interactRange = 1.5f;
    private Transform playerTransform;
    private ItemContainer _container;
    private bool _playerInRange;
    private bool _isDisabled;

    private static EnemyCorpse _openCorpse;
    public static EnemyCorpse OpenCorpse => _openCorpse;
    private bool IsOpen => _openCorpse == this;

    public string TypeTag => "enemyCorpse";

    public ItemContainer Container
    {
        get
        {
            if (_container == null)
            {
                _container = new ItemContainer(columns * rows);
                _container.SlotChanged += OnSlotChanged;
            }
            return _container;
        }
    }

    private void Start()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) playerTransform = player.transform;

        CheckIfEmptyAndDisable();
    }

    private void Update()
    {
        if (_isDisabled || !_playerInRange || Keyboard.current == null) return;

        if (Keyboard.current[KeyBindings.Instance.interact].wasPressedThisFrame)
        {
            if (_openCorpse != null && !_openCorpse.IsOpen) return;

            if (IsOpen) Close();
            else Open();
        }
    }

    private void OnDestroy()
    {
        if (_container != null)
            _container.SlotChanged -= OnSlotChanged;
    }

    /// <summary>
    /// Populates the corpse inventory with initial dropped items upon creation.
    /// </summary>
    /// <param name="itemsToDrop">List of item definitions and quantities to insert.</param>
    public void InitializeDrop(List<DropItem> itemsToDrop)
    {
        var cont = Container;
        foreach (var drop in itemsToDrop)
        {
            if (drop.itemData != null && drop.quantity > 0)
            {
                cont.TryAddItem(drop.itemData, drop.quantity);
            }
        }
    }

    private void Open()
    {
        if (_isDisabled) return;

        if (CorpseUI.Instance != null)
        {
            _openCorpse = this;
            CorpseUI.Instance.Show(Container, "Zwłoki");
        }
        else if (ChestUI.Instance != null)
        {
            _openCorpse = this;
            ChestUI.Instance.Show(Container);
        }
    }

    private void Close()
    {
        if (IsOpen)
        {
            _openCorpse = null;
            if (CorpseUI.Instance != null) CorpseUI.Instance.Hide();
            else if (ChestUI.Instance != null) ChestUI.Instance.Hide();
        }

        CheckIfEmptyAndDisable();
    }

    private void OnSlotChanged(int slotIndex)
    {
        // Do NOT close the window while the player is still interacting with items!
    }

    /// <summary>
    /// Evaluates if the corpse inventory is empty and the player cursor is clear.
    /// Permanently disables colliders and script updates when no items remain.
    /// </summary>
    public void CheckIfEmptyAndDisable()
    {
        if (_container == null || _isDisabled) return;

        bool isEmpty = true;
        for (int i = 0; i < _container.SlotCount; i++)
        {
            var stack = _container.Get(i);
            if (stack != null && !stack.IsEmpty)
            {
                isEmpty = false;
                break;
            }
        }

        var heldController = FindFirstObjectByType<HeldItemController>();
        bool playerIsHoldingItem = heldController != null && heldController.IsHolding;

        if (isEmpty && !playerIsHoldingItem)
        {
            _isDisabled = true;
            _playerInRange = false;

            if (IsOpen)
            {
                _openCorpse = null;
                if (CorpseUI.Instance != null) CorpseUI.Instance.Hide();
                else if (ChestUI.Instance != null) ChestUI.Instance.Hide();
            }

            var colliders = GetComponentsInChildren<Collider2D>();
            foreach (var col in colliders)
            {
                col.enabled = false;
            }

            this.enabled = false;
            Debug.Log("[EnemyCorpse] Corpse is empty – interactions disabled permanently.");
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (_isDisabled || !other.CompareTag("Player")) return;
        _playerInRange = true;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
        if (IsOpen) Close();
    }

    private void OnDisable()
    {
        if (IsOpen) Close();
    }

    /// <summary>
    /// Serializes the current corpse inventory state into a JSON payload for saving.
    /// </summary>
    public string CapturePayload()
    {
        if (_container == null) return null;
        
        var saveData = ContainerSaveUtility.Capture(_container);
        return Newtonsoft.Json.JsonConvert.SerializeObject(saveData);
    }

    /// <summary>
    /// Restores the corpse inventory state from a JSON payload upon game load.
    /// </summary>
    public void RestorePayload(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return;

        var saveData = Newtonsoft.Json.JsonConvert.DeserializeObject<ContainerSaveData>(payload);
        if (saveData != null)
        {
            ContainerSaveUtility.Restore(Container, saveData, this);
            CheckIfEmptyAndDisable();
        }
    }
}