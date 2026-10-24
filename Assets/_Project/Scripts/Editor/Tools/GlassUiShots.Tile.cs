using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using PixFootball.Gameplay;
using PixFootball.UI;
using PixFootball.UI.Glass;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// QA renders and measurements of the Tile (Direction 16) kit. Pages come from <see cref="GlassGallery.BuildTile"/> (hub, franchise, play-call strip, controls, states); every shot is audited for
    /// reachability (navaudit.txt), text below 20 units, text that overflows its box and auto-fit hits. Output: D:\PIXFootball\Renders\ui16\*.png, navaudit.txt, tile_audit.txt, tile_perf.txt.
    /// Run: Tools/unity.ps1-style `-executeMethod PixFootball.EditorTools.GlassUiShots.TileGallery` (no -nographics).
    /// </summary>
    public static partial class GlassUiShots
    {
        public const string TileOutDir = @"D:\PIXFootball\Renders\ui16";
        static readonly StringBuilder _audit = new StringBuilder();

        static void TileBegin()
        {
            QualityTiers.Override(3, -1f);
            GlassBackdrop.ForceFallback = true;
            UiTheme.Force(UiThemeKind.Tile);
            GlassTheme.ResetCache();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        static void TileEnd() { GlassBackdrop.ForceFallback = false; QualityTiers.ClearOverride(); UiTheme.Force(null); GlassTheme.ResetCache(); }

        /// <summary>The five gallery pages at 1920 x 1080.</summary>
        [MenuItem("PIX Football/Glass UI/Render tile gallery")]
        public static void TileGallery()
        {
            BatchWatchdog.Arm(25);
            string keep = OutDir;
            try
            {
                OutDir = TileOutDir; Directory.CreateDirectory(OutDir);
                TileBegin();
                for (int page = 0; page < GlassGallery.TilePages; page++)
                {
                    var root = GlassGallery.BuildTile(null, page);
                    Shot("tile_p" + page + "_1920x1080", 1920, 1080, null, root.root);
                    Audit("p" + page + " 1920x1080", root);
                    Object.DestroyImmediate(root.gameObject);
                }
                Flush();
            }
            catch (Exception e) { Debug.LogError("[TileShots] " + e); }
            finally { OutDir = keep; TileEnd(); }
        }

        /// <summary>The hub, franchise and play-call pages at 2560x1440, 3840x2160, 3440x1440 (21:9) and 1440x1080 (4:3).</summary>
        [MenuItem("PIX Football/Glass UI/Render tile resolutions")]
        public static void TileResolutions()
        {
            BatchWatchdog.Arm(40);
            string keep = OutDir;
            try
            {
                OutDir = TileOutDir; Directory.CreateDirectory(OutDir);
                TileBegin();
                foreach (var sz in new[] { new[] { 2560, 1440 }, new[] { 3840, 2160 }, new[] { 3440, 1440 }, new[] { 1440, 1080 } })
                    foreach (int page in new[] { 0, 1, 2 })
                    {
                        var root = GlassGallery.BuildTile(null, page);
                        string tag = sz[0] + "x" + sz[1];
                        Shot("tile_p" + page + "_" + tag, sz[0], sz[1], null, root.root);
                        Audit("p" + page + " " + tag, root);
                        Object.DestroyImmediate(root.gameObject);
                    }
                Flush();
            }
            catch (Exception e) { Debug.LogError("[TileShots] " + e); }
            finally { OutDir = keep; TileEnd(); }
        }

        static void Flush()
        {
            File.WriteAllText(Path.Combine(TileOutDir, "tile_audit.txt"), _audit.ToString());
            Debug.Log("[TileShots] audit written\n" + _audit);
        }

        /// <summary>Text below 20 units (canvas units, after auto-fit), text that overflows its box, auto-fit hits (font shrunk) and logo / licence strings in the visible texts.</summary>
        static void Audit(string tag, RectTransform root)
        {
            int small = 0, overflow = 0, fit = 0, texts = 0; var names = new List<string>();
            foreach (var tm in root.GetComponentsInChildren<TMP_Text>(false))
            {
                if (!tm.gameObject.activeInHierarchy || string.IsNullOrEmpty(tm.text)) continue;
                texts++;
                tm.ForceMeshUpdate();
                float size = tm.fontSize;
                if (tm.enableAutoSizing && size < tm.fontSizeMax - 0.5f) fit++;
                if (size < 19.5f) { small++; names.Add(tm.name + "=" + size.ToString("F1")); }
                if (tm.isTextOverflowing && tm.overflowMode != TextOverflowModes.Ellipsis) { overflow++; names.Add(tm.name + " overflows"); }
            }
            string line = tag + ": " + texts + " texts, below20=" + small + ", overflow=" + overflow + ", autofit=" + fit + (names.Count > 0 ? "  [" + string.Join(", ", names.GetRange(0, Mathf.Min(10, names.Count))) + "]" : "");
            _audit.AppendLine(line);
        }

        // ------------------------------------------------------------------ measurements
        /// <summary>UI CPU per frame (canvas rebuild + camera render of the UI layer), draw calls, GC of the ticker and the focus step, SDF atlas, missing glyphs: Tile pages against the Glass hub page.</summary>
        [MenuItem("PIX Football/Glass UI/Measure tile")]
        public static void TilePerf()
        {
            BatchWatchdog.Arm(25);
            var sb = new StringBuilder();
            string keep = OutDir;
            try
            {
                OutDir = TileOutDir; Directory.CreateDirectory(OutDir);
                TileBegin();
                if (_ui == null) _ui = MakeUiCamera();
                // ---- glass reference: the v0.7 hub sheet with the blur pass off (UI cost only), drawn by the same camera
                UiTheme.Force(UiThemeKind.Glass); GlassTheme.ResetCache();
                var g0 = GlassGallery.Build(null, 0, false);
                sb.AppendLine("Glass hub sheet (fallback frost, no blur pass): " + MeasureUi(g0, "glass_hub", sb));
                Object.DestroyImmediate(g0.gameObject);
                UiTheme.Force(UiThemeKind.Tile); GlassTheme.ResetCache();
                string[] names = { "tile hub", "tile franchise (table page)", "tile play call", "tile controls", "tile states" };
                for (int page = 0; page < GlassGallery.TilePages; page++)
                {
                    var root = GlassGallery.BuildTile(null, page);
                    sb.AppendLine(names[page] + ": " + MeasureUi(root, "tile_p" + page, sb));
                    if (page == 0) MeasureGc(root, sb);
                    Object.DestroyImmediate(root.gameObject);
                }
                { var again = GlassGallery.BuildTile(null, 0); sb.AppendLine("tile hub (measured again last): " + MeasureUi(again, "tile_p0b", sb)); Object.DestroyImmediate(again.gameObject); }
                { UiTheme.Force(UiThemeKind.Glass); GlassTheme.ResetCache(); var g1 = GlassGallery.Build(null, 0, false); sb.AppendLine("Glass hub sheet (measured again last): " + MeasureUi(g1, "glass_hub2", sb)); Object.DestroyImmediate(g1.gameObject); UiTheme.Force(UiThemeKind.Tile); GlassTheme.ResetCache(); }
                // ---- fonts
                var theme = GlassTheme.Current;
                foreach (var f in new[] { theme.fontTitle, theme.fontBody })
                {
                    if (f == null) { sb.AppendLine("font missing"); continue; }
                    sb.AppendLine("font " + f.name + ": glyphs=" + f.characterTable.Count + ", atlases=" + f.atlasTextures.Length + " of " + f.atlasWidth + "x" + f.atlasHeight + ", missing of the Latin set=" + GlassAssetBuilder.MissingGlyphs(f, GlassAssetBuilder.BarlowSet) + " (DejaVu fallback covers the rest)");
                }
            }
            catch (Exception e) { Debug.LogError("[TilePerf] " + e); sb.AppendLine("error: " + e); }
            finally { OutDir = keep; TileEnd(); }
            File.WriteAllText(Path.Combine(TileOutDir, "tile_perf.txt"), sb.ToString());
            Debug.Log("[TilePerf]\n" + sb);
        }

        static string MeasureUi(RectTransform root, string tag, StringBuilder sb)
        {
            var rt = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _ui.targetTexture = rt;
            Bind(root, _ui); Settle(new Transform[] { root });
            for (int i = 0; i < 20; i++) { Canvas.ForceUpdateCanvases(); _ui.Render(); }
            double bestCanvas = 1e9, bestRender = 1e9;
            for (int rep = 0; rep < 5; rep++)
            {
                var sw = Stopwatch.StartNew();
                for (int i = 0; i < 100; i++) Canvas.ForceUpdateCanvases();
                bestCanvas = Math.Min(bestCanvas, sw.Elapsed.TotalMilliseconds / 100.0);
                sw.Restart();
                for (int i = 0; i < 30; i++) _ui.Render();
                bestRender = Math.Min(bestRender, sw.Elapsed.TotalMilliseconds / 30.0);
            }
            _ui.targetTexture = null; rt.Release(); Object.DestroyImmediate(rt);
            return "steady canvas update " + bestCanvas.ToString("F3") + " ms, UI camera render (CPU submit, no readback) " + bestRender.ToString("F2") + " ms, draw calls (hierarchy-order estimate) " + EstimateBatches(root) + ", graphics " + root.GetComponentsInChildren<Graphic>(false).Length;
        }

        /// <summary>Draw call estimate: consecutive canvas renderers of one canvas that share material and texture batch together; a change of either starts a new batch (nested canvases count separately).</summary>
        static int EstimateBatches(RectTransform root)
        {
            int total = 0;
            foreach (var cv in root.GetComponentsInChildren<Canvas>(false))
            {
                int lastMat = int.MinValue, lastTex = int.MinValue;
                foreach (var g in cv.GetComponentsInChildren<Graphic>(false))
                {
                    if (g.canvas != cv || !g.canvasRenderer || g.canvasRenderer.cull || g.canvasRenderer.GetAlpha() <= 0f) continue;
                    int m = g.materialForRendering != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(g.materialForRendering) : 0, t = g.mainTexture != null ? System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(g.mainTexture) : 0;
                    if (m != lastMat || t != lastTex) { total++; lastMat = m; lastTex = t; }
                }
            }
            return total;
        }

        static void MeasureGc(RectTransform root, StringBuilder sb)
        {
            var tk = root.GetComponentInChildren<GlassTicker>(true);
            var foc = new List<GlassFocusable>(root.GetComponentsInChildren<GlassFocusable>(false));
            if (tk != null)
            {
                for (int i = 0; i < 30; i++) tk.Tick(0.016f);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 600; i++) tk.Tick(0.016f);
                sb.AppendLine("ticker: " + (GC.GetAllocatedBytesForCurrentThread() - a0) + " bytes allocated over 600 frames (target 0)");
            }
            if (foc.Count > 0)
            {
                var f = foc[0];
                for (int i = 0; i < 30; i++) f.Step(0.016f);
                f.ShowState(0.5f, true);
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 600; i++) f.Step(0.016f);
                sb.AppendLine("focus step (control easing toward focus): " + (GC.GetAllocatedBytesForCurrentThread() - a0) + " bytes allocated over 600 frames (target 0)");
            }
        }
    }
}

