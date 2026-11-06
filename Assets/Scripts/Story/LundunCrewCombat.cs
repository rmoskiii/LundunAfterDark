using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// S4: the crew in a fight (missions only; outside them they still can't be hurt and don't fight).
///   fighting   a fighter (Marcus, Tayo) with James goes for the nearest mission enemy: fists by default (Marcus hits
///              like the boxer he was), a pistol if the mission gives them one (they keep 5–14 m off and shoot). The
///              others (Kemi, Leah, Uncle Femi) keep back. Enemies go for whoever's nearest: James or a fighter
///   health     a bar over their head while it matters; it fills again slowly once the fight's over
///   downed     emptied: they drop and 20 seconds start. Get to them and hold REVIVE (two seconds) to get them up, or
///              the mission fails ("Marcus didn't make it")
///   drive-bys  an armed one riding with James shoots out of the window at enemy cars (LundunChaseCar) and enemies on
///              foot: muzzle flash, tracer, the shot, real damage
public partial class LundunCrew
{
    const float DownedFor = 20f, ReviveHold = 2f, ShotRange = 14f, DriveByRange = 40f;
    private readonly List<Health> foes = new List<Health>();
    private static readonly HashSet<string> armedNames = new HashSet<string>();
    private float nextFoes, reviveHeld;
    private GameObject reviveGo;
    private Image reviveFill;
    private TextMeshProUGUI reviveText, downLabel;
    private HoldButton reviveHold;
    private Transform flashPoint;
    private static Material barBackMat, barGreenMat, barRedMat, tracerMat;
    private static int sightMask = -1;

    static bool InMission { get { return LundunMissions.Running || LundunBackup.Active; } }       // S8: or Marcus came when called
    static int SightMask { get { if (sightMask == -1) sightMask = LayerMask.GetMask("Default"); return sightMask; } }

    /// A mission gives 'who' a pistol (true) or takes it back.
    public static void Arm(string who, bool on) { if (on) armedNames.Add(who); else armedNames.Remove(who); }
    public static void DisarmAll() { armedNames.Clear(); }

    // ------------------------------------------------------------------ every frame (from Update)
    void CombatUpdate(Transform j)
    {
        if (Time.time >= nextFoes) { nextFoes = Time.time + 0.4f; RefreshFoes(j); }
        foreach (Member m in members.Values)
        {
            if (!InMission)
            {
                if (m.downed) Revive(m, false);
                m.hp = m.maxHp;
                Holster(m);
            }
            else if (!m.downed && foes.Count == 0 && m.hp < m.maxHp) m.hp = Mathf.Min(m.maxHp, m.hp + m.maxHp * 0.08f * Time.deltaTime);
            if (m.tracer != null && m.tracer.enabled && Time.time > m.tracerOff) m.tracer.enabled = false;
        }
        ReviveUI(j);
    }

    void RefreshFoes(Transform j)
    {
        foes.Clear();
        if (!InMission || j == null) return;
        foreach (LundunAngryDriver a in FindObjectsByType<LundunAngryDriver>(FindObjectsSortMode.None))
        {
            if (a == null || !(a.Foe || LundunBackup.Active) || !a.enabled) continue;
            Health h = a.GetComponent<Health>();
            if (h == null || h.IsDead || LundunMissionRun.FlatDistance(h.transform.position, j.position) > 45f) continue;
            foes.Add(h);
        }
        // S8: Marcus's backup in free roam: gang members going for James count too
        if (LundunBackup.Active && !LundunMissions.Running)
            foreach (LundunGangMember gm in FindObjectsByType<LundunGangMember>(FindObjectsSortMode.None))
            {
                if (gm == null || gm.IsDead || !gm.FightingJames) continue;
                Health h = gm.GetComponent<Health>();
                if (h != null && LundunMissionRun.FlatDistance(h.transform.position, j.position) <= 45f) foes.Add(h);
            }
    }

