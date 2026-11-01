// Lundun → 19  Weather V3b (London rain + wet roads)
//   • makes the rain material (Assets/World/Weather/LundunRain.mat: URP Particles/Unlit, see-through)
//   • adds LundunWeather to the LundunLighting object, and tells it which surfaces get wet:
//     Road (very shiny), Pavement, Marking and Concrete (less), plus the street-dressing atlas a touch
// The weather only changes those materials while playing and puts them back when you stop.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LundunWeatherBuilder
{
    const string RainMat = "Assets/World/Weather/LundunRain.mat";

    struct Wet { public string path; public float smooth, darken; public Wet(string p, float s, float d) { path = p; smooth = s; darken = d; } }
    static readonly Wet[] Surfaces =
    {
        new Wet("Assets/World/Materials/Road.mat", 0.86f, 0.38f),
        new Wet("Assets/World/Materials/Pavement.mat", 0.72f, 0.3f),
        new Wet("Assets/World/Materials/Marking.mat", 0.6f, 0.15f),
        new Wet("Assets/World/Materials/Concrete.mat", 0.6f, 0.25f),
        new Wet("Assets/World/Dressing/Dress_Solid.mat", 0.45f, 0.12f),
        new Wet("Assets/World/Streets/Street_Decals.mat", 0.7f, 0.25f),          // L2 (skipped if you haven't run Lundun → 22)
        new Wet("Assets/World/Streets/Street_Setts.mat", 0.8f, 0.35f),
    };

    [MenuItem("Lundun/19  Weather V3b (London rain + wet roads)", false, 20)]
    public static void Build()
    {
        if (!AssetDatabase.IsValidFolder("Assets/World/Weather")) AssetDatabase.CreateFolder("Assets/World", "Weather");
        Material rain = AssetDatabase.LoadAssetAtPath<Material>(RainMat);
        if (rain == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) { EditorUtility.DisplayDialog("Lundun", "Can't find the URP Particles/Unlit shader.", "OK"); return; }
            rain = new Material(sh) { name = "LundunRain" };
            AssetDatabase.CreateAsset(rain, RainMat);
        }
        // see-through, soft, no depth writing
        rain.SetFloat("_Surface", 1f);
        rain.SetFloat("_Blend", 0f);
        rain.SetFloat("_ZWrite", 0f);
        rain.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        rain.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        rain.SetColor("_BaseColor", Color.white);
        rain.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        rain.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        rain.SetOverrideTag("RenderType", "Transparent");
        EditorUtility.SetDirty(rain);

        LundunLighting lighting = Object.FindAnyObjectByType<LundunLighting>(FindObjectsInactive.Include);
        if (lighting == null) { EditorUtility.DisplayDialog("Lundun", "Couldn't find LundunLighting in the scene.", "OK"); return; }
        LundunWeather w = lighting.GetComponent<LundunWeather>();
        if (w == null) w = lighting.gameObject.AddComponent<LundunWeather>();

        SerializedObject so = new SerializedObject(w);
        so.FindProperty("rainMaterial").objectReferenceValue = rain;
        SerializedProperty arr = so.FindProperty("surfaces");
        List<string> found = new List<string>(), missing = new List<string>();
        arr.arraySize = 0;
        foreach (Wet s in Surfaces)
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(s.path);
            if (m == null) { missing.Add(System.IO.Path.GetFileNameWithoutExtension(s.path)); continue; }
            arr.arraySize++;
            SerializedProperty e = arr.GetArrayElementAtIndex(arr.arraySize - 1);
            e.FindPropertyRelative("material").objectReferenceValue = m;
            e.FindPropertyRelative("wetSmoothness").floatValue = s.smooth;
            e.FindPropertyRelative("darken").floatValue = s.darken;
            found.Add(m.name);
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();
        Debug.Log("[Lundun] Weather added to '" + lighting.name + "'. Wet surfaces: " + string.Join(", ", found.ToArray()) +
                  (missing.Count > 0 ? "  (not found, skipped: " + string.Join(", ", missing.ToArray()) + ")" : "") +
                  ". Press Cmd+S, then Play — the button next to the clock cycles the weather.");
    }
}
