using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>Headless Windows build: Unity -batchmode -executeMethod PixFootball.EditorTools.BuildScript.BuildWindows -quit
    /// Optional arg: -pixVersion 0.2.0 (sets bundleVersion before building).</summary>
    public static class BuildScript
    {
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

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(outDir, "PIXFootball.exe"),
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
            });

            var s = report.summary;
            Debug.Log($"[PIX] Build {s.result}: {s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors, {s.totalTime} -> {outDir}");
            if (s.result != BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
