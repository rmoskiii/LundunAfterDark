using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// Every story mission, in order. S3: 01 One Last Favour, 02 Easy Money, 03 No Way Home (they replace S1's test
/// mission). S4: 04 Estate Business, 05 Uncle's Rules, 06 Passenger Problems. S5: 07 Home Turf, 08 Blood Is Thicker
/// (LundunMissionBook5.cs). Each opens once the one before is passed.
public static class LundunMissionBook
{
    static List<LundunMission> all;

    public static List<LundunMission> All
    {
        get
        {
            if (all == null) all = new List<LundunMission>
            {
                new M01OneLastFavour(), new M02EasyMoney(), new M03NoWayHome(),                // S3
                new M04EstateBusiness(), new M05UnclesRules(), new M06PassengerProblems(),     // S4
                new M07HomeTurf(), new M08BloodIsThicker(),                                     // S5
                new M09PaperTrail(), new M10OpenForBusiness(),                                  // S6
                new M11FamilyMeeting(),                                                         // S9a
                new M12LundunAfterDark(),                                                       // S9b: the finale
                // S8: side missions (not among the 12; 13 and 14 can be played again and again)
                new M13KemisDeliveries(), new M14ProveYourself(), new M15Warehouse9(),
            };
            return all;
        }
    }

    public static bool Done(LundunStoryData d, string id) { return d != null && d.flags.Contains("done:" + id); }
}

// ======================================================================================================================
/// 01 One Last Favour (the tutorial). Phase 5 (A1): a cold open first (three weeks later: the palace, the police, Hollis's
/// helicopter), then Kemi at James's door. He drives her to LAGOS FOODS in his Kestrel (quicker than the old walk); the
/// family in the shop (Uncle Femi, Marcus, Kemi, then Leah drops in); drive to the Gilmore Estate for Uncle's parcel
/// (every camera on the estate smashed or sprayed: no CCTV in this one). One of the Tower Block Kings grabs the parcel
/// and runs: sprint after him and tackle him, then fight him and his mate, pick the parcel up, take it back to the shop,
/// go home. Kemi's text at the end points at the map and the pause menu (work, crew). No police unless you make some.
/// Afterwards LAGOS FOODS is open in the day, with Uncle Femi behind the counter.
public class M01OneLastFavour : LundunMission
{
    static readonly Vector3 Courtyard = new Vector3(330f, 0f, 380f);         // the Gilmore Estate's courtyard ("The Pickup")
    GameObject spareCar;
    LundunSnatch snatch;

    public override string Id { get { return "m01_one_last_favour"; } }
    public override string Title { get { return "One Last Favour"; } }
    public override string Teaser { get { return "Uncle Femi's called the whole family to LAGOS FOODS. Kemi's been sent to drag you there."; } }
    public override string Giver { get { return "KEMI"; } }
    public override long Reward { get { return 250; } }
    public override string MedalNote { get { return "under 7 minutes, no police"; } }
    public override bool Available(LundunStoryData d) { return !LundunMissionBook.Done(d, Id); }

    /// Kemi waits by James's front door.
    public override Vector3 StartAt
    {
        get { return LundunStoryPlaces.Home + Quaternion.Euler(0f, LundunStoryPlaces.HomeYaw, 0f) * Vector3.forward * 3f; }
    }
    public override Vector3 GiverFacing { get { return Quaternion.Euler(0f, LundunStoryPlaces.HomeYaw, 0f) * Vector3.forward; } }

    /// James's own car, the Kestrel on his drive (a stand-in by the door if it isn't there).
    GameObject Kestrel()
    {
        LundunPlaces p = LundunPlaces.Instance;
        if (p != null && p.Kestrel != null && LundunStoryPlaces.Fine(p.Kestrel.gameObject)) return p.Kestrel.gameObject;
        if (!LundunStoryPlaces.Fine(spareCar)) { LundunMissions.LetGo(spareCar); spareCar = LundunStoryPlaces.ParkCar(StartAt + Quaternion.Euler(0f, LundunStoryPlaces.HomeYaw, 0f) * Vector3.forward * 4f, 1); }
        return spareCar;
    }

