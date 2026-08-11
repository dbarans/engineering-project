using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Single source of truth for the handful of gameplay keys that live outside
/// <c>PlayerControls</c> (the generated Input System asset) — interact, throw, cycle
/// pickup, toggle backpack. Each of those used to be its own <c>[SerializeField] Key</c>
/// field, duplicated per component with its own default, which meant changing one key
/// meant hunting through however many scripts happened to read it.
///
/// Lives in <c>Assets/Resources</c> and is loaded the same way as
/// <see cref="ItemDatabase"/> and <see cref="PrefabRegistry"/> — no scene wiring, works
/// from any component via <see cref="Instance"/>. This is deliberately a runtime-mutable
/// ScriptableObject rather than a set of consts: a future rebinding settings screen can
/// assign new <see cref="Key"/> values here at runtime and every consumer picks them up
/// on its next poll, with no extra plumbing.
///
/// Does not cover mouse-button/position reads (SimpleDoor, CameraParallax, HeldItemController,
/// HotbarUI's scroll) — those are pointer input, not rebindable keys, and are out of scope
/// here. Also does not cover <c>PlayerControls</c> actions (movement, attack, aim, …), which
/// already have one shared source in the generated asset; only the ad-hoc keys that were
/// never added to it are collected here.
/// </summary>
[CreateAssetMenu(fileName = "KeyBindings", menuName = "Input/Key Bindings")]
public class KeyBindings : ScriptableObject
{
    /// <summary>Path under Resources the runtime instance is loaded from.</summary>
    public const string ResourcesPath = "KeyBindings";

    [Header("Interaction")]
    [Tooltip("Opens/closes chests and cycles the pick-up selection among stacked ground items.")]
    public Key interact = Key.E;

    [Header("Inventory")]
    [Tooltip("Opens/closes the backpack panel.")]
    public Key toggleBackpack = Key.Tab;

    [Header("Combat")]
    [Tooltip("Throws the noise-making rock toward the cursor.")]
    public Key throwItem = Key.G;

    private static KeyBindings _instance;

    /// <summary>
    /// The project-wide bindings from Resources, or a fallback instance carrying the
    /// defaults above when the asset is missing — so a project that has not run the
    /// setup tool yet still gets working (if unconfigurable) keys instead of null
    /// reference exceptions everywhere.
    /// </summary>
    public static KeyBindings Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = Resources.Load<KeyBindings>(ResourcesPath);
                if (_instance == null)
                {
                    Debug.LogWarning(
                        $"[KeyBindings] No '{ResourcesPath}' asset in Resources — using " +
                        "built-in defaults. Run Tools ▸ Input ▸ Build Key Bindings to create one.");
                    _instance = CreateInstance<KeyBindings>();
                }
            }
            return _instance;
        }
    }
}
