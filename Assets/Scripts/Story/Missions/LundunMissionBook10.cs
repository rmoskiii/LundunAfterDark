using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

// ======================================================================================================================
/// 12 Lundun After Dark (S9b, the finale). The night of the gala. In by the way chosen at the top of the Wheel (11):
/// up Hollis's scaffolding, the trades entrance, the loose railing, or Tayo's staff passes. Inside the palace wing (a
/// walk-in interior high above the palace, like Warehouse 9): the kitchen corridor, the grand gallery (guards with
/// torches), the strongroom and Hollis's ledger. The strongroom's alarmed: 5★. Out through the state rooms to the
/// gardens and fight through palace security (Marcus at your side if you fought for the market in 07), up the
/// scaffolding to the roof and Hollis's own helicopter, and fly it to Tower Plaza. Hollis is waiting. The last choice:
/// give the ledger to the papers (Hollis arrested) or keep it as leverage (an uneasy partnership). Then the Chapter 2
/// card. £25,000.
public class M12LundunAfterDark : LundunMission
{
    public const string MissionId = "m12_lundun_after_dark";
    public const string Key = "choice:m12";
    const float InY = 300f;
    static readonly Vector3 TowerPlaza = new Vector3(1220f, 0f, 560f);
    static readonly Vector3 FrontGate = new Vector3(616f, 0f, 1700f);
    static readonly Vector3 GardenFight = new Vector3(560f, 0f, 1850f);
    static readonly Vector3 ClimbFoot = new Vector3(662f, 0f, 1822f);

    readonly List<LundunGuard> guards = new List<LundunGuard>();
    GameObject wing, roofPad, landPad;
    LundunFlyHeli heli;
    Vector3 origin, roofAt, landAt;
    Quaternion facing;
    int way;
    bool alarm, resolved, outside, fought;

    public override string Id { get { return MissionId; } }
    public override string Title { get { return "Lundun After Dark"; } }
    public override string Teaser { get { return "The gala. The strongroom. Hollis's ledger. Tonight it ends."; } }
    public override string Giver { get { List<string> f = M11FamilyMeeting.Family(); return f.Count > 1 ? f[1] : "UNCLE FEMI"; } }
    public override long Reward { get { return 25000; } }
    public override string MedalNote { get { return "never seen inside, the helicopter in good shape, under 15 minutes"; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, M11FamilyMeeting.MissionId) && !LundunMissionBook.Done(d, Id); }

    static int Way { get { int w = M11FamilyMeeting.WayIn; return w < 0 ? 0 : w; } }
    static Vector3 Approach(int w) { return w == 3 ? LundunMissionRun.Ground(FrontGate) : LundunPalaceWays.Approach(w); }
    static readonly string[] WayGoal =
    {
        "Get to Hollis Construction's site gate, east of the palace",
        "Get to the trades entrance behind the palace gardens",
        "Get to the loose railing on the west side of the forecourt",
        "Get to the palace's east gate in your waiter's jacket",
    };
    static readonly string[] WayIn =
    {
        "Up the scaffolding, over the roof, down through a skylight...",
        "Through the trades gate with a crate of champagne...",
        "Through the gap in the railings and across the dark forecourt...",
        "\"Evening, sir.\" Straight in through the front, tray in hand...",
    };

    public override Vector3 StartAt
    {
        get
        {
            Vector3 a = Approach(Way);
            Vector3 away = a - LundunPalaceWays.PalaceCentre; away.y = 0f; away = away.sqrMagnitude > 1f ? away.normalized : Vector3.right;
            Vector3 p; Quaternion q;
            return LundunStoryPlaces.Kerb(a + away * 70f, out p, out q) ? LundunMissionRun.Ground(p) : LundunMissionRun.Ground(a + away * 60f);
        }
    }

