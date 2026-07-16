using System;
using UnityEngine;

public class RangedAttack : PlayerAttack
{
    [Header("Ranged Settings")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform shootPoint;
    
    [Header("Accuracy Settings")]
    [SerializeField] private float maxSpreadAngle = 40f;
    [SerializeField] private float minSpreadAngle = 2f;
    [SerializeField] private float lineLength = 5f;

    [Header("Visuals")]
    [SerializeField] private LineRenderer leftLine;
    [SerializeField] private LineRenderer rightLine;

    private float currentSpreadAngle;

    public override void StartCharging()
    {
        currentSpreadAngle = maxSpreadAngle;
        base.StartCharging();
    }

    protected override void Update()
    {
        base.Update();

        if (isCharging)
        {
            ToggleLines(true);

            float progress = GetChargeProgress();
            currentSpreadAngle = Mathf.Lerp(maxSpreadAngle, minSpreadAngle, progress);

            UpdateAimLines();
        }
        else
        {
            ToggleLines(false);
        }
    }

    public override void StopCharging()
    {
        base.StopCharging();
        ToggleLines(false);
    }

    protected override void ExecuteAttack()
    {
        float randomOffset = UnityEngine.Random.Range(-currentSpreadAngle / 2f, currentSpreadAngle / 2f);
        Quaternion shootRotation = shootPoint.rotation * Quaternion.Euler(0, 0, randomOffset);
        Instantiate(projectilePrefab, shootPoint.position, shootRotation);
    }

    private void UpdateAimLines()
    {
        if (leftLine == null || rightLine == null || shootPoint == null) return;

        leftLine.SetPosition(0, shootPoint.position);
        rightLine.SetPosition(0, shootPoint.position);

        Vector3 leftDir = Quaternion.Euler(0, 0, currentSpreadAngle / 2f) * shootPoint.right;
        Vector3 rightDir = Quaternion.Euler(0, 0, -currentSpreadAngle / 2f) * shootPoint.right;

        leftLine.SetPosition(1, shootPoint.position + leftDir * lineLength);
        rightLine.SetPosition(1, shootPoint.position + rightDir * lineLength);
    }

    private void ToggleLines(bool state)
    {
        if (leftLine != null) leftLine.enabled = state;
        if (rightLine != null) rightLine.enabled = state;
    }
}