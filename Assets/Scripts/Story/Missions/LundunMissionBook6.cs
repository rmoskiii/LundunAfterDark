using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

// ======================================================================================================================
/// 09 Paper Trail (S6). Night. Uncle Femi (staying at James's since the shop was smashed) wants Hollis's ledger:
/// every debt and every favour, kept in the cabin at his yard (the container yard off Windmill Row, Brixtun, dressed as
/// HOLLIS RECLAMATION). Whoever trusts James more comes: Kemi keeps watch by the car; Marcus comes over the fence.
/// Guards walk the aisles with torches: stay out of the light (the meter at the top). Seen: the alarm, a fight in the
/// yard, then Hollis's men after you in cars. Unseen: in and out like a ghost (gold needs that).
/// Phase 5 (C3): the yard has three cameras on poles, sweeping slowly, their cones on the ground (they fill the same
/// meter as the torches). Ways past: time the sweep, shoot them with the suppressed pistol (any other gun is heard), or
/// the fuse box inside the fence: cameras and floodlights dark for 60 seconds. The cabin has a recorder: WIPE IT
/// (optional, +£500; gold needs it: "never there"). Left alone, whatever the cameras saw is kept (LundunEvidence
/// "yard_recorder"). The ally helps (LundunYardHelp): Kemi, by the car, calls out guards heading for James; Marcus
/// can take one guard down quietly, once.
public class M09PaperTrail : LundunMission
{
    public const string Key = "m09";
    static readonly Vector3 YardMiddle = new Vector3(1141.5f, 0f, -135f);
    static readonly Vector3 Cabin = new Vector3(1141.5f, 0f, -86f);

    readonly List<LundunGuard> guards = new List<LundunGuard>();
    readonly List<Health> guardHealth = new List<Health>();
    public const string Evidence = "yard_recorder";
    readonly List<LundunMissionCam> yardCams = new List<LundunMissionCam>();
    readonly List<Light> floods = new List<Light>();
    LundunRecorder recorder;
    bool camSaw;
    GameObject ledger;
    Vector3 gap, park, ledgerAt;
    bool alarm, alarmOut, got, resolved;            // alarmOut: seen on the way back out (with the ledger)

