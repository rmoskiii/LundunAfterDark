using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// G3: the look of a gunfight. Everything is pooled (made once, reused) and drawn with two simple see-through
/// materials, so firing costs almost nothing extra on the phone.
///   • muzzle flash: a burst of light petals at the muzzle (size per gun) + a quick warm light
///   • gun smoke: soft puffs that drift up and fade
///   • spent cases: brass flicks out to the right, bounces and tinkles (red shotgun shells fly out on the pump)
///   • bullet holes on walls and ground, a little dust puff where shots land, sparks off cars
/// Textures are drawn in code. Created on first use.
public class LundunCombatFX : MonoBehaviour
{
    static LundunCombatFX instance;
    static LundunCombatFX I
    {
        get
        {
            if (instance == null) instance = new GameObject("LundunCombatFX").AddComponent<LundunCombatFX>();
            return instance;
        }
    }

    const int MaxPuffs = 40, MaxFlashes = 6, MaxCases = 40, MaxHoles = 70, MaxSparks = 24;

    class Fx { public Transform t; public Renderer r; public Material m; public float born, life, size0, size1, roll; public Vector3 vel; public Color col; public bool billboard, streak; }
    class Case { public Transform t; public Vector3 vel, spin; public float born, floor; public int bounces; public bool active, shell; }

    private readonly List<Fx> puffs = new List<Fx>(), flashes = new List<Fx>(), sparks = new List<Fx>();
    private readonly List<Transform> holes = new List<Transform>();
    private readonly List<Case> cases = new List<Case>();
    private int nextPuff, nextFlash, nextHole, nextCase, nextSpark;
    private Light flashLight;
    private float flashLightOff;
    private Mesh quad;
    private Material puffMat, flashMat, holeMat, sparkMat;
    private Mesh caseMesh, shellMesh;
    private readonly AudioSource[] tinkles = new AudioSource[3];
    private int nextTinkle;
    private AudioClip tinkleClip;

    // ------------------------------------------------------------------ public
    /// A shot: flash and smoke at the muzzle. size: 1 normal, bigger for the shotgun.
    public static void Muzzle(Transform muzzle, float size, int smokePuffs)
    {
        if (muzzle == null) return;
        LundunCombatFX fx = I;
        fx.Flash(muzzle.position, muzzle.forward, size);
        for (int i = 0; i < smokePuffs; i++)
            fx.Puff(muzzle.position + muzzle.forward * Random.Range(0.02f, 0.12f), muzzle.forward * Random.Range(0.4f, 1.1f) + Vector3.up * 0.25f,
                    new Color(0.75f, 0.75f, 0.74f, 0.32f), 0.07f * size, 0.5f * size, Random.Range(0.8f, 1.3f));
    }

    /// Throw out a spent case from the ejection port (to the gun's right).
    public static void Eject(Transform port, bool shotgunShell)
    {
        if (port == null) return;
        I.ThrowCase(port.position, port.right * Random.Range(2.2f, 3.2f) + port.up * Random.Range(1.2f, 2f) - port.forward * Random.Range(0.2f, 0.7f), shotgunShell);
    }

    /// A bullet landing: hole + dust on solid ground/walls, sparks on vehicles.
    public static void Impact(RaycastHit hit, bool metal)
    {
        LundunCombatFX fx = I;
        if (metal) { for (int i = 0; i < 5; i++) fx.Spark(hit.point + hit.normal * 0.02f, Vector3.Reflect(Random.onUnitSphere, hit.normal) * 0.4f + hit.normal * Random.Range(3f, 7f) + Random.insideUnitSphere * 2f); return; }
        fx.Puff(hit.point + hit.normal * 0.05f, hit.normal * 0.6f + Vector3.up * 0.2f, new Color(0.62f, 0.58f, 0.52f, 0.45f), 0.08f, 0.45f, 0.7f);
        if (hit.collider != null && hit.collider.attachedRigidbody == null) fx.Hole(hit.point, hit.normal);
    }

