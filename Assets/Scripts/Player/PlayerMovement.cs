using System;
using UnityEngine;

/// <summary>
/// Handles player movement and movement mode (walk / sprint / sneak).
/// Sneak is intentionally silent for enemies with hearing-based detection; see <see cref="SoundPlayerDetector"/>.
/// Sneak doubles as crouching: it is what lets the player fit under a table (see <see cref="PlayerHiding"/>).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour, IPlayerMovement
{

    [Header("Movement Speed")]
    [SerializeField] private float sneakSpeed = 2f;
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;

    [Tooltip("Minimum speed to be considered actually moving (vs. standing still while in Walk/Sprint mode).")]
    [SerializeField] private float movingSpeedThreshold = 0.05f;

    public enum MovementMode {Walk, Sprint, Sneak}
    private float currentSpeed;
    private MovementMode currentMode = MovementMode.Walk;
    private Rigidbody2D rb;

    /// <summary>
    /// Current movement mode. Used by hearing-based enemy detection to decide whether
    /// the player is currently making noise (Sneak is silent).
    /// </summary>
    public MovementMode CurrentMode => currentMode;

    /// <summary>
    /// Raised whenever the mode actually changes. Lets components react to crouching
    /// (entering/leaving Sneak) without polling every frame; see <see cref="PlayerHiding"/>.
    /// </summary>
    public event Action<MovementMode> MovementModeChanged;

    /// <summary>
    /// True while the player is actually moving (not just standing still in Walk/Sprint mode).
    /// Standing still makes no noise even outside Sneak mode.
    /// </summary>
    public bool IsMoving => rb.linearVelocity.sqrMagnitude > movingSpeedThreshold * movingSpeedThreshold;

    private void ApplyModeSettings()
    {
        switch(currentMode)
        {
            case MovementMode.Sneak:
                currentSpeed = sneakSpeed;
                break;
            case MovementMode.Sprint:
                currentSpeed = sprintSpeed;
                break;
            default:
                currentSpeed = walkSpeed;
                break;
        }
    }

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        ApplyModeSettings();
    }

    public void SetMovementMode (MovementMode newMode)
    {
        bool changed = currentMode != newMode;
        currentMode = newMode;
        ApplyModeSettings();
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        if (changed) MovementModeChanged?.Invoke(currentMode);
    }

    public void Move(Vector2 direction)
    {
        rb.linearVelocity = direction * currentSpeed;
    }
}