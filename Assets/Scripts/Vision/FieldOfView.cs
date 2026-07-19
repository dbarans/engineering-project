using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a field-of-view mesh around the player using raycasting.
/// Attach directly to the player GameObject.
/// Requires MeshFilter and MeshRenderer components.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class FieldOfView : MonoBehaviour
{
    [Header("View Settings")]
    [SerializeField] private float viewRadius = 8f;
    [Range(1, 360)]
    [SerializeField] private float viewAngle = 360f;
    [Tooltip("Small 360° visibility radius around the player, in addition to the cone (Darkwood-style). Set to 0 to disable.")]
    [SerializeField] private float nearVisionRadius = 2f;

    [Header("Lantern")]
    [Tooltip("When held, the 360° circle around the player grows to Lantern Radius. Toggle here for testing; later driven by an item.")]
    [SerializeField] private bool lanternEnabled = false;
    [Tooltip("Radius of the 360° circle while the lantern is held. Keep below View Radius.")]
    [SerializeField] private float lanternRadius = 5f;

    [Header("Ray Settings")]
    [Tooltip("Number of rays per degree inside the view cone. Higher = smoother but slower.")]
    [SerializeField] private int raysPerDegree = 1;
    [Tooltip("Rays per degree for the near-vision/lantern circle OUTSIDE the cone (the part behind/beside the player). This area doesn't need cone-level smoothness, so it defaults much coarser to cut raycasts without touching cone quality.")]
    [SerializeField] private float nearCircleRaysPerDegree = 0.25f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Edge Detection")]
    [Tooltip("Distance difference between adjacent rays that triggers edge refinement.")]
    [SerializeField] private float edgeDistanceThreshold = 0.5f;
    [SerializeField] private int edgeResolveIterations = 4;

    [Header("Direction")]
    [Tooltip("Assign the Torso transform so the FOV cone follows the player's aim direction. Leave empty for 360°.")]
    [SerializeField] private Transform directionSource;

    [Header("Rendering")]
    [Tooltip("Sorting layer name for the FOV mesh (must match a layer defined in Tags & Layers).")]
    [SerializeField] private string sortingLayerName = "Default";
    [Tooltip("Order in layer. Set lower than DarknessOverlay so stencil is written first.")]
    [SerializeField] private int sortingOrder = 5;

    private Mesh viewMesh;
    private MeshFilter meshFilter;
    private float facingAngle;

    // Reused across frames to avoid per-frame GC allocation (BuildMesh runs every LateUpdate).
    private readonly List<float> angleBuffer = new List<float>(512);
    private readonly List<Vector3> viewPoints = new List<Vector3>(512);
    private Vector3[] vertices = new Vector3[0];
    private Vector2[] uvs = new Vector2[0];
    private int[] triangles = new int[0];

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        viewMesh = new Mesh { name = "FOV Mesh" };
        meshFilter.mesh = viewMesh;

        var mr = GetComponent<MeshRenderer>();
        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = sortingOrder;
    }

    private void LateUpdate()
    {
        BuildMesh();
    }

    private void BuildMesh()
    {
        facingAngle = directionSource != null ? directionSource.eulerAngles.z : transform.eulerAngles.z;

        // When the near-vision circle is active on a cone, sweep the full 360°:
        // inside the cone the reach is viewRadius, elsewhere it drops to the near radius.
        bool useNearCircle = EffectiveNearRadius() > 0f && viewAngle < 360f;
        float sweepAngle = useNearCircle ? 360f : viewAngle;
        float startAngle = useNearCircle ? facingAngle - 180f : facingAngle - viewAngle / 2f;

        BuildAngleSweep(useNearCircle, sweepAngle, startAngle);

        viewPoints.Clear();
        ViewCastInfo prevCast = default;

        for (int i = 0; i < angleBuffer.Count; i++)
        {
            float angle = angleBuffer[i];
            ViewCastInfo cast = Cast(angle);

            if (i > 0)
            {
                bool distGap = Mathf.Abs(prevCast.distance - cast.distance) > edgeDistanceThreshold;
                if (prevCast.hit != cast.hit || (prevCast.hit && cast.hit && distGap))
                {
                    EdgeInfo edge = FindEdge(prevCast, cast);
                    if (edge.pointA != Vector3.zero) viewPoints.Add(edge.pointA);
                    if (edge.pointB != Vector3.zero) viewPoints.Add(edge.pointB);
                }
            }

            viewPoints.Add(cast.point);
            prevCast = cast;
        }

        int vertCount = viewPoints.Count + 1;
        if (vertices.Length != vertCount)
        {
            vertices = new Vector3[vertCount];
            uvs = new Vector2[vertCount];
            triangles = new int[(vertCount - 2) * 3];
        }

        Vector2 origin = transform.position;
        vertices[0] = Vector3.zero;
        uvs[0] = Vector2.zero;
        for (int i = 0; i < viewPoints.Count; i++)
        {
            Vector3 localPoint = transform.InverseTransformPoint(viewPoints[i]);
            vertices[i + 1] = localPoint;

            // UV.x: distance normalized by this direction's reach (so the near circle fades
            // over its own radius and the cone fades over the full radius).
            // UV.y: region flag — 1 for the near-vision circle, 0 for the main cone. The shader
            // keeps the near circle mostly clear so objects behind the player stay visible.
            Vector2 dir = (Vector2)viewPoints[i] - origin;
            float pointAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            bool isNear = useNearCircle && Mathf.Abs(Mathf.DeltaAngle(facingAngle, pointAngle)) > viewAngle * 0.5f;
            float reach = isNear ? EffectiveNearRadius() : viewRadius;
            uvs[i + 1] = new Vector2(Mathf.Clamp01(localPoint.magnitude / reach), isNear ? 1f : 0f);

            if (i < viewPoints.Count - 1)
            {
                int t = i * 3;
                triangles[t] = 0;
                triangles[t + 1] = i + 1;
                triangles[t + 2] = i + 2;
            }
        }

        viewMesh.Clear();
        viewMesh.vertices = vertices;
        viewMesh.uv = uvs;
        viewMesh.triangles = triangles;
        viewMesh.RecalculateNormals();
    }

    /// <summary>
    /// Fills angleBuffer with the angles to raycast this frame. Inside the cone, uses full
    /// raysPerDegree resolution (cone quality is untouched); outside the cone (the near-vision/
    /// lantern circle behind/beside the player, only relevant when useNearCircle is true), uses
    /// the much coarser nearCircleRaysPerDegree — that region doesn't need cone-level smoothness,
    /// so this cuts total raycasts without reducing what the player actually sees ahead of them.
    /// </summary>
    private void BuildAngleSweep(bool useNearCircle, float sweepAngle, float startAngle)
    {
        angleBuffer.Clear();

        if (!useNearCircle)
        {
            int stepCount = Mathf.Max(1, Mathf.RoundToInt(sweepAngle * raysPerDegree));
            float stepSize = sweepAngle / stepCount;
            for (int i = 0; i <= stepCount; i++)
                angleBuffer.Add(startAngle + stepSize * i);
            return;
        }

        float coneStep = 1f / Mathf.Max(0.01f, raysPerDegree);
        float outerStep = Mathf.Max(coneStep, 1f / Mathf.Max(0.01f, nearCircleRaysPerDegree));
        float halfCone = viewAngle * 0.5f;
        float end = startAngle + sweepAngle;

        float angle = startAngle;
        while (angle < end)
        {
            angleBuffer.Add(angle);
            float distFromFacing = Mathf.Abs(Mathf.DeltaAngle(facingAngle, angle));
            angle += distFromFacing <= halfCone ? coneStep : outerStep;
        }
        angleBuffer.Add(end);
    }

    /// <summary>
    /// Current radius of the 360° circle around the player: larger while a lantern is held.
    /// </summary>
    private float EffectiveNearRadius()
    {
        return lanternEnabled ? Mathf.Max(nearVisionRadius, lanternRadius) : nearVisionRadius;
    }

    /// <summary>
    /// Enables or disables the held lantern (grows the 360° circle). Intended to be
    /// driven later by a light-source item held by the player.
    /// </summary>
    public void SetLanternEnabled(bool enabled)
    {
        lanternEnabled = enabled;
    }

    /// <summary>
    /// Reach for a ray at the given world angle: full viewRadius inside the cone,
    /// the near/lantern radius outside it (Darkwood-style circle around the player).
    /// </summary>
    private float MaxDistanceForAngle(float angleDeg)
    {
        if (EffectiveNearRadius() <= 0f || viewAngle >= 360f)
            return viewRadius;

        float offset = Mathf.Abs(Mathf.DeltaAngle(facingAngle, angleDeg));
        return offset <= viewAngle / 2f ? viewRadius : EffectiveNearRadius();
    }

    private ViewCastInfo Cast(float angleDeg)
    {
        Vector3 dir = AngleToDirection(angleDeg);
        float maxDist = MaxDistanceForAngle(angleDeg);
        RaycastHit2D hit = Physics2D.Raycast(transform.position, dir, maxDist, obstacleMask);

        if (hit.collider != null)
            return new ViewCastInfo(true, hit.point, hit.distance, angleDeg);

        return new ViewCastInfo(false, transform.position + dir * maxDist, maxDist, angleDeg);
    }

    private EdgeInfo FindEdge(ViewCastInfo a, ViewCastInfo b)
    {
        float minAngle = a.angle;
        float maxAngle = b.angle;
        Vector3 minPoint = Vector3.zero;
        Vector3 maxPoint = Vector3.zero;

        for (int i = 0; i < edgeResolveIterations; i++)
        {
            float midAngle = (minAngle + maxAngle) / 2f;
            ViewCastInfo mid = Cast(midAngle);

            bool distGap = Mathf.Abs(a.distance - mid.distance) > edgeDistanceThreshold;
            if (mid.hit == a.hit && !distGap)
            {
                minAngle = midAngle;
                minPoint = mid.point;
            }
            else
            {
                maxAngle = midAngle;
                maxPoint = mid.point;
            }
        }

        return new EdgeInfo(minPoint, maxPoint);
    }

    private static Vector3 AngleToDirection(float angleDeg)
    {
        float rad = angleDeg * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f);
    }

    /// <summary>
    /// Returns true if the given world-space point is visible: inside the cone within
    /// viewRadius, or inside the near-vision circle around the player, and not obstructed.
    /// </summary>
    public bool IsVisible(Vector2 worldPoint)
    {
        Vector2 origin = transform.position;
        float distance = Vector2.Distance(origin, worldPoint);

        if (distance > viewRadius)
            return false;

        float facing = directionSource != null ? directionSource.eulerAngles.z : transform.eulerAngles.z;

        if (viewAngle < 360f)
        {
            Vector2 facingDir = new Vector2(Mathf.Cos(facing * Mathf.Deg2Rad), Mathf.Sin(facing * Mathf.Deg2Rad));
            Vector2 toPoint = (worldPoint - origin).normalized;
            float angleToPoint = Vector2.Angle(facingDir, toPoint);

            // Outside the cone, only the near-vision / lantern circle counts.
            bool insideCone = angleToPoint <= viewAngle / 2f;
            bool insideNearCircle = EffectiveNearRadius() > 0f && distance <= EffectiveNearRadius();
            if (!insideCone && !insideNearCircle)
                return false;
        }

        RaycastHit2D hit = Physics2D.Linecast(origin, worldPoint, obstacleMask);
        return hit.collider == null;
    }

    /// <summary>View radius in world units.</summary>
    public float ViewRadius => viewRadius;

    private struct ViewCastInfo
    {
        public bool hit;
        public Vector3 point;
        public float distance;
        public float angle;

        public ViewCastInfo(bool hit, Vector3 point, float distance, float angle)
        {
            this.hit = hit;
            this.point = point;
            this.distance = distance;
            this.angle = angle;
        }
    }

    private struct EdgeInfo
    {
        public Vector3 pointA;
        public Vector3 pointB;

        public EdgeInfo(Vector3 a, Vector3 b) { pointA = a; pointB = b; }
    }
}
