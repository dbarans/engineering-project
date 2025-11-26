using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles player input and forwards it to movement and aiming systems.
/// Decouples input from gameplay logic to allow modularity and testability.
/// </summary>
public class PlayerInputHandler : MonoBehaviour
{
    [Header("Player Systems")]
    [SerializeField] private PlayerMovement playerMovement; // Legs/nodes movement
    [SerializeField] private PlayerAim playerAim;           // Torso aiming

    private PlayerControls controls;   // Generated input actions
    private Vector2 moveInput;         // Current movement vector

    private void Awake()
    {
        // Create instance of generated input actions class
        controls = new PlayerControls();
    }

    private void OnEnable()
    {
        controls.Player.Enable();

        // Subscribe to input events
        controls.Player.Move.performed += OnMovePerformed;
        controls.Player.Move.canceled += OnMoveCanceled;
        controls.Player.Aim.performed += OnAimPerformed;
    }

    private void OnDisable()
    {
        // Unsubscribe to prevent memory leaks
        controls.Player.Move.performed -= OnMovePerformed;
        controls.Player.Move.canceled -= OnMoveCanceled;
        controls.Player.Aim.performed -= OnAimPerformed;

        controls.Player.Disable();
    }

    private void FixedUpdate()
    {
        // Forward movement input to movement system
        playerMovement.Move(moveInput);
    }

    #region Input Callbacks

    /// <summary>
    /// Triggered when movement input is performed.
    /// </summary>
    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    /// <summary>
    /// Triggered when movement input is canceled.
    /// </summary>
    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        moveInput = Vector2.zero;
    }

    /// <summary>
    /// Triggered when aiming input is performed (mouse or joystick).
    /// </summary>
    private void OnAimPerformed(InputAction.CallbackContext context)
    {
        Vector2 aimPosition = context.ReadValue<Vector2>();
        playerAim.SetAimPosition(aimPosition);
    }

    #endregion
}
