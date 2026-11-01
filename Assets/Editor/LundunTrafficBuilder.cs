// Lundun → 9  Build Traffic: makes a traffic-car prefab from a copy of the Kestrel and hands it to the population manager.
// (R3 swaps in proper car models; any prefab with a CarController + Rigidbody can go in the Car Prefabs list.)
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public static class LundunTrafficBuilder
{
    const string PrefabPath = "Assets/Prefabs/TrafficCar.prefab";

    [MenuItem("Lundun/9  Build Traffic", false, 9)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "Lundun") { Dialog("Open the Lundun scene first."); return; }
        Transform popT = Root("LundunPopulation");
        LundunPopulation pop = popT != null ? popT.GetComponent<LundunPopulation>() : null;
        if (pop == null) { Dialog("Run Lundun → 8  Build Population Manager first."); return; }
        Transform car = Root("Car");
        if (car == null || car.GetComponent<CarController>() == null) { Dialog("Can't find your car (a root object called 'Car' with a CarController)."); return; }

        // copy the Kestrel, strip anything that's only for the player's car, save as a prefab
        GameObject copy = Object.Instantiate(car.gameObject);
        copy.name = "TrafficCar";
        copy.transform.position = Vector3.zero;
        copy.transform.rotation = Quaternion.identity;
        foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (mb == null) continue;
            string n = mb.GetType().Name;
            if (n == "SuperSUVKit" || n == "CarLivery" || n == "PoliceLivery") Object.DestroyImmediate(mb);   // editor-only body tools
        }
        if (copy.GetComponent<LundunTrafficCar>() == null) copy.AddComponent<LundunTrafficCar>();
        CarController cc = copy.GetComponent<CarController>();
        if (cc != null) cc.IsDriven = false;
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(copy, PrefabPath);
        Object.DestroyImmediate(copy);
        if (prefab == null) { Dialog("Couldn't save " + PrefabPath); return; }

        SerializedObject so = new SerializedObject(pop);
        SerializedProperty arr = so.FindProperty("carPrefabs");
        arr.arraySize = 1;
        arr.GetArrayElementAtIndex(0).objectReferenceValue = prefab;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeObject = prefab;
        Debug.Log("[Lundun] Traffic built: " + PrefabPath + " (a copy of your Kestrel, repainted per car). Press Play.");
    }

    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }

    static Transform Root(string name)
    {
        foreach (GameObject g in SceneManager.GetActiveScene().GetRootGameObjects()) if (g.name == name) return g.transform;
        return null;
    }
}
