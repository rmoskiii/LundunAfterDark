using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerAnimation : MonoBehaviour
{
    [SerializeField] private float runAnimSpeed = 6f;
    [SerializeField] private float damping = 0.1f;
    [SerializeField] private float airborneDelay = 0.15f;

    private CharacterController controller;
    private Animator animator;
    private float lastGroundedTime;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
    }

    // LateUpdate runs after every Update, so the player has already moved this frame
    void LateUpdate()
    {
        if (animator == null) return;

        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        Vector3 local = transform.InverseTransformDirection(velocity);

        animator.SetFloat("Forward", local.z, damping, Time.deltaTime);
        animator.SetFloat("Strafe", local.x, damping, Time.deltaTime);

        // Only count as airborne after being off the ground for a moment,
        // so tiny flickers don't trigger the jump animation
        if (controller.isGrounded) lastGroundedTime = Time.time;
        bool grounded = Time.time - lastGroundedTime < airborneDelay;
        animator.SetBool("Grounded", grounded);

        float speed = velocity.magnitude;
        animator.SetFloat("AnimSpeed", Mathf.Max(1f, speed / runAnimSpeed));
    }
}