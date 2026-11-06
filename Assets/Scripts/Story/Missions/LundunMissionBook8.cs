using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// ======================================================================================================================
/// 13 Kemi's Deliveries (S8, side mission, repeatable). Kemi runs parcels for the shops on Rye Lane. She waits at the
/// hire-bike dock nearest LAGOS FOODS: take her parcels round the city against the clock, on a hire bike or in a car.
/// Each run is bigger than the last (3 drops, then 4, then 5 every time after), with less time per metre; it pays
/// £150, £475, then £800. Gold: lots of time to spare and no police. Opens after mission 03.
public class M13KemisDeliveries : LundunMission
{
    public const string MissionId = "m13_kemis_deliveries";
    const string RunsKey = "Lundun.KemiRuns";
    int level = -1;
    LundunDrops drops;

    static int Runs { get { return LundunSave.GetInt(RunsKey, 0); } }
    int Level { get { return level >= 0 ? level : Mathf.Min(Runs, 2); } }

    public override string Id { get { return MissionId; } }
    public override string Title { get { return "Kemi's Deliveries"; } }
    public override string Teaser { get { return Level == 0 ? "Kemi's got parcels to drop round the city. Beat the clock." : "Another run for Kemi: " + (3 + Level) + " drops, less time."; } }
    public override string Giver { get { return "KEMI"; } }
    public override long Reward { get { return 150 + 325 * Level; } }
    public override string MedalNote { get { return "a third of the time to spare, no police"; } }
    public override bool Story { get { return false; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m03_no_way_home"); }

    /// Kemi waits by the hire-bike dock nearest LAGOS FOODS.
    public override Vector3 StartAt
    {
        get
        {
            Vector3 shop = LundunStoryPlaces.LagosDoor;
            LundunBikes b = LundunBikes.Instance;
            if (b == null || b.DockCount == 0) return LundunMissionRun.Ground(shop + new Vector3(0f, 0f, 14f));
            Vector3 best = shop; float bd = float.MaxValue;
            for (int i = 0; i < b.DockCount; i++)
            {
                Vector3 p = b.DockAt(i);
                float d = LundunMissionRun.FlatDistance(p, shop);
                if (d > 18f && d < bd) { bd = d; best = p; }                     // not on the shop's own doorstep
            }
            return LundunMissionRun.Ground(best);
        }
    }

    public override void Build(LundunMissionRun run)
    {
        level = Mathf.Min(Runs, 2);
        Vector3 from = StartAt;
        int n = 3 + level;
        List<Vector3> spots = PickDrops(from, n, 170f + 60f * level, 320f + 90f * level);
        float metres = 0f; Vector3 p = from;
        foreach (Vector3 s in spots) { metres += LundunMissionRun.FlatDistance(p, s) * 1.3f; p = s; }     // roads aren't straight
        float scale = run.difficulty == LundunDifficulty.Easy ? 1.3f : run.difficulty == LundunDifficulty.Hard ? 0.88f : 1f;
        float limit = (metres / 7.5f + 14f * n) * (1f - 0.1f * level) * scale;
        run.Add(new LundunTalkPick(() => level == 0
            ? new[] { new LundunLine("KEMI", "James! Perfect timing. I've got " + n + " parcels and no rider."),
                      new LundunLine("KEMI", "Grab a bike off the rack, or take a car. Addresses are on your phone. Don't be late, they rate me."),
                      new LundunLine("JAMES", "How hard can it be?") }
            : new[] { new LundunLine("KEMI", "Back for more? " + n + " drops this time, and they want them quicker."),
                      new LundunLine("KEMI", "Bike's quickest through the traffic. Go, go!") }));
        drops = new LundunDrops(spots, limit, "Deliver Kemi's parcels");
        run.Add(drops);
        run.Add(new LundunNow(r => { LundunSave.SetInt(RunsKey, Runs + 1); LundunStory.MarkDirty(); }));
        run.Add(new LundunTalkPick(() => new[] { new LundunLine("KEMI", drops.Spare > 0.33f
            ? "(phone) All delivered, and early? Five stars. You're hired."
            : "(phone) All delivered. Just about. Same time tomorrow?") }));
    }

    public override int Medal(LundunMissionRun run)
    {
        float spare = drops != null ? drops.Spare : 0f;
        return spare >= 0.33f && !run.wanted ? 3 : spare >= 0.15f ? 2 : 1;
    }

    public override void Tidy() { level = -1; drops = null; }

    /// Drop-offs at the kerb round the city, each a fair ride from the last.
    static List<Vector3> PickDrops(Vector3 from, int n, float near, float far)
    {
        List<Vector3> list = new List<Vector3>();
        Vector3 at = from;
        for (int i = 0; i < n; i++)
        {
            Vector3 got = Vector3.zero; bool ok = false;
            for (int t = 0; t < 16 && !ok; t++)
            {
                float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                Vector3 want = at + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * UnityEngine.Random.Range(near, far);
                if (want.x < -80f || want.x > 1680f || want.z < -900f || want.z > 1850f) continue;
                Vector3 pos; Quaternion rot;
                if (!LundunStoryPlaces.Kerb(want, out pos, out rot)) continue;
                float d = LundunMissionRun.FlatDistance(at, pos);
                if (d < near * 0.6f || d > far * 1.5f) continue;
                bool clash = false;
                foreach (Vector3 o in list) if (LundunMissionRun.FlatDistance(o, pos) < 60f) clash = true;
                if (clash) continue;
                got = LundunMissionRun.Ground(pos); ok = true;
            }
            if (!ok) got = LundunMissionRun.Ground(at + new Vector3(near, 0f, 0f));
            list.Add(got);
            at = got;
        }
        return list;
    }
}

/// S8: a delivery run: drop-offs one after another against one clock (the timer at the top). Get to each marker and
/// slow down there (on foot, on a bike or in a car). Out of time: the mission fails.
public class LundunDrops : LundunStep
{
    readonly List<Vector3> spots;
    readonly float limit;
    readonly string text;
    int at;
    float started;
    GameObject ui;
    TextMeshProUGUI clock;
    /// How much of the time was left at the end (0..1).
    public float Spare { get; private set; }

    public LundunDrops(List<Vector3> where, float seconds, string objectiveText) { spots = where; limit = seconds; text = objectiveText; }

    public override void Begin(LundunMissionRun run)
    {
        at = 0; started = Time.time; Spare = 0f;
        Aim();
        BuildUI();
    }

    void Aim()
    {
        hasMarker = true; marker = spots[at]; markerRadius = 4.5f; route = true;
        objective = text + " (" + (at + 1) + " of " + spots.Count + ")";
    }

    public override bool Tick(LundunMissionRun run)
    {
        float left = limit - (Time.time - started);
        if (clock != null)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(left));
            clock.text = (s / 60) + ":" + (s % 60).ToString("00");
            clock.color = left < 15f ? new Color(1f, 0.3f, 0.25f) : Color.white;
        }
        if (left <= 0f) { run.Fail("Out of time: the customers cancelled"); return false; }
        Transform j = LundunMissionRun.James();
        if (j == null || LundunMissionRun.FlatDistance(j.position, marker) > markerRadius) return false;
        VehicleSystem vs = Object.FindAnyObjectByType<VehicleSystem>();
        if (vs != null && vs.PlayerSpeed > 6f) return false;                       // slow down to hand it over
        LundunHaptics.Medium();
        at++;
        if (at >= spots.Count)
        {
            Spare = Mathf.Clamp01(left / limit);
            LundunMissions.Toast("All delivered!");
            return true;
        }
        LundunMissions.Toast("Delivered (" + at + " of " + spots.Count + ")");
        Aim();
        LundunMissions.Repoint();
        if (LundunGuide.Instance != null) LundunGuide.Instance.Announce(objective);
        return false;
    }

