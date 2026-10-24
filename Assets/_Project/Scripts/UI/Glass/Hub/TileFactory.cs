using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// Builds a Direction 16 tile ("Tile Hub Modern"): charcoal body with a 2 unit border, full-bleed art with the bottom shade, 38 unit title and 20 unit sub at the bottom, a corner badge, a flat icon variant and the
    /// locked "COMING SOON" variant. Focus = 4 unit gold border + 2 unit ring + 46 unit glow + gold sheen (the Frame surface on top) and a 1.035 scale (hero 1.02), raised above its neighbours. Used through
    /// <see cref="GlassKit.Tile"/>; every size of the hub is in <see cref="TileMetrics"/>.
    /// </summary>
    public static class TileFactory
    {
        public static HubTile Build(RectTransform parent, HubTileSpec s, float revealDelay, Action<HubTile> onFocus)
        {
            var t = GlassTheme.T;
            var tile = new HubTile { Spec = s };
            float w = s.Size.x, h = s.Size.y;
            tile.Size = s.Size; tile.Center = new Vector2(s.Pos.x + w * 0.5f, -(s.Pos.y + h * 0.5f));
            var cell = UiKit.Rect(parent, "Tile_" + s.Title);
            UiKit.Anchor(cell, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(s.Pos.x, -s.Pos.y), new Vector2(w, h));
            cell.pivot = new Vector2(0f, 1f); cell.anchoredPosition = new Vector2(s.Pos.x, -s.Pos.y);
            cell.gameObject.AddComponent<CanvasGroup>();
            var rev = cell.gameObject.AddComponent<GlassReveal>(); rev.Delay = revealDelay; rev.FromOffset = new Vector2(0f, -t.revealRise); rev.FromScale = t.revealScale;
            tile.Cell = cell;
            var body = UiKit.Rect(cell, "Body"); UiKit.Stretch(body);

            bool art = s.ArtSprite != null || s.ArtTexture != null;
            var fill = GlassKit.Surface(body, "Fill", GlassKind.Glass, 0f, !s.Locked, s.Locked ? 0.7f : 1.15f);
            UiKit.Stretch(fill.rectTransform);
            tile.Surface = fill;

            if (art && !s.Locked)
            {
                var clip = UiKit.Rect(body, "ArtClip"); UiKit.Stretch(clip, 2f, 2f, 2f, 2f); clip.gameObject.AddComponent<RectMask2D>();
                GlassKit.ArtCover(clip, s.ArtSprite, s.ArtTexture, w - 4f, h - 4f, s.ArtFocal, s.ArtZoom, "Art");
                var shade = GlassKit.Plate(clip, "Shade", Color.white, GlassKit.ShadeSprite()); UiKit.Stretch(shade.rectTransform);
            }
            else if (!string.IsNullOrEmpty(s.IconKey) && !s.Hero)
            {
                float isz = Mathf.Clamp(Mathf.Min(w, h) * 0.34f, 64f, 120f);
                var icon = GlassKit.Icon(body, s.IconKey, isz, s.Locked ? t.textMuted : new Color(t.goldHi.r, t.goldHi.g, t.goldHi.b, 0.95f), "Icon");
                UiKit.Anchor(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -Mathf.Max(30f, h * 0.15f)), new Vector2(isz, isz));
            }

            // ---- text
            float tpad = s.Hero ? 30f : 22f;
            float tsize = s.Hero ? t.sizeHeroTitle : t.sizeTileTitle;
            var title = GlassKit.Text(body, (s.Title ?? "").ToUpperInvariant(), GlassText.Title, TextAlignmentOptions.BottomLeft, "Title");
            GlassKit.Style(title, tsize, s.Locked ? t.textSecondary : t.textPrimary, true, GlassTokens.Em(1f, tsize));
            title.textWrappingMode = TextWrappingModes.Normal;
            float subH = 28f, subY = s.Hero ? 14f : 10f;
            UiKit.Anchor(title.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(tpad, subY + subH + 4f), new Vector2(w - tpad * 2f, tsize * 2.15f));
            UiKit.Fit(title, tsize * 0.62f); title.textWrappingMode = TextWrappingModes.Normal;
            if (s.Locked) { var tc = title.color; tc.a = 0.7f; title.color = tc; }
            var sub = GlassKit.Text(body, s.Locked ? "COMING SOON" : (s.Subtitle ?? ""), GlassText.Body, TextAlignmentOptions.BottomLeft, "Sub");
            GlassKit.Style(sub, t.sizeSub, s.Locked ? t.textMuted : t.textSecondary, s.Locked, s.Locked ? GlassTokens.Em(3f, t.sizeSub) : GlassTokens.Em(1f, t.sizeSub));
            UiKit.Anchor(sub.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(tpad, subY), new Vector2(w - tpad * 2f, subH)); UiKit.Fit(sub, t.sizeSub);
            sub.richText = false;

            if (!string.IsNullOrEmpty(s.Badge) && !s.Locked)
            {
                var badge = GlassWidgets.Chip(body, s.Badge, true, 32f, t.sizeBadge);
                UiKit.Anchor(badge.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -14f), badge.rectTransform.sizeDelta);
            }
            if (s.Locked)
            {
                var lk = GlassKit.Icon(body, "ui_lock", 36f, t.textSecondary, "Lock");
                UiKit.Anchor(lk.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(36f, 36f));
            }

            // ---- frame (border, ring, glow, sheen) on top; it is the click target
            var frame = GlassKit.Surface(body, "Frame", GlassKind.Frame, 0f, false, 1f);
            UiKit.Stretch(frame.rectTransform); frame.EdgeWidth = t.restEdge; frame.FocusEdge = t.focusEdge;
            if (s.Locked) { var fc = frame.color; fc.a = 0.7f; frame.color = fc; }
            if (!s.Locked)
            {
                frame.raycastTarget = true;
                var btn = body.gameObject.AddComponent<Button>(); btn.targetGraphic = frame; btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(UiAudio.Click);
                if (s.Click != null) { var c = s.Click; btn.onClick.AddListener(() => c()); }
                var f = body.gameObject.AddComponent<GlassFocusable>();
                f.Surface = frame; f.Label = null; f.KeepLabelColor = true; f.FocusScale = s.Hero ? t.heroScale : t.tileScale; f.FocusBrighten = 0f; f.RaiseTarget = cell;
                var ff = body.gameObject.AddComponent<HubTileFocus>(); ff.Tile = tile; ff.Focused = onFocus;
                tile.Button = btn;
            }
            return tile;
        }
    }

    public static partial class GlassKit
    {
        /// <summary>
        /// A Direction 16 hub tile with explicit size and position in <paramref name="parent"/> (set <see cref="HubTileSpec.Size"/> and <see cref="HubTileSpec.Pos"/>; sizes are in <see cref="TileMetrics"/>). The
        /// Glass look gets the grid tile of the v0.7 hub instead (Col / Row / ColSpan / RowSpan).
        /// </summary>
        public static HubTile Tile(RectTransform parent, HubTileSpec spec, float revealDelay = 0f, Action<HubTile> onFocus = null)
        {
            if (!T.flat) return HubTileFactory.Build(parent, spec, revealDelay, onFocus);
            if (spec.Size.x <= 0f) spec.Size = TileMetrics.Standard;
            return TileFactory.Build(parent, spec, revealDelay, onFocus);
        }
    }
}
