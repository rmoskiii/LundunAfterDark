using UnityEngine;

/// R2: a body knocked back by a strong gunshot (a shotgun blast up close): slides back along the shot over a moment,
/// easing to a stop, while the death animation plays. Stops at walls and cars, so a body shot against one drops where
/// it is. Added by Health when a person is shot dead; removes itself.
public class LundunKnockback : MonoBehaviour
{
    static int mask = -1;
    private Vector3 dir;
    private float speed, left, total;

    public static void Slide(GameObject body, Vector3 direction, float distance, float seconds)
    {
        if (body == null || distance <= 0.01f || seconds <= 0.01f) return;
        LundunKnockback k = body.AddComponent<LundunKnockback>();
        k.dir = direction.normalized;
        k.total = k.left = seconds;
        k.speed = distance * 3f / seconds;            // eases out (speed × (1 − t)²): covers the distance
    }

    void Update()
    {
        if (left <= 0f) { Destroy(this); return; }
        float t = Mathf.Clamp01(left / total);
        left -= Time.deltaTime;
        float step = speed * t * t * Time.deltaTime;
        if (mask == -1) mask = LayerMask.GetMask("Default", "Vehicle");
        // something solid within reach (and the body's half-width): stop here
        if (Physics.SphereCast(transform.position + Vector3.up * 0.9f, 0.25f, dir, out RaycastHit hit, step + 0.15f, mask, QueryTriggerInteraction.Ignore))
        { Destroy(this); return; }
        transform.position += dir * step;
    }
}
