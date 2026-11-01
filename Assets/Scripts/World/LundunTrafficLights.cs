using System.Collections.Generic;
using UnityEngine;

/// L4: UK traffic lights at the big junctions (built when the game starts, from the road data; nothing to run in the
/// editor). Where: every city junction where an arterial road or the Ring meets another road (not where only a
/// service lane joins), and Brixtun's four-way arterial junctions; none in Kingsmere, which keeps its gates. About 46.
///   the heads    one on the left kerb at the stop line of every road into the junction (black pole, black head with
///                a white-edged backing board, red / amber / green), and a red man / green man facing across that road
///   the cycle    each pair of opposite roads in turn: red+amber 2 s, green 10–18 s (longer on the Ring and arterials),
///                amber 3 s, all red 1 s; then everyone on red and the green man for people (8 s); every junction
///                starts at a different point, so they're not all in step
///   who obeys    traffic, buses and police patrols stop at red and red+amber, and at amber if they can stop in time
///                (reckless drivers run ambers, and once in a while a red); police with lights on, and James, don't
///                have to; people wait at the kerb for the green man (LundunTrafficCar, PoliceAI, NPCBrain)
///   knocked down they fall like a lamp post (LundunSmash), go dark, and are put back later with everything else
/// Running a red isn't a crime (like GTA): it just risks a crash.
public class LundunTrafficLights : MonoBehaviour
{
    public static LundunTrafficLights Instance { get; private set; }

    public enum Light { Red, RedAmber, Green, Amber }
    const byte RA = 0, GO = 1, AM = 2, ALLRED = 3, WALK = 4;

    class Head
    {
        public Transform t;
        public MeshRenderer r;
        public int shown = -1;
        public bool down;
    }

    class Arm
    {
        public int neighbour, group = -1;
        public Vector3 dir;                    // from the junction out along this road
        public float halfWidth;
        public int rank;
        public Head head;
    }

    class Junction
    {
        public int node;
        public Vector3 pos;
        public float stopLine, radius, cycle, offset;
        public readonly List<Arm> arms = new List<Arm>();
        public float[] starts;                 // when each stage starts (seconds into the cycle); one extra at the end
        public int[] group;                    // whose stage it is (-1 = nobody's)
        public byte[] kind;

        public Arm ArmFrom(int neighbour)
        {
            foreach (Arm a in arms) if (a.neighbour == neighbour) return a;
            return null;
        }
    }

    private readonly List<Junction> junctions = new List<Junction>();
    private readonly Dictionary<int, Junction> byNode = new Dictionary<int, Junction>();
    private LundunRoadGraph graph;
    private float nextShow;
    private Transform cam;
    private Mesh headMesh;
    private Material body, border, redOn, redOff, amberOn, amberOff, greenOn, greenOff, manRedOn, manRedOff, manGreenOn, manGreenOff;
    private readonly Dictionary<int, Material[]> sets = new Dictionary<int, Material[]>();

    void Awake() { Instance = this; }
    void OnDestroy() { if (Instance == this) Instance = null; }

    void Start()
    {
        LundunNavigation nav = LundunNavigation.Instance;
        if (nav == null || nav.Graph == null || nav.World == null) { enabled = false; return; }
        graph = nav.Graph;
        Pick(nav.World);
        MakeLook();
        Transform root = new GameObject("TrafficLights").transform;
        root.SetParent(transform, false);
        foreach (Junction j in junctions) Build(j, root);
        Debug.Log("[Lundun] L4: traffic lights at " + junctions.Count + " junctions");
    }

