using System;
using System.Collections;
using System.IO;
using System.Text;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// QA probe for the real game path (run with -pixGlassProbe [-pixGlassProbeOut dir] [-pixUiTheme glass] [-pixTier 3], from the editor via PlayModeRunner.RunAutoTest or from a player build):
    /// in the menu scene it captures what the player sees (real Screen Space Overlay canvases over the real blur), measures the per-frame cost with the blur pass on and with the static frosted fallback
    /// (interleaved, several rounds), captures the fallback look and the team select, writes glassprobe.txt and quits. Does nothing unless the flag is on the command line.
    /// </summary>
    public sealed class GlassProbe : MonoBehaviour
    {
        static bool Flag(string n) { foreach (string a in Environment.GetCommandLineArgs()) if (a == n) return true; return false; }
        static string Arg(string n) { var a = Environment.GetCommandLineArgs(); for (int i = 0; i < a.Length - 1; i++) if (a[i] == n) return a[i + 1]; return null; }

        string _out; readonly StringBuilder _log = new StringBuilder();
        readonly FrameTiming[] _ft = new FrameTiming[1];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!Flag("-pixGlassProbe")) return;
            var go = new GameObject("PixGlassProbe");
            DontDestroyOnLoad(go);
            go.AddComponent<GlassProbe>();
        }

        void Log(string s) { _log.AppendLine(s); Debug.Log("[GlassProbe] " + s); }

        IEnumerator Start()
        {
            _out = Arg("-pixGlassProbeOut");
            if (string.IsNullOrEmpty(_out)) _out = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "glassprobe"));
            Directory.CreateDirectory(_out);
            int tier; if (int.TryParse(Arg("-pixTier"), out tier)) QualityTiers.Override(tier, -1f);
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            yield return new WaitForSeconds(4f);
            Log("theme=" + UiTheme.Current + " tier=" + QualityTiers.Current + " screen=" + Screen.width + "x" + Screen.height + " gpu=" + SystemInfo.graphicsDeviceName + " api=" + SystemInfo.graphicsDeviceType);
            Log("wantBlur=" + GlassBackdrop.WantBlur + " passRuns=" + GlassBackdrop.PassRuns);
            yield return Shot("real_menu");
            for (int round = 0; round < 3; round++)
            {
                GlassBackdrop.ForceFallback = false; int r0 = GlassBackdrop.PassRuns;
                yield return Measure("blur on ", round);
                Log("  pass runs during the round: " + (GlassBackdrop.PassRuns - r0));
                GlassBackdrop.ForceFallback = true;
                yield return Measure("blur off", round);
            }
            yield return Shot("real_menu_fallback");
            GlassBackdrop.ForceFallback = false;
            foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
                if (mb != null && mb.GetType().Name == "MainMenuController") { mb.SendMessage("OpenTeamSelect", SendMessageOptions.DontRequireReceiver); break; }
            yield return new WaitForSeconds(1.5f);
            yield return Shot("real_teamselect");
            Finish();
        }

        IEnumerator Measure(string label, int round)
        {
            for (int i = 0; i < 90; i++) yield return null;   // settle
            double dt = 0, cpu = 0, gpu = 0; int n = 0, g = 0;
            for (int i = 0; i < 240; i++)
            {
                yield return null;
                dt += Time.unscaledDeltaTime; n++;
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, _ft) > 0 && _ft[0].gpuFrameTime > 0) { cpu += _ft[0].cpuFrameTime; gpu += _ft[0].gpuFrameTime; g++; }
            }
            Log(label + " round " + round + ": frame " + (dt / n * 1000.0).ToString("F2") + " ms (" + (n / dt).ToString("F0") + " fps)" + (g > 0 ? ", gpu " + (gpu / g).ToString("F2") + " ms, cpu " + (cpu / g).ToString("F2") + " ms" : ", no frame timings"));
        }

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_out, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            Log("screenshot " + name + " " + Screen.width + "x" + Screen.height);
        }

        void Finish()
        {
            try { File.WriteAllText(Path.Combine(_out, "glassprobe.txt"), _log.ToString()); } catch (Exception e) { Debug.LogError(e); }
            Application.Quit(0);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
            UnityEditor.EditorApplication.delayCall += () => UnityEditor.EditorApplication.Exit(0);
#endif
        }
    }
}
