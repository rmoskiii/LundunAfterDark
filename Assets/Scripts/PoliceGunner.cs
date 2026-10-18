// W1: some tuning fields below are kept (so nothing in the prefabs/scene is lost) but the wanted level now drives
// them through PoliceTiers. This hides the "assigned but never used" warnings for those.
#pragma warning disable 0414
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(PoliceAI))]
public class PoliceGunner : MonoBehaviour
{
    [Header("When")]
    [SerializeField] private int starsToShoot = 3;
    [SerializeField] private float range = 35f;

    [Header("Bursts")]
    [SerializeField] private int shotsPerBurst = 3;
    [SerializeField] private float timeBetweenShots = 0.12f;
    [SerializeField] private float timeBetweenBursts = 1.5f;

    [Header("Damage")]
    [SerializeField] private float damageOnFoot = 10f;
    [SerializeField] private float damageToCar = 4f;
    [SerializeField] private float closeHitChance = 0.7f;   // chance each bullet hits, point blank
    [SerializeField] private float farHitChance = 0.2f;     // chance at max range

    [Header("Effects")]
    [SerializeField] private AudioClip gunshot;
    [SerializeField] private Material tracerMaterial;

    private PoliceAI ai;
    private AudioSource audioSource;
    private LineRenderer tracer;
    private float nextBurst;

    void Start()
    {
        ai = GetComponent<PoliceAI>();

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;
        audioSource.minDistance = 8f;
        audioSource.maxDistance = 120f;

        if (tracerMaterial != null)
        {
            tracer = new GameObject("PoliceTracer").AddComponent<LineRenderer>();
            tracer.transform.SetParent(transform, false);
            tracer.material = tracerMaterial;
            tracer.positionCount = 2;
            tracer.startWidth = 0.04f;
            tracer.endWidth = 0.02f;
            tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            tracer.enabled = false;
        }

        nextBurst = Time.time + 1f;   // a moment's grace when they first arrive
    }

    void Update()
    {
        if (Time.time < nextBurst || !CanShoot()) return;
        nextBurst = Time.time + timeBetweenBursts + shotsPerBurst * timeBetweenShots;
        StartCoroutine(Burst());
    }

    bool CanShoot()
    {
        WantedSystem wanted = WantedSystem.Instance;
        if (!ai.Armed) return false;                                   // W1: only armed response units shoot
        if (wanted.Stars < starsToShoot || !ai.enabled || !ai.CanSeeTarget) return false;
        if (ai.OfficersOut || VehicleSystem.Locked) return false;   // officers on foot do the shooting
        if (PlayerHealth.Instance == null || PlayerHealth.Instance.IsDead) return false;
        return Vector3.Distance(transform.position, wanted.PlayerTarget.position) < range;
    }

    IEnumerator Burst()
    {
        for (int i = 0; i < shotsPerBurst; i++)
        {
            if (!CanShoot()) yield break;
            FireShot();
            yield return new WaitForSeconds(timeBetweenShots);
        }
    }

    void FireShot()
    {
        Transform target = WantedSystem.Instance.PlayerTarget;
        Vector3 from = transform.position + Vector3.up * 2f;     // leaning out above the roof
        Vector3 aim = PoliceAim.Centre(target);
        float distance = Vector3.Distance(from, aim);

        // W1: accuracy and damage come from the wanted level; shooting from a moving car is a bit worse,
        // and a fast-moving target is harder to hit
        PoliceTiers.Tier tier = PoliceTiers.For(WantedSystem.Instance.Stars);
        float chance = Mathf.Lerp(tier.hitChanceNear, tier.hitChanceFar, distance / range) * 0.85f;
        chance -= TargetSpeed(target) * 0.02f;
        chance = Mathf.Clamp(chance, 0.03f, 0.9f);

        // Hit or miss is rolled first; a hit lands unless a building is in the way (see PoliceAim)
        Vector3 end = PoliceAim.Fire(transform, from, target, chance, range, tier.damage * 1.2f, damageToCar);

        if (gunshot != null)
        {
            audioSource.pitch = Random.Range(0.85f, 1f);
            audioSource.PlayOneShot(gunshot, 0.8f);
        }
        if (tracer != null) StartCoroutine(ShowTracer(from, end));
    }

    float TargetSpeed(Transform target)
    {
        CarController car = target.GetComponent<CarController>();
        if (car != null) return Mathf.Abs(car.Speed);
        CharacterController cc = target.GetComponent<CharacterController>();
        if (cc == null) return 0f;
        Vector3 v = cc.velocity;
        v.y = 0f;
        return v.magnitude;
    }

    IEnumerator ShowTracer(Vector3 from, Vector3 to)
    {
        tracer.SetPosition(0, from);
        tracer.SetPosition(1, to);
        tracer.enabled = true;
        yield return new WaitForSeconds(0.04f);
        tracer.enabled = false;
    }
}
