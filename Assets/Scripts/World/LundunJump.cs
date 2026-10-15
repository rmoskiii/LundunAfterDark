using UnityEngine;

/// Plays James's jump on the "LundunAir" animator layer (legs and body only, so his arms keep the rifle):
/// take-off (standing or running), falling, then a soft landing, a heavy one or a roll.
/// Added to James by Lundun → 14  James's Jump; reads PlayerController.
[RequireComponent(typeof(PlayerController))]
public class LundunJump : MonoBehaviour
{
    public const string LayerName = "LundunAir";

    [SerializeField] private float blendIn = 0.08f;
    [SerializeField] private float blendOut = 0.2f;
    [SerializeField] private float softLandingHold = 0.35f;
    [SerializeField] private float hardLandingHold = 0.6f;
    [SerializeField] private float rollHold = 0.9f;

    private PlayerController pc;
    private Animator animator;
    private int layer = -1;
    private float holdUntil;

    void Awake()
    {
        pc = GetComponent<PlayerController>();
        animator = GetComponentInChildren<Animator>();
        if (animator != null) layer = animator.GetLayerIndex(LayerName);
    }

    void OnEnable() { pc.Jumped += OnJumped; pc.Landed += OnLanded; }
    void OnDisable()
    {
        pc.Jumped -= OnJumped; pc.Landed -= OnLanded;
        if (layer >= 0 && animator != null) animator.SetLayerWeight(layer, 0f);
    }

    void OnJumped(bool running)
    {
        if (layer < 0) return;
        LundunAnim.Bool(animator, "AirRun", running);
        LundunAnim.Trigger(animator, "AirJump");
    }

    void OnLanded(int kind, float metres)
    {
        if (layer < 0) return;
        animator.SetInteger("AirLand", kind);
        LundunAnim.Trigger(animator, "AirLanded");
        LundunAnim.Bool(animator, "AirFalling", false);
        holdUntil = Time.time + (kind == 2 ? rollHold : kind == 1 ? hardLandingHold : softLandingHold);
    }

    void Update()
    {
        if (layer < 0) return;
        LundunAnim.Bool(animator, "AirFalling", pc.Airborne);
        LundunAnim.Float(animator, "AirVSpeed", pc.VerticalVelocity);
        bool active = pc.Airborne || Time.time < holdUntil;
        float w = animator.GetLayerWeight(layer);
        float target = active ? 1f : 0f;
        if (!Mathf.Approximately(w, target))
            animator.SetLayerWeight(layer, Mathf.MoveTowards(w, target, Time.deltaTime / (active ? blendIn : blendOut)));
    }
}
