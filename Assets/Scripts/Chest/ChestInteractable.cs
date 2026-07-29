using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets the player open/close this chest when standing in range and pressing
/// interact. Requires a Collider2D on this GameObject set to IsTrigger, sized
/// to whatever radius counts as "in range".
///
/// Uses the new Input System (matches HeldItemController/HotbarUI's style)
/// rather than the legacy Input class. Assumes the player's collider is
/// tagged "Player".
/// </summary>
[RequireComponent(typeof(ChestInventory))]
public class ChestInteractable : MonoBehaviour
{
    [SerializeField] private Key interactKey = Key.E;

    private ChestInventory _inventory;
    private bool _playerInRange;
    private bool _isOpen;

    private void Awake() => _inventory = GetComponent<ChestInventory>();

    private void Update()
    {
        if (!_playerInRange || Keyboard.current == null) return;
        if (!Keyboard.current[interactKey].wasPressedThisFrame) return;

        if (_isOpen) Close();
        else Open();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = true;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
        if (_isOpen) Close();
    }

    private void Open()
    {
        _isOpen = true;
        ChestUI.Instance.Show(_inventory.Container);
    }

    private void Close()
    {
        _isOpen = false;
        ChestUI.Instance.Hide();
    }
}