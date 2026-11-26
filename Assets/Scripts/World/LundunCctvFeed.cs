using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// V2f: the CCTV picture (added by LundunCctv; nothing to set up).
///   caught       when a camera catches James at it (WantedSystem) or reads his stolen plate (LundunCctv), the screen
///                cuts to that camera for about a second. Not more than once every 35 seconds, and never during a cutscene
///   the replay   while he's wanted or has been shooting, the camera that can see him is recorded a few times a second
///                (small pictures, the last three seconds kept). At WASTED or BUSTED, if it had him, those seconds play
///                back first as CCTV footage ("PLAYBACK"), then the WASTED / BUSTED screen as before
/// Phase 5 (gate B):
///   the look     black and white, like real council CCTV: low frame rate, grain, a soft glow round lights, slight
///                lens bulge, interlace lines, the odd tear, dark corners; the on-screen display of a real system
///                (camera name, date and time ticking, ● REC, the operator's name, corner brackets)
///   scripted     missions can cut to any spot as a CCTV camera for a few seconds (Scripted / LundunFeedCut): a
///                caption ("PLATE READ  ·  LK19 XRT") and a tracking box round someone or something (SUBJECT, a
///                plate), for the M03 plate read, Hollis's footage, Leah's help, the shop camera...
/// Phase 5 (gate A2):
///   stills      a scripted cut can keep a still of what the camera saw (keepAs: the evidence id), saved with the story
///               (where the subject was in the picture, and the time on the clock), so a later mission can play it
///               back: Hollis showing James the Basketful tills on his phone in M05 (Playback)
///   phone       a cut or a playback can be shown as a phone held up to James (the picture smaller, the world behind)
/// Costs: one small extra camera that only draws on the frames it's needed (12 a second during a cut, 4 a second while
/// recording), no shadows, no post-processing, no anti-aliasing.
/// G8.1b: the recorder keeps the last 10 seconds at 5 a second (50 small 384×216 pictures in 16-bit colour, ~8 MB,
/// drawn into one working picture and copied into the ring), with the game time of each frame so a clip can be cut for
/// an incident (Cut, used by LundunCctvClips). It also records while James drives fast in a camera's view, and for a few
/// seconds after a big crash; as before, only while a camera actually has him. WASTED / BUSTED play the last ~5 s
/// (same look, same place in the sequence). Fallbacks (only if the iPhone numbers call for them): fallbackFps4,
/// fallbackSmall, set before recording starts.
public class LundunCctvFeed : MonoBehaviour
{
    public static LundunCctvFeed Instance { get; private set; }

    const int W = 480, H = 270;              // the live picture (caught cut, scripted cuts, evidence stills)
    const float LiveGap = 1f / 12f;
    // G8.1b: the recorder
    public const float RingSeconds = 10f;     // what the recorder holds: the last 10 seconds
    const float ReplaySeconds = 5f;           // WASTED / BUSTED play the last ~5 s
    public const float FastSpeed = 12f;       // m/s: recording while driving at least this fast in a camera's view
    const int MinClipFrames = 8;              // a clip needs at least this many frames (1.6 s at 5 a second)
    [Tooltip("G8.1b fallback: record 4 frames a second instead of 5 (set before recording starts; only if the iPhone numbers need it)")]
    [SerializeField] private bool fallbackFps4 = false;
    [Tooltip("G8.1b fallback: 320×180 frames instead of 384×216 (set before recording starts; only if the iPhone numbers need it)")]
    [SerializeField] private bool fallbackSmall = false;
    const float CutLength = 1.1f, CutCooldown = 35f;

    /// What the tracking box is round.
    public enum Mark { None, Subject, Plate }

    private Camera eye;
    private RenderTexture live;
    private RenderTexture[] ring;
    private float[] ringAt, ringHour, ringGame;   // G8.1b: + the game time of each frame
    private int[] ringCam;
    private int ringNext;
    // G8.1b: the ring's size and format, the working picture the eye draws into, the frame waiting to be copied in
    private int frames, rw, rh;
    private float recordGap = 0.2f;
    private RenderTextureFormat ringFormat = RenderTextureFormat.ARGB32;
    private RenderTexture work;
    private int pendingSlot = -1;
    private float keepRecordingUntil = -1f;

