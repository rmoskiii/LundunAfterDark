using UnityEngine;

/// G2: a weapon / ammo pick-up lying on the ground: it turns slowly with a soft glow; walk over it to collect.
/// Armed police drop their carbine when they go down. In NORMAL loadout it unlocks the weapon and adds ammo;
/// in TEST loadout (everything already unlocked) it just tops up. Disappears after a while if left.
public class LundunPickup : MonoBehaviour
{
    [SerializeField] private float reach = 1.6f;
    [SerializeField] private float lifetime = 90f;

    private LundunWeapons.Kind kind;
    private int ammo;
    private float dieAt, baseY;
    private Transform model;
    private Light glow;
    private VehicleSystem vehicles;

    /// Drop a pick-up on the ground near a point.
    public static LundunPickup Spawn(LundunWeapons.Kind kind, int ammo, Vector3 near)
    {
        Vector3 ground = near;
        if (Physics.Raycast(near + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore))
            ground = hit.point;
        GameObject go = new GameObject("Pickup_" + kind);
        go.transform.position = ground;
        LundunPickup p = go.AddComponent<LundunPickup>();
        p.kind = kind; p.ammo = ammo;
        p.BuildModel();
        return p;
    }

    void BuildModel()
    {
        LundunWeapons w = LundunWeapons.Instance;
        GameObject src = w != null ? w.GunModel : null;
        GameObject m = LundunGunModels.Build(kind);                // G3: the real model of that gun
        if (m == null && src != null)
        {
            // a copy of James's gun model, without anything that makes it behave like his gun
            m = Instantiate(src);
            foreach (MonoBehaviour mb in m.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);   // now, so it never follows his hand
            foreach (Collider c in m.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (AudioSource a in m.GetComponentsInChildren<AudioSource>(true)) Destroy(a);
            foreach (Light l in m.GetComponentsInChildren<Light>(true)) Destroy(l);
            m.SetActive(true);
            m.transform.localScale = w.GunScale * (kind == LundunWeapons.Kind.Carbine ? 1.3f : 1f);
        }
        else if (m == null)
        {
            m = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(m.GetComponent<Collider>());
            m.transform.localScale = new Vector3(0.12f, 0.12f, 0.7f);
        }
        foreach (Transform t in m.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 0;
        m.name = "Model";
        model = m.transform;
        model.SetParent(transform, false);
        model.localPosition = Vector3.up * 0.45f;
        model.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject glowGo = new GameObject("Glow");
        glowGo.transform.SetParent(transform, false);
        glowGo.transform.localPosition = Vector3.up * 0.6f;
        glow = glowGo.AddComponent<Light>();
        glow.type = LightType.Point; glow.range = 2.2f; glow.intensity = 1.2f;
        glow.color = new Color(1f, 0.66f, 0.18f);
        glow.shadows = LightShadows.None;
    }

    void Start()
    {
        dieAt = Time.time + lifetime;
        baseY = model != null ? model.localPosition.y : 0.45f;
        vehicles = FindAnyObjectByType<VehicleSystem>();
    }

    void Update()
    {
        if (Time.time > dieAt) { Destroy(gameObject); return; }
        if (model != null)
        {
            model.Rotate(0f, 70f * Time.deltaTime, 0f, Space.World);
            model.localPosition = new Vector3(0f, baseY + Mathf.Sin(Time.time * 2.2f) * 0.06f, 0f);
        }
        if (glow != null) glow.intensity = 0.9f + Mathf.Sin(Time.time * 3f) * 0.35f;

        LundunWeapons w = LundunWeapons.Instance;
        if (w == null || (vehicles != null && vehicles.IsDriving)) return;
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead) return;
        Vector3 d = w.transform.position - transform.position; d.y = 0f;
        if (d.magnitude > reach) return;

        w.Give(kind, ammo);
        LundunHaptics.Medium();
        if (LundunNavigation.Instance != null)
            LundunNavigation.Instance.ShowToast("+ " + kind.ToString().ToUpper() + "  " + ammo + " rounds");
        Destroy(gameObject);
    }
}
