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
    [Header("R2: shot dead")]
    [Tooltip("A gunshot kill at least this strong (a close shotgun blast) knocks the body back before it falls")]
    [SerializeField] private float knockbackFrom = 8f;
    [Tooltip("How far (m) the strongest blasts knock them back")]
    [SerializeField] private float knockbackMax = 1.5f;

    public event Action<Vector3> Damaged;
    public event Action Died;

    private float currentHealth;
    private Renderer rend;
    private Color originalColour;

    public bool IsDead => currentHealth <= 0f;
    public bool DiedFromPhysics { get; private set; }
    /// R2: the killing blow was a gunshot (never launches the body; a strong one knocks it back instead).
    public bool KilledByGunfire { get; private set; }

    void Awake()
    {
        currentHealth = maxHealth;
        rend = GetComponentInChildren<Renderer>();
        if (rend != null) originalColour = rend.material.color;
    }

    /// W3: the look changed after spawning (e.g. armed officers' dark kit): remember the new colour for the hit flash.
    public void RefreshColour()
    {
        if (rend != null) originalColour = rend.material.color;
    }

    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 force)
    {
        TakeDamage(amount, hitPoint, force, false);
    }

    /// R2: gunfire = true for bullets and shotgun pellets: a kill drops the body (knocked back if the blast was strong)
    /// instead of launching it with physics, which sent shotgun victims spinning. Cars still launch bodies.
    public void TakeDamage(float amount, Vector3 hitPoint, Vector3 force, bool gunfire)
    {
        if (IsDead) return;

        currentHealth -= amount;

        if (rend != null) StartCoroutine(Flash());
        Damaged?.Invoke(hitPoint);

        if (IsDead) Die(hitPoint, force, gunfire);
    }

    IEnumerator Flash()
    {
        rend.material.color = flashColour;
        yield return new WaitForSeconds(flashTime);
        if (rend != null) rend.material.color = originalColour;
    }

    void Die(Vector3 hitPoint, Vector3 force, bool gunfire)
    {
        KilledByGunfire = gunfire;
        DiedFromPhysics = physicsDeath || (!gunfire && force.magnitude >= launchThreshold);
        Died?.Invoke();

        // R2: a strong gunshot kill (shotgun up close) knocks a person back along the shot as they fall
        if (!DiedFromPhysics && gunfire && force.magnitude >= knockbackFrom)
        {
            Vector3 flat = new Vector3(force.x, 0f, force.z);
            float k = Mathf.InverseLerp(knockbackFrom, knockbackFrom * 1.65f, force.magnitude);
            if (flat.sqrMagnitude > 0.0001f) LundunKnockback.Slide(gameObject, flat.normalized, Mathf.Lerp(0.5f, knockbackMax, k), 0.35f);
        }

        if (DiedFromPhysics)
        {
            Rigidbody rb = gameObject.AddComponent<Rigidbody>();
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.AddForceAtPosition(force, hitPoint, ForceMode.Impulse);
        }

        Destroy(gameObject, corpseLifetime);
    }
}