using System;

[Serializable]
public class BarrelSaveData
{
    public bool isBroken;
    public float posX;
    public float posY;
    public float posZ;
    public int currentHealth;
    public ContainerSaveData containerData;
}