    private Canvas canvas;
    private RawImage screen;
    private RectTransform picture, box;
    private Image back, bezel;
    private AspectRatioFitter fit;
    private Vector3 lastMarkV;                // where the tracking box last was in the picture (viewport x, y, distance)
    private TextMeshProUGUI camLabel, clockLabel, tagLabel, captionLabel, boxLabel;
    private GameObject captionBar;
    private Image recDot;

    private float showUntil = -1f, lastCut = -99f, nextLive, nextRecord, lastShot = -99f;
    private int showCam = -1;
    private bool replaying, scripted;
    private VehicleSystem vehicles;

    /// A scripted cut is playing.
    public static bool Busy { get { return Instance != null && (Instance.scripted || Instance.replaying); } }

    void Awake() { Instance = this; }
    void OnEnable() { LundunCctv.PlateRead += OnPlateRead; PlayerCombat.GunshotFired += OnGunshot; }
    void OnDisable() { LundunCctv.PlateRead -= OnPlateRead; PlayerCombat.GunshotFired -= OnGunshot; }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (live != null) live.Release();
        if (ring != null) foreach (RenderTexture r in ring) if (r != null) r.Release();
        if (work != null) work.Release();
    }

    void OnPlateRead(int cam) { Show(cam); }
    void OnGunshot(Vector3 at) { lastShot = Time.unscaledTime; }

    /// WantedSystem: a camera caught James at it.
    public static void Caught(int cam) { if (Instance != null) Instance.Show(cam); }

    void Show(int cam)
    {
        LundunCctv cctv = LundunCctv.Instance;
        if (cctv == null || cam < 0 || cam >= cctv.Count || replaying || scripted || VehicleSystem.Locked) return;
        if (Time.unscaledTime - lastCut < CutCooldown) return;
        if (!Ready()) return;
        lastCut = Time.unscaledTime;
        showCam = cam;
        showUntil = Time.unscaledTime + CutLength;
        nextLive = 0f;
        screen.texture = live;
        tagLabel.text = "LIVE";
        captionBar.SetActive(false);
        box.gameObject.SetActive(false);
        canvas.enabled = true;
        LundunHaptics.Light();
    }

    // ------------------------------------------------------------------ Phase 5: scripted cuts
    /// A mission's cut to a CCTV view: from 'lens' looking at look() (each frame), for 'seconds' (real time), named
    /// camName, with a caption (or none) and a tracking box round markAt() (or none). Yield on it to wait.
    public static IEnumerator Scripted(Vector3 lens, Func<Vector3> look, string camName, string caption, float seconds,
                                       Mark mark = Mark.None, Func<Vector3> markAt = null, string markLabel = null,
                                       string keepAs = null, bool phone = false)
    {
        LundunCctvFeed f = Instance;
        if (f == null || !f.Ready()) yield break;
        while (f.replaying) yield return null;
        f.scripted = true;
        f.Phone(phone);
        int drawn = 0; bool kept = keepAs == null;
        f.showUntil = -1f;
        f.screen.texture = f.live;
        f.tagLabel.text = "LIVE";
        f.camLabel.text = camName;
        f.captionBar.SetActive(!string.IsNullOrEmpty(caption));
        f.captionLabel.text = caption ?? "";
        f.box.gameObject.SetActive(false);
        f.canvas.enabled = true;
        LundunHaptics.Light();
        float end = Time.unscaledTime + seconds, next = 0f;
        while (Time.unscaledTime < end && f != null)
        {
            bool frame = Time.unscaledTime >= next;                          // a choppy 12 a second
            if (frame && !kept && drawn >= 6)                                // half a second in: what it's got is kept
            {
                kept = true;
                f.Keep(keepAs, mark != Mark.None ? f.lastMarkV : Vector3.zero, LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f);
            }
            if (frame)
            {
                drawn++;
                next = Time.unscaledTime + LiveGap;
                Vector3 target = look != null ? look() : lens + Vector3.forward;
                Vector3 d = target - lens;
                if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
                f.eye.transform.SetPositionAndRotation(lens, Quaternion.LookRotation(d, Vector3.up));
                f.eye.targetTexture = f.live;
                f.Track(mark, markAt, markLabel);
            }
            f.eye.enabled = frame;                                         // drawn at the end of this frame only
            f.Clock(LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f);
            yield return null;
        }
        if (f != null)
        {
            f.eye.enabled = false;
            f.canvas.enabled = false;
            f.captionBar.SetActive(false);
            f.box.gameObject.SetActive(false);
            f.Phone(false);
            f.scripted = false;
        }
    }

    // ------------------------------------------------------------------ Phase 5 (gate A2): stills of James, played back later
    static string StillPath(string id) { return Path.Combine(Application.persistentDataPath, "evidence_" + id + ".png"); }
    const string StillKey = "evidence_still:";

    /// A still of this evidence is on file (the camera recorded him, and the picture was kept).
    public static bool HasStill(string id)
    {
        if (!LundunEvidence.Exists(id)) return false;
        try { return File.Exists(StillPath(id)); } catch { return false; }
    }

    /// The live picture, as it is now, kept as the still for 'id' (with where the subject was, and the time).
    void Keep(string id, Vector3 markV, float hour)
    {
        if (!LundunGame.IsStory || string.IsNullOrEmpty(id) || live == null) return;
        Texture2D tex = null;
        RenderTexture was = RenderTexture.active;
        try
        {
            RenderTexture.active = live;
            tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            File.WriteAllBytes(StillPath(id), tex.EncodeToPNG());
            CultureInfo c = CultureInfo.InvariantCulture;
            LundunStory.Set(StillKey + id, markV.x.ToString("F3", c) + "," + markV.y.ToString("F3", c) + "," + markV.z.ToString("F2", c) + "," + hour.ToString("F3", c));
            Debug.Log("[Lundun] Evidence still kept: " + id);
        }
        catch (Exception e) { Debug.LogWarning("[Lundun] Couldn't keep the evidence still " + id + ": " + e.Message); }
        finally
        {
            RenderTexture.active = was;
            if (tex != null) Destroy(tex);
        }
    }

    /// A kept still played back as CCTV footage ("<< PLAYBACK", the time it was taken, the box round him): on a phone
    /// held up to James if 'phone'. Nothing (and no wait) if there's no still on file. Yield on it to wait.
    public static IEnumerator Playback(string id, string camName, string caption, float seconds, string markLabel, bool phone)
    {
        LundunCctvFeed f = Instance;
        if (f == null || !HasStill(id) || !f.Ready()) yield break;
        while (f.replaying || f.scripted) yield return null;
        Texture2D tex = new Texture2D(2, 2, TextureFormat.RGB24, false);
        bool ok;
        try { ok = tex.LoadImage(File.ReadAllBytes(StillPath(id))); } catch { ok = false; }
        if (!ok) { Destroy(tex); yield break; }
        Vector3 markV = Vector3.zero; float hour = 21.2f;
        string v;
        if (LundunStory.TryGet(StillKey + id, out v))
        {
            string[] bits = v.Split(',');
            CultureInfo c = CultureInfo.InvariantCulture;
            if (bits.Length >= 4)
            {
                float.TryParse(bits[0], NumberStyles.Float, c, out markV.x); float.TryParse(bits[1], NumberStyles.Float, c, out markV.y);
                float.TryParse(bits[2], NumberStyles.Float, c, out markV.z); float.TryParse(bits[3], NumberStyles.Float, c, out hour);
            }
        }
        f.scripted = true;
        f.eye.enabled = false;
        f.Phone(phone);
        f.screen.texture = tex;
        f.tagLabel.text = "<< PLAYBACK";
        f.camLabel.text = camName;
        f.captionBar.SetActive(!string.IsNullOrEmpty(caption));
        f.captionLabel.text = caption ?? "";
        f.box.gameObject.SetActive(false);
        f.canvas.enabled = true;
        LundunHaptics.Light();
        float start = Time.unscaledTime, end = start + seconds;
        while (Time.unscaledTime < end && f != null)
        {
            float k = Time.unscaledTime - start;
            if (markV.z > 0.2f && k > 0.6f) f.PlaceBox(markV, Mark.Subject, markLabel);   // the operator boxes him a moment in
            f.Clock(hour + k / 3600f);
            yield return null;
        }
        if (f != null)
        {
            f.canvas.enabled = false;
            f.captionBar.SetActive(false);
            f.box.gameObject.SetActive(false);
            f.screen.texture = f.live;
            f.Phone(false);
            f.scripted = false;
        }
        Destroy(tex);
    }

    /// The picture as a phone held up to James (smaller, a bezel round it, the world dimmed behind), or full screen.
    void Phone(bool on)
    {
        if (picture == null) return;
        if (back != null) back.color = on ? new Color(0f, 0f, 0f, 0.55f) : Color.black;
        if (bezel != null) bezel.gameObject.SetActive(on);
        if (fit != null) fit.enabled = !on;
        if (on)
        {
            picture.anchorMin = picture.anchorMax = new Vector2(0.5f, 0.5f);
            picture.pivot = new Vector2(0.5f, 0.5f);
            picture.sizeDelta = new Vector2(1120f, 630f);
            picture.anchoredPosition = new Vector2(0f, 40f);
        }
        else Stretch(picture);
    }

    /// The tracking box: round where markAt() is in the picture (hidden if it's off screen).
    void Track(Mark mark, Func<Vector3> markAt, string markLabel)
    {
        if (mark == Mark.None || markAt == null) { box.gameObject.SetActive(false); return; }
        Vector3 p = markAt();
        Vector3 v = eye.WorldToViewportPoint(p);
        lastMarkV = v;
        PlaceBox(v, mark, markLabel);
    }

    /// The tracking box round viewport point v (x, y in the picture, z its distance).
    void PlaceBox(Vector3 v, Mark mark, string markLabel)
    {
        if (v.z <= 0.2f || v.x < 0.02f || v.x > 0.98f || v.y < 0.02f || v.y > 0.98f) { box.gameObject.SetActive(false); return; }
        Rect r = picture.rect;
        float size = Mathf.Clamp((mark == Mark.Plate ? 2.2f : 4.5f) * r.height / Mathf.Max(1f, v.z), 60f, 420f);
        box.anchoredPosition = new Vector2((v.x - 0.5f) * r.width, (v.y - 0.5f) * r.height);
        box.sizeDelta = mark == Mark.Plate ? new Vector2(size * 1.6f, size * 0.6f) : new Vector2(size * 0.7f, size);
        boxLabel.text = markLabel ?? (mark == Mark.Plate ? "PLATE" : "SUBJECT");
        if (!box.gameObject.activeSelf) box.gameObject.SetActive(true);
    }

    // ------------------------------------------------------------------ each frame: draw the camera only when needed
    void LateUpdate()
    {
        // G8.1b: last frame's recording was drawn into the working picture: copy it into its slot first
        if (pendingSlot >= 0 && work != null && ring != null)
        {
            Graphics.Blit(work, ring[pendingSlot]);
            ringAt[pendingSlot] = pendingAt; ringGame[pendingSlot] = pendingGame; ringCam[pendingSlot] = pendingCam; ringHour[pendingSlot] = pendingHour;
            pendingSlot = -1;
        }
        if (scripted) return;                                           // the scripted cut draws its own frames
        if (eye == null) { if (showUntil < 0f && !Recording()) return; if (!Ready()) return; }
        bool draw = false;
        float now = Time.unscaledTime;
        LundunCctv cctv = LundunCctv.Instance;

        if (showUntil > now && cctv != null)
        {
            if (now >= nextLive)
            {
                nextLive = now + LiveGap;                                   // a choppy 12 a second, like the real thing
                Aim(showCam);
                eye.targetTexture = live;
                draw = true;
            }
            Labels(showCam, LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f);
        }
        else if (showUntil > 0f && !replaying)
        {
            showUntil = -1f;
            canvas.enabled = false;
        }

        if (!draw && !replaying && now >= nextRecord && Recording())
        {
            if (ring == null) MakeRing();
            nextRecord = now + recordGap;
            int cam = cctv.FirstWatching;
            if (cam >= 0)
            {
                Aim(cam);
                eye.targetTexture = work;                                   // G8.1b: drawn small, copied into the ring next frame
                pendingSlot = ringNext;
                pendingAt = now; pendingGame = Time.time; pendingCam = cam;
                pendingHour = LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f;
                ringAt[ringNext] = 0f;                                      // not usable until it's been copied in
                ringNext = (ringNext + 1) % frames;
                draw = true;
            }
        }
        eye.enabled = draw;
    }

    private float pendingAt, pendingGame, pendingHour;
    private int pendingCam;

    /// G8.1b: keep recording (while a camera has him) for a few seconds: just after a big crash, for its aftermath.
    public static void KeepRecording(float seconds)
    {
        if (Instance != null) Instance.keepRecordingUntil = Mathf.Max(Instance.keepRecordingUntil, Time.unscaledTime + seconds);
    }

    /// G8.1b: driving fast (in a camera's view, which Recording checks first).
    bool Fast()
    {
        if (vehicles == null) vehicles = FindAnyObjectByType<VehicleSystem>();
        return vehicles != null && vehicles.IsDriving && vehicles.CurrentCar != null && Mathf.Abs(vehicles.CurrentCar.Speed) >= FastSpeed;
    }

    /// Worth recording: the cameras count, a camera has him, and he's wanted or has just been shooting.
    /// G8.1b: or he's driving fast, or it's just after a big crash (still only while a camera has him).
    bool Recording()
    {
        if (!LundunCctv.Live || LundunCctv.Instance.FirstWatching < 0) return false;
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead) return false;
        WantedSystem w = WantedSystem.Instance;
        if ((w != null && w.Stars > 0) || Time.unscaledTime - lastShot < 12f) return true;
        return Time.unscaledTime < keepRecordingUntil || Fast();
    }

    void Aim(int cam)
    {
        LundunCctv cctv = LundunCctv.Instance;
        if (cctv == null || cam < 0 || cam >= cctv.Count) return;
        Vector3 at = cctv[cam].at;
        if (vehicles == null) vehicles = FindAnyObjectByType<VehicleSystem>();
        Vector3 target = vehicles != null && vehicles.PlayerTarget != null ? vehicles.PlayerTarget.position + Vector3.up : at + Quaternion.Euler(0f, cctv[cam].yaw, 0f) * Vector3.forward * 5f;
        Vector3 d = target - at;
        if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
        eye.transform.SetPositionAndRotation(at + d.normalized * 0.3f, Quaternion.LookRotation(d, Vector3.up));
    }

    // ------------------------------------------------------------------ the replay
    /// WASTED / BUSTED: the last few seconds a camera had of him, as CCTV footage (nothing if no camera had him).
    public IEnumerator Replay()
    {
        if (ring == null || !Ready() || scripted) yield break;
        float now = Time.unscaledTime;
        List<int> take = new List<int>();
        for (int k = 0; k < frames; k++)
        {
            int i = (ringNext + k) % frames;                             // oldest first
            if (ringAt[i] > 0f && now - ringAt[i] < ReplaySeconds + 0.5f) take.Add(i);   // G8.1b: ~5 s (was 3)
        }
        if (take.Count < 4) yield break;
        replaying = true;
        showUntil = -1f;
        eye.enabled = false;
        captionBar.SetActive(false);
        box.gameObject.SetActive(false);
        canvas.enabled = true;
        tagLabel.text = "<< PLAYBACK";
        foreach (int i in take)
        {
            screen.texture = ring[i];
            Labels(ringCam[i], ringHour[i]);
            yield return new WaitForSecondsRealtime(i == take[take.Count - 1] ? 0.6f : recordGap);
        }
        canvas.enabled = false;
        for (int k = 0; k < frames; k++) ringAt[k] = 0f;                // used up
        replaying = false;
    }

    // ------------------------------------------------------------------ G8.1b: clips for incidents
    /// A copy of the frames still in the recorder (the last 10 s) whose game time falls between gameFrom and gameTo.
    /// Null if fewer than 8 (no camera had him in that window). The copy is the clip's own: the recorder overwriting
    /// itself afterwards can't touch it. Used by LundunCctvClips only.
    public LundunCctvClip Cut(float gameFrom, float gameTo)
    {
        if (ring == null) return null;
        float now = Time.unscaledTime;
        List<int> take = new List<int>();
        for (int k = 0; k < frames; k++)
        {
            int i = (ringNext + k) % frames;                             // oldest first
            if (ringAt[i] > 0f && now - ringAt[i] <= RingSeconds && ringGame[i] >= gameFrom && ringGame[i] <= gameTo) take.Add(i);
        }
        if (take.Count < MinClipFrames) return null;
        LundunCctvClip clip = new LundunCctvClip(take.Count, recordGap);
        bool copy = SystemInfo.copyTextureSupport != UnityEngine.Rendering.CopyTextureSupport.None;
        for (int j = 0; j < take.Count; j++)
        {
            int i = take[j];
            RenderTexture rt = new RenderTexture(rw, rh, 0, ringFormat) { name = "CctvClip" + j };
            rt.Create();
            if (copy) Graphics.CopyTexture(ring[i], rt); else Graphics.Blit(ring[i], rt);
            clip.frames[j] = rt; clip.cam[j] = ringCam[i]; clip.hour[j] = ringHour[i];
        }
        return clip;
    }

    /// The recorder's memory (bytes), for the [Replays] readout.
    public long RingBytes { get { return ring == null ? 0L : (long)frames * FrameBytes + (work != null ? (long)rw * rh * 6 : 0L); } }
    public long FrameBytes { get { return (long)rw * rh * (ringFormat == RenderTextureFormat.RGB565 ? 2 : 4); } }

    /// G8.1b: play a clip as CCTV footage (the same look as the WASTED / BUSTED playback), "TAP TO CLOSE" on the caption.
    /// stop: checked all the time (the close tap). Real time, so it plays while the game is paused.
    public IEnumerator PlayClip(LundunCctvClip clip, Func<bool> stop)
    {
        if (clip == null || clip.Freed || !Ready() || scripted || replaying) yield break;
        replaying = true;
        showUntil = -1f;
        eye.enabled = false;
        box.gameObject.SetActive(false);
        captionLabel.text = "TAP TO CLOSE";
        captionBar.SetActive(true);
        canvas.enabled = true;
        tagLabel.text = "<< PLAYBACK";
        for (int j = 0; j < clip.Count; j++)
        {
            if (clip.Freed || (stop != null && stop())) break;
            screen.texture = clip.frames[j];
            Labels(clip.cam[j], clip.hour[j]);
            float until = Time.unscaledTime + (j == clip.Count - 1 ? 0.8f : clip.gap);
            while (Time.unscaledTime < until && !clip.Freed && (stop == null || !stop())) yield return null;
        }
        screen.texture = live;
        captionBar.SetActive(false);
        canvas.enabled = false;
        replaying = false;
    }

    // ------------------------------------------------------------------ building it
    bool Ready()
    {
        if (eye != null) return true;
        try
        {
            GameObject g = new GameObject("CctvEye");
            g.transform.SetParent(transform, false);
            eye = g.AddComponent<Camera>();
            eye.enabled = false;
            eye.fieldOfView = 62f;
            eye.nearClipPlane = 0.3f;
            eye.farClipPlane = 160f;
            eye.depth = -20f;
            eye.allowHDR = false;
            eye.allowMSAA = false;
            int ui = LayerMask.NameToLayer("UI");
            if (ui >= 0) eye.cullingMask = ~(1 << ui);
            if (LundunViewModel.Layer >= 0) eye.cullingMask &= ~(1 << LundunViewModel.Layer);   // V1: not the first-person arms and gun
            UniversalAdditionalCameraData data = eye.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.renderShadows = false;
                data.antialiasing = AntialiasingMode.None;
                data.requiresDepthTexture = false;
                data.requiresColorTexture = false;
            }
            live = new RenderTexture(W, H, 16) { name = "CctvLive" };
            BuildOverlay();
            return true;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            enabled = false;
            return false;
        }
    }

    void MakeRing()
    {
        // G8.1b: 10 s at 5 a second (or the fallbacks), small, 16-bit colour where the phone supports it
        recordGap = fallbackFps4 ? 0.25f : 0.2f;
        frames = Mathf.CeilToInt(RingSeconds / recordGap);
        rw = fallbackSmall ? 320 : 384; rh = fallbackSmall ? 180 : 216;
        ringFormat = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB565) ? RenderTextureFormat.RGB565 : RenderTextureFormat.ARGB32;
        ring = new RenderTexture[frames];
        ringAt = new float[frames]; ringHour = new float[frames]; ringCam = new int[frames]; ringGame = new float[frames];
        for (int i = 0; i < frames; i++) { ring[i] = new RenderTexture(rw, rh, 0, ringFormat) { name = "CctvFrame" + i }; ring[i].Create(); }
        work = new RenderTexture(rw, rh, 16) { name = "CctvWork" };
        ringNext = 0; pendingSlot = -1;
    }

    void BuildOverlay()
    {
        GameObject cg = new GameObject("CctvFeedCanvas");
        cg.transform.SetParent(transform, false);
        canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 400;
        CanvasScaler scaler = cg.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvas.enabled = false;

        // black behind (the picture is 16:9; phones are wider)
        back = NewImage("Back", cg.transform, Color.black);
        Stretch(back.rectTransform);
        // Phase 5 (A2): a phone's body round the picture (shown only when it's a phone held up)
        bezel = NewImage("PhoneBezel", cg.transform, new Color(0.035f, 0.035f, 0.04f, 1f));
        if (LundunHudArt.Pill != null) { bezel.sprite = LundunHudArt.Pill; bezel.type = Image.Type.Sliced; bezel.pixelsPerUnitMultiplier = 0.35f; }
        RectTransform bz = bezel.rectTransform;
        bz.anchorMin = bz.anchorMax = new Vector2(0.5f, 0.5f); bz.sizeDelta = new Vector2(1250f, 710f); bz.anchoredPosition = new Vector2(0f, 40f);
        bezel.gameObject.SetActive(false);

        GameObject sg = new GameObject("Picture", typeof(RectTransform));
        sg.transform.SetParent(cg.transform, false);
        screen = sg.AddComponent<RawImage>();
        screen.raycastTarget = false;
        picture = screen.rectTransform;
        Stretch(picture);
        fit = sg.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;          // all of the picture shows; black bars on wide phones
        fit.aspectRatio = W / (float)H;
        Shader feed = Resources.Load<Shader>("LundunCctvFeed");
        if (feed != null) screen.material = new Material(feed);
        else screen.color = new Color(0.8f, 0.82f, 0.85f);

        // the system's display, on the picture
        camLabel = NewLabel("Cam", picture, new Vector2(0f, 1f), TextAlignmentOptions.TopLeft, new Vector2(70f, -48f));
        clockLabel = NewLabel("Clock", picture, new Vector2(1f, 1f), TextAlignmentOptions.TopRight, new Vector2(-70f, -48f));
        tagLabel = NewLabel("Tag", picture, new Vector2(0f, 0f), TextAlignmentOptions.BottomLeft, new Vector2(116f, 48f));
        TextMeshProUGUI brand = NewLabel("Brand", picture, new Vector2(1f, 0f), TextAlignmentOptions.BottomRight, new Vector2(-70f, 48f));
        brand.text = "LUNDUN BOROUGH COUNCIL  ·  CCTV";
        brand.fontSize = 26f;
        recDot = NewImage("Rec", picture, new Color(1f, 0.15f, 0.12f));
        recDot.sprite = LundunHudArt.Disc;
        RectTransform rr = recDot.rectTransform;
        rr.anchorMin = rr.anchorMax = new Vector2(0f, 0f);
        rr.sizeDelta = new Vector2(26f, 26f);
        rr.anchoredPosition = new Vector2(88f, 66f);
        // corner brackets
        for (int k = 0; k < 4; k++)
        {
            Vector2 corner = new Vector2(k % 2, k / 2);
            Vector2 inward = new Vector2(corner.x < 0.5f ? 1f : -1f, corner.y < 0.5f ? 1f : -1f);
            Image h = NewImage("CornerH" + k, picture, new Color(1f, 1f, 1f, 0.75f));
            Image v = NewImage("CornerV" + k, picture, new Color(1f, 1f, 1f, 0.75f));
            foreach (Image im in new[] { h, v })
            {
                RectTransform t = im.rectTransform;
                t.anchorMin = t.anchorMax = corner; t.pivot = corner;
                t.anchoredPosition = inward * 30f;
            }
            h.rectTransform.sizeDelta = new Vector2(70f, 4f);
            v.rectTransform.sizeDelta = new Vector2(4f, 70f);
        }

        // the caption bar (scripted cuts)
        captionBar = new GameObject("Caption", typeof(RectTransform));
        captionBar.transform.SetParent(picture, false);
        RectTransform cb = (RectTransform)captionBar.transform;
        cb.anchorMin = cb.anchorMax = new Vector2(0.5f, 0f); cb.pivot = new Vector2(0.5f, 0f);
        cb.anchoredPosition = new Vector2(0f, 120f); cb.sizeDelta = new Vector2(1000f, 76f);
        Image cbi = captionBar.AddComponent<Image>(); cbi.color = new Color(0f, 0f, 0f, 0.78f); cbi.raycastTarget = false;
        captionLabel = NewLabel("CaptionText", cb, new Vector2(0.5f, 0.5f), TextAlignmentOptions.Center, Vector2.zero);
        captionLabel.rectTransform.sizeDelta = new Vector2(980f, 70f);
        captionLabel.fontSize = 40f;
        captionLabel.color = Color.white;
        captionBar.SetActive(false);

        // the tracking box
        GameObject bx = new GameObject("Track", typeof(RectTransform));
        bx.transform.SetParent(picture, false);
        box = (RectTransform)bx.transform;
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        Image frame = bx.AddComponent<Image>();
        frame.sprite = LundunHudArt.PanelRim; frame.type = Image.Type.Sliced; frame.pixelsPerUnitMultiplier = 1.4f;
        frame.color = new Color(1f, 1f, 1f, 0.95f); frame.raycastTarget = false;
        boxLabel = NewLabel("TrackLabel", box, new Vector2(0f, 1f), TextAlignmentOptions.BottomLeft, new Vector2(0f, 6f));
        boxLabel.rectTransform.pivot = new Vector2(0f, 0f);
        boxLabel.fontSize = 28f;
        bx.SetActive(false);
    }

    void Labels(int cam, float hour)
    {
        LundunCctv cctv = LundunCctv.Instance;
        string kind = cctv != null && cam >= 0 && cam < cctv.Count && cctv[cam].kind == LundunCctv.Kind.Dome ? "PTZ" : "FIXED";
        camLabel.text = "CAM " + (cam + 1).ToString("0000") + "  ·  " + kind;
        Clock(hour);
    }

    void Clock(float hour)
    {
        int secs = Mathf.FloorToInt(Mathf.Repeat(hour, 24f) * 3600f);
        DateTime today = DateTime.Now;
        clockLabel.text = "<mspace=0.55em>" + today.ToString("dd-MM-yyyy") + "  " + (secs / 3600).ToString("00") + ":" + (secs / 60 % 60).ToString("00") + ":" + (secs % 60).ToString("00") + "</mspace>";
        recDot.enabled = Mathf.Repeat(Time.unscaledTime, 1f) < 0.6f;
    }

    static Image NewImage(string name, Transform parent, Color c)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        Image im = g.AddComponent<Image>();
        im.color = c;
        im.raycastTarget = false;
        return im;
    }

    static TextMeshProUGUI NewLabel(string name, Transform parent, Vector2 corner, TextAlignmentOptions align, Vector2 offset)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        t.fontSize = 32f;
        t.color = new Color(0.95f, 0.97f, 1f, 0.95f);
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.richText = true;
        t.outlineWidth = 0.18f;                                          // legible on bright footage, like a real OSD
        t.outlineColor = new Color32(0, 0, 0, 200);
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = corner;
        rt.pivot = corner;
        rt.sizeDelta = new Vector2(900f, 50f);
        rt.anchoredPosition = offset;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}

