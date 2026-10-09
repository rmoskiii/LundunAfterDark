using UnityEngine;
using UnityEngine.EventSystems;

public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public bool IsHeld { get; private set; }

    public void OnPointerDown(PointerEventData eventData) => IsHeld = true;
    public void OnPointerUp(PointerEventData eventData) => IsHeld = false;

    // If the button gets hidden while held, don't leave it "stuck down"
    void OnDisable() => IsHeld = false;
}