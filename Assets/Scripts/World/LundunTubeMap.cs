using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// G6b: the Lundun Underground's map: the three lines as a Tube map, full screen, drawn in code. Added by LundunTube.
/// Two jobs, both from LundunTubeNetwork's lines and journeys (so they always agree with the list, the times and,
/// later, the full map and the GPS):
///   CHOOSE   where to (BOARD on the platform, SKIP, or a quick ride): tap a stop; the route lights up, and the bar at
///            the bottom says how long, which line, and where you change. RIDE goes
///   RIDE     the journey itself, over the dark: a train marker runs along the route, stop by stop; it pauses at a
///            change ("CHANGE AT …"), and ends on ARRIVING. Short: 1–2 stops about 5–6 s, up to 12 s for the longest
/// Stations that aren't built in this world yet show grey and can't be picked (trains still run through them).
public class LundunTubeMap : MonoBehaviour
{
    public LundunTube tube;

    // where each station sits on the Tube map (grid units), and which side its name goes
    struct Spot { public float x, y; public string side; public Spot(float x, float y, string side) { this.x = x; this.y = y; this.side = side; } }
    static readonly Dictionary<string, Spot> Layout = new Dictionary<string, Spot>
    {
        { "KINGSMERE PALACE", new Spot(5f, 0.5f, "R") },
        { "ST JARVIS'S PARK", new Spot(0.8f, 2.6f, "A") },
        { "HYDEMOOR PARK", new Spot(2.9f, 2.6f, "A") },
        { "KNIGHTSBURY CIRCUS", new Spot(5f, 2.6f, "BR") },
        { "QUAY CROSS", new Spot(7.4f, 2.6f, "A") },
        { "CANARY QUAY", new Spot(9.2f, 4.2f, "R") },
        { "RIVER BRIDGE", new Spot(5f, 4.2f, "L") },
        { "PECKAM HIGH ST", new Spot(5f, 5.8f, "BL") },
        { "BRIXTUN", new Spot(5f, 7.5f, "L") },
        { "BROCKMERE PARK", new Spot(5f, 9f, "R") },
        { "ANGEL FIELDS", new Spot(0.8f, 7.5f, "B") },
        { "GILMORE GREEN", new Spot(2.6f, 5.8f, "A") },
        { "PECKAM RYE", new Spot(7.4f, 5.8f, "A") },
        { "FERNDALE CIRCUS", new Spot(9.2f, 7.5f, "R") },
    };
    const float UX = 142f, UY = 80f;                       // a grid unit on screen (reference 1920 × 1080)
    static readonly Color Navy = new Color(0.106f, 0.153f, 0.4f), Yellow = new Color(1f, 0.78f, 0.17f), Ink = new Color(0.05f, 0.07f, 0.16f);

    class Dot { public string name; public int index = -1; public RectTransform rt; public Image ring, fill; public TextMeshProUGUI label; }
    class Seg { public string a, b; public int line; public Image img; }
    readonly Dictionary<string, Dot> dots = new Dictionary<string, Dot>();
    readonly List<Seg> segs = new List<Seg>();

    GameObject root;
    CanvasGroup group;
    RectTransform mapRoot, hereRing, selRing, trainMark;
    TextMeshProUGUI title, subtitle, infoName, infoSub, rideText, rideNext, hereText;
    GameObject closeBtn, rideBtn, infoBar, rideBar, skipRideBtn;
    Image rideBtnImg;

    // choosing
    int from = -1;
    bool includeHere;
    Action<int, int> chosen;
    string picked;
    // riding
    bool skipRide;

    public bool Showing { get { return root != null && root.activeSelf; } }
    public bool Choosing { get { return Showing && chosen != null; } }

    // ================================================================== CHOOSE
    /// The map, to pick a stop. chosen(to, minutes) when RIDE's pressed; closing it just closes it.
    public void Choose(int fromStation, string sub, bool includeHereToo, Action<int, int> onChosen)
    {
        Build();
        from = fromStation; includeHere = includeHereToo; chosen = onChosen; picked = null;
        root.SetActive(true); group.alpha = 1f; group.blocksRaycasts = true;
        title.text = "LUNDUN UNDERGROUND";
        subtitle.text = sub;
        closeBtn.SetActive(true); infoBar.SetActive(true); rideBar.SetActive(false); trainMark.gameObject.SetActive(false);
        Refresh();
        ShowHere(from >= 0 && from < tube.Names.Length ? tube.Names[from] : null);
        Select(null);
    }

