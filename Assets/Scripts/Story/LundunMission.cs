using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// S1: a story mission: who gives it, where it starts, what it pays, and its steps (built fresh for every attempt, so
/// a retry starts clean from its checkpoint). Missions are listed in LundunMissionBook.
public abstract class LundunMission
{
    public abstract string Id { get; }
    public abstract string Title { get; }
    /// One line on the mission card.
    public abstract string Teaser { get; }
    /// The character who gives it (their letter on the minimap): "KEMI".
    public abstract string Giver { get; }
    public abstract long Reward { get; }
    /// Where its marker stands (on the walkable ground).
    public abstract Vector3 StartAt { get; }
    /// S2: the way the giver faces while waiting by the start marker (beside it).
    public virtual Vector3 GiverFacing { get { return Vector3.forward; } }
    /// S3: the mission's over (passed or abandoned): let go of anything it kept between attempts (its cars).
    public virtual void Tidy() { }
    /// Counts as one of the 12 story missions (the S1 test mission doesn't).
    public virtual bool Story { get { return true; } }
    public virtual bool Available(LundunStoryData d) { return true; }
    /// The steps, in order.
    public abstract void Build(LundunMissionRun run);
    /// 0 none, 1 bronze, 2 silver, 3 gold, judged when it's passed (default: everyone gets bronze).
    public virtual int Medal(LundunMissionRun run) { return 1; }
    /// What the medals ask for (shown on the card and the pass screen).
    public virtual string MedalNote { get { return ""; } }
}

public enum LundunDifficulty { Easy, Normal, Hard }

/// S1: one attempt at a mission: its steps, where we are, what it spawned (cleared on retry, fail or quit) and how it's
/// going (time, whether the police got involved). LundunMissions runs it.
public class LundunMissionRun
{
    public readonly LundunMission mission;
    public readonly LundunDifficulty difficulty;
    public readonly List<LundunStep> steps = new List<LundunStep>();
    public int index = -1;
    public float startedAt, timeBefore;                    // time from earlier attempts isn't counted (fresh start each retry)
    public bool wanted;                                   // the police got involved at some point
    public string failed;                                 // why it failed (null while it's going)
    public readonly List<GameObject> spawned = new List<GameObject>();
    public readonly List<string> crew = new List<string>();     // S2: who's with James because of this mission (LundunJoin)
    public readonly List<KeyValuePair<Vector3, float>> zones = new List<KeyValuePair<Vector3, float>>();

    public LundunMissionRun(LundunMission m, LundunDifficulty d) { mission = m; difficulty = d; }

    public LundunStep Current { get { return index >= 0 && index < steps.Count ? steps[index] : null; } }
    public float Elapsed { get { return Time.time - startedAt; } }

    public LundunStep Add(LundunStep s) { steps.Add(s); return s; }
    public void Zone(Vector3 at, float radius) { zones.Add(new KeyValuePair<Vector3, float>(at, radius)); }
    public void Fail(string why) { if (failed == null) failed = why; }
    public T Spawn<T>(T thing) where T : UnityEngine.Object
    {
        GameObject g = thing as GameObject;
        if (g == null) { Component c = thing as Component; if (c != null) g = c.gameObject; }
        if (g != null) spawned.Add(g);
        return thing;
    }

    // ------------------------------------------------------------------ difficulty
    /// How hard enemies hit.
    public float EnemyDamage { get { return difficulty == LundunDifficulty.Easy ? 0.6f : difficulty == LundunDifficulty.Hard ? 1.25f : 1f; } }
    /// Extra enemies in a fight.
    public int ExtraEnemies { get { return difficulty == LundunDifficulty.Hard ? 1 : 0; } }
    public float RewardScale { get { return difficulty == LundunDifficulty.Easy ? 1f : difficulty == LundunDifficulty.Hard ? 1.5f : 1.25f; } }
    /// Is a checkpoint of this weight used at this difficulty? (Easy: every one; Normal: the major ones; Hard: none
    /// after the start, the start always counting.)
    public bool UsesCheckpoint(LundunCheckpoint c)
    {
        if (c == LundunCheckpoint.None) return false;
        if (difficulty == LundunDifficulty.Easy) return true;
        if (difficulty == LundunDifficulty.Normal) return c == LundunCheckpoint.Major;
        return false;
    }

