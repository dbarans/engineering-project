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
}

[CreateAssetMenu(fileName = "GizmoDebugSettings", menuName = "Debug/Gizmo Debug Settings")]
public class GizmoDebugSettings : ScriptableObject
{
    public GizmoRanges visibleRanges = (GizmoRanges)~0;

    public bool IsVisible(GizmoRanges range) => (visibleRanges & range) != 0;
}