    public override void Build(LundunMissionRun run)
    {
        double W = LundunStoryPlaces.LagosWidth;
        float y;
        Vector3 door = LundunStoryPlaces.LagosDoor;
        Vector3 counter = LundunStoryPlaces.Lagos(LundunShopLayout.AtCounter(W), out y);
        run.Zone(Courtyard, 45f);
        run.Zone(door, 14f);                                                    // the shop's doorstep kept clear

        // the shop open, the family in it
        run.Add(new LundunDo(r =>
        {
            LundunStoryShop.MissionOpen = true;
            float fy; Vector3 p;
            p = LundunStoryPlaces.Lagos(LundunShopLayout.Behind(W), out fy); LundunCrew.Stage("UNCLE FEMI", p, fy);
            p = LundunStoryPlaces.Lagos(LundunShopLayout.ByFridge(W), out fy); LundunCrew.Stage("MARCUS", p, fy);
        }));
        run.Add(new LundunColdOpen());                                         // Phase 5: three weeks later...
        run.Add(new LundunJoin("KEMI")).Checkpoint(LundunCheckpoint.Major);   // (a retry never plays the cold open again)
        run.Add(new LundunTalk("",
            new LundunLine("KEMI", "James! Good, you're up. Uncle wants everyone at the shop. Family meeting."),
            new LundunLine("JAMES", "A family meeting? Who's died?"),
            new LundunLine("KEMI", "Nobody. Yet. Two minutes in the car. You're driving.")));
        run.Add(new LundunEnterCar(() => Kestrel(), "Get in your car", "Your car's been wrecked"));
        run.Add(new LundunDriveTalk(door, 9f, "Drive to LAGOS FOODS on Rye Lane",
            new LundunLine("KEMI", "Uncle's been funny all week. Phone calls in the back room, door shut."),
            new LundunLine("JAMES", "Funny how?"),
            new LundunLine("KEMI", "Funny like scared. Don't tell him I said that.")));
        run.Add(new LundunGoTo(counter, 1.6f, "Go in and see Uncle Femi", false));
        run.Add(new LundunDo(r => { float fy; Vector3 p = LundunStoryPlaces.Lagos(LundunShopLayout.InAisle(W), out fy); LundunCrew.Stage("KEMI", p, fy); }));
        run.Add(new LundunTalk("",
            new LundunLine("UNCLE FEMI", "Ah! The prince arrives. Ten minutes late, as usual."),
            new LundunLine("MARCUS", "Man's always late. You good, bro?"),
            new LundunLine("UNCLE FEMI", "Listen. A man on the Gilmore Estate is holding a parcel for me. Spices, from home. You collect it, you bring it here. Simple."),
            new LundunLine("JAMES", "Why can't Marcus go?"),
            new LundunLine("MARCUS", "Because I've got work in the morning. A real job, with a payslip.")));
        // Leah drops in from her shift
        run.Add(new LundunDo(r =>
        {
            float fy; Vector3 inside = LundunStoryPlaces.Lagos(LundunShopLayout.Inside(W), out fy);
            LundunCrew.Stage("LEAH", LundunStoryPlaces.LagosDoor + Quaternion.Euler(0f, fy, 0f) * Vector3.right * 3f, fy);
            LundunCrew.Stage("LEAH", inside, fy);
        }));
        run.Add(new LundunWait(1.5f));
        run.Add(new LundunTalk("",
            new LundunLine("LEAH", "Sorry, sorry, my shift ran over. Did I miss the drama?"),
            new LundunLine("KEMI", "James is doing a pickup for Uncle."),
            new LundunLine("LEAH", "Be careful, yeah? Text me when you're back."),
            new LundunLine("UNCLE FEMI", "Take your car. And James: be polite. Polite costs nothing.")));

        // the estate: every camera there's been seen to (set dressing: this one has no CCTV)
        run.Add(new LundunDo(r => SmashedCameras(r)));
        run.Add(new LundunGoTo(Courtyard, 12f, "Drive to the Gilmore Estate for Uncle Femi's parcel"));
        run.Add(new LundunTalk("",
            new LundunLine("JAMES", "Evening. Femi Adeyemi sent me. I'm here for his parcel."),
            new LundunLine("TOWER BLOCK KING", "Parcel? Nah. That's ours now. Kings' ends, Kings' parcel. Tax, innit."),
            new LundunLine("TOWER BLOCK KING", "And don't look for cameras, bruv. Ain't none working round here. We see to that."),
            new LundunLine("JAMES", "It's my uncle's. Hand it over."),
            new LundunLine("TOWER BLOCK KING", "Come and get it then!"))).Checkpoint(LundunCheckpoint.Major);
        snatch = new LundunSnatch(() => Courtyard + new Vector3(3f, 0f, 2f), "He's off with the parcel! Catch him");
        run.Add(snatch);
        run.Add(new LundunFight(() => snatch.Fighters, "Get Uncle Femi's parcel back"));
        run.Add(new LundunCollect(() => snatch.DroppedAt, "parcel", "Pick up the parcel"));
        run.Add(new LundunTalk("",
            new LundunLine("KEMI", "(text) All good?? Uncle's wearing a hole in the floor."),
            new LundunLine("JAMES", "Bit of trouble. Got it though. On my way."))).Checkpoint(LundunCheckpoint.Minor);
        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunGoTo(door, 3.5f, "Take the parcel back to LAGOS FOODS"));
        run.Add(new LundunGoTo(counter, 1.6f, "Give Uncle Femi his parcel", false));
        run.Add(new LundunTalk("",
            new LundunLine("UNCLE FEMI", "Your lip is bleeding. What happened to you?"),
            new LundunLine("JAMES", "Two lads from the towers tried to tax it. Tower Block Kings, they said."),
            new LundunLine("UNCLE FEMI", "...Kingsley's boys. Don't go back to that estate, James. Promise me."),
            new LundunLine("MARCUS", "Uncle? Who's Kingsley?"),
            new LundunLine("UNCLE FEMI", "Nobody you need to know. Go home, all of you. Thank you, James.")));
        run.Add(new LundunGoTo(LundunStoryPlaces.Home, 6f, "Go home and get some sleep"));
        // Kemi's text: the map, the pause menu, the work going
        run.Add(new LundunTalk("",
            new LundunLine("KEMI", "(text) Uncle says thank you. He won't say it to your face, so I'm saying it."),
            new LundunLine("KEMI", "(text) Tap your map for the whole city. Pause menu's got your crew, your missions, and any work going.")));
        run.Add(new LundunDo(r => LundunMissions.Toast("YOUR CITY\nTap the minimap: the full map\nPause: MISSIONS · CREW · DAILY")));
    }

    /// Three cameras on poles round the courtyard, every one knocked down and sprayed.
    static void SmashedCameras(LundunMissionRun run)
    {
        Color[] paint = { new Color(0.85f, 0.1f, 0.12f), new Color(0.2f, 0.75f, 0.25f), new Color(0.05f, 0.05f, 0.06f) };
        float[] angle = { 35f, 160f, 270f };
        float[] dist = { 12f, 15f, 11f };
        for (int i = 0; i < angle.Length; i++)
        {
            Vector3 p = Courtyard + Quaternion.Euler(0f, angle[i], 0f) * Vector3.forward * dist[i];
            if (!UnityEngine.AI.NavMesh.SamplePosition(LundunMissionRun.Ground(p) + Vector3.up, out UnityEngine.AI.NavMeshHit hit, 5f, UnityEngine.AI.NavMesh.AllAreas)) continue;
            Vector3 toMiddle = Courtyard - hit.position; toMiddle.y = 0f;
            float yaw = toMiddle.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toMiddle).eulerAngles.y : 0f;
            LundunMissionCam c = LundunMissionCam.CreateOnPole(run, "estate_" + i, hit.position, 4f, yaw, 0f);
            c.Vandalise(paint[i]);
        }
    }

    public override int Medal(LundunMissionRun run)
    {
        if (run.Elapsed < 420f && !run.wanted) return 3;
        if (run.Elapsed < 600f) return 2;
        return 1;
    }

    public override void Tidy() { LundunMissions.LetGo(spareCar); spareCar = null; }
}

// ======================================================================================================================
/// 02 Easy Money. Tayo at FADEZ BARBERS has a plan for James's Kestrel payments: a pistol, his car, Basketful in
/// Brixtun. Phase 5 (A1): the first proper CCTV lesson. Outside, Tayo shows James the camera over the tills (its own
/// picture: what it sees); an optional KILL THE CAMERA (+£200, and gold needs it): shoot it before you rob the till and
/// it has nothing, but it's a gunshot (more police). Leave it and it films James at the till, and that footage is
/// remembered (LundunEvidence "basketful_cam", and a still of it: Hollis shows it to James on his phone in M05). Rob a till (the money counts up as you hold; a
/// cash bag on his back after), a police car pulls up as he comes out, back to Tayo's car, lose the police, swap cars at
/// the lock-up under the railway arches by Brixtun Station.
public class M02EasyMoney : LundunMission
{
    public const string Evidence = "basketful_cam";
    GameObject tayoCar, swapCar;
    LundunMissionCam storeCam;
    bool robbed;

