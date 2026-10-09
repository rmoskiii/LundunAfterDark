using UnityEngine;

public class Knockable : MonoBehaviour
{
    [SerializeField] private float breakSpeed = 5f;    // how hard you need to hit it
    [SerializeField] private float mass = 150f;
    [SerializeField] private AudioClip hitSound;

    private bool knocked;

    void OnCollisionEnter(Collision collision)
    {
        if (knocked) return;
        if (collision.rigidbody == null) return;                      // only moving things (cars) knock it over
        if (collision.relativeVelocity.magnitude < breakSpeed) return;

        knocked = true;

        Rigidbody rb = gameObject.AddComponent<Rigidbody>();
        rb.mass = mass;

        // Shove it the way the car was going, a bit above the impact point so it topples
        Vector3 push = collision.rigidbody.linearVelocity * mass * 0.6f;
        Vector3 point = collision.GetContact(0).point + Vector3.up * 1.5f;
        rb.AddForceAtPosition(push, point, ForceMode.Impulse);

        if (hitSound != null)
            AudioSource.PlayClipAtPoint(hitSound, point, 0.7f);
    }
}