using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// 2K8-style kicking widgets built in code: a wind indicator (compass disc with an arrow and "N MPH") in the top-right corner and a kick meter panel
    /// ("TIMING Perfect / AIM Centered" with a marker gauge per row) bottom-centre above the hint bar. Display only: the owner feeds it values.
    /// </summary>
    public sealed class KickHud : MonoBehaviour
    {
        // ---- wind
        RectTransform _windRoot, _arrow;
        Image _arrowImg;
        TextMeshProUGUI _windText;
        // ---- meter
        RectTransform _meterRoot;
        Row _timing, _aim;
        bool _chrome = true, _meterWanted, _chromeVis = true, _windShown = true;
        /// <summary>v0.11 B-077: the wind dial matters only on a kickoff, punt or kick; the match HUD shows it just then.</summary>
        public void SetWindShown(bool shown) { if (shown == _windShown) return; _windShown = shown; if (_windRoot != null) _windRoot.gameObject.SetActive(shown && _chromeVis); }
        float _baseY = 170f;

        sealed class Row { public TextMeshProUGUI Value; public RectTransform Marker; public Image MarkerImg; public float BarWidth; }

        const float MeterWidth = 600f;

        /// <summary>Builds the widgets under <paramref name="parent"/> (the match HUD canvas root).</summary>
        public static KickHud Create(Transform parent)
        {
            var r = UiKit.Rect(parent, "KickHud"); UiKit.Stretch(r);
            var k = r.gameObject.AddComponent<KickHud>();
            k.BuildWind(r); k.BuildMeter(r);
            PixFootball.UI.Glass.GlassSkinWatcher.Watch(r.gameObject);
            k.SetWind(0f, 0f);
            k.SetKickMeter(false, 0.5f, "", 0.5f, "");
            return k;
        }

        // ============================================================================================ wind
        void BuildWind(RectTransform root)
        {
            _windRoot = UiKit.Rect(root, "Wind"); UiKit.Anchor(_windRoot, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-HudMetrics.SafeX, -HudMetrics.SafeY), new Vector2(150, 160)); _windRoot.localScale = Vector3.one * 0.8f;   // v0.11: smaller, on the title-safe margin
            var ring = UiKit.Panel(_windRoot, "Ring", UiColors.Gold, Disc()); UiKit.Anchor(ring.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), Vector2.zero, new Vector2(112, 112));
            var inner = UiKit.Panel(ring.rectTransform, "Inner", new Color(0.05f, 0.06f, 0.1f, 0.96f), Disc()); UiKit.Anchor(inner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(98, 98));
            // compass tick for north
            var n = UiKit.Label(inner.rectTransform, "", 20, UiColors.TextMuted, true, TextAlignmentOptions.Center, "North"); n.characterSpacing = 1f;   // v0.11 B-083: the arrow is drawn in screen space, so the dial no longer pretends to have a compass north
            UiKit.Anchor(n.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -2), new Vector2(70, 24));
            _arrowImg = UiKit.Panel(inner.rectTransform, "Arrow", Color.white, Arrow()); _arrowImg.preserveAspect = true;
            _arrow = _arrowImg.rectTransform; UiKit.Anchor(_arrow, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(58, 58));
            var plate = UiKit.Panel(_windRoot, "Plate", new Color(0.05f, 0.06f, 0.1f, 0.92f)); UiKit.Anchor(plate.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 0), new Vector2(150, 40));
            _windText = UiKit.Label(plate.rectTransform, "CALM", 28, Color.white, true, TextAlignmentOptions.Center, "Text"); UiKit.Stretch(_windText.rectTransform, 8, 0, 8, 0); _windText.characterSpacing = 1.5f; UiKit.Fit(_windText, HudMetrics.MinFont);   // v0.11 B-082
        }

        /// <summary>Wind indicator. <paramref name="degreesFromNorth"/> is the heading the wind blows TOWARD, clockwise from the compass "N" (screen up); 0 mph = calm (no arrow).</summary>
        public void SetWind(float degreesFromNorth, float mph)
        {
            if (_windRoot == null) return;
            int m = Mathf.RoundToInt(Mathf.Max(0f, mph));
            bool calm = m <= 0;
            _arrowImg.enabled = !calm;
            _arrow.localRotation = Quaternion.Euler(0f, 0f, -degreesFromNorth);
            _windText.text = calm ? "CALM" : m + " MPH";
            _windText.fontSize = 28f;
            _arrowImg.color = m >= 15 ? UiColors.Orange : (m >= 8 ? UiColors.Gold : Color.white);
        }

        // ============================================================================================ kick meter
        void BuildMeter(RectTransform root)
        {
            _meterRoot = UiKit.Rect(root, "KickMeter"); UiKit.Anchor(_meterRoot, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, _baseY), new Vector2(MeterWidth, 150));
            var bg = UiKit.Panel(_meterRoot, "Bg", new Color(0.04f, 0.05f, 0.08f, 0.88f)); UiKit.Stretch(bg.rectTransform);
            var edge = UiKit.Panel(_meterRoot, "Edge", UiColors.Gold); UiKit.Anchor(edge.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(6, 0)); edge.rectTransform.anchorMax = new Vector2(0, 1); edge.rectTransform.sizeDelta = new Vector2(6, 0);
            _timing = MakeRow(_meterRoot, "TIMING", -10f);
            _aim = MakeRow(_meterRoot, "AIM", -80f);
            _meterRoot.gameObject.SetActive(false);
        }

        Row MakeRow(RectTransform parent, string title, float top)
        {
            var row = new Row { BarWidth = MeterWidth - 64f };
            var t = UiKit.Label(parent, title, 26, UiColors.TextSecondary, true, TextAlignmentOptions.MidlineLeft, title + "Title");
            UiKit.Anchor(t.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, top), new Vector2(220, 36)); t.characterSpacing = 3f;
            row.Value = UiKit.Label(parent, "", 32, Color.white, true, TextAlignmentOptions.MidlineRight, title + "Value");
            UiKit.Anchor(row.Value.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-32, top), new Vector2(300, 36));
            var bar = UiKit.Panel(parent, title + "Bar", new Color(1f, 1f, 1f, 0.16f));
            UiKit.Anchor(bar.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, top - 44f), new Vector2(row.BarWidth, 10));
            var sweet = UiKit.Panel(bar.rectTransform, "Sweet", new Color(0.07f, 0.94f, 0.33f, 0.55f));
            UiKit.Anchor(sweet.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(row.BarWidth * 0.12f, 10));
            var tick = UiKit.Panel(bar.rectTransform, "Centre", new Color(1f, 1f, 1f, 0.7f));
            UiKit.Anchor(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(2, 20));
            row.MarkerImg = UiKit.Panel(bar.rectTransform, "Marker", Color.white);
            row.Marker = row.MarkerImg.rectTransform;
            UiKit.Anchor(row.Marker, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(row.BarWidth * 0.5f, 0), new Vector2(10, 28));
            return row;
        }

        /// <summary>Kick meter: marker positions are 0..1 along the bar (0.5 = centre / perfect); labels are free text ("Perfect", "Good", "Early", "Late", "Centered", "Left", "Right").</summary>
        public void SetKickMeter(bool visible, float timing01, string timingLabel, float aim01, string aimLabel)
        {
            _meterWanted = visible;
            if (_meterRoot == null) return;
            _meterRoot.gameObject.SetActive(visible && _chrome);
            if (!visible) return;
            Apply(_timing, timing01, timingLabel);
            Apply(_aim, aim01, aimLabel);
        }

        static void Apply(Row r, float v01, string label)
        {
            v01 = Mathf.Clamp01(v01);
            r.Marker.anchoredPosition = new Vector2(r.BarWidth * v01, 0f);
            r.Value.text = label ?? "";
            Color c = LabelColor(label);
            r.Value.color = c; r.MarkerImg.color = c;
        }

        /// <summary>Colour of a meter verdict: perfect/centred green, good gold, anything else orange.</summary>
        public static Color LabelColor(string label)
        {
            if (string.IsNullOrEmpty(label)) return Color.white;
            string l = label.ToLowerInvariant();
            if (l == "perfect" || l == "centered" || l == "centred") return new Color(0.07f, 0.94f, 0.33f);
            if (l == "good") return UiColors.Gold;
            return UiColors.Orange;
        }

        /// <summary>Bottom offset (canvas px) of the kick meter; the match HUD raises it above the bottom scorebug bar and the hint bar.</summary>
        public void SetBaseY(float y)
        {
            _baseY = y;
            if (_meterRoot != null) _meterRoot.anchoredPosition = new Vector2(0f, y);
        }

        /// <summary>Full-screen panels (play call, pause, final...) hide the widgets.</summary>
        public void SetChrome(bool visible)
        {
            _chrome = visible;
            _chromeVis = visible;
            if (_windRoot != null) _windRoot.gameObject.SetActive(visible && _windShown);
            if (_meterRoot != null) _meterRoot.gameObject.SetActive(visible && _meterWanted);
        }

        // ============================================================================================ procedural sprites
        static Sprite _disc, _arrowSprite;

        static Sprite Disc()
        {
            if (_disc != null) return _disc;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            float c = (n - 1) * 0.5f, rad = n * 0.5f - 1f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(rad - d + 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            _disc = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return _disc;
        }

        /// <summary>An up-pointing arrow (head plus shaft), white with alpha, tinted by the Image.</summary>
        static Sprite Arrow()
        {
            if (_arrowSprite != null) return _arrowSprite;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    bool head = v >= 0.5f && v <= 0.97f && Mathf.Abs(u - 0.5f) <= (0.97f - v) / 0.47f * 0.32f;
                    bool shaft = v >= 0.05f && v < 0.5f && Mathf.Abs(u - 0.5f) <= 0.1f;
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(head || shaft ? 255 : 0));
                }
            tex.SetPixels32(px); tex.Apply(false, true);
            _arrowSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
            return _arrowSprite;
        }
    }
}
