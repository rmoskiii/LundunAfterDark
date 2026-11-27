using System;
using System.Collections.Generic;
using UnityEngine;

/// G8.2a: the LIVE POLICE FEED. Scanner-style dispatches that break into whichever station is tuned (the station fades to
/// 20% underneath and keeps playing; the player is never switched station). It reads only the live wanted level, whether
/// an armed police car and a police helicopter are really out, and James's district. It creates no records and never
/// touches the G8.0 log, police behaviour or missions.
///
/// Priority (strict): opening > escalation > update > stand-down > city. One dispatch at a time; ONE pending slot where
/// the best event wins; the live state is rechecked just before playback; anything older than 8 s is dropped. The 20 s
/// gap applies to updates and city dispatches only. A higher-priority pursuit event interrupts a lower-priority dispatch
/// with a squelch, its clips are released, a 2 s breath, then it plays.
///
/// Losing RadioCanPlay mid-dispatch (out of the car, RADIO OFF, WASTED / BUSTED, CCTV, a mission card, a locked vehicle,
/// an iOS interruption): the scanner stops at once, the current and pending dispatches are discarded, their clips are
/// released, nothing is replayed, and the station is never left ducked. A phone call is the one exception: a dispatch
/// already speaking finishes under the phone's dip; no new one starts during a call. The pause is AudioListener.pause.
public class LundunPoliceFeed : MonoBehaviour
{
    [SerializeField] private float duckLevel = 0.2f;            // −14 dB; changed only with approval after the iPhone test
    [SerializeField] private float scannerLevel = 0.9f;
    [SerializeField] private string state;

    static readonly DispatchComposer[] composers = { new DispatchComposer(1), new DispatchComposer(2) };   // per mode, in memory
    public static void ResetStatics() { composers[0] = new DispatchComposer(1); composers[1] = new DispatchComposer(2); }

    LundunRadio radio;
    FeedBrain brain;
    AudioSource scanner;
    bool bound;
    float duck = 1f, duckTarget = 1f, duckRate = 1f;
    float nextPoll; bool armed, air;
    Transform handset; bool warnedUnknown; int lastCall = -2;
    VehicleSystem vehicles;

    // the dispatch on air
    DispatchPlan cur; List<AudioClip> curClips = new List<AudioClip>(); int stage = -1; float stageUntil;

    public bool Live { get { return bound && WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0; } }

    public void Init(LundunRadio r)
    {
        radio = r;
        GameObject g = new GameObject("Scanner");                          // its own object, so the filter only touches dispatches
        g.transform.SetParent(transform, false);
        scanner = g.AddComponent<AudioSource>();
        scanner.playOnAwake = false; scanner.spatialBlend = 0f; scanner.priority = 0; scanner.volume = scannerLevel; scanner.bypassReverbZones = true;
        AudioHighPassFilter hp = g.AddComponent<AudioHighPassFilter>(); hp.cutoffFrequency = 350f;   // the radio band: code-made, retunable
        AudioLowPassFilter lp = g.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 3400f;
        AudioDistortionFilter ds = g.AddComponent<AudioDistortionFilter>(); ds.distortionLevel = 0.22f;
        brain = new FeedBrain(new Output(this), (ulong)DateTime.UtcNow.Ticks);
    }

    public void Bind()
    {
        bound = true; handset = null; warnedUnknown = false; lastCall = -2; vehicles = FindAnyObjectByType<VehicleSystem>();
        brain.Reset(Time.realtimeSinceStartup);
        LundunRadio.Log("feed bound (" + Composer.Describe() + ")");
    }

    public void Unbind() { Stop("unbound", false); bound = false; handset = null; vehicles = null; }

    DispatchComposer Composer { get { return composers[radio != null ? Mathf.Clamp(radio.Mode, 0, 1) : 0]; } }

    // ------------------------------------------------------------------ from the radio
    public void LoseEligibility(string why) { if (brain != null) brain.LostEligibility(Time.realtimeSinceStartup, why); }
    public void AudibleAgain() { if (brain != null && bound) brain.AudibleAgain(Time.realtimeSinceStartup); }
    public void Unpaused(float pausedFor) { if (brain != null) brain.Unpaused(Time.realtimeSinceStartup, pausedFor); }

