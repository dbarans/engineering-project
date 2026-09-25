using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Generic player interactor that triggers IInteractable via a line-of-sight check.
/// No highlighting; interaction happens only on the Interact input action press.
/// </summary>
public class PlayerInteractor : MonoBehaviour
{
    [SerializeField] private float interactRadius = 2.5f;
    [SerializeField] private LayerMask interactableLayerMask;
    [SerializeField] private LayerMask obstacleLayerMask;

    private PlayerControls controls;

    private void Awake()
    {
        controls = InputService.Controls;
    }

    private void OnEnable()
    {
        // Not controls.Player.Enable() here either — see the note in OnDisable. The map's
        // enabled state is PlayerInputHandler's alone to own on the shared instance;
        // whenever it is on, this subscription is live too.
        controls.Player.Interact.performed += OnInteractPerformed;
    }

    private void OnDisable()
    {
        controls.Player.Interact.performed -= OnInteractPerformed;
        // Not controls.Player.Disable() here: the map is shared with PlayerInputHandler,
        // which is the one place that deliberately disables it (backpack open). This
        // component disabling would otherwise kill player movement/attack too whenever
        // PlayerInteractor itself is toggled off independently of that.
    }

    private void OnInteractPerformed(InputAction.CallbackContext context)
    {
        Vector2 origin = transform.position;

        Collider2D[] candidates = Physics2D.OverlapCircleAll(origin, interactRadius, interactableLayerMask);
        if (candidates == null || candidates.Length == 0) return;

        // Pick the closest visible interactable.
        float bestSqrDist = float.MaxValue;
        IInteractable bestInteractable = null;

        foreach (var col in candidates)
        {
            if (col == null) continue;

            var interactable = col.GetComponentInParent<IInteractable>();
            if (interactable == null) continue;

            Vector2 targetPoint = col.bounds.center;
            float sqrDist = (origin - targetPoint).sqrMagnitude;
            if (sqrDist >= bestSqrDist) continue;

            // Line-of-sight: if something in obstacleLayerMask is between player and target, do not interact.
            RaycastHit2D hit = Physics2D.Linecast(origin, targetPoint, obstacleLayerMask);
            if (hit.collider != null) continue;

            bestSqrDist = sqrDist;
            bestInteractable = interactable;
        }

        if (bestInteractable == null) return;

        bestInteractable.Interact(new InteractContext { Player = gameObject });
    }
}

