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
    [SerializeField] private InventoryUI inventoryUI;
    [SerializeField] private PlayerInventory playerInventory;
    [SerializeField] private PlayerAttack currentAttack;
    [SerializeField] private PlayerStaminaSystem playerStamina;

    private IGameStateManager gameStateManager;
    private PlayerControls controls;
    private Vector2 moveInput;
    private enum MovementState { Walk, Sneaking, Sprinting }
    private MovementState movementState = MovementState.Walk;

    private void Awake()
    {
        controls = new PlayerControls();
        if (playerInventory == null) playerInventory = GetComponent<PlayerInventory>();
        if (inventoryUI == null) inventoryUI = FindFirstObjectByType<InventoryUI>();
        if (playerStamina == null) playerStamina = GetComponent<PlayerStaminaSystem>();
    }

    /// <summary>
    /// Sets the game state manager dependency. Called by GameManager during initialization.
    /// </summary>
    /// <param name="manager">The game state manager instance.</param>
    public void SetGameStateManager(IGameStateManager manager)
    {
        gameStateManager = manager;
    }

    private void ForceStopSprint()
    {
        movementState = MovementState.Walk;
        playerMovement.SetMovementMode(PlayerMovement.MovementMode.Walk);
        playerStamina?.SetSprinting(false);
    }

    private void OnEnable()
    {
        controls.Player.Enable();

        if (playerStamina != null)
            playerStamina.OnStaminaDepletedWhileSprinting += ForceStopSprint;

        // Subscribe to input events
        controls.Player.Move.performed += OnMovePerformed;
        controls.Player.Move.canceled += OnMoveCanceled;
        controls.Player.Aim.performed += OnAimPerformed;
        controls.Player.Sprint.performed += OnSprintPerformed;
        controls.Player.Sprint.canceled += OnMovementModifierCanceled;
        controls.Player.Sneak.performed += OnSneakPerformed;
        controls.Player.Sneak.canceled += OnMovementModifierCanceled;
        controls.Player.Inventory.performed += OnInventoryPerformed;
        controls.Player.Prepare.started += OnPrepareStarted;
        controls.Player.Prepare.canceled += OnPrepareCanceled;
        controls.Player.Attack.performed += OnAttackPerformed;
    }

    private void OnDisable()
    {
        if (playerStamina != null)
            playerStamina.OnStaminaDepletedWhileSprinting -= ForceStopSprint;

        // Unsubscribe to prevent memory leaks
        controls.Player.Move.performed -= OnMovePerformed;
        controls.Player.Move.canceled -= OnMoveCanceled;
        controls.Player.Aim.performed -= OnAimPerformed;
        controls.Player.Sprint.performed -= OnSprintPerformed;
        controls.Player.Sprint.canceled -= OnMovementModifierCanceled;
        controls.Player.Sneak.performed -= OnSneakPerformed;
        controls.Player.Sneak.canceled -= OnMovementModifierCanceled;
        controls.Player.Inventory.performed -= OnInventoryPerformed;
        controls.Player.Prepare.started -= OnPrepareStarted;
        controls.Player.Prepare.canceled -= OnPrepareCanceled;
        controls.Player.Attack.performed -= OnAttackPerformed;

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

    private void OnSprintPerformed(InputAction.CallbackContext context)
    {
        if (movementState == MovementState.Walk && (playerStamina == null || playerStamina.CanSprint()))
        {
            movementState = MovementState.Sprinting;
            playerMovement.SetMovementMode(PlayerMovement.MovementMode.Sprint);
            playerStamina?.SetSprinting(true);
        }
    }

    private void OnSneakPerformed(InputAction.CallbackContext context)
    {
        if(movementState == MovementState.Walk)
        {
            movementState = MovementState.Sneaking;
            playerMovement.SetMovementMode(PlayerMovement.MovementMode.Sneak);
        }
    }

    private void OnMovementModifierCanceled(InputAction.CallbackContext context)
    {
        if (controls.Player.Sprint.IsPressed() && (playerStamina == null || playerStamina.CanSprint()))
        {
            movementState = MovementState.Sprinting;
            playerMovement.SetMovementMode(PlayerMovement.MovementMode.Sprint);
            playerStamina?.SetSprinting(true);
        }
        else if (controls.Player.Sneak.IsPressed())
        {
            movementState = MovementState.Sneaking;
            playerMovement.SetMovementMode(PlayerMovement.MovementMode.Sneak);
            playerStamina?.SetSprinting(false);
        }
        else
        {
            movementState = MovementState.Walk;
            playerMovement.SetMovementMode(PlayerMovement.MovementMode.Walk);
            playerStamina?.SetSprinting(false);
        }
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

    private void OnInventoryPerformed(InputAction.CallbackContext context)
    {
        if (inventoryUI == null) return;
        inventoryUI.Toggle(playerInventory);
    }
    
    /// <summary>
    /// Triggered when prepare input is performed (mouse or joystick).
    /// </summary>
    private void OnPreparePerformed(InputAction.CallbackContext context)
    {
        if (gameStateManager != null && gameStateManager.IsPaused())
        {
            return;
        }
        currentAttack.StartCharging();
    }
    
    /// <summary>
    /// Triggered when prepare input is started (mouse or joystick).
    /// </summary>
    private void OnPrepareStarted(InputAction.CallbackContext context)
    {
        if (gameStateManager != null && gameStateManager.IsPaused())
        {
            return;
        }
        currentAttack.StartCharging();
    }
    
    /// <summary>
    /// Triggered when prepare input is canceled (mouse or joystick).
    /// </summary>
    private void OnPrepareCanceled(InputAction.CallbackContext context)
    {
        currentAttack.StopCharging();
    }
    
    /// <summary>
    /// Triggered when attack input is performed (mouse or joystick).
    /// </summary>
    private void OnAttackPerformed(InputAction.CallbackContext context)
    {
        if (playerStamina != null && !playerStamina.TryUseAttackStamina())
            return;
        currentAttack.Fire();
    }
    
    /// <summary>
    /// Configuration of current type attack
    /// </summary>
    public void SetCurrentAttack(PlayerAttack weapon)
    {
        currentAttack = weapon;
    }
    /// <summary>
    /// Get current type attack
    /// </summary>
    public PlayerAttack GetCurrentAttack()
    {
        return currentAttack;
    }
}
