using UnityEngine;

/// <summary>
/// Renders every light source in the scene into one offscreen vision mask and publishes it as the
/// global <c>_VisionMask</c> texture. Attach to the main camera.
///
/// Light meshes (the player's <see cref="FieldOfView"/> and every <see cref="StationaryLightSource"/>)
/// live on their own layer and are drawn by a dedicated child camera using
/// Shaders/VisionMaskWriter.shader, which combines them with BlendOp Max. Consumers — the darkness
/// overlay and sprites using Custom/SpriteFovMasked — then sample that single mask in screen space.
///
/// The mask is a colour texture: its alpha is how lit a pixel is and its RGB the colour of the
/// brightest light reaching it, so the darkness overlay can wash a lamp's surroundings amber
/// without a second lighting pass.
///
/// This replaces the earlier stencil-based approach, where visibility was a single bit. A bit can
/// only say "seen / not seen", which made sprites pop at the light boundary while the ground faded,
/// and made every light paint its own dark rim on top of whatever another light had already lit.
/// A continuous mask combined by max fixes both.
/// </summary>
[RequireComponent(typeof(Camera))]
public class VisionMaskRenderer : MonoBehaviour
{
    /// <summary>Name of the layer holding the light meshes. Must exist in Tags &amp; Layers.</summary>
    public const string VisionMaskLayerName = "VisionMask";

    private static readonly int VisionMaskId = Shader.PropertyToID("_VisionMask");

    [Tooltip("Resolution of the mask relative to the screen. Below 1 saves fill rate; the mask is a smooth gradient, so it survives downscaling well.")]
    [Range(0.25f, 1f)]
    [SerializeField] private float resolutionScale = 1f;

    private Camera mainCamera;
    private Camera maskCamera;
    private RenderTexture maskTexture;

    /// <summary>Layer index the light meshes must be on for the mask camera to see them.</summary>
    public static int VisionMaskLayer => LayerMask.NameToLayer(VisionMaskLayerName);

    /// <summary>
    /// Creates the child renderer a light source draws its mesh through: a GameObject on the
    /// vision mask layer, so only the mask camera sees it. Light sources keep their own layer
    /// (the player stays on Player, a lamp on whatever it needs for physics) — only this child
    /// moves onto the mask layer.
    /// </summary>
    /// <param name="name">Name of the child GameObject, for readability in the hierarchy.</param>
    /// <param name="parent">The light source's transform; the mesh is built in its local space.</param>
    /// <param name="mesh">Mesh rebuilt by the light source each frame.</param>
    /// <param name="material">Material using Custom/VisionMaskWriter.</param>
    /// <param name="sortingLayerName">Sorting layer for the mask mesh.</param>
    /// <param name="sortingOrder">Order in layer for the mask mesh.</param>
    public static MeshRenderer CreateLightMeshChild(
        string name, Transform parent, Mesh mesh, Material material, string sortingLayerName, int sortingOrder)
    {
        var child = new GameObject(name);
        child.transform.SetParent(parent, false);

        int layer = VisionMaskLayer;
        if (layer >= 0)
            child.layer = layer;
        else
            Debug.LogWarning($"[VisionMaskRenderer] Layer '{VisionMaskLayerName}' is missing — the light on {parent.name} will not reach the vision mask.", parent);

        child.AddComponent<MeshFilter>().sharedMesh = mesh;

        var renderer = child.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material != null ? material : CreateDefaultMaskMaterial(parent);
        renderer.sortingLayerName = sortingLayerName;
        renderer.sortingOrder = sortingOrder;
        return renderer;
    }

    /// <summary>
    /// Builds a mask material from the shader alone, for light sources with no material assigned.
    /// Keeps a lamp working straight after dropping the component on a GameObject — but only in
    /// the Editor. A player build contains just the shaders some built asset references, so there
    /// Shader.Find returns null unless a material using this shader ships with the build. Always
    /// assign Materials/VisionMaskWriter.mat on anything that ends up in a build.
    /// </summary>
    private static Material CreateDefaultMaskMaterial(Transform owner)
    {
        Shader shader = Shader.Find("Custom/VisionMaskWriter");
        if (shader == null)
        {
            Debug.LogWarning($"[VisionMaskRenderer] Custom/VisionMaskWriter shader not found — the light on {owner.name} will not render.", owner);
            return null;
        }

        return new Material(shader);
    }

    private void Awake()
    {
        mainCamera = GetComponent<Camera>();

        int layer = VisionMaskLayer;
        if (layer < 0)
        {
            Debug.LogError($"[VisionMaskRenderer] Layer '{VisionMaskLayerName}' is missing — add it in Tags & Layers. Lights will not render.", this);
            enabled = false;
            return;
        }

        // The main camera must not draw the light meshes directly; they only feed the mask.
        mainCamera.cullingMask &= ~(1 << layer);

        CreateMaskCamera(layer);
        EnsureMaskTexture();
    }

    private void CreateMaskCamera(int layer)
    {
        var child = new GameObject("Vision Mask Camera");
        child.transform.SetParent(transform, false);

        maskCamera = child.AddComponent<Camera>();
        maskCamera.cullingMask = 1 << layer;
        maskCamera.clearFlags = CameraClearFlags.SolidColor;
        // Cleared to zero: anything no light reaches stays pitch dark.
        maskCamera.backgroundColor = Color.clear;
        maskCamera.allowHDR = false;
        maskCamera.allowMSAA = false;
        // Render before the main camera, so the mask is ready when the scene samples it.
        maskCamera.depth = mainCamera.depth - 1;
    }

    private void LateUpdate()
    {
        EnsureMaskTexture();
        CopyProjection();
    }

    /// <summary>
    /// Keeps the mask camera framing identical to the main camera, so mask pixels line up with
    /// screen pixels one to one (zoom, orthographic size and clipping planes can all change at runtime).
    /// </summary>
    private void CopyProjection()
    {
        maskCamera.orthographic = mainCamera.orthographic;
        maskCamera.orthographicSize = mainCamera.orthographicSize;
        maskCamera.fieldOfView = mainCamera.fieldOfView;
        maskCamera.nearClipPlane = mainCamera.nearClipPlane;
        maskCamera.farClipPlane = mainCamera.farClipPlane;
        maskCamera.rect = mainCamera.rect;
    }

    /// <summary>
    /// Creates the mask texture, or recreates it when the screen resolution changed.
    /// </summary>
    private void EnsureMaskTexture()
    {
        int width = Mathf.Max(1, Mathf.RoundToInt(Screen.width * resolutionScale));
        int height = Mathf.Max(1, Mathf.RoundToInt(Screen.height * resolutionScale));

        if (maskTexture != null && maskTexture.width == width && maskTexture.height == height)
            return;

        ReleaseMaskTexture();

        // ARGB rather than a single channel: alpha carries how lit a pixel is (what every
        // consumer sampled before), RGB the colour of the light that reached it, which is what
        // lets a lamp burn amber while the player's own vision stays neutral.
        maskTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
        {
            name = "Vision Mask",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        maskTexture.Create();

        maskCamera.targetTexture = maskTexture;
        Shader.SetGlobalTexture(VisionMaskId, maskTexture);
    }

    private void ReleaseMaskTexture()
    {
        if (maskTexture == null) return;

        if (maskCamera != null)
            maskCamera.targetTexture = null;

        maskTexture.Release();
        Destroy(maskTexture);
        maskTexture = null;
    }

    private void OnDestroy()
    {
        ReleaseMaskTexture();
    }
}
