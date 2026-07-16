using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Lets the player throw a noise-making object (a "rock") toward the mouse cursor to distract
/// hearing-based enemies: the projectile emits a noise where it lands (see <see cref="NoiseProjectile"/>).
/// The projectile is built in code (no prefab needed); a placeholder circle sprite is generated
/// at runtime, the same approach as MeleeAttack's area indicator.
///
/// Input: read directly from the keyboard (default G) — a temporary binding until a Throw
/// action is added to the PlayerControls input asset in the editor.
/// </summary>
public class PlayerThrow : MonoBehaviour
{
    [Tooltip("Temporary direct key binding; replace with a PlayerControls action when editing the input asset.")]
    [SerializeField] private Key throwKey = Key.G;
    [SerializeField] private float throwSpeed = 12f;
    [Tooltip("How far the landing noise carries (world units).")]
    [SerializeField] private float landingNoiseRadius = 10f;
    [Tooltip("Fallback despawn/emit time if the projectile never hits anything.")]
    [SerializeField] private float maxFlightTime = 1.5f;
    [SerializeField] private float throwCooldown = 1f;
    [Tooltip("Spawn offset along the throw direction so the projectile does not start inside the player collider.")]
    [SerializeField] private float spawnOffset = 0.6f;

    private static Sprite rockSprite;

    private float nextThrowTime;

    private void Update()
    {
        if (Keyboard.current == null || !Keyboard.current[throwKey].wasPressedThisFrame) return;
        if (Time.time < nextThrowTime) return;

        Vector2 direction = AimDirection();
        if (direction == Vector2.zero) return;

        nextThrowTime = Time.time + throwCooldown;
        SpawnProjectile(direction);
    }

    /// <summary>Direction from the player to the mouse cursor in world space.</summary>
    private Vector2 AimDirection()
    {
        Camera camera = Camera.main;
        if (camera == null || Mouse.current == null) return Vector2.zero;

        Vector3 mouseWorld = camera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        Vector2 direction = (Vector2)mouseWorld - (Vector2)transform.position;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;
    }

    /// <summary>
    /// Builds the projectile in code: sprite, physics, collider (ignoring the player's own
    /// colliders), and the landing-noise behaviour.
    /// </summary>
    private void SpawnProjectile(Vector2 direction)
    {
        var go = new GameObject("NoiseProjectile");
        go.transform.position = (Vector2)transform.position + direction * spawnOffset;

        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = GetRockSprite();
        renderer.color = new Color(0.45f, 0.42f, 0.38f);
        renderer.sortingOrder = 5;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.linearDamping = 1.5f;
        rb.linearVelocity = direction * throwSpeed;

        var collider = go.AddComponent<CircleCollider2D>();
        collider.radius = 0.12f;
        foreach (Collider2D playerCollider in GetComponentsInChildren<Collider2D>())
            Physics2D.IgnoreCollision(collider, playerCollider);

        var projectile = go.AddComponent<NoiseProjectile>();
        projectile.Initialize(landingNoiseRadius, maxFlightTime);
    }

    /// <summary>Generates (once) a small filled-circle placeholder sprite for the projectile.</summary>
    private static Sprite GetRockSprite()
    {
        if (rockSprite != null) return rockSprite;

        const int size = 16;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float center = (size - 1) / 2f;
        float radius = size / 2f - 1f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                texture.SetPixel(x, y, distance <= radius ? Color.white : Color.clear);
            }
        }
        texture.Apply();

        rockSprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 64f);
        return rockSprite;
    }
}
