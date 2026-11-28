using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// G8.2a: the radio's content pipeline (Editor only).
///   • Import rules for every audio file under Assets/Resources/Radio, applied automatically (nobody sets them by hand):
///       iOS override: MP3, quality 0.70 (the API's 0–1 scale; the Inspector shows 70), Compressed In Memory,
///                     Preload Audio Data off, sample rate preserved.
///       Default (Editor and other platforms): Vorbis 0.70, the same load type, preload off, sample rate preserved.
///       Importer: Load In Background on.
///     Read back and checked after import (Lundun ▸ Radio ▸ Check Imports): any mismatch is reported by file.
///   • The content manifest, Resources/Radio/RadioManifest.txt: every clip's resource path, length and
///     AssetDatabase.GetAssetDependencyHash (changes when the bytes or the import settings change). Regenerated without
///     loops: only audio changes under Resources/Radio trigger it (never the manifest, StreamerSafe.txt, the READ ME or
///     .meta files), it runs once per import batch (EditorApplication.delayCall, re-entry guarded), and it writes only
///     when the content differs.
///   • StreamerSafe.txt warnings (entries matching no file, LIB_ entries, case mismatches): warnings only.
///   • Before every build: the expected manifest is computed in memory and compared (never written): a stale manifest
///     stops the build. Release (non-development) builds also stop if anything under Resources/Radio is named or
///     foldered as placeholder or test material.
public class LundunRadioImport : AssetPostprocessor
{
    public const string RadioFolder = "Assets/Resources/Radio";
    public const string ManifestFile = RadioFolder + "/RadioManifest.txt";
    public const string StreamerSafeFile = RadioFolder + "/StreamerSafe.txt";
    public const float Quality = 0.70f;

    static bool scheduled, running;

