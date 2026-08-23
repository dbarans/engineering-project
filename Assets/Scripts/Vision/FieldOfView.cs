using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates a field-of-view mesh around the player using raycasting. Attach directly to the
/// player GameObject. The mesh is not drawn to the screen: it is rendered into the shared vision
/// mask (see <see cref="VisionMaskRenderer"/>) through a child on the mask layer, together with
/// every other light source, and the darkness overlay and masked sprites read it from there.
/// </summary>
public class FieldOfView : MonoBehaviour
{
    [Header("View Settings")]
    [Tooltip("Base view radius, used whenever no torch is held. See Held View Radius for the torch override.")]
    [SerializeField] private float viewRadius = 15f;
    [Tooltip("Base view cone angle, used whenever no torch is held. Temporarily overridden while aiming — see SetAimNarrowing — and while a torch is held — see Held View Angle.")]
    [Range(1, 360)]
    [SerializeField] private float viewAngle = 360f;
    [Tooltip("Small 360° visibility radius around the player, in addition to the cone (Darkwood-style). Set to 0 to disable.")]
    [SerializeField] private float nearVisionRadius = 2f;

    [Header("Held Light")]
    [Tooltip("A lit torch in the player's hand: the cone widens to Held View Angle and reaches out to Held View Radius, the 360° circle behind the player grows to Held Light Radius, and everything they see takes the flame's colour. Toggle here to test without an item; in play it is driven by HeldTorch from the hotbar.")]
    [SerializeField] private bool heldLightEnabled = false;
    [Tooltip("View cone angle while a torch is held. Wider than the base View Angle — a flame throws light to every side, not just ahead.")]
    [Range(1, 360)]
    [SerializeField] private float heldViewAngle = 130f;
    [Tooltip("View radius while a torch is held. Bigger than the base View Radius — a torch is meant to light up more of the room, not less.")]
    [SerializeField] private float heldViewRadius = 20f;
    [Tooltip("Radius of the 360° circle behind the player while a light is held. Keep below Held View Radius — this only covers what the cone itself does not.")]
    [SerializeField] private float heldLightRadius = 4.5f;
    [Tooltip("Colour the held flame casts over everything the player sees. Only used while a light is held; ordinary vision uses View Tint.")]
    [SerializeField] private Color heldLightColor = new Color(1f, 0.72f, 0.36f, 1f);

    [Header("Ray Settings")]
    [Tooltip("Number of rays per degree inside the view cone. Higher = smoother but slower.")]
    [SerializeField] private int raysPerDegree = 1;
    [Tooltip("Rays per degree for the near-vision/held-light circle OUTSIDE the cone (the part behind/beside the player). This area doesn't need cone-level smoothness, so it defaults much coarser to cut raycasts without touching cone quality.")]
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
    [Tooltip("Material using Custom/VisionMaskWriter. The FOV mesh is drawn through it into the vision mask, never straight to the screen.")]
    [SerializeField] private Material visionMaskMaterial;
    [Tooltip("Colour ordinary eyesight casts over what it lights. Black — the default — casts none, so the scene keeps its own colours and a lamp's warm pool is the only tinted thing on screen. Set a dark blue for a cold cast over everything the player sees.")]
    [SerializeField] private Color viewTint = Color.black;
    [Tooltip("Sorting layer name for the FOV mesh (must match a layer defined in Tags & Layers).")]
    [SerializeField] private string sortingLayerName = "Default";
    [Tooltip("Order in layer. Lights combine by max in the mask, so this only affects draw order between light meshes, not the result.")]
    [SerializeField] private int sortingOrder = 5;

    [Header("Aiming")]
    [Tooltip("How fast the view angle narrows toward its aim target, in degrees/second. Independent of how fast the aim itself charges — the FOV eases toward the target on its own pace instead of tracking the charge progress 1:1.")]
    [SerializeField] private float aimTransitionSpeed = 40f;
    [Tooltip("How fast the view angle widens back to normal after aiming stops, in degrees/second. Faster than aimTransitionSpeed so releasing aim feels snappy while still easing smoothly (not an instant jump).")]
    [SerializeField] private float aimReturnSpeed = 240f;

    private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
    private static readonly int LightTintId = Shader.PropertyToID("_LightTint");

    private Mesh viewMesh;
    private OcclusionMeshBuilder meshBuilder;
    private MeshRenderer maskRenderer;
    private MaterialPropertyBlock propertyBlock;
    private float facingAngle;
    private bool aimNarrowingActive;
    private float aimNarrowAngle;
    private float currentViewAngle;
    private bool useNearCircle;
    private float heldLightIntensity = 1f;

    // Cached delegates handed to the mesh builder, so the per-frame build allocates nothing.
    private System.Func<float, float> reachForAngle;
    private System.Func<float, bool> isNearCircleAngle;