    // ------------------------------------------------------------------ which junctions
    void Pick(LundunWorldData w)
    {
        Dictionary<string, List<LwRoad>> roadsAt = new Dictionary<string, List<LwRoad>>();
        foreach (LwRoad r in w.roads)
            if (r.junctions != null)
                foreach (string id in r.junctions)
                {
                    List<LwRoad> l;
                    if (!roadsAt.TryGetValue(id, out l)) roadsAt[id] = l = new List<LwRoad>();
                    l.Add(r);
                }
        System.Random rng = new System.Random(4242);
        foreach (LwJunction jn in w.junctions)
        {
            if (jn.id == null || jn.arms < 3 || jn.id.StartsWith("KJ")) continue;              // Kingsmere keeps its gates
            List<LwRoad> roads;
            if (!roadsAt.TryGetValue(jn.id, out roads)) continue;
            bool big = false, service = false;
            foreach (LwRoad r in roads)
            {
                if (r.roadClass == "arterial" || r.roadClass == "ring") big = true;
                if (r.roadClass == "service") service = true;
            }
            if (!big || service) continue;
            if (jn.id.StartsWith("XJ") && jn.arms < 4) continue;                               // Brixtun: just the big crossroads

            int node = graph.NearestNode(new NavPoint(jn.x, jn.z));
            if (node < 0 || byNode.ContainsKey(node)) continue;
            NavPoint np = graph.NodePosition(node);
            if (NavPoint.Dist(np, new NavPoint(jn.x, jn.z)) > 3.0) continue;
            Junction j = new Junction { node = node, pos = new Vector3((float)np.x, 0f, (float)np.z) };
            List<int> next = new List<int>();
            graph.NextNodes(node, -1, next);
            float maxHalf = 4f;
            foreach (int o in next)
            {
                NavPoint q = graph.NodePosition(o);
                Vector3 d = new Vector3((float)(q.x - np.x), 0f, (float)(q.z - np.z));
                if (d.sqrMagnitude < 0.01f) continue;
                double width; string cls;
                graph.EdgeInfo(node, o, out width, out cls);
                Arm a = new Arm { neighbour = o, dir = d.normalized, halfWidth = (float)(width * 0.5), rank = cls == "ring" ? 3 : cls == "arterial" || cls == "bridge" ? 2 : cls == "street" ? 1 : 0 };
                j.arms.Add(a);
                maxHalf = Mathf.Max(maxHalf, a.halfWidth);
            }
            if (j.arms.Count < 3) continue;
            j.stopLine = maxHalf + 3f;
            j.radius = maxHalf + 10f;
            Stages(j);
            j.offset = (float)rng.NextDouble() * j.cycle;
            junctions.Add(j);
            byNode[node] = j;
        }
    }

    /// Opposite roads share a stage; each stage: red+amber, green, amber, all red. Then the green man for everyone.
    static void Stages(Junction j)
    {
        int groups = 0;
        for (int i = 0; i < j.arms.Count; i++)
        {
            if (j.arms[i].group >= 0) continue;
            j.arms[i].group = groups;
            for (int k = i + 1; k < j.arms.Count; k++)
                if (j.arms[k].group < 0 && Vector3.Dot(j.arms[i].dir, j.arms[k].dir) < -0.8f) { j.arms[k].group = groups; break; }
            groups++;
        }
        List<float> dur = new List<float>(); List<int> grp = new List<int>(); List<byte> kind = new List<byte>();
        for (int g = 0; g < groups; g++)
        {
            int rank = 0;
            foreach (Arm a in j.arms) if (a.group == g) rank = Mathf.Max(rank, a.rank);
            dur.Add(2f); grp.Add(g); kind.Add(RA);
            dur.Add(Mathf.Clamp(9f + rank * 3f, 10f, 18f)); grp.Add(g); kind.Add(GO);
            dur.Add(3f); grp.Add(g); kind.Add(AM);
            dur.Add(1f); grp.Add(-1); kind.Add(ALLRED);
        }
        dur.Add(8f); grp.Add(-1); kind.Add(WALK);
        dur.Add(1f); grp.Add(-1); kind.Add(ALLRED);
        j.starts = new float[dur.Count + 1];
        for (int i = 0; i < dur.Count; i++) j.starts[i + 1] = j.starts[i] + dur[i];
        j.cycle = j.starts[dur.Count];
        j.group = grp.ToArray();
        j.kind = kind.ToArray();
    }

    // ------------------------------------------------------------------ what each light shows
    static int StageNow(Junction j)
    {
        float t = Mathf.Repeat(Time.time + j.offset, j.cycle);
        for (int i = 0; i < j.kind.Length; i++) if (t < j.starts[i + 1]) return i;
        return j.kind.Length - 1;
    }

    static Light For(Junction j, int group, int stage)
    {
        if (j.group[stage] != group) return Light.Red;
        switch (j.kind[stage])
        {
            case RA: return Light.RedAmber;
            case GO: return Light.Green;
            case AM: return Light.Amber;
            default: return Light.Red;
        }
    }

    static bool Walk(Junction j, int stage) { return j.kind[stage] == WALK; }