    // ------------------------------------------------------------------ set-up
    void Awake()
    {
        quad = MakeQuad();
        puffMat = Mat(Tex("puff", 64, PuffShape), false);
        flashMat = Mat(Tex("flash", 64, FlashShape), true);
        holeMat = Mat(Tex("hole", 64, HoleShape), false);
        sparkMat = Mat(Tex("spark", 32, SparkShape), true);
        caseMesh = LundunGunModels.CaseMesh(false);
        shellMesh = LundunGunModels.CaseMesh(true);
        GameObject l = new GameObject("MuzzleLight");
        l.transform.SetParent(transform, false);
        flashLight = l.AddComponent<Light>();
        flashLight.type = LightType.Point; flashLight.range = 7f; flashLight.intensity = 0f;
        flashLight.color = new Color(1f, 0.72f, 0.38f); flashLight.shadows = LightShadows.None;
        tinkleClip = LundunGunAudio.Casing;
        for (int i = 0; i < tinkles.Length; i++)
        {
            GameObject a = new GameObject("Tinkle" + i);
            a.transform.SetParent(transform, false);
            tinkles[i] = a.AddComponent<AudioSource>();
            tinkles[i].playOnAwake = false; tinkles[i].spatialBlend = 1f; tinkles[i].minDistance = 2f; tinkles[i].maxDistance = 25f; tinkles[i].volume = 0.5f;
        }
    }

    // ------------------------------------------------------------------ pieces
    void Flash(Vector3 at, Vector3 dir, float size)
    {
        Fx f = Next(flashes, ref nextFlash, MaxFlashes, flashMat, "Flash");
        f.t.position = at + dir * 0.04f * size;
        f.billboard = true; f.streak = false;
        f.born = Time.time; f.life = 0.05f;
        f.size0 = f.size1 = 0.32f * size * Random.Range(0.85f, 1.2f);
        f.vel = Vector3.zero; f.col = new Color(1f, 0.85f, 0.55f, 1f);
        f.roll = Random.Range(0f, 360f);
        // a second, smaller petal pointing along the barrel (seen from the side)
        Fx g = Next(flashes, ref nextFlash, MaxFlashes, flashMat, "Flash");
        g.t.position = at + dir * 0.12f * size;
        g.billboard = false; g.streak = false; g.born = Time.time; g.life = 0.04f;
        g.size0 = g.size1 = 0.22f * size; g.vel = Vector3.zero; g.col = new Color(1f, 0.8f, 0.45f, 0.9f);
        g.t.rotation = Quaternion.LookRotation(Vector3.Cross(dir, Vector3.up).sqrMagnitude > 0.01f ? Vector3.Cross(dir, Vector3.up) : Vector3.right, Vector3.up) * Quaternion.Euler(0f, 0f, Random.Range(-20f, 20f));
        flashLight.transform.position = at;
        flashLight.intensity = 2.6f * Mathf.Clamp(size, 0.6f, 1.8f);
        flashLightOff = Time.time + 0.045f;
    }

    void Puff(Vector3 at, Vector3 vel, Color col, float from, float to, float life)
    {
        Fx f = Next(puffs, ref nextPuff, MaxPuffs, puffMat, "Puff");
        f.t.position = at; f.billboard = true; f.streak = false;
        f.born = Time.time; f.life = life; f.size0 = from; f.size1 = to; f.vel = vel; f.col = col;
        f.roll = Random.Range(0f, 360f);
    }

    void Spark(Vector3 at, Vector3 vel)
    {
        Fx f = Next(sparks, ref nextSpark, MaxSparks, sparkMat, "Spark");
        f.t.position = at; f.billboard = false; f.streak = true;
        f.born = Time.time; f.life = Random.Range(0.08f, 0.18f); f.size0 = 0.05f; f.size1 = 0.02f; f.vel = vel;
        f.col = new Color(1f, 0.75f, 0.35f, 1f);
    }

    void Hole(Vector3 at, Vector3 normal)
    {
        Transform t;
        if (holes.Count < MaxHoles) { t = NewQuad("Hole", holeMat).transform; holes.Add(t); }
        else { t = holes[nextHole]; nextHole = (nextHole + 1) % MaxHoles; }
        t.gameObject.SetActive(true);
        t.position = at + normal * 0.006f;
        t.rotation = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        float s = Random.Range(0.06f, 0.09f);
        t.localScale = new Vector3(s, s, 1f);
    }

