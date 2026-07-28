/// <summary>
/// Implemented by the player when they can be hidden from enemies by the world itself
/// (currently: crouched under a <see cref="CrouchHideout"/> such as a table).
/// Checked by <see cref="EnemyBase"/> before anything else — a concealed player is not
/// detected at all, not by any <see cref="IPlayerDetector"/> and not by alwaysDetectRange.
/// </summary>
public interface IPlayerConcealment
{
    /// <summary>True while the player is fully hidden and cannot be detected.</summary>
    bool IsConcealed { get; }
}
