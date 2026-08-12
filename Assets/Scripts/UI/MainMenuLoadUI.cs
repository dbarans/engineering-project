using UnityEngine;

/// <summary>
/// Load-only slot list for the Main Menu — no Save tab, no ink cost, no pause/resume
/// (there is no running game to pause). SaveManager.Load handles the scene transition
/// and state restore itself; this only has to display slots and forward clicks.
/// </summary>
public class MainMenuLoadUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private SaveSlotView[] slotViews;

    private void Awake()
    {
        if (panelRoot != null) panelRoot.SetActive(false);

        if (slotViews != null)
        {
            foreach (var view in slotViews)
            {
                if (view != null) view.Clicked += OnSlotClicked;
            }
        }
    }

    public void Open()
    {
        if (panelRoot == null) return;
        panelRoot.SetActive(true);
        Refresh();
    }

    public void Close()
    {
        if (panelRoot != null) panelRoot.SetActive(false);
    }

    private void Refresh()
    {
        if (slotViews == null) return;

        var infos = SaveManager.Instance.GetSlotInfos();
        for (int i = 0; i < slotViews.Length; i++)
        {
            if (slotViews[i] == null) continue;
            var meta = i < infos.Length ? infos[i] : null;
            slotViews[i].SetInfo(i, meta, interactable: meta != null);
        }
    }

    private void OnSlotClicked(int slot)
    {
        SaveManager.Instance.Load(slot);
        // On success SaveManager loads the gameplay scene itself — this Main Menu
        // scene (and this UI with it) is about to be unloaded, nothing more to do here.
    }
}