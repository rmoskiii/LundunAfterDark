using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// S3: holding up a till (Story only; added by LundunStoryUI). Inside Basketful, Grocera or ValuStack, on foot with a
/// gun out, at a till: HOLD TO ROB THE TILL. Keep holding while the bar fills (about 6 seconds):
///   • the shoppers run, the cashier stays put and hands it over
///   • the alarm: 1★ at once, 2★ by the end
///   • let go early (or walk off, or put the gun away) and you keep what's in the bag so far
/// Allowed while a mission asks for it (MissionStore), and in free play once mission 02 is done.
/// S7: in free play every walk-in store (not LAGOS FOODS) can be robbed once per in-game day (LundunDay): £300–£1,200,
/// more the more stars you've got by the end. A red ROBBED TODAY tag hangs over its doors until the next day (06:00).
/// Free Roam has none of this (there's no money there).
public class LundunRobbery : MonoBehaviour
{
    public static LundunRobbery Instance { get; private set; }
    public const string UnlockFlag = "done:m02_easy_money";
    /// Set by a mission: this store can be robbed now, for this much (paid with the mission, not at the till).
    public static string MissionStore;
    public static long MissionCash;
    /// A till's been emptied: the store, and how much (for missions).
    public static event System.Action<string, long> Robbed;

