using UnityEngine;

/// Phase 5 (gate B): the suppressor, an upgrade to James's pistol (no new weapon slot, nothing resized, same save
/// version: it's one key in the story save, "story.suppressor"). The arms dealer sells it once M06 is done.
/// With it, pistol shots are quiet:
///   nobody hears them   no "shots fired" for the police, the crowd or traffic (PlayerCombat skips both): only people
///                       who can actually see it happen, or who get hit, react
///   cameras             shooting out a camera with it is a quiet disable: the camera's down, no gunfire attention
///   guards              a guard within a few metres still hears it (LundunSneak)
///   looks and sounds    a suppressor on the barrel, a small flash, a soft crack
/// Every other gun (and the pistol without it) is as loud as ever. Story only.
public class LundunSuppressor : MonoBehaviour
{
    public const string Key = "story.suppressor";
    /// Placeholder price (tuned in Phase 6).
    public const long Price = 1500;
    /// How close a guard has to be to hear a suppressed shot.
    public const float HeardWithin = 5f;

    /// Editor test rig only: pretend it's owned (Free Roam included).
    public static bool TestOverride;

    /// The story has it.
    public static bool Owned
    {
        get
        {
            if (TestOverride) return true;
            if (!LundunGame.IsStory) return false;
            string v;
            return LundunStory.TryGet(Key, out v) && v == "1";
        }
    }

    /// The arms dealer offers it (after M06).
    public static bool OnSale { get { return LundunGame.IsStory && LundunStory.Data != null && LundunStory.Data.flags.Contains("done:m06_passenger_problems"); } }

    public static void Buy()
    {
        LundunStory.Set(Key, "1");
        LundunStory.MarkDirty();
        Ensure();
    }

    /// The gun in James's hands right now is the suppressed pistol.
    public static bool Quiet
    {
        get
        {
            LundunWeapons w = LundunWeapons.Instance;
            return Owned && w != null && w.Current != null && w.Current.kind == LundunWeapons.Kind.Pistol;
        }
    }

    /// The last quiet shot (for guards close enough to hear it).
    public static Vector3 LastShotAt { get; private set; }
    public static float LastShotTime { get; private set; } = -99f;

    /// PlayerCombat: a suppressed shot was fired from here.
    public static void QuietShot(Vector3 at) { LastShotAt = at; LastShotTime = Time.time; }

    /// A suppressed shot was fired within 'within' metres of p in the last half second.
    public static bool HeardNear(Vector3 p, float within)
    {
        if (Time.time - LastShotTime > 0.5f) return false;
        return LundunMissionRun.FlatDistance(p, LastShotAt) < within;
    }

    // ------------------------------------------------------------------ the part on the barrel
    static LundunSuppressor instance;

    /// Makes sure the visual is looked after (called by the game when a story starts, the dealer and the test rig).
    public static void Ensure()
    {
        if (instance != null) return;
        GameObject g = new GameObject("LundunSuppressor");
        instance = g.AddComponent<LundunSuppressor>();
    }

    private Transform part, onMuzzle;
    private static Material mat;

    void LateUpdate()
    {
        LundunWeaponRig rig = LundunWeaponRig.Instance;
        Transform muzzle = rig != null ? rig.Muzzle : null;
        bool want = Quiet && muzzle != null;
        if (!want) { if (part != null) part.gameObject.SetActive(false); return; }
        if (part == null || onMuzzle != muzzle)
        {
            if (part != null) Destroy(part.gameObject);
            part = Build(muzzle).transform;
            onMuzzle = muzzle;
        }
        if (!part.gameObject.activeSelf) part.gameObject.SetActive(true);
        int layer = muzzle.gameObject.layer;                         // first person draws the gun on its own layer
        if (part.gameObject.layer != layer) part.gameObject.layer = layer;
    }

    static GameObject Build(Transform muzzle)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.name = "Suppressor";
        Collider c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
        g.transform.SetParent(muzzle, false);
        g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);    // the cylinder's length along the barrel
        g.transform.localPosition = new Vector3(0f, 0f, 0.075f);
        g.transform.localScale = new Vector3(0.042f, 0.08f, 0.042f);  // 16 cm long, 4 cm across
        Renderer r = g.GetComponent<Renderer>();
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.sharedMaterial = Mat();
        return g;
    }

    static Material Mat()
    {
        if (mat != null) return mat;
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        mat = new Material(s != null ? s : Shader.Find("Standard")) { name = "Suppressor" };
        Color c = new Color(0.07f, 0.07f, 0.075f);
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        mat.color = c;
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
        if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0.6f);
        return mat;
    }
}
