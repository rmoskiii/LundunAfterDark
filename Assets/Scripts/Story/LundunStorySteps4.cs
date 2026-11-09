using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// S4: enemy cars after James: they turn up behind him (out of sight where possible) and chase him; done when every
/// one is wrecked or lost (LundunChaseCar). His passengers shoot at them.
public class LundunChase : LundunStep
{
    readonly int count;
    readonly List<LundunChaseCar> cars = new List<LundunChaseCar>();
    readonly System.Random rng = new System.Random();
    bool spawned;
    float begun;
    public LundunChase(int howMany, string objectiveText) { count = howMany; objective = objectiveText; }

    public override void Begin(LundunMissionRun run) { spawned = false; begun = Time.time; cars.Clear(); }

    public override bool Tick(LundunMissionRun run)
    {
        if (!spawned)
        {
            if (!LundunMissionRun.Driving() && Time.time - begun < 6f) return false;      // they come for him in a car
            Spawn(run);
            spawned = true;
            return cars.Count == 0;
        }
        bool left = false;
        LundunMinimap mm = Object.FindAnyObjectByType<LundunMinimap>();
        foreach (LundunChaseCar c in cars)
        {
            if (c == null) continue;
            if (c.Wrecked || c.Lost) { if (mm != null) mm.Untrack(c.transform); continue; }
            left = true;
        }
        return !left;
    }

    void Spawn(LundunMissionRun run)
    {
        LundunPopulation pop = LundunPopulation.Instance;
        LundunNavigation nav = LundunNavigation.Instance;
        Transform j = LundunMissionRun.James();
        if (pop == null || nav == null || nav.Graph == null || j == null) return;
        Camera cam = Camera.main;
        Vector3 jp = j.position, jf = j.forward; jf.y = 0f; jf = jf.sqrMagnitude > 0.01f ? jf.normalized : Vector3.forward;
        LundunMinimap mm = Object.FindAnyObjectByType<LundunMinimap>();
        for (int i = 0; i < count; i++)
        {
            Vector3 pos = Vector3.zero, along = Vector3.forward; bool found = false;
            for (int a = 0; a < 24 && !found; a++)
            {
                NavPoint p, dir; double hw;
                if (!nav.Graph.RandomRoadPoint(new NavPoint(jp.x, jp.z), 55, 110, rng, out p, out dir, out hw)) continue;
                Vector3 q = new Vector3((float)p.x, 0f, (float)p.z);
                if (a < 16 && Vector3.Dot(q - jp, jf) > 0f) continue;                     // behind him first
                if (a < 20 && cam != null && LundunView.CanSee(cam, q)) continue;          // out of sight if we can
                bool clash = false;
                foreach (LundunChaseCar c in cars) if (c != null && (c.transform.position - q).sqrMagnitude < 64f) clash = true;
                if (clash) continue;
                along = new Vector3((float)dir.x, 0f, (float)dir.z);
                if (Vector3.Dot(jp - q, along) < 0f) along = -along;                      // facing his way
                pos = LundunMissionRun.Ground(q + new Vector3(-along.z, 0f, along.x) * Mathf.Min(1.8f, (float)hw * 0.5f));
                found = true;
            }
            if (!found) continue;
            GameObject g = pop.NewCar(LundunMissions.Instance != null ? LundunMissions.Instance.transform : null, pos, Quaternion.LookRotation(along), 11 + i * 3);
            if (g == null) continue;
            LundunChaseCar cc = g.AddComponent<LundunChaseCar>();
            cc.Begin(run.EnemyDamage);
            cars.Add(cc);
            if (mm != null) mm.Track(g.transform, new Color(1f, 0.25f, 0.2f), "", 16f, true);
        }
    }

    public override void End(LundunMissionRun run)
    {
        LundunMinimap mm = Object.FindAnyObjectByType<LundunMinimap>();
        foreach (LundunChaseCar c in cars) if (c != null && mm != null) mm.Untrack(c.transform);
    }
}