    // ------------------------------------------------------------------ on foot
    /// True while they're fighting (then Follow leaves them to it).
    bool Combat(Member m, Transform j)
    {
        if (!InMission || m.look == null || !m.look.fighter || foes.Count == 0) { Holster(m); return false; }
        Health best = null; float bd = 35f;
        foreach (Health h in foes)
        {
            if (h == null || h.IsDead) continue;
            float d0 = LundunMissionRun.FlatDistance(m.t.position, h.transform.position);
            if (d0 < bd) { bd = d0; best = h; }
        }
        if (best == null) { Holster(m); return false; }
        m.foe = best;
        Vector3 to = best.transform.position - m.t.position; to.y = 0f;
        float d = to.magnitude;
        if (armedNames.Contains(m.who))
        {
            Draw(m);
            if (m.agent != null) m.agent.updateRotation = false;
            LundunAnim.Bool(m.anim, "Aim", true);
            if (d > ShotRange) Walk(m, best.transform.position, 5.6f);
            else if (d < 5f) Walk(m, m.t.position - to.normalized * 3f, 2f);
            else StopAgent(m);
            FaceDir(m, to);
            if (m.agent != null) LundunAnim.Float(m.anim, "AimMove", m.t.InverseTransformDirection(m.agent.velocity).z, 0.1f);
            if (d <= ShotRange + 2f && Time.time >= m.nextAttack)
            {
                m.nextAttack = Time.time + Random.Range(0.75f, 1.2f);
                Vector3 from = m.gun != null ? m.gun.transform.position : m.t.position + Vector3.up * 1.4f + m.t.forward * 0.5f;
                Fire(m, from, best.transform, best, null, 0.55f, 22f);
            }
        }
        else
        {
            if (d > 1.3f) Walk(m, best.transform.position, d > 6f ? 5.6f : 3.5f);
            else StopAgent(m);
            FaceDir(m, to);
            if (d <= 1.7f && Time.time >= m.nextAttack)
            {
                m.nextAttack = Time.time + Random.Range(0.9f, 1.2f);
                LundunAnim.Trigger(m.anim, "Punch");
                best.TakeDamage(m.look.punch, best.transform.position + Vector3.up * 1.4f, to.normalized * 2f);
            }
        }
        return true;
    }

    void StopAgent(Member m)
    {
        if (m.agent == null || !m.agent.enabled || !m.agent.isOnNavMesh) return;
        if (m.agent.hasPath) m.agent.ResetPath();
        m.agent.velocity = Vector3.zero;
    }

    void FaceDir(Member m, Vector3 to)
    {
        if (to.sqrMagnitude < 0.01f) return;
        m.t.rotation = Quaternion.RotateTowards(m.t.rotation, Quaternion.LookRotation(to), 540f * Time.deltaTime);
    }

    void Draw(Member m)
    {
        if (m.gun != null || LundunAngryDriver.GunModel == null || m.anim == null || !m.anim.isHuman) return;
        Transform hand = m.anim.GetBoneTransform(HumanBodyBones.RightHand);
        if (hand == null) return;
        m.gun = Instantiate(LundunAngryDriver.GunModel, hand);
        m.gun.name = "CrewGun";
        Vector3 ls = m.t.lossyScale; float s = ls.y > 0.0001f ? ls.y : 1f;          // real size whatever their build
        m.gun.transform.localScale = LundunAngryDriver.GunModel.transform.localScale / s;
        m.gun.transform.localPosition = new Vector3(0.05f, 0.08f, 0.02f) / s;
        m.gun.transform.localRotation = Quaternion.Euler(0f, 90f, 90f);
        foreach (Collider c in m.gun.GetComponentsInChildren<Collider>()) Destroy(c);
    }

    void Holster(Member m)
    {
        if (m.gun != null) { Destroy(m.gun); m.gun = null; }
        if (m.foe != null || (m.agent != null && !m.agent.updateRotation))
        {
            LundunAnim.Bool(m.anim, "Aim", false);
            LundunAnim.Float(m.anim, "AimMove", 0f);
            if (m.agent != null) m.agent.updateRotation = true;
        }
        m.foe = null;
    }