    public override string Id { get { return "m09_paper_trail"; } }
    public override string Title { get { return "Paper Trail"; } }
    public override string Teaser { get { return "Hollis keeps every debt in one book, in a cabin at his yard. Uncle Femi wants it. Tonight, and quietly."; } }
    public override string Giver { get { return "UNCLE FEMI"; } }
    public override long Reward { get { return 3000; } }
    public override string MedalNote { get { return "never seen, recorder wiped, under 9 minutes"; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m08_blood_is_thicker") && !LundunMissionBook.Done(d, Id); }
    // Uncle Femi stands by James's front door while the shop's smashed: the marker's placed so he's right there
    public override Vector3 StartAt { get { return FemiAtHome.Marker; } }
    public override Vector3 GiverFacing { get { return FemiAtHome.Facing; } }

    /// Kemi if she trusts James more than Marcus does; otherwise Marcus.
    static string Ally { get { return LundunTrust.Of("KEMI") > LundunTrust.Of("MARCUS") ? "KEMI" : "MARCUS"; } }

    public override void Build(LundunMissionRun run)
    {
        string ally = Ally;
        run.Zone(YardMiddle, 110f);
        gap = FindGap(); park = Park(gap);
        run.Add(new LundunDo(r =>
        {
            if (resolved) return;
            alarm = false; alarmOut = false; got = false;
            if (LundunClock.Instance != null && LundunClock.Instance.Hour > 4f && LundunClock.Instance.Hour < 22.5f) LundunClock.Instance.SetHour(23.3f);
        }));
        run.Add(new LundunTalkPick(() => LundunTrust.Has(LundunTrust.HollisFavour)
            ? new[] { new LundunLine("UNCLE FEMI", "That man keeps a book. Every debt, every favour. Mine is in it, because of you."),
                      new LundunLine("UNCLE FEMI", "It's in a cabin at his yard off Windmill Row. Bring it to me, and the favour dies with it.") }
            : new[] { new LundunLine("UNCLE FEMI", "That man keeps a book. Every debt, every favour, every shop he squeezes."),
                      new LundunLine("UNCLE FEMI", "It's in a cabin at his yard off Windmill Row. Bring it to me.") }));
        run.Add(new LundunTalkPick(() => ally == "KEMI"
            ? new[] { new LundunLine("JAMES", "I'll plan it. And after?"), new LundunLine("UNCLE FEMI", "Then we stop being afraid. Take Kemi. She has the sense you don't.") }
            : new[] { new LundunLine("JAMES", "I'll plan it. And after?"), new LundunLine("UNCLE FEMI", "Then we stop being afraid. Take Marcus. Quietly, mind. Both of you.") }));
        run.Add(new LundunJoin(ally));
        run.Add(new LundunGoTo(park, 12f, "Drive to Hollis's yard on Windmill Row"));
        // the yard: guards, the cabin, the ledger
        run.Add(new LundunDo(r =>
        {
            if (resolved) return;
            BuildYard(r);
            YardSecurity(r, ally);
            if (ally == "KEMI")
            {
                Transform j = LundunMissionRun.James();
                Vector3 at = j != null ? j.position + j.right * 2.5f : park;
                LundunCrew.Stage("KEMI", LundunMissionRun.Ground(at), j != null ? j.eulerAngles.y : 0f);
            }
        }));
        run.Add(new LundunTalkPick(() => ally == "KEMI"
            ? new[] { new LundunLine("KEMI", "Your plan, your call. I'll stay with the car. Torches mean guards, so stay out of the light."),
                      new LundunLine("KEMI", "Find a gap in the fence. And James? Walk. Don't run.") }
            : new[] { new LundunLine("MARCUS", "Guards. Torches. Your call, James. Keep out of their light and nobody gets hurt."),
                      new LundunLine("MARCUS", "There's a gap in the fence somewhere. Walk, don't run. They'll hear you."),
                      new LundunLine("MARCUS", "Get me behind one of them and I'll put him to sleep. Once. Don't waste it.") }));
        run.Add(new LundunTalk("",
            new LundunLine("JAMES", "Cameras too. Three of them, on poles. And there's a recorder in that cabin."),
            new LundunLine("JAMES", "There'll be a fuse box somewhere. Kill the power and they're blind for a minute.")));
        run.Add(new LundunWhen("Find the gap in the fence", r =>
        {
            Transform j = LundunMissionRun.James();
            return j != null && !LundunMissionRun.Driving() && LundunMissionRun.FlatDistance(j.position, gap) < 6f;
        }).Mark(gap, 3f, false)).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunSneak(() => guards, Cams, () =>
        {
            Transform j = LundunMissionRun.James();
            if (j == null || LundunMissionRun.FlatDistance(j.position, ledgerAt) > 1.5f) return false;
            got = true;
            if (ledger != null) Object.Destroy(ledger);
            LundunMissions.Toast("Got Hollis's ledger");
            return true;
        }, Alarm, "Get Hollis's ledger from the cabin. Stay out of the torchlight").Mark(Cabin, 2.5f, false));
        // seen on the way in: a fight in the yard, then the ledger
        run.Add(new LundunIf(() => alarm && !got, new LundunTalk("", new LundunLine("GUARD", "OI! Who's that? Get him!"))));
        run.Add(new LundunIf(() => alarm && !got, new LundunFight(() => guardHealth, "Hollis's guards: deal with them") { harder = 1.2f }));
        run.Add(new LundunIf(() => alarm && !got, new LundunCollect(Cabin + new Vector3(0f, 0f, -2.6f), "ledger", "Grab the ledger")));
        run.Add(new LundunNow(r => got = true));
        // out again: quietly if they still haven't seen him
        run.Add(new LundunIf(() => !alarm, new LundunSneak(() => guards, Cams, () =>
        {
            Transform j = LundunMissionRun.James();
            return j != null && (LundunMissionRun.FlatDistance(j.position, gap) < 4f || LundunMissionRun.FlatDistance(j.position, YardMiddle) > 90f);
        }, Alarm, "Get back out through the fence. Don't let them see you").Mark(gap, 3f, false)));
        run.Add(new LundunIf(() => alarmOut, new LundunTalk("", new LundunLine("GUARD", "He's got the book! STOP HIM!"))));
        run.Add(new LundunIf(() => alarmOut, new LundunFight(() => guardHealth, "Fight your way out") { harder = 1.2f }));
        run.Add(new LundunJoin(ally));
        run.Add(new LundunNow(r =>
        {
            resolved = true;
            // what the yard's recorder has on him, if it wasn't wiped
            if (recorder != null && recorder.IsWiped) { }                          // (Wiped already set it)
            else if (camSaw || alarm) LundunEvidence.Recorded(Evidence);
        }));
        run.Add(new LundunTalkPick(() => alarm
            ? (ally == "KEMI"
                ? new[] { new LundunLine("KEMI", "I heard the shouting from here! Get in, GET IN!") }
                : new[] { new LundunLine("MARCUS", "So much for quiet. Car. Now.") })
            : (ally == "KEMI"
                ? new[] { new LundunLine("KEMI", "You've got it? You actually got it. Drive. Slowly. Like a nobody.") }
                : new[] { new LundunLine("MARCUS", "In and out, like a ghost. Dad would've been impressed. Let's go.") }))).Checkpoint(LundunCheckpoint.Minor);
        run.Add(new LundunIf(() => alarm, new LundunChase(2, "Hollis's men are after you: lose them")));
        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunDo(r =>
        {
            LundunCrew.Stage("UNCLE FEMI", FemiAtHome.Spot, FemiAtHome.Yaw);
        }));
        run.Add(new LundunGoTo(LundunStoryPlaces.Home, 6f, "Take the ledger to Uncle Femi"));
        run.Add(new LundunTalkPick(() => new[]
        {
            new LundunLine("UNCLE FEMI", "Let me see. ...Every name. Every shop on Rye Lane. Mine, on this page."),
            new LundunLine("UNCLE FEMI", "And here: who he pays. Builders, glaziers, a councillor. He smashed my shop and paid himself to fix the street."),
            ally == "KEMI" ? new LundunLine("KEMI", "So now we fix it ourselves. Properly.") : new LundunLine("MARCUS", "So now we fix it ourselves. And if he comes, he comes."),
            new LundunLine("UNCLE FEMI", "Tomorrow, then. LAGOS FOODS opens again."),
        }));
        run.Add(new LundunLeave(ally));
    }

