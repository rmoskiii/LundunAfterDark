using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// Touch handling for the full map: one finger pans, two fingers pinch-zoom,
/// mouse wheel zooms (editor), a tap drops a waypoint. Sits on the map viewport.
public class LundunMapInput : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IPointerClickHandler, IScrollHandler
{
    [SerializeField] private LundunFullMap map;
    private readonly Dictionary<int, Vector2> fingers = new Dictionary<int, Vector2>();
    private bool pinched;

    public void OnPointerDown(PointerEventData e)
    {
        fingers[e.pointerId] = e.position;
        if (fingers.Count == 1) pinched = false;
    }

    public void OnPointerUp(PointerEventData e) { fingers.Remove(e.pointerId); }

    public void OnDrag(PointerEventData e)
    {
        if (map == null) return;
        if (fingers.Count >= 2)
        {
            pinched = true;
            List<int> ids = new List<int>(fingers.Keys);
            int other = ids[0] == e.pointerId ? ids[1] : ids[0];
            Vector2 o = fingers[other];
            Vector2 before = fingers.ContainsKey(e.pointerId) ? fingers[e.pointerId] : e.position - e.delta;
            float d0 = Vector2.Distance(before, o), d1 = Vector2.Distance(e.position, o);
            if (d0 > 1f) map.ZoomAt((e.position + o) * 0.5f, d1 / d0);
        }
        else if (!pinched) map.Pan(e.delta);
        fingers[e.pointerId] = e.position;
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (map == null || e.dragging || pinched) return;
        map.Tap(e.position);
    }

    public void OnScroll(PointerEventData e)
    {
        if (map != null && Mathf.Abs(e.scrollDelta.y) > 0.01f) map.ZoomAt(e.position, e.scrollDelta.y > 0 ? 1.15f : 1f / 1.15f);
    }
}
