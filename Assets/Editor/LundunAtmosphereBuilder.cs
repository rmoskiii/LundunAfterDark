// Lundun → 18  Atmosphere V3a (Grade + Fog + Glow)
//   • makes the height-fog material (Assets/World/Lighting/LundunHeightFog.mat, shader Lundun/HeightFog)
//   • adds a "LundunHeightFog" Full Screen Pass to the iPhone renderer (Mobile_Renderer) and the editor's (PC_Renderer),
//     drawn just before post-processing, using the depth buffer
//   • adds LundunAtmosphere to the LundunLighting object: per-time-of-day grade, fog settings, lamp halos
// Safe to run again: it updates what's there. To turn the fog off, untick the LundunHeightFog feature on the renderers.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

public static class LundunAtmosphereBuilder
{
    const string ShaderPath = "Assets/World/Shaders/LundunHeightFog.shader";
    const string MatPath = "Assets/World/Lighting/LundunHeightFog.mat";
    const string FeatureName = "LundunHeightFog";
    static readonly string[] Renderers = { "Assets/Settings/Mobile_Renderer.asset", "Assets/Settings/PC_Renderer.asset" };

    [MenuItem("Lundun/18  Atmosphere V3a (Grade + Fog + Glow)", false, 19)]
    public static void Build()
    {
        Shader sh = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (sh == null) { EditorUtility.DisplayDialog("Lundun", "Can't find " + ShaderPath + " — copy the Assets folder in first.", "OK"); return; }
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null) { mat = new Material(sh) { name = "LundunHeightFog" }; AssetDatabase.CreateAsset(mat, MatPath); }
        else mat.shader = sh;

        // the full-screen pass on each renderer
        List<ScriptableRendererFeature> features = new List<ScriptableRendererFeature>();
        foreach (string path in Renderers)
        {
            ScriptableRendererData data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (data == null) { Debug.LogWarning("[Lundun] Renderer not found: " + path); continue; }
            ScriptableRendererFeature f = null;
            foreach (ScriptableRendererFeature x in data.rendererFeatures) if (x != null && x.name == FeatureName) f = x;
            if (f == null)
            {
                f = ScriptableObject.CreateInstance<FullScreenPassRendererFeature>();
                f.name = FeatureName;
                AssetDatabase.AddObjectToAsset(f, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(f, out string guid, out long localId);
                SerializedObject dso = new SerializedObject(data);
                SerializedProperty list = dso.FindProperty("m_RendererFeatures");
                SerializedProperty map = dso.FindProperty("m_RendererFeatureMap");
                list.arraySize++; list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = f;
                map.arraySize++; map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
                dso.ApplyModifiedPropertiesWithoutUndo();
            }
            SerializedObject fso = new SerializedObject(f);
            SetInt(fso, "injectionPoint", (int)RenderPassEvent.BeforeRenderingPostProcessing);
            SetInt(fso, "requirements", (int)ScriptableRenderPassInput.Depth);
            SetBool(fso, "fetchColorBuffer", true);
            SetInt(fso, "passIndex", 0);
            SerializedProperty pm = fso.FindProperty("passMaterial");
            if (pm != null) pm.objectReferenceValue = mat; else Debug.LogWarning("[Lundun] Full Screen Pass: 'passMaterial' not found.");
            fso.ApplyModifiedPropertiesWithoutUndo();
            f.SetActive(true);
            EditorUtility.SetDirty(f);
            EditorUtility.SetDirty(data);
            features.Add(f);
        }

        // LundunAtmosphere next to LundunLighting
        LundunLighting lighting = Object.FindAnyObjectByType<LundunLighting>(FindObjectsInactive.Include);
        if (lighting == null) { EditorUtility.DisplayDialog("Lundun", "Couldn't find LundunLighting in the scene.", "OK"); return; }
        LundunAtmosphere atm = lighting.GetComponent<LundunAtmosphere>();
        if (atm == null) atm = lighting.gameObject.AddComponent<LundunAtmosphere>();
        Volume vol = (Volume)new SerializedObject(lighting).FindProperty("grade").objectReferenceValue;
        SerializedObject aso = new SerializedObject(atm);
        aso.FindProperty("volume").objectReferenceValue = vol;
        aso.FindProperty("fogMaterial").objectReferenceValue = mat;
        SerializedProperty fa = aso.FindProperty("fogFeatures");
        fa.arraySize = features.Count;
        for (int i = 0; i < features.Count; i++) fa.GetArrayElementAtIndex(i).objectReferenceValue = features[i];
        aso.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[Lundun] Atmosphere V3a: height fog on " + features.Count + " renderer(s), LundunAtmosphere on '" + lighting.name + "'" +
                  (vol == null ? " (no grade Volume found — colour grade skipped)" : "") + ". Press Cmd+S, then Play. " +
                  "Tip: the clock's +1h button steps through the looks; Preview Hour on LundunAtmosphere holds just the grade + fog.");
    }

    static void SetInt(SerializedObject so, string n, int v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.intValue = v; else Debug.LogWarning("[Lundun] Full Screen Pass: '" + n + "' not found."); }
    static void SetBool(SerializedObject so, string n, bool v) { SerializedProperty p = so.FindProperty(n); if (p != null) p.boolValue = v; else Debug.LogWarning("[Lundun] Full Screen Pass: '" + n + "' not found."); }
}
