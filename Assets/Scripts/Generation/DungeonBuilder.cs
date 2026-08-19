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

    [Header("Editor gizmos")]
    [Tooltip("Outline and label the rooms with a role in the scene view: the hub, the " +
             "treasure, the exit, and the room holding the exit key. Editor only — the " +
             "drawing is compiled out of a build entirely. Ordinary rooms are left " +
             "unmarked, or the map is a wall of boxes and none of them mean anything.")]
    [SerializeField] private bool drawRoomRoles = true;

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

#if UNITY_EDITOR
    // ---------------------------------------------------------------- editor gizmos

    /// <summary>
    /// Layout drawn by the gizmos when <see cref="CurrentLayout"/> is null, and the seed it
    /// was generated from. The Dungeon scene is authored by baking, so opening it after a
    /// domain reload leaves the builder holding no layout at all while the scene is full of
    /// baked content — which is exactly when someone wants to ask "which room was the key
    /// in". Regenerating from the seed answers that: generation is a pure function of the
    /// seed and the settings, so the preview is the same layout the content was baked from.
    ///
    /// Cached because <see cref="OnDrawGizmos"/> runs on every repaint of every scene view.
    /// </summary>
    [NonSerialized] private DungeonLayout _gizmoLayout;
    [NonSerialized] private string _gizmoSeed;
    [NonSerialized] private GUIStyle _gizmoLabelStyle;

    private void OnDrawGizmos()
    {
        if (!drawRoomRoles) return;

        DungeonLayout layout = CurrentLayout ?? PreviewLayout();
        if (layout == null || painter == null) return;

        foreach (Room room in layout.Rooms)
        {
            bool keyRoom = room.HoldsExitKey;
            if (room.Kind == RoomKind.Normal && !keyRoom) continue;

            Color color = ColorFor(room.Kind, keyRoom);
            Gizmos.color = color;

            Vector2 min = painter.CellCenter(new Vector2Int(room.Bounds.xMin, room.Bounds.yMin));
            Vector2 max = painter.CellCenter(new Vector2Int(room.Bounds.xMax - 1, room.Bounds.yMax - 1));
            var center = new Vector3((min.x + max.x) * 0.5f, (min.y + max.y) * 0.5f, 0f);
            var size = new Vector3(max.x - min.x + painter.CellSize, max.y - min.y + painter.CellSize, 0f);

            Gizmos.DrawWireCube(center, size);

            // Inset second outline: one wire box against the wall tiles is easy to lose,
            // and the doubled edge reads as deliberate marking rather than as geometry.
            Gizmos.DrawWireCube(center, size - new Vector3(painter.CellSize, painter.CellSize, 0f));

            // One style reused across rooms and repaints: a new GUIStyle per label is an
            // allocation on every repaint of every scene view, which is the standard way
            // editor gizmos end up generating garbage faster than the game does.
            _gizmoLabelStyle ??= new GUIStyle(UnityEditor.EditorStyles.boldLabel);
            _gizmoLabelStyle.normal.textColor = color;
            UnityEditor.Handles.Label(center, LabelFor(room, keyRoom), _gizmoLabelStyle);
        }

        // The door in the wall, marked separately from its room: the room is where the way
        // out is, and this is the cell it is actually cut into. Standing in the scene view
        // looking at the exit room, that is the thing worth being able to point at.
        if (layout.HasExitDoor)
        {
            Gizmos.color = ColorFor(RoomKind.Exit, false);
            Vector2 doorCell = painter.CellCenter(layout.ExitDoorCell);
            Gizmos.DrawWireSphere(doorCell, painter.CellSize * 0.45f);

            Gizmos.color = new Color(1f, 0.85f, 0.1f);
            Vector2 threshold = painter.CellCenter(layout.ExitThresholdCell);
            Gizmos.DrawLine(threshold, doorCell);
        }
    }

    /// <summary>
    /// Colour per role. The key room wins over its own kind — it is the one thing these
    /// gizmos exist to point at, and it can land on a Treasure room, whose colour would
    /// otherwise hide it.
    /// </summary>
    private static Color ColorFor(RoomKind kind, bool keyRoom)
    {
        if (keyRoom) return new Color(1f, 0.85f, 0.1f);        // key: amber

        switch (kind)
        {
            case RoomKind.Exit: return new Color(0.3f, 1f, 0.4f);      // exit: green
            case RoomKind.Hub: return new Color(0.4f, 0.7f, 1f);       // hub: blue
            case RoomKind.Treasure: return new Color(1f, 0.4f, 0.9f);  // treasure: magenta
            default: return Color.white;
        }
    }

    private static string LabelFor(Room room, bool keyRoom)
    {
        if (!keyRoom) return room.Kind.ToString().ToUpperInvariant();

        // Both facts, because they are two separate answers: which room holds the key, and
        // what that room is otherwise for.
        return room.Kind == RoomKind.Normal ? "KEY" : $"KEY ({room.Kind})";
    }

    /// <summary>
    /// Regenerates the layout from the inspector seed for drawing purposes only. Never
    /// touches the scene: no painting, no content, no pathfinding grid — this is a
    /// read-only answer to "where would the roles be for this seed".
    /// </summary>
    private DungeonLayout PreviewLayout()
    {
        if (settings == null || string.IsNullOrEmpty(seed)) return null;
        if (_gizmoLayout != null && _gizmoSeed == seed) return _gizmoLayout;

        _gizmoLayout = new RoomCorridorGenerator().Generate(seed, settings.ToParams());
        _gizmoSeed = seed;
        return _gizmoLayout;
    }
#endif
}
