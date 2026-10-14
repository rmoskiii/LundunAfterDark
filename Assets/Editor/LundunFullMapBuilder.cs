// Lundun → 6  Build Full Map + GPS: full-screen map, waypoints, route line on both maps, waypoint beam, "Arrived" message.
using LundunGreybox;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class LundunFullMapBuilder
{
    const string JsonPath = "Assets/World/LundunWorld.json";
    const string GenDir = "Assets/World/Generated";
    const string MatDir = "Assets/World/Materials";
    const float PanelWidth = 440f;
    static readonly Color Purple = new Color(0.72f, 0.36f, 1f, 1f);

    [MenuItem("Lundun/6  Build Full Map + GPS", false, 6)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        Transform ct = Root("Canvas");
        Canvas canvas = ct != null ? ct.GetComponent<Canvas>() : null;
        if (canvas == null) { Dialog("Can't find the HUD Canvas."); return; }
        Transform mmT = FindDeep(ct, "Minimap");
        LundunMinimap minimap = mmT != null ? mmT.GetComponent<LundunMinimap>() : null;
        if (minimap == null) { Dialog("Build the minimap first: Lundun → 5  Build Minimap."); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
        Texture2D mapTex = AssetDatabase.LoadAssetAtPath<Texture2D>(GenDir + "/LundunMap.png");
        Sprite dot = AssetDatabase.LoadAssetAtPath<Sprite>(GenDir + "/MinimapCircle.png");
        Sprite arrow = AssetDatabase.LoadAssetAtPath<Sprite>(GenDir + "/MinimapArrow.png");
        if (json == null || mapTex == null || dot == null || arrow == null) { Dialog("Map files missing. Run Lundun → 5  Build Minimap again."); return; }

        // map area, read from the minimap so both always match
        SerializedObject mso = new SerializedObject(minimap);
        float minX = mso.FindProperty("mapMinX").floatValue, minZ = mso.FindProperty("mapMinZ").floatValue;
        float w = mso.FindProperty("mapWidth").floatValue, h = mso.FindProperty("mapHeight").floatValue;
        Transform car = Root("Car");

        // ---- GPS brain + waypoint beam + "Arrived" message
        Transform old = Root("LundunNavigation");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        GameObject navGo = new GameObject("LundunNavigation");
        LundunNavigation nav = navGo.AddComponent<LundunNavigation>();
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        beam.name = "WaypointBeam";
        Object.DestroyImmediate(beam.GetComponent<Collider>());
        beam.transform.SetParent(navGo.transform, false);
        beam.transform.localScale = new Vector3(3f, 60f, 3f);
        MeshRenderer br = beam.GetComponent<MeshRenderer>();
        br.sharedMaterial = BeamMaterial();
        br.shadowCastingMode = ShadowCastingMode.Off;
        beam.SetActive(false);

        Transform oldToast = ct.Find("NavToast");
        if (oldToast != null) Object.DestroyImmediate(oldToast.gameObject);
        RectTransform toastRt = NewRect("NavToast", ct);
        toastRt.anchorMin = toastRt.anchorMax = new Vector2(0.5f, 1f); toastRt.pivot = new Vector2(0.5f, 1f);
        toastRt.anchoredPosition = new Vector2(0, -110); toastRt.sizeDelta = new Vector2(1000, 70);
        TextMeshProUGUI toast = toastRt.gameObject.AddComponent<TextMeshProUGUI>();
        toast.fontSize = 44; toast.fontStyle = FontStyles.Bold; toast.alignment = TextAlignmentOptions.Center;
        toast.color = new Color(0.85f, 0.7f, 1f); toast.raycastTarget = false; toast.text = "Arrived";
        toastRt.gameObject.SetActive(false);

        SerializedObject nso = new SerializedObject(nav);
        nso.FindProperty("worldJson").objectReferenceValue = json;
        nso.FindProperty("waypointBeam").objectReferenceValue = beam.transform;
        nso.FindProperty("toast").objectReferenceValue = toast;
        nso.ApplyModifiedPropertiesWithoutUndo();

        // ---- route line on the minimap (child of its map picture, so it turns and zooms with it)
        Transform mmImage = FindDeep(mmT, "MapImage");
        Transform oldLine = mmImage.Find("RouteLine");
        if (oldLine != null) Object.DestroyImmediate(oldLine.gameObject);
        RouteLine(mmImage, 7f, minX, minZ, w, h);

        // ---- full map
        Transform oldMap = ct.Find("FullMap");
        if (oldMap != null) Object.DestroyImmediate(oldMap.gameObject);
        RectTransform root = NewRect("FullMap", ct);
        Stretch(root);
        root.gameObject.AddComponent<Canvas>();
        root.gameObject.AddComponent<GraphicRaycaster>();
        Transform blackout = ct.Find("Blackout");
        if (blackout != null) root.SetSiblingIndex(blackout.GetSiblingIndex());
        else root.SetAsLastSibling();

        RectTransform win = NewRect("Window", root);
        Stretch(win);
        Image winBg = win.gameObject.AddComponent<Image>(); winBg.color = new Color(0.03f, 0.035f, 0.04f, 0.97f);

        RectTransform vp = NewRect("Viewport", win);
        vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one; vp.offsetMin = Vector2.zero; vp.offsetMax = new Vector2(-PanelWidth, 0);
        Image vpImg = vp.gameObject.AddComponent<Image>(); vpImg.color = new Color(0, 0, 0, 0);    // invisible, but catches touches
        vp.gameObject.AddComponent<RectMask2D>();
        LundunMapInput input = vp.gameObject.AddComponent<LundunMapInput>();

        RectTransform content = NewRect("Content", vp);
        content.anchorMin = content.anchorMax = content.pivot = new Vector2(0.5f, 0.5f);
        content.sizeDelta = new Vector2(w, h);
        RectTransform mapImg = NewRect("MapImage", content); Stretch(mapImg);
        RawImage raw = mapImg.gameObject.AddComponent<RawImage>(); raw.texture = mapTex; raw.raycastTarget = false;
        RouteLine(content, 6f, minX, minZ, w, h);
        RectTransform labels = NewRect("Labels", content); Stretch(labels);
        RectTransform blips = NewRect("Blips", content); Stretch(blips);

        RectTransform panel = NewRect("Panel", win);
        panel.anchorMin = new Vector2(1, 0); panel.anchorMax = new Vector2(1, 1); panel.pivot = new Vector2(1, 0.5f);
        panel.sizeDelta = new Vector2(PanelWidth, 0); panel.anchoredPosition = Vector2.zero;
        Image pbg = panel.gameObject.AddComponent<Image>(); pbg.color = new Color(0.07f, 0.075f, 0.085f, 1f);

        TextMeshProUGUI title = Text(panel, "LUNDUN", 40, Color.white, FontStyles.Bold);
        Top(title.rectTransform, 30, 50);
        TextMeshProUGUI info = Text(panel, "", 26, new Color(0.85f, 0.7f, 1f), FontStyles.Normal);
        Top(info.rectTransform, 88, 80);

        RectTransform list = NewRect("Destinations", panel);
        list.anchorMin = new Vector2(0, 0); list.anchorMax = new Vector2(1, 1);
        list.offsetMin = new Vector2(24, 24); list.offsetMax = new Vector2(-24, -186);
        VerticalLayoutGroup vlg = list.gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.spacing = 6; vlg.childControlHeight = true; vlg.childControlWidth = true;
        vlg.childForceExpandHeight = false; vlg.childForceExpandWidth = true;

        LundunFullMap fm = root.gameObject.AddComponent<LundunFullMap>();
        SerializedObject so = new SerializedObject(fm);
        so.FindProperty("window").objectReferenceValue = win.gameObject;
        so.FindProperty("viewport").objectReferenceValue = vp;
        so.FindProperty("content").objectReferenceValue = content;
        so.FindProperty("labelLayer").objectReferenceValue = labels;
        so.FindProperty("blipLayer").objectReferenceValue = blips;
        so.FindProperty("listContainer").objectReferenceValue = list;
        so.FindProperty("routeInfo").objectReferenceValue = info;
        so.FindProperty("dotSprite").objectReferenceValue = dot;
        so.FindProperty("arrowSprite").objectReferenceValue = arrow;
        so.FindProperty("playerCar").objectReferenceValue = car;
        so.FindProperty("mapMinX").floatValue = minX; so.FindProperty("mapMinZ").floatValue = minZ;
        so.FindProperty("mapWidth").floatValue = w; so.FindProperty("mapHeight").floatValue = h;
        so.ApplyModifiedPropertiesWithoutUndo();
        SerializedObject iso = new SerializedObject(input);
        iso.FindProperty("map").objectReferenceValue = fm;
        iso.ApplyModifiedPropertiesWithoutUndo();
        win.gameObject.SetActive(false);

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = root.gameObject;
        Debug.Log("[Lundun] Full map + GPS built. Press Play and tap the minimap.");
    }

    static void RouteLine(Transform parent, float width, float minX, float minZ, float w, float h)
    {
        RectTransform rt = NewRect("RouteLine", parent);
        Stretch(rt);
        rt.gameObject.AddComponent<CanvasRenderer>();
        LundunRouteLine line = rt.gameObject.AddComponent<LundunRouteLine>();
        line.color = Purple;
        line.raycastTarget = false;
        SerializedObject so = new SerializedObject(line);
        so.FindProperty("width").floatValue = width;
        so.FindProperty("mapMinX").floatValue = minX; so.FindProperty("mapMinZ").floatValue = minZ;
        so.FindProperty("mapWidth").floatValue = w; so.FindProperty("mapHeight").floatValue = h;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static Material BeamMaterial()
    {
        string path = MatDir + "/Beacon_Waypoint.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        m = new Material(sh != null ? sh : Shader.Find("Standard"));
        m.SetColor("_BaseColor", Purple);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ------------------------------------------------------------------ helpers
    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }

    static RectTransform NewRect(string name, Transform parent)
    {
        GameObject g = new GameObject(name, typeof(RectTransform));
        g.layer = LayerMask.NameToLayer("UI");
        g.transform.SetParent(parent, false);
        return (RectTransform)g.transform;
    }

    static void Stretch(RectTransform r) { r.anchorMin = Vector2.zero; r.anchorMax = Vector2.one; r.offsetMin = r.offsetMax = Vector2.zero; }

    static void Top(RectTransform r, float fromTop, float height)
    {
        r.anchorMin = new Vector2(0, 1); r.anchorMax = new Vector2(1, 1); r.pivot = new Vector2(0.5f, 1);
        r.offsetMin = new Vector2(24, -fromTop - height); r.offsetMax = new Vector2(-24, -fromTop);
    }

    static TextMeshProUGUI Text(Transform parent, string text, float size, Color col, FontStyles style)
    {
        RectTransform rt = NewRect("Text", parent);
        TextMeshProUGUI t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.text = text; t.fontSize = size; t.color = col; t.fontStyle = style;
        t.alignment = TextAlignmentOptions.MidlineLeft; t.raycastTarget = false;
        return t;
    }

    static Transform Root(string name)
    {
        foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects()) if (g.name == name) return g.transform;
        return null;
    }

    static Transform FindDeep(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
        return null;
    }
}
