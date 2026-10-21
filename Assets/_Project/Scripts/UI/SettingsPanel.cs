using System;
using System.Collections.Generic;
using System.Linq;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// Settings screen in four pages (Video, Gameplay, Broadcast, Audio) behind a row of tab buttons: resolution, quality, render scale and upscaler, camera and controller options, the v0.5 broadcast
    /// options (cinematics, automatic replays, TV overlays, first-person facemask) and the volumes. Persisted in PlayerPrefs via PixSettings.
    /// </summary>
    public class SettingsPanel : MonoBehaviour, ICancelHandler
    {
        public Action OnBack;
        public Selectable First;
        readonly List<Vector2Int> _res = new List<Vector2Int>();
        readonly List<GameObject> _pages = new List<GameObject>();
        readonly List<Button> _tabs = new List<Button>();
        public int Page { get; private set; }

        public static SettingsPanel Build(Transform parent, Action onBack)
        {
            var root = UiKit.Rect(parent, "SettingsPanel");
            var sp = root.gameObject.AddComponent<SettingsPanel>();
            sp.OnBack = onBack;
            UiKit.Stretch(root);
            var col = UiKit.Rect(root, "Column");
            UiKit.Anchor(col, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(110, 0), new Vector2(800, 1000));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 6; v.childAlignment = TextAnchor.UpperLeft; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;

            var title = UiKit.Label(col, "SETTINGS", 64, UiColors.TextPrimary, true, TextAlignmentOptions.BottomLeft, "Title");
            title.rectTransform.sizeDelta = new Vector2(760, 80); var tle = title.gameObject.AddComponent<LayoutElement>(); tle.preferredHeight = 80; tle.preferredWidth = 760;
            var gold = UiKit.Panel(col, "TitleRule", UiColors.Gold); gold.rectTransform.sizeDelta = new Vector2(200, 6); var gle = gold.gameObject.AddComponent<LayoutElement>(); gle.preferredHeight = 6; gle.preferredWidth = 200;
            var gap = UiKit.Rect(col, "Gap"); gap.sizeDelta = new Vector2(10, 10); var gp = gap.gameObject.AddComponent<LayoutElement>(); gp.preferredHeight = 10;

            // ---- tab row
            var tabRow = UiKit.Rect(col, "Tabs"); tabRow.sizeDelta = new Vector2(760, 56);
            var tle2 = tabRow.gameObject.AddComponent<LayoutElement>(); tle2.preferredHeight = 56; tle2.preferredWidth = 760;
            var th = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>(); th.spacing = 8; th.childControlWidth = false; th.childControlHeight = false; th.childForceExpandWidth = false; th.childForceExpandHeight = false;
            string[] tabNames = { "VIDEO", "GAMEPLAY", "BROADCAST", "AUDIO" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                int idx = i;
                var b = UiKit.MenuButton(tabRow, tabNames[i], () => sp.SetPage(idx), 184, 56, 26);
                sp._tabs.Add(b);
            }
            var gap2 = UiKit.Rect(col, "Gap2"); gap2.sizeDelta = new Vector2(10, 8); var gp2 = gap2.gameObject.AddComponent<LayoutElement>(); gp2.preferredHeight = 8;

            // ---- pages (all share one holder; only the selected one is active)
            var holder = UiKit.Rect(col, "Pages"); holder.sizeDelta = new Vector2(760, 560);
            var hle = holder.gameObject.AddComponent<LayoutElement>(); hle.preferredHeight = 560; hle.preferredWidth = 760;
            sp.BuildResolutions();
            int curRes = 0;
            int w = PixSettings.ResolutionWidth > 0 ? PixSettings.ResolutionWidth : Screen.width, h = PixSettings.ResolutionHeight > 0 ? PixSettings.ResolutionHeight : Screen.height;
            for (int i = 0; i < sp._res.Count; i++) if (sp._res[i].x == w && sp._res[i].y == h) curRes = i;

            var video = sp.NewPage(holder, "Video");
            var resSel = UiControls.Selector(video, "Resolution", sp._res.Select(r => r.x + " x " + r.y).ToArray(), curRes, i => { PixSettings.ResolutionWidth = sp._res[i].x; PixSettings.ResolutionHeight = sp._res[i].y; PixSettings.Save(); PixSettings.Apply(); if (!Application.isEditor) Screen.SetResolution(sp._res[i].x, sp._res[i].y, PixSettings.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed); });
            UiControls.Selector(video, "Display mode", new[] { "Fullscreen", "Windowed" }, PixSettings.Fullscreen ? 0 : 1, i => { PixSettings.Fullscreen = i == 0; PixSettings.Save(); if (!Application.isEditor) Screen.fullScreenMode = i == 0 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed; });
            int rsIdx = 0; for (int i = 0; i < QualityTiers.RenderScales.Length; i++) if (Mathf.Abs(QualityTiers.RenderScales[i] - PixSettings.RenderScale) < 0.02f) rsIdx = i;
            UiControls.Selector(video, "Graphics quality", QualityTiers.Names, QualityTiers.Current, i => QualityTiers.Select(i));
            UiControls.Selector(video, "Render scale", QualityTiers.RenderScaleNames, rsIdx, i => { PixSettings.RenderScale = QualityTiers.RenderScales[i]; PixSettings.Save(); QualityTiers.ApplyRenderScale(); });
            UiControls.Selector(video, "Upscaler", PixSettings.UpscalerNames, PixSettings.Upscaler, i => { PixSettings.Upscaler = i; PixSettings.Save(); UpscalerControl.Apply(); });
            UiControls.Selector(video, "Lighting", new[] { "Night game", "Day game" }, (int)PixSettings.Lighting, i => { PixSettings.Lighting = (LightingPreset)i; PixSettings.Save(); });

            var game = sp.NewPage(holder, "Gameplay");
            UiControls.Selector(game, "Default camera", new[] { "Broadcast", "Behind player", "First person" }, (int)PixSettings.DefaultCamera, i => { PixSettings.DefaultCamera = (CameraMode)i; PixSettings.Save(); });
            UiControls.Selector(game, "First-person facemask", new[] { "On", "Off" }, PixSettings.FirstPersonMask ? 0 : 1, i => { PixSettings.FirstPersonMask = i == 0; PixSettings.Save(); });
            UiControls.Selector(game, "Invert look Y", new[] { "Off", "On" }, PixSettings.InvertLookY ? 1 : 0, i => { PixSettings.InvertLookY = i == 1; PixSettings.Save(); });
            UiControls.Selector(game, "Controller rumble", new[] { "On", "Off" }, PixSettings.Rumble ? 0 : 1, i => { PixSettings.Rumble = i == 0; PixSettings.Save(); });
            UiControls.Selector(game, "Camera shake on hits", new[] { "On", "Off" }, PixSettings.CameraShake ? 0 : 1, i => { PixSettings.CameraShake = i == 0; PixSettings.Save(); });

            var bc = sp.NewPage(holder, "Broadcast");
            UiControls.Selector(bc, "Cinematics", PixSettings.CinematicsNames, PixSettings.Cinematics, i => { PixSettings.Cinematics = i; PixSettings.Save(); });
            UiControls.Selector(bc, "Automatic replays", new[] { "On", "Off" }, PixSettings.AutoReplay ? 0 : 1, i => { PixSettings.AutoReplay = i == 0; PixSettings.Save(); });
            UiControls.Selector(bc, "TV overlays", new[] { "On", "Off" }, PixSettings.TvOverlays ? 0 : 1, i => { PixSettings.TvOverlays = i == 0; PixSettings.Save(); });
            var note = UiKit.Label(bc, "FULL: establishing shots, post-play cuts, replays and shows.   MINIMAL: big plays only.   OFF: live camera only.", 22, UiColors.TextMuted, false, TextAlignmentOptions.TopLeft, "Note");
            note.textWrappingMode = TextWrappingModes.Normal; note.rectTransform.sizeDelta = new Vector2(760, 70); var nle = note.gameObject.AddComponent<LayoutElement>(); nle.preferredWidth = 760; nle.preferredHeight = 70;

            var audio = sp.NewPage(holder, "Audio");
            UiControls.SliderRow(audio, "Master volume", PixSettings.MasterVolume, x => { PixSettings.MasterVolume = x; AudioListener.volume = x; PixSettings.Save(); });
            UiControls.SliderRow(audio, "Music volume", PixSettings.MusicVolume, x => { PixSettings.MusicVolume = x; PixSettings.Save(); });
            UiControls.SliderRow(audio, "Crowd volume", PixSettings.CrowdVolume, x => { PixSettings.CrowdVolume = x; PixSettings.Save(); });

            UiKit.MenuButton(col, "BACK", () => { if (sp.OnBack != null) sp.OnBack(); }, 760, 54, 30);
            sp.First = sp._tabs.Count > 0 ? (Selectable)sp._tabs[0] : resSel;
            sp.SetPage(0);
            return sp;
        }

        RectTransform NewPage(RectTransform holder, string name)
        {
            var r = UiKit.Rect(holder, "Page_" + name); UiKit.Stretch(r);
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 6; v.childAlignment = TextAnchor.UpperLeft; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            _pages.Add(r.gameObject);
            return r;
        }

        public void SetPage(int i)
        {
            Page = Mathf.Clamp(i, 0, _pages.Count - 1);
            for (int k = 0; k < _pages.Count; k++) _pages[k].SetActive(k == Page);
            // the selected tab keeps the accent colour
            for (int k = 0; k < _tabs.Count; k++)
            {
                var vis = _tabs[k].GetComponent<MenuItemVisual>();
                if (vis != null && vis.Text != null) vis.Text.color = k == Page ? UiColors.Gold : Color.white;
            }
        }

        void BuildResolutions()
        {
            var set = new SortedSet<Vector2Int>(Comparer<Vector2Int>.Create((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y)));
            foreach (var r in Screen.resolutions) if (r.width >= 1280 && r.height >= 720) set.Add(new Vector2Int(r.width, r.height));
            foreach (var c in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) }) set.Add(c);
            set.Add(new Vector2Int(Screen.width, Screen.height));
            _res.AddRange(set);
        }

        public void OnCancel(BaseEventData e) { if (OnBack != null) OnBack(); }
    }
}
