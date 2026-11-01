// Lundun → 8  Build Population Manager: the "life bubble" that spawns people around the player.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LundunPopulationBuilder
{
    [MenuItem("Lundun/8  Build Population Manager", false, 8)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        if (Root("LundunNavigation") == null) { Dialog("Run Lundun → 6  Build Full Map + GPS first (people use its road network)."); return; }

        // every prefab with an NPCBrain becomes a possible pedestrian (more arrive in R4)
        List<GameObject> prefabs = new List<GameObject>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
        {
            GameObject p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (p != null && p.GetComponent<NPCBrain>() != null) prefabs.Add(p);
        }
        if (prefabs.Count == 0) { Dialog("No NPC prefab found in Assets/Prefabs (needs an NPCBrain)."); return; }

        Transform old = Root("LundunPopulation");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        GameObject go = new GameObject("LundunPopulation");
        LundunPopulation pop = go.AddComponent<LundunPopulation>();
        SerializedObject so = new SerializedObject(pop);
        SerializedProperty arr = so.FindProperty("pedestrianPrefabs");
        arr.arraySize = prefabs.Count;
        for (int i = 0; i < prefabs.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = prefabs[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = go;
        string names = ""; foreach (GameObject p in prefabs) names += p.name + " ";
        Debug.Log("[Lundun] Population manager built. Pedestrian prefabs: " + names + "— Budget Medium. Press Play and walk/drive around.");
    }

    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }

    static Transform Root(string name)
    {
        foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects()) if (g.name == name) return g.transform;
        return null;
    }
}
