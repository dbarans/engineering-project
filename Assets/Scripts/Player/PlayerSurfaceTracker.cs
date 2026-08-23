using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps track of which <see cref="NoisySurface"/> patches the player is currently standing
/// on, and answers the only question <see cref="PlayerNoiseEmitter"/> asks: what does the
/// floor under my feet do to my footsteps right now.
///
/// A list rather than a single reference because patches overlap — the generator scatters
/// glass in clusters, so a player crossing one usually stands on two at once. Overlapping
/// patches resolve to the <em>loudest</em> one for the current movement mode: the quiet
/// patch does not muffle the loud one you are also standing on.
///
/// Added automatically by <see cref="PlayerNoiseEmitter"/> when the player prefab does not
/// carry one, so this feature needs no prefab re-authoring to work.
/// </summary>
public class PlayerSurfaceTracker : MonoBehaviour
{
    /// <summary>
    /// Every live tracker. There is one player, but tracker lookup happens from the patch
    /// side and a patch has to be able to clean itself out of whatever is tracking it when
    /// it disappears — see <see cref="ForgetEverywhere"/>.
    /// </summary>
    private static readonly List<PlayerSurfaceTracker> Trackers = new List<PlayerSurfaceTracker>();

    private readonly List<NoisySurface> current = new List<NoisySurface>();

    /// <summary>True while the player stands on at least one noisy surface.</summary>
    public bool HasSurface => current.Count > 0;

    private void OnEnable() => Trackers.Add(this);

    private void OnDisable()
    {
        Trackers.Remove(this);
        current.Clear();
    }

    /// <summary>
    /// The tracker belonging to the collider that entered a patch, or null when it has none.
    /// The collider may be a child of the player root (the movement collider usually is), so
    /// this searches upwards.
    /// </summary>
    public static PlayerSurfaceTracker For(Collider2D collider)
    {
        return collider != null ? collider.GetComponentInParent<PlayerSurfaceTracker>() : null;
    }

    /// <summary>Removes a patch from every tracker holding it.</summary>
    public static void ForgetEverywhere(NoisySurface surface)
    {
        for (int i = 0; i < Trackers.Count; i++)
            Trackers[i].current.Remove(surface);
    }

    public void Enter(NoisySurface surface)
    {
        if (surface == null || current.Contains(surface)) return;
        current.Add(surface);
    }

    public void Exit(NoisySurface surface)
    {
        current.Remove(surface);
    }

    /// <summary>
    /// The loudest radius any surface underfoot gives this movement mode, or 0 when the
    /// player is on ordinary floor (in which case the emitter falls back to its own ranges).
    /// </summary>
    public float NoiseRadiusFor(PlayerMovement.MovementMode mode)
    {
        float loudest = 0f;
        for (int i = current.Count - 1; i >= 0; i--)
        {
            // Destroyed patches are pruned lazily here as well as in NoisySurface.OnDisable:
            // Destroy() does run OnDisable, but a scene unload does not guarantee ordering.
            if (current[i] == null)
            {
                current.RemoveAt(i);
                continue;
            }

            loudest = Mathf.Max(loudest, current[i].NoiseRadiusFor(mode));
        }

        return loudest;
    }

    /// <summary>
    /// Step sound of the loudest surface underfoot for this mode, or null on ordinary floor.
    /// Tied to the same surface the radius came from so what the player hears matches what
    /// the enemies heard.
    /// </summary>
    public string StepSoundFor(PlayerMovement.MovementMode mode)
    {
        NoisySurface loudest = null;
        float best = 0f;

        foreach (NoisySurface surface in current)
        {
            if (surface == null) continue;

            float radius = surface.NoiseRadiusFor(mode);
            if (loudest != null && radius <= best) continue;

            loudest = surface;
            best = radius;
        }

        return loudest != null ? loudest.StepSoundId : null;
    }
}
