using System;
using PixFootball.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11 WATCH+HUD: runs the watch probe in the editor at a fixed game view size. Launch WITHOUT -batchmode:
    /// Unity.exe -projectPath ... -executeMethod PixFootball.EditorTools.WatchRun.Run -pixWatchProbe -pixGvW 1920 -pixGvH 1080 -pixWatchOut dir ...
    /// </summary>
    [InitializeOnLoad]
    public static class WatchRun
    {
        static WatchRun() { WatchProbe.SetResolution = (w, h) => GameViewResolution.SetSize(w, h); }

        static int Arg(string name, int def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == name) { int v; if (int.TryParse(a[i + 1], out v)) return v; }
            return def;
        }

        public static void Run()
        {
            GameViewResolution.SetSize(Arg("-pixGvW", 1920), Arg("-pixGvH", 1080));
            EditorSceneManager.OpenScene(SceneBuilder.MenuScenePath);
            EditorApplication.isPlaying = true;
        }
    }
}
