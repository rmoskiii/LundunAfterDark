using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Dresses a blocky car (Body + Cabin boxes) in a paint job made of textures.
// Click ⋮ on this component and choose "Build livery". Safe to run again.
public class CarLivery : MonoBehaviour
{
    [Header("Paint")]
    [SerializeField] private Color paintColour = new Color(1f, 0.72f, 0.05f);    // body
    [SerializeField] private Color roofColour = new Color(0.06f, 0.06f, 0.07f);  // cabin / roof
    [SerializeField, Range(0f, 1f)] private float paintSmoothness = 0.85f;
    [SerializeField, Range(0f, 1f)] private float paintMetallic = 0.3f;

    [Header("Car parts (found by name if left empty)")]
    [SerializeField] private Transform body;
    [SerializeField] private Transform cabin;

    [Header("Livery textures")]
    [SerializeField] private Texture2D sideTexture;
    [SerializeField] private Texture2D bonnetTexture;
    [SerializeField] private Texture2D frontTexture;
    [SerializeField] private Texture2D rearTexture;
    [SerializeField] private Texture2D windowTexture;

    private const float Gap = 0.006f;   // tiny lift off the surface so panels don't flicker

#if UNITY_EDITOR
    [ContextMenu("Build livery")]
    void Build()
    {
        if (body == null) body = transform.Find("Body");
        if (cabin == null) cabin = transform.Find("Cabin");
        if (body == null || cabin == null || sideTexture == null)
        {
            Debug.LogError("CarLivery: needs Body, Cabin and at least the side texture.");
            return;
        }

        // Materials are saved next to the textures so the car keeps them
        string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(sideTexture));
        string p = name.Replace(" ", "") + "_";
        Material paint  = MakeMaterial(folder, p + "Paint",  null, paintColour, paintSmoothness, paintMetallic);
        Material roof   = MakeMaterial(folder, p + "Roof",   null, roofColour, 0.8f, 0f);
        Material side   = MakeMaterial(folder, p + "Side",   sideTexture, Color.white, paintSmoothness, paintMetallic);
        Material bonnet = MakeMaterial(folder, p + "Bonnet", bonnetTexture, Color.white, paintSmoothness, paintMetallic);
        Material front  = MakeMaterial(folder, p + "Front",  frontTexture, Color.white, 0.6f, 0f);
        Material rear   = MakeMaterial(folder, p + "Rear",   rearTexture, Color.white, 0.6f, 0f);
        Material window = MakeMaterial(folder, p + "Window", windowTexture, Color.white, 0.9f, 0f);
        Material glass  = MakeMaterial(folder, p + "Glass",  null, new Color(0.07f, 0.09f, 0.12f), 0.9f, 0f);
        Material black  = MakeMaterial(folder, p + "Black",  null, new Color(0.05f, 0.05f, 0.06f), 0.4f, 0f);

        // Start fresh each time
        Transform old = transform.Find("Livery");
        if (old != null) DestroyImmediate(old.gameObject);
        Transform root = new GameObject("Livery").transform;
        root.SetParent(transform, false);

        body.GetComponent<Renderer>().sharedMaterial = paint;
        cabin.GetComponent<Renderer>().sharedMaterial = roof;

        // Measure the boxes (in the car's own space)
        Vector3 bp = body.localPosition,  bs = body.localScale;
        Vector3 cp = cabin.localPosition, cs = cabin.localScale;
        float bodyTop = bp.y + bs.y / 2f, bodyFront = bp.z + bs.z / 2f, bodyBack = bp.z - bs.z / 2f;
        float cabinFront = cp.z + cs.z / 2f, cabinBack = cp.z - cs.z / 2f;

        Panel(root, "Side_L", side, new Vector3(bp.x - bs.x / 2f - Gap, bp.y, bp.z), Vector3.left,  Vector3.up, bs.z, bs.y);
        Panel(root, "Side_R", side, new Vector3(bp.x + bs.x / 2f + Gap, bp.y, bp.z), Vector3.right, Vector3.up, bs.z, bs.y);

        float bonnetLength = bodyFront - cabinFront;
        Panel(root, "Bonnet", bonnet, new Vector3(bp.x, bodyTop + Gap, cabinFront + bonnetLength / 2f),
              Vector3.up, Vector3.back, bs.x, bonnetLength);

        Panel(root, "Front", front, new Vector3(bp.x, bp.y, bodyFront + Gap), Vector3.forward, Vector3.up, bs.x, bs.y);
        Panel(root, "Rear",  rear,  new Vector3(bp.x, bp.y, bodyBack - Gap),  Vector3.back,    Vector3.up, bs.x, bs.y);

        float winH = cs.y * 0.7f, winY = cp.y + cs.y * 0.05f;
        Panel(root, "Window_L", window, new Vector3(cp.x - cs.x / 2f - Gap, winY, cp.z), Vector3.left,  Vector3.up, cs.z * 0.9f, winH);
        Panel(root, "Window_R", window, new Vector3(cp.x + cs.x / 2f + Gap, winY, cp.z), Vector3.right, Vector3.up, cs.z * 0.9f, winH);
        Panel(root, "Windscreen", glass, new Vector3(cp.x, winY, cabinFront + Gap), Vector3.forward, Vector3.up, cs.x * 0.88f, winH);
        Panel(root, "RearWindow", glass, new Vector3(cp.x, winY, cabinBack - Gap),  Vector3.back,    Vector3.up, cs.x * 0.88f, winH);

        // Slim black roof spoiler at the back
        GameObject spoiler = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(spoiler.GetComponent<Collider>());
        spoiler.name = "Spoiler";
        spoiler.transform.SetParent(root, false);
        spoiler.transform.localPosition = new Vector3(cp.x, cp.y + cs.y / 2f + 0.02f, cabinBack + 0.12f);
        spoiler.transform.localScale = new Vector3(cs.x * 0.95f, 0.04f, 0.3f);
        spoiler.GetComponent<Renderer>().sharedMaterial = black;

        // Make sure Unity saves the change
        EditorUtility.SetDirty(gameObject);
        EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Debug.Log("CarLivery: livery built on " + name);
    }

    void Panel(Transform parent, string panelName, Material mat, Vector3 localPos, Vector3 outward, Vector3 up, float width, float height)
    {
        if (mat == null) return;
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(quad.GetComponent<Collider>());
        quad.name = panelName;
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPos;
        // A quad shows its picture on the side facing away from its blue arrow
        quad.transform.localRotation = Quaternion.LookRotation(-outward, up);
        quad.transform.localScale = new Vector3(width, height, 1f);

        Renderer r = quad.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    Material MakeMaterial(string folder, string matName, Texture2D texture, Color colour, float smoothness, float metallic)
    {
        // Textured panels are skipped if you leave their texture slot empty
        bool needsTexture = matName.EndsWith("Side") || matName.EndsWith("Bonnet") || matName.EndsWith("Front")
                         || matName.EndsWith("Rear") || matName.EndsWith("Window");
        if (needsTexture && texture == null) return null;

        string path = folder + "/" + matName + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", texture);
        mat.SetColor("_BaseColor", colour);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
#endif
}
