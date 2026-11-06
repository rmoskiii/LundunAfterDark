using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// S7: the railway arches by Brixtun station (Story only; added by LundunStoryUI). Three arches in a row under the
/// viaduct, found at runtime (the nearest free run of three bays to James's lock-up), each with its own front built in
/// code: a painted panel filling the arch, a lit sign, a roller shutter and a door.
///   ARMS DEALER     (sign: SURPLUS & SECURITY) walk in (a fade, like the pubs): SMG £2,500, shotgun £3,500,
///                   carbine £8,000, ammo by the magazine, body armour £500 (the blue bar; it takes hits before health)
///   ARCHES MOTORS   drive onto the forecourt and stop: RESPRAY £250 (a new colour, one star less), REPAIR £100–£800
///                   by the damage, SELL (the chop shop) £400–£3,000 by the car and its state
///   THE LOCK-UP     James's own unit, walk in: REST (full health, six hours pass) and free ammo once a day
/// Closed during missions; the walk-in ones are shut to you while the police are after you.
public class LundunArches : MonoBehaviour
{
    public static LundunArches Instance { get; private set; }
    /// In the arms dealer's or the lock-up (the camera goes indoors, no rain). S8: or a mission's indoor place
    /// (Warehouse 9's floor).
    public static bool Inside { get { return inRoom || MissionInside; } private set { inRoom = value; } }
    private static bool inRoom;
    public static bool MissionInside;

    const float InteriorY = 300f;
    const float Half = 3.2f, Spring = 2.6f, Rad = 3.2f, RoomLen = 12f;
    static readonly Vector3 LockUpStreet = new Vector3(428f, 0f, -56f);

    public enum Kind { Dealer, Motors, LockUp }
    public class Bay
    {
        public Kind kind;
        public string name, sign, sub, letter;
        public Color panel, glow;
        public Vector3 at;                 // the opening's middle, on the ground, on the face of the arch's infill
        public Vector3 outward, along;
        public Quaternion rot;             // +Z out to the street, +X along the viaduct
        public float doorX;                // the door, across the opening (local x)
        public GameObject go, room;
        public Transform shutter, marker;
        public Vector3 Door { get { return at + along * doorX + outward * 1.1f; } }
        public Vector3 Forecourt { get { return at + outward * 4.2f; } }
        public Quaternion RoomRot { get { return rot * Quaternion.Euler(0f, 180f, 0f); } }      // +Z into the arch
        public Vector3 RoomOrigin { get { return new Vector3(at.x, InteriorY, at.z); } }
        public float RoomDoorX { get { return -doorX; } }
    }
    private readonly List<Bay> bays = new List<Bay>();
    public bool Ready { get { return bays.Count == 3; } }
    Bay Of(Kind k) { foreach (Bay b in bays) if (b.kind == k) return b; return null; }

    // prices
    public const long SmgPrice = 2500, ShotgunPrice = 3500, CarbinePrice = 8000, ArmourPrice = 500, ResprayPrice = 250;

    // state
    private Bay inBay;
    private GameObject dealer;
    private bool busy;
    private float nextCheck;
    private VehicleSystem vehicles;

