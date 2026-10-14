// Lundun → 5  Build Minimap: draws the map picture from the world data and builds the minimap HUD.
using System.Collections.Generic;
using System.IO;
using LundunGreybox;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

public static class LundunMinimapBuilder
{
    const string JsonPath = "Assets/World/LundunWorld.json";
    const string GenDir = "Assets/World/Generated";
    const string MapPath = GenDir + "/LundunMap.png";
    const double MetresPerPixel = 0.8;
    const float Size = 280f;            // minimap diameter (UI units, 1920×1080 reference)

    [MenuItem("Lundun/5  Build Minimap", false, 5)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        Transform world = Root("World");
        if (world == null) { Dialog("Build the world first: Lundun → 1  Build Greybox World."); return; }
        Canvas canvas = null;
        Transform ct = Root("Canvas");
        if (ct != null) canvas = ct.GetComponent<Canvas>();
        if (canvas == null) { Dialog("Can't find the HUD Canvas."); return; }

        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
        if (json == null) { Dialog("Can't find " + JsonPath); return; }
        LundunWorldData data = JsonUtility.FromJson<LundunWorldData>(json.text);

        try
        {
            EditorUtility.DisplayProgressBar("Lundun", "Drawing the map…", 0.2f);
            MapImage img = MapRaster.Render(data, Generator.Build(data), MetresPerPixel);
            Texture2D mapTex = SaveMap(img);
            Sprite circle = SaveSprite("MinimapCircle", 128, CirclePixels(128, false));
            Sprite ring = SaveSprite("MinimapRing", 256, CirclePixels(256, true));
            Sprite arrow = SaveSprite("MinimapArrow", 128, ArrowPixels(128));
            EditorUtility.DisplayProgressBar("Lundun", "Building the HUD…", 0.8f);
            BuildHud(canvas, world, mapTex, img, circle, ring, arrow);
            EditorSceneManager.MarkSceneDirty(scene);
            Debug.Log("[Lundun] Minimap built (" + img.width + "×" + img.height + " map). Press Play.");
        }
        finally { EditorUtility.ClearProgressBar(); }
    }

    // ------------------------------------------------------------------ HUD
    static void BuildHud(Canvas canvas, Transform world, Texture2D mapTex, MapImage img, Sprite circle, Sprite ring, Sprite arrow)
    {
        Transform cr = canvas.transform;
        // rebuild cleanly: put the health bar back first, then remove the old minimap
        Transform oldHud = cr.Find("TopLeftHUD");
        Transform health = FindDeep(cr, "HealthBar");
        if (oldHud != null)
        {
            if (health != null && health.IsChildOf(oldHud)) health.SetParent(cr, false);
            Object.DestroyImmediate(oldHud.gameObject);
        }

        RectTransform hud = NewRect("TopLeftHUD", cr);
        hud.anchorMin = hud.anchorMax = new Vector2(0, 1); hud.pivot = new Vector2(0, 1);
        hud.anchoredPosition = new Vector2(36, -30);
        hud.sizeDelta = new Vector2(Size, Size + 30);
        Transform blackout = cr.Find("Blackout");
        if (blackout != null) hud.SetSiblingIndex(blackout.GetSiblingIndex());   // fades still cover it

        // minimap: dark disc that clips everything inside it
        RectTransform mm = NewRect("Minimap", hud);
        mm.anchorMin = mm.anchorMax = new Vector2(0, 1); mm.pivot = new Vector2(0.5f, 0.5f);
        mm.anchoredPosition = new Vector2(Size / 2, -Size / 2);
        mm.sizeDelta = new Vector2(Size, Size);
        Canvas sub = mm.gameObject.AddComponent<Canvas>();          // own canvas: redrawing it every frame doesn't touch the rest of the HUD
        mm.gameObject.AddComponent<GraphicRaycaster>();
        Image disc = mm.gameObject.AddComponent<Image>();
        disc.sprite = circle; disc.color = new Color(0.08f, 0.09f, 0.10f, 0.9f);
        Mask mask = mm.gameObject.AddComponent<Mask>(); mask.showMaskGraphic = true;

        RectTransform rot = NewRect("Rotator", mm);
        rot.anchorMin = rot.anchorMax = rot.pivot = new Vector2(0.5f, 0.5f); rot.sizeDelta = Vector2.zero;
        RectTransform mapRt = NewRect("MapImage", rot);
        mapRt.anchorMin = mapRt.anchorMax = new Vector2(0.5f, 0.5f);
        RawImage raw = mapRt.gameObject.AddComponent<RawImage>();
        raw.texture = mapTex; raw.raycastTarget = false; raw.color = new Color(1, 1, 1, 0.95f);

        RectTransform blips = NewRect("Blips", mm);
        Stretch(blips);

        RectTransform north = NewRect("North", mm);
        north.anchorMin = north.anchorMax = new Vector2(0.5f, 0.5f); north.sizeDelta = new Vector2(30, 30);
        TextMeshProUGUI n = north.gameObject.AddComponent<TextMeshProUGUI>();
        n.text = "N"; n.fontSize = 22; n.fontStyle = FontStyles.Bold; n.alignment = TextAlignmentOptions.Center;
        n.color = new Color(1f, 1f, 1f, 0.85f); n.raycastTarget = false;

        RectTransform arr = NewRect("PlayerArrow", mm);
        arr.anchorMin = arr.anchorMax = new Vector2(0.5f, 0.5f); arr.sizeDelta = new Vector2(28, 28);
        Image ai = arr.gameObject.AddComponent<Image>();
        ai.sprite = arrow; ai.color = Color.white; ai.raycastTarget = false;

        RectTransform bord = NewRect("Border", hud);
        bord.anchorMin = bord.anchorMax = new Vector2(0, 1); bord.pivot = new Vector2(0.5f, 0.5f);
        bord.anchoredPosition = mm.anchoredPosition; bord.sizeDelta = new Vector2(Size + 8, Size + 8);
        Image bi = bord.gameObject.AddComponent<Image>();
        bi.sprite = ring; bi.raycastTarget = false;

        // health bar sits under the minimap
        if (health != null)
        {
            RectTransform h = (RectTransform)health;
            h.SetParent(hud, false);
            h.anchorMin = h.anchorMax = new Vector2(0, 1); h.pivot = new Vector2(0, 1);
            h.anchoredPosition = new Vector2(0, -(Size + 12)); h.sizeDelta = new Vector2(Size, 14);
        }
        else Debug.LogWarning("[Lundun] HealthBar not found — left where it was.");

        // wire up
        LundunMinimap m = mm.gameObject.AddComponent<LundunMinimap>();
        SerializedObject so = new SerializedObject(m);
        so.FindProperty("hudRoot").objectReferenceValue = hud;
        so.FindProperty("rotator").objectReferenceValue = rot;
        so.FindProperty("map").objectReferenceValue = raw;
        so.FindProperty("blipLayer").objectReferenceValue = blips;
        so.FindProperty("playerArrow").objectReferenceValue = arr;
        so.FindProperty("north").objectReferenceValue = north;
        so.FindProperty("border").objectReferenceValue = bi;
        so.FindProperty("dotSprite").objectReferenceValue = circle;
        Transform pois = FindDeep(world, "POIs");
        so.FindProperty("poiGroup").objectReferenceValue = pois;
        Transform car = Root("Car");
        so.FindProperty("playerCar").objectReferenceValue = car;
        so.FindProperty("mapMinX").floatValue = (float)img.minX;
        so.FindProperty("mapMinZ").floatValue = (float)img.minZ;
        so.FindProperty("mapWidth").floatValue = (float)(img.width * img.metresPerPixel);
        so.FindProperty("mapHeight").floatValue = (float)(img.height * img.metresPerPixel);
        so.ApplyModifiedPropertiesWithoutUndo();
        if (pois == null) Debug.LogWarning("[Lundun] World/POIs not found — no safehouse/mission icons.");
        if (car == null) Debug.LogWarning("[Lundun] No root object called 'Car' — no parked-car icon.");
        Selection.activeGameObject = mm.gameObject;
        sub.overrideSorting = false;
    }

