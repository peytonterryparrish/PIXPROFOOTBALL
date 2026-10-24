using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// A news ticker strip. Tile look (Direction 16): a flat #0C0D10 band with a gold NEWS block on the left and a headline line that scrolls right to left (96 units/s, unscaled time, diamond separators, wraps seamlessly
    /// with two copies of the line). Glass look: the glass bar with a "LEAGUE WIRE" chip. The scrolling strip sits on its own nested canvas, so moving it re-batches only the ticker, and the per-frame update is one
    /// transform write: no allocation. Pauses while a dialog is open; hidden when there are no headlines.
    /// </summary>
    public sealed class GlassTicker : MonoBehaviour
    {
        public RectTransform Viewport, Strip;
        public TextMeshProUGUI A, B;
        public float Speed = 96f;
        public float Gap = 160f;
        /// <summary>The object that is hidden when there is nothing to show (set by <see cref="GlassKit.Ticker"/>; defaults to this object).</summary>
        public RectTransform Host;
        float _w, _x;
        bool _fixedLayout;

        static string Sep { get { return GlassTheme.T.flat ? "     <color=#" + ColorUtility.ToHtmlStringRGB(GlassTheme.T.textMuted) + "><size=70%>◆</size></color>     " : "   <color=#F49A52>//</color>   "; } }

        public static string Join(IList<string> headlines)
        {
            var sb = new StringBuilder();
            string sep = Sep;
            for (int i = 0; i < headlines.Count; i++) { if (i > 0) sb.Append(sep); sb.Append(headlines[i]); }
            return sb.ToString();
        }

        public void SetHeadlines(IList<string> headlines)
        {
            bool any = headlines != null && headlines.Count > 0;
            var host = Host != null ? Host.gameObject : gameObject;
            if (!any) { _w = 0f; host.SetActive(false); return; }
            host.SetActive(true);
            string line = Join(headlines);
            A.text = line; B.text = line;
            A.ForceMeshUpdate(); _w = A.GetPreferredValues(line).x;
            var ar = A.rectTransform; ar.sizeDelta = new Vector2(_w + 4f, ar.sizeDelta.y);
            var br = B.rectTransform; br.sizeDelta = new Vector2(_w + 4f, br.sizeDelta.y);
            _fixedLayout = false;
            _x = 0f; Place();
        }

        void Place()
        {
            if (A == null || B == null) return;
            if (!_fixedLayout) { A.rectTransform.anchoredPosition = Vector2.zero; B.rectTransform.anchoredPosition = new Vector2(_w + Gap, 0f); _fixedLayout = true; }
            Strip.anchoredPosition = new Vector2(_x, 0f);   // one transform write per frame: the text meshes never rebuild
        }

        void Update() { Tick(Time.unscaledDeltaTime); }

        /// <summary>One frame of scrolling (also called by the measurement tools): no allocation.</summary>
        public void Tick(float dt)
        {
            if (_w <= 1f || UiDialog.AnyOpen) return;
            _x -= Speed * dt;
            float loop = _w + Gap;
            if (_x <= -loop) _x += loop;
            Place();
        }

        /// <summary>Builds the strip inside <paramref name="parent"/> (stretched by the caller): the band, the NEWS block / chip, a clipped viewport and two text copies.</summary>
        public static GlassTicker Build(RectTransform parent, string chipText, float height, float fontSize = 24f)
        {
            var t = GlassTheme.T;
            if (t.flat) return BuildTile(parent, chipText, height, fontSize);
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
            var strip = UiKit.Rect(vp, "Strip");
            strip.anchorMin = new Vector2(0f, 0f); strip.anchorMax = new Vector2(0f, 1f); strip.pivot = new Vector2(0f, 0.5f); strip.anchoredPosition = Vector2.zero; strip.sizeDelta = new Vector2(10f, 0f);
            tk.Strip = strip;
            tk.A = MakeLine(strip, "LineA", fontSize, t); tk.B = MakeLine(strip, "LineB", fontSize, t);
            return tk;
        }

        static GlassTicker BuildTile(RectTransform parent, string chipText, float height, float fontSize)
        {
            var t = GlassTheme.T;
            var band = UiKit.Rect(parent, "Ticker"); UiKit.Stretch(band);
            var bg = GlassKit.Plate(band, "Band", t.tickerBg); UiKit.Stretch(bg.rectTransform);
            var line = GlassKit.Plate(band, "TopLine", t.topBarLine);
            line.rectTransform.anchorMin = new Vector2(0f, 1f); line.rectTransform.anchorMax = new Vector2(1f, 1f); line.rectTransform.pivot = new Vector2(0.5f, 1f); line.rectTransform.anchoredPosition = Vector2.zero; line.rectTransform.sizeDelta = new Vector2(0f, 1f);
            var tk = band.gameObject.AddComponent<GlassTicker>();
            tk.Speed = t.tickerSpeed;
            // gold NEWS block, full height
            var news = GlassKit.Plate(band, "NewsBlock", t.gold);
            var nt = GlassKit.Text(news.rectTransform, chipText.ToUpperInvariant(), GlassText.Caption, TextAlignmentOptions.Center, "NewsText");
            GlassKit.Style(nt, t.sizeBadge, t.onGold, true, GlassTokens.Em(4f, t.sizeBadge));
            float nw = nt.GetPreferredValues(nt.text).x + 52f;
            news.rectTransform.anchorMin = new Vector2(0f, 0f); news.rectTransform.anchorMax = new Vector2(0f, 1f); news.rectTransform.pivot = new Vector2(0f, 0.5f);
            news.rectTransform.anchoredPosition = Vector2.zero; news.rectTransform.sizeDelta = new Vector2(nw, 0f);
            UiKit.Stretch(nt.rectTransform);
            // clipped viewport; the strip sits on its own nested canvas (isolates the batch rebuilds of the scrolling text)
            var vp = UiKit.Rect(band, "Viewport");
            vp.anchorMin = Vector2.zero; vp.anchorMax = Vector2.one; vp.offsetMin = new Vector2(nw + 26f, 0f); vp.offsetMax = new Vector2(0f, 0f);
            vp.gameObject.AddComponent<RectMask2D>();
            tk.Viewport = vp;
            var strip = UiKit.Rect(vp, "Strip");
            strip.anchorMin = new Vector2(0f, 0f); strip.anchorMax = new Vector2(0f, 1f); strip.pivot = new Vector2(0f, 0.5f); strip.anchoredPosition = Vector2.zero; strip.sizeDelta = new Vector2(10f, 0f);
            var cv = strip.gameObject.AddComponent<Canvas>(); cv.overrideSorting = false;
            tk.Strip = strip;
            tk.A = MakeLine(strip, "LineA", fontSize, t); tk.B = MakeLine(strip, "LineB", fontSize, t);
            tk.A.color = tk.B.color = new Color(0.737f, 0.753f, 0.792f, 1f);   // #BCC0CA
            tk.A.font = tk.B.font = GlassTheme.Current.Body;
            tk.Host = band;
            return tk;
        }

        static TextMeshProUGUI MakeLine(RectTransform parent, string name, float size, GlassTokens t)
        {
            var tx = GlassKit.Text(parent, "", GlassText.Label, TextAlignmentOptions.MidlineLeft, name);
            GlassKit.Style(tx, size, t.textPrimary, true, t.flat ? GlassTokens.Em(1f, size) : 1f);
            tx.richText = true; tx.textWrappingMode = TextWrappingModes.NoWrap; tx.overflowMode = TextOverflowModes.Overflow;
            var r = tx.rectTransform; r.anchorMin = new Vector2(0f, 0f); r.anchorMax = new Vector2(0f, 1f); r.pivot = new Vector2(0f, 0.5f); r.sizeDelta = new Vector2(100f, 0f);
            return tx;
        }
    }
}
