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
/// object's collider, following the <see cref="WorldItemPickup"/> convention, with one
/// extra guard: clicks that land on UI are ignored, so a button overlapping the station
/// on screen doesn't also trigger it. The UI and cursor references auto-resolve so the
/// prefab works dropped into any scene that has the save/load screen on its canvas.
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

    private Collider2D _collider;
    private bool _hovered;   // drives the label only on change, so we don't fight other hover sources

    private void Awake()
    {
        if (ui == null)
            ui = FindFirstObjectByType<SaveLoadUI>(FindObjectsInactive.Include);
        if (cursor == null)
            cursor = FindFirstObjectByType<CursorController>();
        if (worldCamera == null)
            worldCamera = Camera.main;
        _collider = GetComponent<Collider2D>();
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

        SetHovered(IsCursorOver(out bool overUI));

        if (_hovered && !overUI && LeftClickPressedThisFrame())
            ui.Open(SaveLoadUI.Mode.Save); // no-op unless the game is Playing
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
}