    public void Hide()
    {
        chosen = null;
        if (root != null) root.SetActive(false);
    }

    void Tap(string name)
    {
        if (chosen == null) return;
        Select(name);
    }

    void Select(string name)
    {
        picked = name;
        string here = from >= 0 ? tube.Names[from] : null;
        Dot d = name != null && dots.ContainsKey(name) ? dots[name] : null;
        selRing.gameObject.SetActive(d != null && name != here);
        if (d != null) selRing.anchoredPosition = d.rt.anchoredPosition;
        LundunTubeNetwork.Journey j = d != null && here != null && name != here ? LundunTubeNetwork.Plan(here, name) : null;
        Highlight(j);
        if (d == null)
        {
            infoName.text = "Tap a station";
            infoSub.text = here != null ? "You're at " + LundunTubeNetwork.Title(here) + " · " + LundunTubeNetwork.LinesText(here) : "";
            SetRide(false, "RIDE");
            return;
        }
        infoName.text = LundunTubeNetwork.Title(name) + "  " + Pills(name);
        if (d.index < 0) { infoSub.text = "Not open yet"; SetRide(false, "RIDE"); return; }
        if (name == here)
        {
            infoSub.text = includeHere ? "Back up to the street here" : "You're here";
            SetRide(includeHere, includeHere ? "WAY OUT" : "RIDE");
            return;
        }
        int minutes = j != null ? j.Minutes : tube.Minutes(from, d.index);
        string stops = j != null ? j.Hops + (j.Hops == 1 ? " stop" : " stops") : "";
        infoSub.text = "<color=#FFC72C>" + minutes + " min</color> · " + stops + (j != null ? " · " + j.Summary : "");
        SetRide(true, "RIDE  <size=70%>" + minutes + " min</size>");
    }

    void SetRide(bool on, string label)
    {
        rideBtn.GetComponent<Button>().interactable = on;
        rideBtnImg.color = on ? Yellow : new Color(0.35f, 0.37f, 0.45f, 0.9f);
        rideText.text = label;
    }

    void OnRide()
    {
        if (chosen == null || picked == null || !dots.ContainsKey(picked)) return;
        Dot d = dots[picked];
        if (d.index < 0) return;
        if (d.index == from && !includeHere) return;
        Action<int, int> cb = chosen;
        int minutes = tube.Minutes(from, d.index);
        Hide();
        cb(d.index, minutes);
    }

    /// The line colours as little coloured words: "Crown · Quay"
    static string Pills(string station)
    {
        List<int> ls = LundunTubeNetwork.LinesAt(station);
        string s = "";
        foreach (int li in ls) s += (s.Length > 0 ? " " : "") + "<size=60%><color=#" + LundunTubeNetwork.Lines[li].Hex + ">" + LundunTubeNetwork.Lines[li].name + "</color></size>";
        return s;
    }

    // ================================================================== RIDE
    /// Puts the map up for a ride (call as the screen goes dark); SetAlpha fades it with the dark.
    public void ShowRide(LundunTubeNetwork.Journey j, string lead = null)
    {
        Build();
        chosen = null; skipRide = false;
        root.SetActive(true); group.alpha = 0f; group.blocksRaycasts = true;
        Refresh();
        closeBtn.SetActive(false); infoBar.SetActive(false); rideBar.SetActive(true);
        selRing.gameObject.SetActive(false);
        ShowHere(null);
        Highlight(j);
        title.text = "LUNDUN UNDERGROUND";
        subtitle.text = (lead != null ? lead + " · then the " : "") + (j != null && j.Hops > 0 ? LundunTubeNetwork.Lines[j.hopLine[0]].name + " line · towards " + LundunTubeNetwork.Title(Towards(j, 0)) : "");
        trainMark.gameObject.SetActive(j != null && j.stops.Count > 0 && dots.ContainsKey(j.stops[0]));
        if (trainMark.gameObject.activeSelf) trainMark.anchoredPosition = dots[j.stops[0]].rt.anchoredPosition;
        rideNext.text = j != null && j.Hops > 0 ? "NEXT STOP  <color=#FFC72C>" + LundunTubeNetwork.Title(j.stops[1]) + "</color>" : "";
        skipRideBtn.SetActive(true);
    }

