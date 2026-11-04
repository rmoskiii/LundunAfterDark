using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

// ======================================================================================================================
/// 07 Home Turf (S5). Kemi rings: Electric Youth, working for Kingsley, are shaking down the stalls at Brixtun market.
/// James goes with Marcus. Two rounds of what to say (calm, firm, hot-headed, or FIGHT): the right word both times and
/// they back off (Kemi +1, Marcus −1: "choice:m07:a"); a hot-headed word or FIGHT and it's a fight in the crowd, which
/// scatters (Marcus +1: "choice:m07:b"). Either way one of them lets slip who's been talking to Hollis: Tayo.
public class M07HomeTurf : LundunMission
{
    public const string Key = "choice:m07";
    readonly List<Health> youth = new List<Health>();
    LundunTone first, second;
    bool fight, hot, resolved;                         // resolved: past the standoff (a retry from later doesn't redo it)

    public override string Id { get { return "m07_home_turf"; } }
    public override string Title { get { return "Home Turf"; } }
    public override string Teaser { get { return "Kemi's on the phone. Electric Youth are shaking down Brixtun market."; } }
    public override string Giver { get { return "MARCUS"; } }
    public override long Reward { get { return 2000; } }
    public override string MedalNote { get { return "under 6 minutes"; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m06_passenger_problems") && !LundunMissionBook.Done(d, Id); }
    public override Vector3 StartAt { get { return LundunStoryPlaces.Home + Quaternion.Euler(0f, LundunStoryPlaces.HomeYaw, 0f) * Vector3.forward * 3f; } }
    public override Vector3 GiverFacing { get { return Quaternion.Euler(0f, LundunStoryPlaces.HomeYaw, 0f) * Vector3.forward; } }

    bool Talked { get { return !hot && first == LundunTone.Calm && second == LundunTone.Firm; } }

    public override void Build(LundunMissionRun run)
    {
        Vector3 stallDir;
        Vector3 stall = LundunMarket.Stall(out stallDir);
        run.Zone(LundunMarket.Middle, 50f);                                 // the city's own gangs kept out of it

        run.Add(new LundunDo(r => { if (!resolved) { fight = false; hot = false; first = LundunTone.Firm; second = LundunTone.Calm; } }));
        run.Add(new LundunJoin("MARCUS"));
        run.Add(new LundunTalk("",
            new LundunLine("KEMI", "(phone) James? Are you with Marcus? Electric Youth are at the market, going stall to stall."),
            new LundunLine("KEMI", "(phone) They're taking money. Kingsley's money, they're saying. Mrs Okafor's in tears."),
            new LundunLine("MARCUS", "Brixtun market. That's our people, bro. Get the car."),
            new LundunLine("JAMES", "We go, we talk. Yeah? We just talk.")));
        run.Add(new LundunGoTo(LundunMarket.EastEnd, 12f, "Get to Brixtun market"));
        // Electric Youth round a stall
        run.Add(new LundunDo(r =>
        {
            if (resolved) return;
            youth.Clear();
            LundunPopulation pop = LundunPopulation.Instance;
            int n = 4 + r.ExtraEnemies;
            for (int i = 0; i < n && pop != null; i++)
            {
                Vector3 side = Vector3.Cross(Vector3.up, stallDir);
                Vector3 p = stall + side * ((i - (n - 1) * 0.5f) * 1.3f) - stallDir * (i % 2 == 0 ? 0.3f : 1.2f);
                if (!NavMesh.SamplePosition(p + Vector3.up, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
                GameObject g = pop.NewPerson(null, "Street");
                if (g == null) continue;
                NPCBrain b = g.GetComponent<NPCBrain>(); if (b != null) b.enabled = false;
                g.transform.SetPositionAndRotation(hit.position, Quaternion.LookRotation(stallDir));
                g.name = "ElectricYouth_" + i;
                if (LundunGangs.Instance != null) LundunGangs.Instance.Wear(g, "Electric Youth");
                g.SetActive(true);
                r.Spawn(g);
                Health h = g.GetComponent<Health>();
                if (h != null) youth.Add(h);
            }
        }));
        run.Add(new LundunWhen("Walk up to the Electric Youth", r =>
        {
            Transform j = LundunMissionRun.James();
            return j != null && !LundunMissionRun.Driving() && LundunMissionRun.FlatDistance(j.position, stall) < 5.5f;
        }).Mark(stall, 3f, false)).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunNow(r => Face()));
        run.Add(new LundunTalk("",
            new LundunLine("ELECTRIC YOUTH", "Look who it is. Uncle Femi's boys. Come to pay for your stall?"),
            new LundunLine("MARCUS", "Put her money back.")));

        // round one: the right word is the calm one
        run.Add(new LundunReplies("ELECTRIC YOUTH", "Kingsley says this market pays now. You got a problem with Kingsley?",
            new[]
            {
                new LundunReply(LundunTone.Firm, "Yeah. I've got a problem with you taking from pensioners."),
                new LundunReply(LundunTone.Calm, "No problem. Nobody needs a scene in front of all these people."),
                new LundunReply(LundunTone.Hot, "Kingsley can come and say it himself, you little errand boy."),
            }, t => { if (t == LundunReplies.Fight || t == (int)LundunTone.Hot) fight = hot = true; else first = (LundunTone)t; }));
        run.Add(new LundunTalkPick(() => fight
            ? new[] { new LundunLine("ELECTRIC YOUTH", "Errand boy? Yeah? Come on then!") }
            : first == LundunTone.Calm
                ? new[] { new LundunLine("ELECTRIC YOUTH", "...Scene. Yeah. Everyone's watching, innit."), new LundunLine("MARCUS", "They are. Phones out and everything.") }
                : new[] { new LundunLine("ELECTRIC YOUTH", "Pensioners pay like everyone else, bruv."), new LundunLine("MARCUS", "James. Easy.") }));
        // round two: the right word is the firm one
        run.Add(new LundunIf(() => !fight, new LundunReplies("ELECTRIC YOUTH", "So what, we just walk off? What do I tell Kingsley?",
            new[]
            {
                new LundunReply(LundunTone.Hot, "Tell him I'll break his jaw next. Then yours."),
                new LundunReply(LundunTone.Calm, "Tell him whatever you want, mate. Please, just go."),
                new LundunReply(LundunTone.Firm, "Tell him this market's family. It doesn't pay. Not today, not ever."),
            }, t => { if (t == LundunReplies.Fight || t == (int)LundunTone.Hot) fight = hot = true; else second = (LundunTone)t; })));
        // how it goes: both right and they back off; anything else and it kicks off
        run.Add(new LundunNow(r =>
        {
            bool talked = Talked;
            if (!talked) fight = true;
            LundunStoryData d = LundunStory.Data;
            if (d != null)
            {
                d.flags.Remove(Key + ":a"); d.flags.Remove(Key + ":b");
                d.flags.Add(Key + (talked ? ":a" : ":b"));
                LundunStory.MarkDirty();
            }
            LundunTrust.Recalc(true);
        }));
        run.Add(new LundunTalkPick(() => Talked
            ? new[] { new LundunLine("ELECTRIC YOUTH", "...Fine. Keep your market. This time."),
                      new LundunLine("ELECTRIC YOUTH", "Give the old girl her money. Come on, we're going.") }
            : hot ? new[] { new LundunLine("ELECTRIC YOUTH", "Right. That's it. Get them!") }
            : second == LundunTone.Calm
                ? new[] { new LundunLine("ELECTRIC YOUTH", "Please? Hear him, lads? He's begging."), new LundunLine("ELECTRIC YOUTH", "Get them.") }
                : new[] { new LundunLine("ELECTRIC YOUTH", "Family? Pensioners, family, whatever. Everyone pays."), new LundunLine("ELECTRIC YOUTH", "Get them.") }));
        // a fight: the crowd scatters, Marcus fights beside James
        run.Add(new LundunIf(() => fight, new LundunNow(r =>
        {
            Transform j = LundunMissionRun.James();
            LundunMarket.Scatter(j != null ? j.position : stall);
        })));
        run.Add(new LundunIf(() => fight, new LundunFight(() => youth, "Fight off the Electric Youth. Keep Marcus up") { harder = 1.2f }));
        // talked down: off they go
        run.Add(new LundunIf(() => !fight, new LundunNow(r =>
        {
            Transform j = LundunMissionRun.James();
            foreach (Health h in youth)
            {
                if (h == null) continue;
                NPCBrain b = h.GetComponent<NPCBrain>();
                if (b != null) { b.enabled = true; if (j != null) b.Panic(j.position); }
            }
        })));
        // either way: one of them lets slip who's been talking to Hollis
        run.Add(new LundunTalkPick(() => Talked
            ? new[] { new LundunLine("ELECTRIC YOUTH", "(walking off) Ask your mate Tayo who's been talking. He's been at Hollis's yard all week."),
                      new LundunLine("JAMES", "...What did he say?"),
                      new LundunLine("MARCUS", "Tayo. At Hollis's.") }
            : new[] { new LundunLine("ELECTRIC YOUTH", "(on the floor) Alright! ALRIGHT! It weren't us that grassed you up!"),
                      new LundunLine("ELECTRIC YOUTH", "It's Tayo! Tayo's been telling Hollis everything!"),
                      new LundunLine("MARCUS", "Tayo.") }));
        run.Add(new LundunNow(r => resolved = true));
        run.Add(new LundunTalk("",
            new LundunLine("JAMES", "He wouldn't. Not Tayo."),
            new LundunLine("MARCUS", "Basketful was his idea. Hollis knew by morning. Think about it, James."),
            new LundunLine("KEMI", "(text) Mrs Okafor says thank you. Are you both okay?"))).Checkpoint(LundunCheckpoint.Minor);
        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunGoTo(LundunStoryPlaces.Home, 6f, "Take Marcus home"));
        run.Add(new LundunTalkPick(() => Talked
            ? new[] { new LundunLine("MARCUS", "You talked them down. Kemi'll be telling everyone."),
                      new LundunLine("MARCUS", "Me, I'd have flattened him. Maybe that's why it worked.") }
            : new[] { new LundunLine("MARCUS", "You held your own back there. Proper."),
                      new LundunLine("MARCUS", "Kemi's going to kill us. Worth it.") }));
        run.Add(new LundunLeave("MARCUS", () => LundunStoryPlaces.Home));
    }

    /// The Electric Youth turn to James.
    void Face()
    {
        Transform j = LundunMissionRun.James();
        if (j == null) return;
        foreach (Health h in youth)
        {
            if (h == null) continue;
            Vector3 f = j.position - h.transform.position; f.y = 0f;
            if (f.sqrMagnitude > 0.01f) h.transform.rotation = Quaternion.LookRotation(f);
        }
    }

    public override int Medal(LundunMissionRun run) { return run.Elapsed < 360f ? 3 : run.Elapsed < 540f ? 2 : 1; }
    public override void Tidy() { youth.Clear(); resolved = false; }
}

// ======================================================================================================================
/// 08 Blood Is Thicker (S5). Marcus has found Tayo at Knightsbury Circus. He sees them coming and he's off on a hire
/// bike (there's one left for James): keep up (lose sight of him for 15 seconds and he's gone). He dives into the
/// Underground: follow him down (the Tube takes James straight to Tayo's stop, Canary Quay) and catch him at the top
/// of the steps. Then the choice: HAND HIM TO MARCUS ("choice:m08:a": Marcus +1, Tayo's gone till the end) or LET HIM GO
/// (":b": Tayo +2, Marcus −1). That night Hollis's men smash up LAGOS FOODS (it stays smashed until mission 10); Uncle
/// Femi's staying at James's.
public class M08BloodIsThicker : LundunMission
{
    public const string Key = "choice:m08";
    static readonly Vector3 Circus = new Vector3(560f, 0f, 945f);
    const string Stop = "CANARY QUAY";

    GameObject tayo, tayoBike;
    LundunCyclist cyclist;
    LundunRunner runner;
    int station = -1;
    bool cameOff, chose;                               // chose: past the choice (a retry from later doesn't bring Tayo back)

    public override string Id { get { return "m08_blood_is_thicker"; } }
    public override string Title { get { return "Blood Is Thicker"; } }
    public override string Teaser { get { return "Marcus has found Tayo at Knightsbury Circus. He wants answers."; } }
    public override string Giver { get { return "MARCUS"; } }
    public override long Reward { get { return 1500; } }
    public override string MedalNote { get { return "under 7 minutes"; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m07_home_turf") && !LundunMissionBook.Done(d, Id); }
    public override Vector3 StartAt { get { return LundunMissionRun.Ground(Circus + new Vector3(-6f, 0f, 4f)); } }
    public override Vector3 GiverFacing { get { return Vector3.right; } }

    Transform TayoNow { get { return tayo != null && tayo.activeInHierarchy ? tayo.transform : (tayoBike != null ? tayoBike.transform : null); } }

    public override void Build(LundunMissionRun run)
    {
        Vector3 start = LundunMissionRun.Ground(Circus);
        Vector3 kerb; Quaternion along;
        if (!LundunStoryPlaces.Kerb(start + new Vector3(22f, 0f, 10f), out kerb, out along)) { kerb = LundunMissionRun.Ground(start + new Vector3(22f, 0f, 10f)); along = Quaternion.identity; }
        Vector3 tayoSpot = LundunMissionRun.Ground(kerb + along * Vector3.left * 2.6f);      // on the pavement by the kerb

        // Tayo by the kerb a little way off, with the bikes
        run.Add(new LundunDo(r =>
        {
            station = NearestStation(start);
            if (chose) return;
            cameOff = false;
            if (tayo != null) Object.Destroy(tayo);
            if (tayoBike != null) Object.Destroy(tayoBike);
            tayo = null; tayoBike = null; cyclist = null; runner = null;
            tayo = LundunCast.Make("TAYO", LundunMissions.Instance != null ? LundunMissions.Instance.transform : null);
            if (tayo == null) return;
            Vector3 stand = tayoSpot;
            tayo.transform.SetPositionAndRotation(stand, along);
            tayo.SetActive(true);
            NavMeshAgent a = tayo.GetComponent<NavMeshAgent>(); if (a != null && a.isOnNavMesh) a.Warp(stand);
            r.Spawn(tayo);
        }));
        run.Add(new LundunJoin("MARCUS"));
        run.Add(new LundunTalk("",
            new LundunLine("MARCUS", "There. By the bikes. That's him."),
            new LundunLine("JAMES", "Let me talk to him first. Marcus. Let me talk to him."),
            new LundunLine("MARCUS", "Talk, then. I'm right behind you.")));
        run.Add(new LundunWhen("Go and talk to Tayo", r =>
        {
            Transform j = LundunMissionRun.James();
            return tayo == null || (j != null && LundunMissionRun.FlatDistance(j.position, tayo.transform.position) < 9f);
        }).Mark(tayoSpot, 3f, false));
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "James? ...And Marcus. Nah. Nah, not today."),
            new LundunLine("JAMES", "Tayo, wait!")));
        // off he goes on a bike; there's one for James
        run.Add(new LundunNow(r => Bolt(r))).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunLeave("MARCUS"));
        run.Add(new LundunTalk("", new LundunLine("MARCUS", "He's on a bike! There's another one, go! I'll bring the car round.")));
        run.Add(new LundunKeepUp(() => TayoNow,
            () => cyclist != null && cyclist.Arrived,
            () => { if (cyclist != null && cyclist.Crashed && cyclist.RiderUp) cameOff = true; return cameOff; },
            15f, "Keep up with Tayo", "You lost Tayo"));
        // he's down the Underground (unless he came off the bike first: then he's there, dazed)
        run.Add(new LundunIf(() => cameOff && runner == null, new LundunNow(r =>
        {
            if (tayo == null) return;
            runner = tayo.GetComponent<LundunRunner>() ?? tayo.AddComponent<LundunRunner>();
            runner.Stand();
        })));
        run.Add(new LundunIf(() => !cameOff, new LundunNow(r => DownTheSteps())));
        run.Add(new LundunIf(() => !cameOff, new LundunTalk("", new LundunLine("JAMES", "He's gone down the Tube!"))));
        run.Add(new LundunIf(() => !cameOff, new LundunWhen("Follow Tayo down into the Underground", r =>
            LundunTube.Instance == null || station < 0 || (!LundunTube.Instance.Travelling && At(Stop)))
            .Mark(StationTop(), 3f, true)));
        run.Add(new LundunIf(() => !cameOff, new LundunNow(r => UpAtTheQuay())));
        run.Add(new LundunIf(() => !cameOff, new LundunTalk("", new LundunLine("JAMES", "TAYO!"))));
        run.Add(new LundunCatch(() => runner, "Catch Tayo", "Tayo got away"));
        run.Add(new LundunNow(r =>
        {
            if (runner != null) runner.Stand();
            Transform j = LundunMissionRun.James();
            if (tayo != null && j != null) { Vector3 f = j.position - tayo.transform.position; f.y = 0f; if (f.sqrMagnitude > 0.01f) tayo.transform.rotation = Quaternion.LookRotation(f); }
        }));
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "Alright! Alright. I'm done. I'm done running."),
            new LundunLine("JAMES", "Hollis. Was it you?"),
            new LundunLine("TAYO", "After Basketful he found me. Said that was his money and I owed him. Said he knew where my mum lives."),
            new LundunLine("TAYO", "So I told him things. Where you'd be. What Uncle Femi was saying. I'm sorry, bro."),
            new LundunLine("JAMES", "Sorry. Everyone's sorry."),
            new LundunLine("MARCUS", "(phone) I'm two minutes away. Have you got him?")));
        run.Add(new LundunChoice("What happens to Tayo?", "HAND HIM TO MARCUS", 0, "LET HIM GO", Key));
        run.Add(new LundunNow(r =>
        {
            chose = true;
            LundunTrust.Recalc(true);
            if (LundunChoice.Chose(Key, true) && tayo != null)
            {
                Vector3 f = tayo.transform.forward;
                LundunCrew.Stage("MARCUS", LundunMissionRun.Ground(tayo.transform.position - f * 3f + Vector3.Cross(Vector3.up, f) * 2f), tayo.transform.eulerAngles.y);
            }
        }));
        run.Add(new LundunTalkPick(() => LundunChoice.Chose(Key, true)
            ? new[] { new LundunLine("JAMES", "Yeah. I've got him."),
                      new LundunLine("MARCUS", "Tayo. You're coming with me. Somewhere Hollis can't find you."),
                      new LundunLine("TAYO", "James... come on. James!"),
                      new LundunLine("JAMES", "You did this, Tayo. Not me.") }
            : new[] { new LundunLine("JAMES", "Go. Before Marcus gets here."),
                      new LundunLine("TAYO", "...Thank you. I'll make it right, I swear on my mum."),
                      new LundunLine("MARCUS", "(phone) James? JAMES. Tell me you didn't let him go."),
                      new LundunLine("JAMES", "He's my best mate, Marcus.") }));
        run.Add(new LundunNow(r =>
        {
            if (runner == null || tayo == null) return;
            if (LundunChoice.Chose(Key, true)) { runner.speed = 1.5f; runner.RunTo(LundunMissionRun.Ground(tayo.transform.position - tayo.transform.forward * 3f), false); }
            else { runner.speed = 5.5f; runner.Flee(); }
        }));
        run.Add(new LundunWait(3f));
        // that night
        run.Add(new LundunNow(r =>
        {
            if (LundunClock.Instance != null) LundunClock.Instance.SetHour(23.5f);
            LundunStoryData d = LundunStory.Data;
            if (d != null && !d.flags.Contains(LundunStoryShop.SmashedFlag)) { d.flags.Add(LundunStoryShop.SmashedFlag); LundunStory.MarkDirty(); }
            if (LundunStoryShop.Instance != null) LundunStoryShop.Instance.Refresh();
            LundunMissions.Toast("LATER THAT NIGHT");
        }));
        run.Add(new LundunTalk("",
            new LundunLine("KEMI", "(phone) James. It's the shop. Somebody's smashed Uncle's shop to pieces."),
            new LundunLine("KEMI", "(phone) He's alright. He's shaking, but he's alright. Come. Please.")));
        run.Add(new LundunDo(r =>
        {
            LundunStoryShop.MissionOpen = true;                                        // to see the damage
            float fy; Vector3 at = LundunStoryPlaces.Lagos(LundunShopLayout.Inside(LundunStoryPlaces.LagosWidth), out fy);
            LundunCrew.Stage("KEMI", at, fy);
        }));
        run.Add(new LundunGoTo(LundunStoryPlaces.LagosDoor, 4f, "Get to LAGOS FOODS")).Checkpoint(LundunCheckpoint.Minor);
        run.Add(new LundunTalkPick(() => LundunChoice.Chose(Key, true)
            ? new[] { new LundunLine("KEMI", "Glass everywhere. They took nothing. They just wanted him to see it."),
                      new LundunLine("KEMI", "Hollis's men. Uncle says it's because Tayo stopped talking."),
                      new LundunLine("JAMES", "Where is he?"),
                      new LundunLine("KEMI", "At yours. Leah's with him. He wouldn't stay here.") }
            : new[] { new LundunLine("KEMI", "Glass everywhere. They took nothing. They just wanted him to see it."),
                      new LundunLine("KEMI", "An hour after you let Tayo go. Think about that, James."),
                      new LundunLine("JAMES", "Where is he?"),
                      new LundunLine("KEMI", "At yours. Leah's with him. He wouldn't stay here.") }));
        run.Add(new LundunDo(r =>
        {
            float hy = LundunStoryPlaces.HomeYaw;
            Vector3 f = Quaternion.Euler(0f, hy, 0f) * Vector3.forward, side = Vector3.Cross(Vector3.up, f);
            LundunCrew.Stage("UNCLE FEMI", LundunMissionRun.Ground(LundunStoryPlaces.Home + f * 2.2f - side * 2.2f), hy);
        }));
        run.Add(new LundunGoTo(LundunStoryPlaces.Home, 6f, "Go home to Uncle Femi"));
        run.Add(new LundunTalk("",
            new LundunLine("UNCLE FEMI", "Eight years, James. Eight years I swept that floor."),
            new LundunLine("JAMES", "We'll fix it, Uncle. All of it."),
            new LundunLine("UNCLE FEMI", "You cannot fix Hollis with a broom.")));
    }

    // ------------------------------------------------------------------ the chase
    /// He jumps on a bike and rides for the Underground; a bike's left beside James.
    void Bolt(LundunMissionRun run)
    {
        if (tayo == null) return;
        LundunNavigation nav = LundunNavigation.Instance;
        LundunBikes bikes = LundunBikes.Instance;
        Mesh frame, wheel; Material mat;
        if (nav == null || nav.Graph == null || bikes == null || !bikes.Parts(out frame, out wheel, out mat) || station < 0) { RunInstead(); return; }
        LundunRoadGraph g = nav.Graph;
        Vector3 here = tayo.transform.position;
        int edge; double t, dist;
        g.NearestOnRoad(new NavPoint(here.x, here.z), out edge, out t, out dist);
        if (edge < 0) { RunInstead(); return; }
        int a, b; g.EdgeEnds(edge, out a, out b);
        Vector3 top = LundunTube.Instance.Top(station);
        int goal = g.NearestNode(new NavPoint(top.x, top.z));
        List<int> path = g.NodePath(b, goal, a);                                       // no U-turn at the start
        int from = a, to = b;
        if (path == null || path.Count == 0) { from = b; to = a; path = g.NodePath(a, goal, b); }
        if (path == null || path.Count < 1) { RunInstead(); return; }
        if (path[0] != to) path.Insert(0, to);

        NavPoint pa = g.NodePosition(from), pb = g.NodePosition(to);
        Vector3 A = new Vector3((float)pa.x, 0f, (float)pa.z), B = new Vector3((float)pb.x, 0f, (float)pb.z);
        Vector3 dir = (B - A).normalized;
        float s = Mathf.Clamp(Vector3.Dot(here - A, dir), 2f, Mathf.Max(2f, Vector3.Distance(A, B) - 6f));
        double w; string cls; g.EdgeInfo(from, to, out w, out cls);
        Vector3 left = new Vector3(-dir.z, 0f, dir.x);
        Vector3 at = LundunMissionRun.Ground(A + dir * s + left * Mathf.Max(1.2f, (float)(w * 0.5) - 1.1f));

        tayoBike = new GameObject("TayoBike");
        tayoBike.transform.SetPositionAndRotation(at + Vector3.up * 0.05f, Quaternion.LookRotation(dir));
        tayoBike.AddComponent<CharacterController>();
        LundunBike bike = tayoBike.AddComponent<LundunBike>();
        bike.Build(frame, wheel, mat);
        int npc = LayerMask.NameToLayer("NPC");
        if (npc >= 0) tayoBike.layer = npc;
        run.Spawn(tayoBike);
        cyclist = tayoBike.AddComponent<LundunCyclist>();
        cyclist.Begin(g, from, to, tayo, 7.6f);
        cyclist.Follow(path, true);

        // James's, at the kerb beside him
        Transform j = LundunMissionRun.James();
        if (j != null)
        {
            Vector3 jb = LundunMissionRun.Ground(j.position + j.right * 1.6f);
            bikes.LeaveBike(jb, Quaternion.LookRotation(dir));
            LundunMissions.Toast("A Lundun Cycles bike's been left for you\nRIDE to get on");
        }
    }

    /// No bikes or roads (the city's not all built): he runs for it on foot instead.
    void RunInstead()
    {
        cameOff = true;
        if (tayo == null) return;
        runner = tayo.GetComponent<LundunRunner>() ?? tayo.AddComponent<LundunRunner>();
        runner.speed = 5.6f;
        runner.Flee();
    }

    /// Off the bike at the station, down the steps and gone.
    void DownTheSteps()
    {
        if (tayo == null) return;
        LundunBike b = tayoBike != null ? tayoBike.GetComponent<LundunBike>() : null;
        if (b != null && b.Ridden) b.Dismount(false);
        tayo.transform.rotation = Quaternion.Euler(0f, tayo.transform.eulerAngles.y, 0f);
        runner = tayo.GetComponent<LundunRunner>() ?? tayo.AddComponent<LundunRunner>();
        runner.speed = 5.6f;
        runner.RunTo(StationTop(), true);
        LundunTube.ForcedTo = Stop;
        LundunTube.ForcedLabel = "FOLLOW TAYO";
    }

    /// James comes up at Canary Quay: Tayo's just ahead, running.
    void UpAtTheQuay()
    {
        LundunTube.ForcedTo = null;
        LundunTube tube = LundunTube.Instance;
        if (tayo == null || tube == null) return;
        int q = tube.Find(Stop);
        if (q < 0) return;
        Vector3 at = LundunMissionRun.Ground(tube.Top(q) + tube.Out(q) * 14f);
        tayo.SetActive(true);
        NavMeshAgent a = tayo.GetComponent<NavMeshAgent>();
        tayo.transform.SetPositionAndRotation(at, Quaternion.LookRotation(tube.Out(q)));
        if (a != null) { if (!a.enabled) a.enabled = true; if (a.isOnNavMesh) a.Warp(at); }
        runner = tayo.GetComponent<LundunRunner>() ?? tayo.AddComponent<LundunRunner>();
        runner.speed = 5.6f;
        runner.Flee();
    }

    bool At(string name)
    {
        LundunTube tube = LundunTube.Instance;
        Transform j = LundunMissionRun.James();
        int i = tube != null ? tube.Find(name) : -1;
        return i >= 0 && j != null && LundunMissionRun.FlatDistance(j.position, tube.Top(i)) < 12f;
    }

    Vector3 StationTop()
    {
        LundunTube tube = LundunTube.Instance;
        return tube != null && station >= 0 ? LundunMissionRun.Ground(tube.Top(station)) : LundunMissionRun.Ground(Circus);
    }

    /// The Underground station nearest the Circus (not Tayo's stop).
    static int NearestStation(Vector3 from)
    {
        LundunTube tube = LundunTube.Instance;
        if (tube == null || tube.Names == null) return -1;
        int best = -1; float bd = float.MaxValue;
        for (int i = 0; i < tube.Names.Length; i++)
        {
            if (tube.Names[i] == Stop) continue;
            float d = LundunMissionRun.FlatDistance(tube.Top(i), from);
            if (d < bd) { bd = d; best = i; }
        }
        return best;
    }

    public override int Medal(LundunMissionRun run) { return run.Elapsed < 420f ? 3 : run.Elapsed < 600f ? 2 : 1; }

    public override void Tidy() { tayo = null; tayoBike = null; cyclist = null; runner = null; chose = false; LundunTube.ForcedTo = null; }
}
