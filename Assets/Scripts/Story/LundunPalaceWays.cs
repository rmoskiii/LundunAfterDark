using System.Collections;
using TMPro;
using UnityEngine;

/// S9a: the palace's weak points (Story only; added by LundunStoryUI), built in code at the real Kingsmere Palace:
///   0 SCAFFOLDING    Hollis Construction's scaffold up the east wing (to the roof), its site gate in the garden wall
///   1 SERVICE GATE   the trades entrance in the back garden wall (deliveries for the kitchens)
///   2 LOOSE RAILING  a section of the forecourt's west railings, bent and taped off
/// Mission 11 spots them from the top of the Wheel (each lights up: a tall beam and its name); mission 12 uses the one
/// you chose. Drawn only within 600 m of the camera (or while a mission's scouting them).
public class LundunPalaceWays : MonoBehaviour
{
    public static LundunPalaceWays Instance { get; private set; }

    // the palace (from the Kingsmere build: x 470–650, z 1765–1815, 22 m high; the forecourt from z 1708; the back
    // garden walled at x 448 / 672 and z 1888)
    public static readonly Vector3 PalaceCentre = new Vector3(560f, 0f, 1790f);
    public const float PalaceTop = 22f;
    public static readonly string[] Names = { "SCAFFOLDING", "SERVICE GATE", "LOOSE RAILING" };
    static readonly Vector3[] Look = { new Vector3(652f, 14f, 1790f), new Vector3(600f, 2f, 1888.4f), new Vector3(454.6f, 1.6f, 1740f) };

    /// Where each one is, to look at (scouting) and to go to (the finale).
    public static Vector3 Spot(int i) { return Look[Mathf.Clamp(i, 0, Look.Length - 1)]; }
    /// Where you stand outside to use it (on the ground).
    public static Vector3 Approach(int i)
    {
        Vector3 p = i == 0 ? new Vector3(675f, 0f, 1830f) : i == 1 ? new Vector3(600f, 0f, 1891.5f) : new Vector3(451.5f, 0f, 1740f);
        return LundunPlaces.Ground(p);
    }

