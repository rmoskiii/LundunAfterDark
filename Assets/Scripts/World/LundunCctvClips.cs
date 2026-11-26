using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// G8.1b: CCTV footage for incidents. Listens to the incident log (LundunIncidents, G8.0) and never keeps an incident
/// list of its own:
///   cutting     when a record closes (Closed), HELD ones included so the CCTV window is never lost: a chase that ended
///               ESCAPED / ESCAPED BY TRAIN / RESPRAYED / WASTED / BUSTED, or a WASTED / BUSTED with no chase, gets the
///               frames the recorder (LundunCctvFeed) still holds from the last 10 seconds before it closed that fall
///               inside it. No camera had him in that window: no footage. Never MERGED or VOID records, never MISSION,
///               CLEARED or ABANDONED endings.
///   crashes     a CRASH record stays open 10 s after its last impact, so its footage is snapshotted 2 s after the FINAL
///               qualifying impact (12 m/s or more; a later one retakes it), copied out of the recorder and held
///               (protected) until the record closes, then attached. Merged into a chase, VOID or the scene ending
///               first: freed. A crash inside a chase takes no snapshot (the chase gets its own clip).
///   attaching   "replay" (the clip) and "still" (a frame two-thirds through, for G8.1a's cards), through
///               LundunIncidents.Attach with a release action: G8.0 frees them on VOID or eviction.
///   the limit   at most 3 kept clips in all (both modes; a mode switch reloads the scene, which frees them all), plus
///               one held crash snapshot. Never freed by the limit: a clip whose incident is pinned, the held snapshot,
///               the clip being watched. Room needed: the oldest unprotected clip goes. All protected: the new clip is
///               not kept, and the Console says so. A pin is never broken.
///   watching    only SETTLED footage (TryGetSettled). A "CCTV FOOTAGE" button for 6 s after an escape or a big crash
///               that settles the moment it closes, outside a mission (it pauses the game and plays it); "LAST FOOTAGE"
///               on the pause menu (LundunSettings). Tap to close.
/// Readout (Editor and development builds): "[Replays] ring … · clips … · held …" each time a clip is kept or freed.
public class LundunCctvClips : MonoBehaviour
{
    public static LundunCctvClips Instance { get; private set; }

    public const int MaxKept = 3;
    const float SnapAfter = 2f;                    // s after the final qualifying impact
    const float CrashRecording = 4f;               // s the recorder keeps going after a big crash (for the aftermath)
    const float PromptFor = 6f;                    // s the CCTV FOOTAGE button stays up
    const float FreshClose = 0.5f;                 // s: settled "the moment it closed"

    [Header("Read only")]
    [SerializeField] private string memory;
    [SerializeField] private int keptClips;
    [SerializeField] private bool holdingSnapshot;

    readonly List<LundunCctvClip> kept = new List<LundunCctvClip>();
    LundunCctvClip held; string heldId;
    float snapAt = -1f;
    int myMode;

    Canvas hud; GameObject prompt; float promptUntil; string promptId;
    Canvas closeCanvas; bool stopRequested, watching;
    static Sprite playIcon;

    // ------------------------------------------------------------------ attaching itself (both modes)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterFirstLoad()
    {
        SceneManager.sceneLoaded += (s, m) => Ensure();
        Ensure();
    }

    static void Ensure()
    {
        if (FindAnyObjectByType<VehicleSystem>() == null) return;
        if (FindAnyObjectByType<LundunCctvClips>() != null) return;
        new GameObject("LundunCctvClips").AddComponent<LundunCctvClips>();
    }

    void Awake() { Instance = this; myMode = LundunGame.IsStory ? 1 : 0; }

    void OnEnable()
    {
        LundunIncidents.Closed += OnClosed;
        LundunIncidents.Settled += OnSettled;
        CarCrash.Crashed += OnCrashed;
    }

    void OnDisable()
    {
        LundunIncidents.Closed -= OnClosed;
        LundunIncidents.Settled -= OnSettled;
        CarCrash.Crashed -= OnCrashed;
    }

