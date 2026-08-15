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

    /// <summary>
    /// True while the prepare button is held. Visual drivers read this to show the aiming
    /// pose (see <see cref="PlayerAnimationDriver"/>).
    /// </summary>
    public bool IsCharging => isCharging;

    /// <summary>
    /// Raised when a shot/swing actually goes off (after <see cref="CanFire"/> passed).
    /// Visual drivers subscribe to play the one-shot attack animation, mirroring
    /// <see cref="EnemyMeleeAttack.AttackStarted"/> on the enemy side.
    /// </summary>
    public event Action Fired;

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
        {
            OnFireBlocked();
            return false;
        }

        ExecuteAttack();
        Fired?.Invoke();
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
    /// Called when <see cref="CanFire"/> refused a charged shot. Exists so a weapon can
    /// give the player some feedback for the refusal — an out-of-ammo click — instead of
    /// the trigger doing nothing at all, which reads as dropped input. Does nothing by
    /// default: a weapon with no failure condition never reaches it.
    /// </summary>
    protected virtual void OnFireBlocked() { }

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