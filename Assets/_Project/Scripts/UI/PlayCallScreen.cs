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
    /// </summary>
    public sealed class PlayCallScreen : MonoBehaviour
    {
        RectTransform _root;
        // top bar
        Image _sideChip; TextMeshProUGUI _sideText, _teamText, _situation, _scoreText, _clockLabel, _clockNum, _toText, _coachStyle;
        Image _clockBar; Image[] _toPips = new Image[3]; Image _toBtn;
        // tabs / breadcrumb / filters
        readonly List<Image> _tabBg = new List<Image>(); readonly List<TextMeshProUGUI> _tabText = new List<TextMeshProUGUI>();
        RectTransform _tabRow, _filterRow, _listRoot, _cardRoot, _previewRoot, _footer;
        TextMeshProUGUI _crumb;
        readonly List<Image> _filterBg = new List<Image>(); readonly List<TextMeshProUGUI> _filterText = new List<TextMeshProUGUI>();
        // list
        sealed class Row { public RectTransform Rt; public Image Bg, Accent; public TextMeshProUGUI Name, Sub, Count; }
        readonly List<Row> _rows = new List<Row>();
        // preview (formation / selected play)
        PlayDiagramGraphic _preview; TextMeshProUGUI _previewTitle, _previewSub, _previewBody;
        // cards
        sealed class Card { public RectTransform Rt; public Image Bg, Outline, Tag; public TextMeshProUGUI Title, Sub, Info, Reason, TagText; public PlayDiagramGraphic Diagram; public float Scale = 1f, Target = 1f; public CanvasGroup Group; public Play Play; public string Key = ""; }
        readonly List<Card> _cards = new List<Card>();
        TextMeshProUGUI _pageText;
        TextMeshProUGUI _empty;
        // footer
        Transform _footerRow;
        int _lastVersion = -1; bool _lastGamepad = true; PlayCallModel _model;
        readonly Dictionary<string, PlayDiagram> _diagrams = new Dictionary<string, PlayDiagram>();
        static readonly Color SideOff = UiColors.Hex("#E83060"), SideDef = UiColors.Hex("#30E080"), SideSpec = UiColors.Hex("#9040F0");
        const float ListX = 80f, ListW = 520f, CardsX = 650f, CardW = 372f, CardGap = 28f;

        public static PlayCallScreen Create(Transform parent)
        {
            var r = UiKit.Rect(parent, "PlayCallScreen"); UiKit.Stretch(r);
            var s = r.gameObject.AddComponent<PlayCallScreen>();
            s._root = r; s.Build();
            return s;
        }

        // ================================================================================================ build
        void Build()
        {
            var scrim = UiKit.Panel(_root, "Scrim", new Color(0.02f, 0.03f, 0.06f, 0.94f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var diamond = UiKit.Panel(_root, "Diamond", new Color(1f, 1f, 1f, 0.05f), UiKit.Sprite("bg_diamond")); UiKit.Stretch(diamond.rectTransform);
            diamond.enabled = diamond.sprite != null;
            BuildTopBar();
            _tabRow = UiKit.Rect(_root, "Tabs"); UiKit.Anchor(_tabRow, new Vector2(0, 1), new Vector2(0, 1), new Vector2(ListX, -128), new Vector2(1760, 56));
            _crumb = UiKit.Label(_root, "FORMATION", 30, UiColors.Gold, true, TextAlignmentOptions.MidlineLeft, "Crumb");
            UiKit.Anchor(_crumb.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(ListX, -200), new Vector2(760, 50)); _crumb.characterSpacing = 3f;
            _filterRow = UiKit.Rect(_root, "Filters"); UiKit.Anchor(_filterRow, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-80, -200), new Vector2(1000, 50));
            _listRoot = UiKit.Rect(_root, "List"); UiKit.Anchor(_listRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(ListX, -262), new Vector2(ListW, 690));
            _previewRoot = UiKit.Rect(_root, "Preview"); UiKit.Anchor(_previewRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(CardsX, -262), new Vector2(1190, 690));
            _cardRoot = UiKit.Rect(_root, "Cards"); UiKit.Anchor(_cardRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(CardsX, -262), new Vector2(1190, 690));
            BuildPreview();
            for (int i = 0; i < PlayCallModel.PageSize; i++) _cards.Add(BuildCard(i));
            _pageText = UiKit.Label(_root, "", 26, UiColors.TextSecondary, true, TextAlignmentOptions.MidlineRight, "Page");
            UiKit.Anchor(_pageText.rectTransform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-80, 112), new Vector2(520, 40));
            _empty = UiKit.Label(_cardRoot, "NO PLAYS IN THIS VIEW", 40, UiColors.TextMuted, true, TextAlignmentOptions.Center, "Empty"); UiKit.Stretch(_empty.rectTransform);
            var foot = UiKit.Panel(_root, "Footer", UiColors.BgPanelDark); UiKit.Anchor(foot.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 84)); foot.rectTransform.anchorMax = new Vector2(1, 0); foot.rectTransform.sizeDelta = new Vector2(0, 84);
            _footer = foot.rectTransform;
        }

        void BuildTopBar()
        {
            var bar = UiKit.Panel(_root, "TopBar", UiColors.BgNav); UiKit.Anchor(bar.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(0, 112));
            bar.rectTransform.anchorMax = new Vector2(1, 1); bar.rectTransform.sizeDelta = new Vector2(0, 112);
            _sideChip = UiKit.Panel(bar.rectTransform, "SideChip", SideOff); UiKit.Anchor(_sideChip.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(ListX, 0), new Vector2(250, 62));
            _sideText = UiKit.Label(_sideChip.rectTransform, "OFFENSE", 34, Color.white, true, TextAlignmentOptions.Center, "Side"); UiKit.Stretch(_sideText.rectTransform); _sideText.characterSpacing = 3f;
            _teamText = UiKit.Label(bar.rectTransform, "TEAM", 40, Color.white, true, TextAlignmentOptions.MidlineLeft, "Team");
            UiKit.Anchor(_teamText.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(ListX + 276, 18), new Vector2(640, 50)); _teamText.characterSpacing = 3f;
            _situation = UiKit.Label(bar.rectTransform, "1ST & 10", 30, UiColors.Gold, true, TextAlignmentOptions.MidlineLeft, "Situation");
            UiKit.Anchor(_situation.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(ListX + 276, -22), new Vector2(760, 40)); _situation.characterSpacing = 1f; _situation.fontSize = 28;
            _scoreText = UiKit.Label(bar.rectTransform, "", 32, Color.white, true, TextAlignmentOptions.MidlineLeft, "Score");
            UiKit.Anchor(_scoreText.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(1130, 16), new Vector2(300, 44));
            _coachStyle = UiKit.Label(bar.rectTransform, "", 24, UiColors.TextSecondary, true, TextAlignmentOptions.MidlineLeft, "Coach");
            UiKit.Anchor(_coachStyle.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(1130, -22), new Vector2(300, 36)); _coachStyle.characterSpacing = 1f; _coachStyle.fontSize = 22;
            // timeout button (the kit's "Call timeout" look)
            _toBtn = UiKit.Panel(bar.rectTransform, "Timeout", Color.black); UiKit.Anchor(_toBtn.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-250, 6), new Vector2(300, 62));
            var toEdge = UiKit.Panel(_toBtn.rectTransform, "Edge", new Color(1f, 1f, 1f, 0.9f)); UiKit.Anchor(toEdge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 3)); toEdge.rectTransform.anchorMax = new Vector2(1, 0); toEdge.rectTransform.sizeDelta = new Vector2(0, 3);
            _toText = UiKit.Label(_toBtn.rectTransform, "CALL TIMEOUT", 30, Color.white, true, TextAlignmentOptions.MidlineLeft, "Text"); UiKit.Stretch(_toText.rectTransform, 84, 0, 8, 0); _toText.characterSpacing = 2f;
            for (int i = 0; i < 3; i++)
            {
                var p = UiKit.Panel(bar.rectTransform, "Pip" + i, UiColors.AccentBlueHot);
                UiKit.Anchor(p.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-250 - 300 + 6 + i * 100 + 0, -34), new Vector2(90, 6));
                _toPips[i] = p;
            }
            // play clock
            var clockBg = UiKit.Panel(bar.rectTransform, "ClockBg", Color.black); UiKit.Anchor(clockBg.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-80, 0), new Vector2(150, 92));
            _clockNum = UiKit.Label(clockBg.rectTransform, "25", 70, UiColors.Gold, true, TextAlignmentOptions.Center, "Num"); UiKit.Anchor(_clockNum.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(150, 72));
            _clockLabel = UiKit.Label(clockBg.rectTransform, "PLAY CLOCK", 18, Color.white, true, TextAlignmentOptions.Center, "Lbl"); UiKit.Anchor(_clockLabel.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(150, 24));
            _clockBar = UiKit.Panel(clockBg.rectTransform, "Bar", UiColors.Gold); UiKit.Anchor(_clockBar.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(150, 5));
        }

        void BuildPreview()
        {
            var bg = UiKit.Panel(_previewRoot, "Bg", new Color(0.05f, 0.06f, 0.09f, 0.85f)); UiKit.Stretch(bg.rectTransform);
            var gd = UiKit.Panel(_previewRoot, "DiagramBg", Color.black); UiKit.Anchor(gd.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -24), new Vector2(642, 560));
            gd.gameObject.AddComponent<RectMask2D>();
            var gr = UiKit.Rect(gd.rectTransform, "Diagram"); UiKit.Stretch(gr, 3, 3, 3, 3);
            _preview = gr.gameObject.AddComponent<PlayDiagramGraphic>();
            _previewTitle = UiKit.Label(_previewRoot, "", 56, Color.white, true, TextAlignmentOptions.TopLeft, "Title");
            UiKit.Anchor(_previewTitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(700, -34), new Vector2(470, 160)); _previewTitle.characterSpacing = 2f;
            _previewSub = UiKit.Label(_previewRoot, "", 30, UiColors.Gold, true, TextAlignmentOptions.TopLeft, "Sub");
            UiKit.Anchor(_previewSub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(700, -200), new Vector2(470, 44)); _previewSub.characterSpacing = 2f;
            _previewBody = UiKit.Label(_previewRoot, "", 28, UiColors.TextSecondary, false, TextAlignmentOptions.TopLeft, "Body");
            UiKit.Anchor(_previewBody.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(700, -258), new Vector2(470, 400));
            _previewBody.textWrappingMode = TextWrappingModes.Normal; _previewBody.lineSpacing = 12f;
        }

        Card BuildCard(int i)
        {
            var c = new Card();
            c.Rt = UiKit.Rect(_cardRoot, "Card" + i); UiKit.Anchor(c.Rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(i * (CardW + CardGap), 0), new Vector2(CardW, 690));
            c.Group = c.Rt.gameObject.AddComponent<CanvasGroup>();
            c.Outline = UiKit.Panel(c.Rt, "Outline", UiColors.Gold); UiKit.Stretch(c.Outline.rectTransform, -5, -5, -5, -5);
            c.Bg = UiKit.Panel(c.Rt, "Bg", new Color(0.10f, 0.11f, 0.15f, 1f)); UiKit.Stretch(c.Bg.rectTransform);
            c.Title = UiKit.Label(c.Rt, "PLAY NAME", 38, Color.white, true, TextAlignmentOptions.TopLeft, "Title");
            UiKit.Anchor(c.Title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -14), new Vector2(CardW - 36, 50)); c.Title.characterSpacing = 1.5f;
            c.Sub = UiKit.Label(c.Rt, "FORMATION", 22, UiColors.Gold, true, TextAlignmentOptions.TopLeft, "Sub");
            UiKit.Anchor(c.Sub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -62), new Vector2(CardW - 36, 30)); c.Sub.characterSpacing = 2f;
            var dBg = UiKit.Panel(c.Rt, "DiagramBg", Color.black); UiKit.Anchor(dBg.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(10, -100), new Vector2(CardW - 20, 300));
            dBg.gameObject.AddComponent<RectMask2D>();
            var dr = UiKit.Rect(dBg.rectTransform, "Diagram"); UiKit.Stretch(dr, 2, 2, 2, 2);
            c.Diagram = dr.gameObject.AddComponent<PlayDiagramGraphic>();
            c.Tag = UiKit.Panel(c.Rt, "Tag", UiColors.AccentBlue); UiKit.Anchor(c.Tag.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -100 - 300 - 16), new Vector2(124, 40));
            c.TagText = UiKit.Label(c.Tag.rectTransform, "PASS", 22, Color.white, true, TextAlignmentOptions.Center, "TagText"); UiKit.Stretch(c.TagText.rectTransform); c.TagText.characterSpacing = 2f;
            c.Info = UiKit.Label(c.Rt, "", 23, UiColors.TextSecondary, false, TextAlignmentOptions.TopLeft, "Info");
            UiKit.Anchor(c.Info.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -100 - 300 - 66), new Vector2(CardW - 36, 190));
            c.Info.textWrappingMode = TextWrappingModes.Normal; c.Info.lineSpacing = 6f;
            c.Reason = UiKit.Label(c.Rt, "", 22, UiColors.Gold, true, TextAlignmentOptions.BottomLeft, "Reason");
            UiKit.Anchor(c.Reason.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 12), new Vector2(CardW - 36, 56));
            c.Reason.textWrappingMode = TextWrappingModes.Normal; c.Reason.characterSpacing = 1.2f;
            return c;
        }

        // ================================================================================================ rendering
        public void Show(PlayCallModel m, float playClock)
        {
            _model = m;
            if (!_root.gameObject.activeSelf) { _root.gameObject.SetActive(true); _lastVersion = -1; }
            bool pad = MatchInput.GamepadActive;
            if (m.Version != _lastVersion || pad != _lastGamepad) { _lastVersion = m.Version; _lastGamepad = pad; Rebuild(m); }
            UpdateClock(m, playClock);
        }

        public void Hide() { _root.gameObject.SetActive(false); _model = null; _lastVersion = -1; }

        void UpdateClock(PlayCallModel m, float clock)
        {
            int sec = Mathf.Max(0, Mathf.CeilToInt(clock));
            _clockNum.text = sec.ToString();
            bool low = sec <= 5;
            _clockNum.color = low ? UiColors.Danger : UiColors.Gold;
            float total = Mathf.Max(1f, m.Situation.PlayClockSeconds);
            _clockBar.rectTransform.sizeDelta = new Vector2(150f * Mathf.Clamp01(clock / 40f), 5f);
            _clockBar.color = low ? UiColors.Danger : UiColors.Gold;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            foreach (var c in _cards)
            {
                if (!c.Rt.gameObject.activeSelf) continue;
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
            _sideText.color = m.Side == PlaySide.Defense ? Color.black : Color.white;
            _teamText.text = s.TeamName + (m.Side == PlaySide.Offense && s.Title != "OFFENSE" ? "   " + s.Title : "");
            _situation.text = s.DownText + (string.IsNullOrEmpty(s.SpotText) ? "" : "    " + s.SpotText) + "    " + s.ClockText;
            _scoreText.text = s.ScoreText.Replace("  -  ", " - ");
            _coachStyle.text = string.IsNullOrEmpty(s.CoachStyle) ? "" : "COACH: " + s.CoachStyle;
            for (int i = 0; i < 3; i++) _toPips[i].color = i < s.Timeouts ? UiColors.AccentBlueHot : new Color(1f, 1f, 1f, 0.15f);
            _toText.text = m.TimeoutBlocked ? "NO TIMEOUTS LEFT" : "CALL TIMEOUT";
            _toText.color = m.TimeoutBlocked ? UiColors.Danger : Color.white;
            RebuildTabs(m);
            RebuildFilters(m);
            _crumb.text = m.Breadcrumb;
            bool playStage = m.Stage == CallStage.Play;
            _listRoot.gameObject.SetActive(!playStage || m.Tab == CallTab.Playbook);
            _previewRoot.gameObject.SetActive(!playStage);
            _cardRoot.gameObject.SetActive(playStage);
            if (_listRoot.gameObject.activeSelf) RebuildList(m);
            if (!playStage) RebuildPreview(m); else RebuildCards(m);
            RebuildFooter(m);
        }

        void RebuildTabs(PlayCallModel m)
        {
            while (_tabBg.Count < m.Tabs.Count)
            {
                var bg = UiKit.Panel(_tabRow, "Tab" + _tabBg.Count, Color.white); var t = UiKit.Label(bg.rectTransform, "", 28, Color.black, true, TextAlignmentOptions.Center, "Text");
                UiKit.Stretch(t.rectTransform); t.characterSpacing = 2f; _tabBg.Add(bg); _tabText.Add(t);
            }
            float x = 0f;
            for (int i = 0; i < _tabBg.Count; i++)
            {
                bool on = i < m.Tabs.Count;
                _tabBg[i].gameObject.SetActive(on);
                if (!on) continue;
                CallTab tab = m.Tabs[i];
                string name = PlayCallModel.TabName(tab);
                float w = 40f + name.Length * 17f;
                UiKit.Anchor(_tabBg[i].rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(w, 52));
                bool sel = tab == m.Tab;
                _tabBg[i].color = sel ? Color.white : new Color(1f, 1f, 1f, 0.08f);
                _tabText[i].text = name; _tabText[i].color = sel ? Color.black : UiColors.TextSecondary;
                x += w + 14f;
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
            float x = 0f;
            for (int k = _filterBg.Count - 1; k >= 0; k--) { }
            // right aligned: lay out from the right edge
            float total = 0f;
            for (int i = 0; i < fs.Length; i++) total += 28f + PlayCallModel.FilterName(fs[i]).Length * 14f + 10f;
            x = 1000f - total;
            for (int i = 0; i < _filterBg.Count; i++)
            {
                bool on = i < fs.Length;
                _filterBg[i].gameObject.SetActive(on);
                if (!on) continue;
                string name = PlayCallModel.FilterName(fs[i]);
                float w = 28f + name.Length * 14f;
                UiKit.Anchor(_filterBg[i].rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(w, 44));
                bool sel = fs[i] == m.Filter;
                _filterBg[i].color = sel ? UiColors.AccentBlueHot : new Color(1f, 1f, 1f, 0.08f);
                _filterText[i].text = name; _filterText[i].color = sel ? Color.white : UiColors.TextSecondary;
                x += w + 10f;
            }
        }

        // ---- list ----
        Row MakeRow(int i)
        {
            var r = new Row();
            r.Rt = UiKit.Rect(_listRoot, "Row" + i);
            r.Bg = r.Rt.gameObject.AddComponent<Image>(); r.Bg.color = UiColors.Row; r.Bg.raycastTarget = false;
            r.Accent = UiKit.Panel(r.Rt, "Accent", UiColors.Gold); UiKit.Anchor(r.Accent.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(8, 74));
            r.Name = UiKit.Label(r.Rt, "", 36, Color.white, true, TextAlignmentOptions.MidlineLeft, "Name"); UiKit.Stretch(r.Name.rectTransform, 28, 14, 120, 0); r.Name.characterSpacing = 2f;
            r.Sub = UiKit.Label(r.Rt, "", 22, UiColors.TextSecondary, false, TextAlignmentOptions.BottomLeft, "Sub"); UiKit.Stretch(r.Sub.rectTransform, 28, 4, 120, 0);
            r.Count = UiKit.Label(r.Rt, "", 28, UiColors.Gold, true, TextAlignmentOptions.MidlineRight, "Count"); UiKit.Anchor(r.Count.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-16, 0), new Vector2(110, 60));
            return r;
        }

        void RebuildList(PlayCallModel m)
        {
            var items = new List<string[]>(); int selected = 0;
            string header = "";
            if (m.Stage == CallStage.Formation || (m.Stage == CallStage.Play && m.Tab == CallTab.Playbook && false))
            {
                header = "FORMATIONS";
                for (int i = 0; i < m.Formations.Count; i++) items.Add(new[] { m.Formations[i].Label, m.Formations[i].Detail, m.Formations[i].PlayCount.ToString() });
                selected = m.FormationIndex;
            }
            else if (m.Stage == CallStage.Set)
            {
                header = m.CurrentFormation != null ? m.CurrentFormation.Label + " SETS" : "SETS";
                var f = m.CurrentFormation;
                if (f != null) for (int i = 0; i < f.Sets.Count; i++) items.Add(new[] { f.Sets[i].Label, f.Sets[i].Detail, f.Sets[i].Plays.Count.ToString() });
                selected = m.SetIndex;
            }
            else
            {
                // play stage in the Playbook tab: keep the formation / set context visible
                header = "";
                var f = m.CurrentFormation;
                if (f != null) { items.Add(new[] { f.Label, f.Detail, f.PlayCount.ToString() }); selected = 0; if (f.Sets.Count > 1 && m.CurrentSet != null) { items.Add(new[] { m.CurrentSet.Label, m.CurrentSet.Detail, m.CurrentSet.Plays.Count.ToString() }); selected = 1; } }
            }
            const float rowH = 82f, gap = 8f, maxRows = 7;
            int first = 0;
            if (items.Count > maxRows) first = Mathf.Clamp(selected - 3, 0, items.Count - (int)maxRows);
            float y = 0f;
            for (int i = 0; i < _rows.Count; i++) _rows[i].Rt.gameObject.SetActive(false);
            int n = 0;
            for (int i = first; i < items.Count && n < maxRows; i++, n++)
            {
                while (_rows.Count <= n) _rows.Add(MakeRow(_rows.Count));
                var r = _rows[n]; r.Rt.gameObject.SetActive(true);
                UiKit.Anchor(r.Rt, new Vector2(0, 1), new Vector2(0, 1), new Vector2(0, -y), new Vector2(ListW, rowH));
                bool sel = i == selected && (m.Stage != CallStage.Play);
                bool ctx = m.Stage == CallStage.Play;
                r.Bg.color = sel ? UiColors.AccentBlue : (ctx ? new Color(0.16f, 0.17f, 0.23f, 1f) : UiColors.Row);
                r.Accent.gameObject.SetActive(sel);
                r.Name.text = items[i][0]; r.Sub.text = items[i][1]; r.Count.text = items[i][2] + (items[i][2].Length > 0 ? "" : "");
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
            if (m.Stage == CallStage.Formation && m.CurrentFormation != null) { f = m.CurrentFormation.Formation; title = m.CurrentFormation.Label; sub = m.CurrentFormation.Detail; body = m.CurrentFormation.PlayCount + " PLAYS IN " + m.CurrentFormation.Sets.Count + (m.CurrentFormation.Sets.Count == 1 ? " SET" : " SETS") + "\n\nA  CHOOSE FORMATION"; }
            else if (m.Stage == CallStage.Set && m.CurrentSet != null) { f = m.CurrentSet.Formation; title = m.CurrentSet.Label; sub = m.CurrentFormation.Label; body = m.CurrentSet.Plays.Count + " PLAYS\n" + m.CurrentSet.Detail + "\n\nA  CHOOSE SET"; }
            if (f == null) { _preview.SetDiagram(null); return; }
            _previewTitle.text = title; _previewSub.text = sub; _previewBody.text = body;
            _preview.SetDiagram(PlayDiagram.ForFormation(f));
        }

        // ---- cards ----
        static string Key(Play p, bool mirrored) { return p.Id + (mirrored ? "|m" : "") + (p.Mirrored ? "|M" : ""); }

        PlayDiagram DiagramFor(PlayCallModel m, Play p)
        {
            string k = Key(p, false);
            PlayDiagram d;
            if (!_diagrams.TryGetValue(k, out d)) { d = PlayDiagram.For(m.Book, p); _diagrams[k] = d; }
            return d;
        }

        static string TagFor(Play p)
        {
            switch (p.Category)
            {
                case PlayCategory.InsideRun: case PlayCategory.OutsideRun: case PlayCategory.Draw: case PlayCategory.QbRun: return "RUN";
                case PlayCategory.PlayAction: return "RPO";
                case PlayCategory.Screen: return "PASS";
                case PlayCategory.ShortPass: case PlayCategory.MediumPass: case PlayCategory.DeepPass: return "PASS";
                case PlayCategory.ZoneCoverage: case PlayCategory.Prevent: return "ZONE";
                case PlayCategory.ManCoverage: return "MAN";
                case PlayCategory.Blitz: case PlayCategory.GoalLine: return "BLITZ";
                case PlayCategory.Punt: return "PUNT";
                case PlayCategory.FieldGoal: case PlayCategory.PointAfter: case PlayCategory.FieldGoalBlock: return "FG";
                default: return "KICK";
            }
        }

        static Color TagColor(string t)
        {
            switch (t)
            {
                case "RUN": return UiColors.Hex("#FA7805"); case "PASS": return UiColors.Hex("#054EED"); case "RPO": return UiColors.Hex("#05ACED");
                case "ZONE": return UiColors.Hex("#00287D"); case "MAN": return UiColors.Hex("#FFEA00"); case "BLITZ": return UiColors.Hex("#C20000");
                case "PUNT": return UiColors.Hex("#B845FF"); case "FG": return UiColors.Hex("#11F055"); default: return UiColors.Hex("#3A3A3A");
            }
        }

        static string CategoryText(Play p)
        {
            switch (p.Category)
            {
                case PlayCategory.InsideRun: return "INSIDE RUN"; case PlayCategory.OutsideRun: return "OUTSIDE RUN"; case PlayCategory.Draw: return "DRAW"; case PlayCategory.QbRun: return "QB RUN";
                case PlayCategory.ShortPass: return "QUICK PASS"; case PlayCategory.MediumPass: return "INTERMEDIATE"; case PlayCategory.DeepPass: return "DEEP PASS"; case PlayCategory.PlayAction: return "PLAY ACTION";
                case PlayCategory.Screen: return "SCREEN"; case PlayCategory.ZoneCoverage: return "ZONE"; case PlayCategory.ManCoverage: return "MAN"; case PlayCategory.Blitz: return "BLITZ";
                case PlayCategory.GoalLine: return "GOAL LINE"; case PlayCategory.Prevent: return "PREVENT"; case PlayCategory.Kickoff: return "KICKOFF"; case PlayCategory.Onside: return "ONSIDE KICK";
                case PlayCategory.Punt: return "PUNT"; case PlayCategory.FieldGoal: return "FIELD GOAL"; case PlayCategory.PointAfter: return "EXTRA POINT"; case PlayCategory.KickReturn: return "KICK RETURN";
                case PlayCategory.PuntReturn: return "PUNT RETURN"; case PlayCategory.FieldGoalBlock: return "KICK BLOCK"; default: return p.Category.ToString().ToUpperInvariant();
            }
        }

        static string RouteShort(RouteKind k)
        {
            switch (k) { case RouteKind.Go: return "STREAK"; case RouteKind.OutAndUp: return "OUT & UP"; case RouteKind.ScreenRelease: return "SCREEN"; case RouteKind.Corner2: return "CORNER"; default: return k.ToString().ToUpperInvariant(); }
        }

        static string Spaced(string s)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length; i++) { if (i > 0 && char.IsUpper(s[i]) && !char.IsUpper(s[i - 1])) sb.Append(' '); sb.Append(char.ToUpperInvariant(s[i])); }
            return sb.ToString();
        }

        static string GapText(RunGap g)
        {
            if (g == RunGap.None) return "";
            string n = g.ToString();
            return n.Substring(0, 1) + " GAP " + n.Substring(1).ToUpperInvariant();
        }

        /// <summary>Football detail for a card: the run concept, blocking scheme and gap; the quarterback action and routes by receiver; or the coverage, rush and drop.</summary>
        public static string Describe(Playbook book, Play p)
        {
            var sb = new StringBuilder();
            Formation f = book.FindFormation(p.FormationName);
            if (p.Side == PlaySide.Offense)
            {
                var qb = p.Assignments[0] as QbAssignment;
                if (p.IsRun)
                {
                    foreach (Assignment a in p.Assignments)
                    {
                        var c = a as CarryAssignment; if (c == null || c.IsFake) continue;
                        string who = f != null ? DiagramLabel(f, a.Slot) : "";
                        sb.Append(who + " " + Spaced(c.Style.ToString()));
                        string gap = GapText(c.Gap); if (gap.Length > 0) sb.Append("\n" + gap);
                        break;
                    }
                    if (sb.Length == 0 && qb != null) sb.Append("QB " + Spaced(qb.Action.ToString()));
                    bool pull = false, zone = false;
                    for (int i = 2; i <= 6; i++) { var b = p.Assignments[i] as BlockAssignment; if (b == null) continue; if (b.Block == BlockKind.Pull) pull = true; if (b.Block == BlockKind.Zone) zone = true; }
                    sb.Append("\n" + (pull ? "PULLING LINEMAN" : (zone ? "ZONE BLOCKING" : "MAN BLOCKING")));
                    foreach (Assignment a in p.Assignments) { var b = a as BlockAssignment; if (b != null && (b.Block == BlockKind.Lead || b.Block == BlockKind.Kickout) && f != null) { sb.Append("\n" + DiagramLabel(f, a.Slot) + " LEADS THE WAY"); break; } }
                }
                else
                {
                    if (qb != null)
                    {
                        string drop = qb.Action == QbAction.Rollout ? "ROLLOUT" : (qb.Action == QbAction.Boot ? "BOOTLEG" : (qb.Action == QbAction.PlayActionFake ? "PLAY ACTION" : (qb.Action == QbAction.ScreenPass ? "SCREEN" : (qb.DropbackSteps <= 3 ? "QUICK DROP" : qb.DropbackSteps + "-STEP DROP"))));
                        sb.Append(drop);
                        if (qb.ReadProgression.Count > 0 && f != null) sb.Append("   READ 1: " + DiagramLabel(f, qb.ReadProgression[0]));
                    }
                    int n = 0;
                    for (int i = 1; i < p.Assignments.Length && f != null; i++)
                    {
                        var r = p.Assignments[i] as RouteAssignment; if (r == null) continue;
                        sb.Append(n % 2 == 0 ? "\n" : "   ");
                        sb.Append(DiagramLabel(f, i) + " " + RouteShort(r.Route)); n++;
                        if (n >= 6) break;
                    }
                }
            }
            else if (p.Side == PlaySide.Defense)
            {
                sb.Append(DefenseAdjust.CoverageName(p.Scheme));
                int rush = p.PassRushers;
                sb.Append("   " + rush + " RUSH / " + (11 - rush) + " DROP");
                bool press = false, man = false;
                foreach (Assignment a in p.Assignments) { var cv = a as CoverageAssignment; if (cv != null) { press |= cv.Press; man |= cv.IsMan; } }
                if (man) sb.Append("\nMAN COVERAGE" + (press ? " - PRESS" : ""));
                else sb.Append("\nZONE COVERAGE" + (press ? " - PRESS" : ""));
                foreach (Assignment a in p.Assignments) if (a is SpyAssignment) { sb.Append("\nQB SPY"); break; }
                foreach (Assignment a in p.Assignments) { var bz = a as BlitzAssignment; if (bz != null && f != null) { sb.Append("\nBLITZ: " + PlayDiagram.LabelFor(f.Slots[a.Slot]) + " " + GapText(bz.Gap)); break; } }
            }
            else sb.Append(CategoryText(p));
            return sb.ToString();
        }
        static string DiagramLabel(Formation f, int slot)
        {
            SlotSpec s = f.Slots[slot];
            return string.IsNullOrEmpty(s.Label) ? PlayDiagram.LabelFor(s) : s.Label;
        }

        void RebuildCards(PlayCallModel m)
        {
            var page = m.PagePlays();
            _empty.gameObject.SetActive(page.Count == 0);
            _pageText.text = page.Count == 0 ? "" : "PAGE " + (m.Page + 1) + " / " + m.PageCount + "     " + (m.PlayIndex + 1) + " OF " + m.Plays.Count;
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
                c.Sub.text = (f != null ? f.Name.ToUpperInvariant() : "") + (m.Tab != CallTab.Playbook || (m.CurrentSet != null && m.CurrentFormation != null && m.CurrentFormation.Sets.Count > 1) ? "" : "");
                string tag = TagFor(p);
                c.Tag.color = TagColor(tag);
                var sp = UiKit.Sprite("tag_" + tag.ToLowerInvariant());
                c.Tag.sprite = sp; c.TagText.text = sp != null ? "" : tag; c.TagText.color = tag == "MAN" || tag == "FG" ? Color.black : Color.white;
                if (sp != null) c.Tag.color = Color.white;
                c.Info.text = Describe(m.Book, p);
                string reason = m.ReasonFor(page[i]);
                c.Reason.text = reason;
                string key = Key(p, false);
                if (c.Key != key) { c.Key = key; c.Diagram.SetDiagram(DiagramFor(m, p)); }
            }
            // the left rail keeps the selected play's alignment and notes
            var hl = m.Displayed(m.Highlighted);
            if (hl != null && _listRoot.gameObject.activeSelf && _rows.Count > 0) { }
        }

        // ---- footer ----
        void RebuildFooter(PlayCallModel m)
        {
            if (_footerRow != null) Destroy(_footerRow.gameObject);
            var row = UiKit.Rect(_footer, "Prompts"); UiKit.Anchor(row, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(80, 0), new Vector2(1760, 60));
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 44; h.childAlignment = TextAnchor.MiddleLeft; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false;
            _footerRow = row;
            Prompt(row, "glyph_a", m.Stage == CallStage.Play ? "CALL PLAY" : "SELECT");
            if (m.Stage != CallStage.Formation || m.Tab != CallTab.Playbook) Prompt(row, "glyph_b", m.Tab != CallTab.Playbook ? "PLAYBOOK" : "BACK");
            Prompt(row, "glyph_dpad_left", m.Stage == CallStage.Play ? "MOVE" : "CHOOSE");
            if (m.Stage == CallStage.Play) { Prompt(row, "glyph_lb", "PAGE"); Prompt(row, "glyph_rb", "PAGE"); Prompt(row, "glyph_x", "FILTER"); }
            if (m.CanFlip) Prompt(row, "glyph_y", m.Flip ? "FLIPPED" : "FLIP PLAY");
            Prompt(row, "glyph_rt", "TABS");
            Prompt(row, "glyph_lt", "TIMEOUT");
        }

        void Prompt(Transform row, string glyph, string label)
        {
            if (!MatchInput.GamepadActive) { glyph = "key:" + glyph; }
            if (glyph.StartsWith("key:")) KeyPrompt(row, glyph.Substring(4), label); else UiKit.FooterPrompt(row, glyph, label, 44f);
        }

        static string KeyOf(string glyph)
        {
            switch (glyph)
            {
                case "glyph_a": return "SPACE"; case "glyph_b": return "F"; case "glyph_x": return "C"; case "glyph_y": return "R"; case "glyph_lb": return "Z"; case "glyph_rb": return "X";
                case "glyph_dpad_left": return "ARROWS"; case "glyph_rt": return "SHIFT"; case "glyph_lt": return "V"; default: return "?";
            }
        }

        void KeyPrompt(Transform row, string glyph, string label)
        {
            var r = UiKit.Rect(row, "Prompt_" + label);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 10; h.childAlignment = TextAnchor.MiddleLeft; h.childControlHeight = true; h.childControlWidth = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            var key = UiKit.Panel(r, "Key", new Color(0.92f, 0.92f, 0.95f)); var le = key.gameObject.AddComponent<LayoutElement>(); string kt = KeyOf(glyph); le.preferredWidth = 24 + kt.Length * 15; le.preferredHeight = 40;
            var kl = UiKit.Label(key.rectTransform, kt, 22, new Color(0.08f, 0.09f, 0.12f), true, TextAlignmentOptions.Center, "KeyText"); UiKit.Stretch(kl.rectTransform);
            var t = UiKit.Label(r, label, 26, UiColors.TextSecondary, false, TextAlignmentOptions.MidlineLeft);
            var le2 = t.gameObject.AddComponent<LayoutElement>(); le2.preferredHeight = 44; var fit = t.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            var rf = r.gameObject.AddComponent<ContentSizeFitter>(); rf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; rf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        }
    }
}

