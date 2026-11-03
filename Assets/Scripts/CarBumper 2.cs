using UnityEngine;

public class CarBumper : MonoBehaviour
{
    [SerializeField] private Rigidbody carBody;
    [SerializeField] private float minSpeed = 3f;          // slower than this: just a nudge
    [SerializeField] private float damagePerSpeed = 8f;    // 12.5 m/s (~28 mph) is a kill
    [SerializeField] private float launchMultiplier = 1.2f;
    [SerializeField] private GameObject bloodImpactPrefab;
    [SerializeField] private AudioClip hitSound;

    void OnTriggerEnter(Collider other)
    {
        Health health = other.GetComponentInParent<Health>();
        if (health == null) return;

        Vector3 velocity = carBody.linearVelocity;
        float speed = velocity.magnitude;
        if (speed < minSpeed) return;

        Vector3 hitPoint = other.ClosestPoint(transform.position);
        Vector3 launch = velocity * launchMultiplier + Vector3.up * speed * 0.4f;

                if (!health.IsDead)
        {
            health.TakeDamage(speed * damagePerSpeed, hitPoint, launch);

            // Only the player's driving counts as a crime (police hitting people doesn't)
            CarController driver = carBody.GetComponent<CarController>();
            if (driver != null && driver.IsDriven && health.GetComponent<NPCBrain>() != null)
                CrimeSystem.Report(health.IsDead ? CrimeType.KillPedestrian : CrimeType.HitAndRun, hitPoint);
        }
        else if (other.attachedRigidbody != null)
        {
            // Already a body on the floor: just knock it along
            other.attachedRigidbody.AddForce(launch, ForceMode.VelocityChange);
        }

        if (bloodImpactPrefab != null)
            Instantiate(bloodImpactPrefab, hitPoint + Vector3.up * 0.8f, Quaternion.LookRotation(velocity));

        if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, hitPoint, Mathf.Clamp01(speed / 15f));
    }
}