    // ------------------------------------------------------------------ handy for steps
    public static Transform James()
    {
        VehicleSystem vs = UnityEngine.Object.FindAnyObjectByType<VehicleSystem>();
        return vs != null ? vs.PlayerTarget : null;
    }

    public static bool Driving()
    {
        VehicleSystem vs = UnityEngine.Object.FindAnyObjectByType<VehicleSystem>();
        return vs != null && vs.IsDriving;
    }

    public static float FlatDistance(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

    /// The walkable ground at (x, z) (or a little above the road if there's none).
    public static Vector3 Ground(Vector3 p)
    {
        if (NavMesh.SamplePosition(new Vector3(p.x, p.y + 2f, p.z), out NavMeshHit hit, 6f, NavMesh.AllAreas)) return hit.position;
        if (Physics.Raycast(new Vector3(p.x, 60f, p.z), Vector3.down, out RaycastHit r, 120f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore)) return r.point;
        return new Vector3(p.x, 0f, p.z);
    }
}

public enum LundunCheckpoint { None, Minor, Major }

/// S1: one step of a mission. Tick returns true when it's done; call run.Fail(why) to fail the mission.
public abstract class LundunStep
{
    /// The line at the top of the screen ("Drive to the Gilmore Estate").
    public string objective = "";
    /// A retry starts from here (if the difficulty uses checkpoints of this weight).
    public LundunCheckpoint checkpoint = LundunCheckpoint.None;
    /// The yellow marker (and the GPS route to it, if route).
    public bool hasMarker;
    public Vector3 marker;
    public float markerRadius = 4f;
    public bool route;
    /// S8: flying there: a straight purple line to the marker, not a route along the roads.
    public bool air;

    /// Guidance: something moving to head for (a car to get into, someone to catch): the route and the on-screen
    /// pointer follow it (null: the marker, if there is one).
    public virtual Transform Follow { get { return null; } }

    public virtual void Begin(LundunMissionRun run) { }
    public abstract bool Tick(LundunMissionRun run);
    public virtual void End(LundunMissionRun run) { }

