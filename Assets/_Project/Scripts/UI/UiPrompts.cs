using System.Collections.Generic;
using System.Text;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>One prompt in a footer / hint bar: one or more button icons followed by a label ("[A][B][X][Y] THROW").</summary>
    public struct PromptItem
    {
        public string[] Glyphs; public string Text;
        public PromptItem(string[] glyphs, string text) { Glyphs = glyphs; Text = text; }
    }

    /// <summary>Controller glyph / keyboard cap prompts ("A  SNAP") shared by the play-call screen, the pre-snap bar, the penalty panel, every footer and the in-play hint bar.</summary>
    public static class UiPrompts
    {
        /// <summary>The ONE glyph -> keyboard key table (QA-770). Every glyph the UI can draw has an entry.</summary>
        public static string KeyOf(string glyph)
        {
            switch (glyph)
            {
                case "glyph_a": return "SPACE"; case "glyph_b": return "F"; case "glyph_x": return "C"; case "glyph_y": return "R"; case "glyph_lb": return "Z"; case "glyph_rb": return "X";
                case "glyph_lt": return "V"; case "glyph_rt": return "SHIFT"; case "glyph_ls": return "WASD"; case "glyph_rs": return "IJKL";
                case "glyph_dpad_up": return "G"; case "glyph_dpad_down": return "T"; case "glyph_dpad_left": return "Q"; case "glyph_dpad_right": return "E";
                case "glyph_view": return "TAB"; case "glyph_menu": return "ESC"; case "glyph_dpad": return "ARROWS";
                case "glyph_analog_all": return "ARROWS"; case "glyph_analog_click": return "BKSP";
                default: return "?";
            }
        }

        /// <summary>Hint-bar token (the text inside square brackets) -> glyph key, or null when the token is not a button.</summary>
        public static string GlyphOfToken(string token)
        {
            switch (token.ToUpperInvariant())
            {
                case "A": return "glyph_a"; case "B": return "glyph_b"; case "X": return "glyph_x"; case "Y": return "glyph_y";
                case "LB": return "glyph_lb"; case "RB": return "glyph_rb"; case "LT": return "glyph_lt"; case "RT": return "glyph_rt";
                case "LS": return "glyph_ls"; case "RS": return "glyph_rs"; case "VIEW": return "glyph_view"; case "MENU": return "glyph_menu";
                case "UP": return "glyph_dpad_up"; case "DOWN": return "glyph_dpad_down"; case "LEFT": return "glyph_dpad_left"; case "RIGHT": return "glyph_dpad_right";
                case "DPAD": return "glyph_dpad"; case "NAV": return "glyph_analog_all";
                default: return null;
            }
        }

        /// <summary>
        /// Parses a hint string with button tokens ("[A][B] THROW   [RS] LEAD   TAP = BULLET") into prompt items. Text without a token before it becomes a label-only item;
        /// consecutive tokens share the label that follows them. Pure function (EditMode tested).
        /// </summary>
        public static List<PromptItem> ParseHint(string s)
        {
            var items = new List<PromptItem>();
            if (string.IsNullOrEmpty(s)) return items;
            var glyphs = new List<string>(); var text = new StringBuilder();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c == '[')
                {
                    int e = s.IndexOf(']', i + 1);
                    string g = e > i ? GlyphOfToken(s.Substring(i + 1, e - i - 1)) : null;
                    if (g != null)
                    {
                        if (text.ToString().Trim().Length > 0) { Flush(items, glyphs, text); }
                        glyphs.Add(g); i = e + 1; continue;
                    }
                }
                text.Append(c); i++;
            }
            Flush(items, glyphs, text);
            return items;
        }

        static void Flush(List<PromptItem> items, List<string> glyphs, StringBuilder text)
        {
            string t = text.ToString().Trim();
            if (glyphs.Count == 0 && t.Length == 0) { text.Length = 0; return; }
            items.Add(new PromptItem(glyphs.ToArray(), t));
            glyphs.Clear(); text.Length = 0;
        }

        /// <summary>Plain-text version of a hint ("[A] KICK" -> "A KICK" on pad, "SPACE KICK" on keyboard) for logs and non-UI presentations.</summary>
        public static string PlainHint(string s, bool gamepad)
        {
            var sb = new StringBuilder();
            foreach (var it in ParseHint(s))
            {
                if (sb.Length > 0) sb.Append("   ");
                for (int k = 0; k < it.Glyphs.Length; k++) { if (k > 0) sb.Append(' '); sb.Append(gamepad ? it.Glyphs[k].Replace("glyph_", "").ToUpperInvariant() : KeyOf(it.Glyphs[k])); }
                if (it.Glyphs.Length > 0 && it.Text.Length > 0) sb.Append(' ');
                sb.Append(it.Text);
            }
            return sb.ToString();
        }

        public static RectTransform ButtonIcon(Transform parent, string glyph, float size)
        {
            var holder = UiKit.Rect(parent, "Icon_" + glyph);
            if (InputDeviceTracker.GamepadActive)
            {
                holder.sizeDelta = new Vector2(size * 1.4f, size);
                var g = UiKit.Glyph(holder, glyph == "glyph_dpad" ? "glyph_dpad_up" : glyph, size);
                UiKit.Anchor(g.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, g.rectTransform.sizeDelta);
                DirectionMark(holder, glyph, size);
            }
            else
            {
                string kt = KeyOf(glyph);
                float w = Mathf.Max(size * 1.2f, 20f + kt.Length * size * 0.42f);
                holder.sizeDelta = new Vector2(w, size);
                RectTransform keyRt;
                if (UiTheme.IsGlass)
                {
                    // glass keycap: a light silver tile with the pack's hairline edge (readable on any backdrop)
                    var kg = PixFootball.UI.Glass.GlassKit.Solid(holder, "Key", PixFootball.UI.Glass.GlassTheme.T.lightGrey, 7f, 1.2f); keyRt = kg.rectTransform;
                }
                else { var key = UiKit.Panel(holder, "Key", new Color(0.92f, 0.92f, 0.95f)); keyRt = key.rectTransform; }
                UiKit.Anchor(keyRt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, size * 0.86f));
                var t = UiKit.Label(keyRt, kt, size * 0.40f, UiTheme.IsGlass ? PixFootball.UI.Glass.GlassTheme.T.textOnAction : new Color(0.08f, 0.09f, 0.12f), true, TextAlignmentOptions.Center, "KeyText"); UiKit.Stretch(t.rectTransform);
            }
            var le = holder.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = holder.sizeDelta.x; le.preferredHeight = holder.sizeDelta.y;
            return holder;
        }

        /// <summary>The D-pad glyph art looks the same at prompt size whichever direction it marks, so a small gold arrow shows which one is meant (QA-122 / 162).</summary>
        static void DirectionMark(RectTransform holder, string glyph, float size)
        {
            float rot; Vector2 at;
            switch (glyph)
            {
                case "glyph_dpad_up": rot = -90f; at = new Vector2(0.5f, 1f); break;
                case "glyph_dpad_down": rot = 90f; at = new Vector2(0.5f, 0f); break;
                case "glyph_dpad_left": rot = 0f; at = new Vector2(0f, 0.5f); break;
                case "glyph_dpad_right": rot = 180f; at = new Vector2(1f, 0.5f); break;
                default: return;
            }
            var arrow = UiKit.Panel(holder, "DirMark", UiColors.Gold, UiKit.Sprite("sb_arrow")); arrow.preserveAspect = true;
            UiKit.Anchor(arrow.rectTransform, at, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size * 0.4f, size * 0.4f));
            arrow.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rot);
        }

        public static string GlyphOf(Btn b) { return "glyph_" + b.ToString().ToLowerInvariant(); }

        /// <summary>Fills <paramref name="row"/> (a HorizontalLayoutGroup is added) with glyph + label pairs.</summary>
        public static void FillRow(RectTransform row, IList<HintPair> hints, float size = 44f, float font = 26f, float spacing = 40f)
        {
            var items = new List<PromptItem>(hints.Count);
            foreach (HintPair p in hints) items.Add(new PromptItem(string.IsNullOrEmpty(p.Glyph) ? new string[0] : new[] { p.Glyph }, p.Text));
            FillRow(row, items, size, font, spacing);
        }

        /// <summary>Fills <paramref name="row"/> with prompt items (one or more icons then a label each).</summary>
        public static void FillRow(RectTransform row, IList<PromptItem> items, float size = 44f, float font = 26f, float spacing = 40f, TextAnchor align = TextAnchor.MiddleCenter)
        {
            for (int i = row.childCount - 1; i >= 0; i--) { var c = row.GetChild(i).gameObject; c.SetActive(false); UiKit.Kill(c); }
            var h = UiKit.GetOrAdd<HorizontalLayoutGroup>(row.gameObject);
            h.spacing = spacing; h.childAlignment = align; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            for (int n = 0; n < items.Count; n++)
            {
                PromptItem p = items[n];
                var pr = UiKit.Rect(row, "Prompt");
                var ph = pr.gameObject.AddComponent<HorizontalLayoutGroup>(); ph.spacing = 8; ph.childAlignment = TextAnchor.MiddleLeft; ph.childControlHeight = true; ph.childControlWidth = true; ph.childForceExpandWidth = false; ph.childForceExpandHeight = false;
                if (p.Glyphs != null) foreach (string g in p.Glyphs) if (!string.IsNullOrEmpty(g)) ButtonIcon(pr, g, size);
                if (!string.IsNullOrEmpty(p.Text))
                {
                    var t = UiKit.Label(pr, p.Text, font, UiColors.TextPrimary, true, TextAlignmentOptions.MidlineLeft, "Text");
                    var le = t.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = size;
                    var fit = t.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                }
                var rf = pr.gameObject.AddComponent<ContentSizeFitter>(); rf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; rf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }
    }

    /// <summary>
    /// A footer / hint row that rebuilds itself when the active input device flips (controller glyphs vs keycaps). Items are given in controller terms (glyph keys), the row
    /// shows the right art for the device the player is actually using (QA-751 / 752 / 753 / 859 / 876).
    /// </summary>
    public sealed class PromptFooter : MonoBehaviour
    {
        readonly List<PromptItem> _items = new List<PromptItem>();
        RectTransform _row; float _size = 48f, _font = 26f, _spacing = 48f;
        /// <summary>Footers hug the left edge (the hint bar centres itself through its content-sized plate).</summary>
        public TextAnchor Align = TextAnchor.MiddleLeft;
        int _builtScheme = -1;
        bool _dirty;

        /// <summary>Creates the footer content inside <paramref name="row"/> (the row gets the component).</summary>
        public static PromptFooter Attach(RectTransform row, float size = 48f, float font = 26f, float spacing = 48f)
        {
            var f = UiKit.GetOrAdd<PromptFooter>(row.gameObject);
            f._row = row; f._size = size; f._font = font; f._spacing = spacing;
            return f;
        }

        /// <summary>Rebuilds the row now (edit-mode QA tools, where OnEnable does not run).</summary>
        public void ForceRebuild() { if (_row != null && _items.Count > 0) Rebuild(); }

        public PromptFooter SetItems(IList<PromptItem> items)
        {
            _items.Clear(); _items.AddRange(items); _dirty = true;
            if (isActiveAndEnabled) Rebuild();
            return this;
        }

        public PromptFooter Set(params PromptItem[] items)
        {
            _items.Clear(); _items.AddRange(items); _dirty = true;
            if (isActiveAndEnabled) Rebuild();
            return this;
        }

        public static PromptItem Item(string glyph, string text) { return new PromptItem(new[] { glyph }, text); }

        void Rebuild()
        {
            _builtScheme = InputDeviceTracker.GamepadActive ? 1 : 0; _dirty = false;
            UiPrompts.FillRow(_row, _items, _size, _font, _spacing, Align);
        }

        void OnEnable() { if (_row != null && _items.Count > 0) Rebuild(); }

        void Update()
        {
            InputDeviceTracker.Update();
            int s = InputDeviceTracker.GamepadActive ? 1 : 0;
            if (_dirty || s != _builtScheme) { if (_row != null && _items.Count > 0) Rebuild(); }
        }
    }
}
