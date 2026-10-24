using System.Collections.Generic;
using UnityEngine;

/// E5: one of the few things on a supermarket's shelves you can take (everything else is scenery).
///   pick-up   for missions: walk up, PICK UP, and you're carrying it (LundunShopItem.Carrying, ItemTaken)
///   steal     walk up, STEAL: if a shopper, a member of staff or the police sees you, it's a crime (Shoplifting:
///             a witness calls it in, or the police are straight on to you), the same as any other crime
/// Taken items come back on the shelf after ten minutes, when you're not looking. Built by Lundun → 28.
public class LundunShopItem : MonoBehaviour
{
    [SerializeField] private string itemId = "";
    [SerializeField] private string displayName = "";
    [SerializeField] private bool steal;
    [SerializeField] private Vector3 standAt;                 // where you stand to reach it (on the floor in the aisle)
    [SerializeField] private float comeBackAfter = 600f;

    public string Id { get { return itemId; } }
    public string DisplayName { get { return displayName; } }
    public bool IsSteal { get { return steal; } }
    public bool Available { get { return !taken; } }
    public Vector3 StandAt { get { return standAt; } }

    /// Everything you've picked up or stolen so far (ids), for missions.
    public static readonly HashSet<string> Carrying = new HashSet<string>();
    /// Fired when you take something: its id, and whether you stole it.
    public static event System.Action<string, bool> ItemTaken;

    static readonly List<LundunShopItem> active = new List<LundunShopItem>();
    public static IReadOnlyList<LundunShopItem> Active { get { return active; } }

    private bool taken;
    private float takenAt;
    private Renderer[] rends;

    public void SetData(string id, string name, bool isSteal, Vector3 stand) { itemId = id; displayName = name; steal = isSteal; standAt = stand; }

    void Awake() { rends = GetComponentsInChildren<Renderer>(true); }

    void OnEnable()
    {
        active.Add(this);
        LundunShopPrompt.Ensure();
        if (taken && Time.time - takenAt > comeBackAfter)
        {
            Camera cam = Camera.main;
            if (cam == null || Vector3.Distance(cam.transform.position, transform.position) > 30f) { taken = false; Show(true); }
        }
    }

    void OnDisable() { active.Remove(this); }

    void Show(bool on) { if (rends != null) foreach (Renderer r in rends) if (r != null) r.enabled = on; }

    /// Take it (the prompt's button calls this).
    public void Take()
    {
        if (taken) return;
        taken = true; takenAt = Time.time;
        Show(false);
        Carrying.Add(itemId);
        Vector3 at = PlayerHealth.Instance != null && PlayerHealth.Instance.Body != null ? PlayerHealth.Instance.Body.position : transform.position;
        if (steal) CrimeSystem.Report(CrimeType.Shoplifting, at);
        if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast((steal ? "Stole: " : "Picked up: ") + displayName);
        LundunHaptics.Light();
        ItemTaken?.Invoke(itemId, steal);
    }
}
