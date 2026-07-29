using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Placeholder for the crafting table's "repair weapon" action. The button is shown by
/// <see cref="CraftingBoxUI"/> while a <see cref="CraftingTable"/> is in range; clicking
/// it currently does nothing but log, since the weapon repair system is not built yet.
///
/// This is the single insertion point for that future system: replace the body of
/// <see cref="OnClicked"/> when repair is implemented.
/// </summary>
[RequireComponent(typeof(Button))]
public class WeaponRepairButton : MonoBehaviour
{
    private void Awake()
    {
        GetComponent<Button>().onClick.AddListener(OnClicked);
    }

    private void OnClicked()
    {
        // TODO: hook up the weapon repair system here.
        Debug.Log("[WeaponRepair] Repair requested — the weapon repair system is not implemented yet.");
    }
}
