using UnityEngine;

public class CarCrash : MonoBehaviour
{
    [SerializeField] private float minImpact = 4f;        // smaller bumps are ignored
    [SerializeField] private float maxCondition = 100f;
    [SerializeField] private float smokeBelow = 40f;      // start smoking under this
    [SerializeField] private AudioClip crashSound;
    [SerializeField] private ParticleSystem smoke;

    private float condition;
    private CameraFollow cameraFollow;
    private CarController car;

    public float Condition => condition;

    /// G8.0 (notify only, for the incident log): a counted knock (minImpact or more) to the car James is driving:
    /// the car, the impact (m/s) and where. Nothing listens back.
    public static event System.Action<CarController, float, Vector3> Crashed;

    void Start()
    {
        condition = maxCondition;
        cameraFollow = Camera.main.GetComponent<CameraFollow>();
        car = GetComponent<CarController>();
        if (smoke != null) smoke.Stop();
    }

    void OnCollisionEnter(Collision collision)
    {
        float impact = collision.relativeVelocity.magnitude;
        if (impact < minImpact) return;

        Vector3 point = collision.GetContact(0).point;
        if (car != null && car.IsDriven) { try { Crashed?.Invoke(car, impact, point); } catch (System.Exception e) { Debug.LogException(e); } }   // G8.0: notify only

        if (crashSound != null)
            AudioSource.PlayClipAtPoint(crashSound, point, Mathf.Clamp01(impact / 20f));

        // Only shake the screen if we're the one driving
        if (car != null && car.IsDriven && cameraFollow != null)
            cameraFollow.AddShake(Mathf.Clamp(impact * 0.015f, 0.05f, 0.4f));

        condition -= impact * 2f;
        if (condition < smokeBelow && smoke != null && !smoke.isPlaying)
            smoke.Play();
    }
}