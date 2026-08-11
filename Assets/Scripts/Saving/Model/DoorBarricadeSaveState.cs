/// <summary>
/// Saved state of a <see cref="DoorBarricade"/>: how many stages were standing. Health is
/// not stored — it is always <c>stages * healthPerStage</c>, so it is recomputed on
/// restore from whatever the door's current settings say rather than saved separately,
/// which also means a later balance change to healthPerStage does not desync old saves.
/// </summary>
[System.Serializable]
public class DoorBarricadeSaveState
{
    public int stages;
}
