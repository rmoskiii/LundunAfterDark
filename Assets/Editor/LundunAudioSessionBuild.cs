#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

/// G8.2a: after every iOS build, checks the generated Xcode project for the radio's native audio-session observer:
///   • AVFoundation.framework is linked to the UnityFramework target (added if missing; idempotent),
///   • LundunAudioSession.mm is in the project and compiled in UnityFramework's sources,
/// and logs which interruption path the plugin compiles (the iOS 27 lifecycle path needs the iOS 27 SDK; otherwise the
/// legacy path). LundunIosBuild.cs is not touched.
public static class LundunAudioSessionBuild
{
    const string Plugin = "LundunAudioSession.mm";

    [PostProcessBuild(110)]
    public static void Check(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;
        string projPath = PBXProject.GetPBXProjectPath(buildPath);
        if (!File.Exists(projPath)) { Debug.LogError("[Radio] Xcode project not found at " + projPath + ": AVFoundation link not checked"); return; }
        PBXProject proj = new PBXProject();
        proj.ReadFromFile(projPath);
        string fw = proj.GetUnityFrameworkTargetGuid();

        if (proj.ContainsFramework(fw, "AVFoundation.framework")) Debug.Log("[Radio] Xcode: AVFoundation.framework is linked to UnityFramework");
        else
        {
            proj.AddFrameworkToProject(fw, "AVFoundation.framework", false);
            proj.WriteToFile(projPath);
            Debug.Log("[Radio] Xcode: AVFoundation.framework was missing from UnityFramework: added");
        }

        string text = File.ReadAllText(projPath);
        bool inProject = text.Contains(Plugin);
        bool inSources = text.Contains(Plugin + " in Sources");
        if (inProject && inSources) Debug.Log("[Radio] Xcode: " + Plugin + " is compiled in the project's sources");
        else Debug.LogError("[Radio] Xcode: " + Plugin + (inProject ? " is in the project but not in a Sources phase" : " is missing from the project") +
                            ": the radio won't hear audio interruptions. Check Assets/Plugins/iOS/" + Plugin + " is enabled for iOS.");
        Debug.Log("[Radio] Xcode: the plugin compiles its iOS 27 lifecycle path only with the iOS 27 SDK (Xcode with __IPHONE_27_0); " +
                  "with an older SDK it uses the legacy interruption path on every iOS version. The device log line " +
                  "\"[Radio] native: observing: …\" shows which path is running.");
    }
}
#endif
