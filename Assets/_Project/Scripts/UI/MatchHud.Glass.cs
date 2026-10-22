using System;
using System.Collections;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// Glass skin of the in-match HUD. Every builder here fills the same fields as its Classic twin (the scorebug, the 2K8 bottom bar, banner and toast, hint bar, pause rail, final screen, toss
    /// caption), so the rest of MatchHud (SetScore, banners, hints, pause logic) runs unchanged. Surfaces are glass over the live field; team colours appear only as thin accents so the type stays white.
    /// </summary>
    public partial class MatchHud
    {
        bool _glassHud;
        static readonly Color FlagYellow = new Color(1f, 0.918f, 0f, 1f);

        // ============================================================================================ small helpers
        static RectTransform DividerV(Transform parent, float height = 34f)
        {
            var r = UiKit.Rect(parent, "Divider"); r.sizeDelta = new Vector2(1.5f, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = 1.5f; le.minWidth = 1.5f; le.preferredHeight = height;
            var img = r.gameObject.AddComponent<Image>(); var c = GlassTheme.T.edgeA; c.a = 0.30f; img.color = c; img.raycastTarget = false;
            return r;
        }

        // ============================================================================================ bottom bar (2K8 layout)
        Block MakeBarBlockGlass(RectTransform row, string name, bool local)
        {
            var t = GlassTheme.T;
            var cell = BarCell(row, name, 330f);
            var b = new Block { Solid = true };
            // thin team-colour underline instead of a coloured block: FillBar writes the colour into b.Bg
            b.Bg = UiKit.Panel(cell, "TeamBar", local ? new Color(0.1f, 0.3f, 0.7f) : new Color(0.7f, 0.15f, 0.15f));
            UiKit.Anchor(b.Bg.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 4f), new Vector2(300f, 4f));
            b.Crest = UiKit.Panel(cell, "Crest", Color.white); b.Crest.preserveAspect = true;
            UiKit.Anchor(b.Crest.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 3f), new Vector2(50f, 50f));
            b.Abbr = UiKit.Label(cell, "ABC", 38, Color.white, true, TextAlignmentOptions.MidlineLeft, "Abbr");
            UiKit.Anchor(b.Abbr.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(78f, 4f), new Vector2(120f, 52f)); b.Abbr.characterSpacing = 2f;
            b.Score = UiKit.Label(cell, "0", 52, Color.white, true, TextAlignmentOptions.MidlineRight, "Score");
            UiKit.Anchor(b.Score.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-18f, 3f), new Vector2(96f, 62f));
            var poss = UiKit.Panel(cell, "Poss", t.accent, Sp("sb_arrow")); poss.preserveAspect = true;
            UiKit.Anchor(poss.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(206f, 4f), new Vector2(26f, 26f)); b.Poss = poss.gameObject;
            b.Pips = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var p = UiKit.Panel(cell, "TO" + i, Color.white);
                UiKit.Anchor(p.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(80f + i * 22f, -8f), new Vector2(17f, 4f));
                b.Pips[i] = p;
            }
            return b;
        }

        void BuildBottomBarGlass()
        {
            var t = GlassTheme.T;
            var root = UiKit.Rect(_root, "BottomBar");
            root.anchorMin = new Vector2(0, 0); root.anchorMax = new Vector2(1, 0); root.pivot = new Vector2(0.5f, 0); root.anchoredPosition = Vector2.zero; root.sizeDelta = new Vector2(0, BarHeight);
            _bottomRoot = root;
            var glass = GlassKit.Panel(root, "Glass", 1.25f, true, 16f);
            glass.rectTransform.anchorMin = Vector2.zero; glass.rectTransform.anchorMax = Vector2.one; glass.rectTransform.offsetMin = new Vector2(24f, 8f); glass.rectTransform.offsetMax = new Vector2(-24f, 0f);

            var row = UiKit.Rect(root, "Row"); row.anchorMin = Vector2.zero; row.anchorMax = Vector2.one; row.offsetMin = new Vector2(34f, 8f); row.offsetMax = new Vector2(-34f, 0f);
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = 0; h.padding = new RectOffset(0, 8, 0, 0); h.childAlignment = TextAnchor.MiddleLeft;
            h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;

            _bAway = MakeBarBlockGlass(row, "Away", false);
            DividerV(row);
            _bHome = MakeBarBlockGlass(row, "Home", true);
            DividerV(row);
            var dd = BarCell(row, "DownCell", 210f);
            _bDown = BarLabel(dd, "1ST & 10", 34, t.accent, TextAlignmentOptions.Center, "Down", true); _bDown.characterSpacing = 2f;
            var sp = BarCell(row, "SpotCell", 300f);
            _bSpot = BarLabel(sp, "BALL ON BUF 25", 28, Color.white, TextAlignmentOptions.Center, "Spot", true);
            BarCell(row, "Spacer", 0f, true);
            var q = BarCell(row, "QuarterCell", 96f);
            _bQuarter = BarLabel(q, "1ST", 34, t.accent, TextAlignmentOptions.Center, "Quarter");
            var c = BarCell(row, "ClockCell", 172f);
            _bClock = BarLabel(c, "5:00", 54, Color.white, TextAlignmentOptions.Center, "Clock");
            var pc = BarCell(row, "PlayClockCell", 92f);
            _bPlayClockBg = UiKit.Panel(pc, "Box", new Color(1f, 1f, 1f, 0.10f)); UiKit.Stretch(_bPlayClockBg.rectTransform, 6, 12, 6, 12);
            _bPlayClock = UiKit.Label(_bPlayClockBg.rectTransform, "25", 36, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "PlayClock"); UiKit.Stretch(_bPlayClock.rectTransform);
            var cam = BarCell(row, "CamCell", 150f);
            _bCam = BarLabel(cam, "BROADCAST", 17, UiColors.TextMuted, TextAlignmentOptions.MidlineRight, "Cam");

            // penalty flag strip: a rounded yellow pill above the bar (the hint bar sits above it)
            var fg = GlassKit.Solid(_root, "FlagStripBottom", FlagYellow, 12f, 0f); _flagStripB = fg.rectTransform;
            _flagTextB = UiKit.Label(_flagStripB, "", 26, Color.black, true, TextAlignmentOptions.MidlineLeft, "FlagText"); UiKit.Stretch(_flagTextB.rectTransform, 24, 0, 24, 0); _flagTextB.characterSpacing = 2f;
            _flagStripB.gameObject.SetActive(false);
        }

        // ============================================================================================ top-left block
        Block MakeBlockGlass(RectTransform parent, string name, Vector2 pos, bool local)
        {
            var t = GlassTheme.T;
            var b = new Block();
            var r = UiKit.Rect(parent, name); UiKit.Anchor(r, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(400, 54));
            b.Bg = UiKit.Panel(r, "TeamBar", local ? new Color(0.1f, 0.3f, 0.7f) : new Color(0.7f, 0.15f, 0.15f));
            UiKit.Anchor(b.Bg.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(5f, 44f));
            b.Crest = UiKit.Panel(r, "Crest", Color.white); b.Crest.preserveAspect = true;
            UiKit.Anchor(b.Crest.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(18f, 0f), new Vector2(42f, 42f));
            b.Abbr = UiKit.Label(r, "ABC", 34, Color.white, true, TextAlignmentOptions.MidlineLeft, "Abbr");
            UiKit.Anchor(b.Abbr.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(72f, 2f), new Vector2(140f, 50f)); b.Abbr.characterSpacing = 2f;
            b.Score = UiKit.Label(r, "0", 44, Color.white, true, TextAlignmentOptions.MidlineRight, "Score");
            UiKit.Anchor(b.Score.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-14f, 2f), new Vector2(90f, 54f));
            var poss = UiKit.Panel(r, "Poss", t.accent, Sp("sb_arrow")); poss.preserveAspect = true;
            UiKit.Anchor(poss.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(206f, 2f), new Vector2(24f, 24f)); b.Poss = poss.gameObject;
            b.Pips = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var p = UiKit.Panel(r, "TO" + i, Color.white);
                UiKit.Anchor(p.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(240f + i * 22f, 21f), new Vector2(17f, 5f));
                b.Pips[i] = p;
            }
            return b;
        }

        void BuildScorebugGlass()
        {
            var t = GlassTheme.T;
            var bug = UiKit.Rect(_root, "Scorebug"); UiKit.Anchor(bug, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -44), new Vector2(1000, 200));
            _scorebugRoot = bug;
            var glass = GlassKit.Panel(bug, "Glass", 1.25f, true, 16f);
            UiKit.Anchor(glass.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(1000f, 128f));
            _away = MakeBlockGlass(bug, "Away", new Vector2(10f, -8f), false);
            var div = GlassKit.Rule(bug, "RowRule", 0.35f); var dr = div.rectTransform; dr.anchorMin = dr.anchorMax = new Vector2(0f, 1f); dr.pivot = new Vector2(0f, 0.5f); dr.anchoredPosition = new Vector2(24f, -64f); dr.sizeDelta = new Vector2(360f, 1.4f);
            _home = MakeBlockGlass(bug, "Home", new Vector2(10f, -68f), true);
            var vd = GlassKit.Bar(bug, "ColDivider", new Color(t.edgeA.r, t.edgeA.g, t.edgeA.b, 0.30f), 1.5f, 92f); UiKit.Anchor(vd.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(430f, -18f), new Vector2(1.5f, 92f));
            _down = UiKit.Label(bug, "1ST & 10", 38, t.accent, true, TextAlignmentOptions.MidlineLeft, "Down"); _down.characterSpacing = 2f;
            UiKit.Anchor(_down.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(456f, -14f), new Vector2(280f, 50f)); UiKit.Fit(_down, 22f);
            _spot = UiKit.Label(bug, "BALL ON BUF 25", 26, Color.white, true, TextAlignmentOptions.MidlineLeft, "Spot");
            UiKit.Anchor(_spot.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(456f, -66f), new Vector2(280f, 40f)); UiKit.Fit(_spot, 16f);
            _quarter = UiKit.Label(bug, "1ST", 32, t.accent, true, TextAlignmentOptions.MidlineLeft, "Quarter");
            UiKit.Anchor(_quarter.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(748f, -16f), new Vector2(90f, 46f));
            _clock = UiKit.Label(bug, "5:00", 54, Color.white, true, TextAlignmentOptions.MidlineLeft, "Clock");
            UiKit.Anchor(_clock.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(748f, -58f), new Vector2(170f, 62f));
            _clockBg = UiKit.Panel(bug, "ClockBlock", new Color(0f, 0f, 0f, 0f)); UiKit.Anchor(_clockBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(740f, 0f), new Vector2(1f, 1f));
            _playClockBg = UiKit.Panel(bug, "PlayClockBg", new Color(1f, 1f, 1f, 0.10f));
            UiKit.Anchor(_playClockBg.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, -22f), new Vector2(72f, 84f));
            _playClock = UiKit.Label(_playClockBg.rectTransform, "25", 40, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "PlayClock"); UiKit.Stretch(_playClock.rectTransform);
            // penalty flag pill hangs under the block
            var fg = GlassKit.Solid(bug, "FlagStrip", FlagYellow, 12f, 0f); _flagStrip = fg.rectTransform;
            UiKit.Anchor(_flagStrip, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -140f), new Vector2(1000f, 44f));
            _flagText = UiKit.Label(_flagStrip, "", 26, Color.black, true, TextAlignmentOptions.MidlineLeft, "FlagText"); UiKit.Stretch(_flagText.rectTransform, 24, 0, 24, 0); _flagText.characterSpacing = 2f;
            _flagStrip.gameObject.SetActive(false);
            _camLabel = UiKit.Label(bug, "BROADCAST", 18, UiColors.TextMuted, true, TextAlignmentOptions.MidlineRight, "Cam");
            UiKit.Anchor(_camLabel.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -106f), new Vector2(300f, 22f));
        }

        // ============================================================================================ banner + toast
        void BuildBannerGlass()
        {
            var t = GlassTheme.T;
            _banner = UiKit.Rect(_root, "Banner"); UiKit.Anchor(_banner, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -250), new Vector2(1160, 176));
            var glass = GlassKit.Panel(_banner, "Glass", 1.6f, true, t.cornerRadiusLarge); UiKit.Stretch(glass.rectTransform);
            var baseRect = UiKit.Rect(_banner, "Base"); UiKit.Stretch(baseRect);
            _bannerBase = baseRect.gameObject.AddComponent<Image>(); _bannerBase.color = new Color(0f, 0f, 0f, 0f); _bannerBase.raycastTarget = false;
            // a soft wash of the event colour along the left (BannerRoutine tints it), the accent slab and the rail under the headline
            var wash = UiKit.Rect(_banner, "Tint"); UiKit.Stretch(wash, 4f, 4f, 4f, 4f);
            _bannerBg = wash.gameObject.AddComponent<Image>(); _bannerBg.sprite = GlassWidgets.HGradientSprite(); _bannerBg.raycastTarget = false; _bannerBg.color = new Color(1f, 1f, 1f, 0.2f);
            var slab = UiKit.Panel(_banner, "Slab", Color.white); UiKit.Anchor(slab.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18f, 0f), new Vector2(7f, 120f)); _bannerSlab = slab;
            var rail = UiKit.Panel(_banner, "Rail", Color.white); UiKit.Anchor(rail.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0f, 12f), new Vector2(240f, 4f)); _bannerRail = rail;
            _bannerText = UiKit.Label(_banner, "TOUCHDOWN", 84, Color.white, true, TextAlignmentOptions.Center, "Text");
            UiKit.Anchor(_bannerText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 16), new Vector2(980, 110)); _bannerText.characterSpacing = 6f; UiKit.Fit(_bannerText, 44);
            _bannerSub = UiKit.Label(_banner, "", 30, Color.white, true, TextAlignmentOptions.Center, "Sub");
            UiKit.Anchor(_bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -46), new Vector2(900, 44)); _bannerSub.characterSpacing = 3f; UiKit.Fit(_bannerSub, 20);
            _banner.gameObject.SetActive(false);

            _toast = UiKit.Rect(_root, "Toast"); UiKit.Anchor(_toast, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(800, 64));
            var tg = GlassKit.Panel(_toast, "Glass", 1.5f, true, 22f); UiKit.Stretch(tg.rectTransform);
            _toastText = UiKit.Label(_toast, "", 26, Color.white, true, TextAlignmentOptions.Center, "Text"); UiKit.Stretch(_toastText.rectTransform, 24, 0, 24, 0); UiKit.Fit(_toastText, 16);
            _toast.gameObject.SetActive(false);
        }

        // ============================================================================================ hint bar
        void BuildHintGlass()
        {
            var t = GlassTheme.T;
            var g = GlassKit.Panel(_root, "HintBar", 1.5f, true, 22f);
            var rt = g.rectTransform; UiKit.Anchor(rt, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(400, 54));
            _hintBar = g.gameObject;
            var h = _hintBar.AddComponent<HorizontalLayoutGroup>(); h.padding = new RectOffset(30, 30, 0, 0); h.spacing = 16; h.childAlignment = TextAnchor.MiddleCenter; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            var fit = _hintBar.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            _hintGroup = _hintBar.AddComponent<CanvasGroup>(); _hintGroup.blocksRaycasts = false; _hintGroup.interactable = false;
            _hint = UiKit.Label(rt, "", 24, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "Hint");
            _hint.characterSpacing = 1.5f;
            _hintRow = UiKit.Rect(rt, "HintRow");
            _hintFooter = PromptFooter.Attach(_hintRow, 38f, 23f, 28f);
            _hintRow.gameObject.SetActive(false);
        }

        // ============================================================================================ pause rail
        void BuildPauseGlass(RectTransform root)
        {
            var t = GlassTheme.T;
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.025f, 0.035f, 0.40f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            GlassWidgets.SideShade(root, true, 1100f, 0.5f);
            GlassWidgets.TopBar(root, "PAUSED");

            var fitHost = UiKit.Rect(root, "PauseFit"); UiKit.Stretch(fitHost);
            var pblock = UiKit.Rect(fitHost, "Block"); pblock.anchorMin = pblock.anchorMax = new Vector2(0f, 1f); pblock.pivot = new Vector2(0f, 1f); pblock.anchoredPosition = Vector2.zero; pblock.sizeDelta = new Vector2(t.safeMarginX + 780f, 150f + 760f);
            var card = GlassKit.Panel(pblock, "PauseCard", 1.2f, true);
            _pauseCardRt = card.rectTransform;
            UiKit.Anchor(card.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(t.safeMarginX, -150f), new Vector2(780f, 760f));
            var pfs = fitHost.gameObject.AddComponent<FitScale>(); pfs.Block = pblock; pfs.DesignSize = new Vector2(t.safeMarginX + 780f, 150f + 760f + 130f);   // shrink about the top-left corner on short canvases

            var title = GlassKit.Text(card.rectTransform, "PAUSED", GlassText.Title, TextAlignmentOptions.MidlineLeft, "Title"); GlassKit.Style(title, 52f, t.textPrimary, true, t.trackingTitle);
            UiKit.Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -22f), new Vector2(724f, 70f));
            var rule = GlassKit.Rule(card.rectTransform); var rr = rule.rectTransform; rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f); rr.pivot = new Vector2(0.5f, 1f); rr.anchoredPosition = new Vector2(0f, -100f); rr.sizeDelta = new Vector2(-56f, 1.4f);

            // the item list sits in a host that shrinks it to the room under the title on short canvases
            var host = UiKit.Rect(card.rectTransform, "ItemsHost"); host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = new Vector2(28f, 24f); host.offsetMax = new Vector2(-28f, -116f);
            var col = UiKit.Rect(host, "Items"); col.anchorMin = col.anchorMax = new Vector2(0f, 1f); col.pivot = new Vector2(0f, 1f); col.anchoredPosition = Vector2.zero; col.sizeDelta = new Vector2(724f, 700f);
            var fit = host.gameObject.AddComponent<FitScale>(); fit.Block = col; fit.DesignSize = new Vector2(724f, 700f);
            _pauseCol = col.gameObject;
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 8; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            const float rowH = 66f, w = 724f;
            _pauseResume = GlassKit.Button(col, "RESUME", () => { if (ResumeRequested != null) ResumeRequested(); }, GlassButtonStyle.Primary, w, rowH, "ui_arrow_right");
            _pauseReplay = GlassKit.Button(col, "INSTANT REPLAY", () => { if (ReplayRequested != null) ReplayRequested(); }, GlassButtonStyle.Secondary, w, rowH, "fb_film_reel");
            _pauseCine = UiControls.Selector(col, "Cinematics", PixSettings.CinematicsNames, PixSettings.Cinematics, i => { PixSettings.Cinematics = i; }, w, rowH);
            _pauseCam = UiControls.Selector(col, "Camera", new[] { "Classic", "Madden", "Wide", "Broadcast", "First person" }, 0, i => { if (CameraCycleRequested != null && _dir != null && _dir.Cams != null) { CameraMode m = i <= 2 ? CameraMode.Behind : (i == 3 ? CameraMode.Broadcast : CameraMode.FirstPerson); if (i <= 2) PixSettings.UserCameraStyle = (CameraStyle)i; _dir.Cams.SetMode(m, false); PixSettings.DefaultCamera = m; } }, w, rowH);
            _pauseLight = UiControls.Selector(col, "Lighting", new[] { "Night game", "Day game" }, 0, i => { if (LightingToggleRequested != null && _dir != null && _dir.Lighting != null && (int)_dir.Lighting.Current != i) LightingToggleRequested(); }, w, rowH);
            _pauseSb = UiControls.Selector(col, "Scorebug", PixSettings.ScorebugNames, (int)PixSettings.Scorebug, i => { PixSettings.Scorebug = (ScorebugStyle)i; ApplyScorebugLayout(); }, w, rowH);
            _pauseSettings = GlassKit.Button(col, "SOUND AND CONTROLS", ShowPauseSettings, GlassButtonStyle.Secondary, w, rowH, "ui_settings");
            GlassKit.Button(col, "QUIT TO MENU", ConfirmQuit, GlassButtonStyle.Secondary, w, rowH, "ui_close");

            var col2 = UiKit.Rect(host, "SettingsItems"); col2.anchorMin = col2.anchorMax = new Vector2(0f, 1f); col2.pivot = new Vector2(0f, 1f); col2.anchoredPosition = Vector2.zero; col2.sizeDelta = new Vector2(724f, 700f);
            _pauseSettingsCol = col2.gameObject;
            var v2 = col2.gameObject.AddComponent<VerticalLayoutGroup>(); v2.spacing = 8; v2.childControlWidth = false; v2.childControlHeight = false; v2.childForceExpandWidth = false; v2.childForceExpandHeight = false;
            _pauseVolFirst = UiControls.SliderRow(col2, "Master volume", PixSettings.MasterVolume, x => { PixSettings.MasterVolume = x; AudioListener.volume = x; }, w, rowH);
            UiControls.SliderRow(col2, "Crowd volume", PixSettings.CrowdVolume, x => { PixSettings.CrowdVolume = x; }, w, rowH);
            UiControls.SliderRow(col2, "Effects volume", PixSettings.SfxVolume, x => { PixSettings.SfxVolume = x; }, w, rowH);
            UiControls.Selector(col2, "Controller rumble", new[] { "On", "Off" }, PixSettings.Rumble ? 0 : 1, i => { PixSettings.Rumble = i == 0; }, w, rowH);
            UiControls.Selector(col2, "Camera shake on hits", new[] { "On", "Off" }, PixSettings.CameraShake ? 0 : 1, i => { PixSettings.CameraShake = i == 0; }, w, rowH);
            GlassKit.Button(col2, "BACK", HidePauseSettings, GlassButtonStyle.Secondary, w, rowH, "ui_arrow_left");
            _pauseSettingsCol.SetActive(false);

            // game state card (right): the two teams, the score, the quarter and clock (filled when the pause opens)
            BuildPauseStateCard(root);

            var foot = GlassWidgets.BottomBar(root, null);
            foot.Prompts.Set(PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_b", "BACK"), PromptFooter.Item("glyph_menu", "RESUME"), PromptFooter.Item("glyph_analog_all", "NAVIGATE"));
        }

        RectTransform _pauseCardRt;
        bool _hintWasOn, _sbWasOn, _bbWasOn, _underPauseHidden;

        /// <summary>Glass pause: the hint pill and the bottom scorebug sit exactly where the pause footer is, and the state card already shows the score, so they step aside while paused.</summary>
        void HideHudUnderGlassPause(bool paused)
        {
            if (paused == _underPauseHidden) return;
            _underPauseHidden = paused;
            if (paused)
            {
                _hintWasOn = _hintBar != null && _hintBar.activeSelf; _sbWasOn = _scorebugRoot != null && _scorebugRoot.gameObject.activeSelf;
                if (_hintBar != null) _hintBar.SetActive(false);
                if (_scorebugRoot != null) _scorebugRoot.gameObject.SetActive(false);
                _bbWasOn = _bottomRoot != null && _bottomRoot.gameObject.activeSelf; if (_bottomRoot != null) _bottomRoot.gameObject.SetActive(false);
            }
            else
            {
                if (_hintBar != null) _hintBar.SetActive(_hintWasOn);
                if (_scorebugRoot != null) _scorebugRoot.gameObject.SetActive(_sbWasOn);
                if (_bottomRoot != null) _bottomRoot.gameObject.SetActive(_bbWasOn);
            }
        }

        sealed class StateRow { public Image Crest, Bar; public TextMeshProUGUI Abbr, Score; }
        StateRow _stAway, _stHome; TextMeshProUGUI _stQuarter, _stClock, _stDown;

        void BuildPauseStateCard(RectTransform root)
        {
            var t = GlassTheme.T;
            var card = GlassWidgets.Card(root, "GAME STATE", 520f, 340f, "StateCard", true, 1.2f);
            UiKit.Anchor(card.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-t.safeMarginX, -150f), new Vector2(520f, 340f));
            _stAway = MakeStateRow(card.Body, 0f); _stHome = MakeStateRow(card.Body, -78f);
            var rule = GlassKit.Rule(card.Body); var rr = rule.rectTransform; rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f); rr.pivot = new Vector2(0.5f, 1f); rr.anchoredPosition = new Vector2(0f, -166f); rr.sizeDelta = new Vector2(0f, 1.4f);
            _stQuarter = GlassKit.Text(card.Body, "1ST", GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Quarter"); GlassKit.Style(_stQuarter, 34f, t.accent, true, 2f);
            UiKit.Anchor(_stQuarter.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -184f), new Vector2(120f, 48f));
            _stClock = GlassKit.Text(card.Body, "5:00", GlassText.Heading, TextAlignmentOptions.MidlineRight, "Clock"); GlassKit.Style(_stClock, 44f, t.textPrimary, true, 1f);
            UiKit.Anchor(_stClock.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -180f), new Vector2(220f, 56f));
            _stDown = GlassKit.Eyebrow(card.Body, "", TextAlignmentOptions.MidlineLeft, "Down"); GlassKit.Style(_stDown, 20f, t.textSecondary, true, 2f);
            UiKit.Anchor(_stDown.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -236f), new Vector2(440f, 30f));
        }

        StateRow MakeStateRow(RectTransform body, float y)
        {
            var t = GlassTheme.T;
            var s = new StateRow();
            s.Bar = UiKit.Panel(body, "TeamBar", Color.white); UiKit.Anchor(s.Bar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y - 36f), new Vector2(5f, 52f));
            s.Crest = UiKit.Panel(body, "Crest", Color.white); s.Crest.preserveAspect = true; UiKit.Anchor(s.Crest.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, y - 36f), new Vector2(54f, 54f));
            s.Abbr = GlassKit.Text(body, "ABC", GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Abbr"); GlassKit.Style(s.Abbr, 40f, t.textPrimary, true, 3f);
            UiKit.Anchor(s.Abbr.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(96f, y - 36f), new Vector2(220f, 56f));
            s.Score = GlassKit.Text(body, "0", GlassText.Title, TextAlignmentOptions.MidlineRight, "Score"); GlassKit.Style(s.Score, 56f, t.textPrimary, true);
            UiKit.Anchor(s.Score.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, y - 34f), new Vector2(160f, 64f));
            return s;
        }

        void FillStateCard()
        {
            if (_stAway == null || _lastSnap == null) return;
            var s = _lastSnap;
            FillStateRow(_stAway, s.AwayAbbr, s.AwayCrest, s.AwayColor, s.AwayScore); FillStateRow(_stHome, s.HomeAbbr, s.HomeCrest, s.HomeColor, s.HomeScore);
            _stQuarter.text = s.Quarter; _stClock.text = s.Clock; _stDown.text = (s.DownDistance + "   " + s.Spot).Trim();
        }

        void FillStateRow(StateRow r, string abbr, string crest, Color c, int score)
        {
            r.Abbr.text = abbr; r.Score.text = score.ToString(); var sp = Sp(crest); r.Crest.sprite = sp; r.Crest.enabled = sp != null; r.Bar.color = new Color(c.r, c.g, c.b, 1f);
        }

        // ============================================================================================ final screen
        void ShowFinalGlass(FinalData d, Action onContinue)
        {
            var t = GlassTheme.T;
            if (_finalRoot != null) Destroy(_finalRoot);
            var root = UiKit.Rect(_root, "Final"); UiKit.Stretch(root); _finalRoot = root.gameObject;
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.025f, 0.035f, 0.55f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            GlassWidgets.TopBar(root, "FINAL");
            var block = UiKit.Rect(root, "Block"); block.anchorMin = block.anchorMax = new Vector2(0.5f, 0.5f); block.pivot = new Vector2(0.5f, 0.5f); block.anchoredPosition = new Vector2(0f, -30f); block.sizeDelta = new Vector2(1500, 960);
            var fit = root.gameObject.AddComponent<FitScale>(); fit.Block = block; fit.DesignSize = new Vector2(1500, 960);

            var head = GlassKit.Text(block, d.Headline, GlassText.Hero, TextAlignmentOptions.Center, "Headline");
            GlassKit.Style(head, 84f, d.Tie ? t.textPrimary : (d.UserWon ? t.accent : t.textPrimary), true, 8f);
            UiKit.Anchor(head.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -4f), new Vector2(1500f, 104f)); UiKit.Fit(head, 56f);

            // line score card
            var line = GlassKit.Panel(block, "LineScore", 1.3f, true, t.cornerRadiusLarge);
            UiKit.Anchor(line.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -120f), new Vector2(1300f, 250f));
            string[] qn = { "1", "2", "3", "4", "OT" };
            for (int i = 0; i < 5; i++)
            {
                var hl = GlassKit.Eyebrow(line.rectTransform, qn[i], TextAlignmentOptions.Center, "QH" + i); GlassKit.Style(hl, 22f, t.textSecondary, true, 3f);
                UiKit.Anchor(hl.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(QuarterCellX(i), -14f), new Vector2(90f, 32f));
            }
            FinalRowGlass(line.rectTransform, -44f, d.AwayAbbr, d.AwayName, d.AwayCrest, d.AwayColor, d.AwayByQuarter, d.AwayScore, d.AwayScore > d.HomeScore);
            FinalRowGlass(line.rectTransform, -140f, d.HomeAbbr, d.HomeName, d.HomeCrest, d.HomeColor, d.HomeByQuarter, d.HomeScore, d.HomeScore > d.AwayScore);

            // stats card
            var A = d.AwayStats ?? new TeamStats(); var H = d.HomeStats ?? new TeamStats();
            string[][] rows =
            {
                new[] { "TOTAL YARDS", A.TotalYards.ToString(), H.TotalYards.ToString() },
                new[] { "PASSING YARDS", A.PassYards.ToString(), H.PassYards.ToString() },
                new[] { "RUSHING YARDS", A.RushYards.ToString(), H.RushYards.ToString() },
                new[] { "COMP / ATT", A.PassComp + "/" + A.PassAtt, H.PassComp + "/" + H.PassAtt },
                new[] { "SACKS", A.Sacks.ToString(), H.Sacks.ToString() },
                new[] { "FIRST DOWNS", A.FirstDowns.ToString(), H.FirstDowns.ToString() },
                new[] { "3RD DOWN", A.ThirdDownConv + "/" + A.ThirdDownAtt, H.ThirdDownConv + "/" + H.ThirdDownAtt },
                new[] { "TURNOVERS", A.Turnovers.ToString(), H.Turnovers.ToString() },
                new[] { "PENALTIES", A.Penalties + " - " + A.PenaltyYards + " YDS", H.Penalties + " - " + H.PenaltyYards + " YDS" },
            };
            var stats = GlassWidgets.Card(block, "TEAM STATS", 980f, 60f + rows.Length * 38f + 30f, "StatsCard", true, 1.3f);
            UiKit.Anchor(stats.Root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -388f), new Vector2(980f, 60f + rows.Length * 38f + 30f));
            var col = GlassKit.Column(stats.Body, "Rows", 0f); UiKit.Stretch((RectTransform)col.transform);
            for (int i = 0; i < rows.Length; i++) GlassWidgets.CompareRow(col.transform, rows[i][1], rows[i][0], rows[i][2], 924f, 38f, i % 2 == 0);

            var btn = GlassKit.Button(block, "CONTINUE", () => { if (onContinue != null) onContinue(); }, GlassButtonStyle.Primary, 560f, 84f, "ui_arrow_right");
            UiKit.Anchor((RectTransform)btn.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(560f, 84f));
            _finalRoot.transform.SetAsLastSibling(); if (_fade != null) _fade.transform.SetAsLastSibling();
            StartCoroutine(SelectLater(btn.gameObject));
        }

        void FinalRowGlass(RectTransform parent, float y, string abbr, string name, string crest, Color color, int[] byQ, int total, bool winner)
        {
            var t = GlassTheme.T;
            var r = UiKit.Rect(parent, "TeamRow_" + abbr); UiKit.Anchor(r, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(1300f, 100f));
            var bar = UiKit.Panel(r, "TeamBar", new Color(color.r, color.g, color.b, 1f)); UiKit.Anchor(bar.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(6f, 72f));
            var cr = UiKit.Panel(r, "Crest", Color.white, Sp(crest)); cr.preserveAspect = true; cr.enabled = cr.sprite != null;
            UiKit.Anchor(cr.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(50f, 0f), new Vector2(76f, 76f));
            var n = GlassKit.Text(r, name.ToUpperInvariant(), GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Name"); GlassKit.Style(n, 36f, t.textPrimary, true, 2f);
            UiKit.Anchor(n.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(150f, 0f), new Vector2(430f, 60f)); UiKit.Fit(n, 20f);
            int[] vals = LineScoreCells(byQ);
            for (int i = 0; i < 5; i++)
            {
                var ql = GlassKit.Text(r, vals[i].ToString(), GlassText.Heading, TextAlignmentOptions.Center, "Q" + i); GlassKit.Style(ql, 34f, t.textSecondary, true);
                UiKit.Anchor(ql.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(QuarterCellX(i), 0f), new Vector2(90f, 56f));
            }
            var tt = GlassKit.Text(r, total.ToString(), GlassText.Title, TextAlignmentOptions.MidlineRight, "Total"); GlassKit.Style(tt, 80f, winner ? t.accent : t.textPrimary, true);
            UiKit.Anchor(tt.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-40f, 0f), new Vector2(180f, 92f));
        }

        // ============================================================================================ toss caption
        void BuildTossCaptionGlass()
        {
            var t = GlassTheme.T;
            var bar = UiKit.Rect(_root, "TossCaption");
            UiKit.Anchor(bar, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 44f), new Vector2(1500f, 72f));
            var g = GlassKit.Panel(bar, "Glass", 1.5f, true, 20f); UiKit.Stretch(g.rectTransform);
            var accent = GlassKit.Solid(bar, "Accent", t.accent, 3f); UiKit.Anchor(accent.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f, 0f), new Vector2(6f, 40f));
            _tossCaptionText = UiKit.Label(bar, "", 34, Color.white, true, TextAlignmentOptions.MidlineLeft, "Text");
            UiKit.Stretch(_tossCaptionText.rectTransform, 52, 0, 40, 0); _tossCaptionText.characterSpacing = 1.5f; UiKit.Fit(_tossCaptionText, 20f);
            _tossCaption = bar;
            bar.gameObject.SetActive(false);
        }
    }
}
