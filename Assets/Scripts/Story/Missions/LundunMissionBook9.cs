using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// ======================================================================================================================
/// 11 Family Meeting (S9a). Uncle Femi's had enough of Hollis: his leverage sits in the Kingsmere Palace strongroom and
/// the gala's coming. Pick up a fast getaway car from the Kestrel showroom in Knightsbury, drive it to the Lundun Wheel
/// and ride a capsule with the family. Who comes depends on what you chose: Kemi if you talked Electric Youth down (07),
/// Marcus if you fought them, Tayo if you let him go (08); Uncle Femi either way (cold about the favour, if that's what
/// you promised Hollis in 05). At the top, look across at the palace through the binoculars and find its three weak
/// points (scaffolding, a service gate, a loose railing): each lights up. Then choose your way in for the finale (the
/// favour closes the service gate: Hollis's people will be waiting there; Tayo adds his staff passes).
public class M11FamilyMeeting : LundunMission
{
    public const string MissionId = "m11_family_meeting";
    static readonly Vector3 Showroom = new Vector3(690f, 0f, 860f);
    GameObject car;
    float carStart;
    bool resolved;

    public override string Id { get { return MissionId; } }
    public override string Title { get { return "Family Meeting"; } }
    public override string Teaser { get { return "Hollis's leverage is in the palace strongroom. The family meets at the top of the Wheel."; } }
    public override string Giver { get { return "UNCLE FEMI"; } }
    public override long Reward { get { return 2000; } }
    public override string MedalNote { get { return "under 9 minutes, the getaway car not badly damaged"; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m10_open_for_business") && !LundunMissionBook.Done(d, Id); }
    public override Vector3 StartAt { get { return FemiAtHome.Marker; } }
    public override Vector3 GiverFacing { get { return FemiAtHome.Facing; } }

    /// The way in chosen at the top of the Wheel (0 scaffolding, 1 service gate, 2 loose railing, 3 Tayo's staff
    /// passes), or -1.
    public static int WayIn
    {
        get
        {
            LundunStoryData d = LundunStory.Data;
            if (d == null) return -1;
            for (int i = 0; i < 4; i++) if (d.flags.Contains("m11:way:" + i)) return i;
            return -1;
        }
    }

    /// Who rides with James (Uncle Femi always).
    public static List<string> Family()
    {
        List<string> f = new List<string> { "UNCLE FEMI" };
        if (LundunChoice.Chose("choice:m07", true)) f.Add("KEMI");
        if (LundunChoice.Chose("choice:m07", false)) f.Add("MARCUS");
        if (LundunChoice.Chose("choice:m08", false)) f.Add("TAYO");
        if (f.Count == 1) f.Add("MARCUS");
        return f;
    }

    public override void Build(LundunMissionRun run)
    {
        bool favour = LundunTrust.Has(LundunTrust.HollisFavour);
        List<string> family = Family();
        LundunWheel wheel = Object.FindAnyObjectByType<LundunWheel>();
        Vector3 board = wheel != null ? wheel.BoardPoint : new Vector3(880f, 1.2f, 538f);
        Vector3 park, shop; Quaternion q;
        if (!LundunStoryPlaces.Kerb(board, out park, out q)) park = LundunMissionRun.Ground(board);
        if (!LundunStoryPlaces.Kerb(Showroom, out shop, out q)) shop = LundunMissionRun.Ground(Showroom);
        run.Zone(Showroom, 50f);

        run.Add(new LundunDo(r =>
        {
            LundunClock c = LundunClock.Instance;
            if (c != null && (c.Hour < 9f || c.Hour > 19.5f)) c.SetHour(16.5f);              // the light's good from up there
            if (resolved) return;
            if (LundunStoryPlaces.Fine(car)) return;
            LundunPopulation pop = LundunPopulation.Instance;
            int look = pop == null ? 3 : Mathf.Max(0, pop.LookIndex("sport") >= 0 ? pop.LookIndex("sport") : pop.LookIndex("coupe"));
            car = LundunStoryPlaces.ParkCar(shop, look);
        }));
        run.Add(new LundunTalkPick(() => favour
            ? new[] { new LundunLine("UNCLE FEMI", "You promised that man a favour. Men like Hollis always collect. So we move first."),
                      new LundunLine("UNCLE FEMI", "His papers, his hold over every shop on Rye Lane, it's all in the palace strongroom. The gala is in two nights."),
                      new LundunLine("UNCLE FEMI", "Get us a fast car from the Kestrel showroom. Then the Wheel. From the top you can see the whole palace.") }
            : new[] { new LundunLine("UNCLE FEMI", "We paid him every penny, and still he squeezes. Enough."),
                      new LundunLine("UNCLE FEMI", "His papers, his hold over every shop on Rye Lane, it's all in the palace strongroom. The gala is in two nights."),
                      new LundunLine("UNCLE FEMI", "Get us a fast car from the Kestrel showroom. Then the Wheel. From the top you can see the whole palace.") }));
        run.Add(new LundunGoTo(shop, 5f, "Pick up the getaway car at the Kestrel showroom in Knightsbury")).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunEnterCar(() => car, "Get in the getaway car", "The getaway car got wrecked"));
        run.Add(new LundunNow(r =>
        {
            resolved = true;
            VehicleHealth vh = car != null ? car.GetComponent<VehicleHealth>() : null;
            carStart = vh != null ? vh.Current : 0f;
            LundunMissions.Say("JAMES", "(phone) Got it. She's quick. On my way to the Wheel.", 3f);
        }));
        run.Add(new LundunGoTo(park, 7f, "Drive the getaway car to the Lundun Wheel", true, true)).Checkpoint(LundunCheckpoint.Minor);
        run.Add(new LundunDo(r =>                                               // (again on a retry from the platform)
        {
            Vector3 side = wheel != null ? wheel.transform.right : Vector3.right;
            for (int i = 0; i < family.Count; i++)
                LundunCrew.Stage(family[i], board + side * (1.6f + i * 1.1f), wheel != null ? wheel.transform.eulerAngles.y + 180f : 0f);
            LundunCrew.ShowUpNow();
        }));
        run.Add(new LundunWhen("Meet the family on the platform and ride the Wheel", r => wheel != null && wheel.Riding)
            .Mark(board, 3f, true)).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunNow(r =>
        {
            LundunWheel.SpeedScale = 3f;                                             // up to the top a little quicker
            foreach (string w in family) LundunCrew.Leave(w, board);                // in with him
            if (wheel != null && wheel.RideCapsule >= 0) wheel.SetRiders(wheel.RideCapsule, Mathf.Min(8, family.Count));
        }));
        run.Add(new LundunTalkPick(() => CapsuleTalk(family, favour)));
        run.Add(new LundunWhen("Up to the top", r => wheel == null || Mathf.Abs(wheel.RideDelta) > 172f));
        run.Add(new LundunNow(r => { LundunWheel.SpeedScale = 0f; }));
        run.Add(new LundunTalk("", new LundunLine("UNCLE FEMI", "There. Kingsmere Palace. Take the binoculars, James. Find us a way in.")));
        run.Add(new LundunScout("Find the palace's three weak points", family));
        run.Add(new LundunWayIn(favour, LundunChoice.Chose("choice:m08", false)));
        run.Add(new LundunNow(r => { LundunWheel.SpeedScale = 4f; }));
        run.Add(new LundunTalkPick(() => WayTalk(WayIn, family)));
        run.Add(new LundunWhen("Ride back down", r => wheel == null || !wheel.Riding));
        run.Add(new LundunTalk("", new LundunLine("UNCLE FEMI", "Two nights. Rest, all of you. Then we finish this."),
                                   new LundunLine("JAMES", "Then we finish it.")));
    }