    /// One shot from 'from' at a person (who) or a car (car): a hit now and then (damage), otherwise past them.
    void Fire(Member m, Vector3 from, Transform target, Health who, VehicleHealth car, float chance, float damage)
    {
        Vector3 aim = target.position + Vector3.up * (car != null ? 0.9f : 1.3f);
        Vector3 end = aim;
        bool clear = !Physics.Linecast(from, aim, SightMask, QueryTriggerInteraction.Ignore);
        if (clear && Random.value < chance)
        {
            if (car != null) car.TakeDamage(damage);
            else if (who != null) who.TakeDamage(damage, aim, (aim - from).normalized * 3f, true);
        }
        else
        {
            Vector3 side = Vector3.Cross(Vector3.up, (aim - from).normalized);
            end = aim + side * Random.Range(0.8f, 1.8f) * (Random.value < 0.5f ? -1f : 1f) + Vector3.up * Random.Range(-0.3f, 0.6f);
        }
        Tracer(m, from, end);
        AudioClip shot = LundunGunAudio.Shot(LundunWeapons.Kind.Pistol);
        if (shot != null) AudioSource.PlayClipAtPoint(shot, from, 0.75f);
        if (flashPoint == null) { flashPoint = new GameObject("CrewMuzzle").transform; flashPoint.SetParent(transform, false); }
        flashPoint.position = from; flashPoint.rotation = Quaternion.LookRotation(end - from);
        LundunCombatFX.Muzzle(flashPoint, 0.8f, 1);
    }