    void OnDestroy()
    {
        // the pictures belong to this scene: free everything (attachments left in the log then read as expired)
        if (held != null) held.Free();
        held = null; heldId = null;
        foreach (LundunCctvClip c in kept) c.Free();
        kept.Clear();
        if (watching) { Time.timeScale = 1f; AudioListener.pause = false; }
        if (Instance == this) Instance = null;
    }

    // ------------------------------------------------------------------ crashes: the snapshot after the final impact
    void OnCrashed(CarController car, float impact, Vector3 at)
    {
        try
        {
            if (impact < LundunIncidentLog.BigCrash) return;
            LundunCctvFeed.KeepRecording(CrashRecording);                // the aftermath, if a camera has him
            if (LundunIncidents.Chase != null) return;                    // inside a chase: the chase gets its own clip
            snapAt = Time.time + SnapAfter;                                // a later qualifying impact moves it on
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    void TakeSnapshot()
    {
        snapAt = -1f;
        if (LundunIncidents.Chase != null) return;
        LundunIncident rec = LundunIncidents.Latest(LundunIncidentKind.Crash);
        if (rec == null || rec.closed || rec.status == LundunIncidentStatus.Merged || rec.status == LundunIncidentStatus.Void) return;
        LundunCctvFeed feed = LundunCctvFeed.Instance;
        LundunCctvClip clip = feed != null ? feed.Cut(Time.time - LundunCctvFeed.RingSeconds, Time.time) : null;
        if (clip == null) return;                                         // no camera had him
        if (held != null) held.Free();                                    // a retake: the earlier impact's footage goes
        held = clip; heldId = rec.id;
        clip.incident = rec.id; clip.mode = myMode;
        Report("crash footage held for " + rec.id);
    }

    // ------------------------------------------------------------------ incidents closing: cut and attach
    void OnClosed(LundunIncident r)
    {
        try
        {
            if (r == null || r.mode != myMode) return;
            if (heldId != null && r.id == heldId)
            {
                LundunCctvClip c = held; held = null; heldId = null;
                if (r.status == LundunIncidentStatus.Merged || r.status == LundunIncidentStatus.Void) { c.Free(); Report("crash footage freed (" + r.status.ToString().ToLowerInvariant() + ")"); return; }
                Keep(r, c);
                return;
            }
            if (r.status == LundunIncidentStatus.Merged || r.status == LundunIncidentStatus.Void) return;
            if (!Wanted(r)) return;
            LundunCctvFeed feed = LundunCctvFeed.Instance;
            if (feed == null) return;
            float from = r.kind == LundunIncidentKind.Chase ? r.startTime : r.endTime - LundunCctvFeed.RingSeconds;
            LundunCctvClip clip = feed.Cut(from, r.endTime + 0.01f);       // only what the recorder still holds (last 10 s)
            if (clip == null) return;
            clip.incident = r.id; clip.mode = myMode;
            Keep(r, clip);
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    static bool Wanted(LundunIncident r)
    {
        if (r.kind == LundunIncidentKind.Wasted || r.kind == LundunIncidentKind.Busted) return true;
        if (r.kind != LundunIncidentKind.Chase) return false;
        return r.end == LundunIncidentEnd.Escaped || r.end == LundunIncidentEnd.EscapedByTrain || r.end == LundunIncidentEnd.Resprayed
            || r.end == LundunIncidentEnd.Wasted || r.end == LundunIncidentEnd.Busted;
    }

    void Keep(LundunIncident r, LundunCctvClip clip)
    {
        // the limit: make room by freeing the oldest unprotected clip; if every one is protected, don't keep this one
        while (kept.Count >= MaxKept)
        {
            LundunCctvClip victim = null;
            foreach (LundunCctvClip c in kept) if (!Protected(c)) { victim = c; break; }   // kept is oldest first
            if (victim == null)
            {
                clip.Free();
                Report("footage not kept: all " + MaxKept + " clips in use (" + r.id + ")");
                return;
            }
            victim.Free();
            kept.Remove(victim);
            Report("oldest footage freed (" + victim.incident + ")");
        }
        kept.Add(clip);
        LundunCctvClip mine = clip;
        bool ok = LundunIncidents.Attach(r.id, "replay", clip, () => { mine.Free(); if (Instance != null) Instance.kept.Remove(mine); });
        if (!ok) { Report("footage not attached (" + r.id + ")"); return; }    // G8.0 has already run the release
        LundunIncidents.Attach(r.id, "still", new LundunCctvStill(clip, Mathf.Clamp(Mathf.RoundToInt((clip.Count - 1) * 0.66f), 0, clip.Count - 1)), null);
        Report("footage kept for " + r.id + " (" + clip.Count + " frames)");
    }

    bool Protected(LundunCctvClip c)
    {
        if (c.playing) return true;
        LundunIncident rec = LundunIncidents.Get(c.incident);
        return rec != null && rec.pinnedUntil > Time.unscaledTime;
    }

    // ------------------------------------------------------------------ the CCTV FOOTAGE button
    void OnSettled(LundunIncident r)
    {
        try
        {
            if (r == null || r.mode != myMode || !r.closed) return;
            bool escape = r.kind == LundunIncidentKind.Chase && (r.end == LundunIncidentEnd.Escaped || r.end == LundunIncidentEnd.EscapedByTrain || r.end == LundunIncidentEnd.Resprayed);
            if (!escape && r.kind != LundunIncidentKind.Crash) return;
            if (Time.time - r.endTime > FreshClose) return;                // settled now by a mission pass: pause menu only
            if (LundunMissions.Running || LundunMissions.CardOpen || VehicleSystem.Locked) return;
            if (Clip(r.id) == null) return;
            ShowPrompt(r.id);
        }
        catch (Exception e) { Debug.LogException(e); }
    }

    /// Settled footage for this incident (null if none, or it's been freed).
    static LundunCctvClip Clip(string id)
    {
        object o;
        if (!LundunIncidents.TryGetSettled(id, "replay", out o)) return null;
        LundunCctvClip c = o as LundunCctvClip;
        return c != null && !c.Freed ? c : null;
    }

    /// The newest settled footage in this mode (null if none).
    static string LatestId()
    {
        foreach (LundunIncident r in LundunIncidents.History) if (Clip(r.id) != null) return r.id;
        return null;
    }

    /// The pause menu's LAST FOOTAGE: is there any?
    public static bool HasFootage { get { return Instance != null && LatestId() != null; } }

    /// The pause menu's LAST FOOTAGE (the game's already paused there).
    public static void WatchLatest(bool fromPause)
    {
        if (Instance == null) return;
        string id = LatestId();
        if (id != null) Instance.Watch(id, fromPause);
    }

    void Watch(string id, bool fromPause)
    {
        if (watching || LundunCctvFeed.Instance == null || LundunCctvFeed.Busy) return;
        LundunCctvClip clip = Clip(id);
        if (clip == null) return;
        StartCoroutine(WatchRoutine(id, clip, fromPause));
    }

    IEnumerator WatchRoutine(string id, LundunCctvClip clip, bool fromPause)
    {
        watching = true;
        clip.playing = true;
        LundunIncidents.Pin(id);
        HidePrompt();
        float before = Time.timeScale > 0f ? Time.timeScale : 1f;
        bool pausedHere = !fromPause && Time.timeScale > 0f;
        if (pausedHere) { Time.timeScale = 0f; AudioListener.pause = true; }
        stopRequested = false;
        CloseLayer(true);
        LundunHaptics.Light();
        yield return LundunCctvFeed.Instance.PlayClip(clip, () => stopRequested);
        CloseLayer(false);
        clip.playing = false;
        LundunIncidents.Unpin(id);
        if (pausedHere && (LundunSettings.Instance == null || !LundunSettings.Instance.IsOpen)) { Time.timeScale = before; AudioListener.pause = false; }
        watching = false;
    }

    void Update()
    {
        if (snapAt > 0f && Time.time >= snapAt) { try { TakeSnapshot(); } catch (Exception e) { Debug.LogException(e); } }
        if (prompt != null && prompt.activeSelf)
        {
            bool dead = PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead;
            if (Time.unscaledTime >= promptUntil || dead || LundunMissions.Running || LundunMissions.CardOpen || VehicleSystem.Locked || Clip(promptId) == null) HidePrompt();
        }
        keptClips = kept.Count;
        holdingSnapshot = held != null;
    }

    void ShowPrompt(string id)
    {
        if (hud == null) BuildPrompt();
        promptId = id;
        promptUntil = Time.unscaledTime + PromptFor;
        prompt.SetActive(true);
    }

    void HidePrompt() { if (prompt != null) prompt.SetActive(false); }

    void BuildPrompt()
    {
        GameObject cg = new GameObject("CctvFootageButton");
        cg.transform.SetParent(transform, false);
        hud = cg.AddComponent<Canvas>();
        hud.renderMode = RenderMode.ScreenSpaceOverlay;
        hud.sortingOrder = 70;                                             // above the HUD, under the pause menu (85)
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();

        GameObject b = new GameObject("Button", typeof(RectTransform));
        b.transform.SetParent(cg.transform, false);
        RectTransform r = (RectTransform)b.transform;
        r.anchorMin = r.anchorMax = new Vector2(1f, 0.64f); r.pivot = new Vector2(1f, 0.5f);
        r.anchoredPosition = new Vector2(-36f, 0f); r.sizeDelta = new Vector2(380f, 84f);
        Image bg = b.AddComponent<Image>();
        if (LundunHudArt.Pill != null) { bg.sprite = LundunHudArt.Pill; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 0.67f; }
        bg.color = new Color(0.03f, 0.05f, 0.09f, 0.9f);
        Button btn = b.AddComponent<Button>(); btn.targetGraphic = bg;
        btn.onClick.AddListener(() => { if (promptId != null) Watch(promptId, false); });
        b.AddComponent<LundunPressFX>();
        Content(r, "CCTV FOOTAGE", Color.white, 30f);
        prompt = b;
        prompt.SetActive(false);
    }

    /// The play triangle and a label inside a pill (shared with the pause menu's LAST FOOTAGE).
    public static void Content(RectTransform pill, string label, Color col, float size)
    {
        GameObject ig = new GameObject("Play", typeof(RectTransform));
        ig.transform.SetParent(pill, false);
        RectTransform ir = (RectTransform)ig.transform;
        ir.anchorMin = ir.anchorMax = new Vector2(0f, 0.5f); ir.pivot = new Vector2(0f, 0.5f);
        ir.anchoredPosition = new Vector2(30f, 0f); ir.sizeDelta = new Vector2(size * 0.8f, size * 0.8f);
        Image icon = ig.AddComponent<Image>(); icon.sprite = PlayIcon; icon.color = new Color(1f, 0.66f, 0.18f); icon.raycastTarget = false;

        GameObject tg = new GameObject("Label", typeof(RectTransform));
        tg.transform.SetParent(pill, false);
        RectTransform tr = (RectTransform)tg.transform;
        tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = new Vector2(30f + size, 0f); tr.offsetMax = new Vector2(-20f, 0f);
        TextMeshProUGUI t = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = label; t.fontSize = size; t.color = col; t.fontStyle = FontStyles.Bold; t.characterSpacing = 6f;
        t.alignment = TextAlignmentOptions.Left; t.raycastTarget = false;
    }

    /// A small play triangle, drawn in code (no font glyph needed).
    static Sprite PlayIcon
    {
        get
        {
            if (playIcon != null) return playIcon;
            const int n = 32;
            Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "PlayIcon" };
            Color32[] px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) / n, fy = (y + 0.5f) / n;
                    float half = 0.5f * (1f - (fx - 0.1f) / 0.85f);          // the triangle narrows to the right
                    bool inside = fx >= 0.1f && fx <= 0.95f && Mathf.Abs(fy - 0.5f) <= half;
                    px[y * n + x] = inside ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
                }
            tex.SetPixels32(px); tex.Apply();
            playIcon = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return playIcon;
        }
    }

    // ------------------------------------------------------------------ tap to close (its own layer: the CCTV picture keeps no touches)
    void CloseLayer(bool on)
    {
        if (closeCanvas == null)
        {
            GameObject cg = new GameObject("CctvFootageClose");
            cg.transform.SetParent(transform, false);
            closeCanvas = cg.AddComponent<Canvas>();
            closeCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            closeCanvas.sortingOrder = 401;                                // just above the CCTV picture (400)
            cg.AddComponent<CanvasScaler>();
            cg.AddComponent<GraphicRaycaster>();
            GameObject b = new GameObject("Tap", typeof(RectTransform));
            b.transform.SetParent(cg.transform, false);
            RectTransform r = (RectTransform)b.transform;
            r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero;
            Image img = b.AddComponent<Image>(); img.color = new Color(0f, 0f, 0f, 0f);
            Button btn = b.AddComponent<Button>(); btn.targetGraphic = img; btn.transition = Selectable.Transition.None;
            btn.onClick.AddListener(() => stopRequested = true);
        }
        closeCanvas.gameObject.SetActive(on);
    }

    // ------------------------------------------------------------------ readout
    void Report(string what)
    {
        LundunCctvFeed feed = LundunCctvFeed.Instance;
        long ring = feed != null ? feed.RingBytes : 0L;
        long clips = 0L; foreach (LundunCctvClip c in kept) clips += c.Bytes;
        long h = held != null ? held.Bytes : 0L;
        memory = "ring " + Mb(ring) + " · clips " + kept.Count + " (" + Mb(clips) + ") · held " + (held != null ? 1 : 0) + " (" + Mb(h) + ")";
        if (Application.isEditor || Debug.isDebugBuild) Debug.Log("[Replays] " + what + " · " + memory);
    }

    static string Mb(long bytes) { return (bytes / (1024f * 1024f)).ToString("0.0") + " MB"; }

    [ContextMenu("Pin every kept clip's incident (pin test)")]
    void PinAll()
    {
        foreach (LundunCctvClip c in kept) Debug.Log("[Replays] pin " + c.incident + ": " + LundunIncidents.Pin(c.incident));
    }

    [ContextMenu("Report memory")]
    void ReportNow() { Report("now"); }
}

/// G8.1b: one incident's CCTV footage: its own copies of the frames (so the recorder can't overwrite them), the camera
/// and clock time of each, the gap between frames. Freed by the clip limit, VOID, eviction or the scene ending.
public class LundunCctvClip
{
    public readonly RenderTexture[] frames;
    public readonly int[] cam;
    public readonly float[] hour;
    public readonly float gap;
    public string incident;
    public int mode;
    public bool playing;
    public bool Freed { get; private set; }

