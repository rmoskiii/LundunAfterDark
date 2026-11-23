using UnityEngine;

/// G8e: the way in at the bottom of a Tube entrance's steps (Lundun → 42 puts one in every stairwell). When James walks
/// into it, it's as if he'd tapped TAKE THE TUBE at the top: down into the ticket hall.
[RequireComponent(typeof(BoxCollider))]
public class LundunTubeWayIn : MonoBehaviour
{
    [SerializeField] private string station = "";

    public void Set(string stationName) { station = stationName; }
    public string Station => station;

    void Awake() { GetComponent<BoxCollider>().isTrigger = true; }

    // G8e fix: the stairwells go 2–3 m below the pavement, under the "drowned" line (LundunWorldRuntime kills anyone
    // below y = -1, for the river). Down a stairwell isn't the river, so that check asks here first.
    static readonly System.Collections.Generic.List<LundunTubeWayIn> all = new System.Collections.Generic.List<LundunTubeWayIn>();
    void OnEnable() { if (!all.Contains(this)) all.Add(this); }
    void OnDisable() { all.Remove(this); }

    /// Is this spot inside one of the stairwells (the steps, the landing, or just above them)?
    public static bool InStairwell(Vector3 p)
    {
        for (int i = 0; i < all.Count; i++)
        {
            LundunTubeWayIn w = all[i];
            if (w == null) continue;
            Vector3 l = Quaternion.Inverse(w.transform.rotation) * (p - w.transform.position);
            if (Mathf.Abs(l.x) < 3f && Mathf.Abs(l.z) < 11f && l.y > -4f && l.y < 6f) return true;
        }
        return false;
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.Body == null || LundunTube.Instance == null) return;
        if (other.transform != ph.Body && !other.transform.IsChildOf(ph.Body)) return;
        LundunTube.Instance.WalkedDown(transform.position);
    }
}
