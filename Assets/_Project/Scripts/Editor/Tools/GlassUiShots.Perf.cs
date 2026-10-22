using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using PixFootball.Gameplay;
using PixFootball.UI;
using PixFootball.UI.Glass;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Resolution spot checks (4K, 1080p, 21:9, 4:3) and the cost of the glass backdrop blur pass. The perf run renders the live menu stage camera N times per configuration (blur pass on, blur pass off with the
    /// glass drawn from the static frosted fallback), forcing a GPU sync with a 1 pixel read after every frame so wall time includes the GPU work. The UI itself is drawn by a separate camera in these tools, so the delta
    /// is the pass (copy + two gaussian passes + publish), which is the only per-frame cost glass adds over the scene.
    /// Run: Tools/unity.ps1 -Method PixFootball.EditorTools.GlassUiShots.Perf -Log glass_perf.log   (no -nographics). Output: D:\PIXFootball\Renders\glass_ui\perf.txt
    /// </summary>
    public static partial class GlassUiShots
    {
        [MenuItem("PIX Football/Glass UI/Render resolutions")]
        public static void Resolutions()
        {
            BatchWatchdog.Arm(25);
            var keep = Sizes;
            try
            {
                QualityTiers.Override(3, -1f);
                Sizes = new[] { new[] { 3840, 2160 }, new[] { 1920, 1080 }, new[] { 3440, 1440 }, new[] { 1440, 1080 } };
                Menus();
            }
            finally { Sizes = keep; QualityTiers.ClearOverride(); }
        }

        [MenuItem("PIX Football/Glass UI/Measure blur cost")]
        public static void Perf()
        {
            BatchWatchdog.Arm(12);
            var sb = new StringBuilder();
            try
            {
                UiTheme.Force(UiThemeKind.Glass);
                QualityTiers.Override(3, -1f);
                var cam = OpenMenuStage();
                var root = GlassGallery.Build(null, 0, false);
                foreach (var sz in new[] { new[] { 1920, 1080 }, new[] { 2560, 1440 }, new[] { 3840, 2160 } })
                {
                    var rt = new RenderTexture(sz[0], sz[1], 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                    cam.targetTexture = rt;
                    var tk = GlassTheme.T; int keepPasses = tk.blurPasses;
                    var byPass = new System.Collections.Generic.Dictionary<int, float>();
                    try
                    {
                        foreach (int k in new[] { 1, 2, 4, 2, 1, 4 })
                        {
                            tk.blurPasses = k; float v = Measure(cam, rt, tex, false);
                            float best; if (!byPass.TryGetValue(k, out best) || v < best) byPass[k] = v;
                        }
                    }
                    finally { tk.blurPasses = keepPasses; }
                    float a = (byPass[4] - byPass[1]) / 3f;   // GPU + CPU cost of one extra gaussian pass at half resolution
                    string line = sz[0] + "x" + sz[1] + ": scene frame with 1 / 2 / 4 blur passes = " + byPass[1].ToString("F2") + " / " + byPass[2].ToString("F2") + " / " + byPass[4].ToString("F2") + " ms; one pass costs about " + a.ToString("F2") + " ms, the shipping setting (2 passes + downsample) about " + (2f * a).ToString("F2") + " ms";
                    sb.AppendLine(line); Debug.Log("[GlassPerf] " + line);
                    cam.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
                }
                Object.DestroyImmediate(root.gameObject);
            }
            catch (System.Exception e) { Debug.LogError("[GlassPerf] " + e); sb.AppendLine("error: " + e.Message); }
            finally { GlassBackdrop.ForceFallback = false; QualityTiers.ClearOverride(); }
            Directory.CreateDirectory(OutDir);
            File.WriteAllText(Path.Combine(OutDir, "perf.txt"), sb.ToString());
        }

        static float Measure(Camera cam, RenderTexture rt, Texture2D tex, bool fallback)
        {
            GlassBackdrop.ForceFallback = fallback;
            int runs0 = GlassBackdrop.PassRuns;
            for (int i = 0; i < 6; i++) { cam.Render(); Sync(rt, tex); }   // warm up (shader variants, RT allocation)
            var sw = Stopwatch.StartNew();
            const int N = 40;
            for (int i = 0; i < N; i++) { cam.Render(); Sync(rt, tex); }
            sw.Stop();
            if (fallback && GlassBackdrop.PassRuns != runs0 + 0 && GlassBackdrop.PassRuns - runs0 > 0) Debug.LogWarning("[GlassPerf] the pass still ran in fallback mode");
            if (!fallback && GlassBackdrop.PassRuns == runs0) Debug.LogWarning("[GlassPerf] the pass did not run in blur mode");
            return (float)sw.Elapsed.TotalMilliseconds / N;
        }

        static void Sync(RenderTexture rt, Texture2D tex)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            tex.ReadPixels(new Rect(rt.width / 2, rt.height / 2, 1, 1), 0, 0);
            RenderTexture.active = prev;
        }
    }
}

