using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UI;

/// Pubs on the street corners (both modes; Story and Free Roam alike). Worked out when the city loads: at most road
/// junctions, the building on one corner gets a pub front (painted panelling, gold-lettered fascia, warm windows, a
/// hanging sign, lanterns and hanging baskets, a barrel to lean on). No change to the city's files.
///   • every pub can be walked into: GO IN at the door fades to its bar (one bar room, made once, placed high above
///     that pub so the map still shows where you are); LEAVE at the bar's door brings you back out
///   • at the bar: BUY A PINT (£5 in Story, on the house in Free Roam): each one makes James drunker (LundunDrunk)
///   • a barman and people drinking inside; outside, a few stand with pints from midday; at night some stagger home,
///     and people walk up and go in
/// Not during a story mission, and not with the police after you.
public class LundunPubs : MonoBehaviour
{
    public static LundunPubs Instance { get; private set; }
    /// James is in a pub right now (the camera comes in closer, no rain).
    public static bool InsidePub { get; private set; }

    public const long PintPrice = 5;
    const float InteriorY = 300f;                 // the bar room sits this high above the pub you went into
    const int MaxPubs = 80;

    static readonly string[] Names =
    {
        "THE RED LION", "THE KING'S HEAD", "THE CROWN", "THE ROSE & CROWN", "THE WHITE HART", "THE ROYAL OAK",
        "THE PRINCE ALBERT", "THE COACH & HORSES", "THE HOPE", "THE LAMB", "THE PLOUGH", "THE FOX & HOUNDS",
        "THE BLACK HORSE", "THE GEORGE", "THE SWAN", "THE BELL", "THE RAILWAY TAVERN", "THE DUKE OF PECKAM",
        "THE BRIXTUN ARMS", "THE QUAY INN", "THE GILMORE TAP", "THE GREEN MAN", "THE THREE BELLS", "THE ANCHOR",
        "THE FALCON", "THE NAG'S HEAD", "THE WINDMILL", "THE QUEEN'S HEAD", "THE STAR", "THE OLD SHIP",
    };
    static readonly Color[] Paints =
    {
        new Color(0.07f, 0.2f, 0.12f), new Color(0.32f, 0.05f, 0.06f), new Color(0.05f, 0.08f, 0.2f), new Color(0.05f, 0.05f, 0.05f),
    };

    public class Pub
    {
        public string name;
        public Vector3 at, outward, along;         // the door (on the pavement), out to the street, along the front
        public Vector3 corner;                     // the building's corner, on the ground: the pub's own origin
        public Quaternion rot;
        public int paint, brick;
        public GameObject go;
        public Vector3 Door { get { return at + outward * 0.9f; } }
        public readonly List<GameObject> outside = new List<GameObject>();
    }

    private readonly List<Pub> pubs = new List<Pub>();
    public IList<Pub> All { get { return pubs; } }

    private GameObject interior;
    private Pub inPub;
    private Vector3 interiorOrigin;
    private readonly List<GameObject> inside = new List<GameObject>();