    // ------------------------------------------------------------------ every frame, after every Update (the phone has moved by then)
    void LateUpdate()
    {
        float now = Time.realtimeSinceStartup;
        if (bound && radio != null)
        {
            if (now >= nextPoll) { nextPoll = now + 1f; Poll(); }
            FeedWorld w = new FeedWorld();
            w.stars = WantedSystem.Instance != null ? WantedSystem.Instance.Stars : 0;
            w.armed = armed && w.stars > 0; w.air = air && w.stars > 0;
            w.canPlay = radio.CanPlay; w.paused = AudioListener.pause;
            w.call = CallState();
            if (w.call != lastCall)                                         // development log: the exact frame the phone state changes
            {
                if (lastCall != -2) LundunRadio.Log("feed: phone state " + CallName(lastCall) + " → " + CallName(w.call) + " (frame " + Time.frameCount + ")");
                lastCall = w.call;
            }
            brain.Tick(now, AudioListener.pause ? 0f : Time.unscaledDeltaTime, w);
            if (!AudioListener.pause) TickPlayback(now);
        }
        duck = Mathf.MoveTowards(duck, duckTarget, (AudioListener.pause ? 0f : Time.unscaledDeltaTime) * duckRate);
        if (radio != null) radio.DuckLevel = duck;
        state = brain != null ? brain.Describe() : "";
    }

    /// Armed response only while an armed police car is out (not leaving); air support only while a helicopter is up
    /// (listed, not leaving, not burning or wrecked). Read once a second; nothing in the police code changes.
    void Poll()
    {
        armed = false;
        foreach (PoliceAI p in PoliceAI.Active) if (p != null && p.isActiveAndEnabled && p.Armed && !p.IsLeaving) { armed = true; break; }
        air = false;
        foreach (LundunHeli h in LundunHeli.Active)
        {
            if (h == null || h.Leaving) continue;
            VehicleHealth vh = h.GetComponent<VehicleHealth>();
            if (vh != null && (vh.IsBurning || vh.IsWrecked)) continue;
            air = true; break;
        }
    }

    /// 0 idle, 1 call active, -1 unknown (Story with no phone or handset: no new dispatches). Free Roam has no phone.
    /// Read from LundunPhone.Instance's "PhoneUI/Handset" GameObject.activeSelf, which the phone itself sets every frame.
    int CallState()
    {
        if (!LundunGame.IsStory) return 0;
        LundunPhone p = LundunPhone.Instance;
        if (p == null) { WarnUnknown("no LundunPhone"); return -1; }
        if (handset == null) handset = p.transform.Find("PhoneUI/Handset");
        if (handset == null) { WarnUnknown("no PhoneUI/Handset"); return -1; }
        return handset.gameObject.activeSelf ? 1 : 0;
    }

    static string CallName(int c) { return c == 1 ? "active" : c == 0 ? "idle" : "unknown"; }

    void WarnUnknown(string what)
    {
        if (warnedUnknown) return;
        warnedUnknown = true;
        if (Debug.isDebugBuild) Debug.LogWarning("[Radio] feed: phone state unknown (" + what + "): no new dispatches until it's known");
    }

    // ------------------------------------------------------------------ James's district (a private copy of the map's lookup)
    string DistrictNow()
    {
        if (vehicles == null) vehicles = FindAnyObjectByType<VehicleSystem>();
        if (vehicles == null || vehicles.PlayerTarget == null) return "";
        return District(vehicles.PlayerTarget.position);
    }

