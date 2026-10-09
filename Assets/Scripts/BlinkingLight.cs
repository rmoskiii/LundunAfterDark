using UnityEngine;

public class BlinkingLight : MonoBehaviour
{
    [SerializeField] private float interval = 0.6f;
    [SerializeField] private Color onColour = new Color(1f, 0.54f, 0f) * 4f;

    private Material mat;

    void Start()
    {
        mat = GetComponent<Renderer>().material;
        // Start each beacon at a random point so they don't all blink in sync
        InvokeRepeating(nameof(Toggle), Random.Range(0f, interval), interval);
    }

    void Toggle()
    {
        bool isOn = mat.GetColor("_EmissionColor").maxColorComponent > 0.1f;
        mat.SetColor("_EmissionColor", isOn ? Color.black : onColour);
    }
}