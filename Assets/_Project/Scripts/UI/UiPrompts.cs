using System.Collections.Generic;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>Controller glyph / keyboard cap prompts ("A  SNAP") shared by the play-call screen, the pre-snap bar and the penalty panel.</summary>
    public static class UiPrompts
    {
        public static string KeyOf(string glyph)
        {
            switch (glyph)
            {
                case "glyph_a": return "SPACE"; case "glyph_b": return "F"; case "glyph_x": return "C"; case "glyph_y": return "R"; case "glyph_lb": return "Z"; case "glyph_rb": return "X";
                case "glyph_lt": return "V"; case "glyph_rt": return "SHIFT"; case "glyph_ls": return "WASD"; case "glyph_rs": return "IJKL";
                case "glyph_dpad_up": return "G"; case "glyph_dpad_down": return "T"; case "glyph_dpad_left": return "Q"; case "glyph_dpad_right": return "E";
                case "glyph_view": return "TAB"; case "glyph_menu": return "ESC"; case "glyph_dpad": return "ARROWS"; default: return "?";
            }
        }

        public static RectTransform ButtonIcon(Transform parent, string glyph, float size)
        {
            var holder = UiKit.Rect(parent, "Icon_" + glyph);
            if (MatchInput.GamepadActive)
            {
                holder.sizeDelta = new Vector2(size * 1.4f, size);
                var g = UiKit.Glyph(holder, glyph == "glyph_dpad" ? "glyph_dpad_up" : glyph, size);
                UiKit.Anchor(g.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, g.rectTransform.sizeDelta);
            }
            else
            {
                string kt = KeyOf(glyph);
                float w = Mathf.Max(size * 1.2f, 20f + kt.Length * size * 0.42f);
                holder.sizeDelta = new Vector2(w, size);
                var key = UiKit.Panel(holder, "Key", new Color(0.92f, 0.92f, 0.95f)); UiKit.Anchor(key.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(w, size * 0.86f));
                var t = UiKit.Label(key.rectTransform, kt, size * 0.40f, new Color(0.08f, 0.09f, 0.12f), true, TextAlignmentOptions.Center, "KeyText"); UiKit.Stretch(t.rectTransform);
            }
            var le = holder.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = holder.sizeDelta.x; le.preferredHeight = holder.sizeDelta.y;
            return holder;
        }

        public static string GlyphOf(Btn b) { return "glyph_" + b.ToString().ToLowerInvariant(); }

        /// <summary>Fills <paramref name="row"/> (a HorizontalLayoutGroup is added) with glyph + label pairs.</summary>
        public static void FillRow(RectTransform row, IList<HintPair> hints, float size = 44f, float font = 26f, float spacing = 40f)
        {
            for (int i = row.childCount - 1; i >= 0; i--) Object.Destroy(row.GetChild(i).gameObject);
            var h = row.GetComponent<HorizontalLayoutGroup>() ?? row.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childAlignment = TextAnchor.MiddleCenter; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            foreach (HintPair p in hints)
            {
                var pr = UiKit.Rect(row, "Prompt");
                var ph = pr.gameObject.AddComponent<HorizontalLayoutGroup>(); ph.spacing = 10; ph.childAlignment = TextAnchor.MiddleLeft; ph.childControlHeight = true; ph.childControlWidth = true; ph.childForceExpandWidth = false; ph.childForceExpandHeight = false;
                if (!string.IsNullOrEmpty(p.Glyph)) ButtonIcon(pr, p.Glyph, size);
                var t = UiKit.Label(pr, p.Text, font, UiColors.TextPrimary, true, TextAlignmentOptions.MidlineLeft, "Text");
                var le = t.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = size;
                var fit = t.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                var rf = pr.gameObject.AddComponent<ContentSizeFitter>(); rf.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; rf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
        }
    }
}
