/// <summary>
/// Produces a dungeon layout from a seed. Implementations must be deterministic:
/// the same seed and parameters always yield an identical layout, because the save
/// system stores only the seed and rebuilds the dungeon from it on load.
/// </summary>
public interface IDungeonLayoutGenerator
{
    /// <summary>
    /// Generates a layout. Never returns null — when no attempt passes validation the
    /// best attempt so far is returned and the failure is logged, so a bad parameter
    /// set degrades into a playable dungeon rather than a crash.
    /// </summary>
    DungeonLayout Generate(string seed, LayoutParams parameters);
}