    void Tracer(Member m, Vector3 from, Vector3 to)
    {
        if (m.tracer == null)
        {
            GameObject g = new GameObject("CrewTracer_" + m.who);
            g.transform.SetParent(transform, false);
            m.tracer = g.AddComponent<LineRenderer>();
            m.tracer.positionCount = 2; m.tracer.startWidth = 0.03f; m.tracer.endWidth = 0.015f;
            m.tracer.sharedMaterial = Unlit(ref tracerMat, new Color(1f, 0.85f, 0.5f));
            m.tracer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        m.tracer.SetPosition(0, from); m.tracer.SetPosition(1, to);
        m.tracer.enabled = true;
        m.tracerOff = Time.time + 0.06f;
    }

    // ------------------------------------------------------------------ drive-bys
    void DriveBy(Member m)
    {
        if (!InMission || !armedNames.Contains(m.who) || Time.time < m.nextAttack || m.car == null) return;
        m.nextAttack = Time.time + 0.25f;                                   // look again soon if there's nothing to shoot
        Transform c = m.car.transform;
        float side = m.seat == 2 ? 1f : -1f;
        Vector3 window = c.position + Vector3.up * 1.25f + c.right * (0.95f * side) + c.forward * (m.seat == 0 ? 0.2f : -0.8f);
        Transform best = null; VehicleHealth bestCar = null; Health bestWho = null; float bd = DriveByRange;
        foreach (LundunChaseCar e in LundunChaseCar.Active)
        {
            if (e == null || e.Wrecked) continue;
            float d = Vector3.Distance(e.transform.position, c.position);
            if (d < bd) { bd = d; best = e.transform; bestCar = e.Health; bestWho = null; }
        }
        foreach (Health h in foes)
        {
            if (h == null || h.IsDead) continue;
            float d = Vector3.Distance(h.transform.position, c.position);
            if (d < Mathf.Min(bd, 30f)) { bd = d; best = h.transform; bestWho = h; bestCar = null; }
        }
        if (best == null) return;
        if (Physics.Linecast(window, best.position + Vector3.up, SightMask, QueryTriggerInteraction.Ignore)) return;
        m.nextAttack = Time.time + Random.Range(0.55f, 0.9f);
        Fire(m, window, best, bestWho, bestCar, bestCar != null ? 0.5f : 0.45f, bestCar != null ? LundunChaseCar.HitDamage : 22f);
    }

    // ------------------------------------------------------------------ hurt, down, back up
    void HurtBy(Member m, float amount)
    {
        if (!InMission || m.downed || m.state == State.InCar) return;
        m.hp -= amount;
        if (m.hp <= 0f) Down(m);
    }

    void Down(Member m)
    {
        m.downed = true; m.downedAt = Time.time; m.hp = 0f;
        Holster(m);
        StopAgent(m);
        if (m.agent != null && m.agent.isOnNavMesh) m.agent.isStopped = true;
        LundunAnim.Trigger(m.anim, "Die");
        LundunMissions.Say(m.who, "I'm hit... James!", 2.5f);
        LundunHaptics.Medium();
    }

    void DownedTick(Member m, Transform j)
    {
        if (m.agent != null && m.agent.isOnNavMesh) m.agent.isStopped = true;
        if (Time.time - m.downedAt > DownedFor) { m.downed = false; LundunMissions.FailNow(Pretty(m.who) + " didn't make it"); }
    }

    void Revive(Member m, bool said)
    {
        m.downed = false;
        m.hp = m.maxHp * 0.5f;
        if (m.anim != null && m.anim.isActiveAndEnabled) m.anim.Play("Locomotion", 0, 0f);
        if (m.agent != null && m.agent.isOnNavMesh) m.agent.isStopped = false;
        if (said) LundunMissions.Say(m.who, Random.value < 0.5f ? "Cheers, bro. I'm good. I'm good." : "Get me up... right. Let's finish this.", 2.5f);
    }

    /// The REVIVE button by a downed one, and the countdown at the top.
    void ReviveUI(Transform j)
    {
        Member down = null; float soonest = float.MaxValue;
        foreach (Member m in members.Values) if (m.downed && m.downedAt < soonest) { soonest = m.downedAt; down = m; }
        if (down == null && reviveGo == null) return;
        if (reviveGo == null) BuildReviveUI();
        if (down == null) { reviveGo.SetActive(false); downLabel.gameObject.SetActive(false); reviveHeld = 0f; return; }
        float left = Mathf.Max(0f, DownedFor - (Time.time - down.downedAt));
        downLabel.gameObject.SetActive(true);
        downLabel.text = Pretty(down.who).ToUpperInvariant() + " IS DOWN  ·  0:" + Mathf.CeilToInt(left).ToString("00") + "  ·  get to them";
        PlayerHealth ph = PlayerHealth.Instance;
        bool near = ph != null && ph.OnFoot && !ph.IsDead && LundunMissionRun.FlatDistance(ph.Body.position, down.t.position) < 2.6f;
        reviveGo.SetActive(near);
        if (!near) { reviveHeld = 0f; return; }
        reviveText.text = "HOLD TO REVIVE " + Pretty(down.who).ToUpperInvariant();
        reviveHeld = reviveHold != null && reviveHold.IsHeld ? reviveHeld + Time.deltaTime : 0f;
        reviveFill.fillAmount = reviveHeld / ReviveHold;
        if (reviveHeld >= ReviveHold) { reviveHeld = 0f; Revive(down, true); }
    }

    void BuildReviveUI()
    {
        GameObject cg = new GameObject("CrewUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas cv = cg.AddComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 63;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920, 1080); sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();
        reviveGo = new GameObject("ReviveButton", typeof(RectTransform));
        reviveGo.transform.SetParent(cg.transform, false);
        RectTransform rt = (RectTransform)reviveGo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 380f); rt.sizeDelta = new Vector2(520f, 120f);
        reviveGo.AddComponent<Image>().color = new Color(0.1f, 0.45f, 0.25f, 0.92f);
        reviveHold = reviveGo.AddComponent<HoldButton>();
        GameObject fg = new GameObject("Fill", typeof(RectTransform));
        fg.transform.SetParent(reviveGo.transform, false);
        RectTransform fr = (RectTransform)fg.transform; fr.anchorMin = Vector2.zero; fr.anchorMax = new Vector2(1f, 0f); fr.pivot = new Vector2(0.5f, 0f);
        fr.sizeDelta = new Vector2(0f, 14f); fr.anchoredPosition = Vector2.zero;
        reviveFill = fg.AddComponent<Image>(); reviveFill.color = new Color(0.6f, 1f, 0.7f); reviveFill.type = Image.Type.Filled;
        reviveFill.fillMethod = Image.FillMethod.Horizontal; reviveFill.fillAmount = 0f; reviveFill.raycastTarget = false;
        reviveText = Text(reviveGo.transform, 34f, new Vector2(10f, 14f), new Vector2(-10f, 0f));
        GameObject lg = new GameObject("Down", typeof(RectTransform));
        lg.transform.SetParent(cg.transform, false);
        RectTransform lr = (RectTransform)lg.transform;
        lr.anchorMin = lr.anchorMax = new Vector2(0.5f, 1f); lr.pivot = new Vector2(0.5f, 1f);
        lr.anchoredPosition = new Vector2(0f, -186f); lr.sizeDelta = new Vector2(980f, 56f);
        lg.AddComponent<Image>().color = new Color(0.55f, 0.06f, 0.08f, 0.85f);
        downLabel = Text(lg.transform, 30f, Vector2.zero, Vector2.zero);
        reviveGo.SetActive(false); lg.SetActive(false);
    }

    static TextMeshProUGUI Text(Transform parent, float size, Vector2 min, Vector2 max)
    {
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(parent, false);
        RectTransform tr = (RectTransform)tg.transform; tr.anchorMin = Vector2.zero; tr.anchorMax = Vector2.one; tr.offsetMin = min; tr.offsetMax = max;
        TextMeshProUGUI t = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.fontSize = size; t.alignment = TextAlignmentOptions.Center; t.color = Color.white; t.fontStyle = FontStyles.Bold; t.raycastTarget = false;
        return t;
    }

    // ------------------------------------------------------------------ who the enemies go for
    /// A foe goes for the nearest of James and the fighters with him (one that's hitting it, a little sooner).
    Transform PickFor(LundunAngryDriver a)
    {
        Transform j = James();
        if (a == null || j == null || !InMission) return null;
        Vector3 p = a.transform.position;
        float best = LundunMissionRun.FlatDistance(p, j.position);
        Transform pick = null;
        Health mine = a.GetComponent<Health>();
        foreach (Member m in members.Values)
        {
            if (m.go == null || !m.go.activeInHierarchy || m.downed || m.look == null || !m.look.fighter || m.state != State.Following) continue;
            float d = LundunMissionRun.FlatDistance(p, m.t.position) * (m.foe == mine ? 0.7f : 1f);
            if (d < best) { best = d; pick = m.t; }
        }
        return pick;
    }

    // ------------------------------------------------------------------ health bars
    void Bars(Camera cam)
    {
        foreach (Member m in members.Values)
        {
            bool show = InMission && !m.downed && m.go != null && m.go.activeInHierarchy && m.look != null &&
                        ((m.look.fighter && foes.Count > 0) || m.hp < m.maxHp - 0.5f);
            if (!show) { if (m.barBack != null) { m.barBack.gameObject.SetActive(false); m.barFill.gameObject.SetActive(false); } continue; }
            if (m.barBack == null) { m.barBack = Quad("Bar_" + m.who, Unlit(ref barBackMat, new Color(0.05f, 0.05f, 0.05f, 1f))); m.barFill = Quad("BarFill_" + m.who, Unlit(ref barGreenMat, new Color(0.3f, 0.9f, 0.4f))); }
            m.barBack.gameObject.SetActive(true); m.barFill.gameObject.SetActive(true);
            float f = Mathf.Clamp01(m.hp / Mathf.Max(1f, m.maxHp));
            Vector3 top = m.head != null ? m.head.position + Vector3.up * 0.28f : m.t.position + Vector3.up * 1.98f;
            Quaternion face = Quaternion.LookRotation(top - cam.transform.position);
            m.barBack.SetPositionAndRotation(top, face);
            m.barBack.localScale = new Vector3(0.64f, 0.085f, 1f);
            Vector3 right = face * Vector3.right;
            m.barFill.SetPositionAndRotation(top - right * (0.3f * (1f - f)) - (face * Vector3.forward) * 0.005f, face);
            m.barFill.localScale = new Vector3(0.6f * f, 0.055f, 1f);
            Renderer r = m.barFill.GetComponent<Renderer>();
            r.sharedMaterial = f > 0.35f ? Unlit(ref barGreenMat, new Color(0.3f, 0.9f, 0.4f)) : Unlit(ref barRedMat, new Color(0.95f, 0.25f, 0.2f));
        }
    }

    Transform Quad(string name, Material mat)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        g.name = name;
        Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
        g.transform.SetParent(transform, false);
        Renderer r = g.GetComponent<Renderer>(); r.sharedMaterial = mat; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g.transform;
    }

    static Material Unlit(ref Material m, Color c)
    {
        if (m != null) return m;
        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        m = new Material(s != null ? s : Shader.Find("Unlit/Color")) { name = "CrewUnlit" };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        m.color = c;
        return m;
    }

    void CombatGone(Member m)
    {
        if (m.gun != null) Destroy(m.gun);
        if (m.barBack != null) Destroy(m.barBack.gameObject);
        if (m.barFill != null) Destroy(m.barFill.gameObject);
        if (m.tracer != null) Destroy(m.tracer.gameObject);
    }
}
