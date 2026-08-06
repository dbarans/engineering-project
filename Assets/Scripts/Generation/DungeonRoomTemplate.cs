using UnityEngine;

/// <summary>
/// A hand-authored room interior that the generator can drop into a generated room.
///
/// This is the standard answer to the usual complaint about procedural levels — that
/// they feel uniform. The layout stays generated, so no two runs share a map, but what
/// fills a room can be designed rather than scattered. Darkwood mixes authored and
/// generated content the same way.
///
/// Authoring: a prefab whose root carries this component and whose children carry
/// <see cref="DungeonSpawnMarker"/>. Markers are read as cell offsets from the
/// template's lower-left corner, so place them on whole-unit positions.
/// </summary>
public class DungeonRoomTemplate : MonoBehaviour
{
    [Tooltip("Footprint in cells. The template is only used in rooms at least this large.")]
    [SerializeField] private Vector2Int size = new Vector2Int(5, 5);

    [Tooltip("Room roles this template may fill. Empty means any role except Start.")]
    [SerializeField] private RoomKind[] allowedKinds = { RoomKind.Normal };

    [Tooltip("Relative likelihood against the other templates that fit.")]
    [Min(0f)] [SerializeField] private float weight = 1f;

    public Vector2Int Size => size;
    public float Weight => weight;

    /// <summary>Markers to convert into spawns, in a stable child order.</summary>
    public DungeonSpawnMarker[] Markers => GetComponentsInChildren<DungeonSpawnMarker>(true);

    /// <summary>True when this template fits the room and suits its role.</summary>
    public bool Fits(Room room)
    {
        if (room == null) return false;
        if (room.Kind == RoomKind.Start) return false; // the start room stays empty
        if (room.Bounds.width < size.x || room.Bounds.height < size.y) return false;
        if (!FootprintIsInside(room)) return false;

        if (allowedKinds == null || allowedKinds.Length == 0) return true;

        foreach (RoomKind kind in allowedKinds)
        {
            if (kind == room.Kind) return true;
        }
        return false;
    }

    /// <summary>
    /// True when every cell the template would cover belongs to the room.
    ///
    /// Fitting the bounding box is no longer enough now that rooms can be L-shaped or
    /// ring-shaped: a template centred in such a room's box lands partly in solid rock,
    /// and half its markers would be silently dropped. Better to decline the room and
    /// let the random tables fill it.
    /// </summary>
    private bool FootprintIsInside(Room room)
    {
        Vector2Int anchor = AnchorIn(room);

        for (int y = 0; y < size.y; y++)
        {
            for (int x = 0; x < size.x; x++)
            {
                if (!room.Contains(anchor + new Vector2Int(x, y))) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Cell the template's lower-left corner lands on: centred in the room, so a small
    /// template in a large room does not hug a wall.
    /// </summary>
    public Vector2Int AnchorIn(Room room)
    {
        return new Vector2Int(
            room.Bounds.xMin + (room.Bounds.width - size.x) / 2,
            room.Bounds.yMin + (room.Bounds.height - size.y) / 2);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.3f, 1f, 0.5f, 0.8f);
        var center = transform.position + new Vector3(size.x, size.y, 0f) * 0.5f;
        Gizmos.DrawWireCube(center, new Vector3(size.x, size.y, 0.01f));
    }
}
