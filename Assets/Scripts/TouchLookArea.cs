using UnityEngine;
using UnityEngine.EventSystems;

public class TouchLookArea : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private Vector2 accumulatedDelta;
    private bool isDragging;
    private int activePointerId;

    /// A1: when someone last steered the camera (the free-look area or the fire button). Aim assist only tracks while you're aiming.
    public static float LastDragTime { get; private set; } = -99f;

    public Vector2 ConsumeDelta()
    {
        Vector2 delta = accumulatedDelta;
        accumulatedDelta = Vector2.zero;
        return delta;
    }

    /// A1: other controls can steer the camera too (the fire button: hold to shoot, drag to aim).
    public void AddDelta(Vector2 delta)
    {
        accumulatedDelta += delta;
        if (delta.sqrMagnitude > 0f) LastDragTime = Time.time;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (isDragging) return;
        isDragging = true;
        activePointerId = eventData.pointerId;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!isDragging || eventData.pointerId != activePointerId) return;
        AddDelta(eventData.delta);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId == activePointerId) isDragging = false;
    }
}