    public override void End(LundunMissionRun run) { if (ui != null) Object.Destroy(ui); }

    void BuildUI()
    {
        ui = new GameObject("DropsClock", typeof(RectTransform));
        Canvas cv = ui.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 58;
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        RectTransform root = (RectTransform)ui.transform;
        Image back = LundunUIBits.Box(root, "Back", Vector2.zero, new Vector2(220f, 70f), new Color(0f, 0f, 0f, 0.55f));
        if (LundunHudArt.Pill != null) { back.sprite = LundunHudArt.Pill; back.type = Image.Type.Sliced; back.pixelsPerUnitMultiplier = 1f; }
        RectTransform br = back.rectTransform; br.anchorMin = br.anchorMax = new Vector2(0.5f, 1f); br.anchoredPosition = new Vector2(0f, -200f);
        clock = LundunUIBits.Label(br, "", 44f, Vector2.zero, new Vector2(220f, 70f));
    }
}

/// S8: a countdown on screen around another step (Prove Yourself's trials): the step runs as normal; the clock just
/// says whether it was done in time (no fail). Shows "OVER TIME" once it's gone past.
public class LundunClocked : LundunStep
{
    readonly LundunStep inner;
    readonly float seconds;
    readonly Action<bool> result;
    float started;
    GameObject ui;
    TextMeshProUGUI clock;
    public override Transform Follow { get { return inner.Follow; } }

    public LundunClocked(LundunStep step, float target, Action<bool> inTime)
    {
        inner = step; seconds = target; result = inTime;
        objective = step.objective; checkpoint = step.checkpoint;
        hasMarker = step.hasMarker; marker = step.marker; markerRadius = step.markerRadius; route = step.route;
    }

    public override void Begin(LundunMissionRun run)
    {
        started = Time.time;
        inner.Begin(run);
        objective = inner.objective; hasMarker = inner.hasMarker; marker = inner.marker; markerRadius = inner.markerRadius; route = inner.route;
        ui = new GameObject("TrialClock", typeof(RectTransform));
        Canvas cv = ui.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 58;
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        RectTransform root = (RectTransform)ui.transform;
        Image back = LundunUIBits.Box(root, "Back", Vector2.zero, new Vector2(300f, 70f), new Color(0f, 0f, 0f, 0.55f));
        if (LundunHudArt.Pill != null) { back.sprite = LundunHudArt.Pill; back.type = Image.Type.Sliced; back.pixelsPerUnitMultiplier = 1f; }
        RectTransform br = back.rectTransform; br.anchorMin = br.anchorMax = new Vector2(0.5f, 1f); br.anchoredPosition = new Vector2(0f, -200f);
        clock = LundunUIBits.Label(br, "", 40f, Vector2.zero, new Vector2(300f, 70f));
    }

    public override bool Tick(LundunMissionRun run)
    {
        float left = seconds - (Time.time - started);
        if (clock != null)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(left));
            clock.text = left > 0f ? (s / 60) + ":" + (s % 60).ToString("00") : "OVER TIME";
            clock.color = left < 10f ? new Color(1f, 0.3f, 0.25f) : Color.white;
        }
        bool done = inner.Tick(run);
        objective = inner.objective;
        if (done && result != null) result(left > 0f);
        return done;
    }

    public override void End(LundunMissionRun run)
    {
        inner.End(run);
        if (ui != null) Object.Destroy(ui);
    }
}

