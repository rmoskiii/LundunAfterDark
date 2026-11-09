using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// Phone calls (Story only; added by LundunStoryUI). Any line that starts "(phone)" rings James's phone: a handset
/// slides up on the right (an original design: the caller's initial in their colour, their name, MOBILE, signal bars,
/// the call timer, a waveform while they talk), it rings twice (with a buzz), picks itself up with a click, and the line
/// shows as the subtitle (without the tag) while the caller's voice murmurs down the line: each character has their
/// own pitch and pace of syllables (made in code, no audio files), under a soft line hum. The rest of the game's
/// sound dips while the call's on. Phone lines one after another are one call; James's replies stay in it. When
/// nobody's said anything for a moment: the hang-up beeps and the phone slides away. You keep playing throughout.
public class LundunPhone : MonoBehaviour
{
    public static LundunPhone Instance { get; private set; }
    public const float RingTime = 2.1f;
    const float Rate = 22050f, Dip = 0.55f;

    struct Voice { public float pitch, pace, spread, breath; public Voice(float p, float s, float sp, float b) { pitch = p; pace = s; spread = sp; breath = b; } }
    static readonly Dictionary<string, Voice> Voices = new Dictionary<string, Voice>
    {
        { "UNCLE FEMI", new Voice(108f, 0.17f, 0.10f, 0.3f) },   // low and slow
        { "KEMI",       new Voice(235f, 0.095f, 0.18f, 0.15f) }, // bright and quick
        { "MARCUS",     new Voice(96f, 0.11f, 0.07f, 0.25f) },   // deep and clipped
        { "TAYO",       new Voice(170f, 0.085f, 0.2f, 0.2f) },   // fast and jumpy
        { "LEAH",       new Voice(205f, 0.13f, 0.1f, 0.18f) },   // measured
        { "HOLLIS",     new Voice(112f, 0.14f, 0.06f, 0.35f) },  // smooth and low
        { "ARMS DEALER", new Voice(90f, 0.15f, 0.05f, 0.4f) },  // v50: gravelly, unhurried
        { "JAMES",      new Voice(138f, 0.115f, 0.12f, 0.2f) },
    };

    private string caller;
    private bool inCall, picked;
    private float callStart, ringStart, lineEnds, slide;
    private AudioSource voice, line, fx;
    private float listenerWas = 1f;
    private bool dipped;

