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
    const float FrontWidth = 6f;
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
        public Vector3 at, outward, along;         // the front's middle (on the pavement), out to the street, along it
        public int paint;
        public GameObject go;
        public Vector3 Door { get { return at + outward * 0.9f; } }
        public readonly List<GameObject> outside = new List<GameObject>();
    }

    private readonly List<Pub> pubs = new List<Pub>();
    public IList<Pub> All { get { return pubs; } }

    // shared looks
    private static Mesh frontMesh;
    private static Material[][] frontMats;
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

    bool TryCorner(Vector3 centre, Vector3 dir, int mask, int h, out Pub pub)
    {
        pub = null;
        RaycastHit hit;
        if (!Physics.Raycast(centre + Vector3.up * 1.6f, dir, out hit, 32f, mask, QueryTriggerInteraction.Ignore)) return false;
        if (hit.distance < 5f || !IsBuilding(hit.collider)) return false;
        Vector3 n = hit.normal; n.y = 0f;
        if (n.sqrMagnitude < 0.5f) return false;
        n.Normalize();
        Vector3 t = Vector3.Cross(Vector3.up, n);
        if (Vector3.Dot(t, hit.point - centre) < 0f) t = -t;                       // away from the corner
        Vector3 c = hit.point + t * (FrontWidth * 0.5f + 0.6f);
        // a flat, unbroken front across its width (and nothing standing right in front of it)
        Vector3 at = Vector3.zero;
        foreach (float s in new[] { 0f, -FrontWidth * 0.48f, FrontWidth * 0.48f })
        {
            Vector3 from = c + t * s + n * 2.2f; from.y = centre.y + 1.5f;
            RaycastHit fh;
            if (!Physics.Raycast(from, -n, out fh, 3.2f, mask, QueryTriggerInteraction.Ignore)) return false;
            if (!IsBuilding(fh.collider) || Vector3.Dot(fh.normal, n) < 0.9f || fh.distance < 1.4f) return false;
            if (s == 0f) at = fh.point;
        }
        at.y = LundunPlaces.Ground(at + n * 1.2f).y;
        if (Reserved(at)) return false;
        pub = new Pub { name = Names[h % Names.Length], at = at, outward = n, along = t, paint = (h / 7) % Paints.Length };
        // the same name twice nearby reads oddly: shift along the list
        foreach (Pub o in pubs) if (o.name == pub.name && (o.at - at).sqrMagnitude < 600f * 600f) pub.name = Names[(h / 3 + pubs.Count) % Names.Length];
        return true;
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

    // ------------------------------------------------------------------ the pub front
    void Build(Pub pub)
    {
        if (frontMesh == null) BuildFrontMesh();
        GameObject g = new GameObject("Pub_" + pub.name);
        g.transform.SetParent(transform, false);
        g.transform.SetPositionAndRotation(pub.at, Quaternion.LookRotation(pub.outward));
        g.AddComponent<MeshFilter>().sharedMesh = frontMesh;
        MeshRenderer r = g.AddComponent<MeshRenderer>();
        r.sharedMaterials = frontMats[pub.paint];
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Color gold = new Color(0.95f, 0.78f, 0.35f);
        Words(g.transform, pub.name, new Vector3(0f, 3.35f, 0.37f), 180f, new Vector2(5.8f, 0.48f), 3.2f, gold, false);
        string two = TwoLines(pub.name);
        Words(g.transform, two, new Vector3(3.0f + 0.07f, 3.25f, 1.2f), -90f, new Vector2(0.68f, 0.66f), 1.4f, gold, true);
        Words(g.transform, two, new Vector3(3.0f - 0.07f, 3.25f, 1.2f), 90f, new Vector2(0.68f, 0.66f), 1.4f, gold, true);
        pub.go = g;
    }

    static string TwoLines(string name)
    {
        string s = name.StartsWith("THE ") ? name.Substring(4) : name;
        return "THE\n" + s;
    }

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

    // materials: 0 paint, 1 gold, 2 warm glass, 3 dark wood, 4 black iron, 5 lamp, 6 leaves, 7 flowers, 8 stone
    static void BuildFrontMesh()
    {
        LundunMeshKit k = new LundunMeshKit(9);
        float hw = FrontWidth / 2f;
        k.Box(0, new Vector3(0, 1.8f, 0.05f), new Vector3(FrontWidth, 3.6f, 0.1f));                 // panelling
        k.Box(0, new Vector3(0, 3.35f, 0.2f), new Vector3(FrontWidth + 0.3f, 0.6f, 0.32f));          // fascia
        k.Box(1, new Vector3(0, 3.66f, 0.36f), new Vector3(FrontWidth + 0.3f, 0.05f, 0.04f));
        k.Box(1, new Vector3(0, 3.04f, 0.36f), new Vector3(FrontWidth + 0.3f, 0.05f, 0.04f));
        k.Box(3, new Vector3(0, 3.75f, 0.25f), new Vector3(FrontWidth + 0.5f, 0.14f, 0.42f));        // cornice
        foreach (float x in new[] { -hw, -0.75f, 0.75f, hw })
        {
            k.Box(0, new Vector3(x, 1.5f, 0.16f), new Vector3(0.28f, 3.0f, 0.14f));                  // pilasters
            k.Box(1, new Vector3(x, 2.95f, 0.22f), new Vector3(0.34f, 0.1f, 0.2f));
            k.Box(3, new Vector3(x, 0.15f, 0.2f), new Vector3(0.34f, 0.3f, 0.2f));
        }
        foreach (float s in new[] { -1f, 1f })
        {
            float x = s * 1.875f;
            k.Box(2, new Vector3(x, 1.75f, 0.11f), new Vector3(1.85f, 1.5f, 0.04f));                 // windows (lit, warm)
            k.Box(3, new Vector3(x, 0.5f, 0.13f), new Vector3(1.85f, 0.8f, 0.08f));                  // stall riser
            k.Box(3, new Vector3(x, 0.93f, 0.18f), new Vector3(1.95f, 0.06f, 0.16f));                // sill
            k.Box(3, new Vector3(x, 1.75f, 0.14f), new Vector3(0.06f, 1.5f, 0.04f));                 // glazing bars
            k.Box(3, new Vector3(x, 2.15f, 0.14f), new Vector3(1.85f, 0.06f, 0.04f));
            k.Box(1, new Vector3(x, 1.75f, 0.135f), new Vector3(1.1f, 0.03f, 0.01f));                 // a gold line etched across
            k.Box(4, new Vector3(0.75f * s, 2.75f, 0.32f), new Vector3(0.2f, 0.3f, 0.2f));           // lanterns by the door
            k.Box(5, new Vector3(0.75f * s, 2.75f, 0.33f), new Vector3(0.14f, 0.22f, 0.16f));
            k.Box(4, new Vector3(0.75f * s, 2.95f, 0.26f), new Vector3(0.04f, 0.04f, 0.14f));
            float bx = s * 2.6f;                                                                       // hanging baskets
            k.Box(4, new Vector3(bx, 3.0f, 0.5f), new Vector3(0.03f, 0.3f, 0.03f));
            k.Cyl(6, new Vector3(bx, 2.75f, 0.5f), 0.26f, 0.3f);
            k.Cyl(7, new Vector3(bx, 2.92f, 0.5f), 0.28f, 0.07f);
            k.Cyl(6, new Vector3(bx, 2.58f, 0.5f), 0.16f, 0.12f);
        }
        k.Box(3, new Vector3(0, 1.2f, 0.1f), new Vector3(1.3f, 2.4f, 0.06f));                        // door
        k.Box(2, new Vector3(0, 1.75f, 0.135f), new Vector3(0.8f, 0.7f, 0.02f));
        k.Box(2, new Vector3(0, 2.65f, 0.12f), new Vector3(1.3f, 0.35f, 0.03f));                     // fanlight
        k.Box(1, new Vector3(0.45f, 1.15f, 0.15f), new Vector3(0.05f, 0.2f, 0.05f));                 // brass handle
        k.Box(8, new Vector3(0, 0.05f, 0.3f), new Vector3(1.5f, 0.1f, 0.4f));                         // step
        // the hanging sign on its bracket at the corner end
        k.Box(4, new Vector3(hw, 3.95f, 0.75f), new Vector3(0.06f, 0.06f, 1.3f));
        k.Box(4, new Vector3(hw, 3.7f, 0.35f), new Vector3(0.04f, 0.5f, 0.04f));
        k.Box(4, new Vector3(hw, 3.82f, 1.2f), new Vector3(0.03f, 0.22f, 0.03f));
        k.Box(1, new Vector3(hw, 3.25f, 1.2f), new Vector3(0.08f, 0.9f, 0.8f));
        k.Box(0, new Vector3(hw, 3.25f, 1.2f), new Vector3(0.1f, 0.78f, 0.68f));
        // a barrel to lean on
        k.Cyl(3, new Vector3(2.2f, 0.5f, 1.0f), 0.32f, 1.0f);
        k.Cyl(4, new Vector3(2.2f, 0.25f, 1.0f), 0.335f, 0.05f);
        k.Cyl(4, new Vector3(2.2f, 0.78f, 1.0f), 0.335f, 0.05f);
        k.Cyl(3, new Vector3(2.2f, 1.02f, 1.0f), 0.38f, 0.05f);
        frontMesh = k.Build("PubFront");

        Material gold = LundunMeshKit.Mat("PubGold", new Color(0.82f, 0.64f, 0.25f), 0.7f, Color.black);
        Material glass = LundunMeshKit.Mat("PubWindow", new Color(0.25f, 0.15f, 0.06f), 0.95f, new Color(1f, 0.62f, 0.25f) * 0.9f);
        Material wood = LundunMeshKit.Mat("PubWood", new Color(0.18f, 0.1f, 0.05f), 0.35f, Color.black);
        Material iron = LundunMeshKit.Mat("PubIron", new Color(0.04f, 0.04f, 0.04f), 0.4f, Color.black);
        Material lamp = LundunMeshKit.Mat("PubLamp", new Color(1f, 0.85f, 0.5f), 0.5f, new Color(1f, 0.75f, 0.35f) * 2.2f);
        Material leaves = LundunMeshKit.Mat("PubLeaves", new Color(0.12f, 0.35f, 0.1f), 0.1f, Color.black);
        Material flowers = LundunMeshKit.Mat("PubFlowers", new Color(0.85f, 0.15f, 0.35f), 0.1f, Color.black);
        Material stone = LundunMeshKit.Mat("PubStep", new Color(0.55f, 0.53f, 0.5f), 0.2f, Color.black);
        frontMats = new Material[Paints.Length][];
        for (int i = 0; i < Paints.Length; i++)
        {
            Material paint = LundunMeshKit.Mat("PubPaint" + i, Paints[i], 0.6f, Color.black);
            frontMats[i] = new[] { paint, gold, glass, wood, iron, lamp, leaves, flowers, stone };
        }
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
