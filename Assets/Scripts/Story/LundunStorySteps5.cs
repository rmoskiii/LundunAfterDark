using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// S5: a step that only happens if something's true when it's reached (what James said, a choice): skipped otherwise.
public class LundunIf : LundunStep
{
    readonly Func<bool> when;
    readonly LundunStep inner;
    bool on;
    public LundunIf(Func<bool> condition, LundunStep step) { when = condition; inner = step; checkpoint = step.checkpoint; }
    public LundunStep Inner { get { return inner; } }

    public override void Begin(LundunMissionRun run)
    {
        on = when();
        if (on) { inner.Begin(run); Copy(); }
        else { hasMarker = false; objective = ""; }
    }
    public override bool Tick(LundunMissionRun run)
    {
        if (!on) return true;
        bool done = inner.Tick(run);
        Copy();
        return done;
    }
    public override void End(LundunMissionRun run) { if (on) inner.End(run); }

    void Copy()
    {
        objective = inner.objective; hasMarker = inner.hasMarker; marker = inner.marker; markerRadius = inner.markerRadius; route = inner.route;
    }
}

/// S5: something done once, at this point only (not replayed on a retry, unlike LundunDo).
public class LundunNow : LundunStep
{
    readonly Action<LundunMissionRun> act;
    public LundunNow(Action<LundunMissionRun> what) { act = what; }
    public override bool Tick(LundunMissionRun run) { if (act != null) act(run); return true; }
}

/// S5: the tone of something James can say.
public enum LundunTone { Calm, Firm, Hot }

public struct LundunReply
{
    public string text; public LundunTone tone;
    public LundunReply(LundunTone t, string say) { tone = t; text = say; }
}

/// S5: what James says back. The game pauses on what was said to him and his three answers (calm, firm, hot-headed:
/// in whatever order the mission lists them) and FIGHT. picked gets the tone, or −1 for FIGHT. His answer is then said
/// out loud.
public class LundunReplies : LundunStep
{
    public const int Fight = -1;
    readonly string who, line;
    readonly LundunReply[] replies;
    readonly Action<int> picked;
    GameObject ui; bool chosen; float before = 1f, sayUntil; string said;

    public LundunReplies(string speaker, string theyLine, LundunReply[] answers, Action<int> onPick)
    {
        who = speaker; line = theyLine; replies = answers; picked = onPick;
    }

    public override void Begin(LundunMissionRun run)
    {
        chosen = false; said = null;
        LundunMissions.Say(null, null, 0f);
        before = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        Build();
    }

    public override bool Tick(LundunMissionRun run)
    {
        if (!chosen) return false;
        if (said != null) { LundunMissions.Say("JAMES", said, 2.6f); sayUntil = Time.time + 2.6f; said = null; }
        return Time.time >= sayUntil;
    }

    public override void End(LundunMissionRun run)
    {
        if (ui != null) Object.Destroy(ui);
        if (Time.timeScale == 0f) Time.timeScale = before;
    }

    void Pick(int i)
    {
        if (chosen) return;
        chosen = true;
        if (ui != null) Object.Destroy(ui);
        Time.timeScale = before;
        LundunHaptics.Light();
        said = i >= 0 ? replies[i].text : null;
        sayUntil = Time.time + 0.4f;
        if (picked != null) picked(i >= 0 ? (int)replies[i].tone : Fight);
    }

    void Build()
    {
        ui = new GameObject("LundunReplies", typeof(RectTransform));
        Canvas cv = ui.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 80;
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        ui.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)ui.transform;
        Image dim = LundunUIBits.Box(root, "Dim", Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.5f));
        RectTransform dr = dim.rectTransform; dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one; dr.offsetMin = dr.offsetMax = Vector2.zero;
        Color c = LundunMissions.ColourOf(who);
        LundunUIBits.Label(root, "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + who + "</color>\n" + line, 38f, new Vector2(0f, 330f), new Vector2(1500f, 150f));
        LundunUIBits.Label(root, "WHAT DO YOU SAY?", 26f, new Vector2(0f, 215f), new Vector2(900f, 50f)).color = new Color(1f, 0.66f, 0.18f);
        for (int i = 0; i < replies.Length; i++)
        {
            int idx = i;
            LundunUIBits.Button(root, replies[i].text, new Vector2(0f, 110f - i * 132f), new Vector2(1240f, 112f), 32f,
                                new Color(0.1f, 0.14f, 0.26f, 0.95f), () => Pick(idx));
        }
        LundunUIBits.Button(root, "FIGHT", new Vector2(0f, 110f - replies.Length * 132f - 20f), new Vector2(420f, 100f), 36f,
                            new Color(0.62f, 0.12f, 0.1f, 0.95f), () => Pick(-1));
    }
}

