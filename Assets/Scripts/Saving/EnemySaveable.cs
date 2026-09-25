using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Entity-state provider for enemies (plan Phase 4, §5.4): serializes the
/// <see cref="EnemyBase"/> save hooks into the entity payload. One component covers the
/// whole enemy hierarchy — the concrete type comes back with the scene (load = scene
/// reload), only the mutable state travels through the save. Lives on each enemy
/// prefab next to <see cref="SaveableEntity"/>.
/// </summary>
[RequireComponent(typeof(EnemyBase))]
[RequireComponent(typeof(SaveableEntity))]
public class EnemySaveable : MonoBehaviour, ISaveableComponent
{
    public string TypeTag => "enemy";

    public string CapturePayload()
    {
        return JsonConvert.SerializeObject(GetComponent<EnemyBase>().CaptureSaveState());
    }

    public void RestorePayload(string payload)
    {
        var state = JsonConvert.DeserializeObject<EnemySaveState>(payload);
        if (state != null)
            GetComponent<EnemyBase>().RestoreSaveState(state);
    }
}