    static LundunLine[] CapsuleTalk(List<string> family, bool favour)
    {
        List<LundunLine> l = new List<LundunLine>();
        l.Add(new LundunLine("UNCLE FEMI", "All of us, in one little glass box. Your father would laugh."));
        if (family.Contains("KEMI")) l.Add(new LundunLine("KEMI", "You talked those boys down at the market. Didn't think you had it in you."));
        if (family.Contains("MARCUS")) l.Add(new LundunLine("MARCUS", "Whatever this is, I'm in. Somebody has to keep you breathing."));
        if (family.Contains("TAYO")) l.Add(new LundunLine("TAYO", "I owe you all. Let me pay it back. I've got something for this, trust me."));
        if (favour) l.Add(new LundunLine("UNCLE FEMI", "And no more favours to that man. Ever."));
        else l.Add(new LundunLine("UNCLE FEMI", "We paid. We kept our word. Now he keeps nothing."));
        return l.ToArray();
    }

    static LundunLine[] WayTalk(int way, List<string> family)
    {
        string helper = family.Contains("MARCUS") ? "MARCUS" : family.Contains("KEMI") ? "KEMI" : "UNCLE FEMI";
        switch (way)
        {
            case 0: return new[] { new LundunLine(helper, "Up Hollis's own scaffolding. Onto the roof, in through the top. I like it.") };
            case 1: return new[] { new LundunLine(helper, "The trades entrance. In with the deliveries, through the kitchens.") };
            case 2: return new[] { new LundunLine(helper, "Through the railings and across the forecourt in the dark. Bold.") };
            default: return new[] { new LundunLine("TAYO", "Staff passes. You walk in the front door in a waiter's jacket. Nobody looks at the waiters.") };
        }
    }

