using UnityEngine;

/// <summary>What a marker inside a room template turns into at generation time.</summary>
public enum SpawnMarkerKind
{
    /// <summary>Instantiates a <see cref="PrefabRegistry"/> entry.</summary>
    Prefab = 0,

    /// <summary>Drops an <see cref="ItemData"/> on the ground.</summary>
    Item = 1,

    /// <summary>Spawns nothing; only reserves the cell so random content stays off it.</summary>
    KeepClear = 2
}

/// <summary>
/// One authored spawn point inside a <see cref="DungeonRoomTemplate"/>.
///
/// Placed as a child object in the template prefab, so a room interior is laid out
/// visually in the scene view instead of as a list of coordinates. Its local position
/// is read as a cell offset from the template's lower-left corner, which is why
/// templates must be authored on whole-unit positions.
/// </summary>
public class DungeonSpawnMarker : MonoBehaviour
{
    [SerializeField] private SpawnMarkerKind kind = SpawnMarkerKind.Prefab;

    [Tooltip("PrefabRegistry id, or ItemData id when the kind is Item. Ignored for KeepClear.")]
    [SerializeField] private string id;

    [Tooltip("Item count. Only used when the kind is Item.")]
    [Min(1)] [SerializeField] private int minCount = 1;
    [Min(1)] [SerializeField] private int maxCount = 1;

    [Tooltip("Probability this marker produces anything. Below 1 the room varies between runs.")]
    [Range(0f, 1f)] [SerializeField] private float chance = 1f;

    public SpawnMarkerKind Kind => kind;
    public string Id => id;
    public int MinCount => minCount;
    public int MaxCount => Mathf.Max(minCount, maxCount);
    public float Chance => chance;

    /// <summary>Cell offset from the template's lower-left corner.</summary>
    public Vector2Int CellOffset => new Vector2Int(
        Mathf.RoundToInt(transform.localPosition.x),
        Mathf.RoundToInt(transform.localPosition.y));

    private void OnDrawGizmos()
    {
        Gizmos.color = kind switch
        {
            SpawnMarkerKind.Prefab => new Color(1f, 0.6f, 0.2f, 0.9f),
            SpawnMarkerKind.Item => new Color(0.4f, 0.8f, 1f, 0.9f),
            _ => new Color(1f, 1f, 1f, 0.35f)
        };
        Gizmos.DrawWireCube(transform.position, Vector3.one * 0.8f);
    }
}
