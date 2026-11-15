// Lundun → 31  Look V2b (Light + Grade)
// Re-tunes how the city is lit, at every hour, on top of what's already built (nothing is rebuilt, no navigation bake):
//   • the four lighting presets (Night, Dawn, Day, Dusk) get the V2b values: night is properly dark (a low blue fill
//     instead of a bright one), so lamp pools, lit windows and shop signs carry the street; more contrast and colour
//     at every hour; fog further off
//   • the colour grades and ground fog (LundunAtmosphere): clearer air, a stronger teal / sodium split at night
//   • shop signs and lit shop fronts follow the clock: dim by day, bright after dark
//   • lamp pools, lamp heads and halos a little stronger; wet roads shinier, with more lamp reflections
//   • road markings are worn paint instead of fresh white; a few stars on clear nights (the sky shader)
// Rain itself is gentler on the picture too (that part is in LundunWeather's code, not here).
// Lundun → 31b puts every value back exactly as it was before. Afterwards: Cmd+S, then Play.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LundunLookBuilder
{
    const string PresetDir = "Assets/World/Lighting";
    static readonly string[] Names = { "Night", "Dawn", "Day", "Dusk" };
    static readonly string[] SignMats =
    {
        "Assets/World/Dressing/Dress_Glow.mat", "Assets/World/Expansions/Brixtun/Dress_Glow_Brixtun.mat", "Assets/World/Expansions/Kingsmere/Dress_Glow_Kingsmere.mat",
    };

    [MenuItem("Lundun/31  Look V2b (Light + Grade)", false, 37)]
    public static void Build() { Apply(true); }

    [MenuItem("Lundun/31b  Look as before V2b", false, 38)]
    public static void Undo() { Apply(false); }

    static void Apply(bool on)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { EditorUtility.DisplayDialog("Lundun", "Open the Lundun scene first.", "OK"); return; }
        LundunLighting lighting = Object.FindAnyObjectByType<LundunLighting>();
        if (lighting == null) { EditorUtility.DisplayDialog("Lundun", "No LundunLighting in the scene (run Lundun → 1 first).", "OK"); return; }

        // the four presets
        int presets = 0;
        foreach (string n in Names)
        {
            LundunLightingPreset p = AssetDatabase.LoadAssetAtPath<LundunLightingPreset>(PresetDir + "/" + n + ".asset");
            if (p == null) continue;
            p.look = on ? LightingLook.V2b(n) : LightingLook.Default(n);
            EditorUtility.SetDirty(p);
            presets++;
        }

        // grades and ground fog
        LundunAtmosphere atmosphere = Object.FindAnyObjectByType<LundunAtmosphere>();
        if (atmosphere != null) { atmosphere.UseV2bGrades(on); EditorUtility.SetDirty(atmosphere); }

        // lamps and signs
        SerializedObject so = new SerializedObject(lighting);
        so.FindProperty("lightPoolStrength").floatValue = on ? 0.8f : 0.7f;
        so.FindProperty("lampGlowStrength").floatValue = on ? 3.2f : 2.5f;
        SerializedProperty signs = so.FindProperty("signMaterials");
        int signCount = 0;
        if (signs != null)
        {
            signs.arraySize = 0;
            foreach (string path in SignMats)
            {
                Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null) continue;
                if (on) { signs.arraySize++; signs.GetArrayElementAtIndex(signs.arraySize - 1).objectReferenceValue = m; signCount++; }
                else { m.SetColor("_EmissionColor", Color.white * 1.5f); EditorUtility.SetDirty(m); }     // as Lundun → 13 makes them
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(lighting);

        // wet roads
        LundunWeather weather = Object.FindAnyObjectByType<LundunWeather>();
        if (weather != null)
        {
            SerializedObject ws = new SerializedObject(weather);
            ws.FindProperty("streakCount").intValue = on ? 24 : 16;
            ws.FindProperty("streakStrength").floatValue = on ? 0.8f : 0.6f;
            SerializedProperty surf = ws.FindProperty("surfaces");
            for (int i = 0; surf != null && i < surf.arraySize; i++)
            {
                SerializedProperty e = surf.GetArrayElementAtIndex(i);
                Material m = e.FindPropertyRelative("material").objectReferenceValue as Material;
                if (m == null || m.name != "Road") continue;
                e.FindPropertyRelative("wetSmoothness").floatValue = on ? 0.9f : 0.86f;
                e.FindPropertyRelative("darken").floatValue = on ? 0.3f : 0.38f;
            }
            ws.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(weather);
        }

        // road markings: worn paint, not fresh white (they glared at night)
        Material marking = AssetDatabase.LoadAssetAtPath<Material>("Assets/World/Materials/Marking.mat");
        if (marking != null)
        {
            Color mc = on ? new Color(0.74f, 0.74f, 0.70f) : new Color(0.92f, 0.92f, 0.88f);
            if (marking.HasProperty("_BaseColor")) marking.SetColor("_BaseColor", mc);
            if (marking.HasProperty("_Color")) marking.SetColor("_Color", mc);
            EditorUtility.SetDirty(marking);
        }

        // show it now (the lighting object follows its preset in the editor)
        if (lighting.preset != null) lighting.Apply(lighting.preset.look);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[Lundun] " + (on ? "Look V2b applied" : "Look put back as before V2b") + ": " + presets + " lighting presets, " +
                  (atmosphere != null ? "grades and ground fog, " : "(no LundunAtmosphere: run Lundun → 18) ") +
                  (on ? signCount + " sign materials following the clock, " : "signs always lit again, ") +
                  (weather != null ? "wet roads" : "(no LundunWeather: run Lundun → 19)") + ". Press Cmd+S, then Play.");
    }
}
