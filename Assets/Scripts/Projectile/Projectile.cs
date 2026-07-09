using UnityEngine;

public class Projectile : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float speed = 15f;
    [SerializeField] private float lifeTime = 3f;

    [Header("Combat")]
    [SerializeField] private float damage = 25f;
    [SerializeField] private float knockbackForce = 1f;
    [Tooltip("Layers the projectile can hit: enemies deal damage, anything else just stops the bullet.")]
    [SerializeField] private LayerMask hitMask;

    private Rigidbody2D rb;
    private Vector2 lastPosition;
    private bool consumed;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        rb.linearVelocity = transform.right * speed;
        lastPosition = rb.position;
        Destroy(gameObject, lifeTime);
    }

    private void FixedUpdate()
    {
        // At high speeds the bullet covers several enemy-widths per physics step, and
        // trigger colliders get no continuous collision detection, so an overlap can be
        // skipped entirely. Sweep the segment travelled since the last step instead.
        Vector2 currentPosition = rb.position;
        RaycastHit2D hit = Physics2D.Linecast(lastPosition, currentPosition, hitMask);
        if (hit.collider != null)
        {
            HandleHit(hit.collider);
        }
        lastPosition = currentPosition;
    }

    // Fallback for point-blank shots where the bullet already spawns inside the target
    // and the swept segment is too short to cross the collider's edge.
    private void OnTriggerEnter2D(Collider2D collision)
    {
        if (((1 << collision.gameObject.layer) & hitMask.value) == 0) return;
        HandleHit(collision);
    }

    private void HandleHit(Collider2D collider)
    {
        if (consumed) return;
        consumed = true;

        if (collider.TryGetComponent<EnemyBase>(out EnemyBase enemy))
        {
            enemy.TakeDamage(damage);
            enemy.Knockback(transform.right, knockbackForce);
        }

        Destroy(gameObject);
    }
}
