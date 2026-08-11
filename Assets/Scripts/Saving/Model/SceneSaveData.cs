using System;
using System.Collections.Generic;

/// <summary>
/// Dynamic state of one scene: what changed relative to the authored (or generated)
/// baseline — entity states keyed by <see cref="SaveableEntity"/> guid plus items
/// lying on the ground. Static geometry is never saved.
/// </summary>
[Serializable]
public class SceneSaveData
{
    /// <summary>
    /// Seed for a procedurally generated dungeon; null while the dungeon is static
    /// (kept in the model from day one so switching later is cheap — plan §4).
    /// </summary>
    public string generationSeed;

    /// <summary>Persistent world objects (enemies, chests…), keyed by SaveableEntity guid.</summary>
    public Dictionary<string, EntityState> entities = new Dictionary<string, EntityState>();

    /// <summary>Items lying on the ground (WorldItem drops/loot). Fungible — no guids.</summary>
    public List<DroppedItemSaveData> droppedItems = new List<DroppedItemSaveData>();
}

/// <summary>
/// State of a single persistent world object. Common fields live here; type-specific
/// state (e.g. <see cref="EnemySaveState"/>) is serialized into <see cref="payload"/>
/// and dispatched on <see cref="typeTag"/> at restore.
/// </summary>
[Serializable]
public class EntityState
{
    /// <summary>Registry id used to respawn runtime-spawned entities; null for scene-authored ones.</summary>
    public string prefabId;

    /// <summary>World position as [x, y].</summary>
    public float[] position;

    /// <summary>
    /// False = permanently dead/consumed. Needed because dead enemies are deactivated,
    /// not destroyed — without this marker they would come back after loading.
    /// </summary>
    public bool alive = true;

    /// <summary>Restore dispatch tag, e.g. "enemy" / "loot" / "chest".</summary>
    public string typeTag;

    /// <summary>JSON of the type-specific state matching <see cref="typeTag"/>.</summary>
    public string payload;
}

/// <summary>One WorldItem lying on the ground.</summary>
[Serializable]
public class DroppedItemSaveData
{
    public string itemId;
    public int count;

    /// <summary>
    /// Remaining durability the drop carries (see <see cref="ItemStack.durability"/>).
    /// Defaults to <c>-1</c> ("full/undamaged") so saves written before ground items
    /// tracked wear restore them intact rather than broken.
    /// </summary>
    public int durability = -1;

    /// <summary>World position as [x, y].</summary>
    public float[] position;
}