    public override string Id { get { return "m02_easy_money"; } }
    public override string Title { get { return "Easy Money"; } }
    public override string Teaser { get { return "Tayo's at FADEZ BARBERS. He reckons he can fix your money problems in one night."; } }
    public override string Giver { get { return "TAYO"; } }
    public override long Reward { get { return 1200; } }
    public override string MedalNote { get { return "kill the camera, under 8 minutes"; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m01_one_last_favour") && !LundunMissionBook.Done(d, Id); }

    public override Vector3 StartAt { get { float y; return LundunStoryPlaces.Fadez(out y); } }
    public override Vector3 GiverFacing { get { float y; LundunStoryPlaces.Fadez(out y); return Quaternion.Euler(0f, y, 0f) * Vector3.forward; } }

    public override void Build(LundunMissionRun run)
    {
        float fy;
        Vector3 fadez = LundunStoryPlaces.Fadez(out fy);
        Vector3 outside, inside;
        LundunStoryPlaces.Store("Basketful", out outside, out inside);
        Quaternion lr; Vector3 lockUp = LundunStoryPlaces.LockUp(out lr);
        Vector3 tills;
        if (!LundunStoryPlaces.Tills("Basketful", out tills)) tills = inside;

        run.Add(new LundunDo(r => { LundunCashBag.Carry(false); if (!LundunStoryPlaces.Fine(tayoCar)) { LundunMissions.LetGo(tayoCar); tayoCar = LundunStoryPlaces.ParkCar(fadez + Quaternion.Euler(0f, fy, 0f) * Vector3.forward * 6f, 2); } }));
        run.Add(new LundunJoin("TAYO"));
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "Jamesy! You're not here for a trim. Sit down, it's business."),
            new LundunLine("JAMES", "What business?"),
            new LundunLine("TAYO", "Your Kestrel. Two payments behind, bruv. Don't look at me like that, the whole ends knows."),
            new LundunLine("TAYO", "Basketful, Brixtun. One till. Quick lick: in, out, nobody gets hurt, and your payments are sorted."),
            new LundunLine("JAMES", "A lick. You mean rob a supermarket."),
            new LundunLine("TAYO", "I mean fix your life, bro. I've got the whip outside. And I've got this.")));
        run.Add(new LundunDo(r =>
        {
            LundunWeapons w = LundunWeapons.Instance;
            if (w == null) return;
            w.Give(LundunWeapons.Kind.Pistol, 36);
            w.Equip((int)LundunWeapons.Kind.Pistol, false);
            LundunMissions.Toast("PISTOL\nSwap weapons from the weapon panel");
        }));
        run.Add(new LundunEnterCar(() => tayoCar, "Get in Tayo's car", "Tayo's car was wrecked"));
        run.Add(new LundunGoTo(outside, 16f, "Drive to Basketful in Brixtun"));
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "Park up. I'll keep the engine running."),
            new LundunLine("JAMES", "You're not coming in?"),
            new LundunLine("TAYO", "Someone's got to drive, innit. But clock this first.")));

        // Phase 5: the camera over the tills, what it sees, and the choice
        run.Add(new LundunDo(r =>
        {
            robbed = false;
            Vector3 inward = tills - inside; inward.y = 0f;
            inward = inward.sqrMagnitude > 0.01f ? inward.normalized : Vector3.forward;
            Vector3 lens = LundunMissionRun.Ground(tills) + inward * 1.1f + Vector3.up * 2.7f;
            Vector3 view = inside - lens; view.y = 0f;
            storeCam = LundunMissionCam.Create(r, Evidence, lens, Quaternion.LookRotation(view.sqrMagnitude > 0.01f ? view : -inward).eulerAngles.y, 30f, 12f, 30f, -30f);
            storeCam.judgeSelf = false;                                        // it doesn't call anyone: it records
            storeCam.Down += (c, quiet) =>
            {
                if (!robbed) r.CompleteOptional("kill_camera");
                if (!quiet && WantedSystem.Instance != null) WantedSystem.Instance.AddCrime(CrimeType.Gunfire, c.transform.position);   // a shot out camera, on top of the shot
            };
            r.AddOptional("kill_camera", "KILL THE CAMERA before the till", 200);
        }));
        run.Add(new LundunFeedCut(() => storeCam != null ? storeCam.Lens : tills + Vector3.up * 2.7f, () => tills + Vector3.up * 1f,
            "BASKETFUL  ·  CAM 02  ·  TILLS", "THE CAMERA OVER THE TILLS", 3.5f));
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "See that? Camera right over the tills. Leave it and it's got your face, clear as day."),
            new LundunLine("JAMES", "And if I shoot it?"),
            new LundunLine("TAYO", "Then the whole of Brixtun hears a gunshot. Your call, bruv. Gun out, straight to a till. Go, go.")));
        run.Add(new LundunDo(r =>
        {
            GameObject c = tayoCar;
            if (c != null) LundunCrew.Stage("TAYO", c.transform.position - c.transform.right * 1.8f, c.transform.eulerAngles.y);
        }));
        run.Add(new LundunGoTo(inside, 3f, "Go into Basketful", false)).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunRob("Basketful", 900, "Rob a till: gun out, hold the button"));
        // what the camera got (or didn't), the bag, and the police pulling up outside
        run.Add(new LundunDo(r =>
        {
            robbed = true;
            LundunCashBag.Carry(true);
            bool caught = storeCam != null && !storeCam.Destroyed;
            if (caught)
            {
                LundunEvidence.Recorded(Evidence);
                if (LundunCctvFeed.Instance != null)
                    LundunCctvFeed.Instance.StartCoroutine(LundunCctvFeed.Scripted(storeCam.Lens, () => LundunMissionRun.James().position + Vector3.up * 1.1f,
                        "BASKETFUL  ·  CAM 02  ·  TILLS", "RECORDING  ·  SUBJECT AT THE TILL", 3f, LundunCctvFeed.Mark.Subject,
                        () => LundunMissionRun.James().position + Vector3.up * 1f, "SUBJECT 01", Evidence));   // A2: a still kept for Hollis (M05)
            }
            else LundunEvidence.Destroyed(Evidence);
            PoliceDirector.SendCarTo(outside);
        }));
        run.Add(new LundunJoin("TAYO"));
        run.Add(new LundunEnterCar(() => tayoCar, "Get back to Tayo's car", "Tayo's car was wrecked"));
        run.Add(new LundunTalk("", new LundunLine("TAYO", "GO GO GO! Floor it!")));
        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunDo(r => { if (!LundunStoryPlaces.Fine(swapCar)) { LundunMissions.LetGo(swapCar); swapCar = LundunStoryPlaces.ParkCar(lockUp, 5); } })).Checkpoint(LundunCheckpoint.Minor);
        run.Add(new LundunGoTo(lockUp, 12f, "Drive to the lock-up under the railway arches"));
        run.Add(new LundunEnterCar(() => swapCar, "Swap cars", "The swap car was wrecked"));
        run.Add(new LundunDo(r => LundunCashBag.Carry(false)));                // the bag goes in the boot
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "See? Easy bread. Your half's yours, my half's mine."),
            new LundunLine("JAMES", "My hands won't stop shaking."),
            new LundunLine("TAYO", "That's the buzz, bro. You get used to it. Then you start to miss it.")));
    }

    public override int Medal(LundunMissionRun run)
    {
        int m = run.Elapsed < 480f ? 3 : run.Elapsed < 720f ? 2 : 1;
        if (!run.OptionalDone("kill_camera")) m = Mathf.Min(m, 2);              // gold needs the camera dealt with
        return m;
    }

    public override void Tidy()
    {
        LundunCashBag.Carry(false);
        LundunMissions.LetGo(tayoCar); tayoCar = null; LundunMissions.LetGo(swapCar); swapCar = null;
        storeCam = null;
    }
}

