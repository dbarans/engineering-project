/// <summary>
/// Save hooks for scene-level systems (player vitals, inventory, world item collector).
/// <see cref="SaveManager"/> discovers implementers among scene MonoBehaviours on every
/// save/load. Restore only overlays saved state on a freshly loaded scene — it must
/// never "undo" anything (plan decision D5). For per-object world state use
/// <see cref="SaveableEntity"/> instead.
/// </summary>
public interface ISaveable
{
    /// <summary>Writes this system's state into <paramref name="data"/>.</summary>
    void Capture(GameSaveData data);

    /// <summary>
    /// Applies state from <paramref name="data"/>. Runs after the scene's Start()
    /// methods (which reset vitals to max), on the frame after the scene loaded.
    /// </summary>
    void Restore(GameSaveData data);
}
