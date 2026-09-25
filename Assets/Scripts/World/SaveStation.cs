using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// The typewriter save point. Hovering it shows "Save game" above the cursor — the same
/// <see cref="CursorController"/> label <see cref="WorldItemPickup"/> uses for items on
/// the ground — and left-clicking it opens the full-screen <see cref="SaveLoadUI"/> in
/// Save mode (the screen itself has a Load tab).
///
/// Hover and click both come from projecting the mouse into the world and testing this
/// object's collider, following the <see cref="WorldItemPickup"/> convention, with two
/// extra guards: the player has to stand within <see cref="interactRange"/> of the
/// station — out of reach it is inert and shows no label, so the cursor never promises
/// an interaction that wouldn't happen — and clicks that land on UI are ignored, so a
/// button overlapping the station on screen doesn't also trigger it. The UI, cursor and
/// player references auto-resolve so the prefab works dropped into any scene that has
/// the save/load screen on its canvas.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class SaveStation : MonoBehaviour
{
    [Tooltip("Save/load screen to open. Auto-resolved if left unset.")]
    [SerializeField] private SaveLoadUI ui;
    [Tooltip("Cursor that displays the hover label. Auto-resolved if left unset.")]
    [SerializeField] private CursorController cursor;
    [Tooltip("Camera used to project the cursor into the world. Defaults to Camera.main.")]
    [SerializeField] private Camera worldCamera;
    [Tooltip("Label shown above the cursor while the station is hovered.")]
    [SerializeField] private string hoverText = "Save game";
    [Tooltip("How close the player must stand to use the station. Further away it shows " +
             "no label and ignores clicks.")]
    [SerializeField] private float interactRange = 2.5f;
    [Tooltip("Transform the range is measured from (the player). Auto-resolved if left unset.")]
    [SerializeField] private Transform player;

    private Collider2D _collider;
    private GameManager _gameManager;
    private bool _hovered;   // drives the label only on change, so we don't fight other hover sources

    private void Awake()
    {
        if (ui == null)
            ui = FindFirstObjectByType<SaveLoadUI>(FindObjectsInactive.Include);
        if (cursor == null)
            cursor = FindFirstObjectByType<CursorController>();
        if (worldCamera == null)
            worldCamera = Camera.main;
        _gameManager = FindFirstObjectByType<GameManager>();
        _collider = GetComponent<Collider2D>();
        ResolvePlayer();
    }

    private void OnDisable() => SetHovered(false);

    private void Update()
    {
        // While the screen is open the world isn't hoverable, and the label would sit
        // on top of it.
        if (ui == null || ui.IsOpen)
        {
            SetHovered(false);
            return;
        }

        bool cursorOver = IsCursorOver(out bool overUI);
        SetHovered(cursorOver && PlayerInRange());

        if (_hovered && !overUI && LeftClickPressedThisFrame())
        {
            ui.Open(SaveLoadUI.Mode.Save); // no-op unless the game is Playing

            // Gated on the screen having actually opened, not on the click: Open bails out
            // silently when the game is not Playing, and a station that answers a click with
            // a sound and nothing else reads as a broken interaction.
            if (ui.IsOpen) AudioService.PlayAt(SoundId.SaveStationOpen, transform.position);
        }
    }

    /// <summary>
    /// Whether the mouse is over this station's collider. <paramref name="overUI"/>
    /// reports a cursor sitting on a UI element — still a hover, but not a click on us.
    /// </summary>
    private bool IsCursorOver(out bool overUI)
    {
        overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        var mouse = Mouse.current;
        if (mouse == null) return false;

        if (worldCamera == null) worldCamera = Camera.main;
        if (worldCamera == null) return false;

        Vector2 point = worldCamera.ScreenToWorldPoint(mouse.position.ReadValue());
        return _collider.OverlapPoint(point);
    }

    /// <summary>
    /// Whether the player stands close enough to reach the station. Measured to the
    /// nearest point of the collider rather than to the pivot, so a wide typewriter is
    /// usable from either end. With no player in the scene the station stays usable — a
    /// missing reference shouldn't quietly disable saving.
    /// </summary>
    private bool PlayerInRange()
    {
        Transform origin = ResolvePlayer();
        if (origin == null) return true;

        Vector2 playerPoint = origin.position;
        Vector2 nearest = _collider.ClosestPoint(playerPoint);
        return (nearest - playerPoint).sqrMagnitude <= interactRange * interactRange;
    }

    /// <summary>
    /// The player transform, re-resolved whenever it goes missing: the dungeon generator
    /// respawns the player, which leaves a reference captured once at Awake dangling.
    /// </summary>
    private Transform ResolvePlayer()
    {
        if (player != null) return player;

        if (_gameManager == null) _gameManager = FindFirstObjectByType<GameManager>();
        GameObject found = _gameManager != null ? _gameManager.GetPlayer() : null;
        if (found == null) found = GameObject.FindWithTag("Player");

        player = found != null ? found.transform : null;
        return player;
    }

    private void SetHovered(bool hovered)
    {
        if (_hovered == hovered) return;
        _hovered = hovered;

        if (cursor == null) return;
        if (hovered) cursor.SetHoverText(hoverText);
        else cursor.ClearHoverText();
    }

    private static bool LeftClickPressedThisFrame()
    {
        var mouse = Mouse.current;
        return mouse != null && mouse.leftButton.wasPressedThisFrame;
    }

    private void OnDrawGizmosSelected()
    {
        // Drawn around the collider, which is what the range is measured to — the real
        // limit is this sphere pushed out by the collider's own extents.
        var col = _collider != null ? _collider : GetComponent<Collider2D>();
        Vector3 center = col != null ? col.bounds.center : transform.position;

        Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireSphere(center, interactRange);
    }
}
