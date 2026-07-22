using UnityEngine;

/// <summary>
/// Put on the same GameObject as a SpriteRenderer authored with the Custom/SpriteFovMasked
/// material (Materials/SpriteFovMasked.mat — stencil comparison Always, so it renders normally
/// in the Editor: Scene view, Prefab view, Project thumbnails, where no FieldOfView has ever
/// written the vision stencil). At Awake here we swap in Materials/SpriteFovMaskedClipped.mat,
/// the same shader with stencil comparison baked to Equal, so hard FOV clipping only applies
/// once the game is actually running.
///
/// A MaterialPropertyBlock override was tried first but does not affect the fixed-function
/// Stencil block — that state is baked per-material, not resolved per-draw-call — hence the
/// material swap instead.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class FovMaskedSpriteRuntime : MonoBehaviour
{
    [Tooltip("Material with the same shader but stencil comparison baked to Equal.")]
    [SerializeField] private Material clippedMaterial;

    private void Awake()
    {
        if (clippedMaterial == null)
        {
            Debug.LogWarning($"[FovMaskedSpriteRuntime] No clipped material assigned on {name} — FOV clipping will not apply.", this);
            return;
        }

        GetComponent<SpriteRenderer>().sharedMaterial = clippedMaterial;
    }
}
