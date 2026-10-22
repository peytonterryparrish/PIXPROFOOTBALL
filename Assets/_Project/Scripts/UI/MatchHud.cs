using System;
using System.Collections;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// The in-game presentation layer for a match, built in code in the UI-kit style: scorebug (crests, score, quarter, clock, play clock, down and distance,
    /// timeouts, possession), event banners, the pre-snap play-call panel, receiver button icons, coin toss, final score and the pause menu.
    /// </summary>
    public partial class MatchHud : MonoBehaviour, IMatchPresentation
    {
        // ---- interface events ----
        public event Action ResumeRequested, QuitRequested, CameraCycleRequested, LightingToggleRequested, ReplayRequested;
        public bool PauseOpen { get { return _pauseRoot != null && _pauseRoot.activeSelf; } }
        public Transform HudRoot { get { return _root; } }
        /// <summary>True while a full-screen panel owns the screen (call screen, coin toss, final score, penalty panel, pause): TV overlays and chrome step aside.</summary>
        /// <summary>True while an event banner (TOUCHDOWN, FIRST DOWN ...) is on screen; the TV cards wait for it to clear.</summary>
        public bool BannerVisible { get { return _banner != null && _banner.gameObject.activeSelf; } }
        public bool ModalOpen { get { return _callShown || _tossRoot != null || _finalRoot != null || PauseOpen || (_penalty != null && _penalty.IsOpen); } }
        bool _replayMode, _hudMinimal;
        Button _pauseReplay;
        OptionSelector _pauseCine;

        MatchDirector _dir;
        Canvas _canvas; RectTransform _root;
        // scorebug
        sealed class Block { public Image Bg, Crest; public TextMeshProUGUI Abbr, Score; public GameObject Poss; public Image[] Pips; public bool Solid; }
        Block _away, _home;
        TextMeshProUGUI _quarter, _clock, _playClock, _down, _spot, _inPlay;
        Image _clockBg, _playClockBg;
        // banner
        RectTransform _banner; Image _bannerBg, _bannerBase, _bannerSlab, _bannerRail; TextMeshProUGUI _bannerText, _bannerSub; Coroutine _bannerCo;
        RectTransform _toast; TextMeshProUGUI _toastText; Coroutine _toastCo;
        // play call / pre-snap / penalty (v0.3)
        PlayCallScreen _callScreen; PlayCallStrip _callStrip; PreSnapPanel _preSnap; PenaltyPanel _penalty; Action<bool> _penaltyDecision;
        bool _callShown, _chromeVisible = true;
        RectTransform _scorebugRoot; GameObject _hintBar; RectTransform _flagStrip; TextMeshProUGUI _flagText;
        // coin toss / final / pause
        GameObject _tossRoot, _finalRoot, _pauseRoot;
        TextMeshProUGUI _hint, _camLabel;
        Image _fade;
        readonly List<GameObject> _iconPool = new List<GameObject>();
        OptionSelector _pauseCam, _pauseLight;
        Button _pauseResume;
        RectTransform _iconLayer;

        // ============================================================================================ build
        public void Build(MatchDirector director)
        {
            _dir = director;
            UiKit.EnsureEventSystem();
            _canvas = UiKit.CreateCanvas("MatchHud", 20, transform);
            _root = (RectTransform)_canvas.transform;
            _glassHud = UiTheme.IsGlass;
            _iconLayer = UiKit.Rect(_root, "ReceiverIcons"); UiKit.Stretch(_iconLayer);
            BuildScorebug();
            BuildBottomBar();
            BuildBanner();
            BuildHint();
            // v0.7: the glass theme opens the call sheet (an overlay strip over the live field); Classic keeps the full-screen three-stage page
            MatchFlow.UseCallSheet = _glassHud;
            _callScreen = PlayCallScreen.Create(_root); _callScreen.Hide();
            if (_glassHud) { _callStrip = PlayCallStrip.Create(_root); _callStrip.Hide(); }
            _preSnap = PreSnapPanel.Create(_root); _preSnap.Hide();
            _penalty = PenaltyPanel.Create(_root);
            _kick = KickHud.Create(_root);
            _fade = UiKit.Panel(_root, "Fade", new Color(0, 0, 0, 0)); UiKit.Stretch(_fade.rectTransform); _fade.raycastTarget = false;
            _fade.transform.SetAsLastSibling();
            BuildPause();
            ApplyScorebugLayout();
            if (_glassHud) PixFootball.UI.Glass.GlassSkinWatcher.Watch(_canvas.gameObject);
        }

        Sprite Sp(string k) { return UiKit.Sprite(k); }

        static Color Readable(Color c) { float l = 0.299f * c.r + 0.587f * c.g + 0.114f * c.b; return l > 0.55f ? new Color(0.06f, 0.07f, 0.1f) : Color.white; }

        // ---------------------------------------------------------------------------------------- scorebug
        Block MakeBlock(RectTransform parent, string name, Vector2 pos, bool local)
        {
            var b = new Block();
            var r = UiKit.Rect(parent, name); UiKit.Anchor(r, new Vector2(0, 1), new Vector2(0, 1), pos, new Vector2(430, 84));
            b.Bg = r.gameObject.AddComponent<Image>(); b.Bg.sprite = Sp(local ? "sb_local" : "sb_visitor"); b.Bg.type = Image.Type.Simple; b.Bg.raycastTarget = false;
            if (b.Bg.sprite == null) b.Bg.color = new Color(0.15f, 0.16f, 0.2f, 1f);
            b.Crest = UiKit.Panel(r, "Crest", Color.white); b.Crest.preserveAspect = true;
            UiKit.Anchor(b.Crest.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(58, 0), new Vector2(60, 60));
            b.Abbr = UiKit.Label(r, "ABC", 44, Color.white, true, TextAlignmentOptions.MidlineLeft, "Abbr");
            UiKit.Anchor(b.Abbr.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(122, 4), new Vector2(170, 60)); b.Abbr.characterSpacing = 2f;
            b.Score = UiKit.Label(r, "0", 58, Color.white, true, TextAlignmentOptions.MidlineRight, "Score");
            UiKit.Anchor(b.Score.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-66, 4), new Vector2(110, 70));
            var poss = UiKit.Panel(r, "Poss", UiColors.Gold, Sp("sb_arrow")); poss.preserveAspect = true; poss.rectTransform.localRotation = Quaternion.Euler(0, 0, 0);
            UiKit.Anchor(poss.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 2), new Vector2(30, 30)); b.Poss = poss.gameObject;
            b.Pips = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                var p = UiKit.Panel(r, "TO" + i, UiColors.Gold, Sp("sb_to_on")); p.preserveAspect = true;
                UiKit.Anchor(p.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(124 + i * 34, 6), new Vector2(26, 10));   // the kit timeout pip at a size that reads from the couch (QA-756)
                b.Pips[i] = p;
            }
            return b;
        }

        void BuildScorebug()
        {
            if (_glassHud) { BuildScorebugGlass(); return; }
            var bug = UiKit.Rect(_root, "Scorebug"); UiKit.Anchor(bug, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -44), new Vector2(1240, 140));
            _scorebugRoot = bug;
            _away = MakeBlock(bug, "Away", new Vector2(0, 0), false);
            _home = MakeBlock(bug, "Home", new Vector2(420, 0), true);
            // clock block
            var cb = UiKit.Panel(bug, "ClockBlock", new Color(0.07f, 0.08f, 0.11f, 1f)); _clockBg = cb;
            UiKit.Anchor(cb.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(850, 0), new Vector2(320, 84));
            var edge = UiKit.Panel(cb.rectTransform, "Edge", UiColors.Gold); UiKit.Anchor(edge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(6, 84)); edge.rectTransform.anchorMax = new Vector2(0, 1); edge.rectTransform.sizeDelta = new Vector2(6, 0);
            _quarter = UiKit.Label(cb.rectTransform, "1ST", 34, UiColors.Gold, true, TextAlignmentOptions.MidlineLeft, "Quarter");
            UiKit.Anchor(_quarter.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24, 2), new Vector2(90, 50));
            _clock = UiKit.Label(cb.rectTransform, "5:00", 60, Color.white, true, TextAlignmentOptions.MidlineLeft, "Clock");
            UiKit.Anchor(_clock.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(108, 2), new Vector2(150, 70));
            _playClockBg = UiKit.Panel(cb.rectTransform, "PlayClockBg", new Color(1f, 1f, 1f, 0.12f));
            UiKit.Anchor(_playClockBg.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(64, 56));
            _playClock = UiKit.Label(_playClockBg.rectTransform, "25", 36, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "PlayClock"); UiKit.Stretch(_playClock.rectTransform);
            // lower strip: down and distance, ball spot
            var strip = UiKit.Panel(bug, "Strip", new Color(0.05f, 0.06f, 0.09f, 1f));   // opaque: the sponsor boards no longer bleed through the numbers (QA-025 / 229 / 045)
            UiKit.Anchor(strip.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -84), new Vector2(1170, 46));
            _down = UiKit.Label(strip.rectTransform, "1ST & 10", 30, UiColors.Gold, true, TextAlignmentOptions.MidlineLeft, "Down");
            UiKit.Anchor(_down.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(24, 0), new Vector2(300, 44)); _down.characterSpacing = 2f;
            _spot = UiKit.Label(strip.rectTransform, "BALL ON BUF 25", 26, Color.white, true, TextAlignmentOptions.MidlineLeft, "Spot");
            UiKit.Anchor(_spot.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(330, 0), new Vector2(420, 44));
            // flag strip (penalty announcements) hangs under the scorebug
            _flagStrip = UiKit.Rect(bug, "FlagStrip"); UiKit.Anchor(_flagStrip, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -130), new Vector2(1170, 44));
            var fimg = _flagStrip.gameObject.AddComponent<Image>(); fimg.color = new Color(1f, 0.918f, 0f, 1f); fimg.raycastTarget = false;
            _flagText = UiKit.Label(_flagStrip, "", 28, Color.black, true, TextAlignmentOptions.MidlineLeft, "FlagText"); UiKit.Stretch(_flagText.rectTransform, 24, 0, 24, 0); _flagText.characterSpacing = 2f;
            _flagStrip.gameObject.SetActive(false);
            _camLabel = UiKit.Label(strip.rectTransform, "BROADCAST", 26, UiColors.TextSecondary, true, TextAlignmentOptions.MidlineRight, "Cam");
            UiKit.Anchor(_camLabel.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(300, 44));
        }

        public void SetScore(ScoreSnapshot s)
        {
            if (_home == null || s == null) return;
            FillBottom(s);
            Fill(_home, s.HomeAbbr, s.HomeCrest, s.HomeColor, s.HomeScore, s.HomeTimeouts, s.PossessionHome);
            Fill(_away, s.AwayAbbr, s.AwayCrest, s.AwayColor, s.AwayScore, s.AwayTimeouts, !s.PossessionHome);
            _quarter.text = s.Quarter; _clock.text = s.Clock;
            _clock.color = s.TwoMinute ? UiColors.Orange : Color.white;
            _playClock.text = s.PlayClock >= 0 ? s.PlayClock.ToString() : "--";
            _playClock.color = s.PlayClock >= 0 && s.PlayClock <= 5 ? UiColors.Danger : UiColors.TextSecondary;
            _down.text = s.DownDistance; _spot.text = s.Spot;
            bool flag = !string.IsNullOrEmpty(s.FlagText);
            UpdateFlag(flag, s.FlagText);
        }

        void Fill(Block b, string abbr, string crest, Color color, int score, int timeouts, bool poss)
        {
            b.Abbr.text = abbr; b.Score.text = score.ToString();
            var sp = Sp(crest); b.Crest.sprite = sp; b.Crest.enabled = sp != null;
            Color c = color;
            if (b.Bg.sprite != null || _glassHud) b.Bg.color = new Color(c.r, c.g, c.b, 1f);
            Color fg = !_glassHud && b.Bg.sprite != null ? Readable(c) : Color.white;   // light kits (yellow, white, silver) get dark text (QA-748)
            b.Abbr.color = fg; b.Score.color = fg;
            b.Poss.SetActive(poss);
            for (int i = 0; i < 3; i++) b.Pips[i].color = i < timeouts ? UiColors.Gold : new Color(1f, 1f, 1f, 0.18f);
        }

        public void SetCameraLabel(string label) { if (_camLabel != null) _camLabel.text = label; if (_bCam != null) _bCam.text = label; }

        // ---------------------------------------------------------------------------------------- banner and toast
        void BuildBanner()
        {
            if (_glassHud) { BuildBannerGlass(); return; }
            _banner = UiKit.Rect(_root, "Banner"); UiKit.Anchor(_banner, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -250), new Vector2(1160, 176));
            // fully opaque base (the glassy kit art is only used as a subtle tint on top) so the headline always reads
            var baseRect = UiKit.Rect(_banner, "Base"); UiKit.Stretch(baseRect);
            _bannerBase = baseRect.gameObject.AddComponent<Image>(); _bannerBase.color = new Color(0.04f, 0.05f, 0.09f, 1f); _bannerBase.raycastTarget = false;
            var topRect = UiKit.Rect(_banner, "Tint"); UiKit.Stretch(topRect);
            _bannerBg = topRect.gameObject.AddComponent<Image>(); _bannerBg.sprite = Sp("sb_notification"); _bannerBg.raycastTarget = false;
            var slab = UiKit.Panel(_banner, "Slab", Color.white); UiKit.Anchor(slab.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(26, 0)); slab.rectTransform.anchorMax = new Vector2(0, 1); slab.rectTransform.sizeDelta = new Vector2(26, 0); _bannerSlab = slab;
            var rail = UiKit.Panel(_banner, "Rail", Color.white); UiKit.Anchor(rail.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 8)); rail.rectTransform.anchorMax = new Vector2(1, 0); rail.rectTransform.sizeDelta = new Vector2(0, 8); _bannerRail = rail;
            _bannerText = UiKit.Label(_banner, "TOUCHDOWN", 92, Color.white, true, TextAlignmentOptions.Center, "Text");
            UiKit.Anchor(_bannerText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 14), new Vector2(1000, 120)); _bannerText.characterSpacing = 6f; UiKit.Fit(_bannerText, 52);   // long team names ("... TIMEOUT") shrink instead of spilling past the plate (QA-885)
            _bannerSub = UiKit.Label(_banner, "", 34, Color.white, true, TextAlignmentOptions.Center, "Sub");
            UiKit.Anchor(_bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -52), new Vector2(900, 50)); _bannerSub.characterSpacing = 3f; UiKit.Fit(_bannerSub, 22);
            _banner.gameObject.SetActive(false);

            _toast = UiKit.Rect(_root, "Toast"); UiKit.Anchor(_toast, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 150), new Vector2(800, 60));
            var tb = _toast.gameObject.AddComponent<Image>(); tb.color = new Color(0.05f, 0.06f, 0.09f, 0.85f); tb.raycastTarget = false;
            _toastText = UiKit.Label(_toast, "", 30, Color.white, true, TextAlignmentOptions.Center, "Text"); UiKit.Stretch(_toastText.rectTransform);
            _toast.gameObject.SetActive(false);
        }

        /// <summary>The event banner sits below whichever scorebug is showing: the top-left block and its flag strip reach y=-174, the bottom bar leaves the top free (QA-746).</summary>
        float BannerY { get { return _bottomActive ? -250f : -316f; } }

        public void ShowBanner(string text, Color color, float seconds, string sub)
        {
            if (_banner == null) return;
            if (_bannerCo != null) StopCoroutine(_bannerCo);
            _bannerCo = StartCoroutine(BannerRoutine(text, color, seconds, sub));
        }

        IEnumerator BannerRoutine(string text, Color color, float seconds, string sub)
        {
            _banner.gameObject.SetActive(true);
            _bannerBg.color = new Color(color.r, color.g, color.b, 0.30f);
            _bannerSlab.color = color; _bannerRail.color = color;
            Color sc = Color.Lerp(color, Color.white, 0.45f);
            _bannerText.text = text; _bannerText.color = Color.white; _bannerSub.text = sub ?? ""; _bannerSub.color = sc;
            var cg = UiKit.GetOrAdd<CanvasGroup>(_banner.gameObject);
            float t = 0f;
            while (t < 0.28f) { t += Time.unscaledDeltaTime; float u = Mathf.SmoothStep(0, 1, t / 0.28f); _banner.anchoredPosition = new Vector2(Mathf.Lerp(-900f, 0f, u), BannerY); cg.alpha = u; yield return null; }
            _banner.anchoredPosition = new Vector2(0f, BannerY); cg.alpha = 1f;
            t = 0f; while (t < seconds - 0.5f) { t += Time.unscaledDeltaTime; yield return null; }
            t = 0f;
            while (t < 0.25f) { t += Time.unscaledDeltaTime; float u = t / 0.25f; cg.alpha = 1f - u; _banner.anchoredPosition = new Vector2(Mathf.Lerp(0f, 500f, u), BannerY); yield return null; }
            _banner.gameObject.SetActive(false);
        }

        public void ShowToast(string text, float seconds)
        {
            if (_toast == null) return;
            if (_toastCo != null) StopCoroutine(_toastCo);
            _toastCo = StartCoroutine(ToastRoutine(text, seconds));
        }

        IEnumerator ToastRoutine(string text, float seconds)
        {
            _toast.gameObject.SetActive(true); _toastText.text = text;
            float t = 0f; while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
            _toast.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------------------------------- hint bar and fade
        // The hint bar sizes itself to its content (text or glyph pills) and fades after a few quiet seconds. Hints are written with button tokens ([A] [LB] [RS] ...); the bar draws controller glyphs or
        // keycaps for the device in use (QA-790 / 791 / 35 / 157 / 93 / 240).
        RectTransform _hintRow; PromptFooter _hintFooter; CanvasGroup _hintGroup; string _hintRaw = ""; float _hintAge; int _hintScheme = -1;

        void BuildHint()
        {
            if (_glassHud) { BuildHintGlass(); return; }
            var bar = UiKit.Panel(_root, "HintBar", new Color(0.04f, 0.05f, 0.08f, 0.78f));
            UiKit.Anchor(bar.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 36), new Vector2(400, 54));
            _hintBar = bar.gameObject;
            var h = _hintBar.AddComponent<HorizontalLayoutGroup>(); h.padding = new RectOffset(28, 28, 0, 0); h.spacing = 16; h.childAlignment = TextAnchor.MiddleCenter; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = true;
            var fit = _hintBar.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            _hintGroup = _hintBar.AddComponent<CanvasGroup>(); _hintGroup.blocksRaycasts = false; _hintGroup.interactable = false;
            _hint = UiKit.Label(bar.rectTransform, "", 26, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "Hint");
            _hint.characterSpacing = 1.5f;
            _hintRow = UiKit.Rect(bar.rectTransform, "HintRow");
            _hintFooter = PromptFooter.Attach(_hintRow, 40f, 25f, 30f);
            _hintRow.gameObject.SetActive(false);
        }

        public void SetHint(string hint)
        {
            if (_hint == null) return;
            hint = hint ?? "";
            if (hint == _hintRaw && _hintScheme == (InputDeviceTracker.GamepadActive ? 1 : 0)) return;
            if (hint != _hintRaw) _hintAge = 0f;
            _hintRaw = hint;
            RenderHint();
        }

        void RenderHint()
        {
            _hintScheme = InputDeviceTracker.GamepadActive ? 1 : 0;
            var items = UiPrompts.ParseHint(_hintRaw);
            bool glyphs = false; foreach (var it in items) if (it.Glyphs.Length > 0) { glyphs = true; break; }
            if (glyphs) { _hintFooter.SetItems(items); _hint.text = ""; }
            else _hint.text = _hintRaw;
            _hint.gameObject.SetActive(!glyphs); _hintRow.gameObject.SetActive(glyphs);
            _hintBar.SetActive(!string.IsNullOrEmpty(_hintRaw) && _chromeVisible);
        }

        public void SetFade(float alpha) { if (_fade != null) _fade.color = new Color(0, 0, 0, Mathf.Clamp01(alpha)); }

        // ---------------------------------------------------------------------------------------- play call screen (v0.3)
        public void ShowPlayCall(PlayCallModel model, float playClock)
        {
            if (_callScreen == null) return;
            if (!_callShown) { _callShown = true; SetHint(""); }
            Component view = _callScreen;
            if (model.SheetMode && _callStrip != null) { if (_callScreen.gameObject.activeSelf) _callScreen.Hide(); _callStrip.Show(model, playClock); view = _callStrip; }
            else { if (_callStrip != null && _callStrip.gameObject.activeSelf) _callStrip.Hide(); _callScreen.Show(model, playClock); }
            // reorder only when needed (this runs every frame while the call screen is open; a sibling change rebatches the whole canvas)
            int last = _root.childCount - 1;
            if (_fade != null && view.transform.GetSiblingIndex() != last - 1) { view.transform.SetAsLastSibling(); _fade.transform.SetAsLastSibling(); }
        }

        public void HidePlayCall()
        {
            if (_callScreen != null) _callScreen.Hide();
            if (_callStrip != null) _callStrip.Hide();
            _callShown = false;
            if (_tossRoot != null) Destroy(_tossRoot);
        }

        public void SetPreSnap(PreSnapHud hud)
        {
            if (_preSnap == null) return;
            if (hud == null) { _preSnap.Hide(); return; }
            _preSnap.Show(hud);
            if (_preSnap.transform.GetSiblingIndex() < _root.childCount - 2) _preSnap.transform.SetSiblingIndex(_root.childCount - 2);
            SetHint("");
        }

        public void ShowPenalty(PenaltyPanelData data, Action<bool> onDecision)
        {
            if (_penalty == null) return;
            _penalty.Show(data, onDecision);
            _penaltyDecision = onDecision;
            if (_fade != null) _fade.transform.SetAsLastSibling();
        }

        public void RefreshPenalty(PenaltyPanelData data) { if (_penalty != null) _penalty.Refresh(data); }
        public void HidePenalty() { if (_penalty != null) _penalty.Hide(); _penaltyDecision = null; }

        static string KeyFor(Btn b) { return UiPrompts.KeyOf(GlyphKey(b)); }   // one glyph -> key table (QA-770)
        static string GlyphKey(Btn b) { return "glyph_" + b.ToString().ToLowerInvariant(); }

        Transform MakeButtonIcon(Transform parent, Btn b, float size)
        {
            var holder = UiKit.Rect(parent, "Icon_" + b);
            holder.sizeDelta = new Vector2(size * 1.4f, size);
            if (MatchInput.GamepadActive)
            {
                var g = UiKit.Glyph(holder, GlyphKey(b), size);
                UiKit.Anchor(g.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, g.rectTransform.sizeDelta);
            }
            else
            {
                var key = UiKit.Panel(holder, "Key", new Color(0.92f, 0.92f, 0.95f)); UiKit.Anchor(key.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 1.35f, size * 0.85f));
                var t = UiKit.Label(key.rectTransform, KeyFor(b), size * 0.42f, new Color(0.08f, 0.09f, 0.12f), true, TextAlignmentOptions.Center, "KeyText"); UiKit.Stretch(t.rectTransform);
            }
            return holder;
        }

        void LateUpdate()
        {
            // full-screen panels hide the scorebug, banner and hint bar (nothing may show through the final screen, the coin toss, the call screen or the pause menu)
            bool want = !(_callShown || _tossRoot != null || _finalRoot != null || PauseOpen);
            if (want != _chromeVisible) SetChrome(want);
            if (_hintGroup != null)
            {
                _hintAge += Time.unscaledDeltaTime;
                _hintGroup.alpha = Mathf.MoveTowards(_hintGroup.alpha, _hintAge > 8f ? 0.5f : 1f, Time.unscaledDeltaTime * 1.5f);   // the long controls line steps back after a few seconds
                if (_hintScheme != (InputDeviceTracker.GamepadActive ? 1 : 0) && !string.IsNullOrEmpty(_hintRaw)) RenderHint();
            }
        }

        /// <summary>Full-screen panels (play call, coin toss, final, pause) hide the scorebug, banner and hint bar so nothing shows through them.</summary>
        void SetChrome(bool visible)
        {
            if (_scorebugRoot != null) _scorebugRoot.gameObject.SetActive(visible);
            if (!visible) UpdateFlag(false, null);
            if (_hintBar != null) _hintBar.SetActive(visible && !string.IsNullOrEmpty(_hintRaw));
            if (_banner != null && !visible) _banner.gameObject.SetActive(false);
            _chromeVisible = visible;
            if (_kick != null) _kick.SetChrome(visible);
            ApplyScorebugLayout();
        }
        // ---------------------------------------------------------------------------------------- receiver icons
        readonly List<Image> _leaders = new List<Image>();
        readonly List<Vector2> _iconPos = new List<Vector2>(), _iconAnchor = new List<Vector2>();
        readonly List<bool> _iconOk = new List<bool>();

        public void SetReceiverIcons(List<ReceiverIcon> icons, Camera cam)
        {
            int n = icons == null ? 0 : icons.Count;
            while (_iconPool.Count < n) _iconPool.Add(MakeReceiverIcon());
            for (int i = 0; i < _iconPool.Count; i++) _iconPool[i].SetActive(i < n);
            for (int i = 0; i < _leaders.Count; i++) _leaders[i].gameObject.SetActive(false);
            if (n == 0 || cam == null) return;
            _iconPos.Clear(); _iconAnchor.Clear(); _iconOk.Clear();
            for (int i = 0; i < n; i++)
            {
                Vector3 sp = cam.WorldToScreenPoint(icons[i].World);
                Vector2 local = Vector2.zero; bool ok = sp.z >= 0f;
                if (ok) RectTransformUtility.ScreenPointToLocalPointInRectangle(_iconLayer, sp, null, out local);
                _iconPos.Add(local); _iconAnchor.Add(local); _iconOk.Add(ok);
            }
            SeparateIcons();
            for (int i = 0; i < n; i++)
            {
                var go = _iconPool[i];
                if (!_iconOk[i]) { go.SetActive(false); continue; }
                ((RectTransform)go.transform).anchoredPosition = _iconPos[i];
                UpdateIconGlyph(go, icons[i].Button, icons[i].Arrow);
                var ring = go.transform.GetChild(0).GetComponent<Image>();
                if (ring != null) ring.color = icons[i].Highlight ? new Color(1f, 0.85f, 0.2f, 1f) : (ring.sprite != null ? Color.white : new Color(0.55f, 0.1f, 1f, 0.8f));
                go.transform.localScale = Vector3.one * (icons[i].Highlight ? 1.18f : 1f);
                // a thin leader from the displaced icon back to its player
                if ((_iconPos[i] - _iconAnchor[i]).magnitude > 18f) Leader(i, _iconAnchor[i], _iconPos[i]);
            }
        }

        /// <summary>Pushes overlapping icons apart (and back inside the screen) so the receiver buttons never stack on top of each other.</summary>
        void SeparateIcons()
        {
            const float minDist = 100f, margin = 62f;
            Rect r = _iconLayer.rect;
            float xMin = r.xMin + margin, xMax = r.xMax - margin, yMin = r.yMin + margin + 120f, yMax = r.yMax - margin - 190f;
            int n = _iconPos.Count;
            for (int pass = 0; pass < 24; pass++)
            {
                bool moved = false;
                for (int i = 0; i < n; i++)
                {
                    if (!_iconOk[i]) continue;
                    for (int j = i + 1; j < n; j++)
                    {
                        if (!_iconOk[j]) continue;
                        Vector2 d = _iconPos[j] - _iconPos[i]; float len = d.magnitude;
                        if (len >= minDist) continue;
                        Vector2 dir = len > 0.5f ? d / len : new Vector2(Mathf.Cos(i * 1.7f + j), Mathf.Sin(i * 1.3f + j * 2f));
                        // prefer spreading sideways first (receivers are lined up across the field)
                        dir = new Vector2(dir.x * 1.0f, dir.y * 0.7f).normalized;
                        float push = (minDist - len) * 0.5f + 0.5f;
                        _iconPos[i] -= dir * push; _iconPos[j] += dir * push; moved = true;
                    }
                    Vector2 p = _iconPos[i];
                    p.x = Mathf.Clamp(p.x, xMin, xMax); p.y = Mathf.Clamp(p.y, yMin, yMax);
                    _iconPos[i] = p;
                }
                if (!moved) break;
            }
        }

        void Leader(int i, Vector2 a, Vector2 b)
        {
            while (_leaders.Count <= i)
            {
                var im = UiKit.Panel(_iconLayer, "Leader" + _leaders.Count, new Color(1f, 1f, 1f, 0.75f)); im.transform.SetAsFirstSibling();
                im.rectTransform.pivot = new Vector2(0f, 0.5f); _leaders.Add(im);
            }
            var l = _leaders[i]; l.gameObject.SetActive(true);
            Vector2 d = b - a;
            l.rectTransform.anchorMin = l.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            l.rectTransform.anchoredPosition = a; l.rectTransform.sizeDelta = new Vector2(d.magnitude, 4f);
            l.rectTransform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        }

        GameObject MakeReceiverIcon()
        {
            var r = UiKit.Rect(_iconLayer, "RecIcon"); r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f); r.sizeDelta = new Vector2(84, 84);
            var ring = UiKit.Panel(r, "Ring", Color.white, Sp("ring_b")); ring.preserveAspect = true; UiKit.Stretch(ring.rectTransform);
            if (ring.sprite == null) ring.color = new Color(0.55f, 0.1f, 1f, 0.8f);
            var holder = UiKit.Rect(r, "Glyph"); UiKit.Stretch(holder);
            return r.gameObject;
        }

        // glyph variants (one per button / device / arrow) are built once per icon and only toggled afterwards: shuffling receivers no longer destroys and recreates UI objects (QA-762)
        readonly Dictionary<GameObject, string> _iconBtn = new Dictionary<GameObject, string>();
        void UpdateIconGlyph(GameObject go, Btn b, bool arrow)
        {
            string key = arrow ? "arrow" : b.ToString() + (MatchInput.GamepadActive ? "p" : "k");
            string cur;
            if (_iconBtn.TryGetValue(go, out cur) && cur == key) return;
            _iconBtn[go] = key;
            var holder = go.transform.GetChild(1);
            Transform found = null;
            for (int i = 0; i < holder.childCount; i++)
            {
                var c = holder.GetChild(i);
                bool match = c.name == key;
                c.gameObject.SetActive(match);
                if (match) found = c;
            }
            if (found != null) return;
            GameObject made;
            if (arrow)
            {
                var t = UiKit.Label(holder, ">", 56, Color.white, true, TextAlignmentOptions.Center, "Arrow"); UiKit.Stretch(t.rectTransform);
                made = t.gameObject;
            }
            else
            {
                var icon = MakeButtonIcon(holder, b, 46f);
                UiKit.Anchor((RectTransform)icon, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, ((RectTransform)icon).sizeDelta);
                made = icon.gameObject;
            }
            made.name = key;
        }
        // ---------------------------------------------------------------------------------------- coin toss
        RectTransform BeginModal(string name, string title, string sub)
        {
            if (_tossRoot != null) Destroy(_tossRoot);
            var root = UiKit.Rect(_root, name); UiKit.Stretch(root); _tossRoot = root.gameObject;
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.03f, 0.06f, 0.88f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var t = UiKit.Label(root, title, 92, Color.white, true, TextAlignmentOptions.Center, "Title");
            UiKit.Anchor(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(1500, 120)); t.characterSpacing = 6f;
            var rule = UiKit.Panel(root, "Rule", UiColors.Gold); UiKit.Anchor(rule.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 178), new Vector2(220, 7));
            var s = UiKit.Label(root, sub, 36, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "Sub");
            if (_glassHud) { UiKit.Fit(s, 20f); UiKit.Fit(t, 56f); }
            UiKit.Anchor(s.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 110), new Vector2(1500, 60)); s.characterSpacing = 2f;
            _tossRoot.transform.SetAsLastSibling(); if (_fade != null) _fade.transform.SetAsLastSibling();
            ApplyTossLayout();
            return root;
        }

        RectTransform ButtonColumn(Transform parent, float y)
        {
            var col = UiKit.Rect(parent, "Buttons"); UiKit.Anchor(col, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(560, 300));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 16; v.childAlignment = TextAnchor.UpperCenter; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            return col;
        }

        public void ShowCoinTossCall(string userTeam, string cpuTeam, Action<bool> onCallHeads)
        {
            var root = BeginModal("CoinToss", "COIN TOSS", userTeam.ToUpperInvariant() + " CALL IT IN THE AIR   (VS " + cpuTeam.ToUpperInvariant() + ")");
            var col = ButtonColumn(root, -110);
            Button first = null;
            first = UiKit.MenuButton(col, "HEADS", () => { Destroy(_tossRoot); onCallHeads(true); }, 560, 88, 40);
            UiKit.MenuButton(col, "TAILS", () => { Destroy(_tossRoot); onCallHeads(false); }, 560, 88, 40);
            ApplyTossLayout();
            StartCoroutine(SelectLater(first.gameObject));
        }

        public void ShowCoinTossResult(bool resultHeads, bool userWon, string winnerName, Action<bool> onReceive)
        {
            var root = BeginModal("CoinTossResult", resultHeads ? "IT'S HEADS" : "IT'S TAILS", winnerName.ToUpperInvariant() + " WON THE TOSS");
            if (userWon)
            {
                var col = ButtonColumn(root, -110);
                var first = UiKit.MenuButton(col, "RECEIVE", () => { Destroy(_tossRoot); onReceive(true); }, 560, 88, 40);
                UiKit.MenuButton(col, "DEFER TO 2ND HALF", () => { Destroy(_tossRoot); onReceive(false); }, 560, 88, 36);
                StartCoroutine(SelectLater(first.gameObject));
            }
            else
            {
                var t = UiKit.Label(root, "ELECTS TO RECEIVE", 44, UiColors.Gold, true, TextAlignmentOptions.Center, "Elect");
                UiKit.Anchor(t.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(1200, 70)); t.characterSpacing = 4f;
                StartCoroutine(AutoContinue(onReceive, 2.6f));
            }
            ApplyTossLayout();
        }

        IEnumerator AutoContinue(Action<bool> cb, float seconds)
        {
            float t = 0f; while (t < seconds) { t += Time.unscaledDeltaTime; yield return null; }
            if (_tossRoot != null) Destroy(_tossRoot);
            cb(true);
        }

        IEnumerator SelectLater(GameObject g)
        {
            yield return null;
            if (g != null && EventSystem.current != null) EventSystem.current.SetSelectedGameObject(g);
        }

        // ---------------------------------------------------------------------------------------- final
        public void ShowFinal(FinalData d, Action onContinue)
        {
            if (_glassHud) { ShowFinalGlass(d, onContinue); return; }
            if (_finalRoot != null) Destroy(_finalRoot);
            var root = UiKit.Rect(_root, "Final"); UiKit.Stretch(root); _finalRoot = root.gameObject;
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.03f, 0.06f, 0.96f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            // the content is a 1500 x 1080 design block that shrinks to the canvas (short and narrow screens keep every row clear of the button)
            var block = UiKit.Rect(root, "Block"); block.anchorMin = block.anchorMax = new Vector2(0.5f, 0.5f); block.pivot = new Vector2(0.5f, 0.5f); block.anchoredPosition = Vector2.zero; block.sizeDelta = new Vector2(1500, 1080);
            var fit = root.gameObject.AddComponent<FitScale>(); fit.Block = block; fit.DesignSize = new Vector2(1500, 1080);
            var head = UiKit.Label(block, d.Headline, 100, d.Tie ? Color.white : (d.UserWon ? UiColors.Gold : Color.white), true, TextAlignmentOptions.Center, "Headline");
            UiKit.Anchor(head.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -50), new Vector2(1500, 130)); head.characterSpacing = 8f;
            var rule = UiKit.Panel(block, "Rule", UiColors.Gold); UiKit.Anchor(rule.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -186), new Vector2(240, 7));

            // team rows: HOME on top like everywhere else is not used here; the order is AWAY then HOME (the same as the line score on TV), each row labelled
            float y0 = -250f;
            // quarter headers: 1 2 3 4 and OT (all overtime periods summed)
            string[] qn = { "1", "2", "3", "4", "OT" };
            for (int i = 0; i < 5; i++)
            {
                var hl = UiKit.Label(block, qn[i], 26, UiColors.TextMuted, true, TextAlignmentOptions.Center, "QH" + i);
                UiKit.Anchor(hl.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(QuarterCellX(i) - 650f, y0 + 30f), new Vector2(90, 34));
            }
            Row(block, y0 - 8f, d.AwayAbbr, d.AwayName, d.AwayCrest, d.AwayColor, d.AwayByQuarter, d.AwayScore, d.AwayScore > d.HomeScore);
            Row(block, y0 - 8f - 124f, d.HomeAbbr, d.HomeName, d.HomeCrest, d.HomeColor, d.HomeByQuarter, d.HomeScore, d.HomeScore > d.AwayScore);

            // stats (the same rows the TV comparison card uses)
            float sy = y0 - 8f - 124f - 150f;
            var title = UiKit.Label(block, "TEAM STATS", 32, UiColors.Gold, true, TextAlignmentOptions.Center, "StatsTitle");
            UiKit.Anchor(title.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, sy + 8), new Vector2(600, 40)); title.characterSpacing = 4f;
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
            for (int i = 0; i < rows.Length; i++)
            {
                var bg = UiKit.Panel(block, "StatRow" + i, i % 2 == 0 ? UiColors.Row : new Color(0.14f, 0.15f, 0.2f, 1f));
                UiKit.Anchor(bg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, sy - 30f - i * 42f), new Vector2(900, 40));
                var a = UiKit.Label(bg.rectTransform, rows[i][1], 28, Color.white, true, TextAlignmentOptions.MidlineLeft, "A"); UiKit.Anchor(a.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 0), new Vector2(270, 40));
                var m = UiKit.Label(bg.rectTransform, rows[i][0], 24, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "M"); UiKit.Anchor(m.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(340, 40)); m.characterSpacing = 2f;
                var h = UiKit.Label(bg.rectTransform, rows[i][2], 28, Color.white, true, TextAlignmentOptions.MidlineRight, "H"); UiKit.Anchor(h.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-30, 0), new Vector2(270, 40));
            }
            var col = UiKit.Rect(block, "Buttons"); UiKit.Anchor(col, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 34), new Vector2(560, 84));
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            var btn = UiKit.MenuButton(col, "CONTINUE", () => { if (onContinue != null) onContinue(); }, 560, 84, 38);
            _finalRoot.transform.SetAsLastSibling(); if (_fade != null) _fade.transform.SetAsLastSibling();
            StartCoroutine(SelectLater(btn.gameObject));
        }

        /// <summary>Centre x of the line-score cell for quarter index 0..4 (the last is the summed overtime cell) inside a 1300 px team row: clear of the 180 px total box on the right.</summary>
        public static float QuarterCellX(int i) { return 640f + i * 92f; }

        /// <summary>Points per period for the line score: quarters 1..4 and every overtime period folded into one OT cell; short arrays are tolerated.</summary>
        public static int[] LineScoreCells(int[] byQ)
        {
            var cells = new int[5];
            if (byQ == null) return cells;
            for (int i = 1; i <= 4 && i < byQ.Length; i++) cells[i - 1] = byQ[i];
            for (int i = 5; i < byQ.Length; i++) cells[4] += byQ[i];
            return cells;
        }

        void Row(RectTransform root, float y, string abbr, string name, string crest, Color color, int[] byQ, int total, bool winner)
        {
            var bg = UiKit.Panel(root, "TeamRow_" + abbr, new Color(color.r * 0.55f, color.g * 0.55f, color.b * 0.55f, 0.95f), Sp("sb_local"));
            if (bg.sprite != null) bg.color = color;
            UiKit.Anchor(bg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, y), new Vector2(1300, 116));
            Color fg = Readable(color);
            var cr = UiKit.Panel(bg.rectTransform, "Crest", Color.white, Sp(crest)); cr.preserveAspect = true; cr.enabled = cr.sprite != null;
            UiKit.Anchor(cr.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(58, 0), new Vector2(92, 92));
            var n = UiKit.Label(bg.rectTransform, name.ToUpperInvariant(), 40, fg, true, TextAlignmentOptions.MidlineLeft, "Name");
            UiKit.Anchor(n.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(176, 0), new Vector2(430, 70)); n.characterSpacing = 2f; UiKit.Fit(n, 24);
            int[] vals = LineScoreCells(byQ);
            for (int i = 0; i < 5; i++)
            {
                var ql = UiKit.Label(bg.rectTransform, vals[i].ToString(), 36, fg, true, TextAlignmentOptions.Center, "Q" + i);
                UiKit.Anchor(ql.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(QuarterCellX(i), 0), new Vector2(90, 60));
            }
            var t = UiKit.Label(bg.rectTransform, total.ToString(), 84, winner ? (fg.r > 0.5f ? UiColors.Gold : new Color(0.55f, 0.35f, 0f)) : fg, true, TextAlignmentOptions.MidlineRight, "Total");
            UiKit.Anchor(t.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-40, 0), new Vector2(180, 100));
        }

        // ---------------------------------------------------------------------------------------- pause
        void BuildPause()
        {
            var c = UiKit.CreateCanvas("PauseCanvas", 60, transform);
            _pauseRoot = c.gameObject;
            var root = (RectTransform)c.transform;
            if (_glassHud) { BuildPauseGlass(root); _pauseRoot.AddComponent<PauseCancel>().Target = this; _pauseRoot.SetActive(false); return; }
            var scrim = UiKit.Panel(root, "Scrim", UiColors.Scrim); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var rail = UiKit.Panel(root, "Rail", new Color(0.05f, 0.06f, 0.1f, 0.94f));
            UiKit.Anchor(rail.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(860, 0)); rail.rectTransform.anchorMax = new Vector2(0, 1); rail.rectTransform.sizeDelta = new Vector2(860, 0);
            var edge = UiKit.Panel(root, "RailEdge", UiColors.AccentBlue);
            UiKit.Anchor(edge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(860, 0), new Vector2(4, 0)); edge.rectTransform.anchorMax = new Vector2(0, 1); edge.rectTransform.sizeDelta = new Vector2(4, 0);
            var title = UiKit.Label(root, "PAUSED", 120, Color.white, true, TextAlignmentOptions.BottomLeft, "Title");
            UiKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(100, -80), new Vector2(700, 140)); title.characterSpacing = 4f;
            var rule = UiKit.Panel(root, "Rule", UiColors.Gold); UiKit.Anchor(rule.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(108, -228), new Vector2(220, 8));
            // the item list sits in a host that shrinks it to the room under the title on short canvases
            var host = UiKit.Rect(root, "ItemsHost"); host.anchorMin = new Vector2(0, 0); host.anchorMax = new Vector2(0, 1); host.pivot = new Vector2(0, 0.5f);
            host.offsetMin = new Vector2(100, 96); host.offsetMax = new Vector2(820, -262);
            var col = UiKit.Rect(host, "Items"); col.anchorMin = col.anchorMax = new Vector2(0, 1); col.pivot = new Vector2(0, 1); col.anchoredPosition = Vector2.zero; col.sizeDelta = new Vector2(720, 700);
            var fit = host.gameObject.AddComponent<FitScale>(); fit.Block = col; fit.DesignSize = new Vector2(720, 700);
            _pauseCol = col.gameObject;
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 12; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            const float rowH = 76f;
            _pauseResume = UiKit.MenuButton(col, "RESUME", () => { if (ResumeRequested != null) ResumeRequested(); }, 720, rowH, 36);
            _pauseReplay = UiKit.MenuButton(col, "INSTANT REPLAY", () => { if (ReplayRequested != null) ReplayRequested(); }, 720, rowH, 36);
            _pauseCine = UiControls.Selector(col, "Cinematics", PixSettings.CinematicsNames, PixSettings.Cinematics, i => { PixSettings.Cinematics = i; }, 720, rowH);
            _pauseCam = UiControls.Selector(col, "Camera", new[] { "Classic", "Madden", "Wide", "Broadcast", "First person" }, 0, i => { if (CameraCycleRequested != null && _dir != null && _dir.Cams != null) { CameraMode m = i <= 2 ? CameraMode.Behind : (i == 3 ? CameraMode.Broadcast : CameraMode.FirstPerson); if (i <= 2) PixSettings.UserCameraStyle = (CameraStyle)i; _dir.Cams.SetMode(m, false); PixSettings.DefaultCamera = m; } }, 720, rowH);
            // the selector says what it means: choosing the preset that is already on does nothing, choosing the other one switches (it used to toggle blindly) (QA-744)
            _pauseLight = UiControls.Selector(col, "Lighting", new[] { "Night game", "Day game" }, 0, i => { if (LightingToggleRequested != null && _dir != null && _dir.Lighting != null && (int)_dir.Lighting.Current != i) LightingToggleRequested(); }, 720, rowH);
            _pauseSb = UiControls.Selector(col, "Scorebug", PixSettings.ScorebugNames, (int)PixSettings.Scorebug, i => { PixSettings.Scorebug = (ScorebugStyle)i; ApplyScorebugLayout(); }, 720, rowH);
            _pauseSettings = UiKit.MenuButton(col, "SOUND AND CONTROLS", ShowPauseSettings, 720, rowH, 36);
            UiKit.MenuButton(col, "QUIT TO MENU", ConfirmQuit, 720, rowH, 36);

            // second page of the rail: volumes, rumble, shake (these used to be reachable only from the main menu) (QA-820)
            var col2 = UiKit.Rect(host, "SettingsItems"); col2.anchorMin = col2.anchorMax = new Vector2(0, 1); col2.pivot = new Vector2(0, 1); col2.anchoredPosition = Vector2.zero; col2.sizeDelta = new Vector2(720, 700);
            _pauseSettingsCol = col2.gameObject;
            var v2 = col2.gameObject.AddComponent<VerticalLayoutGroup>(); v2.spacing = 12; v2.childControlWidth = false; v2.childControlHeight = false; v2.childForceExpandWidth = false; v2.childForceExpandHeight = false;
            _pauseVolFirst = UiControls.SliderRow(col2, "Master volume", PixSettings.MasterVolume, x => { PixSettings.MasterVolume = x; AudioListener.volume = x; }, 720, rowH);
            UiControls.SliderRow(col2, "Crowd volume", PixSettings.CrowdVolume, x => { PixSettings.CrowdVolume = x; }, 720, rowH);
            UiControls.SliderRow(col2, "Effects volume", PixSettings.SfxVolume, x => { PixSettings.SfxVolume = x; }, 720, rowH);
            UiControls.Selector(col2, "Controller rumble", new[] { "On", "Off" }, PixSettings.Rumble ? 0 : 1, i => { PixSettings.Rumble = i == 0; }, 720, rowH);
            UiControls.Selector(col2, "Camera shake on hits", new[] { "On", "Off" }, PixSettings.CameraShake ? 0 : 1, i => { PixSettings.CameraShake = i == 0; }, 720, rowH);
            UiKit.MenuButton(col2, "BACK", HidePauseSettings, 720, rowH, 36);
            _pauseSettingsCol.SetActive(false);

            var bar = UiKit.Panel(root, "Footer", UiColors.BgPanelDark); UiKit.Anchor(bar.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 76)); bar.rectTransform.anchorMax = new Vector2(1, 0); bar.rectTransform.sizeDelta = new Vector2(0, 76);
            var row = UiKit.Rect(bar.rectTransform, "Prompts"); UiKit.Anchor(row, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(100, 0), new Vector2(1100, 56));
            PromptFooter.Attach(row, 48, 26, 48).Set(PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_b", "BACK"), PromptFooter.Item("glyph_menu", "RESUME"), PromptFooter.Item("glyph_analog_all", "NAVIGATE"));
            _pauseRoot.AddComponent<PauseCancel>().Target = this;
            _pauseRoot.SetActive(false);
        }

        GameObject _pauseCol, _pauseSettingsCol; Button _pauseSettings; Slider _pauseVolFirst;

        void ShowPauseSettings()
        {
            _pauseCol.SetActive(false); _pauseSettingsCol.SetActive(true);
            if (_pauseCardRt != null) _pauseCardRt.sizeDelta = new Vector2(_pauseCardRt.sizeDelta.x, 610f);   // glass: the short page gets a short card
            StartCoroutine(SelectLater(_pauseVolFirst.gameObject));
        }

        void HidePauseSettings()
        {
            _pauseSettingsCol.SetActive(false); _pauseCol.SetActive(true);
            if (_pauseCardRt != null) _pauseCardRt.sizeDelta = new Vector2(_pauseCardRt.sizeDelta.x, 760f);
            StartCoroutine(SelectLater(_pauseSettings.gameObject));
        }

        /// <summary>QUIT TO MENU throws the game away, so it asks first (QA-888).</summary>
        void ConfirmQuit()
        {
            UiDialog.Show(_pauseRoot.transform, "Quit to menu?", "The game in progress will be lost.", "QUIT", "KEEP PLAYING", () => { if (QuitRequested != null) QuitRequested(); }, null);
        }

        /// <summary>B / Esc (the UI cancel action) steps back one level in the pause menu and resumes from the first page (QA-754).</summary>
        sealed class PauseCancel : MonoBehaviour, UnityEngine.EventSystems.ICancelHandler
        {
            public MatchHud Target;
            public void OnCancel(UnityEngine.EventSystems.BaseEventData e)
            {
                if (Target == null) return;
                if (Target._pauseSettingsCol != null && Target._pauseSettingsCol.activeSelf) { Target.HidePauseSettings(); return; }
                if (Target.ResumeRequested != null) Target.ResumeRequested();
            }
        }

        bool _replayAvailable;
        public void SetReplayAvailable(bool available) { _replayAvailable = available; if (_pauseReplay != null && PauseOpen) _pauseReplay.gameObject.SetActive(available); }

        public void SetReplayMode(bool on)
        {
            _replayMode = on;
            if (on) { if (_hintBar != null) _hintBar.SetActive(false); if (_marker != null) _marker.gameObject.SetActive(false); if (_whistle != null) _whistle.gameObject.SetActive(false); SetRouteBadges(null, null); if (_preSnap != null) _preSnap.Hide(); SetReceiverIcons(null, null); UpdateFlag(false, null); if (_banner != null) _banner.gameObject.SetActive(false); }
            // a replay shows no live camera name or play clock, and the top-left block sits below the letterbox bar instead of straddling it (QA-132 / 206)
            if (_camLabel != null) _camLabel.gameObject.SetActive(!on && !_hudMinimal);
            if (_bCam != null) _bCam.gameObject.SetActive(!on && !_hudMinimal);
            if (_playClockBg != null) _playClockBg.gameObject.SetActive(!on);
            if (_bPlayClockBg != null) _bPlayClockBg.gameObject.SetActive(!on);
            if (_scorebugRoot != null) _scorebugRoot.anchoredPosition = new Vector2(60f, on ? -170f : -44f);
            ApplyScorebugScale();
        }

        public void SetHudVisible(bool visible) { if (_canvas != null) _canvas.enabled = visible; }

        /// <summary>First-person camera: a small scorebug and a quiet hint bar.</summary>
        public void SetHudMinimal(bool minimal)
        {
            if (_hudMinimal == minimal) return;
            _hudMinimal = minimal;
            ApplyScorebugScale();
            if (_camLabel != null) _camLabel.gameObject.SetActive(!minimal);
            if (_bCam != null) _bCam.gameObject.SetActive(!minimal);
            if (_hint != null) _hint.alpha = minimal ? 0.55f : 1f;
        }

        public void SetPaused(bool paused)
        {
            if (_pauseRoot == null) return;
            _pauseRoot.SetActive(paused);
            if (_glassHud) HideHudUnderGlassPause(paused);
            if (paused)
            {
                if (_pauseCol != null) { _pauseCol.SetActive(true); _pauseSettingsCol.SetActive(false); }
                if (_pauseCine != null) _pauseCine.SetOptions(_pauseCine.Options, PixSettings.Cinematics);
                if (_pauseReplay != null) _pauseReplay.gameObject.SetActive(_replayAvailable);
                if (_dir != null && _dir.Cams != null) _pauseCam.SetOptions(_pauseCam.Options, _dir.Cams.Mode == CameraMode.Broadcast ? 3 : _dir.Cams.Mode == CameraMode.FirstPerson ? 4 : (int)PixSettings.UserCameraStyle);
                if (_dir != null && _dir.Lighting != null) _pauseLight.SetOptions(_pauseLight.Options, (int)_dir.Lighting.Current);
                if (_pauseSb != null) _pauseSb.SetOptions(_pauseSb.Options, (int)PixSettings.Scorebug);
                if (_glassHud) FillStateCard();
                StartCoroutine(SelectLater(_pauseResume.gameObject));
            }
        }
    }
}






