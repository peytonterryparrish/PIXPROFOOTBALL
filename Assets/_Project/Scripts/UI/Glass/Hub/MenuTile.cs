using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>The picture of an art tile: one or two crests over a wash of the team colours (all ours: no photos, no real marks).</summary>
    public sealed class TileArt
    {
        public Sprite CrestA, CrestB;
        public Color ColorA = new Color(0.16f, 0.17f, 0.2f, 1f), ColorB = new Color(0.16f, 0.17f, 0.2f, 1f);
        public string NameA, NameB;
        /// <summary>Crests of the info panel (power rankings).</summary>
        public List<Sprite> Strip;
        public List<string> StripNames;
    }

    /// <summary>One built hub tile: its cell (the reveal host), surface, button (null when locked or not selectable) and its centre / size in block coordinates (y up) for the navigation links.</summary>
    public sealed class MenuTile
    {
        public PlanTile Plan;
        public RectTransform Cell;
        public GlassGraphic Surface;
        public Button Button;
        public Vector2 Center, Size;
        public bool Locked { get { return Plan.Locked; } }
        public HubNav.Cell NavCell { get { return new HubNav.Cell(Center, Size, Plan.Selectable && Button != null); } }
    }

    /// <summary>Selecting a tile (pad or mouse) tells the hub, which drifts the stage camera.</summary>
    public sealed class MenuTileFocus : MonoBehaviour, ISelectHandler
    {
        public Action<MenuTile> Focused; public MenuTile Tile;
        public void OnSelect(BaseEventData e) { if (Focused != null) Focused(Tile); }
    }

    /// <summary>
    /// Builds the tiles of direction 16 from a <see cref="PlanTile"/>: art tiles (picture, shade from the bottom, title and sub on the shade, gold badge top right), flat icon tiles, wide icon tiles, locked
    /// "COMING SOON" tiles and the information panel. Everything reads the theme tokens (gold, square corners and Barlow in the Tile theme) and the types stay at 20 units or more.
    /// </summary>
    public static class MenuTileBuilder
    {
        static Sprite _top;
        /// <summary>The vertical gradient flipped so it is opaque at the top (the art wash).</summary>
        static Sprite TopGradient()
        {
            if (_top != null) return _top;
            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 64; y++) { float a = y / 63f; a = a * a * (3f - 2f * a); tex.SetPixel(0, y, new Color(1f, 1f, 1f, a)); }
            tex.Apply();
            _top = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f), 100f);
            _top.hideFlags = HideFlags.HideAndDontSave;
            return _top;
        }

        public static MenuTile Build(RectTransform parent, PlanTile p, TileArt art, float revealDelay, Action<MenuTile> onFocus, Action click)
        {
            var t = GlassTheme.T;
            var tile = new MenuTile { Plan = p };
            tile.Size = new Vector2(p.W, p.H); tile.Center = p.Center(MenuHubPlan.BlockSize.y);

            // the cell hosts the reveal (fade / rise / scale), so the focus scale of the tile itself never fights it
            var cell = UiKit.Rect(parent, "Tile_" + p.Id);
            UiKit.Anchor(cell, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(p.X, -p.Y), new Vector2(p.W, p.H));
            cell.gameObject.AddComponent<CanvasGroup>();
            var rev = cell.gameObject.AddComponent<GlassReveal>(); rev.Delay = revealDelay; rev.FromOffset = new Vector2(0f, -18f); rev.FromScale = 0.97f;
            tile.Cell = cell;

            bool info = p.Look == PlanLook.Info;
            var surf = GlassKit.Panel(cell, "Surface", p.Locked ? 0.7f : 1.15f, !p.Locked && !info, t.cornerRadiusLarge);
            UiKit.Stretch(surf.rectTransform);
            tile.Surface = surf;
            var sr = surf.rectTransform;

            switch (p.Look)
            {
                case PlanLook.Art: BuildArt(sr, p, art); break;
                case PlanLook.Wide: BuildWide(sr, p); break;
                case PlanLook.Info: BuildInfo(sr, p, art); break;
                default: BuildFlat(sr, p); break;
            }
            if (!string.IsNullOrEmpty(p.Badge) && !p.Locked) Badge(sr, p.Badge);
            if (p.Locked)
            {
                var lk = GlassKit.Icon(sr, "ui_lock", 36f, t.textSecondary, "Lock");
                UiKit.Anchor(lk.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-22f, -22f), new Vector2(36f, 36f));
            }

            // Tile look: the border / ring / glow / sheen is a Frame surface on top of the content (the click target); Glass keeps the surface itself
            GlassGraphic frame = null;
            if (t.flat)
            {
                frame = GlassKit.Surface(sr, "Frame", GlassKind.Frame, 0f, false, 1f);
                UiKit.Stretch(frame.rectTransform); frame.EdgeWidth = t.restEdge; frame.FocusEdge = t.focusEdge;
                if (p.Locked) { var fc = frame.color; fc.a = 0.7f; frame.color = fc; }
                tile.Surface = frame;
            }

            // ---- interaction (locked tiles and the information panel take no focus)
            if (!p.Locked && !info)
            {
                surf.raycastTarget = frame == null; if (frame != null) frame.raycastTarget = true;
                var btn = surf.gameObject.AddComponent<Button>(); btn.targetGraphic = frame != null ? (Graphic)frame : surf; btn.transition = Selectable.Transition.None;
                btn.onClick.AddListener(UiAudio.Click);
                if (click != null) btn.onClick.AddListener(() => click());
                var f = surf.gameObject.AddComponent<GlassFocusable>();
                f.Surface = frame != null ? frame : surf; f.Label = frame != null ? null : _lastTitle; f.KeepLabelColor = frame != null; f.RaiseTarget = frame != null ? cell : null;
                f.FocusScale = p.Hero ? (t.flat ? t.heroScale : 1.02f) : (t.flat ? t.tileScale : 1.035f); f.LabelNormal = t.textPrimary; f.LabelFocus = Color.white; f.FocusBrighten = frame != null ? 0f : 0.05f;
                var ff = surf.gameObject.AddComponent<MenuTileFocus>(); ff.Tile = tile; ff.Focused = onFocus;
                tile.Button = btn;
            }
            return tile;
        }

        static TextMeshProUGUI _lastTitle;

        // ------------------------------------------------------------------ text helpers
        static TextMeshProUGUI Title(RectTransform parent, string text, float size, bool locked, float x, float y, float w, float h, bool wrap = false)
        {
            var t = GlassTheme.T;
            var tx = GlassKit.Text(parent, text, GlassText.Title, TextAlignmentOptions.BottomLeft, "Title");
            GlassKit.Style(tx, size, locked ? t.textSecondary : t.textPrimary, true, 1f);
            UiKit.Anchor(tx.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, y), new Vector2(w, h));
            tx.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            UiKit.Fit(tx, 24f);
            if (locked) { var c = tx.color; c.a = 0.75f; tx.color = c; }
            _lastTitle = tx;
            return tx;
        }

        static TextMeshProUGUI Sub(RectTransform parent, string text, bool locked, float x, float y, float w, float h)
        {
            var t = GlassTheme.T;
            var tx = GlassKit.Text(parent, locked ? "COMING SOON" : (text ?? ""), GlassText.Body, TextAlignmentOptions.TopLeft, "Sub");
            GlassKit.Style(tx, 20f, locked ? t.textMuted : t.textSecondary, locked, locked ? 3f : 1f);
            UiKit.Anchor(tx.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(x, y), new Vector2(w, h));
            tx.textWrappingMode = TextWrappingModes.Normal; tx.overflowMode = TextOverflowModes.Ellipsis; tx.richText = false;
            return tx;
        }

        static void Badge(RectTransform parent, string text)
        {
            var t = GlassTheme.T;
            float w = text.Length * 15f + 36f;
            var plate = GlassKit.Solid(parent, "Badge", t.accent, 0f);
            UiKit.Anchor(plate.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -18f), new Vector2(w, 36f));
            var tx = GlassKit.Text(plate.rectTransform, text, GlassText.Caption, TextAlignmentOptions.Center, "BadgeText");
            GlassKit.Style(tx, 20f, t.textOnAction, true, 3f); UiKit.Stretch(tx.rectTransform, 4f, 0f, 4f, 0f);
            tx.textWrappingMode = TextWrappingModes.NoWrap;
        }

        // ------------------------------------------------------------------ looks
        static void BuildArt(RectTransform sr, PlanTile p, TileArt art)
        {
            var t = GlassTheme.T;
            art = art ?? new TileArt();
            // picture area: a wash of the team colour(s), opaque at the top and clear towards the shade
            var wash = UiKit.Panel(sr, "ArtWash", Color.white, TopGradient()); UiKit.Stretch(wash.rectTransform, 2f, 2f, 2f, 2f); wash.raycastTarget = false;
            var ca = art.ColorA; ca.a = 0.62f; wash.color = ca;
            if (art.CrestB != null)
            {
                var wb = UiKit.Panel(sr, "ArtWashB", Color.white, GlassWidgets.HGradientSprite()); UiKit.Stretch(wb.rectTransform, 2f, 2f, 2f, 2f); wb.raycastTarget = false;
                wb.rectTransform.localScale = new Vector3(-1f, 1f, 1f);
                var cb = art.ColorB; cb.a = 0.42f; wb.color = cb;
            }
            // crests
            float cs = Mathf.Min(p.H * (p.Hero ? 0.42f : 0.36f), p.W * 0.34f);
            float cy = -(p.H * (p.Hero ? 0.10f : 0.2f));
            if (art.CrestA != null && art.CrestB != null)
            {
                Crest(sr, art.CrestA, -cs * 0.58f - p.W * 0.03f, cy, cs, "CrestAway");
                Crest(sr, art.CrestB, cs * 0.58f + p.W * 0.03f, cy, cs, "CrestHome");
                var at = GlassKit.Text(sr, "AT", GlassText.Heading, TextAlignmentOptions.Center, "At"); GlassKit.Style(at, 30f, t.accent, true, 3f);
                UiKit.Anchor(at.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f), new Vector2(0f, cy - cs * 0.5f), new Vector2(80f, 40f));
            }
            else if (art.CrestA != null) Crest(sr, art.CrestA, 0f, cy, cs * 1.2f, "Crest");
            else if (!string.IsNullOrEmpty(p.Icon))
            {
                float isz = Mathf.Min(p.H * 0.4f, 190f);
                var ic = GlassKit.Icon(sr, p.Icon, isz, new Color(t.lightGrey.r, t.lightGrey.g, t.lightGrey.b, 0.85f), "ArtIcon");
                UiKit.Anchor(ic.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(p.H * 0.12f)), new Vector2(isz, isz));
            }
            // shade from the bottom
            var shade = UiKit.Panel(sr, "Shade", new Color(0.04f, 0.045f, 0.055f, 0.94f), GlassWidgets.GradientSprite()); shade.raycastTarget = false;
            UiKit.Anchor(shade.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(2f, 2f), new Vector2(p.W - 4f, p.H * 0.62f));
            shade.rectTransform.anchorMax = new Vector2(1f, 0f); shade.rectTransform.sizeDelta = new Vector2(-4f, p.H * 0.62f);
            if (art.NameA != null && art.NameB != null && p.Hero)
            {
                Names(sr, art.NameA, art.NameB, cs, cy);
            }
            float pad = p.Hero ? 34f : 24f;
            float ts = p.Hero ? 62f : 38f, th = p.Hero ? 76f : 48f;
            Title(sr, p.Title, ts, false, pad, p.Hero ? 62f : 72f, p.W - pad * 2f, th);
            Sub(sr, p.Sub, false, pad, 12f, p.W - pad * 2f, p.Hero ? 44f : 58f);
        }

        static void Names(RectTransform sr, string a, string b, float cs, float cy)
        {
            var t = GlassTheme.T;
            float off = cs * 0.58f + 0f;
            var na = GlassKit.Eyebrow(sr, a, TextAlignmentOptions.Center, "NameAway"); GlassKit.Style(na, 20f, t.textSecondary, true, 2f);
            UiKit.Anchor(na.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-off - 8f, cy - cs - 4f), new Vector2(cs + 120f, 28f)); UiKit.Fit(na, 20f);
            var nb = GlassKit.Eyebrow(sr, b, TextAlignmentOptions.Center, "NameHome"); GlassKit.Style(nb, 20f, t.textSecondary, true, 2f);
            UiKit.Anchor(nb.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(off + 8f, cy - cs - 4f), new Vector2(cs + 120f, 28f)); UiKit.Fit(nb, 20f);
        }

        static void Crest(RectTransform sr, Sprite sp, float xFromCenter, float y, float size, string name)
        {
            var im = UiKit.Panel(sr, name, Color.white, sp); im.preserveAspect = true; im.raycastTarget = false;
            UiKit.Anchor(im.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(xFromCenter, y), new Vector2(size, size));
        }

        static void BuildFlat(RectTransform sr, PlanTile p)
        {
            var t = GlassTheme.T;
            if (!string.IsNullOrEmpty(p.Icon))
            {
                float isz = p.W >= 700f ? 180f : (p.W < 260f ? 84f : 100f);
                var ic = GlassKit.Icon(sr, p.Icon, isz, p.Locked ? t.textMuted : t.accent, "Icon");
                UiKit.Anchor(ic.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -(p.H * (p.H > 500f ? 0.28f : 0.16f))), new Vector2(isz, isz));
            }
            bool small = p.W < 260f;
            float pad = small ? 22f : 28f;
            Title(sr, p.Title, small ? 34f : (p.Hero ? 56f : 38f), p.Locked, pad, small ? 64f : 62f, p.W - pad * 2f, small ? 76f : 48f, small);
            Sub(sr, p.Sub, p.Locked, pad, 14f, p.W - pad * 2f, small ? 44f : 44f);
        }

        static void BuildWide(RectTransform sr, PlanTile p)
        {
            var t = GlassTheme.T;
            float isz = Mathf.Min(p.H - 48f, 84f);
            if (!string.IsNullOrEmpty(p.Icon))
            {
                var ic = GlassKit.Icon(sr, p.Icon, isz, p.Locked ? t.textMuted : t.accent, "Icon");
                UiKit.Anchor(ic.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f, 0f), new Vector2(isz, isz));
            }
            float tx = 28f + isz + 24f;
            var title = GlassKit.Text(sr, p.Title, GlassText.Title, TextAlignmentOptions.BottomLeft, "Title");
            GlassKit.Style(title, 36f, p.Locked ? t.textSecondary : t.textPrimary, true, 1f);
            UiKit.Anchor(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(tx, 2f), new Vector2(p.W - tx - 70f, 46f)); UiKit.Fit(title, 24f);
            title.textWrappingMode = TextWrappingModes.NoWrap;
            var c = title.color; if (p.Locked) { c.a = 0.75f; title.color = c; }
            _lastTitle = title;
            var sub = GlassKit.Text(sr, p.Locked ? "COMING SOON" : (p.Sub ?? ""), GlassText.Body, TextAlignmentOptions.TopLeft, "Sub");
            GlassKit.Style(sub, 20f, p.Locked ? t.textMuted : t.textSecondary, p.Locked, p.Locked ? 3f : 1f);
            UiKit.Anchor(sub.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 1f), new Vector2(tx, -2f), new Vector2(p.W - tx - 24f, 34f));
            sub.textWrappingMode = TextWrappingModes.NoWrap; sub.richText = false;
        }

        static void BuildInfo(RectTransform sr, PlanTile p, TileArt art)
        {
            var t = GlassTheme.T;
            // a row of crest boxes (the four best teams), then the caption at the bottom
            int n = art != null && art.Strip != null ? Mathf.Min(4, art.Strip.Count) : 0;
            float box = 92f, gap = 16f, x0 = (p.W - (n * box + (n - 1) * gap)) * 0.5f;
            for (int i = 0; i < n; i++)
            {
                var well = GlassKit.Well(sr, "CrestBox" + i, 0f);
                UiKit.Anchor(well.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x0 + i * (box + gap), -26f), new Vector2(box, box));
                var im = UiKit.Panel(well.rectTransform, "Crest", Color.white, art.Strip[i]); im.preserveAspect = true; im.raycastTarget = false;
                UiKit.Anchor(im.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(box - 16f, box - 16f));
                if (art.StripNames != null && i < art.StripNames.Count)
                {
                    var rk = GlassKit.Text(well.rectTransform, (i + 1).ToString(), GlassText.Caption, TextAlignmentOptions.TopLeft, "Rank"); GlassKit.Style(rk, 20f, t.accent, true, 0f);
                    UiKit.Anchor(rk.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(6f, -2f), new Vector2(30f, 26f));
                }
            }
            Title(sr, p.Title, 34f, false, 28f, 60f, p.W - 56f, 44f);
            Sub(sr, p.Sub, false, 28f, 14f, p.W - 56f, 40f);
        }
    }
}
