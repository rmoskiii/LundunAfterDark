// Lundun → 7  Build Day-Night Clock: adds the game clock, the Dusk preset and the HUD clock under the minimap.
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LundunClockBuilder
{
    const string LightingDir = "Assets/World/Lighting";

    [MenuItem("Lundun/7  Build Day-Night Clock", false, 7)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        Transform lt = Root("LundunLighting");
        LundunLighting lighting = lt != null ? lt.GetComponent<LundunLighting>() : null;
        if (lighting == null) { Dialog("No lighting rig yet. Run Lundun → 1  Build Greybox World."); return; }
        Transform ct = Root("Canvas");
        Transform hud = ct != null ? FindDeep(ct, "TopLeftHUD") : null;
        if (hud == null) { Dialog("Build the minimap first: Lundun → 5  Build Minimap."); return; }

        LundunLightingPreset night = Preset("Night"), dawn = Preset("Dawn"), day = Preset("Day"), dusk = Preset("Dusk");

        // HUD clock under the health bar
        Transform oldText = hud.Find("Clock");
        if (oldText != null) Object.DestroyImmediate(oldText.gameObject);
        GameObject tg = new GameObject("Clock", typeof(RectTransform));
        tg.layer = LayerMask.NameToLayer("UI");
        tg.transform.SetParent(hud, false);
        RectTransform rt = (RectTransform)tg.transform;
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1);
        rt.anchoredPosition = new Vector2(0, -312); rt.sizeDelta = new Vector2(100, 36);
        TextMeshProUGUI text = tg.AddComponent<TextMeshProUGUI>();
        text.text = "23:00"; text.fontSize = 30; text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.MidlineLeft; text.color = new Color(1f, 1f, 1f, 0.9f); text.raycastTarget = false;
        RectTransform hudRt = (RectTransform)hud;
        hudRt.sizeDelta = new Vector2(hudRt.sizeDelta.x, 352);

        Transform old = Root("LundunClock");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        GameObject go = new GameObject("LundunClock");
        LundunClock clock = go.AddComponent<LundunClock>();
        SerializedObject so = new SerializedObject(clock);
        so.FindProperty("lighting").objectReferenceValue = lighting;
        so.FindProperty("night").objectReferenceValue = night;
        so.FindProperty("dawn").objectReferenceValue = dawn;
        so.FindProperty("day").objectReferenceValue = day;
        so.FindProperty("dusk").objectReferenceValue = dusk;
        so.FindProperty("clockText").objectReferenceValue = text;
        so.FindProperty("buttonSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/World/Generated/MinimapCircle.png");
        so.ApplyModifiedPropertiesWithoutUndo();

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = go;
        Debug.Log("[Lundun] Day-night clock built. First session starts 23:00; night 30 game min per real min, day 60. Time carries over between sessions. Use +1h / x12 / x60 on the HUD to test.");
    }

    static LundunLightingPreset Preset(string which)
    {
        if (!AssetDatabase.IsValidFolder(LightingDir)) AssetDatabase.CreateFolder("Assets/World", "Lighting");
        string path = LightingDir + "/" + which + ".asset";
        LundunLightingPreset p = AssetDatabase.LoadAssetAtPath<LundunLightingPreset>(path);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<LundunLightingPreset>();
            p.look = LightingLook.Default(which);
            AssetDatabase.CreateAsset(p, path);
        }
        return p;
    }

    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }

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
}
