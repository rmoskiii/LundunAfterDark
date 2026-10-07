using System;
using System.Collections;
using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private Color flashColour = Color.red;
    [SerializeField] private float flashTime = 0.1f;
    [SerializeField] private float corpseLifetime = 6f;
    [Tooltip("Ticked: topple over with physics (blocks, props). Unticked: another script handles death (NPCs).")]
    [SerializeField] private bool physicsDeath = true;

    public event Action<Vector3> Damaged;   // passes where the hit landed
    public event Action Died;

    private float currentHealth;
    private Renderer rend;
    private Color originalColour;

    public bool IsDead => currentHealth <= 0f;

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
        Died?.Invoke();

        if (physicsDeath)
        {
            Rigidbody rb = gameObject.AddComponent<Rigidbody>();
            rb.AddForceAtPosition(force, hitPoint, ForceMode.Impulse);
        }

        Destroy(gameObject, corpseLifetime);
    }
}