    void Alarm()
    {
        if (got) alarmOut = true;
        alarm = true;
        if (!got && ledger != null) Object.Destroy(ledger);                      // it's picked up after the fight
        foreach (LundunGuard g in guards) if (g != null) g.Wake();
        LundunMissions.Toast("You've been seen!");
    }

    public override int Medal(LundunMissionRun run)
    {
        bool fast = run.Elapsed < 540f;
        int m = !alarm && fast ? 3 : (!alarm || fast) ? 2 : 1;
        if (!run.OptionalDone("wipe_recorder")) m = Mathf.Min(m, 2);              // gold: never there
        return m;
    }

    public override void Tidy() { guards.Clear(); guardHealth.Clear(); ledger = null; resolved = false; yardCams.Clear(); floods.Clear(); recorder = null; }

    // ------------------------------------------------------------------ Phase 5: the yard's cameras, fuse box, recorder
    /// The cameras, for the sneak meter (and noting whether any of them had him: the recorder keeps that).
    List<LundunMissionCam> Cams()
    {
        Transform j = LundunMissionRun.James();
        if (!camSaw && j != null) foreach (LundunMissionCam c in yardCams) if (c != null && c.Sees(j.position) > 0f) { camSaw = true; break; }
        return yardCams;
    }

    void YardSecurity(LundunMissionRun run, string ally)
    {
        yardCams.Clear(); camSaw = false;
        // three on poles: down the west aisle, the east aisle, and over the front of the cabin
        Vector3[] at = { P(1109.3f, -150f), P(1173.7f, -128f), P(1151.5f, -99f) };   // (at the aisles' edges)
        float[] look = { 0f, 180f, 225f };
        for (int i = 0; i < at.Length; i++)
        {
            LundunMissionCam c = LundunMissionCam.CreateOnPole(run, "yard_" + i, at[i], 4.4f, look[i], 45f, 14f, 26f, -26f);
            c.sweepSeconds = 10f;
            yardCams.Add(c);
        }
        // the fuse box, on a post just inside the fence, beside the gap: the cameras and the floodlights
        Vector3 fb = LundunMissionRun.Ground(gap + new Vector3(5f, 0f, 6f));
        LundunFuseBox box = LundunFuseBox.Create(run, fb, 180f, yardCams, floods);
        box.seconds = 60f;
        // the recorder, by the cabin's door
        Vector3 cg = LundunMissionRun.Ground(Cabin);
        recorder = LundunRecorder.Create(run, Evidence, cg + new Vector3(2.4f, 0f, -1.7f), 180f);
        recorder.Wiped += () => run.CompleteOptional("wipe_recorder");
        run.AddOptional("wipe_recorder", "WIPE THE RECORDER by the cabin door", 500);
        // the ally's help
        LundunYardHelp help = LundunYardHelp.Create(run, ally, () => guards, () => !alarm);
        help.TookDown += g =>
        {
            guards.Remove(g);
            Health h = g.GetComponent<Health>();
            if (h != null) guardHealth.Remove(h);
        };
    }

    // ------------------------------------------------------------------ the yard
    /// A gap in the yard's fence on its south side (nearest the middle), where a man can walk in.
    static Vector3 FindGap()
    {
        int mask = LayerMask.GetMask("Default");
        float bestX = 1140f, bestD = float.MaxValue;
        float runStart = float.NaN;
        for (float x = 1076f; x <= 1600f; x += 0.5f)
        {
            Vector3 from = new Vector3(x, 0f, -186f); from = LundunMissionRun.Ground(from) + Vector3.up * 1f;
            bool blocked = Physics.Raycast(from, Vector3.back, 12f, mask, QueryTriggerInteraction.Ignore);
            if (!blocked) { if (float.IsNaN(runStart)) runStart = x; continue; }
            if (!float.IsNaN(runStart))
            {
                if (x - runStart >= 3f)
                {
                    float mid = (runStart + x) / 2f, d = Mathf.Abs(mid - 1140f);
                    if (d < bestD) { bestD = d; bestX = mid; }
                }
                runStart = float.NaN;
            }
        }
        return LundunMissionRun.Ground(new Vector3(bestX, 0f, -192f));
    }

    static Vector3 Park(Vector3 gap)
    {
        Vector3 p; Quaternion q;
        return LundunStoryPlaces.Kerb(gap + new Vector3(0f, 0f, -12f), out p, out q) ? p : LundunMissionRun.Ground(gap + new Vector3(0f, 0f, -14f));
    }

