using System;
using UnityEngine;

/// <summary>
/// Orchestrates dungeon generation in the scene: layout, geometry, content, *then* the
/// pathfinding grid, player placement last.
///
/// The step order is the important part and is not negotiable, but content has to come
/// before the grid, not after: <see cref="DungeonPopulator"/> (subscribed on
/// <see cref="Built"/>, invoked synchronously below) spawns tables, statues and every
/// other blocking prop, and each one adds its own collider to the scene. Configuring
/// <see cref="PathfindingGrid"/> — which samples physics — before that ran meant every
/// prop's collider simply did not exist yet, so nothing populated ever blocked a path,
/// silently, no matter what layer or trigger flag it carried. <see cref="DungeonPopulator"/>
/// itself never needs the grid: its own placement logic reads <c>DungeonLayout.IsWalkable</c>,
/// the pre-physics abstract layout, not the physics-sampled grid.
/// </summary>
public class DungeonBuilder : MonoBehaviour, ISaveable
{
    [Header("Wiring")]
    [SerializeField] private DungeonGenerationSettings settings;
    [SerializeField] private DungeonPainter painter;
    [SerializeField] private PathfindingGrid pathfindingGrid;

    [Tooltip("Optional. A scene Transform kept at the hub's world position on every build " +
             "— normally GameManager's own \"Player Spawn Point\" marker.\n\n" +
             "This exists because of how this scene is actually used: the Dungeon scene is " +
             "authored by baking (generate once in the editor, then save the scene), so at " +
             "runtime buildOnStart is off and Build() never runs — the only thing that " +
             "places the player at game start is GameManager.TeleportPlayerToSpawn(), which " +
             "reads this Transform's static position. Without this field, that marker keeps " +
             "whatever position it had the last time someone moved it by hand, which drifts " +
             "away from the hub the moment the dungeon is regenerated with a different seed " +
             "or different generation settings — the player spawns wherever the marker was " +
             "left, not in the hub.")]
    [SerializeField] private Transform playerSpawnMarker;

    [Header("Startup")]
    [Tooltip("Generate on Start. Leave off when the save system drives generation instead.")]
    [SerializeField] private bool buildOnStart;

    [Tooltip("Seed used by the context-menu buttons and by Build On Start when empty.")]
    [SerializeField] private string seed = "demo";

    /// <summary>The layout currently in the scene, or null before the first build.</summary>
    public DungeonLayout CurrentLayout { get; private set; }

    /// <summary>Seed the current layout was built from.</summary>
    public string CurrentSeed => CurrentLayout != null ? CurrentLayout.Seed : null;

    /// <summary>
    /// Raised after the geometry and the pathfinding grid are ready, before the player
    /// is placed. Content spawning subscribes here rather than being called directly,
    /// so generation does not need to know what populates a dungeon.
    /// </summary>
    public event Action<DungeonLayout> Built;

    /// <summary>Converts a layout cell to a world position; the single source of truth.</summary>
    public Vector2 CellCenter(Vector2Int cell) => painter.CellCenter(cell);

    /// <summary>World-space cell size, for callers that need to reason about distances
    /// rather than just cell-to-world positions (door orientation is the one today).</summary>
    public float CellSize => painter.CellSize;

    private void OnEnable()
    {
        SaveManager.BuildWorld += BuildFromSave;
    }

    private void OnDisable()
    {
        SaveManager.BuildWorld -= BuildFromSave;
    }

    private void Start()
    {
        // A load already rebuilt the dungeon from its saved seed: scene-loaded callbacks
        // run before Start, so building again here would replace the restored world with
        // a different one right before the saved state lands on it.
        if (CurrentLayout != null) return;

        if (buildOnStart) Build(seed);
    }

    /// <summary>
    /// Rebuilds the dungeon a save was made in, from the seed stored with it. Hooked to
    /// <see cref="SaveManager.BuildWorld"/>, which fires after the scene loads and before
    /// any entity state is restored — the world has to exist before state can be laid
    /// over it.
    /// </summary>
    private void BuildFromSave(GameSaveData data)
    {
        if (data == null) return;

        string sceneName = gameObject.scene.name;
        if (data.scenes == null || !data.scenes.TryGetValue(sceneName, out var scene)) return;

        if (string.IsNullOrEmpty(scene.generationSeed))
            return; // save predates generation, or this scene is hand-built

        Build(scene.generationSeed);
    }

    /// <summary>
    /// Writes the current seed into the scene's save bucket. The dungeon itself is never
    /// saved — a seed plus the entity diffs is enough to rebuild it exactly, and it does
    /// not grow with the size of the map.
    /// </summary>
    public void Capture(GameSaveData data)
    {
        if (data == null || CurrentLayout == null) return;

        data.GetOrCreateScene(gameObject.scene.name).generationSeed = CurrentLayout.Seed;
    }