    static string District(Vector3 p)
    {
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.World == null) return "";
        LundunWorldData w = nav.World;
        return Inside(w.districts, p, true) ?? (w.expansion != null ? Inside(w.expansion.districts, p, true) : null)
               ?? Inside(w.districts, p, false) ?? (w.expansion != null ? Inside(w.expansion.districts, p, false) : null) ?? "";
    }

    static string Inside(LwPoly[] polys, Vector3 p, bool skipPen)
    {
        if (polys == null) return null;
        foreach (LwPoly d in polys)
        {
            if (d == null || d.polygon == null || (skipPen && d.id == "D_CANARY_PEN")) continue;
            bool c = false; LwPoint[] poly = d.polygon;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                if ((poly[i].z > p.z) != (poly[j].z > p.z) && p.x < (poly[j].x - poly[i].x) * (p.z - poly[i].z) / (poly[j].z - poly[i].z) + poly[i].x) c = !c;
            if (c) return d.name;
        }
        return null;
    }

    static List<string> WorldDistricts()
    {
        List<string> l = new List<string>();
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.World == null) return l;
        Add(l, nav.World.districts);
        if (nav.World.expansion != null) Add(l, nav.World.expansion.districts);
        return l;
    }

    static void Add(List<string> l, LwPoly[] polys)
    {
        if (polys == null) return;
        foreach (LwPoly d in polys) if (d != null && !string.IsNullOrEmpty(d.name) && d.id != "D_CANARY_PEN" && !l.Contains(d.name)) l.Add(d.name);
    }

    // ------------------------------------------------------------------ playback: squelch, the parts, squelch
    bool Begin(DispatchEvent e)
    {
        RadioCatalog cat = radio.Catalog;
        string district = DistrictNow();
        bool mission = LundunMissions.Running;
        DispatchComposer.Situation sit = new DispatchComposer.Situation { district = district, avoidDistrict = mission ? district : null, armed = armed, air = air };
        DispatchPlan plan = cat != null ? Composer.Compose(e, sit, cat) : null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (plan == null && cat != null && cat.setting == RadioSetting.On && !Composer.HasRecordings(cat))
            plan = Composer.ComposePlaceholder(e, sit, WorldDistricts());
#endif
        if (plan == null) return false;
        cur = plan;
        curClips.Clear();
        if (!plan.placeholder)
            foreach (string path in plan.paths)
            {
                AudioClip c = Resources.Load<AudioClip>(path);
                if (c != null && c.loadState != AudioDataLoadState.Loaded && c.loadState != AudioDataLoadState.Loading) c.LoadAudioData();
                curClips.Add(c);
            }
        duckTarget = duckLevel; duckRate = (1f - duckLevel) / 0.3f;      // down over about 0.3 s
        scanner.Stop(); scanner.clip = Squelch; scanner.loop = false; scanner.Play();
        stage = 0; stageUntil = 0f;
        LundunRadio.Log("feed: " + e.kind + (e.escalation != null ? " (" + e.escalation + ")" : "") + (e.audibleAgain ? " [audible again]" : "") + ": \"" + plan.text + "\"" + (plan.placeholder ? " (PLACEHOLDER)" : ""));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (plan.placeholder && radio != null) radio.ShowCaption("SCANNER (PLACEHOLDER): " + plan.text, 2.5f + plan.text.Length * 0.05f);
#endif
        return true;
    }

    void TickPlayback(float now)
    {
        if (cur == null || stage < 0) return;
        if (now < stageUntil) return;
        if (scanner.loop) { scanner.Stop(); scanner.loop = false; }         // a placeholder crackle has run its time
        if (scanner.isPlaying) return;
        int parts = cur.placeholder ? 1 : curClips.Count;
        if (stage < parts)
        {
            if (cur.placeholder)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                scanner.clip = DispatchPlaceholder.Crackle; scanner.loop = true; scanner.Play();
                stageUntil = now + 1.2f + cur.text.Length * 0.05f;          // about as long as the line would take
#endif
                stage++;
                return;
            }
            AudioClip c = curClips[stage];
            if (c == null || c.loadState == AudioDataLoadState.Failed) { Stop("a part failed to load", false); brain.Aborted(now); return; }
            if (c.loadState != AudioDataLoadState.Loaded)
            {
                if (now - brain.PlayingDue > FeedBrain.Expiry) { Stop("parts not loaded in time", false); brain.Aborted(now); }
                return;                                                     // the squelch has played; waiting for the data
            }
            scanner.clip = c; scanner.loop = false; scanner.Play();
            stage++;
            return;
        }
        if (stage == parts)
        {
            scanner.loop = false; scanner.Stop();
            scanner.clip = Squelch; scanner.Play();
            stage++;
            return;
        }
        // done
        Release();
        duckTarget = 1f; duckRate = (1f - duckLevel) / 0.6f;               // back up over about 0.6 s
        stage = -1; cur = null;
        brain.Finished(now);
    }

    /// Stops the scanner at once (optionally with a short squelch), releases the clips and never leaves the station ducked.
    void Stop(string reason, bool squelch)
    {
        bool was = cur != null;
        scanner.Stop(); scanner.loop = false; scanner.clip = null;
        if (squelch) { scanner.clip = Squelch; scanner.Play(); }
        Release();
        cur = null; stage = -1;
        duckTarget = 1f; duckRate = (1f - duckLevel) / 0.3f;               // restored over about 0.3 s
        if (was) LundunRadio.Log("feed: dispatch cut (" + reason + "), clips released, station restored");
    }

    void Release()
    {
        foreach (AudioClip c in curClips) if (c != null && c.loadState == AudioDataLoadState.Loaded) c.UnloadAudioData();
        curClips.Clear();
    }

    static AudioClip squelch;
    /// The scanner's squelch: a short code-made noise burst (part of the scanner sound, not a placeholder).
    static AudioClip Squelch
    {
        get
        {
            if (squelch != null) return squelch;
            const int rate = 22050; int n = (int)(rate * 0.2f); float[] d = new float[n];
            System.Random r = new System.Random(3);
            for (int i = 0; i < n; i++) { float t = i / (float)n; d[i] = 0.3f * (float)(r.NextDouble() * 2 - 1) * (t < 0.1f ? t * 10f : 1f - t); }
            squelch = AudioClip.Create("scanner_squelch", n, 1, rate, false); squelch.SetData(d, 0);
            return squelch;
        }
    }

    sealed class Output : IFeedOutput
    {
        readonly LundunPoliceFeed f;
        public Output(LundunPoliceFeed f) { this.f = f; }
        public bool Start(DispatchEvent e) { return f.Begin(e); }
        public void Cut(string reason, bool squelch) { f.Stop(reason, squelch); }
        public void Log(string s) { LundunRadio.Log("feed: " + s); }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [ContextMenu("Test: city dispatch now")] void TestCity() { brain.ForceCity(Time.realtimeSinceStartup); }
#endif
}

