using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// V2f: the CCTV picture (added by LundunCctv; nothing to set up).
///   caught       when a camera catches James at it (WantedSystem) or reads his stolen plate (LundunCctv), the screen
///                cuts to that camera for about a second: grainy, green-grey, scanlines, "CAM 0412", the time, REC.
///                Not more than once every 35 seconds, and never during a cutscene
///   the replay   while he's wanted or has been shooting, the camera that can see him is recorded a few times a second
///                (small pictures, the last three seconds kept). At WASTED or BUSTED, if it had him, those seconds play
///                back first as CCTV footage ("<< PLAYBACK"), then the WASTED / BUSTED screen as before
/// Costs: one small extra camera that only draws on the frames it's needed (12 a second during the cut, 4 a second
/// while recording), no shadows, no post-processing, no anti-aliasing.
public class LundunCctvFeed : MonoBehaviour
{
    public static LundunCctvFeed Instance { get; private set; }

    const int W = 384, H = 216;
    const int Frames = 12;                    // 3 seconds at 4 a second
    const float RecordGap = 0.25f, LiveGap = 1f / 12f;
    const float CutLength = 1.1f, CutCooldown = 35f;

    private Camera eye;
    private RenderTexture live;
    private RenderTexture[] ring;
    private float[] ringAt, ringHour;
    private int[] ringCam;
    private int ringNext;

    private Canvas canvas;
    private RawImage screen;
    private TextMeshProUGUI camLabel, clockLabel, tagLabel;
    private Image recDot;

    private float showUntil = -1f, lastCut = -99f, nextLive, nextRecord, lastShot = -99f;
    private int showCam = -1;
    private bool replaying;
    private VehicleSystem vehicles;