// ======================================================================================================================
/// 03 No Way Home. Phase 5 (A2): it opens on the council's CCTV: a junction camera, PLATE READ, the swap car's number
/// boxed. Then Tayo's phone rings: the plate's been clocked. 3★, and the roads out of Brixtun are shut where you can see
/// it (two roadblocks put on the roads either side of the lock-up, and a cut to one on CCTV). Ditch the car, get to
/// Brixtun Station on foot and catch the train before it goes. The station has a camera on a pole over the entrance,
/// sweeping the approach: time it, or shoot it out (loud). If it sees James go in, it keeps a still (LundunEvidence
/// "brixtun_station": Hollis can use it in M05) and the police are waiting at Rye Lane: a short foot escape off the
/// platform. Unseen (optional, +£300): the train's clean. Tayo and James fall out on the ride; walk home.
public class M03NoWayHome : LundunMission
{
    public const string Evidence = "brixtun_station";
    const string Plate = "LV11 KDT";
    GameObject swapCar;
    LundunMissionCam stationCam;
    bool seen;
    Vector3 block;
    bool blocked;

    public override string Id { get { return "m03_no_way_home"; } }
    public override string Title { get { return "No Way Home"; } }
    public override string Teaser { get { return "Tayo's still at the lock-up, on the phone and sweating. Something's wrong."; } }
    public override string Giver { get { return "TAYO"; } }
    public override long Reward { get { return 800; } }
    public override string MedalNote { get { return "the station camera never sees you, under 6 minutes"; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m02_easy_money") && !LundunMissionBook.Done(d, Id); }

    /// On the pavement by the lock-up (not where the car's parked, so it doesn't open as you sit there).
    public override Vector3 StartAt
    {
        get { Quaternion r; Vector3 p = LundunStoryPlaces.LockUp(out r); return LundunMissionRun.Ground(p + r * Vector3.left * 2.6f + r * Vector3.forward * 5f); }
    }
    public override Vector3 GiverFacing { get { Quaternion r; LundunStoryPlaces.LockUp(out r); return r * Vector3.right; } }

    public override void Build(LundunMissionRun run)
    {
        Vector3 station = LundunStoryPlaces.BrixtunStation;
        Vector3 platform = LundunStoryPlaces.BrixtunPlatform;
        Quaternion lr; Vector3 lockUp = LundunStoryPlaces.LockUp(out lr);
        Vector3 fwd = lr * Vector3.forward, right = lr * Vector3.right;

        run.Add(new LundunNow(r => blocked = false));
        // the swap car from last night, still at the lock-up
        run.Add(new LundunDo(r => { if (!LundunStoryPlaces.Fine(swapCar)) { LundunMissions.LetGo(swapCar); swapCar = LundunStoryPlaces.ParkCar(lockUp, 5); } }));
        run.Add(new LundunJoin("TAYO"));
        // the plate read, as the council's camera saw it
        run.Add(new LundunFeedCut(() => CarAt(lockUp) - fwd * 15f + right * 3f + Vector3.up * 5.5f, () => CarAt(lockUp) + Vector3.up * 0.6f,
            "BRIXTUN ROAD  ·  CAM 0217  ·  ANPR", "PLATE READ  ·  " + Plate + "  ·  WANTED: BASKETFUL ROBBERY", 4f,
            LundunCctvFeed.Mark.Plate, () => CarAt(lockUp) - CarFwd(fwd) * 2.2f + Vector3.up * 0.55f, Plate));
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "(on the phone) What do you mean they've got the plate? ...Bruv."),
            new LundunLine("TAYO", "James. The swap car's been clocked. Feds have got the plate."),
            new LundunLine("JAMES", "You said it was clean!")));
        run.Add(new LundunDo(r =>
        {
            LundunRoadblocks.MissionFromStars = 3;
            PlayerHealth ph = PlayerHealth.Instance;
            if (WantedSystem.Instance != null && ph != null) WantedSystem.Instance.RaiseTo(3, ph.Body.position, false);
            // the roads out shut, close enough to see from here
            Vector3 at;
            if (LundunRoadblocks.PlaceAt(lockUp + fwd * 95f, out at)) { block = at; blocked = true; }
            if (LundunRoadblocks.PlaceAt(lockUp - fwd * 95f, out at) && !blocked) { block = at; blocked = true; }
            LundunMissions.Toast("The roads out of Brixtun are being closed");
        }));
        run.Add(new LundunFeedCut(() => BlockLens(lockUp, fwd), () => (blocked ? block : lockUp + fwd * 95f) + Vector3.up * 1f,
            "BRIXTUN ROAD  ·  CAM 0219", "ROADBLOCK  ·  ALL ROADS OUT OF BRIXTUN CLOSED", 3f));
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "Old Bill's shutting the roads. We ditch the car and get the train. Brixtun Station, now!"),
            new LundunLine("TAYO", "And clock the station. There's a camera on a pole by the door. If it sees us go in, they'll be waiting at the other end."),
            new LundunLine("JAMES", "So it doesn't see us."),
            new LundunLine("TAYO", "Time it when it's looking away, or shoot it. But shooting's loud, bro.")));
        run.Add(new LundunWhen("Ditch the car", r => !LundunMissionRun.Driving()));
        // the station's camera, on a pole by the entrance, sweeping the way in
        run.Add(new LundunDo(r =>
        {
            seen = false;                                                      // (a retry from the station starts unseen again)
            Vector3 outward = lockUp - station; outward.y = 0f;
            if (outward.sqrMagnitude < 1f) { outward = station - platform; outward.y = 0f; }
            outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, outward);
            Vector3 foot = LundunMissionRun.Ground(station + side * 2.6f + outward * 0.8f);
            stationCam = LundunMissionCam.CreateOnPole(r, Evidence, foot, 4.2f, Quaternion.LookRotation(outward).eulerAngles.y, 40f, 16f, 28f, -24f);
            stationCam.sweepSeconds = 7f;
            stationCam.Spotted += (c, clear) =>
            {
                if (seen) return;
                seen = true;
                LundunEvidence.Recorded(Evidence);
                LundunMissions.Toast("THE STATION CAMERA SAW YOU\nThey'll know you're on the train");
                if (LundunCctvFeed.Instance != null)
                    LundunCctvFeed.Instance.StartCoroutine(LundunCctvFeed.Scripted(c.Lens, () => LundunMissionRun.James().position + Vector3.up * 1.1f,
                        "BRIXTUN STATION  ·  CAM 01  ·  ENTRANCE", "SUBJECT ENTERING THE STATION", 2.5f, LundunCctvFeed.Mark.Subject,
                        () => LundunMissionRun.James().position + Vector3.up * 1f, "SUBJECT 01", Evidence));
            };
            stationCam.Down += (c, quiet) => { if (!seen) LundunEvidence.Destroyed(Evidence); };
            r.AddOptional("station_unseen", "GET ON THE TRAIN without the station camera seeing you", 300);
        }));
        run.Add(new LundunDo(r => { if (LundunTrain.Instance != null) LundunTrain.Instance.HoldAt(true, 240f); }));
        run.Add(new LundunGoTo(station, 6f, "Get to Brixtun Station on foot")).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunWhen("Catch the train before it leaves", r => LundunTrain.Instance == null || LundunTrain.Instance.Riding)).Mark(platform, 3f, false);
        run.Add(new LundunNow(r =>
        {
            if (stationCam != null) stationCam.judgeSelf = false;                // on the train: it's done watching
            if (!seen) r.CompleteOptional("station_unseen");
        }));
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "...Okay. Okay. We're good."),
            new LundunLine("JAMES", "We're good? They've got that car, Tayo. With us all over it."),
            new LundunLine("TAYO", "Gloves, bro. I'm not a wasteman."),
            new LundunLine("JAMES", "You are. And now so am I."),
            new LundunLine("TAYO", "You took the bread, James. Nobody made you."),
            new LundunLine("JAMES", "Get off at Rye Lane. Don't ring me for a bit."),
            new LundunLine("TAYO", "Say less. But take this. Kemi had it stashed at the lock-up. Better you than the feds."),
            new LundunLine("JAMES", "A shotgun? On a train, Tayo?"),
            new LundunLine("TAYO", "Under your coat. Up close, it ends arguments.")));
        run.Add(new LundunNow(r =>
        {
            LundunWeapons w = LundunWeapons.Instance;
            if (w != null) w.Give(LundunWeapons.Kind.Shotgun, 16);
            LundunMissions.Toast("SHOTGUN\nSwap weapons from the weapon panel");
        }));
        run.Add(new LundunWhen("Ride the train to Rye Lane", r => LundunTrain.Instance == null || (!LundunTrain.Instance.Riding && LundunTrain.Instance.AtRyeLane)));
        // the camera saw them go in: the police are at Rye Lane
        run.Add(new LundunNow(r => { if (seen) PoliceAtRyeLane(r); }));
        run.Add(new LundunTalkPick(() => seen
            ? new[] { new LundunLine("TAYO", "Bruv. Look at the platform. That's feds."),
                      new LundunLine("JAMES", "The camera at Brixtun. It clocked us going in."),
                      new LundunLine("TAYO", "Split up. Go, go!") }
            : new[] { new LundunLine("TAYO", "Nobody waiting. See? Clean."),
                      new LundunLine("JAMES", "Clean. Right.") }));
        run.Add(new LundunLeave("TAYO"));
        run.Add(new LundunDo(r => LundunRoadblocks.MissionFromStars = 0));
        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunGoTo(LundunStoryPlaces.Home, 6f, "Walk home"));
        // v50: the arms dealer introduces himself (word gets round)
        run.Add(new LundunTalk("",
            new LundunLine("ARMS DEALER", "(phone) Mr Adeyemi. You don't know me. A friend of Kemi's says you might need supplies."),
            new LundunLine("JAMES", "Who is this?"),
            new LundunLine("ARMS DEALER", "(phone) Surplus and Security, under the railway arches. Ammunition, protection, better tools. Trade only."),
            new LundunLine("ARMS DEALER", "(phone) Cash, no questions, no receipts. Come and see me when you're ready.")));
        run.Add(new LundunNow(r => LundunMissions.Toast("ARMS DEALER\nUnder the arches: ammo, armour and guns")));
    }

    Vector3 CarAt(Vector3 lockUp) { return LundunStoryPlaces.Fine(swapCar) ? swapCar.transform.position : lockUp; }
    Vector3 CarFwd(Vector3 fallback) { return LundunStoryPlaces.Fine(swapCar) ? swapCar.transform.forward : fallback; }

    /// A council camera up a lamp post, looking down the road at the roadblock from the lock-up's side.
    Vector3 BlockLens(Vector3 lockUp, Vector3 fwd)
    {
        Vector3 b = blocked ? block : lockUp + fwd * 95f;
        Vector3 back = lockUp - b; back.y = 0f;
        back = back.sqrMagnitude > 0.01f ? back.normalized : -fwd;
        return b + back * 22f + Vector3.Cross(Vector3.up, back) * 4f + Vector3.up * 6f;
    }

    /// Two officers at Rye Lane's street entrance, two stars, and a car on its way.
    static void PoliceAtRyeLane(LundunMissionRun run)
    {
        LundunTrain t = LundunTrain.Instance;
        Vector3 platform = t != null ? t.RyeLanePlatform : LundunStoryPlaces.Home;
        Vector3 door = t != null ? t.Entrance : platform;
        WantedSystem w = WantedSystem.Instance;
        if (w != null) w.RaiseTo(2, platform, true);
        PoliceAI car = PoliceDirector.CarPrefab;
        PoliceOfficer prefab = car != null ? car.OfficerPrefab : null;
        for (int i = 0; i < 2 && prefab != null; i++)
        {
            Vector3 p = door + new Vector3(i == 0 ? 1.5f : -1.5f, 0f, 0f);
            if (!NavMesh.SamplePosition(p + Vector3.up, out NavMeshHit hit, 6f, NavMesh.AllAreas)) continue;
            PoliceOfficer o = Object.Instantiate(prefab, hit.position, Quaternion.LookRotation((platform - hit.position).sqrMagnitude > 0.01f ? new Vector3(platform.x - hit.position.x, 0f, platform.z - hit.position.z) : Vector3.forward));
            o.StartPatrol(null);
            o.Respond();
            run.Spawn(o.gameObject);
        }
        PoliceDirector.SendCarTo(door);
        LundunMissions.Toast("POLICE AT RYE LANE\nThe station camera saw you get on");
    }

    public override int Medal(LundunMissionRun run)
    {
        int m = run.Elapsed < 360f ? 3 : run.Elapsed < 510f ? 2 : 1;
        if (!run.OptionalDone("station_unseen")) m = Mathf.Min(m, 2);       // gold: the station camera never had you
        return m;
    }

    public override void Tidy()
    {
        LundunRoadblocks.MissionFromStars = 0;
        LundunMissions.LetGo(swapCar); swapCar = null;
        stationCam = null;
    }
}

