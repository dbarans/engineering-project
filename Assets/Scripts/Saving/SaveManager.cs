using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Save/load orchestrator: 5 slots, one JSON file per slot in
/// <c>Application.persistentDataPath</c> (the only writable path on Android).
/// Writes are atomic (tmp file + replace) so a crash mid-save never destroys the
/// previous save. Load reloads the saved scene, then restores state on top of it —
/// restore only overlays, it never undoes anything (plan decision D5).
///
/// Survives scene reloads via DontDestroyOnLoad; access through <see cref="Instance"/>,
/// which bootstraps the object on first use — no scene wiring needed.
/// </summary>
public class SaveManager : MonoBehaviour
{
    /// <summary>Number of save slots, 0-based (decision D3).</summary>
    public const int MaxSlots = 5;

    private static SaveManager _instance;

    /// <summary>Raised right after the saved scene finished loading, before entity
    /// restore — the future procedural generator's entry point (plan §4). Static
    /// dungeon = nobody listens.</summary>
    public static event Action<GameSaveData> BuildWorld;

    private GameSaveData _pendingLoad;   // set by Load, consumed by OnSceneLoaded
    private GameSaveData _session;       // last loaded save; keeps other scenes' state

    /// <summary>The persistent instance, created on first access.</summary>
    public static SaveManager Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject(nameof(SaveManager));
                _instance = go.AddComponent<SaveManager>();
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }
        _instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    /// <summary>
    /// Captures the game state and writes it to the given slot (overwrites silently —
    /// the confirmation dialog is UI's job). Returns false when capture or IO failed;
    /// a failed write never corrupts the slot's previous save.
    /// </summary>
    public bool Save(int slot)
    {
        if (!IsValidSlot(slot)) return false;

        GameSaveData data;
        try
        {
            data = CaptureGame();
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] Capture failed, slot {slot} untouched: {e}", this);
            return false;
        }

        try
        {
            WriteAtomic(SlotPath(slot), JsonConvert.SerializeObject(data, Formatting.Indented));
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] Writing slot {slot} failed: {e}", this);
            return false;
        }

        _session = data;
        return true;
    }

    /// <summary>
    /// Loads the given slot: reads the file, reloads the saved scene, then restores
    /// state after the scene's Start() methods ran. Returns false when the slot is
    /// empty, unreadable or points at an unknown scene — the running game is untouched.
    /// </summary>
    public bool Load(int slot)
    {
        var data = ReadSlot(slot, logErrors: true);
        if (data == null) return false;

        if (data.version > GameSaveData.CurrentVersion)
        {
            Debug.LogError(
                $"[SaveManager] Slot {slot} has version {data.version}, newer than " +
                $"supported {GameSaveData.CurrentVersion} — refusing to load.", this);
            return false;
        }

        if (string.IsNullOrEmpty(data.currentScene) ||
            !Application.CanStreamedLevelBeLoaded(data.currentScene))
        {
            Debug.LogError(
                $"[SaveManager] Slot {slot} references scene '{data.currentScene}' " +
                "which cannot be loaded (renamed or missing from build settings).", this);
            return false;
        }

        _pendingLoad = data;
        SceneManager.LoadScene(data.currentScene);
        return true;
    }

    /// <summary>True when the slot holds a save file (readable or not).</summary>
    public bool HasSave(int slot)
    {
        return IsValidSlot(slot) && File.Exists(SlotPath(slot));
    }

    /// <summary>Deletes the slot's save file. Missing file is not an error.</summary>
    public void DeleteSave(int slot)
    {
        if (!IsValidSlot(slot)) return;
        try
        {
            File.Delete(SlotPath(slot));
            File.Delete(SlotPath(slot) + ".tmp"); // stale leftover from a crashed save
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] Deleting slot {slot} failed: {e}", this);
        }
    }

    /// <summary>
    /// Metadata of every slot for the save/load UI; index = slot, null = empty
    /// (or unreadable) slot.
    /// </summary>
    public SaveMetadata[] GetSlotInfos()
    {
        var infos = new SaveMetadata[MaxSlots];
        for (int slot = 0; slot < MaxSlots; slot++)
            infos[slot] = ReadSlot(slot, logErrors: false)?.meta;
        return infos;
    }

    private GameSaveData CaptureGame()
    {
        string sceneName = SceneManager.GetActiveScene().name;

        var data = new GameSaveData { currentScene = sceneName };

        // Carry over state of scenes visited earlier in this session, so saving in
        // scene B does not lose what the loaded save knew about scene A.
        if (_session != null)
        {
            foreach (var pair in _session.scenes)
                data.scenes[pair.Key] = pair.Value;
        }
        data.GetOrCreateScene(sceneName); // current scene is recaptured from live state

        foreach (var saveable in FindSaveables())
            saveable.Capture(data);

        data.meta = new SaveMetadata
        {
            savedAtUtc = DateTime.UtcNow.ToString("o"),
            sceneDisplayName = sceneName,
            playerHealth = data.player != null ? data.player.health : 0
        };
        return data;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (_pendingLoad == null || scene.name != _pendingLoad.currentScene) return;

        var data = _pendingLoad;
        _pendingLoad = null;
        StartCoroutine(RestoreRoutine(data));
    }

    private IEnumerator RestoreRoutine(GameSaveData data)
    {
        BuildWorld?.Invoke(data); // procedural hook — no-op for the static dungeon

        // Wait one frame so every Start() has run: player systems reset vitals to max
        // there and would overwrite restored values (plan §5.5). Real time, so this
        // works while Time.timeScale is still 0 from the pause menu.
        yield return null;

        // StartGame before restore: it teleports the player to spawn, which the
        // restored position must win over, not lose to.
        var gameManager = FindFirstObjectByType<GameManager>();
        if (gameManager != null)
            gameManager.StartGame();

        foreach (var saveable in FindSaveables())
        {
            try
            {
                saveable.Restore(data);
            }
            catch (Exception e)
            {
                Debug.LogError(
                    $"[SaveManager] Restore failed on {saveable.GetType().Name}: {e}", this);
            }
        }

        _session = data;

        // Load can be triggered from the pause menu (timeScale 0) — the pipeline must
        // end unfrozen even when StartGame early-returned because state was Playing.
        Time.timeScale = 1f;
    }

    /// <summary>Scene systems implementing <see cref="ISaveable"/>, inactive objects included.</summary>
    private static List<ISaveable> FindSaveables()
    {
        var result = new List<ISaveable>();
        var behaviours = FindObjectsByType<MonoBehaviour>(
            FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
        foreach (var behaviour in behaviours)
        {
            if (behaviour is ISaveable saveable)
                result.Add(saveable);
        }
        return result;
    }

    private GameSaveData ReadSlot(int slot, bool logErrors)
    {
        if (!IsValidSlot(slot)) return null;

        string path = SlotPath(slot);
        if (!File.Exists(path)) return null;

        try
        {
            return JsonConvert.DeserializeObject<GameSaveData>(File.ReadAllText(path));
        }
        catch (Exception e)
        {
            if (logErrors)
                Debug.LogError($"[SaveManager] Reading slot {slot} failed: {e}", this);
            return null;
        }
    }

    private static string SlotPath(int slot)
    {
        return Path.Combine(Application.persistentDataPath, $"save_{slot}.json");
    }

    private bool IsValidSlot(int slot)
    {
        if (slot >= 0 && slot < MaxSlots) return true;
        Debug.LogError($"[SaveManager] Invalid slot {slot}; valid range is 0..{MaxSlots - 1}.", this);
        return false;
    }

    /// <summary>
    /// Writes via a tmp file so the previous save survives a crash mid-write —
    /// especially important when overwriting an occupied slot. File.Replace throws
    /// when the target does not yet exist (first save to a slot), hence the Move path.
    /// </summary>
    private static void WriteAtomic(string path, string contents)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        if (File.Exists(path))
            File.Replace(tmp, path, null);
        else
            File.Move(tmp, path);
    }
}
