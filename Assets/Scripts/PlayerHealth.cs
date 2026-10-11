using System;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealth : MonoBehaviour
{
    public static PlayerHealth Instance { get; private set; }
    public event Action Died;

    [SerializeField] private PlayerController playerController;   // drag James in

    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float regenDelay = 5f;        // seconds without a hit before healing starts
    [SerializeField] private float regenPerSecond = 20f;

    [Header("UI")]
    [SerializeField] private Image healthBar;              // Image Type: Filled
    [SerializeField] private Image damageFlash;            // the red vignette

    public float Current { get; private set; }
    public bool IsDead { get; private set; }
    public Transform Body => playerController.transform;
    public bool OnFoot => playerController.gameObject.activeInHierarchy;

    private float lastHitTime = -999f;
    private float flash;

    void Awake()
    {
        Instance = this;
        Current = maxHealth;
    }

    void Update()
    {
        // CoD-style: stay out of trouble for a few seconds and you heal
        if (!IsDead && Current < maxHealth && Time.time > lastHitTime + regenDelay)
            Current = Mathf.Min(maxHealth, Current + regenPerSecond * Time.deltaTime);

        flash = Mathf.MoveTowards(flash, 0f, Time.unscaledDeltaTime * 1.5f);
        float lowHealthPulse = (!IsDead && Current < maxHealth * 0.3f)
            ? 0.35f + Mathf.Sin(Time.unscaledTime * 6f) * 0.1f : 0f;

        if (damageFlash != null)
        {
            Color c = damageFlash.color;
            c.a = Mathf.Max(flash, lowHealthPulse);
            damageFlash.color = c;
        }
        if (healthBar != null) healthBar.fillAmount = Current / maxHealth;
    }

    public void TakeDamage(float amount)
    {
        // Locked controls (BUSTED / WASTED cutscenes) = can't be hurt
        if (IsDead || amount <= 0f || !playerController.enabled) return;

        Current -= amount;
        lastHitTime = Time.time;
        flash = 0.6f;

        if (Current <= 0f) Kill();
    }

    public void Kill()
    {
        if (IsDead) return;
        Current = 0f;
        IsDead = true;
        Died?.Invoke();
    }

    public void ResetHealth()
    {
        Current = maxHealth;
        IsDead = false;
        flash = 0f;
    }
}