    private GameObject root;
    private readonly GameObject[] beams = new GameObject[3];
    private float nextCheck;
    /// A mission's scouting: drawn whatever the distance.
    public static bool Scouting;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) { Instance = null; Scouting = false; } }

    IEnumerator Start()
    {
        yield return new WaitForSeconds(1.5f);
        Build();
    }

    void Update()
    {
        if (root == null || Time.time < nextCheck) return;
        nextCheck = Time.time + 0.5f;
        Camera c = Camera.main;
        bool want = Scouting || (c != null && LundunMissionRun.FlatDistance(c.transform.position, PalaceCentre) < 600f);
        if (root.activeSelf != want) root.SetActive(want);
    }

    /// Light one up (spotted): a tall beam over it and its name.
    public void Highlight(int i, bool on)
    {
        if (i < 0 || i >= beams.Length) return;
        if (on && beams[i] == null) beams[i] = Beam(i);
        if (beams[i] != null) beams[i].SetActive(on);
    }

    public void HighlightNone() { for (int i = 0; i < beams.Length; i++) Highlight(i, false); }

    // ------------------------------------------------------------------ building
    void Build()
    {
        root = new GameObject("PalaceWays");
        root.transform.SetParent(transform, false);
        Scaffold();
        ServiceGate();
        Railing();
    }

    static float G(float x, float z) { return LundunPlaces.Ground(new Vector3(x, 0f, z)).y; }

    /// Tube scaffold up the east wing (poles, ledgers, boards, a ladder bay), green netting on the outside with
    /// Hollis Construction's banner, a hoist arm above the roof; a hoarding gate in the garden wall.
    void Scaffold()
    {
        float y0 = G(655f, 1790f);
        LundunMeshKit k = new LundunMeshKit(5);
        const float X0 = 650.4f, X1 = 653f, Z0 = 1774f, Z1 = 1806f, Top = 25f;
        for (float z = Z0; z <= Z1 + 0.01f; z += 2f)
            foreach (float x in new[] { X0, X1 })
                k.Box(0, new Vector3(x, Top / 2f, z), new Vector3(0.1f, Top, 0.1f));
        for (float y = 2f; y <= Top; y += 2f)
        {
            foreach (float x in new[] { X0, X1 }) k.Box(0, new Vector3(x, y, (Z0 + Z1) / 2f), new Vector3(0.08f, 0.08f, Z1 - Z0));
            k.Box(1, new Vector3((X0 + X1) / 2f, y - 0.03f, (Z0 + Z1) / 2f), new Vector3(X1 - X0, 0.06f, Z1 - Z0));     // boards
        }
        for (float z = Z0; z < Z1; z += 4f) k.Box(0, new Vector3(X1, Top / 2f, z + 1f), new Vector3(0.06f, Top * 1.02f, 0.06f), Quaternion.Euler(10f, 0f, 0f));   // bracing
        // netting on the outside, in strips (so it reads as mesh), the banner across it
        for (float y = 0.5f; y < Top; y += 1.6f) k.Box(2, new Vector3(X1 + 0.1f, y + 0.6f, (Z0 + Z1) / 2f), new Vector3(0.02f, 1.2f, Z1 - Z0));
        k.Box(3, new Vector3(X1 + 0.14f, 9f, (Z0 + Z1) / 2f), new Vector3(0.02f, 2.2f, 16f));
        // a hoist arm over the roof
        k.Box(0, new Vector3(X1 - 0.4f, Top + 2f, Z0 + 4f), new Vector3(0.14f, 4f, 0.14f));
        k.Box(0, new Vector3(X1 - 3f, Top + 3.9f, Z0 + 4f), new Vector3(5.6f, 0.14f, 0.14f));
        // the site gate: hoarding in the garden wall (x 672), facing the street
        k.Box(4, new Vector3(672.4f, 1.4f, 1830f), new Vector3(0.1f, 2.8f, 6f));
        k.Box(0, new Vector3(672.5f, 1.4f, 1830f), new Vector3(0.06f, 2.6f, 0.06f));
        Place(k, "PalaceScaffold", new Vector3(0f, y0, 0f), new[]
        {
            LundunMeshKit.Mat("ScaffTube", new Color(0.55f, 0.57f, 0.6f), 0.5f, Color.black),
            LundunMeshKit.Mat("ScaffBoard", new Color(0.55f, 0.42f, 0.25f), 0.2f, Color.black),
            LundunMeshKit.Mat("ScaffNet", new Color(0.12f, 0.3f, 0.18f), 0.1f, Color.black),
            LundunMeshKit.Mat("ScaffBanner", new Color(0.95f, 0.95f, 0.92f), 0.2f, new Color(0.2f, 0.2f, 0.2f)),
            LundunMeshKit.Mat("ScaffHoarding", new Color(0.1f, 0.12f, 0.16f), 0.3f, Color.black),
        });
        Words("HOLLIS CONSTRUCTION", new Vector3(653.2f, y0 + 9.1f, 1790f), -90f, new Vector2(15f, 1.6f), 12f, new Color(0.08f, 0.1f, 0.25f));
        Words("RENOVATING KINGSMERE PALACE", new Vector3(653.2f, y0 + 8.2f, 1790f), -90f, new Vector2(14f, 0.5f), 4f, new Color(0.55f, 0.1f, 0.08f));
        Words("SITE ENTRANCE\nHOLLIS CONSTRUCTION · AUTHORISED ONLY", new Vector3(672.5f, y0 + 1.8f, 1830f), -90f, new Vector2(5.4f, 1.2f), 3f, new Color(0.95f, 0.85f, 0.3f));
    }

    /// The trades entrance: a pair of green gates in the back garden wall (z 1888), a sign and a lamp.
    void ServiceGate()
    {
        float y0 = G(600f, 1892f);
        LundunMeshKit k = new LundunMeshKit(3);
        const float Z = 1888.35f;
        k.Box(0, new Vector3(599.1f, 1.5f, Z), new Vector3(1.75f, 3f, 0.08f));
        k.Box(0, new Vector3(600.9f, 1.5f, Z), new Vector3(1.75f, 3f, 0.08f));
        for (float x = 598.4f; x < 601.8f; x += 0.35f) k.Box(1, new Vector3(x, 1.5f, Z + 0.06f), new Vector3(0.05f, 2.9f, 0.04f));
        k.Box(1, new Vector3(600f, 3.2f, Z + 0.05f), new Vector3(3.9f, 0.14f, 0.1f));
        k.Box(2, new Vector3(600f, 3.6f, Z + 0.2f), new Vector3(0.3f, 0.12f, 0.24f));
        Place(k, "PalaceServiceGate", new Vector3(0f, y0, 0f), new[]
        {
            LundunMeshKit.Mat("GateGreen", new Color(0.1f, 0.24f, 0.17f), 0.4f, Color.black),
            LundunMeshKit.Mat("GateIron", new Color(0.06f, 0.06f, 0.06f), 0.5f, Color.black),
            LundunMeshKit.Mat("GateLamp", new Color(1f, 0.9f, 0.7f), 0.5f, new Color(1f, 0.85f, 0.6f) * 2f),
        });
        Words("TRADES ENTRANCE\nKITCHEN DELIVERIES 6AM–10AM", new Vector3(603.3f, y0 + 2f, Z + 0.15f), 180f, new Vector2(2.2f, 0.8f), 2f, new Color(0.95f, 0.92f, 0.85f));
    }

    /// The loose railing: a few bars of the west railings bent outwards, red and white tape across.
    void Railing()
    {
        float y0 = G(452f, 1740f);
        LundunMeshKit k = new LundunMeshKit(2);
        for (int i = 0; i < 6; i++)
        {
            float z = 1738.4f + i * 0.32f, lean = (i == 2 || i == 3) ? 24f : 8f;
            k.Box(0, new Vector3(454.5f, 1.5f, z), new Vector3(0.05f, 3f, 0.05f), Quaternion.Euler(0f, 0f, lean));
        }
        k.Box(1, new Vector3(454.2f, 1.1f, 1739.2f), new Vector3(0.02f, 0.08f, 2.4f), Quaternion.Euler(18f, 0f, 0f));
        k.Box(1, new Vector3(454.2f, 1.6f, 1739.2f), new Vector3(0.02f, 0.08f, 2.4f), Quaternion.Euler(-14f, 0f, 0f));
        Place(k, "PalaceLooseRailing", new Vector3(0f, y0, 0f), new[]
        {
            LundunMeshKit.Mat("RailBlack", new Color(0.04f, 0.04f, 0.045f), 0.6f, Color.black),
            LundunMeshKit.Mat("RailTape", new Color(0.9f, 0.15f, 0.12f), 0.3f, new Color(0.2f, 0.03f, 0.02f)),
        });
    }

    void Place(LundunMeshKit k, string name, Vector3 at, Material[] mats)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(root.transform, false);
        g.transform.position = at;
        g.AddComponent<MeshFilter>().sharedMesh = k.Build(name);
        g.AddComponent<MeshRenderer>().sharedMaterials = mats;
    }

    void Words(string text, Vector3 at, float yaw, Vector2 box, float size, Color colour)
    {
        GameObject g = new GameObject("Words", typeof(RectTransform));
        g.transform.SetParent(root.transform, false);
        g.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        ((RectTransform)g.transform).sizeDelta = box;
        TextMeshPro t = g.AddComponent<TextMeshPro>();
        t.text = text; t.color = colour; t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center;
        t.enableAutoSizing = true; t.fontSizeMin = 0.2f; t.fontSizeMax = size; t.textWrappingMode = TextWrappingModes.Normal;
        MeshRenderer r = g.GetComponent<MeshRenderer>();
        if (r != null) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
    }

    /// A spotted weak point: a tall amber beam and its name over it (faces the camera).
    GameObject Beam(int i)
    {
        Vector3 p = Spot(i); p.y = G(p.x, p.z);
        GameObject g = new GameObject("WeakPoint_" + Names[i]);
        g.transform.SetParent(transform, false);
        g.transform.position = p;
        LundunMeshKit k = new LundunMeshKit(1);
        k.Cyl(0, new Vector3(0f, 30f, 0f), 0.9f, 60f, Quaternion.identity, 12);
        GameObject b = new GameObject("Beam");
        b.transform.SetParent(g.transform, false);
        b.AddComponent<MeshFilter>().sharedMesh = k.Build("WeakBeam");
        MeshRenderer mr = b.AddComponent<MeshRenderer>();
        mr.sharedMaterial = LundunMeshKit.Mat("WeakBeam", new Color(1f, 0.7f, 0.2f), 0.2f, new Color(1f, 0.6f, 0.15f) * 4f);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        GameObject lg = new GameObject("Name", typeof(RectTransform));
        lg.transform.SetParent(g.transform, false);
        lg.transform.localPosition = new Vector3(0f, (i == 0 ? PalaceTop : 8f) + 10f, 0f);
        ((RectTransform)lg.transform).sizeDelta = new Vector2(40f, 8f);
        TextMeshPro t = lg.AddComponent<TextMeshPro>();
        t.text = Names[i]; t.color = new Color(1f, 0.8f, 0.3f); t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center;
        t.fontSize = 40f; t.textWrappingMode = TextWrappingModes.NoWrap; t.outlineWidth = 0.2f; t.outlineColor = new Color32(0, 0, 0, 255);
        lg.AddComponent<LundunFaceCamera>();
        return g;
    }
}
