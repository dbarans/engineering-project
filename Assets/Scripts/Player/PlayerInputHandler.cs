using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles player input and forwards it to movement and aiming systems.
/// Decouples input from gameplay logic to allow modularity and testability.
/// Respects game state and blocks input during pause.
/// </summary>
public class PlayerInputHandler : MonoBehaviour
{
    [Header("Player Systems")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private PlayerAim playerAim;
    [SerializeField] private PlayerLegs playerLegs;

    private IGameStateManager gameStateManager;
    private PlayerControls controls;
    private Vector2 moveInput;

    private void Awake()
    {
        controls = new PlayerControls();
    }

    /// <summary>
    /// Sets the game state manager dependency. Called by GameManager during initialization.
    /// </summary>
    /// <param name="manager">The game state manager instance.</param>
    public void SetGameStateManager(IGameStateManager manager)
    {
        gameStateManager = manager;
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
        if (gameStateManager != null && gameStateManager.IsPaused())
        {
            return;
        }

        playerMovement.Move(moveInput);
    }

    /// <summary>
    /// Triggered when movement input is performed.
    /// </summary>
    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        if (gameStateManager != null && gameStateManager.IsPaused())
        {
            moveInput = Vector2.zero;
            return;
        }

        moveInput = context.ReadValue<Vector2>();
        playerLegs.SetLegsPosition(moveInput);
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
        if (gameStateManager != null && gameStateManager.IsPaused())
        {
            return;
        }

        Vector2 aimPosition = context.ReadValue<Vector2>();
        playerAim.SetAimPosition(aimPosition);
    }
}
