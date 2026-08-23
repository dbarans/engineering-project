using UnityEngine;

/// <summary>
/// Turns a light-source item selected in the hotbar into an actual light in the player's hand.
/// While such an item is selected — a torch, whose <see cref="ItemData.lightRadius"/> is what
/// marks it as one — the 360° circle the player sees around themselves grows to that radius and
/// everything in view takes the flame's colour, through
/// <see cref="FieldOfView.SetHeldLight"/>.
///
/// The flame's own wavering is not this component's job: put an <see cref="ILightIntensity"/>
/// component next to it (<see cref="FlameFlicker"/>) and the light is scaled by it every frame,
/// exactly as a <see cref="StationaryLightSource"/> is. With none, the torch burns steadily.
///
/// Attach to the player. The hotbar lives in the UI canvas rather than on the player prefab, so
/// it is resolved at runtime like the other hotbar-driven player components do.
/// </summary>
public class HeldTorch : MonoBehaviour
{
    [Tooltip("The view the held light feeds. Auto-resolved from this GameObject if left unset.")]
    [SerializeField] private FieldOfView fieldOfView;

    [Tooltip("Auto-resolved at runtime if left unset (the hotbar lives in the UI canvas, not the player prefab).")]
    [SerializeField] private HotbarUI hotbar;

    private ILightIntensity flame;

    /// <summary>The light-source item currently in the player's hand, or null when none is.</summary>
    public ItemData HeldLight { get; private set; }

    /// <summary>True while a light source is selected in the hotbar and burning.</summary>
    public bool IsLit => HeldLight != null;

    private void Awake()
    {
        if (fieldOfView == null) fieldOfView = GetComponent<FieldOfView>();
        flame = GetComponent<ILightIntensity>();
    }

    private void Start()
    {
        if (hotbar == null) hotbar = FindFirstObjectByType<HotbarUI>();
    }

    private void OnDisable()
    {
        // Putting the torch out on the way, so a disabled player never leaves the view stuck
        // amber and wide.
        HeldLight = null;
        if (fieldOfView != null) fieldOfView.SetHeldLight(false, 0f, Color.white, 1f);
    }

    /// <summary>
    /// Feeds the view what the hand is holding this frame. Pushed every frame rather than only on
    /// a selection change, because the flame's brightness changes continuously while the item
    /// under the selection does not.
    /// </summary>
    private void Update()
    {
        if (fieldOfView == null) return;

        HeldLight = LightSourceFor(hotbar != null ? hotbar.SelectedItem : null);

        if (HeldLight == null)
        {
            fieldOfView.SetHeldLight(false, 0f, Color.white, 1f);
            return;
        }

        float intensity = flame != null ? Mathf.Clamp01(flame.Intensity) : 1f;
        fieldOfView.SetHeldLight(true, HeldLight.lightRadius, HeldLight.lightColor, intensity);
    }

    /// <summary>The item if it lights anything at all, otherwise null.</summary>
    private static ItemData LightSourceFor(ItemData item)
    {
        return item != null && item.lightRadius > 0f ? item : null;
    }
}