    void BuildYard(LundunMissionRun run)
    {
        guards.Clear(); guardHealth.Clear(); floods.Clear();
        Transform parent = LundunMissions.Instance != null ? LundunMissions.Instance.transform : null;
        // the cabin (the yard's office), the ledger on the table outside its door, floodlights, the name on the fence
        GameObject cabin = new GameObject("HollisCabin");
        cabin.transform.SetParent(parent, false);
        Vector3 cg = LundunMissionRun.Ground(Cabin);
        cabin.transform.position = cg;
        LundunMeshKit k = new LundunMeshKit(5);
        k.Box(0, new Vector3(0, 1.35f, 0), new Vector3(6f, 2.7f, 2.6f));                      // portacabin
        k.Box(4, new Vector3(0, 2.75f, 0), new Vector3(6.1f, 0.1f, 2.7f));
        k.Box(1, new Vector3(0, 1.0f, -1.31f), new Vector3(0.9f, 2.0f, 0.04f));                // door
        k.Box(2, new Vector3(-1.9f, 1.6f, -1.31f), new Vector3(1.4f, 0.8f, 0.04f));            // lit window
        k.Box(2, new Vector3(1.9f, 1.6f, -1.31f), new Vector3(1.4f, 0.8f, 0.04f));
        k.Box(3, new Vector3(0, 0.12f, -1.7f), new Vector3(1.2f, 0.24f, 0.8f));                // steps
        k.Box(3, new Vector3(0, 0.45f, -2.6f), new Vector3(0.9f, 0.9f, 0.6f));                 // a crate for a table
        cabin.AddComponent<MeshFilter>().sharedMesh = k.Build("HollisCabin");
        cabin.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            LundunMeshKit.Mat("CabinWall", new Color(0.22f, 0.3f, 0.38f), 0.3f, Color.black),
            LundunMeshKit.Mat("CabinDoor", new Color(0.1f, 0.1f, 0.11f), 0.3f, Color.black),
            LundunMeshKit.Mat("CabinWindow", new Color(0.6f, 0.5f, 0.3f), 0.8f, new Color(1f, 0.8f, 0.45f) * 1.2f),
            LundunMeshKit.Mat("CabinCrate", new Color(0.45f, 0.32f, 0.18f), 0.2f, Color.black),
            LundunMeshKit.Mat("CabinRoof", new Color(0.3f, 0.3f, 0.32f), 0.2f, Color.black),
        };
        BoxCollider bc = cabin.AddComponent<BoxCollider>(); bc.center = new Vector3(0, 1.35f, 0); bc.size = new Vector3(6f, 2.7f, 2.6f);
        Sign(cabin.transform, "HOLLIS RECLAMATION", new Vector3(0f, 2.35f, -1.34f), new Vector2(4.5f, 0.4f));
        run.Spawn(cabin);
        ledgerAt = cg + new Vector3(0f, 0f, -2.6f);
        ledger = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(ledger.GetComponent<Collider>());
        ledger.name = "HollisLedger";
        ledger.transform.position = ledgerAt + new Vector3(0f, 0.93f, 0f);
        ledger.transform.localScale = new Vector3(0.3f, 0.06f, 0.22f);
        ledger.GetComponent<Renderer>().sharedMaterial = LundunMeshKit.Mat("Ledger", new Color(0.45f, 0.06f, 0.05f), 0.4f, new Color(0.5f, 0.1f, 0.05f) * 0.6f);
        run.Spawn(ledger);
        // a board by the gap
        GameObject board = new GameObject("YardSign");
        board.transform.SetParent(parent, false);
        board.transform.position = gap + new Vector3(4.5f, 0f, -0.3f);
        board.transform.rotation = Quaternion.LookRotation(Vector3.back);
        GameObject plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(plank.GetComponent<Collider>());
        plank.transform.SetParent(board.transform, false);
        plank.transform.localPosition = new Vector3(0f, 1.6f, 0f); plank.transform.localScale = new Vector3(3.2f, 0.7f, 0.05f);
        plank.GetComponent<Renderer>().sharedMaterial = LundunMeshKit.Mat("YardSignBoard", new Color(0.85f, 0.82f, 0.7f), 0.2f, Color.black);
        Sign(board.transform, "HOLLIS RECLAMATION\n<size=60%>KEEP OUT · DOGS ON PATROL</size>", new Vector3(0f, 1.6f, -0.04f), new Vector2(3f, 0.6f), new Color(0.6f, 0.05f, 0.05f));
        run.Spawn(board);
        foreach (Vector3 fp in new[] { new Vector3(1106f, 0f, -96f), new Vector3(1177f, 0f, -96f) })
        {
            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.transform.SetParent(parent, false);
            pole.transform.position = LundunMissionRun.Ground(fp) + Vector3.up * 4f;
            pole.transform.localScale = new Vector3(0.18f, 4f, 0.18f);
            GameObject lampGo = new GameObject("Flood");
            lampGo.transform.SetParent(pole.transform, false);
            lampGo.transform.position = LundunMissionRun.Ground(fp) + Vector3.up * 7.8f;
            lampGo.transform.rotation = Quaternion.LookRotation((Cabin - fp).normalized * 0.4f + Vector3.down);
            Light l = lampGo.AddComponent<Light>();
            l.type = LightType.Spot; l.range = 22f; l.spotAngle = 75f; l.intensity = 3.5f; l.color = new Color(0.85f, 0.9f, 1f); l.shadows = LightShadows.None;
            floods.Add(l);                                                     // Phase 5: the fuse box cuts these
            run.Spawn(pole);
        }
        // the guards and their rounds (the aisles between the containers, and along the front of the cabin)
        List<Vector3[]> rounds = new List<Vector3[]>
        {
            new[] { P(1107.5f, -176f), P(1107.5f, -104f), P(1133f, -104f), P(1133f, -176f) },
            new[] { P(1150f, -104f), P(1150f, -176f), P(1175.5f, -176f), P(1175.5f, -104f) },
            new[] { P(1118f, -95f), P(1166f, -95f) },
        };
        if (run.difficulty != LundunDifficulty.Easy) rounds.Add(new[] { P(1141.5f, -176f), P(1141.5f, -106f) });
        if (run.difficulty == LundunDifficulty.Hard) rounds.Add(new[] { P(1124.5f, -106f), P(1124.5f, -176f), P(1158.5f, -176f), P(1158.5f, -106f) });
        LundunPopulation pop = LundunPopulation.Instance;
        for (int i = 0; i < rounds.Count && pop != null; i++)
        {
            GameObject g = pop.NewPerson(null, "Street");
            if (g == null) continue;
            NPCBrain b = g.GetComponent<NPCBrain>(); if (b != null) b.enabled = false;
            NavMeshAgent a = g.GetComponent<NavMeshAgent>(); if (a != null) a.enabled = false;
            Vector3[] rt = rounds[i];
            int start = i % rt.Length;
            g.transform.SetPositionAndRotation(rt[start], Quaternion.LookRotation(rt[(start + 1) % rt.Length] - rt[start]));
            g.name = "HollisGuard_" + i;
            g.SetActive(true);
            LundunGuard guard = g.AddComponent<LundunGuard>();
            Vector3[] loop = new Vector3[rt.Length];
            for (int n = 0; n < rt.Length; n++) loop[n] = rt[(start + 1 + n) % rt.Length];
            guard.route = loop;
            guards.Add(guard);
            Health h = g.GetComponent<Health>();
            if (h != null) guardHealth.Add(h);
            run.Spawn(g);
        }
        LundunMinimap mm = Object.FindAnyObjectByType<LundunMinimap>();
        if (mm != null) foreach (LundunGuard g in guards) mm.Track(g.transform, new Color(0.95f, 0.25f, 0.2f), "", 12f, false);
    }

    static Vector3 P(float x, float z) { return LundunMissionRun.Ground(new Vector3(x, 0f, z)); }

    static void Sign(Transform parent, string text, Vector3 at, Vector2 box, Color? colour = null)
    {
        GameObject g = new GameObject("Words", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        g.transform.localPosition = at;
        g.transform.localRotation = Quaternion.identity;
        ((RectTransform)g.transform).sizeDelta = box;
        TextMeshPro t = g.AddComponent<TextMeshPro>();
        t.text = text; t.color = colour ?? Color.white; t.fontStyle = FontStyles.Bold; t.alignment = TextAlignmentOptions.Center;
        t.enableAutoSizing = true; t.fontSizeMin = 0.2f; t.fontSizeMax = 3f; t.textWrappingMode = TextWrappingModes.Normal; t.richText = true;
    }
}

