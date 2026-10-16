// Lundun → 16  HUD Style (V2a)
// Adds LundunHud to your HUD Canvas and gives it the Rajdhani font (Assets/UI/Fonts, SIL Open Font License).
// LundunHud restyles the HUD when the game starts: nothing in the scene is rebuilt or rewired, so every button keeps
// doing exactly what it does now. Remove the LundunHud component to get the old look back.
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class LundunHudBuilder
{
    const string FontPath = "Assets/UI/Fonts/Rajdhani-Bold.ttf";

    [MenuItem("Lundun/16  HUD Style (V2a)", false, 17)]
    public static void Build()
    {
        Font font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
        if (font == null) { EditorUtility.DisplayDialog("Lundun", "Can't find " + FontPath + " — copy the Assets folder from the zip in first.", "OK"); return; }

        // the main HUD canvas: the one holding the on-foot controls
        Canvas hud = null;
        foreach (Canvas c in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (c.isRootCanvas && c.transform.Find("OnFootControls") != null) { hud = c; break; }
        if (hud == null) { EditorUtility.DisplayDialog("Lundun", "Couldn't find the HUD Canvas (the one with OnFootControls).", "OK"); return; }

        LundunHud h = Object.FindAnyObjectByType<LundunHud>(FindObjectsInactive.Include);
        if (h == null) h = hud.gameObject.AddComponent<LundunHud>();
        SerializedObject so = new SerializedObject(h);
        so.FindProperty("font").objectReferenceValue = font;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("[Lundun] HUD style added to '" + hud.name + "' (font: Rajdhani Bold). Press Cmd+S, then Play. " +
                  "Tap the clock 3 times to show/hide the performance numbers.");
    }
}