/// S4: gunmen waiting as James leaves a place: done once he's 'escape' metres away from it.
public class LundunAmbush : LundunStep
{
    readonly Vector3 at; readonly int count; readonly float escape;
    readonly List<Health> men = new List<Health>();
    public LundunAmbush(Vector3 where, int gunmen, float escapeMetres, string objectiveText) { at = where; count = gunmen; escape = escapeMetres; objective = objectiveText; }

    public override void Begin(LundunMissionRun run)
    {
        men.Clear();
        LundunMissions.LendGun();                          // v50
        LundunPopulation pop = LundunPopulation.Instance;
        int n = count + run.ExtraEnemies;
        Vector3 c = LundunMissionRun.Ground(at);
        for (int i = 0; i < n && pop != null; i++)
        {
            float a = i * Mathf.PI * 2f / n + 0.4f;
            Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 16f;
            if (!NavMesh.SamplePosition(p + Vector3.up, out NavMeshHit hit, 5f, NavMesh.AllAreas)) continue;
            GameObject g = pop.NewPerson(null, "Street");
            if (g == null) continue;
            NPCBrain b = g.GetComponent<NPCBrain>(); if (b != null) b.enabled = false;
            g.transform.position = hit.position;
            g.SetActive(true);
            run.Spawn(g);
            LundunAngryDriver ad = g.AddComponent<LundunAngryDriver>();
            ad.Foe = true;
            ad.Begin(true, 9999f, 90f, run.EnemyDamage);
            Health h = g.GetComponent<Health>();
            if (h != null) men.Add(h);
        }
        LundunMissions.Mark(men, true);
    }

    public override bool Tick(LundunMissionRun run)
    {
        Transform j = LundunMissionRun.James();
        return j != null && LundunMissionRun.FlatDistance(j.position, at) > escape;
    }

    public override void End(LundunMissionRun run) { LundunMissions.Mark(men, false); }
}

/// S4: drive somewhere while the story's told in the car: the lines come one after another while James is driving
/// (paused while he isn't). Done on arrival.
public class LundunDriveTalk : LundunStep
{
    readonly LundunLine[] lines;
    int at = -1; float next;
    public LundunDriveTalk(Vector3 where, float radius, string objectiveText, params LundunLine[] said)
    {
        lines = said; objective = objectiveText;
        hasMarker = true; marker = LundunMissionRun.Ground(where); markerRadius = radius; route = true;
    }
    public override void Begin(LundunMissionRun run) { at = -1; next = Time.time + 4f; }
    public override bool Tick(LundunMissionRun run)
    {
        Transform j = LundunMissionRun.James();
        if (j == null) return false;
        if (LundunMissionRun.Driving() && Time.time >= next && at + 1 < lines.Length && !LundunMissions.Talking)
        {
            at++;
            float secs = LundunMissions.ReadTime(lines[at].text);
            LundunMissions.Say(lines[at].who, lines[at].text, secs);
            next = Time.time + secs + LundunMissions.TakeDelay() + 1.2f;
        }
        return LundunMissionRun.FlatDistance(j.position, marker) < markerRadius;
    }
}

/// S4: lines that depend on what happened (a choice made earlier): picked when the step starts.
public class LundunTalkPick : LundunStep
{
    readonly Func<LundunLine[]> pick;
    LundunLine[] lines; int at = -1; float next;
    public LundunTalkPick(Func<LundunLine[]> which) { pick = which; }
    public override void Begin(LundunMissionRun run) { lines = pick() ?? new LundunLine[0]; at = -1; next = 0f; LundunMissions.TakeSkip(); }
    public override bool Tick(LundunMissionRun run)
    {
        if (Time.time < next && !LundunMissions.TakeSkip()) return false;      // guidance: tap for the next line
        at++;
        if (at >= lines.Length) { LundunMissions.Say(null, null, 0f); return true; }
        float secs = LundunMissions.ReadTime(lines[at].text);
        LundunMissions.Say(lines[at].who, lines[at].text, secs);
        next = Time.time + secs + LundunMissions.TakeDelay();
        return false;
    }
    public override void End(LundunMissionRun run) { LundunMissions.Say(null, null, 0f); }
}

