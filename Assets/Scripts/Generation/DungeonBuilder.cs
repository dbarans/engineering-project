using System;
using UnityEngine;

/// <summary>
/// Orchestrates dungeon generation in the scene: layout, geometry, pathfinding grid,
/// player placement, then content.
///
/// The step order is the important part and is not negotiable. Colliders must be final
/// before <see cref="PathfindingGrid"/> samples them, and the grid must be valid before
/// anything spawns, because spawn placement checks walkability.
/// </summary>
public class DungeonBuilder : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private DungeonGenerationSettings settings;
    [SerializeField] private DungeonPainter painter;
    [SerializeField] private PathfindingGrid pathfindingGrid;

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

    private void Start()
    {
        if (buildOnStart) Build(seed);
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

        // Only now are the colliders final, so this is the earliest the grid may sample
        // them. Doing it before painting silently produces a grid that disagrees with
        // the walls on screen.
        pathfindingGrid.Configure(painter.Origin, layout.Width, layout.Height);

        CurrentLayout = layout;
        Built?.Invoke(layout);

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
        GameObject player = ResolvePlayer();
        if (player == null) return;

        player.transform.position = CellCenter(layout.SpawnCell);

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
