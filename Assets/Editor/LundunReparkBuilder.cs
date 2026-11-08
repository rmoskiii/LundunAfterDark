// Lundun → 23c  Re-park Brixtun + Kingsmere (F1 fix)
// Brixtun's and Kingsmere's parked cars were baked before the car models had their bodies set up, so only the wheels,
// number plates and indicators made it in (tyres along the kerbs, no cars), and the retail park's car file went
// missing. This bakes just their parked cars again from today's car models: same bays, same colours, new meshes,
// new colliders. Nothing else in either area is touched (no need to rebuild them or re-bake navigation).
// Afterwards: Cmd+S.
using System;
using LundunGreybox;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LundunReparkBuilder
{
    [MenuItem("Lundun/23c Re-park Brixtun + Kingsmere (F1 fix)", false, 29)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { EditorUtility.DisplayDialog("Lundun", "Open the Lundun scene first.", "OK"); return; }
        TextAsset json = AssetDatabase.LoadAssetAtPath<TextAsset>(LundunWorldFiles.BasePath);
        if (json == null) { EditorUtility.DisplayDialog("Lundun", "Can't find " + LundunWorldFiles.BasePath + ".", "OK"); return; }
        GameObject top = GameObject.Find("LundunExpansion");
        if (top == null) { EditorUtility.DisplayDialog("Lundun", "No LundunExpansion in the scene (Brixtun and Kingsmere aren't built).", "OK"); return; }

        string log = "";
        try
        {
            LundunWorldData data = LundunExpansionBuilder.Parse(json.text);
            EditorUtility.DisplayProgressBar("Lundun", "Re-parking Brixtun…", 0.2f);
            log += Area(top.transform, "Brixtun", "BRX", data, false);
            EditorUtility.DisplayProgressBar("Lundun", "Re-parking Kingsmere…", 0.6f);
            log += Area(top.transform, "Kingsmere", "KGM", data, true);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorUtility.DisplayDialog("Lundun", "Re-parking failed: " + e.Message + "\n\nThe Console has the details — copy them to Claude.", "OK");
            return;
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            LundunBrixtunBuilder.UseArea("Brixtun", "BRX");
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[Lundun] Re-parked: " + log + " Press Cmd+S.");
    }

    static string Area(Transform top, string name, string tag, LundunWorldData data, bool kingsmere)
    {
        Transform area = top.Find(name);
        if (area == null) return name + ": not built, skipped. ";
        GreyboxResult x = kingsmere ? Generator.BuildKingsmere(data) : Generator.BuildExpansion(data);
        if (x.meshes.Count == 0) return name + ": no roads in LundunExpansion.json, skipped. ";
        LundunWorldData xw = Generator.ExpansionWorld;
        DressingResult xd = Generator.Dress(xw, x);
        StreetResult xs = Generator.Streets(xw, x, xd);
        foreach (string old in new[] { "Parked", "RetailParked" })
        {
            Transform t = area.Find(old);
            if (t != null) Object.DestroyImmediate(t.gameObject);
        }
        LundunBrixtunBuilder.UseArea(name, tag);
        Vector4[] bays; Renderer[] parked;
        int cars = LundunBrixtunBuilder.BuildParked(area, xs, out bays, out parked);
        LundunExpansionArea rt = area.GetComponent<LundunExpansionArea>();
        if (rt != null) { rt.SetParked(bays, parked); EditorUtility.SetDirty(rt); }
        return name + ": " + cars + " parked cars, " + bays.Length + " bays for real ones. ";
    }
}
