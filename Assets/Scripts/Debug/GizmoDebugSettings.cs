using System;
using UnityEngine;

/// <summary>
/// Controls which range gizmos are drawn in the Scene view for tuning purposes. Purely an
/// editor visualization toggle — has no effect on gameplay or builds (Gizmos never draw
/// outside the editor). One shared asset so every component can be toggled from one place.
/// </summary>
[Flags]
public enum GizmoRanges
{
    None = 0,
    PlayerWalkNoise = 1 << 0,
    PlayerSprintNoise = 1 << 1,
    PlayerThrowNoise = 1 << 2,
    PlayerShootNoise = 1 << 3,
    EnemyVision = 1 << 4,
    EnemyHearing = 1 << 5,
    EnemyAlwaysDetect = 1 << 6,
    LightRadius = 1 << 7,

    /// <summary>
    /// Any emitted noise whose radius matches no <see cref="NoiseSettings"/> field: a step
    /// on a <see cref="NoisySurface"/>, a barricade stage breaking, a door being smashed.
    /// Those sources carry their own radii on the component that makes them, so there is
    /// nothing central to match them against — without this catch-all they were classified
    /// as <see cref="None"/> and therefore never drawn at all.
    /// </summary>
    OtherNoise = 1 << 8,
}

[CreateAssetMenu(fileName = "GizmoDebugSettings", menuName = "Debug/Gizmo Debug Settings")]
public class GizmoDebugSettings : ScriptableObject
{
    public GizmoRanges visibleRanges = (GizmoRanges)~0;

    public bool IsVisible(GizmoRanges range) => (visibleRanges & range) != 0;
}