// ======================================================================================================================
/// 14 Prove Yourself (S8, side mission, repeatable for a better medal). Marcus wants to see what James can do before
/// he backs him for real. Four trials on and around the Gilmore Estate, each against a clock (miss it and you carry on,
/// the medal just drops):
///   1  a street fight, fists (45 s)          2  an ambush, they're armed (60 s)
///   3  lose 3★ (2 minutes)                    4  a clean drive along the Ring in Marcus's car (70 s, under 15% damage)
/// Four in time = gold, two or three = silver. Silver or gold unlocks Marcus's backup call (LundunBackup). Pays £1,000 the
/// first time, £250 after. Opens after mission 05.
public class M14ProveYourself : LundunMission
{
    public const string MissionId = "m14_prove_yourself";
    static readonly Vector3 Green = new Vector3(265f, 0f, 240f);             // Gilmore Green
    int met;
    GameObject car;
    float carStart;

    public override string Id { get { return MissionId; } }
    public override string Title { get { return "Prove Yourself"; } }
    public override string Teaser { get { return "Marcus wants to see what you can do. Four trials. Don't embarrass him."; } }
    public override string Giver { get { return "MARCUS"; } }
    public override long Reward { get { return LundunMissionBook.Done(LundunStory.Data, Id) ? 250 : 1000; } }
    public override string MedalNote { get { return "all four trials in time (silver+ unlocks Marcus's backup)"; } }
    public override bool Story { get { return false; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m05_uncles_rules"); }
    public override Vector3 StartAt { get { return LundunMissionRun.Ground(Green + new Vector3(0f, 0f, 30f)); } }

    public override void Build(LundunMissionRun run)
    {
        met = 0;
        Vector3 fightAt = LundunMissionRun.Ground(Green + new Vector3(-20f, 0f, -10f));
        Vector3 ambushAt = LundunMissionRun.Ground(Green + new Vector3(40f, 0f, -45f));
        Vector3 ringA, ringB; Quaternion qa, qb;
        if (!LundunStoryPlaces.Kerb(new Vector3(380f, 0f, 562f), out ringA, out qa)) ringA = LundunMissionRun.Ground(new Vector3(380f, 0f, 562f));
        if (!LundunStoryPlaces.Kerb(new Vector3(990f, 0f, 562f), out ringB, out qb)) ringB = LundunMissionRun.Ground(new Vector3(990f, 0f, 562f));
        run.Zone(Green, 60f);

        run.Add(new LundunTalk("",
            new LundunLine("MARCUS", "You want me watching your back? Then show me you can watch your own."),
            new LundunLine("MARCUS", "Four trials. A scrap, an ambush, the police, and a drive. Each one's on the clock."),
            new LundunLine("JAMES", "And if I'm slow?"),
            new LundunLine("MARCUS", "Then you're slow, and I'll know about it. Go.")));
        // 1: a street fight
        run.Add(new LundunClocked(new LundunFight(fightAt, 2, false, "Trial 1 of 4: win a street fight", "Street", 30f), 45f, ok => Score(ok, "Trial 1")))
           .Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunTalk("", new LundunLine("MARCUS", "Not bad. Now they're armed. Keep moving, use cover.")));
        // 2: an ambush
        run.Add(new LundunClocked(new LundunFight(ambushAt, 3, true, "Trial 2 of 4: survive the ambush", "Street", 40f) { harder = 0.8f }, 60f, ok => Score(ok, "Trial 2")))
           .Checkpoint(LundunCheckpoint.Major);
        // 3: the police
        run.Add(new LundunTalk("", new LundunLine("MARCUS", "All that noise? The police are on their way. Three stars. Lose them, quick.")));
        run.Add(new LundunNow(r =>
        {
            WantedSystem w = WantedSystem.Instance;
            Transform j = LundunMissionRun.James();
            if (w != null && j != null) w.RaiseTo(3, j.position, true);
        }));
        run.Add(new LundunClocked(new LundunEscape("Trial 3 of 4: lose the police"), 120f, ok => Score(ok, "Trial 3"))).Checkpoint(LundunCheckpoint.Major);
        // 4: a clean drive along the Ring
        run.Add(new LundunDo(r =>
        {
            if (car == null || !LundunStoryPlaces.Fine(car)) car = LundunStoryPlaces.ParkCar(ringA, -1);
        }));
        run.Add(new LundunTalk("", new LundunLine("MARCUS", "(phone) Last one. My car's on the Ring by the Westbridge. Get it across town without a scratch.")));
        run.Add(new LundunEnterCar(() => car, "Trial 4 of 4: get in Marcus's car on the Ring", "Marcus's car got wrecked")).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunNow(r => { VehicleHealth vh = car != null ? car.GetComponent<VehicleHealth>() : null; carStart = vh != null ? vh.Current : 0f; }));
        run.Add(new LundunClocked(new LundunGoTo(ringB, 8f, "Trial 4 of 4: drive along the Ring to the marker. Clean!", true, true), 70f, ok =>
        {
            VehicleHealth vh = car != null ? car.GetComponent<VehicleHealth>() : null;
            bool clean = vh == null || vh.Max <= 0f || (carStart - vh.Current) / vh.Max < 0.15f;
            if (!clean) LundunMissions.Toast("Trial 4: too many scratches");
            Score(ok && clean, "Trial 4");
        }));
        run.Add(new LundunTalkPick(() => met >= 4
            ? new[] { new LundunLine("MARCUS", "(phone) Four from four. Alright. I've got your back. Any time you're in it, ring me.") }
            : met >= 2
            ? new[] { new LundunLine("MARCUS", "(phone) " + met + " from four. Good enough. You get in trouble, you ring me. Once a day, mind.") }
            : new[] { new LundunLine("MARCUS", "(phone) " + met + " from four. Not yet, bro. Try it again when you're sharper.") }));
    }

