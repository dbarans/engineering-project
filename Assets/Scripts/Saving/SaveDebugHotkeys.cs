#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Editor-only test harness for the save system until the Phase 5 UI exists:
/// F5 = save slot 0, F9 = load slot 0. Spawns itself on play — no scene wiring,
/// compiled out of builds entirely.
/// </summary>
public class SaveDebugHotkeys : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        var go = new GameObject(nameof(SaveDebugHotkeys));
        go.AddComponent<SaveDebugHotkeys>();
        DontDestroyOnLoad(go);
    }

    private void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        if (keyboard.f5Key.wasPressedThisFrame)
        {
            bool ok = SaveManager.Instance.Save(0);
            Debug.Log(ok ? "[SaveDebugHotkeys] Saved slot 0." : "[SaveDebugHotkeys] Save failed — see errors above.");
        }
        else if (keyboard.f9Key.wasPressedThisFrame)
        {
            if (!SaveManager.Instance.Load(0))
                Debug.Log("[SaveDebugHotkeys] Load failed — empty slot or errors above.");
        }
    }
}
#endif
