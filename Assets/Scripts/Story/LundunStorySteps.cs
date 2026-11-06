using System;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

/// S3: a step that sets something up and is done at once (people placed, the shop opened, a car parked, a gun given).
/// Starting again from a checkpoint replays every set-up step before it, so the world is as it should be by then.
public abstract class LundunSetup : LundunStep
{
    public abstract void Apply(LundunMissionRun run);
    public override bool Tick(LundunMissionRun run) { Apply(run); return true; }
}

/// S3: any set-up, written in place.
public class LundunDo : LundunSetup
{
    readonly Action<LundunMissionRun> act;
    public LundunDo(Action<LundunMissionRun> what) { act = what; }
    public override void Apply(LundunMissionRun run) { if (act != null) act(run); }
}

/// S3: get into a particular car (Tayo's, the swap car). Fails if it's wrecked or gone first.
public class LundunEnterCar : LundunStep
{
    readonly Func<GameObject> car;
    readonly string lost;
    public override Transform Follow { get { GameObject c = car(); return c != null ? c.transform : null; } }
    public LundunEnterCar(Func<GameObject> whichCar, string objectiveText, string ifWrecked)
    {
        car = whichCar; objective = objectiveText; lost = ifWrecked;
    }

    public override void Begin(LundunMissionRun run)
    {
        GameObject c = car();
        hasMarker = c != null;
        if (c != null) { marker = c.transform.position; markerRadius = 2.2f; route = LundunMissionRun.FlatDistance(c.transform.position, Here()) > 40f; }
    }

    public override bool Tick(LundunMissionRun run)
    {
        GameObject c = car();
        VehicleHealth vh = c != null ? c.GetComponent<VehicleHealth>() : null;
        if (c == null || (vh != null && vh.IsWrecked)) { run.Fail(lost); return false; }
        VehicleSystem vs = Object.FindAnyObjectByType<VehicleSystem>();
        return vs != null && vs.CurrentCar != null && vs.CurrentCar.gameObject == c;
    }

    static Vector3 Here() { Transform j = LundunMissionRun.James(); return j != null ? j.position : Vector3.zero; }
}

/// S3: hold up a till in a store (LundunRobbery). The cash is paid with the mission.
public class LundunRob : LundunStep
{
    readonly string store;
    readonly long cash;
    bool done;
    public LundunRob(string storeName, long tillCash, string objectiveText) { store = storeName; cash = tillCash; objective = objectiveText; }

    public override void Begin(LundunMissionRun run)
    {
        done = false;
        LundunRobbery.MissionStore = store; LundunRobbery.MissionCash = cash;
        LundunRobbery.Robbed += OnRobbed;
        Vector3 tills;
        if (LundunStoryPlaces.Tills(store, out tills)) { hasMarker = true; marker = tills; markerRadius = 2.5f; route = false; }
    }

    void OnRobbed(string s, long c) { if (s == store) done = true; }
    public override bool Tick(LundunMissionRun run) { return done; }
    public override void End(LundunMissionRun run) { LundunRobbery.Robbed -= OnRobbed; LundunRobbery.MissionStore = null; LundunRobbery.MissionCash = 0; }
}

/// S3: the story's places, found in the city (with fallbacks if a builder hasn't been re-run yet).
public static class LundunStoryPlaces
{
    /// James's front door.
    public static Vector3 Home
    {
        get
        {
            LundunPlaces p = LundunPlaces.Instance;
            return p != null && p.Home != null ? p.Home.door : new Vector3(989.4f, 0f, 103.8f);
        }
    }

    public static float HomeYaw { get { LundunPlaces p = LundunPlaces.Instance; return p != null && p.Home != null ? p.Home.doorYaw : 270f; } }

    /// FADEZ BARBERS on Rye Lane: outside its door, and the way it faces.
    public static Vector3 Fadez(out float yaw)
    {
        LundunPlaces p = LundunPlaces.Instance;
        LundunPlaces.Place f = p != null ? p.Get("FADEZ_BARBERS") : null;
        yaw = f != null ? f.doorYaw : 270f;
        return f != null ? f.door : new Vector3(803.5f, 0f, 339.3f);
    }

    /// Somewhere in LAGOS FOODS (LundunShopLayout's frame), and which way to face there.
    public static Vector3 Lagos(double[] spot, out float yaw)
    {
        LundunStoryShop s = LundunStoryShop.Instance;
        if (s != null && s.Ready) { yaw = s.SpotYaw(spot); return s.SpotPos(spot); }
        // not built yet (Lundun → 13): the shop's front on Rye Lane, facing east
        Vector3 outward = Vector3.right, along = new Vector3(0f, 0f, 1f);
        Vector3 front = new Vector3(776.8f, 0.15f, 396f) - outward * 1.2f - along * (float)LundunShopLayout.DoorX(7.34);
        yaw = 270f + (float)spot[2];
        return front + along * (float)spot[0] - outward * (float)spot[1];
    }