    /// <summary>
    /// Nothing to do: generation already happened in <see cref="BuildFromSave"/>, which
    /// runs earlier in the load sequence than this.
    /// </summary>
    public void Restore(GameSaveData data)
    {
    }

    /// <summary>
    /// Builds a dungeon from the given seed. A null or empty seed generates a random
    /// one, which is then readable from <see cref="CurrentSeed"/> so it can be saved.
    /// Returns the layout, or null when the scene is not wired up.
    /// </summary>
    public DungeonLayout Build(string dungeonSeed)
    {
        if (!Validate()) return null;

        if (string.IsNullOrEmpty(dungeonSeed))
            dungeonSeed = NewRandomSeed();

        var generator = new RoomCorridorGenerator();
        DungeonLayout layout = generator.Generate(dungeonSeed, settings.ToParams());

        if (!generator.LastGenerationSucceeded)
        {
            // The generator reports rather than logs, so that it stays testable; this
            // is where the report becomes visible.
            Debug.LogWarning(
                $"[DungeonBuilder] Seed '{dungeonSeed}' failed validation after " +
                $"{generator.LastAttemptCount} attempts ({generator.LastFailureReason}). " +
                "Using the best attempt — loosen the room settings if this repeats.", this);
        }

        if (!painter.Paint(layout)) return null;

        CurrentLayout = layout;

        // Fires DungeonPopulator.Populate synchronously — a C# event invocation blocks
        // until every subscriber returns — so every table, statue, barrel and chest it
        // spawns already has its collider in the scene by the time control returns here.
        Built?.Invoke(layout);

        // Only now are ALL colliders final: the painted walls, *and* everything Built's
        // subscribers just spawned. Configuring before painting would have disagreed with
        // the walls on screen; configuring before this line silently produced a grid that
        // let every populated prop go right on being unwalkable.
        //
        // painter.CellSize, not a hardcoded 1 or whatever the grid last had serialized: it
        // is the tilemap's actual world-space cell size, and the two must match or every
        // sample lands off-centre from the tile it is meant to test.
        pathfindingGrid.Configure(painter.Origin, painter.CellSize, layout.Width, layout.Height);

        MovePlayerToSpawn(layout);
        return layout;
    }

    /// <summary>A short, readable seed — short enough that a tester can retype it.</summary>
    public static string NewRandomSeed()
    {
        return Guid.NewGuid().ToString("N").Substring(0, 8);
    }

    private void MovePlayerToSpawn(DungeonLayout layout)
    {
        Vector2 spawnPosition = CellCenter(layout.SpawnCell);

        // Keeps GameManager's own spawn teleport correct on a baked scene: see the field's
        // tooltip for why this is not optional polish. Set even when no live player exists
        // (edit mode baking), since this is what a later Play session will actually read.
        if (playerSpawnMarker != null) playerSpawnMarker.position = spawnPosition;

        GameObject player = ResolvePlayer();
        if (player == null) return;

        player.transform.position = spawnPosition;

        var body = player.GetComponent<Rigidbody2D>();
        if (body != null) body.linearVelocity = Vector2.zero;
    }

    /// <summary>
    /// The player through <see cref="GameManager"/> when it has one, otherwise by tag —
    /// generation can run in edit mode, where GameManager has not initialised.
    /// </summary>
    private static GameObject ResolvePlayer()
    {
        var gameManager = FindFirstObjectByType<GameManager>();
        GameObject player = gameManager != null ? gameManager.GetPlayer() : null;
        if (player != null) return player;

        var movement = FindFirstObjectByType<PlayerMovement>();
        return movement != null ? movement.gameObject : null;
    }

    private bool Validate()
    {
        if (settings == null)
        {
            Debug.LogError("[DungeonBuilder] No generation settings assigned.", this);
            return false;
        }
        if (painter == null)
        {
            Debug.LogError("[DungeonBuilder] No painter assigned.", this);
            return false;
        }
        if (pathfindingGrid == null)
        {
            Debug.LogError(
                "[DungeonBuilder] No PathfindingGrid assigned — enemies would not be able " +
                "to path through the generated dungeon.", this);
            return false;
        }
        return true;
    }

    [ContextMenu("Generate (current seed)")]
    private void GenerateCurrentSeed()
    {
        Build(seed);
    }

    [ContextMenu("Generate (random seed)")]
    private void GenerateRandomSeed()
    {
        seed = NewRandomSeed();
        Build(seed);
        Debug.Log($"[DungeonBuilder] Generated seed '{seed}'.", this);
    }
}
