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
    private PlayerController player;
    // G1: weapons (fists, pistol, SMG, carbine, shotgun)
    private LundunWeapons weapons;
    private AudioSource layerAudio;                    // the original recording under each gun's own sound (own source: pitch is per source)
    private bool triggerWasDown;
    private float tapBufferedUntil = -1f;              // -1 = no tap waiting (0 would count as a tap on the very first frame)
    private readonly Dictionary<Health, ShotHit> shotHits = new Dictionary<Health, ShotHit>();
    class ShotHit { public float damage; public bool head; public RaycastHit hit; }
    private Transform cam;
    private float nextFireTime;
    private float nextPunchTime;
    private readonly Collider[] nearby = new Collider[16];
    private readonly Queue<GameObject> decals = new Queue<GameObject>();

    void Start()
    {
        fireAction = InputSystem.actions.FindAction("Attack");
        player = GetComponent<PlayerController>();
        if (player == null) player = GetComponentInParent<PlayerController>();
        cam = Camera.main.transform;
        weapons = GetComponent<LundunWeapons>();
        if (weapons == null) weapons = gameObject.AddComponent<LundunWeapons>();
        if (gunAudio != null)
        {
            GameObject g = new GameObject("GunLayer");
            g.transform.SetParent(gunAudio.transform, false);
            layerAudio = g.AddComponent<AudioSource>();
            layerAudio.playOnAwake = false;
            layerAudio.outputAudioMixerGroup = gunAudio.outputAudioMixerGroup;
            layerAudio.spatialBlend = gunAudio.spatialBlend;
            layerAudio.volume = gunAudio.volume;
            layerAudio.minDistance = gunAudio.minDistance;
            layerAudio.maxDistance = gunAudio.maxDistance;
            layerAudio.rolloffMode = gunAudio.rolloffMode;
        }
    }

    void Update()
    {
        if (weapons != null) { WeaponUpdate(); return; }        // G1
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

    // ------------------------------------------------------------------ G1: weapons
    void WeaponUpdate()
    {
        bool wants = WantsToFire();
        bool manual = wants && fireAction.IsPressed();
        bool pressedNow = wants && !triggerWasDown;
        triggerWasDown = wants;
        bool buffered = tapBufferedUntil > 0f && Time.time <= tapBufferedUntil;
        if (!wants && !buffered) return;

        LundunWeapons.Weapon w = weapons.Current;

        // fists: the fire button throws punches (auto-fire only punches someone actually in reach)
        if (weapons.IsFists)
        {
            if (!wants || !weapons.Ready || Time.time < nextPunchTime) return;
            Health target = FindPunchTarget();
            if (target == null && !manual) return;
            nextPunchTime = Time.time + 1f / punchRate;
            if (target != null) Punch(target);
            else Whiff();
            return;
        }

        // someone right in front of James: fists, not bullets (as before)
        if (wants)
        {
            Health close = FindPunchTarget();
            if (close != null)
            {
                if (Time.time >= nextPunchTime) { nextPunchTime = Time.time + 1f / punchRate; Punch(close); }
                return;
            }
        }

        // semi-auto and pump: one shot per tap (a tap during the cooldown is remembered for a moment)
        bool semi = !w.automatic;
        if (!weapons.Ready || Time.time < nextFireTime)
        {
            if (semi && pressedNow) tapBufferedUntil = Time.time + 0.15f;
            return;
        }
        bool autoFiring = wants && !manual;                     // A3 auto-fire: semi weapons fire at their tap rate
        if (semi && !(pressedNow || buffered || autoFiring)) return;
        tapBufferedUntil = -1f;
        if (!weapons.TakeRound()) return;                      // empty: reloading (or a dry click)
        nextFireTime = Time.time + 1f / Mathf.Max(0.1f, w.fireRate);
        ShootWeapon(w);
        weapons.AfterShot(w);
    }

    /// A punch at thin air (fists, nobody in reach).
    void Whiff()
    {
        LundunPlayerReact.Act("Punch", 0.7f);
        if (LundunWeaponRig.Instance != null) LundunWeaponRig.Instance.Suspend(0.75f);   // G3: free his arms for the punch
        if (cameraFollow != null) cameraFollow.AddShake(shakeAmount * 0.3f);
    }

    void ShootWeapon(LundunWeapons.Weapon w)
    {
        GunshotFired?.Invoke(transform.position);
        CrimeSystem.Report(CrimeType.Gunfire, transform.position);
        PlayWeaponFeedback(w);

        // aim assist: a near-miss on a threat still lands (A1), a bit more forgiving for spray / shotgun
        Vector3 centre = LundunAimAssist.Instance != null ? LundunAimAssist.Instance.ShotDirection(cam.position, cam.forward, w.assist) : cam.forward;
        LundunAimAssist.NotifyShot();
        weapons.NotifyShot();

        float spread = weapons.SpreadNow;
        int pellets = Mathf.Max(1, w.pellets);
        shotHits.Clear();
        int impacts = 0;
        for (int i = 0; i < pellets; i++)
        {
            float cone = pellets > 1 ? w.pelletCone : spread;
            Vector3 dir = Scatter(centre, cone);
            Vector3 end = cam.position + dir * w.range;
            if (Physics.Raycast(cam.position, dir, out RaycastHit hit, w.range, hitMask, QueryTriggerInteraction.Ignore))
            {
                end = hit.point;
                Health health = hit.collider.GetComponentInParent<Health>();
                LundunBreakable.HitAt(hit.collider, hit.point, dir);                 // E5: shop displays, chiller glass, doors
                // G3: bullet holes and dust on walls / ground, sparks off vehicles
                if (health == null && impacts < 4) LundunCombatFX.Impact(hit, hit.collider.GetComponentInParent<VehicleHealth>() != null || hit.collider.GetComponentInParent<LundunHeli>() != null);
                if (health == null || impacts < 3)
                {
                    GameObject fx = health != null ? bloodImpactPrefab : surfaceImpactPrefab;
                    if (fx != null && impacts < 4) { Instantiate(fx, hit.point, Quaternion.LookRotation(hit.normal)); impacts++; }
                }
                VehicleHealth vehicle = hit.collider.GetComponentInParent<VehicleHealth>();
                if (vehicle != null) vehicle.TakeDamage(weapons.DamageAt(hit.distance) * vehicleDamageMultiplier);
                if (health != null && !health.IsDead)
                {
                    bool head = IsHeadshot(health, hit.point);
                    float dmg = weapons.DamageAt(hit.distance) * (head ? w.headshot : 1f);
                    if (!shotHits.TryGetValue(health, out ShotHit sh)) { sh = new ShotHit { hit = hit }; shotHits[health] = sh; }
                    sh.damage += dmg;
                    sh.head |= head;
                }
            }
            if (pellets == 1 || i % 2 == 0) SpawnTracer(end, w.tracerScale);
        }

        // each person hit takes the whole shot at once (one crime report, one hit marker)
        foreach (KeyValuePair<Health, ShotHit> kv in shotHits) ApplyShot(kv.Key, kv.Value, centre, w);
    }

    void ApplyShot(Health health, ShotHit sh, Vector3 dir, LundunWeapons.Weapon w)
    {
        if (health == null || health.IsDead) return;
        float push = w.kind == LundunWeapons.Kind.Shotgun ? 2.2f : 1f;     // the shotgun knocks people back
        health.TakeDamage(sh.damage, sh.hit.point, dir * impactForce * push);

        if (health.GetComponent<NPCBrain>() != null)
            CrimeSystem.Report(health.IsDead ? CrimeType.KillPedestrian : CrimeType.AssaultPedestrian, sh.hit.point);

        if (hitMarker != null)
        {
            if (sh.head) hitMarker.ShowHeadshot(health.IsDead);
            else hitMarker.Show(health.IsDead);
        }
        if (sh.head || health.IsDead) LundunHaptics.Light();
        SpawnBloodDecals(sh.hit);
    }

    /// Head = the top of a person (about 1.45 m and up from their feet).
    static bool IsHeadshot(Health h, Vector3 point)
    {
        if (h.GetComponent<NPCBrain>() == null && h.GetComponent<PoliceOfficer>() == null) return false;
        return point.y - h.transform.position.y >= 1.45f;
    }

    /// A direction somewhere inside a cone (degrees across) round the centre, bunched towards the middle.
    static Vector3 Scatter(Vector3 centre, float coneDegrees)
    {
        if (coneDegrees <= 0.001f) return centre;
        Vector2 r = Random.insideUnitCircle;
        r *= Mathf.Lerp(0.6f, 1f, Random.value);
        float half = coneDegrees * 0.5f;
        Quaternion q = Quaternion.LookRotation(centre);
        return q * Quaternion.Euler(-r.y * half, r.x * half, 0f) * Vector3.forward;
    }

    void PlayWeaponFeedback(LundunWeapons.Weapon w)
    {
        // G3: James's new guns: flash and smoke at the real muzzle, the gun kicks, brass flies out
        LundunWeaponRig rig = LundunWeaponRig.Instance;
        if (rig != null && rig.Muzzle != null)
        {
            int puffs = w.kind == LundunWeapons.Kind.Shotgun ? 4 : w.kind == LundunWeapons.Kind.Carbine ? 2 : 1;
            LundunCombatFX.Muzzle(rig.Muzzle, w.flashSize, puffs);
            rig.Fired(w);
            if (w.kind != LundunWeapons.Kind.Shotgun) LundunCombatFX.Eject(rig.EjectPoint, false);   // the shotgun ejects on the pump
        }
        else if (muzzleFlash != null) muzzleFlash.Flash(w.flashSize);
        if (gunAudio != null)
        {
            // each gun's own sound (generated, or your own clip if set), with a little of the original recording underneath
            AudioClip own = w.customShot != null ? w.customShot : LundunGunAudio.Shot(w.kind);
            gunAudio.pitch = 1f;
            if (own != null) gunAudio.PlayOneShot(own, w.volume);
            AudioSource layer = layerAudio != null ? layerAudio : gunAudio;
            if (gunshotClip != null && w.recordedLayer > 0f && layer != gunAudio)
            {
                layer.pitch = w.pitch;
                layer.PlayOneShot(gunshotClip, w.volume * w.recordedLayer);
            }
        }
        if (cameraFollow != null)
        {
            Vector2 kick = weapons.Recoil();
            float steady = LundunAim.Aiming ? 0.7f : 1f;             // steadier aimed in
            cameraFollow.AddRecoil(kick.x * steady, kick.y * steady);
            cameraFollow.AddShake(w.shake);
        }
    }

    bool WantsToFire()
    {
        if (player != null && player.IsStunned) return false;              // W1: tased
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
        if (LundunWeaponRig.Instance != null) LundunWeaponRig.Instance.Suspend(0.75f);   // G3: free his arms for the punch

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
        LundunBreakable.HitAt(hit.collider, hit.point, cam.forward);              // E5

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

    void SpawnTracer(Vector3 end) { SpawnTracer(end, 1f); }

    void SpawnTracer(Vector3 end, float widthScale)
    {
        // G3: from the muzzle of the gun James is actually holding
        Transform from = LundunWeaponRig.Instance != null && LundunWeaponRig.Instance.Muzzle != null ? LundunWeaponRig.Instance.Muzzle : muzzle;
        if (tracerMaterial == null || from == null) return;

        GameObject tracer = new GameObject("Tracer");
        LineRenderer line = tracer.AddComponent<LineRenderer>();
        line.material = tracerMaterial;
        line.positionCount = 2;
        line.SetPosition(0, from.position);
        line.SetPosition(1, end);
        line.startWidth = tracerWidth * widthScale;
        line.endWidth = tracerWidth * 0.5f * widthScale;
        line.shadowCastingMode = ShadowCastingMode.Off;

        Destroy(tracer, tracerTime);
    }
}