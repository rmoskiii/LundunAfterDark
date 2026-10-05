using UnityEngine;
using UnityEngine.EventSystems;

public class TouchLookArea : MonoBehaviour,
    IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    private Vector2 accumulatedDelta;
    private bool isDragging;
    private int activePointerId;

    public Vector2 ConsumeDelta()
    {
        Vector2 delta = accumulatedDelta;
        accumulatedDelta = Vector2.zero;
        return delta;
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
        accumulatedDelta += eventData.delta;
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.pointerId == activePointerId) isDragging = false;
    }
}