namespace PixFootball.EditorTools
{
    public static partial class GlassUiShots
    {
        /// <summary>Gamepad navigation audit of what a shot shows: every interactable selectable must be reachable from the first one by up / down / left / right moves (the same graph the EventSystem walks with the d-pad and left stick). Appended to navaudit.txt next to the renders.</summary>
        static void NavAudit(string name, Transform[] roots)
        {
            var sels = new List<UnityEngine.UI.Selectable>();
            foreach (var r in roots)
                foreach (var s in r.GetComponentsInChildren<UnityEngine.UI.Selectable>(false))
                    if (s != null && s.gameObject.activeInHierarchy && s.IsInteractable() && s.navigation.mode != UnityEngine.UI.Navigation.Mode.None) sels.Add(s);
            string line;
            if (sels.Count == 0) line = name + ": display only (no selectables)";
            else
            {
                var set = new HashSet<UnityEngine.UI.Selectable>(sels);
                var seen = new HashSet<UnityEngine.UI.Selectable> { sels[0] };
                var q = new Queue<UnityEngine.UI.Selectable>(); q.Enqueue(sels[0]);
                while (q.Count > 0)
                {
                    var c = q.Dequeue();
                    foreach (var n in new[] { c.FindSelectableOnUp(), c.FindSelectableOnDown(), c.FindSelectableOnLeft(), c.FindSelectableOnRight() })
                        if (n != null && set.Contains(n) && seen.Add(n)) q.Enqueue(n);
                }
                var lost = new List<string>(); foreach (var s in sels) if (!seen.Contains(s)) lost.Add(s.name);
                line = name + ": " + sels.Count + " selectables, " + seen.Count + " reachable" + (lost.Count > 0 ? ", UNREACHABLE: " + string.Join(", ", lost) : ", all reachable") + "; first=" + sels[0].name;
            }
            Debug.Log("[GlassNav] " + line);
            Directory.CreateDirectory(OutDir);
            File.AppendAllText(Path.Combine(OutDir, "navaudit.txt"), line + "\n");
        }
    }
}

namespace PixFootball.EditorTools
{
    public static partial class GlassUiShots
    {
        /// <summary>HUD, play call, pause, final and the extras at 21:9 and 4:3 (the layouts were designed for 16:9).</summary>
        [MenuItem("PIX Football/Glass UI/Render HUD resolutions")]
        public static void HudResolutions()
        {
            BatchWatchdog.Arm(40);
            var keep = Sizes;
            try
            {
                QualityTiers.Override(3, -1f);
                foreach (var s in new[] { new[] { 3440, 1440 }, new[] { 1440, 1080 } })
                {
                    Sizes = new[] { s };
                    Hud();
                    Extras();
                }
            }
            finally { Sizes = keep; QualityTiers.ClearOverride(); }
        }
    }
}
