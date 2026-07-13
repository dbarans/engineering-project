using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Captures and restores items lying on the ground (plan Phase 3, §5.6). Drops are
/// fungible, so there are no per-instance guids — just a list of {itemId, count,
/// position} in the scene's save bucket. On restore the save is the source of truth:
/// every <see cref="WorldItem"/> in the scene is despawned first, then the saved list
/// is respawned through <see cref="WorldItemPickup.SpawnAt"/>. The pickup delay is
/// deliberately not armed — there is no drop click to block. Lives on the
/// WorldItemPickup prefab, next to the spawner it uses.
/// </summary>
[RequireComponent(typeof(WorldItemPickup))]
public class WorldItemsSaveable : MonoBehaviour, ISaveable
{
    public void Capture(GameSaveData data)
    {
        var dropped = new List<DroppedItemSaveData>();
        foreach (var item in FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
        {
            if (item.Item == null || item.Count <= 0) continue;

            if (string.IsNullOrEmpty(item.Item.Id))
            {
                Debug.LogWarning(
                    $"[WorldItemsSaveable] '{item.Item.name}' has no stable id — drop not " +
                    "saved. Run Tools ▸ Save System ▸ Rebuild Item Database.", item);
                continue;
            }

            dropped.Add(new DroppedItemSaveData
            {
                itemId = item.Item.Id,
                count = item.Count,
                position = new[] { item.transform.position.x, item.transform.position.y }
            });
        }

        // Replace, never append — the bucket may still carry the list loaded from an
        // earlier save of this scene.
        data.GetOrCreateScene(gameObject.scene.name).droppedItems = dropped;
    }

    public void Restore(GameSaveData data)
    {
        if (data.scenes == null ||
            !data.scenes.TryGetValue(gameObject.scene.name, out var scene) ||
            scene.droppedItems == null)
            return; // save predates world data — leave the fresh scene alone

        foreach (var item in FindObjectsByType<WorldItem>(FindObjectsSortMode.None))
            Destroy(item.gameObject);

        var pickup = GetComponent<WorldItemPickup>();
        var database = ItemDatabase.Instance;
        foreach (var saved in scene.droppedItems)
        {
            if (saved == null || saved.position == null || saved.position.Length < 2)
                continue;

            var item = database != null ? database.Resolve(saved.itemId) : null;
            if (item == null)
            {
                Debug.LogWarning(
                    $"[WorldItemsSaveable] Item id '{saved.itemId}' from the save is not in " +
                    "the ItemDatabase (asset removed?) — drop skipped.", this);
                continue;
            }

            pickup.SpawnAt(item, saved.count, new Vector2(saved.position[0], saved.position[1]));
        }
    }
}
