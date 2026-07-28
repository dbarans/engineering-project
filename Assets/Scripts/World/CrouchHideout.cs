using UnityEngine;

/// <summary>
/// Marks the footprint a crouching player can hide under (e.g. under a table). Goes on an
/// object whose own collider is a trigger covering that footprint; the solid part that blocks
/// a standing player lives on a child on the CrouchPassable layer.
///
/// This component only reports enter/exit to <see cref="PlayerHiding"/> — whether the player
/// actually counts as hidden (and therefore undetectable) is decided there, since it also
/// depends on crouching.
///
/// The solid child is deliberately inset a little from this trigger: the player is only ever
/// let out of the hideout on trigger exit, and by then they must already be clear of the
/// smaller solid box, so standing up at the edge can never leave them stuck inside it.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class CrouchHideout : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.attachedRigidbody == null) return;

        var hiding = other.attachedRigidbody.GetComponent<PlayerHiding>();
        if (hiding != null) hiding.EnterHideout();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other.attachedRigidbody == null) return;

        var hiding = other.attachedRigidbody.GetComponent<PlayerHiding>();
        if (hiding != null) hiding.ExitHideout();
    }
}