/// Where Uncle Femi stands by James's front door while LAGOS FOODS is smashed (LundunStoryShop puts him there), and a
/// start marker placed so a mission's giver (him) stands on exactly that spot.
public static class FemiAtHome
{
    static Vector3 F { get { return Quaternion.Euler(0f, LundunStoryPlaces.HomeYaw, 0f) * Vector3.forward; } }
    static Vector3 S { get { return Vector3.Cross(Vector3.up, F); } }
    public static Vector3 Spot { get { return LundunMissionRun.Ground(LundunStoryPlaces.Home + F * 2.2f - S * 2.2f); } }
    public static float Yaw { get { return LundunStoryPlaces.HomeYaw; } }
    public static Vector3 Facing { get { return F; } }
    /// The giver stands at marker + side × 1.6 + facing × 0.4 (LundunMissions.GiverWaits).
    public static Vector3 Marker { get { return Spot - S * 1.6f - F * 0.4f; } }
}

// ======================================================================================================================
/// 10 Open For Business (S6). Uncle Femi's ready: take the van round for what the shop needs (glass from ValuStack,
/// shelving from FLATPAK, stock from Grocera), bring it to LAGOS FOODS, and the crew put it right. As the shutters go
/// up, Hollis's men turn up: see them off. LAGOS FOODS is open again ("lagos:smashed" cleared): Uncle Femi's back behind
/// his counter. Tayo's there helping if he was let go (08); Hollis's favour (05) gets a line.
/// Phase 5 (C3): after the first pick-up one of Hollis's spotters tails the van (a grey car hanging back, never closing
/// in). LOSE THE SPOTTER (optional, +£300): shake him or wreck him before the shop and four of Hollis's men come; let
/// him follow you there and he's brought friends (seven). Uncle Femi's had a camera put up over the new shop front
/// ("lagos:camera": it stays, in free play too), and the defence opens on OUR camera's picture: Hollis's men coming up
/// Rye Lane, the tables turned.
public class M10OpenForBusiness : LundunMission
{
    GameObject van;
    int loaded;
    bool resolved;
    LundunChaseCar spotter;
    bool tailed;                                       // the spotter followed the van all the way to the shop
    readonly List<Health> hollisMen = new List<Health>();

