using System.Collections.Generic;
using UnityEngine;

/// S1: every mission in the game, in story order. S3 adds 01 One Last Favour, 02 Easy Money and 03 No Way Home.
public static class LundunMissionBook
{
    static List<LundunMission> all;

    public static List<LundunMission> All
    {
        get
        {
            if (all == null)
            {
                all = new List<LundunMission>
                {
                    new TestGilmoreRun(),               // S1: the test of the mission system (not one of the 12)
                };
            }
            return all;
        }
    }
}

/// S1: a test of the mission system: drive to the Gilmore Estate, deal with two men, get the parcel back, take it home.
/// Uses every part: a conversation, a checkpoint, the GPS route, a fight, a pick-up, a medal and the pay. It doesn't
/// count as one of the 12, and can be played again. Replaced by mission 01 in S3.
public class TestGilmoreRun : LundunMission
{
    // the safehouse in Peckam (where James lives) and the courtyard on the Gilmore Estate (the world's "The Pickup")
    static readonly Vector3 Safehouse = new Vector3(905f, 0f, 205f);
    static readonly Vector3 Courtyard = new Vector3(330f, 0f, 380f);

    public override string Id { get { return "test_gilmore_run"; } }
    public override string Title { get { return "TEST: The Gilmore Run"; } }
    public override string Teaser { get { return "Kemi needs a parcel collected from the Gilmore Estate. A test of the mission system: drive, fight, collect, come home."; } }
    public override string Giver { get { return "KEMI"; } }
    public override long Reward { get { return 250; } }
    public override bool Story { get { return false; } }
    public override string MedalNote { get { return "under 4 minutes, no police"; } }

    /// By James's front door (the home safehouse if he has one, otherwise the Peckam safehouse).
    public override Vector3 StartAt
    {
        get
        {
            LundunPlaces places = LundunPlaces.Instance;
            if (places != null && places.Home != null)
            {
                Vector3 door = places.Home.door;
                Vector3 f = Quaternion.Euler(0f, places.Home.doorYaw, 0f) * Vector3.forward;
                return door + f * 3f;
            }
            return Safehouse;
        }
    }

    Vector3 Home
    {
        get
        {
            LundunPlaces places = LundunPlaces.Instance;
            return places != null && places.Home != null ? places.Home.door : Safehouse;
        }
    }

    public override void Build(LundunMissionRun run)
    {
        run.Zone(Courtyard, 45f);                                    // nobody else's corner here while it's on

        run.Add(new LundunTalk("",
            new LundunLine("KEMI", "James. You up? Uncle needs a parcel brought back from Gilmore."),
            new LundunLine("JAMES", "Now? It's dark out, Kem."),
            new LundunLine("KEMI", "Ten minutes, cuz. The courtyard on the estate. Someone's holding it for us."),
            new LundunLine("JAMES", "Fine. Ten minutes.")));

        run.Add(new LundunGoTo(Courtyard, 12f, "Go to the courtyard on the Gilmore Estate"));

        run.Add(new LundunTalk("",
            new LundunLine("JAMES", "Kemi said someone'd be waiting with a parcel."),
            new LundunLine("JAMES", "...That's not who I was expecting."))).Checkpoint(LundunCheckpoint.Major);

        run.Add(new LundunFight(Courtyard + new Vector3(4f, 0f, 3f), 2, false, "Deal with the two men"));

        run.Add(new LundunCollect(Courtyard + new Vector3(3f, 0f, 1.5f), "parcel", "Pick up the parcel"));

        run.Add(new LundunTalk("",
            new LundunLine("JAMES", "Got it. Bit of trouble, Kem."),
            new LundunLine("KEMI", "You alright? Just bring it home."))).Checkpoint(LundunCheckpoint.Minor);

        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunGoTo(Home, 6f, "Take the parcel home"));
    }

    public override int Medal(LundunMissionRun run)
    {
        if (run.Elapsed < 240f && !run.wanted) return 3;
        if (run.Elapsed < 360f) return 2;
        return 1;
    }
}