    // UI
    private GameObject buttonGo;
    private TextMeshProUGUI label;
    private Image back, fadeImage;
    private enum Offer { None, GoIn, Pint, Leave }
    private Offer offer;
    private Pub offerPub;
    private bool busy;
    private float nextCheck, nextLife;

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) { Instance = null; InsidePub = false; } }

    IEnumerator Start()
    {
        BuildUI();
        // the city's colliders and the road data have to be there first
        for (int i = 0; i < 300; i++)
        {
            LundunNavigation nav = LundunNavigation.Instance;
            if (nav != null && nav.World != null && nav.Graph != null) break;
            yield return null;
        }
        yield return null;
        Find();
    }

    // ------------------------------------------------------------------ where the pubs go
    void Find()
    {
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.World == null) return;
        List<LwJunction> js = new List<LwJunction>();
        if (nav.World.junctions != null) js.AddRange(nav.World.junctions);
        if (nav.World.expansion != null && nav.World.expansion.junctions != null) js.AddRange(nav.World.expansion.junctions);
        int mask = LayerMask.GetMask("Default");
        foreach (LwJunction j in js)
        {
            if (pubs.Count >= MaxPubs) break;
            int h = Hash(j.id);
            if (h % 100 >= 80) continue;                                         // most corners, not every one
            Vector3 centre = LundunPlaces.Ground(new Vector3(j.x, 0f, j.z));
            if (TooClose(centre, 70f)) continue;
            for (int k = 0; k < 8; k++)
            {
                float a = (22.5f + ((k + h) % 8) * 45f) * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Pub p;
                if (TryCorner(centre, dir, mask, h, out p)) { pubs.Add(p); break; }
            }
        }
        Debug.Log("[Lundun] Pubs: " + pubs.Count + " on the corners.");
    }

    /// A ray from the junction hit a building (facade A). Find that building's corner, check the other street's
    /// facade (B) runs on from it, both at least a pub's depth long and flat: the corner pub goes there.
    bool TryCorner(Vector3 centre, Vector3 dir, int mask, int h, out Pub pub)
    {
        pub = null;
        RaycastHit hit;
        if (!Physics.Raycast(centre + Vector3.up * 1.6f, dir, out hit, 32f, mask, QueryTriggerInteraction.Ignore)) return false;
        if (hit.distance < 4f || !IsBuilding(hit.collider)) return false;
        Vector3 nA = hit.normal; nA.y = 0f;
        if (nA.sqrMagnitude < 0.5f) return false;
        nA.Normalize();
        Vector3 t = Vector3.Cross(Vector3.up, nA);
        if (Vector3.Dot(t, centre - hit.point) < 0f) t = -t;                       // t: along A, towards the junction (the corner)
        float y = centre.y + 1.5f;
        // walk along A towards the corner until the wall stops, then narrow it down
        float last = -1f, first = -1f;
        for (float s = 0f; s <= 30f; s += 0.5f)
        {
            if (FacadeAt(hit.point + t * s, nA, y, mask)) last = s; else { first = s; break; }
        }
        if (last < 0f || first < 0f) return false;
        for (int i = 0; i < 6; i++)
        {
            float mid = (last + first) * 0.5f;
            if (FacadeAt(hit.point + t * mid, nA, y, mask)) last = mid; else first = mid;
        }
        Vector3 corner = hit.point + t * last;
        Vector3 nB = t;                                                             // the other street's side faces the junction
        // the pub's own frame: Z out of one face, X along it away from the corner (the other face looks along -X)
        Vector3 Z, X;
        if (Vector3.Dot(Vector3.Cross(Vector3.up, nA), -nB) > 0f) { Z = nA; X = -nB; }
        else { Z = nB; X = -nA; }
        Quaternion rot = Quaternion.LookRotation(Z);
        // both faces there, flat, all the way along (corner to the far end of the pub)
        foreach (float u in new[] { 0.6f, 3.5f, Depth - 0.4f })
        {
            if (!FaceCheck(corner + X * u + Z * 1.6f, -Z, y, mask)) return false;
            if (!FaceCheck(corner - Z * u - X * 1.6f, X, y, mask)) return false;
        }
        Vector3 door = corner + X * DoorU;
        Vector3 ground = LundunPlaces.Ground(corner + Z * 1.2f - X * 1.2f);
        corner.y = ground.y; door.y = ground.y;
        if (Reserved(corner) || Reserved(corner + X * Depth - Z * Depth)) return false;
        pub = new Pub { name = Names[h % Names.Length], corner = corner, rot = rot, at = door, outward = Z, along = X,
                        paint = (h / 7) % Paints.Length, brick = (h / 11) % Bricks.Length };
        // the same name twice nearby reads oddly: shift along the list
        foreach (Pub o in pubs) if (o.name == pub.name && (o.at - door).sqrMagnitude < 600f * 600f) pub.name = Names[(h / 3 + pubs.Count) % Names.Length];
        return true;
    }

    static bool FacadeAt(Vector3 p, Vector3 n, float y, int mask)
    {
        Vector3 from = p + n * 1.5f; from.y = y;
        RaycastHit fh;
        return Physics.Raycast(from, -n, out fh, 2.6f, mask, QueryTriggerInteraction.Ignore) && IsBuilding(fh.collider) && Vector3.Dot(fh.normal, n) > 0.9f;
    }

    static bool FaceCheck(Vector3 from, Vector3 dir, float y, int mask)
    {
        from.y = y;
        RaycastHit fh;
        if (!Physics.Raycast(from, dir, out fh, 2.6f, mask, QueryTriggerInteraction.Ignore)) return false;
        return IsBuilding(fh.collider) && Vector3.Dot(fh.normal, -dir) > 0.9f && fh.distance > 0.9f && fh.distance < 2.3f;
    }

    static bool IsBuilding(Collider c)
    {
        if (c == null || c.attachedRigidbody != null) return false;
        Bounds b = c.bounds;
        return b.size.y > 4f && (b.size.x > 5f || b.size.z > 5f);
    }

    bool TooClose(Vector3 p, float d)
    {
        foreach (Pub o in pubs) if ((o.at - p).sqrMagnitude < d * d) return true;
        return false;
    }

    /// Not on the story's places, walk-in stores, LAGOS FOODS or James's home.
    static bool Reserved(Vector3 p)
    {
        if (LundunShopInterior.At(p) != null) return true;
        foreach (LundunShopInterior s in LundunShopInterior.All) if (s != null && (s.transform.position - p).sqrMagnitude < 30f * 30f) return true;
        LundunPlaces places = LundunPlaces.Instance;
        if (places != null && places.Places != null)
            foreach (LundunPlaces.Place pl in places.Places) if (pl != null && (pl.door - p).sqrMagnitude < 22f * 22f) return true;
        LundunStoryShop shop = LundunStoryShop.Instance;
        if (shop != null && shop.Ready && (shop.DoorOutside - p).sqrMagnitude < 25f * 25f) return true;
        return false;
    }

    static int Hash(string s)
    {
        unchecked { int h = 17; if (s != null) foreach (char ch in s) h = h * 31 + ch; return h & 0x7fffffff; }
    }

    // ------------------------------------------------------------------ every frame
    void Update()
    {
        // taken out some other way (wasted, busted, a teleport): the bar room goes
        if (InsidePub && !busy)
        {
            PlayerHealth ph = PlayerHealth.Instance;
            if (ph != null && ph.Body != null && ph.Body.position.y < InteriorY - 40f) Eject();
        }
        if (busy) return;
        if (Time.time >= nextCheck)
        {
            nextCheck = Time.time + 0.2f;
            ShowNear();
            Prompt();
        }
        if (!InsidePub && Time.time >= nextLife) { nextLife = Time.time + 2f; Life(); }
    }

    /// Pub fronts are made when you first come near, and only drawn when you're near.
    void ShowNear()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Vector3 p = cam.transform.position;
        if (InsidePub && inPub != null) p = inPub.at;
        foreach (Pub pub in pubs)
        {
            float d = (pub.at - p).sqrMagnitude;
            bool want = d < 230f * 230f;
            if (want && pub.go == null) Build(pub);
            if (pub.go != null && pub.go.activeSelf != want) pub.go.SetActive(want);
        }
    }

    void Prompt()
    {
        offer = Offer.None; offerPub = null;
        PlayerHealth ph = PlayerHealth.Instance;
        bool can = ph != null && !ph.IsDead && ph.OnFoot && !VehicleSystem.Locked
                   && !(LundunBikes.Instance != null && LundunBikes.Instance.Riding);
        if (can)
        {
            Vector3 j = ph.Body.position;
            if (InsidePub)
            {
                Vector3 l = j - interiorOrigin;
                if (l.z < -3.4f && Mathf.Abs(l.x) < 1.3f) offer = Offer.Leave;
                else if (l.z > 1.2f && l.z < 2.7f && Mathf.Abs(l.x) < 4.3f) offer = Offer.Pint;
            }
            else
            {
                foreach (Pub pub in pubs)
                {
                    Vector3 d = pub.Door - j; d.y = 0f;
                    if (d.sqrMagnitude < 1.7f * 1.7f && Mathf.Abs(pub.Door.y - j.y) < 2f) { offer = Offer.GoIn; offerPub = pub; break; }
                }
            }
        }
        if (offer == Offer.None) { buttonGo.SetActive(false); return; }
        bool story = LundunGame.IsStory;
        switch (offer)
        {
            case Offer.GoIn:
                bool blocked = LundunMissions.Running || (WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0);
                label.text = (blocked ? "CLOSED" : "GO IN") + "\n<size=60%>" + offerPub.name + (blocked ? (LundunMissions.Running ? " · not now" : " · not with the police after you") : "") + "</size>";
                back.color = blocked ? new Color(0.2f, 0.2f, 0.22f, 0.92f) : new Color(0.32f, 0.05f, 0.06f, 0.92f);
                break;
            case Offer.Pint:
                bool cant = story && !LundunWallet.CanAfford(PintPrice);
                label.text = (cant ? "SKINT" : "BUY A PINT") + "\n<size=60%>" + (story ? "£" + PintPrice : "on the house") + (LundunDrunk.Pints > 0.5f ? "  ·  had " + Mathf.CeilToInt(LundunDrunk.Pints) : "") + "</size>";
                back.color = cant ? new Color(0.2f, 0.2f, 0.22f, 0.92f) : new Color(0.55f, 0.36f, 0.06f, 0.92f);
                break;
            case Offer.Leave:
                label.text = "LEAVE\n<size=60%>" + (inPub != null ? inPub.name : "") + "</size>";
                back.color = new Color(0.12f, 0.16f, 0.29f, 0.92f);
                break;
        }
        buttonGo.SetActive(true);
    }

    void OnPress()
    {
        if (busy) return;
        switch (offer)
        {
            case Offer.GoIn:
                if (LundunMissions.Running || (WantedSystem.Instance != null && WantedSystem.Instance.Stars > 0)) { LundunHaptics.Light(); return; }
                StartCoroutine(GoIn(offerPub));
                break;
            case Offer.Pint: BuyPint(); break;
            case Offer.Leave: StartCoroutine(Leave()); break;
        }
        buttonGo.SetActive(false);
        nextCheck = Time.time + 0.6f;
    }

    void BuyPint()
    {
        if (LundunGame.IsStory)
        {
            if (!LundunWallet.CanAfford(PintPrice)) { LundunHaptics.Light(); ShowToast("You can't afford a pint"); return; }
            LundunWallet.Pay(PintPrice, "A pint");
        }
        LundunDrunk.Drink();
        int had = Mathf.CeilToInt(LundunDrunk.Pints);
        string[] lines = { "Cheers!", "Lovely. Another?", "Getting a taste for it…", "You're swaying a bit, mate.", "Last one, yeah? Yeah?", "Right, that's enough for you." };
        ShowToast(lines[Mathf.Clamp(had - 1, 0, lines.Length - 1)]);
    }

    static void ShowToast(string s) { if (LundunNavigation.Instance != null) LundunNavigation.Instance.ShowToast(s); }

    // ------------------------------------------------------------------ in and out
    IEnumerator GoIn(Pub pub)
    {
        busy = true;
        yield return Fade(1f);
        if (interior == null) BuildInterior();
        inPub = pub;
        interiorOrigin = new Vector3(pub.at.x, InteriorY, pub.at.z);
        interior.transform.position = interiorOrigin;
        interior.SetActive(true);
        Physics.SyncTransforms();
        FillInside();
        Place(interiorOrigin + new Vector3(0f, 0.1f, -2.7f), Quaternion.identity);
        InsidePub = true;
        yield return new WaitForSecondsRealtime(0.25f);
        yield return Fade(0f);
        busy = false;
    }

    IEnumerator Leave()
    {
        busy = true;
        yield return Fade(1f);
        Pub pub = inPub;
        InsidePub = false;
        foreach (GameObject g in inside) if (g != null) Destroy(g);
        inside.Clear();
        if (interior != null) interior.SetActive(false);
        if (pub != null) Place(pub.at + pub.outward * 1.8f, Quaternion.LookRotation(pub.outward));
        inPub = null;
        yield return new WaitForSecondsRealtime(0.2f);
        yield return Fade(0f);
        busy = false;
    }

    /// Wasted or busted inside, a mission starting: out of the pub without the walk.
    public void Eject()
    {
        if (!InsidePub) return;
        InsidePub = false;
        foreach (GameObject g in inside) if (g != null) Destroy(g);
        inside.Clear();
        if (interior != null) interior.SetActive(false);
        inPub = null;
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
        fadeImage.gameObject.SetActive(true);
        float from = fadeImage.color.a;
        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / 0.4f)
        {
            fadeImage.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, t));
            yield return null;
        }
        fadeImage.color = new Color(0f, 0f, 0f, to);
        if (to <= 0f) fadeImage.gameObject.SetActive(false);
    }

    // ------------------------------------------------------------------ people
    /// Inside: the barman, and people drinking at the bar and the tables (some of them well on their way).
    void FillInside()
    {
        foreach (GameObject g in inside) if (g != null) Destroy(g);
        inside.Clear();
        Vector3 o = interiorOrigin;
        Person(o + new Vector3(0.4f, 0f, 3.75f), 180f, false, false);                            // behind the bar
        Vector3[] spots = { new Vector3(-2.6f, 0, 2.05f), new Vector3(-1.7f, 0, 2.05f), new Vector3(2.2f, 0, 2.05f),
                            new Vector3(-4.0f, 0, -0.85f), new Vector3(-3.3f, 0, -1.9f), new Vector3(2.6f, 0, -1.2f),
                            new Vector3(4.5f, 0, -0.05f), new Vector3(1.3f, 0, -2.3f) };
        float[] yaws = { 0f, 0f, 0f, 160f, 30f, 130f, 250f, 60f };
        int n = Random.Range(4, 7);
        List<int> order = new List<int>(); for (int i = 0; i < spots.Length; i++) order.Add(i);
        for (int i = 0; i < order.Count; i++) { int k = Random.Range(i, order.Count); int tmp = order[i]; order[i] = order[k]; order[k] = tmp; }
        for (int i = 0; i < n; i++) { int s = order[i]; Person(o + spots[s], yaws[s] + Random.Range(-15f, 15f), true, Random.value < 0.45f); }
    }

    GameObject Person(Vector3 at, float yaw, bool pint, bool drunk)
    {
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return null;
        GameObject g = pop.NewPerson(null);
        if (g == null) return null;
        Still(g);
        g.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
        g.SetActive(true);
        foreach (Animator a in g.GetComponentsInChildren<Animator>(true)) a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (pint) GivePint(g);
        if (drunk) g.AddComponent<LundunSway>().amount = Random.Range(0.5f, 1f);
        inside.Add(g);
        return g;
    }

    /// No wandering, no NavMesh: they stand where they're put.
    static void Still(GameObject g)
    {
        NPCBrain b = g.GetComponent<NPCBrain>(); if (b != null) b.enabled = false;
        NavMeshAgent a = g.GetComponent<NavMeshAgent>(); if (a != null) a.enabled = false;
    }

    static Material pintMat, headMat;
    /// A pint in the right hand.
    static void GivePint(GameObject g)
    {
        Animator an = g.GetComponentInChildren<Animator>();
        Transform hand = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (hand == null) return;
        if (pintMat == null)
        {
            pintMat = LundunMeshKit.Mat("Pint", new Color(0.55f, 0.3f, 0.05f, 1f), 0.9f, Color.black);
            headMat = LundunMeshKit.Mat("PintHead", new Color(0.95f, 0.92f, 0.82f), 0.3f, Color.black);
        }
        GameObject glass = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(glass.GetComponent<Collider>());
        glass.name = "Pint";
        glass.transform.SetParent(hand, false);
        glass.transform.localPosition = new Vector3(0f, 0.06f, 0.05f);
        glass.transform.localScale = new Vector3(0.075f, 0.075f, 0.075f);
        glass.transform.rotation = Quaternion.identity;
        glass.GetComponent<Renderer>().sharedMaterial = pintMat;
        GameObject head = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Destroy(head.GetComponent<Collider>());
        head.transform.SetParent(glass.transform, false);
        head.transform.localPosition = new Vector3(0f, 0.92f, 0f);
        head.transform.localScale = new Vector3(1f, 0.12f, 1f);
        head.GetComponent<Renderer>().sharedMaterial = headMat;
        glass.AddComponent<LundunKeepUpright>();
    }

    /// Outside, near James: people with pints by the door (midday on), people staggering home (at night), and
    /// people walking up and going in.
    void Life()
    {
        PlayerHealth ph = PlayerHealth.Instance;
        if (ph == null || ph.Body == null) return;
        Vector3 j = ph.Body.position;
        float hour = LundunClock.Instance != null ? LundunClock.Instance.Hour : 14f;
        bool open = hour >= 12f || hour < 2.5f;
        bool late = hour >= 21f || hour < 3f;
        int count = 0;
        foreach (Pub pub in pubs)
        {
            pub.outside.RemoveAll(g => g == null);
            float d = Vector3.Distance(pub.at, j);
            if (d > 95f || !open)
            {
                foreach (GameObject g in pub.outside) if (g != null && !Seen(g.transform.position)) Destroy(g);
                continue;
            }
            count += pub.outside.Count;
        }
        if (count >= 9) return;
        foreach (Pub pub in pubs)
        {
            if (!open || Vector3.Distance(pub.at, j) > 75f) continue;
            // two with pints by the barrel
            int standing = 0;
            foreach (GameObject g in pub.outside) if (g != null && g.GetComponent<LundunWalker>() == null) standing++;
            if (standing < 2 && !Seen(pub.at + pub.outward * 1.2f + pub.along * 2.2f))
            {
                Vector3 p = pub.at + pub.outward * (1.0f + standing * 0.7f) + pub.along * (2.2f + standing * 0.5f);
                GameObject g = Outside(pub, p, Quaternion.LookRotation(-pub.along + pub.outward * (standing == 0 ? 0.6f : -0.6f)), true, late && Random.value < 0.6f);
                if (g != null) return;
            }
            // coming out, three sheets to the wind (at night)
            if (late && Random.value < 0.18f)
            {
                Vector3 to = pub.at + pub.along * (Random.value < 0.5f ? -45f : 45f) + pub.outward * 2.2f;
                GameObject g = Outside(pub, pub.Door, Quaternion.LookRotation(pub.outward), false, true);
                if (g != null) { Walk(g, to, 0.85f, true); return; }
            }
            // going in for one (out of sight a little way along the street, walking up to the door)
            if (Random.value < 0.12f)
            {
                Vector3 from = pub.at + pub.along * (Random.value < 0.5f ? -32f : 32f) + pub.outward * 2.2f;
                if (Seen(from)) continue;
                GameObject g = Outside(pub, from, Quaternion.identity, false, false);
                if (g != null) { Walk(g, pub.Door, 1.4f, false); return; }
            }
        }
    }

    GameObject Outside(Pub pub, Vector3 at, Quaternion rot, bool pint, bool drunk)
    {
        LundunPopulation pop = LundunPopulation.Instance;
        if (pop == null) return null;
        NavMeshHit hit;
        if (!NavMesh.SamplePosition(at + Vector3.up, out hit, 2.5f, NavMesh.AllAreas)) return null;
        GameObject g = pop.NewPerson(null);
        if (g == null) return null;
        Still(g);
        g.transform.SetPositionAndRotation(hit.position, rot);
        g.SetActive(true);
        if (pint) GivePint(g);
        if (drunk) g.AddComponent<LundunSway>().amount = Random.Range(0.6f, 1f);
        pub.outside.Add(g);
        return g;
    }

    static void Walk(GameObject g, Vector3 to, float speed, bool shout)
    {
        LundunWalker w = g.AddComponent<LundunWalker>();
        w.to = to; w.speed = speed; w.shout = shout;
    }

    static bool Seen(Vector3 p) { Camera c = Camera.main; return c != null && LundunView.CanSee(c, p + Vector3.up); }

    // ------------------------------------------------------------------ the pub: its own corner building
    // Built round the corner of the building that's there (a skin just proud of it, Depth along both streets):
    //   ground floor  painted, glazed pub front wrapping both streets: pilasters, big windows with glazing bars,
    //                 the door (on the X face, by the corner), the fascia with the name in gold on both faces, a
    //                 cornice with greenery spilling over it, lanterns, a hanging sign at the far end
    //   above         two storeys of brick with white sash windows, stone sills and lintels, string courses, a
    //                 tall painted sign on the corner, a stone parapet and a chimney stack with pots
    // Local frame: the corner at the origin, X along one street (the face looks +Z), the other face looks -X.
    const float Depth = 7f, DoorU = 1.05f, GroundTop = 4.2f, Top = 10.2f, Proud = 0.15f;

    void Build(Pub pub)
    {
        if (pubMesh == null) BuildPubMesh();
        GameObject g = new GameObject("Pub_" + pub.name);
        g.transform.SetParent(transform, false);
        g.transform.SetPositionAndRotation(pub.corner, pub.rot);
        g.AddComponent<MeshFilter>().sharedMesh = pubMesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterials = PubMaterials(pub.paint, pub.brick);
        Color gold = new Color(0.95f, 0.78f, 0.35f);
        float mid = Depth / 2f - Proud / 2f;
        Words(g.transform, pub.name, new Vector3(mid, 3.78f, Proud + 0.36f), 180f, new Vector2(Depth - 0.6f, 0.5f), 3.2f, gold, false);
        Words(g.transform, pub.name, new Vector3(-Proud - 0.36f, 3.78f, -mid), 90f, new Vector2(Depth - 0.6f, 0.5f), 3.2f, gold, false);
        string two = TwoLines(pub.name);
        Words(g.transform, two, new Vector3(Depth - 0.15f + 0.07f, 4.45f, 1.25f), -90f, new Vector2(0.68f, 0.66f), 1.4f, gold, true);
        Words(g.transform, two, new Vector3(Depth - 0.15f - 0.07f, 4.45f, 1.25f), 90f, new Vector2(0.68f, 0.66f), 1.4f, gold, true);
        Words(g.transform, Stacked(pub.name), new Vector3(0.9f, 7.55f, Proud + 0.2f), 180f, new Vector2(1.0f, 3.3f), 2.2f, gold, true);
        Words(g.transform, "FINE\nALES\n&\nWINES", new Vector3(0.9f, 5.25f, Proud + 0.2f), 180f, new Vector2(1.0f, 1.1f), 1.2f, new Color(0.95f, 0.9f, 0.75f), true);
        pub.go = g;
    }

    static string TwoLines(string name)
    {
        string s = name.StartsWith("THE ") ? name.Substring(4) : name;
        return "THE\n" + s;
    }

    /// One word to a line, for the tall corner sign.
    static string Stacked(string name) { return name.Replace(" & ", "\n&\n").Replace(' ', '\n'); }

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
        t.characterSpacing = wrap ? 0f : 6f;
        MeshRenderer r = g.GetComponent<MeshRenderer>();
        if (r != null) { r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
    }

    static readonly Color[] Bricks =
    {
        new Color(0.55f, 0.24f, 0.17f),        // red
        new Color(0.72f, 0.6f, 0.42f),         // London yellow stock
        new Color(0.42f, 0.18f, 0.13f),        // dark red
    };

    private static Mesh pubMesh;
    private static readonly Dictionary<int, Material[]> pubMats = new Dictionary<int, Material[]>();
    private static Material[] shared;            // everything but the paint and the brick

    // materials: 0 paint, 1 gold, 2 warm glass, 3 dark wood, 4 black iron, 5 lamp, 6 leaves, 7 flowers, 8 stone,
    //            9 brick, 10 white (sash frames), 11 dark glass (upstairs), 12 lit glass (upstairs), 13 chimney pot
    const int MPaint = 0, MGold = 1, MGlass = 2, MWood = 3, MIron = 4, MLamp = 5, MLeaf = 6, MFlower = 7, MStone = 8,
              MBrick = 9, MWhite = 10, MDark = 11, MLit = 12, MPot = 13;

    /// The ground floor's pieces, on either face: u along the face from the corner, y up, o out from the wall.
    struct Face
    {
        public LundunMeshKit k; public bool b;      // b: the face looking -X (u runs along -Z)
        public void Box(int m, float u, float y, float o, float su, float sy, float so)
        {
            if (!b) k.Box(m, new Vector3(u, y, o), new Vector3(su, sy, so));
            else k.Box(m, new Vector3(-o, y, -u), new Vector3(so, sy, su));
        }
        public void Cyl(int m, float u, float y, float o, float r, float h)
        {
            if (!b) k.Cyl(m, new Vector3(u, y, o), r, h);
            else k.Cyl(m, new Vector3(-o, y, -u), r, h);
        }
    }

    static void BuildPubMesh()
    {
        LundunMeshKit k = new LundunMeshKit(14);
        float D = Depth, P = Proud, mid = D / 2f - P / 2f;
        // the building's skin: brick all round (it may stand taller than what's behind, so its sides show)
        k.Box(MBrick, new Vector3(mid, Top / 2f, -mid), new Vector3(D + P, Top, D + P));
        k.Box(MStone, new Vector3(mid, Top + 0.13f, -mid), new Vector3(D + P + 0.12f, 0.26f, D + P + 0.12f));   // parapet coping
        k.Box(MBrick, new Vector3(D - 1.2f, Top + 1.1f, -D + 1.2f), new Vector3(1.3f, 2.2f, 0.75f));           // chimney stack
        k.Box(MStone, new Vector3(D - 1.2f, Top + 2.25f, -D + 1.2f), new Vector3(1.42f, 0.12f, 0.87f));
        for (int i = 0; i < 3; i++) k.Cyl(MPot, new Vector3(D - 1.62f + i * 0.42f, Top + 2.5f, -D + 1.2f), 0.13f, 0.42f);

        foreach (bool b in new[] { false, true })
        {
            Face f = new Face { k = k, b = b };
            float o = P;                                                             // the wall's face
            // ground floor: the painted front all along, fascia, cornice, greenery
            f.Box(MPaint, mid, 1.9f, o + 0.04f, D + P, 3.8f, 0.08f);
            f.Box(MPaint, mid, 3.78f, o + 0.19f, D + P + 0.2f, 0.66f, 0.34f);
            f.Box(MGold, mid, 4.1f, o + 0.36f, D + P + 0.2f, 0.05f, 0.04f);
            f.Box(MGold, mid, 3.46f, o + 0.36f, D + P + 0.2f, 0.05f, 0.04f);
            f.Box(MWood, mid, 4.2f, o + 0.24f, D + P + 0.45f, 0.16f, 0.46f);
            for (int i = 0; i < 14; i++)                                              // planting along the cornice, spilling over
            {
                float u = 0.25f + i * (D - 0.5f) / 13f;
                float hgt = 0.25f + ((i * 7) % 5) * 0.06f;
                f.Box(MLeaf, u, 4.32f + hgt / 2f, o + 0.3f, 0.55f, hgt, 0.42f);
                if (i % 2 == 0) f.Box(MLeaf, u + 0.2f, 3.98f, o + 0.5f, 0.3f, 0.45f, 0.14f);
                if (i % 3 == 0) f.Box(MFlower, u, 4.35f + hgt, o + 0.32f, 0.3f, 0.08f, 0.3f);
            }
            // pilasters and bays
            float[] pil = { 0.12f, 1.95f, 3.85f, 5.65f, D - 0.12f };
            foreach (float u in pil)
            {
                f.Box(MPaint, u, 1.7f, o + 0.13f, 0.28f, 3.4f, 0.14f);
                f.Box(MGold, u, 3.36f, o + 0.19f, 0.34f, 0.1f, 0.2f);
                f.Box(MWood, u, 0.15f, o + 0.17f, 0.34f, 0.3f, 0.2f);
            }
            for (int bay = 0; bay < 4; bay++)
            {
                float u0 = pil[bay] + 0.14f, u1 = pil[bay + 1] - 0.14f, uc = (u0 + u1) / 2f, w = u1 - u0;
                if (!b && bay == 0)
                {
                    // the door (X face, by the corner): panelled, glazed top, fanlight, brass, a step
                    f.Box(MWood, uc, 1.2f, o + 0.08f, w, 2.4f, 0.06f);
                    f.Box(MGlass, uc, 1.75f, o + 0.115f, w - 0.5f, 0.7f, 0.02f);
                    f.Box(MGlass, uc, 2.75f, o + 0.1f, w, 0.45f, 0.03f);
                    f.Box(MGold, uc + w * 0.3f, 1.15f, o + 0.13f, 0.05f, 0.2f, 0.05f);
                    f.Box(MStone, uc, 0.05f, o + 0.3f, w + 0.2f, 0.1f, 0.4f);
                    continue;
                }
                f.Box(MGlass, uc, 1.85f, o + 0.09f, w, 1.7f, 0.04f);                   // window
                f.Box(MGlass, uc, 3.0f, o + 0.09f, w, 0.35f, 0.04f);                   // fanlight over it
                f.Box(MWood, uc, 2.78f, o + 0.12f, w, 0.06f, 0.05f);
                f.Box(MWood, uc, 0.5f, o + 0.11f, w, 0.8f, 0.08f);                     // stall riser
                f.Box(MWood, uc, 0.96f, o + 0.16f, w + 0.1f, 0.06f, 0.16f);            // sill
                f.Box(MWood, uc, 1.85f, o + 0.12f, 0.05f, 1.7f, 0.04f);                // glazing bars
                f.Box(MWood, uc, 2.3f, o + 0.12f, w, 0.05f, 0.04f);
                f.Box(MGold, uc, 1.85f, o + 0.115f, w * 0.6f, 0.025f, 0.01f);          // etched line
            }
            // lanterns: either side of the door, and at the corner
            float[] lan = b ? new[] { 3.85f } : new[] { 0.12f, 1.95f, 5.65f };
            foreach (float u in lan)
            {
                f.Box(MIron, u, 3.05f, o + 0.42f, 0.2f, 0.3f, 0.2f);
                f.Box(MLamp, u, 3.05f, o + 0.43f, 0.14f, 0.22f, 0.16f);
                f.Box(MIron, u, 3.25f, o + 0.3f, 0.04f, 0.04f, 0.24f);
            }
            // upstairs: two storeys of sash windows, string courses between
            f.Box(MStone, mid, 7.2f, o + 0.04f, D + P + 0.1f, 0.18f, 0.08f);
            float[] wins = b ? new[] { 1.4f, 3.5f, 5.6f } : new[] { 2.9f, 5.2f };
            for (int fl = 0; fl < 2; fl++)
            {
                float wy = 5.75f + fl * 3.0f;
                for (int i = 0; i < wins.Length; i++)
                {
                    float u = wins[i];
                    bool lit = ((i + fl * 2 + (b ? 1 : 0)) % 3) == 0;
                    f.Box(lit ? MLit : MDark, u, wy, o + 0.02f, 0.9f, 1.55f, 0.04f);
                    f.Box(MWhite, u, wy + 0.8f, o + 0.05f, 1.0f, 0.07f, 0.06f);
                    f.Box(MWhite, u, wy - 0.8f, o + 0.05f, 1.0f, 0.07f, 0.06f);
                    f.Box(MWhite, u - 0.48f, wy, o + 0.05f, 0.07f, 1.6f, 0.06f);
                    f.Box(MWhite, u + 0.48f, wy, o + 0.05f, 0.07f, 1.6f, 0.06f);
                    f.Box(MWhite, u, wy, o + 0.06f, 0.95f, 0.06f, 0.05f);              // the sash's meeting rail
                    f.Box(MWhite, u, wy, o + 0.055f, 0.04f, 1.55f, 0.04f);
                    f.Box(MStone, u, wy - 0.93f, o + 0.07f, 1.2f, 0.09f, 0.14f);       // sill
                    f.Box(MStone, u, wy + 0.93f, o + 0.03f, 1.15f, 0.16f, 0.06f);      // lintel
                }
            }
        }
        // the tall sign on the corner (X face, upstairs), framed in gold
        k.Box(MGold, new Vector3(0.9f, 6.9f, P + 0.08f), new Vector3(1.32f, 4.7f, 0.1f));
        k.Box(MPaint, new Vector3(0.9f, 6.9f, P + 0.12f), new Vector3(1.18f, 4.56f, 0.1f));
        k.Box(MGold, new Vector3(0.9f, 5.92f, P + 0.175f), new Vector3(0.9f, 0.03f, 0.01f));
        // the hanging sign on its bracket at the far end of the X face
        float hx = D - 0.15f;
        k.Box(MIron, new Vector3(hx, 5.1f, 0.85f), new Vector3(0.06f, 0.06f, 1.4f));
        k.Box(MIron, new Vector3(hx, 4.85f, 0.4f), new Vector3(0.04f, 0.5f, 0.04f));
        k.Box(MIron, new Vector3(hx, 4.98f, 1.25f), new Vector3(0.03f, 0.22f, 0.03f));
        k.Box(MGold, new Vector3(hx, 4.45f, 1.25f), new Vector3(0.08f, 0.9f, 0.8f));
        k.Box(MPaint, new Vector3(hx, 4.45f, 1.25f), new Vector3(0.1f, 0.78f, 0.68f));
        // hanging baskets at the corner, and a barrel to lean on round the side
        foreach (Vector3 bp in new[] { new Vector3(0.12f, 3.0f, P + 0.75f), new Vector3(-P - 0.75f, 3.0f, -0.12f) })
        {
            k.Box(MIron, bp + new Vector3(0, 0.45f, 0), new Vector3(0.03f, 0.35f, 0.03f));
            k.Cyl(MLeaf, bp, 0.26f, 0.3f);
            k.Cyl(MFlower, bp + new Vector3(0, 0.17f, 0), 0.28f, 0.07f);
            k.Cyl(MLeaf, bp - new Vector3(0, 0.17f, 0), 0.16f, 0.12f);
        }
        Vector3 bar = new Vector3(-P - 1.0f, 0f, -2.9f);
        k.Cyl(MWood, bar + new Vector3(0, 0.5f, 0), 0.32f, 1.0f);
        k.Cyl(MIron, bar + new Vector3(0, 0.25f, 0), 0.335f, 0.05f);
        k.Cyl(MIron, bar + new Vector3(0, 0.78f, 0), 0.335f, 0.05f);
        k.Cyl(MWood, bar + new Vector3(0, 1.02f, 0), 0.38f, 0.05f);
        pubMesh = k.Build("PubBuilding");
    }

    static Material[] PubMaterials(int paint, int brick)
    {
        int key = paint * 10 + brick;
        Material[] m;
        if (pubMats.TryGetValue(key, out m)) return m;
        if (shared == null)
        {
            shared = new Material[14];
            shared[MGold] = LundunMeshKit.Mat("PubGold", new Color(0.82f, 0.64f, 0.25f), 0.7f, Color.black);
            shared[MGlass] = LundunMeshKit.Mat("PubWindow", new Color(0.25f, 0.15f, 0.06f), 0.95f, new Color(1f, 0.62f, 0.25f) * 0.9f);
            shared[MWood] = LundunMeshKit.Mat("PubWood", new Color(0.18f, 0.1f, 0.05f), 0.35f, Color.black);
            shared[MIron] = LundunMeshKit.Mat("PubIron", new Color(0.04f, 0.04f, 0.04f), 0.4f, Color.black);
            shared[MLamp] = LundunMeshKit.Mat("PubLamp", new Color(1f, 0.85f, 0.5f), 0.5f, new Color(1f, 0.75f, 0.35f) * 2.2f);
            shared[MLeaf] = LundunMeshKit.Mat("PubLeaves", new Color(0.12f, 0.35f, 0.1f), 0.1f, Color.black);
            shared[MFlower] = LundunMeshKit.Mat("PubFlowers", new Color(0.85f, 0.15f, 0.35f), 0.1f, Color.black);
            shared[MStone] = LundunMeshKit.Mat("PubStone", new Color(0.78f, 0.75f, 0.68f), 0.2f, Color.black);
            shared[MWhite] = LundunMeshKit.Mat("PubSash", new Color(0.92f, 0.92f, 0.9f), 0.4f, Color.black);
            shared[MDark] = LundunMeshKit.Mat("PubUpstairsGlass", new Color(0.08f, 0.09f, 0.11f), 0.95f, Color.black);
            shared[MLit] = LundunMeshKit.Mat("PubUpstairsLit", new Color(0.3f, 0.22f, 0.12f), 0.9f, new Color(1f, 0.78f, 0.45f) * 0.6f);
            shared[MPot] = LundunMeshKit.Mat("PubChimneyPot", new Color(0.6f, 0.3f, 0.18f), 0.2f, Color.black);
        }
        m = (Material[])shared.Clone();
        m[MPaint] = LundunMeshKit.Mat("PubPaint" + paint, Paints[paint], 0.6f, Color.black);
        m[MBrick] = BrickMaterial(brick);
        pubMats[key] = m;
        return m;
    }

    private static readonly Material[] brickMats = new Material[3];
    /// Brick, drawn here: a metre square of stretcher bond (4 bricks across, 13 courses), mortar lines, a little
    /// colour in each brick. The mesh's UVs are in metres, so it tiles at true size.
    static Material BrickMaterial(int i)
    {
        if (brickMats[i] != null) return brickMats[i];
        const int N = 256;
        Texture2D t = new Texture2D(N, N, TextureFormat.RGBA32, true);
        System.Random r = new System.Random(77 + i);
        Color baseC = Bricks[i], mortar = new Color(0.7f, 0.68f, 0.62f);
        Color[] px = new Color[N * N];
        int courses = 13; float ch = N / (float)courses, bw = N / 4f;
        for (int c = 0; c < courses; c++)
        {
            int y0 = Mathf.RoundToInt(c * ch), y1 = Mathf.RoundToInt((c + 1) * ch);
            float off = (c % 2) * bw * 0.5f;
            for (int bi = -1; bi < 5; bi++)
            {
                float v = (float)(r.NextDouble() * 0.18 - 0.09);
                Color bc = new Color(Mathf.Clamp01(baseC.r + v), Mathf.Clamp01(baseC.g + v * 0.8f), Mathf.Clamp01(baseC.b + v * 0.6f));
                int x0 = Mathf.RoundToInt(bi * bw + off), x1 = Mathf.RoundToInt((bi + 1) * bw + off);
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        if (x < 0 || x >= N) continue;
                        bool joint = y - y0 < 2 || x - x0 < 2;
                        float grain = (float)(r.NextDouble() * 0.05);
                        px[y * N + x] = joint ? mortar : new Color(bc.r - grain, bc.g - grain, bc.b - grain, 1f);
                    }
            }
        }
        t.SetPixels(px);
        t.wrapMode = TextureWrapMode.Repeat; t.filterMode = FilterMode.Bilinear; t.anisoLevel = 2;
        t.Apply(true, false);
        Material m = LundunMeshKit.Mat("PubBrick" + i, Color.white, 0.15f, Color.black);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
        m.mainTexture = t;
        brickMats[i] = m;
        return m;
    }

    // ------------------------------------------------------------------ the bar room (one, made once)
    // materials: 0 floorboards, 1 wallpaper, 2 dark wood, 3 brass, 4 ceiling, 5 green bottles, 6 amber bottles,
    //            7 lamps, 8 mirror, 9 carpet, 10 fruit machine, 11 black, 12 red leather, 13 warm window
    void BuildInterior()
    {
        interior = new GameObject("PubInterior");
        interior.transform.SetParent(transform, false);
        LundunMeshKit k = new LundunMeshKit(14);
        const float X = 6f, Z = 4.5f, Y = 3.4f;
        k.Box(0, new Vector3(0, -0.1f, 0), new Vector3(2 * X, 0.2f, 2 * Z));                          // floor
        k.Box(9, new Vector3(-1.5f, 0.005f, -1.2f), new Vector3(6.5f, 0.01f, 4.2f));                  // carpet
        k.Box(4, new Vector3(0, Y + 0.1f, 0), new Vector3(2 * X, 0.2f, 2 * Z));                         // ceiling
        for (float x = -X + 1f; x < X; x += 2f) k.Box(2, new Vector3(x, Y - 0.12f, 0), new Vector3(0.22f, 0.24f, 2 * Z));   // beams
        // walls: wallpaper above, panelling below, a dado rail
        Wall(k, new Vector3(0, Y / 2, Z + 0.1f), new Vector3(2 * X, Y, 0.2f), Vector3.back);
        Wall(k, new Vector3(0, Y / 2, -Z - 0.1f), new Vector3(2 * X, Y, 0.2f), Vector3.forward);
        Wall(k, new Vector3(X + 0.1f, Y / 2, 0), new Vector3(0.2f, Y, 2 * Z), Vector3.left);
        Wall(k, new Vector3(-X - 0.1f, Y / 2, 0), new Vector3(0.2f, Y, 2 * Z), Vector3.right);
        // the front: the door you came in by, and the backs of the lit windows
        k.Box(2, new Vector3(0, 1.2f, -Z + 0.02f), new Vector3(1.3f, 2.4f, 0.06f));
        k.Box(13, new Vector3(0, 1.75f, -Z + 0.06f), new Vector3(0.8f, 0.7f, 0.02f));
        foreach (float x in new[] { -3.2f, 3.2f })
        {
            k.Box(13, new Vector3(x, 1.8f, -Z + 0.02f), new Vector3(2.4f, 1.5f, 0.04f));
            k.Box(2, new Vector3(x, 1.8f, -Z + 0.05f), new Vector3(0.06f, 1.5f, 0.03f));
            k.Box(2, new Vector3(x, 1.05f, -Z + 0.1f), new Vector3(2.5f, 0.06f, 0.2f));
        }
        // the bar: counter, brass rail and foot rail, the pumps; the back bar with bottles and a mirror
        k.Box(2, new Vector3(0, 0.55f, 2.95f), new Vector3(9.2f, 1.1f, 0.7f));
        k.Box(2, new Vector3(0, 1.12f, 2.9f), new Vector3(9.4f, 0.06f, 0.85f));
        k.Box(3, new Vector3(0, 0.15f, 2.5f), new Vector3(9.0f, 0.05f, 0.05f));
        k.Box(3, new Vector3(0, 1.0f, 2.5f), new Vector3(9.2f, 0.04f, 0.04f));
        for (int i = 0; i < 6; i++)
        {
            float x = -2.5f + i * 1.0f;
            k.Cyl(3, new Vector3(x, 1.3f, 2.95f), 0.04f, 0.3f);
            k.Box(i % 2 == 0 ? 2 : 11, new Vector3(x, 1.6f, 2.95f), new Vector3(0.05f, 0.32f, 0.05f));
            k.Box(i % 3 == 0 ? 10 : 13, new Vector3(x, 1.68f, 2.92f), new Vector3(0.09f, 0.12f, 0.02f));     // pump clips
        }
        k.Box(2, new Vector3(0, 1.1f, Z - 0.25f), new Vector3(9.2f, 2.2f, 0.4f));                      // back bar
        k.Box(8, new Vector3(0, 2.4f, Z - 0.04f), new Vector3(6.0f, 1.2f, 0.04f));                     // mirror
        for (int shelf = 0; shelf < 2; shelf++)
        {
            float sy = 1.6f + shelf * 0.55f;
            k.Box(2, new Vector3(0, sy - 0.03f, Z - 0.28f), new Vector3(9.0f, 0.04f, 0.32f));
            for (int i = 0; i < 26; i++)
            {
                float x = -4.2f + i * 0.33f;
                float bh = 0.26f + ((i * 7 + shelf * 3) % 5) * 0.03f;
                k.Cyl(((i + shelf) % 3 == 0) ? 5 : 6, new Vector3(x, sy + bh / 2f, Z - 0.3f), 0.045f, bh, Quaternion.identity, 8);
            }
        }
        // tables and stools
        Vector3[] tables = { new Vector3(-3.6f, 0, -1.35f), new Vector3(-1.2f, 0, -2.4f), new Vector3(2.1f, 0, -1.9f), new Vector3(4.6f, 0, -0.6f), new Vector3(-0.3f, 0, 0.2f) };
        List<Bounds> blocks = new List<Bounds>();
        foreach (Vector3 t in tables)
        {
            k.Cyl(2, t + new Vector3(0, 0.74f, 0), 0.48f, 0.05f);
            k.Cyl(11, t + new Vector3(0, 0.37f, 0), 0.06f, 0.72f);
            k.Cyl(11, t + new Vector3(0, 0.02f, 0), 0.3f, 0.04f);
            for (int s = 0; s < 3; s++)
            {
                float a = s * 2.1f + t.x;
                Vector3 sp = t + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 0.85f;
                k.Cyl(12, sp + new Vector3(0, 0.46f, 0), 0.19f, 0.07f);
                k.Cyl(2, sp + new Vector3(0, 0.22f, 0), 0.04f, 0.44f);
            }
            blocks.Add(new Bounds(t + new Vector3(0, 0.4f, 0), new Vector3(1f, 0.8f, 1f)));
        }
        // a padded bench along the left wall, the fruit machine and the dartboard on the right
        k.Box(12, new Vector3(-X + 0.35f, 0.45f, -0.5f), new Vector3(0.6f, 0.2f, 5f));
        k.Box(12, new Vector3(-X + 0.12f, 0.85f, -0.5f), new Vector3(0.18f, 0.7f, 5f));
        k.Box(2, new Vector3(-X + 0.35f, 0.18f, -0.5f), new Vector3(0.6f, 0.36f, 5f));
        k.Box(11, new Vector3(X - 0.35f, 0.9f, 1.6f), new Vector3(0.6f, 1.8f, 0.7f));
        k.Box(10, new Vector3(X - 0.66f, 1.2f, 1.6f), new Vector3(0.02f, 0.8f, 0.55f));
        k.Cyl(11, new Vector3(X - 0.04f, 1.75f, -1.2f), 0.3f, 0.06f, Quaternion.Euler(0, 0, 90));
        k.Cyl(9, new Vector3(X - 0.08f, 1.75f, -1.2f), 0.22f, 0.02f, Quaternion.Euler(0, 0, 90));
        k.Cyl(3, new Vector3(X - 0.09f, 1.75f, -1.2f), 0.04f, 0.02f, Quaternion.Euler(0, 0, 90));
        // lamps hanging over the tables and the bar
        Vector3[] lamps = { new Vector3(-3.6f, 0, -1.35f), new Vector3(2.1f, 0, -1.9f), new Vector3(-1.5f, 0, 2.6f), new Vector3(1.5f, 0, 2.6f) };
        foreach (Vector3 l in lamps)
        {
            k.Box(11, new Vector3(l.x, Y - 0.45f, l.z), new Vector3(0.02f, 0.7f, 0.02f));
            k.Cyl(3, new Vector3(l.x, Y - 0.85f, l.z), 0.22f, 0.12f);
            k.Cyl(7, new Vector3(l.x, Y - 0.93f, l.z), 0.14f, 0.05f);
        }
        GameObject body = new GameObject("Room");
        body.transform.SetParent(interior.transform, false);
        body.AddComponent<MeshFilter>().sharedMesh = k.Build("PubInterior");
        MeshRenderer mr = body.AddComponent<MeshRenderer>();
        mr.sharedMaterials = new[]
        {
            LundunMeshKit.Mat("PubFloor", new Color(0.35f, 0.2f, 0.1f), 0.45f, Color.black),
            LundunMeshKit.Mat("PubWallpaper", new Color(0.36f, 0.08f, 0.09f), 0.15f, Color.black),
            LundunMeshKit.Mat("PubWoodIn", new Color(0.2f, 0.1f, 0.05f), 0.55f, Color.black),
            LundunMeshKit.Mat("PubBrass", new Color(0.85f, 0.65f, 0.3f), 0.85f, Color.black),
            LundunMeshKit.Mat("PubCeiling", new Color(0.86f, 0.8f, 0.66f), 0.1f, Color.black),
            LundunMeshKit.Mat("PubBottleGreen", new Color(0.1f, 0.35f, 0.15f), 0.9f, new Color(0.05f, 0.2f, 0.08f)),
            LundunMeshKit.Mat("PubBottleAmber", new Color(0.6f, 0.32f, 0.05f), 0.9f, new Color(0.3f, 0.15f, 0.02f)),
            LundunMeshKit.Mat("PubLampIn", new Color(1f, 0.85f, 0.55f), 0.5f, new Color(1f, 0.75f, 0.4f) * 3f),
            LundunMeshKit.Mat("PubMirror", new Color(0.55f, 0.55f, 0.6f), 1f, new Color(0.08f, 0.06f, 0.04f)),
            LundunMeshKit.Mat("PubCarpet", new Color(0.4f, 0.06f, 0.1f), 0.05f, Color.black),
            LundunMeshKit.Mat("PubMachine", new Color(0.9f, 0.5f, 0.2f), 0.6f, new Color(1f, 0.45f, 0.15f) * 1.6f),
            LundunMeshKit.Mat("PubBlack", new Color(0.03f, 0.03f, 0.03f), 0.4f, Color.black),
            LundunMeshKit.Mat("PubLeather", new Color(0.35f, 0.06f, 0.05f), 0.5f, Color.black),
            LundunMeshKit.Mat("PubWindowIn", new Color(0.55f, 0.45f, 0.3f), 0.9f, new Color(0.9f, 0.7f, 0.45f) * 0.7f),
        };
        // solid: floor, walls, the bar, the tables
        Solid(new Vector3(0, -0.1f, 0), new Vector3(2 * X, 0.2f, 2 * Z));
        Solid(new Vector3(0, Y / 2, Z + 0.1f), new Vector3(2 * X, Y, 0.2f));
        Solid(new Vector3(0, Y / 2, -Z - 0.1f), new Vector3(2 * X, Y, 0.2f));
        Solid(new Vector3(X + 0.1f, Y / 2, 0), new Vector3(0.2f, Y, 2 * Z));
        Solid(new Vector3(-X - 0.1f, Y / 2, 0), new Vector3(0.2f, Y, 2 * Z));
        Solid(new Vector3(0, Y + 0.1f, 0), new Vector3(2 * X, 0.2f, 2 * Z));
        Solid(new Vector3(0, 0.55f, 3.2f), new Vector3(9.4f, 1.1f, 1.3f));
        Solid(new Vector3(-X + 0.35f, 0.4f, -0.5f), new Vector3(0.7f, 0.8f, 5f));
        Solid(new Vector3(X - 0.35f, 0.9f, 1.6f), new Vector3(0.7f, 1.8f, 0.7f));
        foreach (Bounds b in blocks) Solid(b.center, b.size);
        // warm light
        foreach (Vector3 l in new[] { new Vector3(-2.5f, Y - 0.9f, -1f), new Vector3(2.5f, Y - 0.9f, 1f) })
        {
            GameObject lg = new GameObject("PubLight");
            lg.transform.SetParent(interior.transform, false);
            lg.transform.localPosition = l;
            Light li = lg.AddComponent<Light>();
            li.type = LightType.Point; li.range = 9f; li.intensity = 2.2f; li.color = new Color(1f, 0.78f, 0.5f);
            li.shadows = LightShadows.None;
        }
        foreach (Transform t in interior.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
        interior.SetActive(false);
    }

    static void Wall(LundunMeshKit k, Vector3 c, Vector3 size, Vector3 inward)
    {
        k.Box(1, c, size);
        Vector3 lowC = new Vector3(c.x, 0.55f, c.z) + inward * 0.03f;
        Vector3 lowS = new Vector3(Mathf.Abs(inward.x) > 0.5f ? 0.06f : size.x, 1.1f, Mathf.Abs(inward.z) > 0.5f ? 0.06f : size.z);
        k.Box(2, lowC, lowS);
        k.Box(2, new Vector3(c.x, 1.12f, c.z) + inward * 0.06f, new Vector3(Mathf.Abs(inward.x) > 0.5f ? 0.06f : size.x, 0.06f, Mathf.Abs(inward.z) > 0.5f ? 0.06f : size.z));
    }

    void Solid(Vector3 c, Vector3 size)
    {
        BoxCollider b = interior.AddComponent<BoxCollider>();
        b.center = c; b.size = size;
    }

    // ------------------------------------------------------------------ UI
    void BuildUI()
    {
        GameObject cg = new GameObject("PubUI", typeof(RectTransform));
        cg.transform.SetParent(transform, false);
        Canvas canvas = cg.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 62;
        CanvasScaler sc = cg.AddComponent<CanvasScaler>();
        sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        sc.referenceResolution = new Vector2(1920, 1080);
        sc.matchWidthOrHeight = 0.5f;
        cg.AddComponent<GraphicRaycaster>();
        buttonGo = new GameObject("PubPrompt", typeof(RectTransform));
        buttonGo.transform.SetParent(cg.transform, false);
        back = buttonGo.AddComponent<Image>();
        if (LundunHudArt.Pill != null) { back.sprite = LundunHudArt.Pill; back.type = Image.Type.Sliced; back.pixelsPerUnitMultiplier = 0.8f; }
        Button b = buttonGo.AddComponent<Button>(); b.targetGraphic = back; b.onClick.AddListener(OnPress);
        RectTransform rt = (RectTransform)buttonGo.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f); rt.pivot = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 230f); rt.sizeDelta = new Vector2(500f, 120f);
        GameObject tg = new GameObject("Text", typeof(RectTransform));
        tg.transform.SetParent(buttonGo.transform, false);
        RectTransform trt = (RectTransform)tg.transform; trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
        label = tg.AddComponent<TextMeshProUGUI>();
        if (LundunHud.Instance != null && LundunHud.Instance.FontAsset != null) label.font = LundunHud.Instance.FontAsset;
        label.fontSize = 38; label.alignment = TextAlignmentOptions.Center; label.color = Color.white;
        label.fontStyle = FontStyles.Bold; label.raycastTarget = false; label.richText = true;
        buttonGo.SetActive(false);

        GameObject fg = new GameObject("PubFade", typeof(RectTransform));
        fg.transform.SetParent(cg.transform, false);
        RectTransform fr = (RectTransform)fg.transform; fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = fr.offsetMax = Vector2.zero;
        fadeImage = fg.AddComponent<Image>(); fadeImage.color = new Color(0f, 0f, 0f, 0f);
        fg.SetActive(false);
    }
}