    public override string Id { get { return "m10_open_for_business"; } }
    public override string Title { get { return "Open For Business"; } }
    public override string Teaser { get { return "Glass, shelves, stock. James is running the rebuild, and LAGOS FOODS opens again today."; } }
    public override string Giver { get { return "UNCLE FEMI"; } }
    public override long Reward { get { return 4500; } }          // Phase 6: was 4,000
    public override string MedalNote { get { return "under 10 minutes, van not badly damaged"; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m09_paper_trail") && !LundunMissionBook.Done(d, Id); }
    public override Vector3 StartAt { get { return FemiAtHome.Marker; } }
    public override Vector3 GiverFacing { get { return FemiAtHome.Facing; } }

    static Vector3 Supply(string store, Vector3 fallback)
    {
        Vector3 outside, inside;
        return LundunStoryPlaces.Store(store, out outside, out inside) ? outside : fallback;
    }

    public override void Build(LundunMissionRun run)
    {
        bool tayo = LundunChoice.Chose("choice:m08", false);                        // let go: he turns up to help
        run.Zone(LundunStoryPlaces.LagosDoor, 60f);
        run.Add(new LundunDo(r =>
        {
            if (LundunClock.Instance != null && (LundunClock.Instance.Hour < 8f || LundunClock.Instance.Hour > 19f)) LundunClock.Instance.SetHour(9.5f);
            if (resolved) return;
            loaded = 0;
            if (LundunStoryPlaces.Fine(van)) return;
            LundunPopulation pop = LundunPopulation.Instance;
            int look = pop == null ? 0 : Mathf.Max(0, pop.LookIndex("delivery") >= 0 ? pop.LookIndex("delivery") : pop.LookIndex("Minivan"));
            van = LundunStoryPlaces.ParkCar(LundunStoryPlaces.Home + Quaternion.Euler(0f, LundunStoryPlaces.HomeYaw, 0f) * Vector3.forward * 10f, look);
            if (van != null) r.Spawn(van);
        }));
        run.Add(new LundunTalkPick(() => new[]
        {
            new LundunLine("UNCLE FEMI", "The insurance won't pay. The bank won't lend. But people are good, James."),
            new LundunLine("UNCLE FEMI", "ValuStack has glass for us. FLATPAK, shelving. Grocera will give me stock on credit."),
            LundunTrust.Has(LundunTrust.HollisFavour)
                ? new LundunLine("UNCLE FEMI", "And the favour you owed that man is ash in my kitchen sink. Take the van.")
                : new LundunLine("UNCLE FEMI", "Take the van. Marcus will help you carry."),
        }));
        run.Add(new LundunJoin("MARCUS"));
        run.Add(new LundunEnterCar(() => van, "Get in the van", "The van's wrecked"));
        run.Add(new LundunVanTo(() => van, () => Supply("ValuStack", new Vector3(1492f, 0f, -560f)), 3f, "Pick up the glass at ValuStack", "Loading the glass", "The van's wrecked"));
        run.Add(new LundunNow(r => { Cargo(); LundunMissions.Toast("Glass loaded  1 / 3"); })).Checkpoint(LundunCheckpoint.Minor);
        // Phase 5: one of Hollis's spotters, hanging back
        run.Add(new LundunNow(r => Spotter(r)));
        run.Add(new LundunIf(() => spotter != null, new LundunTalk("",
            new LundunLine("MARCUS", "Grey car. Three back. It's been on us since ValuStack."),
            new LundunLine("JAMES", "Hollis?"),
            new LundunLine("MARCUS", "One of his spotters. Lose him, or he'll bring his mates to the shop."))));
        run.Add(new LundunVanTo(() => van, () => Supply("FLATPAK Home Store", new Vector3(1420f, 0f, -350f)), 3f, "Pick up the shelving at FLATPAK", "Loading the shelving", "The van's wrecked"));
        run.Add(new LundunNow(r => { Cargo(); LundunMissions.Toast("Shelving loaded  2 / 3"); })).Checkpoint(LundunCheckpoint.Minor);
        run.Add(new LundunTalk("",
            new LundunLine("MARCUS", "Dad used to do this. Saturday mornings, the cash and carry, the old van."),
            new LundunLine("JAMES", "The one with no second gear?"),
            new LundunLine("MARCUS", "That's the one.")));
        run.Add(new LundunVanTo(() => van, () => Supply("Grocera", new Vector3(1272f, 0f, -560f)), 3f, "Pick up the stock at Grocera", "Loading the stock", "The van's wrecked"));
        run.Add(new LundunNow(r => { Cargo(); LundunMissions.Toast("Stock loaded  3 / 3"); })).Checkpoint(LundunCheckpoint.Minor);
        run.Add(new LundunDo(r =>
        {
            LundunStoryShop.MissionOpen = true;
            Vector3 door = LundunStoryPlaces.LagosDoor;
            float y = LundunStoryShop.Instance != null ? LundunStoryShop.Instance.OutYaw : 90f;
            Vector3 o = Quaternion.Euler(0f, y, 0f) * Vector3.forward, a = Vector3.Cross(Vector3.up, o);
            LundunCrew.Stage("UNCLE FEMI", LundunMissionRun.Ground(door + o * 1.5f + a * 1.5f), y + 180f);
            LundunCrew.Stage("KEMI", LundunMissionRun.Ground(door + o * 2.5f - a * 1.2f), y + 200f);
            LundunCrew.Stage("LEAH", LundunMissionRun.Ground(door + o * 3.2f - a * 2.4f), y + 160f);
            if (tayo) LundunCrew.Stage("TAYO", LundunMissionRun.Ground(door + o * 3.5f + a * 2.8f), y + 210f);
        }));
        run.Add(new LundunVanTo(() => van, () => LundunStoryPlaces.LagosDoor, 2f, "Take it all to LAGOS FOODS", "Unloading", "The van's wrecked"));
        run.Add(new LundunNow(r =>
        {
            tailed = spotter != null && !spotter.Lost && !spotter.Wrecked;
            if (spotter != null && !tailed) r.CompleteOptional("lose_spotter");
            if (spotter != null)
            {
                LundunMinimap mm = Object.FindAnyObjectByType<LundunMinimap>();
                if (mm != null) mm.Untrack(spotter.transform);
                spotter.enabled = false;
                LundunMissions.LetGo(spotter.gameObject);
            }
            if (tailed) LundunMissions.Toast("THE SPOTTER FOLLOWED YOU HERE\nHe'll be back with friends");
        }));
        run.Add(new LundunTalkPick(() =>
        {
            List<LundunLine> l = new List<LundunLine>
            {
                new LundunLine("KEMI", "Look at all this! Leah, grab the other end."),
                new LundunLine("LEAH", "Mind your backs. Glass coming through."),
            };
            if (tayo) { l.Add(new LundunLine("TAYO", "I said I'd make it right. Where do you want me, boss?")); l.Add(new LundunLine("MARCUS", "...Shelves. Don't make me regret it.")); }
            else l.Add(new LundunLine("MARCUS", "Tayo's mum sent jollof. Don't ask how I know."));
            l.Add(new LundunLine("UNCLE FEMI", "Everybody, inside. We have a shop to open."));
            return l.ToArray();
        }));
        run.Add(new LundunNow(r =>
        {
            if (LundunClock.Instance != null) LundunClock.Instance.SetHour(LundunClock.Instance.Hour + 4f);
            LundunStoryData d = LundunStory.Data;
            if (d != null && d.flags.Remove(LundunStoryShop.SmashedFlag)) LundunStory.MarkDirty();
            if (d != null && !d.flags.Contains(LundunStoryShop.CameraFlag)) { d.flags.Add(LundunStoryShop.CameraFlag); LundunStory.MarkDirty(); }   // Phase 5: our camera
            if (LundunStoryShop.Instance != null) LundunStoryShop.Instance.Refresh();
            LundunMissions.Toast("A FEW HOURS LATER");
        }));
        run.Add(new LundunTalk("",
            new LundunLine("UNCLE FEMI", "Look at her. Better than before."),
            new LundunLine("UNCLE FEMI", "And look up, James. A camera. Our camera. Let them come and smile for it.")));
        // Hollis's men up the street, as our camera sees them
        run.Add(new LundunDo(r => HollisMen(r, tailed ? 7 : 4))).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunFeedCut(() => LundunStoryShop.Instance != null ? LundunStoryShop.Instance.FrontLens : LundunStoryPlaces.LagosDoor + Vector3.up * 3.2f,
            () => LundunStoryShop.Instance != null ? LundunStoryShop.Instance.FrontLook : LundunStoryPlaces.LagosDoor,
            "LAGOS FOODS  ·  CAM 02  ·  SHOP FRONT", "OUR CAMERA  ·  HOLLIS'S MEN ON RYE LANE", 3.5f, LundunCctvFeed.Mark.Subject,
            () => (LundunStoryShop.Instance != null ? LundunStoryShop.Instance.FrontLook : LundunStoryPlaces.LagosDoor) + Vector3.up * 0.2f, tailed ? "7 SUBJECTS" : "4 SUBJECTS"));
        run.Add(new LundunTalkPick(() => tailed
            ? new[] { new LundunLine("MARCUS", "Seven of them. Told you he'd bring his mates."), new LundunLine("MARCUS", "Your call, James. I'm with you.") }
            : new[] { new LundunLine("MARCUS", "Company. Hollis's lot, coming up the street."), new LundunLine("MARCUS", "Only four. Losing that spotter paid off. Your call, James.") }));
        run.Add(new LundunFight(() => hollisMen, "Hollis's men are here. Defend LAGOS FOODS") { harder = 1.3f });
        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunNow(r => resolved = true));
        run.Add(new LundunDo(r =>
        {
            float fy; Vector3 at = LundunStoryPlaces.Lagos(LundunShopLayout.Behind(LundunStoryPlaces.LagosWidth), out fy);
            LundunCrew.Stage("UNCLE FEMI", at, fy);
        }));
        run.Add(new LundunGoTo(LundunStoryPlaces.LagosDoor, 3f, "Go back to the shop"));
        run.Add(new LundunTalkPick(() =>
        {
            List<LundunLine> l = new List<LundunLine>
            {
                new LundunLine("UNCLE FEMI", "James. Come. First customer gets a free malt."),
                new LundunLine("JAMES", "I'm not a customer, Uncle."),
                new LundunLine("UNCLE FEMI", "Today everybody is a customer. LAGOS FOODS is open!"),
            };
            if (LundunChoice.Chose("choice:m08", true)) l.Add(new LundunLine("KEMI", "Marcus says Tayo asked after you. Just so you know."));
            return l.ToArray();
        }));
    }