// ====================================================================== plain logic (no scene objects)

public enum DispatchKind { Opening = 1, Escalation = 2, Update = 3, StandDown = 4, City = 5 }

public sealed class DispatchEvent
{
    public DispatchKind kind; public string escalation; public float due, notBefore; public bool audibleAgain; public int serial;
    public override string ToString() { return kind + (escalation != null ? "(" + escalation + ")" : "") + "#" + serial; }
}

public struct FeedWorld { public int stars; public bool armed, air, canPlay, paused; public int call; }

public interface IFeedOutput { bool Start(DispatchEvent e); void Cut(string reason, bool squelch); void Log(string s); }

/// The feed's rules, with no scene objects, so they can be simulated. Times are real seconds.
public sealed class FeedBrain
{
    public const float Gap = 20f, Breath = 2f, Expiry = 8f, LeadIn = 0.5f, Beat = 2.5f, PauseStale = 10f;
    public const float OpenMin = 5f, OpenMax = 10f, UpdateMin = 45f, UpdateMax = 90f, CityMin = 180f, CityMax = 360f;

    readonly IFeedOutput output;
    readonly RadioRandom rnd;
    DispatchEvent playing, pending;
    float playingSince, lastEnd = -999f;
    bool inPursuit, openingDone, armedSaid, airSaid;
    float openingAt = -1f, nextUpdate = -1f, audibleAgainAt = -1f;
    float cityListen, cityNeed;
    int serial;

    public FeedBrain(IFeedOutput output, ulong seed) { this.output = output; rnd = new RadioRandom(seed); cityNeed = R(CityMin, CityMax); }

    float R(float a, float b) { return a + (float)rnd.Value() * (b - a); }
    static int Rank(DispatchKind k) { return (int)k; }
    static bool Pursuit(DispatchKind k) { return k != DispatchKind.City; }
    static bool GapApplies(DispatchKind k) { return k == DispatchKind.Update || k == DispatchKind.City; }

