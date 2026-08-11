using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Entity-state provider for a barricaded door: saves how many <see cref="DoorBarricade"/>
/// stages are standing, so a door the player nailed shut is still nailed shut after a
/// save/load instead of quietly resetting.
///
/// Lives on the door's root GameObject (<c>Door_System</c>) next to
/// <see cref="SaveableEntity"/> — the same GameObject <see cref="PrefabRegistry.Spawn"/>
/// and <see cref="SaveableEntity.Capture"/>/<see cref="SaveableEntity.Restore"/> look for
/// a single <see cref="ISaveableComponent"/> on — while the actual
/// <see cref="DoorBarricade"/> and <see cref="SimpleDoor"/> live one level down on
/// <c>Door_Visual</c>. This reaches into children to find it rather than requiring the
/// door prefab to be flattened.
///
/// Scoped to the barricade only: <see cref="SimpleDoor"/> itself (locked/open state,
/// remaining health, destroyed) has no <see cref="ISaveableComponent"/> of its own yet,
/// so that part of a door still resets to its authored state on every load.
/// </summary>
[RequireComponent(typeof(SaveableEntity))]
public class DoorBarricadeSaveable : MonoBehaviour, ISaveableComponent
{
    public string TypeTag => "doorBarricade";

    private DoorBarricade _barricade;

    private DoorBarricade Barricade =>
        _barricade != null ? _barricade : (_barricade = GetComponentInChildren<DoorBarricade>(true));

    public string CapturePayload()
    {
        DoorBarricade barricade = Barricade;
        if (barricade == null) return null;

        return JsonConvert.SerializeObject(new DoorBarricadeSaveState { stages = barricade.Stages });
    }

    public void RestorePayload(string payload)
    {
        DoorBarricade barricade = Barricade;
        if (barricade == null || string.IsNullOrEmpty(payload)) return;

        var state = JsonConvert.DeserializeObject<DoorBarricadeSaveState>(payload);
        if (state != null) barricade.SetStages(state.stages);
    }
}