    /// Phase 5: the spotter: a grey car that turns up behind the van and hangs back, all the way to the shop if let.
    void Spotter(LundunMissionRun run)
    {
        spotter = null;
        LundunPopulation pop = LundunPopulation.Instance;
        LundunNavigation nav = LundunNavigation.Instance;
        Transform j = LundunMissionRun.James();
        if (pop == null || nav == null || nav.Graph == null || j == null) return;
        System.Random rng = new System.Random();
        Vector3 jp = j.position, jf = j.forward; jf.y = 0f; jf = jf.sqrMagnitude > 0.01f ? jf.normalized : Vector3.forward;
        for (int a = 0; a < 24; a++)
        {
            NavPoint p, dir; double hw;
            if (!nav.Graph.RandomRoadPoint(new NavPoint(jp.x, jp.z), 60, 110, rng, out p, out dir, out hw)) continue;
            Vector3 q = new Vector3((float)p.x, 0f, (float)p.z);
            if (a < 18 && Vector3.Dot(q - jp, jf) > 0f) continue;                      // behind
            Vector3 along = new Vector3((float)dir.x, 0f, (float)dir.z);
            if (Vector3.Dot(jp - q, along) < 0f) along = -along;
            GameObject g = pop.NewCar(LundunMissions.Instance != null ? LundunMissions.Instance.transform : null, LundunMissionRun.Ground(q), Quaternion.LookRotation(along), 4);
            if (g == null) return;
            spotter = g.AddComponent<LundunChaseCar>();
            spotter.tailOnly = true;
            spotter.Begin(run.EnemyDamage);
            run.Spawn(g);
            LundunMinimap mm = Object.FindAnyObjectByType<LundunMinimap>();
            if (mm != null) mm.Track(g.transform, new Color(0.75f, 0.75f, 0.8f), "", 12f, true);
            run.AddOptional("lose_spotter", "LOSE THE SPOTTER before the shop", 300);
            return;
        }
    }