/// S5: small UI pieces for steps that build their own screens.
public static class LundunUIBits
{
    public static Image Box(RectTransform parent, string name, Vector2 at, Vector2 size, Color c)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        RectTransform r = (RectTransform)g.transform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.anchoredPosition = at; r.sizeDelta = size;
        Image i = g.AddComponent<Image>(); i.color = c;
        return i;
    }

    public static TextMeshProUGUI Label(RectTransform parent, string text, float size, Vector2 at, Vector2 box)
    {
        GameObject g = new GameObject("Text", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        RectTransform r = (RectTransform)g.transform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.anchoredPosition = at; r.sizeDelta = box;
        TextMeshProUGUI t = g.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = text; t.fontSize = size; t.alignment = TextAlignmentOptions.Center; t.color = Color.white; t.fontStyle = FontStyles.Bold;
        t.raycastTarget = false; t.richText = true; t.textWrappingMode = TextWrappingModes.Normal;
        return t;
    }

    public static void Button(RectTransform parent, string text, Vector2 at, Vector2 size, float fontSize, Color c, Action click)
    {
        Image img = Box(parent, "Button", at, size, c);
        if (LundunHudArt.Pill != null) { img.sprite = LundunHudArt.Pill; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 0.67f; }
        Button btn = img.gameObject.AddComponent<Button>(); btn.targetGraphic = img; btn.onClick.AddListener(() => click());
        img.gameObject.AddComponent<LundunPressFX>();
        TextMeshProUGUI t = Label((RectTransform)img.transform, text, fontSize, Vector2.zero, size - new Vector2(60f, 10f));
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    }
}

/// S5: someone a mission made runs: away from James (Flee: caught once he's close), to a place (and out of sight
/// there if hide: down the Tube's steps), or stands where they are (Stand). Turns their walk/run on.
public class LundunRunner : MonoBehaviour
{
    public enum Mode { Stand, To, Flee }
    public Mode mode = Mode.Stand;
    public float speed = 5.8f;
    public bool Caught { get; private set; }
    public bool There { get; private set; }
    Vector3 to; bool hide; float next;
    NavMeshAgent agent; Animator anim;

    void Awake() { agent = GetComponent<NavMeshAgent>(); anim = GetComponentInChildren<Animator>(); }

    public void Stand() { mode = Mode.Stand; if (agent != null && agent.enabled && agent.isOnNavMesh) agent.isStopped = true; }
    public void RunTo(Vector3 where, bool hideThere) { mode = Mode.To; to = where; hide = hideThere; There = false; next = 0f; }
    public void Flee() { mode = Mode.Flee; Caught = false; next = 0f; }

    bool On()
    {
        if (agent == null) return false;
        if (!agent.enabled)
        {
            if (!NavMesh.SamplePosition(transform.position, out NavMeshHit h, 3f, NavMesh.AllAreas)) return false;
            agent.enabled = true; agent.Warp(h.position);
        }
        return agent.isOnNavMesh;
    }

    void Update()
    {
        if (anim != null && agent != null && agent.enabled) LundunAnim.Float(anim, "Speed", agent.velocity.magnitude, 0.1f);
        Transform j = LundunMissionRun.James();
        if (mode == Mode.Stand || !On()) return;
        agent.speed = speed; agent.acceleration = 16f; agent.angularSpeed = 720f; agent.isStopped = false;
        if (mode == Mode.To)
        {
            if (Time.time >= next) { next = Time.time + 1f; agent.SetDestination(to); }
            if (LundunMissionRun.FlatDistance(transform.position, to) < 1.4f)
            {
                There = true; Stand();
                if (hide) gameObject.SetActive(false);
            }
            return;
        }
        // fleeing: of a few ways out, the one that keeps the most ground between them
        if (j == null) return;
        if (LundunMissionRun.FlatDistance(transform.position, j.position) < 1.9f) { Caught = true; Stand(); return; }
        if (Time.time < next) return;
        next = Time.time + 0.6f;
        Vector3 away = transform.position - j.position; away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : transform.forward;
        Vector3 best = transform.position; float bestScore = float.MinValue;
        for (int i = -3; i <= 3; i++)
        {
            Vector3 d = Quaternion.Euler(0f, i * 35f, 0f) * away;
            if (!NavMesh.SamplePosition(transform.position + d * 14f, out NavMeshHit hit, 4f, NavMesh.AllAreas)) continue;
            float score = LundunMissionRun.FlatDistance(hit.position, j.position) - Mathf.Abs(i) * 1.5f;
            if (score > bestScore) { bestScore = score; best = hit.position; }
        }
        agent.SetDestination(best);
    }
}

/// S5: keep up with someone getting away on a bike (or anyhow): fail once they've been lost (far away and out of
/// sight, or very far) for loseAfter seconds; done when they get where they're going, or come off the bike.
public class LundunKeepUp : LundunStep
{
    readonly Func<Transform> who; readonly Func<bool> arrived, fell; readonly float loseAfter; readonly string text, failText;
    float lostFor;
    public LundunKeepUp(Func<Transform> target, Func<bool> gotThere, Func<bool> cameOff, float seconds, string objectiveText, string ifLost)
    {
        who = target; arrived = gotThere; fell = cameOff; loseAfter = seconds; text = objectiveText; failText = ifLost; objective = text;
    }