    public DispatchEvent Playing { get { return playing; } }
    public DispatchEvent Pending { get { return pending; } }
    public float PlayingDue { get { return playing != null ? playing.due : 0f; } }
    public bool InPursuit { get { return inPursuit; } }

    public void Reset(float now)
    {
        if (playing != null) output.Cut("reset", false);
        playing = null; pending = null; lastEnd = now - Gap; inPursuit = false; openingAt = nextUpdate = audibleAgainAt = -1f;
        cityListen = 0f; cityNeed = R(CityMin, CityMax);
    }

    public void Tick(float now, float listenDt, FeedWorld w)
    {
        bool canStart = w.canPlay && !w.paused && w.call == 0;

        // a call ringing during a dispatch's lead-in (squelch, before any words) cancels it silently
        if (playing != null && w.call != 0 && now - playingSince < LeadIn)
        {
            output.Cut("call rang during the lead-in", false);
            output.Log("cancelled " + playing + " before any words (phone)");
            playing = null; pending = null; lastEnd = now;
        }
        if (w.call != 0 && pending != null) { output.Log("discarded pending " + pending + " (phone)"); pending = null; }

        // the pursuit, from the live wanted level only
        if (w.stars > 0 && !inPursuit)
        {
            inPursuit = true; openingDone = false; armedSaid = airSaid = false;
            openingAt = now + R(OpenMin, OpenMax); nextUpdate = -1f;
        }
        else if (w.stars == 0 && inPursuit)
        {
            inPursuit = false; openingAt = -1f; nextUpdate = -1f; audibleAgainAt = -1f;
            Due(now, DispatchKind.StandDown, null, false, canStart);
        }
        if (inPursuit)
        {
            if (openingAt > 0f && now >= openingAt) { openingAt = -1f; Due(now, DispatchKind.Opening, null, false, canStart); }
            if (openingAt < 0f)
            {
                if (w.armed && !armedSaid) { armedSaid = true; Due(now, DispatchKind.Escalation, "armed", false, canStart); }
                if (w.air && !airSaid) { airSaid = true; Due(now, DispatchKind.Escalation, "air", false, canStart); }
                if (nextUpdate < 0f) nextUpdate = now + R(UpdateMin, UpdateMax);
                else if (now >= nextUpdate) { nextUpdate = now + R(UpdateMin, UpdateMax); Due(now, DispatchKind.Update, null, false, canStart); }
            }
        }

        // city dispatches: one every 3–6 minutes of listening (half as often during a pursuit)
        if (canStart) cityListen += listenDt * (inPursuit ? 0.5f : 1f);
        if (cityListen >= cityNeed) { cityListen = 0f; cityNeed = R(CityMin, CityMax); Due(now, DispatchKind.City, null, false, canStart); }

        // audible again mid-pursuit: one current line after a short beat, never a backlog
        if (audibleAgainAt > 0f && now >= audibleAgainAt)
        {
            audibleAgainAt = -1f;
            if (inPursuit) Due(now, openingDone ? DispatchKind.Update : DispatchKind.Opening, null, true, canStart);
            if (inPursuit && openingAt > 0f) openingAt = -1f;
        }

        TryPending(now, w, canStart);
    }

    void Due(float now, DispatchKind kind, string esc, bool again, bool canStart)
    {
        DispatchEvent e = new DispatchEvent { kind = kind, escalation = esc, due = now, notBefore = now, audibleAgain = again, serial = ++serial };
        if (!canStart) { output.Log("discarded " + e + " (radio inaudible, paused or a call): not queued"); return; }
        if (playing != null)
        {
            if (Pursuit(kind) && Rank(kind) < Rank(playing.kind))
            {
                output.Cut("interrupted by " + e, true);
                output.Log(playing + " cut with a squelch for " + e);
                playing = null; lastEnd = now;
                e.notBefore = now + Breath;
                Offer(e);
                return;
            }
            if (kind == DispatchKind.City) { output.Log("discarded " + e + " (another dispatch on air; city lines are never late)"); return; }
            Offer(e);
            return;
        }
        if (GapApplies(kind) && now - lastEnd < Gap)
        {
            if (kind == DispatchKind.City) { output.Log("discarded " + e + " (20 s gap)"); return; }
            e.notBefore = lastEnd + Gap; Offer(e); return;
        }
        if (now - lastEnd < Breath) { e.notBefore = lastEnd + Breath; Offer(e); return; }
        StartNow(now, e);
    }

