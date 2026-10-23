using System;
using System.Collections.Generic;
using System.Text;
using PixFootball.Core;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// The full play-call screen (2K5 / NCAA style, gamepad first): top bar with situation, play clock and timeout; tabs (Playbook, Ask Coach,
    /// Recent, Special Teams); the Formation -> Set -> Play flow with three play cards per page drawn from the plays' own assignments; type filters;
    /// footer prompts. It only renders the <see cref="PlayCallModel"/> (the director feeds the model the input).
    /// Every size comes from <see cref="PlayCallLayout"/> for the actual canvas size, so the screen fits 4:3, 16:10, 16:9 and 21:9.
    /// </summary>
    public sealed class PlayCallScreen : MonoBehaviour
    {
        RectTransform _root;
        // top bar
        Image _sideChip; TextMeshProUGUI _sideText, _teamText, _situation, _scoreText, _clockLabel, _clockNum, _toText, _coachStyle;
        Image _clockBar; Image[] _toPips = new Image[3]; Image _toBtn; RectTransform _toIcon;
        // tabs / breadcrumb / filters
        readonly List<Image> _tabBg = new List<Image>(); readonly List<TextMeshProUGUI> _tabText = new List<TextMeshProUGUI>();
        RectTransform _tabRow, _filterRow, _listRoot, _cardRoot, _previewRoot, _footer;
        TextMeshProUGUI _crumb;
        readonly List<Image> _filterBg = new List<Image>(); readonly List<TextMeshProUGUI> _filterText = new List<TextMeshProUGUI>();
        // list
        sealed class Row { public RectTransform Rt; public Image Bg, Accent; public TextMeshProUGUI Name, Sub, Count; }
        readonly List<Row> _rows = new List<Row>();
        // preview (formation / selected play)
        RectTransform _previewDiagramBg; PlayDiagramGraphic _preview; TextMeshProUGUI _previewTitle, _previewSub, _previewBody;
        // cards
        sealed class Card { public RectTransform Rt, DiagramBg; public Image Bg, Outline, Tag; public TextMeshProUGUI Title, Sub, Info, Reason, TagText; public PlayDiagramGraphic Diagram; public float Scale = 1f, Target = 1f; public CanvasGroup Group; public Play Play; public string Key = ""; }
        readonly List<Card> _cards = new List<Card>();
        TextMeshProUGUI _pageText;
        TextMeshProUGUI _empty;
        // footer
        RectTransform _footerRow; int _footerRows = 1;
        int _lastVersion = -1; bool _lastGamepad = true; PlayCallModel _model;
        // layout / clock caches
        RectTransform _canvasRt; Vector2 _canvasSize = new Vector2(PlayCallLayout.RefW, PlayCallLayout.RefH); PlayCallRects _r;
        int _lastSecond = -1; bool _lastLow; int _lastBarPx = -1;
        static readonly string[] SecondText = BuildSecondText();
        readonly Dictionary<string, PlayDiagram> _diagrams = new Dictionary<string, PlayDiagram>();
        // QA-158: orange / blue instead of the red / green pair (colour-blind safe); the chip text names the side as well
        static readonly Color SideOff = UiColors.Hex("#FF9000"), SideDef = UiColors.Hex("#2D6AED"), SideSpec = UiColors.Hex("#9040F0");

        public static PlayCallScreen Create(Transform parent)
        {
            var r = UiKit.Rect(parent, "PlayCallScreen"); UiKit.Stretch(r);
            var s = r.gameObject.AddComponent<PlayCallScreen>();
            s._root = r; s.Build();
            if (UiTheme.IsGlass) PixFootball.UI.Glass.GlassSkin.Apply(r);
            return s;
        }

        static string[] BuildSecondText()
        {
            var t = new string[100];
            for (int i = 0; i < t.Length; i++) t[i] = i.ToString();
            return t;
        }

        // ================================================================================================ build
        void Build()
        {
            var scrim = UiKit.Panel(_root, "Scrim", UiTheme.IsGlass ? new Color(0.02f, 0.025f, 0.035f, 0.5f) : new Color(0.02f, 0.03f, 0.06f, 0.94f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var diamond = UiKit.Panel(_root, "Diamond", new Color(1f, 1f, 1f, 0.05f), UiKit.Sprite("bg_diamond")); UiKit.Stretch(diamond.rectTransform);
            diamond.enabled = diamond.sprite != null && !UiTheme.IsGlass;
            BuildTopBar();
            _tabRow = UiKit.Rect(_root, "Tabs");
            _crumb = UiKit.Label(_root, "FORMATION", 30, UiColors.Gold, true, TextAlignmentOptions.MidlineLeft, "Crumb"); _crumb.characterSpacing = 3f;
            _filterRow = UiKit.Rect(_root, "Filters");
            _listRoot = UiKit.Rect(_root, "List");
            _previewRoot = UiKit.Rect(_root, "Preview");
            _cardRoot = UiKit.Rect(_root, "Cards");
            BuildPreview();
            for (int i = 0; i < PlayCallModel.PageSize; i++) _cards.Add(BuildCard(i));
            _pageText = UiKit.Label(_root, "", 26, UiColors.TextSecondary, true, TextAlignmentOptions.MidlineRight, "Page");
            _empty = UiKit.Label(_cardRoot, "NO PLAYS IN THIS VIEW", 40, UiColors.TextMuted, true, TextAlignmentOptions.Center, "Empty"); UiKit.Stretch(_empty.rectTransform);
            var foot = UiKit.Panel(_root, "Footer", UiColors.BgPanelDark);
            _footer = foot.rectTransform;
            _footerRow = UiKit.Rect(_footer, "Prompts");
            _r = PlayCallLayout.Compute(PlayCallLayout.RefW, PlayCallLayout.RefH, 1);
            ApplyLayout();
        }

        void BuildTopBar()
        {
            var bar = UiKit.Panel(_root, "TopBar", UiColors.BgNav); UiKit.Anchor(bar.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(0, PlayCallLayout.TopBarH));
            bar.rectTransform.anchorMax = new Vector2(1, 1); bar.rectTransform.sizeDelta = new Vector2(0, PlayCallLayout.TopBarH);
            _sideChip = UiKit.Panel(bar.rectTransform, "SideChip", SideOff); UiKit.Anchor(_sideChip.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(PlayCallLayout.Margin, 0), new Vector2(250, 62));
            _sideText = UiKit.Label(_sideChip.rectTransform, "OFFENSE", 34, Color.white, true, TextAlignmentOptions.Center, "Side"); UiKit.Stretch(_sideText.rectTransform); _sideText.characterSpacing = 3f;
            _teamText = UiKit.Label(bar.rectTransform, "TEAM", 40, Color.white, true, TextAlignmentOptions.MidlineLeft, "Team"); _teamText.characterSpacing = 3f;
            FitText(_teamText, 24f, 40f);
            _situation = UiKit.Label(bar.rectTransform, "1ST & 10", 28, UiColors.Gold, true, TextAlignmentOptions.MidlineLeft, "Situation"); _situation.characterSpacing = 1f;
            FitText(_situation, 20f, 28f);
            _scoreText = UiKit.Label(bar.rectTransform, "", 32, Color.white, true, TextAlignmentOptions.MidlineLeft, "Score"); FitText(_scoreText, 22f, 32f);
            _coachStyle = UiKit.Label(bar.rectTransform, "", 22, UiColors.TextSecondary, true, TextAlignmentOptions.MidlineLeft, "Coach"); _coachStyle.characterSpacing = 1f; FitText(_coachStyle, 16f, 22f);
            // timeout button (a real button: blue fill, light edge, the LT glyph at its left)
            _toBtn = UiKit.Panel(bar.rectTransform, "Timeout", UiColors.AccentBlue); UiKit.Anchor(_toBtn.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-250, 6), new Vector2(300, 62));
            var toEdge = UiKit.Panel(_toBtn.rectTransform, "Edge", new Color(1f, 1f, 1f, 0.9f)); UiKit.Anchor(toEdge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 3)); toEdge.rectTransform.anchorMax = new Vector2(1, 0); toEdge.rectTransform.sizeDelta = new Vector2(0, 3);
            _toText = UiKit.Label(_toBtn.rectTransform, "CALL TIMEOUT", 30, Color.white, true, TextAlignmentOptions.MidlineLeft, "Text"); UiKit.Stretch(_toText.rectTransform, 84, 0, 8, 0); _toText.characterSpacing = 2f;
            FitText(_toText, 18f, 30f);
            // three pips centred under the button (button spans -550..-250 from the right edge; pips 90 wide, 100 pitch => right edges -455 / -355 / -255)
            for (int i = 0; i < 3; i++)
            {
                var p = UiKit.Panel(bar.rectTransform, "Pip" + i, UiColors.AccentBlueHot);
                UiKit.Anchor(p.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-455 + i * 100, -34), new Vector2(90, 6));
                _toPips[i] = p;
            }
            // play clock
            var clockBg = UiKit.Panel(bar.rectTransform, "ClockBg", Color.black); UiKit.Anchor(clockBg.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-80, 0), new Vector2(150, 92));
            _clockNum = UiKit.Label(clockBg.rectTransform, "25", 70, UiColors.Gold, true, TextAlignmentOptions.Center, "Num"); UiKit.Anchor(_clockNum.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(150, 72));
            _clockLabel = UiKit.Label(clockBg.rectTransform, "PLAY CLOCK", 18, Color.white, true, TextAlignmentOptions.Center, "Lbl"); UiKit.Anchor(_clockLabel.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(150, 24));
            _clockBar = UiKit.Panel(clockBg.rectTransform, "Bar", UiColors.Gold); UiKit.Anchor(_clockBar.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(150, 5));
        }

        /// <summary>Single-line text that shrinks to its box instead of running into its neighbour, and ends in "..." when even the smallest size does not fit.</summary>
        static void FitText(TextMeshProUGUI t, float min, float max)
        {
            t.enableAutoSizing = true; t.fontSizeMin = min; t.fontSizeMax = max;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
        }

        void BuildPreview()
        {
            var bg = UiKit.Panel(_previewRoot, "Bg", new Color(0.05f, 0.06f, 0.09f, 0.85f)); UiKit.Stretch(bg.rectTransform);
            var gd = UiKit.Panel(_previewRoot, "DiagramBg", Color.black); _previewDiagramBg = gd.rectTransform;
            gd.gameObject.AddComponent<RectMask2D>();
            var gr = UiKit.Rect(gd.rectTransform, "Diagram"); UiKit.Stretch(gr, 3, 3, 3, 3);
            _preview = gr.gameObject.AddComponent<PlayDiagramGraphic>();
            _previewTitle = UiKit.Label(_previewRoot, "", 56, Color.white, true, TextAlignmentOptions.TopLeft, "Title"); _previewTitle.characterSpacing = 2f;
            _previewTitle.textWrappingMode = TextWrappingModes.Normal; _previewTitle.enableAutoSizing = true; _previewTitle.fontSizeMin = 30f; _previewTitle.fontSizeMax = 56f;
            _previewSub = UiKit.Label(_previewRoot, "", 30, UiColors.Gold, true, TextAlignmentOptions.TopLeft, "Sub"); _previewSub.characterSpacing = 2f;
            FitText(_previewSub, 20f, 30f);
            _previewBody = UiKit.Label(_previewRoot, "", 28, UiColors.TextSecondary, false, TextAlignmentOptions.TopLeft, "Body");
            _previewBody.textWrappingMode = TextWrappingModes.Normal; _previewBody.lineSpacing = 12f;
        }

        Card BuildCard(int i)
        {
            var c = new Card();
            c.Rt = UiKit.Rect(_cardRoot, "Card" + i);
            c.Group = c.Rt.gameObject.AddComponent<CanvasGroup>();
            c.Outline = UiKit.Panel(c.Rt, "Outline", UiColors.Gold); UiKit.Stretch(c.Outline.rectTransform, -5, -5, -5, -5);
            c.Bg = UiKit.Panel(c.Rt, "Bg", new Color(0.10f, 0.11f, 0.15f, 1f)); UiKit.Stretch(c.Bg.rectTransform);
            c.Title = UiKit.Label(c.Rt, "PLAY NAME", 38, Color.white, true, TextAlignmentOptions.TopLeft, "Title"); c.Title.characterSpacing = 1.5f; FitText(c.Title, 24f, 38f);
            c.Sub = UiKit.Label(c.Rt, "FORMATION", 22, UiColors.Gold, true, TextAlignmentOptions.TopLeft, "Sub"); c.Sub.characterSpacing = 2f; FitText(c.Sub, 15f, 22f);
            var dBg = UiKit.Panel(c.Rt, "DiagramBg", Color.black); c.DiagramBg = dBg.rectTransform;
            dBg.gameObject.AddComponent<RectMask2D>();
            var dr = UiKit.Rect(dBg.rectTransform, "Diagram"); UiKit.Stretch(dr, 2, 2, 2, 2);
            c.Diagram = dr.gameObject.AddComponent<PlayDiagramGraphic>();
            c.Tag = UiKit.Panel(c.Rt, "Tag", UiColors.AccentBlue);
            c.TagText = UiKit.Label(c.Tag.rectTransform, "PASS", 22, Color.white, true, TextAlignmentOptions.Center, "TagText"); UiKit.Stretch(c.TagText.rectTransform); c.TagText.characterSpacing = 2f;
            c.Info = UiKit.Label(c.Rt, "", 23, UiColors.TextSecondary, false, TextAlignmentOptions.TopLeft, "Info");
            c.Info.textWrappingMode = TextWrappingModes.Normal; c.Info.lineSpacing = 6f; c.Info.enableAutoSizing = true; c.Info.fontSizeMin = 16f; c.Info.fontSizeMax = 23f;
            c.Reason = UiKit.Label(c.Rt, "", 22, UiColors.Gold, true, TextAlignmentOptions.BottomLeft, "Reason");
            c.Reason.textWrappingMode = TextWrappingModes.Normal; c.Reason.characterSpacing = 1.2f; c.Reason.enableAutoSizing = true; c.Reason.fontSizeMin = 15f; c.Reason.fontSizeMax = 22f;
            return c;
        }

        // ================================================================================================ layout
        /// <summary>Canvas size in reference units (falls back to 1920 x 1080 until the CanvasScaler has run).</summary>
        Vector2 CanvasSize()
        {
            if (_canvasRt == null)
            {
                var c = GetComponentInParent<Canvas>();
                if (c != null) _canvasRt = (RectTransform)c.rootCanvas.transform;
            }
            if (_canvasRt == null) return _canvasSize;
            Vector2 s = _canvasRt.rect.size;
            return s.x < 100f || s.y < 100f ? _canvasSize : s;
        }

        /// <summary>Positions and sizes every block from <see cref="_r"/>. Cheap and idempotent; called on every full rebuild.</summary>
        void ApplyLayout()
        {
            PlayCallRects r = _r;
            // top bar texts
            UiKit.Anchor(_teamText.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(r.TeamX, 18), new Vector2(r.TeamW, 50));
            UiKit.Anchor(_situation.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(r.TeamX, -22), new Vector2(r.TeamW, 40));
            UiKit.Anchor(_scoreText.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(r.ScoreX, 16), new Vector2(r.ScoreW, 44));
            UiKit.Anchor(_coachStyle.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(r.ScoreX, -22), new Vector2(r.ScoreW, 36));
            // tabs, breadcrumb, filters
            float rightGap = r.CanvasW - (r.CardsX + r.CardsW);
            UiKit.Anchor(_tabRow, new Vector2(0, 1), new Vector2(0, 1), new Vector2(r.ListX, -128), new Vector2(r.CanvasW - r.ListX - PlayCallLayout.Margin, 56));
            UiKit.Anchor(_crumb.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(r.ListX, -200), new Vector2(760, 50));
            UiKit.Anchor(_filterRow, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-rightGap, -200), new Vector2(r.FilterAvail, 50));
            // content blocks
            UiKit.Anchor(_listRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(r.ListX, -r.ContentTop), new Vector2(r.ListW, Mathf.Min(690f, r.ContentH)));
            UiKit.Anchor(_previewRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(r.CardsX, -r.ContentTop), new Vector2(r.CardsW, r.PreviewH));
            UiKit.Anchor(_cardRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(r.CardsX, -r.ContentTop), new Vector2(r.CardsW, r.CardH));
            // preview internals
            UiKit.Anchor(_previewDiagramBg, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(r.PreviewDiagramW, r.PreviewDiagramH));
            UiKit.Anchor(_previewTitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(r.PreviewTextX, -34), new Vector2(r.PreviewTextW, 160));
            UiKit.Anchor(_previewSub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(r.PreviewTextX, -200), new Vector2(r.PreviewTextW, 44));
            UiKit.Anchor(_previewBody.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(r.PreviewTextX, -258), new Vector2(r.PreviewTextW, Mathf.Max(200f, r.PreviewH - 290f)));
            // cards
            for (int i = 0; i < _cards.Count; i++)
            {
                Card c = _cards[i];
                UiKit.Anchor(c.Rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(i * (r.CardW + r.CardGap), 0), new Vector2(r.CardW, r.CardH));
                UiKit.Anchor(c.Title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -14), new Vector2(r.CardW - 36, 50));
                UiKit.Anchor(c.Sub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -62), new Vector2(r.CardW - 36, 30));
                UiKit.Anchor(c.DiagramBg, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -100), new Vector2(r.CardW - 20, r.DiagramH));
                UiKit.Anchor(c.Tag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -(100 + r.DiagramH + 12)), new Vector2(124, 40));
                UiKit.Anchor(c.Info.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -(100 + r.DiagramH + 62)), new Vector2(r.CardW - 36, r.InfoH));
                UiKit.Anchor(c.Reason.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 12), new Vector2(r.CardW - 36, 56));
            }
            // page counter and footer
            UiKit.Anchor(_pageText.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-rightGap, r.FooterH + 10), new Vector2(520, 40));
            UiKit.Anchor(_footer, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, r.FooterH));
            _footer.anchorMax = new Vector2(1, 0); _footer.sizeDelta = new Vector2(0, r.FooterH);
            float rowsH = _footerRows * 44f + (_footerRows - 1) * 6f;
            UiKit.Anchor(_footerRow, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(PlayCallLayout.Margin, 0), new Vector2(r.FooterAvail, rowsH));
        }

        // ================================================================================================ rendering
        public void Show(PlayCallModel m, float playClock)
        {
            _model = m;
            if (!_root.gameObject.activeSelf) { _root.gameObject.SetActive(true); _lastVersion = -1; }
            bool pad = MatchInput.GamepadActive;
            Vector2 cs = CanvasSize();
            bool resized = Mathf.Abs(cs.x - _canvasSize.x) > 0.5f || Mathf.Abs(cs.y - _canvasSize.y) > 0.5f;
            if (m.Version != _lastVersion || pad != _lastGamepad || resized) { _lastVersion = m.Version; _lastGamepad = pad; _canvasSize = cs; Rebuild(m); _lastSecond = -1; }
            UpdateClock(m, playClock);
        }

        public void Hide() { _root.gameObject.SetActive(false); _model = null; _lastVersion = -1; _lastSecond = -1; }

        /// <summary>Allocation-free per frame: the number, its colour and the bar are touched only when the second (or the bar's pixel width) changes (QA-783 / 773).</summary>
        void UpdateClock(PlayCallModel m, float clock)
        {
            int sec = Mathf.Max(0, Mathf.CeilToInt(clock));
            float total = Mathf.Max(1f, m.Situation.PlayClockSeconds);
            int px = Mathf.RoundToInt(150f * Mathf.Clamp01(clock / total));
            if (sec == _lastSecond && px == _lastBarPx) return;
            bool low = sec <= 5;
            if (sec != _lastSecond)
            {
                _lastSecond = sec;
                _clockNum.text = sec < SecondText.Length ? SecondText[sec] : sec.ToString();
                if (low != _lastLow || _lastBarPx < 0) { _clockNum.color = low ? UiColors.Danger : UiColors.Gold; _clockBar.color = low ? UiColors.Danger : UiColors.Gold; }
                _lastLow = low;
            }
            if (px != _lastBarPx) { _lastBarPx = px; _clockBar.rectTransform.sizeDelta = new Vector2(px, 5f); }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                if (!c.Rt.gameObject.activeSelf || Mathf.Abs(c.Scale - c.Target) < 0.0004f) continue;
                c.Scale = Mathf.Lerp(c.Scale, c.Target, 1f - Mathf.Exp(-18f * dt));
                c.Rt.localScale = new Vector3(c.Scale, c.Scale, 1f);
            }
        }

        static Color SideColor(PlaySide s) { return s == PlaySide.Offense ? SideOff : (s == PlaySide.Defense ? SideDef : SideSpec); }

        void Rebuild(PlayCallModel m)
        {
            var s = m.Situation;
            _sideChip.color = SideColor(m.Side);
            _sideText.text = m.Tab == CallTab.Special && m.Tabs.Count == 1 ? "SPECIAL" : (m.Side == PlaySide.Offense ? "OFFENSE" : "DEFENSE");
            _sideText.color = m.Side == PlaySide.Offense ? Color.black : Color.white;
            _teamText.text = s.TeamName + (m.Side == PlaySide.Offense && s.Title != "OFFENSE" ? "   " + s.Title : "");
            _situation.text = s.DownText + (string.IsNullOrEmpty(s.SpotText) ? "" : "    " + s.SpotText) + "    " + s.ClockText;
            _scoreText.text = s.DisplayScore;
            _coachStyle.text = string.IsNullOrEmpty(s.CoachStyle) ? "" : "COACH: " + s.CoachStyle;
            for (int i = 0; i < 3; i++) _toPips[i].color = i < s.Timeouts ? UiColors.AccentBlueHot : new Color(1f, 1f, 1f, 0.15f);
            _toText.text = m.TimeoutBlocked ? "NO TIMEOUTS LEFT" : (s.Timeouts > 0 ? "CALL TIMEOUT (" + s.Timeouts + ")" : "CALL TIMEOUT");
            _toText.color = m.TimeoutBlocked ? UiColors.Danger : Color.white;
            RebuildTimeoutIcon();
            _crumb.text = m.Breadcrumb;
            RebuildFooter(m);
            _r = PlayCallLayout.Compute(_canvasSize.x, _canvasSize.y, _footerRows, _crumb.GetPreferredValues(_crumb.text).x);
            ApplyLayout();
            RebuildTabs(m);
            RebuildFilters(m);
            bool playStage = m.Stage == CallStage.Play;
            _listRoot.gameObject.SetActive(!playStage || m.Tab == CallTab.Playbook);
            _previewRoot.gameObject.SetActive(!playStage);
            _cardRoot.gameObject.SetActive(playStage);
            if (_listRoot.gameObject.activeSelf) RebuildList(m);
            if (!playStage) RebuildPreview(m); else RebuildCards(m);
            if (UiTheme.IsGlass) PixFootball.UI.Glass.GlassSkin.Apply(_root);   // rows, tabs and filter chips are created lazily
        }

        /// <summary>The LT glyph (or key cap) at the left of the timeout button, drawn for the device in use.</summary>
        void RebuildTimeoutIcon()
        {
            if (_toIcon != null) { _toIcon.gameObject.SetActive(false); Destroy(_toIcon.gameObject); }
            _toIcon = UiPrompts.ButtonIcon(_toBtn.rectTransform, "glyph_lt", 40f);
            UiKit.Anchor(_toIcon, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 2), _toIcon.sizeDelta);
        }

        void RebuildTabs(PlayCallModel m)
        {
            while (_tabBg.Count < m.Tabs.Count)
            {
                var bg = UiKit.Panel(_tabRow, "Tab" + _tabBg.Count, Color.white); var t = UiKit.Label(bg.rectTransform, "", 28, Color.black, true, TextAlignmentOptions.Center, "Text");
                UiKit.Stretch(t.rectTransform); t.characterSpacing = 2f; _tabBg.Add(bg); _tabText.Add(t);
            }
            // measured widths (QA-779), shrunk together when the row would be wider than the canvas
            var widths = new float[m.Tabs.Count]; float total = 0f;
            for (int i = 0; i < m.Tabs.Count; i++)
            {
                _tabText[i].fontSize = 28f; _tabText[i].text = PlayCallModel.TabName(m.Tabs[i]);
                widths[i] = 40f + _tabText[i].preferredWidth; total += widths[i] + 14f;
            }
            float k = PlayCallLayout.FitScale(total, _r.CanvasW - _r.ListX - PlayCallLayout.Margin);
            float x = 0f;
            for (int i = 0; i < _tabBg.Count; i++)
            {
                bool on = i < m.Tabs.Count;
                _tabBg[i].gameObject.SetActive(on);
                if (!on) continue;
                CallTab tab = m.Tabs[i];
                float w = widths[i] * k;
                UiKit.Anchor(_tabBg[i].rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(w, 52));
                bool sel = tab == m.Tab;
                _tabBg[i].color = sel ? Color.white : new Color(1f, 1f, 1f, 0.08f);
                _tabText[i].fontSize = 28f * k; _tabText[i].color = sel ? Color.black : UiColors.TextSecondary;
                x += w + 14f * k;
            }
        }

        void RebuildFilters(PlayCallModel m)
        {
            bool show = m.Stage == CallStage.Play && (m.Tab == CallTab.Playbook || m.Tab == CallTab.Recent);
            _filterRow.gameObject.SetActive(show);
            if (!show) return;
            CallFilter[] fs = m.FiltersFor();
            while (_filterBg.Count < fs.Length)
            {
                var bg = UiKit.Panel(_filterRow, "F" + _filterBg.Count, Color.white); var t = UiKit.Label(bg.rectTransform, "", 24, Color.black, true, TextAlignmentOptions.Center, "Text");
                UiKit.Stretch(t.rectTransform); t.characterSpacing = 2f; _filterBg.Add(bg); _filterText.Add(t);
            }
            // measured, right aligned to the card block; shrunk when the row would run into the breadcrumb
            var widths = new float[fs.Length]; float total = 0f;
            for (int i = 0; i < fs.Length; i++)
            {
                _filterText[i].fontSize = 24f; _filterText[i].text = PlayCallModel.FilterName(fs[i]);
                widths[i] = 28f + _filterText[i].preferredWidth; total += widths[i] + 10f;
            }
            float k = PlayCallLayout.FitScale(total, _r.FilterAvail);
            float rowW = _r.FilterAvail;
            float x = rowW - total * k + 10f * k;
            for (int i = 0; i < _filterBg.Count; i++)
            {
                bool on = i < fs.Length;
                _filterBg[i].gameObject.SetActive(on);
                if (!on) continue;
                float w = widths[i] * k;
                UiKit.Anchor(_filterBg[i].rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(w, 44));
                bool sel = fs[i] == m.Filter;
                _filterBg[i].color = sel ? UiColors.AccentBlueHot : new Color(1f, 1f, 1f, 0.08f);
                _filterText[i].fontSize = 24f * k; _filterText[i].color = sel ? Color.white : UiColors.TextSecondary;
                x += w + 10f * k;
            }
        }

        // ---- list ----
        Row MakeRow(int i)
        {
            var r = new Row();
            r.Rt = UiKit.Rect(_listRoot, "Row" + i);
            r.Bg = r.Rt.gameObject.AddComponent<Image>(); r.Bg.color = UiColors.Row; r.Bg.raycastTarget = false;
            r.Accent = UiKit.Panel(r.Rt, "Accent", UiColors.Gold); UiKit.Anchor(r.Accent.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(8, 74));
            r.Name = UiKit.Label(r.Rt, "", 36, Color.white, true, TextAlignmentOptions.MidlineLeft, "Name"); UiKit.Stretch(r.Name.rectTransform, 28, 14, 120, 0); r.Name.characterSpacing = 2f; FitText(r.Name, 22f, 36f);
            r.Sub = UiKit.Label(r.Rt, "", 22, UiColors.TextSecondary, false, TextAlignmentOptions.BottomLeft, "Sub"); UiKit.Stretch(r.Sub.rectTransform, 28, 4, 120, 0); FitText(r.Sub, 14f, 22f);
            r.Count = UiKit.Label(r.Rt, "", 28, UiColors.Gold, true, TextAlignmentOptions.MidlineRight, "Count"); UiKit.Anchor(r.Count.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(110, 60));
            return r;
        }

        void RebuildList(PlayCallModel m)
        {
            var items = new List<string[]>(); int selected = 0;
            if (m.Stage == CallStage.Formation)
            {
                for (int i = 0; i < m.Formations.Count; i++) items.Add(new[] { m.Formations[i].Label, m.Formations[i].Detail, m.Formations[i].PlayCount.ToString() });
                selected = m.FormationIndex;
            }
            else if (m.Stage == CallStage.Set)
            {
                var f = m.CurrentFormation;
                if (f != null) for (int i = 0; i < f.Sets.Count; i++) items.Add(new[] { f.Sets[i].Label, f.Sets[i].Detail, f.Sets[i].Plays.Count.ToString() });
                selected = m.SetIndex;
            }
            else
            {
                // play stage in the Playbook tab: keep the formation / set context visible
                var f = m.CurrentFormation;
                if (f != null) { items.Add(new[] { f.Label, f.Detail, f.PlayCount.ToString() }); selected = 0; if (f.Sets.Count > 1 && m.CurrentSet != null) { items.Add(new[] { m.CurrentSet.Label, m.CurrentSet.Detail, m.CurrentSet.Plays.Count.ToString() }); selected = 1; } }
            }
            const float rowH = 82f, gap = 8f;
            int maxRows = Mathf.Clamp(Mathf.FloorToInt((_r.ContentH + gap) / (rowH + gap)), 3, 7);
            int first = 0;
            if (items.Count > maxRows) first = Mathf.Clamp(selected - maxRows / 2, 0, items.Count - maxRows);
            float y = 0f;
            for (int i = 0; i < _rows.Count; i++) _rows[i].Rt.gameObject.SetActive(false);
            int n = 0;
            for (int i = first; i < items.Count && n < maxRows; i++, n++)
            {
                while (_rows.Count <= n) _rows.Add(MakeRow(_rows.Count));
                var r = _rows[n]; r.Rt.gameObject.SetActive(true);
                UiKit.Anchor(r.Rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -y), new Vector2(_r.ListW, rowH));
                bool sel = i == selected && (m.Stage != CallStage.Play);
                bool ctx = m.Stage == CallStage.Play;
                r.Bg.color = sel ? UiColors.AccentBlue : (ctx ? new Color(0.16f, 0.17f, 0.23f, 1f) : UiColors.Row);
                r.Accent.gameObject.SetActive(sel);
                r.Name.text = items[i][0]; r.Sub.text = items[i][1]; r.Count.text = items[i][2];
                y += rowH + gap;
            }
            if (m.Stage != CallStage.Play && items.Count > maxRows)
            {
                // scroll hint
                _crumb.text = m.Breadcrumb + "   (" + (selected + 1) + "/" + items.Count + ")";
            }
        }

        // ---- preview (formation or set) ----
        void RebuildPreview(PlayCallModel m)
        {
            Formation f = null; string title = "", sub = "", body = "";
            bool pad = MatchInput.GamepadActive;
            if (m.Stage == CallStage.Formation && m.CurrentFormation != null)
            {
                f = m.CurrentFormation.Formation; title = m.CurrentFormation.Label; sub = m.CurrentFormation.Detail;
                body = m.CurrentFormation.PlayCount + " PLAYS IN " + m.CurrentFormation.Sets.Count + (m.CurrentFormation.Sets.Count == 1 ? " SET" : " SETS") + "\n\n" + UiPrompts.PlainHint("[A] CHOOSE FORMATION", pad);
            }
            else if (m.Stage == CallStage.Set && m.CurrentSet != null)
            {
                f = m.CurrentSet.Formation; title = m.CurrentSet.Label; sub = m.CurrentFormation.Label;
                body = m.CurrentSet.Plays.Count + " PLAYS\n" + m.CurrentSet.Detail + "\n\n" + UiPrompts.PlainHint("[A] CHOOSE SET", pad);
            }
            if (f == null) { _preview.SetDiagram(null); return; }
            _previewTitle.text = title; _previewSub.text = sub; _previewBody.text = body;
            _preview.SetDiagram(PlayDiagram.ForFormation(f));
        }

        // ---- cards ----
        static string Key(Play p) { return p.Id + (p.Mirrored ? "|M" : ""); }

        PlayDiagram DiagramFor(PlayCallModel m, Play p)
        {
            string k = Key(p);
            PlayDiagram d;
            if (!_diagrams.TryGetValue(k, out d)) { d = PlayDiagram.For(m.Book, p); _diagrams[k] = d; }
            return d;
        }

        static string RouteShort(RouteKind k)
        {
            switch (k) { case RouteKind.Go: return "STREAK"; case RouteKind.OutAndUp: return "OUT & UP"; case RouteKind.ScreenRelease: return "SCREEN"; case RouteKind.Corner2: return "CORNER"; default: return k.ToString().ToUpperInvariant(); }
        }

        /// <summary>"Left" / "Right" (and "" for none) of a run gap.</summary>
        static string GapSide(RunGap g)
        {
            string n = g.ToString();
            return n.EndsWith("Left", StringComparison.Ordinal) ? "LEFT" : (n.EndsWith("Right", StringComparison.Ordinal) ? "RIGHT" : "");
        }

        /// <summary>Where a run or a blitz goes, in words a fan uses ("OFF TACKLE RIGHT") instead of the gap letter (QA-67).</summary>
        public static string GapText(RunGap g)
        {
            if (g == RunGap.None) return "";
            string side = GapSide(g);
            switch (g.ToString().Substring(0, 1))
            {
                case "A": return "UP THE MIDDLE";
                case "B": return "OFF GUARD " + side;
                case "C": return "OFF TACKLE " + side;
                default: return "TO THE EDGE " + side;
            }
        }

        /// <summary>Player-facing name of a slot's role: position names for the backfield and tight ends, the alignment letter (X, Z, H...) for receivers.</summary>
        static string RoleName(Formation f, int slot)
        {
            if (f == null || slot < 0 || slot >= f.Slots.Length) return "";
            switch (f.Slots[slot].Position)
            {
                case Position.QB: return "QB"; case Position.RB: return "HB"; case Position.FB: return "FB"; case Position.TE: return "TE";
                default: return ReceiverLabel(f, slot);
            }
        }

        static string ReceiverLabel(Formation f, int slot)
        {
            if (f == null || slot < 0 || slot >= f.Slots.Length) return "";
            SlotSpec s = f.Slots[slot];
            return string.IsNullOrEmpty(s.Label) ? PlayDiagram.LabelFor(f, slot) : s.Label;
        }

        /// <summary>Football detail for a card: the run concept, blocking scheme and where it goes; the quarterback action and routes by receiver; the coverage, rush and drop; the kick, coverage and return plan. Never throws on short or custom assignment arrays (QA-781).</summary>
        public static string Describe(Playbook book, Play p)
        {
            var sb = new StringBuilder();
            if (p == null) return "";
            if (p.Assignments == null) return PlayCallModel.CategoryName(p.Category);
            Formation f = book != null ? book.FindFormation(p.FormationName) : null;
            Assignment[] asg = p.Assignments;
            if (p.Side == PlaySide.Offense)
            {
                var qb = asg.Length > 0 ? asg[0] as QbAssignment : null;
                if (p.IsRun)
                {
                    foreach (Assignment a in asg)
                    {
                        var c = a as CarryAssignment; if (c == null || c.IsFake) continue;
                        sb.Append(RoleName(f, a.Slot) + " " + PlayCallModel.SpacedUpper(c.Style.ToString()));
                        string gap = GapText(c.Gap); if (gap.Length > 0) sb.Append("\n" + gap);
                        break;
                    }
                    if (sb.Length == 0 && qb != null) sb.Append("QB " + PlayCallModel.SpacedUpper(qb.Action.ToString()));
                    bool pull = false, zone = false;
                    for (int i = 2; i <= 6 && i < asg.Length; i++) { var b = asg[i] as BlockAssignment; if (b == null) continue; if (b.Block == BlockKind.Pull) pull = true; if (b.Block == BlockKind.Zone) zone = true; }
                    if (sb.Length > 0) sb.Append("\n");
                    sb.Append(pull ? "PULLING LINEMAN" : (zone ? "ZONE BLOCKING" : "MAN BLOCKING"));
                    foreach (Assignment a in asg) { var b = a as BlockAssignment; if (b != null && (b.Block == BlockKind.Lead || b.Block == BlockKind.Kickout) && f != null) { sb.Append("\n" + RoleName(f, a.Slot) + " LEADS THE WAY"); break; } }
                }
                else
                {
                    if (qb != null)
                    {
                        string drop = qb.Action == QbAction.Rollout ? "ROLLOUT" : (qb.Action == QbAction.Boot ? "BOOTLEG" : (qb.Action == QbAction.PlayActionFake ? "PLAY ACTION" : (qb.Action == QbAction.ScreenPass ? "SCREEN" : (qb.DropbackSteps <= 3 ? "QUICK DROP" : qb.DropbackSteps + "-STEP DROP"))));
                        sb.Append(drop);
                        if (qb.ReadProgression.Count > 0 && f != null) sb.Append("   READ 1: " + ReceiverLabel(f, qb.ReadProgression[0]));
                    }
                    int n = 0;
                    for (int i = 1; i < asg.Length && f != null; i++)
                    {
                        var r = asg[i] as RouteAssignment; if (r == null) continue;
                        sb.Append(n % 2 == 0 ? "\n" : "   ");
                        sb.Append(ReceiverLabel(f, i) + " " + RouteShort(r.Route)); n++;
                        if (n >= 6) break;
                    }
                }
            }
            else if (p.Side == PlaySide.Defense)
            {
                string scheme = PlayCallModel.SchemeName(p.Scheme);
                sb.Append(scheme.Length > 0 ? scheme : PlayCallModel.CategoryName(p.Category));
                int rush = p.PassRushers;
                sb.Append("   " + rush + " RUSH / " + (11 - rush) + " DROP");
                bool press = false, man = false;
                foreach (Assignment a in asg) { var cv = a as CoverageAssignment; if (cv != null) { press |= cv.Press; man |= cv.IsMan; } }
                if (man) sb.Append("\nMAN COVERAGE" + (press ? " - PRESS" : ""));
                else sb.Append("\nZONE COVERAGE" + (press ? " - PRESS" : ""));
                foreach (Assignment a in asg) if (a is SpyAssignment) { sb.Append("\nQB SPY"); break; }
                foreach (Assignment a in asg) { var bz = a as BlitzAssignment; if (bz != null && f != null && a.Slot >= 0 && a.Slot < f.Slots.Length) { sb.Append("\nBLITZ: " + PlayDiagram.LabelFor(f, a.Slot) + " " + GapText(bz.Gap)); break; } }
            }
            else DescribeSpecial(sb, p, asg);
            if (!string.IsNullOrEmpty(p.Note)) { if (sb.Length > 0) sb.Append("\n"); sb.Append(p.Note); }   // v0.10.0: the play's coaching line
            return sb.ToString();
        }

        /// <summary>Special teams plays describe the plan instead of repeating the category chip (QA-65 / 64): the kick, who covers or rushes, how the return runs.</summary>
        static void DescribeSpecial(StringBuilder sb, Play p, Assignment[] asg)
        {
            KickAssignment kick = null; int gunners = 0, lanes = 0, rushers = 0; ReturnerAssignment ret = null;
            foreach (Assignment a in asg)
            {
                var k = a as KickAssignment; if (k != null && (kick == null || k.Role == KickRole.Kicker)) kick = k;
                var l = a as LaneAssignment; if (l != null) { lanes++; if (l.Gunner) gunners++; }
                if (a is RushAssignment || a is BlitzAssignment) rushers++;
                var r = a as ReturnerAssignment; if (r != null && ret == null) ret = r;
            }
            if (kick != null)
            {
                bool directional = kick.KickType == KickKind.DirectionalLeft || kick.KickType == KickKind.DirectionalRight;
                string aim = directional || kick.KickType == KickKind.FieldGoal || kick.KickType == KickKind.ExtraPoint ? "" : (kick.AimX < -2f ? "   AIM LEFT" : (kick.AimX > 2f ? "   AIM RIGHT" : ""));
                string kname = PlayCallModel.SpacedUpper(kick.KickType.ToString());
                if (kname == PlayTags.For(p)) kname = "STANDARD " + kname;   // never the same word as the category chip above it
                sb.Append(kname + aim);
            }
            if (gunners > 0) sb.Append((sb.Length > 0 ? "\n" : "") + gunners + " GUNNER" + (gunners == 1 ? "" : "S") + (lanes > gunners ? " / " + (lanes - gunners) + " IN COVERAGE" : ""));
            else if (lanes > 0) sb.Append((sb.Length > 0 ? "\n" : "") + lanes + " IN COVERAGE");
            if (rushers > 0) sb.Append((sb.Length > 0 ? "\n" : "") + rushers + " RUSHING THE KICK");
            if (ret != null)
            {
                string lane = ret.ReturnLaneX < -3f ? "LEFT" : (ret.ReturnLaneX > 3f ? "RIGHT" : "UP THE MIDDLE");
                sb.Append((sb.Length > 0 ? "\n" : "") + "RETURN " + lane + (ret.FairCatchAllowed ? "\nFAIR CATCH ALLOWED" : ""));
            }
            if (sb.Length == 0) sb.Append(PlayCallModel.CategoryName(p.Category));
        }

        void RebuildCards(PlayCallModel m)
        {
            var page = m.PagePlays();
            _empty.gameObject.SetActive(page.Count == 0);
            // one counter only (QA-160): the position of the highlighted play in the list
            _pageText.text = page.Count == 0 ? "" : "PLAY " + (m.PlayIndex + 1) + " OF " + m.Plays.Count;
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                if (i >= page.Count) { c.Rt.gameObject.SetActive(false); continue; }
                Play p = m.Displayed(page[i]);
                c.Rt.gameObject.SetActive(true);
                c.Play = p;
                bool sel = i == m.SelectedOnPage;
                c.Target = sel ? 1.035f : 0.965f; c.Scale = Mathf.Lerp(c.Scale, c.Target, 0.5f);
                c.Outline.gameObject.SetActive(sel);
                c.Bg.color = sel ? new Color(0.14f, 0.16f, 0.24f, 1f) : new Color(0.09f, 0.10f, 0.13f, 1f);
                c.Group.alpha = sel ? 1f : 0.82f;
                c.Title.text = p.Name.ToUpperInvariant();
                Formation f = m.Book.FindFormation(p.FormationName);
                c.Sub.text = f != null ? f.Name.ToUpperInvariant() : "";
                string tag = PlayTags.For(p);
                var sp = UiTheme.IsGlass ? null : UiKit.Sprite(PlayTags.SpriteKey(tag));
                c.Tag.sprite = sp;
                if (sp != null) { c.Tag.color = Color.white; c.TagText.text = ""; UiKit.Anchor(c.Tag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), c.Tag.rectTransform.anchoredPosition, new Vector2(124, 40)); }
                else
                {
                    c.Tag.color = PlayTags.ColorOf(tag); c.TagText.text = tag; c.TagText.color = PlayTags.TextColorOf(tag);
                    if (UiTheme.IsGlass) { c.Tag.color = new Color(0.12f, 0.12f, 0.14f, 0.9f); c.TagText.color = Color.Lerp(PlayTags.ColorOf(tag), Color.white, 0.35f); }
                    UiKit.Anchor(c.Tag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), c.Tag.rectTransform.anchoredPosition, new Vector2(Mathf.Max(124f, c.TagText.preferredWidth + 28f), 40));
                }
                c.Info.text = Describe(m.Book, p);
                c.Reason.text = m.ReasonFor(page[i]);
                string key = Key(p);
                if (c.Key != key) { c.Key = key; c.Diagram.SetDiagram(DiagramFor(m, p)); }
            }
        }

        // ---- footer ----
        void RebuildFooter(PlayCallModel m)
        {
            var items = new List<PromptItem>(12);
            items.Add(new PromptItem(new[] { "glyph_a" }, m.Stage == CallStage.Play ? "CALL PLAY" : "SELECT"));
            if (m.Stage != CallStage.Formation || m.Tab != CallTab.Playbook) items.Add(new PromptItem(new[] { "glyph_b" }, m.Tab != CallTab.Playbook ? "PLAYBOOK" : "BACK"));
            items.Add(new PromptItem(new[] { "glyph_dpad" }, m.Stage == CallStage.Play ? "MOVE" : "CHOOSE"));
            if (m.Stage == CallStage.Play)
            {
                items.Add(new PromptItem(new[] { "glyph_lb", "glyph_rb" }, "PAGE"));
                if (m.Tab == CallTab.Playbook || m.Tab == CallTab.Recent) items.Add(new PromptItem(new[] { "glyph_x" }, "FILTER"));
            }
            if (m.CanFlip) items.Add(new PromptItem(new[] { "glyph_y" }, m.Flip ? "FLIPPED" : "FLIP PLAY"));
            if (m.Tabs.Count > 1) items.Add(new PromptItem(new[] { "glyph_rt" }, "TABS"));
            items.Add(new PromptItem(new[] { "glyph_lt" }, "TIMEOUT"));
            items.Add(new PromptItem(new[] { "glyph_view" }, "CAMERA"));
            items.Add(new PromptItem(new[] { "glyph_menu" }, "PAUSE"));
            _footerRows = PromptWrap.Fill(_footerRow, items, 44f, 26f, 44f, _canvasSize.x - 2f * PlayCallLayout.Margin, TextAnchor.MiddleLeft, 2);
        }
    }
}