    public override void Begin(LundunMissionRun run)
    {
        lostFor = 0f;
        Transform t = who();
        LundunMinimap mm = Object.FindAnyObjectByType<LundunMinimap>();
        if (t != null && mm != null) mm.Track(t, new Color(0.4f, 0.9f, 0.45f), "", 16f, true);
    }

    public override bool Tick(LundunMissionRun run)
    {
        Transform t = who(), j = LundunMissionRun.James();
        if (t == null) { run.Fail(failText); return false; }
        if (fell != null && fell()) return true;
        if (arrived != null && arrived()) return true;
        if (j == null) return false;
        float d = LundunMissionRun.FlatDistance(t.position, j.position);
        Camera cam = Camera.main;
        bool seen = cam != null && LundunView.CanSee(cam, t.position + Vector3.up * 1.2f);
        bool lost = d > 95f || (d > 45f && !seen);
        lostFor = lost ? lostFor + Time.deltaTime : Mathf.Max(0f, lostFor - Time.deltaTime * 2f);
        objective = lostFor > 0.5f ? text + "  ·  losing him: " + Mathf.CeilToInt(loseAfter - lostFor) : text;
        if (lostFor >= loseAfter) run.Fail(failText);
        return false;
    }

    public override void End(LundunMissionRun run)
    {
        Transform t = who();
        LundunMinimap mm = Object.FindAnyObjectByType<LundunMinimap>();
        if (t != null && mm != null) mm.Untrack(t);
    }
}

/// S5: catch someone running on foot (LundunRunner fleeing): done when James reaches them. Fails if they get too far.
public class LundunCatch : LundunStep
{
    readonly Func<LundunRunner> who; readonly string failText;
    float farFor;
    public LundunCatch(Func<LundunRunner> runner, string objectiveText, string ifLost) { who = runner; objective = objectiveText; failText = ifLost; }

    public override void Begin(LundunMissionRun run)
    {
        farFor = 0f;
        LundunRunner r = who();
        LundunMinimap mm = Object.FindAnyObjectByType<LundunMinimap>();
        if (r != null && mm != null) mm.Track(r.transform, new Color(0.4f, 0.9f, 0.45f), "", 16f, true);
    }

    public override bool Tick(LundunMissionRun run)
    {
        LundunRunner r = who();
        Transform j = LundunMissionRun.James();
        if (r == null) { run.Fail(failText); return false; }
        if (r.mode != LundunRunner.Mode.Flee && !r.Caught && j != null && LundunMissionRun.FlatDistance(r.transform.position, j.position) < 1.9f) return true;   // dazed (came off the bike)
        if (r.Caught) return true;
        if (j != null && LundunMissionRun.FlatDistance(r.transform.position, j.position) > 110f) farFor += Time.deltaTime; else farFor = 0f;
        if (farFor > 10f) run.Fail(failText);
        return false;
    }

    public override void End(LundunMissionRun run)
    {
        LundunRunner r = who();
        LundunMinimap mm = Object.FindAnyObjectByType<LundunMinimap>();
        if (r != null && mm != null) mm.Untrack(r.transform);
    }
}

/// S5: Brixtun market (the square by Brixtun Station, Lundun → 28): its middle, a stall's front, and its crowd.
public static class LundunMarket
{
    public static readonly Vector3 Middle = new Vector3(484f, 0f, -66.5f);
    public static readonly Vector3 EastEnd = new Vector3(519f, 0f, -66.5f);

    /// The market's crowd (not a store's).
    public static LundunMarketCrowd Crowd()
    {
        LundunMarketCrowd best = null; float bd = float.MaxValue;
        foreach (LundunMarketCrowd c in Object.FindObjectsByType<LundunMarketCrowd>(FindObjectsSortMode.None))
        {
            Vector3[] t = c.TraderSpots;
            if (t == null || t.Length == 0) continue;
            float d = LundunMissionRun.FlatDistance(t[0], Middle);
            if (d < bd) { bd = d; best = c; }
        }
        return bd < 80f ? best : null;
    }

    /// In the aisle in front of the stall nearest the middle (where trouble stands), and which way the stall is.
    public static Vector3 Stall(out Vector3 stallDir)
    {
        stallDir = Vector3.back;
        LundunMarketCrowd c = Crowd();
        if (c != null)
        {
            Vector3[] at = c.TraderSpots, face = c.TraderFacing;
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < at.Length; i++) { float d = LundunMissionRun.FlatDistance(at[i], Middle); if (d < bd) { bd = d; best = i; } }
            if (best >= 0)
            {
                Vector3 f = face != null && best < face.Length ? face[best] : Vector3.forward; f.y = 0f;
                f = f.sqrMagnitude > 0.01f ? f.normalized : Vector3.forward;
                stallDir = -f;
                return LundunMissionRun.Ground(at[best] + f * 3.2f);
            }
        }
        return LundunMissionRun.Ground(Middle);
    }

    /// Trouble: everyone near runs.
    public static void Scatter(Vector3 from)
    {
        LundunMarketCrowd c = Crowd();
        if (c != null) c.HoldUp(from, 45f, new Vector3(0f, -1000f, 0f), 0f);
    }
}
