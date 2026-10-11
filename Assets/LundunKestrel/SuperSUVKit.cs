using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

// Replaces the boxy look of a car with a low, wide, sporty super-SUV body.
// The original Body/Cabin boxes stay as invisible colliders, so the physics doesn't change.
// Click ⋮ on this component → "Build super-SUV body". Safe to run again.
public class SuperSUVKit : MonoBehaviour
{
    [Header("Paint")]
    [SerializeField] private Color paintColour = new Color(1f, 0.68f, 0.03f);
    [SerializeField, Range(0f, 1f)] private float paintSmoothness = 0.9f;
    [SerializeField, Range(0f, 1f)] private float paintMetallic = 0.35f;

    [Header("Where the generated meshes/materials are saved")]
    [SerializeField] private string saveFolder = "Assets/CarBodies";

#if UNITY_EDITOR
    private const int Paint = 0, Black = 1, Glass = 2;

    [ContextMenu("Build super-SUV body")]
    void Build()
    {
        EnsureFolder();
        string p = name.Replace(" ", "") + "_";

        Material paint  = Mat(p + "Paint",  paintColour, paintSmoothness, paintMetallic);
        Material black  = Mat(p + "Black",  new Color(0.03f, 0.03f, 0.035f), 0.75f, 0.1f);
        Material glass  = Mat(p + "Glass",  new Color(0.05f, 0.07f, 0.1f), 0.95f, 0.2f);
        Material rim    = Mat(p + "Rim",    new Color(0.12f, 0.12f, 0.13f), 0.8f, 0.9f);
        Material head   = Mat(p + "Headlight", Color.white, 0.9f, 0f, new Color(1f, 0.97f, 0.9f) * 3f);
        Material tail   = Mat(p + "Taillight", new Color(0.6f, 0f, 0f), 0.9f, 0f, new Color(1f, 0.05f, 0.03f) * 3f);
        Material plateW = Mat(p + "PlateFront", new Color(0.95f, 0.95f, 0.95f), 0.3f, 0f);
        Material plateY = Mat(p + "PlateRear",  new Color(1f, 0.82f, 0f), 0.3f, 0f);
        Material[] shell = { paint, black, glass };

        // Clear out old looks (CarLivery panels or a previous kit)
        foreach (string old in new[] { "Livery", "BodyKit" })
        {
            Transform t = transform.Find(old);
            if (t != null) DestroyImmediate(t.gameObject);
        }
        Transform kit = new GameObject("BodyKit").transform;
        kit.SetParent(transform, false);

        // Hide the boxes but keep their colliders
        foreach (string part in new[] { "Body", "Cabin" })
        {
            Transform t = transform.Find(part);
            if (t != null && t.TryGetComponent(out MeshRenderer r)) r.enabled = false;
        }

        // ---------- Lower body: side profile (z = front/back, y = height) ----------
        Vector2[] hull =
        {
            new Vector2( 2.20f, -0.12f),   // front bottom
            new Vector2( 2.26f,  0.24f),   // front bumper
            new Vector2( 2.06f,  0.48f),   // nose
            new Vector2( 0.90f,  0.66f),   // bonnet rises to the windscreen
            new Vector2(-1.55f,  0.66f),   // shoulder
            new Vector2(-2.02f,  0.62f),   // boot lid
            new Vector2(-2.22f,  0.44f),   // tail
            new Vector2(-2.16f, -0.12f),   // rear bottom
        };
        // Which material each edge uses (edge i runs from point i to i+1)
        int[] hullMats = { Black, Paint, Paint, Paint, Paint, Paint, Paint, Black };
        AddPiece(kit, p + "Hull", hull, 0.93f, 0.86f, hullMats, Paint, shell);

        // ---------- Cabin: raked windscreen, fastback roof ----------
        Vector2[] cabin =
        {
            new Vector2( 0.92f, 0.65f),    // windscreen base
            new Vector2(-0.10f, 1.18f),    // roof front
            new Vector2(-0.95f, 1.18f),    // roof back
            new Vector2(-1.90f, 0.65f),    // fastback base
        };
        int[] cabinMats = { Glass, Black, Glass, Paint };
        AddPiece(kit, p + "Cabin", cabin, 0.83f, 0.6f, cabinMats, Glass, shell);

        // ---------- Details ----------
        Box(kit, "Headlight_L", head, new Vector3(-0.58f, 0.43f, 2.13f), new Vector3(0.6f, 0.045f, 0.12f), -12f);
        Box(kit, "Headlight_R", head, new Vector3( 0.58f, 0.43f, 2.13f), new Vector3(0.6f, 0.045f, 0.12f), -12f);
        Box(kit, "Intake",      black, new Vector3(0f, 0.04f, 2.22f), new Vector3(1.55f, 0.24f, 0.08f));
        Box(kit, "Splitter",    black, new Vector3(0f, -0.11f, 2.2f), new Vector3(1.8f, 0.03f, 0.25f));
        Box(kit, "PlateFront",  plateW, new Vector3(0f, 0.2f, 2.25f), new Vector3(0.52f, 0.11f, 0.02f));

        Box(kit, "TailBar",     tail, new Vector3(0f, 0.47f, -2.17f), new Vector3(1.72f, 0.045f, 0.08f));
        Box(kit, "Diffuser",    black, new Vector3(0f, -0.02f, -2.17f), new Vector3(1.75f, 0.22f, 0.1f));
        Box(kit, "PlateRear",   plateY, new Vector3(0f, 0.25f, -2.23f), new Vector3(0.52f, 0.11f, 0.02f));
        foreach (float x in new[] { -0.62f, -0.45f, 0.45f, 0.62f })
            Cyl(kit, "Exhaust", rim, new Vector3(x, -0.05f, -2.22f), new Vector3(0.1f, 0.05f, 0.1f), new Vector3(90f, 0f, 0f));

        Box(kit, "Skirt_L", black, new Vector3(-0.92f, -0.06f, 0f), new Vector3(0.05f, 0.13f, 2.2f));
        Box(kit, "Skirt_R", black, new Vector3( 0.92f, -0.06f, 0f), new Vector3(0.05f, 0.13f, 2.2f));
        Box(kit, "Mirror_L", black, new Vector3(-0.95f, 0.77f, 0.62f), new Vector3(0.16f, 0.08f, 0.12f));
        Box(kit, "Mirror_R", black, new Vector3( 0.95f, 0.77f, 0.62f), new Vector3(0.16f, 0.08f, 0.12f));
        Box(kit, "Spoiler",  black, new Vector3(0f, 1.18f, -1.0f), new Vector3(1.1f, 0.03f, 0.22f), 6f);

        // ---------- Dark alloy rims on the existing wheels ----------
        foreach (Transform child in transform)
        {
            if (!child.name.StartsWith("Wheel")) continue;
            Transform oldRim = child.Find("Rim");
            if (oldRim != null) DestroyImmediate(oldRim.gameObject);
            GameObject r = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            DestroyImmediate(r.GetComponent<Collider>());
            r.name = "Rim";
            r.transform.SetParent(child, false);
            r.transform.localScale = new Vector3(0.72f, 1.1f, 0.72f);
            r.GetComponent<Renderer>().sharedMaterial = rim;
        }

        EditorUtility.SetDirty(gameObject);
        EditorSceneManager.MarkSceneDirty(gameObject.scene);
        AssetDatabase.SaveAssets();
        Debug.Log("SuperSUVKit: body built on " + name);
    }