    // ------------------------------------------------------------------ for drivers and people
    /// A driver on the road from 'from' to 'to' (then 'next'), 'toEnd' metres short of 'to', going 'speed' m/s, able to
    /// brake at 'brake' m/s²: the metres to the stop line where they must stop, or -1 to carry on. Red and red+amber:
    /// stop. Amber: stop if they comfortably can (reckless drivers never do). 'dareRed': this one runs the red.
    /// Lights that are knocked down don't count.
    public float StopAhead(int from, int to, int next, float toEnd, float speed, float brake, bool reckless, bool dareRed)
    {
        Junction j; Arm a; float dist;
        if (byNode.TryGetValue(to, out j)) { a = j.ArmFrom(from); dist = toEnd; }
        else if (next >= 0 && byNode.TryGetValue(next, out j))
        {
            a = j.ArmFrom(to);
            NavPoint p = graph.NodePosition(to), q = graph.NodePosition(next);
            dist = toEnd + (float)NavPoint.Dist(p, q);
        }
        else return -1f;
        if (a == null || (a.head != null && a.head.down)) return -1f;
        float line = dist - j.stopLine;
        if (line < -0.5f || line > 70f) return -1f;                                   // over the line already, or far off
        Light l = For(j, a.group, StageNow(j));
        if (l == Light.Green) return -1f;
        if (l == Light.Amber)
        {
            if (reckless) return -1f;
            float need = speed * speed / (2f * Mathf.Max(1f, brake));
            return need > line - 0.5f ? -1f : Mathf.Max(0f, line);
        }
        return dareRed ? -1f : Mathf.Max(0f, line);
    }

    /// Someone on foot at 'p': is there a signalled junction here showing the red man? (Then they wait at the kerb.)
    public bool RedManAt(Vector3 p)
    {
        foreach (Junction j in junctions)
        {
            float dx = p.x - j.pos.x, dz = p.z - j.pos.z;
            if (dx * dx + dz * dz > j.radius * j.radius) continue;
            return !Walk(j, StageNow(j));
        }
        return false;
    }

    // ------------------------------------------------------------------ showing it
    void Update()
    {
        if (Time.time < nextShow) return;
        nextShow = Time.time + 0.1f;
        if (cam == null && Camera.main != null) cam = Camera.main.transform;
        Vector3 c = cam != null ? cam.position : Vector3.zero;
        foreach (Junction j in junctions)
        {
            float dx = c.x - j.pos.x, dz = c.z - j.pos.z;
            if (dx * dx + dz * dz > 260f * 260f) continue;
            int stage = StageNow(j);
            bool walk = Walk(j, stage);
            foreach (Arm a in j.arms)
            {
                Head h = a.head;
                if (h == null || h.r == null) continue;
                int key = h.down ? 99 : (int)For(j, a.group, stage) * 2 + (walk ? 1 : 0);
                if (key == h.shown) continue;
                h.shown = key;
                h.r.sharedMaterials = Set(key);
            }
        }
    }

    Material[] Set(int key)
    {
        Material[] m;
        if (sets.TryGetValue(key, out m)) return m;
        bool dark = key == 99;
        Light l = dark ? Light.Red : (Light)(key / 2);
        bool walk = !dark && (key & 1) == 1;
        bool red = !dark && (l == Light.Red || l == Light.RedAmber), amber = !dark && (l == Light.RedAmber || l == Light.Amber), green = !dark && l == Light.Green;
        m = new[] { body, border, red ? redOn : redOff, amber ? amberOn : amberOff, green ? greenOn : greenOff,
                    !dark && !walk ? manRedOn : manRedOff, walk ? manGreenOn : manGreenOff };
        sets[key] = m;
        return m;
    }

    // ------------------------------------------------------------------ building them
    void Build(Junction j, Transform root)
    {
        int smash = LayerMask.NameToLayer("Smash");
        int ground = LayerMask.GetMask("Default");
        foreach (Arm a in j.arms)
        {
            // on the left kerb (UK) of the road into the junction, at the stop line, facing the traffic coming in
            Vector3 leftIn = new Vector3(a.dir.z, 0f, -a.dir.x);
            Vector3 p = j.pos + a.dir * (j.stopLine - 0.5f) + leftIn * (a.halfWidth + 0.7f);
            p.y = Ground(p, ground);
            GameObject g = new GameObject("TrafficLight");
            g.transform.SetParent(root, false);
            g.transform.SetPositionAndRotation(p, Quaternion.LookRotation(a.dir, Vector3.up));
            g.layer = smash >= 0 ? smash : 0;
            g.AddComponent<MeshFilter>().sharedMesh = headMesh;
            MeshRenderer r = g.AddComponent<MeshRenderer>();
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            r.sharedMaterials = Set(0);
            BoxCollider bc = g.AddComponent<BoxCollider>();
            bc.center = new Vector3(0f, 1.7f, 0f); bc.size = new Vector3(0.22f, 3.4f, 0.22f);
            Head h = new Head { t = g.transform, r = r };
            a.head = h;
            int layer = g.layer;
            if (LundunSmash.Instance != null)
                LundunSmash.Instance.AddLoose(g.transform, LundunSmash.Kind.TrafficLight, new Vector3(0.12f, 3.4f, 0.12f), down =>
                {
                    h.down = down;
                    h.shown = -1;
                    if (h.t != null) h.t.gameObject.layer = down ? 0 : layer;            // lying on the road, it's solid
                    if (h.r != null) h.r.sharedMaterials = Set(down ? 99 : 0);
                });
        }
    }

