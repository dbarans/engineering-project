using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour, IPlayerMovement
{
    [SerializeField] private float moveSpeed = 5f;
    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    public void Move(Vector2 direction)
    {
        rb.linearVelocity = direction * moveSpeed;
    }
}