    /// <summary>
    /// Narrows the view cone toward aimAngle while aiming (e.g. a ranged weapon's spread cone
    /// while charging a shot) — the player trades peripheral vision for focus on the target.
    /// The transition itself is gradual (aimTransitionSpeed), not synced to the aim's own
    /// charge curve. Call with active = false to ease back to viewAngle. Whoever is currently
    /// aiming (only one weapon can be equipped at a time) owns this call.
    /// </summary>
    public void SetAimNarrowing(bool active, float aimAngle)
    {
        aimNarrowingActive = active;
        aimNarrowAngle = aimAngle;
    }

    /// <summary>
    /// Target view cone angle: the held-torch angle while a light is held, otherwise the base
    /// angle — either way, narrowed further while aiming. Aiming takes priority over the torch
    /// because it is the rarer, more deliberate state: a shot lined up should narrow the view
    /// exactly as it would with no torch in hand.
    /// </summary>
    private float TargetViewAngle =>
        aimNarrowingActive ? Mathf.Clamp(aimNarrowAngle, 1f, 360f) : EffectiveViewAngle();

    /// <summary>Base view cone angle: wider while a torch is held, otherwise <see cref="viewAngle"/>.</summary>
    private float EffectiveViewAngle() => heldLightEnabled ? heldViewAngle : viewAngle;

    /// <summary>View cone reach: further while a torch is held, otherwise <see cref="viewRadius"/>.</summary>
    private float EffectiveViewRadius() => heldLightEnabled ? heldViewRadius : viewRadius;

    // Reused across frames to avoid per-frame GC allocation (BuildMesh runs every LateUpdate).
    private readonly List<float> angleBuffer = new List<float>(512);

    private void Awake()
    {
        viewMesh = new Mesh { name = "FOV Mesh" };
        currentViewAngle = viewAngle;
        meshBuilder = new OcclusionMeshBuilder(edgeDistanceThreshold, edgeResolveIterations);
        reachForAngle = MaxDistanceForAngle;
        isNearCircleAngle = IsNearCircleAngle;

        propertyBlock = new MaterialPropertyBlock();

        // The mesh is drawn by a child on the vision mask layer, not by the player itself — the
        // player has to stay on its own layer for physics and enemy detection.
        maskRenderer = VisionMaskRenderer.CreateLightMeshChild(
            "FOV Mask Mesh", transform, viewMesh, visionMaskMaterial, sortingLayerName, sortingOrder);

        // Legacy setups draw the FOV mesh straight from the player GameObject. That would now
        // paint the raw mask onto the screen, so silence it.
        var localRenderer = GetComponent<MeshRenderer>();
        if (localRenderer != null)
            localRenderer.enabled = false;
    }

    private void LateUpdate()
    {
        float speed = aimNarrowingActive ? aimTransitionSpeed : aimReturnSpeed;
        currentViewAngle = Mathf.MoveTowards(currentViewAngle, TargetViewAngle, speed * Time.deltaTime);
        ApplyLight();
        BuildMesh();
    }

    /// <summary>
    /// Pushes the colour and brightness the view is drawn with into the vision mask. Ordinary
    /// eyesight is neutral and steady; while a torch is held, everything the player sees is lit by
    /// it, so the whole view takes the flame's colour and wavers with it.
    ///
    /// Written through a <see cref="MaterialPropertyBlock"/>, which is what lets the player's view
    /// and every lamp share one mask material while each burns its own colour.
    /// </summary>
    private void ApplyLight()
    {
        if (maskRenderer == null) return;

        propertyBlock.SetColor(LightTintId, heldLightEnabled ? heldLightColor : viewTint);
        propertyBlock.SetFloat(IntensityId, heldLightEnabled ? Mathf.Clamp01(heldLightIntensity) : 1f);
        maskRenderer.SetPropertyBlock(propertyBlock);
    }

    private void BuildMesh()
    {
        facingAngle = directionSource != null ? directionSource.eulerAngles.z : transform.eulerAngles.z;

        // When the near-vision circle is active on a cone, sweep the full 360°:
        // inside the cone the reach is viewRadius, elsewhere it drops to the near radius.
        useNearCircle = EffectiveNearRadius() > 0f && currentViewAngle < 360f;
        float sweepAngle = useNearCircle ? 360f : currentViewAngle;
        float startAngle = useNearCircle ? facingAngle - 180f : facingAngle - currentViewAngle / 2f;

        BuildAngleSweep(sweepAngle, startAngle);

        // UV.x: distance normalized by this direction's reach (so the near circle fades over its
        // own radius and the cone over the full radius). UV.y: region flag — 1 for the near-vision
        // circle, 0 for the main cone. The shader keeps the near circle mostly clear so objects
        // behind the player stay visible.
        meshBuilder.Build(viewMesh, transform, obstacleMask, angleBuffer, reachForAngle, isNearCircleAngle);
    }

