using UnityEngine;

/// V1: skid marks. Every sliding wheel lays a dark strip on the road; all of them share one mesh (one draw call), the
/// oldest disappearing once there are 600 pieces, and each fading out over a couple of minutes. Made by the first
/// wheel that skids (no scene object needed).
public class LundunSkids : MonoBehaviour
{
    const int Max = 600;
    const float Width = 0.24f, MinStep = 0.35f, FadeAfter = 90f, FadeOver = 60f;

    static LundunSkids instance;
    private Mesh mesh;
    private Vector3[] verts = new Vector3[Max * 4];
    private Color32[] cols = new Color32[Max * 4];
    private float[] born = new float[Max];
    private byte[] alpha = new byte[Max];
    private int next, used;
    private bool dirty;
    private float nextFade;

    // the strip each wheel is laying: where it got to and the edge it ended on
    private Vector3[] lastPos = new Vector3[256], lastL = new Vector3[256], lastR = new Vector3[256];
    private bool[] busy = new bool[256], fresh = new bool[256];

    /// The wheel has stopped skidding: its mark ends here.
    public static void End(int id)
    {
        if (instance != null && id >= 0 && id < instance.busy.Length) instance.busy[id] = false;
    }

    /// A wheel at 'point' is skidding. Pass the id from last time (−1 to start a new mark); returns the id to pass
    /// next time.
    public static int Mark(int id, Vector3 point, Vector3 normal, float strength)
    {
        if (instance == null)
        {
            GameObject g = new GameObject("LundunSkids");
            instance = g.AddComponent<LundunSkids>();
            instance.Make();
        }
        return instance.Add(id, point, normal, strength);
    }

    void Make()
    {
        mesh = new Mesh { name = "SkidMarks" };
        mesh.MarkDynamic();
        int[] tri = new int[Max * 6];
        for (int i = 0; i < Max; i++)
        {
            int v = i * 4, t = i * 6;
            tri[t] = v; tri[t + 1] = v + 1; tri[t + 2] = v + 2; tri[t + 3] = v; tri[t + 4] = v + 2; tri[t + 5] = v + 3;
        }
        mesh.vertices = verts; mesh.colors32 = cols; mesh.triangles = tri;
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = gameObject.AddComponent<MeshRenderer>();
        Shader sh = Shader.Find("Sprites/Default");                         // vertex colour with alpha, always available
        Material m = new Material(sh) { renderQueue = 2990 };
        m.color = Color.white;
        mr.sharedMaterial = m;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    int Add(int id, Vector3 p, Vector3 n, float strength)
    {
        p += n * 0.02f;                                                       // just above the road
        if (id < 0 || id >= busy.Length || !busy[id])
        {
            id = FreeId();
            if (id < 0) return -1;
            busy[id] = true; lastPos[id] = p; fresh[id] = true;
            return id;
        }
        Vector3 d = p - lastPos[id];
        if (d.sqrMagnitude < MinStep * MinStep) return id;
        Vector3 side = Vector3.Cross(n, d.normalized) * (Width * 0.5f);
        Vector3 l = p - side, r = p + side;
        if (fresh[id]) { lastL[id] = lastPos[id] - side; lastR[id] = lastPos[id] + side; fresh[id] = false; }
        int q = next; next = (next + 1) % Max; used = Mathf.Min(Max, used + 1);
        int v = q * 4;
        verts[v] = lastL[id]; verts[v + 1] = l; verts[v + 2] = r; verts[v + 3] = lastR[id];
        byte a = (byte)Mathf.RoundToInt(Mathf.Lerp(70f, 150f, Mathf.Clamp01(strength)));
        alpha[q] = a; born[q] = Time.time;
        Color32 c = new Color32(18, 18, 18, a);
        cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = c;
        lastPos[id] = p; lastL[id] = l; lastR[id] = r;
        dirty = true;
        return id;
    }

    int FreeId()
    {
        for (int i = 0; i < busy.Length; i++) if (!busy[i]) return i;
        // all taken: recycle (old wheels never told us they stopped)
        for (int i = 0; i < busy.Length; i++) busy[i] = false;
        return 0;
    }

    void LateUpdate()
    {
        // marks a wheel stopped adding to are ended by the wheel passing −1 next time; here, just the fade
        if (Time.time >= nextFade)
        {
            nextFade = Time.time + 1f;
            for (int q = 0; q < used; q++)
            {
                float age = Time.time - born[q];
                if (age < FadeAfter) continue;
                float k = 1f - Mathf.Clamp01((age - FadeAfter) / FadeOver);
                byte a = (byte)(alpha[q] * k);
                int v = q * 4;
                if (cols[v].a == a) continue;
                Color32 c = cols[v]; c.a = a;
                cols[v] = cols[v + 1] = cols[v + 2] = cols[v + 3] = c;
                dirty = true;
            }
        }
        if (!dirty || mesh == null) return;
        dirty = false;
        mesh.vertices = verts;
        mesh.colors32 = cols;
    }
}
