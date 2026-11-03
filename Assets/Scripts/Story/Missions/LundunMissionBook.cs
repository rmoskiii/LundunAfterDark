using System.Collections.Generic;
using UnityEngine;

/// Every story mission, in order. S3: 01 One Last Favour, 02 Easy Money, 03 No Way Home (they replace S1's test
/// mission). Each opens once the one before is passed.
public static class LundunMissionBook
{
    static List<LundunMission> all;

    public static List<LundunMission> All
    {
        get
        {
            if (all == null) all = new List<LundunMission> { new M01OneLastFavour(), new M02EasyMoney(), new M03NoWayHome() };
            return all;
        }
    }

    public static bool Done(LundunStoryData d, string id) { return d != null && d.flags.Contains("done:" + id); }
}

// ======================================================================================================================
/// 01 One Last Favour (the tutorial). Kemi fetches James for a family meeting at LAGOS FOODS: walk there with her (the
/// GPS route), the family in the shop (Uncle Femi, Marcus, Kemi, then Leah drops in), drive to the Gilmore Estate for
/// Uncle's parcel, two Tower Block Kings take it, fight them, bring it back to the shop, go home. No police (unless you
/// make some). Afterwards LAGOS FOODS is open in the day, with Uncle Femi behind the counter.
public class M01OneLastFavour : LundunMission
{
    static readonly Vector3 Courtyard = new Vector3(330f, 0f, 380f);         // the Gilmore Estate's courtyard ("The Pickup")

    public override string Id { get { return "m01_one_last_favour"; } }
    public override string Title { get { return "One Last Favour"; } }
    public override string Teaser { get { return "Uncle Femi wants the family at LAGOS FOODS. Kemi's come to fetch you."; } }
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
        run.Add(new LundunJoin("KEMI"));
        run.Add(new LundunTalk("",
            new LundunLine("KEMI", "James! You're up. Uncle Femi wants everyone at the shop. Family meeting."),
            new LundunLine("JAMES", "A family meeting? Who's died?"),
            new LundunLine("KEMI", "Nobody. Yet. Come on, it's two minutes. Walk with me.")));
        run.Add(new LundunGoTo(door, 3.5f, "Walk to LAGOS FOODS on Rye Lane"));
        run.Add(new LundunGoTo(counter, 1.6f, "Go in and see Uncle Femi", false));
        run.Add(new LundunDo(r => { float fy; Vector3 p = LundunStoryPlaces.Lagos(LundunShopLayout.InAisle(W), out fy); LundunCrew.Stage("KEMI", p, fy); }));
        run.Add(new LundunTalk("",
            new LundunLine("UNCLE FEMI", "Ah! The prince arrives. Ten minutes late, as usual."),
            new LundunLine("MARCUS", "He's always late. Alright, bro."),
            new LundunLine("JAMES", "Marcus. Uncle."),
            new LundunLine("UNCLE FEMI", "Listen. A man on the Gilmore Estate is holding a parcel for me. Spices, from home. You collect it, you bring it here."),
            new LundunLine("JAMES", "Why can't Marcus go?"),
            new LundunLine("MARCUS", "Because I've got work in the morning. A real job."),
            new LundunLine("KEMI", "Ignore him.")));
        // Leah drops in from her shift
        run.Add(new LundunDo(r =>
        {
            float fy; Vector3 inside = LundunStoryPlaces.Lagos(LundunShopLayout.Inside(W), out fy);
            LundunCrew.Stage("LEAH", LundunStoryPlaces.LagosDoor + Quaternion.Euler(0f, fy, 0f) * Vector3.right * 3f, fy);
            LundunCrew.Stage("LEAH", inside, fy);
        }));
        run.Add(new LundunWait(2.5f));
        run.Add(new LundunTalk("",
            new LundunLine("LEAH", "Sorry, sorry, my shift ran over. Did I miss the drama?"),
            new LundunLine("KEMI", "James is running errands for Uncle."),
            new LundunLine("LEAH", "Be careful, yeah? Text me when you're back."),
            new LundunLine("UNCLE FEMI", "Take your car. And James: be polite.")));