    /// <summary>
    /// True when the given world angle falls outside the view cone and is therefore covered by
    /// the near-vision / held-light circle rather than the cone itself.
    /// </summary>
    private bool IsNearCircleAngle(float angleDeg)
    {
        return useNearCircle && Mathf.Abs(Mathf.DeltaAngle(facingAngle, angleDeg)) > currentViewAngle * 0.5f;
    }

    /// <summary>
    /// Fills angleBuffer with the angles to raycast this frame. Inside the cone, uses full
    /// raysPerDegree resolution (cone quality is untouched); outside the cone (the near-vision/
    /// held-light circle behind/beside the player, only relevant when useNearCircle is true), uses
    /// the much coarser nearCircleRaysPerDegree — that region doesn't need cone-level smoothness,
    /// so this cuts total raycasts without reducing what the player actually sees ahead of them.
    /// </summary>
    private void BuildAngleSweep(float sweepAngle, float startAngle)
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
        float halfCone = EffectiveViewAngle() * 0.5f;
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
    /// Current radius of the 360° circle around the player: larger while a light is held.
    /// </summary>
    private float EffectiveNearRadius()
    {
        return heldLightEnabled ? Mathf.Max(nearVisionRadius, heldLightRadius) : nearVisionRadius;
    }

    /// <summary>
    /// Hands the player's held light source to the view. While lit, the cone widens to
    /// <see cref="heldViewAngle"/> and reaches out to <see cref="heldViewRadius"/>, the 360°
    /// circle behind the player grows to <paramref name="radius"/>, and everything they see is
    /// drawn in <paramref name="color"/> at <paramref name="intensity"/> — so a flame's flicker
    /// reaches the screen without the view having to know what a flame is.
    ///
    /// Called every frame by <see cref="HeldTorch"/> while a torch is selected in the hotbar, and
    /// once with <paramref name="lit"/> false when it is put away.
    /// </summary>
    /// <param name="lit">Whether a light is currently burning in the player's hand.</param>
    /// <param name="radius">How far the near-vision circle behind the player reaches, in world units.</param>
    /// <param name="color">Colour the flame casts over the view.</param>
    /// <param name="intensity">How brightly it burns this frame: 0 out, 1 full strength.</param>
    public void SetHeldLight(bool lit, float radius, Color color, float intensity)
    {
        heldLightEnabled = lit;
        heldLightIntensity = intensity;

        // The near-circle radius and colour are left at their serialized values when nothing is
        // held, so the inspector toggle still describes a usable light to test with. The cone's
        // own angle and radius are not item-driven at all — heldViewAngle/heldViewRadius are
        // tuned once for "a torch is out", not per item.
        if (!lit) return;

        heldLightRadius = radius;
        heldLightColor = color;
    }

    /// <summary>
    /// Reach for a ray at the given world angle: the full effective view radius inside the cone,
    /// the near/held-light radius outside it (Darkwood-style circle around the player).
    /// </summary>
    private float MaxDistanceForAngle(float angleDeg)
    {
        float viewReach = EffectiveViewRadius();

        if (EffectiveNearRadius() <= 0f || currentViewAngle >= 360f)
            return viewReach;

        float offset = Mathf.Abs(Mathf.DeltaAngle(facingAngle, angleDeg));
        return offset <= currentViewAngle / 2f ? viewReach : EffectiveNearRadius();
    }

    /// <summary>
    /// Returns true if the given world-space point is visible: inside the cone within the
    /// effective view radius, or inside the near-vision circle around the player, and not
    /// obstructed.
    /// </summary>
    public bool IsVisible(Vector2 worldPoint)
    {
        Vector2 origin = transform.position;
        float distance = Vector2.Distance(origin, worldPoint);

        if (distance > EffectiveViewRadius())
            return false;

        float facing = directionSource != null ? directionSource.eulerAngles.z : transform.eulerAngles.z;

        if (currentViewAngle < 360f)
        {
            Vector2 facingDir = new Vector2(Mathf.Cos(facing * Mathf.Deg2Rad), Mathf.Sin(facing * Mathf.Deg2Rad));
            Vector2 toPoint = (worldPoint - origin).normalized;
            float angleToPoint = Vector2.Angle(facingDir, toPoint);

            // Outside the cone, only the near-vision / held-light circle counts.
            bool insideCone = angleToPoint <= currentViewAngle / 2f;
            bool insideNearCircle = EffectiveNearRadius() > 0f && distance <= EffectiveNearRadius();
            if (!insideCone && !insideNearCircle)
                return false;
        }

        RaycastHit2D hit = Physics2D.Linecast(origin, worldPoint, obstacleMask);
        return hit.collider == null;
    }

    /// <summary>Current view radius in world units — wider while a torch is held.</summary>
    public float ViewRadius => EffectiveViewRadius();
}
