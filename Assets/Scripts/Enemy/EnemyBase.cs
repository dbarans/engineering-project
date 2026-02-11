using UnityEngine;

/// <summary>
/// Abstract base class for all enemy types.
/// Defines common properties and methods that must be implemented by concrete types.
/// Uses IMovementStrategy for movement (composition over inheritance).
/// </summary>
public abstract class EnemyBase : MonoBehaviour
{
    [Header("Stats")]
    [SerializeField] protected float maxHealth = 100f;
    [SerializeField] protected float moveSpeed = 3f;

    protected float currentHealth;
    private IMovementStrategy movementStrategy;

    /// <summary>
    /// Current health of the enemy.
    /// </summary>
    public float CurrentHealth => currentHealth;

    /// <summary>
    /// Maximum health of the enemy.
    /// </summary>
    public float MaxHealth => maxHealth;

    /// <summary>
    /// Whether the enemy is dead.
    /// </summary>
    public bool IsDead => currentHealth <= 0f;

    protected virtual void Awake()
    {
        currentHealth = maxHealth;
        movementStrategy = GetComponent<IMovementStrategy>();

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.constraints = RigidbodyConstraints2D.FreezeRotation;
    }

    protected virtual void Update()
    {
        if (!IsDead && movementStrategy != null)
        {
            Move();
        }
    }

    /// <summary>
    /// Applies damage to the enemy.
    /// </summary>
    /// <param name="damage">Amount of damage.</param>
    public virtual void TakeDamage(float damage)
    {
        if (IsDead) return;

        currentHealth = Mathf.Max(0f, currentHealth - damage);

        if (IsDead)
        {
            OnDeath();
        }
        else
        {
            OnDamageTaken(damage);
        }
    }

    /// <summary>
    /// Called when enemy takes damage (before death).
    /// Derived types can override for visual/audio effects.
    /// </summary>
    protected virtual void OnDamageTaken(float damage)
    {
        // Optional implementation in derived classes
    }

    /// <summary>
    /// Called when enemy health drops to zero.
    /// Must be implemented by derived classes.
    /// </summary>
    protected abstract void OnDeath();

    /// <summary>
    /// Target position for movement. Override in derived classes.
    /// </summary>
    protected abstract Vector3 GetTargetPosition();

    /// <summary>
    /// Delegates movement to IMovementStrategy. Override for custom movement.
    /// </summary>
    protected virtual void Move()
    {
        movementStrategy?.Move(transform, GetTargetPosition(), moveSpeed);
    }
}
