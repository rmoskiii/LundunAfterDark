// Lundun → 17  Sharper Maps (V2b)
// Redraws the map picture used by the minimap and the full map at twice the detail (0.5 m per pixel instead of 0.8),
// same colours, plus the hospital / police / safehouse plots tinted in. Imports it so the iPhone keeps it sharp
// (up to 4096 pixels, high-quality ASTC), and lines the minimap, full map and route lines up with it.
// Only the map picture and those four numbers change; nothing in the HUD is rebuilt.
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
    const double MetresPerPixel = 0.5;

    [MenuItem("Lundun/17  Sharper Maps (V2b)", false, 18)]
    public static void Build()
    {
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
        if (json == null) { EditorUtility.DisplayDialog("Lundun", "Can't find " + JsonPath, "OK"); return; }
        LundunWorldData data = JsonUtility.FromJson<LundunWorldData>(json.text);
        MapImage img;
        try
        {
            EditorUtility.DisplayProgressBar("Lundun", "Drawing the map at 0.5 m per pixel…", 0.3f);
            img = MapRaster.Render(data, Generator.Build(data), MetresPerPixel);

            EditorUtility.DisplayProgressBar("Lundun", "Saving " + img.width + "×" + img.height + "…", 0.7f);
            Texture2D t = new Texture2D(img.width, img.height, TextureFormat.RGBA32, false);
            t.LoadRawTextureData(img.rgba);
            t.Apply();
            File.WriteAllBytes(MapPath, t.EncodeToPNG());            // same file, so everything using it keeps its link
            Object.DestroyImmediate(t);
        }
        finally { EditorUtility.ClearProgressBar(); }

        AssetDatabase.ImportAsset(MapPath, ImportAssetOptions.ForceUpdate);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(MapPath);
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
            if (platform == "iPhone") ps.format = TextureImporterFormat.ASTC_4x4;       // sharpest ASTC
            ti.SetPlatformTextureSettings(ps);
        }
        ti.SaveAndReimport();

        // line everything up with the new picture (the area is the same; width/height round to the new pixel size)
        float minX = (float)img.minX, minZ = (float)img.minZ;
        float w = (float)(img.width * img.metresPerPixel), h = (float)(img.height * img.metresPerPixel);
        int fixedUp = 0;
        foreach (MonoBehaviour mb in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!(mb is LundunMinimap) && !(mb is LundunFullMap) && !(mb is LundunRouteLine)) continue;
            SerializedObject so = new SerializedObject(mb);
            SerializedProperty a = so.FindProperty("mapMinX"), b = so.FindProperty("mapMinZ"), c = so.FindProperty("mapWidth"), d = so.FindProperty("mapHeight");
            if (a == null || b == null || c == null || d == null) continue;
            a.floatValue = minX; b.floatValue = minZ; c.floatValue = w; d.floatValue = h;
            so.ApplyModifiedPropertiesWithoutUndo();
            fixedUp++;
        }
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Lundun] Map redrawn: " + img.width + "×" + img.height + " (0.5 m per pixel), iPhone: 4096 ASTC 4x4. " +
                  fixedUp + " map components lined up. Press Cmd+S.");
    }
}
