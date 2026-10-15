using UnityEngine;

/// James's extra moves: reeling from a punch, and throwing one. Lundun → 12 Build People adds a "LundunReactions" layer to his animator;
/// this switches that layer on for the length of the reaction, then fades it back out so his normal
/// running and aiming carry on underneath.
public class LundunPlayerReact : MonoBehaviour
{
    public const string LayerName = "LundunReactions";
    const float FadeOut = 0.25f;

    private Animator animator;
    private int layer = -1;
    private float holdUntil;

    /// Rock James back from a punch (only on foot, only if his animator has the reaction layer).
    public static void Punched()
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.IsDead || !ph.OnFoot) return;
        Animator a = ph.Body.GetComponentInChildren<Animator>();
        if (a == null) return;
        LundunPlayerReact r = a.GetComponent<LundunPlayerReact>();
        if (r == null) r = a.gameObject.AddComponent<LundunPlayerReact>();
        r.Play("Punched", 1.0f);
    }

    /// James throws a move from the reactions layer (e.g. "Punch"), for hold seconds.
    public static void Act(string trigger, float hold)
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.IsDead || !ph.OnFoot) return;
        Animator a = ph.Body.GetComponentInChildren<Animator>();
        if (a == null) return;
        LundunPlayerReact r = a.GetComponent<LundunPlayerReact>();
        if (r == null) r = a.gameObject.AddComponent<LundunPlayerReact>();
        r.Play(trigger, hold);
    }

    void Play(string trigger, float hold)
    {
        if (animator == null) { animator = GetComponent<Animator>(); layer = animator.GetLayerIndex(LayerName); }
        if (layer < 0) return;
        LundunAnim.Trigger(animator, trigger);
        animator.SetLayerWeight(layer, 1f);
        holdUntil = Time.time + hold;
    }

    void Update()
    {
        if (layer < 0 || animator == null || Time.time < holdUntil) return;
        float w = animator.GetLayerWeight(layer);
        if (w > 0f) animator.SetLayerWeight(layer, Mathf.MoveTowards(w, 0f, Time.deltaTime / FadeOut));
    }
}
