using UnityEngine;
using UnityEngine.EventSystems;

/// Three quick taps (used on the clock to show / hide the performance numbers).
public class LundunTripleTap : MonoBehaviour, IPointerClickHandler
{
    public event System.Action Tapped;
    private int count;
    private float last;

    public void OnPointerClick(PointerEventData e)
    {
        count = Time.unscaledTime - last < 0.45f ? count + 1 : 1;
        last = Time.unscaledTime;
        if (count >= 3) { count = 0; if (Tapped != null) Tapped(); }
    }
}