// ======================================================================================================================
/// 04 Estate Business. Kingsley wants a word about Basketful: James drives Marcus to the Gilmore Estate. Phase 5 (A2):
/// the Kings watch the road in on two cameras of their own (on poles, either side of the estate's entrance, looking
/// out along the road for cars). Seen: Kingsley's on Marcus's phone straight away ("Nice car, James") and more of
/// his boys are waiting with guns on the way out (5). Shoot them first or come in out of their view (optional, +£300)
/// and only 2 are ready. Then Kingsley himself, on a balcony above the courtyard: he says his piece looking down at
/// them, turns his back and walks in as his boys come out with knives. Marcus fights beside James (keep him up), then
/// the gunmen as they leave: get out of the estate, and take Marcus home.
public class M04EstateBusiness : LundunMission
{
    static readonly Vector3 Courtyard = new Vector3(330f, 0f, 380f);
    bool kingsSaw;

    public override string Id { get { return "m04_estate_business"; } }
    public override string Title { get { return "Estate Business"; } }
    public override string Teaser { get { return "Word's got round about Basketful. Kingsley wants a word, and Marcus isn't letting you go alone."; } }
    public override string Giver { get { return "MARCUS"; } }
    public override long Reward { get { return 1500; } }
    public override string MedalNote { get { return "under 7 minutes"; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m03_no_way_home") && !LundunMissionBook.Done(d, Id); }
    public override Vector3 StartAt { get { return LundunStoryPlaces.Home + Quaternion.Euler(0f, LundunStoryPlaces.HomeYaw, 0f) * Vector3.forward * 3f; } }
    public override Vector3 GiverFacing { get { return Quaternion.Euler(0f, LundunStoryPlaces.HomeYaw, 0f) * Vector3.forward; } }

    public override void Build(LundunMissionRun run)
    {
        run.Zone(Courtyard, 60f);
        run.Add(new LundunNow(r => kingsSaw = false));
        run.Add(new LundunJoin("MARCUS"));
        run.Add(new LundunTalk("",
            new LundunLine("MARCUS", "Kingsley rang me. He wants a word. About Basketful."),
            new LundunLine("JAMES", "How does Kingsley know about Basketful?"),
            new LundunLine("MARCUS", "Tayo can't hold water, bro. Everyone knows. Get the car, we're going Gilmore."),
            new LundunLine("JAMES", "And if it goes wrong?"),
            new LundunLine("MARCUS", "Then you stay behind me."),
            new LundunLine("MARCUS", "And Kingsley watches the road in. Two cameras of his own, the only ones on that estate that still work."),
            new LundunLine("JAMES", "Then he doesn't see us coming.")));
        // the Kings' own cameras on the road in
        run.Add(new LundunDo(r =>
        {
            KingsCameras(r);
            r.AddOptional("arrive_unseen", "ARRIVE UNSEEN by the Kings' cameras", 300);
        }));
        run.Add(new LundunNow(r => LundunMissions.Toast("THE KINGS' CAMERAS\nTwo on the road in: shoot them, or keep out of their view")));
        run.Add(new LundunGoTo(Courtyard, 12f, "Drive Marcus to the Gilmore Estate"));
        run.Add(new LundunDo(r =>
        {
            foreach (LundunMissionCam c in LundunMissionCam.All) if (c != null && c.id.StartsWith("kings_")) c.judgeSelf = false;   // he's here now
            if (!kingsSaw) r.CompleteOptional("arrive_unseen");
        }));
        // Kingsley, up on his balcony
        run.Add(new LundunBalcony(Courtyard, () =>
        {
            List<LundunLine> l = new List<LundunLine>();
            l.Add(kingsSaw ? new LundunLine("KINGSLEY", "James. Marcus. Saw you coming on my cameras. Nice car. Bit loud for round here.")
                           : new LundunLine("KINGSLEY", "James. Marcus. Didn't even see you come in. Somebody's been teaching you manners."));
            l.Add(new LundunLine("KINGSLEY", "That shop your friend licked pays Mr Hollis for its peace."));
            l.Add(new LundunLine("KINGSLEY", "So that was Hollis's money. And Hollis is my friend."));
            l.Add(new LundunLine("MARCUS", "Hollis? The scaffolding man?"));
            l.Add(new LundunLine("KINGSLEY", "My boys explain things better than me."));
            l.Add(new LundunLine("TOWER BLOCK KING", "Shanks out, lads."));
            return l.ToArray();
        })).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunFight(Courtyard + new Vector3(4f, 0f, 3f), 4, false, "Fight off the Tower Block Kings. Keep Marcus up", "Street", 22f) { harder = 1.5f });
        run.Add(new LundunTalkPick(() => kingsSaw
            ? new[] { new LundunLine("MARCUS", "More of them! Loads! They're strapped! Car, now!") }
            : new[] { new LundunLine("MARCUS", "Couple more, and they're strapped! Car, now!") }));
        run.Add(new LundunAmbush(Courtyard, () => kingsSaw ? 5 : 2, 140f, "Get Marcus out of the estate")).Checkpoint(LundunCheckpoint.Minor);
        run.Add(new LundunGoTo(LundunStoryPlaces.Home, 6f, "Take Marcus home"));
        run.Add(new LundunTalk("",
            new LundunLine("MARCUS", "Hollis. Uncle's been off for months. You think he owes him?"),
            new LundunLine("JAMES", "I think we ask him."),
            new LundunLine("MARCUS", "Not tonight. Get some sleep. And keep away from Tayo for a bit."),
            new LundunLine("MARCUS", "And take this. After today you need more than a pistol."),
            new LundunLine("JAMES", "Marcus. Where did you get an SMG?"),
            new LundunLine("MARCUS", "Doors I work, people leave things. Don't ask, and keep it clean.")));
        run.Add(new LundunNow(r =>
        {
            LundunWeapons w = LundunWeapons.Instance;
            if (w != null) w.Give(LundunWeapons.Kind.Smg, 90);
            LundunMissions.Toast("SMG UNLOCKED\nSwap weapons from the weapon panel");
        }));
        run.Add(new LundunLeave("MARCUS", () => LundunStoryPlaces.Home));
    }

    /// Two cameras on poles by the estate's road, either side of where it meets the courtyard, each looking out along
    /// the road for anything coming in (cars included).
    void KingsCameras(LundunMissionRun run)
    {
        Vector3 road, along;
        if (!RoadNear(Courtyard, out road, out along)) { road = LundunMissionRun.Ground(Courtyard + new Vector3(0f, 0f, -35f)); along = Vector3.right; }
        Vector3 toCourt = Courtyard - road; toCourt.y = 0f;
        toCourt = toCourt.sqrMagnitude > 0.01f ? toCourt.normalized : Vector3.forward;
        for (int i = 0; i < 2; i++)
        {
            float sgn = i == 0 ? 1f : -1f;
            Vector3 p = road + along * (sgn * 16f) + toCourt * 6f;
            if (!NavMesh.SamplePosition(LundunMissionRun.Ground(p) + Vector3.up, out NavMeshHit hit, 6f, NavMesh.AllAreas)) continue;
            Vector3 look = (road + along * (sgn * 40f)) - hit.position; look.y = 0f;
            LundunMissionCam c = LundunMissionCam.CreateOnPole(run, "kings_" + i, hit.position, 4.5f, Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look : along * sgn).eulerAngles.y, 22f, 28f, 30f, -12f);
            c.seesCars = true;
            c.sweepSeconds = 9f;
            c.Spotted += (cam, clear) =>
            {
                if (kingsSaw) return;
                kingsSaw = true;
                LundunMissions.Toast("THE KINGS' CAMERA SAW YOU\nMore of them will be waiting");
                LundunMissions.Say("KINGSLEY", "(on Marcus's phone) Nice car, James. I can see you, you know. Come on in.", 4.5f);
            };
        }
    }

