using TMPro;
using UnityEngine;

/// Keeps a contextual pill as wide as its label.
public class LundunPill : MonoBehaviour
{
    private TextMeshProUGUI label;
    private float padding;
    private string last;

    public void Init(TextMeshProUGUI l, float pad) { label = l; padding = pad; last = null; }

    void LateUpdate()
    {
        if (label == null || label.text == last) return;
        last = label.text;
        RectTransform rt = (RectTransform)transform;
        float w = label.GetPreferredValues(label.text, 2000f, rt.sizeDelta.y).x;
        rt.sizeDelta = new Vector2(Mathf.Max(rt.sizeDelta.y, padding + w), rt.sizeDelta.y);
    }
}