    /// Hollis's men, up the street from the shop, standing waiting (they come for James as the fight starts).
    void HollisMen(LundunMissionRun run, int n)
    {
        hollisMen.Clear();
        LundunPopulation pop = LundunPopulation.Instance;
        Vector3 centre = LundunMissionRun.Ground(LundunStoryShop.Instance != null ? LundunStoryShop.Instance.StreetSpot : LundunStoryPlaces.LagosDoor + Vector3.forward * 16f);
        Vector3 door = LundunStoryPlaces.LagosDoor;
        n += run.ExtraEnemies;
        for (int i = 0; i < n && pop != null; i++)
        {
            float ang = i * Mathf.PI * 2f / n;
            Vector3 p = centre + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * (n > 5 ? 3f : 2.2f);
            if (!NavMesh.SamplePosition(p + Vector3.up, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
            GameObject g = pop.NewPerson(null, "Street");
            if (g == null) continue;
            NPCBrain b = g.GetComponent<NPCBrain>(); if (b != null) b.enabled = false;
            Vector3 face = door - hit.position; face.y = 0f;
            g.transform.SetPositionAndRotation(hit.position, Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.forward));
            g.SetActive(true);
            run.Spawn(g);
            Health h = g.GetComponent<Health>();
            if (h != null) hollisMen.Add(h);
        }
    }

    /// Something in the back of the van for each pick-up.
    void Cargo()
    {
        loaded++;
        if (van == null) return;
        Bounds b = new Bounds(van.transform.position, Vector3.one);
        foreach (Renderer r in van.GetComponentsInChildren<Renderer>()) b.Encapsulate(r.bounds);
        GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(c.GetComponent<Collider>());
        c.name = "Cargo" + loaded;
        c.transform.SetParent(van.transform, true);
        Vector3 top = new Vector3(b.center.x, b.max.y + 0.15f + (loaded - 1) * 0.3f, b.center.z) - van.transform.forward * (b.extents.magnitude * 0.25f);
        c.transform.position = top;
        c.transform.rotation = van.transform.rotation;
        c.transform.localScale = new Vector3(1.2f, 0.28f, 1.6f - loaded * 0.25f);
        Color[] cols = { new Color(0.6f, 0.75f, 0.8f), new Color(0.55f, 0.4f, 0.25f), new Color(0.8f, 0.65f, 0.3f) };
        c.GetComponent<Renderer>().sharedMaterial = LundunMeshKit.Mat("Cargo" + loaded, cols[(loaded - 1) % 3], 0.3f, Color.black);
    }

    public override int Medal(LundunMissionRun run)
    {
        VehicleHealth vh = van != null ? van.GetComponent<VehicleHealth>() : null;
        bool tidy = vh == null || vh.Max <= 0f || vh.Current / vh.Max > 0.5f;
        bool fast = run.Elapsed < 600f;                                       // Phase 6: provisional until the Phase 7 playtest
        return fast && tidy ? 3 : (fast || tidy) ? 2 : 1;
    }

    public override void Tidy() { van = null; resolved = false; loaded = 0; spotter = null; tailed = false; hollisMen.Clear(); }
}