    /// The road nearest a spot, and which way it runs.
    static bool RoadNear(Vector3 near, out Vector3 at, out Vector3 along)
    {
        at = near; along = Vector3.right;
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.Graph == null) return false;
        NavPoint p = nav.Graph.NearestOnRoad(new NavPoint(near.x, near.z), out int edge, out double t, out double dist);
        if (edge < 0 || dist > 70.0) return false;
        nav.Graph.EdgeEnds(edge, out int na, out int nb);
        NavPoint pa = nav.Graph.NodePosition(na), pb = nav.Graph.NodePosition(nb);
        Vector3 dir = new Vector3((float)(pb.x - pa.x), 0f, (float)(pb.z - pa.z));
        if (dir.sqrMagnitude < 1f) return false;
        along = dir.normalized;
        at = LundunMissionRun.Ground(new Vector3((float)p.x, 0f, (float)p.z));
        return true;
    }

    public override int Medal(LundunMissionRun run) { return run.Elapsed < 420f ? 3 : run.Elapsed < 600f ? 2 : 1; }
}

// ======================================================================================================================
/// 05 Uncle's Rules. Uncle Femi tells James about the debt on a long drive to Tower Plaza, Canary Quay (the first
/// A-to-Z drive, the story told in the car). Hollis wants Basketful put right: pay £1,000 of James's own money, or
/// promise him a favour (the first choice: S5 makes later scenes remember it). He gives James a carbine. Phase 5 (A2):
/// then he holds up his phone: James's face on CCTV (LundunHollisPhone: the Basketful till camera from M02 if it
/// wasn't shot out, else the station camera from M03 if it saw him, else a Canary Quay camera, live, on James right
/// now: he always has something, and the lines say which). "Copies in a safe place." As he leaves, Kingsley's boys
/// roll up in two cars with guns: the first fight with the new carbine. Lose the police, Uncle Femi back to his shop,
/// then a card ("HOLLIS OWNS JAMES NOW") and about 15 seconds of what's coming.
public class M05UnclesRules : LundunMission
{
    static readonly Vector3 TowerPlaza = new Vector3(1220f, 0f, 560f);
    static Vector3 HollisSpot { get { return LundunMissionRun.Ground(TowerPlaza + new Vector3(4f, 0f, 2f)); } }
    LundunHollisPhone.Footage footage;
    LundunRollUp rollUp;

