// Keeps everyone the same height, automatically: so an oversized pedestrian (Ped_Casual_3) can never come back.
// Runs the Lundun → Fix People Sizes check (quietly) every time you press Play and before every build.
// If a prefab had lost its fix (e.g. re-imported, rebuilt, or the change never got committed) it's put right and
// the Console says which file changed, so it can be committed. Costs a fraction of a second; changes nothing otherwise.
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

[InitializeOnLoad]
public static class LundunPeopleSizesGuard
{
    static LundunPeopleSizesGuard()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode) Check();
        };
    }

    public static void Check()
    {
        try { LundunPeopleSizes.Fix(true); }
        catch (System.Exception e) { UnityEngine.Debug.LogWarning("[Lundun] People size check skipped: " + e.Message); }
    }
}

public class LundunPeopleSizesBuildCheck : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;
    public void OnPreprocessBuild(BuildReport report) { LundunPeopleSizesGuard.Check(); }
}