    const float HoldTime = 6f, Reach = 2.6f;
    private LundunShopInterior store;
    private bool robbedHere;
    private float nextTags;
    private readonly Dictionary<LundunShopInterior, List<GameObject>> tags = new Dictionary<LundunShopInterior, List<GameObject>>();
    private Vector3 till;
    private LundunMarketCrowd crowd;
    private bool holding, started, twoStars;
    private float progress, nextLook;
    private GameObject buttonGo;
    private Image fill, buttonImg;
    private TextMeshProUGUI label;
    private HoldButton hold;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; MissionStore = null; MissionCash = 0; }
    void Start() { BuildUI(); }

    /// Every walk-in store but Uncle Femi's.
    static bool Robbable(LundunShopInterior s)
    {
        return s != null && !string.IsNullOrEmpty(s.StoreName) && s.StoreName.ToUpperInvariant().IndexOf("LAGOS") < 0;
    }

    /// Robbed already today (free play)?
    public static bool RobbedToday(string name) { return LundunGame.IsStory && LundunDay.DoneToday("rob:" + name); }

    bool Allowed(string name)
    {
        if (!LundunGame.IsStory) return false;
        if (MissionStore != null) return name == MissionStore;
        if (LundunMissions.Running) return false;
        LundunStoryData d = LundunStory.Data;
        return d != null && d.flags.Contains(UnlockFlag);
    }

    void Update()
    {
        if (Time.time >= nextTags) { nextTags = Time.time + 2f; Tags(); }
        if (buttonGo == null) return;
        PlayerHealth ph = PlayerHealth.Instance;
        bool onFoot = ph != null && !ph.IsDead && ph.OnFoot && !VehicleSystem.Locked && ph.Body != null && ph.Body.gameObject.activeInHierarchy;
        LundunWeapons wpn = LundunWeapons.Instance;
        bool gun = wpn != null && !wpn.IsFists;

        if (holding)
        {
            bool still = onFoot && gun && hold != null && hold.IsHeld && LundunMissionRun.FlatDistance(ph.Body.position, till) < Reach + 0.6f;
            if (!still) { Finish(); return; }
            progress += Time.deltaTime / HoldTime;
            if (!twoStars && progress > 0.6f) { twoStars = true; Alarm(2); }
            fill.fillAmount = progress;
            if (progress >= 1f) { progress = 1f; Finish(); }
            return;
        }

        if (Time.time >= nextLook)
        {
            nextLook = Time.time + 0.2f;
            store = null;
            if (onFoot) Look(ph.Body.position);
        }
        bool show = store != null;
        if (buttonGo.activeSelf != show) buttonGo.SetActive(show);
        if (!show) return;
        fill.fillAmount = 0f;
        if (robbedHere)
        {
            label.text = "ROBBED TODAY\n<size=70%>the till's empty till tomorrow</size>";
            buttonImg.color = new Color(0.2f, 0.2f, 0.22f, 0.85f);
            return;
        }
        label.text = gun ? "HOLD TO ROB THE TILL" : "TAKE OUT A GUN\n<size=70%>to rob the till</size>";
        buttonImg.color = gun ? new Color(0.62f, 0.08f, 0.1f, 0.92f) : new Color(0.2f, 0.2f, 0.22f, 0.85f);
        if (gun && hold != null && hold.IsHeld) Begin();
    }

    /// In a robbable store, by a till (where the cashiers stand)?
    void Look(Vector3 p)
    {
        LundunShopInterior s = LundunShopInterior.At(p);
        if (!Robbable(s) || !Allowed(s.StoreName)) return;
        bool done = MissionStore == null && RobbedToday(s.StoreName);
        LundunMarketCrowd c = CrowdOf(s);
        if (c == null || c.TraderSpots == null || c.TraderSpots.Length == 0)
        {
            // no staff spots: the tills just inside the doors
            Vector3[] doors = s.DoorCentres;
            if (doors == null) return;
            Vector3 mid = (s.Min + s.Max) * 0.5f;
            foreach (Vector3 dc in doors)
            {
                Vector3 inward = mid - dc; inward.y = 0f;
                if (inward.sqrMagnitude < 1f) continue;
                Vector3 t = dc + inward.normalized * 5f;
                if (LundunMissionRun.FlatDistance(p, t) < Reach + 1f) { store = s; till = t; crowd = null; robbedHere = done; return; }
            }
            return;
        }
        float best = Reach;
        Vector3[] at = c.TraderSpots, face = c.TraderFacing;
        for (int i = 0; i < at.Length; i++)
        {
            if (!s.Inside(at[i])) continue;
            Vector3 f = face != null && i < face.Length ? face[i] : Vector3.zero;
            Vector3 t = at[i] - f.normalized * 0.9f;                       // the till is across the counter from the cashier
            float d = LundunMissionRun.FlatDistance(p, t);
            if (d < best) { best = d; store = s; till = t; crowd = c; robbedHere = done; }
        }
    }

    static LundunMarketCrowd CrowdOf(LundunShopInterior s)
    {
        foreach (LundunMarketCrowd c in FindObjectsByType<LundunMarketCrowd>(FindObjectsSortMode.None))
        {
            Vector3[] t = c.TraderSpots;
            if (t != null && t.Length > 0 && s.Inside(t[0])) return c;
        }
        return null;
    }

    void Begin()
    {
        if (robbedHere) return;
        holding = true; started = true; twoStars = false; progress = 0f;
        PlayerHealth ph = PlayerHealth.Instance;
        Vector3 me = ph.Body.position;
        if (crowd != null) crowd.HoldUp(me, 45f, till, 2.5f);                  // everyone runs but the cashier
        Alarm(1);
        LundunMissions.Say("CASHIER", Random.value < 0.5f ? "Okay! Okay! Don't shoot, take it!" : "Alright, alright! It's all yours!", 3f);
        LundunHaptics.Medium();
    }

    void Alarm(int stars)
    {
        WantedSystem w = WantedSystem.Instance;
        PlayerHealth ph = PlayerHealth.Instance;
        if (w != null && ph != null) w.RaiseTo(stars, ph.Body.position, stars == 1);
    }

    void Finish()
    {
        holding = false;
        if (!started || store == null) return;
        started = false;
        string name = store.StoreName;
        float got = progress;
        progress = 0f;
        buttonGo.SetActive(false);
        if (got < 0.12f) { LundunMissions.Toast("Nothing taken"); return; }
        bool mission = MissionStore == name;
        // S7: £300–£600 in the till, worth more the hotter it got (2★ ×1.25 … 5★ ×2), never more than £1,200
        int stars = WantedSystem.Instance != null ? WantedSystem.Instance.Stars : 1;
        long till0 = 300 + 50 * Random.Range(0, 7);
        long free = (long)Mathf.Min(1200f, till0 * (0.75f + 0.25f * Mathf.Max(1, stars)));
        long full = mission && MissionCash > 0 ? MissionCash : free;
        long cash = (long)Mathf.Round(full * Mathf.Clamp01(got) / 10f) * 10;
        if (!mission) { LundunDay.MarkToday("rob:" + name); LundunStory.MarkDirty(); nextTags = 0f; }
        if (!mission) LundunWallet.Earn(cash, "Till robbed");
        else LundunMissions.Toast(LundunWallet.Format(cash) + " in the bag");
        Robbed?.Invoke(name, cash);
        store = null;
    }

    // ------------------------------------------------------------------ S7: ROBBED TODAY over the doors
    void Tags()
    {
        foreach (LundunShopInterior s in LundunShopInterior.All)
        {
            if (!Robbable(s)) continue;
            bool want = RobbedToday(s.StoreName);
            List<GameObject> have;
            tags.TryGetValue(s, out have);
            if (want && have == null)
            {
                have = new List<GameObject>();
                Vector3 mid = (s.Min + s.Max) * 0.5f;
                if (s.DoorCentres != null)
                    foreach (Vector3 dc in s.DoorCentres)
                    {
                        Vector3 outward = dc - mid; outward.y = 0f;
                        outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
                        have.Add(Tag(LundunPlaces.Ground(dc) + outward * 1.2f + Vector3.up * 3.3f));
                    }
                tags[s] = have;
            }
            else if (!want && have != null)
            {
                foreach (GameObject g in have) if (g != null) Destroy(g);
                tags.Remove(s);
            }
        }
    }

    GameObject Tag(Vector3 at)
    {
        GameObject g = new GameObject("RobbedToday", typeof(RectTransform));
        g.transform.SetParent(transform, false);
        g.transform.position = at;
        ((RectTransform)g.transform).sizeDelta = new Vector2(4f, 0.8f);
        TextMeshPro t = g.AddComponent<TextMeshPro>();
        t.text = "ROBBED TODAY"; t.color = new Color(1f, 0.25f, 0.2f); t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center; t.textWrappingMode = TextWrappingModes.NoWrap;
        t.fontSize = 4.5f; t.outlineWidth = 0.2f; t.outlineColor = new Color32(0, 0, 0, 255);
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        MeshRenderer r = g.GetComponent<MeshRenderer>();
        if (r != null) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
        g.AddComponent<LundunFaceCamera>();
        return g;
    }

    // ------------------------------------------------------------------ the button (bottom centre, above the train's)
    void BuildUI()
    {
        GameObject cg = new GameObject("RobberyUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas cv = cg.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 62;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();
        buttonGo = new GameObject("RobButton", typeof(RectTransform));
        buttonGo.transform.SetParent(cg.transform, false);
        RectTransform rt = (RectTransform)buttonGo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 380f); rt.sizeDelta = new Vector2(500f, 120f);
        buttonImg = buttonGo.AddComponent<Image>();
        hold = buttonGo.AddComponent<HoldButton>();
        GameObject fg = new GameObject("Fill", typeof(RectTransform));
        fg.transform.SetParent(buttonGo.transform, false);
        RectTransform fr = (RectTransform)fg.transform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(1f, 0f); fr.pivot = new Vector2(0.5f, 0f);
        fr.sizeDelta = new Vector2(0f, 14f); fr.anchoredPosition = Vector2.zero;
        fill = fg.AddComponent<Image>(); fill.color = new Color(1f, 0.82f, 0.25f); fill.type = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal; fill.fillAmount = 0f; fill.raycastTarget = false;
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(buttonGo.transform, false);
        RectTransform tr = (RectTransform)tg.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(10f, 14f); tr.offsetMax = new Vector2(-10f, 0f);
        label = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) label.font = LundunHud.Instance.FontAsset;
        label.fontSize = 36; label.alignment = TextAlignmentOptions.Center; label.color = Color.white;
        label.fontStyle = FontStyles.Bold; label.raycastTarget = false; label.richText = true;
        buttonGo.SetActive(false);
    }
}
