// Lundun → 35  Build Outfits (Phase 3)
//   Turns the outfit textures in Assets/Characters/Outfits (James's clothes recoloured: <Model>_<Outfit>_<set>.png, e.g.
//   Ch06_Olive_1001.png) into materials, one per outfit for each of his materials that has them (copies of his own,
//   only the colour map changed), and gives James a LundunOutfitSet holding them. The new textures get the same import
//   settings as his own. Run it once (again after adding outfit textures), then Cmd+S. Midnight is his own materials.
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LundunOutfitBuilder
{
    const string Folder = "Assets/Characters/Outfits";

    [MenuItem("Lundun/35  Build Outfits (Phase 3)", false, 41)]
    public static void Build()
    {
        PlayerController pc = Object.FindAnyObjectByType<PlayerController>(FindObjectsInactive.Include);
        if (pc == null) { Dialog("Can't find James (PlayerController) in the open scene. Open the Lundun scene first."); return; }
        Animator anim = pc.GetComponentInChildren<Animator>(true);
        Transform model = anim != null ? anim.transform : pc.transform;
        if (!AssetDatabase.IsValidFolder(Folder)) { Dialog("No outfit textures: " + Folder + " is missing. Copy it in from the Phase 3 zip first."); return; }

        // his materials (each once), as they are now: if an outfit's on, go back to its original
        LundunOutfitSet old = pc.GetComponent<LundunOutfitSet>();
        List<Material> mats = new List<Material>();
        foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
        {
            if (r.name.EndsWith("_FPArms")) continue;
            foreach (Material m in r.sharedMaterials)
            {
                Material o = Original(m);
                if (o != null && !mats.Contains(o)) mats.Add(o);
            }
        }

        IList<LundunOutfits.Outfit> outfits = LundunOutfits.All;
        string[] ids = new string[outfits.Count];
        for (int i = 0; i < outfits.Count; i++) ids[i] = outfits[i].id;

        List<LundunOutfitSet.Slot> slots = new List<LundunOutfitSet.Slot>();
        List<string> report = new List<string>(), missing = new List<string>();
        int made = 0;
        foreach (Material m in mats)
        {
            Texture baseTex = Colour(m);
            string prefix, set;
            if (baseTex == null || !Split(baseTex.name, out prefix, out set)) continue;     // eyelashes and the like: no outfits
            LundunOutfitSet.Slot slot = new LundunOutfitSet.Slot { original = m, byOutfit = new Material[outfits.Count] };
            bool any = false;
            for (int i = 0; i < outfits.Count; i++)
            {
                LundunOutfits.Outfit o = outfits[i];
                if (string.IsNullOrEmpty(o.file)) continue;                                  // Midnight: his own
                string texPath = Folder + "/" + prefix + "_" + o.file + "_" + set + ".png";
                if (!File.Exists(texPath)) { missing.Add(Path.GetFileName(texPath)); continue; }
                MatchImport(baseTex, texPath);
                Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                if (tex == null) { missing.Add(Path.GetFileName(texPath) + " (couldn't load)"); continue; }
                slot.byOutfit[i] = OutfitMaterial(m, o, tex);
                any = true; made++;
            }
            if (any) { slots.Add(slot); report.Add(m.name + " (" + baseTex.name + ")"); }
        }
        AssetDatabase.SaveAssets();
        if (slots.Count == 0) { Dialog("No outfits built: none of the textures were found.\n" + string.Join("\n", missing.ToArray())); return; }

        // put him back in his own clothes in the scene (outfits go on at runtime), and give him the set
        if (old != null) old.WearOn(model.gameObject, LundunOutfits.DefaultId);
        LundunOutfitSet setComp = old != null ? old : Undo.AddComponent<LundunOutfitSet>(pc.gameObject);
        Undo.RecordObject(setComp, "Outfits");
        setComp.Set(ids, slots.ToArray());
        EditorUtility.SetDirty(setComp);
        EditorSceneManager.MarkSceneDirty(pc.gameObject.scene);

        string msg = made + " outfit material(s) for " + slots.Count + " of James's materials: " + string.Join(", ", report.ToArray()) +
                     (missing.Count > 0 ? "\nMissing: " + string.Join(", ", missing.ToArray()) : "") + "\n\nNow Cmd+S to save the scene.";
        Debug.Log("[Lundun] Phase 3 outfits: " + msg);
        Dialog(msg);
    }

    /// If m is one of the outfit materials this builds, the material it was copied from.
    static Material Original(Material m)
    {
        if (m == null) return null;
        string path = AssetDatabase.GetAssetPath(m);
        if (!path.StartsWith(Folder + "/")) return m;
        string baseName = m.name.Substring(0, Mathf.Max(0, m.name.LastIndexOf('_')));
        foreach (string guid in AssetDatabase.FindAssets(baseName + " t:Material"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (p.StartsWith(Folder + "/")) continue;
            Material o = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (o != null && o.name == baseName) return o;
        }
        return null;
    }

    /// The material's colour map (URP's _BaseMap, or the old _MainTex).
    static Texture Colour(Material m)
    {
        if (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) return m.GetTexture("_BaseMap");
        if (m.HasProperty("_MainTex")) return m.GetTexture("_MainTex");
        return null;
    }

    /// "Ch06_1001_Diffuse" → "Ch06" and "1001".
    static bool Split(string texName, out string prefix, out string set)
    {
        prefix = set = null;
        string n = texName.EndsWith("_Diffuse") ? texName.Substring(0, texName.Length - "_Diffuse".Length) : texName;
        int cut = n.LastIndexOf('_');
        if (cut <= 0 || cut >= n.Length - 1) return false;
        prefix = n.Substring(0, cut); set = n.Substring(cut + 1);
        return true;
    }

    /// A copy of his material with the outfit's colour map (made once, kept up to date after that).
    static Material OutfitMaterial(Material original, LundunOutfits.Outfit o, Texture2D tex)
    {
        string path = Folder + "/" + original.name + "_" + o.file + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(original) { name = original.name + "_" + o.file };
            AssetDatabase.CreateAsset(m, path);
        }
        else m.CopyPropertiesFromMaterial(original);
        if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
        if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// The outfit texture imports exactly like his own (size, compression, mip-maps, colour space, per platform).
    static void MatchImport(Texture baseTex, string texPath)
    {
        TextureImporter from = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(baseTex)) as TextureImporter;
        TextureImporter to = AssetImporter.GetAtPath(texPath) as TextureImporter;
        if (from == null || to == null) return;
        TextureImporterSettings s = new TextureImporterSettings();
        from.ReadTextureSettings(s);
        to.SetTextureSettings(s);
        to.maxTextureSize = from.maxTextureSize;
        to.textureCompression = from.textureCompression;
        foreach (string platform in new[] { "iPhone", "Android", "Standalone" })
        {
            TextureImporterPlatformSettings p = from.GetPlatformTextureSettings(platform);
            p.name = platform;
            to.SetPlatformTextureSettings(p);
        }
        to.SaveAndReimport();
    }

    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }
}