    public void SetAlpha(float a) { if (group != null) group.alpha = a; }

    /// The train along the route: `seconds` in all (a little at the start, the pauses at changes, ARRIVING at the end).
    public IEnumerator Animate(LundunTubeNetwork.Journey j, float seconds)
    {
        if (j == null || j.Hops == 0 || !Showing) yield break;
        const float StartHold = 0.4f, EndHold = 0.6f, ChangeHold = 0.6f;
        int changes = j.changes.Count;
        float move = Mathf.Max(0.6f * j.Hops, seconds - StartHold - EndHold - changes * ChangeHold);
        float per = move / j.Hops;
        yield return Wait(StartHold);
        for (int i = 0; i < j.Hops && !skipRide; i++)
        {
            string a = j.stops[i], b = j.stops[i + 1];
            if (i > 0 && j.hopLine[i] != j.hopLine[i - 1])
            {
                LundunTubeNetwork.Line nl = LundunTubeNetwork.Lines[j.hopLine[i]];
                rideNext.text = "CHANGE AT  <color=#FFC72C>" + LundunTubeNetwork.Title(a) + "</color>  for the <color=#" + nl.Hex + ">" + nl.name + " line</color>";
                Pulse(a);
                yield return Wait(ChangeHold);
                subtitle.text = nl.name + " line · towards " + LundunTubeNetwork.Title(Towards(j, i));
            }
            rideNext.text = "NEXT STOP  <color=#FFC72C>" + LundunTubeNetwork.Title(b) + "</color>";
            Vector2 pa = Pos(a), pb = Pos(b);
            for (float t = 0f; t < 1f && !skipRide; t += Time.unscaledDeltaTime / per)
            {
                trainMark.anchoredPosition = Vector2.Lerp(pa, pb, Mathf.SmoothStep(0f, 1f, t));
                yield return null;
            }
            trainMark.anchoredPosition = pb;
            Pulse(b);
        }
        trainMark.anchoredPosition = Pos(j.to);
        rideNext.text = "ARRIVING  <color=#FFC72C>" + LundunTubeNetwork.Title(j.to) + "</color>";
        skipRideBtn.SetActive(false);
        yield return Wait(skipRide ? 0.25f : EndHold);
    }

    IEnumerator Wait(float s) { for (float t = 0f; t < s && !skipRide; t += Time.unscaledDeltaTime) yield return null; }

    static string Towards(LundunTubeNetwork.Journey j, int hop)
    {
        string[] stops = LundunTubeNetwork.Lines[j.hopLine[hop]].stops;
        int a = Array.IndexOf(stops, j.stops[hop]), b = Array.IndexOf(stops, j.stops[hop + 1]);
        return b > a ? stops[stops.Length - 1] : stops[0];
    }

    Vector2 Pos(string station) { return dots.ContainsKey(station) ? dots[station].rt.anchoredPosition : Vector2.zero; }

    void Pulse(string station)
    {
        if (!dots.ContainsKey(station)) return;
        StartCoroutine(PulseRoutine(dots[station].rt));
    }

    IEnumerator PulseRoutine(RectTransform rt)
    {
        for (float t = 0f; t < 0.35f; t += Time.unscaledDeltaTime)
        {
            float s = 1f + 0.5f * Mathf.Sin(t / 0.35f * Mathf.PI);
            if (rt != null) rt.localScale = new Vector3(s, s, 1f);
            yield return null;
        }
        if (rt != null) rt.localScale = Vector3.one;
    }

    // ================================================================== drawing
    /// Which stations are built in this world (the rest are grey).
    void Refresh()
    {
        foreach (Dot d in dots.Values)
        {
            d.index = tube.Find(d.name);
            bool open = d.index >= 0;
            d.label.color = open ? Color.white : new Color(1f, 1f, 1f, 0.35f);
            d.fill.color = open ? Color.white : new Color(0.55f, 0.57f, 0.62f);
        }
    }

    void ShowHere(string station)
    {
        bool on = station != null && dots.ContainsKey(station);
        hereRing.gameObject.SetActive(on);
        if (on) hereRing.anchoredPosition = dots[station].rt.anchoredPosition;
    }

