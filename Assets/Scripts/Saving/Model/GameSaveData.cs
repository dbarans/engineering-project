using System;
using System.Collections.Generic;

/// <summary>
/// Root DTO of a save file — one instance per slot, serialized to JSON by
/// <see cref="SaveManager"/>. Pure data: no Unity object references, only stable
/// ids/guids (see docs/save-system-plan.md §5.1). <see cref="version"/> is written
/// from day one so old files can be migrated (or invalidated) when the schema changes.
/// </summary>
[Serializable]
public class GameSaveData
{
    /// <summary>Schema version written into new saves. Bump on breaking model changes.</summary>
    public const int CurrentVersion = 1;

    public int version = CurrentVersion;

    /// <summary>Shown in the slot list UI without touching the rest of the file.</summary>
    public SaveMetadata meta;

    /// <summary>Scene to load before restoring state.</summary>
    public string currentScene;

    public PlayerSaveData player;

    /// <summary>Per-scene world state, keyed by scene name — visited scenes keep theirs.</summary>
    public Dictionary<string, SceneSaveData> scenes = new Dictionary<string, SceneSaveData>();

    /// <summary>Returns the state bucket of the given scene, creating an empty one on first use.</summary>
    public SceneSaveData GetOrCreateScene(string sceneName)
    {
        if (!scenes.TryGetValue(sceneName, out var scene))
        {
            scene = new SceneSaveData();
            scenes[sceneName] = scene;
        }
        return scene;
    }
}

/// <summary>Slot-list display data: "empty" vs date + level + HP.</summary>
[Serializable]
public class SaveMetadata
{
    /// <summary>Save timestamp, ISO 8601 (<c>DateTime.UtcNow.ToString("o")</c>).</summary>
    public string savedAtUtc;
    public string sceneDisplayName;
    public int playerHealth;
    public float playtimeSeconds;
}
