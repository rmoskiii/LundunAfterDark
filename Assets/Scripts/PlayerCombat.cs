using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCombat : MonoBehaviour
{
    [SerializeField] private float damage = 25f;
    [SerializeField] private float range = 100f;
    [SerializeField] private float fireRate = 8f;
    [SerializeField] private float impactForce = 6f;
    [SerializeField] private LayerMask hitMask = ~0;

    [Header("Effects")]
    [SerializeField] private GameObject bloodImpactPrefab;
    [SerializeField] private GameObject surfaceImpactPrefab;
    [SerializeField] private HitMarkerUI hitMarker;

    private InputAction fireAction;
    private Transform cam;
    private float nextFireTime;

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

        // In touch mode, only the on-screen Fire button should shoot.
        // Ignore presses that come straight from the screen (mouse or finger),
        // unless we're in mouse-look mode with the cursor locked.
        InputDevice device = fireAction.activeControl?.device;
        if (device is Pointer && Cursor.lockState != CursorLockMode.Locked)
        {
            return false;
        }

        return true;
    }

    void Shoot()
    {
        if (!Physics.Raycast(cam.position, cam.forward, out RaycastHit hit,
                             range, hitMask, QueryTriggerInteraction.Ignore))
        {
            return;
        }

        Health health = hit.collider.GetComponentInParent<Health>();

        // Blood on things that can be hurt, dust on everything else
        GameObject impactPrefab = health != null ? bloodImpactPrefab : surfaceImpactPrefab;
        if (impactPrefab != null)
        {
            Instantiate(impactPrefab, hit.point, Quaternion.LookRotation(hit.normal));
        }

        if (health != null)
        {
            bool wasAlive = !health.IsDead;
            health.TakeDamage(damage, hit.point, cam.forward * impactForce);

            if (wasAlive && hitMarker != null)
            {
                hitMarker.Show(health.IsDead);
            }
        }
    }
}