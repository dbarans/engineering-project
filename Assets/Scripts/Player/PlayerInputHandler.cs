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
    [SerializeField] private PlayerAttack currentAttack;
    [SerializeField] private PlayerStaminaSystem playerStamina;
    [SerializeField] private PlayerHiding playerHiding;
    [SerializeField] private BackpackUI backpackUI;

    private IGameStateManager gameStateManager;
    private PlayerControls controls;
    private Vector2 moveInput;
    private enum MovementState { Walk, Sneaking, Sprinting }
    private MovementState movementState = MovementState.Walk;
    private bool isAiming;

    private void Awake()
    {
        controls = new PlayerControls();
        if (playerStamina == null) playerStamina = GetComponent<PlayerStaminaSystem>();
        if (playerHiding == null) playerHiding = GetComponent<PlayerHiding>();
        if (backpackUI == null) backpackUI = FindFirstObjectByType<BackpackUI>();
    }

    /// <summary>
    /// Sets the game state manager dependency. Called by GameManager during initialization.
    /// </summary>
    /// <param name="manager">The game state manager instance.</param>
    public void SetGameStateManager(IGameStateManager manager)
    {
        gameStateManager = manager;
    }

    /// <summary>
    /// True while the player is hidden under something (see <see cref="PlayerHiding"/>) and so
    /// must stay crouched: neither releasing Ctrl nor running out of stamina may stand them up
    /// into the object above them. They leave the crouch by walking out of the hideout.
    /// </summary>
    private bool CrouchLocked => playerHiding != null && playerHiding.IsHidden;

    private void ForceStopSprint()
    {
        bool locked = CrouchLocked;
        movementState = locked ? MovementState.Sneaking : MovementState.Walk;
        playerMovement.SetMovementMode(locked
            ? PlayerMovement.MovementMode.Sneak
            : PlayerMovement.MovementMode.Walk);
        playerStamina?.SetSprinting(false);
    }

    private void OnEnable()
    {
        controls.Player.Enable();

        if (playerStamina != null)
            playerStamina.OnStaminaDepletedWhileSprinting += ForceStopSprint;

        if (playerHiding != null)
            playerHiding.InHideoutChanged += OnInHideoutChanged;

        // Subscribe to input events
        controls.Player.Move.performed += OnMovePerformed;
        controls.Player.Move.canceled += OnMoveCanceled;
        controls.Player.Aim.performed += OnAimPerformed;
        controls.Player.Sprint.performed += OnSprintPerformed;
        controls.Player.Sprint.canceled += OnMovementModifierCanceled;
        controls.Player.Sneak.performed += OnSneakPerformed;
        controls.Player.Sneak.canceled += OnMovementModifierCanceled;
        controls.Player.Prepare.started += OnPrepareStarted;
        controls.Player.Prepare.canceled += OnPrepareCanceled;
        controls.Player.Attack.performed += OnAttackPerformed;

        if (backpackUI != null)
            backpackUI.OpenStateChanged += OnBackpackOpenStateChanged;
    }

    private void OnDisable()
    {
        if (playerStamina != null)
            playerStamina.OnStaminaDepletedWhileSprinting -= ForceStopSprint;

        if (playerHiding != null)
            playerHiding.InHideoutChanged -= OnInHideoutChanged;

        // Unsubscribe to prevent memory leaks
        controls.Player.Move.performed -= OnMovePerformed;
        controls.Player.Move.canceled -= OnMoveCanceled;
        controls.Player.Aim.performed -= OnAimPerformed;
        controls.Player.Sprint.performed -= OnSprintPerformed;
        controls.Player.Sprint.canceled -= OnMovementModifierCanceled;
        controls.Player.Sneak.performed -= OnSneakPerformed;
        controls.Player.Sneak.canceled -= OnMovementModifierCanceled;
        controls.Player.Prepare.started -= OnPrepareStarted;
        controls.Player.Prepare.canceled -= OnPrepareCanceled;
        controls.Player.Attack.performed -= OnAttackPerformed;

        if (backpackUI != null)
            backpackUI.OpenStateChanged -= OnBackpackOpenStateChanged;

        controls.Player.Disable();
    }

    /// <summary>
    /// Blocks all player gameplay input while the backpack is open and restores it when
    /// closed. UI input (pointer clicks, the Tab toggle) runs on a separate map and is
    /// unaffected.
    /// </summary>
    private void OnBackpackOpenStateChanged(bool open)
    {
        SetPlayerInputBlocked(open);
    }

    private void SetPlayerInputBlocked(bool blocked)
    {
        if (blocked)
        {
            controls.Player.Disable();
            moveInput = Vector2.zero;
            playerLegs?.SetLegsPosition(Vector2.zero);
            playerStamina?.SetMoving(false);
            currentAttack?.StopCharging();
            isAiming = false;
            ForceStopSprint();
        }
        else
        {
            controls.Player.Enable();
        }
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
        playerStamina?.SetMoving(moveInput.sqrMagnitude > 0.01f);
    }

    private void OnSprintPerformed(InputAction.CallbackContext context)
    {
        if (isAiming) return;
        if (CrouchLocked) return;

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
        ApplyMovementModifierState();
    }

    /// <summary>
    /// Fired when the player leaves the last hideout. Crouch was locked on while inside, so the
    /// mode now has to be re-derived from the keys actually held — Ctrl may have been released
    /// long before the player walked out from under the table.
    /// </summary>
    private void OnInHideoutChanged(bool inHideout)
    {
        if (!inHideout) ApplyMovementModifierState();
    }

    /// <summary>
    /// Re-derives the movement mode from the movement modifier keys currently held, or forces
    /// Sneak while crouch is locked by a hideout.
    /// </summary>
    private void ApplyMovementModifierState()
    {
        if (CrouchLocked)
        {
            movementState = MovementState.Sneaking;
            playerMovement.SetMovementMode(PlayerMovement.MovementMode.Sneak);
            playerStamina?.SetSprinting(false);
        }
        else if (!isAiming && controls.Player.Sprint.IsPressed() && (playerStamina == null || playerStamina.CanSprint()))
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
        playerStamina?.SetMoving(false);
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

    /// <summary>
    /// Triggered when prepare input is performed (mouse or joystick).
    /// </summary>
    private void OnPreparePerformed(InputAction.CallbackContext context)
    {
        if (gameStateManager != null && gameStateManager.IsPaused())
        {
            return;
        }
        currentAttack?.StartCharging();
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
        if (movementState == MovementState.Sprinting)
        {
            return;
        }
        if (currentAttack == null)
        {
            return;
        }
        isAiming = true;
        currentAttack.StartCharging();
    }

    /// <summary>
    /// Triggered when prepare input is canceled (mouse or joystick).
    /// </summary>
    private void OnPrepareCanceled(InputAction.CallbackContext context)
    {
        isAiming = false;
        currentAttack?.StopCharging();
    }

    /// <summary>
    /// Triggered when attack input is performed (mouse or joystick).
    /// </summary>
    private void OnAttackPerformed(InputAction.CallbackContext context)
    {
        if (currentAttack == null)
            return;
        if (playerStamina != null && !playerStamina.CanAttack())
            return;
        if (currentAttack.Fire())
            playerStamina?.UseAttackStamina();
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