    public override void Build(LundunMissionRun run)
    {
        way = Way;
        List<string> family = M11FamilyMeeting.Family();
        string lead = family.Count > 1 ? family[1] : "UNCLE FEMI";
        bool marcus = family.Contains("MARCUS"), kemi = family.Contains("KEMI"), favour = LundunTrust.Has(LundunTrust.HollisFavour);
        bool tayoHanded = LundunChoice.Chose("choice:m08", true);
        Vector3 approach = Approach(way);
        // the wing floats above the palace, facing north (in from the south end)
        facing = Quaternion.identity;
        origin = new Vector3(LundunPalaceWays.PalaceCentre.x - 6f, InY, 1740f);
        roofAt = Roof(new Vector3(625f, 0f, 1790f));
        landAt = M15Warehouse9.OpenGround(TowerPlaza, 70f);
        run.Zone(LundunPalaceWays.PalaceCentre, 140f);

        // the night, the wing, the helicopter on the roof and the pad at Tower Plaza (again on a retry)
        run.Add(new LundunDo(r =>
        {
            if (!resolved) { alarm = false; outside = false; fought = false; }
            LundunClock c = LundunClock.Instance;
            if (c != null && c.Hour > 4f && c.Hour < 21.5f) c.SetHour(22.5f);
            Clear();
            wing = r.Spawn(BuildWing(r));
            roofPad = LundunFlyHeli.Pad(roofAt, 6f, new Color(1f, 0.8f, 0.2f));
            landPad = LundunFlyHeli.Pad(landAt, 7f, new Color(0.3f, 1f, 0.45f));
            heli = LundunFlyHeli.Spawn(roofAt, 90f);
        }));
        run.Add(new LundunTalkPick(() => new[]
        {
            new LundunLine(lead, "This is it. The gala's started. Every rich face in Lundun is in there, and Hollis's ledger is in the strongroom."),
            new LundunLine(lead, way == 3 ? "Tayo's passes will get you in the front. Keep your head down and your tray up."
                               : way == 0 ? "His own scaffolding. Up and over. Poetic." : way == 1 ? "Trades entrance. Deliveries never stop at a gala." : "The railings. Quick and quiet, yeah?"),
            new LundunLine("JAMES", "In, ledger, out."),
            new LundunLine(lead, marcus ? "And if it goes loud, I'll be in the gardens. I've got you." : "And if it goes loud? Run. Don't be a hero."),
        }));
        run.Add(new LundunGoTo(approach, 2.6f, WayGoal[way])).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunNow(r =>
        {
            LundunFlash.Black(1.1f);
            LundunMissions.Toast(WayIn[way]);
            Vector3 start = way == 0 || way == 3 ? new Vector3(0f, 0f, 25f) : new Vector3(0f, 0f, 1.2f);
            M15Warehouse9.Put(At(start), facing);
            LundunArches.MissionInside = true;
        }));
        Vector3 ledger = At(new Vector3(0f, 0f, 66f));
        run.Add(new LundunSneak(() => guards, () => Near(ledger, 1.6f), Seen,
            "Find the strongroom at the end of the grand gallery. Stay out of the torchlight").Mark(ledger, 1.6f, false));
        run.Add(new LundunIf(() => alarm, new LundunWhen("They've seen you! Get the ledger", r => Near(ledger, 1.6f)).Mark(ledger, 1.6f, false)));
        run.Add(new LundunNow(r =>
        {
            LundunMissions.Toast("Got Hollis's ledger. The strongroom's alarmed!");
            LundunHaptics.Heavy();
            foreach (LundunGuard g in guards) if (g != null) g.Wake();
        }));
        run.Add(new LundunTalk("", new LundunLine(lead, "(phone) Alarms everywhere! Out through the state rooms, to the gardens!")));
        Vector3 sr1 = At(new Vector3(16f, 0f, 52f)), sr2 = At(new Vector3(16f, 0f, 22f)), garden = At(new Vector3(16f, 0f, 3f));
        run.Add(new LundunWhen("Through the state rooms", r => Near(sr1, 2.5f)).Mark(sr1, 2.5f, false));
        run.Add(new LundunWhen("Keep going!", r => Near(sr2, 2.5f)).Mark(sr2, 2.5f, false));
        run.Add(new LundunWhen("Out through the garden doors", r => Near(garden, 1.8f)).Mark(garden, 1.8f, false));
        run.Add(new LundunNow(r =>
        {
            LundunFlash.Black(0.6f);
            LundunArches.MissionInside = false;
            M15Warehouse9.Put(LundunMissionRun.Ground(new Vector3(560f, 0f, 1822f)), Quaternion.identity);
            resolved = true; outside = true;
        }));
        run.Add(new LundunDo(r =>                                                // out in the gardens: 5★ (and again on a retry)
        {
            if (!outside) return;
            WantedSystem w = WantedSystem.Instance;
            Transform j = LundunMissionRun.James();
            if (w != null && j != null) w.RaiseTo(5, j.position, true);
            if (marcus && !fought) { LundunCrew.Join("MARCUS"); LundunCrew.Arm("MARCUS", true); }
        }));
        run.Add(new LundunFight(LundunMissionRun.Ground(GardenFight), 5, true, "Palace security: fight your way through the gardens", "Suit", 40f) { harder = 1.2f })
            .Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunIf(() => marcus, new LundunTalk("", new LundunLine("MARCUS", "Go! The scaffolding! I'll hold them here!"))));
        run.Add(new LundunNow(r => { fought = true; if (marcus) LundunCrew.Leave("MARCUS"); }));
        run.Add(new LundunGoTo(LundunMissionRun.Ground(ClimbFoot), 2.5f, "Climb Hollis's scaffolding to the roof"));
        run.Add(new LundunNow(r =>
        {
            LundunFlash.Black(1f);
            M15Warehouse9.Put(roofAt + new Vector3(-9f, 0.2f, 0f), Quaternion.Euler(0f, 90f, 0f));
            LundunMissions.Toast("Hollis's helicopter. On the roof.");
        }));
        run.Add(new LundunWhen("Get in Hollis's helicopter", r =>
        {
            if (heli == null || heli.Wrecked) { r.Fail("The helicopter's been wrecked"); return false; }
            Transform j = LundunMissionRun.James();
            if (j == null || LundunMissionRun.FlatDistance(j.position, heli.transform.position) > 4.5f) return false;
            heli.Board(); heli.CanLeave = false;
            return true;
        }).Mark(roofAt, 4f, false)).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunFly(() => heli, landAt, "Fly to Tower Plaza and land on the green pad"));
        run.Add(new LundunNow(r =>
        {
            WantedSystem w = WantedSystem.Instance;
            if (w != null) w.ClearWanted();
            if (heli != null) heli.Release();
            Vector3 toward = (landAt - LundunPalaceWays.PalaceCentre); toward.y = 0f; toward.Normalize();
            Vector3 side = new Vector3(toward.z, 0f, -toward.x);
            LundunCrew.Stage("HOLLIS", LundunMissionRun.Ground(landAt + toward * 11f), Quaternion.LookRotation(-toward).eulerAngles.y);
            if (kemi) LundunCrew.Stage("KEMI", LundunMissionRun.Ground(landAt - toward * 12f + side * 4f), Quaternion.LookRotation(toward).eulerAngles.y);
            LundunCrew.ShowUpNow();
            LundunMissions.Toast("The police have gone quiet. Somebody called them off.");
        }));
        run.Add(new LundunGoTo(LundunMissionRun.Ground(landAt), 12f, "Face Hollis"));
        run.Add(new LundunTalkPick(() =>
        {
            List<LundunLine> l = new List<LundunLine>
            {
                new LundunLine("HOLLIS", "James. My helicopter, my ledger and my evening. You've been busy."),
                new LundunLine("HOLLIS", "I sent the police home. This is a private conversation."),
            };
            if (favour) l.Add(new LundunLine("HOLLIS", "You still owe me a favour, by the way. I'd hate for that to slip your mind."));
            else l.Add(new LundunLine("HOLLIS", "Your uncle paid every penny. Very honourable. Very stupid."));
            l.Add(new LundunLine("JAMES", "Every shop on Rye Lane. Every name you've leaned on. It's all in here."));
            l.Add(new LundunLine("HOLLIS", "So what now? The papers? Or are you cleverer than your uncle?"));
            return l.ToArray();
        }));
        run.Add(new LundunChoice("Hollis's ledger", "GIVE IT TO THE PAPERS", 0, "KEEP IT AS LEVERAGE", Key));
        run.Add(new LundunTalkPick(() => LundunChoice.Chose(Key, true)
            ? new[] { new LundunLine("HOLLIS", "You've no idea who else is in that book."),
                      new LundunLine("JAMES", "Then they'll be reading about themselves tomorrow."),
                      new LundunLine("UNCLE FEMI", "(phone) It's on every front page. HOLLIS ARRESTED. James... the shop is ours. Truly ours.") }
            : new[] { new LundunLine("HOLLIS", "Clever boy. So we're partners now, you and I."),
                      new LundunLine("JAMES", "No. You work for me now. Rye Lane's off limits. Forever."),
                      new LundunLine("UNCLE FEMI", "(phone) The debt is gone. He signed it away. I won't ask how, James. I don't want to know.") }));
        run.Add(new LundunIf(() => tayoHanded, new LundunTalk("", new LundunLine("TAYO", "(phone) Heard you did it. I'm out of the hospital tomorrow. I'm sorry, James. For all of it."))));
        run.Add(new LundunIf(() => kemi, new LundunTalk("", new LundunLine("KEMI", "Get in. I'm driving. Let's go home."))));
        run.Add(new LundunCard("CHAPTER 2", LundunChoice.Chose(Key, true)
            ? "Hollis is in a cell. The ledger is in the papers.\nBut page 41 is missing from what they printed.\nSomebody paid a lot to keep it that way.\n\nLundun After Dark continues..."
            : "Hollis answers to James now. The ledger is in a safe only James can open.\nOn page 41: names you know from the news. Names with their own Hollises.\nThey'll be coming for it.\n\nLundun After Dark continues..."));
    }

    bool Near(Vector3 p, float r)
    {
        Transform j = LundunMissionRun.James();
        return j != null && LundunMissionRun.FlatDistance(j.position, p) < r && Mathf.Abs(j.position.y - p.y) < 4f;
    }

    void Seen()
    {
        alarm = true;
        foreach (LundunGuard g in guards) if (g != null) g.Wake();
        LundunMissions.Toast("You've been seen!");
    }

    Vector3 At(Vector3 local) { return origin + facing * local; }

    /// The palace roof at x, z (from above), or 22 m up if there's nothing to hit.
    static Vector3 Roof(Vector3 p)
    {
        if (Physics.Raycast(new Vector3(p.x, 90f, p.z), Vector3.down, out RaycastHit h, 120f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore) && h.point.y > 12f)
            return h.point;
        return new Vector3(p.x, LundunPlaces.Ground(p).y + LundunPalaceWays.PalaceTop, p.z);
    }

    public override int Medal(LundunMissionRun run)
    {
        int n = 0;
        if (!alarm) n++;
        if (heli == null || heli.HealthFraction >= 0.6f) n++;
        if (run.Elapsed < 900f) n++;
        return n >= 3 ? 3 : n == 2 ? 2 : 1;
    }

    public override void Tidy()
    {
        LundunArches.MissionInside = false;
        guards.Clear();
        resolved = false; outside = false; fought = false;
        if (heli != null) LundunMissions.LetGo(heli.gameObject);
        LundunMissions.LetGo(roofPad); LundunMissions.LetGo(landPad);
        heli = null; wing = null; roofPad = null; landPad = null;
    }

    void Clear()
    {
        guards.Clear();
        foreach (GameObject g in new[] { wing, roofPad, landPad, heli != null ? heli.gameObject : null }) if (g != null) Object.Destroy(g);
        heli = null;
    }

    // ------------------------------------------------------------------ the wing (high above the palace)
    // Local frame: +Z north. The kitchen corridor (x ±5, z 0–22), the grand gallery (x ±8, z 22–58, 9 m high, two rows
    // of columns), the strongroom (x ±4, z 58–70, the ledger on a plinth), and beside them the state rooms (x 10–22,
    // z 0–70, two rooms) out to the garden doors at the south end. Dim, gilded, red carpet.
    GameObject BuildWing(LundunMissionRun run)
    {
        GameObject g = new GameObject("PalaceWing");
        g.transform.SetPositionAndRotation(origin, facing);
        LundunMeshKit k = new LundunMeshKit(9);
        List<Bounds> solid = new List<Bounds>();
        Action<int, Vector3, Vector3, bool> Add = (m, c, sz, block) => { k.Box(m, c, sz); if (block) solid.Add(new Bounds(c, sz)); };
        // floors and ceilings
        Add(0, new Vector3(0f, -0.1f, 11f), new Vector3(10f, 0.2f, 22f), true);
        Add(1, new Vector3(0f, -0.1f, 40f), new Vector3(16f, 0.2f, 36f), true);
        Add(0, new Vector3(0f, -0.1f, 64f), new Vector3(8f, 0.2f, 12f), true);
        Add(1, new Vector3(16f, -0.1f, 35f), new Vector3(12f, 0.2f, 70f), true);
        Add(2, new Vector3(0f, 3.7f, 11f), new Vector3(10f, 0.2f, 22f), true);
        Add(2, new Vector3(0f, 9.1f, 40f), new Vector3(16f, 0.2f, 36f), true);
        Add(2, new Vector3(0f, 3.7f, 64f), new Vector3(8f, 0.2f, 12f), true);
        Add(2, new Vector3(16f, 6.1f, 35f), new Vector3(12f, 0.2f, 70f), true);
        Add(3, new Vector3(0f, 0.01f, 40f), new Vector3(3f, 0.02f, 35f), false);                                  // red carpet
        Add(3, new Vector3(16f, 0.01f, 35f), new Vector3(3f, 0.02f, 68f), false);
        // kitchen walls and its island counters
        Add(4, new Vector3(-5.1f, 1.8f, 11f), new Vector3(0.2f, 3.6f, 22f), true);
        Add(4, new Vector3(5.1f, 1.8f, 11f), new Vector3(0.2f, 3.6f, 22f), true);
        Add(4, new Vector3(0f, 1.8f, -0.1f), new Vector3(10f, 3.6f, 0.2f), true);
        Add(5, new Vector3(0f, 0.45f, 8f), new Vector3(2f, 0.9f, 4f), true);
        Add(5, new Vector3(0f, 0.45f, 15f), new Vector3(2f, 0.9f, 4f), true);
        Add(5, new Vector3(-4.4f, 0.45f, 11f), new Vector3(1f, 0.9f, 14f), true);
        for (int i = 0; i < 5; i++) Add(6, new Vector3(-4.4f, 1.1f, 6f + i * 2.5f), new Vector3(0.4f, 0.3f, 0.4f), false);   // pans
        // the gallery: walls, columns, plinths, paintings
        Add(7, new Vector3(-8.1f, 4.5f, 40f), new Vector3(0.2f, 9f, 36f), true);
        Add(7, new Vector3(8.1f, 4.5f, 40f), new Vector3(0.2f, 9f, 36f), true);
        Add(7, new Vector3(-6.5f, 4.5f, 22f), new Vector3(3f, 9f, 0.2f), true);              // the end walls, a wide doorway in each
        Add(7, new Vector3(6.5f, 4.5f, 22f), new Vector3(3f, 9f, 0.2f), true);
        Add(7, new Vector3(0f, 6.3f, 22f), new Vector3(10f, 5.4f, 0.2f), true);
        Add(7, new Vector3(-6f, 4.5f, 58f), new Vector3(4f, 9f, 0.2f), true);
        Add(7, new Vector3(6f, 4.5f, 58f), new Vector3(4f, 9f, 0.2f), true);
        Add(7, new Vector3(0f, 6f, 58f), new Vector3(8f, 6f, 0.2f), true);
        for (float z = 27f; z < 56f; z += 6f)
            foreach (float x in new[] { -4.5f, 4.5f })
            {
                Add(8, new Vector3(x, 4.5f, z), new Vector3(0.9f, 9f, 0.9f), true);
                Add(6, new Vector3(x, 0.3f, z), new Vector3(1.3f, 0.6f, 1.3f), false);
            }
        for (float z = 26f; z < 56f; z += 5f)
            foreach (float x in new[] { -7.95f, 7.95f })
            {
                Add(6, new Vector3(x, 4f, z), new Vector3(0.08f, 2.4f, 3f), false);           // gilt frames
                Add(3, new Vector3(x + (x < 0 ? 0.05f : -0.05f), 4f, z), new Vector3(0.04f, 2f, 2.6f), false);
            }
        foreach (float z in new[] { 33f, 45f }) Add(8, new Vector3(0f, 0.6f, z), new Vector3(1f, 1.2f, 1f), true);   // plinths (a bust each)
        // the strongroom: thick walls, a vault door standing open, the ledger on its plinth
        Add(4, new Vector3(-4.1f, 1.8f, 64f), new Vector3(0.2f, 3.6f, 12f), true);
        Add(4, new Vector3(4.1f, 1.8f, 61f), new Vector3(0.2f, 3.6f, 6f), true);
        Add(4, new Vector3(4.1f, 1.8f, 69f), new Vector3(0.2f, 3.6f, 2f), true);             // a door east (to the state rooms) at z 64–68
        Add(4, new Vector3(0f, 1.8f, 70.1f), new Vector3(8f, 3.6f, 0.2f), true);
        Add(6, new Vector3(-2.2f, 1.5f, 58.6f), new Vector3(0.3f, 3f, 2.4f), false);          // the vault door, swung open
        Add(8, new Vector3(0f, 0.5f, 66.6f), new Vector3(0.8f, 1f, 0.8f), true);
        // the state rooms: walls, the dividing wall with its doorway, a long table, sofas, the garden doors
        Add(7, new Vector3(9.9f, 3f, 43f), new Vector3(0.2f, 6f, 42f), true);                // the state rooms' west wall (a gap at z 64–68)
        Add(0, new Vector3(7f, -0.1f, 66f), new Vector3(6f, 0.2f, 4f), true);                 // the passage from the strongroom
        Add(4, new Vector3(7f, 1.8f, 63.9f), new Vector3(5.8f, 3.6f, 0.2f), true);
        Add(4, new Vector3(7f, 1.8f, 68.1f), new Vector3(5.8f, 3.6f, 0.2f), true);
        Add(2, new Vector3(7f, 3.7f, 66f), new Vector3(6f, 0.2f, 4f), true);
        Add(7, new Vector3(9.9f, 3f, 69f), new Vector3(0.2f, 6f, 2f), true);
        Add(7, new Vector3(9.9f, 3f, 11f), new Vector3(0.2f, 6f, 22f), true);
        Add(7, new Vector3(22.1f, 3f, 35f), new Vector3(0.2f, 6f, 70f), true);
        Add(7, new Vector3(16f, 3f, 70.1f), new Vector3(12f, 6f, 0.2f), true);
        Add(7, new Vector3(16f, 3f, -0.1f), new Vector3(12f, 6f, 0.2f), true);
        Add(7, new Vector3(12.5f, 3f, 36f), new Vector3(5f, 6f, 0.2f), true);
        Add(7, new Vector3(19.5f, 3f, 36f), new Vector3(5f, 6f, 0.2f), true);
        Add(5, new Vector3(18.5f, 0.4f, 52f), new Vector3(2.2f, 0.8f, 10f), true);
        Add(3, new Vector3(12.2f, 0.4f, 25f), new Vector3(1f, 0.8f, 3f), true);
        Add(3, new Vector3(20f, 0.4f, 18f), new Vector3(1f, 0.8f, 3f), true);
        Add(6, new Vector3(16f, 1.3f, 0.05f), new Vector3(3f, 2.6f, 0.08f), false);           // the garden doors
        foreach (Vector3 ch in new[] { new Vector3(16f, 5.3f, 52f), new Vector3(16f, 5.3f, 18f), new Vector3(0f, 8.2f, 33f), new Vector3(0f, 8.2f, 47f) })
            Add(6, ch, new Vector3(1.6f, 0.6f, 1.6f), false);                                // chandeliers
        g.AddComponent<MeshFilter>().sharedMesh = k.Build("PalaceWing");
        g.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            LundunMeshKit.Mat("WingTiles", new Color(0.7f, 0.68f, 0.62f), 0.5f, Color.black),
            LundunMeshKit.Mat("WingParquet", new Color(0.42f, 0.27f, 0.15f), 0.55f, Color.black),
            LundunMeshKit.Mat("WingCeiling", new Color(0.85f, 0.82f, 0.74f), 0.2f, Color.black),
            LundunMeshKit.Mat("WingRed", new Color(0.5f, 0.06f, 0.08f), 0.2f, Color.black),
            LundunMeshKit.Mat("WingPlain", new Color(0.78f, 0.76f, 0.7f), 0.15f, Color.black),
            LundunMeshKit.Mat("WingSteel", new Color(0.55f, 0.57f, 0.6f), 0.7f, Color.black),
            LundunMeshKit.Mat("WingGilt", new Color(0.85f, 0.65f, 0.25f), 0.7f, new Color(0.25f, 0.18f, 0.05f)),
            LundunMeshKit.Mat("WingGreen", new Color(0.2f, 0.32f, 0.27f), 0.25f, Color.black),
            LundunMeshKit.Mat("WingMarble", new Color(0.92f, 0.9f, 0.86f), 0.6f, Color.black),
        };
        foreach (Bounds b in solid) { BoxCollider c = g.AddComponent<BoxCollider>(); c.center = b.center; c.size = b.size; }
        // the ledger
        GameObject book = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(book.GetComponent<Collider>());
        book.transform.SetParent(g.transform, false);
        book.transform.localPosition = new Vector3(0f, 1.06f, 66.6f); book.transform.localScale = new Vector3(0.35f, 0.08f, 0.45f);
        book.GetComponent<Renderer>().sharedMaterial = LundunMeshKit.Mat("Ledger", new Color(0.35f, 0.08f, 0.08f), 0.4f, new Color(0.15f, 0.02f, 0.02f));
        // dim, warm light
        foreach (Vector3 lp in new[] { new Vector3(0f, 3f, 10f), new Vector3(0f, 7.5f, 33f), new Vector3(0f, 7.5f, 47f), new Vector3(0f, 3f, 64f), new Vector3(16f, 5f, 52f), new Vector3(16f, 5f, 18f) })
        {
            GameObject lg = new GameObject("WingLight");
            lg.transform.SetParent(g.transform, false);
            lg.transform.localPosition = lp;
            Light l = lg.AddComponent<Light>();
            l.type = LightType.Point; l.range = 14f; l.intensity = 0.9f; l.color = new Color(1f, 0.82f, 0.55f); l.shadows = LightShadows.None;
        }
        // the guards and their rounds: fewer for the staff passes and the roof, more through the kitchens and railings
        List<Vector3[]> rounds = new List<Vector3[]>
        {
            new[] { At(new Vector3(-6.5f, 0f, 25f)), At(new Vector3(-6.5f, 0f, 55f)) },
            new[] { At(new Vector3(6.5f, 0f, 55f)), At(new Vector3(6.5f, 0f, 25f)) },
        };
        if (way == 1 || way == 2) rounds.Add(new[] { At(new Vector3(-2.5f, 0f, 4f)), At(new Vector3(-2.5f, 0f, 19f)), At(new Vector3(2.5f, 0f, 19f)), At(new Vector3(2.5f, 0f, 4f)) });
        if (way != 3) rounds.Add(new[] { At(new Vector3(-6f, 0f, 40f)), At(new Vector3(6f, 0f, 40f)) });
        if (run.difficulty == LundunDifficulty.Hard) rounds.Add(new[] { At(new Vector3(-2f, 0f, 56f)), At(new Vector3(2f, 0f, 56f)) });
        LundunPopulation pop = LundunPopulation.Instance;
        for (int i = 0; i < rounds.Count && pop != null; i++)
        {
            GameObject p = pop.NewPerson(null, "Suit");
            if (p == null) continue;
            NPCBrain br = p.GetComponent<NPCBrain>(); if (br != null) br.enabled = false;
            NavMeshAgent a = p.GetComponent<NavMeshAgent>(); if (a != null) a.enabled = false;
            Vector3[] rt = rounds[i];
            p.transform.SetPositionAndRotation(rt[0] + Vector3.up * 0.05f, Quaternion.LookRotation(rt[1] - rt[0]));
            p.name = "PalaceGuard_" + i;
            p.SetActive(true);
            foreach (Animator an in p.GetComponentsInChildren<Animator>(true)) an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Health h = p.GetComponent<Health>(); if (h != null) h.Invulnerable = true;
            LundunGuard guard = p.AddComponent<LundunGuard>();
            Vector3[] loop = new Vector3[rt.Length];
            for (int n = 0; n < rt.Length; n++) loop[n] = rt[(1 + n) % rt.Length];
            guard.route = loop;
            guards.Add(guard);
            run.Spawn(p);
        }
        return g;
    }
}

