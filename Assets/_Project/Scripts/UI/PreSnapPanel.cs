using System.Collections.Generic;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// The pre-snap overlay at the line: the play being run, the active adjustments (flip, hot routes, slide, motion, coverage shifts), the audible /
    /// coverage menu with its button glyphs, the hot-route wheel and the prompt bar. The director supplies a <see cref="PreSnapHud"/> every frame.
    /// </summary>
    public sealed class PreSnapPanel : MonoBehaviour
    {
        RectTransform _root, _adjRoot, _menuRoot, _wheelRoot, _promptRow;
        TextMeshProUGUI _playName, _mode, _menuTitle, _wheelTitle;
        Image _promptBar;
        readonly List<GameObject> _adjChips = new List<GameObject>();
        readonly List<GameObject> _menuRows = new List<GameObject>();
        readonly List<GameObject> _wheelNodes = new List<GameObject>();
        string _hintSig = "", _adjSig = "", _menuSig = "", _wheelSig = "";
        bool _gamepad = true;

        public static PreSnapPanel Create(Transform parent)
        {
            var r = UiKit.Rect(parent, "PreSnap"); UiKit.Stretch(r);
            var p = r.gameObject.AddComponent<PreSnapPanel>(); p._root = r; p.Build();
            return p;
        }

        void Build()
        {
            _promptBar = UiKit.Panel(_root, "PromptBar", new Color(0.04f, 0.05f, 0.08f, 0.9f));
            UiKit.Anchor(_promptBar.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1760, 72));
            _promptRow = UiKit.Rect(_promptBar.rectTransform, "Row"); UiKit.Stretch(_promptRow);
            // title block: play name + mode
            var tb = UiKit.Panel(_root, "PlayBlock", new Color(0.04f, 0.05f, 0.08f, 0.86f)); UiKit.Anchor(tb.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(80, 118), new Vector2(560, 86));
            var edge = UiKit.Panel(tb.rectTransform, "Edge", UiColors.Gold); UiKit.Anchor(edge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(6, 0)); edge.rectTransform.anchorMax = new Vector2(0, 1); edge.rectTransform.sizeDelta = new Vector2(6, 0);
            _playName = UiKit.Label(tb.rectTransform, "", 34, Color.white, true, TextAlignmentOptions.MidlineLeft, "Play"); UiKit.Anchor(_playName.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -8), new Vector2(520, 44)); _playName.characterSpacing = 2f;
            _mode = UiKit.Label(tb.rectTransform, "", 24, UiColors.Gold, true, TextAlignmentOptions.MidlineLeft, "Mode"); UiKit.Anchor(_mode.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(24, 8), new Vector2(520, 32)); _mode.characterSpacing = 2f;
            _adjRoot = UiKit.Rect(_root, "Adjustments"); UiKit.Anchor(_adjRoot, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -246), new Vector2(900, 120));
            var av = _adjRoot.gameObject.AddComponent<VerticalLayoutGroup>(); av.spacing = 6; av.childControlWidth = false; av.childControlHeight = false; av.childForceExpandWidth = false; av.childForceExpandHeight = false;
            // menu (right)
            _menuRoot = UiKit.Rect(_root, "Menu"); UiKit.Anchor(_menuRoot, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-80, 40), new Vector2(620, 560));
            var mbg = UiKit.Panel(_menuRoot, "Bg", new Color(0.04f, 0.05f, 0.08f, 0.92f)); UiKit.Stretch(mbg.rectTransform);
            var mEdge = UiKit.Panel(_menuRoot, "Edge", UiColors.AccentBlueHot); UiKit.Anchor(mEdge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(6, 0)); mEdge.rectTransform.anchorMax = new Vector2(0, 1); mEdge.rectTransform.sizeDelta = new Vector2(6, 0);
            _menuTitle = UiKit.Label(_menuRoot, "AUDIBLE", 40, Color.white, true, TextAlignmentOptions.MidlineLeft, "Title"); UiKit.Anchor(_menuTitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(28, -18), new Vector2(560, 56)); _menuTitle.characterSpacing = 4f;
            // wheel (centre-left of the field)
            _wheelRoot = UiKit.Rect(_root, "Wheel"); UiKit.Anchor(_wheelRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(760, 640));
            _wheelTitle = UiKit.Label(_wheelRoot, "", 34, Color.white, true, TextAlignmentOptions.Center, "Title"); UiKit.Anchor(_wheelTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-380f, 0f), new Vector2(220, 90)); _wheelTitle.characterSpacing = 2f;
            _wheelTitle.textWrappingMode = TextWrappingModes.Normal;
        }

        public void Show(PreSnapHud h)
        {
            if (!_root.gameObject.activeSelf) _root.gameObject.SetActive(true);
            _playName.text = string.IsNullOrEmpty(h.PlayName) ? "" : h.PlayName.ToUpperInvariant();
            _mode.text = (string.IsNullOrEmpty(h.Formation) ? "" : h.Formation.ToUpperInvariant()) + (string.IsNullOrEmpty(h.Mode) ? "" : "    " + h.Mode);
            bool pad = MatchInput.GamepadActive;
            // prompt bar
            string hs = SigOf(h.Hints) + pad;
            if (hs != _hintSig || pad != _gamepad) { _hintSig = hs; _gamepad = pad; UiPrompts.FillRow(_promptRow, h.Hints, 42f, 24f, 34f); }
            // adjustments
            string asg = string.Join("|", h.Adjustments.ToArray());
            if (asg != _adjSig) { _adjSig = asg; RebuildAdjustments(h.Adjustments); }
            // menu
            bool hasMenu = h.Menu.Count > 0;
            _menuRoot.gameObject.SetActive(hasMenu);
            if (hasMenu) { string ms = h.MenuTitle + MenuSig(h.Menu) + pad; if (ms != _menuSig) { _menuSig = ms; RebuildMenu(h); } }
            else _menuSig = "";
            // wheel
            bool hasWheel = h.Wheel.Count > 0;
            _wheelRoot.gameObject.SetActive(hasWheel);
            if (hasWheel) { string ws = h.WheelTitle + WheelSig(h.Wheel); if (ws != _wheelSig) { _wheelSig = ws; RebuildWheel(h); } }
            else _wheelSig = "";
        }

        public void Hide() { _root.gameObject.SetActive(false); _hintSig = _adjSig = _menuSig = _wheelSig = ""; }

        static string SigOf(List<HintPair> l) { var sb = new System.Text.StringBuilder(); foreach (var p in l) sb.Append(p.Glyph).Append(':').Append(p.Text).Append(';'); return sb.ToString(); }
        static string MenuSig(List<MenuRow> l) { var sb = new System.Text.StringBuilder(); foreach (var p in l) sb.Append((int)p.Button).Append(p.Label).Append(p.Selected).Append(p.Dim).Append(';'); return sb.ToString(); }
        static string WheelSig(List<WheelOption> l) { var sb = new System.Text.StringBuilder(); foreach (var p in l) sb.Append(p.Slot).Append(p.Label).Append(p.Selected).Append(';'); return sb.ToString(); }

        void RebuildAdjustments(List<string> adj)
        {
            foreach (var g in _adjChips) Destroy(g);
            _adjChips.Clear();
            for (int i = 0; i < adj.Count && i < 7; i++)
            {
                var chip = UiKit.Panel(_adjRoot, "Adj" + i, new Color(1f, 0.784f, 0.216f, 0.95f));
                var t = UiKit.Label(chip.rectTransform, adj[i], 24, Color.black, true, TextAlignmentOptions.MidlineLeft, "Text"); UiKit.Stretch(t.rectTransform, 14, 0, 14, 0); t.characterSpacing = 2f;
                float w = 36f + adj[i].Length * 15f;
                chip.rectTransform.sizeDelta = new Vector2(w, 38);
                var le = chip.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = 38;
                _adjChips.Add(chip.gameObject);
            }
        }

        void RebuildMenu(PreSnapHud h)
        {
            foreach (var g in _menuRows) Destroy(g);
            _menuRows.Clear();
            _menuTitle.text = h.MenuTitle;
            float y = 90f;
            foreach (MenuRow r in h.Menu)
            {
                var row = UiKit.Panel(_menuRoot, "Row", r.Selected ? UiColors.AccentBlue : (r.Dim ? new Color(0.14f, 0.15f, 0.19f, 1f) : UiColors.Row));
                UiKit.Anchor(row.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -y), new Vector2(580, 76));
                var icon = UiPrompts.ButtonIcon(row.rectTransform, UiPrompts.GlyphOf(r.Button), 52f);
                icon.GetComponent<LayoutElement>().ignoreLayout = true;
                UiKit.Anchor(icon, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), icon.sizeDelta);
                var lbl = UiKit.Label(row.rectTransform, r.Label.ToUpperInvariant(), 30, r.Dim ? UiColors.TextMuted : Color.white, true, TextAlignmentOptions.MidlineLeft, "Label");
                UiKit.Anchor(lbl.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(128, 0), new Vector2(580 - 128 - 120, 60)); lbl.characterSpacing = 1.5f; lbl.fontSize = 28;
                if (!string.IsNullOrEmpty(r.Tag))
                {
                    var chip = UiKit.Panel(row.rectTransform, "Tag", TagColor(r.Tag)); UiKit.Anchor(chip.rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-12, 0), new Vector2(100, 38));
                    var tt = UiKit.Label(chip.rectTransform, r.Tag, 20, r.Tag == "MAN" || r.Tag == "FG" ? Color.black : Color.white, true, TextAlignmentOptions.Center, "TagText"); UiKit.Stretch(tt.rectTransform); tt.characterSpacing = 1.5f;
                }
                _menuRows.Add(row.gameObject);
                y += 84f;
            }
            _menuRoot.sizeDelta = new Vector2(620, 100f + h.Menu.Count * 84f);
        }

        static Color TagColor(string tag)
        {
            switch (tag)
            {
                case "RUN": return UiColors.Hex("#FA7805"); case "PASS": return UiColors.Hex("#054EED"); case "RPO": return UiColors.Hex("#05ACED");
                case "ZONE": return UiColors.Hex("#00287D"); case "MAN": return UiColors.Hex("#FFEA00"); case "BLITZ": return UiColors.Hex("#C20000");
                default: return UiColors.Hex("#2D5ABC");
            }
        }

        void RebuildWheel(PreSnapHud h)
        {
            foreach (var g in _wheelNodes) Destroy(g);
            _wheelNodes.Clear();
            _wheelTitle.text = h.WheelTitle;
            var back = UiKit.Panel(_wheelRoot, "Disc", new Color(0.04f, 0.05f, 0.08f, 0.78f)); UiKit.Anchor(back.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-380f, 0f), new Vector2(480, 480));
            back.sprite = UiKit.Sprite("ring_a"); back.preserveAspect = true; if (back.sprite != null) back.color = new Color(0.55f, 0.1f, 1f, 0.55f);
            back.transform.SetAsFirstSibling();
            _wheelNodes.Add(back.gameObject);
            Vector2[] dirs = { new Vector2(0, 1), new Vector2(0.72f, 0.72f), new Vector2(1, 0), new Vector2(0.72f, -0.72f), new Vector2(0, -1), new Vector2(-0.72f, -0.72f), new Vector2(-1, 0), new Vector2(-0.72f, 0.72f) };
            foreach (WheelOption o in h.Wheel)
            {
                Vector2 d = dirs[o.Slot & 7];
                var node = UiKit.Panel(_wheelRoot, "Opt", o.Selected ? UiColors.AccentBlueHot : new Color(0.16f, 0.17f, 0.23f, 0.96f));
                UiKit.Anchor(node.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(d.x * 190f - 380f, d.y * 175f), new Vector2(150, 50));
                var t = UiKit.Label(node.rectTransform, o.Label, 24, Color.white, true, TextAlignmentOptions.Center, "Text"); UiKit.Stretch(t.rectTransform); t.characterSpacing = 1.5f;
                _wheelNodes.Add(node.gameObject);
            }
        }
    }
}

