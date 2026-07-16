using UnityEngine;

/// <summary>
/// A thrown object that emits a noise (via <see cref="NoiseEvents"/>) where it lands —
/// used to distract hearing-based enemies. Emits on the first collision or, as a fallback,
/// after maxFlightTime, then destroys itself. Configured by <see cref="PlayerThrow"/> on spawn.
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class NoiseProjectile : MonoBehaviour
{
    private float noiseRadius;
    private float maxFlightTime;
    private float spawnTime;
    private bool hasLanded;

    /// <summary>
    /// Sets landing parameters. Called by the thrower right after spawning.
    /// </summary>
    /// <param name="radius">How far the landing noise carries.</param>
    /// <param name="flightTime">Fallback: emit and despawn after this many seconds even without hitting anything.</param>
    public void Initialize(float radius, float flightTime)
    {
        noiseRadius = radius;
        maxFlightTime = flightTime;
        spawnTime = Time.time;
    }

    private void Update()
    {
        if (!hasLanded && Time.time - spawnTime >= maxFlightTime)
            Land();
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        Land();
    }

    /// <summary>Emits the landing noise once and removes the projectile.</summary>
    private void Land()
    {
        if (hasLanded) return;
        hasLanded = true;

        NoiseEvents.Emit(transform.position, noiseRadius);
        Destroy(gameObject);
    }
}
