using System;
using System.Collections;
using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private Color flashColour = Color.red;
    [SerializeField] private float flashTime = 0.1f;
    [SerializeField] private float corpseLifetime = 6f;
    [Tooltip("Ticked: always topple with physics (props). Unticked: play a death animation unless hit hard.")]
    [SerializeField] private bool physicsDeath = true;
    [Tooltip("A killing blow at least this strong launches the body with physics (cars do this, bullets don't)")]
    [SerializeField] private float launchThreshold = 10f;

    public event Action<Vector3> Damaged;
    public event Action Died;

    private float currentHealth;
    private Renderer rend;
    private Color originalColour;

    public bool IsDead => currentHealth <= 0f;
    public bool DiedFromPhysics { get; private set; }

    void Awake()
    {
        currentHealth = maxHealth;
        rend = GetComponentInChildren<Renderer>();
        if (rend != null) originalColour = rend.material.color;
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 force)
    {
        if (IsDead) return;

        currentHealth -= amount;

        if (rend != null) StartCoroutine(Flash());
        Damaged?.Invoke(hitPoint);

        if (IsDead) Die(hitPoint, force);
    }

    IEnumerator Flash()
    {
        rend.material.color = flashColour;
        yield return new WaitForSeconds(flashTime);
        if (rend != null) rend.material.color = originalColour;
    }

    void Die(Vector3 hitPoint, Vector3 force)
    {
        DiedFromPhysics = physicsDeath || force.magnitude >= launchThreshold;
        Died?.Invoke();

        if (DiedFromPhysics)
        {
            Rigidbody rb = gameObject.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.AddForceAtPosition(force, hitPoint, ForceMode.Impulse);
        }

        Destroy(gameObject, corpseLifetime);
    }
}