    public LundunStep Checkpoint(LundunCheckpoint c) { checkpoint = c; return this; }
}

/// A character's line in a conversation.
public struct LundunLine
{
    public string who, text;
    public LundunLine(string who, string text) { this.who = who; this.text = text; }
}

// ====================================================================== the steps missions are built from
/// Get to a place (on foot or driving). needCar: only counts in a vehicle ("Get a car" until then).
public class LundunGoTo : LundunStep
{
    readonly bool needCar;
    readonly string onFoot;
    public LundunGoTo(Vector3 at, float radius, string objectiveText, bool showRoute = true, bool mustDrive = false)
    {
        objective = objectiveText; hasMarker = true; marker = LundunMissionRun.Ground(at); markerRadius = radius; route = showRoute;
        needCar = mustDrive; onFoot = objectiveText;
    }
    public override bool Tick(LundunMissionRun run)
    {
        Transform j = LundunMissionRun.James();
        if (j == null) return false;
        bool driving = LundunMissionRun.Driving();
        objective = needCar && !driving ? "Get in a car" : onFoot;
        return (!needCar || driving) && LundunMissionRun.FlatDistance(j.position, marker) < markerRadius;
    }
}

/// A conversation: each line on screen for a time that suits its length. Doesn't stop James moving.
public class LundunTalk : LundunStep
{
    readonly LundunLine[] lines;
    int at = -1;
    float next;
    public LundunTalk(string objectiveText, params LundunLine[] said) { lines = said; objective = objectiveText; }
    public override void Begin(LundunMissionRun run) { at = -1; next = 0f; LundunMissions.TakeSkip(); }
    public override bool Tick(LundunMissionRun run)
    {
        if (Time.time < next && !LundunMissions.TakeSkip()) return false;      // guidance: tap the subtitle for the next line
        at++;
        if (at >= lines.Length) { LundunMissions.Say(null, null, 0f); return true; }
        // guidance: long enough to read at an easy pace (at least 4 seconds)
        float secs = LundunMissions.ReadTime(lines[at].text);
        LundunMissions.Say(lines[at].who, lines[at].text, secs);
        next = Time.time + secs + LundunMissions.TakeDelay();
        return false;
    }
    public override void End(LundunMissionRun run) { LundunMissions.Say(null, null, 0f); }
}

/// Wait a moment (a pause in the action).
public class LundunWait : LundunStep
{
    readonly float seconds; float until;
    public LundunWait(float secs, string objectiveText = "") { seconds = secs; objective = objectiveText; }
    public override void Begin(LundunMissionRun run) { until = Time.time + seconds; }
    public override bool Tick(LundunMissionRun run) { return Time.time >= until; }
}

/// Lose the police (done straight away if there are none).
public class LundunEscape : LundunStep
{
    public LundunEscape(string objectiveText = "Lose the police") { objective = objectiveText; }
    public override bool Tick(LundunMissionRun run) { return WantedSystem.Instance == null || WantedSystem.Instance.Stars == 0; }
}

/// A fight: people waiting at a spot (standing about, turned towards James) who go for him once he's close. Done when
/// every one of them is dead or has had enough (beaten down to under a third of their health, they give up and run).
public class LundunFight : LundunStep
{
    Vector3 at; readonly int count; readonly bool guns; readonly string look; readonly float engageWithin;
    /// S4: how hard they hit (× the difficulty's): knives out = more.
    public float harder = 1f;
    readonly List<Health> enemies = new List<Health>();
    readonly List<bool> beaten = new List<bool>();
    bool engaged;
    readonly Func<List<Health>> already;
    /// S5: a fight with people already there (a standoff that went wrong): they go for James at once.
    public LundunFight(Func<List<Health>> group, string objectiveText)
    {
        already = group; objective = objectiveText; look = "Street"; engageWithin = 9999f; hasMarker = false;
    }
    public LundunFight(Vector3 where, int people, bool withGuns, string objectiveText, string lookName = "Street", float startWithin = 18f)
    {
        at = where; count = people; guns = withGuns; objective = objectiveText; look = lookName; engageWithin = startWithin;
        hasMarker = false;
    }
    public override void Begin(LundunMissionRun run)
    {
        enemies.Clear(); beaten.Clear(); engaged = false;
        if (already != null)
        {
            List<Health> g = already();
            if (g != null) foreach (Health h in g) if (h != null && !h.IsDead) { enemies.Add(h); beaten.Add(false); }
            if (enemies.Count > 0) { Vector3 c = Vector3.zero; foreach (Health h in enemies) c += h.transform.position; at = c / enemies.Count; }
            LundunMissions.Mark(enemies, true);
            return;
        }
        LundunPopulation pop = LundunPopulation.Instance;
        int n = count + run.ExtraEnemies;
        Vector3 centre = LundunMissionRun.Ground(at);
        for (int i = 0; i < n && pop != null; i++)
        {
            float a = i * Mathf.PI * 2f / n;
            Vector3 p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 2.2f;
            if (!NavMesh.SamplePosition(p + Vector3.up, out NavMeshHit hit, 3f, NavMesh.AllAreas)) continue;
            GameObject g = pop.NewPerson(null, look);
            if (g == null) continue;
            NPCBrain b = g.GetComponent<NPCBrain>(); if (b != null) b.enabled = false;
            Vector3 face = centre - hit.position; face.y = 0f;
            g.transform.SetPositionAndRotation(hit.position, Quaternion.LookRotation(face.sqrMagnitude > 0.01f ? face : Vector3.forward));
            g.SetActive(true);
            run.Spawn(g);
            Health h = g.GetComponent<Health>();
            if (h != null) { enemies.Add(h); beaten.Add(false); }
        }
        LundunMissions.Mark(enemies, true);
    }
    public override bool Tick(LundunMissionRun run)
    {
        Transform j = LundunMissionRun.James();
        if (!engaged && j != null && LundunMissionRun.FlatDistance(j.position, at) < engageWithin)
        {
            engaged = true;
            foreach (Health h in enemies)
                if (h != null && !h.IsDead) { LundunAngryDriver ad = h.gameObject.AddComponent<LundunAngryDriver>(); ad.Foe = true; ad.Begin(guns, 9999f, 150f, run.EnemyDamage * harder); }
        }
        bool left = false;
        for (int i = 0; i < enemies.Count; i++)
        {
            Health h = enemies[i];
            if (h == null || h.IsDead || beaten[i]) continue;
            if (engaged && h.Current < h.Max * 0.33f)
            {
                // had enough: off they run (and they're no longer a target)
                beaten[i] = true;
                LundunAngryDriver ad = h.GetComponent<LundunAngryDriver>();
                if (ad != null) UnityEngine.Object.Destroy(ad);
                NPCBrain b = h.GetComponent<NPCBrain>();
                if (b != null) { b.enabled = true; if (j != null) b.Panic(j.position); }
                LundunMissions.Mark(h, false);
                continue;
            }
            // a fight is only over when they're dealt with; one wandered off calm (lost James): back at it
            if (engaged && h.GetComponent<LundunAngryDriver>() == null && !LundunStagger.Busy(h.gameObject))
                { LundunAngryDriver ad = h.gameObject.AddComponent<LundunAngryDriver>(); ad.Foe = true; ad.Begin(guns, 9999f, 150f, run.EnemyDamage * harder); }
            left = true;
        }
        if (!left) LundunMissions.Mark(enemies, false);
        return engaged && !left;
    }
}

/// Pick something up: a glowing package on the ground; walk over it.
public class LundunCollect : LundunStep
{
    readonly string what;
    GameObject item;
    public LundunCollect(Vector3 where, string thing, string objectiveText)
    {
        what = thing; objective = objectiveText; hasMarker = true; marker = LundunMissionRun.Ground(where); markerRadius = 1.4f; route = false;
    }
    public override void Begin(LundunMissionRun run)
    {
        item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = "Mission_" + what;
        Collider c = item.GetComponent<Collider>(); if (c != null) UnityEngine.Object.Destroy(c);
        item.transform.position = marker + Vector3.up * 0.2f;
        item.transform.localScale = new Vector3(0.45f, 0.3f, 0.35f);
        Renderer r = item.GetComponent<Renderer>();
        if (r != null) r.sharedMaterial = LundunMissions.ParcelMaterial;
        run.Spawn(item);
    }
    public override bool Tick(LundunMissionRun run)
    {
        if (item != null) item.transform.Rotate(new Vector3(0f, 90f * Time.deltaTime, 0f));
        Transform j = LundunMissionRun.James();
        if (j == null || LundunMissionRun.Driving()) return false;
        if (LundunMissionRun.FlatDistance(j.position, marker) > markerRadius) return false;
        if (item != null) UnityEngine.Object.Destroy(item);
        LundunMissions.Toast("Got the " + what);
        return true;
    }
}

/// Anything else: a check run each frame (true = done), with its own objective line.
public class LundunWhen : LundunStep
{
    readonly Func<LundunMissionRun, bool> check;
    public LundunWhen(string objectiveText, Func<LundunMissionRun, bool> done) { objective = objectiveText; check = done; }
    public override bool Tick(LundunMissionRun run) { return check(run); }
}
