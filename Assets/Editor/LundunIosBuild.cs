#if UNITY_IOS
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

/// G8.1a: after every iOS build, makes sure Info.plist has the photo-library "add" permission that the share sheet's
/// Save Image needs (without it iOS terminates the app when a player picks Save Image):
///   NSPhotoLibraryAddUsageDescription = "Save gameplay images to your Photos"
/// Idempotent: a plist key exists at most once and this only adds it when it's missing, so repeated builds (Replace or
/// Append) never duplicate it, and a value you've set yourself is left alone. Touches nothing else in the plist.
public static class LundunIosBuild
{
    public const string PhotoKey = "NSPhotoLibraryAddUsageDescription";
    public const string PhotoText = "Save gameplay images to your Photos";

    [PostProcessBuild(100)]
    public static void AddPhotoPermission(BuildTarget target, string buildPath)
    {
        if (target != BuildTarget.iOS) return;
        string plistPath = Path.Combine(buildPath, "Info.plist");
        if (!File.Exists(plistPath)) { Debug.LogWarning("[Lundun] Info.plist not found at " + plistPath + ": photo permission not added"); return; }
        PlistDocument plist = new PlistDocument();
        plist.ReadFromFile(plistPath);
        if (plist.root.values.ContainsKey(PhotoKey))
        {
            Debug.Log("[Lundun] Info.plist already has " + PhotoKey + " (left as it is)");
            return;
        }
        plist.root.SetString(PhotoKey, PhotoText);
        plist.WriteToFile(plistPath);
        Debug.Log("[Lundun] Info.plist: added " + PhotoKey + " = \"" + PhotoText + "\"");
    }
}
#endif
