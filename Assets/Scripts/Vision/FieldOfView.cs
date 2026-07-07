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

    [Header("Ray Settings")]
    [Tooltip("Number of rays per degree. Higher = smoother but slower.")]
    [SerializeField] private int raysPerDegree = 1;
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
        int stepCount = Mathf.Max(1, Mathf.RoundToInt(viewAngle * raysPerDegree));
        float stepSize = viewAngle / stepCount;

        List<Vector3> viewPoints = new List<Vector3>(stepCount + 16);
        ViewCastInfo prevCast = default;

        float facingAngle = directionSource != null ? directionSource.eulerAngles.z : transform.eulerAngles.z;

        for (int i = 0; i <= stepCount; i++)
        {
            float angle = facingAngle - viewAngle / 2f + stepSize * i;
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
        Vector3[] vertices = new Vector3[vertCount];
        int[] triangles = new int[(vertCount - 2) * 3];

        vertices[0] = Vector3.zero;
        for (int i = 0; i < viewPoints.Count; i++)
        {
            vertices[i + 1] = transform.InverseTransformPoint(viewPoints[i]);
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
        viewMesh.triangles = triangles;
        viewMesh.RecalculateNormals();
    }

    private ViewCastInfo Cast(float angleDeg)
    {
        Vector3 dir = AngleToDirection(angleDeg);
        RaycastHit2D hit = Physics2D.Raycast(transform.position, dir, viewRadius, obstacleMask);

        if (hit.collider != null)
            return new ViewCastInfo(true, hit.point, hit.distance, angleDeg);

        return new ViewCastInfo(false, transform.position + dir * viewRadius, viewRadius, angleDeg);
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
    /// Returns true if the given world-space point is within the FOV cone and no obstacle is blocking.
    /// </summary>
    public bool IsVisible(Vector2 worldPoint)
    {
        Vector2 origin = transform.position;

        if (Vector2.Distance(origin, worldPoint) > viewRadius)
            return false;

        if (viewAngle < 360f)
        {
            float facingAngle = directionSource != null ? directionSource.eulerAngles.z : transform.eulerAngles.z;
            Vector2 facingDir = new Vector2(Mathf.Cos(facingAngle * Mathf.Deg2Rad), Mathf.Sin(facingAngle * Mathf.Deg2Rad));
            Vector2 toPoint = (worldPoint - origin).normalized;
            float angleToPoint = Vector2.Angle(facingDir, toPoint);

            if (angleToPoint > viewAngle / 2f)
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