    void Score(bool ok, string which)
    {
        if (ok) met++;
        LundunMissions.Toast(which + (ok ? ": IN TIME" : ": too slow"));
    }

    public override int Medal(LundunMissionRun run) { return met >= 4 ? 3 : met >= 2 ? 2 : 1; }
    public override void Tidy() { LundunMissions.LetGo(car); car = null; }
}

// ======================================================================================================================
/// 15 Warehouse 9 (S8, side mission). Night. Hollis keeps the gala's bribe money in Warehouse 9 on Warehouse Lane
/// (Canary Quay) tonight, and his own helicopter waits on a pad in Quay Gardens. Tayo has a plan (Marcus, if Tayo's
/// gone): in through the side door, past the torch guards to the office at the back, grab the bag. The safe's alarmed:
/// 4★. Out (Hollis's men waiting outside if they saw you), through the streets to the helicopter (the prompts lead the
/// way), and fly it across the city with the police helicopter after you, to land on the pad in Hydemoor Park. Gold:
/// never seen, the helicopter in good shape, quick. Opens after mission 10.
public class M15Warehouse9 : LundunMission
{
    public const string MissionId = "m15_warehouse_9";
    static readonly Vector3 Poi = new Vector3(1260f, 0f, 780f);
    static readonly Vector3 QuayGardens = new Vector3(1287f, 0f, 477f);
    static readonly Vector3 Hydemoor = new Vector3(174f, 0f, 1040f);
    const float InY = 300f;

    readonly List<LundunGuard> guards = new List<LundunGuard>();
    GameObject room, front, heliPad, landPad;
    LundunFlyHeli heli;
    Vector3 door, inward, padAt, landAt, safeAt, insideOrigin;
    bool alarm, got, resolved;

    static string Planner { get { return LundunTrust.Around("TAYO") ? "TAYO" : "MARCUS"; } }

