using TMPro;
using UnityEngine;

/// The double-decker's destination blinds (Lundun → 11 makes the bus with empty ones): each time a bus comes onto
/// the road it shows a route number and where it's going, front, back and side. Invented routes for Lundun's places.
public class LundunBusBlind : MonoBehaviour
{
    static readonly string[] Routes =
    {
        "36|PECKAM RYE", "12|KNIGHTSBURY CIRCUS", "159|BRIXTUN", "15|CANARY QUAY", "73|GILMORE GREEN",
        "2|MAYFIELD ST", "188|QUAY BRIDGE", "N29|HYDEMOOR PARK", "45|RYE LANE", "68|TOWER PLAZA",
    };

    private TextMeshPro[] routeText, destText;

    void Awake()
    {
        var route = new System.Collections.Generic.List<TextMeshPro>();
        var dest = new System.Collections.Generic.List<TextMeshPro>();
        foreach (TextMeshPro t in GetComponentsInChildren<TextMeshPro>(true))
        {
            if (t.name == "BlindRoute") route.Add(t);
            else if (t.name == "BlindDest") dest.Add(t);
        }
        routeText = route.ToArray(); destText = dest.ToArray();
    }

    void OnEnable() { Pick(); }

    /// A new route for this trip (the population pools buses: each time it's back on the road).
    public void Pick()
    {
        if (routeText == null) return;
        string[] r = Routes[Random.Range(0, Routes.Length)].Split('|');
        foreach (TextMeshPro t in routeText) if (t != null) t.text = r[0];
        foreach (TextMeshPro t in destText) if (t != null) t.text = r[1];
    }
}
