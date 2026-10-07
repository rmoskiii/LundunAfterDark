using UnityEngine;

public class GunHold : MonoBehaviour
{
    [SerializeField] private Transform hand;        // mixamorig9:RightHand
    [SerializeField] private Transform aimSource;   // Player
    [Tooltip("Where the gun sits relative to the hand: X right, Y up, Z forward")]
    [SerializeField] private Vector3 positionOffset = new Vector3(0f, 0f, 0.1f);
    [Tooltip("Corrects which way the model points. Try 90° steps until the barrel faces forward")]
    [SerializeField] private Vector3 rotationOffset = Vector3.zero;

    // LateUpdate runs after the animation has posed the hand this frame
    void LateUpdate()
    {
        if (hand == null || aimSource == null) return;

        transform.position = hand.position + aimSource.rotation * positionOffset;
        transform.rotation = Quaternion.LookRotation(aimSource.forward)
                           * Quaternion.Euler(rotationOffset);
    }
}