using UnityEngine;

/// <summary>
/// The threshold of the way out. Ends the run when the player stands on it with the exit
/// door open.
///
/// Spawned by <see cref="DungeonPopulator"/> on the one floor cell in front of the exit
/// door — the door cut into the exit room's outer wall, which takes the dungeon's only key.
/// The room itself is entered through ordinary doors, so the player can find the way out
/// long before they can use it; this component is what makes standing in the right place
/// with the door open mean something, and standing there without the key mean nothing.
///
/// Deliberately knows nothing about how the game ends beyond calling
/// <see cref="IGameStateManager.WinGame"/> — what a win looks like on screen belongs to the
/// UI listening on <see cref="IGameStateManager.OnGameStateChanged"/>, not here.
/// </summary>
[RequireComponent(typeof(BoxCollider2D))]
public class DungeonExit : MonoBehaviour
{
    [Tooltip("Tag of the entity that ends the run by leaving. Enemies chase the player " +
             "through doorways, and one wandering onto the threshold must not finish the game.")]
    [SerializeField] private string playerTag = "Player";

    [Tooltip("The door in the wall this threshold belongs to. The run ends only once it is " +
             "open. Assigned by the generator; a threshold with no door ends the run on " +
             "contact, which is only ever right for a dungeon generated without a key.")]
    [SerializeField] private SimpleDoor exitDoor;

    private bool triggered;

    private void Reset()
    {
        var box = GetComponent<BoxCollider2D>();
        box.isTrigger = true;
    }

    /// <summary>
    /// Wires the threshold to the door it belongs to. Called by the generator, which has
    /// just spawned both and is the only thing that knows they are a pair — a prefab asset
    /// cannot hold a reference to a scene object.
    /// </summary>
    public void BindDoor(SimpleDoor door)
    {
        exitDoor = door;
    }

    /// <summary>
    /// Stay rather than Enter, and this is the whole reason the check is written this way:
    /// the player has to be standing in front of the door to open it, so by the time the
    /// door swings they are already inside the trigger and Enter has long since fired. Enter
    /// alone meant the run only ended if the player stepped off the threshold and back onto
    /// it after unlocking.
    /// </summary>
    private void OnTriggerStay2D(Collider2D other)
    {
        if (triggered) return;
        if (!other.CompareTag(playerTag)) return;

        // A door that was never assigned leaves the threshold open, which is correct for a
        // dungeon generated with no key at all — there is nothing to wait for.
        if (exitDoor != null && !exitDoor.IsOpen) return;

        triggered = true;

        var state = FindFirstObjectByType<GameManager>();
        if (state == null)
        {
            Debug.LogWarning(
                "[DungeonExit] The player reached the way out but there is no GameManager in " +
                "the scene, so the run cannot be ended.", this);
            return;
        }

        Debug.Log("[DungeonExit] The player left the dungeon — run complete.");
        state.WinGame();
    }
}
