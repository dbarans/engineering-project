using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Entity-state provider for a door: the <see cref="SimpleDoor"/> leaf's own state (open,
/// locked, destroyed, health, which way it swung) and how many <see cref="DoorBarricade"/>
/// stages are nailed across it. A door the player opened is still open after a save/load,
/// and one they barricaded is still barricaded.
///
/// Lives on the door's root GameObject (<c>Door_System</c>) next to
/// <see cref="SaveableEntity"/> — the same GameObject <see cref="PrefabRegistry.Spawn"/>
/// and <see cref="SaveableEntity.Capture"/>/<see cref="SaveableEntity.Restore"/> look for
/// a single <see cref="ISaveableComponent"/> on — while the actual
/// <see cref="DoorBarricade"/> and <see cref="SimpleDoor"/> live one level down on
/// <c>Door_Visual</c>. This reaches into children to find them rather than requiring the
/// door prefab to be flattened.
///
/// It covers the leaf rather than letting <see cref="SimpleDoor"/> speak for itself, even
/// though that class implements <see cref="ISaveableComponent"/> perfectly well. The entity
/// only ever consults the one component sitting beside it, and the guid it is saved under
/// belongs to the root — the one <see cref="PrefabRegistry.Spawn"/> stamps with the
/// generator's deterministic id. A second <see cref="SaveableEntity"/> on the leaf would
/// draw a random guid at spawn instead, and match nothing on the next load.
/// </summary>
[RequireComponent(typeof(SaveableEntity))]
public class DoorBarricadeSaveable : MonoBehaviour, ISaveableComponent
{
    /// <summary>The two halves of a door's state, each serialized by whoever owns it.</summary>
    [System.Serializable]
    private class DoorEntityState
    {
        public int barricadeStages;

        /// <summary>
        /// <see cref="SimpleDoor"/>'s own payload, carried verbatim. Kept as an opaque
        /// string so the leaf stays the only thing that decides what a door remembers.
        /// </summary>
        public string door;
    }

    public string TypeTag => "door";

    private DoorBarricade _barricade;
    private SimpleDoor _door;

    private DoorBarricade Barricade =>
        _barricade != null ? _barricade : (_barricade = GetComponentInChildren<DoorBarricade>(true));

    private SimpleDoor Door =>
        _door != null ? _door : (_door = GetComponentInChildren<SimpleDoor>(true));

    public string CapturePayload()
    {
        DoorBarricade barricade = Barricade;
        SimpleDoor door = Door;
        if (barricade == null && door == null) return null;

        return JsonConvert.SerializeObject(new DoorEntityState
        {
            barricadeStages = barricade != null ? barricade.Stages : 0,
            door = door != null ? door.CapturePayload() : null,
        });
    }

    public void RestorePayload(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return;

        var state = JsonConvert.DeserializeObject<DoorEntityState>(payload);
        if (state == null) return;

        // The leaf first: a barricade is nailed across a door, so restoring the planks onto
        // a door that has not yet been told it is open reads the wrong way round.
        SimpleDoor door = Door;
        if (door != null && !string.IsNullOrEmpty(state.door)) door.RestorePayload(state.door);

        DoorBarricade barricade = Barricade;
        if (barricade != null) barricade.SetStages(state.barricadeStages);
    }
}
