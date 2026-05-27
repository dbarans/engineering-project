using System;
using UnityEngine;

public abstract class PlayerAttack : MonoBehaviour
{
    [Header("Settings")]
    [SerializeField] protected float chargeTimeRequired = 1.5f;
    [SerializeField] protected LayerMask enemyLayer;
    
    protected float chargeStartTime;
    protected bool isCharging = false;
    protected bool isReady = false;

    public virtual void StartCharging()
    {
        isCharging = true;
        chargeStartTime = Time.time;
        isReady = false;
        Debug.Log("loading");
    }

    public virtual void StopCharging()
    {
        isCharging = false;
        isReady = false;
        Debug.Log("reset");
    }

    public virtual void Fire()
    {
        if (isReady)
        {
            ExecuteAttack();
            isReady = false;
            isCharging = false;
        }
        else
        {
            Debug.Log("not ready");
        }
    }

    protected virtual void Update()
    {
        if (isCharging && !isReady)
        {
            if (Time.time - chargeStartTime >= chargeTimeRequired)
            {
                isReady = true;
            }
        }
    }

    protected abstract void ExecuteAttack();

    public float GetChargeProgress() => isReady ? 1f : (isCharging ? Mathf.Clamp01((Time.time - chargeStartTime) / chargeTimeRequired) : 0f);
}