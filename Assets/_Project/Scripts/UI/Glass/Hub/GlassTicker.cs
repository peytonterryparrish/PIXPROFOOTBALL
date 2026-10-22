using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// A news ticker strip: a glass bar with a "LEAGUE WIRE" chip on the left and a headline line that scrolls right to left on unscaled time and wraps seamlessly (two copies of the line).
    /// The text is live TextMeshPro; the viewport clips it.
    /// </summary>
    public sealed class GlassTicker : MonoBehaviour
    {
        public RectTransform Viewport, Strip;
        public TextMeshProUGUI A, B;
        public float Speed = 96f;
        public float Gap = 160f;
        float _w, _x;

        const string Sep = "   <color=#F49A52>//</color>   ";

        public static string Join(IList<string> headlines)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < headlines.Count; i++) { if (i > 0) sb.Append(Sep); sb.Append(headlines[i]); }
            return sb.ToString();
        }

        public void SetHeadlines(IList<string> headlines)
        {
            string line = Join(headlines);
            A.text = line; B.text = line;
            A.ForceMeshUpdate(); _w = A.GetPreferredValues(line).x;
            var ar = A.rectTransform; ar.sizeDelta = new Vector2(_w + 4f, ar.sizeDelta.y);
            var br = B.rectTransform; br.sizeDelta = new Vector2(_w + 4f, br.sizeDelta.y);
            _x = 0f; Place();
        }

        void Place()
        {
            if (A == null || B == null) return;
            A.rectTransform.anchoredPosition = new Vector2(_x, 0f);
            B.rectTransform.anchoredPosition = new Vector2(_x + _w + Gap, 0f);
        }

        void Update()
        {
            if (_w <= 1f) return;
            _x -= Speed * Time.unscaledDeltaTime;
            float loop = _w + Gap;
            if (_x <= -loop) _x += loop;
            Place();
        }

        /// <summary>Builds the strip inside <paramref name="parent"/> (stretched by the caller): chip, clipped viewport, two text copies.</summary>
        public static GlassTicker Build(RectTransform parent, string chipText, float height, float fontSize = 24f)
        {
            var t = GlassTheme.T;
            var bar = GlassKit.Panel(parent, "Ticker", 1.25f, false, height * 0.5f);
            UiKit.Stretch(bar.rectTransform);
            var tk = bar.gameObject.AddComponent<GlassTicker>();
            // chip (fixed width) on the left
            var chip = GlassKit.Surface(bar.rectTransform, "NewsChip", GlassKind.Accent, height * 0.5f - 5f, false, 1f);
            UiKit.Anchor(chip.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(6f, 0f), new Vector2(210f, height - 12f));
            var ct = GlassKit.Text(chip.rectTransform, chipText.ToUpperInvariant(), GlassText.Caption, TextAlignmentOptions.Center, "ChipText");
            GlassKit.Style(ct, 20f, t.textOnAction, true, 3f); UiKit.Stretch(ct.rectTransform, 6f, 0f, 6f, 0f); UiKit.Fit(ct, 16f);
            // viewport
            var vp = UiKit.Rect(bar.rectTransform, "Viewport");
            vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one; vp.offsetMin = new Vector2(236f, 0f); vp.offsetMax = new Vector2(-24f, 0f);
            vp.gameObject.AddComponent<RectMask2D>();
            tk.Viewport = vp;
            var strip = UiKit.Rect(vp, "Strip"); UiKit.Stretch(strip); tk.Strip = strip;
            tk.A = MakeLine(strip, "LineA", fontSize, t); tk.B = MakeLine(strip, "LineB", fontSize, t);
            return tk;
        }

        static TextMeshProUGUI MakeLine(RectTransform parent, string name, float size, GlassTokens t)
        {
            var tx = GlassKit.Text(parent, "", GlassText.Label, TextAlignmentOptions.MidlineLeft, name);
            GlassKit.Style(tx, size, t.textPrimary, true, 1f);
            tx.richText = true; tx.textWrappingMode = TextWrappingModes.NoWrap; tx.overflowMode = TextOverflowModes.Overflow;
            var r = tx.rectTransform; r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 0.5f); r.sizeDelta = new Vector2(100f, 0f);
            return tx;
        }
    }
}