    // ------------------------------------------------------------------ textures
    static Texture2D SaveMap(MapImage img)
    {
        EnsureFolder(GenDir);
        Texture2D t = new Texture2D(img.width, img.height, TextureFormat.RGBA32, false);
        t.LoadRawTextureData(img.rgba);
        t.Apply();
        File.WriteAllBytes(MapPath, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(MapPath, ImportAssetOptions.ForceUpdate);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(MapPath);
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Trilinear;
        ti.maxTextureSize = 4096;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(MapPath);
    }

    static Sprite SaveSprite(string name, int size, Color32[] px)
    {
        EnsureFolder(GenDir);
        string path = GenDir + "/" + name + ".png";
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.SetPixels32(px); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Sprite;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = false;
        ti.textureCompression = TextureImporterCompression.Uncompressed;
        ti.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    /// White disc (or ring) with a soft 1-pixel edge.
    static Color32[] CirclePixels(int s, bool ring)
    {
        Color32[] px = new Color32[s * s];
        float r = s / 2f - 1f, stroke = s * 0.035f;
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - s / 2f) * (x + 0.5f - s / 2f) + (y + 0.5f - s / 2f) * (y + 0.5f - s / 2f));
                float a = Mathf.Clamp01(r - d + 0.5f);
                if (ring) a = Mathf.Min(a, Mathf.Clamp01(d - (r - stroke) + 0.5f));
                px[y * s + x] = new Color32(255, 255, 255, (byte)(a * 255));
            }
        return px;
    }

    /// Arrow pointing up, with a notch at the back (like GTA's player marker).
    static Color32[] ArrowPixels(int s)
    {
        Color32[] px = new Color32[s * s];
        Vector2 tip = new Vector2(0.5f, 0.95f), left = new Vector2(0.12f, 0.08f), right = new Vector2(0.88f, 0.08f), notch = new Vector2(0.5f, 0.3f);
        const int SS = 4;    // 4×4 samples per pixel for smooth edges
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                int hits = 0;
                for (int sy = 0; sy < SS; sy++)
                    for (int sx = 0; sx < SS; sx++)
                    {
                        Vector2 p = new Vector2((x + (sx + 0.5f) / SS) / s, (y + (sy + 0.5f) / SS) / s);
                        if (InTri(p, tip, left, notch) || InTri(p, tip, notch, right)) hits++;
                    }
                px[y * s + x] = new Color32(255, 255, 255, (byte)(255 * hits / (SS * SS)));
            }
        return px;
    }

    static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Side(p, a, b), d2 = Side(p, b, c), d3 = Side(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    static float Side(Vector2 p, Vector2 a, Vector2 b) { return (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y); }

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

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
