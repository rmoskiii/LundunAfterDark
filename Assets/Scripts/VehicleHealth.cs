using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class VehicleHealth : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float crashDamageSpeed = 8f;       // softer bumps than this do nothing
    [SerializeField] private float crashDamageMultiplier = 3f;

    [Header("Explosion")]
    [SerializeField] private float fuseTime = 4f;               // seconds on fire before it blows
    [SerializeField] private float explosionRadius = 8f;
    [SerializeField] private float explosionDamage = 120f;
    [SerializeField] private float explosionPush = 12f;

    [Header("Effects")]
    [SerializeField] private Material fireMaterial;
    [SerializeField] private Material smokeMaterial;
    [SerializeField] private AudioClip explosionSound;

    public float Current { get; private set; }
    public bool IsBurning { get; private set; }
    public bool IsWrecked { get; private set; }

    private Rigidbody rb;
    private CarController car;
    private ParticleSystem smoke;
    private ParticleSystem fire;
    private float fuse;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        car = GetComponent<CarController>();
        Current = maxHealth;
    }

    void OnCollisionEnter(Collision collision)
    {
        float impact = collision.relativeVelocity.magnitude;
        if (impact > crashDamageSpeed)
            TakeDamage((impact - crashDamageSpeed) * crashDamageMultiplier);
    }

    public void TakeDamage(float amount)
    {
        if (IsWrecked || amount <= 0f) return;
        Current -= amount;

        // Badly damaged: engine starts smoking
        if (smoke == null && Current < maxHealth * 0.4f)
            smoke = MakeParticles("Smoke", smokeMaterial, new Color(0.25f, 0.25f, 0.25f, 0.6f), 1.2f, 2.5f, 2f, 12f);

        // Out of health: catches fire, then explodes after the fuse (time to jump out!)
        if (Current <= 0f && !IsBurning)
        {
            IsBurning = true;
            fuse = fuseTime;
            fire = MakeParticles("Fire", fireMaterial, new Color(1f, 0.55f, 0.15f, 1f), 1f, 0.7f, 2.5f, 30f);
        }
    }

    void Update()
    {
        if (!IsBurning || IsWrecked) return;
        fuse -= Time.deltaTime;
        if (fuse <= 0f) Explode();
    }

    void Explode()
    {
        IsWrecked = true;
        Vector3 centre = transform.position;
        bool playerInside = car != null && car.IsDriven;

        // Switch off driving, AI, sirens, lights: it's a wreck now
        foreach (MonoBehaviour mb in GetComponentsInChildren<MonoBehaviour>())
            if (mb != this) mb.enabled = false;
        foreach (AudioSource a in GetComponentsInChildren<AudioSource>()) a.Stop();
        foreach (Light l in GetComponentsInChildren<Light>()) l.enabled = false;

        // Burnt look
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r is ParticleSystemRenderer) continue;
            foreach (Material m in r.materials) m.color = m.color * 0.12f;
        }

        // Pop the wreck into the air
        rb.AddForce(Vector3.up * 7f, ForceMode.VelocityChange);
        rb.AddTorque(Random.insideUnitSphere * 2.5f, ForceMode.VelocityChange);

        // Push and damage everything nearby (chain reactions!)
        var pushed = new HashSet<Rigidbody> { rb };
        var damaged = new HashSet<VehicleHealth> { this };
        var people = new HashSet<Health>();
        foreach (Collider hit in Physics.OverlapSphere(centre, explosionRadius, ~0, QueryTriggerInteraction.Ignore))
        {
            Rigidbody other = hit.attachedRigidbody;
            if (other != null && !other.isKinematic && pushed.Add(other))
                other.AddExplosionForce(explosionPush, centre, explosionRadius, 1f, ForceMode.VelocityChange);

            VehicleHealth otherCar = hit.GetComponentInParent<VehicleHealth>();
            if (otherCar != null && damaged.Add(otherCar))
                otherCar.TakeDamage(explosionDamage * Falloff(otherCar.transform.position, centre));

            // Pedestrians: hurt and thrown away from the blast
            Health person = hit.GetComponentInParent<Health>();
            if (person != null && !person.IsDead && people.Add(person))
            {
                Vector3 pos = person.transform.position;
                float falloff = Falloff(pos, centre);
                Vector3 throwForce = ((pos - centre).normalized + Vector3.up) * 15f * falloff;
                person.TakeDamage(explosionDamage * falloff, pos + Vector3.up, throwForce);
            }
        }

        // The player: killed if inside, hurt if standing close
        PlayerHealth player = PlayerHealth.Instance;
        if (player != null)
        {
            if (playerInside) player.Kill();
            else if (player.OnFoot) player.TakeDamage(explosionDamage * Falloff(player.Body.position, centre));
        }

        // Boom: flash, fireball, smoke, sound
        StartCoroutine(Flash(centre));
        Burst(centre, fireMaterial, new Color(1f, 0.6f, 0.2f, 1f), 3.5f, 0.8f, 9f, 40);
        Burst(centre, smokeMaterial, new Color(0.15f, 0.15f, 0.15f, 0.8f), 4f, 3f, 4f, 25);
        if (explosionSound != null) PlaySound(centre);

        if (smoke == null) smoke = MakeParticles("Smoke", smokeMaterial, new Color(0.1f, 0.1f, 0.1f, 0.7f), 1.8f, 4f, 2.5f, 15f);
        if (fire != null) fire.Stop();

        // Blowing up a police car is a serious crime. Wrecks get cleared so fresh units can come.
        if (GetComponent<PoliceAI>() != null)
        {
            CrimeSystem.Report(CrimeType.DestroyPoliceCar, centre);
            Destroy(gameObject, 30f);
        }
    }

    float Falloff(Vector3 point, Vector3 centre)
    {
        return Mathf.Clamp01(1f - Vector3.Distance(point, centre) / explosionRadius);
    }

    // ---------- effects built in code so you don't have to make prefabs ----------

    ParticleSystem MakeParticles(string name, Material mat, Color colour, float size, float lifetime, float speed, float rate)
    {
        if (mat == null) return null;
        GameObject go = new GameObject(name);
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, 1f, 1.2f);   // roughly the bonnet
        go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);    // point up

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.startLifetime = lifetime;
        main.startSpeed = speed;
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size);
        main.startColor = colour;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        var emission = ps.emission;
        emission.rateOverTime = rate;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 12f;
        shape.radius = 0.3f;

        SetFadeAndGrow(ps, 2f);
        go.GetComponent<ParticleSystemRenderer>().material = mat;
        ps.Play();
        return ps;
    }

    void Burst(Vector3 at, Material mat, Color colour, float size, float lifetime, float speed, int count)
    {
        if (mat == null) return;
        GameObject go = new GameObject("ExplosionBurst");
        go.transform.position = at + Vector3.up;

        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = false;
        main.duration = 0.1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime * 0.6f, lifetime);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
        main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size);
        main.startColor = colour;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = -0.1f;

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.8f;

        var drag = ps.limitVelocityOverLifetime;
        drag.enabled = true;
        drag.drag = 3f;

        SetFadeAndGrow(ps, 1.8f);
        go.GetComponent<ParticleSystemRenderer>().material = mat;
        ps.Play();
        Destroy(go, lifetime + 0.5f);
    }

    void SetFadeAndGrow(ParticleSystem ps, float growTo)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        Gradient g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = g;

        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, growTo));
    }

    IEnumerator Flash(Vector3 at)
    {
        Light light = new GameObject("ExplosionLight").AddComponent<Light>();
        light.transform.position = at + Vector3.up * 2f;
        light.type = LightType.Point;
        light.color = new Color(1f, 0.6f, 0.25f);
        light.range = 25f;
        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            light.intensity = Mathf.Lerp(40f, 0f, t / 0.6f);
            yield return null;
        }
        Destroy(light.gameObject);
    }

    void PlaySound(Vector3 at)
    {
        AudioSource a = new GameObject("ExplosionSound").AddComponent<AudioSource>();
        a.transform.position = at;
        a.clip = explosionSound;
        a.spatialBlend = 0.7f;
        a.minDistance = 15f;
        a.maxDistance = 200f;
        a.Play();
        Destroy(a.gameObject, explosionSound.length + 0.2f);
    }
}
