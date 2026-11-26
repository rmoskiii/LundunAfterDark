using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// G8.1a: share cards. A 1080×1920 portrait image (9:16, for TikTok, Reels and Stories) for three moments, made from
/// the incident log (LundunIncidents, G8.0) and never from a list of its own:
///   ESCAPED   a chase that reached 2★ or more, then escaped (lost them, by train, or resprayed)
///   WASTED    a chase that ended WASTED, or a WASTED with no chase
///   BUSTED    likewise
/// Only SETTLED incidents (LundunIncidents.Eligible): never from a failed, retried, quit or abandoned mission attempt.
/// The card: the CCTV still from G8.1b drawn through the same CCTV shader as the replays (or a branded backdrop when
/// there's no footage), the headline in the game's colours, the street and district, the stars, the chase time, the
/// game-clock time, one or two facts, and LUNDUN AFTER DARK · lundunafterdark.com.
/// Nothing is drawn until SHARE is tapped: then an offscreen canvas is rendered once into an image by its own camera
/// (far below the city, seen by no other camera), saved as a PNG in the app's temporary folder, attached to the
/// incident as "card" (so sharing again reuses it; G8.0 deletes the file on eviction), and everything else is freed.
/// iPhone: the share sheet (Plugins/iOS/LundunShare.mm) with "lundunafterdark.com #LundunAfterDark". Editor: the PNG is
/// also saved to <persistentDataPath>/ShareCards and shown in Finder. Android and the browser: no SHARE (deferred).
public class LundunShareCard : MonoBehaviour
{
    public static LundunShareCard Instance { get; private set; }

    public const string ShareText = "lundunafterdark.com #LundunAfterDark";
    const int CardW = 1080, CardH = 1920;
    static readonly Color Ink = new Color(0.03f, 0.05f, 0.09f, 1f);
    static readonly Color Amber = new Color(1f, 0.66f, 0.18f);
    static readonly Color WastedRed = new Color(0.545f, 0f, 0f);         // the WASTED text's own colour
    static readonly Color BustedBlue = new Color(0.35f, 0.6f, 1f);        // BUSTED's police blue
    static readonly Color Grey = new Color(0.62f, 0.66f, 0.72f);

    /// Drawing the card (the SHARE label reads PREPARING…).
    public bool Busy { get; private set; }
    /// The share sheet is up (the game stays paused until it closes).
    public bool SheetOpen { get; private set; }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void LundunShareImage(string path, string text, float x, float y, float w, float h);
#endif

    /// SHARE is offered here: the iPhone (share sheet) and the Editor (saves the PNG). Not Android or the browser yet.
    public static bool CanShare
    {
        get
        {
            if (Application.isEditor) return true;
            return Application.platform == RuntimePlatform.IPhonePlayer;
        }
    }

    public static LundunShareCard Ensure()
    {
        if (Instance != null) return Instance;
        GameObject g = new GameObject("LundunShareCard");
        return g.AddComponent<LundunShareCard>();
    }