    void Awake() { Instance = this; }
    void OnEnable() { LundunCctv.PlateRead += OnPlateRead; PlayerCombat.GunshotFired += OnGunshot; }
    void OnDisable() { LundunCctv.PlateRead -= OnPlateRead; PlayerCombat.GunshotFired -= OnGunshot; }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (live != null) live.Release();
        if (ring != null) foreach (RenderTexture r in ring) if (r != null) r.Release();
    }

    void OnPlateRead(int cam) { Show(cam); }
    void OnGunshot(Vector3 at) { lastShot = Time.unscaledTime; }

    /// WantedSystem: a camera caught James at it.
    public static void Caught(int cam) { if (Instance != null) Instance.Show(cam); }

    void Show(int cam)
    {
        LundunCctv cctv = LundunCctv.Instance;
        if (cctv == null || cam < 0 || cam >= cctv.Count || replaying || VehicleSystem.Locked) return;
        if (Time.unscaledTime - lastCut < CutCooldown) return;
        if (!Ready()) return;
        lastCut = Time.unscaledTime;
        showCam = cam;
        showUntil = Time.unscaledTime + CutLength;
        nextLive = 0f;
        screen.texture = live;
        tagLabel.text = "LIVE";
        canvas.enabled = true;
        LundunHaptics.Light();
    }

    // ------------------------------------------------------------------ each frame: draw the camera only when needed
    void LateUpdate()
    {
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
            nextRecord = now + RecordGap;
            int cam = cctv.FirstWatching;
            if (cam >= 0)
            {
                if (ring == null) MakeRing();
                Aim(cam);
                eye.targetTexture = ring[ringNext];
                ringAt[ringNext] = now;
                ringCam[ringNext] = cam;
                ringHour[ringNext] = LundunClock.Instance != null ? LundunClock.Instance.Hour : 22f;
                ringNext = (ringNext + 1) % Frames;
                draw = true;
            }
        }
        eye.enabled = draw;
    }

    /// Worth recording: the cameras count, a camera has him, and he's wanted or has just been shooting.
    bool Recording()
    {
        if (!LundunCctv.Live || LundunCctv.Instance.FirstWatching < 0) return false;
        if (PlayerHealth.Instance != null && PlayerHealth.Instance.IsDead) return false;
        WantedSystem w = WantedSystem.Instance;
        return (w != null && w.Stars > 0) || Time.unscaledTime - lastShot < 12f;
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
        if (ring == null || !Ready()) yield break;
        float now = Time.unscaledTime;
        List<int> take = new List<int>();
        for (int k = 0; k < Frames; k++)
        {
            int i = (ringNext + k) % Frames;                             // oldest first
            if (ringAt[i] > 0f && now - ringAt[i] < 4f) take.Add(i);
        }
        if (take.Count < 4) yield break;
        replaying = true;
        showUntil = -1f;
        eye.enabled = false;
        canvas.enabled = true;
        tagLabel.text = "<< PLAYBACK";
        foreach (int i in take)
        {
            screen.texture = ring[i];
            Labels(ringCam[i], ringHour[i]);
            yield return new WaitForSecondsRealtime(i == take[take.Count - 1] ? 0.6f : RecordGap);
        }
        canvas.enabled = false;
        for (int k = 0; k < Frames; k++) ringAt[k] = 0f;                // used up
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
        catch (System.Exception e)
        {
            Debug.LogException(e);
            enabled = false;
            return false;
        }
    }

    void MakeRing()
    {
        ring = new RenderTexture[Frames];
        ringAt = new float[Frames]; ringHour = new float[Frames]; ringCam = new int[Frames];
        for (int i = 0; i < Frames; i++) ring[i] = new RenderTexture(W, H, 16) { name = "CctvFrame" + i };
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
        Image back = NewImage("Back", cg.transform, Color.black);
        Stretch(back.rectTransform);

        GameObject sg = new GameObject("Picture", typeof(RectTransform));
        sg.transform.SetParent(cg.transform, false);
        screen = sg.AddComponent<RawImage>();
        screen.raycastTarget = false;
        Stretch(screen.rectTransform);
        AspectRatioFitter fit = sg.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
        fit.aspectRatio = W / (float)H;
        Shader feed = Resources.Load<Shader>("LundunCctvFeed");
        if (feed != null) screen.material = new Material(feed);
        else screen.color = new Color(0.75f, 0.85f, 0.78f);

        camLabel = NewLabel("Cam", cg.transform, new Vector2(0f, 1f), TextAlignmentOptions.TopLeft, new Vector2(150f, -70f));
        clockLabel = NewLabel("Clock", cg.transform, new Vector2(1f, 1f), TextAlignmentOptions.TopRight, new Vector2(-150f, -70f));
        tagLabel = NewLabel("Tag", cg.transform, new Vector2(0f, 0f), TextAlignmentOptions.BottomLeft, new Vector2(150f, 70f));
        TextMeshProUGUI brand = NewLabel("Brand", cg.transform, new Vector2(1f, 0f), TextAlignmentOptions.BottomRight, new Vector2(-150f, 70f));
        brand.text = "LUNDUN CCTV";
        recDot = NewImage("Rec", cg.transform, new Color(1f, 0.15f, 0.12f));
        recDot.sprite = LundunHudArt.Disc;
        RectTransform rr = recDot.rectTransform;
        rr.anchorMin = rr.anchorMax = new Vector2(0f, 1f);
        rr.sizeDelta = new Vector2(30f, 30f);
        rr.anchoredPosition = new Vector2(120f, -90f);
    }

    void Labels(int cam, float hour)
    {
        camLabel.text = "CAM " + (cam + 1).ToString("0000") + "   REC";
        int secs = Mathf.FloorToInt(Mathf.Repeat(hour, 24f) * 3600f);
        System.DateTime today = System.DateTime.Now;
        clockLabel.text = today.ToString("dd-MM-yyyy") + "  " + (secs / 3600).ToString("00") + ":" + (secs / 60 % 60).ToString("00") + ":" + (secs % 60).ToString("00");
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
        t.fontSize = 38f;
        t.color = new Color(0.92f, 0.96f, 0.92f, 0.95f);
        t.alignment = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        RectTransform rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = corner;
        rt.pivot = corner;
        rt.sizeDelta = new Vector2(900f, 60f);
        rt.anchoredPosition = offset;
        return t;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
    }
}
