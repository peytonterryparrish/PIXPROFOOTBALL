using System.Collections.Generic;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// The pre-snap overlay at the line: the play being run, the active adjustments (flip, hot routes, slide, motion, coverage shifts), the audible /
    /// coverage menu with its button glyphs, the hot-route wheel and the prompt bar. The director supplies a <see cref="PreSnapHud"/> every frame, so
    /// <see cref="Show"/> compares the content with what is on screen without allocating and rebuilds a block only when it changed (QA-785).
    /// </summary>
    public sealed class PreSnapPanel : MonoBehaviour
    {
        RectTransform _root, _adjRoot, _menuRoot, _wheelRoot, _promptRow;
        TextMeshProUGUI _playName, _mode, _menuTitle, _wheelTitle;
        Image _promptBar;
        readonly List<GameObject> _adjChips = new List<GameObject>();
        readonly List<GameObject> _menuRows = new List<GameObject>();
        readonly List<GameObject> _wheelNodes = new List<GameObject>();
        bool _gamepad = true;
        RectTransform _playBlock;
        float _inset;

        // what is on screen now (copies, compared against the incoming hud every frame)
        string _shownPlay, _shownFormation, _shownMode, _shownMenuTitle, _shownWheelTitle;
        readonly List<HintPair> _hints = new List<HintPair>();
        readonly List<string> _adj = new List<string>();
        readonly List<MenuRow> _menu = new List<MenuRow>();
        readonly List<WheelOption> _wheel = new List<WheelOption>();
        bool _hintsBuilt, _adjBuilt, _menuBuilt, _wheelBuilt;
        RectTransform _canvasRt; float _availW = -1f;

        /// <summary>Distance of the play-name block above the bottom edge: clear of the TV lower-thirds / ticker, which sit at y 118..242 (QA-787).</summary>
        public const float PlayBlockY = 290f;
        const int MaxAdjustmentChips = 6;

        /// <summary>Lifts the prompt bar and play-name block above a bottom scorebug bar (inset = its height in canvas px; 0 = none).</summary>
        public void SetBottomInset(float inset)
        {
            _inset = inset;
            if (_promptBar != null) _promptBar.rectTransform.anchoredPosition = new Vector2(0f, 30f + inset);
            if (_playBlock != null) _playBlock.anchoredPosition = new Vector2(80f, PlayBlockY + inset);
            if (_adjRoot != null) _adjRoot.anchoredPosition = new Vector2(80f, PlayBlockY + 86f + 14f + inset);
        }

        public static PreSnapPanel Create(Transform parent)
        {
            var r = UiKit.Rect(parent, "PreSnap"); UiKit.Stretch(r);
            var p = r.gameObject.AddComponent<PreSnapPanel>(); p._root = r; p.Build();
            PixFootball.UI.Glass.GlassSkinWatcher.Watch(r.gameObject);
            return p;
        }

        void Build()
        {
            // prompt bar: stretches between the 80 px margins of whatever canvas width there is; grows to a second row when the prompts do not fit (QA-786)
            _promptBar = UiKit.Panel(_root, "PromptBar", new Color(0.04f, 0.05f, 0.08f, 0.9f));
            var pb = _promptBar.rectTransform;
            pb.anchorMin = new Vector2(0, 0); pb.anchorMax = new Vector2(1, 0); pb.pivot = new Vector2(0.5f, 0); pb.anchoredPosition = new Vector2(0, 30); pb.sizeDelta = new Vector2(-160f, 72f);
            _promptRow = UiKit.Rect(pb, "Row"); UiKit.Stretch(_promptRow);
            // title block: play name + mode. It sits above the TV lower-thirds, not on them (QA-787).
            var tb = UiKit.Panel(_root, "PlayBlock", new Color(0.04f, 0.05f, 0.08f, 0.86f)); UiKit.Anchor(tb.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(80, PlayBlockY), new Vector2(560, 86)); _playBlock = tb.rectTransform;
            var edge = UiKit.Panel(tb.rectTransform, "Edge", UiColors.Gold); UiKit.Anchor(edge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(6, 0)); edge.rectTransform.anchorMax = new Vector2(0, 1); edge.rectTransform.sizeDelta = new Vector2(6, 0);
            _playName = UiKit.Label(tb.rectTransform, "", 34, Color.white, true, TextAlignmentOptions.MidlineLeft, "Play"); UiKit.Anchor(_playName.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -8), new Vector2(520, 44)); _playName.characterSpacing = 2f;
            Fit(_playName, 22f, 34f);
            _mode = UiKit.Label(tb.rectTransform, "", 24, UiColors.Gold, true, TextAlignmentOptions.MidlineLeft, "Mode"); UiKit.Anchor(_mode.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 8), new Vector2(520, 32)); _mode.characterSpacing = 2f;
            Fit(_mode, 16f, 24f);
            // adjustment chips: stacked upward from just above the play block (they used to hang under the banner area and run over the field)
            _adjRoot = UiKit.Rect(_root, "Adjustments"); UiKit.Anchor(_adjRoot, new Vector2(0, 0), new Vector2(0, 0), new Vector2(80, PlayBlockY + 86f + 14f), new Vector2(520, (MaxAdjustmentChips + 1) * 44f));
            var av = _adjRoot.gameObject.AddComponent<VerticalLayoutGroup>(); av.spacing = 6; av.childAlignment = TextAnchor.LowerLeft; av.childControlWidth = false; av.childControlHeight = false; av.childForceExpandWidth = false; av.childForceExpandHeight = false;
            // menu (right)
            _menuRoot = UiKit.Rect(_root, "Menu"); UiKit.Anchor(_menuRoot, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-80, 40), new Vector2(620, 560));
            var mbg = UiKit.Panel(_menuRoot, "Bg", new Color(0.04f, 0.05f, 0.08f, 0.92f)); UiKit.Stretch(mbg.rectTransform);
            var mEdge = UiKit.Panel(_menuRoot, "Edge", UiColors.AccentBlueHot); UiKit.Anchor(mEdge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(6, 0)); mEdge.rectTransform.anchorMax = new Vector2(0, 1); mEdge.rectTransform.sizeDelta = new Vector2(6, 0);
            _menuTitle = UiKit.Label(_menuRoot, "AUDIBLE", 40, Color.white, true, TextAlignmentOptions.MidlineLeft, "Title"); UiKit.Anchor(_menuTitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -18), new Vector2(560, 56)); _menuTitle.characterSpacing = 4f;
            // wheel (centre-left of the field)
            _wheelRoot = UiKit.Rect(_root, "Wheel"); UiKit.Anchor(_wheelRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(760, 640));
            _wheelTitle = UiKit.Label(_wheelRoot, "", 34, Color.white, true, TextAlignmentOptions.Center, "Title"); UiKit.Anchor(_wheelTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-380f, 0f), new Vector2(220, 90)); _wheelTitle.characterSpacing = 2f;
            _wheelTitle.textWrappingMode = TextWrappingModes.Normal;
            SetBottomInset(_inset);
        }

        static void Fit(TextMeshProUGUI t, float min, float max)
        {
            t.enableAutoSizing = true; t.fontSizeMin = min; t.fontSizeMax = max;
            t.textWrappingMode = TextWrappingModes.NoWrap; t.overflowMode = TextOverflowModes.Ellipsis;
        }

        // ================================================================================================ per frame
        public void Show(PreSnapHud h)
        {
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            if (!string.Equals(h.PlayName, _shownPlay) || !string.Equals(h.Formation, _shownFormation) || !string.Equals(h.Mode, _shownMode))
            {
                _shownPlay = h.PlayName; _shownFormation = h.Formation; _shownMode = h.Mode;
                _playName.text = string.IsNullOrEmpty(h.PlayName) ? "" : h.PlayName.ToUpperInvariant();
                _mode.text = ModeLine(h.Formation, h.Mode);
            }
            bool pad = MatchInput.GamepadActive;
            float avail = AvailableWidth();
            // prompt bar
            if (!_hintsBuilt || pad != _gamepad || Mathf.Abs(avail - _availW) > 0.5f || !SameHints(_hints, h.Hints))
            {
                _hintsBuilt = true; _gamepad = pad; _availW = avail;
                _hints.Clear(); _hints.AddRange(h.Hints);
                RebuildPrompts(avail);
            }
            // adjustments
            if (!_adjBuilt || !SameStrings(_adj, h.Adjustments)) { _adjBuilt = true; _adj.Clear(); _adj.AddRange(h.Adjustments); RebuildAdjustments(_adj); }
            // menu
            bool hasMenu = h.Menu.Count > 0;
            if (_menuRoot.gameObject.activeSelf != hasMenu) _menuRoot.gameObject.SetActive(hasMenu);
            if (hasMenu)
            {
                if (!_menuBuilt || pad != _menuPad || !string.Equals(h.MenuTitle, _shownMenuTitle) || !SameMenu(_menu, h.Menu))
                {
                    _menuBuilt = true; _menuPad = pad; _shownMenuTitle = h.MenuTitle; _menu.Clear(); _menu.AddRange(h.Menu);
                    RebuildMenu(h.MenuTitle, _menu);
                }
            }
            else _menuBuilt = false;
            // wheel
            bool hasWheel = h.Wheel.Count > 0;
            if (_wheelRoot.gameObject.activeSelf != hasWheel) _wheelRoot.gameObject.SetActive(hasWheel);
            if (hasWheel)
            {
                if (!_wheelBuilt || !string.Equals(h.WheelTitle, _shownWheelTitle) || !SameWheel(_wheel, h.Wheel))
                {
                    _wheelBuilt = true; _shownWheelTitle = h.WheelTitle; _wheel.Clear(); _wheel.AddRange(h.Wheel);
                    RebuildWheel(h.WheelTitle, _wheel);
                }
            }
            else _wheelBuilt = false;
        }

        bool _menuPad = true;

        public void Hide()
        {
            _root.gameObject.SetActive(false);
            _hintsBuilt = _adjBuilt = _menuBuilt = _wheelBuilt = false;
            _shownPlay = _shownFormation = _shownMode = null;
        }

        float AvailableWidth()
        {
            if (_canvasRt == null) _canvasRt = PlayCallLayout.RootCanvasRect(this);
            float w = _canvasRt != null ? _canvasRt.rect.width : PlayCallLayout.RefW;
            if (w < 100f) w = PlayCallLayout.RefW;
            return w - 2f * PlayCallLayout.Margin;
        }

        /// <summary>"SHOTGUN  -  HOT ROUTE: X": upper case, one separator (QA-63).</summary>
        public static string ModeLine(string formation, string mode)
        {
            string f = string.IsNullOrEmpty(formation) ? "" : formation.Trim().ToUpperInvariant();
            string m = string.IsNullOrEmpty(mode) ? "" : mode.Trim().ToUpperInvariant();
            if (f.Length == 0) return m;
            return m.Length == 0 ? f : f + "  -  " + m;
        }

        // ---- allocation-free content comparison (QA-785) ----
        static bool SameHints(List<HintPair> a, List<HintPair> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!string.Equals(a[i].Glyph, b[i].Glyph) || !string.Equals(a[i].Text, b[i].Text)) return false;
            return true;
        }
        static bool SameStrings(List<string> a, List<string> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (!string.Equals(a[i], b[i])) return false;
            return true;
        }
        static bool SameMenu(List<MenuRow> a, List<MenuRow> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                MenuRow x = a[i], y = b[i];
                if (x.Button != y.Button || x.Selected != y.Selected || x.Dim != y.Dim || !string.Equals(x.Label, y.Label) || !string.Equals(x.Tag, y.Tag)) return false;
            }
            return true;
        }
        static bool SameWheel(List<WheelOption> a, List<WheelOption> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++) if (a[i].Slot != b[i].Slot || a[i].Selected != b[i].Selected || !string.Equals(a[i].Label, b[i].Label)) return false;
            return true;
        }

        // ================================================================================================ rebuilds
        void RebuildPrompts(float avail)
        {
            var items = new List<PromptItem>(_hints.Count);
            foreach (HintPair p in _hints) items.Add(new PromptItem(string.IsNullOrEmpty(p.Glyph) ? new string[0] : new[] { p.Glyph }, p.Text));
            int rows = PromptWrap.Fill(_promptRow, items, 42f, 24f, 34f, avail - 40f, TextAnchor.MiddleCenter, 3);
            _promptBar.rectTransform.sizeDelta = new Vector2(-160f, rows * 42f + (rows - 1) * 6f + 30f);
        }

        void RebuildAdjustments(List<string> adj)
        {
            foreach (var g in _adjChips) { g.SetActive(false); Destroy(g); }
            _adjChips.Clear();
            int shown = adj.Count > MaxAdjustmentChips ? MaxAdjustmentChips - 1 : adj.Count;   // room for the "+N MORE" chip
            int chips = shown + (adj.Count > shown ? 1 : 0);
            for (int i = 0; i < chips; i++)
            {
                string text = i < shown ? adj[i] : "+" + (adj.Count - shown) + " MORE";
                var chip = UiKit.Panel(_adjRoot, "Adj" + i, new Color(1f, 0.784f, 0.216f, 0.95f));
                var t = UiKit.Label(chip.rectTransform, text, 24, Color.black, true, TextAlignmentOptions.MidlineLeft, "Text"); UiKit.Stretch(t.rectTransform, 14, 0, 14, 0); t.characterSpacing = 2f;
                Fit(t, 15f, 24f);
                float w = Mathf.Clamp(36f + text.Length * 15f, 90f, 440f);
                chip.rectTransform.sizeDelta = new Vector2(w, 38);
                var le = chip.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = 38;
                _adjChips.Add(chip.gameObject);
            }
        }

        void RebuildMenu(string title, List<MenuRow> menu)
        {
            foreach (var g in _menuRows) { g.SetActive(false); Destroy(g); }
            _menuRows.Clear();
            _menuTitle.text = title;
            float y = 90f;
            foreach (MenuRow r in menu)
            {
                var row = UiKit.Panel(_menuRoot, "Row", r.Selected ? UiColors.AccentBlue : (r.Dim ? new Color(0.14f, 0.15f, 0.19f, 1f) : UiColors.Row));
                UiKit.Anchor(row.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -y), new Vector2(580, 76));
                var icon = UiPrompts.ButtonIcon(row.rectTransform, UiPrompts.GlyphOf(r.Button), 52f);
                icon.GetComponent<LayoutElement>().ignoreLayout = true;
                UiKit.Anchor(icon, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), icon.sizeDelta);
                bool hasTag = !string.IsNullOrEmpty(r.Tag);
                // long coverage / play names shrink and end in "..." before they reach the tag chip (QA-789)
                float labelW = 580f - 128f - (hasTag ? 124f : 16f);
                var lbl = UiKit.Label(row.rectTransform, r.Label.ToUpperInvariant(), 28, r.Dim ? UiColors.TextMuted : Color.white, true, TextAlignmentOptions.MidlineLeft, "Label");
                UiKit.Anchor(lbl.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(128, 0), new Vector2(labelW, 60)); lbl.characterSpacing = 1.5f;
                Fit(lbl, 16f, 28f);
                if (hasTag)
                {
                    var chip = UiKit.Panel(row.rectTransform, "Tag", PlayTags.ColorOf(r.Tag, UiColors.AccentBlue)); UiKit.Anchor(chip.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(100, 38));
                    var tt = UiKit.Label(chip.rectTransform, r.Tag, 20, PlayTags.TextColorOf(r.Tag), true, TextAlignmentOptions.Center, "TagText"); UiKit.Stretch(tt.rectTransform); tt.characterSpacing = 1.5f;
                    Fit(tt, 13f, 20f);
                }
                _menuRows.Add(row.gameObject);
                y += 84f;
            }
            _menuRoot.sizeDelta = new Vector2(620, 100f + menu.Count * 84f);
        }

        void RebuildWheel(string title, List<WheelOption> wheel)
        {
            foreach (var g in _wheelNodes) { g.SetActive(false); Destroy(g); }
            _wheelNodes.Clear();
            _wheelTitle.text = title;
            // the disc and ring are drawn as a vector mesh at native resolution (the old ring sprite was a low-res bitmap; QA-60)
            var back = UiKit.Rect(_wheelRoot, "Disc");
            UiKit.Anchor(back, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-380f, 0f), new Vector2(480, 480));
            var ring = back.gameObject.AddComponent<VectorRingGraphic>(); ring.raycastTarget = false; ring.color = new Color(0.55f, 0.1f, 1f, 0.9f);
            back.SetAsFirstSibling();
            _wheelNodes.Add(back.gameObject);
            Vector2[] dirs = { new Vector2(0, 1), new Vector2(0.72f, 0.72f), new Vector2(1, 0), new Vector2(0.72f, -0.72f), new Vector2(0, -1), new Vector2(-0.72f, -0.72f), new Vector2(-1, 0), new Vector2(-0.72f, 0.72f) };
            foreach (WheelOption o in wheel)
            {
                Vector2 d = dirs[o.Slot & 7];
                var node = UiKit.Panel(_wheelRoot, "Opt", o.Selected ? UiColors.AccentBlueHot : new Color(0.16f, 0.17f, 0.23f, 0.96f));
                UiKit.Anchor(node.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(d.x * 190f - 380f, d.y * 175f), new Vector2(150, 50));
                var t = UiKit.Label(node.rectTransform, o.Label, 24, Color.white, true, TextAlignmentOptions.Center, "Text"); UiKit.Stretch(t.rectTransform); t.characterSpacing = 1.5f;
                Fit(t, 15f, 24f);
                _wheelNodes.Add(node.gameObject);
            }
        }

    }

        /// <summary>A flat disc with a ring around it, generated as geometry so it stays sharp at any canvas scale.</summary>
    public sealed class VectorRingGraphic : MaskableGraphic
        {
            const int Segments = 96;
            protected override void OnPopulateMesh(VertexHelper vh)
            {
                vh.Clear();
                Rect rect = GetPixelAdjustedRect();
                Vector2 c = rect.center;
                float ro = Mathf.Min(rect.width, rect.height) * 0.5f, ri = Mathf.Max(0f, ro - 7f);
                Color32 ring = color, fill = new Color(0.04f, 0.05f, 0.08f, 0.78f);
                // disc fan
                int centre = vh.currentVertCount;
                vh.AddVert(c, fill, Vector2.zero);
                for (int i = 0; i <= Segments; i++)
                {
                    float a = i * Mathf.PI * 2f / Segments;
                    vh.AddVert(c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ri, fill, Vector2.zero);
                }
                for (int i = 0; i < Segments; i++) vh.AddTriangle(centre, centre + 1 + i, centre + 2 + i);
                // ring band
                int band = vh.currentVertCount;
                for (int i = 0; i <= Segments; i++)
                {
                    float a = i * Mathf.PI * 2f / Segments; Vector2 d = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    vh.AddVert(c + d * ri, ring, Vector2.zero); vh.AddVert(c + d * ro, ring, Vector2.zero);
                }
                for (int i = 0; i < Segments; i++)
                {
                    int k = band + i * 2;
                    vh.AddTriangle(k, k + 1, k + 3); vh.AddTriangle(k, k + 3, k + 2);
                }
            }
        }
}
