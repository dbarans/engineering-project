using UnityEngine;

/// <summary>
/// Creates a large black quad that covers the entire scene.
/// Works together with FovMaskWriter (on FOV mesh) and DarknessOverlay shader:
///   - FOV mesh writes stencil = 1 (Queue Transparent+1)
///   - This quad renders black where stencil != 1 (Queue Transparent+2)
///
/// Setup in editor:
///   1. Create an empty GameObject named "DarknessOverlay".
///   2. Add this component.
///   3. Assign a Material using the Custom/DarknessOverlay shader.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class DarknessOverlayQuad : MonoBehaviour
{
    [Tooltip("Material using the Custom/DarknessOverlay shader.")]
    [SerializeField] private Material darknessMaterial;

    [Tooltip("Half-size of the darkness quad in world units. Should be larger than camera view.")]
    [SerializeField] private float quadHalfSize = 100f;

    [Header("Rendering")]
    [SerializeField] private string sortingLayerName = "Default";
    [Tooltip("Must be higher than FieldOfView sortingOrder so darkness renders after stencil write.")]
    [SerializeField] private int sortingOrder = 6;

    [Header("Follow")]
    [Tooltip("If true, the quad follows the main camera each frame so it always covers the view.")]
    [SerializeField] private bool followCamera = true;

    private Camera targetCamera;

    private void Awake()
    {
        BuildQuad();
        targetCamera = Camera.main;
    }

    private void LateUpdate()
    {
        if (!followCamera || targetCamera == null) return;

        Vector3 camPos = targetCamera.transform.position;
        transform.position = new Vector3(camPos.x, camPos.y, transform.position.z);
    }

    private void BuildQuad()
    {
        var mf = GetComponent<MeshFilter>();
        var mr = GetComponent<MeshRenderer>();

        Mesh quad = new Mesh { name = "Darkness Quad" };

        float s = quadHalfSize;
        quad.vertices = new Vector3[]
        {
            new Vector3(-s, -s, 0f),
            new Vector3( s, -s, 0f),
            new Vector3( s,  s, 0f),
            new Vector3(-s,  s, 0f),
        };
        quad.triangles = new int[] { 0, 2, 1, 0, 3, 2 };
        quad.RecalculateNormals();

        mf.mesh = quad;
        if (darknessMaterial != null)
            mr.sharedMaterial = darknessMaterial;

        mr.sortingLayerName = sortingLayerName;
        mr.sortingOrder = sortingOrder;
    }
}
