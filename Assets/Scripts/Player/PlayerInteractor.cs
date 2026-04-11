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
        controls = new PlayerControls();
    }

    private void OnEnable()
    {
        controls.Player.Enable();
        controls.Player.Interact.performed += OnInteractPerformed;
    }

    private void OnDisable()
    {
        controls.Player.Interact.performed -= OnInteractPerformed;
        controls.Player.Disable();
    }

    private void OnDestroy()
    {
        controls?.Dispose();
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

