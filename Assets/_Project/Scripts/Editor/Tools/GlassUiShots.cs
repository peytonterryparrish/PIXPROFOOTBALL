using System.Collections.Generic;
using System.IO;
using PixFootball.Gameplay;
using PixFootball.UI;
using PixFootball.UI.Glass;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.UI;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// QA renders of the glass UI (Docs/QA/glass_ui.md). The 3D scene camera renders the real scene (the glass backdrop blur pass runs inside that render, exactly as in the game), then the canvases are
    /// drawn by a second camera without post-processing, once over black and once over white, which gives the exact premultiplied colour and alpha of the overlay; the two are composited over the scene
    /// frame in linear light on the CPU. That reproduces what a Screen Space - Overlay canvas draws after post-processing. Output: D:\PIXFootball\Renders\glass_ui\*.png.
    /// Run: Tools/unity.ps1 -Method PixFootball.EditorTools.GlassUiShots.Gallery -Log glass_shots.log   (no -nographics).
    /// </summary>
    public static partial class GlassUiShots
    {
        public static string OutDir = @"D:\PIXFootball\Renders\glass_ui";
        static Camera _scene, _ui;

        // ------------------------------------------------------------------ cameras
        public static Camera MakeUiCamera()
        {
            var go = new GameObject("GlassUiCam") { hideFlags = HideFlags.DontSave };
            go.transform.position = new Vector3(5000f, 5000f, -5000f);
            var cam = go.AddComponent<Camera>();
            var hd = go.AddComponent<HDAdditionalCameraData>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = Color.black; cam.cullingMask = 1 << 5; cam.allowHDR = false;
            cam.nearClipPlane = 0.1f; cam.farClipPlane = 100f;
            hd.volumeLayerMask = 0; hd.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
            hd.clearColorMode = HDAdditionalCameraData.ClearColorMode.Color; hd.backgroundColorHDR = Color.black;
            hd.customRenderingSettings = true;
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.Postprocess] = true;
            hd.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.Postprocess, false);
            hd.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.CustomPass] = true;
            hd.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.CustomPass, false);
            GlassBackdrop.SkipCameraName = "GlassUiCam";
            return cam;
        }

        static void Bind(Transform root, Camera cam)
        {
            foreach (var tr in root.GetComponentsInChildren<Transform>(true)) tr.gameObject.layer = 5;   // the UI camera only draws the UI layer
            foreach (var cv in root.GetComponentsInChildren<Canvas>(true))
                if (cv.isRootCanvas) { cv.renderMode = RenderMode.ScreenSpaceCamera; cv.worldCamera = cam; cv.planeDistance = 1f; GlassGraphic.Prepare(cv); }
        }

        static void Settle(Transform[] roots)
        {
            foreach (var r in roots) foreach (var pf in r.GetComponentsInChildren<PromptFooter>(false)) pf.ForceRebuild();
            foreach (var r in roots) { GlassSkin.Apply(r); GlassSkin.RefreshAll(r); }
            Canvas.ForceUpdateCanvases();
            foreach (var s in Object.FindObjectsByType<CanvasScaler>(FindObjectsInactive.Include, FindObjectsSortMode.None)) s.SendMessage("Handle", SendMessageOptions.DontRequireReceiver);
            foreach (var f in Object.FindObjectsByType<FitScale>(FindObjectsInactive.Include, FindObjectsSortMode.None)) f.Apply();
            Canvas.ForceUpdateCanvases();
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var r in roots)
                {
                    var all = r.GetComponentsInChildren<RectTransform>(false);
                    for (int i = all.Length - 1; i >= 0; i--) if (all[i].GetComponent<LayoutGroup>() != null || all[i].GetComponent<ContentSizeFitter>() != null) LayoutRebuilder.ForceRebuildLayoutImmediate(all[i]);
                }
                Canvas.ForceUpdateCanvases();
            }
        }

        static Texture2D Read(RenderTexture rt)
        {
            var prev = RenderTexture.active; RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); tex.Apply();
            RenderTexture.active = prev;
            return tex;
        }

        static float[] _lin; static byte[] _enc;
        static void Luts()
        {
            if (_lin != null) return;
            _lin = new float[256]; _enc = new byte[4097];
            for (int i = 0; i < 256; i++) { float c = i / 255f; _lin[i] = c <= 0.04045f ? c / 12.92f : Mathf.Pow((c + 0.055f) / 1.055f, 2.4f); }
            for (int i = 0; i <= 4096; i++) { float l = i / 4096f; float c = l <= 0.0031308f ? l * 12.92f : 1.055f * Mathf.Pow(l, 1f / 2.4f) - 0.055f; _enc[i] = (byte)Mathf.Clamp(Mathf.RoundToInt(c * 255f), 0, 255); }
        }

        /// <summary>Renders the scene camera and the canvases under <paramref name="roots"/> and writes the composite (linear-light premultiplied over) as a PNG; returns the final texture.</summary>
        public static Texture2D Shot(string name, int w, int h, Camera sceneCam, params Transform[] roots)
        {
            if (_ui == null) _ui = MakeUiCamera();
            Luts();
            var rtScene = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var rtUi = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            _ui.targetTexture = rtUi;
            foreach (var r in roots) Bind(r, _ui);
            Settle(roots);
            NavAudit(name, roots);
            foreach (var gs in Object.FindObjectsByType<GlassStage>(FindObjectsSortMode.None)) gs.Refresh((float)w / h);   // per-aspect framing of the menu player (GUI-020)

            Texture2D scene = null;
            if (sceneCam != null)
            {
                sceneCam.targetTexture = rtScene;
                int passBefore = GlassBackdrop.PassRuns;
                for (int i = 0; i < 4; i++) sceneCam.Render();
                scene = Read(rtScene);
                sceneCam.targetTexture = null;
                if (GlassBackdrop.WantBlur && GlassBackdrop.PassRuns == passBefore) Debug.LogWarning("[GlassUiShots] the backdrop blur pass did not run for " + name);
            }

            _ui.backgroundColor = Color.black; _ui.GetComponent<HDAdditionalCameraData>().backgroundColorHDR = Color.black;
            for (int i = 0; i < 2; i++) _ui.Render();
            var onBlack = Read(rtUi);
            _ui.backgroundColor = Color.white; _ui.GetComponent<HDAdditionalCameraData>().backgroundColorHDR = Color.white;
            for (int i = 0; i < 2; i++) _ui.Render();
            var onWhite = Read(rtUi);
            _ui.targetTexture = null;

            var outTex = new Texture2D(w, h, TextureFormat.RGB24, false, false);
            var pb = onBlack.GetPixels32(); var pw = onWhite.GetPixels32(); var ps = scene != null ? scene.GetPixels32() : null;
            var res = new Color32[pb.Length];
            for (int i = 0; i < pb.Length; i++)
            {
                float br = _lin[pb[i].r], bg = _lin[pb[i].g], bb = _lin[pb[i].b];
                float a = 1f - ((_lin[pw[i].r] - br) + (_lin[pw[i].g] - bg) + (_lin[pw[i].b] - bb)) / 3f;
                a = Mathf.Clamp01(a);
                float sr = 0.07f, sg = 0.075f, sb = 0.085f;
                if (ps != null) { sr = _lin[ps[i].r]; sg = _lin[ps[i].g]; sb = _lin[ps[i].b]; }
                float r = br + sr * (1f - a), g = bg + sg * (1f - a), b = bb + sb * (1f - a);
                res[i] = new Color32(_enc[Mathf.Clamp(Mathf.RoundToInt(r * 4096f), 0, 4096)], _enc[Mathf.Clamp(Mathf.RoundToInt(g * 4096f), 0, 4096)], _enc[Mathf.Clamp(Mathf.RoundToInt(b * 4096f), 0, 4096)], 255);
            }
            outTex.SetPixels32(res); outTex.Apply();
            Directory.CreateDirectory(OutDir);
            File.WriteAllBytes(Path.Combine(OutDir, name + ".png"), outTex.EncodeToPNG());
            Debug.Log("[GlassUiShots] " + name + " " + w + "x" + h + " backdropOn=" + GlassBackdrop.WantBlur);
            Object.DestroyImmediate(onBlack); Object.DestroyImmediate(onWhite); if (scene != null) Object.DestroyImmediate(scene);
            rtScene.Release(); rtUi.Release(); Object.DestroyImmediate(rtScene); Object.DestroyImmediate(rtUi);
            return outTex;
        }

        // ------------------------------------------------------------------ scenes
        /// <summary>Opens the menu scene (stadium backdrop scene added, old stage floor hidden, camera framed for the glass layout) and returns the camera.</summary>
        public static Camera OpenMenuStage(bool backdrop = true)
        {
            if (backdrop && !File.Exists(GlassSceneBuilder.BackdropScenePath)) GlassSceneBuilder.BuildMenuBackdrop();
            EditorSceneManager.OpenScene(SceneBuilder.MenuScenePath);
            if (backdrop && File.Exists(GlassSceneBuilder.BackdropScenePath)) { EditorSceneManager.OpenScene(GlassSceneBuilder.BackdropScenePath, OpenSceneMode.Additive); GlassStage.HideStageFloor(); }
            var cam = Camera.main;
            GlassStage.Frame(cam, null);
            return cam;
        }

        static void Cleanup(params Component[] parts) { foreach (var p in parts) if (p != null) Object.DestroyImmediate(p.gameObject); }

        // ------------------------------------------------------------------ entry points
        /// <summary>First run: builds the glass assets (fonts, material, theme, icon registry) and renders the gallery in one editor session.</summary>
        public static void BuildAndGallery()
        {
            BatchWatchdog.Arm(14);
            GlassAssetBuilder.BuildAll();
            Gallery();
        }

        public static void All() { BatchWatchdog.Arm(25); QualityTiers.Override(3, -1f); try { Gallery(); Menus(); } finally { QualityTiers.ClearOverride(); } }

        static int[][] Sizes = { new[] { 2560, 1440 } };

        /// <summary>The menu screens: main menu, team select (two slots), match options tab, the five settings pages, a confirm dialog.</summary>
        [MenuItem("PIX Football/Glass UI/Render menus")]
        public static void Menus()
        {
            BatchWatchdog.Arm(14);
            try
            {
                UiTheme.Force(UiThemeKind.Glass);
                var cam = OpenMenuStage();
                var mm = Object.FindFirstObjectByType<MainMenuController>();
                mm.SendMessage("Awake");
                var canvasRoot = mm.transform.Find("MenuCanvas");
                var menuRoot = mm.transform.Find("MenuCanvas/MainMenu");
                var settings = mm.GetComponentInChildren<SettingsPanel>(true);
                var team = mm.GetComponentInChildren<TeamSelectPanel>(true);
                foreach (var sz in Sizes)
                {
                    string tag = sz[0] + "x" + sz[1];
                    menuRoot.gameObject.SetActive(true); settings.gameObject.SetActive(false); team.gameObject.SetActive(false);
                    Shot("menu_" + tag, sz[0], sz[1], cam, mm.transform);
                    menuRoot.gameObject.SetActive(false); team.gameObject.SetActive(true);
                    Shot("teamselect_" + tag, sz[0], sz[1], cam, mm.transform);
                    team.gameObject.SetActive(false); settings.gameObject.SetActive(true);
                    for (int p = 0; p < 5; p++) { settings.SetPage(p); Shot("settings_page" + p + "_" + tag, sz[0], sz[1], cam, mm.transform); }
                    settings.gameObject.SetActive(false); menuRoot.gameObject.SetActive(true);
                    var dlg = UiDialog.Show(canvasRoot, "Keep these display settings?", "If the picture is unreadable, do nothing: the previous mode comes back by itself.", "KEEP", "REVERT", null, null, 15f);
                    Shot("dialog_" + tag, sz[0], sz[1], cam, mm.transform);
                    Object.DestroyImmediate(dlg.gameObject);
                }
            }
            catch (System.Exception e) { Debug.LogError("[GlassUiShots] " + e); }
        }

        [MenuItem("PIX Football/Glass UI/Render gallery")]
        public static void Gallery()
        {
            BatchWatchdog.Arm(12);
            try
            {
                UiTheme.Force(UiThemeKind.Glass);
                var cam = OpenMenuStage();
                foreach (int page in new[] { 0, 1 })
                {
                    var root = GlassGallery.Build(null, page, false);
                    Shot("gallery_p" + page + "_2560x1440", 2560, 1440, cam, root.root);
                    Object.DestroyImmediate(root.gameObject);
                }
            }
            catch (System.Exception e) { Debug.LogError("[GlassUiShots] " + e); }
        }
    }
}