        run.Add(new LundunGoTo(Courtyard, 12f, "Drive to the Gilmore Estate for Uncle Femi's parcel"));
        run.Add(new LundunTalk("",
            new LundunLine("JAMES", "Evening. Uncle Femi sent me, for the parcel."),
            new LundunLine("TOWER BLOCK KING", "The parcel? Nah. That's ours now. Kings' estate, Kings' parcel."),
            new LundunLine("JAMES", "It's my uncle's. Hand it over."))).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunFight(Courtyard + new Vector3(4f, 0f, 3f), 2, false, "Get Uncle Femi's parcel back"));
        run.Add(new LundunCollect(Courtyard + new Vector3(3f, 0f, 1.5f), "parcel", "Pick up the parcel"));
        run.Add(new LundunTalk("",
            new LundunLine("KEMI", "(text) All good? Uncle's pacing."),
            new LundunLine("JAMES", "Bit of trouble. Got it though. On my way."))).Checkpoint(LundunCheckpoint.Minor);
        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunGoTo(door, 3.5f, "Take the parcel back to LAGOS FOODS"));
        run.Add(new LundunGoTo(counter, 1.6f, "Give Uncle Femi his parcel", false));
        run.Add(new LundunTalk("",
            new LundunLine("UNCLE FEMI", "Your lip is bleeding. What happened?"),
            new LundunLine("JAMES", "Two lads from the towers. Tower Block Kings, they said."),
            new LundunLine("UNCLE FEMI", "...Kingsley's boys. Don't go back there, James. Promise me."),
            new LundunLine("MARCUS", "Uncle? Who's Kingsley?"),
            new LundunLine("UNCLE FEMI", "Nobody. Go home, all of you. Thank you, James.")));
        run.Add(new LundunGoTo(LundunStoryPlaces.Home, 6f, "Go home and get some sleep"));
    }

    public override int Medal(LundunMissionRun run)
    {
        if (run.Elapsed < 420f && !run.wanted) return 3;
        if (run.Elapsed < 600f) return 2;
        return 1;
    }
}

// ======================================================================================================================
/// 02 Easy Money. Tayo at FADEZ BARBERS has a plan for James's Kestrel payments: a pistol, his car, Basketful in
/// Brixtun. Rob a till (hold the button while the cash bar fills and the stars climb), back to Tayo's car, lose the
/// police, swap cars at the lock-up under the railway arches by Brixtun Station.
public class M02EasyMoney : LundunMission
{
    GameObject tayoCar, swapCar;

    public override string Id { get { return "m02_easy_money"; } }
    public override string Title { get { return "Easy Money"; } }
    public override string Teaser { get { return "Tayo's waiting at FADEZ BARBERS. He says he can fix your money problems."; } }
    public override string Giver { get { return "TAYO"; } }
    public override long Reward { get { return 1200; } }
    public override string MedalNote { get { return "under 8 minutes"; } }
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

        run.Add(new LundunDo(r => { if (!LundunStoryPlaces.Fine(tayoCar)) { LundunMissions.LetGo(tayoCar); tayoCar = LundunStoryPlaces.ParkCar(fadez + Quaternion.Euler(0f, fy, 0f) * Vector3.forward * 6f, 2); } }));
        run.Add(new LundunJoin("TAYO"));
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "Jamesy! Not for a trim. Sit down, it's business."),
            new LundunLine("JAMES", "What business?"),
            new LundunLine("TAYO", "The Kestrel. You're two payments behind. Don't look at me like that, everyone knows."),
            new LundunLine("TAYO", "Basketful, in Brixtun. One till. In and out, nobody gets hurt, your payments are sorted."),
            new LundunLine("JAMES", "You want to rob a supermarket."),
            new LundunLine("TAYO", "I want to fix your life, bro. I've got the car outside. And I've got this.")));
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
            new LundunLine("TAYO", "Someone's got to drive, innit. Gun out, straight to a till. Go.")));
        run.Add(new LundunDo(r =>
        {
            GameObject c = tayoCar;
            if (c != null) LundunCrew.Stage("TAYO", c.transform.position - c.transform.right * 1.8f, c.transform.eulerAngles.y);
        }));
        run.Add(new LundunGoTo(inside, 3f, "Go into Basketful", false)).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunRob("Basketful", 900, "Rob a till: gun out, hold the button"));
        run.Add(new LundunJoin("TAYO"));
        run.Add(new LundunEnterCar(() => tayoCar, "Get back to Tayo's car", "Tayo's car was wrecked"));
        run.Add(new LundunTalk("", new LundunLine("TAYO", "GO GO GO! Floor it!")));
        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunDo(r => { if (!LundunStoryPlaces.Fine(swapCar)) { LundunMissions.LetGo(swapCar); swapCar = LundunStoryPlaces.ParkCar(lockUp, 5); } })).Checkpoint(LundunCheckpoint.Minor);
        run.Add(new LundunGoTo(lockUp, 12f, "Drive to the lock-up under the railway arches"));
        run.Add(new LundunEnterCar(() => swapCar, "Swap cars", "The swap car was wrecked"));
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "See? Easy money. Your half's yours, my half's mine."),
            new LundunLine("JAMES", "My hands are still shaking."),
            new LundunLine("TAYO", "That's the adrenaline, bro. You get used to it.")));
    }

    public override int Medal(LundunMissionRun run)
    {
        if (run.Elapsed < 480f) return 3;
        if (run.Elapsed < 720f) return 2;
        return 1;
    }

    public override void Tidy() { LundunMissions.LetGo(tayoCar); tayoCar = null; LundunMissions.LetGo(swapCar); swapCar = null; }
}

