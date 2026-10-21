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
        static readonly string[] SlimPrefabs = { "player_medium", "player_small", "player_large", "referee", "coach_a", "coach_b", "coach_c" };

        // clip keys used by AnimKit (see Docs/ARCHITECTURE.md v0.2 animation mapping); long BoNES takes are excluded by the length cap
        const float SlimClipMaxSeconds = 12f; const int SlimClipVariants = 6;
        static readonly System.Collections.Generic.HashSet<string> SlimClipKeys = new System.Collections.Generic.HashSet<string>
        {
            "stance_3pt", "dl_stance", "idle_alt", "stance_safety", "stance_halfback", "stance_2pt", "qb_stance_shotgun", "qb_stance_under",
            "ol_pass_set", "ol_pass_set_b", "dl_bull_rush", "dl_bull_rush_b", "dl_rush_swim", "dl_rush_swim_b", "dl_rush_spin", "dl_rush_spin_b", "dl_shed", "dl_shed_b",
            "qb_throw_bullet", "qb_throw_lob_short", "qb_throw_lob_long", "handoff", "handoff_b",
            "catch_high", "catch_low", "catch_run", "catch_jump", "catch_side", "interception",
            "tackle_wrap", "tackle_wrap_b", "tackle_success", "tackle_success_b", "tackle_dive", "tackle_dive_b", "tackle_arm", "tackle_arm_b", "qb_sack", "qb_sack_b", "fall",
            "celebrate_td_2", "celebrate_td_3"
        };

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
            // v0.2: the 22-player match needs the mocap clips the animation mapping uses (clips under SlimClipMaxSeconds, a few variants per key) and the ball
            var perKey = new System.Collections.Generic.Dictionary<string, int>();
            foreach (var c in full.clips)
            {
                if (c == null || c.clip == null || !(SlimClipKeys.Contains(c.key) || c.key.StartsWith("ref_") || c.key.StartsWith("coach_"))) continue;
                int n; perKey.TryGetValue(c.key, out n);
                if (n >= SlimClipVariants || c.clip.length > (c.key.Contains("stance") ? 50f : SlimClipMaxSeconds)) continue;
                perKey[c.key] = n + 1;
                slim.clips.Add(new ArtLibrary.ClipEntry { key = c.key, clip = c.clip, source = c.source, length = c.length, loop = c.loop });
            }
            foreach (var p in full.prefabs) if (p != null && p.key == "prop_footballball") slim.prefabs.Add(new ArtLibrary.PrefabEntry { key = p.key, prefab = p.prefab });
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

            PlayerSettings.enableFrameTimingStats = true;   // the autotest logs CPU / GPU frame times
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
            if (s.result == BuildResult.Succeeded)
            {
                // Unity writes a "BackUpThisFolder_ButDontShipItWithYourGame" folder (IL2CPP / burst debug data) next to the player: it is not part of the release
                foreach (string dir in Directory.GetDirectories(outDir, "*_BackUpThisFolder_ButDontShipItWithYourGame"))
                {
                    try { Directory.Delete(dir, true); Debug.Log("[PIX] removed " + Path.GetFileName(dir)); } catch (System.Exception e) { Debug.LogWarning("[PIX] could not remove " + dir + ": " + e.Message); }
                }
            }
            if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}

