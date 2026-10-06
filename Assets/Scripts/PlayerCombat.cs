using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCombat : MonoBehaviour
{
    [SerializeField] private float damage = 25f;
    [SerializeField] private float range = 100f;
    [SerializeField] private float fireRate = 8f;
    [SerializeField] private LayerMask hitMask = ~0;

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
        if (fireAction.IsPressed() && Time.time >= nextFireTime)
        {
            nextFireTime = Time.time + 1f / fireRate;
            Shoot();
        }
    }

    void Shoot()
    {
        Debug.DrawRay(cam.position, cam.forward * range, Color.red, 0.5f);

        if (Physics.Raycast(cam.position, cam.forward, out RaycastHit hit,
                            range, hitMask, QueryTriggerInteraction.Ignore))
        {
            SpawnHitMarker(hit.point);

            Health health = hit.collider.GetComponentInParent<Health>();
            if (health != null) health.TakeDamage(damage);
        }
    }

    void SpawnHitMarker(Vector3 position)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.transform.position = position;
        marker.transform.localScale = Vector3.one * 0.15f;
        Destroy(marker.GetComponent<Collider>());
        Destroy(marker, 1f);
    }
}