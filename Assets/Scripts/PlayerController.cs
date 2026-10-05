using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 5f;

    private InputAction moveAction;

    void Start()
    {
        moveAction = InputSystem.actions.FindAction("Move");
    }

    void Update()
    {
        Vector2 input = moveAction.ReadValue<Vector2>();
        Vector3 direction = new Vector3(input.x, 0f, input.y);
        transform.Translate(direction * moveSpeed * Time.deltaTime, Space.World);
    }
}