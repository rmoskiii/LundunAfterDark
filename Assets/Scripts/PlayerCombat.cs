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
    [SerializeField] private LayerMask hitMask = ~0;

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
    private readonly Queue<GameObject> decals = new Queue<GameObject>();

    void Start()
    {
        fireAction = InputSystem.actions.FindAction("Attack");
        cam = Camera.main.transform;
    }

    void Update()
    {
        if (WantsToFire() && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + 1f / fireRate;
            Shoot();
        }
    }

    bool WantsToFire()
    {
        if (!fireAction.IsPressed()) return false;

        InputDevice device = fireAction.activeControl?.device;
        if (device is Pointer && Cursor.lockState != CursorLockMode.Locked)
        {
            return false;
        }

        return true;
    }

    void Shoot()
    {
        GunshotFired?.Invoke(transform.position);
        PlayGunFeedback();

        Vector3 endPoint = cam.position + cam.forward * range;

        if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit,
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
            cameraFollow.AddRecoil(recoilUp, Random.Range(-recoilSideways, recoilSideways));
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

        if (health == null) return;

        bool wasAlive = !health.IsDead;
        health.TakeDamage(damage, hit.point, cam.forward * impactForce);

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