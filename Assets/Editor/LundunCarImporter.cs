// Lundun → 10  Import Car Models (V2)
//   Turns real car models into Lundun cars, whatever pack they come from:
//     Assets/Vehicles/Packs/<any pack>/   one or many cars per model file (FBX), textures in a Textures folder beside it
//                                         → traffic and parked cars (Assets/Vehicles/Prefabs/Car_<name>.prefab)
//     Assets/Vehicles/Kestrel/            one car → James's own car (the scene's "Car"), keeping all its scripts
//   For every car it finds: the body and its four wheels (by name, or by shape), which way is the front (from the
//   head and tail lights), the right size and height, Lundun materials (paint jobs, glass, lights, wheels), fictional
//   number plates, then the physics (springs at the wheels, body box, bumper, health) copied from James's car.
//   White vans, lorries, the ambulance and the red buses stay as they are. Safe to run again.
//   Lundun → Vehicles → Flip Selected Car Front-to-Back : if one ever comes out backwards.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LundunCarImporter
{
    const string PackDir = "Assets/Vehicles/Packs";
    const string KestrelDir = "Assets/Vehicles/Kestrel";
    const string PrefabDir = "Assets/Vehicles/Prefabs";
    const string GenDir = "Assets/Vehicles/Generated";
    const string MatDir = GenDir + "/Materials";
    const string PlateMeshes = GenDir + "/LundunPlates.asset";
    const string PlateTexture = "Assets/Vehicles/Plates.png";
    const int PlateRows = 8;

    // kept in traffic alongside the new cars (white vans, lorries, the ambulance; the buses have their own list)
    static readonly string[] KeepInTraffic = { "Veh_van", "Veh_delivery", "Veh_delivery-flat", "Veh_truck", "Veh_garbage-truck", "Veh_ambulance" };

    class Part
    {
        public string name; public Transform src; public Mesh mesh; public Material[] mats; public Bounds b;
    }

    class Car
    {
        public string name; public Part body; public List<Part> wheels = new List<Part>(); public List<Part> extras = new List<Part>();
        public string folder;                                  // the model file's folder (Textures, recipe)
        public Dictionary<string, string> recipe = new Dictionary<string, string>();
    }

    // ------------------------------------------------------------------ menu
    [MenuItem("Lundun/10  Import Car Models (V2)", false, 10)]
    public static void ImportAll()
    {
        GameObject template; LundunPopulation pop;
        if (!LundunVehicleBuilder.Ready(out template, out pop)) return;
        try
        {
            LundunVehicleBuilder.EnsureFolder(PackDir); LundunVehicleBuilder.EnsureFolder(KestrelDir);
            LundunVehicleBuilder.EnsureFolder(PrefabDir); LundunVehicleBuilder.EnsureFolder(MatDir);
            Prepare();
            List<string> log = new List<string>();

            // 1. the traffic cars
            List<GameObject> made = new List<GameObject>();
            int index = 0;
            foreach (string path in Models(PackDir))
            {
                EditorUtility.DisplayProgressBar("Lundun", "Cars from " + Path.GetFileName(path) + "…", 0.1f);
                foreach (Car car in FindCars(path))
                {
                    EditorUtility.DisplayProgressBar("Lundun", "Making the " + car.name + "…", 0.2f + 0.05f * index);
                    GameObject prefab = BuildTrafficCar(car, template, 1 + index % (PlateRows - 1), log);
                    if (prefab != null) made.Add(prefab);
                    index++;
                }
                Tidy();                                                   // done with this file's cars
            }

            // 2. James's car
            string kestrelReport = null;
            List<string> kestrel = Models(KestrelDir);
            if (kestrel.Count > 0)
            {
                EditorUtility.DisplayProgressBar("Lundun", "James's car…", 0.85f);
                List<Car> cars = FindCars(kestrel[0]);
                if (cars.Count > 0) kestrelReport = RebuildKestrel(cars[0], template, log);
                Tidy();
            }

            // 3. traffic uses them (twice, so they're most of what you see), plus the vans and lorries
            if (made.Count > 0)
            {
                List<GameObject> list = new List<GameObject>();
                foreach (GameObject g in made) { list.Add(g); list.Add(g); }
                foreach (string keep in KeepInTraffic)
                {
                    GameObject g = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/" + keep + ".prefab");
                    if (g != null) list.Add(g);
                }
                SerializedObject so = new SerializedObject(pop);
                SerializedProperty arr = so.FindProperty("carPrefabs");
                arr.arraySize = list.Count;
                for (int i = 0; i < list.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = list[i];
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            string names = ""; foreach (GameObject g in made) names += (names.Length > 0 ? ", " : "") + g.name.Replace("Car_", "");
            Debug.Log("[Lundun] V2 cars: " + made.Count + " traffic cars (" + names + ")" + (kestrelReport != null ? "; " + kestrelReport : "; no model in " + KestrelDir + " (James's car unchanged)") +
                      ".\n" + string.Join("\n", log.ToArray()) +
                      "\nNext: Lundun → 23 Street Furniture + Parked Cars (and 23 for Brixtun / Kingsmere if you use them) so the parked cars are the new ones too, then Cmd+S and Play.");
            if (made.Count == 0 && kestrelReport == null)
                LundunVehicleBuilder.Dialog("No car models found.\n\nPut a car pack's folder (the FBX and its Textures folder) in " + PackDir + ", and James's car model in " + KestrelDir + ", then run this again.");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            LundunVehicleBuilder.Dialog("Car import failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.");
        }
        finally { Tidy(); EditorUtility.ClearProgressBar(); }
    }

    [MenuItem("Lundun/Vehicles/Flip Selected Car Front-to-Back", false, 60)]
    public static void FlipSelected()
    {
        int n = 0;
        foreach (Object o in Selection.objects)
        {
            string path = AssetDatabase.GetAssetPath(o);
            if (!path.EndsWith(".prefab")) continue;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            Transform model = root.transform.Find("Model");
            if (model != null) { model.RotateAround(root.transform.position, Vector3.up, 180f); PrefabUtility.SaveAsPrefabAsset(root, path); n++; }
            PrefabUtility.UnloadPrefabContents(root);
        }
        Debug.Log("[Lundun] Flipped " + n + " car(s). Select the car prefab(s) in Assets/Vehicles/Prefabs first.");
    }

    // ------------------------------------------------------------------ the files
    static List<string> Models(string dir)
    {
        List<string> list = new List<string>();
        if (!AssetDatabase.IsValidFolder(dir)) return list;
        foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { dir })) list.Add(AssetDatabase.GUIDToAssetPath(guid));
        list.Sort();
        return list;
    }

    /// Import settings: no animation/cameras/lights in the models; textures 1024 (wheels 512), ASTC on iPhone, the
    /// metal/smoothness maps as data (linear).
    static void Prepare()
    {
        foreach (string dir in new[] { PackDir, KestrelDir })
            foreach (string path in Models(dir))
            {
                ModelImporter mi = AssetImporter.GetAtPath(path) as ModelImporter;
                if (mi == null) continue;
                bool dirty = false;
                if (mi.importAnimation) { mi.importAnimation = false; dirty = true; }
                if (mi.importCameras) { mi.importCameras = false; dirty = true; }
                if (mi.importLights) { mi.importLights = false; dirty = true; }
                if (mi.materialImportMode == ModelImporterMaterialImportMode.None) { mi.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription; dirty = true; }
                if (dirty) mi.SaveAndReimport();
            }
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { PackDir, KestrelDir, "Assets/Vehicles" }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith(PackDir) && !path.StartsWith(KestrelDir) && path != PlateTexture) continue;
            string file = Path.GetFileNameWithoutExtension(path);
            bool data = file.EndsWith("_MS");
            int max = file.StartsWith("Wheel_") ? 512 : 1024;
            TexSettings(path, data, max);
        }
    }

    static void TexSettings(string path, bool linear, int max)
    {
        TextureImporter ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        bool dirty = false;
        if (ti.sRGBTexture == linear) { ti.sRGBTexture = !linear; dirty = true; }
        if (ti.maxTextureSize != max) { ti.maxTextureSize = max; dirty = true; }
        if (!ti.mipmapEnabled) { ti.mipmapEnabled = true; dirty = true; }
        TextureImporterPlatformSettings ios = ti.GetPlatformTextureSettings("iPhone");
        if (!ios.overridden || ios.maxTextureSize != max || ios.format != TextureImporterFormat.ASTC_6x6)
        {
            ios.overridden = true; ios.maxTextureSize = max; ios.format = TextureImporterFormat.ASTC_6x6;
            ti.SetPlatformTextureSettings(ios); dirty = true;
        }
        if (dirty) ti.SaveAndReimport();
    }

    // ------------------------------------------------------------------ finding the cars in a model file
    static List<Car> FindCars(string path)
    {
        List<Car> cars = new List<Car>();
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (asset == null) return cars;
        string folder = Path.GetDirectoryName(path).Replace('\\', '/');
        Dictionary<string, string> recipe = Recipe(path);

        GameObject inst = Object.Instantiate(asset);
        inst.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        List<Part> parts = new List<Part>();
        foreach (MeshRenderer r in inst.GetComponentsInChildren<MeshRenderer>(true))
        {
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            Bounds b = r.bounds;
            if (b.size.x > 14f || b.size.z > 14f) continue;                         // a ground plane or backdrop, not a car part
            parts.Add(new Part { name = r.name, src = r.transform, mesh = mf.sharedMesh, mats = r.sharedMaterials, b = b });
        }

        List<Part> bodies = new List<Part>(), wheels = new List<Part>(), rest = new List<Part>();
        foreach (Part p in parts)
        {
            string n = p.name.ToLowerInvariant();
            if (n.Contains("body") || n.Contains("chassis") || n.Contains("shell")) bodies.Add(p);
            else if (IsWheelName(n)) wheels.Add(p);
            else rest.Add(p);
        }
        if (bodies.Count == 0)
        {   // one car per file, parts not named: the biggest part is the body
            Part big = null;
            foreach (Part p in rest) if (big == null || Vol(p.b) > Vol(big.b)) big = p;
            if (big == null) { Object.DestroyImmediate(inst); return cars; }
            bodies.Add(big); rest.Remove(big);
        }
        if (wheels.Count < 4 * bodies.Count && bodies.Count == 1)
        {   // wheels not named: the four biggest disc-shaped parts low down
            Part body = bodies[0];
            List<Part> discs = new List<Part>();
            foreach (Part p in rest) if (IsDisc(p.b) && p.b.center.y < body.b.center.y && p.b.size.y > 0.3f) discs.Add(p);
            discs.Sort((a, c) => c.b.size.y.CompareTo(a.b.size.y));
            for (int i = 0; i < discs.Count && wheels.Count < 4; i++) { wheels.Add(discs[i]); rest.Remove(discs[i]); }
        }

        foreach (Part body in bodies)
        {
            Car car = new Car { body = body, folder = folder, recipe = recipe };
            string nm;
            car.name = recipe.TryGetValue("name", out nm) ? nm : CarWord(body.name, Path.GetFileNameWithoutExtension(path), bodies.Count);
            // its wheels: the four nearest whose centres are under the body
            List<Part> near = new List<Part>();
            foreach (Part w in wheels) if (Flat(w.b.center - body.b.center) < Mathf.Max(body.b.extents.x, body.b.extents.z) + 0.6f) near.Add(w);
            near.Sort((a, c) => Flat(a.b.center - body.b.center).CompareTo(Flat(c.b.center - body.b.center)));
            for (int i = 0; i < near.Count && car.wheels.Count < 4; i++) car.wheels.Add(near[i]);
            foreach (Part w in car.wheels) wheels.Remove(w);
            // other bits on this car (lights, mirrors, calipers…)
            Bounds grown = body.b; grown.Expand(0.4f);
            foreach (Part p in rest) if (grown.Contains(p.b.center)) car.extras.Add(p);
            cars.Add(car);
        }
        // the instance is kept until the cars are built (they copy its meshes and transforms); FindCars' caller builds
        // straight away, so tidy up through the car parts' root
        pending.Add(inst);
        return cars;
    }

    static readonly List<GameObject> pending = new List<GameObject>();
    static void Tidy() { foreach (GameObject g in pending) if (g != null) Object.DestroyImmediate(g); pending.Clear(); }

    static bool IsWheelName(string n)
    {
        return n.Contains("wheel") || n.Contains("tyre") || n.Contains("tire");
    }

    static bool IsDisc(Bounds b)
    {
        float[] s = { b.size.x, b.size.y, b.size.z };
        System.Array.Sort(s);
        return s[2] / Mathf.Max(0.001f, s[1]) < 1.25f && s[0] < 0.65f * s[1];
    }

    static float Vol(Bounds b) { return b.size.x * b.size.y * b.size.z; }
    static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    /// "minivan body" → Minivan; a single-car file → its file name.
    static string CarWord(string bodyName, string fileName, int carsInFile)
    {
        string w = carsInFile > 1 ? bodyName.Split(' ', '_', '.')[0] : fileName;
        if (w.Length == 0) w = fileName;
        if (w.ToUpperInvariant() == w) return w;                           // SUV stays SUV
        return char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant();
    }

    /// The optional recipe beside a model (same name, .txt): "material name = role" lines, plus name = …, paint = #rrggbb.
    static Dictionary<string, string> Recipe(string modelPath)
    {
        Dictionary<string, string> d = new Dictionary<string, string>();
        string txt = Path.ChangeExtension(modelPath, ".txt");
        if (!File.Exists(txt)) return d;
        foreach (string raw in File.ReadAllLines(txt))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return d;
    }

    // ------------------------------------------------------------------ material roles
    /// What a material is: paint, lights (a texture of lamps), head, tail, glass, wheel, tyre, rim, chrome, trim,
    /// caliper, or "" (keep its colour).
    static string Role(Car car, Material m)
    {
        if (m == null) return "";
        string r;
        if (car.recipe.TryGetValue(m.name, out r)) return r.ToLowerInvariant();
        string n = m.name.ToLowerInvariant();
        if (n.Contains("glass") || n.Contains("window")) return "glass";
        if (n.Contains("optic") || n.Contains("light") || n.Contains("lamp")) return "lights";
        if (n.Contains("whee") || n.Contains("tyre") || n.Contains("tire")) return "wheel";
        if (n.Contains("rim")) return "rim";
        if (n.Contains("body") || n.Contains("paint")) return "paint";
        return "";
    }

    // ------------------------------------------------------------------ which way it faces
    /// The car's centre (between the wheels, on the ground plane) and its forward direction.
    static void Frame(Car car, Texture2D lights, out Vector3 centre, out Vector3 forward)
    {
        if (car.wheels.Count >= 2)
        {
            Vector3 c = Vector3.zero;
            foreach (Part w in car.wheels) c += w.b.center;
            c /= car.wheels.Count;
            float sxx = 0f, szz = 0f, sxz = 0f;
            foreach (Part w in car.wheels) { Vector3 d = w.b.center - c; sxx += d.x * d.x; szz += d.z * d.z; sxz += d.x * d.z; }
            float angle = 0.5f * Mathf.Atan2(2f * sxz, sxx - szz);               // the long way through the wheels
            forward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            centre = c;
        }
        else
        {
            Bounds b = car.body.b;
            centre = b.center;
            forward = b.size.x > b.size.z ? Vector3.right : Vector3.forward;
        }
        centre.y = 0f;
        if (FrontSign(car, centre, forward, lights) < 0f) forward = -forward;
    }

    /// +1 if the headlights are ahead along 'axis', −1 if behind (from the lamp materials, or the lamps' texture).
    static float FrontSign(Car car, Vector3 centre, Vector3 axis, Texture2D lights)
    {
        float head = 0f, tail = 0f; int nh = 0, nt = 0;
        List<Part> all = new List<Part> { car.body }; all.AddRange(car.extras);
        foreach (Part p in all)
        {
            Mesh m = p.mesh;
            Vector3[] v = m.vertices; Vector2[] uv = m.uv;
            for (int sub = 0; sub < m.subMeshCount; sub++)
            {
                if (p.mats.Length == 0) continue;
                string role = Role(car, p.mats[Mathf.Min(sub, p.mats.Length - 1)]);
                if (role != "lights" && role != "head" && role != "tail") continue;
                int[] tri = m.GetTriangles(sub);
                for (int i = 0; i + 2 < tri.Length; i += 3)
                {
                    Vector3 local = (v[tri[i]] + v[tri[i + 1]] + v[tri[i + 2]]) / 3f;
                    float d = Vector3.Dot(p.src.TransformPoint(local) - centre, axis);
                    string kind = role;
                    if (role == "lights")
                    {
                        if (lights == null || uv == null || uv.Length != v.Length) continue;
                        Vector2 t = (uv[tri[i]] + uv[tri[i + 1]] + uv[tri[i + 2]]) / 3f;
                        Color c = lights.GetPixelBilinear(t.x - Mathf.Floor(t.x), t.y - Mathf.Floor(t.y));
                        if (c.r > c.g * 2.2f && c.r > c.b * 2.2f && c.r > 0.35f) kind = "tail";
                        else if (c.r + c.g + c.b > 1.41f && Mathf.Abs(c.r - c.b) < 0.24f) kind = "head";
                        else continue;
                    }
                    if (kind == "head") { head += d; nh++; } else { tail += d; nt++; }
                }
            }
        }
        if (nh > 0 && nt > 0) return head / nh >= tail / nt ? 1f : -1f;
        if (nh > 0) return head >= 0f ? 1f : -1f;
        if (nt > 0) return tail <= 0f ? 1f : -1f;
        Debug.LogWarning("[Lundun] " + car.name + ": no head or tail lights found to tell the front; if it drives backwards, select its prefab and use Lundun → Vehicles → Flip Selected Car Front-to-Back.");
        return 1f;
    }

    // ------------------------------------------------------------------ building the look
    /// A "Model" object (at the origin) holding copies of the car's parts, turned to face +Z, centred between the
    /// wheels, wheels touching y = 0, scaled to a sensible size. Wheels named Wheel_FL/FR/RL/RR.
    static GameObject BuildModel(Car car, Materials mats, out List<Vector3> wheelCentres, out float radius, out Bounds bodyBounds)
    {
        Texture2D lights = mats.LightsForFront;
        Vector3 centre, forward;
        Frame(car, lights, out centre, out forward);
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        Quaternion turn = Quaternion.Euler(0f, -yaw, 0f);

        GameObject model = new GameObject("Model");
        List<Transform> wheelCopies = new List<Transform>();
        Transform shell = Copy(car.body, model.transform, "Shell", car, mats);
        foreach (Part w in car.wheels) wheelCopies.Add(Copy(w, model.transform, "Wheel", car, mats));
        int e = 0;
        foreach (Part x in car.extras) Copy(x, model.transform, NiceName(x.name, ref e), car, mats);

        // turn and centre: rotate the whole model about the car's centre
        foreach (Transform t in Children(model.transform))
        {
            t.position = turn * (t.position - centre);
            t.rotation = turn * t.rotation;
        }
        // size: real cars are 3–7 m long; anything else came in at the wrong scale
        Bounds b = Bounds(model);
        if (b.size.z < 2.5f || b.size.z > 8f)
        {
            float s = 4.5f / Mathf.Max(0.01f, b.size.z);
            model.transform.localScale = Vector3.one * s;
            Debug.LogWarning("[Lundun] " + car.name + " came in " + b.size.z.ToString("0.0") + " m long; scaled to 4.5 m.");
            b = Bounds(model);
        }
        // the wheels touch the ground at y = 0 (or the body's bottom, if it has no wheels)
        float bottom = b.min.y;
        if (wheelCopies.Count > 0) { bottom = float.MaxValue; foreach (Transform w in wheelCopies) bottom = Mathf.Min(bottom, RBounds(w).min.y); }
        model.transform.position -= new Vector3(0f, bottom, 0f);

        // name the wheels by corner
        wheelCentres = new List<Vector3>();
        radius = 0f;
        foreach (Transform w in wheelCopies)
        {
            Bounds wb = RBounds(w);
            w.name = "Wheel_" + (wb.center.z > 0f ? "F" : "R") + (wb.center.x < 0f ? "L" : "R");
            wheelCentres.Add(wb.center);
            radius += wb.extents.y;
        }
        if (wheelCopies.Count > 0) radius /= wheelCopies.Count;
        bodyBounds = Bounds(model);
        if (wheelCopies.Count < 4)
        {   // no wheels found: springs at the usual spots
            radius = 0.33f * bodyBounds.size.z / 4.5f;
            float wx = bodyBounds.size.x * 0.38f, wz = bodyBounds.size.z * 0.32f;
            wheelCentres.Clear();
            wheelCentres.Add(new Vector3(-wx, radius, wz)); wheelCentres.Add(new Vector3(wx, radius, wz));
            wheelCentres.Add(new Vector3(-wx, radius, -wz)); wheelCentres.Add(new Vector3(wx, radius, -wz));
        }
        radius = Mathf.Clamp(radius, 0.25f, 0.6f);
        mats.ShellRenderer = shell.GetComponent<MeshRenderer>();
        return model;
    }

    static string NiceName(string n, ref int i)
    {
        i++;
        return "Part_" + i;
    }

    static IEnumerable<Transform> Children(Transform t) { List<Transform> l = new List<Transform>(); foreach (Transform c in t) l.Add(c); return l; }

    static Transform Copy(Part p, Transform parent, string name, Car car, Materials mats)
    {
        GameObject g = new GameObject(name);
        g.transform.SetParent(parent, false);
        g.transform.SetPositionAndRotation(p.src.position, p.src.rotation);
        g.transform.localScale = p.src.lossyScale;
        g.AddComponent<MeshFilter>().sharedMesh = p.mesh;
        MeshRenderer mr = g.AddComponent<MeshRenderer>();
        Material[] m = new Material[Mathf.Max(1, p.mesh.subMeshCount)];
        for (int i = 0; i < m.Length; i++)
        {
            Material src = p.mats.Length == 0 ? null : p.mats[Mathf.Min(i, p.mats.Length - 1)];
            m[i] = mats.For(car, src, Role(car, src), p.name, i, p == car.body);
        }
        mr.sharedMaterials = m;
        return g.transform;
    }

    static Bounds Bounds(GameObject g)
    {
        Renderer[] rs = g.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(g.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static Bounds RBounds(Transform t) { Renderer r = t.GetComponent<Renderer>(); return r != null ? r.bounds : new Bounds(t.position, Vector3.zero); }

    // ------------------------------------------------------------------ materials
    class Materials
    {
        public string folder, car;
        public Texture2D LightsForFront;                       // a readable copy of the lamps texture, for telling the front
        public MeshRenderer ShellRenderer;
        public int paintSlot = -1;
        public List<Material> paints = new List<Material>();
        public Color paintColour = new Color(0.6f, 0.61f, 0.63f);

        public Materials(string folder, string car)
        {
            this.folder = folder; this.car = car;
            string lights = TexPath("Lights");
            if (lights != null)
            {
                LightsForFront = new Texture2D(2, 2);
                LightsForFront.LoadImage(File.ReadAllBytes(lights));
            }
        }

        string TexDir => folder + "/Textures";

        public string TexPath(string file)
        {
            foreach (string ext in new[] { ".png", ".jpg", ".tga" })
            {
                string p = TexDir + "/" + file + ext;
                if (File.Exists(p)) return p;
            }
            return null;
        }

        Texture2D Tex(string file) { string p = TexPath(file); return p != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(p) : null; }

        public Material For(Car c, Material src, string role, string partName, int slot, bool isBody)
        {
            switch (role)
            {
                case "paint":
                {
                    Texture2D ms = Tex("Car_" + car + "_MS");
                    List<string> jobs = PaintJobs();
                    if (jobs.Count > 0)
                    {
                        if (paints.Count == 0)
                            foreach (string job in jobs)
                                paints.Add(Lit("LC_" + car + "_" + job, Tex("Car_" + car + "_" + job), Color.white, ms, 0f, 1f, null, Color.black));
                        if (isBody) paintSlot = slot;
                        return paints[0];
                    }
                    Material pm = Lit("LC_" + car + "_Paint", null, paintColour, null, 0.45f, 0.82f, null, Color.black);
                    return pm;
                }
                case "lights":
                {
                    Texture2D t = Tex("Lights");
                    return Lit("LC_" + Pack() + "_Lights", t, Color.white, null, 0f, 0.85f, t, Color.white * 0.25f);
                }
                case "glass": return Lit("LC_Glass", null, new Color(0.02f, 0.025f, 0.03f), null, 0.2f, 0.95f, null, Color.black);
                case "wheel":
                {
                    string letter = WheelLetter(partName);
                    Texture2D d = letter != null ? Tex("Wheel_" + letter + "_D") : null;
                    if (d != null) return Lit("LC_" + Pack() + "_Wheel_" + letter, d, Color.white, Tex("Wheel_" + letter + "_MS"), 0f, 1f, null, Color.black);
                    return Lit("LC_Tyre", null, new Color(0.04f, 0.04f, 0.04f), null, 0f, 0.25f, null, Color.black);
                }
                case "tyre": return Lit("LC_Tyre", null, new Color(0.04f, 0.04f, 0.04f), null, 0f, 0.25f, null, Color.black);
                case "rim":
                case "chrome": return Lit("LC_Chrome", null, new Color(0.55f, 0.56f, 0.58f), null, 0.9f, 0.75f, null, Color.black);
                case "trim": return Lit("LC_Trim", null, new Color(0.03f, 0.03f, 0.035f), null, 0.1f, 0.5f, null, Color.black);
                case "head": return Lit("LC_Headlight", null, Color.white, null, 0f, 0.9f, null, new Color(1f, 0.97f, 0.9f) * 1.6f);
                case "tail": return Lit("LC_Taillight", null, new Color(0.5f, 0f, 0f), null, 0f, 0.9f, null, new Color(1f, 0.05f, 0.03f) * 1.5f);
                case "caliper": return Lit("LC_Caliper", null, new Color(0.6f, 0.02f, 0.02f), null, 0.3f, 0.6f, null, Color.black);
            }
            // anything else keeps its own colour
            Color col = src != null && src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor") : src != null && src.HasProperty("_Color") ? src.color : Color.grey;
            string key = "LC_Colour_" + ColorUtility.ToHtmlStringRGB(col);
            return Lit(key, null, col, null, 0f, 0.5f, null, Color.black);
        }

        string Pack() { return Path.GetFileName(folder); }

        /// The paint jobs made for this car: Car_<car>_<Colour> textures (not the _MS map).
        List<string> PaintJobs()
        {
            List<string> jobs = new List<string>();
            if (!Directory.Exists(TexDir)) return jobs;
            string prefix = "car_" + car.ToLowerInvariant() + "_";
            foreach (string f in Directory.GetFiles(TexDir))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".meta" || !n.ToLowerInvariant().StartsWith(prefix)) continue;
                string job = n.Substring(prefix.Length);
                if (job.ToUpperInvariant() == "MS" || jobs.Contains(job)) continue;
                jobs.Add(job);
            }
            jobs.Sort((a, b) => a == "Original" ? -1 : b == "Original" ? 1 : string.CompareOrdinal(a, b));
            return jobs;
        }

        static string WheelLetter(string partName)
        {
            string n = partName.ToLowerInvariant();
            int i = n.IndexOf("wheel_");
            if (i < 0 || i + 6 >= n.Length) return null;
            return n.Substring(i + 6, 1).ToUpperInvariant();
        }
    }

    /// A URP Lit material, made once (made again with the same name = updated in place).
    static Material Lit(string name, Texture2D baseMap, Color baseColour, Texture2D ms, float metallic, float smooth, Texture2D emissionMap, Color emission)
    {
        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool isNew = m == null;
        if (isNew)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            m = new Material(sh != null ? sh : Shader.Find("Standard"));
        }
        m.SetTexture("_BaseMap", baseMap); m.SetTexture("_MainTex", baseMap);
        m.SetColor("_BaseColor", baseColour); m.SetColor("_Color", baseColour);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smooth);
        if (ms != null) { m.SetTexture("_MetallicGlossMap", ms); m.EnableKeyword("_METALLICSPECGLOSSMAP"); m.SetFloat("_SmoothnessTextureChannel", 0f); }
        else { m.SetTexture("_MetallicGlossMap", null); m.DisableKeyword("_METALLICSPECGLOSSMAP"); }
        if (emission.maxColorComponent > 0f)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission);
            m.SetTexture("_EmissionMap", emissionMap);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        else { m.DisableKeyword("_EMISSION"); m.SetColor("_EmissionColor", Color.black); }
        m.enableInstancing = true;
        if (isNew) AssetDatabase.CreateAsset(m, path); else EditorUtility.SetDirty(m);
        return m;
    }

    // ------------------------------------------------------------------ plates
    /// Fictional number plates (white front, yellow rear) from Plates.png: one row per car.
    static void AddPlates(GameObject model, Transform shell, int row)
    {
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PlateTexture);
        if (tex == null || shell == null) return;
        Material mat = Lit("LC_Plates", tex, Color.white, null, 0f, 0.35f, null, Color.black);
        MeshFilter mf = shell.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null) return;
        // (the model is still at the origin, facing +Z, wheels on y = 0)
        Vector3[] v = mf.sharedMesh.vertices;
        Matrix4x4 toWorld = shell.localToWorldMatrix;
        Bounds b = Bounds(model);
        float h = b.size.y, ground = b.min.y;
        Vector3 front = new Vector3(0f, 0f, float.MinValue), rear = new Vector3(0f, 0f, float.MaxValue);
        foreach (Vector3 lv in v)
        {
            Vector3 p = toWorld.MultiplyPoint3x4(lv);
            float y = p.y - ground;
            if (Mathf.Abs(p.x) > 0.3f || y < h * 0.15f || y > h * 0.55f) continue;
            if (p.z > front.z) front = p;
            if (p.z < rear.z) rear = p;
        }
        if (front.z == float.MinValue) return;
        for (int side = 0; side < 2; side++)
        {
            Vector3 at = side == 0 ? front : rear;
            GameObject g = new GameObject(side == 0 ? "Plate_Front" : "Plate_Rear");
            g.transform.SetParent(model.transform, false);
            g.transform.position = new Vector3(0f, ground + Mathf.Clamp(at.y - ground, h * 0.2f, h * 0.5f), at.z + (side == 0 ? 0.012f : -0.012f));
            g.transform.rotation = side == 0 ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);
            g.transform.localScale = new Vector3(1f / model.transform.localScale.x, 1f / model.transform.localScale.y, 1f / model.transform.localScale.z);
            g.AddComponent<MeshFilter>().sharedMesh = PlateMesh(row, side);
            MeshRenderer r = g.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
    }

    /// The quad for one plate (row 0-7, side 0 = front/white, 1 = rear/yellow), kept in one mesh asset.
    static Mesh PlateMesh(int row, int side)
    {
        string name = "Plate_" + row + "_" + (side == 0 ? "F" : "R");
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(PlateMeshes) ?? new Object[0])
            if (o is Mesh && o.name == name) return (Mesh)o;
        const float W = 0.52f, H = 0.115f;
        float u0 = side * 0.5f, u1 = u0 + 0.5f, v1 = 1f - row / (float)PlateRows, v0 = v1 - 1f / PlateRows;
        Mesh m = new Mesh { name = name };
        // facing +Z (out of the car's nose); the rear one is turned round
        m.vertices = new[] { new Vector3(W / 2f, -H / 2f, 0f), new Vector3(-W / 2f, -H / 2f, 0f), new Vector3(-W / 2f, H / 2f, 0f), new Vector3(W / 2f, H / 2f, 0f) };
        m.uv = new[] { new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1) };
        m.triangles = new[] { 0, 2, 1, 0, 3, 2 };
        m.RecalculateNormals(); m.RecalculateBounds();
        if (AssetDatabase.LoadMainAssetAtPath(PlateMeshes) == null) AssetDatabase.CreateAsset(m, PlateMeshes);
        else AssetDatabase.AddObjectToAsset(m, PlateMeshes);
        return m;
    }

    // ------------------------------------------------------------------ a traffic car
    static GameObject BuildTrafficCar(Car car, GameObject template, int plateRow, List<string> log)
    {
        Materials mats = new Materials(car.folder, car.name);
        List<Vector3> wheels; float radius; Bounds b;
        GameObject root = new GameObject("Car_" + car.name);
        root.layer = template.layer;
        GameObject model = BuildModel(car, mats, out wheels, out radius, out b);
        model.transform.SetParent(root.transform, false);
        AddPlates(model, mats.ShellRenderer != null ? mats.ShellRenderer.transform : null, plateRow);
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = template.layer;
        b = Bounds(root);

        float massScale = Mathf.Clamp(b.size.x * b.size.z / (1.8f * 4.4f), 0.8f, 1.7f);
        LundunVehicleBuilder.AddPhysics(root, template, wheels, radius, b, massScale);
        root.AddComponent<LundunTrafficCar>();
        if (mats.paints.Count > 0 && mats.ShellRenderer != null && mats.paintSlot >= 0)
            root.AddComponent<LundunCarPaint>().Setup(mats.ShellRenderer, mats.paintSlot, mats.paints.ToArray());

        // far away: not drawn at all (a car this small on screen is a few pixels)
        LODGroup lod = root.AddComponent<LODGroup>();
        lod.SetLODs(new[] { new LOD(0.012f, model.GetComponentsInChildren<Renderer>(true)) });
        lod.RecalculateBounds();

        log.Add("  " + car.name + ": " + b.size.z.ToString("0.0") + " m long, wheels " + (radius * 2f).ToString("0.00") + " m, " + Mathf.Max(1, mats.paints.Count) + " paint job(s)");
        return LundunVehicleBuilder.Save(root, PrefabDir + "/Car_" + car.name + ".prefab");
    }

    // ------------------------------------------------------------------ James's car
    /// The scene's "Car" gets the new body; its scripts, springs, bumper, smoke and everything pointing at it stay.
    static string RebuildKestrel(Car car, GameObject jamesCar, List<string> log)
    {
        CarController cc = jamesCar.GetComponent<CarController>();
        Rigidbody rb = jamesCar.GetComponent<Rigidbody>();
        Materials mats = new Materials(car.folder, car.name);

        // the paint: what the old body kit used, unless the recipe says
        string hex; Color paint;
        if (car.recipe.TryGetValue("paint", out hex) && ColorUtility.TryParseHtmlString(hex, out paint)) mats.paintColour = paint;
        else mats.paintColour = new Color(1f, 0.68f, 0.03f);
        SuperSUVKit kit = jamesCar.GetComponent<SuperSUVKit>();
        if (kit != null)
        {
            if (!car.recipe.ContainsKey("paint")) mats.paintColour = new SerializedObject(kit).FindProperty("paintColour").colorValue;
            Object.DestroyImmediate(kit);
        }

        List<Vector3> wheels; float radius; Bounds b;
        GameObject model = BuildModel(car, mats, out wheels, out radius, out b);
        AddPlates(model, mats.ShellRenderer != null ? mats.ShellRenderer.transform : null, 0);
        float height = b.size.y, width = b.size.x, length = b.size.z, frontZ = b.max.z, midZ = b.center.z;

        // out with the old look (the boxes stay as the body's colliders, unseen)
        foreach (Transform t in Children(jamesCar.transform))
        {
            string n = t.name;
            if (n == "BodyKit" || n == "Livery" || n == "Model" || n.StartsWith("Wheel")) Object.DestroyImmediate(t.gameObject);
        }
        foreach (string part in new[] { "Body", "Cabin" })
        {
            Transform t = jamesCar.transform.Find(part);
            if (t != null && t.GetComponent<MeshRenderer>() != null) t.GetComponent<MeshRenderer>().enabled = false;
        }

        // springs stay level with the car's root (as before); the road sits where they settle
        SerializedObject so = new SerializedObject(cc);
        float rest = so.FindProperty("restLength").floatValue, k = so.FindProperty("springStrength").floatValue;
        float sag = rb.mass * 9.81f / (4f * Mathf.Max(1f, k));
        float ground = -(rest + radius - sag);
        LundunVehicleBuilder.SetF(so, "wheelRadius", radius);
        SerializedProperty pts = so.FindProperty("springPoints");
        string[] corners = { "FL", "FR", "RL", "RR" };
        pts.arraySize = 4;
        for (int i = 0; i < 4; i++)
        {
            Vector3 w = wheels[0];
            foreach (Vector3 c in wheels) if ((c.z > 0f) == (corners[i][0] == 'F') && (c.x < 0f) == (corners[i][1] == 'L')) w = c;
            Transform sp = jamesCar.transform.Find("Spring_" + corners[i]);
            if (sp == null) { sp = new GameObject("Spring_" + corners[i]).transform; sp.SetParent(jamesCar.transform, false); }
            sp.localPosition = new Vector3(w.x, 0f, w.z);
            sp.localRotation = Quaternion.identity;
            pts.GetArrayElementAtIndex(i).objectReferenceValue = sp;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        Vector3 lift = model.transform.position;                       // where BuildModel put it so the wheels touch y = 0
        model.transform.SetParent(jamesCar.transform, false);
        model.transform.localPosition = lift + new Vector3(0f, ground, 0f);
        model.transform.localRotation = Quaternion.identity;
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = jamesCar.layer;

        // the body's collision boxes fitted to the new shape
        float low = ground + radius * 1.1f, waist = ground + height * 0.6f, top = ground + height * 0.97f;
        Fit(jamesCar.transform.Find("Body"), new Vector3(0f, (low + waist) / 2f, midZ), new Vector3(width * 0.95f, waist - low, length * 0.97f));
        Fit(jamesCar.transform.Find("Cabin"), new Vector3(0f, (waist + top) / 2f, midZ - length * 0.05f), new Vector3(width * 0.8f, top - waist, length * 0.5f));
        Transform bumper = jamesCar.transform.Find("Bumper");
        if (bumper != null)
        {
            bumper.localPosition = new Vector3(0f, ground + height * 0.42f, frontZ - 0.35f);
            BoxCollider bc = bumper.GetComponent<BoxCollider>();
            if (bc != null) { bc.center = Vector3.zero; bc.size = new Vector3(width, height * 0.6f, 1.2f); }
        }
        Transform smoke = jamesCar.transform.Find("Smoke");
        if (smoke != null) smoke.localPosition = new Vector3(0f, ground + height * 0.72f, frontZ - 1.0f);

        EditorUtility.SetDirty(jamesCar);
        log.Add("  James's car (" + car.name + "): " + length.ToString("0.0") + " m long, wheels " + (radius * 2f).ToString("0.00") + " m");
        return "James's car is now the " + car.name;
    }

    static void Fit(Transform t, Vector3 centre, Vector3 size)
    {
        if (t == null) return;
        t.localPosition = centre;
        t.localRotation = Quaternion.identity;
        t.localScale = size;                                    // its BoxCollider is a unit cube
        BoxCollider bc = t.GetComponent<BoxCollider>();
        if (bc != null) { bc.center = Vector3.zero; bc.size = Vector3.one; }
    }

    // ------------------------------------------------------------------ parked cars (Lundun → 23 uses this)
    static readonly Dictionary<string, string[]> ParkedAs = new Dictionary<string, string[]>
    {
        { "hatchback-sports", new[] { "Compact", "Hatchback" } },
        { "sedan", new[] { "Sedan", "Wagon", "Coupe" } },
        { "suv", new[] { "SUV", "Offroad", "Minivan" } },
        { "suv-luxury", new[] { "SUV", "Pickup", "Minivan" } },
        { "sedan-sports", new[] { "Sport", "Coupe", "Sedan" } },
    };

    /// The new car to park in a bay meant for this old model (vans stay vans), the same one every time for this spot;
    /// null if the new cars haven't been made.
    public static GameObject ForParkedSlot(string oldModel, double x, double z)
    {
        string[] options;
        if (!ParkedAs.TryGetValue(oldModel, out options)) return null;
        int h = System.Math.Abs(((int)x * 73856093) ^ ((int)z * 19349663)) & 0x7fffffff;
        for (int k = 0; k < options.Length; k++)
        {
            GameObject g = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabDir + "/Car_" + options[(h + k) % options.Length] + ".prefab");
            if (g != null) return g;
        }
        return null;
    }
}
