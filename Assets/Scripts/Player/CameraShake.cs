using UnityEngine;

/// <summary>
/// Calculates a random shake offset vector based on duration and magnitude, 
/// exposing it to other components without directly modifying the transform's position.
/// </summary>
public class CameraShake : MonoBehaviour
{
    private float shakeDuration = 0f;
    private float shakeMagnitude = 0f;
    private float shakeDamping = 1f;

    public Vector3 CurrentShakeOffset { get; private set; } = Vector3.zero;

    public bool IsShaking => shakeDuration > 0f;

    public void TriggerShake(float duration, float magnitude, float forceMultiplier = 1f)
    {
        shakeDuration = duration;
        shakeMagnitude = magnitude * forceMultiplier;

        if (duration > 0f)
        {
            shakeDamping = shakeMagnitude / duration;
        }
    }

    private void LateUpdate()
    {
        if (shakeDuration > 0f)
        {
            Vector2 randomCircle = Random.insideUnitCircle * shakeMagnitude;
            CurrentShakeOffset = new Vector3(randomCircle.x, randomCircle.y, 0f);

            shakeDuration -= Time.deltaTime;
            shakeMagnitude -= shakeDamping * Time.deltaTime;

            if (shakeDuration <= 0f)
            {
                CurrentShakeOffset = Vector3.zero;
            }
        }
        else
        {
            CurrentShakeOffset = Vector3.Lerp(CurrentShakeOffset, Vector3.zero, Time.deltaTime * 15f);
        }
    }
}