    public override int Medal(LundunMissionRun run)
    {
        VehicleHealth vh = car != null ? car.GetComponent<VehicleHealth>() : null;
        bool clean = vh == null || vh.Max <= 0f || (carStart - vh.Current) / vh.Max < 0.25f;
        float t = run.Elapsed;
        return t < 540f && clean ? 3 : t < 780f ? 2 : 1;
    }

    public override void Tidy()
    {
        LundunWheel.SpeedScale = 1f;
        LundunPalaceWays.Scouting = false;
        if (LundunPalaceWays.Instance != null) LundunPalaceWays.Instance.HighlightNone();
        LundunMissions.LetGo(car); car = null;
        resolved = false;
    }
}

/// S9a: the binoculars at the top of the Wheel. The camera looks across at Kingsmere Palace (as if through binoculars:
/// a closer, narrower view, two circles); drag to look around, hold the middle on a weak point and it lights up (a beam
/// and its name). The ring in the middle fills while you're on one; an arrow at the edge points to the nearest one
/// left when you're far off. Done once all three are found.
public class LundunScout : LundunStep
{
    readonly List<string> family;
    readonly bool[] found = new bool[3];
    float hold, doneAt = -1f, yaw, pitch, baseYaw, basePitch, fovWas;
    Camera cam;
    CameraFollow follow;
    Vector3 eye;
    GameObject ui;
    Image ring, arrow;
    TextMeshProUGUI title;

    public LundunScout(string text, List<string> who) { objective = text; family = who; }

    public override void Begin(LundunMissionRun run)
    {
        for (int i = 0; i < 3; i++) found[i] = false;
        hold = 0f; doneAt = -1f;
        LundunPalaceWays.Scouting = true;
        cam = Camera.main;
        follow = Object.FindAnyObjectByType<CameraFollow>();
        if (follow != null) follow.enabled = false;
        // where the Wheel sees it from, brought close (binoculars): south-south-east of the palace, high up
        LundunWheel w = Object.FindAnyObjectByType<LundunWheel>();
        Vector3 from = w != null ? w.RidePosition : new Vector3(880f, 130f, 538f);
        Vector3 to = LundunPalaceWays.PalaceCentre;
        Vector3 d = from - to; d.y = 0f; d.Normalize();
        eye = to + d * 230f + Vector3.up * 110f;
        Vector3 look = (to + Vector3.up * 8f) - eye;
        baseYaw = yaw = Mathf.Atan2(look.x, look.z) * Mathf.Rad2Deg;
        basePitch = pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
        if (cam != null) { fovWas = cam.fieldOfView; cam.fieldOfView = 34f; }
        Apply();
        BuildUI();
    }

    void Apply()
    {
        if (cam == null) return;
        cam.transform.SetPositionAndRotation(eye, Quaternion.Euler(pitch, yaw, 0f));
    }

    public void Look(Vector2 drag)
    {
        float k = 0.06f * LundunSettings.LookSpeed;
        yaw = Mathf.Clamp(Mathf.DeltaAngle(baseYaw, yaw + drag.x * k), -40f, 40f) + baseYaw;
        pitch = Mathf.Clamp(pitch - drag.y * k, basePitch - 20f, basePitch + 15f);
    }

    public override bool Tick(LundunMissionRun run)
    {
        Apply();
        if (doneAt > 0f) return Time.time - doneAt > 1.8f;
        // the nearest weak point not yet found
        int best = -1; float bestA = 999f;
        Vector3 f = cam != null ? cam.transform.forward : Vector3.forward;
        for (int i = 0; i < 3; i++)
        {
            if (found[i]) continue;
            float a = Vector3.Angle(f, LundunPalaceWays.Spot(i) - eye);
            if (a < bestA) { bestA = a; best = i; }
        }
        if (best >= 0 && bestA < 3.2f)
        {
            hold += Time.deltaTime;
            if (hold >= 1.1f) Found(best);
        }
        else hold = Mathf.Max(0f, hold - Time.deltaTime * 2f);
        if (ring != null) ring.fillAmount = Mathf.Clamp01(hold / 1.1f);
        // a nudge towards the nearest one left, when it's well off the view
        if (arrow != null)
        {
            bool show = best >= 0 && bestA > 22f && cam != null;
            arrow.gameObject.SetActive(show);
            if (show)
            {
                Vector3 local = cam.transform.InverseTransformDirection(LundunPalaceWays.Spot(best) - eye);
                float ang = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
                RectTransform ar = arrow.rectTransform;
                ar.localRotation = Quaternion.Euler(0f, 0f, ang - 90f);
                ar.anchoredPosition = new Vector2(Mathf.Cos(ang * Mathf.Deg2Rad), Mathf.Sin(ang * Mathf.Deg2Rad)) * 330f;
            }
        }
        return false;
    }

