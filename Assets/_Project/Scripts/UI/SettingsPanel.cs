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
    /// <summary>Settings screen: resolution, quality, invert look, default camera, volumes. Persisted in PlayerPrefs via PixSettings.</summary>
    public class SettingsPanel : MonoBehaviour, ICancelHandler
    {
        public Action OnBack;
        public Selectable First;
        readonly List<Vector2Int> _res = new List<Vector2Int>();

        public static SettingsPanel Build(Transform parent, Action onBack)
        {
            var root = UiKit.Rect(parent, "SettingsPanel");
            var sp = root.gameObject.AddComponent<SettingsPanel>();
            sp.OnBack = onBack;
            UiKit.Stretch(root);
            var col = UiKit.Rect(root, "Column");
            UiKit.Anchor(col, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(110, 10), new Vector2(800, 880));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 8; v.childAlignment = TextAnchor.UpperLeft; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;

            var title = UiKit.Label(col, "SETTINGS", 64, UiColors.TextPrimary, true, TextAlignmentOptions.BottomLeft, "Title");
            title.rectTransform.sizeDelta = new Vector2(760, 80); var tle = title.gameObject.AddComponent<LayoutElement>(); tle.preferredHeight = 80; tle.preferredWidth = 760;
            var gold = UiKit.Panel(col, "TitleRule", UiColors.Gold); gold.rectTransform.sizeDelta = new Vector2(200, 6); var gle = gold.gameObject.AddComponent<LayoutElement>(); gle.preferredHeight = 6; gle.preferredWidth = 200;
            var gap = UiKit.Rect(col, "Gap"); gap.sizeDelta = new Vector2(10, 10); var gp = gap.gameObject.AddComponent<LayoutElement>(); gp.preferredHeight = 10;

            // resolution
            sp.BuildResolutions();
            int curRes = 0;
            int w = PixSettings.ResolutionWidth > 0 ? PixSettings.ResolutionWidth : Screen.width, h = PixSettings.ResolutionHeight > 0 ? PixSettings.ResolutionHeight : Screen.height;
            for (int i = 0; i < sp._res.Count; i++) if (sp._res[i].x == w && sp._res[i].y == h) curRes = i;
            var resSel = UiControls.Selector(col, "Resolution", sp._res.Select(r => r.x + " x " + r.y).ToArray(), curRes, i => { PixSettings.ResolutionWidth = sp._res[i].x; PixSettings.ResolutionHeight = sp._res[i].y; PixSettings.Save(); PixSettings.Apply(); if (!Application.isEditor) Screen.SetResolution(sp._res[i].x, sp._res[i].y, PixSettings.Fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed); });
            sp.First = resSel;
            UiControls.Selector(col, "Display mode", new[] { "Fullscreen", "Windowed" }, PixSettings.Fullscreen ? 0 : 1, i => { PixSettings.Fullscreen = i == 0; PixSettings.Save(); if (!Application.isEditor) Screen.fullScreenMode = i == 0 ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed; });
            int q = PixSettings.Quality >= 0 ? PixSettings.Quality : QualitySettings.GetQualityLevel();
            UiControls.Selector(col, "Quality", QualitySettings.names, Mathf.Clamp(q, 0, QualitySettings.names.Length - 1), i => { PixSettings.Quality = i; PixSettings.Save(); QualitySettings.SetQualityLevel(i, true); });
            UiControls.Selector(col, "Invert look Y", new[] { "Off", "On" }, PixSettings.InvertLookY ? 1 : 0, i => { PixSettings.InvertLookY = i == 1; PixSettings.Save(); });
            UiControls.Selector(col, "Default camera", new[] { "Broadcast", "Behind player", "First person" }, (int)PixSettings.DefaultCamera, i => { PixSettings.DefaultCamera = (CameraMode)i; PixSettings.Save(); });
            UiControls.Selector(col, "Lighting", new[] { "Night game", "Day game" }, (int)PixSettings.Lighting, i => { PixSettings.Lighting = (LightingPreset)i; PixSettings.Save(); });
            UiControls.SliderRow(col, "Master volume", PixSettings.MasterVolume, x => { PixSettings.MasterVolume = x; AudioListener.volume = x; PixSettings.Save(); });
            UiControls.SliderRow(col, "Music volume", PixSettings.MusicVolume, x => { PixSettings.MusicVolume = x; PixSettings.Save(); });
            UiControls.SliderRow(col, "Crowd volume", PixSettings.CrowdVolume, x => { PixSettings.CrowdVolume = x; PixSettings.Save(); });
            UiKit.MenuButton(col, "BACK", () => { if (sp.OnBack != null) sp.OnBack(); }, 760, 62, 30);
            return sp;
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
