using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A stationary light (oil lamp, lantern) that carves a lit circle out of the darkness, the way
/// the player's own near-vision circle does in <see cref="FieldOfView"/>. Anything standing in
/// that circle — enemies included — becomes visible even when the player is not looking at it,
/// because the lit area feeds the same vision mask the FOV mesh feeds.
///
/// Walls and trees on the obstacle mask cut the light, so a lamp does not shine through a wall.
/// Small props never block it, per the project's vision-blocking convention.
///
/// The colour it burns with is its own (<see cref="lightColor"/>), and so is how far its rim
/// fades out (<see cref="edgeSoftness"/>): both are pushed per lamp through a
/// <see cref="MaterialPropertyBlock"/>, so every light in the scene can share one mask material
/// and still look like a different kind of light.
///
/// Brightness over time is not this component's job: add an <see cref="ILightIntensity"/>
/// component next to it — <see cref="FlameFlicker"/>, since every light in this game is something
/// burning — and the light is scaled by it. That never changes the shape of the lit area
/// (the raycast mesh), only how bright the shader draws it.
///
/// Attach to the lamp GameObject and assign the same VisionMaskWriter material the player's
/// FieldOfView uses; the mesh itself is drawn by a generated child on the vision mask layer.
/// </summary>
public class StationaryLightSource : MonoBehaviour
{
    [Header("Light")]
    [Tooltip("Radius of the lit circle, in world units.")]
    [SerializeField] private float lightRadius = 4f;
    [Tooltip("Layers that block the light (walls, trees) — same mask the player's FieldOfView uses.")]
    [SerializeField] private LayerMask obstacleMask;
    [Tooltip("Rays per degree used to trace the lit circle. The lit area is a plain circle with no cone to keep sharp, so this can stay well below the player's cone resolution.")]
    [SerializeField] private float raysPerDegree = 0.5f;
    [Tooltip("Colour this lamp burns with. Warm by default: an oil flame is the only warm thing in a dungeon lit by daylight-white vision, which is what separates a lit room from a seen one at a glance.")]
    [SerializeField] private Color lightColor = new Color(1f, 0.68f, 0.34f, 1f);
    [Tooltip("How much of the radius the lamp fades out over: 0 ends at a hard edge, 1 fades from the middle. Wide by default so a lamp bleeds into the dark instead of cutting a disc out of it.")]
    [Range(0f, 1f)]
    [SerializeField] private float edgeSoftness = 0.6f;

    [Header("Rebuilding")]
    [Tooltip("Seconds between mesh rebuilds. The lamp does not move, so it only needs to keep up with obstacles that do (opening doors). Set to 0 to rebuild every frame.")]
    [SerializeField] private float rebuildInterval = 0.1f;

    [Header("Edge Detection")]
    [Tooltip("Distance difference between adjacent rays that triggers edge refinement.")]
    [SerializeField] private float edgeDistanceThreshold = 0.5f;
    [SerializeField] private int edgeResolveIterations = 4;

    [Header("Rendering")]
    [Tooltip("Material using Custom/VisionMaskWriter — the same one the player's FieldOfView uses.")]
    [SerializeField] private Material visionMaskMaterial;
    [Tooltip("Sorting layer name for the light mesh (must match the player's FOV mesh).")]
    [SerializeField] private string sortingLayerName = "Default";
    [Tooltip("Order in layer. Lights combine by max in the mask, so this only affects draw order between light meshes, not the result.")]
    [SerializeField] private int sortingOrder = 5;

    [Header("Debug")]
    [SerializeField] private GizmoDebugSettings gizmoDebugSettings;

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int LightTintId = Shader.PropertyToID("_LightTint");
    private static readonly int EdgeSoftnessId = Shader.PropertyToID("_EdgeSoftness");

    private Mesh lightMesh;
    private MeshRenderer meshRenderer;
    private MaterialPropertyBlock propertyBlock;
    private OcclusionMeshBuilder meshBuilder;
    private ILightFuel fuel;
    private float nextRebuildTime;
    private bool wasLit;
    private ILightIntensity intensitySource;
    private float lastAppliedIntensity = -1f;

    private readonly List<float> angleBuffer = new List<float>(256);
    private System.Func<float, float> reachForAngle;

