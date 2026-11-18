// Lundun → Tidy: Remove Dev Beacons (Phase 1)
//   Takes the nine tall coloured greybox columns (World/Beacons/Beacon_*: landmarks, the old M1–M4 spots, hospital,
//   police, safehouse) out of the open scene. Menu 1 no longer makes them, so this is only for a scene built before
//   Phase 1; running it again does nothing. Kept: the GPS waypoint beam, mission markers and glows, every prop.
//   Undo works. Then Cmd+S.
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LundunWorldTidy
{
    [MenuItem("Lundun/Tidy: Remove Dev Beacons (Phase 1)", false, 48)]
    public static void RemoveBeacons()
    {
        GameObject world = GameObject.Find("World");
        if (world == null) { Dialog("Can't find the World object. Open the Lundun scene first."); return; }

        List<GameObject> gone = new List<GameObject>();
        foreach (Transform t in world.GetComponentsInChildren<Transform>(true))
        {
            if (t == null || t == world.transform) continue;
            if (t.name == "Beacons" && t.parent == world.transform) { gone.Add(t.gameObject); continue; }
            // a stray one anywhere else in the world: only a bare cylinder named Beacon_ (never the waypoint beam)
            if (t.name.StartsWith("Beacon_") && IsBareCylinder(t.gameObject)) gone.Add(t.gameObject);
        }

        int columns = 0;
        foreach (GameObject g in gone)
        {
            if (g == null) continue;   // already went with its parent
            columns += g.name == "Beacons" ? g.transform.childCount : 1;
            Undo.DestroyObjectImmediate(g);
        }

        if (columns == 0 && gone.Count == 0) { Dialog("No development beacons in this scene: nothing to do."); return; }
        EditorSceneManager.MarkSceneDirty(world.scene);
        Debug.Log("[Lundun] Phase 1: removed " + columns + " development beacon column(s). Cmd+S to save the scene.");
        Dialog("Removed " + columns + " development beacon column(s).\n\nNow press Cmd+S to save the scene.");
    }

    /// Just a cylinder mesh and its renderer (plus the navmesh ignore): nothing that does anything at runtime.
    static bool IsBareCylinder(GameObject g)
    {
        MeshFilter mf = g.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null || mf.sharedMesh.name != "Cylinder") return false;
        foreach (Component c in g.GetComponents<Component>())
        {
            if (c == null || c is Transform || c is MeshFilter || c is MeshRenderer) continue;
            if (c.GetType().Name == "NavMeshModifier") continue;
            return false;
        }
        return g.transform.childCount == 0;
    }

    static void Dialog(string m) { EditorUtility.DisplayDialog("Lundun", m, "OK"); }
}