    // the handset
    private RectTransform handset;
    private Image avatar;
    private TextMeshProUGUI initial, nameText, stateText, clockText;
    private readonly List<RectTransform> bars = new List<RectTransform>();
    private static AudioClip ringClip, clickClip, hangClip, humClip;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) { Instance = null; } if (dipped) AudioListener.volume = listenerWas; }

    void Start()
    {
        voice = Source(); line = Source(); fx = Source();
        line.loop = true; line.clip = Hum(); line.volume = 0f;
        BuildUI();
    }

    AudioSource Source()
    {
        AudioSource a = gameObject.AddComponent<AudioSource>();
        a.playOnAwake = false; a.spatialBlend = 0f; a.ignoreListenerVolume = true; a.ignoreListenerPause = false;
        return a;
    }

    // ------------------------------------------------------------------ from LundunMissions.Say
    /// A line's being said. Returns how long to wait before showing it (the ring, when a call starts).
    public static float Line(string who, string text, bool phone, float seconds)
    {
        LundunPhone p = Instance;
        if (p == null || p.handset == null) return 0f;
        if (!p.inCall)
        {
            if (!phone) return 0f;
            p.Ring(who == "JAMES" ? null : who);
            p.QueueVoice(who, text, seconds, RingTime);
            return RingTime;
        }
        if (phone || who == "JAMES" || who == p.caller)
        {
            if (p.caller == null && who != "JAMES") p.SetCaller(who);
            p.QueueVoice(who, text, seconds, 0f);
            return 0f;
        }
        p.HangUp();                                                          // someone's talking face to face now
        return 0f;
    }

    /// The subtitle's gone (or skipped): stop murmuring.
    public static void Quiet()
    {
        LundunPhone p = Instance;
        if (p == null || p.voice == null) return;
        p.voice.Stop();
        p.pendingAt = 0f;
        p.lineEnds = Mathf.Min(p.lineEnds, Time.time);
    }

    private AudioClip pendingClip;
    private float pendingAt;

    void QueueVoice(string who, string text, float seconds, float after)
    {
        Voice v; if (!Voices.TryGetValue(who ?? "", out v)) v = new Voice(150f, 0.12f, 0.12f, 0.2f);
        float speak = Mathf.Min(seconds * 0.85f, 0.6f + (text ?? "").Length * 0.055f);
        pendingClip = Babble(v, speak, (text ?? "").GetHashCode());
        pendingAt = Time.time + after;
        lineEnds = Time.time + after + seconds;
        if (after <= 0f) PlayPending();
    }

    void PlayPending()
    {
        if (pendingClip == null || voice == null) return;
        voice.Stop();
        voice.clip = pendingClip; voice.volume = 0.8f;
        voice.Play();
        pendingAt = 0f;
    }

    // ------------------------------------------------------------------ the call
    void Ring(string who)
    {
        inCall = true; picked = false;
        ringStart = Time.time;
        SetCaller(who);
        stateText.text = "INCOMING CALL";
        clockText.text = Clock();
        fx.Stop(); fx.clip = RingClip(); fx.volume = 0.6f; fx.Play();
        LundunHaptics.Light();
        if (!dipped) { listenerWas = AudioListener.volume; AudioListener.volume = listenerWas * Dip; dipped = true; }
    }

    void SetCaller(string who)
    {
        caller = who;
        Color c = who != null ? LundunMissions.ColourOf(who) : new Color(0.6f, 0.6f, 0.65f);
        avatar.color = c;
        initial.text = who != null ? who.Substring(who.StartsWith("UNCLE ") ? 6 : 0, 1) : "?";
        nameText.text = who != null ? Title(who) : "Unknown";
    }

    void PickUp()
    {
        picked = true;
        callStart = Time.time;
        fx.Stop(); fx.PlayOneShot(ClickClip(), 0.7f);
        line.volume = 0.05f; if (!line.isPlaying) line.Play();
        LundunHaptics.Light();
    }

    void HangUp()
    {
        if (!inCall) return;
        inCall = false; picked = false; caller = null;
        voice.Stop(); pendingAt = 0f; pendingClip = null;
        line.Stop(); line.volume = 0f;
        fx.Stop(); fx.PlayOneShot(HangClip(), 0.5f);
        stateText.text = "CALL ENDED";
        if (dipped) { AudioListener.volume = listenerWas; dipped = false; }
    }

    void Update()
    {
        if (handset == null) return;
        float dt = Time.unscaledDeltaTime;
        if (inCall)
        {
            if (!picked)
            {
                if (Time.time - ringStart > 1.05f && Time.time - ringStart < 1.1f) LundunHaptics.Light();
                if (Time.time - ringStart >= RingTime) PickUp();
            }
            if (pendingAt > 0f && Time.time >= pendingAt) PlayPending();
            if (picked)
            {
                int s = Mathf.FloorToInt(Time.time - callStart);
                stateText.text = (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
                // hang up once nobody's said anything for a moment
                if (!voice.isPlaying && pendingAt <= 0f && Time.time > lineEnds + 1.1f && !LundunMissions.Talking) HangUp();
            }
            if (Time.timeScale == 0f && voice.isPlaying) voice.Pause(); else if (Time.timeScale > 0f && picked && voice.clip != null && !voice.isPlaying && voice.time > 0f && voice.time < voice.clip.length) voice.UnPause();
        }
        // slide in while there's a call (and a moment after it ends), out otherwise
        bool show = inCall || Time.time - lineEnds < 1.6f && slide > 0.01f;
        slide = Mathf.MoveTowards(slide, show ? 1f : 0f, dt * 3.2f);
        float e = slide * slide * (3f - 2f * slide);
        handset.anchoredPosition = new Vector2(Mathf.Lerp(360f, -36f, e), -40f);
        handset.gameObject.SetActive(slide > 0.001f);
        // the waveform: moves while someone's talking
        bool talking = picked && voice.isPlaying;
        for (int i = 0; i < bars.Count; i++)
        {
            float h = talking ? 8f + 34f * Mathf.Abs(Mathf.Sin(Time.time * (7f + i * 1.3f) + i)) * Mathf.PerlinNoise(Time.time * 4f, i) : 4f;
            bars[i].sizeDelta = new Vector2(8f, Mathf.Lerp(bars[i].sizeDelta.y, h, dt * 18f));
        }
    }

    static string Title(string who)
    {
        string[] w = who.ToLowerInvariant().Split(' ');
        for (int i = 0; i < w.Length; i++) if (w[i].Length > 0) w[i] = char.ToUpperInvariant(w[i][0]) + w[i].Substring(1);
        return string.Join(" ", w);
    }

    static string Clock()
    {
        LundunClock c = LundunClock.Instance;
        if (c == null) return "";
        int h = Mathf.FloorToInt(c.Hour) % 24, m = Mathf.FloorToInt((c.Hour - Mathf.Floor(c.Hour)) * 60f);
        return h.ToString("00") + ":" + m.ToString("00");
    }

    // ------------------------------------------------------------------ sounds (made in code)
    /// Murmured syllables in a voice: a buzzy voiced tone with a vowel-ish formant, its pitch wandering, little
    /// consonant clicks between, word gaps; then made to sound like a phone line (thin and a bit crunchy).
    static AudioClip Babble(Voice v, float seconds, int seed)
    {
        System.Random r = new System.Random(seed);
        int n = Mathf.Max(1, Mathf.RoundToInt(seconds * Rate));
        float[] d = new float[n];
        int at = 0;
        float phase = 0f, fphase = 0f, lp = 0f, hp = 0f, prev = 0f;
        while (at < n)
        {
            float syl = v.pace * (0.7f + (float)r.NextDouble() * 0.7f);
            int len = Mathf.Min(n - at, Mathf.RoundToInt(syl * Rate));
            float f0 = v.pitch * (1f + ((float)r.NextDouble() * 2f - 1f) * v.spread);
            float f1 = f0 * (1f + ((float)r.NextDouble() * 2f - 1f) * v.spread * 0.6f);
            float formant = 500f + (float)r.NextDouble() * 1400f;
            for (int i = 0; i < len; i++)
            {
                float t = (float)i / len;
                float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t * 1.15f)) * (t < 0.08f ? t / 0.08f : 1f);
                float f = Mathf.Lerp(f0, f1, t);
                phase += 2f * Mathf.PI * f / Rate;
                if (phase > 2f * Mathf.PI) phase -= 2f * Mathf.PI;
                // a buzzy source (a few harmonics), coloured by the formant
                float src = Mathf.Sin(phase) + 0.6f * Mathf.Sin(phase * 2f) + 0.35f * Mathf.Sin(phase * 3f) + 0.2f * Mathf.Sin(phase * 4f);
                fphase += 2f * Mathf.PI * formant / Rate;
                if (fphase > 2f * Mathf.PI) fphase -= 2f * Mathf.PI;
                float form = 0.6f + 0.4f * Mathf.Sin(fphase);
                float s = src * form * env * 0.32f;
                if (t < 0.12f && r.NextDouble() < 0.5) s += ((float)r.NextDouble() * 2f - 1f) * 0.18f * (1f - t / 0.12f);   // a consonant
                s += ((float)r.NextDouble() * 2f - 1f) * v.breath * 0.03f * env;
                d[at + i] = s;
            }
            at += len;
            // gaps between words
            if (r.NextDouble() < 0.3) at += Mathf.RoundToInt((0.05f + (float)r.NextDouble() * 0.12f) * Rate);
        }
        // the phone line: thin it (high-pass), soften the top (low-pass), a little crunch
        for (int i = 0; i < n; i++)
        {
            float x = d[i];
            hp = 0.94f * (hp + x - prev); prev = x;
            lp += (hp - lp) * 0.45f;
            d[i] = Mathf.Clamp(lp * 1.6f, -0.9f, 0.9f);
        }
        AudioClip c = AudioClip.Create("Babble", n, 1, (int)Rate, false);
        c.SetData(d, 0);
        return c;
    }

    /// The ring: a British double ring, twice.
    static AudioClip RingClip()
    {
        if (ringClip != null) return ringClip;
        int n = Mathf.RoundToInt(RingTime * Rate);
        float[] d = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / Rate, c = t % 1.05f;
            bool on = c < 0.32f || (c > 0.5f && c < 0.82f);
            float env = on ? 1f : 0f;
            d[i] = env * 0.25f * (Mathf.Sin(2f * Mathf.PI * 400f * t) + Mathf.Sin(2f * Mathf.PI * 450f * t));
        }
        ringClip = AudioClip.Create("Ring", n, 1, (int)Rate, false); ringClip.SetData(d, 0);
        return ringClip;
    }

    static AudioClip ClickClip()
    {
        if (clickClip != null) return clickClip;
        int n = Mathf.RoundToInt(0.06f * Rate);
        float[] d = new float[n];
        System.Random r = new System.Random(7);
        for (int i = 0; i < n; i++) d[i] = ((float)r.NextDouble() * 2f - 1f) * (1f - (float)i / n) * 0.5f;
        clickClip = AudioClip.Create("Click", n, 1, (int)Rate, false); clickClip.SetData(d, 0);
        return clickClip;
    }

    /// Hanging up: three short falling beeps.
    static AudioClip HangClip()
    {
        if (hangClip != null) return hangClip;
        int n = Mathf.RoundToInt(0.75f * Rate);
        float[] d = new float[n];
        float[] f = { 900f, 700f, 520f };
        for (int i = 0; i < n; i++)
        {
            float t = i / Rate; int k = Mathf.Min(2, Mathf.FloorToInt(t / 0.25f)); float c = t - k * 0.25f;
            d[i] = c < 0.16f ? 0.3f * Mathf.Sin(2f * Mathf.PI * f[k] * t) * (1f - c / 0.16f) : 0f;
        }
        hangClip = AudioClip.Create("HangUp", n, 1, (int)Rate, false); hangClip.SetData(d, 0);
        return hangClip;
    }

    /// The line: a soft hiss with a faint low hum, looping.
    static AudioClip Hum()
    {
        if (humClip != null) return humClip;
        int n = Mathf.RoundToInt(2f * Rate);
        float[] d = new float[n];
        System.Random r = new System.Random(11);
        float lp = 0f;
        for (int i = 0; i < n; i++)
        {
            lp += (((float)r.NextDouble() * 2f - 1f) - lp) * 0.2f;
            d[i] = lp * 0.5f + 0.25f * Mathf.Sin(2f * Mathf.PI * 50f * i / Rate);
        }
        humClip = AudioClip.Create("LineHum", n, 1, (int)Rate, false); humClip.SetData(d, 0);
        return humClip;
    }

    // ------------------------------------------------------------------ the handset (right side, touch-through)
    void BuildUI()
    {
        GameObject cg = new GameObject("PhoneUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas cv = cg.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 56;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        RectTransform root = (RectTransform)cg.transform;

        handset = Rect("Handset", root);
        handset.anchorMin = handset.anchorMax = new Vector2(1f, 0.5f); handset.pivot = new Vector2(1f, 0.5f);
        handset.sizeDelta = new Vector2(260f, 470f); handset.anchoredPosition = new Vector2(360f, -40f);
        Img(handset, new Color(0.06f, 0.06f, 0.07f, 0.97f), 2.4f);                                  // the body
        RectTransform screen = Rect("Screen", handset);
        screen.anchorMin = Vector2.zero; screen.anchorMax = Vector2.one; screen.offsetMin = new Vector2(12f, 14f); screen.offsetMax = new Vector2(-12f, -14f);
        Img(screen, new Color(0.11f, 0.13f, 0.17f, 1f), 2.8f);
        RectTransform notch = Rect("Notch", handset);
        notch.anchorMin = notch.anchorMax = new Vector2(0.5f, 1f); notch.pivot = new Vector2(0.5f, 1f);
        notch.anchoredPosition = new Vector2(0f, -18f); notch.sizeDelta = new Vector2(70f, 14f);
        Img(notch, new Color(0.03f, 0.03f, 0.035f, 1f), 1f);

        // status bar: the time, signal bars
        clockText = Text(screen, "", 18f, new Color(1f, 1f, 1f, 0.8f), new Vector2(0f, 1f), new Vector2(18f, -12f), new Vector2(90f, 24f), TextAlignmentOptions.MidlineLeft);
        for (int i = 0; i < 4; i++)
        {
            RectTransform b = Rect("Signal", screen);
            b.anchorMin = b.anchorMax = new Vector2(1f, 1f); b.pivot = new Vector2(0.5f, 0f);
            b.anchoredPosition = new Vector2(-48f + i * 8f, -32f); b.sizeDelta = new Vector2(5f, 5f + i * 4f);
            b.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.8f);
        }
        // the caller
        RectTransform av = Rect("Avatar", screen);
        av.anchorMin = av.anchorMax = new Vector2(0.5f, 1f); av.pivot = new Vector2(0.5f, 1f);
        av.anchoredPosition = new Vector2(0f, -70f); av.sizeDelta = new Vector2(110f, 110f);
        avatar = av.gameObject.AddComponent<Image>(); avatar.sprite = LundunHudArt.Disc; avatar.raycastTarget = false;
        initial = Text(av, "", 58f, new Color(0.05f, 0.05f, 0.06f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(110f, 110f), TextAlignmentOptions.Center);
        nameText = Text(screen, "", 28f, Color.white, new Vector2(0.5f, 1f), new Vector2(0f, -196f), new Vector2(230f, 36f), TextAlignmentOptions.Center);
        Text(screen, "MOBILE", 16f, new Color(1f, 1f, 1f, 0.5f), new Vector2(0.5f, 1f), new Vector2(0f, -228f), new Vector2(230f, 22f), TextAlignmentOptions.Center).characterSpacing = 6f;
        stateText = Text(screen, "", 22f, new Color(0.55f, 0.9f, 0.6f), new Vector2(0.5f, 1f), new Vector2(0f, -258f), new Vector2(230f, 28f), TextAlignmentOptions.Center);
        // the waveform
        for (int i = 0; i < 9; i++)
        {
            RectTransform b = Rect("Wave", screen);
            b.anchorMin = b.anchorMax = new Vector2(0.5f, 0f); b.pivot = new Vector2(0.5f, 0.5f);
            b.anchoredPosition = new Vector2(-64f + i * 16f, 130f); b.sizeDelta = new Vector2(8f, 4f);
            Image im = b.gameObject.AddComponent<Image>(); im.color = new Color(1f, 1f, 1f, 0.7f); im.raycastTarget = false;
            bars.Add(b);
        }
        // the red end-call button (just for show: calls hang up by themselves)
        RectTransform end = Rect("End", screen);
        end.anchorMin = end.anchorMax = new Vector2(0.5f, 0f); end.pivot = new Vector2(0.5f, 0f);
        end.anchoredPosition = new Vector2(0f, 28f); end.sizeDelta = new Vector2(64f, 64f);
        Image ei = end.gameObject.AddComponent<Image>(); ei.sprite = LundunHudArt.Disc; ei.color = new Color(0.9f, 0.2f, 0.18f); ei.raycastTarget = false;
        RectTransform hs = Rect("Handpiece", end);
        hs.anchorMin = hs.anchorMax = new Vector2(0.5f, 0.5f); hs.sizeDelta = new Vector2(30f, 9f); hs.localRotation = Quaternion.Euler(0f, 0f, -20f);
        hs.gameObject.AddComponent<Image>().color = Color.white;
        foreach (Graphic g in handset.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;   // touches go through to the game
        handset.gameObject.SetActive(false);
    }

    static RectTransform Rect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static Image Img(RectTransform r, Color c, float ppu)
    {
        Image i = r.gameObject.AddComponent<Image>();
        if (LundunHudArt.Pill != null) { i.sprite = LundunHudArt.Pill; i.type = Image.Type.Sliced; i.pixelsPerUnitMultiplier = ppu; }
        i.color = c; i.raycastTarget = false;
        return i;
    }

    static TextMeshProUGUI Text(RectTransform parent, string s, float size, Color c, Vector2 anchor, Vector2 pos, Vector2 box, TextAlignmentOptions align)
    {
        RectTransform r = Rect("Text", parent);
        r.anchorMin = r.anchorMax = anchor; r.pivot = new Vector2(anchor.x == 0f ? 0f : anchor.x == 1f ? 1f : 0.5f, anchor.y == 1f ? 1f : anchor.y == 0f ? 0f : 0.5f);
        r.anchoredPosition = pos; r.sizeDelta = box;
        TextMeshProUGUI t = r.gameObject.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = s; t.fontSize = size; t.color = c; t.alignment = align; t.fontStyle = FontStyles.Bold; t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
        return t;
    }
}