    public override string Id { get { return MissionId; } }
    public override string Title { get { return "Warehouse 9"; } }
    public override string Teaser { get { return "Hollis's gala money sits in Warehouse 9 tonight. And his helicopter's parked next door."; } }
    public override string Giver { get { return Planner; } }
    public override long Reward { get { return 6000; } }
    public override string MedalNote { get { return "never seen, helicopter in good shape, under 10 minutes"; } }
    public override bool Story { get { return false; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m10_open_for_business") && !LundunMissionBook.Done(d, Id); }
    public override Vector3 StartAt { get { Vector3 p; Quaternion q; return LundunStoryPlaces.Kerb(new Vector3(1180f, 0f, 812f), out p, out q) ? LundunMissionRun.Ground(p) : LundunMissionRun.Ground(new Vector3(1180f, 0f, 815f)); } }

    public override void Build(LundunMissionRun run)
    {
        string who = Planner;
        FindDoor();
        padAt = OpenGround(QuayGardens, 46f);
        landAt = OpenGround(Hydemoor, 100f);
        insideOrigin = new Vector3(door.x, InY, door.z) + inward * 1.5f;
        safeAt = Inside(new Vector3(0f, 0f, 33f));
        run.Zone(Poi, 70f);

        run.Add(new LundunDo(r =>
        {
            if (!resolved) { alarm = false; got = false; }
            if (LundunClock.Instance != null && LundunClock.Instance.Hour > 4f && LundunClock.Instance.Hour < 22f) LundunClock.Instance.SetHour(23f);
            Clear();
            front = r.Spawn(BuildFront());
            room = r.Spawn(BuildRoom(r));
            // the pads and the helicopter aren't run.Spawn-ed: they stay after a pass (and go once nobody's looking)
            heliPad = LundunFlyHeli.Pad(padAt, 7f, new Color(1f, 0.8f, 0.2f));
            landPad = LundunFlyHeli.Pad(landAt, 7f, new Color(0.3f, 1f, 0.45f));
            heli = LundunFlyHeli.Spawn(padAt, 0f);
        }));
        run.Add(new LundunTalkPick(() => who == "TAYO"
            ? new[] { new LundunLine("TAYO", "Warehouse 9. Hollis keeps the gala money in there tonight. Bribes. A whole bag of them."),
                      new LundunLine("TAYO", "Side door's open, guards have torches. Office at the back. And bro, his helicopter's on a pad in Quay Gardens."),
                      new LundunLine("JAMES", "I can't fly a helicopter."),
                      new LundunLine("TAYO", "Nobody can, till they do. Go!") }
            : new[] { new LundunLine("MARCUS", "Tayo's tip, before he went. Warehouse 9, Hollis's gala money, tonight."),
                      new LundunLine("MARCUS", "Guards with torches. Office at the back. And his helicopter's sitting in Quay Gardens."),
                      new LundunLine("JAMES", "I can't fly a helicopter."),
                      new LundunLine("MARCUS", "Then learn fast. Go.") }));
        run.Add(new LundunGoTo(door, 2.4f, "Get to the side door of Warehouse 9")).Checkpoint(LundunCheckpoint.Major);
        // in: a moment's black, and he's on the warehouse floor
        run.Add(new LundunNow(r =>
        {
            LundunFlash.Black(0.6f);
            Put(insideOrigin + inward * 0.5f, Quaternion.LookRotation(inward));
            LundunArches.MissionInside = true;
            LundunMissions.Toast("Stay out of the torchlight");
        }));
        run.Add(new LundunSneak(() => guards, () =>
        {
            Transform j = LundunMissionRun.James();
            return j != null && LundunMissionRun.FlatDistance(j.position, safeAt) < 1.6f;
        }, Seen, "Find the cash in the office at the back. Stay out of the torchlight").Mark(safeAt, 1.6f, false));
        run.Add(new LundunIf(() => alarm, new LundunWhen("They've seen you! Grab the bag from the office", r =>
        {
            Transform j = LundunMissionRun.James();
            return j != null && LundunMissionRun.FlatDistance(j.position, safeAt) < 1.6f;
        }).Mark(safeAt, 1.6f, false)));
        run.Add(new LundunNow(r =>
        {
            got = true;
            LundunMissions.Toast("Got the bag. ALARM!");
            LundunHaptics.Heavy();
            foreach (LundunGuard g in guards) if (g != null) g.Wake();
        }));
        run.Add(new LundunWhen("Get out of Warehouse 9!", r =>
        {
            Transform j = LundunMissionRun.James();
            return j != null && LundunMissionRun.FlatDistance(j.position, insideOrigin) < 1.8f;
        }).Mark(insideOrigin, 1.8f, false));
        run.Add(new LundunNow(r =>
        {
            LundunFlash.Black(0.6f);
            LundunArches.MissionInside = false;
            Put(door - inward * 1.2f, Quaternion.LookRotation(-inward));
            WantedSystem w = WantedSystem.Instance;
            if (w != null) w.RaiseTo(4, door, true);
            resolved = true;
        }));
        run.Add(new LundunDo(r =>                                                // a retry after this point: still wanted
        {
            WantedSystem w = WantedSystem.Instance;
            if (resolved && w != null) w.RaiseTo(4, door, true);
        }));
        run.Add(new LundunIf(() => alarm, new LundunFight(door - inward * 9f, 3, true, "Hollis's men saw it all: get past them", "Street", 40f) { harder = 1.1f }));
        run.Add(new LundunTalkPick(() => new[] { new LundunLine(who, who == "TAYO"
            ? "(phone) Police everywhere! The helicopter, James! Quay Gardens! GO GO GO!"
            : "(phone) Police all over. Get to that helicopter in Quay Gardens. Now.") }));
        run.Add(new LundunWhen("Get to the helicopter in Quay Gardens", r =>
        {
            if (heli == null || heli.Wrecked) { r.Fail("The helicopter's been wrecked"); return false; }
            Transform j = LundunMissionRun.James();
            if (j == null || LundunMissionRun.Driving()) return false;
            if (LundunMissionRun.FlatDistance(j.position, heli.transform.position) > 4.2f) return false;
            heli.Board();
            heli.CanLeave = false;
            return true;
        }).Mark(padAt, 5f, true)).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunFly(() => heli, landAt, "Fly to Hydemoor Park and land on the green pad"));
        run.Add(new LundunNow(r =>
        {
            WantedSystem w = WantedSystem.Instance;
            if (w != null) w.ClearWanted();
            if (heli != null) heli.Release();
            LundunMissions.Toast("THEY'VE LOST YOU");
        }));
        run.Add(new LundunTalkPick(() => new[]
        {
            new LundunLine(who, who == "TAYO" ? "(phone) You FLEW it?! Bro. BRO. I'm telling everyone." : "(phone) You flew it. Dad would never believe this."),
            new LundunLine("JAMES", "Hollis is going to be furious."),
            new LundunLine(who, who == "TAYO" ? "(phone) Good. Let him come to his gala without his bribes." : "(phone) Good. He can go to his gala empty-handed."),
        }));
    }

    void Seen()
    {
        alarm = true;
        foreach (LundunGuard g in guards) if (g != null) g.Wake();
        LundunMissions.Toast("You've been seen!");
    }

    public override int Medal(LundunMissionRun run)
    {
        int n = 0;
        if (!alarm) n++;
        if (heli == null || heli.HealthFraction >= 0.6f) n++;
        if (run.Elapsed < 600f) n++;
        return n >= 3 ? 3 : n == 2 ? 2 : 1;
    }

    public override void Tidy()
    {
        LundunArches.MissionInside = false;
        guards.Clear();
        resolved = false;
        if (heli != null) LundunMissions.LetGo(heli.gameObject);
        LundunMissions.LetGo(heliPad); LundunMissions.LetGo(landPad);
        heli = null; room = null; front = null; heliPad = null; landPad = null;
    }

    void Clear()
    {
        guards.Clear();
        foreach (GameObject g in new[] { room, front, heliPad, landPad, heli != null ? heli.gameObject : null }) if (g != null) Object.Destroy(g);
        heli = null;
    }

    public static void Put(Vector3 at, Quaternion rot)
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.Body == null) return;
        VehicleSystem vs = Object.FindAnyObjectByType<VehicleSystem>();
        if (vs != null && vs.IsDriving) vs.ExitCar();
        CharacterController cc = ph.Body.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        ph.Body.SetPositionAndRotation(at + Vector3.up * 0.1f, rot);
        if (cc != null) cc.enabled = true;
    }

    // ------------------------------------------------------------------ where things are
    /// The warehouse's side door: the building on the south side of Warehouse Lane by the old Warehouse 9 marker.
    void FindDoor()
    {
        int mask = LayerMask.GetMask("Default");
        door = LundunMissionRun.Ground(new Vector3(Poi.x, 0f, 806f));
        inward = Vector3.back;
        for (int k = 0; k < 9; k++)
        {
            float x = Poi.x + (k % 2 == 0 ? 1 : -1) * (k / 2) * 8f;
            Vector3 from = LundunMissionRun.Ground(new Vector3(x, 0f, 806f)) + Vector3.up * 1.4f;
            if (!Physics.Raycast(from, Vector3.back, out RaycastHit hit, 30f, mask, QueryTriggerInteraction.Ignore)) continue;
            Vector3 n = hit.normal; n.y = 0f;
            if (n.sqrMagnitude < 0.5f || Vector3.Dot(n.normalized, Vector3.forward) < 0.8f) continue;
            if (hit.collider.bounds.size.y < 4f) continue;
            inward = -n.normalized;
            door = LundunMissionRun.Ground(hit.point - inward * 0.8f);
            return;
        }
    }

    /// The clearest flat spot (room for a helicopter) nearest 'centre' within 'reach'.
    public static Vector3 OpenGround(Vector3 centre, float reach)
    {
        int mask = LayerMask.GetMask("Default");
        for (float r = 0f; r <= reach; r += 6f)
        {
            int steps = r < 1f ? 1 : Mathf.Max(6, Mathf.RoundToInt(r * 2f * Mathf.PI / 8f));
            for (int i = 0; i < steps; i++)
            {
                float a = i * Mathf.PI * 2f / steps;
                Vector3 c = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * r;
                if (!Physics.Raycast(new Vector3(c.x, 200f, c.z), Vector3.down, out RaycastHit g, 400f, mask, QueryTriggerInteraction.Ignore)) continue;
                if (g.normal.y < 0.97f) continue;
                Vector3 p = g.point;
                bool flat = true;
                foreach (Vector3 o in new[] { new Vector3(6f, 0f, 0f), new Vector3(-6f, 0f, 0f), new Vector3(0f, 0f, 6f), new Vector3(0f, 0f, -6f) })
                    if (!Physics.Raycast(new Vector3(p.x + o.x, p.y + 20f, p.z + o.z), Vector3.down, out RaycastHit h2, 40f, mask, QueryTriggerInteraction.Ignore) || Mathf.Abs(h2.point.y - p.y) > 0.5f) flat = false;
                if (!flat) continue;
                bool clear = true;
                foreach (Collider col in Physics.OverlapBox(p + Vector3.up * 5f, new Vector3(6.5f, 4.5f, 6.5f), Quaternion.identity, mask, QueryTriggerInteraction.Ignore))
                    if (col.attachedRigidbody == null) { clear = false; break; }
                if (clear) return p;
            }
        }
        return LundunMissionRun.Ground(centre);
    }

    /// A point on the warehouse floor (up high): x across, z in from the door.
    Vector3 Inside(Vector3 local)
    {
        Quaternion q = Quaternion.LookRotation(inward);
        return insideOrigin + q * local;
    }

    // ------------------------------------------------------------------ the building
    /// The street side: a sign, a roller shutter and the side door, a skin just proud of the wall.
    GameObject BuildFront()
    {
        GameObject g = new GameObject("Warehouse9Front");
        g.transform.SetPositionAndRotation(door + inward * 0.75f, Quaternion.LookRotation(-inward));
        LundunMeshKit k = new LundunMeshKit(5);
        k.Box(0, new Vector3(-4.5f, 2f, 0.06f), new Vector3(5f, 4f, 0.1f));                       // the shutter
        for (float y = 0.1f; y < 4f; y += 0.12f) k.Box(1, new Vector3(-4.5f, y, 0.12f), new Vector3(5f, 0.02f, 0.02f));
        k.Box(2, new Vector3(0f, 1.05f, 0.05f), new Vector3(1.1f, 2.1f, 0.08f));                  // the side door
        k.Box(3, new Vector3(-2f, 4.9f, 0.1f), new Vector3(5.2f, 1.1f, 0.12f));                   // the sign board
        k.Box(4, new Vector3(0f, 2.4f, 0.25f), new Vector3(0.3f, 0.12f, 0.2f));                   // a lamp over the door
        g.AddComponent<MeshFilter>().sharedMesh = k.Build("Warehouse9Front");
        g.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            LundunMeshKit.Mat("W9Shutter", new Color(0.45f, 0.47f, 0.48f), 0.5f, Color.black),
            LundunMeshKit.Mat("W9Groove", new Color(0.2f, 0.21f, 0.22f), 0.4f, Color.black),
            LundunMeshKit.Mat("W9Door", new Color(0.12f, 0.2f, 0.3f), 0.4f, Color.black),
            LundunMeshKit.Mat("W9Sign", new Color(0.9f, 0.85f, 0.7f), 0.3f, new Color(0.3f, 0.28f, 0.22f)),
            LundunMeshKit.Mat("W9Lamp", new Color(1f, 0.9f, 0.7f), 0.5f, new Color(1f, 0.85f, 0.6f) * 2f),
        };
        Words(g.transform, "WAREHOUSE 9", new Vector3(-2f, 4.95f, 0.18f), 180f, new Vector2(4.8f, 0.9f), 6f, new Color(0.15f, 0.12f, 0.1f));
        Words(g.transform, "HOLLIS LOGISTICS · NO ENTRY", new Vector3(-2f, 4.55f, 0.18f), 180f, new Vector2(4.8f, 0.25f), 1.5f, new Color(0.55f, 0.08f, 0.06f));
        GameObject lg = new GameObject("DoorLight");
        lg.transform.SetParent(g.transform, false);
        lg.transform.localPosition = new Vector3(0f, 2.3f, 1f);
        Light l = lg.AddComponent<Light>();
        l.type = LightType.Point; l.range = 7f; l.intensity = 1.5f; l.color = new Color(1f, 0.85f, 0.6f); l.shadows = LightShadows.None;
        return g;
    }

    /// The warehouse floor (high above the building, like the pubs): 24 m across, 36 m deep, racking in rows with
    /// aisles between, crates by the door, the glass office at the back with the safe. Dim; the guards' torches.
    GameObject BuildRoom(LundunMissionRun run)
    {
        GameObject g = new GameObject("Warehouse9Inside");
        Quaternion q = Quaternion.LookRotation(inward);
        g.transform.SetPositionAndRotation(insideOrigin, q);
        LundunMeshKit k = new LundunMeshKit(7);
        const float W = 12f, D = 36f, H = 7f;
        List<Bounds> solid = new List<Bounds>();
        Action<int, Vector3, Vector3, bool> Add = (m, c, sz, block) => { k.Box(m, c, sz); if (block) solid.Add(new Bounds(c, sz)); };
        Add(0, new Vector3(0f, -0.1f, D / 2f - 1f), new Vector3(2f * W, 0.2f, D), true);                         // floor
        Add(1, new Vector3(-W - 0.1f, H / 2f, D / 2f - 1f), new Vector3(0.2f, H, D), true);                       // walls
        Add(1, new Vector3(W + 0.1f, H / 2f, D / 2f - 1f), new Vector3(0.2f, H, D), true);
        Add(1, new Vector3(0f, H / 2f, -1.1f), new Vector3(2f * W, H, 0.2f), true);
        Add(1, new Vector3(0f, H / 2f, D - 0.9f), new Vector3(2f * W, H, 0.2f), true);
        Add(1, new Vector3(0f, H + 0.1f, D / 2f - 1f), new Vector3(2f * W, 0.2f, D), true);                       // roof
        Add(2, new Vector3(0f, 1.05f, -0.98f), new Vector3(1.1f, 2.1f, 0.05f), false);                           // the door you came in by
        // racking: three long rows with a cross aisle half way
        foreach (float x in new[] { -7f, 0f, 7f })
            foreach (Vector2 run2 in new[] { new Vector2(6f, 14.5f), new Vector2(17.5f, 26f) })
            {
                float zc = (run2.x + run2.y) / 2f, len = run2.y - run2.x;
                Add(3, new Vector3(x, 1.6f, zc), new Vector3(1.3f, 3.2f, len), true);
                for (int s = 0; s < 3; s++) Add(4, new Vector3(x, 0.55f + s * 1.05f, zc), new Vector3(1.1f, 0.6f, len - 0.4f), false);
            }
        // crates by the door
        foreach (Vector3 c in new[] { new Vector3(-9f, 0.6f, 2.5f), new Vector3(-9f, 1.8f, 2.5f), new Vector3(8.5f, 0.6f, 3f), new Vector3(-4f, 0.6f, 3.5f) })
            Add(4, c, new Vector3(1.4f, 1.2f, 1.4f), true);
        // the office: glass on the front with a doorway, a desk and the safe
        Add(5, new Vector3(-3.6f, 1.4f, 29f), new Vector3(4.8f, 2.8f, 0.08f), true);
        Add(5, new Vector3(3.6f, 1.4f, 29f), new Vector3(4.8f, 2.8f, 0.08f), true);
        Add(1, new Vector3(-6f, 1.4f, 32f), new Vector3(0.15f, 2.8f, 6f), true);
        Add(1, new Vector3(6f, 1.4f, 32f), new Vector3(0.15f, 2.8f, 6f), true);
        Add(1, new Vector3(0f, 2.85f, 32f), new Vector3(12f, 0.1f, 6.1f), false);
        Add(3, new Vector3(-3f, 0.4f, 33f), new Vector3(2.2f, 0.8f, 1f), true);                                 // desk
        Add(6, new Vector3(0f, 0.5f, 34.2f), new Vector3(0.9f, 1f, 0.8f), true);                                // the safe
        g.AddComponent<MeshFilter>().sharedMesh = k.Build("Warehouse9Inside");
        g.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            LundunMeshKit.Mat("W9Floor", new Color(0.35f, 0.35f, 0.33f), 0.25f, Color.black),
            LundunMeshKit.Mat("W9Wall", new Color(0.3f, 0.32f, 0.34f), 0.15f, Color.black),
            LundunMeshKit.Mat("W9DoorIn", new Color(0.12f, 0.2f, 0.3f), 0.4f, Color.black),
            LundunMeshKit.Mat("W9Rack", new Color(0.12f, 0.25f, 0.55f), 0.4f, Color.black),
            LundunMeshKit.Mat("W9Box", new Color(0.55f, 0.42f, 0.25f), 0.2f, Color.black),
            LundunMeshKit.Mat("W9Glass", new Color(0.4f, 0.55f, 0.6f), 0.95f, new Color(0.1f, 0.14f, 0.15f)),
            LundunMeshKit.Mat("W9Safe", new Color(0.15f, 0.15f, 0.16f), 0.7f, Color.black),
        };
        foreach (Bounds b in solid) { BoxCollider c = g.AddComponent<BoxCollider>(); c.center = b.center; c.size = b.size; }
        // the bag on the safe
        GameObject bag = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(bag.GetComponent<Collider>());
        bag.transform.SetParent(g.transform, false);
        bag.transform.localPosition = new Vector3(0f, 1.15f, 34.2f); bag.transform.localScale = new Vector3(0.6f, 0.3f, 0.35f);
        bag.GetComponent<Renderer>().sharedMaterial = LundunMissions.ParcelMaterial;
        // dim lights: one over the office, a couple down the aisles
        foreach (Vector3 lp in new[] { new Vector3(0f, 5.5f, 31f), new Vector3(-3.5f, 6f, 10f), new Vector3(3.5f, 6f, 22f) })
        {
            GameObject lg = new GameObject("W9Light");
            lg.transform.SetParent(g.transform, false);
            lg.transform.localPosition = lp;
            Light l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.range = 12f; l.intensity = 0.8f; l.color = new Color(0.7f, 0.8f, 1f); l.shadows = LightShadows.None;
        }
        // the guards and their rounds (the aisles, the cross aisle, in front of the office)
        List<Vector3[]> rounds = new List<Vector3[]>
        {
            new[] { Inside(new Vector3(-3.5f, 0f, 4f)), Inside(new Vector3(-3.5f, 0f, 27f)) },
            new[] { Inside(new Vector3(3.5f, 0f, 27f)), Inside(new Vector3(3.5f, 0f, 4f)) },
            new[] { Inside(new Vector3(-10f, 0f, 16f)), Inside(new Vector3(10f, 0f, 16f)) },
        };
        if (run.difficulty != LundunDifficulty.Easy) rounds.Add(new[] { Inside(new Vector3(-9f, 0f, 28f)), Inside(new Vector3(9f, 0f, 28f)) });
        if (run.difficulty == LundunDifficulty.Hard) rounds.Add(new[] { Inside(new Vector3(10.5f, 0f, 6f)), Inside(new Vector3(10.5f, 0f, 26f)), Inside(new Vector3(-10.5f, 0f, 26f)), Inside(new Vector3(-10.5f, 0f, 6f)) });
        LundunPopulation pop = LundunPopulation.Instance;
        for (int i = 0; i < rounds.Count && pop != null; i++)
        {
            GameObject p = pop.NewPerson(null, "Street");
            if (p == null) continue;
            NPCBrain b = p.GetComponent<NPCBrain>(); if (b != null) b.enabled = false;
            NavMeshAgent a = p.GetComponent<NavMeshAgent>(); if (a != null) a.enabled = false;
            Vector3[] rt = rounds[i];
            Vector3 start = rt[0] + Vector3.up * 0.05f;
            p.transform.SetPositionAndRotation(start, Quaternion.LookRotation(rt[1 % rt.Length] - rt[0]));
            p.name = "W9Guard_" + i;
            p.SetActive(true);
            foreach (Animator an in p.GetComponentsInChildren<Animator>(true)) an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Health h = p.GetComponent<Health>(); if (h != null) h.Invulnerable = true;      // no fight in here: they raise the alarm
            LundunGuard guard = p.AddComponent<LundunGuard>();
            Vector3[] loop = new Vector3[rt.Length];
            for (int n = 0; n < rt.Length; n++) loop[n] = rt[(1 + n) % rt.Length];
            guard.route = loop;
            guards.Add(guard);
            run.Spawn(p);
        }
        return g;
    }

    static void Words(Transform parent, string text, Vector3 at, float yaw, Vector2 box, float size, Color colour)
    {
        GameObject g = new GameObject("Words", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        g.transform.localPosition = at;
        g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        ((RectTransform)g.transform).sizeDelta = box;
        TextMeshPro t = g.AddComponent<TextMeshPro>();
        t.text = text; t.color = colour; t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center;
        t.enableAutoSizing = true; t.fontSizeMin = 0.2f; t.fontSizeMax = size; t.textWrappingMode = TextWrappingModes.NoWrap;
    }
}