    public override string Id { get { return "m05_uncles_rules"; } }
    public override string Title { get { return "Uncle's Rules"; } }
    public override string Teaser { get { return "Uncle Femi has something to confess, and somewhere he can't go alone."; } }
    public override string Giver { get { return "UNCLE FEMI"; } }
    public override long Reward { get { return 400; } }
    public override string MedalNote { get { return "under 11 minutes"; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m04_estate_business") && !LundunMissionBook.Done(d, Id); }
    /// At his counter.
    public override Vector3 StartAt { get { float y; return LundunStoryPlaces.Lagos(LundunShopLayout.AtCounter(LundunStoryPlaces.LagosWidth), out y); } }
    public override Vector3 GiverFacing { get { float y; LundunStoryPlaces.Lagos(LundunShopLayout.AtCounter(LundunStoryPlaces.LagosWidth), out y); return Quaternion.Euler(0f, y + 180f, 0f) * Vector3.forward; } }

    public override void Build(LundunMissionRun run)
    {
        run.Zone(TowerPlaza, 30f);
        run.Add(new LundunDo(r => LundunStoryShop.MissionOpen = true));
        run.Add(new LundunJoin("UNCLE FEMI"));
        run.Add(new LundunTalk("",
            new LundunLine("UNCLE FEMI", "James. I have something to tell you. Then you will drive me somewhere."),
            new LundunLine("JAMES", "Where?"),
            new LundunLine("UNCLE FEMI", "Canary Quay. Get the car. I will talk while you drive. It is easier to say to a windscreen.")));
        run.Add(new LundunWhen("Get in a car with Uncle Femi", r => LundunMissionRun.Driving()));
        run.Add(new LundunDriveTalk(TowerPlaza, 14f, "Drive Uncle Femi to Tower Plaza, Canary Quay",
            new LundunLine("UNCLE FEMI", "Eight years ago the shop was dying. The bank said no. Mr Hollis said yes."),
            new LundunLine("JAMES", "Hollis. The scaffolding man? Kingsley's friend?"),
            new LundunLine("UNCLE FEMI", "Nobody works for Hollis. They owe him. There is a difference, and it is worse."),
            new LundunLine("UNCLE FEMI", "I have paid him every month since. Every month. And still I owe more than I borrowed."),
            new LundunLine("JAMES", "Why didn't you tell anyone? Marcus? Mum?"),
            new LundunLine("UNCLE FEMI", "Because I am the uncle. The uncle fixes things. He does not go begging to his nephews."),
            new LundunLine("UNCLE FEMI", "Then you robbed a shop he takes money from. Now he wants to see me. And you."),
            new LundunLine("JAMES", "I'm sorry, Uncle."),
            new LundunLine("UNCLE FEMI", "Sorry is a word, my son. Let us see what he wants.")));
        run.Add(new LundunDo(r => LundunCrew.Stage("HOLLIS", HollisSpot, 250f)));
        run.Add(new LundunGoTo(TowerPlaza + new Vector3(4f, 0f, 2f), 3.5f, "Meet Hollis", false)).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunTalk("",
            new LundunLine("HOLLIS", "Mr Adeyemi. And the famous nephew. Lovely evening for it."),
            new LundunLine("HOLLIS", "Your friend took money from a shop I look after. Someone has to put that right."),
            new LundunLine("JAMES", "I'll put it right."),
            new LundunLine("HOLLIS", "Good lad. A thousand now and we're square. Or a favour, one evening, when I ask. Your choice, James.")));
        run.Add(new LundunChoice("Hollis wants it put right", "PAY £1,000", 1000, "PROMISE A FAVOUR", "choice:m05"));
        run.Add(new LundunTalkPick(() => LundunChoice.Chose("choice:m05", true)
            ? new[] { new LundunLine("HOLLIS", "Sensible. Money's honest. Off you pop."),
                      new LundunLine("UNCLE FEMI", "...That was your money, James."),
                      new LundunLine("JAMES", "It was your shop.") }
            : new[] { new LundunLine("HOLLIS", "Even better. I'll be in touch."),
                      new LundunLine("UNCLE FEMI", "James. Do you know what you have just done?"),
                      new LundunLine("JAMES", "Kept your money in your pocket."),
                      new LundunLine("UNCLE FEMI", "No. You have given him you.") }));
        run.Add(new LundunTalk("",
            new LundunLine("HOLLIS", "Oh, and James. I hear Kingsley's boys were rude to you. Have this, on me."),
            new LundunLine("HOLLIS", "A good carbine. People who work for me shouldn't get hurt."),
            new LundunLine("JAMES", "I don't work for you."),
            new LundunLine("HOLLIS", "Course not. It's a gift.")));
        run.Add(new LundunNow(r =>
        {
            LundunWeapons w = LundunWeapons.Instance;
            if (w != null) { w.Give(LundunWeapons.Kind.Carbine, 90); w.Equip((int)LundunWeapons.Kind.Carbine, false); }
            LundunMissions.Toast("CARBINE\nAccurate at range, full auto");
        }));
        // Phase 5: what Hollis has on his phone
        run.Add(new LundunNow(r => footage = LundunHollisPhone.Pick()));
        run.Add(new LundunTalkPick(() =>
        {
            if (footage == LundunHollisPhone.Footage.Basketful)
                return new[] { new LundunLine("HOLLIS", "One more thing. Have a look at this."),
                               new LundunLine("HOLLIS", "Basketful. The camera over the tills. That's a lovely clear face, James.") };
            if (footage == LundunHollisPhone.Footage.Station)
                return new[] { new LundunLine("HOLLIS", "One more thing. You shot the camera at Basketful. Clever lad."),
                               new LundunLine("HOLLIS", "But Brixtun Station has one by the door. And you walked right past it.") };
            if (LundunEvidence.Get(M02EasyMoney.Evidence) == LundunEvidence.State.Destroyed)
                return new[] { new LundunLine("HOLLIS", "One more thing. You shot the camera at Basketful, and you got past the one at the station. I'm impressed."),
                               new LundunLine("HOLLIS", "So I'll make do with this. Canary Quay's got more cameras than people, and most of them answer to me. Smile, James.") };
            return new[] { new LundunLine("HOLLIS", "One more thing. I've got the tape from Basketful somewhere safe. But why show you old footage?"),
                           new LundunLine("HOLLIS", "Canary Quay's got more cameras than people, and most of them answer to me. Smile, James.") };
        }));
        run.Add(new LundunHollisPhone(() => footage, () => HollisSpot));
        run.Add(new LundunTalk("",
            new LundunLine("HOLLIS", "Copies in a safe place. Anything happens to me, they go to the police. So nothing happens to me."),
            new LundunLine("JAMES", "What do you want?"),
            new LundunLine("HOLLIS", "Not tonight. I'll ring."),
            new LundunLine("HOLLIS", "Oh. Kingsley's lads followed you here, by the way. Not my business, I'm afraid. Enjoy the carbine.")));
        run.Add(new LundunDo(r => LundunCrew.Leave("HOLLIS")));
        // Kingsley's boys roll up: the carbine's first outing
        rollUp = new LundunRollUp(TowerPlaza + new Vector3(-18f, 0f, -12f), 2, 4);
        run.Add(rollUp).Checkpoint(LundunCheckpoint.Minor);
        run.Add(new LundunTalk("", new LundunLine("UNCLE FEMI", "James! Behind the car! Behind the car!")));
        run.Add(new LundunFight(() => rollUp.Men, "Fight off Kingsley's boys", true));
        run.Add(new LundunTalk("",
            new LundunLine("UNCLE FEMI", "Is it over? James, is it over?"),
            new LundunLine("JAMES", "It's over. Get in the car.")));
        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunDriveTalk(LundunStoryPlaces.LagosDoor, 4f, "Take Uncle Femi back to LAGOS FOODS",
            new LundunLine("UNCLE FEMI", "He has your face, James. On his telephone."),
            new LundunLine("JAMES", "I know."),
            new LundunLine("UNCLE FEMI", "Not a word to Marcus."),
            new LundunLine("JAMES", "Not a word.")));
        run.Add(new LundunLeave("UNCLE FEMI", () => { float y; return LundunStoryPlaces.Lagos(LundunShopLayout.Inside(LundunStoryPlaces.LagosWidth), out y); }));
        // the end of the chapter: who owns who now, and a look at what's coming
        run.Add(new LundunCard("HOLLIS OWNS JAMES NOW", () => (LundunChoice.Chose("choice:m05", true)
            ? "You paid him a thousand pounds. He kept the footage anyway."
            : "You promised him a favour. He'll choose the night.") + "\n\nCHAPTER 2  ·  RUNNER  ·  COMPLETE"));
        run.Add(new LundunChapterPreview("CHAPTER 3  ·  PLAYER"));
    }