    void ThrowCase(Vector3 at, Vector3 vel, bool shell)
    {
        Case c;
        if (cases.Count < MaxCases)
        {
            GameObject g = new GameObject("Case");
            g.transform.SetParent(transform, false);
            g.AddComponent<MeshFilter>();
            MeshRenderer mr = g.AddComponent<MeshRenderer>(); mr.shadowCastingMode = ShadowCastingMode.Off;
            c = new Case { t = g.transform };
            cases.Add(c);
        }
        else { c = cases[nextCase]; nextCase = (nextCase + 1) % MaxCases; }
        c.t.GetComponent<MeshFilter>().sharedMesh = shell ? shellMesh : caseMesh;
        c.t.GetComponent<MeshRenderer>().sharedMaterials = LundunGunModels.CaseMaterials(shell);
        c.t.gameObject.SetActive(true);
        c.t.position = at; c.t.rotation = Random.rotation;
        c.vel = vel; c.spin = Random.insideUnitSphere * 1400f; c.born = Time.time; c.bounces = 0; c.active = true; c.shell = shell;
        c.floor = Physics.Raycast(at + Vector3.up * 0.2f, Vector3.down, out RaycastHit h, 6f, LayerMask.GetMask("Default"), QueryTriggerInteraction.Ignore) ? h.point.y + (shell ? 0.01f : 0.005f) : at.y - 1.5f;
    }

