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
    private void FixedUpdate()
    {
        Vector2 currentPosition = rb.position;
        Vector2 nextPosition = currentPosition + rb.linearVelocity * Time.fixedDeltaTime;

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