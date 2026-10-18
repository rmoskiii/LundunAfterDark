using UnityEngine;
using UnityEngine.EventSystems;

/// A1: the fire button doubles as an aim pad. Hold it to shoot (its On-Screen Button still does that),
/// and slide your thumb while holding to steer the aim, so you never have to lift off to correct.
/// The free-look area on the right still works as before. Added automatically by LundunAimAssist.
public class LundunFireAim : MonoBehaviour, IPointerDownHandler, IInitializePotentialDragHandler, IDragHandler, IPointerUpHandler
{
    [Tooltip("Steering from the fire button, relative to the free-look area")]
    [SerializeField] private float dragScale = 1f;

    private TouchLookArea look;
    private int pointer = int.MinValue;

    public void Init(TouchLookArea area) { look = area; }

    public void OnPointerDown(PointerEventData e) { if (pointer == int.MinValue) pointer = e.pointerId; }

    // no dead zone before steering starts: small corrections count straight away
    public void OnInitializePotentialDrag(PointerEventData e) { e.useDragThreshold = false; }

    public void OnDrag(PointerEventData e)
    {
        if (look == null || e.pointerId != pointer) return;
        look.AddDelta(e.delta * dragScale);
    }

    public void OnPointerUp(PointerEventData e) { if (e.pointerId == pointer) pointer = int.MinValue; }

    void OnDisable() { pointer = int.MinValue; }
}
