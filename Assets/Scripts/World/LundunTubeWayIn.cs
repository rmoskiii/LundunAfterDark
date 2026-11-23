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

    void OnTriggerEnter(Collider other)
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.Body == null || LundunTube.Instance == null) return;
        if (other.transform != ph.Body && !other.transform.IsChildOf(ph.Body)) return;
        LundunTube.Instance.WalkedDown(transform.position);
    }
}