    /// <summary>True while the lamp is actually emitting light (enabled and, if it has a fuel component, still fuelled).</summary>
    public bool IsLit => isActiveAndEnabled && (fuel == null || fuel.HasFuel);

    /// <summary>Radius of the lit circle in world units.</summary>
    public float LightRadius => lightRadius;

    private void Awake()
    {
        lightMesh = new Mesh { name = "Light Mesh" };
        meshBuilder = new OcclusionMeshBuilder(edgeDistanceThreshold, edgeResolveIterations);
        reachForAngle = _ => lightRadius;
        fuel = GetComponent<ILightFuel>();
        intensitySource = GetComponent<ILightIntensity>();
        propertyBlock = new MaterialPropertyBlock();

        // Drawn by a child on the vision mask layer, so the lamp GameObject keeps whatever layer
        // it needs for physics and interaction.
        meshRenderer = VisionMaskRenderer.CreateLightMeshChild(
            "Light Mask Mesh", transform, lightMesh, visionMaskMaterial, sortingLayerName, sortingOrder);
    }

    private void LateUpdate()
    {
        bool lit = IsLit;
        if (lit != wasLit)
        {
            meshRenderer.enabled = lit;
            wasLit = lit;
        }

        if (!lit) return;

        ApplyLight();

        if (Time.time < nextRebuildTime) return;
        nextRebuildTime = Time.time + Mathf.Max(0f, rebuildInterval);

        BuildMesh();
    }

    /// <summary>
    /// Pushes this lamp's own look — colour, rim fade and the brightness its
    /// <see cref="ILightIntensity"/> component asks for — onto the mask renderer. Runs every
    /// frame — far cheaper than <see cref="rebuildInterval"/>, which paces the raycast re-trace —
    /// so a flicker stays smooth even while the mesh itself is retraced rarely. Only pushes when
    /// the brightness actually changed, since the colour and the fade never do on their own.
    /// </summary>
    private void ApplyLight()
    {
        float intensity = intensitySource != null ? Mathf.Clamp01(intensitySource.Intensity) : 1f;
        if (Mathf.Approximately(intensity, lastAppliedIntensity)) return;

        lastAppliedIntensity = intensity;
        propertyBlock.SetFloat(IntensityId, intensity);
        // Re-set every frame the block is pushed: SetPropertyBlock replaces the whole block,
        // so anything left out of it would fall back to the shared material's value.
        propertyBlock.SetColor(LightTintId, lightColor);
        propertyBlock.SetFloat(EdgeSoftnessId, edgeSoftness);
        meshRenderer.SetPropertyBlock(propertyBlock);
    }

    /// <summary>
    /// Traces the lit circle: a full 360° sweep, every ray reaching lightRadius unless an
    /// obstacle stops it earlier.
    /// </summary>
    private void BuildMesh()
    {
        BuildAngleSweep();

        // UV.y is left at the main-region flag (0) for every vertex: unlike the player's
        // near-vision circle, a lamp should fade out gradually at its rim rather than end abruptly.
        meshBuilder.Build(lightMesh, transform, obstacleMask, angleBuffer, reachForAngle, null);
    }

    /// <summary>Fills angleBuffer with an evenly spaced full-circle sweep.</summary>
    private void BuildAngleSweep()
    {
        angleBuffer.Clear();

        int stepCount = Mathf.Max(3, Mathf.RoundToInt(360f * Mathf.Max(0.01f, raysPerDegree)));
        float stepSize = 360f / stepCount;
        for (int i = 0; i <= stepCount; i++)
            angleBuffer.Add(stepSize * i);
    }

    /// <summary>
    /// True if the given world point is lit by this lamp: within the radius and not behind an
    /// obstacle. Lets gameplay code ask about the light without reading the rendered mesh.
    /// </summary>
    public bool IsPointLit(Vector2 worldPoint)
    {
        if (!IsLit) return false;

        Vector2 origin = transform.position;
        if (Vector2.Distance(origin, worldPoint) > lightRadius) return false;

        return Physics2D.Linecast(origin, worldPoint, obstacleMask).collider == null;
    }

    private void OnDrawGizmos()
    {
        if (gizmoDebugSettings != null && !gizmoDebugSettings.IsVisible(GizmoRanges.LightRadius)) return;

        Gizmos.color = lightColor;
        Gizmos.DrawWireSphere(transform.position, lightRadius);
    }
}