    [ContextMenu("Remove body kit (back to boxes)")]
    void Remove()
    {
        Transform kit = transform.Find("BodyKit");
        if (kit != null) DestroyImmediate(kit.gameObject);
        foreach (string part in new[] { "Body", "Cabin" })
        {
            Transform t = transform.Find(part);
            if (t != null && t.TryGetComponent(out MeshRenderer r)) r.enabled = true;
        }
        EditorUtility.SetDirty(gameObject);
        EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }

    // Takes a side-view outline and extrudes it across the car's width,
    // narrower at the top than the bottom (like a real car's tumblehome).
    void AddPiece(Transform parent, string meshName, Vector2[] profile, float halfWidthBottom, float halfWidthTop,
                  int[] edgeMats, int sideMat, Material[] mats)
    {
        float minY = float.MaxValue, maxY = float.MinValue;
        Vector2 centre = Vector2.zero;
        foreach (Vector2 pt in profile) { minY = Mathf.Min(minY, pt.y); maxY = Mathf.Max(maxY, pt.y); centre += pt; }
        centre /= profile.Length;

        int n = profile.Length;
        Vector3[] left = new Vector3[n], right = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            float hw = Mathf.Lerp(halfWidthBottom, halfWidthTop, Mathf.InverseLerp(minY, maxY, profile[i].y));
            left[i]  = new Vector3(-hw, profile[i].y, profile[i].x);
            right[i] = new Vector3( hw, profile[i].y, profile[i].x);
        }
        float centreHw = Mathf.Lerp(halfWidthBottom, halfWidthTop, Mathf.InverseLerp(minY, maxY, centre.y));
        Vector3 cl = new Vector3(-centreHw, centre.y, centre.x), cr = new Vector3(centreHw, centre.y, centre.x);