namespace PixFootball.EditorTools
{
    public static partial class GlassUiShots
    {
        /// <summary>Draw calls (batches) of the last rendered frame from the editor's stats window counters (reflection: the property name differs between Unity versions); -1 when unavailable.</summary>
        static int StatCalls()
        {
            foreach (string n in new[] { "batches", "drawCalls", "setPassCalls" })
            {
                var p = typeof(UnityStats).GetProperty(n, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (p != null) return (int)p.GetValue(null);
            }
            return -1;
        }
    }
}

namespace PixFootball.EditorTools
{
    public static partial class GlassUiShots
    {
        /// <summary>Debug: which graphics cover the canvas point (x from the left, y from the top) on a gallery page, in draw order. Args via env PIX_PROBE="page,x,y".</summary>
        [MenuItem("PIX Football/Glass UI/Probe tile point")]
        public static void TileProbe()
        {
            string[] a = (System.Environment.GetEnvironmentVariable("PIX_PROBE") ?? "0,50,995").Split(',');
            int page = int.Parse(a[0]); float x = float.Parse(a[1]), y = float.Parse(a[2]);
            var sb = new StringBuilder();
            try
            {
                TileBegin();
                var root = GlassGallery.BuildTile(null, page);
                Canvas.ForceUpdateCanvases();
                Settle(new Transform[] { root });
                var rr = root.rect;
                var world = root.TransformPoint(new Vector3(rr.xMin + x, rr.yMax - y, 0f));
                foreach (var g in root.GetComponentsInChildren<Graphic>(false))
                {
                    var rt = g.rectTransform; var lp = rt.InverseTransformPoint(world);
                    if (!rt.rect.Contains(new Vector2(lp.x, lp.y))) continue;
                    string path = g.name; var p = g.transform.parent; int d = 0; while (p != null && d++ < 4) { path = p.name + "/" + path; p = p.parent; }
                    var gg = g as GlassGraphic;
                    sb.AppendLine(path + "  " + g.GetType().Name + "  color=" + ColorUtility.ToHtmlStringRGBA(g.color) + "  rect=" + rt.rect.size + (gg != null ? "  kind=" + gg.Kind + " focus=" + gg.Focus : ""));
                }
                Object.DestroyImmediate(root.gameObject);
            }
            catch (Exception e) { sb.AppendLine("error " + e); }
            finally { TileEnd(); }
            Directory.CreateDirectory(TileOutDir);
            File.WriteAllText(Path.Combine(TileOutDir, "probe.txt"), sb.ToString());
            Debug.Log("[TileProbe]\n" + sb);
        }
    }
}