/// A drinker who's had a few: sways on their feet (the body leans and comes back, never quite still).
public class LundunSway : MonoBehaviour
{
    public float amount = 1f;
    private Transform hips;
    private float seed;
    void Start()
    {
        Animator a = GetComponentInChildren<Animator>();
        hips = a != null && a.isHuman ? a.GetBoneTransform(HumanBodyBones.Spine) : null;
        seed = Random.value * 50f;
    }
    void LateUpdate()
    {
        if (hips == null) return;
        float t = Time.time + seed;
        float roll = (Mathf.Sin(t * 1.3f) * 7f + Mathf.Sin(t * 2.9f) * 3f) * amount;
        float pitch = Mathf.Sin(t * 0.9f) * 4f * amount;
        hips.rotation = Quaternion.AngleAxis(roll, transform.forward) * Quaternion.AngleAxis(pitch, transform.right) * hips.rotation;
    }
}

/// Keeps a pint the right way up in a hand (no spilling).
public class LundunKeepUpright : MonoBehaviour
{
    void LateUpdate() { transform.rotation = Quaternion.Euler(0f, transform.parent != null ? transform.parent.eulerAngles.y : 0f, 0f); }
}

/// Someone walking somewhere for the pubs (going in, or staggering home): on arrival or far from James, gone.
/// shout: now and then shouts something (words over their head).
public class LundunWalker : MonoBehaviour
{
    public Vector3 to;
    public float speed = 1.3f;
    public bool shout;
    private NavMeshAgent agent;
    private Animator anim;
    private float nextShout, pauseUntil, born;
    private TextMeshPro words;
    static readonly string[] Shouts = { "OI OI!", "I LOVE YOU, MATE!", "ONE MORE!", "WHERE'S ME KEBAB?", "LAAAAADS!", "...where am I?" };

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        anim = GetComponentInChildren<Animator>();
        born = Time.time;
        nextShout = Time.time + Random.Range(2f, 6f);
        if (agent != null)
        {
            agent.enabled = true;
            if (agent.isOnNavMesh) { agent.speed = speed; agent.SetDestination(to); }
        }
    }

    void Update()
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh) { Destroy(gameObject); return; }
        LundunAnim.Float(anim, "Speed", agent.velocity.magnitude, 0.1f);
        PlayerHealth ph = PlayerHealth.Instance;
        float fromJames = ph != null && ph.Body != null ? Vector3.Distance(ph.Body.position, transform.position) : 0f;
        bool there = !agent.pathPending && agent.remainingDistance < 0.8f;
        if (there || fromJames > 110f || Time.time - born > 120f) { Destroy(gameObject); return; }
        if (shout)
        {
            // stop, sway, carry on: a wobbly line
            if (Time.time > pauseUntil && Random.value < 0.004f) { pauseUntil = Time.time + Random.Range(1f, 2.5f); agent.isStopped = true; }
            if (agent.isStopped && Time.time > pauseUntil) agent.isStopped = false;
            if (Time.time > nextShout)
            {
                nextShout = Time.time + Random.Range(6f, 12f);
                Say(Shouts[Random.Range(0, Shouts.Length)]);
            }
            if (words != null && Time.time > nextShout - 4f) words.gameObject.SetActive(false);
        }
    }

    void Say(string s)
    {
        if (words == null)
        {
            GameObject g = new GameObject("Shout", typeof(RectTransform));
            g.transform.SetParent(transform, false);
            g.transform.localPosition = new Vector3(0f, 2.2f, 0f);
            ((RectTransform)g.transform).sizeDelta = new Vector2(3f, 0.6f);
            words = g.AddComponent<TextMeshPro>();
            words.fontSize = 3f; words.alignment = TextAlignmentOptions.Center; words.color = Color.white; words.fontStyle = FontStyles.Bold;
            words.textWrappingMode = TextWrappingModes.NoWrap;
            g.AddComponent<LundunFaceCamera>();
        }
        words.text = s;
        words.gameObject.SetActive(true);
    }
}

/// Words that always face the camera.
public class LundunFaceCamera : MonoBehaviour
{
    void LateUpdate() { Camera c = Camera.main; if (c != null) transform.rotation = Quaternion.LookRotation(transform.position - c.transform.position); }
}
