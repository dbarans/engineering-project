using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Entity-state provider for a chest: serializes its <see cref="ChestInventory"/> into
/// the entity payload, item ids and remaining durability included. Lives on the Chest
/// prefab next to <see cref="SaveableEntity"/>, which supplies the per-instance guid —
/// that guid is what keeps every chest on the map independent of every other one.
///
/// Two paths bring a chest back after a load, both already handled by
/// <see cref="SaveManager"/>:
/// - the chest is in the freshly loaded scene (authored, or respawned by the dungeon
///   generator from the same seed) — its saved payload is overlaid onto it;
/// - the chest is nowhere in the world — it is recreated from
///   <see cref="PrefabRegistry"/> via the <see cref="SaveableEntity.PrefabId"/> the save
///   recorded, then restored.
///
/// Restore overwrites every slot (see <see cref="ContainerSaveUtility.Restore"/>), so a
/// chest the player emptied stays empty even though the generator refilled it while
/// rebuilding the dungeon.
/// </summary>
[RequireComponent(typeof(ChestInventory))]
[RequireComponent(typeof(SaveableEntity))]
public class ChestSaveable : MonoBehaviour, ISaveableComponent
{
    public string TypeTag => "chest";

    public string CapturePayload()
    {
        return JsonConvert.SerializeObject(
            ContainerSaveUtility.Capture(GetComponent<ChestInventory>().Container));
    }

    public void RestorePayload(string payload)
    {
        var saved = JsonConvert.DeserializeObject<ContainerSaveData>(payload);
        ContainerSaveUtility.Restore(GetComponent<ChestInventory>().Container, saved, this);
    }
}