/// S9b: a full-screen card (black, a title, a few lines), there until it's tapped: the Chapter 2 teaser.
public class LundunCard : LundunStep
{
    readonly string title, body;
    GameObject ui;
    bool tapped;
    float shown;

    public LundunCard(string heading, string text) { title = heading; body = text; objective = ""; }

    public override void Begin(LundunMissionRun run)
    {
        tapped = false; shown = Time.unscaledTime;
        ui = new GameObject("LundunCard", typeof(RectTransform));
        Canvas cv = ui.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 85;
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        ui.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)ui.transform;
        Image bg = LundunUIBits.Box(root, "Black", Vector2.zero, Vector2.zero, new Color(0.01f, 0.01f, 0.02f, 0.97f));
        RectTransform br = bg.rectTransform; br.anchorMin = Vector2.zero; br.anchorMax = Vector2.one; br.offsetMin = br.offsetMax = Vector2.zero;
        Button b = bg.gameObject.AddComponent<Button>(); b.targetGraphic = bg;
        b.onClick.AddListener(() => { if (Time.unscaledTime - shown > 2f) tapped = true; });
        TextMeshProUGUI t = LundunUIBits.Label(root, title, 96f, new Vector2(0f, 230f), new Vector2(1600f, 130f));
        t.color = new Color(1f, 0.66f, 0.18f); t.characterSpacing = 20f;
        TextMeshProUGUI m = LundunUIBits.Label(root, body, 36f, new Vector2(0f, -30f), new Vector2(1400f, 380f));
        m.fontStyle = FontStyles.Normal; m.color = new Color(1f, 1f, 1f, 0.9f);
        TextMeshProUGUI tap = LundunUIBits.Label(root, "TAP TO CONTINUE", 26f, new Vector2(0f, -400f), new Vector2(800f, 50f));
        tap.color = new Color(1f, 1f, 1f, 0.5f);
        LundunHaptics.Medium();
    }

    public override bool Tick(LundunMissionRun run) { return tapped; }
    public override void End(LundunMissionRun run) { if (ui != null) Object.Destroy(ui); }
}
