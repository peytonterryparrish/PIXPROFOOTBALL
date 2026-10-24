using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// v0.11 pop-up lane (owner note 12): the event banner, the whistle pill, the penalty flag and the toast all hang in ONE column at the top centre of the picture, stacked top to bottom with a fixed gap,
    /// so they are always centred, never overlap and never sit over the middle of the field. Two banner sizes: a big card for the events that matter (score, turnover, quarter end) and a slim strip for
    /// everything else (first down, sack, incomplete). Styled with the Tile Hub tokens (<see cref="HudStyle"/>): flat charcoal, one coloured slab, square corners.
    /// </summary>
    public partial class MatchHud
    {
        float _laneBannerY = -HudMetrics.PopupTop;

        /// <summary>True when the banner on screen already carries this text as its headline or its sub-line (the same tackle is never announced twice, B-044).</summary>
        bool BannerSays(string text)
        {
            if (_banner == null || !_banner.gameObject.activeSelf || string.IsNullOrEmpty(text)) return false;
            string u = text.Trim().ToUpperInvariant();
            string h = _bannerText != null ? _bannerText.text.Trim().ToUpperInvariant() : "", s = _bannerSub != null ? _bannerSub.text.Trim().ToUpperInvariant() : "";
            return (h.Length > 0 && u.StartsWith(h)) || (s.Length > 0 && u.StartsWith(s));
        }
        bool _bannerBig;

        static readonly string[] BigBannerKeys = { "TOUCHDOWN", "FIELD GOAL", "EXTRA POINT", "TWO-POINT", "SAFETY", "INTERCEPTION", "FUMBLE", "TURNOVER", "HALFTIME", "OVERTIME", "END OF", "FINAL" };

        /// <summary>True for the events that get the big banner card.</summary>
        public static bool IsBigBanner(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string u = text.ToUpperInvariant();
            foreach (var k in BigBannerKeys) if (u.Contains(k)) return true;
            return false;
        }

        /// <summary>Builds the tile-style banner and toast (both themes). Returns true when it did.</summary>
        bool BuildBannerTile()
        {
            _banner = UiKit.Rect(_root, "Banner"); UiKit.Anchor(_banner, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -HudMetrics.PopupTop), new Vector2(820f, 116f));
            var edge = UiKit.Panel(_banner, "Frame", HudStyle.Border); UiKit.Stretch(edge.rectTransform);
            var baseRect = UiKit.Panel(_banner, "Base", HudStyle.TileSolid); UiKit.Stretch(baseRect.rectTransform, 2f, 2f, 2f, 2f); _bannerBase = baseRect;
            var wash = UiKit.Panel(_banner, "Tint", new Color(1f, 1f, 1f, 0.14f), GlassSprite()); UiKit.Stretch(wash.rectTransform, 2f, 2f, 2f, 2f); _bannerBg = wash;
            var slab = UiKit.Panel(_banner, "Slab", Color.white); slab.rectTransform.anchorMin = Vector2.zero; slab.rectTransform.anchorMax = new Vector2(0f, 1f); slab.rectTransform.pivot = new Vector2(0f, 0.5f); slab.rectTransform.sizeDelta = new Vector2(8f, 0f); slab.rectTransform.anchoredPosition = Vector2.zero; _bannerSlab = slab;
            var rail = UiKit.Panel(_banner, "Rail", HudStyle.Gold); rail.rectTransform.anchorMin = Vector2.zero; rail.rectTransform.anchorMax = new Vector2(1f, 0f); rail.rectTransform.pivot = new Vector2(0.5f, 0f); rail.rectTransform.sizeDelta = new Vector2(0f, 3f); rail.rectTransform.anchoredPosition = Vector2.zero; _bannerRail = rail;
            _bannerText = UiKit.Label(_banner, "TOUCHDOWN", 60, HudStyle.Text1, true, TextAlignmentOptions.Center, "Text"); _bannerText.characterSpacing = 4f;
            _bannerSub = UiKit.Label(_banner, "", 24, HudStyle.Text2, true, TextAlignmentOptions.Center, "Sub"); _bannerSub.characterSpacing = 2f;
            _banner.gameObject.SetActive(false);

            _toast = UiKit.Rect(_root, "Toast"); UiKit.Anchor(_toast, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -200), new Vector2(560, 48));
            var tb = UiKit.Panel(_toast, "Bg", HudStyle.Tile); UiKit.Stretch(tb.rectTransform);
            var tgold = UiKit.Panel(_toast, "Gold", HudStyle.Gold); tgold.rectTransform.anchorMin = Vector2.zero; tgold.rectTransform.anchorMax = new Vector2(0f, 1f); tgold.rectTransform.pivot = new Vector2(0f, 0.5f); tgold.rectTransform.sizeDelta = new Vector2(5f, 0f); tgold.rectTransform.anchoredPosition = Vector2.zero;
            _toastText = UiKit.Label(_toast, "", 24, HudStyle.Text1, true, TextAlignmentOptions.Center, "Text"); UiKit.Stretch(_toastText.rectTransform, 22, 0, 16, 0); UiKit.Fit(_toastText, HudMetrics.MinFont);
            _toast.gameObject.SetActive(false);
            return true;
        }

        static Sprite GlassSprite() { return PixFootball.UI.Glass.GlassWidgets.HGradientSprite(); }

        /// <summary>Sizes the banner for its tier and shortens its time on screen (big 3.0 s, slim 1.9 s); a held banner (QA shots pass a very long time) is left alone.</summary>
        void ApplyBannerTier(string text, ref float seconds)
        {
            bool big = IsBigBanner(text);
            _bannerBig = big;
            if (seconds < 20f) seconds = big ? Mathf.Min(seconds, 3.0f) : Mathf.Min(seconds, 1.9f);
            if (big)
            {
                _banner.sizeDelta = new Vector2(820f, 116f);
                _bannerText.fontSize = 60f; UiKit.Fit(_bannerText, 34f); _bannerText.characterSpacing = 4f;
                UiKit.Anchor(_bannerText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(4f, 14f), new Vector2(740f, 70f));
                _bannerSub.fontSize = 24f; UiKit.Fit(_bannerSub, HudMetrics.MinFont);
                UiKit.Anchor(_bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(4f, -34f), new Vector2(740f, 32f));
            }
            else
            {
                _banner.sizeDelta = new Vector2(600f, 78f);
                _bannerText.fontSize = 34f; UiKit.Fit(_bannerText, HudMetrics.MinFont + 4f); _bannerText.characterSpacing = 3f;
                UiKit.Anchor(_bannerText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(4f, 12f), new Vector2(540f, 44f));
                _bannerSub.fontSize = 22f; UiKit.Fit(_bannerSub, HudMetrics.MinFont);
                UiKit.Anchor(_bannerSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(4f, -22f), new Vector2(540f, 28f));
            }
        }

        /// <summary>A banner without a sub-line centres its headline.</summary>
        void CenterBannerText()
        {
            bool none = string.IsNullOrEmpty(_bannerSub.text);
            _bannerSub.gameObject.SetActive(!none);
            var p = _bannerText.rectTransform.anchoredPosition; p.y = none ? 0f : (_bannerBig ? 14f : 12f); _bannerText.rectTransform.anchoredPosition = p;
        }

        /// <summary>Stacks the visible pop-ups in the lane (called every frame; cheap).</summary>
        void RestackPopups()
        {
            float cursor = _bottomActive ? -HudMetrics.PopupTop : -HudMetrics.PopupTop - 150f;   // the top-left scorebug variant keeps the lane clear of its block
            _laneBannerY = cursor;
            if (_banner != null && _banner.gameObject.activeSelf) cursor -= _banner.sizeDelta.y + HudMetrics.Gap;
            cursor = PlaceInLane(_whistle, cursor, new Vector2(0f, 0f));
            if (_bottomActive) cursor = PlaceInLane(_flagStripB, cursor, new Vector2(HudMetrics.PopupMaxWidth, 40f));
            cursor = PlaceInLane(_toast, cursor, new Vector2(560f, 48f));
        }

        float PlaceInLane(RectTransform r, float cursor, Vector2 size)
        {
            if (r == null || !r.gameObject.activeSelf) return cursor;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 1f); r.pivot = new Vector2(0.5f, 1f);
            if (size.x > 0f) r.sizeDelta = size;
            r.anchoredPosition = new Vector2(0f, cursor);
            return cursor - r.sizeDelta.y - HudMetrics.Gap;
        }

        /// <summary>B-077: the wind dial only on the kicking plays (kickoff, punt, field goal / point after).</summary>
        void UpdateWindShown()
        {
            if (_kick == null || _dir == null) return;
            bool kick = _dir.KickingPlay;
            _kick.SetWindShown(kick || _windForced);
        }
        bool _windForced;

        // ============================================================================================ score bar width
        float _barW = -1f;

        /// <summary>The bottom score bar is a centred block no wider than <see cref="HudMetrics.BarMaxWidth"/>, lifted off the screen edge by the safe margin (it used to run edge to edge on a 21:9 canvas).</summary>
        void FitBottomBar()
        {
            if (_bottomRoot == null || _root == null) return;
            float w = HudMetrics.BarWidth(_root.rect.width);
            HudMetrics.SetCanvasHeight(_root.rect.height);
            if (Mathf.Abs(w - _barW) < 0.5f && Mathf.Abs(_bottomRoot.anchoredPosition.y - HudMetrics.BarLift) < 0.5f) return;
            _barW = w;
            _bottomRoot.anchorMin = _bottomRoot.anchorMax = new Vector2(0.5f, 0f); _bottomRoot.pivot = new Vector2(0.5f, 0f);
            _bottomRoot.sizeDelta = new Vector2(w, BarHeight);
            _bottomRoot.anchoredPosition = new Vector2(0f, HudMetrics.BarLift);
            ApplyScorebugScale();   // the pieces stacked above the bar follow its lift
        }
    }
}
