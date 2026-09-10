using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines a single loot drop configuration with probability and quantity bounds.
/// </summary>
[Serializable]
public class BarrelDropItem
{
    public ItemData itemData;
    [Range(0f, 100f)]
    public float dropChance = 100f;
    [Min(1)]
    public int minQuantity = 1;
    [Min(1)]
    public int maxQuantity = 1;
}

/// <summary>
/// Manages destructible barrels, damage handling, loot instantiation, and state persistence.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D), typeof(SaveableEntity))]
public class Barrel : MonoBehaviour, ISaveableComponent
{
    [Header("Health & Damage")]
    [SerializeField] private int maxHealth = 30;
    [SerializeField] private GameObject destructionEffectPrefab;
    [SerializeField] private AudioClip hitSound;
    [SerializeField] private AudioClip destroySound;

    [Header("Broken Barrel Loot Container")]
    [Tooltip("Broken barrel prefab containing the EnemyCorpse component.")]
    [SerializeField] private GameObject brokenBarrelPrefab;

    [Header("Loot Drop on Destruction (Randomized)")]
    [SerializeField] private List<BarrelDropItem> dropsOnDestroy = new List<BarrelDropItem>();

    private int _currentHealth;
    private Rigidbody2D _rb;
    private Collider2D _col;
    private SpriteRenderer _sr;

    private bool _isBroken = false;
    private EnemyCorpse _spawnedBrokenCorpse;

    public string TypeTag => "barrel";

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _col = GetComponent<Collider2D>();
        _sr = GetComponent<SpriteRenderer>();
        _currentHealth = maxHealth;
    }

    /// <summary>
    /// Applies incoming damage to the barrel and triggers destruction when health drops to zero.
    /// </summary>
    /// <param name="damage">The amount of damage dealt.</param>
    public void TakeDamage(int damage)
    {
        if (_currentHealth <= 0 || _isBroken) return;

        _currentHealth -= damage;

        if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, transform.position);

        if (_currentHealth <= 0)
        {
            Break();
        }
    }

    /// <summary>
    /// Destroys the barrel, instantiates destruction effects, spawns the broken barrel container with loot, and hides the original visual state.
    /// </summary>
    public void Break()
    {
        if (_isBroken) return;
        _isBroken = true;
        _currentHealth = 0;

        var draggable = GetComponent<DraggableBarrel>();
        if (draggable != null)
        {
            draggable.enabled = false;
        }

        Vector3 spawnPosition = transform.position;

        if (destroySound != null)
            AudioSource.PlayClipAtPoint(destroySound, spawnPosition);

        if (destructionEffectPrefab != null)
            Instantiate(destructionEffectPrefab, spawnPosition, Quaternion.identity);

        if (brokenBarrelPrefab != null)
        {
            GameObject brokenObj = Instantiate(brokenBarrelPrefab, spawnPosition, Quaternion.identity);
            _spawnedBrokenCorpse = brokenObj.GetComponent<EnemyCorpse>();

            if (_spawnedBrokenCorpse != null && dropsOnDestroy != null && dropsOnDestroy.Count > 0)
            {
                var container = _spawnedBrokenCorpse.Container;
                foreach (var drop in dropsOnDestroy)
                {
                    if (drop.itemData == null) continue;

                    float roll = UnityEngine.Random.Range(0f, 100f);
                    if (roll <= drop.dropChance)
                    {
                        int count = UnityEngine.Random.Range(drop.minQuantity, drop.maxQuantity + 1);
                        if (count > 0)
                        {
                            container.TryAddItem(drop.itemData, count);
                        }
                    }
                }
            }
        }
        else
        {
            Debug.LogWarning("[Barrel] Missing brokenBarrelPrefab reference in the Inspector.");
        }
        ApplyBrokenVisualState();
    }

    /// <summary>
    /// Disables visual renderers, colliders, child objects, and stops physics movement.
    /// </summary>
    private void ApplyBrokenVisualState()
    {
        // Read while the collider is still enabled: a disabled one reports empty bounds, and
        // this is the footprint the navigation grid has to be told about.
        Bounds footprint = _col != null
            ? _col.bounds
            : new Bounds(transform.position, Vector3.one);

        if (_col != null) _col.enabled = false;
        if (_sr != null) _sr.enabled = false;
        if (_rb != null)
        {
            _rb.linearVelocity = Vector2.zero;
            _rb.bodyType = RigidbodyType2D.Kinematic;
        }

        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(false);
        }

        // Last, once nothing of this barrel is solid any more: the grid re-runs the same
        // overlap query the build used, and would still find whatever is left enabled.
        //
        // Without it the cell stays blocked for the rest of the run — enemies keep walking
        // around a gap that is no longer there, and a barrel that had been the only way
        // through leaves everything beyond it permanently unreachable to them.
        PathfindingGrid.Active?.RefreshArea(footprint);
    }

    /// <summary>
    /// Serializes the current barrel state, transform position, and loot container data into a JSON payload.
    /// </summary>
    /// <returns>Serialized JSON string representation of the barrel state.</returns>
    public string CapturePayload()
    {
        var data = new BarrelSaveData
        {
            isBroken = _isBroken,
            posX = transform.position.x,
            posY = transform.position.y,
            posZ = transform.position.z,
            currentHealth = _currentHealth
        };

        if (_isBroken && _spawnedBrokenCorpse != null && _spawnedBrokenCorpse.Container != null)
        {
            data.containerData = ContainerSaveUtility.Capture(_spawnedBrokenCorpse.Container);
        }

        return Newtonsoft.Json.JsonConvert.SerializeObject(data);
    }

    /// <summary>
    /// Restores the barrel state, position, and spawned broken container loot from a serialized JSON payload.
    /// </summary>
    /// <param name="payload">Serialized JSON payload containing saved barrel state.</param>
    public void RestorePayload(string payload)
    {
        if (string.IsNullOrEmpty(payload)) return;

        var data = Newtonsoft.Json.JsonConvert.DeserializeObject<BarrelSaveData>(payload);
        if (data == null) return;

        _isBroken = data.isBroken;
        _currentHealth = data.currentHealth;
        transform.position = new Vector3(data.posX, data.posY, data.posZ);

        if (_isBroken)
        {
            ApplyBrokenVisualState();

            if (brokenBarrelPrefab != null && _spawnedBrokenCorpse == null)
            {
                GameObject brokenObj = Instantiate(brokenBarrelPrefab, transform.position, Quaternion.identity);
                _spawnedBrokenCorpse = brokenObj.GetComponent<EnemyCorpse>();

                if (_spawnedBrokenCorpse != null && data.containerData != null)
                {
                    ContainerSaveUtility.Restore(_spawnedBrokenCorpse.Container, data.containerData, _spawnedBrokenCorpse);
                    _spawnedBrokenCorpse.CheckIfEmptyAndDisable();
                }
            }
        }
    }
}