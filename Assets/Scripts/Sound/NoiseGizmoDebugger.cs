using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Debug visualizer: draws a fading wire circle in the Scene view for every noise emitted
/// through <see cref="NoiseEvents"/>, at its actual radius. Add to any GameObject in the scene
/// (e.g. GameManager) to see noise propagation live in Play mode. Each noise is classified by
/// matching its radius against <see cref="NoiseSettings"/> so it can be hidden per-source
/// (walk/sprint/throw/shoot) via the same flags as the static range previews.
/// </summary>
public class NoiseGizmoDebugger : MonoBehaviour
{
    [Tooltip("How long each noise circle stays visible before fully fading out.")]
    [SerializeField] private float displayDuration = 0.6f;
    [SerializeField] private Color noiseColor = Color.yellow;
    [SerializeField] private NoiseSettings noiseSettings;
    [Tooltip("Hides emitted-noise circles per source, matching the static Player noise range toggles.")]
    [SerializeField] private GizmoDebugSettings gizmoDebugSettings;

    private struct ActiveNoise
    {
        public Vector2 position;
        public float radius;
        public float emitTime;
        public GizmoRanges category;
    }

    private readonly List<ActiveNoise> activeNoises = new List<ActiveNoise>();

    private void OnEnable()
    {
        NoiseEvents.NoiseEmitted += OnNoiseEmitted;
    }

    private void OnDisable()
    {
        NoiseEvents.NoiseEmitted -= OnNoiseEmitted;
    }

    private void OnNoiseEmitted(Vector2 position, float radius)
    {
        activeNoises.Add(new ActiveNoise
        {
            position = position,
            radius = radius,
            emitTime = Time.time,
            category = ClassifyRadius(radius)
        });
    }

    /// <summary>Matches an emitted radius back to which NoiseSettings field it came from.</summary>
    private GizmoRanges ClassifyRadius(float radius)
    {
        if (noiseSettings == null) return GizmoRanges.None;

        if (Mathf.Approximately(radius, noiseSettings.walkNoiseRadius)) return GizmoRanges.PlayerWalkNoise;
        if (Mathf.Approximately(radius, noiseSettings.sprintNoiseRadius)) return GizmoRanges.PlayerSprintNoise;
        if (Mathf.Approximately(radius, noiseSettings.throwLandingRadius)) return GizmoRanges.PlayerThrowNoise;
        if (Mathf.Approximately(radius, noiseSettings.shootNoiseRadius)) return GizmoRanges.PlayerShootNoise;

        return GizmoRanges.None;
    }

    private void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        for (int i = activeNoises.Count - 1; i >= 0; i--)
        {
            float age = Time.time - activeNoises[i].emitTime;
            if (age >= displayDuration)
            {
                activeNoises.RemoveAt(i);
                continue;
            }

            if (gizmoDebugSettings != null && !gizmoDebugSettings.IsVisible(activeNoises[i].category))
                continue;

            float alpha = 1f - age / displayDuration;
            Gizmos.color = new Color(noiseColor.r, noiseColor.g, noiseColor.b, alpha);
            Gizmos.DrawWireSphere(activeNoises[i].position, activeNoises[i].radius);
        }
    }
}
