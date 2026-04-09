using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour, IPlayerMovement
{
    
    [Header("Movement Speed")]
    [SerializeField] private float sneakSpeed = 2f;
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 8f;

    public enum MovementMode {Walk, Sprint, Sneak}
    private float currentSpeed;
    private MovementMode currentMode = MovementMode.Walk;
    private Rigidbody2D rb;

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
        currentMode = newMode;
        ApplyModeSettings();
    }

    public void Move(Vector2 direction)
    {
        rb.linearVelocity = direction * currentSpeed;
    }
}