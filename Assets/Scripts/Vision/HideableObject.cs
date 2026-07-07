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

    private Renderer[] renderers;

    private void Start()
    {
        renderers = GetComponentsInChildren<Renderer>();

        if (playerFov == null)
            playerFov = FindFirstObjectByType<FieldOfView>();

        if (visibilityCheckPoint == null)
            visibilityCheckPoint = transform;
    }

    private void Update()
    {
        if (playerFov == null) return;

        bool visible = playerFov.IsVisible(visibilityCheckPoint.position);
        SetVisible(visible);
    }

    private void SetVisible(bool visible)
    {
        foreach (var r in renderers)
            r.enabled = visible;
    }
}
