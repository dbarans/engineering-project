using System;
using System.Collections.Generic;

[Serializable]
public class CorpseItemSaveData
{
    public string itemId;
    public int quantity;
}

[Serializable]
public class EnemyCorpseSaveState
{
    public List<CorpseItemSaveData> items = new List<CorpseItemSaveData>();
}