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
    /// Settings screen in five pages (Video, Gameplay, Camera, Broadcast, Audio) behind a row of tab buttons (LB / RB or PageUp / PageDown switch pages): resolution, display mode, vsync and
    /// frame cap, graphics quality, render scale and upscaler, camera and controller options, the v0.5 broadcast options and the volumes. Persisted in PlayerPrefs via PixSettings (writes
    /// are debounced there). Display changes ask for confirmation and revert by themselves after 15 s.
    /// </summary>
    public partial class SettingsPanel : MonoBehaviour, ICancelHandler
    {
        public Action OnBack;
        /// <summary>Raised after "reset all options" so the owner can rebuild the panel with the default values.</summary>
        public Action OnReset;
        public Selectable First;
        readonly List<Vector2Int> _res = new List<Vector2Int>();
        readonly List<GameObject> _pages = new List<GameObject>();
        readonly List<Button> _tabs = new List<Button>();
        public int Page { get; private set; }
        TextMeshProUGUI _pageLabel;
        OptionSelector _resSel, _modeSel;
        // the display mode that was last confirmed (what a revert goes back to)
        int _snapW, _snapH; bool _snapFull;

        const int PageCount = 5;

        public static SettingsPanel Build(Transform parent, Action onBack)
        {
            var root = UiKit.Rect(parent, "SettingsPanel");
            var sp = root.gameObject.AddComponent<SettingsPanel>();
            sp.OnBack = onBack;
            UiKit.Stretch(root);
            RectTransform col, holder;
            if (UiTheme.IsGlass) sp.BuildGlassFrame(root, out col, out holder);
            else
            {
            col = UiKit.Rect(root, "Column");
            UiKit.Anchor(col, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(110, 0), new Vector2(800, 1000));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 6; v.childAlignment = TextAnchor.UpperLeft; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;

            var title = UiKit.Label(col, "SETTINGS", 64, UiColors.TextPrimary, true, TextAlignmentOptions.BottomLeft, "Title");
            title.rectTransform.sizeDelta = new Vector2(760, 80); var tle = title.gameObject.AddComponent<LayoutElement>(); tle.preferredHeight = 80; tle.preferredWidth = 760;
            sp._pageLabel = UiKit.Label(title.rectTransform, "", 26, UiColors.TextSecondary, true, TextAlignmentOptions.BottomRight, "PageLabel");
            UiKit.Anchor(sp._pageLabel.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(0, 8), new Vector2(360, 36)); sp._pageLabel.characterSpacing = 3f;
            var gold = UiKit.Panel(col, "TitleRule", UiColors.Gold); gold.rectTransform.sizeDelta = new Vector2(200, 6); var gle = gold.gameObject.AddComponent<LayoutElement>(); gle.preferredHeight = 6; gle.preferredWidth = 200;
            var gap = UiKit.Rect(col, "Gap"); gap.sizeDelta = new Vector2(10, 10); var gp = gap.gameObject.AddComponent<LayoutElement>(); gp.preferredHeight = 10;

            // ---- tab row
            var tabRow = UiKit.Rect(col, "Tabs"); tabRow.sizeDelta = new Vector2(760, 56);
            var tle2 = tabRow.gameObject.AddComponent<LayoutElement>(); tle2.preferredHeight = 56; tle2.preferredWidth = 760;
            var th = tabRow.gameObject.AddComponent<HorizontalLayoutGroup>(); th.spacing = 8; th.childControlWidth = false; th.childControlHeight = false; th.childForceExpandWidth = false; th.childForceExpandHeight = false;
            string[] tabNames = { "VIDEO", "GAMEPLAY", "CAMERA", "BROADCAST", "AUDIO" };
            for (int i = 0; i < tabNames.Length; i++)
            {
                int idx = i;
                var b = UiKit.MenuButton(tabRow, tabNames[i], () => sp.SetPage(idx), 144, 56, 24);
                sp._tabs.Add(b);
            }
            var gap2 = UiKit.Rect(col, "Gap2"); gap2.sizeDelta = new Vector2(10, 8); var gp2 = gap2.gameObject.AddComponent<LayoutElement>(); gp2.preferredHeight = 8;

            // ---- pages (all share one holder; only the selected one is active)
            holder = UiKit.Rect(col, "Pages"); holder.sizeDelta = new Vector2(760, 560);
            var hle = holder.gameObject.AddComponent<LayoutElement>(); hle.preferredHeight = 560; hle.preferredWidth = 760;
            }
            sp.BuildResolutions();
            sp.SnapshotDisplay();

            var video = sp.NewPage(holder, "Video");
            sp._resSel = UiControls.Selector(video, "Resolution", sp._res.Select(r => r.x + " x " + r.y).ToArray(), sp.IndexOfResolution(sp._snapW, sp._snapH), i => { PixSettings.ResolutionWidth = sp._res[i].x; PixSettings.ResolutionHeight = sp._res[i].y; sp.DisplayChanged(); });
            sp._modeSel = UiControls.Selector(video, "Display mode", new[] { "Fullscreen", "Windowed" }, PixSettings.Fullscreen ? 0 : 1, i => { PixSettings.Fullscreen = i == 0; sp.DisplayChanged(); });
            UiControls.Selector(video, "VSync", new[] { "Quality default", "Off", "On" }, PixSettings.VSync + 1, i => { PixSettings.VSync = i - 1; PixSettings.ApplyFrameSettings(); });
            UiControls.Selector(video, "Frame cap", PixSettings.FrameCapNames, PixSettings.FrameCapIndex, i => { PixSettings.FrameCapIndex = i; PixSettings.ApplyFrameSettings(); });
            int rsIdx = 0; for (int i = 0; i < QualityTiers.RenderScales.Length; i++) if (Mathf.Abs(QualityTiers.RenderScales[i] - PixSettings.RenderScale) < 0.02f) rsIdx = i;
            UiControls.Selector(video, "Graphics quality", QualityTiers.Names, QualityTiers.Current, i => QualityTiers.Select(i));
            UiControls.Selector(video, "Render scale", QualityTiers.RenderScaleNames, rsIdx, i => { PixSettings.RenderScale = QualityTiers.RenderScales[i]; QualityTiers.ApplyRenderScale(); });
            UiControls.Selector(video, "Upscaler", PixSettings.UpscalerNames, PixSettings.Upscaler, i => { PixSettings.Upscaler = i; UpscalerControl.Apply(); });

            var game = sp.NewPage(holder, "Gameplay");
            UiControls.Selector(game, "Scorebug", PixSettings.ScorebugNames, (int)PixSettings.Scorebug, i => { PixSettings.Scorebug = (ScorebugStyle)i; });
            // look controls apply to free roam, the first-person camera and the replay free camera (the behind-the-QB camera has no look input)
            UiControls.Selector(game, "Invert look Y", new[] { "Off", "On" }, PixSettings.InvertLookY ? 1 : 0, i => { PixSettings.InvertLookY = i == 1; });
            UiControls.SliderRow(game, "Look sensitivity", Mathf.InverseLerp(0.2f, 3f, PixSettings.LookSensitivity), x => { PixSettings.LookSensitivity = Mathf.Lerp(0.2f, 3f, x); });
            UiControls.Selector(game, "Controller rumble", new[] { "On", "Off" }, PixSettings.Rumble ? 0 : 1, i => { PixSettings.Rumble = i == 0; });
            UiControls.Selector(game, "Camera shake on hits", new[] { "On", "Off" }, PixSettings.CameraShake ? 0 : 1, i => { PixSettings.CameraShake = i == 0; });
            UiControls.Selector(game, "UI theme (next menu)", UiTheme.Names, (int)UiTheme.Current, i => { UiTheme.Current = UiTheme.FromIndex(i); });
            UiKit.MenuButton(game, "RESET ALL OPTIONS TO DEFAULT", () => sp.ConfirmReset(), 760, 56, 26);

            // v0.6: the camera page. Classic (All-Pro 2K8) is the default; Madden and Wide are variants of the behind-the-offense camera; Broadcast is for simulated games (optional for the user).
            var camp = sp.NewPage(holder, "Camera");
            int camIdx = PixSettings.DefaultCamera == CameraMode.Broadcast ? 3 : PixSettings.DefaultCamera == CameraMode.FirstPerson ? 4 : (int)PixSettings.UserCameraStyle;
            UiControls.Selector(camp, "Camera (last used)", new[] { "Classic", "Madden", "Wide", "Broadcast", "First person" }, camIdx, i =>
            {
                if (i <= 2) { PixSettings.DefaultCamera = CameraMode.Behind; PixSettings.UserCameraStyle = (CameraStyle)i; }
                else PixSettings.DefaultCamera = i == 3 ? CameraMode.Broadcast : CameraMode.FirstPerson;
            });
            UiControls.SliderRow(camp, "Camera height", (PixSettings.CameraHeightTrim + 3f) / 9f, x => { PixSettings.CameraHeightTrim = x * 9f - 3f; });
            UiControls.SliderRow(camp, "Camera distance", (PixSettings.CameraDistanceTrim + 4f) / 12f, x => { PixSettings.CameraDistanceTrim = x * 12f - 4f; });
            UiControls.SliderRow(camp, "Zoom out at the snap", PixSettings.CameraZoomOut, x => { PixSettings.CameraZoomOut = x; });
            UiControls.SliderRow(camp, "Field of view", (PixSettings.CameraFovTrim + 10f) / 25f, x => { PixSettings.CameraFovTrim = x * 25f - 10f; });
            UiControls.Selector(camp, "First-person facemask", new[] { "On", "Off" }, PixSettings.FirstPersonMask ? 0 : 1, i => { PixSettings.FirstPersonMask = i == 0; });
            UiControls.Selector(camp, "Pre-snap stars", new[] { "On", "Off" }, PixSettings.PreSnapStars ? 0 : 1, i => { PixSettings.PreSnapStars = i == 0; });
            UiControls.Selector(camp, "Models (next match)", new[] { "Photoreal", "Classic" }, PixSettings.PhotoPlayers ? 0 : 1, i => { PixSettings.PhotoPlayers = i == 0; });

            var bc = sp.NewPage(holder, "Broadcast");
            UiControls.Selector(bc, "Cinematics", PixSettings.CinematicsNames, PixSettings.Cinematics, i => { PixSettings.Cinematics = i; });
            UiControls.Selector(bc, "Automatic replays", new[] { "On", "Off" }, PixSettings.AutoReplay ? 0 : 1, i => { PixSettings.AutoReplay = i == 0; });
            UiControls.Selector(bc, "TV overlays", new[] { "On", "Off" }, PixSettings.TvOverlays ? 0 : 1, i => { PixSettings.TvOverlays = i == 0; });
            var note = UiKit.Label(bc, "FULL: establishing shots, post-play cuts, replays and shows.   MINIMAL: only the biggest plays get a replay and a cut.   OFF: live camera only.", 26, UiColors.TextSecondary, false, TextAlignmentOptions.TopLeft, "Note");
            note.textWrappingMode = TextWrappingModes.Normal; note.rectTransform.sizeDelta = new Vector2(760, 110); var nle = note.gameObject.AddComponent<LayoutElement>(); nle.preferredWidth = 760; nle.preferredHeight = 110;

            var audio = sp.NewPage(holder, "Audio");
            UiControls.SliderRow(audio, "Master volume", PixSettings.MasterVolume, x => { PixSettings.MasterVolume = x; AudioListener.volume = x; });
            UiControls.SliderRow(audio, "Menu music volume", PixSettings.MusicVolume, x => { PixSettings.MusicVolume = x; });
            UiControls.SliderRow(audio, "Crowd volume", PixSettings.CrowdVolume, x => { PixSettings.CrowdVolume = x; });
            UiControls.SliderRow(audio, "Effects volume", PixSettings.SfxVolume, x => { PixSettings.SfxVolume = x; });

            UiKit.MenuButton(col, "BACK", () => { if (sp.OnBack != null) sp.OnBack(); }, 760, 54, 30);
            sp.First = sp._tabs.Count > 0 ? (Selectable)sp._tabs[0] : sp._resSel;
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

        /// <summary>The tab button of a page (the control that gets the focus when the screen opens on that page); null when there is none.</summary>
        public Selectable TabFor(int i) { return i >= 0 && i < _tabs.Count ? _tabs[i] : null; }

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
            if (_glassTabs != null) _glassTabs.SetIndex(Page, false);
            if (_pageLabel != null) _pageLabel.text = "LB   PAGE " + (Page + 1) + " / " + _pages.Count + "   RB";
        }

        /// <summary>LB / RB (PageUp / PageDown) step through the pages.</summary>
        void Update()
        {
            if (UiDialog.AnyOpen) return;
            int dir = 0;
            var pad = UnityEngine.InputSystem.Gamepad.current;
            if (pad != null) { if (pad.leftShoulder.wasPressedThisFrame) dir = -1; else if (pad.rightShoulder.wasPressedThisFrame) dir = 1; }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null) { if (kb.pageUpKey.wasPressedThisFrame) dir = -1; else if (kb.pageDownKey.wasPressedThisFrame) dir = 1; }
            if (dir == 0) return;
            // a long selector (none here) keeps its own LB / RB paging; the settings pages have none
            SetPage((Page + dir + _pages.Count) % _pages.Count);
            if (EventSystem.current != null && Page < _tabs.Count) EventSystem.current.SetSelectedGameObject(_tabs[Page].gameObject);
        }

        // ---- resolution list: only modes the display reports (plus the current window size)
        void BuildResolutions()
        {
            var set = new SortedSet<Vector2Int>(Comparer<Vector2Int>.Create((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y)));
            foreach (var r in Screen.resolutions) if (r.width >= 1280 && r.height >= 720) set.Add(new Vector2Int(r.width, r.height));
            // common 16:9 modes at or below the display's largest mode (windowed play at a smaller size); nothing the monitor cannot show
            int maxW = 0, maxH = 0; foreach (var r in Screen.resolutions) { maxW = Mathf.Max(maxW, r.width); maxH = Mathf.Max(maxH, r.height); }
            if (maxW == 0) { maxW = Screen.currentResolution.width; maxH = Screen.currentResolution.height; }
            foreach (var c in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440), new Vector2Int(3840, 2160) })
                if (c.x <= Mathf.Max(maxW, 1280) && c.y <= Mathf.Max(maxH, 720)) set.Add(c);
            set.Add(new Vector2Int(Screen.width, Screen.height));
            _res.AddRange(set);
        }

        int IndexOfResolution(int w, int h)
        {
            int best = 0; long bd = long.MaxValue;
            for (int i = 0; i < _res.Count; i++)
            {
                long d = Math.Abs(_res[i].x - w) + Math.Abs(_res[i].y - h);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        void SnapshotDisplay()
        {
            _snapW = PixSettings.ResolutionWidth > 0 ? PixSettings.ResolutionWidth : Screen.width;
            _snapH = PixSettings.ResolutionHeight > 0 ? PixSettings.ResolutionHeight : Screen.height;
            _snapFull = PixSettings.Fullscreen;
        }

        /// <summary>Applies the new resolution / display mode once, then asks the player to keep it; no answer in 15 s (an unreadable screen) puts the old mode back.</summary>
        void DisplayChanged()
        {
            PixSettings.Apply();
            if (Application.isEditor || UiDialog.AnyOpen) { SnapshotDisplay(); return; }
            UiDialog.Show(transform.parent, "Keep these display settings?", "If the picture is unreadable, do nothing: the previous mode comes back by itself.", "KEEP", "REVERT",
                () => SnapshotDisplay(), RevertDisplay, 15f);
        }

        void RevertDisplay()
        {
            PixSettings.ResolutionWidth = _snapW; PixSettings.ResolutionHeight = _snapH; PixSettings.Fullscreen = _snapFull;
            PixSettings.Apply();
            if (_resSel != null) _resSel.SetOptions(_resSel.Options, IndexOfResolution(_snapW, _snapH));
            if (_modeSel != null) _modeSel.SetOptions(_modeSel.Options, _snapFull ? 0 : 1);
        }

        void ConfirmReset()
        {
            UiDialog.Show(transform.parent, "Reset all options?", "Gameplay, camera, broadcast, audio and graphics options go back to their defaults. Display mode and resolution stay.", "RESET", "CANCEL",
                () => { PixSettings.ResetToDefaults(); if (OnReset != null) OnReset(); }, null);
        }

        public void OnCancel(BaseEventData e) { if (OnBack != null) OnBack(); }
    }
}
