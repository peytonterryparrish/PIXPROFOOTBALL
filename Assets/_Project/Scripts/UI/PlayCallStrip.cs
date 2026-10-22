using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// The v0.7 play-call sheet (glass): an overlay strip along the bottom of the screen over the live, blurred field instead of a full-screen page. Tabs along the top (SUGGESTED, FORMATION, CONCEPT,
    /// PLAY TYPE, PERSONNEL, RECENT, FAVORITES, SPECIAL TEAMS), a group list on the left (formation families, concepts, play types, personnel packages), three big play-art cards (the focused one is
    /// larger: the enlarged preview) with the formation drawn on a mini field, the coach's three picks always visible on the right, a quick-search field, and one row of button prompts. Opening it
    /// lands on the first suggestion, so one A press calls a play. It only renders the <see cref="PlayCallModel"/> (sheet mode); the director feeds the model the input.
    /// </summary>
    public sealed class PlayCallStrip : MonoBehaviour
    {
        // design size of the strip block (canvas units): it sits centred on the bottom edge; the canvas is never smaller than 1920 x 1080 (CanvasScaler "Expand")
        const float BlockW = 1824f, BlockH = 440f, BottomMargin = 36f, Pad = 24f;
        const float LeftColW = 300f, RightColW = 330f, ColGap = 22f, TabH = 48f, LegendH = 42f;
        const float FocusCardW = 460f, SideCardW = 288f, CardGap = 28f;

        RectTransform _root, _block, _tabRow, _leftCol, _cardArea, _rightCol, _legendRow, _searchBox;
        PlayCallModel _model; int _lastVersion = -1; bool _lastGamepad = true; Vector2 _lastCanvas;
        RectTransform _canvasRt;
        // header
        GlassGraphic _sideChip; TextMeshProUGUI _sideText, _teamText, _situation, _score, _coach;
        GlassGraphic _clockPanel; TextMeshProUGUI _clockNum, _clockLabel, _toText; Image[] _pips = new Image[3]; RectTransform _toIcon; RectTransform _toHolder;
        int _lastSecond = -1;
        // tabs
        sealed class TabW { public RectTransform Rt; public TextMeshProUGUI Label; public Image Line; }
        readonly List<TabW> _tabs = new List<TabW>();
        TextMeshProUGUI _searchText, _searchHint, _pageText;
        // left column
        sealed class RowW { public RectTransform Rt; public GlassGraphic Bg; public Image Rail; public TextMeshProUGUI Name, Count; }
        readonly List<RowW> _rows = new List<RowW>();
        TextMeshProUGUI _leftTitle, _leftBody;
        // cards
        sealed class CardW
        {
            public RectTransform Rt; public GlassGraphic Surface, DiagramBg, Tag; public PlayDiagramGraphic Diagram;
            public TextMeshProUGUI Title, Sub, TagText, Info, Reason; public CanvasGroup Group; public string Key = ""; public bool WasSel;
        }
        readonly List<CardW> _cards = new List<CardW>();
        TextMeshProUGUI _empty;
        // right column
        sealed class PickW { public RectTransform Rt; public GlassGraphic Chip; public TextMeshProUGUI Num, Name, Reason; }
        readonly List<PickW> _picks = new List<PickW>();
        TextMeshProUGUI _rightTitle;
        readonly Dictionary<string, PlayDiagram> _diagrams = new Dictionary<string, PlayDiagram>();
        static readonly string[] SecondText = BuildSecondText();
        static string[] BuildSecondText() { var t = new string[100]; for (int i = 0; i < t.Length; i++) t[i] = i.ToString(); return t; }

        public static PlayCallStrip Create(Transform parent)
        {
            var r = UiKit.Rect(parent, "PlayCallStrip"); UiKit.Stretch(r);
            var s = r.gameObject.AddComponent<PlayCallStrip>();
            s._root = r; s.Build();
            return s;
        }

        // ================================================================================================ build
        void Build()
        {
            var t = GlassTheme.T;
            // a soft shade up the lower part of the screen keeps the strip readable over a bright field, the upper part stays the live game
            var shade = GlassWidgets.EdgeShade(_root, false, 760f, 0.62f, "BottomShade"); shade.raycastTarget = false;
            var topShade = GlassWidgets.EdgeShade(_root, true, 240f, 0.55f, "TopShade"); topShade.raycastTarget = false;
            BuildHeader();

            _block = UiKit.Rect(_root, "Block");
            _block.anchorMin = _block.anchorMax = new Vector2(0.5f, 0f); _block.pivot = new Vector2(0.5f, 0f);
            _block.anchoredPosition = new Vector2(0f, BottomMargin); _block.sizeDelta = new Vector2(BlockW, BlockH);
            var panel = GlassKit.Panel(_block, "Panel", 1.25f, true, t.cornerRadiusLarge); UiKit.Stretch(panel.rectTransform);

            _tabRow = UiKit.Rect(_block, "Tabs"); UiKit.Anchor(_tabRow, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, -10f), new Vector2(BlockW - 2f * Pad - 380f, TabH));
            var rule = GlassKit.Rule(_block, "TabRule", 0.7f); UiKit.Anchor(rule.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, -(10f + TabH + 4f)), new Vector2(BlockW - 2f * Pad, 1.4f));
            BuildSearch();

            float top = -(10f + TabH + 14f), contentH = BlockH - (10f + TabH + 14f) - LegendH - 14f;
            _leftCol = UiKit.Rect(_block, "Left"); UiKit.Anchor(_leftCol, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Pad, top), new Vector2(LeftColW, contentH));
            float cardsX = Pad + LeftColW + ColGap, cardsW = BlockW - 2f * Pad - LeftColW - RightColW - 2f * ColGap;
            _cardArea = UiKit.Rect(_block, "Cards"); UiKit.Anchor(_cardArea, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(cardsX, top), new Vector2(cardsW, contentH));
            _rightCol = UiKit.Rect(_block, "Right"); UiKit.Anchor(_rightCol, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Pad, top), new Vector2(RightColW, contentH));
            _legendRow = UiKit.Rect(_block, "Legend"); UiKit.Anchor(_legendRow, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(Pad, 10f), new Vector2(BlockW - 2f * Pad, LegendH));

            BuildLeft(contentH); BuildCards(contentH, cardsW); BuildRight(contentH);
        }

        TextMeshProUGUI Txt(Transform parent, string name, float size, Color color, bool bold, TextAlignmentOptions align, float spacing = 0f)
        {
            var tm = GlassKit.Text(parent, "", bold ? GlassText.Heading : GlassText.Body, align, name);
            GlassKit.Style(tm, size, color, bold, spacing);
            tm.textWrappingMode = TextWrappingModes.NoWrap; tm.overflowMode = TextOverflowModes.Ellipsis;
            return tm;
        }

        void BuildHeader()
        {
            var t = GlassTheme.T;
            // top left: side, team, down and distance, score
            var bar = GlassKit.Panel(_root, "Situation", 1.2f, true, t.cornerRadiusLarge);
            UiKit.Anchor(bar.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(t.safeMarginX * 0.5f, -t.safeMarginY * 0.6f), new Vector2(1100f, 84f));
            _sideChip = GlassKit.Solid(bar.rectTransform, "SideChip", t.accent, 12f, 0f);
            UiKit.Anchor(_sideChip.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(200f, 56f));
            _sideText = Txt(_sideChip.rectTransform, "Side", 28f, t.textOnAction, true, TextAlignmentOptions.Center, 2f); UiKit.Stretch(_sideText.rectTransform);
            _teamText = Txt(bar.rectTransform, "Team", 30f, t.textPrimary, true, TextAlignmentOptions.MidlineLeft, 2f);
            UiKit.Anchor(_teamText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(236f, 17f), new Vector2(400f, 40f)); UiKit.Fit(_teamText, 20f);
            _situation = Txt(bar.rectTransform, "Situation", 24f, t.accent, true, TextAlignmentOptions.MidlineLeft, 1.5f);
            UiKit.Anchor(_situation.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(236f, -19f), new Vector2(400f, 34f)); UiKit.Fit(_situation, 16f);
            _score = Txt(bar.rectTransform, "Score", 30f, t.textPrimary, true, TextAlignmentOptions.MidlineLeft, 1f);
            UiKit.Anchor(_score.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(650f, 0f), new Vector2(430f, 44f)); UiKit.Fit(_score, 18f);
            _coach = Txt(bar.rectTransform, "Coach", 20f, t.textSecondary, true, TextAlignmentOptions.MidlineLeft, 1f);
            UiKit.Anchor(_coach.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(660f, -19f), new Vector2(420f, 30f));

            // top right: play clock + timeouts
            _clockPanel = GlassKit.Panel(_root, "Clock", 1.2f, true, t.cornerRadiusLarge);
            UiKit.Anchor(_clockPanel.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-t.safeMarginX * 0.5f, -t.safeMarginY * 0.6f), new Vector2(680f, 84f));
            _clockNum = Txt(_clockPanel.rectTransform, "Num", 58f, t.textPrimary, true, TextAlignmentOptions.Center);
            UiKit.Anchor(_clockNum.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 2f), new Vector2(100f, 70f));
            _clockLabel = Txt(_clockPanel.rectTransform, "Lbl", 20f, t.textSecondary, true, TextAlignmentOptions.MidlineRight, 2f);
            UiKit.Anchor(_clockLabel.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-136f, 0f), new Vector2(190f, 30f)); _clockLabel.text = "PLAY CLOCK";
            for (int i = 0; i < 3; i++)
            {
                var p = UiKit.Panel(_clockPanel.rectTransform, "Pip" + i, t.accent);
                UiKit.Anchor(p.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(100f + i * 28f, -27f), new Vector2(22f, 8f));
                _pips[i] = p;
            }
            _toHolder = UiKit.Rect(_clockPanel.rectTransform, "Timeout"); UiKit.Anchor(_toHolder, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(22f, 8f), new Vector2(330f, 48f));
            _toText = Txt(_toHolder, "Text", 24f, t.textPrimary, true, TextAlignmentOptions.MidlineLeft, 1.5f); UiKit.Stretch(_toText.rectTransform, 74f, 0f, 0f, 0f); UiKit.Fit(_toText, 18f);
        }

        void BuildSearch()
        {
            var t = GlassTheme.T;
            var box = GlassKit.Well(_block, "Search", t.cornerRadiusSmall); _searchBox = box.rectTransform;
            UiKit.Anchor(_searchBox, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-Pad, -10f), new Vector2(350f, TabH - 2f));
            var icon = GlassKit.Icon(_searchBox, "ui_search", 26f, t.textMuted);
            UiKit.Anchor(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(26f, 26f));
            _searchText = Txt(_searchBox, "Text", 24f, t.textPrimary, true, TextAlignmentOptions.MidlineLeft, 1f); UiKit.Stretch(_searchText.rectTransform, 52f, 0f, 12f, 0f);
            _searchHint = Txt(_searchBox, "Hint", 20f, t.textMuted, false, TextAlignmentOptions.MidlineLeft); UiKit.Stretch(_searchHint.rectTransform, 52f, 0f, 12f, 0f);
        }

        void BuildLeft(float h)
        {
            var t = GlassTheme.T;
            _leftTitle = Txt(_leftCol, "Title", 20f, t.textSecondary, true, TextAlignmentOptions.MidlineLeft, 3f);
            UiKit.Anchor(_leftTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(4f, 0f), new Vector2(LeftColW, 30f));
            _leftBody = GlassKit.Text(_leftCol, "", GlassText.Body, TextAlignmentOptions.TopLeft, "Body");
            GlassKit.Style(_leftBody, 22f, t.textSecondary, false, 0f); _leftBody.textWrappingMode = TextWrappingModes.Normal; _leftBody.lineSpacing = 8f;
            UiKit.Anchor(_leftBody.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(4f, -40f), new Vector2(LeftColW - 8f, h - 44f));
        }

        void BuildCards(float h, float w)
        {
            for (int i = 0; i < PlayCallModel.PageSize; i++) _cards.Add(MakeCard(i, h));
            _empty = Txt(_cardArea, "Empty", 34f, GlassTheme.T.textMuted, true, TextAlignmentOptions.Center, 3f); UiKit.Stretch(_empty.rectTransform); _empty.text = "NO PLAYS IN THIS VIEW";
            _pageText = Txt(_block, "Page", 20f, GlassTheme.T.textMuted, true, TextAlignmentOptions.MidlineRight, 2f);
            UiKit.Anchor(_pageText.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-Pad, 14f), new Vector2(420f, 30f));
            _pageText.gameObject.SetActive(false);
        }

        CardW MakeCard(int i, float h)
        {
            var t = GlassTheme.T; var c = new CardW();
            c.Rt = UiKit.Rect(_cardArea, "Card" + i); c.Rt.anchorMin = c.Rt.anchorMax = new Vector2(0f, 1f); c.Rt.pivot = new Vector2(0f, 1f);
            c.Group = c.Rt.gameObject.AddComponent<CanvasGroup>();
            c.Surface = GlassKit.Panel(c.Rt, "Surface", 1.1f, true, t.cornerRadius); UiKit.Stretch(c.Surface.rectTransform);
            c.Title = Txt(c.Rt, "Title", 28f, t.textPrimary, true, TextAlignmentOptions.MidlineLeft, 1.5f); UiKit.Anchor(c.Title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -10f), new Vector2(300f, 36f));
            c.Sub = Txt(c.Rt, "Sub", 20f, t.textSecondary, true, TextAlignmentOptions.MidlineLeft, 2f); UiKit.Anchor(c.Sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -46f), new Vector2(300f, 28f));
            c.DiagramBg = GlassKit.Surface(c.Rt, "DiagramBg", GlassKind.Well, t.cornerRadiusSmall, false, 1f);
            c.DiagramBg.gameObject.AddComponent<RectMask2D>();
            var dr = UiKit.Rect(c.DiagramBg.rectTransform, "Diagram"); UiKit.Stretch(dr, 3f, 3f, 3f, 3f);
            c.Diagram = dr.gameObject.AddComponent<PlayDiagramGraphic>();
            c.Tag = GlassKit.Solid(c.Rt, "Tag", new Color(0.12f, 0.12f, 0.14f, 0.9f), 10f, 1.2f);
            c.TagText = Txt(c.Tag.rectTransform, "TagText", 20f, t.textPrimary, true, TextAlignmentOptions.Center, 2f); UiKit.Stretch(c.TagText.rectTransform);
            c.Info = GlassKit.Text(c.Rt, "", GlassText.Body, TextAlignmentOptions.TopLeft, "Info"); GlassKit.Style(c.Info, 21f, t.textSecondary, false, 0f);
            c.Info.textWrappingMode = TextWrappingModes.Normal; c.Info.lineSpacing = 4f; c.Info.overflowMode = TextOverflowModes.Ellipsis;
            c.Reason = Txt(c.Rt, "Reason", 20f, t.accent, true, TextAlignmentOptions.BottomLeft, 1f);
            return c;
        }

        void BuildRight(float h)
        {
            var t = GlassTheme.T;
            _rightTitle = Txt(_rightCol, "Title", 20f, t.textSecondary, true, TextAlignmentOptions.MidlineLeft, 3f);
            UiKit.Anchor(_rightTitle.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(4f, 0f), new Vector2(RightColW, 30f));
            float ph = (h - 40f - 2f * 10f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var p = new PickW();
                p.Rt = UiKit.Rect(_rightCol, "Pick" + i); UiKit.Anchor(p.Rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -40f - i * (ph + 10f)), new Vector2(RightColW, ph));
                var bg = GlassKit.Surface(p.Rt, "Bg", GlassKind.Glass, t.cornerRadiusSmall, false, 0.8f); UiKit.Stretch(bg.rectTransform);
                p.Chip = GlassKit.Solid(p.Rt, "Chip", t.accent, 18f, 0f); UiKit.Anchor(p.Chip.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(36f, 36f));
                p.Num = Txt(p.Chip.rectTransform, "Num", 24f, t.textOnAction, true, TextAlignmentOptions.Center); UiKit.Stretch(p.Num.rectTransform);
                p.Name = Txt(p.Rt, "Name", 24f, t.textPrimary, true, TextAlignmentOptions.MidlineLeft, 1f); UiKit.Anchor(p.Name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -8f), new Vector2(RightColW - 72f, 32f)); UiKit.Fit(p.Name, 18f);
                p.Reason = Txt(p.Rt, "Reason", 20f, t.textSecondary, false, TextAlignmentOptions.TopLeft); p.Reason.textWrappingMode = TextWrappingModes.Normal;
                UiKit.Anchor(p.Reason.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -40f), new Vector2(RightColW - 72f, ph - 44f));
                _picks.Add(p);
            }
        }

        // ================================================================================================ showing
        public void Show(PlayCallModel m, float playClock)
        {
            _model = m;
            if (!_root.gameObject.activeSelf) { _root.gameObject.SetActive(true); _lastVersion = -1; }
            bool pad = MatchInput.GamepadActive;
            Vector2 cs = CanvasSize();
            bool resized = Mathf.Abs(cs.x - _lastCanvas.x) > 0.5f || Mathf.Abs(cs.y - _lastCanvas.y) > 0.5f;
            if (m.Version != _lastVersion || pad != _lastGamepad || resized) { _lastVersion = m.Version; _lastGamepad = pad; _lastCanvas = cs; Rebuild(m); _lastSecond = -1; }
            UpdateClock(m, playClock);
        }

        public void Hide() { _root.gameObject.SetActive(false); _model = null; _lastVersion = -1; _lastSecond = -1; }

        Vector2 CanvasSize()
        {
            if (_canvasRt == null) { var c = GetComponentInParent<Canvas>(); if (c != null) _canvasRt = (RectTransform)c.rootCanvas.transform; }
            return _canvasRt != null ? _canvasRt.rect.size : new Vector2(1920f, 1080f);
        }

        void UpdateClock(PlayCallModel m, float clock)
        {
            int sec = Mathf.Max(0, Mathf.CeilToInt(clock));
            if (sec == _lastSecond) return;
            _lastSecond = sec;
            _clockNum.text = sec < SecondText.Length ? SecondText[sec] : sec.ToString();
            _clockNum.color = sec <= 5 ? GlassTheme.T.danger : GlassTheme.T.textPrimary;
        }

        static string TabLabel(CallTab t)
        {
            switch (t) { case CallTab.AskCoach: return "SUGGESTED"; case CallTab.Special: return "SPECIAL TEAMS"; default: return PlayCallModel.TabName(t); }
        }

        void Rebuild(PlayCallModel m)
        {
            var t = GlassTheme.T; var s = m.Situation;
            // ---- header
            bool off = m.Side == PlaySide.Offense;
            _sideChip.color = m.Side == PlaySide.Offense ? t.accent : (m.Side == PlaySide.Defense ? t.lightGrey : t.warning);
            _sideText.text = m.Tab == CallTab.Special && m.Tabs.Count == 1 ? "SPECIAL" : (off ? "OFFENSE" : "DEFENSE");
            _teamText.text = s.TeamName;
            _situation.text = s.DownText + (string.IsNullOrEmpty(s.SpotText) ? "" : "   " + s.SpotText);
            _score.text = s.ClockText + "    " + s.DisplayScore;
            _coach.text = "";
            for (int i = 0; i < 3; i++) _pips[i].color = i < s.Timeouts ? t.accent : new Color(1f, 1f, 1f, 0.16f);
            _toText.text = m.TimeoutBlocked ? "NO TIMEOUTS" : (s.Timeouts > 0 ? "TIMEOUT (" + s.Timeouts + ")" : "TIMEOUT");
            _toText.color = m.TimeoutBlocked ? t.danger : t.textPrimary;
            if (_toIcon != null) { _toIcon.gameObject.SetActive(false); Destroy(_toIcon.gameObject); }
            _toIcon = UiPrompts.ButtonIcon(_toHolder, "glyph_analog_click", 36f);
            UiKit.Anchor(_toIcon, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), _toIcon.sizeDelta);
            UiKit.Stretch(_toText.rectTransform, _toIcon.sizeDelta.x + 14f, 0f, 0f, 0f);
            // ---- sections
            RebuildTabs(m); RebuildSearch(m); RebuildLeft(m); RebuildCards(m); RebuildRight(m); RebuildLegend(m);
        }

        void RebuildTabs(PlayCallModel m)
        {
            var t = GlassTheme.T;
            while (_tabs.Count < m.Tabs.Count)
            {
                var w = new TabW { Rt = UiKit.Rect(_tabRow, "Tab" + _tabs.Count) };
                w.Label = Txt(w.Rt, "Label", 22f, t.textSecondary, true, TextAlignmentOptions.Center, 2f); UiKit.Stretch(w.Label.rectTransform);
                w.Line = UiKit.Panel(w.Rt, "Line", t.accent); UiKit.Anchor(w.Line.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 2f), new Vector2(60f, 4f));
                _tabs.Add(w);
            }
            // the LT / RT prompts frame the row
            var widths = new float[m.Tabs.Count]; float total = 0f;
            for (int i = 0; i < m.Tabs.Count; i++) { _tabs[i].Label.text = TabLabel(m.Tabs[i]); _tabs[i].Label.fontSize = 22f; widths[i] = 36f + _tabs[i].Label.preferredWidth; total += widths[i] + 6f; }
            float avail = BlockW - 2f * Pad - 380f, k = Mathf.Min(1f, avail / Mathf.Max(1f, total));
            float x = 0f;
            for (int i = 0; i < _tabs.Count; i++)
            {
                bool on = i < m.Tabs.Count; _tabs[i].Rt.gameObject.SetActive(on); if (!on) continue;
                float w = widths[i] * k; bool sel = m.Tabs[i] == m.Tab;
                UiKit.Anchor(_tabs[i].Rt, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(w, TabH));
                _tabs[i].Label.color = sel ? t.textPrimary : t.textSecondary; _tabs[i].Label.fontSize = 22f * k;
                _tabs[i].Line.gameObject.SetActive(sel); _tabs[i].Line.rectTransform.sizeDelta = new Vector2(Mathf.Max(30f, w - 36f), 4f);
                x += w + 6f * k;
            }
        }

        void RebuildSearch(PlayCallModel m)
        {
            bool has = m.SearchText.Length > 0;
            _searchText.gameObject.SetActive(has); _searchHint.gameObject.SetActive(!has);
            _searchText.text = has ? (m.SearchIsLetter ? m.SearchText.ToUpperInvariant() + "..." : m.SearchText.ToUpperInvariant()) : "";
            _searchHint.text = MatchInput.GamepadActive ? "RS LEFT / RIGHT: FIND A-Z" : "/ TO SEARCH BY NAME";
            _searchBox.gameObject.SetActive(m.SheetMode);
        }

        // ---- left column: the group list (or a summary on the tabs without groups)
        RowW MakeRow(int i)
        {
            var t = GlassTheme.T; var r = new RowW();
            r.Rt = UiKit.Rect(_leftCol, "Row" + i); r.Rt.anchorMin = r.Rt.anchorMax = new Vector2(0f, 1f); r.Rt.pivot = new Vector2(0f, 1f);
            r.Bg = GlassKit.Surface(r.Rt, "Bg", GlassKind.Glass, t.cornerRadiusSmall, false, 0.8f); UiKit.Stretch(r.Bg.rectTransform);
            r.Rail = UiKit.Panel(r.Rt, "Rail", t.accent); UiKit.Anchor(r.Rail.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(5f, 30f));
            r.Name = Txt(r.Rt, "Name", 22f, t.textPrimary, true, TextAlignmentOptions.MidlineLeft, 1f); UiKit.Stretch(r.Name.rectTransform, 18f, 0f, 70f, 0f); UiKit.Fit(r.Name, 16f);
            r.Count = Txt(r.Rt, "Count", 20f, t.textSecondary, true, TextAlignmentOptions.MidlineRight); UiKit.Anchor(r.Count.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(64f, 32f));
            return r;
        }

        void RebuildLeft(PlayCallModel m)
        {
            var t = GlassTheme.T;
            for (int i = 0; i < _rows.Count; i++) _rows[i].Rt.gameObject.SetActive(false);
            if (m.TabHasGroups && m.Groups.Count > 0)
            {
                _leftTitle.text = TabLabel(m.Tab) + "  (" + (m.GroupIndex + 1) + "/" + m.Groups.Count + ")"; _leftBody.gameObject.SetActive(false);
                const float rowH = 44f, gap = 6f;
                float avail = _leftCol.rect.height - 40f;
                int visible = Mathf.Clamp(Mathf.FloorToInt((avail + gap) / (rowH + gap)), 3, 8);
                int first = Mathf.Clamp(m.GroupIndex - visible / 2, 0, Mathf.Max(0, m.Groups.Count - visible));
                for (int n = 0, i = first; i < m.Groups.Count && n < visible; i++, n++)
                {
                    while (_rows.Count <= n) _rows.Add(MakeRow(_rows.Count));
                    var r = _rows[n]; r.Rt.gameObject.SetActive(true);
                    UiKit.Anchor(r.Rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -40f - n * (rowH + gap)), new Vector2(LeftColW, rowH));
                    bool sel = i == m.GroupIndex; var g = m.Groups[i];
                    r.Name.text = g.Label; r.Count.text = g.Plays.Count.ToString();
                    r.Bg.SetFocus(sel ? 1f : 0f); r.Bg.SetBrighten(sel ? 0.06f : 0f); r.Bg.GlowScale = 0.5f;
                    r.Rail.gameObject.SetActive(sel);
                    r.Name.color = sel ? t.textPrimary : t.textSecondary; r.Count.color = sel ? t.accent : t.textMuted;
                }
            }
            else
            {
                _leftBody.gameObject.SetActive(true);
                bool pad = MatchInput.GamepadActive;
                string title, body;
                switch (m.Tab)
                {
                    case CallTab.AskCoach: title = "COACH'S CALL"; body = "The coach reads the down, distance, field position and clock.\n\n" + UiPrompts.PlainHint("[A] CALL THE FIRST PICK", pad); break;
                    case CallTab.Recent: title = "RECENT CALLS"; body = "The last plays you called, newest first."; break;
                    case CallTab.Favorites: title = "FAVORITES"; body = m.FavoritePlays.Count == 0 ? "Plays you call twice or more show up here." : "The plays you call the most."; break;
                    case CallTab.Special: title = "SPECIAL TEAMS"; body = "Kicks, returns and blocks for this situation."; break;
                    default: title = m.SearchText.Length > 0 ? "SEARCH" : TabLabel(m.Tab); body = m.SearchText.Length > 0 ? m.Plays.Count + " PLAYS MATCH.\n\n" + UiPrompts.PlainHint("[B] CLEAR THE SEARCH", pad) : ""; break;
                }
                _leftTitle.text = title; _leftBody.text = body;
            }
        }

        // ---- cards
        static string Key(Play p) { return p.Id + (p.Mirrored ? "|M" : ""); }

        PlayDiagram DiagramFor(PlayCallModel m, Play p)
        {
            string k = Key(p); PlayDiagram d;
            if (!_diagrams.TryGetValue(k, out d)) { d = PlayDiagram.For(m.Book, p); _diagrams[k] = d; }
            return d;
        }

        void RebuildCards(PlayCallModel m)
        {
            var t = GlassTheme.T;
            var page = m.PagePlays();
            _empty.gameObject.SetActive(page.Count == 0);
            _pageText.gameObject.SetActive(page.Count > 0);
            if (page.Count > 0)
            {
                _pageText.text = "PLAY " + (m.PlayIndex + 1) + " OF " + m.Plays.Count + (m.Filter != CallFilter.All ? "   -   " + PlayCallModel.FilterName(m.Filter) : "");
            }
            // widths: the focused card is the enlarged preview
            float areaW = _cardArea.rect.width, areaH = _cardArea.rect.height;
            int n = page.Count; float x = 0f;
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i];
                if (i >= n) { c.Rt.gameObject.SetActive(false); continue; }
                bool sel = i == m.SelectedOnPage;
                float w = n == 1 ? Mathf.Min(areaW, FocusCardW + 140f) : (sel ? FocusCardW : (n == 2 ? Mathf.Min(areaW - FocusCardW - CardGap, 360f) : SideCardW));
                c.Rt.gameObject.SetActive(true);
                UiKit.Anchor(c.Rt, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, 0f), new Vector2(w, areaH));
                x += w + CardGap;
                Play p = m.Displayed(page[i]);
                c.Surface.SetFocus(sel ? 1f : 0f); c.Surface.SetBrighten(sel ? 0.07f : -0.02f); c.Surface.SetDensity(sel ? 1.3f : 1.1f);
                c.Group.alpha = sel ? 1f : 0.86f;
                if (sel && !c.WasSel) c.Rt.localScale = Vector3.one * 0.965f;   // the focused card grows into place (eased in Update)
                c.WasSel = sel;
                // text
                c.Title.text = p.Name.ToUpperInvariant();
                c.Title.fontSize = sel ? 30f : 26f;
                string tagName = PlayTags.For(p); float tagW = Mathf.Max(70f, tagName.Length * 13f + 30f);
                UiKit.Anchor(c.Title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -10f), new Vector2(w - 40f - tagW - 6f, 38f)); UiKit.Fit(c.Title, 16f);
                Formation f = m.Book.FindFormation(p.FormationName);
                string formation = f != null ? f.Name.ToUpperInvariant() : "";
                string pers = f != null && p.Side == PlaySide.Offense ? PlayCallModel.PersonnelText(f) : (f != null ? f.Unit : "");
                c.Sub.text = formation + (pers.Length > 0 && sel ? "   " + pers.ToUpperInvariant() : "");
                UiKit.Anchor(c.Sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -48f), new Vector2(w - 40f, 28f)); UiKit.Fit(c.Sub, 16f);
                // tag chip (top right): category colour on dark glass
                string tag = PlayTags.For(p);
                c.TagText.text = tag; c.TagText.color = Color.Lerp(PlayTags.ColorOf(tag), Color.white, 0.4f);
                float tw = Mathf.Max(70f, c.TagText.preferredWidth + 26f);
                UiKit.Anchor(c.Tag.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -12f), new Vector2(tw, 32f));
                // diagram: the mini field with the formation and the routes
                float dh = sel ? 160f : 128f;
                UiKit.Anchor(c.DiagramBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -82f), new Vector2(w - 24f, dh));
                string key = Key(p);
                if (c.Key != key) { c.Key = key; c.Diagram.SetDiagram(DiagramFor(m, p)); }
                // detail: the focused card explains the play, the others show the coach's line or the concept
                c.Info.gameObject.SetActive(sel); c.Reason.gameObject.SetActive(true);
                float infoTop = 82f + dh + 6f;
                UiKit.Anchor(c.Info.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -infoTop), new Vector2(w - 40f, 30f));
                c.Info.text = sel ? FirstLines(PlayCallScreen.Describe(m.Book, p), 1) : "";
                string reason = m.Tab == CallTab.AskCoach ? m.ReasonFor(page[i]) : PlayCallModel.ConceptLabel(p);
                c.Reason.text = reason;
                c.Reason.textWrappingMode = TextWrappingModes.Normal; c.Reason.overflowMode = TextOverflowModes.Ellipsis; c.Reason.enableAutoSizing = true; c.Reason.fontSizeMin = 17f; c.Reason.fontSizeMax = 20f;
                UiKit.Anchor(c.Reason.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 8f), new Vector2(w - 40f, sel ? 32f : 52f));
            }
        }

        static string FirstLines(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            int idx = 0, count = 0;
            for (; idx < s.Length; idx++) { if (s[idx] == '\n') { count++; if (count >= n) break; } }
            return idx >= s.Length ? s : s.Substring(0, idx);
        }

        void Update()
        {
            // a card that just got the focus eases up to full size (UI scale only: no relayout, the diagrams keep their labels)
            float k = 1f - Mathf.Exp(-16f * Time.unscaledDeltaTime);
            for (int i = 0; i < _cards.Count; i++)
            {
                var c = _cards[i]; if (!c.Rt.gameObject.activeSelf) continue;
                float cur = c.Rt.localScale.x;
                if (cur < 0.9999f) c.Rt.localScale = Vector3.one * Mathf.Lerp(cur, 1f, k);
            }
        }

        // ---- right column: the coach's three picks stay on screen on every tab
        void RebuildRight(PlayCallModel m)
        {
            var t = GlassTheme.T;
            bool onSuggested = m.Tab == CallTab.AskCoach;
            _rightTitle.text = onSuggested ? "THE SITUATION" : "COACH SUGGESTS";
            for (int i = 0; i < _picks.Count; i++)
            {
                var pk = _picks[i];
                bool have = !onSuggested && i < m.CoachPicks.Count;
                if (onSuggested)
                {
                    // on the suggestions tab the right column carries the situation instead (the cards ARE the suggestions)
                    pk.Rt.gameObject.SetActive(true);
                    string[] lines = SituationLines(m);
                    pk.Chip.gameObject.SetActive(false);
                    pk.Num.text = "";
                    pk.Name.text = i < lines.Length ? lines[i] : "";
                    UiKit.Anchor(pk.Name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -8f), new Vector2(RightColW - 36f, 32f));
                    pk.Reason.text = i < lines.Length ? SituationDetail(m, i) : "";
                    UiKit.Anchor(pk.Reason.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -40f), new Vector2(RightColW - 36f, pk.Rt.rect.height - 44f));
                    continue;
                }
                pk.Rt.gameObject.SetActive(have);
                if (!have) continue;
                pk.Chip.gameObject.SetActive(true);
                var cp = m.CoachPicks[i]; var p = cp.Play;
                bool hot = m.Highlighted != null && m.Highlighted == p;
                pk.Num.text = (i + 1).ToString(); pk.Chip.color = hot ? t.accent : t.lightGrey;
                pk.Name.text = p.Name.ToUpperInvariant(); pk.Name.color = t.textPrimary;
                UiKit.Anchor(pk.Name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -8f), new Vector2(RightColW - 72f, 32f));
                pk.Reason.text = PlayCallModel.RefineReason(p, cp.Reason, false);
                UiKit.Anchor(pk.Reason.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -40f), new Vector2(RightColW - 72f, pk.Rt.rect.height - 44f));
            }
        }

        static string[] SituationLines(PlayCallModel m)
        {
            var s = m.Situation;
            return new[] { s.DownText, string.IsNullOrEmpty(s.SpotText) ? s.Title : s.SpotText, s.ClockText + "   " + s.DisplayScore };
        }

        static string SituationDetail(PlayCallModel m, int i)
        {
            var s = m.Situation;
            if (i == 0) return m.Side == PlaySide.Offense ? "Your play, your call." : "Read the offense and pick a coverage.";
            if (i == 1) return string.IsNullOrEmpty(s.CoachStyle) ? "" : "Coach: " + s.CoachStyle;
            return "Timeouts left: " + s.Timeouts;
        }

        // ---- legend
        void RebuildLegend(PlayCallModel m)
        {
            var items = new List<PromptItem>(12);
            items.Add(new PromptItem(new[] { "glyph_a" }, "CALL PLAY"));
            items.Add(new PromptItem(new[] { "glyph_b" }, m.SearchText.Length > 0 ? "CLEAR SEARCH" : (m.Tab == CallTab.AskCoach ? "SUGGESTED" : "SUGGESTED")));
            items.Add(new PromptItem(new[] { "glyph_x" }, "FILTER"));
            if (m.CanFlip) items.Add(new PromptItem(new[] { "glyph_y" }, m.Flip ? "FLIPPED" : "FLIP"));
            items.Add(new PromptItem(new[] { "glyph_lb", "glyph_rb" }, "PAGE"));
            items.Add(new PromptItem(new[] { "glyph_lt", "glyph_rt" }, "TABS"));
            if (m.TabHasGroups) items.Add(new PromptItem(new[] { "glyph_dpad" }, "GROUP / PLAY"));
            else items.Add(new PromptItem(new[] { "glyph_dpad" }, "MOVE"));
            PromptWrap.Fill(_legendRow, items, 34f, 22f, 26f, BlockW - 2f * Pad, TextAnchor.MiddleLeft, 1);
        }
    }
}