/// S8: fly the helicopter to a pad and put it down on it. The straight purple line and the pointer show the way;
/// the landing pad has the tall waypoint beam. Wrecked: the mission fails. James can't get out until it's done.
public class LundunFly : LundunStep
{
    readonly Func<LundunFlyHeli> heli;
    public override Transform Follow { get { return null; } }

    public LundunFly(Func<LundunFlyHeli> which, Vector3 pad, string objectiveText)
    {
        heli = which; objective = objectiveText;
        hasMarker = true; marker = pad; markerRadius = 8f; air = true; route = false;
    }

    public override void Begin(LundunMissionRun run)
    {
        LundunFlyHeli h = heli();
        if (h != null) h.Hint = null;
    }

    public override bool Tick(LundunMissionRun run)
    {
        LundunFlyHeli h = heli();
        if (h == null || h.Wrecked) { run.Fail("The helicopter went down"); return false; }
        if (!h.Occupied) { run.Fail("You left the helicopter"); return false; }
        float d = LundunMissionRun.FlatDistance(h.transform.position, marker);
        if (h.Ready && h.HeightAboveGround > 3f) h.Hint = d < 40f ? "Over the pad: hold DOWN to land" : null;
        if (d < markerRadius && h.Landed && h.Ready) { h.Hint = null; LundunHaptics.Medium(); return true; }
        return false;
    }
}

