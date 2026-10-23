// Lundun → 21  Building Kit (L1)
// Paints the two building atlases (LundunBuildingKitArt), makes the Bld_Kit_A / Bld_Kit_B materials, then runs
// Lundun → 13 so the city's buildings are rebuilt with the kit and the shop fronts follow the new building types.
// Every plot stays exactly where it was. Afterwards: Lundun → 3 Bake Navigation, then Cmd+S.
// Lundun → 21b puts the old plain boxes back (and remembers that until you run 21 again).
using System.IO;
using LundunGreybox;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
public static class LundunBuildingKitBuilder
{
    const string Dir = "Assets/World/Generated/BuildingKit";
    const string MatDir = "Assets/World/Materials";
    const string PrefKey = "Lundun.BuildingKit";
    const int ArtVersion = 4;                       // 2 = L5 window light variety, 3 = L6 railway strips, 4 = E7 Wheel signs. Bump when the painting code changes, so the atlases are redrawn

    static LundunBuildingKitBuilder() { Generator.UseBuildingKit = EditorPrefs.GetBool(PrefKey, true); }

    [MenuItem("Lundun/21  Building Kit (L1)", false, 23)]
    public static void Build()
    {
        EditorPrefs.SetBool(PrefKey, true);
        Generator.UseBuildingKit = true;
        if (!EnsureMaterials(true)) return;
        LundunDressingBuilder.Build();             // rebuilds World/Buildings with the kit, and re-dresses the streets
        RegisterWindowGlow();
        Debug.Log("[Lundun] Building kit in. Next: Lundun → 3 Bake Navigation, then Cmd+S.");
    }

    [MenuItem("Lundun/21b Building Kit – Back to Boxes", false, 24)]
    public static void Undo()
    {
        EditorPrefs.SetBool(PrefKey, false);
        Generator.UseBuildingKit = false;
        LundunDressingBuilder.Build();
        Debug.Log("[Lundun] Plain buildings back (Lundun → 21 brings the kit back). Then Lundun → 3 Bake Navigation, Cmd+S.");
    }

    // ------------------------------------------------------------------ materials
    /// Makes sure both kit materials and their textures exist. force = redraw the textures.
    public static bool EnsureMaterials(bool force = false)
    {
        Ensure("Assets", "World"); Ensure("Assets/World", "Generated"); Ensure("Assets/World/Generated", "BuildingKit"); Ensure("Assets/World", "Materials");
        string vfile = Dir + "/version.txt";
        bool redraw = force || !File.Exists(vfile) || File.ReadAllText(vfile).Trim() != ArtVersion.ToString()
                      || AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Kit_A.png") == null || AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Kit_B.png") == null;
        try
        {
            for (int a = 0; a < 2; a++)
            {
                string n = a == 0 ? "A" : "B";
                if (redraw)
                {
                    EditorUtility.DisplayProgressBar("Lundun", "Painting the building atlas " + n + "…", 0.2f + a * 0.4f);
                    KitArt.Atlas at = KitArt.Paint(a);
                    SaveTexture(Dir + "/Kit_" + n + ".png", at.rgba, KitArt.W, KitArt.H, true);
                    SaveTexture(Dir + "/Kit_" + n + "_Glow.png", at.glow, KitArt.GlowW, KitArt.GlowH, false);
                }
                MakeMaterial(a);
            }
            if (redraw) { File.WriteAllText(vfile, ArtVersion.ToString()); AssetDatabase.ImportAsset(vfile); }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Lundun", "Building kit failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.", "OK");
            return false;
        }
        finally { EditorUtility.ClearProgressBar(); }
        AssetDatabase.SaveAssets();
        return true;
    }

    /// The material for one of the kit names (Bld_Kit_A / Bld_Kit_B), created if needed.
    public static Material MaterialFor(string name)
    {
        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null && m.GetTexture("_BaseMap") != null) return m;
        EnsureMaterials();
        return AssetDatabase.LoadAssetAtPath<Material>(path);
    }

    static Material MakeMaterial(int atlas)
    {
        string name = atlas == 0 ? Generator.KitMatA : Generator.KitMatB, n = atlas == 0 ? "A" : "B";
        string path = MatDir + "/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Lit");
            if (sh == null) sh = Shader.Find("Standard");
            m = new Material(sh) { name = name };
            AssetDatabase.CreateAsset(m, path);
        }
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Kit_" + n + ".png"));
        m.SetTextureScale("_BaseMap", Vector2.one); m.SetTextureOffset("_BaseMap", Vector2.zero);
        m.SetColor("_BaseColor", Color.white);
        // smoothness comes from the atlas alpha: glass shiny, brick and concrete matte
        m.SetFloat("_SmoothnessTextureChannel", 1f);
        m.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        m.SetFloat("_Smoothness", 0.8f);
        m.SetFloat("_Metallic", 0f);
        // lit windows at night (LundunLighting turns this up and down with the clock)
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        m.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Dir + "/Kit_" + n + "_Glow.png"));
        if (!m.HasProperty("_EmissionColor") || m.GetColor("_EmissionColor").maxColorComponent < 0.01f) m.SetColor("_EmissionColor", Color.white * 1.3f);
        m.enableInstancing = true;
        EditorUtility.SetDirty(m);
        return m;
    }

    static void SaveTexture(string path, byte[] rgba, int w, int h, bool albedo)
    {
        Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false);
        t.LoadRawTextureData(rgba); t.Apply();
        File.WriteAllBytes(path, t.EncodeToPNG());
        Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        TextureImporter ti = (TextureImporter)AssetImporter.GetAtPath(path);
        ti.textureType = TextureImporterType.Default;
        ti.sRGBTexture = true; ti.mipmapEnabled = true;
        ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp;     // walls repeat along, strips never wrap up/down
        ti.filterMode = FilterMode.Trilinear; ti.anisoLevel = albedo ? 4 : 1;
        ti.alphaSource = albedo ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
        ti.alphaIsTransparency = false;
        ti.maxTextureSize = 4096; ti.textureCompression = TextureImporterCompression.CompressedHQ;
        TextureImporterPlatformSettings ps = ti.GetPlatformTextureSettings("iPhone");
        ps.overridden = true; ps.maxTextureSize = 4096;
        ps.format = albedo ? TextureImporterFormat.ASTC_4x4 : TextureImporterFormat.ASTC_6x6;
        ps.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.SetPlatformTextureSettings(ps);
        ti.SaveAndReimport();
    }

    /// The day/night clock drives window glow through LundunLighting's window materials: add the kit's.
    public static void RegisterWindowGlow()
    {
        LundunLighting ll = null;
        foreach (LundunLighting l in Object.FindObjectsByType<LundunLighting>(FindObjectsInactive.Include)) { ll = l; break; }
        if (ll == null) { Debug.LogWarning("[Lundun] No LundunLighting in the scene — windows won't follow the clock (run Lundun → Lighting → Rebuild Lighting Rig)."); return; }
        SerializedObject so = new SerializedObject(ll);
        SerializedProperty arr = so.FindProperty("windowMaterials");
        if (arr == null) return;
        foreach (string name in new[] { Generator.KitMatA, Generator.KitMatB })
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "/" + name + ".mat");
            if (m == null) continue;
            bool found = false;
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == m) found = true;
            if (found) continue;
            arr.arraySize++;
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = m;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        if (ll.preset != null) ll.Apply(ll.preset.look);
        EditorUtility.SetDirty(ll);
    }

    static void Ensure(string parent, string child) { if (!AssetDatabase.IsValidFolder(parent + "/" + child)) AssetDatabase.CreateFolder(parent, child); }
}