/// S4: a choice. The game pauses and two big buttons come up; the first can cost money (greyed out if James can't
/// afford it). Saved as a flag ("<key>:a" or "<key>:b"), so later scenes can react to it (S5).
public class LundunChoice : LundunStep
{
    readonly string title, a, b, key; readonly long cost;
    GameObject ui; bool picked; float before = 1f;
    public LundunChoice(string question, string first, long firstCosts, string second, string flagKey)
    {
        title = question; a = first; cost = firstCosts; b = second; key = flagKey;
    }

    public static bool Chose(string flagKey, bool first) { LundunStoryData d = LundunStory.Data; return d != null && d.flags.Contains(flagKey + (first ? ":a" : ":b")); }

    public override void Begin(LundunMissionRun run)
    {
        picked = false;
        LundunStoryData d = LundunStory.Data;
        if (d != null && (d.flags.Contains(key + ":a") || d.flags.Contains(key + ":b"))) { d.flags.Remove(key + ":a"); d.flags.Remove(key + ":b"); }   // a replay chooses again
        before = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        Build();
    }

    public override bool Tick(LundunMissionRun run) { return picked; }
    public override void End(LundunMissionRun run) { if (ui != null) Object.Destroy(ui); if (Time.timeScale == 0f) Time.timeScale = before; }

    void Choose(bool first)
    {
        if (picked) return;
        if (first && cost > 0 && !LundunWallet.CanAfford(cost)) return;
        picked = true;
        if (first && cost > 0) LundunWallet.Pay(cost, title);
        LundunStoryData d = LundunStory.Data;
        if (d != null) { d.flags.Add(key + (first ? ":a" : ":b")); LundunStory.MarkDirty(); }
        if (ui != null) Object.Destroy(ui);
        Time.timeScale = before;
        LundunHaptics.Light();
    }

    void Build()
    {
        ui = new GameObject("LundunChoice", typeof(RectTransform));
        Canvas cv = ui.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 80;
        CanvasScaler sc = ui.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        ui.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)ui.transform;
        Image dim = Box(root, "Dim", Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.55f));
        RectTransform dr = dim.rectTransform; dr.anchorMin = Vector2.zero; dr.anchorMax = Vector2.one; dr.offsetMin = dr.offsetMax = Vector2.zero;
        TextMeshProUGUI q = Label(root, title, 44f, new Vector2(0f, 170f), new Vector2(1400f, 90f));
        q.color = new Color(1f, 0.66f, 0.18f);
        bool afford = cost <= 0 || LundunWallet.CanAfford(cost);
        Button(root, a + (cost > 0 ? "\n<size=65%>" + LundunWallet.Format(cost) + (afford ? "" : " · you've only got " + LundunWallet.Format(LundunWallet.Cash)) + "</size>" : ""),
               new Vector2(-330f, -10f), afford ? new Color(0.62f, 0.4f, 0.08f, 0.95f) : new Color(0.25f, 0.25f, 0.27f, 0.9f), () => Choose(true));
        Button(root, b, new Vector2(330f, -10f), new Color(0.12f, 0.2f, 0.42f, 0.95f), () => Choose(false));
    }

    static Image Box(RectTransform parent, string name, Vector2 at, Vector2 size, Color c)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.transform.SetParent(parent, false);
        RectTransform r = (RectTransform)g.transform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.anchoredPosition = at; r.sizeDelta = size;
        Image i = g.AddComponent<Image>(); i.color = c;
        return i;
    }

    static TextMeshProUGUI Label(RectTransform parent, string text, float size, Vector2 at, Vector2 box)
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

    static void Button(RectTransform parent, string text, Vector2 at, Color c, Action click)
    {
        Image img = Box(parent, "Choice", at, new Vector2(600f, 200f), c);
        Button btn = img.gameObject.AddComponent<Button>(); btn.targetGraphic = img; btn.onClick.AddListener(() => click());
        img.gameObject.AddComponent<LundunPressFX>();
        TextMeshProUGUI t = Label((RectTransform)img.transform, text, 40f, Vector2.zero, new Vector2(560f, 180f));
        t.rectTransform.anchorMin = t.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
    }
}
