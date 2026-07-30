using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds a raycast fan mesh that stops at obstacles: the shape "what can be seen from this
/// point". Shared by <see cref="FieldOfView"/> (the player's cone plus near-vision circle) and
/// <see cref="StationaryLightSource"/> (a lamp's lit circle), so the casting, edge refinement
/// and mesh assembly exist once.
///
/// The caller decides the shape by supplying the sweep angles and a reach per angle; the builder
/// only turns that into geometry. UVs follow the convention of the FOV shaders: U is the
/// distance normalized by that direction's reach (0 at the origin, 1 at the outer edge), V is
/// the region flag selecting which edge softness the shader applies.
/// </summary>
public class OcclusionMeshBuilder
{
    private readonly float edgeDistanceThreshold;
    private readonly int edgeResolveIterations;

    // Reused across frames to avoid per-frame GC allocation (building runs every LateUpdate).
    private readonly List<Vector3> viewPoints = new List<Vector3>(512);
    private Vector3[] vertices = new Vector3[0];
    private Vector2[] uvs = new Vector2[0];
    private int[] triangles = new int[0];

    // Cast state for the current Build call, kept in fields so Cast/FindEdge stay parameter-light.
    private Transform origin;
    private LayerMask obstacleMask;
    private Func<float, float> reachForAngle;

    /// <param name="edgeDistanceThreshold">Distance difference between adjacent rays that triggers edge refinement.</param>
    /// <param name="edgeResolveIterations">Binary-search steps used to pin down a refined edge.</param>
    public OcclusionMeshBuilder(float edgeDistanceThreshold, int edgeResolveIterations)
    {
        this.edgeDistanceThreshold = edgeDistanceThreshold;
        this.edgeResolveIterations = edgeResolveIterations;
    }

    /// <summary>
    /// Casts one ray per angle, refines silhouette edges, and writes the resulting fan into
    /// <paramref name="target"/> in the local space of <paramref name="meshOrigin"/>.
    /// </summary>
    /// <param name="target">Mesh to overwrite.</param>
    /// <param name="meshOrigin">Transform the rays start from; also the mesh's local space.</param>
    /// <param name="obstacles">Layers that stop a ray (walls, trees).</param>
    /// <param name="angles">World-space angles in degrees to cast, in sweep order.</param>
    /// <param name="reach">Maximum ray length for a given angle.</param>
    /// <param name="isSoftEdgeRegion">
    /// Whether a given angle belongs to the region using the shader's near-circle edge softness
    /// (written to UV.y). Pass null to flag every vertex as the main region.
    /// </param>
    public void Build(
        Mesh target,
        Transform meshOrigin,
        LayerMask obstacles,
        List<float> angles,
        Func<float, float> reach,
        Func<float, bool> isSoftEdgeRegion)
    {
        origin = meshOrigin;
        obstacleMask = obstacles;
        reachForAngle = reach;

        viewPoints.Clear();
        ViewCastInfo prevCast = default;

        for (int i = 0; i < angles.Count; i++)
        {
            ViewCastInfo cast = Cast(angles[i]);

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

        WriteMesh(target, isSoftEdgeRegion);
    }

    /// <summary>
    /// Turns the collected hit points into a triangle fan around the origin vertex.
    /// </summary>
    private void WriteMesh(Mesh target, Func<float, bool> isSoftEdgeRegion)
    {
        int vertCount = viewPoints.Count + 1;
        if (vertices.Length != vertCount)
        {
            vertices = new Vector3[vertCount];
            uvs = new Vector2[vertCount];
            triangles = new int[Mathf.Max(0, (vertCount - 2) * 3)];
        }

        Vector2 originPosition = origin.position;
        vertices[0] = Vector3.zero;
        uvs[0] = Vector2.zero;

        for (int i = 0; i < viewPoints.Count; i++)
        {
            Vector3 localPoint = origin.InverseTransformPoint(viewPoints[i]);
            vertices[i + 1] = localPoint;

            Vector2 direction = (Vector2)viewPoints[i] - originPosition;
            float pointAngle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            bool softEdge = isSoftEdgeRegion != null && isSoftEdgeRegion(pointAngle);
            float pointReach = Mathf.Max(0.0001f, reachForAngle(pointAngle));
            uvs[i + 1] = new Vector2(Mathf.Clamp01(localPoint.magnitude / pointReach), softEdge ? 1f : 0f);

            if (i < viewPoints.Count - 1)
            {
                int t = i * 3;
                triangles[t] = 0;
                triangles[t + 1] = i + 1;
                triangles[t + 2] = i + 2;
            }
        }

        target.Clear();
        target.vertices = vertices;
        target.uv = uvs;
        target.triangles = triangles;
        target.RecalculateNormals();
    }

    private ViewCastInfo Cast(float angleDeg)
    {
        Vector3 direction = AngleToDirection(angleDeg);
        float maxDistance = reachForAngle(angleDeg);
        RaycastHit2D hit = Physics2D.Raycast(origin.position, direction, maxDistance, obstacleMask);

        if (hit.collider != null)
            return new ViewCastInfo(true, hit.point, hit.distance, angleDeg);

        return new ViewCastInfo(false, origin.position + direction * maxDistance, maxDistance, angleDeg);
    }

    /// <summary>
    /// Binary-searches the angle range between two casts that landed on different surfaces,
    /// so the silhouette of an obstacle stays sharp instead of being cut by ray resolution.
    /// </summary>
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

    private struct ViewCastInfo
    {
        public readonly bool hit;
        public readonly Vector3 point;
        public readonly float distance;
        public readonly float angle;

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
        public readonly Vector3 pointA;
        public readonly Vector3 pointB;

        public EdgeInfo(Vector3 pointA, Vector3 pointB)
        {
            this.pointA = pointA;
            this.pointB = pointB;
        }
    }
}
