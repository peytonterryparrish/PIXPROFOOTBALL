using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>What a hub tile shows and does (a plain description the hub turns into a glass tile).</summary>
    public sealed class HubTileSpec
    {
        public string Title, Subtitle, Eyebrow, IconKey;
        public Action Click;
        public bool Locked;
        public int Col, Row, ColSpan = 1, RowSpan = 1;
        /// <summary>Hero tiles carry a big picture area above the title (the crests of the last matchup, or a large icon).</summary>
        public bool Hero;
        public Sprite CrestA, CrestB; public string NameA, NameB;
        // ---- Direction 16 additions (v0.11): used by GlassKit.Tile / TileFactory
        /// <summary>Corner badge text (FEATURED, LIVE, NEW, RESUME, LVL 14, +2,500 PX).</summary>
        public string Badge;
        /// <summary>Full-bleed picture (sprite or texture), cropped to cover; the focal point is measured from the top left (0..1).</summary>
        public Sprite ArtSprite; public Texture ArtTexture; public Vector2 ArtFocal = new Vector2(0.5f, 0.5f); public float ArtZoom = 1f;
        /// <summary>Flat icon tile (charcoal gradient, line icon on top) instead of art.</summary>
        public bool Flat;
        /// <summary>Absolute placement inside the parent (top-left origin, y down) and size; Size.x &gt; 0 switches the tile to absolute layout.</summary>
        public Vector2 Pos, Size;
    }

    /// <summary>One built tile: its cell (the reveal host), surface, button (null when locked) and its centre / size in hub block coordinates (y up) for the navigation links.</summary>
    public sealed class HubTile
    {
        public HubTileSpec Spec;
        public RectTransform Cell;
        public GlassGraphic Surface;
        public Button Button;
        public Vector2 Center, Size;
        public bool Locked { get { return Spec.Locked; } }
        public HubNav.Cell NavCell { get { return new HubNav.Cell(Center, Size, !Spec.Locked && Button != null); } }
    }

    /// <summary>Selecting a tile (pad or mouse) tells the hub, which drifts the stage camera.</summary>
    public sealed class HubTileFocus : MonoBehaviour, ISelectHandler
    {
        public Action<HubTile> Focused; public HubTile Tile;
        public void OnSelect(BaseEventData e) { if (Focused != null) Focused(Tile); }
    }

    /// <summary>Builds the glass hub tiles (hero, wide, small and locked "coming soon" variants).</summary>
    public static class HubTileFactory
    {
        public const float CellW = 288f, CellH = 212f, Gap = 20f;
        public static readonly Vector2 BlockSize = new Vector2(4 * CellW + 3 * Gap, 3 * CellH + 2 * Gap);

        public static HubTile Build(RectTransform parent, HubTileSpec s, float revealDelay, Action<HubTile> onFocus)
        {
            var t = GlassTheme.T;
            var tile = new HubTile { Spec = s };
            float w = s.ColSpan * CellW + (s.ColSpan - 1) * Gap, h = s.RowSpan * CellH + (s.RowSpan - 1) * Gap;
            float x = s.Col * (CellW + Gap), y = s.Row * (CellH + Gap);
            tile.Size = new Vector2(w, h); tile.Center = new Vector2(x + w * 0.5f, BlockSize.y - (y + h * 0.5f));

            // the cell hosts the reveal (fade / slide / scale), so the focus scale of the tile itself never fights it
            var cell = UiKit.Rect(parent, "Tile_" + s.Title);
            UiKit.Anchor(cell, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -y), new Vector2(w, h));
            cell.gameObject.AddComponent<CanvasGroup>();
            var rev = cell.gameObject.AddComponent<GlassReveal>(); rev.Delay = revealDelay; rev.FromOffset = new Vector2(0f, -18f); rev.FromScale = 0.97f;
            tile.Cell = cell;

            var surf = GlassKit.Panel(cell, "Surface", s.Locked ? 0.7f : 1.15f, !s.Locked, t.cornerRadiusLarge);
            UiKit.Stretch(surf.rectTransform);
            tile.Surface = surf;
            Color dimCol = s.Locked ? new Color(1f, 1f, 1f, 0.5f) : Color.white;

            bool hero = s.Hero, wide = !hero && w > CellW * 1.5f;
            var surfRt = surf.rectTransform;

            // ---- picture area of the hero tile
            if (hero) BuildHeroPicture(surfRt, s, w, h);

            // ---- icon well
            float iconBox = hero ? 0f : (wide ? 96f : 68f);
            if (!hero && !string.IsNullOrEmpty(s.IconKey))
            {
                var well = GlassKit.Well(surfRt, "IconWell", t.cornerRadiusSmall);
                if (wide) UiKit.Anchor(well.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(26f, 0f), new Vector2(iconBox, iconBox));
                else UiKit.Anchor(well.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(iconBox, iconBox));
                var icon = GlassKit.Icon(well.rectTransform, s.IconKey, iconBox * 0.6f, s.Locked ? t.textMuted : t.lightGrey);
                UiKit.Anchor(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(iconBox * 0.6f, iconBox * 0.6f));
            }
            if (s.Locked)
            {
                var lk = GlassKit.Icon(surfRt, "ui_lock", 36f, t.textSecondary, "Lock");
                UiKit.Anchor(lk.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, -22f), new Vector2(36f, 36f));
            }

            // ---- text
            TextMeshProUGUI title, sub;
            if (hero)
            {
                if (!string.IsNullOrEmpty(s.Eyebrow))
                {
                    var eb = GlassKit.Eyebrow(surfRt, s.Eyebrow, TextAlignmentOptions.BottomLeft, "Eyebrow"); GlassKit.Style(eb, 20f, t.accent, true, 4f);
                    UiKit.Anchor(eb.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32f, 146f), new Vector2(w - 64f, 28f));
                }
                title = GlassKit.Text(surfRt, s.Title, GlassText.Title, TextAlignmentOptions.BottomLeft, "Title"); GlassKit.Style(title, 66f, t.textPrimary, true, 3f);
                UiKit.Anchor(title.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(30f, 68f), new Vector2(w - 60f, 76f)); UiKit.Fit(title, 36f);
                sub = GlassKit.Text(surfRt, s.Subtitle ?? "", GlassText.Body, TextAlignmentOptions.TopLeft, "Sub"); GlassKit.Style(sub, 24f, t.textSecondary, false, 0f);
                UiKit.Anchor(sub.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(32f, 22f), new Vector2(w - 64f, 40f)); UiKit.Fit(sub, 20f);
            }
            else if (wide)
            {
                float tx = 26f + iconBox + 24f;
                title = GlassKit.Text(surfRt, s.Title, GlassText.Heading, TextAlignmentOptions.BottomLeft, "Title"); GlassKit.Style(title, 36f, s.Locked ? t.textSecondary : t.textPrimary, true, 3f);
                UiKit.Anchor(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(tx, 4f), new Vector2(w - tx - 56f, 50f)); UiKit.Fit(title, 24f);
                sub = GlassKit.Text(surfRt, s.Locked ? "COMING SOON" : (s.Subtitle ?? ""), GlassText.Body, TextAlignmentOptions.TopLeft, "Sub");
                GlassKit.Style(sub, s.Locked ? 20f : 24f, t.textSecondary, s.Locked, s.Locked ? 3f : 0f);
                UiKit.Anchor(sub.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(tx, -2f), new Vector2(w - tx - 28f, 56f)); UiKit.Fit(sub, 20f);
                sub.textWrappingMode = TextWrappingModes.Normal;
            }
            else
            {
                title = GlassKit.Text(surfRt, s.Title, GlassText.Heading, TextAlignmentOptions.BottomLeft, "Title"); GlassKit.Style(title, 28f, s.Locked ? t.textSecondary : t.textPrimary, true, 1f);
                UiKit.Anchor(title.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 66f), new Vector2(w - 40f, 42f)); UiKit.Fit(title, 20f);
                sub = GlassKit.Text(surfRt, s.Locked ? "COMING SOON" : (s.Subtitle ?? ""), GlassText.Body, TextAlignmentOptions.TopLeft, "Sub");
                GlassKit.Style(sub, 20f, t.textSecondary, s.Locked, s.Locked ? 3f : 0f);
                UiKit.Anchor(sub.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 16f), new Vector2(w - 44f, 48f));
                sub.textWrappingMode = TextWrappingModes.Normal; sub.overflowMode = TextOverflowModes.Ellipsis;
            }
            if (s.Locked) { var tc = title.color; tc.a = 0.7f; title.color = tc; }
            sub.richText = false;

            // ---- interaction
            if (!s.Locked)
            {
                surf.raycastTarget = true;
                var btn = surf.gameObject.AddComponent<Button>(); btn.targetGraphic = surf; btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(UiAudio.Click);
                if (s.Click != null) { var c = s.Click; btn.onClick.AddListener(() => c()); }
                var f = surf.gameObject.AddComponent<GlassFocusable>();
                f.Surface = surf; f.Label = title; f.FocusScale = hero ? 1.02f : 1.035f; f.LabelNormal = t.textPrimary; f.LabelFocus = Color.white; f.FocusBrighten = 0.05f;
                var ff = surf.gameObject.AddComponent<HubTileFocus>(); ff.Tile = tile; ff.Focused = onFocus;
                tile.Button = btn;
            }
            return tile;
        }

        static void BuildHeroPicture(RectTransform parent, HubTileSpec s, float w, float h)
        {
            var t = GlassTheme.T;
            if (s.CrestA != null || s.CrestB != null)
            {
                float cs = h > 500f ? 190f : 150f, y = 36f;
                float mid = w * 0.5f;
                var a = UiKit.Panel(parent, "CrestAway", Color.white, s.CrestA); a.preserveAspect = true; a.enabled = s.CrestA != null;
                UiKit.Anchor(a.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(mid - cs * 0.5f - 54f, -y), new Vector2(cs, cs));
                var b = UiKit.Panel(parent, "CrestHome", Color.white, s.CrestB); b.preserveAspect = true; b.enabled = s.CrestB != null;
                UiKit.Anchor(b.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(mid + cs * 0.5f + 54f, -y), new Vector2(cs, cs));
                var at = GlassKit.Text(parent, "AT", GlassText.Heading, TextAlignmentOptions.Center, "At"); GlassKit.Style(at, 34f, t.accent, true, 3f);
                UiKit.Anchor(at.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 0.5f), new Vector2(mid, -y - cs * 0.5f), new Vector2(80f, 44f));
                if (!string.IsNullOrEmpty(s.NameA))
                {
                    var na = GlassKit.Eyebrow(parent, s.NameA, TextAlignmentOptions.Center, "NameAway"); GlassKit.Style(na, 20f, t.textSecondary, true, 2f);
                    UiKit.Anchor(na.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(mid - cs * 0.5f - 54f, -y - cs - 6f), new Vector2(cs + 70f, 28f)); UiKit.Fit(na, 20f);
                    var nb = GlassKit.Eyebrow(parent, s.NameB, TextAlignmentOptions.Center, "NameHome"); GlassKit.Style(nb, 20f, t.textSecondary, true, 2f);
                    UiKit.Anchor(nb.rectTransform, new Vector2(0f, 1f), new Vector2(0.5f, 1f), new Vector2(mid + cs * 0.5f + 54f, -y - cs - 6f), new Vector2(cs + 70f, 28f)); UiKit.Fit(nb, 20f);
                }
            }
            else if (!string.IsNullOrEmpty(s.IconKey))
            {
                float isz = h > 500f ? 210f : 150f;
                var icon = GlassKit.Icon(parent, s.IconKey, isz, new Color(t.lightGrey.r, t.lightGrey.g, t.lightGrey.b, 0.85f), "HeroIcon");
                UiKit.Anchor(icon.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(h > 500f ? 90f : 44f)), new Vector2(isz, isz));
            }
        }
    }
}
