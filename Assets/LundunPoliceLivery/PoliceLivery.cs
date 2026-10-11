using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Dresses the blocky police car in UK Battenburg livery.
// Open the PoliceCar prefab, click ⋮ on this component, choose "Build UK livery".
public class PoliceLivery : MonoBehaviour
{
    [Header("Car parts (found by name if left empty)")]
    [SerializeField] private Transform body;
    [SerializeField] private Transform cabin;

    [Header("Livery textures")]
    [SerializeField] private Texture2D sideTexture;      // police_side
    [SerializeField] private Texture2D bonnetTexture;    // police_bonnet
    [SerializeField] private Texture2D frontTexture;     // police_front
    [SerializeField] private Texture2D rearTexture;      // police_rear
    [SerializeField] private Texture2D windowTexture;    // police_window

    private const float Gap = 0.006f;   // tiny lift off the surface so panels don't flicker

#if UNITY_EDITOR
    [ContextMenu("Build UK livery")]
    void Build()
    {
        if (body == null) body = transform.Find("Body");
        if (cabin == null) cabin = transform.Find("Cabin");
        if (body == null || cabin == null || sideTexture == null)
        {
            Debug.LogError("PoliceLivery: needs Body, Cabin and at least the side texture.");
            return;
        }

        // Materials are saved next to the textures so the prefab keeps them
        string folder = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(sideTexture));
        Material white  = MakeMaterial(folder, "Livery_White",  null, new Color(0.95f, 0.95f, 0.95f), 0.55f);
        Material side   = MakeMaterial(folder, "Livery_Side",   sideTexture, Color.white, 0.5f);
        Material bonnet = MakeMaterial(folder, "Livery_Bonnet", bonnetTexture, Color.white, 0.5f);
        Material front  = MakeMaterial(folder, "Livery_Front",  frontTexture, Color.white, 0.5f);
        Material rear   = MakeMaterial(folder, "Livery_Rear",   rearTexture, Color.white, 0.5f);
        Material window = MakeMaterial(folder, "Livery_Window", windowTexture, Color.white, 0.9f);
        Material glass  = MakeMaterial(folder, "Livery_Glass",  null, new Color(0.07f, 0.09f, 0.12f), 0.9f);
        Material black  = MakeMaterial(folder, "Livery_Black",  null, new Color(0.05f, 0.05f, 0.06f), 0.3f);

        // Start fresh each time
        Transform old = transform.Find("Livery");
        if (old != null) DestroyImmediate(old.gameObject);
        Transform root = new GameObject("Livery").transform;
        root.SetParent(transform, false);

        // Body and cabin become plain white; the panels go on top
        body.GetComponent<Renderer>().sharedMaterial = white;
        cabin.GetComponent<Renderer>().sharedMaterial = white;

        // Measure the boxes (in the car's own space)
        Vector3 bp = body.localPosition,  bs = body.localScale;
        Vector3 cp = cabin.localPosition, cs = cabin.localScale;
        float bodyTop = bp.y + bs.y / 2f, bodyFront = bp.z + bs.z / 2f, bodyBack = bp.z - bs.z / 2f;
        float cabinFront = cp.z + cs.z / 2f, cabinBack = cp.z - cs.z / 2f;

        // Battenburg down both sides
        Panel(root, "Side_L", side, new Vector3(bp.x - bs.x / 2f - Gap, bp.y, bp.z), Vector3.left,  Vector3.up, bs.z, bs.y);
        Panel(root, "Side_R", side, new Vector3(bp.x + bs.x / 2f + Gap, bp.y, bp.z), Vector3.right, Vector3.up, bs.z, bs.y);

        // Hi-vis bonnet (text reads the right way up from in front of the car)
        float bonnetLength = bodyFront - cabinFront;
        Panel(root, "Bonnet", bonnet != null ? bonnet : side,
              new Vector3(bp.x, bodyTop + Gap, cabinFront + bonnetLength / 2f), Vector3.up, Vector3.back, bs.x, bonnetLength);

        // Nose and tail
        Panel(root, "Front", front, new Vector3(bp.x, bp.y, bodyFront + Gap), Vector3.forward, Vector3.up, bs.x, bs.y);
        Panel(root, "Rear",  rear,  new Vector3(bp.x, bp.y, bodyBack - Gap),  Vector3.back,    Vector3.up, bs.x, bs.y);

        // Tinted windows, leaving a white frame round each one
        float winH = cs.y * 0.7f, winY = cp.y + cs.y * 0.05f;
        Panel(root, "Window_L", window, new Vector3(cp.x - cs.x / 2f - Gap, winY, cp.z), Vector3.left,  Vector3.up, cs.z * 0.9f, winH);
        Panel(root, "Window_R", window, new Vector3(cp.x + cs.x / 2f + Gap, winY, cp.z), Vector3.right, Vector3.up, cs.z * 0.9f, winH);
        Panel(root, "Windscreen", glass, new Vector3(cp.x, winY, cabinFront + Gap), Vector3.forward, Vector3.up, cs.x * 0.88f, winH);
        Panel(root, "RearWindow", glass, new Vector3(cp.x, winY, cabinBack - Gap),  Vector3.back,    Vector3.up, cs.x * 0.88f, winH);

        // Black light-bar base under the blue lights
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(bar.GetComponent<Collider>());
        bar.name = "LightBarBase";
        bar.transform.SetParent(root, false);
        bar.transform.localPosition = new Vector3(cp.x, cp.y + cs.y / 2f + 0.03f, cp.z);
        bar.transform.localScale = new Vector3(cs.x * 0.75f, 0.06f, 0.3f);
        bar.GetComponent<Renderer>().sharedMaterial = black;

        // Make sure Unity saves the prefab
        EditorUtility.SetDirty(gameObject);
        EditorSceneManager.MarkSceneDirty(gameObject.scene);
        Debug.Log("PoliceLivery: UK livery built.");
    }

    void Panel(Transform parent, string name, Material mat, Vector3 localPos, Vector3 outward, Vector3 up, float width, float height)
    {
        if (mat == null) return;
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        DestroyImmediate(quad.GetComponent<Collider>());
        quad.name = name;
        quad.transform.SetParent(parent, false);
        quad.transform.localPosition = localPos;
        // A quad shows its picture on the side facing away from its blue arrow
        quad.transform.localRotation = Quaternion.LookRotation(-outward, up);
        quad.transform.localScale = new Vector3(width, height, 1f);

        Renderer r = quad.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    Material MakeMaterial(string folder, string name, Texture2D texture, Color colour, float smoothness)
    {
        if (texture == null && name != "Livery_White" && name != "Livery_Glass" && name != "Livery_Black")
            return null;

        string path = folder + "/" + name + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", texture);
        mat.SetColor("_BaseColor", colour);
        mat.SetFloat("_Smoothness", smoothness);
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
#endif
}
