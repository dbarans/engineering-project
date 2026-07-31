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

    public virtual bool Fire()
    {
        if (!isCharging)
            return false;

        if (!CanFire())
            return false;

        ExecuteAttack();
        RestartCharge();
        return true;
    }

    /// <summary>
    /// Last check before a charged shot is released. Base weapons can always fire;
    /// subclasses override to add requirements such as available ammo. Returning
    /// false aborts the shot without consuming the charge or attack stamina.
    /// </summary>
    protected virtual bool CanFire() => true;

    /// <summary>
    /// Resets charge progress after a shot. Charging continues from the start
    /// as long as the prepare button is still held, so aiming is not interrupted.
    /// </summary>
    protected void RestartCharge()
    {
        isReady = false;
        if (isCharging)
        {
            chargeStartTime = Time.time;
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

    protected virtual void TryDamageTarget(Collider2D hitCollider, float damage)
    {
        if (hitCollider == null) return;

        SimpleDoor door = hitCollider.GetComponent<SimpleDoor>();
        if (door != null)
        {
            door.TakeDamage(damage);
        }
    }

    public float GetChargeProgress() => isReady ? 1f : (isCharging ? Mathf.Clamp01((Time.time - chargeStartTime) / chargeTimeRequired) : 0f);
}