    public override int Medal(LundunMissionRun run) { return run.Elapsed < 660f ? 3 : run.Elapsed < 900f ? 2 : 1; }
}

// ======================================================================================================================
/// 06 Passenger Problems. James picks Marcus up after a night on the door at Knightsbury Circus; on the way home the
/// Rye Lane Riders come after them in two cars. James drives, Marcus shoots out of the window: lose them, or let him take
/// them out. Home, and Marcus hands over body armour and ammo (v50: the SMG came in 04).
public class M06PassengerProblems : LundunMission
{
    static readonly Vector3 Circus = new Vector3(560f, 0f, 945f);

    public override string Id { get { return "m06_passenger_problems"; } }
    public override string Title { get { return "Passenger Problems"; } }
    public override string Teaser { get { return "Marcus is off the door in Knightsbury and needs a lift. The Riders have been asking about you."; } }
    public override string Giver { get { return "MARCUS"; } }
    public override long Reward { get { return 2500; } }
    public override string MedalNote { get { return "under 6 minutes"; } }
    public override bool Available(LundunStoryData d) { return LundunMissionBook.Done(d, "m05_uncles_rules") && !LundunMissionBook.Done(d, Id); }
    public override Vector3 StartAt { get { return LundunMissionRun.Ground(Circus); } }
    public override Vector3 GiverFacing { get { return Vector3.back; } }

    public override void Build(LundunMissionRun run)
    {
        Vector3 start = LundunMissionRun.Ground(Circus);
        run.Add(new LundunJoin("MARCUS"));
        run.Add(new LundunDo(r => LundunCrew.Arm("MARCUS", true)));
        run.Add(new LundunTalk("",
            new LundunLine("MARCUS", "Long night on the door. Drive me home, yeah?"),
            new LundunLine("MARCUS", "And after Gilmore I'm not taking chances. I brought this."),
            new LundunLine("JAMES", "Marcus. Is that a gun?"),
            new LundunLine("MARCUS", "It's insurance. Drive.")));
        run.Add(new LundunWhen("Get in a car with Marcus", r => LundunMissionRun.Driving()));
        run.Add(new LundunWhen("Drive Marcus home", r =>
        {
            Transform j = LundunMissionRun.James();
            return j != null && LundunMissionRun.Driving() && LundunMissionRun.FlatDistance(j.position, start) > 180f;
        }).Mark(LundunStoryPlaces.Home, 6f, true));
        run.Add(new LundunTalk("",
            new LundunLine("MARCUS", "Two cars on us. Rye Lane Riders."),
            new LundunLine("JAMES", "What do they want with us?"),
            new LundunLine("MARCUS", "Kingsley's lent them out. Your name's ringing now, James. Just drive, I'll do the rest.")));
        run.Add(new LundunChase(2, "Lose the Rye Lane Riders, or let Marcus take them out")).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunTalk("", new LundunLine("MARCUS", "That's them done. Breathe, bro. Breathe.")));
        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunGoTo(LundunStoryPlaces.Home, 6f, "Take Marcus home"));
        run.Add(new LundunTalk("",
            new LundunLine("MARCUS", "Gilmore, Hollis, now the Riders. This is getting out of hand."),
            new LundunLine("JAMES", "I know. I'm handling it."),
            new LundunLine("MARCUS", "Here. A vest from the door, and a bag of rounds. If they come again, I want you ready.")));
        run.Add(new LundunNow(r =>
        {
            PlayerHealth ph = PlayerHealth.Instance;
            if (ph != null) ph.GiveArmour(100f);
            LundunWeapons w = LundunWeapons.Instance;
            if (w != null)
                foreach (LundunWeapons.Kind k in new[] { LundunWeapons.Kind.Pistol, LundunWeapons.Kind.Smg, LundunWeapons.Kind.Shotgun, LundunWeapons.Kind.Carbine })
                    if (w.Owns(k)) w.Give(k, w.Of(k).magazine * 2);
            LundunMissions.Toast("BODY ARMOUR + AMMO\nThe blue bar soaks up hits");
        }));
        run.Add(new LundunLeave("MARCUS", () => LundunStoryPlaces.Home));
    }

    public override int Medal(LundunMissionRun run) { return run.Elapsed < 360f ? 3 : run.Elapsed < 540f ? 2 : 1; }
}