    /// The route bright, the rest of the map faded back (or everything bright if there's no route).
    void Highlight(LundunTubeNetwork.Journey j)
    {
        HashSet<string> onRoute = new HashSet<string>();
        HashSet<Seg> segOn = new HashSet<Seg>();
        if (j != null)
        {
            foreach (string s in j.stops) onRoute.Add(s);
            for (int i = 0; i < j.Hops; i++)
                foreach (Seg sg in segs)
                    if (sg.line == j.hopLine[i] && ((sg.a == j.stops[i] && sg.b == j.stops[i + 1]) || (sg.b == j.stops[i] && sg.a == j.stops[i + 1]))) segOn.Add(sg);
        }
        bool all = j == null || j.Hops == 0;
        foreach (Seg sg in segs)
        {
            Color c = LundunTubeNetwork.Lines[sg.line].colour;
            c.a = all || segOn.Contains(sg) ? 1f : 0.22f;
            sg.img.color = c;
        }
        foreach (Dot d in dots.Values)
        {
            float a = all || onRoute.Contains(d.name) ? 1f : 0.35f;
            Color lc = d.label.color; lc.a = d.index >= 0 ? a : a * 0.35f; d.label.color = lc;
            Color rc = d.ring.color; rc.a = a; d.ring.color = rc;
        }
    }

    void Build()
    {
        if (root != null) return;
        // its own canvas, over the dark of the ride and the station's caption
        root = new GameObject("LundunTubeMap", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        Canvas cv = root.AddComponent<Canvas>();
        cv.renderMode = RenderMode.ScreenSpaceOverlay;
        cv.sortingOrder = 67;
        CanvasScaler sc = root.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();
        group = root.AddComponent<CanvasGroup>();

        Image bg = Box(root.transform, "Back", Ink, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        bg.color = new Color(Ink.r, Ink.g, Ink.b, 0.97f);

        // header
        Image head = Box(root.transform, "Header", Navy, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -130f), Vector2.zero);
        Box(root.transform, "Stripe", Yellow, new Vector2(0f, 1f), Vector2.one, new Vector2(0f, -140f), new Vector2(0f, -130f));
        title = Text(head.transform, "LUNDUN UNDERGROUND", 50, TextAlignmentOptions.TopLeft, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(60f, 0f), new Vector2(-700f, -18f));
        subtitle = Text(head.transform, "", 26, TextAlignmentOptions.BottomLeft, new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(62f, 14f), new Vector2(-700f, 0f));
        subtitle.fontStyle = FontStyles.Normal; subtitle.color = new Color(1f, 0.84f, 0.47f);
        float lx = -600f;
        foreach (LundunTubeNetwork.Line l in LundunTubeNetwork.Lines)
        {
            GameObject p = Pill(head.transform, l.name, l.colour, new Vector2(1f, 0.5f), new Vector2(lx, 0f), new Vector2(150f, 44f), 26);
            lx += 165f;
            p.GetComponent<Image>().raycastTarget = false;
        }
        closeBtn = Pill(head.transform, "CLOSE", new Color(0.25f, 0.27f, 0.35f, 0.95f), new Vector2(1f, 0.5f), new Vector2(-95f, 0f), new Vector2(150f, 64f), 26);
        closeBtn.AddComponent<Button>().onClick.AddListener(Hide);

        // the map
        GameObject mr = new GameObject("Map", typeof(RectTransform));
        mr.transform.SetParent(root.transform, false);
        mapRoot = (RectTransform)mr.transform;
        mapRoot.anchorMin = mapRoot.anchorMax = new Vector2(0.5f, 0.5f);
        mapRoot.anchoredPosition = new Vector2(-40f, 12f);
        mapRoot.sizeDelta = Vector2.zero;
        for (int li = 0; li < LundunTubeNetwork.Lines.Length; li++)
        {
            string[] st = LundunTubeNetwork.Lines[li].stops;
            for (int i = 0; i + 1 < st.Length; i++) Line(st[i], st[i + 1], li);
        }
        foreach (KeyValuePair<string, Spot> kv in Layout) Station(kv.Key, kv.Value);

        hereRing = Ring("Here", Yellow, 62f);
        hereText = Text(hereRing, "YOU ARE HERE", 18, TextAlignmentOptions.Center, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-90f, -30f), new Vector2(90f, -4f));
        hereText.color = Yellow;
        selRing = Ring("Picked", Color.white, 56f);
        trainMark = Ring("Train", Yellow, 34f);
        Image tf = trainMark.GetComponent<Image>(); tf.sprite = LundunHudArt.Disc; tf.color = Yellow;
        Image tb = new GameObject("Edge", typeof(RectTransform)).AddComponent<Image>();
        tb.transform.SetParent(trainMark, false); tb.sprite = LundunHudArt.RingThick; tb.color = Navy; tb.raycastTarget = false;
        RectTransform tbr = (RectTransform)tb.transform; tbr.anchorMin = Vector2.zero; tbr.anchorMax = Vector2.one; tbr.offsetMin = tbr.offsetMax = Vector2.zero;

        // the bottom bar: choosing
        Image bar = Box(root.transform, "Info", Navy, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 128f));
        infoBar = bar.gameObject;
        infoName = Text(bar.transform, "", 40, TextAlignmentOptions.TopLeft, Vector2.zero, Vector2.one, new Vector2(60f, 0f), new Vector2(-420f, -12f));
        infoSub = Text(bar.transform, "", 25, TextAlignmentOptions.BottomLeft, Vector2.zero, Vector2.one, new Vector2(62f, 16f), new Vector2(-420f, 0f));
        infoSub.fontStyle = FontStyles.Normal; infoSub.color = new Color(0.9f, 0.92f, 0.96f);
        rideBtn = Pill(bar.transform, "RIDE", Yellow, new Vector2(1f, 0.5f), new Vector2(-200f, 0f), new Vector2(320f, 90f), 38);
        rideBtnImg = rideBtn.GetComponent<Image>();
        rideText = rideBtn.GetComponentInChildren<TextMeshProUGUI>(); rideText.color = Navy;
        Button rb = rideBtn.AddComponent<Button>(); rb.targetGraphic = rideBtnImg; rb.onClick.AddListener(OnRide);

