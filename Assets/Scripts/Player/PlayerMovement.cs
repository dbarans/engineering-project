using System;
using UnityEngine;

/// <summary>
/// Handles player movement and movement mode (walk / sprint / sneak / dragging).
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

    public enum MovementMode { Walk, Sprint, Sneak }
    private float currentSpeed;
    private MovementMode currentMode = MovementMode.Walk;
    private Rigidbody2D rb;

    private bool isDragging = false;
    private float customDragSpeed = 2.0f;

    public MovementMode CurrentMode => currentMode;
    public event Action<MovementMode> MovementModeChanged;

    public bool IsMoving => rb.linearVelocity.sqrMagnitude > movingSpeedThreshold * movingSpeedThreshold;
    public bool IsSprinting => currentMode == MovementMode.Sprint && IsMoving && !isDragging;

    private void ApplyModeSettings()
    {
        if (isDragging)
        {
            currentSpeed = customDragSpeed;
            return;
        }

        switch (currentMode)
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

    public void SetMovementMode(MovementMode newMode)
    {
        bool changed = currentMode != newMode;
        currentMode = newMode;
        ApplyModeSettings();
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;

        if (changed) MovementModeChanged?.Invoke(currentMode);
    }

    /// <summary>
    /// Locks/unlocks player speed to the dragging speed while pulling objects.
    /// </summary>
    public void SetDragging(bool dragging, float speed = 2.0f)
    {
        isDragging = dragging;
        customDragSpeed = speed;
        ApplyModeSettings();
    }

    public void Move(Vector2 direction)
    {
        rb.linearVelocity = direction * currentSpeed;
    }
}