using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PlayerCombat : MonoBehaviour
{
    // Anyone can listen for gunshots (NPCs use it to panic)
    public static event System.Action<Vector3> GunshotFired;

    [Header("Gun")]
    [SerializeField] private float damage = 25f;
    [SerializeField] private float range = 100f;
    [SerializeField] private float fireRate = 8f;
    [SerializeField] private float impactForce = 6f;
    [SerializeField] private float vehicleDamageMultiplier = 0.25f;   // cars are tougher than people
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Punch (Attack with someone in arm's reach punches instead of shooting)")]
    [SerializeField] private float punchReach = 1.8f;           // metres from James
    [SerializeField] private float punchDamage = 20f;           // 5 punches floors someone
    [SerializeField] private float punchRate = 1.6f;            // punches per second when held
    [Range(0f, 1f)] [SerializeField] private float punchFightBack = 0.3f;   // they square up to James afterwards
    [SerializeField] private AudioClip punchClip;               // optional thud

    [Header("Feel")]
    [SerializeField] private float recoilUp = 0.6f;
    [SerializeField] private float recoilSideways = 0.25f;
    [SerializeField] private float shakeAmount = 0.05f;
    [SerializeField] private float tracerWidth = 0.03f;
    [SerializeField] private float tracerTime = 0.04f;

    [Header("References")]
    [SerializeField] private Transform muzzle;
    [SerializeField] private MuzzleFlash muzzleFlash;
    [SerializeField] private AudioSource gunAudio;
    [SerializeField] private AudioClip gunshotClip;
    [SerializeField] private Material tracerMaterial;
    [SerializeField] private CameraFollow cameraFollow;

    [Header("Effects")]
    [SerializeField] private GameObject bloodImpactPrefab;
    [SerializeField] private GameObject surfaceImpactPrefab;
    [SerializeField] private HitMarkerUI hitMarker;
    [SerializeField] private GameObject bloodDecalPrefab;
    [SerializeField] private int maxDecals = 30;

    private InputAction fireAction;
    private Transform cam;
    private float nextFireTime;
    private float nextPunchTime;
    private readonly Collider[] nearby = new Collider[16];
    private readonly Queue<GameObject> decals = new Queue<GameObject>();

    void Start()
    {
        fireAction = InputSystem.actions.FindAction("Attack");
        cam = Camera.main.transform;
    }

    void Update()
    {
        if (!WantsToFire()) return;

        // someone right in front of James: fists, not bullets
        Health close = FindPunchTarget();
        if (close != null)
        {
            if (Time.time >= nextPunchTime) { nextPunchTime = Time.time + 1f / punchRate; Punch(close); }
            return;
        }

        if (Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + 1f / fireRate;
            Shoot();
        }
    }

    bool WantsToFire()
    {
        // A3: FIRE = AUTO shoots by itself while the crosshair is red on a threat (the fire button still works too)
        if (!fireAction.IsPressed())
            return LundunSettings.AutoFire && Time.timeScale > 0f && LundunAimAssist.Instance != null
                   && LundunAimAssist.Instance.AutoFireReady;

        InputDevice device = fireAction.activeControl?.device;
        if (device is Pointer && Cursor.lockState != CursorLockMode.Locked)
        {
            return false;
        }

        return true;
    }

    // ------------------------------------------------------------------ punching
    Health FindPunchTarget()
    {
        Vector3 me = transform.position;
        Vector3 look = cam.forward; look.y = 0f;
        if (look.sqrMagnitude < 0.01f) look = transform.forward;
        look.Normalize();

        int n = Physics.OverlapSphereNonAlloc(me + Vector3.up, punchReach, nearby, ~0, QueryTriggerInteraction.Ignore);
        Health best = null; float bestD = float.MaxValue;
        for (int i = 0; i < n; i++)
        {
            Health h = nearby[i].GetComponentInParent<Health>();
            if (h == null || h.IsDead || h.transform.IsChildOf(transform)) continue;
            if (h.GetComponent<NPCBrain>() == null && h.GetComponent<PoliceOfficer>() == null) continue;   // people only, not bins
            Vector3 to = h.transform.position - me; to.y = 0f;
            float d = to.magnitude;
            if (d > punchReach || d < 0.01f) continue;
            if (Vector3.Dot(to / d, look) < 0.35f) continue;          // roughly where you're aiming
            if (d < bestD) { bestD = d; best = h; }
        }
        return best;
    }

    void Punch(Health target)
    {
        Vector3 to = target.transform.position - transform.position; to.y = 0f;
        Vector3 dir = to.sqrMagnitude > 0.001f ? to.normalized : transform.forward;
        transform.rotation = Quaternion.LookRotation(dir);          // square up to them
        LundunPlayerReact.Act("Punch", 0.7f);

        if (gunAudio != null && punchClip != null) gunAudio.PlayOneShot(punchClip);
        if (cameraFollow != null) cameraFollow.AddShake(shakeAmount * 0.6f);

        bool isOfficer = target.GetComponent<PoliceOfficer>() != null;
        NPCBrain brain = target.GetComponent<NPCBrain>();
        GameObject who = target.gameObject;
        Vector3 from = transform.position;

        // pedestrians reel (facing James) before the damage lands, so they flinch with the punch, not the gunshot flinch
        if (brain != null && !isOfficer)
        {
            float faceYaw = Mathf.Atan2(-dir.x, -dir.z) * Mathf.Rad2Deg;
            bool fightBack = Random.value < punchFightBack;
            LundunStagger.Play(who, "Punched", 1.0f, true, faceYaw, () =>
            {
                if (who == null) return;
                if (fightBack && who.GetComponent<LundunAngryDriver>() == null) who.AddComponent<LundunAngryDriver>().Begin(false);
                else { NPCBrain b = who.GetComponent<NPCBrain>(); if (b != null) b.Panic(from); }
            });
        }

        bool wasAlive = !target.IsDead;
        Vector3 hitPoint = target.transform.position + Vector3.up * 1.5f;
        target.TakeDamage(punchDamage, hitPoint, dir * 2f);
        if (!wasAlive) return;

        if (isOfficer) CrimeSystem.Report(target.IsDead ? CrimeType.KillOfficer : CrimeType.AssaultOfficer, hitPoint);
        else if (brain != null) CrimeSystem.Report(target.IsDead ? CrimeType.KillPedestrian : CrimeType.AssaultPedestrian, hitPoint);
        if (hitMarker != null) hitMarker.Show(target.IsDead);
    }

    void Shoot()
    {
        GunshotFired?.Invoke(transform.position);
        CrimeSystem.Report(CrimeType.Gunfire, transform.position);
        PlayGunFeedback();

        // A1: a near-miss on a threat (cop chasing you, someone attacking you) still lands; civilians never
        Vector3 dir = LundunAimAssist.Instance != null ? LundunAimAssist.Instance.ShotDirection(cam.position, cam.forward) : cam.forward;
        LundunAimAssist.NotifyShot();
        Vector3 endPoint = cam.position + dir * range;

        if (Physics.Raycast(cam.position, dir, out RaycastHit hit,
                            range, hitMask, QueryTriggerInteraction.Ignore))
        {
            endPoint = hit.point;
            HandleHit(hit);
        }

        SpawnTracer(endPoint);
    }

    void PlayGunFeedback()
    {
        if (muzzleFlash != null) muzzleFlash.Flash();

        if (gunAudio != null && gunshotClip != null)
        {
            gunAudio.pitch = Random.Range(0.92f, 1.08f);
            gunAudio.PlayOneShot(gunshotClip);
        }

        if (cameraFollow != null)
        {
            float steady = LundunAim.Aiming ? 0.7f : 1f;             // A2: steadier aimed in
            cameraFollow.AddRecoil(recoilUp * steady, Random.Range(-recoilSideways, recoilSideways) * steady);
            cameraFollow.AddShake(shakeAmount);
        }
    }

    void HandleHit(RaycastHit hit)
    {
        Health health = hit.collider.GetComponentInParent<Health>();

        GameObject impactPrefab = health != null ? bloodImpactPrefab : surfaceImpactPrefab;
        if (impactPrefab != null)
        {
            Instantiate(impactPrefab, hit.point, Quaternion.LookRotation(hit.normal));
        }

        // NEW: shooting cars damages them (enough and they catch fire, then explode)
        VehicleHealth vehicle = hit.collider.GetComponentInParent<VehicleHealth>();   // NEW
        if (vehicle != null)                                                         // NEW
            vehicle.TakeDamage(damage * vehicleDamageMultiplier);                    // NEW

        if (health == null) return;

        bool wasAlive = !health.IsDead;
        health.TakeDamage(damage, hit.point, cam.forward * impactForce);

        if (wasAlive && health.GetComponent<NPCBrain>() != null)
            CrimeSystem.Report(health.IsDead ? CrimeType.KillPedestrian : CrimeType.AssaultPedestrian, hit.point);

        if (wasAlive && hitMarker != null)
        {
            hitMarker.Show(health.IsDead);
        }

        SpawnBloodDecals(hit);
    }

    void SpawnBloodDecals(RaycastHit hit)
    {
        if (bloodDecalPrefab == null) return;

        // Spray onto whatever is behind the target (the ray carries on through it)
        if (Physics.Raycast(hit.point + cam.forward * 0.05f, cam.forward, out RaycastHit behind,
                            4f, hitMask, QueryTriggerInteraction.Ignore))
        {
            PlaceDecal(behind);
        }

        // And a pool on the floor beneath
        if (Physics.Raycast(hit.point + hit.normal * 0.1f, Vector3.down, out RaycastHit floor,
                            3f, hitMask, QueryTriggerInteraction.Ignore))
        {
            PlaceDecal(floor);
        }
    }

    void PlaceDecal(RaycastHit surface)
    {
        // Don't paint on things that move or die (they'd leave floating blood behind)
        if (surface.collider.GetComponentInParent<Health>() != null) return;

        Quaternion rotation = Quaternion.LookRotation(-surface.normal)
                            * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));

        GameObject decal = Instantiate(bloodDecalPrefab,
                                       surface.point + surface.normal * 0.25f, rotation);

        DecalProjector projector = decal.GetComponent<DecalProjector>();
        if (projector != null)
        {
            float size = Random.Range(0.6f, 1.3f);
            projector.size = new Vector3(size, size, 0.5f);
            projector.pivot = new Vector3(0f, 0f, 0.25f);
        }

        // Keep only the most recent splats, for phone performance
        decals.Enqueue(decal);
        while (decals.Count > maxDecals)
        {
            GameObject oldest = decals.Dequeue();
            if (oldest != null) Destroy(oldest);
        }
    }

    void SpawnTracer(Vector3 end)
    {
        if (tracerMaterial == null || muzzle == null) return;

        GameObject tracer = new GameObject("Tracer");
        LineRenderer line = tracer.AddComponent<LineRenderer>();
        line.material = tracerMaterial;
        line.positionCount = 2;
        line.SetPosition(0, muzzle.position);
        line.SetPosition(1, end);
        line.startWidth = tracerWidth;
        line.endWidth = tracerWidth * 0.5f;
        line.shadowCastingMode = ShadowCastingMode.Off;

        Destroy(tracer, tracerTime);
    }
}