    /// The single pending slot: a higher-ranked (or newer same-ranked) event replaces what's there. Never a queue.
    void Offer(DispatchEvent e)
    {
        if (pending == null || Rank(e.kind) <= Rank(pending.kind))
        {
            if (pending != null) output.Log("pending " + pending + " replaced by " + e);
            pending = e;
        }
        else output.Log("discarded " + e + " (pending " + pending + " outranks it)");
    }

    bool Valid(DispatchEvent e, FeedWorld w)
    {
        switch (e.kind)
        {
            case DispatchKind.Opening: case DispatchKind.Update: return w.stars > 0;
            case DispatchKind.Escalation: return w.stars > 0 && (e.escalation == "armed" ? w.armed : w.air);
            case DispatchKind.StandDown: return w.stars == 0;
            default: return true;
        }
    }

    void TryPending(float now, FeedWorld w, bool canStart)
    {
        if (pending == null || playing != null) return;
        if (now - pending.due > Expiry) { output.Log("dropped " + pending + " (older than 8 s)"); pending = null; return; }
        if (!canStart) { output.Log("dropped " + pending + " (radio inaudible, paused or a call)"); pending = null; return; }
        if (now < pending.notBefore) return;
        if (GapApplies(pending.kind) && now - lastEnd < Gap) return;
        if (now - lastEnd < Breath) return;
        if (!Valid(pending, w)) { output.Log("dropped " + pending + " (no longer true when rechecked)"); pending = null; return; }
        DispatchEvent e = pending; pending = null;
        StartNow(now, e);
    }

    void StartNow(float now, DispatchEvent e)
    {
        if (!output.Start(e)) { output.Log("couldn't compose " + e + " (a required part is missing): not played"); return; }
        playing = e; playingSince = now;
        if (e.kind == DispatchKind.Opening) openingDone = true;
    }

    /// The dispatch ended naturally.
    public void Finished(float now) { playing = null; lastEnd = now; }

    /// Playback gave up (a part failed or didn't load in time); the output has already stopped and released it.
    public void Aborted(float now) { playing = null; lastEnd = now; }

    /// RadioCanPlay became false (anything except a phone call or the pause).
    public void LostEligibility(float now, string why)
    {
        if (playing != null) { output.Cut(why, false); output.Log("discarded " + playing + " (" + why + "), not replayed"); playing = null; lastEnd = now; }
        if (pending != null) { output.Log("discarded pending " + pending + " (" + why + ")"); pending = null; }
        audibleAgainAt = -1f;
    }

    public void AudibleAgain(float now) { if (inPursuit) audibleAgainAt = now + Beat; }

    public void Unpaused(float now, float pausedFor)
    {
        if (pausedFor <= PauseStale) return;
        if (playing != null) { output.Cut("stale after a " + pausedFor.ToString("0") + " s pause", false); output.Log("discarded " + playing + " (stale after the pause)"); playing = null; lastEnd = now; }
        if (pending != null) { output.Log("discarded pending " + pending + " (stale after the pause)"); pending = null; }
    }

    public void ForceCity(float now) { cityListen = cityNeed; }

    public string Describe()
    {
        return (playing != null ? "on air " + playing : "idle") + (pending != null ? " · pending " + pending : "") + (inPursuit ? " · pursuit" : "");
    }
}

/// One composed dispatch: recorded parts (resource paths) or, in the Editor and development builds, a placeholder line.
public sealed class DispatchPlan { public string[] paths; public string text; public bool placeholder; public string combo, body; }

/// Stitches dispatches from parts with no-repeat memory: never one of the last 10 combinations, never one of the last 4
/// bodies (relaxed only when there aren't enough bodies to choose from). Kept per mode, in memory.
public sealed class DispatchComposer
{
    public struct Situation { public string district, avoidDistrict; public bool armed, air; }
    readonly LinkedList<string> combos = new LinkedList<string>(), bodies = new LinkedList<string>();
    readonly RadioRandom rnd;

