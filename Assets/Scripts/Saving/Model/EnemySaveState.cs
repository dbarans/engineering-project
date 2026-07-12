using System;

/// <summary>
/// Enemy-specific state carried in <see cref="EntityState.payload"/>. Covers the whole
/// EnemyBase hierarchy — new enemy types inherit saving for free and may append their
/// own payload. Waypoints are saved as an index, never a Transform reference.
/// </summary>
[Serializable]
public class EnemySaveState
{
    public float health;

    /// <summary>
    /// (int)EnemyState. Restore maps InvestigateLastKnown to ReturnToPatrol — the
    /// private investigate fields are not saved and would leave the enemy stuck (plan §5.4).
    /// </summary>
    public int aiState;

    public int waypointIndex;

    /// <summary>Last seen player position as [x, y]; null when the enemy never saw the player.</summary>
    public float[] lastKnownPlayerPos;
}