/// S8: a moment of black (going in or out of a building in a mission): fades back in by itself.
public class LundunFlash : MonoBehaviour
{
    static LundunFlash instance;
    Image img;
    float from, at, length;

    public static void Black(float seconds)
    {
        if (instance == null)
        {
            GameObject g = new GameObject("LundunFlash", typeof(RectTransform));
            DontDestroyOnLoad(g);
            Canvas c = g.AddComponent<Canvas>(); c.renderMode = RenderMode.ScreenSpaceOverlay; c.sortingOrder = 90;
            GameObject ig = new GameObject("Black", typeof(RectTransform));
            ig.transform.SetParent(g.transform, false);
            RectTransform r = (RectTransform)ig.transform; r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            instance = g.AddComponent<LundunFlash>();
            instance.img = ig.AddComponent<Image>();
            instance.img.raycastTarget = false;
        }
        instance.gameObject.SetActive(true);
        instance.at = Time.unscaledTime; instance.length = Mathf.Max(0.1f, seconds);
        instance.img.color = Color.black;
    }

    void Update()
    {
        float t = (Time.unscaledTime - at) / length;
        img.color = new Color(0f, 0f, 0f, Mathf.Clamp01(1f - t * t));
        if (t >= 1f) gameObject.SetActive(false);
    }
}
