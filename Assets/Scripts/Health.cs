using System.Collections;
using UnityEngine;

public class Health : MonoBehaviour
{
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private Color flashColour = Color.red;
    [SerializeField] private float flashTime = 0.1f;

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

    public void TakeDamage(float amount)
    {
        if (IsDead) return;

        currentHealth -= amount;
        Debug.Log($"{name} took {amount} damage, {currentHealth} left");

        if (rend != null) StartCoroutine(Flash());
        if (IsDead) Die();
    }

    IEnumerator Flash()
    {
        rend.material.color = flashColour;
        yield return new WaitForSeconds(flashTime);
        if (rend != null) rend.material.color = originalColour;
    }

    void Die()
    {
        Destroy(gameObject);
    }
}