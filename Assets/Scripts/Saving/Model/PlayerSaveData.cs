using System;

/// <summary>
/// Saved player state: vitals, position and inventory. Containers store item ids
/// resolved through <see cref="ItemDatabase"/>, never asset references.
/// </summary>
[Serializable]
public class PlayerSaveData
{
    /// <summary>World position as [x, y].</summary>
    public float[] position;

    /// <summary>Health is an int in PlayerHealthSystem.</summary>
    public int health;

    public float stamina;

    public ContainerSaveData hotbar;
    public ContainerSaveData backpack;

    /// <summary>HotbarUI.SelectedIndex — so the player holds the same weapon after load.</summary>
    public int selectedHotbarIndex;
}