// ======================================================================================================================
/// 03 No Way Home. The swap car's been clocked: 3★, and the roads out of Brixtun are being closed (roadblocks from 3★
/// while it's on). Ditch the car, get to Brixtun Station on foot and catch the train before it goes (board it unseen
/// and the police lose you). Tayo and James fall out on the ride. Walk home from Rye Lane.
public class M03NoWayHome : LundunMission
{
    public override string Id { get { return "m03_no_way_home"; } }
    public override string Title { get { return "No Way Home"; } }
    public override string Teaser { get { return "Tayo's still at the lock-up. Something's wrong."; } }
    public override string Giver { get { return "TAYO"; } }
    public override long Reward { get { return 800; } }
    public override string MedalNote { get { return "under 5 minutes"; } }
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

        run.Add(new LundunJoin("TAYO"));
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "(on the phone) What do you mean they've got the plate? ...Bruv."),
            new LundunLine("TAYO", "James. The swap car's been clocked. The feds are looking for it."),
            new LundunLine("JAMES", "You said it was clean!")));
        run.Add(new LundunDo(r =>
        {
            LundunRoadblocks.MissionFromStars = 3;
            PlayerHealth ph = PlayerHealth.Instance;
            if (WantedSystem.Instance != null && ph != null) WantedSystem.Instance.RaiseTo(3, ph.Body.position, false);
            LundunMissions.Toast("The roads out of Brixtun are being closed");
        }));
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "Roads are shut. We ditch the car and take the train. Brixtun Station, now!")));
        run.Add(new LundunWhen("Ditch the car", r => !LundunMissionRun.Driving()));
        run.Add(new LundunDo(r => { if (LundunTrain.Instance != null) LundunTrain.Instance.HoldAt(true, 240f); }));
        run.Add(new LundunGoTo(station, 6f, "Get to Brixtun Station on foot")).Checkpoint(LundunCheckpoint.Major);
        run.Add(new LundunWhen("Catch the train before it leaves", r => LundunTrain.Instance == null || LundunTrain.Instance.Riding)).Mark(platform, 3f, false);
        run.Add(new LundunTalk("",
            new LundunLine("TAYO", "...Okay. Okay. We're good."),
            new LundunLine("JAMES", "We're good? They've got that car, Tayo. With us all over it."),
            new LundunLine("TAYO", "Gloves, bro. I'm not stupid."),
            new LundunLine("JAMES", "You are stupid. And now so am I."),
            new LundunLine("TAYO", "You took the money, James. Nobody made you."),
            new LundunLine("JAMES", "Get off at Rye Lane. Don't call me for a bit.")));
        run.Add(new LundunWhen("Ride the train to Rye Lane", r => LundunTrain.Instance == null || (!LundunTrain.Instance.Riding && LundunTrain.Instance.AtRyeLane)));
        run.Add(new LundunLeave("TAYO"));
        run.Add(new LundunDo(r => LundunRoadblocks.MissionFromStars = 0));
        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunGoTo(LundunStoryPlaces.Home, 6f, "Walk home"));
    }

    public override int Medal(LundunMissionRun run)
    {
        if (run.Elapsed < 300f) return 3;
        if (run.Elapsed < 480f) return 2;
        return 1;
    }
}
