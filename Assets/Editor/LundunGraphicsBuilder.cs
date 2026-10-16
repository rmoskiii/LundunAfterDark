// Lundun → 15  Graphics V1 (Sharp + Shadows)
// The iPhone uses the "Mobile" quality level and Assets/Settings/Mobile_RPAsset. This:
//   • backs that asset up first (Assets/Settings/Backups/Mobile_RPAsset_beforeV1.asset)
//   • sharper picture: render scale 0.9 (adjusted live by LundunGraphics), FSR upscaling, 4x MSAA anti-aliasing
//   • better shadows: soft, 2048 resolution, 2 cascades, out to 70 m
//   • textures stay sharp at shallow angles (anisotropic filtering forced on for the Mobile level)
//   • switches on Frame Timing Stats so the game can read the real GPU time on the phone
//   • adds LundunGraphics to the scene: keeps 30 fps by trimming resolution only when needed, and shows the numbers
// Run it again any time; it only sets these values. To undo, copy the backup's settings back (or ask Claude).
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LundunGraphicsBuilder
{
    const string AssetPath = "Assets/Settings/Mobile_RPAsset.asset";
    const string BackupDir = "Assets/Settings/Backups";
    const string BackupPath = BackupDir + "/Mobile_RPAsset_beforeV1.asset";

    [MenuItem("Lundun/15  Graphics V1 (Sharp + Shadows)", false, 15)]
    public static void Build()
    {
        Object rp = AssetDatabase.LoadMainAssetAtPath(AssetPath);
        if (rp == null) { EditorUtility.DisplayDialog("Lundun", "Can't find " + AssetPath + ".", "OK"); return; }

        // 1. backup (once: the first run keeps your original)
        if (!AssetDatabase.IsValidFolder(BackupDir)) AssetDatabase.CreateFolder("Assets/Settings", "Backups");
        bool backedUp = false;
        if (AssetDatabase.LoadMainAssetAtPath(BackupPath) == null) backedUp = AssetDatabase.CopyAsset(AssetPath, BackupPath);

        // 2. the render pipeline asset
        SerializedObject so = new SerializedObject(rp);
        Set(so, "m_RenderScale", 0.9f);
        Set(so, "m_UpscalingFilter", 3);                 // FSR: sharper upscaling than plain stretching
        Set(so, "m_FsrOverrideSharpness", true);
        Set(so, "m_FsrSharpness", 0.85f);
        Set(so, "m_MSAA", 4);                            // 4x anti-aliasing: smooth edges (cheap on Apple GPUs)
        Set(so, "m_MainLightShadowmapResolution", 2048);
        Set(so, "m_ShadowDistance", 70f);
        Set(so, "m_ShadowCascadeCount", 2);
        Set(so, "m_Cascade2Split", 0.3f);
        Set(so, "m_SoftShadowsSupported", true);
        Set(so, "m_SoftShadowQuality", 2);               // medium
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(rp);

        // 3. the Mobile quality level: anisotropic filtering forced on
        Object[] qs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset");
        bool aniso = false;
        if (qs.Length > 0)
        {
            SerializedObject q = new SerializedObject(qs[0]);
            SerializedProperty levels = q.FindProperty("m_QualitySettings");
            for (int i = 0; levels != null && i < levels.arraySize; i++)
            {
                SerializedProperty lv = levels.GetArrayElementAtIndex(i);
                if (lv.FindPropertyRelative("name").stringValue != "Mobile") continue;
                lv.FindPropertyRelative("anisotropicTextures").intValue = 2;      // Forced On
                aniso = true;
            }
            q.ApplyModifiedPropertiesWithoutUndo();
        }

        // 4. real GPU timings on the phone
        PlayerSettings.enableFrameTimingStats = true;

        // 5. LundunGraphics in the scene
        Scene scene = SceneManager.GetActiveScene();
        LundunGraphics g = Object.FindAnyObjectByType<LundunGraphics>(FindObjectsInactive.Include);
        if (g == null)
        {
            GameObject host = GameObject.Find("LundunRuntime");
            if (host == null) host = new GameObject("LundunGraphics");
            g = host.AddComponent<LundunGraphics>();
        }
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();

        Debug.Log("[Lundun] Graphics V1 applied to " + AssetPath + ": render scale 0.9 (FSR), 4x MSAA, soft shadows 2048 / 2 cascades / 70 m" +
                  (aniso ? ", anisotropic filtering on" : ", (Mobile quality level not found — anisotropic unchanged)") +
                  ", frame timing stats on, LundunGraphics on '" + g.gameObject.name + "'." +
                  (backedUp ? "  Backup: " + BackupPath : "  (Backup already existed: " + BackupPath + ")") +
                  "  Press Cmd+S to save the scene.");
    }

    static void Set(SerializedObject so, string name, float v) { SerializedProperty p = so.FindProperty(name); if (p != null) p.floatValue = v; else Missing(name); }
    static void Set(SerializedObject so, string name, int v) { SerializedProperty p = so.FindProperty(name); if (p != null) p.intValue = v; else Missing(name); }
    static void Set(SerializedObject so, string name, bool v) { SerializedProperty p = so.FindProperty(name); if (p != null) p.boolValue = v; else Missing(name); }
    static void Missing(string name) { Debug.LogWarning("[Lundun] Graphics: setting " + name + " not found on the URP asset (skipped)."); }
}