    void Awake() { Instance = this; }
    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (Busy || SheetOpen) { Time.timeScale = 1f; AudioListener.pause = false; }
    }

    // ------------------------------------------------------------------ which incidents get a card
    public enum Kind { None, Escaped, Wasted, Busted }

    /// The card this incident would make (None if it isn't card-worthy). Doesn't check SETTLED: see Eligible.
    public static Kind CardKind(LundunIncident r)
    {
        if (r == null || !r.closed) return Kind.None;
        if (r.kind == LundunIncidentKind.Wasted) return Kind.Wasted;
        if (r.kind == LundunIncidentKind.Busted) return Kind.Busted;
        if (r.kind != LundunIncidentKind.Chase) return Kind.None;
        switch (r.end)
        {
            case LundunIncidentEnd.Wasted: return Kind.Wasted;
            case LundunIncidentEnd.Busted: return Kind.Busted;
            case LundunIncidentEnd.Escaped:
            case LundunIncidentEnd.EscapedByTrain:
            case LundunIncidentEnd.Resprayed:
                return r.peakStars >= 2 ? Kind.Escaped : Kind.None;          // 2★ and above
            default: return Kind.None;                                         // MISSION, CLEARED, ABANDONED
        }
    }

    /// A card can be shared for this incident now: card-worthy and SETTLED (so never VOID, MERGED or HELD).
    public static bool Eligible(LundunIncident r) { return CardKind(r) != Kind.None && LundunIncidents.Eligible(r); }

    /// The newest incident in this mode with a card available (null if none): the pause menu's SHARE.
    public static string LatestId()
    {
        foreach (LundunIncident r in LundunIncidents.History) if (Eligible(r)) return r.id;
        return null;
    }

    public static string Headline(Kind k) { return k == Kind.Wasted ? "WASTED" : k == Kind.Busted ? "BUSTED" : k == Kind.Escaped ? "ESCAPED" : ""; }
    public static Color HeadlineColour(Kind k) { return k == Kind.Wasted ? WastedRed : k == Kind.Busted ? BustedBlue : Amber; }

    // ------------------------------------------------------------------ share
    /// Share this incident's card (drawn now if it hasn't been). anchor: the button tapped (iPad anchors the sheet there).
    public static void Share(string id, RectTransform anchor) { Ensure().StartShare(id, anchor); }

    void StartShare(string id, RectTransform anchor)
    {
        if (Busy || SheetOpen || !CanShare) return;
        LundunIncident r = LundunIncidents.Get(id);
        if (!Eligible(r)) return;                                              // VOID / HELD / MERGED / not card-worthy: never
        StartCoroutine(ShareRoutine(r, anchor));
    }

    IEnumerator ShareRoutine(LundunIncident r, RectTransform anchor)
    {
        Busy = true;
        LundunIncidents.Pin(r.id);
        // the game pauses while the card is made and the sheet is up (the pause menu has already paused it)
        bool pausedHere = Time.timeScale > 0f;
        float before = Time.timeScale > 0f ? Time.timeScale : 1f;
        if (pausedHere) { Time.timeScale = 0f; AudioListener.pause = true; }
        string path = null;
        object o;
        if (LundunIncidents.TryGetSettled(r.id, "card", out o)) { path = o as string; if (path != null && !File.Exists(path)) path = null; }
        if (path == null)
        {
            string made = null;
            yield return Draw(r, p => made = p);
            path = made;
            if (path != null)
            {
                string file = path;
                if (!LundunIncidents.Attach(r.id, "card", file, () => { try { if (File.Exists(file)) File.Delete(file); } catch { } }))
                    path = null;                                               // the record went away meanwhile: nothing to share
            }
        }
        Busy = false;
        if (path != null)
        {
            SheetOpen = Application.platform == RuntimePlatform.IPhonePlayer;   // the sheet says when it closes
            Send(path, anchor);
            float giveUp = Time.unscaledTime + 600f;
            while (SheetOpen && Time.unscaledTime < giveUp) yield return null;
            SheetOpen = false;
        }
        LundunIncidents.Unpin(r.id);
        if (pausedHere && (LundunSettings.Instance == null || !LundunSettings.Instance.IsOpen)) { Time.timeScale = before; AudioListener.pause = false; }
    }

    /// From the iPhone share sheet (UnitySendMessage) when it closes, shared or not.
    void OnShareClosed(string result) { SheetOpen = false; }

    void OnApplicationPause(bool paused)
    {
        // coming back from another app (e.g. TikTok) with the sheet long gone: don't wait any longer
        if (!paused && SheetOpen && Application.platform == RuntimePlatform.IPhonePlayer) StartCoroutine(ReleaseSoon());
    }

    IEnumerator ReleaseSoon() { yield return new WaitForSecondsRealtime(1.5f); SheetOpen = false; }

    void Send(string path, RectTransform anchor)
    {
#if UNITY_EDITOR
        try
        {
            string dir = Path.Combine(Application.persistentDataPath, "ShareCards");
            Directory.CreateDirectory(dir);
            string copy = Path.Combine(dir, Path.GetFileName(path));
            File.Copy(path, copy, true);
            Debug.Log("[Share] card saved: " + copy + "   (with the text: " + ShareText + ")");
            UnityEditor.EditorUtility.RevealInFinder(copy);
        }
        catch (Exception e) { Debug.LogException(e); }
#elif UNITY_IOS
        Rect n = Normalised(anchor);
        try { LundunShareImage(path, ShareText, n.x, n.y, n.width, n.height); } catch (Exception e) { Debug.LogException(e); }
#endif
    }

    /// The button's rectangle as 0..1 of the screen, top-left origin (UIKit's way up), for the iPad's popover.
    static Rect Normalised(RectTransform r)
    {
        if (r == null || Screen.width <= 0 || Screen.height <= 0) return new Rect(0.45f, 0.45f, 0.1f, 0.1f);
        Vector3[] c = new Vector3[4];
        r.GetWorldCorners(c);                                                  // overlay canvases: world = screen pixels
        float x0 = Mathf.Min(c[0].x, c[2].x), x1 = Mathf.Max(c[0].x, c[2].x);
        float y0 = Mathf.Min(c[0].y, c[2].y), y1 = Mathf.Max(c[0].y, c[2].y);
        return new Rect(x0 / Screen.width, 1f - y1 / Screen.height, (x1 - x0) / Screen.width, (y1 - y0) / Screen.height);
    }

    // ------------------------------------------------------------------ drawing the card
    IEnumerator Draw(LundunIncident r, Action<string> done)
    {
        Kind kind = CardKind(r);
        GameObject root = null;
        RenderTexture rt = null;
        Texture2D tex = null;
        string path = null;
        Material cctvMat = null;
        Exception failed = null;
        Camera cam = null;
        try
        {
            root = new GameObject("ShareCardRig");
            root.transform.position = new Vector3(0f, -10000f, 0f);           // far below the city: no game camera sees it
            int uiLayer = LayerMask.NameToLayer("UI"); if (uiLayer < 0) uiLayer = 5;

            cam = new GameObject("ShareCardCamera").AddComponent<Camera>();
            cam.transform.SetParent(root.transform, false);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Ink;
            cam.cullingMask = 1 << uiLayer;
            cam.orthographic = true;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 50f;
            cam.allowHDR = false; cam.allowMSAA = false;
            cam.depth = -50f;
            rt = new RenderTexture(CardW, CardH, 24, RenderTextureFormat.ARGB32) { name = "ShareCard" };
            rt.Create();
            cam.targetTexture = rt;

            GameObject cg = new GameObject("ShareCardCanvas");
            cg.layer = uiLayer;
            cg.transform.SetParent(root.transform, false);
            Canvas canvas = cg.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 10f;
            CanvasScaler sc = cg.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; sc.scaleFactor = 1f;
            Layout((RectTransform)cg.transform, r, kind, uiLayer, out cctvMat);
            cam.enabled = true;
        }
        catch (Exception e) { failed = e; }

        if (failed == null) yield return new WaitForEndOfFrame();                 // the card camera has drawn it

        if (failed == null)
        {
            RenderTexture was = RenderTexture.active;
            try
            {
                RenderTexture.active = rt;
                tex = new Texture2D(CardW, CardH, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, CardW, CardH), 0, 0);
                tex.Apply();
                path = Path.Combine(Application.temporaryCachePath, "lad_card_" + r.id + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception e) { failed = e; path = null; }
            finally { RenderTexture.active = was; }
        }

        if (cam != null) { cam.enabled = false; cam.targetTexture = null; }
        if (tex != null) Destroy(tex);
        if (rt != null) { rt.Release(); Destroy(rt); }
        if (cctvMat != null) Destroy(cctvMat);
        if (root != null) Destroy(root);
        if (failed != null) Debug.LogException(failed);
        done(path);
    }

    void Layout(RectTransform canvas, LundunIncident r, Kind kind, int layer, out Material cctvMat)
    {
        cctvMat = null;
        TMP_FontAsset font = LundunHud.Instance != null ? LundunHud.Instance.FontAsset : null;

        Img(canvas, "Back", Ink, 0f, 0f, CardW, CardH, layer, true);

        // brand along the top
        Txt(canvas, "LUNDUN AFTER DARK", 40f, Amber, 0f, -96f, CardW, 60f, font, layer, 22f, FontStyles.Bold);
        Img(canvas, "TopRule", new Color(Amber.r, Amber.g, Amber.b, 0.6f), 0f, -170f, 880f, 4f, layer, false);

        // the CCTV frame (16:9), or the branded backdrop
        const float fw = 1000f, fh = 562.5f, fy = -230f;
        object o; LundunCctvStill still = null;
        if (LundunIncidents.TryGetSettled(r.id, "still", out o)) still = o as LundunCctvStill;
        Texture shot = still != null ? still.Texture : null;
        Img(canvas, "FrameEdge", new Color(1f, 1f, 1f, 0.18f), 0f, fy + 6f, fw + 12f, fh + 12f, layer, false);
        if (shot != null)
        {
            RawImage ri = NewRaw(canvas, "Footage", 0f, fy, fw, fh, layer);
            ri.texture = shot;
            Shader feed = Resources.Load<Shader>("LundunCctvFeed");
            if (feed != null) { cctvMat = new Material(feed); ri.material = cctvMat; }
            else ri.color = new Color(0.8f, 0.82f, 0.85f);
            string camText = "CAM " + (still.Camera + 1).ToString("0000");
            Txt(canvas, camText, 30f, Color.white, -fw * 0.5f + 40f + 180f, fy - 26f, 360f, 44f, font, layer, 4f, FontStyles.Normal, TextAlignmentOptions.Left);
            Txt(canvas, Clock(still.Hour) + "   REC", 30f, Color.white, fw * 0.5f - 40f - 180f, fy - 26f, 360f, 44f, font, layer, 4f, FontStyles.Normal, TextAlignmentOptions.Right);
            Txt(canvas, "<< PLAYBACK", 26f, new Color(1f, 1f, 1f, 0.85f), -fw * 0.5f + 40f + 180f, fy - fh + 60f, 360f, 40f, font, layer, 4f, FontStyles.Normal, TextAlignmentOptions.Left);
        }
        else
        {
            Img(canvas, "Backdrop", new Color(0.07f, 0.09f, 0.14f, 1f), 0f, fy, fw, fh, layer, false);
            Img(canvas, "Fog", new Color(Amber.r, Amber.g, Amber.b, 0.07f), 0f, fy - fh * 0.55f, fw, fh * 0.45f, layer, false);
            Txt(canvas, "NO FOOTAGE", 44f, new Color(1f, 1f, 1f, 0.35f), 0f, fy - fh * 0.5f + 34f, fw, 60f, font, layer, 20f, FontStyles.Bold);
            Txt(canvas, "OFF CAMERA", 30f, new Color(Amber.r, Amber.g, Amber.b, 0.6f), 0f, fy - fh * 0.5f - 30f, fw, 44f, font, layer, 16f, FontStyles.Normal);
        }

        // the headline, the stars
        Txt(canvas, Headline(kind), 190f, HeadlineColour(kind), 0f, -880f, CardW, 220f, font, layer, 10f, FontStyles.Bold);
        int stars = Mathf.Clamp(r.peakStars, 0, 5);
        if (stars > 0)
        {
            const float s = 76f, gap = 18f;
            float total = stars * s + (stars - 1) * gap;
            for (int i = 0; i < stars; i++)
            {
                Image st = Img(canvas, "Star" + i, Amber, -total * 0.5f + s * 0.5f + i * (s + gap), -1110f, s, s, layer, false);
                st.sprite = LundunHudArt.Star;
            }
        }

        // where, how long, when
        string street = !string.IsNullOrEmpty(r.endStreet) ? r.endStreet : r.startStreet;
        string district = !string.IsNullOrEmpty(r.endDistrict) ? r.endDistrict : r.startDistrict;
        float placeY = stars > 0 ? -1210f : -1130f;
        if (!string.IsNullOrEmpty(street))
        {
            Txt(canvas, street, 72f, Color.white, 0f, placeY, CardW - 80f, 90f, font, layer, 2f, FontStyles.Bold);
            Txt(canvas, (district ?? "").ToUpperInvariant(), 40f, Grey, 0f, placeY - 96f, CardW - 80f, 56f, font, layer, 14f, FontStyles.Normal);
        }
        else Txt(canvas, (district ?? "LUNDUN").ToUpperInvariant(), 72f, Color.white, 0f, placeY, CardW - 80f, 90f, font, layer, 8f, FontStyles.Bold);

        string when = r.startHour >= 0f ? Clock(r.startHour) : "";                // the game-clock time it began
        string length = r.kind == LundunIncidentKind.Chase ? "CHASE " + Duration(r.Duration) : "";
        string line = length + (length.Length > 0 && when.Length > 0 ? "   ·   " : "") + when;
        Txt(canvas, line, 46f, Color.white, 0f, placeY - 190f, CardW - 80f, 64f, font, layer, 6f, FontStyles.Normal);
        string facts = Facts(r);
        if (facts.Length > 0) Txt(canvas, facts, 36f, Amber, 0f, placeY - 262f, CardW - 80f, 100f, font, layer, 6f, FontStyles.Normal);

        // the footer
        Img(canvas, "BottomRule", new Color(Amber.r, Amber.g, Amber.b, 0.6f), 0f, -1712f, 880f, 4f, layer, false);
        Txt(canvas, "LUNDUN AFTER DARK", 64f, Color.white, 0f, -1745f, CardW, 84f, font, layer, 16f, FontStyles.Bold);
        Txt(canvas, "lundunafterdark.com", 40f, Amber, 0f, -1830f, CardW, 56f, font, layer, 4f, FontStyles.Normal);
    }

    /// One or two of the most telling facts.
    static string Facts(LundunIncident r)
    {
        System.Collections.Generic.List<string> f = new System.Collections.Generic.List<string>();
        if (r.end == LundunIncidentEnd.EscapedByTrain) f.Add("ESCAPED BY TRAIN");
        if (r.end == LundunIncidentEnd.Resprayed) f.Add("RESPRAYED");
        if (r.policeCars > 0) f.Add(r.policeCars + " POLICE CAR" + (r.policeCars > 1 ? "S" : "") + " DESTROYED");
        if (f.Count < 2 && r.officersKilled > 0) f.Add(r.officersKilled + " OFFICER" + (r.officersKilled > 1 ? "S" : "") + " DOWN");
        if (f.Count < 2 && r.crashes > 1) f.Add(r.crashes + " CRASHES");
        if (f.Count < 2 && r.onCctv) f.Add(r.identified ? "IDENTIFIED ON CCTV" : "FACE NOT CLEAR ON CCTV");
        if (f.Count > 2) f.RemoveRange(2, f.Count - 2);
        return string.Join("   ·   ", f.ToArray());
    }

    static string Clock(float hour)
    {
        hour = ((hour % 24f) + 24f) % 24f;
        int h = Mathf.FloorToInt(hour), m = Mathf.FloorToInt((hour - h) * 60f);
        return h.ToString("00") + ":" + m.ToString("00");
    }

    static string Duration(float s) { int t = Mathf.Max(0, Mathf.RoundToInt(s)); return (t / 60) + ":" + (t % 60).ToString("00"); }

    // ------------------------------------------------------------------ small builders (top-centre anchored)
    static RectTransform Node(RectTransform parent, string name, float x, float y, float w, float h, int layer)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.layer = layer;
        g.transform.SetParent(parent, false);
        RectTransform t = (RectTransform)g.transform;
        t.anchorMin = t.anchorMax = new Vector2(0.5f, 1f); t.pivot = new Vector2(0.5f, 1f);
        t.anchoredPosition = new Vector2(x, y); t.sizeDelta = new Vector2(w, h);
        return t;
    }

    static Image Img(RectTransform parent, string name, Color c, float x, float y, float w, float h, int layer, bool stretch)
    {
        RectTransform t = Node(parent, name, x, y, w, h, layer);
        if (stretch) { t.anchorMin = Vector2.zero; t.anchorMax = Vector2.one; t.offsetMin = t.offsetMax = Vector2.zero; }
        Image i = t.gameObject.AddComponent<Image>(); i.color = c; i.raycastTarget = false;
        return i;
    }

    static RawImage NewRaw(RectTransform parent, string name, float x, float y, float w, float h, int layer)
    {
        RectTransform t = Node(parent, name, x, y, w, h, layer);
        RawImage i = t.gameObject.AddComponent<RawImage>(); i.raycastTarget = false;
        return i;
    }

    static TextMeshProUGUI Txt(RectTransform parent, string text, float size, Color c, float x, float y, float w, float h,
                               TMP_FontAsset font, int layer, float spacing, FontStyles style,
                               TextAlignmentOptions align = TextAlignmentOptions.Center)
    {
        RectTransform t = Node(parent, "Text", x, y, w, h, layer);
        TextMeshProUGUI m = t.gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) m.font = font;
        m.text = text; m.fontSize = size; m.color = c; m.characterSpacing = spacing; m.fontStyle = style;
        m.alignment = align; m.raycastTarget = false;
        m.textWrappingMode = TextWrappingModes.Normal;
        return m;
    }
}
