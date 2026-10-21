using System.IO;
using System.Linq;
using PixFootball.Art;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>Headless Windows build: Unity -batchmode -executeMethod PixFootball.EditorTools.BuildScript.BuildWindows -quit
    /// Optional arg: -pixVersion 0.2.0 (sets bundleVersion before building).
    /// The full ArtLibrary (700 clips, 250 audio clips, ...) is swapped for a slim one in Resources for the duration of the build so the player only contains what v0.1 uses.</summary>
    public static class BuildScript
    {
        const string ResLib = "Assets/_Licensed/Resources/ArtLibrary.asset";
        static readonly string BackupDir = "Library/PIXBackup";
        static readonly string[] SlimPrefabs = { "player_medium", "player_small", "player_large" };

        static bool _swapped;

        static void SwapInSlimLibrary()
        {
            ArtLibrary.ResetCache();
            var full = AssetDatabase.LoadAssetAtPath<ArtLibrary>(ArtLibrary.EditorAssetPath);
            if (full == null || !File.Exists(ResLib)) return;
            Directory.CreateDirectory(BackupDir);
            File.Copy(ResLib, BackupDir + "/ArtLibrary.asset", true);
            File.Copy(ResLib + ".meta", BackupDir + "/ArtLibrary.asset.meta", true);
            AssetDatabase.DeleteAsset(ResLib);
            var slim = ScriptableObject.CreateInstance<ArtLibrary>();
            slim.playerAvatar = full.playerAvatar;
            foreach (var p in full.prefabs) if (p != null && SlimPrefabs.Contains(p.key)) slim.prefabs.Add(new ArtLibrary.PrefabEntry { key = p.key, prefab = p.prefab });
            foreach (var a in full.audio) if (a != null && (a.key == "crowd_ambience" || a.key == "crowd_cheer") && slim.audio.Count < 2) slim.audio.Add(new ArtLibrary.AudioEntry { key = a.key, clip = a.clip });
            AssetDatabase.CreateAsset(slim, ResLib);
            AssetDatabase.SaveAssets();
            _swapped = true;
            Debug.Log("[PIX] slim ArtLibrary written for the player build (" + slim.prefabs.Count + " prefabs)");
        }

        static void RestoreLibrary()
        {
            if (!_swapped && !File.Exists(BackupDir + "/ArtLibrary.asset")) return;
            AssetDatabase.DeleteAsset(ResLib);
            if (File.Exists(BackupDir + "/ArtLibrary.asset"))
            {
                File.Copy(BackupDir + "/ArtLibrary.asset", ResLib, true);
                File.Copy(BackupDir + "/ArtLibrary.asset.meta", ResLib + ".meta", true);
                File.Delete(BackupDir + "/ArtLibrary.asset"); File.Delete(BackupDir + "/ArtLibrary.asset.meta");
            }
            AssetDatabase.Refresh();
            ArtLibrary.ResetCache();
            _swapped = false;
            Debug.Log("[PIX] full ArtLibrary restored");
        }

        [MenuItem("PIX Football/Build/Windows")]
        public static void BuildWindows()
        {
            var args = System.Environment.GetCommandLineArgs();
            int vi = System.Array.IndexOf(args, "-pixVersion");
            if (vi >= 0 && vi + 1 < args.Length) PlayerSettings.bundleVersion = args[vi + 1];

            string version = PlayerSettings.bundleVersion;
            string outDir = Path.GetFullPath($"Builds/PIXFootball_v{version}_Windows");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);

            var scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
            if (scenes.Length == 0) throw new System.Exception("[PIX] No scenes in Build Settings");

            PlayerPrefabBuilder.RegisterDiffusionProfiles();
            BuildReport report;
            try
            {
                SwapInSlimLibrary();
                report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = scenes,
                    locationPathName = Path.Combine(outDir, "PIXFootball.exe"),
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None,
                });
            }
            finally { RestoreLibrary(); }

            var s = report.summary;
            Debug.Log($"[PIX] Build {s.result}: {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalTime} -> {outDir}");
            if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