    // ------------------------------------------------------------------ every frame
    void LateUpdate()
    {
        float now = Time.time, dt = Time.deltaTime;
        Camera cam = Camera.main;
        Quaternion face = cam != null ? cam.transform.rotation : Quaternion.identity;
        if (flashLight.intensity > 0f && now > flashLightOff) flashLight.intensity = 0f;
        Animate(flashes, now, dt, face, cam);
        Animate(puffs, now, dt, face, cam);
        Animate(sparks, now, dt, face, cam);

        foreach (Case c in cases)
        {
            if (!c.active) continue;
            float age = now - c.born;
            if (age > 6f) { c.active = false; c.t.gameObject.SetActive(false); continue; }
            if (c.bounces >= 3) continue;                                         // lying still on the ground
            c.vel += Physics.gravity * dt;
            c.t.position += c.vel * dt;
            c.t.Rotate(c.spin * dt, Space.World);
            if (c.t.position.y <= c.floor)
            {
                Vector3 p = c.t.position; p.y = c.floor; c.t.position = p;
                c.vel = new Vector3(c.vel.x * 0.45f, -c.vel.y * 0.35f, c.vel.z * 0.45f);
                c.spin *= 0.4f;
                c.bounces++;
                if (c.bounces == 1) Tinkle(p, c.shell);
                if (c.bounces >= 3) c.t.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 90f);   // settle on its side
            }
        }
    }

    void Animate(List<Fx> list, float now, float dt, Quaternion face, Camera cam)
    {
        foreach (Fx f in list)
        {
            if (!f.t.gameObject.activeSelf) continue;
            float k = (now - f.born) / Mathf.Max(0.001f, f.life);
            if (k >= 1f) { f.t.gameObject.SetActive(false); continue; }
            f.t.position += f.vel * dt;
            if (f.streak) f.vel += Physics.gravity * dt * 0.5f;
            else f.vel *= 1f - Mathf.Min(1f, dt * 1.8f);
            float s = Mathf.Lerp(f.size0, f.size1, 1f - (1f - k) * (1f - k));
            if (f.billboard) { f.t.rotation = face * Quaternion.Euler(0f, 0f, f.roll); f.t.localScale = new Vector3(s, s, 1f); }
            else if (f.streak)
            {
                Vector3 v = f.vel.sqrMagnitude > 0.01f ? f.vel.normalized : Vector3.up;
                Vector3 toCam = cam != null ? (cam.transform.position - f.t.position).normalized : Vector3.back;
                f.t.rotation = Quaternion.LookRotation(-toCam, v);
                f.t.localScale = new Vector3(0.012f, Mathf.Max(0.04f, f.vel.magnitude * 0.03f), 1f);
            }
            else f.t.localScale = new Vector3(s, s * 0.45f, 1f);
            Color c = f.col; c.a *= 1f - k;
            f.m.SetColor("_BaseColor", c);
        }
    }

    void Tinkle(Vector3 at, bool shell)
    {
        if (tinkleClip == null) return;
        AudioSource a = tinkles[nextTinkle]; nextTinkle = (nextTinkle + 1) % tinkles.Length;
        a.transform.position = at;
        a.pitch = shell ? Random.Range(0.55f, 0.7f) : Random.Range(0.9f, 1.25f);
        a.PlayOneShot(tinkleClip, shell ? 0.35f : 0.5f);
    }

    // ------------------------------------------------------------------ helpers
    Fx Next(List<Fx> list, ref int next, int max, Material shared, string name)
    {
        Fx f;
        if (list.Count < max)
        {
            GameObject g = NewQuad(name, shared);
            Renderer r = g.GetComponent<Renderer>();
            f = new Fx { t = g.transform, r = r, m = r.material };          // own copy, so each can fade on its own
            list.Add(f);
        }
        else { f = list[next]; next = (next + 1) % max; }
        f.t.gameObject.SetActive(true);
        return f;
    }

    GameObject NewQuad(string name, Material m)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(transform, false);
        g.AddComponent<MeshFilter>().sharedMesh = quad;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        return g;
    }

    static Mesh MakeQuad()
    {
        Mesh m = new Mesh { name = "FxQuad" };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(0.5f, -0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(-0.5f, 0.5f, 0) };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    static Material Mat(Texture2D tex, bool additive)
    {
        Shader s = Shader.Find("Universal Render Pipeline/Unlit");
        Material m = new Material(s) { name = "Fx_" + tex.name };
        m.SetTexture("_BaseMap", tex);
        m.SetColor("_BaseColor", Color.white);
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 0f); m.SetFloat("_ZWrite", 0f);
        m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_Cull", (float)CullMode.Off);
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        m.renderQueue = (int)RenderQueue.Transparent + (additive ? 10 : 0);
        return m;
    }

    delegate Color Shape(float x, float y);       // x, y in -1..1

    static Texture2D Tex(string name, int size, Shape shape)
    {
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Clamp };
        Color[] px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = shape((x + 0.5f) / size * 2f - 1f, (y + 0.5f) / size * 2f - 1f);
        t.SetPixels(px); t.Apply(true, true);
        return t;
    }

    static Color PuffShape(float x, float y)
    {
        float r = Mathf.Sqrt(x * x + y * y);
        float n = Mathf.PerlinNoise(x * 3f + 7f, y * 3f + 3f) * 0.5f + 0.5f;
        float a = Mathf.Clamp01(1f - r) * Mathf.Clamp01(1f - r) * n;
        return new Color(1f, 1f, 1f, a);
    }

    static Color FlashShape(float x, float y)
    {
        float r = Mathf.Sqrt(x * x + y * y);
        float ang = Mathf.Atan2(y, x);
        float petals = Mathf.Pow(Mathf.Abs(Mathf.Cos(ang * 2.5f)), 6f);        // five spiky petals
        float a = Mathf.Clamp01(1f - r * 1.6f) + Mathf.Clamp01(1f - r) * petals * 0.9f;
        a = Mathf.Clamp01(a);
        float hot = Mathf.Clamp01(1f - r * 3f);
        return new Color(1f, Mathf.Lerp(0.75f, 1f, hot), Mathf.Lerp(0.4f, 0.95f, hot), a);
    }

    static Color HoleShape(float x, float y)
    {
        float r = Mathf.Sqrt(x * x + y * y);
        float jag = Mathf.PerlinNoise(Mathf.Atan2(y, x) * 2.5f + 11f, 0.5f) * 0.25f;
        float core = r < 0.22f ? 1f : 0f;
        float ring = Mathf.Clamp01(1f - (r - 0.22f) / (0.55f + jag)) * 0.75f;
        float a = Mathf.Max(core, ring) * Mathf.Clamp01((1f - r) * 3f);
        float shade = r < 0.22f ? 0.02f : 0.12f;
        return new Color(shade, shade, shade, a);
    }

    static Color SparkShape(float x, float y)
    {
        float a = Mathf.Clamp01(1f - Mathf.Abs(x) * 1.2f) * Mathf.Clamp01(1f - Mathf.Abs(y));
        return new Color(1f, 0.9f, 0.6f, a);
    }
}