    static float Ground(Vector3 p, int mask)
    {
        // the lowest thing under it (the pavement, not an awning or a bridge above)
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(p.x, 60f, p.z), Vector3.down, 120f, mask, QueryTriggerInteraction.Ignore);
        float y = float.MaxValue;
        if (hits != null) foreach (RaycastHit h in hits) if (h.point.y < y && h.point.y > -20f) y = h.point.y;
        return y == float.MaxValue ? 0f : y;
    }

    void MakeLook()
    {
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        body = Mat(lit, new Color(0.04f, 0.04f, 0.045f), Color.black, null, "TL_Body");
        border = Mat(lit, new Color(0.92f, 0.92f, 0.9f), Color.black, null, "TL_Border");
        Color R = new Color(1f, 0.08f, 0.05f), A = new Color(1f, 0.55f, 0.05f), G = new Color(0.15f, 1f, 0.45f);
        redOn = Mat(lit, R, R * 3.5f, null, "TL_RedOn"); redOff = Mat(lit, R * 0.18f, Color.black, null, "TL_RedOff");
        amberOn = Mat(lit, A, A * 3.5f, null, "TL_AmberOn"); amberOff = Mat(lit, A * 0.18f, Color.black, null, "TL_AmberOff");
        greenOn = Mat(lit, G, G * 3.5f, null, "TL_GreenOn"); greenOff = Mat(lit, G * 0.15f, Color.black, null, "TL_GreenOff");
        Texture2D standing = Glyph(StandingMan), walking = Glyph(WalkingMan);
        manRedOn = Mat(lit, R, R * 3f, standing, "TL_ManRedOn"); manRedOff = Mat(lit, R * 0.15f, Color.black, standing, "TL_ManRedOff");
        manGreenOn = Mat(lit, G, G * 3f, walking, "TL_ManGreenOn"); manGreenOff = Mat(lit, G * 0.12f, Color.black, walking, "TL_ManGreenOff");
        headMesh = HeadMesh();
    }

    static Material Mat(Shader s, Color c, Color glow, Texture2D tex, string name)
    {
        Material m = new Material(s) { name = name };
        m.SetColor("_BaseColor", c); m.color = c;
        m.SetFloat("_Smoothness", 0.35f);
        if (tex != null) m.SetTexture("_BaseMap", tex);
        if (glow.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", glow);
            if (tex != null) m.SetTexture("_EmissionMap", tex);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        return m;
    }

    // ------------------------------------------------------------------ the shapes
    // 0 pole + head + hoods   1 backing board's white edge   2 red   3 amber   4 green   5 red man   6 green man
    static Mesh HeadMesh()
    {
        List<Vector3> v = new List<Vector3>(); List<Vector3> n = new List<Vector3>(); List<Vector2> uv = new List<Vector2>();
        List<int>[] t = new List<int>[7];
        for (int i = 0; i < t.Length; i++) t[i] = new List<int>();
        // the pole
        Box(v, n, uv, t[0], new Vector3(0f, 1.6f, 0f), new Vector3(0.12f, 3.2f, 0.12f));
        // the head: housing, the black board behind it and its white edge
        Box(v, n, uv, t[0], new Vector3(0f, 2.75f, 0.04f), new Vector3(0.34f, 1.0f, 0.24f));
        Box(v, n, uv, t[0], new Vector3(0f, 2.75f, -0.1f), new Vector3(0.56f, 1.22f, 0.03f));
        Box(v, n, uv, t[1], new Vector3(0f, 2.75f, -0.115f), new Vector3(0.62f, 1.28f, 0.02f));
        // the three lamps (on the front, facing the traffic), each with a hood over it
        float[] ys = { 3.05f, 2.75f, 2.45f };
        for (int i = 0; i < 3; i++)
        {
            Box(v, n, uv, t[2 + i], new Vector3(0f, ys[i], 0.17f), new Vector3(0.21f, 0.21f, 0.02f));
            Box(v, n, uv, t[0], new Vector3(0f, ys[i] + 0.125f, 0.22f), new Vector3(0.25f, 0.02f, 0.12f));
        }
        // the red man / green man box, facing across the road (the road is to the pole's left: −x)
        Box(v, n, uv, t[0], new Vector3(-0.16f, 2.05f, 0f), new Vector3(0.2f, 0.62f, 0.32f));
        Face(v, n, uv, t[5], new Vector3(-0.265f, 2.2f, 0f), 0.26f, 0.26f);
        Face(v, n, uv, t[6], new Vector3(-0.265f, 1.9f, 0f), 0.26f, 0.26f);
        Mesh m = new Mesh { name = "TrafficLight" };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
        m.subMeshCount = t.Length;
        for (int i = 0; i < t.Length; i++) m.SetTriangles(t[i], i);
        m.RecalculateBounds();
        return m;
    }

    static void Box(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 c, Vector3 s)
    {
        Vector3 h = s * 0.5f;
        Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        foreach (Vector3 d in dirs)
        {
            Vector3 a = d.y != 0f ? Vector3.forward : Vector3.up;
            Vector3 b = Vector3.Cross(d, a);
            Vector3 centre = c + Vector3.Scale(d, h);
            Vector3 ea = Vector3.Scale(a, h), eb = Vector3.Scale(b, h);
            int i0 = v.Count;
            v.Add(centre - ea - eb); v.Add(centre - ea + eb); v.Add(centre + ea + eb); v.Add(centre + ea - eb);
            for (int k = 0; k < 4; k++) n.Add(d);
            uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
            // wound so the face points along d (Unity: a triangle faces along Cross(b - a, c - a))
            if (Vector3.Dot(Vector3.Cross(v[i0 + 1] - v[i0], v[i0 + 2] - v[i0]), d) > 0f) { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3); }
            else { t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2); }
        }
    }

    /// A flat square facing −x (towards the road), with the picture the right way up.
    static void Face(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> t, Vector3 c, float w, float h)
    {
        int i0 = v.Count;
        // seen from −x, +z is on the viewer's left: so u runs from +z to −z
        v.Add(c + new Vector3(0f, -h / 2f, w / 2f)); v.Add(c + new Vector3(0f, -h / 2f, -w / 2f));
        v.Add(c + new Vector3(0f, h / 2f, -w / 2f)); v.Add(c + new Vector3(0f, h / 2f, w / 2f));
        for (int k = 0; k < 4; k++) n.Add(Vector3.left);
        uv.Add(new Vector2(0f, 0f)); uv.Add(new Vector2(1f, 0f)); uv.Add(new Vector2(1f, 1f)); uv.Add(new Vector2(0f, 1f));
        if (Vector3.Dot(Vector3.Cross(v[i0 + 1] - v[i0], v[i0 + 2] - v[i0]), Vector3.left) > 0f) { t.Add(i0); t.Add(i0 + 1); t.Add(i0 + 2); t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 3); }
        else { t.Add(i0); t.Add(i0 + 2); t.Add(i0 + 1); t.Add(i0); t.Add(i0 + 3); t.Add(i0 + 2); }
    }

    static readonly string[] StandingMan = {
        "............",
        ".....XX.....",
        ".....XX.....",
        "............",
        "....XXXX....",
        "...X.XX.X...",
        "...X.XX.X...",
        "...X.XX.X...",
        "...X.XX.X...",
        ".....XX.....",
        "....X..X....",
        "....X..X....",
        "....X..X....",
        "....X..X....",
        "...XX..XX...",
        "............" };
    static readonly string[] WalkingMan = {
        "............",
        "......XX....",
        "......XX....",
        "............",
        ".....XXX....",
        "....X.XX.X..",
        "...X..XX..X.",
        "......XX....",
        "......XX....",
        ".....X..X...",
        "....X....X..",
        "...X......X.",
        "..X........X",
        "..X.........",
        "............",
        "............" };

    /// The man, white on black (the material's colour tints him red or green), the right way up.
    static Texture2D Glyph(string[] rows)
    {
        int w = rows[0].Length, h = rows.Length;
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "TL_Man" };
        Color on = Color.white, off = new Color(0.03f, 0.03f, 0.03f, 1f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                tex.SetPixel(x, h - 1 - y, rows[y][x] == 'X' ? on : off);
        tex.Apply(false, true);
        return tex;
    }
}
