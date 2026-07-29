using UnityEngine;

public class Projectile : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float speed = 15f;
    [SerializeField] private float lifeTime = 3f;

    [Header("Combat")]
    [SerializeField] private float damage = 25f;
    [SerializeField] private float knockbackForce = 1f;

    private Rigidbody2D rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();

        rb.linearVelocity = transform.right * speed;
        Destroy(gameObject, lifeTime);
    }

    // FixedUpdate runs on Unity's fixed physics timestep (same clock the
    // physics engine itself uses), which makes it the right place to do our
    // own manual collision check alongside/instead of relying on the engine's.

    private void FixedUpdate()
    {
        // OnTriggerEnter2D only fires if the physics engine notices an overlap
        // AT one of its fixed check points. A fast bullet can be before a thin
        // wall on one check and past it on the next, with no check in between
        // where they actually overlapped - so nothing ever fires. "Continuous"
        // mode mainly prevents this for solid (non-trigger) collisions, which
        // is why it didn't help here.
        //
        // Fix: before the bullet moves this step, we manually draw a line from
        // where it currently is to where it's ABOUT to be, and check everything
        // along that whole path ourselves. This can't be skipped over, no
        // matter how thin the wall or how fast the bullet.

        Vector2 currentPosition = rb.position;
        Vector2 nextPosition = currentPosition + rb.linearVelocity * Time.fixedDeltaTime;

        // LinecastAll returns every collider along that path (not just the
        // closest), so a decoration or pickup sitting in front of a wall
        // can't accidentally hide the wall from this check.
        RaycastHit2D[] hits = Physics2D.LinecastAll(currentPosition, nextPosition);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (RaycastHit2D hit in hits)
        {
            if (hit.collider.gameObject == gameObject) continue;

            if (TryHandleHit(hit.collider))
            {
                return;
            }
        }
    }

    // Returns true if this collider is something the bullet should react to
    // (and be destroyed for). Returns false for anything irrelevant, so
    // FixedUpdate's loop keeps checking further along the path instead of
    // stopping on something harmless.
    private bool TryHandleHit(Collider2D collision)
    {
        if (collision.TryGetComponent<EnemyBase>(out EnemyBase enemy))
        {
            enemy.TakeDamage(damage);
            enemy.Knockback(transform.right, knockbackForce);

            Destroy(gameObject);
            return true;
        }

        if (collision.CompareTag("Obstacle"))
        {
            Destroy(gameObject);
            return true;
        }

        return false;
    }
}