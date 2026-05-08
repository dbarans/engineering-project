using System;
using UnityEngine;

public class RangedAttack : PlayerAttack
{
    [SerializeField] private GameObject arrowPrefab;
    [SerializeField] private Transform shootPoint;

    protected override void ExecuteAttack()
    {
        Debug.Log("shot");
    }
}
