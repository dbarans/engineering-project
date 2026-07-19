using UnityEngine;

/// <summary>
/// Add this component to any GameObject (enemy, item, NPC) that should be hidden
/// when outside the player's field of view.
/// Automatically finds FieldOfView on Start; can also be assigned in inspector.
/// </summary>
public class HideableObject : MonoBehaviour
{
    [Tooltip("Leave empty to auto-find FieldOfView in scene.")]
    [SerializeField] private FieldOfView playerFov;

    [Tooltip("Which point on this object to use for visibility check (defaults to own transform).")]
    [SerializeField] private Transform visibilityCheckPoint;

    [Tooltip("Re-check visibility every N frames instead of every frame. Each instance is offset so checks spread evenly across frames instead of all landing on the same one — with many hideable objects this cuts total Physics2D.Linecast calls per frame without noticeable staleness (a few frames of delay at 60 FPS).")]
    [SerializeField] private int checkEveryNFrames = 3;

    private static int nextOffset;

    private Renderer[] renderers;
    private int frameOffset;

    private void Start()
    {
        renderers = GetComponentsInChildren<Renderer>();

        if (playerFov == null)
            playerFov = FindFirstObjectByType<FieldOfView>();

        if (visibilityCheckPoint == null)
            visibilityCheckPoint = transform;

        frameOffset = nextOffset++;
    }

    private void Update()
    {
        if (playerFov == null) return;
        if (checkEveryNFrames > 1 && (Time.frameCount + frameOffset) % checkEveryNFrames != 0) return;

        bool visible = playerFov.IsVisible(visibilityCheckPoint.position);
        SetVisible(visible);
    }

    private void SetVisible(bool visible)
    {
        foreach (var r in renderers)
            r.enabled = visible;
    }
}