    public DispatchComposer(ulong seed) { rnd = new RadioRandom(seed ^ (ulong)DateTime.UtcNow.Ticks); }

    public string Describe() { return "no-repeat memory " + combos.Count + " combinations, " + bodies.Count + " bodies"; }

    public bool HasRecordings(RadioCatalog cat) { return cat.In("Feed/Openers").Count > 0 || cat.In("Feed/City").Count > 0 || cat.In("Feed/Pursuit").Count > 0; }

    public bool RecentlyUsed(string combo, string body) { return combos.Contains(combo) || bodies.Contains(body); }

    void Remember(string combo, string body)
    {
        combos.AddLast(combo); while (combos.Count > 10) combos.RemoveFirst();
        bodies.AddLast(body); while (bodies.Count > 4) bodies.RemoveFirst();
    }

    /// Recorded parts only. Required: an opener, a body and a place for city, opening and update lines; escalation and
    /// stand-down lines are complete on their own. The closer is optional. Null if anything required is missing.
    public DispatchPlan Compose(DispatchEvent e, Situation s, RadioCatalog cat)
    {
        List<string> openers = Paths(cat.In("Feed/Openers")), closers = Paths(cat.In("Feed/Closers"));
        List<string> bodyList; string place = null; List<string> places = null;
        switch (e.kind)
        {
            case DispatchKind.City:
                bodyList = Paths(cat.In("Feed/City"));
                places = new List<string>();
                foreach (string p in Paths(cat.In("Feed/Places")))
                    if (s.avoidDistrict == null || RadioCatalog.Norm(Name(p)) != RadioCatalog.Norm(s.avoidDistrict)) places.Add(p);   // never James's district during a mission
                break;
            case DispatchKind.Opening: bodyList = Paths(cat.In("Feed/Pursuit", "OPEN_")); place = cat.Named("Feed/Districts", s.district); break;
            case DispatchKind.Update: bodyList = Paths(cat.In("Feed/Pursuit", "UPDATE_")); place = cat.Named("Feed/Districts", s.district); break;
            case DispatchKind.Escalation: bodyList = Paths(cat.In("Feed/Escalation", e.escalation == "armed" ? "ARMED_" : "AIR_")); break;
            default: bodyList = Paths(cat.In("Feed/StandDown")); break;
        }
        bool needsPlace = e.kind == DispatchKind.City || e.kind == DispatchKind.Opening || e.kind == DispatchKind.Update;
        bool needsOpener = e.kind != DispatchKind.StandDown;
        if (bodyList.Count == 0 || (needsOpener && openers.Count == 0)) return null;
        if (needsPlace && place == null && (places == null || places.Count == 0)) return null;
        for (int attempt = 0; attempt < 16; attempt++)
        {
            List<string> parts = new List<string>();
            if (needsOpener) parts.Add(openers[rnd.Range(openers.Count)]);
            string body = bodyList[rnd.Range(bodyList.Count)];
            parts.Add(body);
            if (needsPlace) parts.Add(place ?? places[rnd.Range(places.Count)]);
            if (e.audibleAgain && e.kind == DispatchKind.Update)            // "the situation now": the escalation that's true now
            {
                if (s.armed) { List<string> a = Paths(cat.In("Feed/Escalation", "ARMED_")); if (a.Count > 0) parts.Add(a[0]); }
                if (s.air) { List<string> a = Paths(cat.In("Feed/Escalation", "AIR_")); if (a.Count > 0) parts.Add(a[0]); }
            }
            if (closers.Count > 0 && rnd.Value() < 0.4) parts.Add(closers[rnd.Range(closers.Count)]);
            string combo = string.Join("|", parts.ToArray());
            bool relaxBody = bodyList.Count <= 4;
            if ((combos.Contains(combo) || (!relaxBody && bodies.Contains(body))) && attempt < 15) continue;
            Remember(combo, body);
            return new DispatchPlan { paths = parts.ToArray(), text = string.Join(" · ", parts.ConvertAll(Name).ToArray()), combo = combo, body = body };
        }
        return null;
    }

