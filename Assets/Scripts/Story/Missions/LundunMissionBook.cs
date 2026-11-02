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
/// S2: Kemi waits for James at his door, comes with him (in the car too), keeps back in the fight and goes in at the end.
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

    /// Kemi waits beside the start marker, facing out from the door.
    public override Vector3 GiverFacing
    {
        get
        {
            LundunPlaces places = LundunPlaces.Instance;
            return places != null && places.Home != null ? Quaternion.Euler(0f, places.Home.doorYaw, 0f) * Vector3.forward : Vector3.forward;
        }
    }

    public override void Build(LundunMissionRun run)
    {
        run.Zone(Courtyard, 45f);                                    // nobody else's corner here while it's on

        run.Add(new LundunJoin("KEMI"));                             // S2: she's waiting at the door, and comes along
        run.Add(new LundunTalk("",
            new LundunLine("KEMI", "James! Finally. Uncle needs a parcel brought back from Gilmore."),
            new LundunLine("JAMES", "Now? It's dark out, Kem."),
            new LundunLine("KEMI", "Which is why I'm coming with you. Get the car."),
            new LundunLine("JAMES", "Fine. Ten minutes.")));

        run.Add(new LundunGoTo(Courtyard, 12f, "Take Kemi to the courtyard on the Gilmore Estate"));

        run.Add(new LundunTalk("",
            new LundunLine("KEMI", "This is it. He said he'd be by the benches."),
            new LundunLine("JAMES", "...That's not who I was expecting."),
            new LundunLine("KEMI", "James. James, they've seen us."))).Checkpoint(LundunCheckpoint.Major);

        run.Add(new LundunFight(Courtyard + new Vector3(4f, 0f, 3f), 2, false, "Deal with the two men"));

        run.Add(new LundunCollect(Courtyard + new Vector3(3f, 0f, 1.5f), "parcel", "Pick up the parcel"));

        run.Add(new LundunTalk("",
            new LundunLine("KEMI", "You good? Let's go before anyone else turns up."),
            new LundunLine("JAMES", "Got it. Car. Now."))).Checkpoint(LundunCheckpoint.Minor);

        run.Add(new LundunEscape("Lose the police"));
        run.Add(new LundunGoTo(Home, 6f, "Take Kemi and the parcel home"));
        run.Add(new LundunTalk("",
            new LundunLine("KEMI", "Uncle owes you one. I'll tell him you were brave."),
            new LundunLine("JAMES", "Tell him I was quick.")));
        run.Add(new LundunLeave("KEMI", () => Home));                // in through the front door
    }

    public override int Medal(LundunMissionRun run)
    {
        if (run.Elapsed < 240f && !run.wanted) return 3;
        if (run.Elapsed < 360f) return 2;
        return 1;
    }
}
