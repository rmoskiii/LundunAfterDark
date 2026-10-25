// Lundun → 17  Sharper Maps (V2b)
// Redraws the map picture used by the minimap and the full map at twice the detail (0.5 m per pixel instead of 0.8),
// same colours, plus the hospital / police / safehouse plots tinted in. Imports it so the iPhone keeps it sharp
// (up to 4096 pixels, high-quality ASTC), and lines the minimap, full map and route lines up with it.
// Only the map picture and those four numbers change; nothing in the HUD is rebuilt.
// E8: Brixtun and Kingsmere both on the picture; and LundunMapBackdrop.png, Greater Lundun round the playable city (the
// river across the middle, the outer boroughs, the dark beyond) for the full map, which zooms out to show all of it.
using System.Collections.Generic;
using LundunGreybox;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LundunMapSharpBuilder
{
    const string JsonPath = "Assets/World/LundunWorld.json";
    const string MapPath = "Assets/World/Generated/LundunMap.png";
    const string BackdropPath = "Assets/World/Generated/LundunMapBackdrop.png";
    const double BackdropMetresPerPixel = 2.25;
    const double MetresPerPixel = 0.5;

    [MenuItem("Lundun/17  Sharper Maps (V2b)", false, 18)]
    public static void Build()
    {
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
        if (json == null) { EditorUtility.DisplayDialog("Lundun", "Can't find " + JsonPath, "OK"); return; }
        LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
        MapImage img, back;
        try
        {
            EditorUtility.DisplayProgressBar("Lundun", "Drawing the map at 0.5 m per pixel…", 0.3f);
            // E0: as fine as 0.5 m per pixel, but no side over 4096 pixels (the map grows with the expansions)
            LwBounds wb = data.worldBounds;
            double side = System.Math.Max(wb.wallMaxX - wb.wallMinX, wb.wallMaxZ - wb.wallMinZ) + 60;
            double mpp = System.Math.Max(MetresPerPixel, System.Math.Ceiling(side / 4096.0 * 100) / 100);
            // E1: the expansion areas too (built first; then the city again, which the map's places need)
            // E8: Brixtun (with its retail park's shapes, kept before the next build), then Kingsmere, then the city
            List<MapArea> areas = new List<MapArea>();
            GreyboxResult xr = Generator.BuildExpansion(data);
            if (xr.buildings.Count > 0) areas.Add(new MapArea { world = Generator.ExpansionWorld, result = xr, shapes = new List<Generator.RetailMapPoly>(Generator.RetailMap) });
            GreyboxResult kr = Generator.BuildKingsmere(data);
            if (kr.buildings.Count > 0) areas.Add(new MapArea { world = Generator.ExpansionWorld, result = kr, shapes = Generator.KingsmereMap() });
            GreyboxResult city = Generator.Build(data);
            img = MapRaster.Render(data, city, mpp, areas);

            EditorUtility.DisplayProgressBar("Lundun", "Saving " + img.width + "×" + img.height + "…", 0.6f);
            SavePng(MapPath, img);                                    // same file, so everything using it keeps its link

            EditorUtility.DisplayProgressBar("Lundun", "Drawing Greater Lundun round it…", 0.75f);
            back = MapRaster.RenderBackdrop(data, BackdropMetresPerPixel);
            SavePng(BackdropPath, back);
        }
        finally { EditorUtility.ClearProgressBar(); }

        Import(MapPath, TextureImporterFormat.ASTC_4x4);
        Import(BackdropPath, TextureImporterFormat.ASTC_6x6);
        Texture2D backTex = AssetDatabase.LoadAssetAtPath<Texture2D>(BackdropPath);

        // line everything up with the new picture (the area is the same; width/height round to the new pixel size)
        float minX = (float)img.minX, minZ = (float)img.minZ;
        float w = (float)(img.width * img.metresPerPixel), h = (float)(img.height * img.metresPerPixel);
        List<KeyValuePair<string, V2>> outer = MapRaster.OuterNames(data);
        string[] outerNames = new string[outer.Count]; Vector2[] outerAt = new Vector2[outer.Count];
        for (int i = 0; i < outer.Count; i++) { outerNames[i] = outer[i].Key; outerAt[i] = new Vector2((float)outer[i].Value.x, (float)outer[i].Value.z); }
        int fixedUp = 0;
        foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!(mb is LundunMinimap) && !(mb is LundunFullMap) && !(mb is LundunRouteLine)) continue;
            SerializedObject so = new SerializedObject(mb);
            SerializedProperty a = so.FindProperty("mapMinX"), b = so.FindProperty("mapMinZ"), c = so.FindProperty("mapWidth"), d = so.FindProperty("mapHeight");
            if (a == null || b == null || c == null || d == null) continue;
            a.floatValue = minX; b.floatValue = minZ; c.floatValue = w; d.floatValue = h;
            so.ApplyModifiedPropertiesWithoutUndo();
            if (mb is LundunFullMap fm)
                fm.SetBackdrop(backTex, new Vector4((float)back.minX, (float)back.minZ, (float)(back.width * back.metresPerPixel), (float)(back.height * back.metresPerPixel)), outerNames, outerAt);
            EditorUtility.SetDirty(mb);
            fixedUp++;
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Lundun] Map redrawn: " + img.width + "×" + img.height + " (" + img.metresPerPixel + " m per pixel), iPhone: 4096 ASTC 4x4; Greater Lundun backdrop " +
                  back.width + "×" + back.height + ". " + fixedUp + " map components lined up. Press Cmd+S.");
    }

    static void SavePng(string path, MapImage im)
    {
        Texture2D t = new Texture2D(im.width, im.height, TextureFormat.RGBA32, false);
        t.LoadRawTextureData(im.rgba);
        t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
    }

    static void Import(string path, TextureImporterFormat iphone)
    {
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true;
        ti.mipmapEnabled = true;
        ti.wrapMode = TextureWrapMode.Clamp;
        ti.filterMode = FilterMode.Trilinear;
        ti.anisoLevel = 4;
        ti.maxTextureSize = 4096;
        ti.npotScale = TextureImporterNPOTScale.None;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        foreach (string platform in new[] { "iPhone", "Standalone" })
        {
            TextureImporterPlatformSettings ps = ti.GetPlatformTextureSettings(platform);
            ps.overridden = true;
            ps.maxTextureSize = 4096;
            ps.textureCompression = TextureImporterCompression.CompressedHQ;
            if (platform == "iPhone") ps.format = iphone;       // the map: sharpest ASTC; the backdrop: a little softer
            ti.SetPlatformTextureSettings(ps);
        }
        ti.SaveAndReimport();
    }
}
