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

    /// <summary>
    /// The post this enemy guards / roams around, as [x, y]. Runtime-spawned enemies get their
    /// position from the save after Awake has already run, so without this a loaded guard would
    /// treat wherever the prefab was instantiated as home. Null in saves written before this
    /// field existed — the enemy then keeps the post it picked up on spawn.
    /// </summary>
    public float[] homePos;
}