    static List<string> Paths(List<RadioManifest.Entry> l) { List<string> r = new List<string>(); foreach (RadioManifest.Entry e in l) r.Add(e.path); return r; }
    static string Name(string path) { return path.Substring(path.LastIndexOf('/') + 1); }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    static readonly string[] Openers = { "Control to all units", "All units", "Any unit in the area" };
    static readonly string[] CityBodies = { "reports of disorder", "a vehicle stopped on the bridge", "an alarm activation", "a fight outside a takeaway",
        "a report of a break-in", "a vehicle blocking the bus lane", "a noise complaint", "a shoplifter detained by security", "a broken-down van causing tailbacks",
        "concern for a person on the platform", "a burst water main" };
    static readonly string[] OpenBodies = { "vehicle failing to stop", "pursuit in progress", "suspect vehicle making off" };
    static readonly string[] UpdateBodies = { "suspect vehicle still mobile", "pursuit continuing", "units still in pursuit", "suspect still making off" };
    static readonly string[] StandDowns = { "All units, stand down, resume patrol", "Stand down, all units", "All units, resume normal patrol" };
    static readonly string[] Closers = { "over", "received" };

    /// Development only (RADIO ON, no recordings at all): the same structure as text, shown as a caption with a crackle.
    public DispatchPlan ComposePlaceholder(DispatchEvent e, Situation s, List<string> districts)
    {
        for (int attempt = 0; attempt < 16; attempt++)
        {
            string opener = Openers[rnd.Range(Openers.Length)], body, place = null, text;
            switch (e.kind)
            {
                case DispatchKind.City:
                    body = CityBodies[rnd.Range(CityBodies.Length)];
                    List<string> ok = new List<string>();
                    foreach (string d in districts) if (s.avoidDistrict == null || d != s.avoidDistrict) ok.Add(d);
                    if (ok.Count == 0) return null;
                    place = ok[rnd.Range(ok.Count)];
                    text = opener + ", " + body + ", " + place; break;
                case DispatchKind.Opening:
                    body = OpenBodies[rnd.Range(OpenBodies.Length)]; place = string.IsNullOrEmpty(s.district) ? "Lundun" : s.district;
                    text = opener + ", " + body + ", " + place + " area"; break;
                case DispatchKind.Update:
                    body = UpdateBodies[rnd.Range(UpdateBodies.Length)]; place = string.IsNullOrEmpty(s.district) ? "Lundun" : s.district;
                    text = opener + ", " + body + ", " + place;
                    if (e.audibleAgain && s.armed) text += ". Armed response en route";
                    if (e.audibleAgain && s.air) text += ". Air support is up";
                    break;
                case DispatchKind.Escalation:
                    body = e.escalation == "armed" ? "Armed response en route" : "Air support is up";
                    text = opener + ", " + body; break;
                default:
                    body = StandDowns[rnd.Range(StandDowns.Length)]; text = body; break;
            }
            if (rnd.Value() < 0.4) text += ", " + Closers[rnd.Range(Closers.Length)];
            if ((combos.Contains(text) || bodies.Contains(body)) && attempt < 15 && e.kind == DispatchKind.City) continue;
            if (combos.Contains(text) && attempt < 15) continue;
            Remember(text, body);
            return new DispatchPlan { paths = new string[0], text = text + ".", placeholder = true, combo = text, body = body };
        }
        return null;
    }
#endif
}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
/// Development only: the crackle under a placeholder dispatch caption. Compiled out of release builds.
public static class DispatchPlaceholder
{
    static AudioClip crackle;
    public static AudioClip Crackle
    {
        get
        {
            if (crackle != null) return crackle;
            const int rate = 22050; int n = rate; float[] d = new float[n];
            System.Random r = new System.Random(11);
            for (int i = 0; i < n; i++) d[i] = 0.05f * (float)(r.NextDouble() * 2 - 1) * (0.6f + 0.4f * Mathf.Sin(i * 0.002f));
            crackle = AudioClip.Create("scanner_placeholder_crackle", n, 1, rate, false); crackle.SetData(d, 0);
            return crackle;
        }
    }
}
#endif