    public static bool IsRadioAudio(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith(RadioFolder + "/", StringComparison.Ordinal)) return false;
        string ext = Path.GetExtension(assetPath).ToLowerInvariant();
        return ext == ".mp3" || ext == ".wav";
    }

    // ------------------------------------------------------------------ import rules
    void OnPreprocessAudio()
    {
        if (!assetPath.StartsWith(RadioFolder + "/", StringComparison.Ordinal)) return;
        AudioImporter imp = (AudioImporter)assetImporter;
        imp.loadInBackground = true;
        imp.defaultSampleSettings = new AudioImporterSampleSettings
        {
            loadType = AudioClipLoadType.CompressedInMemory, compressionFormat = AudioCompressionFormat.Vorbis, quality = Quality,
            sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate, preloadAudioData = false
        };
        bool ok = imp.SetOverrideSampleSettings(BuildTargetGroup.iOS, IosSettings());
        if (!ok) Debug.LogError("[Radio] import: Unity didn't accept the iOS override (MP3, quality 0.70) for " + assetPath + ": nothing else is chosen; please report this.");
    }

    public static AudioImporterSampleSettings IosSettings()
    {
        return new AudioImporterSampleSettings
        {
            loadType = AudioClipLoadType.CompressedInMemory, compressionFormat = AudioCompressionFormat.MP3, quality = Quality,
            sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate, preloadAudioData = false
        };
    }

    // ------------------------------------------------------------------ the manifest, without loops
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (Any(imported) || Any(deleted) || Any(moved) || Any(movedFrom)) Schedule();
    }

    static bool Any(string[] paths) { if (paths != null) foreach (string p in paths) if (IsRadioAudio(p)) return true; return false; }

    static void Schedule()
    {
        if (scheduled || running) return;                                  // one regeneration per batch; never re-entered
        scheduled = true;
        EditorApplication.delayCall += Regenerate;
    }

    static void Regenerate()
    {
        scheduled = false;
        if (running) return;
        running = true;
        try
        {
            int clips;
            string want = BuildManifest(out clips);
            string have = File.Exists(ManifestFile) ? File.ReadAllText(ManifestFile) : null;
            if (want != have)
            {
                File.WriteAllText(ManifestFile, want);
                AssetDatabase.ImportAsset(ManifestFile);                    // its own import is never a trigger
                Debug.Log("[Radio] manifest regenerated (" + clips + " clips)");
            }
            else Debug.Log("[Radio] manifest checked: unchanged, nothing written (" + clips + " clips)");
            CheckImports(false);
            CheckStreamerSafe();
        }
        finally { running = false; }
    }

    [MenuItem("Lundun/Radio/Rebuild Manifest")]
    public static void RebuildManifest() { if (!scheduled) Regenerate(); }

    static List<string> RadioAudioPaths()
    {
        List<string> l = new List<string>();
        if (!AssetDatabase.IsValidFolder(RadioFolder)) return l;
        foreach (string guid in AssetDatabase.FindAssets("t:AudioClip", new[] { RadioFolder }))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid);
            if (IsRadioAudio(p) && !l.Contains(p)) l.Add(p);
        }
        l.Sort(StringComparer.Ordinal);
        return l;
    }

    /// "Assets/Resources/Radio/Southside/Tracks/x.mp3" → "Radio/Southside/Tracks/x" (how Resources.Load names it).
    public static string ResourcePath(string assetPath)
    {
        string r = assetPath.Substring("Assets/Resources/".Length);
        return r.Substring(0, r.Length - Path.GetExtension(r).Length);
    }

    public static string BuildManifest(out int clips)
    {
        List<RadioManifest.Entry> entries = new List<RadioManifest.Entry>();
        foreach (string p in RadioAudioPaths())
        {
            AudioClip c = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
            entries.Add(new RadioManifest.Entry { path = ResourcePath(p), length = c != null ? c.length : 0.0, hash = AssetDatabase.GetAssetDependencyHash(p).ToString() });
        }
        clips = entries.Count;
        return RadioManifest.Format(entries);
    }

    // ------------------------------------------------------------------ read-back check
    [MenuItem("Lundun/Radio/Check Imports")]
    static void CheckImportsMenu() { CheckImports(true); }

    public static int CheckImports(bool verbose)
    {
        int bad = 0, n = 0;
        foreach (string p in RadioAudioPaths())
        {
            AudioImporter imp = AssetImporter.GetAtPath(p) as AudioImporter;
            if (imp == null) continue;
            n++;
            List<string> wrong = new List<string>();
            if (!imp.ContainsSampleSettingsOverride(BuildTargetGroup.iOS)) wrong.Add("no iOS override");
            else
            {
                AudioImporterSampleSettings s = imp.GetOverrideSampleSettings(BuildTargetGroup.iOS);
                if (s.compressionFormat != AudioCompressionFormat.MP3) wrong.Add("iOS format " + s.compressionFormat + " (want MP3)");
                if (Mathf.Abs(s.quality - Quality) > 0.005f) wrong.Add("iOS quality " + Mathf.RoundToInt(s.quality * 100f) + " (want 70)");
                if (s.loadType != AudioClipLoadType.CompressedInMemory) wrong.Add("iOS load type " + s.loadType);
                if (s.preloadAudioData) wrong.Add("iOS Preload Audio Data on");
                if (s.sampleRateSetting != AudioSampleRateSetting.PreserveSampleRate) wrong.Add("iOS sample rate " + s.sampleRateSetting);
            }
            AudioImporterSampleSettings d = imp.defaultSampleSettings;
            if (d.loadType != AudioClipLoadType.CompressedInMemory || d.preloadAudioData) wrong.Add("default load type / preload");
            if (!imp.loadInBackground) wrong.Add("Load In Background off");
            if (wrong.Count > 0) { bad++; Debug.LogError("[Radio] import check: " + p + ": " + string.Join(", ", wrong.ToArray())); }
            else if (verbose) Debug.Log("[Radio] import check: " + p + ": iOS MP3, quality 0.70 (70), Compressed In Memory, preload off, background on, sample rate preserved");
        }
        Debug.Log("[Radio] import check: " + (n - bad) + " of " + n + " radio clips as intended" + (bad > 0 ? " (" + bad + " reported above)" : ""));
        return bad;
    }

    // ------------------------------------------------------------------ StreamerSafe.txt warnings (never authorises anything)
    static void CheckStreamerSafe()
    {
        if (!File.Exists(StreamerSafeFile)) { Debug.LogWarning("[Radio] StreamerSafe.txt is missing: STREAMER-SAFE plays nothing"); return; }
        HashSet<string> list = RadioStreamerSafe.Parse(File.ReadAllText(StreamerSafeFile));
        HashSet<string> real = new HashSet<string>(StringComparer.Ordinal);
        Dictionary<string, string> lower = new Dictionary<string, string>();
        foreach (string p in RadioAudioPaths()) { string r = ResourcePath(p); real.Add(r); lower[r.ToLowerInvariant()] = r; }
        foreach (string e in list)
        {
            if (RadioStreamerSafe.IsLibrary(e)) Debug.LogWarning("[Radio] StreamerSafe.txt: \"" + e + "\" is a LIB_ file: never played in STREAMER-SAFE, even when listed");
            if (!real.Contains(e))
            {
                string near;
                if (lower.TryGetValue(e.ToLowerInvariant(), out near)) Debug.LogWarning("[Radio] StreamerSafe.txt: \"" + e + "\" doesn't match (case differs from \"" + near + "\"): not authorised");
                else Debug.LogWarning("[Radio] StreamerSafe.txt: \"" + e + "\" matches no file");
            }
        }
    }

    // ------------------------------------------------------------------ before every build
    class BuildCheck : IPreprocessBuildWithReport
    {
        public int callbackOrder { get { return 0; } }

        public void OnPreprocessBuild(BuildReport report)
        {
            int clips;
            string want = BuildManifest(out clips);                         // computed in memory; never written here
            string have = File.Exists(ManifestFile) ? File.ReadAllText(ManifestFile) : null;
            if (want != have)
                throw new BuildFailedException("[Radio] RadioManifest.txt is out of date (" + clips + " radio clips). Run Lundun ▸ Radio ▸ Rebuild Manifest, then build again.");
            Debug.Log("[Radio] build check: manifest current (" + clips + " clips)");

            bool development = (report.summary.options & BuildOptions.Development) != 0;
            if (development) { Debug.Log("[Radio] build check: development build: placeholders are compiled in (Editor-style testing)"); return; }
            List<string> bad = new List<string>();
            if (AssetDatabase.IsValidFolder(RadioFolder))
                foreach (string guid in AssetDatabase.FindAssets("", new[] { RadioFolder }))
                {
                    string p = AssetDatabase.GUIDToAssetPath(guid);
                    if (IsPlaceholderOrTest(p)) bad.Add(p);
                }
            if (bad.Count > 0)
                throw new BuildFailedException("[Radio] release build stopped: placeholder or test material under Resources/Radio: " + string.Join(", ", bad.ToArray()));
            Debug.Log("[Radio] build check: release build: placeholder code compiled out (RadioPlaceholders, DispatchPlaceholder, captions: UNITY_EDITOR || DEVELOPMENT_BUILD only); no placeholder or test files under Resources/Radio");
        }
    }

    public static bool IsPlaceholderOrTest(string assetPath)
    {
        string rel = assetPath.StartsWith(RadioFolder + "/", StringComparison.Ordinal) ? assetPath.Substring(RadioFolder.Length + 1) : assetPath;
        foreach (string seg in rel.Split('/'))
        {
            string s = seg.ToLowerInvariant();
            if (s == "placeholder" || s == "placeholders" || s == "test" || s == "tests") return true;
            if (s.StartsWith("placeholder_", StringComparison.Ordinal) || s.StartsWith("test_", StringComparison.Ordinal)) return true;
        }
        return false;
    }
}