using UnityEngine;
using UnityEngine.EventSystems;

/// Buttons sink a little while pressed, with a light haptic tap.
public class LundunPressFX : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private float pressedScale = 0.9f;
    private float target = 1f, scale = 1f;

    public void OnPointerDown(PointerEventData e) { target = pressedScale; LundunHaptics.Light(); }
    public void OnPointerUp(PointerEventData e) { target = 1f; }
    void OnDisable() { target = scale = 1f; transform.localScale = Vector3.one; }

    void Update()
    {
        if (Mathf.Approximately(scale, target)) return;
        scale = Mathf.MoveTowards(scale, target, Time.unscaledDeltaTime * 2.5f);
        transform.localScale = new Vector3(scale, scale, 1f);
    }
}