    // UI
    private GameObject buttonGo, panelGo, fadeGo;
    private TextMeshProUGUI label, panelTitle, panelNote;
    private Image back, fadeImage;
    private RectTransform rowsRt;
    private enum Offer { None, GoIn, Leave, Browse, Rest, Ammo, Garage }
    private Offer offer, panelFor;
    private Bay offerBay;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) { Instance = null; Inside = false; } }

    IEnumerator Start()
    {
        BuildUI();
        vehicles = FindAnyObjectByType<VehicleSystem>();
        for (int i = 0; i < 900; i++)
        {
            LundunTrain t = LundunTrain.Instance;
            LundunNavigation nav = LundunNavigation.Instance;
            if (t != null && t.Path != null && t.Path.Length > 1 && nav != null && nav.World != null) break;
            yield return null;
        }
        yield return new WaitForSeconds(1f);                       // after the pubs have picked their corners
        yield return Find();
        if (!Ready) { Debug.LogWarning("[Lundun] Arches: couldn't find three free arches under the viaduct."); yield break; }
        foreach (Bay b in bays) { BuildFront(b); Mark(b); }
        Debug.Log("[Lundun] Arches: arms dealer, Arches Motors and the lock-up at " + bays[1].at.ToString("F0"));
        StartCoroutine(Intro());
    }

    // ------------------------------------------------------------------ where they go
    struct Sample { public float d; public Vector3 p, t; }

    IEnumerator Find()
    {
        Vector3[] path = LundunTrain.Instance.Path;
        List<Sample> samples = new List<Sample>();
        for (int i = 0; i < path.Length - 1; i++)
        {
            Vector3 a = path[i], b = path[i + 1];
            Vector3 t = b - a; t.y = 0f;
            float len = t.magnitude;
            if (len < 0.5f) continue;
            t /= len;
            int n = Mathf.Max(1, Mathf.FloorToInt(len / 6f));
            for (int k = 0; k < n; k++)
            {
                Vector3 p = Vector3.Lerp(a, b, (k + 0.5f) / n);
                samples.Add(new Sample { d = LundunMissionRun.FlatDistance(p, LockUpStreet), p = p, t = t });
            }
        }
        samples.Sort((x, y) => x.d.CompareTo(y.d));
        int mask = LayerMask.GetMask("Default");
        int tries = 0;
        foreach (Sample s in samples)
        {
            foreach (float side in new[] { 1f, -1f })
                if (TryRow(s.p, s.t, side, mask)) yield break;
            if (++tries % 8 == 0) yield return null;
        }
    }

    /// From a point on the line, across to the viaduct's side: a run of three arches with open ground in front.
    bool TryRow(Vector3 P, Vector3 T, float side, int mask)
    {
        Vector3 N = Vector3.Cross(Vector3.up, T) * side;
        Vector3 outside = P + N * 13f;
        Vector3 g = LundunPlaces.Ground(new Vector3(outside.x, P.y - 6.75f, outside.z));
        if (P.y - g.y < 5f) return false;                                      // not up on the viaduct here
        float y = g.y + 1.2f;
        RaycastHit hit;
        if (!Physics.Raycast(new Vector3(outside.x, y, outside.z), -N, out hit, 12f, mask, QueryTriggerInteraction.Ignore)) return false;
        Vector3 hn = hit.normal; hn.y = 0f;
        if (hn.sqrMagnitude < 0.8f || Vector3.Dot(hn.normalized, N) < 0.9f) return false;
        N = hn.normalized;
        Vector3 along = Vector3.Cross(Vector3.up, N);
        Vector3 face = hit.point;

        // the wall's profile along the viaduct: piers stand out, the arches' infill is set back half a metre
        const float Span = 36f, Step = 0.2f;
        int count = Mathf.RoundToInt(2f * Span / Step) + 1;
        float[] dist = new float[count];
        float near = 99f;
        for (int i = 0; i < count; i++)
        {
            Vector3 o = face + along * (-Span + i * Step) + N * 2f; o.y = y;
            RaycastHit h;
            dist[i] = Physics.Raycast(o, -N, out h, 4f, mask, QueryTriggerInteraction.Ignore) && Vector3.Dot(h.normal, N) > 0.9f ? h.distance : 99f;
            if (dist[i] > 1f && dist[i] < near) near = dist[i];
        }
        if (near > 50f) return false;
        // 0 = something else, 1 = pier, 2 = recess
        int[] code = new int[count];
        for (int i = 0; i < count; i++)
            code[i] = Mathf.Abs(dist[i] - near) < 0.15f ? 1 : Mathf.Abs(dist[i] - near - 0.5f) < 0.15f ? 2 : 0;
        List<float> opens = new List<float>();
        for (int i = 1; i < count; i++)
        {
            if (code[i] != 2 || code[i - 1] != 1) continue;
            int j = i;
            while (j < count && code[j] == 2) j++;
            if (j >= count || code[j] != 1) { i = j; continue; }
            float w = (j - i) * Step;
            if (w > 5.8f && w < 7.0f) opens.Add(-Span + (i + j - 1) * 0.5f * Step);
            i = j;
        }
        // three in a row (8 m apart), the middle one nearest where we started
        float best = 1e9f; int pick = -1;
        for (int i = 0; i + 2 < opens.Count; i++)
        {
            float g1 = opens[i + 1] - opens[i], g2 = opens[i + 2] - opens[i + 1];
            if (g1 < 7.2f || g1 > 8.8f || g2 < 7.2f || g2 > 8.8f) continue;
            if (Mathf.Abs(opens[i + 1]) < best) { best = Mathf.Abs(opens[i + 1]); pick = i; }
        }
        if (pick < 0) return false;

        Quaternion rot = Quaternion.LookRotation(N);
        Vector3[] at = new Vector3[3];
        for (int k = 0; k < 3; k++)
        {
            Vector3 c = face + along * opens[pick + k] + N * (2f - (near + 0.5f));
            Vector3 front = LundunPlaces.Ground(new Vector3(c.x, g.y, c.z) + N * 1.5f);
            Vector3 far = LundunPlaces.Ground(new Vector3(c.x, g.y, c.z) + N * 5f);
            if (Mathf.Abs(front.y - far.y) > 0.45f) return false;               // steps or a slope in front
            c.y = front.y;
            at[k] = c;
            // open ground in front: room for a car at Arches Motors, a clear doorstep at the others (cars and people
            // don't count)
            Vector3 boxAt = k == 1 ? c + N * 3.6f + Vector3.up * 1.6f : c + along * (k == 0 ? -1.95f : 1.95f) + N * 1.9f + Vector3.up * 1.3f;
            Vector3 boxHalf = k == 1 ? new Vector3(2.8f, 1.2f, 2.6f) : new Vector3(0.7f, 0.9f, 1.0f);
            Collider[] cs = Physics.OverlapBox(boxAt, boxHalf, rot, mask, QueryTriggerInteraction.Ignore);
            foreach (Collider col in cs) if (col.attachedRigidbody == null) return false;
        }
        if (Reserved(at[1])) return false;

        Kind[] kinds = { Kind.Dealer, Kind.Motors, Kind.LockUp };
        for (int k = 0; k < 3; k++) bays.Add(NewBay(kinds[k], at[k], N, along, rot));
        return true;
    }

    static bool Reserved(Vector3 p)
    {
        foreach (LundunShopInterior s in LundunShopInterior.All) if (s != null && LundunMissionRun.FlatDistance(s.transform.position, p) < 40f) return true;
        LundunStoryShop shop = LundunStoryShop.Instance;
        if (shop != null && shop.Ready && LundunMissionRun.FlatDistance(shop.DoorOutside, p) < 30f) return true;
        LundunTrain t = LundunTrain.Instance;
        if (t != null && t.HasBrixtun && LundunMissionRun.FlatDistance(t.BrixtunEntrance, p) < 24f) return true;
        if (t != null && LundunMissionRun.FlatDistance(t.Entrance, p) < 24f) return true;
        LundunPubs pubs = LundunPubs.Instance;
        if (pubs != null) foreach (LundunPubs.Pub pb in pubs.All) if (LundunMissionRun.FlatDistance(pb.at, p) < 26f) return true;
        return false;
    }

    static Bay NewBay(Kind k, Vector3 at, Vector3 N, Vector3 along, Quaternion rot)
    {
        Bay b = new Bay { kind = k, at = at, outward = N, along = along, rot = rot };
        switch (k)
        {
            case Kind.Dealer:
                b.name = "ARMS DEALER"; b.sign = "SURPLUS & SECURITY"; b.sub = "EST. 1987 · TRADE ONLY"; b.letter = "A";
                b.panel = new Color(0.2f, 0.24f, 0.16f); b.glow = new Color(1f, 0.68f, 0.22f); b.doorX = -1.95f;
                break;
            case Kind.Motors:
                b.name = "ARCHES MOTORS"; b.sign = "ARCHES MOTORS"; b.sub = "RESPRAYS · REPAIRS · CASH FOR CARS"; b.letter = "R";
                b.panel = new Color(0.1f, 0.15f, 0.3f); b.glow = new Color(1f, 0.25f, 0.2f); b.doorX = 0f;
                break;
            default:
                b.name = "THE LOCK-UP"; b.sign = "THE LOCK-UP"; b.sub = "UNIT 7 · PRIVATE"; b.letter = "U";
                b.panel = new Color(0.09f, 0.22f, 0.15f); b.glow = new Color(1f, 0.92f, 0.75f); b.doorX = 1.95f;
                break;
        }
        return b;
    }

    void Mark(Bay b)
    {
        GameObject m = new GameObject("ArchMarker_" + b.kind);
        m.transform.SetParent(transform, false);
        m.transform.position = b.kind == Kind.Motors ? b.Forecourt : b.Door;
        b.marker = m.transform;
        LundunMinimap mm = FindAnyObjectByType<LundunMinimap>();
        if (mm != null) mm.Track(m.transform, b.glow, b.letter, 20f, false);
    }

    IEnumerator Intro()
    {
        yield return new WaitForSeconds(12f);
        LundunStoryData d = LundunStory.Data;
        if (d == null || d.flags.Contains("seen:arches")) yield break;
        while (LundunMissions.Running || LundunMissions.Talking) yield return new WaitForSeconds(3f);
        d.flags.Add("seen:arches"); LundunStory.MarkDirty();
        Toast("THE ARCHES are open under the railway\nGuns and armour (A), resprays and repairs (R), your lock-up (U)");
    }

    // ------------------------------------------------------------------ every frame
    void Update()
    {
        if (!Ready) return;
        if (Inside && !busy)
        {
            PlayerHealth ph = PlayerHealth.Instance;
            if (ph != null && ph.Body != null && ph.Body.position.y < InteriorY - 40f) Eject();     // wasted, busted, teleported
        }
        if (busy) return;
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.2f;
            ShowNear();
            Prompt();
        }
    }

    void ShowNear()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 p = Inside && inBay != null ? inBay.at : cam.transform.position;
        bool want = (bays[1].at - p).sqrMagnitude < 260f * 260f;
        foreach (Bay b in bays) if (b.go != null && b.go.activeSelf != want) b.go.SetActive(want);
    }

    void Prompt()
    {
        Offer was = offer;
        offer = Offer.None; offerBay = null;
        PlayerHealth ph = PlayerHealth.Instance;
        bool alive = ph != null && !ph.IsDead && !VehicleSystem.Locked;
        if (alive && Inside && inBay != null)
        {
            Vector3 l = Quaternion.Inverse(inBay.RoomRot) * (ph.Body.position - inBay.RoomOrigin);
            offerBay = inBay;
            if (l.z < -RoomLen / 2f + 1.2f && Mathf.Abs(l.x - inBay.RoomDoorX) < 1.3f) offer = Offer.Leave;
            else if (inBay.kind == Kind.Dealer && l.z > 0.2f && l.z < 1.3f && Mathf.Abs(l.x) < 2.4f) offer = Offer.Browse;
            else if (inBay.kind == Kind.LockUp && l.x < -1.2f && l.z > 0.2f && l.z < 2.9f) offer = Offer.Rest;
            else if (inBay.kind == Kind.LockUp && l.x > 1.3f && l.z > -2.6f && l.z < -0.5f) offer = Offer.Ammo;
        }
        else if (alive && ph.OnFoot && !(LundunBikes.Instance != null && LundunBikes.Instance.Riding))
        {
            Vector3 j = ph.Body.position;
            foreach (Bay b in bays)
            {
                if (b.kind == Kind.Motors) continue;
                if (LundunMissionRun.FlatDistance(b.Door, j) < 1.7f && Mathf.Abs(b.Door.y - j.y) < 2f) { offer = Offer.GoIn; offerBay = b; break; }
            }
        }
        else if (alive && vehicles != null && vehicles.IsDriving && vehicles.CurrentCar != null)
        {
            Bay m = Of(Kind.Motors);
            CarController car = vehicles.CurrentCar;
            if (LundunMissionRun.FlatDistance(car.transform.position, m.Forecourt) < 4.5f && Mathf.Abs(car.Speed) < 1.5f) { offer = Offer.Garage; offerBay = m; }
        }
        if (panelGo.activeSelf && (offer != panelFor)) ClosePanel();
        if (offer == Offer.None || panelGo.activeSelf) { buttonGo.SetActive(false); return; }

        bool mission = LundunMissions.Running;
        bool wanted = WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0;
        Color go = new Color(0.32f, 0.05f, 0.06f, 0.92f), grey = new Color(0.2f, 0.2f, 0.22f, 0.92f), blue = new Color(0.12f, 0.16f, 0.29f, 0.92f);
        switch (offer)
        {
            case Offer.GoIn:
                bool shut = mission || wanted;
                Set((shut ? "CLOSED" : "GO IN") + "\n<size=60%>" + offerBay.name + (mission ? " · not now" : wanted ? " · not with the police after you" : "") + "</size>", shut ? grey : go);
                break;
            case Offer.Leave: Set("LEAVE\n<size=60%>" + inBay.name + "</size>", blue); break;
            case Offer.Browse: Set("WHAT HAVE YOU GOT?\n<size=60%>guns · ammo · body armour</size>", new Color(0.55f, 0.36f, 0.06f, 0.92f)); break;
            case Offer.Rest: Set("REST\n<size=60%>full health · six hours pass</size>", blue); break;
            case Offer.Ammo:
                bool taken = LundunDay.DoneToday("lockup-ammo");
                Set(taken ? "AMMO TAKEN TODAY\n<size=60%>more tomorrow (from 06:00)</size>" : "TAKE AMMO\n<size=60%>free · once a day</size>", taken ? grey : new Color(0.15f, 0.35f, 0.18f, 0.92f));
                break;
            case Offer.Garage:
                Set(mission ? "CLOSED\n<size=60%>ARCHES MOTORS · not now</size>" : "ARCHES MOTORS\n<size=60%>resprays · repairs · cash for cars</size>", mission ? grey : go);
                break;
        }
        buttonGo.SetActive(true);
        if (was != offer) LundunHaptics.Light();
    }

    void Set(string text, Color c) { label.text = text; back.color = c; }

    void OnPress()
    {
        if (busy) return;
        bool mission = LundunMissions.Running;
        bool wanted = WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0;
        switch (offer)
        {
            case Offer.GoIn:
                if (mission || wanted) { LundunHaptics.Light(); return; }
                StartCoroutine(GoIn(offerBay));
                break;
            case Offer.Leave: StartCoroutine(Leave()); break;
            case Offer.Browse: OpenPanel(Offer.Browse); break;
            case Offer.Rest: StartCoroutine(Rest()); break;
            case Offer.Ammo: TakeAmmo(); break;
            case Offer.Garage:
                if (mission) { LundunHaptics.Light(); return; }
                OpenPanel(Offer.Garage);
                break;
        }
        buttonGo.SetActive(false);
        nextCheck = Time.time + 0.4f;
    }

    // ------------------------------------------------------------------ in and out
    IEnumerator GoIn(Bay b)
    {
        busy = true;
        yield return Fade(1f);
        if (b.room == null) b.room = BuildRoom(b);
        b.room.transform.SetPositionAndRotation(b.RoomOrigin, b.RoomRot);
        b.room.SetActive(true);
        Physics.SyncTransforms();
        inBay = b;
        if (b.kind == Kind.Dealer) dealer = Person(b, new Vector3(0.6f, 0f, 2.45f), 180f);
        Place(b.RoomOrigin + b.RoomRot * new Vector3(b.RoomDoorX, 0.1f, -RoomLen / 2f + 1.4f), b.RoomRot);
        Inside = true;
        yield return new WaitForSecondsRealtime(0.25f);
        yield return Fade(0f);
        busy = false;
    }

    IEnumerator Leave()
    {
        busy = true;
        ClosePanel();
        yield return Fade(1f);
        Bay b = inBay;
        Out();
        if (b != null) Place(b.Door + b.outward * 0.7f, Quaternion.LookRotation(b.outward));
        yield return new WaitForSecondsRealtime(0.2f);
        yield return Fade(0f);
        busy = false;
    }

    void Out()
    {
        Inside = false;
        if (dealer != null) Destroy(dealer);
        dealer = null;
        if (inBay != null && inBay.room != null) inBay.room.SetActive(false);
        inBay = null;
    }

    /// Wasted or busted inside: out without the walk.
    public void Eject()
    {
        if (!Inside) return;
        ClosePanel();
        Out();
    }

    GameObject Person(Bay b, Vector3 local, float yaw)
    {
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return null;
        GameObject g = pop.NewPerson(null);
        if (g == null) return null;
        NPCBrain br = g.GetComponent<NPCBrain>(); if (br != null) br.enabled = false;
        NavMeshAgent ag = g.GetComponent<NavMeshAgent>(); if (ag != null) ag.enabled = false;
        g.transform.SetPositionAndRotation(b.RoomOrigin + b.RoomRot * local, b.RoomRot * Quaternion.Euler(0f, yaw, 0f));
        g.SetActive(true);
        foreach (Animator a in g.GetComponentsInChildren<Animator>(true)) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        Health h = g.GetComponent<Health>(); if (h != null) h.Invulnerable = true;
        return g;
    }

    static void Place(Vector3 at, Quaternion rot)
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.Body == null) return;
        CharacterController cc = ph.Body.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        ph.Body.SetPositionAndRotation(at, rot);
        if (cc != null) cc.enabled = true;
    }

    IEnumerator Fade(float to)
    {
        fadeGo.SetActive(true);
        float from = fadeImage.color.a;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.4f)
        {
            fadeImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t));
            yield return null;
        }
        fadeImage.color = new Color(0f, 0f, 0f, to);
        if (to <= 0f) fadeGo.SetActive(false);
    }

    static void Toast(string s) { if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(s); }

    // ------------------------------------------------------------------ the lock-up
    IEnumerator Rest()
    {
        busy = true;
        yield return Fade(1f);
        if (LundunClock.Instance != null) LundunClock.Instance.AddHours(6f);
        if (PlayerHealth.Instance != null) PlayerHealth.Instance.Heal();
        LundunDrunk.Sober();
        LundunDaily.Note("rest");                                        // S8: a daily challenge
        LundunHaptics.Light();
        yield return new WaitForSecondsRealtime(1.2f);
        LundunStory.MarkDirty();
        yield return Fade(0f);
        Toast("You slept for six hours\nFully rested");
        busy = false;
    }

    void TakeAmmo()
    {
        if (LundunDay.DoneToday("lockup-ammo")) { LundunHaptics.Light(); Toast("You've had today's ammo\nMore tomorrow"); return; }
        LundunWeapons w = LundunWeapons.Instance;
        if (w == null) return;
        int gave = 0;
        foreach (LundunWeapons.Kind k in new[] { LundunWeapons.Kind.Pistol, LundunWeapons.Kind.Smg, LundunWeapons.Kind.Shotgun, LundunWeapons.Kind.Carbine })
        {
            if (!w.Owns(k)) continue;
            LundunWeapons.Weapon wp = w.Of(k);
            int need = wp.reserve - wp.spare;
            if (need > 0) { w.Give(k, need); gave += need; }
        }
        if (gave == 0) { Toast("You're already full up"); return; }
        LundunDay.MarkToday("lockup-ammo");
        LundunStory.MarkDirty();
        LundunHaptics.Medium();
        Toast("Ammo topped up\nMore tomorrow");
    }

    // ------------------------------------------------------------------ the menu panel (arms dealer, Arches Motors)
    void OpenPanel(Offer what)
    {
        panelFor = what;
        panelGo.SetActive(true);
        buttonGo.SetActive(false);
        Refresh();
    }

    void ClosePanel()
    {
        if (panelGo != null) panelGo.SetActive(false);
        panelFor = Offer.None;
    }

    void Refresh()
    {
        for (int i = rowsRt.childCount - 1; i >= 0; i--) Destroy(rowsRt.GetChild(i).gameObject);
        rowY = 0f;
        if (panelFor == Offer.Browse) DealerRows(); else if (panelFor == Offer.Garage) GarageRows();
        float h = -rowY + 210f;
        ((RectTransform)panelGo.transform).sizeDelta = new Vector2(820f, h);
    }

    // the arms dealer
    void DealerRows()
    {
        panelTitle.text = "ARMS DEALER";
        panelNote.text = "Cash only. No questions. You've got " + LundunWallet.Format(LundunWallet.Cash) + ".";
        LundunWeapons w = LundunWeapons.Instance;
        if (w == null) return;
        Gun(w, LundunWeapons.Kind.Smg, "SMG", "fast, light, thirsty", SmgPrice);
        Gun(w, LundunWeapons.Kind.Shotgun, "SHOTGUN", "up close, it ends arguments", ShotgunPrice);
        Gun(w, LundunWeapons.Kind.Carbine, "CARBINE", "accurate at range, full auto", CarbinePrice);
        Ammo(w, LundunWeapons.Kind.Pistol, "PISTOL MAGAZINE", 40);
        Ammo(w, LundunWeapons.Kind.Smg, "SMG MAGAZINE", 90);
        Ammo(w, LundunWeapons.Kind.Shotgun, "SHOTGUN SHELLS", 60);
        Ammo(w, LundunWeapons.Kind.Carbine, "CARBINE MAGAZINE", 150);
        PlayerHealth ph = PlayerHealth.Instance;
        bool full = ph != null && ph.Armour >= PlayerHealth.MaxArmour - 0.5f;
        Row("BODY ARMOUR", full ? "you're wearing a full vest" : "takes the hits before you do" + (ph != null && ph.Armour > 0.5f ? " · tops yours up" : ""),
            full ? "FULL" : LundunWallet.Format(ArmourPrice), !full, () =>
            {
                if (!Buy(ArmourPrice, "Body armour")) return;
                PlayerHealth.Instance.GiveArmour(PlayerHealth.MaxArmour);
                Toast("Body armour on");
            });
    }

    void Gun(LundunWeapons w, LundunWeapons.Kind k, string name, string sub, long price)
    {
        bool owned = w.Owns(k);
        Row(name, owned ? "you've got one" : sub, owned ? "OWNED" : LundunWallet.Format(price), !owned, () =>
        {
            if (!Buy(price, name)) return;
            LundunWeapons.Weapon wp = w.Of(k);
            wp.inMag = wp.magazine;
            w.Give(k, wp.magazine * 3);
            LundunStory.MarkDirty();
            Toast(name + " bought\nIt's in your weapon wheel");
        });
    }

    void Ammo(LundunWeapons w, LundunWeapons.Kind k, string name, long price)
    {
        if (!w.Owns(k)) return;
        LundunWeapons.Weapon wp = w.Of(k);
        bool full = wp.spare >= wp.reserve;
        Row(name, "×" + wp.magazine + " · you're carrying " + wp.spare, full ? "FULL" : LundunWallet.Format(price), !full, () =>
        {
            if (!Buy(price, name.ToLowerInvariant())) return;
            w.Give(k, Mathf.Min(wp.magazine, wp.reserve - wp.spare));
            LundunStory.MarkDirty();
        });
    }

    bool Buy(long price, string what)
    {
        if (!LundunWallet.CanAfford(price)) { LundunHaptics.Light(); Toast("Not enough cash\n" + what + " is " + LundunWallet.Format(price)); return false; }
        LundunWallet.Pay(price, what);
        LundunHaptics.Medium();
        return true;
    }

    // Arches Motors
    void GarageRows()
    {
        panelTitle.text = "ARCHES MOTORS";
        CarController car = vehicles != null ? vehicles.CurrentCar : null;
        if (car == null) { ClosePanel(); return; }
        WantedSystem ws = WantedSystem.Instance;
        int stars = ws != null ? ws.Stars : 0;
        bool seen = ws != null && ws.Seen;
        bool police = car.GetComponent<PoliceAI>() != null || car.GetComponentInChildren<PoliceOfficer>() != null;
        panelNote.text = police ? "\"That's a police car. Get it away from my arch.\"" : seen ? "\"Not with them watching. Lose them first.\"" : "You've got " + LundunWallet.Format(LundunWallet.Cash) + ".";
        if (police) return;

        LundunCarPaint paint = car.GetComponentInChildren<LundunCarPaint>();
        bool canPaint = paint != null && paint.Variants != null && paint.Variants.Length > 1;
        Row("RESPRAY", !canPaint ? "can't respray this one" : stars > 0 ? "a new colour · one star less" : "a new colour",
            LundunWallet.Format(ResprayPrice), canPaint && !seen, () => Service(car, 0, ResprayPrice));

        VehicleHealth vh = car.GetComponent<VehicleHealth>();
        float frac = vh != null && vh.Max > 0f ? Mathf.Clamp01(vh.Current / vh.Max) : 1f;
        long fix = RepairPrice(frac);
        bool dented = frac < 0.995f;
        Row("REPAIR", dented ? "good as new · " + Mathf.RoundToInt(frac * 100f) + "% now" : "nothing to fix", dented ? LundunWallet.Format(fix) : "—", dented && !seen,
            () => Service(car, 1, fix));

        bool mine = LundunPlaces.Instance != null && LundunPlaces.Instance.Kestrel != null && car.transform == LundunPlaces.Instance.Kestrel;
        long worth = SellPrice(car, frac);
        Row("SELL", mine ? "\"That's your own motor, keep it\"" : "the chop shop · cash now, the car's gone", mine ? "—" : LundunWallet.Format(worth), !mine && !seen,
            () => Service(car, 2, worth));
    }

    static long RepairPrice(float frac) { return (long)Mathf.Clamp(Mathf.Round((100f + 700f * (1f - frac)) / 10f) * 10f, 100f, 800f); }

    /// What the chop shop pays: by the model (£900–£3,000 when it's perfect), less the damage, never under £400.
    static long SellPrice(CarController car, float frac)
    {
        string model = car.gameObject.name.Replace("(Clone)", "").Trim();
        int h = 17; unchecked { foreach (char c in model) h = h * 31 + c; }
        float top = 900f + (Mathf.Abs(h) % 22) * 100f;
        return (long)Mathf.Clamp(Mathf.Round(top * (0.4f + 0.6f * frac) / 50f) * 50f, 400f, 3000f);
    }

    void Service(CarController car, int job, long price)
    {
        if (busy || car == null) return;
        if (job != 2 && !LundunWallet.CanAfford(price)) { LundunHaptics.Light(); Toast("Not enough cash\nThat's " + LundunWallet.Format(price)); return; }
        StartCoroutine(Work(car, job, price));
    }

    IEnumerator Work(CarController car, int job, long price)
    {
        busy = true;
        ClosePanel();
        buttonGo.SetActive(false);
        Bay m = Of(Kind.Motors);
        car.SetHandbrake(true);
        yield return Shutter(m, true);
        yield return Fade(1f);
        LundunHaptics.Light();
        yield return new WaitForSecondsRealtime(0.6f);
        LundunHaptics.Light();
        yield return new WaitForSecondsRealtime(0.6f);
        string toast = "";
        if (car != null)
        {
            switch (job)
            {
                case 0:
                    LundunWallet.Pay(price, "Respray");
                    LundunCarPaint paint = car.GetComponentInChildren<LundunCarPaint>();
                    if (paint != null && paint.Variants.Length > 1)
                    {
                        Material now = paint.Body != null && paint.Slot < paint.Body.sharedMaterials.Length ? paint.Body.sharedMaterials[paint.Slot] : null;
                        int cur = Array.IndexOf(paint.Variants, now);
                        int next = (cur + 1 + UnityEngine.Random.Range(0, paint.Variants.Length - 1)) % paint.Variants.Length;
                        if (next == cur) next = (cur + 1) % paint.Variants.Length;
                        paint.Use(next);
                    }
                    WantedSystem ws = WantedSystem.Instance;
                    bool had = ws != null && ws.Stars > 0;
                    if (had) ws.DropStar();
                    toast = had ? (ws.Stars > 0 ? "Resprayed\nOne star less" : "Resprayed\nThe police have lost you") : "Resprayed\nLike new";
                    break;
                case 1:
                    LundunWallet.Pay(price, "Repairs");
                    VehicleHealth vh = car.GetComponent<VehicleHealth>();
                    if (vh != null) vh.Repair();
                    toast = "Repaired\nGood as new";
                    break;
                case 2:
                    if (vehicles != null && vehicles.CurrentCar == car) vehicles.ExitCar();
                    Destroy(car.gameObject);
                    Place(m.Door + m.outward * 1.6f + m.along * 3.2f, Quaternion.LookRotation(m.outward));
                    LundunWallet.Earn(price, "Chop shop");
                    toast = "Sold to the chop shop";
                    break;
            }
        }
        LundunStory.MarkDirty();
        yield return new WaitForSecondsRealtime(0.3f);
        if (car != null) car.SetHandbrake(false);
        yield return Fade(0f);
        if (toast.Length > 0) Toast(toast);
        yield return Shutter(m, false);
        busy = false;
        nextCheck = Time.time + 1f;
    }

    IEnumerator Shutter(Bay m, bool up)
    {
        if (m == null || m.shutter == null) yield break;
        float to = up ? 3.2f : 0f;
        Vector3 p = m.shutter.localPosition;
        float from = p.y;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.8f)
        {
            p.y = Mathf.Lerp(from, to, t * t * (3f - 2f * t));
            m.shutter.localPosition = p;
            yield return null;
        }
        p.y = to; m.shutter.localPosition = p;
    }

    // ------------------------------------------------------------------ the fronts
    // Local frame: the opening's middle on the ground at the infill's face; +Z out to the street, +X along.
    // The arch: x in ±3.2, straight sides up to 2.6, then a semicircle (radius 3.2) up to 5.8 in the middle.
    // materials: 0 panel, 1 dark steel, 2 shutter, 3 sign glow, 4 sign box, 5 lamp, 6 yellow lines, 7 door, 8 inside dark
    struct Hole { public float x0, x1, h; public Hole(float a, float b, float c) { x0 = a; x1 = b; h = c; } }

    static float ArchTop(float x) { return Spring + Mathf.Sqrt(Mathf.Max(0f, Rad * Rad - x * x)); }

    /// Fills the arch with vertical strips (z from z0 to z1), leaving the holes open.
    static void Strips(LundunMeshKit k, int mat, float z0, float z1, Hole[] holes)
    {
        for (float x = -Half; x < Half - 0.01f; x += 0.2f)
        {
            float xc = x + 0.1f;
            float top = Mathf.Min(ArchTop(x), ArchTop(x + 0.2f)) - 0.03f;
            float y0 = 0f;
            foreach (Hole h in holes) if (xc > h.x0 && xc < h.x1) y0 = Mathf.Max(y0, h.h);
            if (top - y0 < 0.02f) continue;
            k.Box(mat, new Vector3(xc, (y0 + top) * 0.5f, (z0 + z1) * 0.5f), new Vector3(0.2f, top - y0, z1 - z0));
        }
    }

    void BuildFront(Bay b)
    {
        LundunMeshKit k = new LundunMeshKit(9);
        Hole door = new Hole(b.doorX - 0.5f, b.doorX + 0.5f, 2.15f);
        Hole shut;
        float signY;
        if (b.kind == Kind.Dealer) { shut = new Hole(-1.1f, 2.7f, 2.7f); signY = 3.8f; }
        else if (b.kind == Kind.Motors) { shut = new Hole(-2.4f, 2.4f, 3.5f); signY = 4.38f; }
        else { shut = new Hole(-2.7f, 0.95f, 2.9f); signY = 3.88f; }
        Hole[] holes = b.kind == Kind.Motors ? new[] { shut } : new[] { door, shut };
        Strips(k, 0, 0.02f, 0.16f, holes);
        // the shutter's housing and guides, the dark garage behind it
        float sw = shut.x1 - shut.x0, sc = (shut.x0 + shut.x1) * 0.5f;
        k.Box(1, new Vector3(sc, shut.h + 0.2f, 0.2f), new Vector3(sw + 0.24f, 0.42f, 0.36f));
        k.Box(1, new Vector3(shut.x0 - 0.04f, shut.h * 0.5f, 0.12f), new Vector3(0.1f, shut.h, 0.16f));
        k.Box(1, new Vector3(shut.x1 + 0.04f, shut.h * 0.5f, 0.12f), new Vector3(0.1f, shut.h, 0.16f));
        k.Box(8, new Vector3(sc, shut.h * 0.5f, 0.005f), new Vector3(sw, shut.h, 0.01f));
        if (b.kind != Kind.Motors)
        {
            // the door: steel frame, the door itself set back, a handle and a little light over it
            k.Box(1, new Vector3(door.x0 - 0.04f, 1.08f, 0.12f), new Vector3(0.08f, 2.16f, 0.16f));
            k.Box(1, new Vector3(door.x1 + 0.04f, 1.08f, 0.12f), new Vector3(0.08f, 2.16f, 0.16f));
            k.Box(1, new Vector3(b.doorX, 2.19f, 0.12f), new Vector3(1.16f, 0.08f, 0.16f));
            k.Box(7, new Vector3(b.doorX, 1.07f, 0.06f), new Vector3(1.0f, 2.14f, 0.06f));
            k.Box(1, new Vector3(b.doorX + (b.doorX < 0 ? 0.36f : -0.36f), 1.05f, 0.11f), new Vector3(0.05f, 0.22f, 0.05f));
            k.Box(5, new Vector3(b.doorX, 2.42f, 0.24f), new Vector3(0.3f, 0.1f, 0.16f));
        }
        if (b.kind == Kind.Dealer)
        {
            k.Box(1, new Vector3(-2.95f, 2.45f, 0.3f), new Vector3(0.14f, 0.14f, 0.32f));          // a camera over the door
            k.Box(1, new Vector3(-2.95f, 2.6f, 0.2f), new Vector3(0.04f, 0.18f, 0.04f));
        }
        if (b.kind == Kind.Motors)
        {
            // the forecourt: a yellow box to stop in
            float z0 = 0.8f, z1 = 7.2f, x = 2.7f, y = 0.012f;
            k.Box(6, new Vector3(-x, y, (z0 + z1) * 0.5f), new Vector3(0.14f, 0.02f, z1 - z0));
            k.Box(6, new Vector3(x, y, (z0 + z1) * 0.5f), new Vector3(0.14f, 0.02f, z1 - z0));
            k.Box(6, new Vector3(0f, y, z1), new Vector3(2f * x + 0.14f, 0.02f, 0.14f));
            k.Box(6, new Vector3(0f, y, z0), new Vector3(2f * x + 0.14f, 0.02f, 0.14f));
        }
        // the lightbox sign and a lamp over it
        k.Box(4, new Vector3(0f, signY, 0.27f), new Vector3(4.3f, 0.9f, 0.24f));
        k.Box(3, new Vector3(0f, signY, 0.395f), new Vector3(4.1f, 0.76f, 0.02f));
        k.Box(1, new Vector3(0f, signY + 0.62f, 0.32f), new Vector3(0.06f, 0.06f, 0.4f));
        k.Box(5, new Vector3(0f, signY + 0.58f, 0.52f), new Vector3(0.36f, 0.08f, 0.14f));

        GameObject g = new GameObject("Arch_" + b.kind);
        g.transform.SetParent(transform, false);
        g.transform.SetPositionAndRotation(b.at, b.rot);
        g.AddComponent<MeshFilter>().sharedMesh = k.Build("ArchFront_" + b.kind);
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterials = new[]
        {
            LundunMeshKit.Mat("ArchPanel_" + b.kind, b.panel, 0.25f, Color.black),
            Steel,
            ShutterMat,
            LundunMeshKit.Mat("ArchSign_" + b.kind, b.glow, 0.4f, b.glow * 1.6f),
            LundunMeshKit.Mat("ArchSignBox", new Color(0.05f, 0.05f, 0.06f), 0.5f, Color.black),
            LampMat,
            LundunMeshKit.Mat("ArchLines", new Color(0.95f, 0.78f, 0.1f), 0.2f, Color.black),
            LundunMeshKit.Mat("ArchDoor_" + b.kind, Color.Lerp(b.panel, Color.black, 0.35f), 0.35f, Color.black),
            LundunMeshKit.Mat("ArchDark", new Color(0.02f, 0.02f, 0.025f), 0.1f, Color.black),
        };
        Words(g.transform, b.sign, new Vector3(0f, signY + 0.08f, 0.41f), 180f, new Vector2(3.9f, 0.48f), 3.4f, SignInk(b), false);
        Words(g.transform, b.sub, new Vector3(0f, signY - 0.25f, 0.41f), 180f, new Vector2(3.8f, 0.16f), 1.2f, SignInk(b), false);

        // the roller shutter (moves: Arches Motors opens it for a job)
        LundunMeshKit s = new LundunMeshKit(2);
        for (float yy = 0.06f; yy < shut.h; yy += 0.11f)
        {
            s.Box(0, new Vector3(0f, yy, 0.05f), new Vector3(sw, 0.09f, 0.04f));
            s.Box(1, new Vector3(0f, yy + 0.05f, 0.04f), new Vector3(sw, 0.02f, 0.03f));
        }
        s.Box(1, new Vector3(0f, 0.04f, 0.06f), new Vector3(sw, 0.08f, 0.06f));
        GameObject sg = new GameObject("Shutter");
        sg.transform.SetParent(g.transform, false);
        sg.transform.localPosition = new Vector3(sc, 0f, 0f);
        sg.AddComponent<MeshFilter>().sharedMesh = s.Build("ArchShutter_" + b.kind);
        sg.AddComponent<MeshRenderer>().sharedMaterials = new[] { b.kind == Kind.LockUp ? LundunMeshKit.Mat("ArchShutterGreen", new Color(0.16f, 0.3f, 0.2f), 0.4f, Color.black) : ShutterMat, Steel };
        b.shutter = sg.transform;
        if (b.kind == Kind.Motors)
        {
            // what's behind it: a lit workshop (a strip light and a lift), only seen while the shutter's up
            LundunMeshKit w = new LundunMeshKit(2);
            w.Box(1, new Vector3(sc, shut.h - 0.25f, 0.03f), new Vector3(1.6f, 0.06f, 0.02f));
            w.Box(0, new Vector3(sc - 1.5f, 1.2f, 0.02f), new Vector3(0.18f, 2.4f, 0.02f));
            w.Box(0, new Vector3(sc + 1.5f, 1.2f, 0.02f), new Vector3(0.18f, 2.4f, 0.02f));
            GameObject wg = new GameObject("Workshop");
            wg.transform.SetParent(g.transform, false);
            wg.AddComponent<MeshFilter>().sharedMesh = w.Build("ArchWorkshop");
            wg.AddComponent<MeshRenderer>().sharedMaterials = new[] { LundunMeshKit.Mat("ArchLift", new Color(0.8f, 0.55f, 0.1f), 0.4f, Color.black), LampMat };
        }

        // light on the street in front
        GameObject lg = new GameObject("ArchLight");
        lg.transform.SetParent(g.transform, false);
        lg.transform.localPosition = new Vector3(0f, signY + 0.2f, 1.7f);
        Light li = lg.AddComponent<Light>();
        li.type = LightType.Point; li.range = 8f; li.intensity = 1.6f; li.shadows = LightShadows.None;
        li.color = Color.Lerp(b.glow, new Color(1f, 0.85f, 0.6f), 0.5f);
        b.go = g;
    }

    static Color SignInk(Bay b) { return b.kind == Kind.LockUp ? new Color(0.08f, 0.2f, 0.12f) : b.kind == Kind.Motors ? Color.white : new Color(0.12f, 0.1f, 0.05f); }

    private static Material steelMat, shutterMat, lampMat;
    static Material Steel { get { if (steelMat == null) steelMat = LundunMeshKit.Mat("ArchSteel", new Color(0.12f, 0.12f, 0.13f), 0.45f, Color.black); return steelMat; } }
    static Material ShutterMat { get { if (shutterMat == null) shutterMat = LundunMeshKit.Mat("ArchShutter", new Color(0.55f, 0.57f, 0.58f), 0.55f, Color.black); return shutterMat; } }
    static Material LampMat { get { if (lampMat == null) lampMat = LundunMeshKit.Mat("ArchLamp", new Color(1f, 0.92f, 0.75f), 0.5f, new Color(1f, 0.85f, 0.6f) * 2.5f); return lampMat; } }

    static void Words(Transform parent, string text, Vector3 at, float yaw, Vector2 box, float size, Color colour, bool wrap)
    {
        GameObject g = new GameObject("Words", typeof(RectTransform));
        g.transform.SetParent(parent, false);
        g.transform.localPosition = at;
        g.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        ((RectTransform)g.transform).sizeDelta = box;
        TextMeshPro t = g.AddComponent<TextMeshPro>();
        t.text = text; t.color = colour; t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        t.enableAutoSizing = true; t.fontSizeMin = 0.2f; t.fontSizeMax = size; t.fontSize = size;
        t.characterSpacing = 4f;
        MeshRenderer r = g.GetComponent<MeshRenderer>();
        if (r != null) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
    }

    // ------------------------------------------------------------------ the rooms (high above the arch; a fade in and out)
    // Local frame: +Z into the arch, the street door at z = -6. A brick barrel vault, whitewashed walls, concrete floor.
    // materials: 0 concrete, 1 brick, 2 whitewash, 3 wood, 4 dark steel, 5 olive crates, 6 strip light, 7 accent,
    //            8 fabric, 9 black
    GameObject BuildRoom(Bay b)
    {
        GameObject room = new GameObject("ArchRoom_" + b.kind);
        room.transform.SetParent(transform, false);
        LundunMeshKit k = new LundunMeshKit(10);
        float L = RoomLen / 2f;
        k.Box(0, new Vector3(0f, -0.1f, 0f), new Vector3(2f * Half, 0.2f, RoomLen));
        k.Box(2, new Vector3(-Half - 0.1f, Spring / 2f, 0f), new Vector3(0.2f, Spring, RoomLen));
        k.Box(2, new Vector3(Half + 0.1f, Spring / 2f, 0f), new Vector3(0.2f, Spring, RoomLen));
        // the vault
        const int Seg = 16;
        for (int i = 0; i < Seg; i++)
        {
            float t0 = Mathf.PI - Mathf.PI * i / Seg, t1 = Mathf.PI - Mathf.PI * (i + 1) / Seg;
            Vector2 p0 = new Vector2(Rad * Mathf.Cos(t0), Spring + Rad * Mathf.Sin(t0));
            Vector2 p1 = new Vector2(Rad * Mathf.Cos(t1), Spring + Rad * Mathf.Sin(t1));
            Vector2 mid = (p0 + p1) * 0.5f, d = p1 - p0;
            Vector2 outN = new Vector2(mid.x, mid.y - Spring).normalized;
            Vector2 c = mid + outN * 0.11f;
            k.Box(1, new Vector3(c.x, c.y, 0f), new Vector3(d.magnitude + 0.04f, 0.22f, RoomLen), Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg));
        }
        // the ends: the street wall (with the door) and the brick back wall
        Hole door = new Hole(b.RoomDoorX - 0.55f, b.RoomDoorX + 0.55f, 2.2f);
        StripsAt(k, 2, -L - 0.2f, -L, new[] { door });
        k.Box(4, new Vector3(b.RoomDoorX, 1.1f, -L + 0.03f), new Vector3(1.1f, 2.2f, 0.06f));
        k.Box(9, new Vector3(b.RoomDoorX + (b.RoomDoorX < 0 ? 0.38f : -0.38f), 1.05f, -L + 0.08f), new Vector3(0.05f, 0.2f, 0.05f));
        StripsAt(k, 1, L, L + 0.2f, new Hole[0]);
        // strip lights along the crown
        for (float z = -L + 2f; z < L - 1f; z += 3.2f)
        {
            k.Box(4, new Vector3(0f, Spring + Rad - 0.3f, z), new Vector3(0.18f, 0.06f, 1.5f));
            k.Box(6, new Vector3(0f, Spring + Rad - 0.34f, z), new Vector3(0.12f, 0.03f, 1.4f));
        }
        List<Bounds> solids = new List<Bounds>();
        if (b.kind == Kind.Dealer) DealerRoom(k, solids, room.transform, b); else LockUpRoom(k, solids, room.transform);

        GameObject body = new GameObject("Room");
        body.transform.SetParent(room.transform, false);
        body.AddComponent<MeshFilter>().sharedMesh = k.Build("ArchRoom_" + b.kind);
        body.AddComponent<MeshRenderer>().sharedMaterials = new[]
        {
            LundunMeshKit.Mat("ArchConcrete", new Color(0.42f, 0.41f, 0.39f), 0.2f, Color.black),
            LundunMeshKit.Mat("ArchBrick", new Color(0.45f, 0.22f, 0.15f), 0.1f, Color.black),
            LundunMeshKit.Mat("ArchWhitewash", new Color(0.82f, 0.8f, 0.74f), 0.1f, Color.black),
            LundunMeshKit.Mat("ArchWood", new Color(0.36f, 0.22f, 0.12f), 0.35f, Color.black),
            Steel,
            LundunMeshKit.Mat("ArchCrate", new Color(0.24f, 0.28f, 0.16f), 0.25f, Color.black),
            LundunMeshKit.Mat("ArchTube", new Color(0.95f, 0.97f, 1f), 0.5f, new Color(0.9f, 0.95f, 1f) * 2.2f),
            LundunMeshKit.Mat("ArchAccent_" + b.kind, b.glow, 0.4f, b.glow * 0.8f),
            LundunMeshKit.Mat("ArchFabric_" + b.kind, b.kind == Kind.Dealer ? new Color(0.22f, 0.27f, 0.36f) : new Color(0.3f, 0.3f, 0.32f), 0.15f, Color.black),
            LundunMeshKit.Mat("ArchBlack", new Color(0.03f, 0.03f, 0.03f), 0.4f, Color.black),
        };
        // solid: floor, walls, the ends, a ceiling, the furniture
        Solid(room, new Vector3(0f, -0.1f, 0f), new Vector3(2f * Half, 0.2f, RoomLen));
        Solid(room, new Vector3(-Half - 0.1f, 3f, 0f), new Vector3(0.2f, 6f, RoomLen));
        Solid(room, new Vector3(Half + 0.1f, 3f, 0f), new Vector3(0.2f, 6f, RoomLen));
        Solid(room, new Vector3(0f, 3f, -L - 0.1f), new Vector3(2f * Half, 6f, 0.2f));
        Solid(room, new Vector3(0f, 3f, L + 0.1f), new Vector3(2f * Half, 6f, 0.2f));
        Solid(room, new Vector3(0f, Spring + Rad + 0.2f, 0f), new Vector3(2f * Half, 0.2f, RoomLen));
        foreach (Bounds sb in solids) Solid(room, sb.center, sb.size);
        foreach (Vector3 l in new[] { new Vector3(0f, 4.6f, -2.5f), new Vector3(0f, 4.6f, 2.5f) })
        {
            GameObject lg = new GameObject("ArchRoomLight");
            lg.transform.SetParent(room.transform, false);
            lg.transform.localPosition = l;
            Light li = lg.AddComponent<Light>();
            li.type = LightType.Point; li.range = 10f; li.intensity = 2f; li.shadows = LightShadows.None;
            li.color = b.kind == Kind.Dealer ? new Color(1f, 0.82f, 0.6f) : new Color(0.9f, 0.95f, 1f);
        }
        foreach (Transform t in room.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
        room.SetActive(false);
        return room;
    }

    static void StripsAt(LundunMeshKit k, int mat, float z0, float z1, Hole[] holes) { Strips(k, mat, z0, z1, holes); }

    static void Solid(GameObject room, Vector3 c, Vector3 size)
    {
        BoxCollider bc = room.AddComponent<BoxCollider>();
        bc.center = c; bc.size = size;
    }

    static void Add(LundunMeshKit k, List<Bounds> solids, int mat, Vector3 c, Vector3 size, bool solid)
    {
        k.Box(mat, c, size);
        if (solid) solids.Add(new Bounds(c, size));
    }

    /// The arms dealer: a counter across the arch, guns on a pegboard behind it, crates of ammo, a vest on a stand.
    void DealerRoom(LundunMeshKit k, List<Bounds> solids, Transform room, Bay b)
    {
        float L = RoomLen / 2f;
        Add(k, solids, 3, new Vector3(-0.3f, 0.52f, 1.75f), new Vector3(4.6f, 1.04f, 0.7f), true);           // the counter
        Add(k, solids, 4, new Vector3(-0.3f, 1.07f, 1.75f), new Vector3(4.8f, 0.06f, 0.82f), false);
        Add(k, solids, 3, new Vector3(2.75f, 0.52f, 2.6f), new Vector3(0.7f, 1.04f, 2.4f), true);            // its return to the wall
        for (int i = 0; i < 4; i++) Add(k, solids, 5, new Vector3(-1.9f + i * 0.5f, 1.17f, 1.7f), new Vector3(0.36f, 0.16f, 0.24f), false);   // ammo tins
        Add(k, solids, 9, new Vector3(0f, 2.0f, L - 0.06f), new Vector3(5.4f, 2.3f, 0.06f), false);           // the pegboard
        for (int r = 0; r < 2; r++) Add(k, solids, 4, new Vector3(0f, 1.55f + r * 0.95f, L - 0.14f), new Vector3(5.2f, 0.05f, 0.14f), false);
        // crates down the left wall, a pallet of them on the right by the door
        float[] cz = { -4.4f, -3.4f, -2.4f, -0.4f };
        foreach (float z in cz) Add(k, solids, 5, new Vector3(-2.7f, 0.35f, z), new Vector3(0.9f, 0.7f, 0.85f), true);
        Add(k, solids, 5, new Vector3(-2.7f, 1.0f, -3.9f), new Vector3(0.85f, 0.6f, 0.8f), true);
        Add(k, solids, 3, new Vector3(2.7f, 0.08f, -1.8f), new Vector3(1.0f, 0.16f, 1.0f), true);
        Add(k, solids, 5, new Vector3(2.7f, 0.5f, -1.8f), new Vector3(0.9f, 0.68f, 0.9f), true);
        // a vest on a stand, by the counter
        Add(k, solids, 9, new Vector3(2.4f, 0.6f, 0.4f), new Vector3(0.06f, 1.2f, 0.06f), false);
        Add(k, solids, 8, new Vector3(2.4f, 1.45f, 0.4f), new Vector3(0.5f, 0.62f, 0.26f), true);
        Add(k, solids, 9, new Vector3(2.4f, 0.03f, 0.4f), new Vector3(0.5f, 0.06f, 0.5f), false);
        // the guns on the pegboard
        LundunWeapons.Kind[] top = { LundunWeapons.Kind.Smg, LundunWeapons.Kind.Carbine, LundunWeapons.Kind.Shotgun };
        for (int i = 0; i < 3; i++) Hang(room, top[i], new Vector3(-1.7f + i * 1.7f, 2.75f, L - 0.2f));
        for (int i = 0; i < 4; i++) Hang(room, LundunWeapons.Kind.Pistol, new Vector3(-1.8f + i * 1.2f, 1.82f, L - 0.2f));
        Words(room, "CASH ONLY · NO QUESTIONS", new Vector3(0f, 3.55f, L - 0.08f), 0f, new Vector2(4.6f, 0.36f), 2.4f, b.glow, false);
    }

    static void Hang(Transform room, LundunWeapons.Kind kind, Vector3 at)
    {
        GameObject gun = LundunGunModels.Build(kind);
        if (gun == null) return;
        gun.transform.SetParent(room, false);
        gun.transform.localPosition = at;
        gun.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
        foreach (Collider c in gun.GetComponentsInChildren<Collider>(true)) Destroy(c);
    }

    /// James's lock-up: a battered sofa, a workbench, an ammo locker, and a project car under a sheet.
    void LockUpRoom(LundunMeshKit k, List<Bounds> solids, Transform room)
    {
        float L = RoomLen / 2f;
        Add(k, solids, 8, new Vector3(-2.45f, 0.24f, 1.5f), new Vector3(1.2f, 0.48f, 2.6f), true);          // the sofa
        Add(k, solids, 8, new Vector3(-2.95f, 0.75f, 1.5f), new Vector3(0.3f, 0.75f, 2.6f), true);
        Add(k, solids, 8, new Vector3(-2.45f, 0.62f, 0.3f), new Vector3(1.2f, 0.32f, 0.22f), true);
        Add(k, solids, 8, new Vector3(-2.45f, 0.62f, 2.7f), new Vector3(1.2f, 0.32f, 0.22f), true);
        Add(k, solids, 7, new Vector3(-1.1f, 0.005f, 1.5f), new Vector3(2.0f, 0.01f, 2.6f), false);         // a rug
        Add(k, solids, 3, new Vector3(-1.0f, 0.22f, 1.5f), new Vector3(0.6f, 0.44f, 1.0f), true);           // a crate for a table
        Add(k, solids, 3, new Vector3(2.65f, 0.92f, 1.6f), new Vector3(0.9f, 0.08f, 3.0f), true);           // the workbench
        foreach (float z in new[] { 0.25f, 2.95f }) Add(k, solids, 4, new Vector3(2.65f, 0.44f, z), new Vector3(0.8f, 0.88f, 0.08f), false);
        Add(k, solids, 9, new Vector3(Half - 0.04f, 1.75f, 1.6f), new Vector3(0.04f, 1.1f, 2.8f), false);   // the tool board
        for (int i = 0; i < 6; i++) Add(k, solids, 4, new Vector3(Half - 0.1f, 1.6f + (i % 2) * 0.35f, 0.5f + i * 0.45f), new Vector3(0.06f, 0.3f, 0.06f), false);
        Add(k, solids, 4, new Vector3(2.6f, 0.55f, -1.5f), new Vector3(0.9f, 1.1f, 0.7f), true);            // the ammo locker
        Add(k, solids, 5, new Vector3(2.14f, 0.75f, -1.5f), new Vector3(0.02f, 0.5f, 0.6f), false);
        Add(k, solids, 8, new Vector3(0f, 0.62f, 3.9f), new Vector3(1.9f, 1.0f, 3.9f), true);               // the car under a sheet
        Add(k, solids, 8, new Vector3(0f, 1.3f, 3.7f), new Vector3(1.6f, 0.42f, 2.0f), false);
        Add(k, solids, 9, new Vector3(-2.75f, 0.9f, -3.6f), new Vector3(0.7f, 1.8f, 0.65f), true);          // a fridge
        Add(k, solids, 2, new Vector3(-2.75f, 0.9f, -3.27f), new Vector3(0.66f, 1.74f, 0.02f), false);
        Words(room, "REST", new Vector3(-Half + 0.05f, 2.0f, 1.5f), -90f, new Vector2(1.6f, 0.4f), 2.4f, new Color(0.9f, 0.85f, 0.7f), false);
        Words(room, "AMMO", new Vector3(2.12f, 1.25f, -1.5f), 90f, new Vector2(0.6f, 0.24f), 1.6f, new Color(0.95f, 0.85f, 0.3f), false);
    }

    // ------------------------------------------------------------------ UI
    void BuildUI()
    {
        GameObject cg = new GameObject("ArchesUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 63;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();

        buttonGo = new GameObject("ArchPrompt", typeof(RectTransform));
        buttonGo.transform.SetParent(cg.transform, false);
        back = buttonGo.AddComponent<Image>();
        Pill(back, 0.8f);
        Button b = buttonGo.AddComponent<Button>(); b.targetGraphic = back; b.onClick.AddListener(OnPress);
        RectTransform rt = (RectTransform)buttonGo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 230f); rt.sizeDelta = new Vector2(520f, 120f);
        label = Text(rt, "", 38f, Color.white);
        buttonGo.SetActive(false);

        // the menu: a panel in the middle, a row per thing, CLOSE at the bottom
        panelGo = new GameObject("ArchPanel", typeof(RectTransform));
        panelGo.transform.SetParent(cg.transform, false);
        Image pi = panelGo.AddComponent<Image>(); Pill(pi, 2.4f); pi.color = new Color(0.06f, 0.06f, 0.08f, 0.94f);
        RectTransform pr = (RectTransform)panelGo.transform;
        pr.anchorMin = pr.anchorMax = new Vector2(0.5f, 0.5f); pr.pivot = new Vector2(0.5f, 0.5f);
        pr.anchoredPosition = new Vector2(0f, 20f); pr.sizeDelta = new Vector2(820f, 600f);
        panelTitle = Text(pr, "", 44f, Color.white);
        Pin(panelTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(760f, 56f));
        panelNote = Text(pr, "", 22f, new Color(1f, 1f, 1f, 0.6f));
        panelNote.fontStyle = FontStyles.Normal;
        Pin(panelNote.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(760f, 32f));
        rowsRt = (RectTransform)new GameObject("Rows", typeof(RectTransform)).transform;
        rowsRt.SetParent(pr, false);
        rowsRt.anchorMin = new Vector2(0f, 1f); rowsRt.anchorMax = new Vector2(1f, 1f); rowsRt.pivot = new Vector2(0.5f, 1f);
        rowsRt.anchoredPosition = new Vector2(0f, -120f); rowsRt.sizeDelta = new Vector2(-40f, 10f);
        RectTransform close = (RectTransform)new GameObject("Close", typeof(RectTransform)).transform;
        close.SetParent(pr, false);
        close.anchorMin = close.anchorMax = new Vector2(0.5f, 0f); close.pivot = new Vector2(0.5f, 0f);
        close.anchoredPosition = new Vector2(0f, 20f); close.sizeDelta = new Vector2(260f, 64f);
        Image ci = close.gameObject.AddComponent<Image>(); Pill(ci, 0.94f); ci.color = new Color(0.12f, 0.16f, 0.29f, 0.95f);
        Button cb = close.gameObject.AddComponent<Button>(); cb.targetGraphic = ci;
        cb.onClick.AddListener(() => { LundunHaptics.Light(); ClosePanel(); nextCheck = Time.time + 0.5f; });
        close.gameObject.AddComponent<LundunPressFX>();
        Text(close, "CLOSE", 28f, Color.white);
        panelGo.SetActive(false);

        fadeGo = new GameObject("ArchFade", typeof(RectTransform));
        fadeGo.transform.SetParent(cg.transform, false);
        RectTransform fr = (RectTransform)fadeGo.transform; fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = fr.offsetMax = Vector2.zero;
        fadeImage = fadeGo.AddComponent<Image>(); fadeImage.color = new Color(0f, 0f, 0f, 0f);
        fadeGo.SetActive(false);
    }

    private float rowY;

    /// One row of the menu: what, a line about it, the price (or OWNED / FULL) on a button.
    void Row(string what, string sub, string price, bool on, Action buy)
    {
        RectTransform row = (RectTransform)new GameObject("Row", typeof(RectTransform)).transform;
        row.SetParent(rowsRt, false);
        row.anchorMin = new Vector2(0f, 1f); row.anchorMax = new Vector2(1f, 1f); row.pivot = new Vector2(0.5f, 1f);
        row.anchoredPosition = new Vector2(0f, rowY); row.sizeDelta = new Vector2(0f, 74f);
        Image bg = row.gameObject.AddComponent<Image>(); Pill(bg, 1.2f); bg.color = new Color(1f, 1f, 1f, 0.06f);
        TextMeshProUGUI t = Text(row, what, 27f, on ? Color.white : new Color(1f, 1f, 1f, 0.45f));
        t.alignment = TextAlignmentOptions.MidlineLeft;
        Pin(t.rectTransform, new Vector2(0f, 0.5f), new Vector2(22f, 12f), new Vector2(520f, 34f), new Vector2(0f, 0.5f));
        TextMeshProUGUI s = Text(row, sub, 19f, new Color(1f, 1f, 1f, 0.55f));
        s.fontStyle = FontStyles.Normal; s.alignment = TextAlignmentOptions.MidlineLeft; s.overflowMode = TextOverflowModes.Ellipsis;
        Pin(s.rectTransform, new Vector2(0f, 0.5f), new Vector2(22f, -16f), new Vector2(540f, 26f), new Vector2(0f, 0.5f));
        RectTransform pb = (RectTransform)new GameObject("Buy", typeof(RectTransform)).transform;
        pb.SetParent(row, false);
        pb.anchorMin = pb.anchorMax = new Vector2(1f, 0.5f); pb.pivot = new Vector2(1f, 0.5f);
        pb.anchoredPosition = new Vector2(-10f, 0f); pb.sizeDelta = new Vector2(190f, 56f);
        Image bi = pb.gameObject.AddComponent<Image>(); Pill(bi, 0.94f);
        bi.color = on ? new Color(1f, 0.66f, 0.18f, 0.92f) : new Color(1f, 1f, 1f, 0.12f);
        Button bt = pb.gameObject.AddComponent<Button>(); bt.targetGraphic = bi; bt.interactable = on;
        bt.onClick.AddListener(() => { if (busy) return; buy(); if (panelGo.activeSelf) Refresh(); });
        pb.gameObject.AddComponent<LundunPressFX>();
        Text(pb, price, 24f, Color.white);
        rowY -= 82f;
    }

    static void Pill(Image im, float ppu)
    {
        if (LundunHudArt.Pill == null) return;
        im.sprite = LundunHudArt.Pill; im.type = Image.Type.Sliced; im.pixelsPerUnitMultiplier = ppu;
    }

    static void Pin(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size) { Pin(r, anchor, pos, size, anchor); }
    static void Pin(RectTransform r, Vector2 anchor, Vector2 pos, Vector2 size, Vector2 pivot)
    {
        r.anchorMin = r.anchorMax = anchor; r.pivot = pivot; r.anchoredPosition = pos; r.sizeDelta = size;
    }

    static TextMeshProUGUI Text(RectTransform parent, string text, float size, Color colour)
    {
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(parent, false);
        RectTransform trt = (RectTransform)tg.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        TextMeshProUGUI t = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) t.font = LundunHud.Instance.FontAsset;
        t.text = text; t.fontSize = size; t.alignment = TextAlignmentOptions.Center; t.color = colour;
        t.fontStyle = FontStyles.Bold; t.raycastTarget = false; t.richText = true;
        return t;
    }
}