        var verts = new List<Vector3>();
        var tris = new List<int>[mats.Length];
        for (int m = 0; m < mats.Length; m++) tris[m] = new List<int>();

        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            // Sides (fan from the middle)
            Tri(verts, tris[sideMat], cl, left[i], left[j], Vector3.left);
            Tri(verts, tris[sideMat], cr, right[i], right[j], Vector3.right);

            // The strip running round the outline (bonnet, roof, bumper...)
            Vector2 mid = (profile[i] + profile[j]) * 0.5f - centre;
            Vector3 outward = new Vector3(0f, mid.y, mid.x);
            Tri(verts, tris[edgeMats[i]], left[i], right[i], right[j], outward);
            Tri(verts, tris[edgeMats[i]], left[i], right[j], left[j], outward);
        }

        Mesh mesh = new Mesh { name = meshName };
        mesh.SetVertices(verts);
        mesh.subMeshCount = mats.Length;
        for (int m = 0; m < mats.Length; m++) mesh.SetTriangles(tris[m], m);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        mesh = SaveMesh(mesh);

        GameObject go = new GameObject(meshName);
        go.transform.SetParent(parent, false);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = mats;
    }

    // Adds one flat-shaded triangle, flipping it if needed so it faces outward
    void Tri(List<Vector3> verts, List<int> list, Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f) { Vector3 t = b; b = c; c = t; }
        int start = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c);
        list.Add(start); list.Add(start + 1); list.Add(start + 2);
    }

    void Box(Transform parent, string partName, Material mat, Vector3 pos, Vector3 size, float tiltX = 0f)
    {
        GameObject b = GameObject.CreatePrimitive(PrimitiveType.Cube);
        DestroyImmediate(b.GetComponent<Collider>());
        b.name = partName;
        b.transform.SetParent(parent, false);
        b.transform.localPosition = pos;
        b.transform.localRotation = Quaternion.Euler(tiltX, 0f, 0f);
        b.transform.localScale = size;
        b.GetComponent<Renderer>().sharedMaterial = mat;
    }

    void Cyl(Transform parent, string partName, Material mat, Vector3 pos, Vector3 size, Vector3 euler)
    {
        GameObject c = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        DestroyImmediate(c.GetComponent<Collider>());
        c.name = partName;
        c.transform.SetParent(parent, false);
        c.transform.localPosition = pos;
        c.transform.localRotation = Quaternion.Euler(euler);
        c.transform.localScale = size;
        c.GetComponent<Renderer>().sharedMaterial = mat;
    }

    Material Mat(string matName, Color colour, float smoothness, float metallic, Color? emission = null)
    {
        string path = saveFolder + "/" + matName + ".mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", colour);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        if (emission.HasValue)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", emission.Value);
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    Mesh SaveMesh(Mesh mesh)
    {
        string path = saveFolder + "/" + mesh.name + ".asset";
        Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing == null) { AssetDatabase.CreateAsset(mesh, path); return mesh; }
        existing.Clear();
        EditorUtility.CopySerialized(mesh, existing);
        EditorUtility.SetDirty(existing);
        return existing;
    }

    void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(saveFolder)) return;
        string parent = System.IO.Path.GetDirectoryName(saveFolder).Replace("\\", "/");
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(saveFolder));
    }
#endif
}