/// Phase 5 (gate B): a mission step that cuts to a CCTV view (LundunCctvFeed.Scripted) and waits for it to finish.
public class LundunFeedCut : LundunStep
{
    readonly Func<Vector3> lens, look, markAt;
    readonly string camName, caption, markLabel;
    readonly float seconds;
    readonly LundunCctvFeed.Mark mark;
    readonly Action after;
    readonly string keepAs;
    readonly bool phone;
    bool done;

    public LundunFeedCut(Func<Vector3> lensAt, Func<Vector3> lookAt, string cam, string captionText, float secs,
                         LundunCctvFeed.Mark marked = LundunCctvFeed.Mark.None, Func<Vector3> marker = null, string markText = null, Action then = null,
                         string keepStillAs = null, bool onPhone = false)
    {
        lens = lensAt; look = lookAt; camName = cam; caption = captionText; seconds = secs;
        mark = marked; markAt = marker; markLabel = markText; after = then; keepAs = keepStillAs; phone = onPhone;
    }

    public override void Begin(LundunMissionRun run)
    {
        done = false;
        if (LundunCctvFeed.Instance == null) { done = true; return; }
        LundunCctvFeed.Instance.StartCoroutine(Play());
    }

    IEnumerator Play()
    {
        yield return LundunCctvFeed.Scripted(lens(), look, camName, caption, seconds, mark, markAt, markLabel, keepAs, phone);
        done = true;
        if (after != null) after();
    }

    public override bool Tick(LundunMissionRun run) { return done; }
}
