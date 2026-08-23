using UnityEngine;

/// <summary>
/// Picks which of several sprites this object draws, and how far it is turned, from where it
/// stands in the world. Debris is spawned from one prefab many times over — the generator drops
/// broken glass in clusters — and the same picture repeated three times in a corner reads as a
/// copy-paste rather than as a mess someone left.
///
/// The choice is hashed from the object's position rather than rolled from
/// <c>UnityEngine.Random</c>, so a given dungeon seed rebuilds exactly the same patches every
/// time and nothing about the look has to be saved.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class SpriteVariant : MonoBehaviour
{
    [Tooltip("Sprites to choose between. With fewer than two, only the rotation below still does anything.")]
    [SerializeField] private Sprite[] variants = new Sprite[0];

    [Tooltip("Turn the object to a random angle as well. Fine for debris on the floor, wrong for " +
             "anything with an up — a barrel drawn from above has one, a scatter of glass does not.")]
    [SerializeField] private bool randomRotation = true;

    private void Awake()
    {
        // Hashed from the position the object was spawned at, rounded to a tenth of a unit so
        // floating-point noise in the spawn maths cannot change the picture.
        var random = new DeterministicRandom(
            $"variant:{Mathf.RoundToInt(transform.position.x * 10f)}:{Mathf.RoundToInt(transform.position.y * 10f)}");

        if (variants.Length > 0)
        {
            var renderer = GetComponent<SpriteRenderer>();
            Sprite chosen = variants[random.RangeInclusive(0, variants.Length - 1)];
            if (chosen != null) renderer.sprite = chosen;
        }

        if (randomRotation)
            transform.rotation = Quaternion.Euler(0f, 0f, random.NextFloat() * 360f);
    }
}
