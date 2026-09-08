using UnityEngine;

/// <summary>
/// Reports which way a character is currently facing, in degrees, with 0 = +X (right) and
/// counter-clockwise positive — the same convention as <see cref="Mathf.Atan2"/>.
/// Implemented by the component that owns the visual turning (e.g. the animation driver) so
/// sensors like <see cref="VisionPlayerDetector"/> can share one facing instead of deriving
/// their own and drifting out of sync with the sprite.
/// </summary>
public interface IFacingProvider
{
    /// <summary>Current facing direction in degrees, 0 = right, CCW positive.</summary>
    float FacingAngleDeg { get; }
}
