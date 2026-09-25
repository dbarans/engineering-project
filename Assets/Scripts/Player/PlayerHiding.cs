using System;
using UnityEngine;

/// <summary>
/// Lets a crouching player slip under low obstacles (tables) and hide there.
///
/// Two independent halves:
/// <list type="bullet">
/// <item>While crouching, the player's own collider excludes <see cref="crouchPassableLayers"/>,
/// so the solid part of every hideout stops blocking them. Standing up makes those obstacles
/// solid again — a table then behaves exactly like a barrel.</item>
/// <item><see cref="CrouchHideout"/> triggers report when the player is inside a hideout
/// footprint. Being inside one <em>while crouching</em> means concealed: enemies cannot detect
/// the player at all (<see cref="IPlayerConcealment"/>), and <see cref="PlayerInputHandler"/>
/// keeps crouch locked on so releasing Ctrl under a table doesn't stand the player up into it.</item>
/// </list>
///
/// The count of overlapping hideouts is tracked rather than a bool, so two adjacent tables
/// don't cancel each other out when the player walks from one straight into the other.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerHiding : MonoBehaviour, IPlayerConcealment
{
    [Tooltip("The player's body collider. While crouching it stops colliding with Crouch Passable Layers so the player fits under tables.")]
    [SerializeField] private Collider2D bodyCollider;
    [Tooltip("Layers holding the solid part of crouch-passable obstacles (the CrouchPassable layer). Must match the layer on the hideout's solid collider.")]
    [SerializeField] private LayerMask crouchPassableLayers;

    /// <summary>
    /// Raised when the player enters the first hideout or leaves the last one. Consumed by
    /// <see cref="PlayerInputHandler"/> to re-evaluate the movement mode once crouch is no
    /// longer locked.
    /// </summary>
    public event Action<bool> InHideoutChanged;

    private PlayerMovement movement;
    private int hideoutCount;

    /// <summary>True while the player's body overlaps at least one hideout footprint.</summary>
    public bool InHideout => hideoutCount > 0;

    /// <summary>
    /// True while the player is actually hidden: inside a hideout <em>and</em> crouching.
    /// A standing player brushing the outer edge of a table is not hidden — the solid collider
    /// is inset from the trigger, so that sliver is reachable while standing.
    /// </summary>
    public bool IsHidden => InHideout && movement.CurrentMode == PlayerMovement.MovementMode.Sneak;

    public bool IsConcealed => IsHidden;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        if (bodyCollider == null) bodyCollider = GetComponent<Collider2D>();
    }

    private void OnEnable()
    {
        movement.MovementModeChanged += OnMovementModeChanged;
        ApplyCrouchPassability(movement.CurrentMode);
    }

    private void OnDisable()
    {
        movement.MovementModeChanged -= OnMovementModeChanged;
        ApplyCrouchPassability(PlayerMovement.MovementMode.Walk);
    }

    private void OnMovementModeChanged(PlayerMovement.MovementMode mode)
    {
        ApplyCrouchPassability(mode);
    }

    /// <summary>
    /// Makes crouch-passable obstacles solid or not for the player's own collider. Only the
    /// configured layer bits are touched, so any other exclusions on the collider survive.
    /// </summary>
    private void ApplyCrouchPassability(PlayerMovement.MovementMode mode)
    {
        if (bodyCollider == null) return;

        int excluded = bodyCollider.excludeLayers.value;
        bool crouching = mode == PlayerMovement.MovementMode.Sneak;

        bodyCollider.excludeLayers = crouching
            ? excluded | crouchPassableLayers.value
            : excluded & ~crouchPassableLayers.value;
    }

    /// <summary>Called by <see cref="CrouchHideout"/> when the player enters its footprint.</summary>
    public void EnterHideout()
    {
        hideoutCount++;
        if (hideoutCount == 1) InHideoutChanged?.Invoke(true);
    }

    /// <summary>Called by <see cref="CrouchHideout"/> when the player leaves its footprint.</summary>
    public void ExitHideout()
    {
        if (hideoutCount == 0) return;

        hideoutCount--;
        if (hideoutCount == 0) InHideoutChanged?.Invoke(false);
    }
}