    public static Vector3 LagosDoor { get { LundunStoryShop s = LundunStoryShop.Instance; return s != null && s.Ready ? s.DoorOutside : new Vector3(776.8f, 0f, 396f); } }
    public static double LagosWidth { get { LundunStoryShop s = LundunStoryShop.Instance; return s != null && s.Ready ? s.Width : 7.34; } }

    /// A walk-in store's entrance: a spot in its car park in front, and one just inside.
    public static bool Store(string name, out Vector3 outside, out Vector3 inside)
    {
        foreach (LundunShopInterior s in LundunShopInterior.All)
        {
            if (s == null || s.StoreName != name || s.DoorCentres == null || s.DoorCentres.Length == 0) continue;
            Vector3 door = s.DoorCentres[0];
            Vector3 mid = (s.Min + s.Max) * 0.5f;
            Vector3 inward = mid - door; inward.y = 0f; inward = inward.sqrMagnitude > 0.01f ? inward.normalized : Vector3.forward;
            outside = LundunMissionRun.Ground(door - inward * 14f);
            inside = LundunMissionRun.Ground(door + inward * 5f);
            return true;
        }
        outside = new Vector3(1110f, 0f, -560f); inside = new Vector3(1110f, 0f, -600f);
        return false;
    }

    /// Where the tills are in a store (by the first cashier's spot).
    public static bool Tills(string name, out Vector3 at)
    {
        at = Vector3.zero;
        foreach (LundunShopInterior s in LundunShopInterior.All)
        {
            if (s == null || s.StoreName != name) continue;
            foreach (LundunMarketCrowd c in Object.FindObjectsByType<LundunMarketCrowd>(FindObjectsSortMode.None))
            {
                Vector3[] t = c.TraderSpots;
                if (t == null) continue;
                foreach (Vector3 p in t) if (s.Inside(p)) { at = p; return true; }
            }
        }
        return false;
    }

    /// Brixtun Station's street entrance, and where you step off at its platform.
    public static Vector3 BrixtunStation { get { LundunTrain t = LundunTrain.Instance; return t != null && t.HasBrixtun ? t.BrixtunEntrance : new Vector3(451f, 0f, -73f); } }
    public static Vector3 BrixtunPlatform { get { LundunTrain t = LundunTrain.Instance; return t != null && t.HasBrixtun ? t.BrixtunPlatform : BrixtunStation; } }

    /// The lock-up under the railway arches, by the bridge over Brixtun Road near the station.
    public static Vector3 LockUp(out Quaternion rot)
    {
        Vector3 p;
        if (Kerb(new Vector3(428f, 0f, -56f), out p, out rot)) return p;
        rot = Quaternion.identity;
        return LundunMissionRun.Ground(new Vector3(428f, 0f, -56f));
    }

    /// A parking spot by the kerb of the nearest road (on the left, facing along it), clear of junctions.
    public static bool Kerb(Vector3 near, out Vector3 pos, out Quaternion rot)
    {
        pos = near; rot = Quaternion.identity;
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.Graph == null) return false;
        int edge; double t, dist;
        nav.Graph.NearestOnRoad(new NavPoint(near.x, near.z), out edge, out t, out dist);
        if (edge < 0) return false;
        int a, b; nav.Graph.EdgeEnds(edge, out a, out b);
        NavPoint na = nav.Graph.NodePosition(a), nb = nav.Graph.NodePosition(b);
        Vector3 A = new Vector3((float)na.x, 0f, (float)na.z), B = new Vector3((float)nb.x, 0f, (float)nb.z);
        Vector3 dir = B - A; float len = dir.magnitude;
        if (len < 1f) return false;
        dir /= len;
        float s = Mathf.Clamp((float)t * len, Mathf.Min(16f, len / 2f), Mathf.Max(len - 16f, len / 2f));    // not in a junction
        double width; string cls;
        if (!nav.Graph.EdgeInfo(a, b, out width, out cls)) width = 8;
        Vector3 left = new Vector3(-dir.z, 0f, dir.x);
        pos = LundunMissionRun.Ground(A + dir * s + left * Mathf.Max(1.2f, (float)width / 2f - 1.3f));
        rot = Quaternion.LookRotation(dir);
        return true;
    }

    /// A car for a mission, parked at the kerb nearest 'near' (null if the city has no car models).
    public static GameObject ParkCar(Vector3 near, int look)
    {
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return null;
        Vector3 pos; Quaternion rot;
        if (!Kerb(near, out pos, out rot)) { pos = LundunMissionRun.Ground(near); rot = Quaternion.identity; }
        return pop.NewCar(LundunMissions.Instance != null ? LundunMissions.Instance.transform : null, pos, rot, look);
    }

    /// Is the car usable (there and not wrecked)?
    public static bool Fine(GameObject car)
    {
        if (car == null) return false;
        VehicleHealth vh = car.GetComponent<VehicleHealth>();
        return vh == null || !vh.IsWrecked;
    }
}

/// S3: steps can show a marker (and the GPS route) wherever they like.
public static class LundunStepMarks
{
    public static LundunStep Mark(this LundunStep s, Vector3 at, float radius, bool showRoute)
    {
        s.hasMarker = true; s.marker = at; s.markerRadius = radius; s.route = showRoute;
        return s;
    }
}
