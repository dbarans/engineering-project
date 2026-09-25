using UnityEngine;

/// <summary>
/// Put on the same GameObject as a SpriteRenderer authored with the Custom/SpriteFovMasked
/// material (Materials/SpriteFovMasked.mat — vision masking off, so it renders normally in the
/// Editor: Scene view, Prefab view, Project thumbnails, where no vision mask has ever been
/// rendered). At Awake here we swap in Materials/SpriteFovMaskedClipped.mat, the same shader with
/// masking enabled, so the sprite is masked by the player's vision only once the game is running.
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