    public LundunCctvClip(int count, float gap)
    {
        frames = new RenderTexture[count]; cam = new int[count]; hour = new float[count]; this.gap = gap;
    }

    public int Count { get { return frames.Length; } }

    public long Bytes
    {
        get
        {
            if (Freed || frames.Length == 0 || frames[0] == null) return 0L;
            RenderTexture f = frames[0];
            return (long)f.width * f.height * (f.format == RenderTextureFormat.RGB565 ? 2 : 4) * frames.Length;
        }
    }

    public void Free()
    {
        if (Freed) return;
        Freed = true;
        for (int i = 0; i < frames.Length; i++)
        {
            if (frames[i] == null) continue;
            frames[i].Release();
            UnityEngine.Object.Destroy(frames[i]);
            frames[i] = null;
        }
    }
}

/// G8.1b: the still for an incident (for G8.1a's share cards): one frame of its clip. Texture is null once the clip's
/// been freed.
public class LundunCctvStill
{
    public readonly LundunCctvClip clip;
    public readonly int index;
    public LundunCctvStill(LundunCctvClip clip, int index) { this.clip = clip; this.index = index; }
    public Texture Texture { get { return clip == null || clip.Freed ? null : clip.frames[index]; } }
    public int Camera { get { return clip == null ? -1 : clip.cam[index]; } }
    public float Hour { get { return clip == null ? 0f : clip.hour[index]; } }
}