    void Found(int i)
    {
        found[i] = true; hold = 0f;
        if (LundunPalaceWays.Instance != null) LundunPalaceWays.Instance.Highlight(i, true);
        LundunHaptics.Medium();
        int n = 0; foreach (bool b in found) if (b) n++;
        if (title != null) title.text = "WEAK POINTS  " + n + " / 3";
        string who = family.Count > 1 ? family[(i + 1) % family.Count] : "UNCLE FEMI";
        string[] said = { "Scaffolding up the east wing. Hollis's own firm. It goes right to the roof.",
                          "A trades entrance round the back. That's where the kitchens get their deliveries.",
                          "Those railings by the forecourt. Bent. Somebody's been through there already." };
        LundunMissions.Say(who, said[i], 4f);
        LundunMissions.Toast(LundunPalaceWays.Names[i] + " spotted");
        if (n >= 3) doneAt = Time.time;
    }

    public override void End(LundunMissionRun run)
    {
        LundunPalaceWays.Scouting = false;
        if (cam != null) cam.fieldOfView = fovWas > 1f ? fovWas : 60f;
        if (follow != null) follow.enabled = true;
        if (ui != null) Object.Destroy(ui);
    }

    void BuildUI()
    {
        ui = new GameObject("Binoculars", typeof(RectTransform));
        Canvas cv = ui.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 59;
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        ui.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)ui.transform;
        // the binocular mask (dark outside two overlapping circles), and the drag-to-look over everything
        GameObject mg = new GameObject("Mask", typeof(RectTransform));
        mg.transform.SetParent(root, false);
        RectTransform mr = (RectTransform)mg.transform; mr.anchorMin = Vector2.zero; mr.anchorMax = Vector2.one; mr.offsetMin = mr.offsetMax = Vector2.zero;
        RawImage mask = mg.AddComponent<RawImage>(); mask.texture = MaskTexture(); mask.raycastTarget = true;
        mg.AddComponent<LundunScoutPad>().scout = this;
        // the ring in the middle (fills while you hold on a weak point) and its cross
        Image back = LundunUIBits.Box(root, "Ring", Vector2.zero, new Vector2(120f, 120f), new Color(1f, 1f, 1f, 0.25f));
        back.sprite = LundunHudArt.Disc; back.raycastTarget = false;
        ring = LundunUIBits.Box(root, "Fill", Vector2.zero, new Vector2(120f, 120f), new Color(1f, 0.72f, 0.2f, 0.85f));
        ring.sprite = LundunHudArt.Disc; ring.type = Image.Type.Filled; ring.fillMethod = Image.FillMethod.Radial360; ring.fillAmount = 0f; ring.raycastTarget = false;
        LundunUIBits.Box(root, "CrossH", Vector2.zero, new Vector2(60f, 3f), new Color(0f, 0f, 0f, 0.8f)).raycastTarget = false;
        LundunUIBits.Box(root, "CrossV", Vector2.zero, new Vector2(3f, 60f), new Color(0f, 0f, 0f, 0.8f)).raycastTarget = false;
        arrow = LundunUIBits.Box(root, "Arrow", Vector2.zero, new Vector2(46f, 64f), new Color(1f, 0.72f, 0.2f, 0.9f));
        arrow.raycastTarget = false; arrow.gameObject.SetActive(false);
        title = LundunUIBits.Label(root, "WEAK POINTS  0 / 3", 40f, new Vector2(0f, 420f), new Vector2(900f, 60f));
        title.color = new Color(1f, 0.72f, 0.2f);
        TextMeshProUGUI tip = LundunUIBits.Label(root, "Drag to look around. Hold the middle on a weak point:\nscaffolding, a service gate, a loose railing", 26f, new Vector2(0f, -430f), new Vector2(1300f, 90f));
        tip.color = new Color(1f, 1f, 1f, 0.85f);
    }

    static Texture2D maskTex;
    static Texture2D MaskTexture()
    {
        if (maskTex != null) return maskTex;
        const int W = 512, H = 288;
        maskTex = new Texture2D(W, H, TextureFormat.RGBA32, false);
        maskTex.wrapMode = TextureWrapMode.Clamp;
        Color32[] px = new Color32[W * H];
        Vector2 a = new Vector2(W * 0.36f, H * 0.5f), b = new Vector2(W * 0.64f, H * 0.5f);
        float r = H * 0.47f;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                Vector2 p = new Vector2(x, y);
                float d = Mathf.Min(Vector2.Distance(p, a), Vector2.Distance(p, b));
                float t = Mathf.Clamp01((d - (r - 6f)) / 6f);
                px[y * W + x] = new Color32(0, 0, 0, (byte)(t * 245));
            }
        maskTex.SetPixels32(px);
        maskTex.Apply();
        return maskTex;
    }
}