        // the bottom bar: riding
        Image rbar = Box(root.transform, "RideBar", Navy, Vector2.zero, new Vector2(1f, 0f), Vector2.zero, new Vector2(0f, 128f));
        rideBar = rbar.gameObject;
        rideNext = Text(rbar.transform, "", 40, TextAlignmentOptions.Left, Vector2.zero, Vector2.one, new Vector2(60f, 0f), new Vector2(-340f, 0f));
        skipRideBtn = Pill(rbar.transform, "SKIP  <size=70%>>></size>", new Color(0.25f, 0.27f, 0.35f, 0.95f), new Vector2(1f, 0.5f), new Vector2(-150f, 0f), new Vector2(220f, 70f), 28);
        skipRideBtn.AddComponent<Button>().onClick.AddListener(() => skipRide = true);
        rideBar.SetActive(false);

        root.SetActive(false);
    }

    void Line(string a, string b, int li)
    {
        if (!Layout.ContainsKey(a) || !Layout.ContainsKey(b)) return;
        Vector2 pa = Grid(Layout[a]), pb = Grid(Layout[b]);
        GameObject g = new GameObject("Line", typeof(RectTransform));
        g.transform.SetParent(mapRoot, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = (pa + pb) / 2f;
        rt.sizeDelta = new Vector2(Vector2.Distance(pa, pb) + 14f, 16f);
        rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(pb.y - pa.y, pb.x - pa.x) * Mathf.Rad2Deg);
        Image img = g.AddComponent<Image>(); img.color = LundunTubeNetwork.Lines[li].colour; img.raycastTarget = false;
        segs.Add(new Seg { a = a, b = b, line = li, img = img });
    }

    void Station(string name, Spot s)
    {
        bool inter = LundunTubeNetwork.IsInterchange(name);
        float size = inter ? 38f : 28f;
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(mapRoot, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Grid(s);
        rt.sizeDelta = new Vector2(size, size);
        Image ring = g.AddComponent<Image>(); ring.sprite = LundunHudArt.Disc; ring.color = Ink;
        GameObject fg = new GameObject("Fill", typeof(RectTransform));
        fg.transform.SetParent(g.transform, false);
        RectTransform frt = (RectTransform)fg.transform; frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
        frt.offsetMin = new Vector2(5f, 5f); frt.offsetMax = new Vector2(-5f, -5f);
        Image fill = fg.AddComponent<Image>(); fill.sprite = LundunHudArt.Disc; fill.color = Color.white; fill.raycastTarget = false;
        // the name
        GameObject lg = new GameObject("Name", typeof(RectTransform));
        lg.transform.SetParent(g.transform, false);
        TextMeshProUGUI t = lg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = LundunTubeNetwork.Title(name); t.fontSize = 27; t.fontStyle = FontStyles.Bold; t.color = Color.white;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.raycastTarget = true;
        RectTransform lrt = (RectTransform)lg.transform;
        lrt.anchorMin = lrt.anchorMax = new Vector2(0.5f, 0.5f);
        lrt.sizeDelta = new Vector2(300f, 40f);
        float h = size / 2f;
        switch (s.side)
        {
            case "R": lrt.pivot = new Vector2(0f, 0.5f); lrt.anchoredPosition = new Vector2(h + 14f, 0f); t.alignment = TextAlignmentOptions.Left; break;
            case "L": lrt.pivot = new Vector2(1f, 0.5f); lrt.anchoredPosition = new Vector2(-h - 14f, 0f); t.alignment = TextAlignmentOptions.Right; break;
            case "A": lrt.pivot = new Vector2(0.5f, 0f); lrt.anchoredPosition = new Vector2(0f, h + 10f); t.alignment = TextAlignmentOptions.Bottom; break;
            case "B": lrt.pivot = new Vector2(0.5f, 1f); lrt.anchoredPosition = new Vector2(0f, -h - 8f); t.alignment = TextAlignmentOptions.Top; break;
            case "BR": lrt.pivot = new Vector2(0f, 1f); lrt.anchoredPosition = new Vector2(h + 8f, -h + 4f); t.alignment = TextAlignmentOptions.TopLeft; break;
            default: lrt.pivot = new Vector2(1f, 1f); lrt.anchoredPosition = new Vector2(-h - 8f, -h + 4f); t.alignment = TextAlignmentOptions.TopRight; break;   // "BL"
        }
        // tap the dot or the name (a generous invisible target round the dot)
        GameObject hit = new GameObject("Tap", typeof(RectTransform));
        hit.transform.SetParent(g.transform, false);
        RectTransform hrt = (RectTransform)hit.transform; hrt.anchorMin = hrt.anchorMax = new Vector2(0.5f, 0.5f); hrt.sizeDelta = new Vector2(86f, 86f);
        Image hi = hit.AddComponent<Image>(); hi.color = new Color(0f, 0f, 0f, 0f);
        string n = name;
        hit.AddComponent<Button>().onClick.AddListener(() => Tap(n));
        lg.AddComponent<Button>().onClick.AddListener(() => Tap(n));
        dots[name] = new Dot { name = name, rt = rt, ring = ring, fill = fill, label = t };
    }

    static Vector2 Grid(Spot s) { return new Vector2((s.x - 5f) * UX, -(s.y - 4.75f) * UY); }

    RectTransform Ring(string name, Color c, float size)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(mapRoot, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        Image img = g.AddComponent<Image>(); img.sprite = LundunHudArt.RingThick; img.color = c; img.raycastTarget = false;
        g.SetActive(false);
        return rt;
    }

    Image Box(Transform parent, string name, Color c, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
        Image img = g.AddComponent<Image>(); img.color = c;
        return img;
    }

    TextMeshProUGUI Text(Transform parent, string text, float size, TextAlignmentOptions align, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        GameObject g = new GameObject("Text", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = text; t.fontSize = size; t.alignment = align; t.color = Color.white;
        t.fontStyle = FontStyles.Bold; t.raycastTarget = false; t.richText = true;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }

    GameObject Pill(Transform parent, string text, Color c, Vector2 anchor, Vector2 pos, Vector2 size, float fontSize)
    {
        GameObject g = new GameObject("Pill", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)g.transform;
        rt.anchorMin = rt.anchorMax = anchor; rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos; rt.sizeDelta = size;
        Image img = g.AddComponent<Image>(); img.color = c;
        if (LundunHudArt.Pill != null) { img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1.4f; }
        TextMeshProUGUI t = Text(g.transform, text, fontSize, TextAlignmentOptions.Center, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        t.overflowMode = TextOverflowModes.Overflow;
        return g;
    }
}
