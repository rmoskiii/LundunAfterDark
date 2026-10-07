using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerAnimation : MonoBehaviour
{
    [SerializeField] private float runAnimSpeed = 6f;
    [SerializeField] private float damping = 0.1f;

    private CharacterController controller;
    private Animator animator;

    void Start()
    {
        controller = GetComponent<CharacterController>();
        animator = GetComponentInChildren<Animator>();
    }

    void Update()
    {
        if (animator == null) return;

        // How fast are we actually moving, relative to where we're facing?
        Vector3 velocity = controller.velocity;
        velocity.y = 0f;
        Vector3 local = transform.InverseTransformDirection(velocity);

        animator.SetFloat("Forward", local.z, damping, Time.deltaTime);
        animator.SetFloat("Strafe", local.x, damping, Time.deltaTime);
        animator.SetBool("Grounded", controller.isGrounded);

        // Faster than the run animation was made for? Speed the legs up
        // so the feet don't slide like ice skating.
        float speed = velocity.magnitude;
        animator.SetFloat("AnimSpeed", Mathf.Max(1f, speed / runAnimSpeed));
    }
}