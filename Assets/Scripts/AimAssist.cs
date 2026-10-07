using UnityEngine;

public class AimAssist : MonoBehaviour
{
    [SerializeField] private float range = 60f;
    [Tooltip("How close to the crosshair a target must be (degrees)")]
    [SerializeField] private float assistAngle = 6f;
    [Tooltip("How hard it pulls. Higher = stickier")]
    [SerializeField] private float strength = 3f;
    [SerializeField] private LayerMask lineOfSightMask = ~0;

    private CameraFollow cameraFollow;
    private Transform cam;

    void Start()
    {
        cam = Camera.main.transform;
        cameraFollow = cam.GetComponent<CameraFollow>();
    }

    void Update()
    {
        if (cameraFollow == null) return;

        Vector3? target = FindTargetNearCrosshair();
        if (target == null) return;

        // The camera angles that would point straight at the target
        Vector3 dir = (target.Value - cam.position).normalized;
        float targetYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        float targetPitch = -Mathf.Asin(dir.y) * Mathf.Rad2Deg;

        float yawDiff = Mathf.DeltaAngle(cameraFollow.Yaw, targetYaw);
        float pitchDiff = Mathf.DeltaAngle(cameraFollow.Pitch, targetPitch);

        float amount = Mathf.Clamp01(strength * Time.deltaTime);
        cameraFollow.Nudge(yawDiff * amount, pitchDiff * amount);
    }

    Vector3? FindTargetNearCrosshair()
    {
        Vector3? best = null;
        float bestAngle = assistAngle;

        foreach (Health health in FindObjectsByType<Health>(FindObjectsSortMode.None))
        {
            if (health.IsDead) continue;

            Collider col = health.GetComponentInChildren<Collider>();
            Vector3 point = col != null ? col.bounds.center : health.transform.position;
            Vector3 toTarget = point - cam.position;
            if (toTarget.magnitude > range) continue;

            float angle = Vector3.Angle(cam.forward, toTarget);
            if (angle > bestAngle) continue;

            // No help through walls
            if (Physics.Linecast(cam.position, point, out RaycastHit hit,
                                 lineOfSightMask, QueryTriggerInteraction.Ignore)
                && hit.collider.GetComponentInParent<Health>() != health)
            {
                continue;
            }

            bestAngle = angle;
            best = point;
        }

        return best;
    }
}