/// S9a: drag anywhere while scouting to look around.
public class LundunScoutPad : MonoBehaviour, IDragHandler
{
    public LundunScout scout;
    public void OnDrag(PointerEventData e) { if (scout != null) scout.Look(e.delta); }
}

/// S9a: choose the way into the palace (the finale goes in that way). The service gate is closed if James promised
/// Hollis a favour (his people are waiting there); Tayo's staff passes are there if he was let go.
public class LundunWayIn : LundunStep
{
    readonly bool favour, tayo;
    GameObject ui;
    bool picked;
    float before = 1f;

    public LundunWayIn(bool hollisFavour, bool tayoFree) { favour = hollisFavour; tayo = tayoFree; objective = "Choose your way in"; }

    public override void Begin(LundunMissionRun run)
    {
        picked = false;
        LundunStoryData d = LundunStory.Data;
        if (d != null) for (int i = 0; i < 4; i++) d.flags.Remove("m11:way:" + i);        // a replay chooses again
        before = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        Build();
    }

    public override bool Tick(LundunMissionRun run) { return picked; }
    public override void End(LundunMissionRun run) { if (ui != null) Object.Destroy(ui); if (Time.timeScale == 0f) Time.timeScale = before; }

    void Pick(int way)
    {
        if (picked) return;
        picked = true;
        LundunStoryData d = LundunStory.Data;
        if (d != null) { d.flags.Add("m11:way:" + way); LundunStory.MarkDirty(); }
        if (ui != null) Object.Destroy(ui);
        Time.timeScale = before;
        LundunHaptics.Light();
    }

    void Build()
    {
        ui = new GameObject("LundunWayIn", typeof(RectTransform));
        Canvas cv = ui.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 80;
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        ui.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)ui.transform;
        Image dim = LundunUIBits.Box(root, "Dim", Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.6f));
        RectTransform dr = dim.rectTransform; dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one; dr.offsetMin = dr.offsetMax = Vector2.zero;
        TextMeshProUGUI q = LundunUIBits.Label(root, "How do you get into the palace?", 44f, new Vector2(0f, 300f), new Vector2(1400f, 80f));
        q.color = new Color(1f, 0.66f, 0.18f);
        LundunUIBits.Label(root, "The finale goes in this way", 24f, new Vector2(0f, 250f), new Vector2(1200f, 40f)).color = new Color(1f, 1f, 1f, 0.7f);
        List<string> labels = new List<string>
        {
            "UP THE SCAFFOLDING\n<size=60%>onto the roof, in through the top. A climb, few guards</size>",
            favour ? "THE SERVICE GATE\n<size=60%>closed: Hollis's people expect you there (your favour)</size>"
                   : "THE SERVICE GATE\n<size=60%>in with the deliveries, through the kitchens</size>",
            "THE LOOSE RAILING\n<size=60%>across the forecourt in the dark. Quick, but open ground</size>",
        };
        if (tayo) labels.Add("TAYO'S STAFF PASSES\n<size=60%>walk in the front door as a waiter. The easy way</size>");
        float y = 150f;
        for (int i = 0; i < labels.Count; i++)
        {
            int way = i;
            bool shut = way == 1 && favour;
            LundunUIBits.Button(root, labels[i], new Vector2(0f, y), new Vector2(980f, 112f), 34f,
                shut ? new Color(0.25f, 0.25f, 0.27f, 0.9f) : way == 3 ? new Color(0.12f, 0.35f, 0.2f, 0.95f) : new Color(0.12f, 0.2f, 0.42f, 0.95f),
                () => { if (!shut) Pick(way); else LundunHaptics.Light(); });
            y -= 128f;
        }
    }
}
