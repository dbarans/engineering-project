using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets the player open/close this chest when standing in range and pressing
/// interact.
///
/// The reach trigger is a **child** GameObject (<c>ReachTrigger</c>), not a collider on
/// this one, and that split matters: this GameObject's own collider is the chest's
/// physical footprint on layer <c>ObstaclePathOnly</c>, sized to exactly one dungeon
/// cell. The reach trigger needs to be considerably bigger than that to be usable, and if
/// it shared this GameObject's layer — Unity layers are per-object, not per-collider — it
/// would count as an obstacle too, at several times the chest's real size. For a
/// hand-placed chest with clearance around it that went unnoticed; the dungeon generator
/// deliberately anchors chests flush against a wall (<see cref="DungeonPopulator"/>'s
/// wall-bias placement), which pushed the oversized reach collider into the wall itself —
/// both physically, tightening <see cref="PathfindingGrid"/>'s walkable space near the
/// wall by more than the chest actually occupies, and visually. The child sits on layer
/// <c>Interactable</c> instead, which nothing treats as an obstacle, and forwards its
/// trigger events here via <see cref="ChestReachForwarder"/> — the same relay pattern
/// <see cref="DoorCollisionForwarder"/> uses for the door.
///
/// Uses the new Input System (matches HeldItemController/HotbarUI's style)
/// rather than the legacy Input class. Assumes the player's collider is
/// tagged "Player".
///
/// Which chest is open is tracked statically rather than per chest. Once a map holds
/// more than a handful of chests their reach zones overlap, and every chest containing
/// the player sees the same key press in the same frame — with a per-chest flag one
/// press toggled all of them at once and the shared <see cref="ChestUI"/> ended up
/// showing whichever chest Unity happened to update last, while the others still
/// believed they were open.
/// </summary>
[RequireComponent(typeof(ChestInventory))]
public class ChestInteractable : MonoBehaviour
{
    /// <summary>The chest currently showing in the shared panel, or null when none is.</summary>
    private static ChestInteractable _open;

    private ChestInventory _inventory;
    private bool _playerInRange;

    private bool IsOpen => _open == this;

    private void Awake() => _inventory = GetComponent<ChestInventory>();

    private void Update()
    {
        if (!_playerInRange || Keyboard.current == null) return;
        if (!Keyboard.current[KeyBindings.Instance.interact].wasPressedThisFrame) return;

        // Another chest holds the panel: closing it wins over opening this one, and its
        // own Update does that. Deferring here is what makes the outcome independent of
        // the order Unity updates overlapping chests in.
        if (_open != null && !IsOpen) return;

        if (IsOpen) Close();
        else Open();
    }

    // Called via SendMessage from ChestReachForwarder on the child ReachTrigger object,
    // not by a collider on this GameObject directly — see the class comment.
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = true;
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (!other.CompareTag("Player")) return;
        _playerInRange = false;
        if (IsOpen) Close();
    }

    // Covers being destroyed or deactivated while open — a chest respawned by the save
    // system, or a scene unloading — so the static never points at a dead chest.
    private void OnDisable()
    {
        if (IsOpen) Close();
    }

    private void Open()
    {
        // Explicit null check rather than trusting the singleton: chests are spawned into
        // generated dungeons, and a scene that never had the panel built would otherwise
        // throw the moment the player presses interact.
        if (ChestUI.Instance == null)
        {
            Debug.LogError(
                "[ChestInteractable] No ChestUI in the scene — the chest cannot be opened. " +
                "Run Tools ▸ Slot Inventory ▸ Build Chest & UI.", this);
            return; // stays closed: claiming the panel it failed to show would wedge every other chest
        }

        _open = this;
        AudioService.PlayAt(SoundId.ChestOpen, transform.position);
        ChestUI.Instance.Show(_inventory.Container);
    }

    private void Close()
    {
        _open = null;
        AudioService.PlayAt(SoundId.ChestClose, transform.position);
        if (ChestUI.Instance != null) ChestUI.Instance.Hide();
    }
}