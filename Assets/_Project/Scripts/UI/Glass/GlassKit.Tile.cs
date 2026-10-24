using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>Sizes and bands of Direction 16 "Tile Hub Modern" in canvas units (1920 x 1080 reference). Screens place their tiles and panels with these numbers.</summary>
    public static class TileMetrics
    {
        // hub tile sizes (SPEC section 1)
        public static readonly Vector2 Hero = new Vector2(780f, 470f), Standard = new Vector2(380f, 292f), Wide = new Vector2(480f, 290f), WideShort = new Vector2(480f, 162f),
            HalfTall = new Vector2(230f, 288f), Half = new Vector2(240f, 238f), Large = new Vector2(500f, 250f), LargeShort = new Vector2(500f, 252f);
        // bands
        public const float TopBar = 112f, TabHeight = 54f, Ticker = 46f, Footer = 62f;
        public const float MarginX = 60f, ContentTop = 138f, TileGutter = 20f, CardGutter = 32f;
        /// <summary>Distance from the bottom of the screen to the top of the ticker band: content stays above it (46 + 62).</summary>
        public const float BottomClear = Ticker + Footer;
        // list / table
        public const float ListRow = 124f, TableRow = 50f, TableHead = 56f;
        // play-call strip
        public const float StripTop = 640f, PlayCardW = 406f, PlayCardH = 286f, SelectedPanelW = 498f, SideListW = 480f;
        /// <summary>The usable content rectangle between the top bar and the ticker, as offsets from the screen edges (left, bottom, right, top) for <see cref="UiKit.Stretch"/>.</summary>
        public static Vector4 ContentInsets { get { return new Vector4(MarginX, BottomClear + 14f, MarginX, ContentTop - 2f); } }
    }

    /// <summary>Category tag colours of a play chip.</summary>
    public enum TagKind { Pass, Run, Info }

    /// <summary>A list row (Direction 16): rest fill, focus fill with the 4 unit gold bar, optional thumbnail slot (play diagram), tag chip, title and sub lines. Pinned = selected without focus.</summary>
    public sealed class GlassListRow : MonoBehaviour
    {
        public Button Button; public GlassGraphic Surface; public GlassFocusable Focus;
        public RectTransform Thumb;
        public TextMeshProUGUI Title, Sub;
        public GlassGraphic Tag; public TextMeshProUGUI TagText;
        /// <summary>Selected = the focus look stays on while another control has the focus.</summary>
        public bool Selected { get { return Focus != null && Focus.Pinned; } set { if (Focus != null) Focus.Pinned = value; } }
        /// <summary>Takes the row out of any layout group of its parent (manual placement).</summary>
        public void LayoutOff() { var le = GetComponent<LayoutElement>(); if (le != null) le.ignoreLayout = true; }

        public void SetTag(string text, TagKind kind)
        {
            if (Tag == null) return;
            bool on = !string.IsNullOrEmpty(text);
            Tag.gameObject.SetActive(on);
            if (!on) return;
            var t = GlassTheme.T;
            Tag.color = kind == TagKind.Pass ? t.tagPass : kind == TagKind.Run ? t.tagRun : t.textSecondary;
            TagText.text = text.ToUpperInvariant();
            float w = TagText.GetPreferredValues(TagText.text).x + 24f;
            Tag.rectTransform.sizeDelta = new Vector2(w, Tag.rectTransform.sizeDelta.y);
        }
    }

    /// <summary>Fades and drifts a content panel in when the tab changes (0.20 s, 24 units in the LB / RB direction), on unscaled time.</summary>
    public sealed class TileCrossfade : MonoBehaviour
    {
        CanvasGroup _cg; RectTransform _rt; Vector2 _rest; bool _haveRest; float _t = 1f, _dir;

        public static void Play(RectTransform content, int direction)
        {
            if (content == null) return;
            var c = UiKit.GetOrAdd<TileCrossfade>(content.gameObject);
            c.Begin(direction);
        }

        void Begin(int dir)
        {
            if (_cg == null) _cg = UiKit.GetOrAdd<CanvasGroup>(gameObject);
            _rt = (RectTransform)transform;
            if (!_haveRest) { _rest = _rt.anchoredPosition; _haveRest = true; }
            _dir = dir == 0 ? 0f : Mathf.Sign(dir); _t = 0f; Apply(0f);
        }

        void Update()
        {
            if (_t >= 1f) return;
            _t += Time.unscaledDeltaTime / Mathf.Max(0.02f, GlassTheme.T.crossfade);
            Apply(Mathf.Clamp01(_t));
        }

        void OnDisable() { if (_haveRest && _rt != null) { _t = 1f; Apply(1f); } }

        void Apply(float u)
        {
            float e = 1f - (1f - u) * (1f - u);
            _cg.alpha = e;
            _rt.anchoredPosition = _rest + new Vector2(_dir * GlassTheme.T.crossDrift * (1f - e), 0f);
        }
    }

    public static partial class GlassKit
    {
        // ================================================================================== helpers
        /// <summary>An explicit corner radius of the Glass look; the Tile look is square (0).</summary>
        public static float Rad(float r) { return T.flat ? 0f : r; }

        /// <summary>Option-row surface: Tile draws the list-row fill with the gold focus bar, Glass keeps its glass.</summary>
        public static void RowStyle(GlassGraphic g)
        {
            if (!T.flat) return;
            g.Kind = GlassKind.Row; g.EdgeWidth = 1f; g.Radius = 0f; g.FocusEdge = 4f;
        }

        static readonly Dictionary<long, Sprite> _vgrad = new Dictionary<long, Sprite>();

        /// <summary>A 1 x 64 vertical gradient sprite from <paramref name="top"/> to <paramref name="bottom"/> (cached; use with a white Image).</summary>
        public static Sprite VGradient(Color top, Color bottom)
        {
            long key = ((long)ColorUtility.ToHtmlStringRGBA(top).GetHashCode() << 32) ^ (uint)ColorUtility.ToHtmlStringRGBA(bottom).GetHashCode();
            Sprite s;
            if (_vgrad.TryGetValue(key, out s) && s != null) return s;
            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 64; y++) tex.SetPixel(0, y, Color.Lerp(bottom, top, y / 63f));   // row 0 is the bottom
            tex.Apply();
            s = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f), 100f); s.hideFlags = HideFlags.HideAndDontSave;
            _vgrad[key] = s;
            return s;
        }

        static Sprite _shade;
        /// <summary>The tile shade: bottom-up #0A0B0E at 92% / 35% / 5% at 0, 45% and 75% of the height, clear above (keeps titles legible over art).</summary>
        public static Sprite ShadeSprite()
        {
            if (_shade != null) return _shade;
            var t = GlassTheme.T;
            var tex = new Texture2D(1, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 128; y++)
            {
                float u = y / 127f;   // 0 = bottom
                float a = u < 0.45f ? Mathf.Lerp(t.shadeAlpha0, t.shadeAlpha45, u / 0.45f) : u < 0.75f ? Mathf.Lerp(t.shadeAlpha45, t.shadeAlpha75, (u - 0.45f) / 0.30f) : Mathf.Lerp(t.shadeAlpha75, 0f, (u - 0.75f) / 0.25f);
                tex.SetPixel(0, y, new Color(t.shade.r, t.shade.g, t.shade.b, a));
            }
            tex.Apply();
            _shade = Sprite.Create(tex, new Rect(0, 0, 1, 128), new Vector2(0.5f, 0.5f), 100f); _shade.hideFlags = HideFlags.HideAndDontSave;
            return _shade;
        }

        static Sprite _radial;
        static Sprite RadialSprite()
        {
            if (_radial != null) return _radial;
            const int w = 128, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float dx = (x + 0.5f) / w * 2f - 1f, dy = (y + 0.5f) / h * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(1f - r / 0.7f); a = a * a * (3f - 2f * a);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            _radial = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f); _radial.hideFlags = HideFlags.HideAndDontSave;
            return _radial;
        }

        /// <summary>The Tile screen backdrop: linear #17181C to #101114 with the soft radial #2A2D34 at the top. Stretched, full-bleed, first child of the screen.</summary>
        public static RectTransform TileBackdrop(Transform parent, string name = "TileBackdrop")
        {
            var t = T;
            var root = UiKit.Rect(parent, name); UiKit.Stretch(root);
            var g = GlassKit.Plate(root, "Gradient", Color.white, VGradient(t.bgTop, t.bgBottom)); UiKit.Stretch(g.rectTransform);
            var glow = GlassKit.Plate(root, "Glow", t.bgGlow, RadialSprite());
            glow.rectTransform.anchorMin = new Vector2(-0.3f, -0.2f); glow.rectTransform.anchorMax = new Vector2(1.3f, 1.2f); glow.rectTransform.offsetMin = glow.rectTransform.offsetMax = Vector2.zero;
            return root;
        }

        /// <summary>Picture that covers its rectangle (aspect-fill, cropped by the parent mask) around a focal point; returns the Graphic. Pass a sprite or a texture.</summary>
        public static Graphic ArtCover(RectTransform parent, Sprite sprite, Texture texture, float width, float height, Vector2 focal, float zoom = 1f, string name = "Art")
        {
            float sw = sprite != null ? sprite.rect.width : texture != null ? texture.width : 0f, sh = sprite != null ? sprite.rect.height : texture != null ? texture.height : 0f;
            var rt = UiKit.Rect(parent, name);
            Graphic g;
            if (sprite != null) { var im = rt.gameObject.AddComponent<Image>(); im.sprite = sprite; im.preserveAspect = false; g = im; }
            else { var ri = rt.gameObject.AddComponent<RawImage>(); ri.texture = texture; g = ri; }
            g.raycastTarget = false;
            if (sw < 1f || sh < 1f) { UiKit.Stretch(rt); return g; }
            float k = Mathf.Max(width / sw, height / sh) * Mathf.Max(1f, zoom);
            float iw = sw * k, ih = sh * k;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f); rt.pivot = new Vector2(0.5f, 0.5f); rt.sizeDelta = new Vector2(iw, ih);
            float ox = Mathf.Clamp((0.5f - focal.x) * iw, -(iw - width) * 0.5f, (iw - width) * 0.5f);
            float oy = Mathf.Clamp((focal.y - 0.5f) * ih, -(ih - height) * 0.5f, (ih - height) * 0.5f);   // focal.y is measured from the top
            rt.anchoredPosition = new Vector2(ox, oy);
            return g;
        }

        // ================================================================================== glyph legend pieces
        /// <summary>Controller face button: a coloured disc with the letter (A green, B red, X blue, Y gold with dark text).</summary>
        public static RectTransform GlyphDisc(Transform parent, string letter, float size)
        {
            var t = T;
            Color fill = letter == "A" ? t.glyphA : letter == "B" ? t.glyphB : letter == "X" ? t.glyphX : t.glyphY;
            Color text = letter == "Y" ? t.onGold : Color.white;
            var d = Solid(parent, "Disc_" + letter, fill, size * 0.5f, 0f);
            d.rectTransform.sizeDelta = new Vector2(size, size);
            var tx = Text(d.rectTransform, letter, GlassText.Caption, TextAlignmentOptions.Center, "Letter");
            Style(tx, Mathf.Max(20f, size * 0.5f), text, true, 0f); UiKit.Stretch(tx.rectTransform); tx.rectTransform.offsetMin = new Vector2(0f, 1f);
            return d.rectTransform;
        }

        /// <summary>Grey key cap (LB / RB / LT / RT and keyboard keys): vertical #777 to #3C3C3C with a lighter rim.</summary>
        public static RectTransform GlyphCap(Transform parent, string label, float height)
        {
            var t = T;
            var k = Surface(parent, "Cap_" + label, GlassKind.Key, 7f, false, 1f); k.EdgeWidth = 1f;
            var tx = Text(k.rectTransform, label, GlassText.Caption, TextAlignmentOptions.Center, "Key");
            Style(tx, Mathf.Max(20f, height * 0.58f), t.glyphKeyText, true, GlassTokens.Em(1f, 20f));
            float w = Mathf.Max(height * 1.35f, tx.GetPreferredValues(label).x + 22f);
            k.rectTransform.sizeDelta = new Vector2(w, height);
            UiKit.Stretch(tx.rectTransform);
            return k.rectTransform;
        }

        // ================================================================================== tabs
        /// <summary>
        /// The skewed tab bar of Direction 16 (use <see cref="TabBar"/> for the one with LB / RB caps): tabs are -14 degree parallelograms with upright text, the gold plate slides to the active tab in 0.16 s,
        /// LB / RB page when <see cref="GlassTabs.BumperPaging"/> is on. Same GlassTabs API as the glass tabs (Buttons, Labels, SetIndex, Changed).
        /// </summary>
        static GlassTabs TileTabs(Transform parent, string[] names, int selected, Action<int> onChanged, float fontSize, string name)
        {
            var t = T;
            if (fontSize < 25f) fontSize = t.sizeTab;
            bool small = fontSize <= t.sizeTabSmall + 0.1f;
            float padX = small ? 22f : t.tabPadX, h = t.tabHeight, gap = t.tabGap;
            int n = names.Length;
            var root = UiKit.Rect(parent, name);
            var tabs = root.gameObject.AddComponent<GlassTabs>();
            tabs.Root = root; tabs.Tile = true;
            tabs.Buttons = new Button[n]; tabs.Labels = new TextMeshProUGUI[n]; tabs.Underlines = new Image[n]; tabs.Chips = new GlassGraphic[n];
            tabs.Bgs = new GlassGraphic[n]; tabs.SlotX = new float[n]; tabs.SlotW = new float[n];
            var bgLayer = UiKit.Rect(root, "Bgs"); UiKit.Stretch(bgLayer);
            var plate = Surface(root, "Plate", GlassKind.TabOn, 0f, false, 1f); plate.Skew = -t.tabSkew; plate.EdgeWidth = 0f; plate.raycastTarget = false;
            var labelLayer = UiKit.Rect(root, "Labels"); UiKit.Stretch(labelLayer);
            tabs.Plate = plate;
            float x = 0f;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var lbl = Text(labelLayer, names[i].ToUpperInvariant(), GlassText.Heading, TextAlignmentOptions.Center, "Tab_" + names[i]);
                Style(lbl, fontSize, t.textTab, true, GlassTokens.Em(3f, fontSize));
                float w = Mathf.Ceil(lbl.GetPreferredValues(lbl.text).x) + padX * 2f;
                lbl.rectTransform.anchorMin = lbl.rectTransform.anchorMax = new Vector2(0f, 0.5f); lbl.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                lbl.rectTransform.sizeDelta = new Vector2(w - 8f, h); lbl.rectTransform.anchoredPosition = new Vector2(x + w * 0.5f, 0f);
                UiKit.Fit(lbl, fontSize * 0.75f);
                var bg = Surface(bgLayer, "Tab_" + names[i], GlassKind.Tab, 0f, false, 1f);
                bg.Skew = -t.tabSkew; bg.EdgeWidth = 0f; bg.raycastTarget = true;
                bg.rectTransform.anchorMin = bg.rectTransform.anchorMax = new Vector2(0f, 0.5f); bg.rectTransform.pivot = new Vector2(0f, 0.5f);
                bg.rectTransform.sizeDelta = new Vector2(w, h); bg.rectTransform.anchoredPosition = new Vector2(x, 0f);
                var b = bg.gameObject.AddComponent<Button>(); b.targetGraphic = bg; b.transition = Selectable.Transition.None;
                var f = bg.gameObject.AddComponent<GlassFocusable>();
                f.Surface = bg; f.Label = null; f.FocusScale = 1f; f.FocusBrighten = 0.05f; f.KeepLabelColor = true; f.PlaySound = true;
                b.onClick.AddListener(() => { UiAudio.Click(); tabs.SetIndex(idx, true); });
                tabs.Buttons[i] = b; tabs.Labels[i] = lbl; tabs.Chips[i] = bg; tabs.Bgs[i] = bg; tabs.SlotX[i] = x; tabs.SlotW[i] = w;
                x += w + gap;
            }
            float total = Mathf.Max(0f, x - gap);
            root.sizeDelta = new Vector2(total, h);
            var le = root.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = total; le.preferredHeight = h; le.minWidth = total; le.minHeight = h;
            plate.rectTransform.anchorMin = plate.rectTransform.anchorMax = new Vector2(0f, 0.5f); plate.rectTransform.pivot = new Vector2(0f, 0.5f);
            tabs.Changed = onChanged;
            tabs.SetIndex(Mathf.Clamp(selected, 0, n - 1), false);
            tabs.SnapPlate();
            return tabs;
        }

        /// <summary>
        /// Direction 16 tab bar with the LB / RB key caps either side (the top bar's page tabs; <paramref name="small"/> = the 25 unit franchise tabs). In the Glass look it returns the plain underline tabs.
        /// The returned GlassTabs has <see cref="GlassTabs.Root"/> = the whole bar (caps included); LB / RB paging is on when <paramref name="bumpers"/> is true.
        /// </summary>
        public static GlassTabs TabBar(Transform parent, string[] names, int selected, Action<int> onChanged, bool small = false, bool bumpers = true, string name = "TabBar")
        {
            var t = T;
            if (!t.flat) { var gt = Tabs(parent, names, selected, onChanged, 56f, small ? 22f : 24f, name); gt.BumperPaging = bumpers; return gt; }
            var bar = UiKit.Rect(parent, name);
            var tabs = TileTabs(bar, names, selected, onChanged, small ? t.sizeTabSmall : t.sizeTab, "Tabs");
            tabs.BumperPaging = bumpers;
            float tw = tabs.Root.sizeDelta.x, h = t.tabHeight, total = tw;
            if (bumpers)
            {
                var lb = GlyphCap(bar, "LB", 32f); var rb = GlyphCap(bar, "RB", 32f);
                float cw = lb.sizeDelta.x, rw = rb.sizeDelta.x;
                lb.anchorMin = lb.anchorMax = new Vector2(0f, 0.5f); lb.pivot = new Vector2(0f, 0.5f); lb.anchoredPosition = Vector2.zero;
                tabs.Root.anchorMin = tabs.Root.anchorMax = new Vector2(0f, 0.5f); tabs.Root.pivot = new Vector2(0f, 0.5f); tabs.Root.anchoredPosition = new Vector2(cw + 14f, 0f);
                rb.anchorMin = rb.anchorMax = new Vector2(0f, 0.5f); rb.pivot = new Vector2(0f, 0.5f); rb.anchoredPosition = new Vector2(cw + 14f + tw + 14f, 0f);
                total = cw + 14f + tw + 14f + rw;
            }
            else { tabs.Root.anchorMin = tabs.Root.anchorMax = new Vector2(0f, 0.5f); tabs.Root.pivot = new Vector2(0f, 0.5f); tabs.Root.anchoredPosition = Vector2.zero; }
            bar.sizeDelta = new Vector2(total, h);
            var le = bar.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = total; le.preferredHeight = h;
            tabs.BarRoot = bar;
            return tabs;
        }

        // ================================================================================== menu item (pause rail) / list row
        static Button TileMenuItem(Transform parent, string label, string iconKey, Action onClick, float width, float height, string name)
        {
            var t = T;
            var r = UiKit.Rect(parent, name ?? "Menu_" + label);
            r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var g = r.gameObject.AddComponent<GlassGraphic>(); g.Kind = GlassKind.Row; g.raycastTarget = true; g.Radius = 0f; g.EdgeWidth = 1f; g.FocusEdge = 4f;
            var btn = r.gameObject.AddComponent<Button>(); btn.targetGraphic = g; btn.transition = Selectable.Transition.None;
            float pad = 26f, isz = Mathf.Min(height * 0.46f, 44f);
            Image icon = null;
            if (!string.IsNullOrEmpty(iconKey))
            {
                icon = Icon(r, iconKey, isz, t.textSecondary);
                UiKit.Anchor(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(pad + 4f, 0f), new Vector2(isz, isz));
            }
            float fs = Mathf.Clamp(height * 0.34f, 24f, t.sizeRowTitle + 3f);
            var txt = Text(r, label.ToUpperInvariant(), GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Label");
            Style(txt, fs, t.textSecondary, true, GlassTokens.Em(1f, fs));
            float lx = icon != null ? pad + 4f + isz + 18f : pad + 4f;
            UiKit.Stretch(txt.rectTransform, lx, 0f, 24f, 0f); UiKit.Fit(txt, 22f);
            var f = r.gameObject.AddComponent<GlassFocusable>();
            f.Surface = g; f.Label = txt; f.Icon = icon; f.FocusScale = 1f; f.FocusBrighten = 0f;
            f.LabelNormal = t.textSecondary; f.LabelFocus = Color.white; f.IconNormal = t.textSecondary; f.IconFocus = t.accent;
            btn.onClick.AddListener(UiAudio.Click);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        /// <summary>
        /// List row of Direction 16: fill #1A1C20, focus fill #272A31 with the 4 unit gold bar, 1 unit hairline below. Optional thumbnail slot at the left (150 x row height - 20: a play diagram), a tag chip, a
        /// 27 unit title and two 20 unit sub lines. In the Glass look it is the glass row. Returns the component with the parts to fill.
        /// </summary>
        public static GlassListRow ListRow(Transform parent, string title, string sub = null, float width = 480f, float height = 124f, Action onClick = null, bool thumb = false, string name = null)
        {
            var t = T;
            var r = UiKit.Rect(parent, name ?? "Row_" + title);
            r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var g = r.gameObject.AddComponent<GlassGraphic>(); g.raycastTarget = true; g.Density = 0.7f;
            if (t.flat) { g.Kind = GlassKind.Row; g.EdgeWidth = 1f; g.Radius = 0f; g.FocusEdge = 4f; } else { g.Radius = t.cornerRadiusSmall; }
            var row = r.gameObject.AddComponent<GlassListRow>(); row.Surface = g;
            var btn = r.gameObject.AddComponent<Button>(); btn.targetGraphic = g; btn.transition = Selectable.Transition.None; row.Button = btn;
            float x = 22f;
            if (thumb)
            {
                row.Thumb = UiKit.Rect(r, "Thumb");
                UiKit.Anchor(row.Thumb, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(14f, 0f), new Vector2(150f, height - 22f));
                x = 14f + 150f + 16f;
            }
            float tagH = 28f;
            row.Tag = Solid(r, "Tag", t.tagPass, 0f, 0f);
            UiKit.Anchor(row.Tag.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -10f), new Vector2(70f, tagH));
            row.TagText = Text(row.Tag.rectTransform, "", GlassText.Caption, TextAlignmentOptions.Center, "TagText"); Style(row.TagText, t.sizeBadge, t.tagText, true, GlassTokens.Em(3f, t.sizeBadge)); UiKit.Stretch(row.TagText.rectTransform);
            row.Tag.gameObject.SetActive(false);
            row.Title = Text(r, (title ?? "").ToUpperInvariant(), GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Title");
            Style(row.Title, t.sizeRowTitle, t.textPrimary, true, GlassTokens.Em(1f, t.sizeRowTitle));
            UiKit.Anchor(row.Title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -40f), new Vector2(width - x - 18f, 36f)); UiKit.Fit(row.Title, 22f);
            row.Sub = Text(r, sub ?? "", GlassText.Body, TextAlignmentOptions.TopLeft, "Sub");
            Style(row.Sub, t.sizeSub, t.textMuted, false, GlassTokens.Em(1f, t.sizeSub)); row.Sub.textWrappingMode = TextWrappingModes.Normal; row.Sub.overflowMode = TextOverflowModes.Ellipsis; row.Sub.lineSpacing = -10f;
            UiKit.Anchor(row.Sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -74f), new Vector2(width - x - 18f, height - 76f));
            var f = r.gameObject.AddComponent<GlassFocusable>();
            f.Surface = g; f.Label = row.Title; f.FocusScale = 1f; f.FocusBrighten = 0f; f.LabelNormal = t.textPrimary; f.LabelFocus = Color.white; row.Focus = f;
            btn.onClick.AddListener(UiAudio.Click);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return row;
        }

        /// <summary>PASS (blue) / RUN (orange) category chip with dark text, 28 units high.</summary>
        public static GlassGraphic TagChip(Transform parent, string text, TagKind kind)
        {
            var t = T;
            Color c = kind == TagKind.Pass ? t.tagPass : kind == TagKind.Run ? t.tagRun : t.textSecondary;
            var g = Solid(parent, "Tag_" + text, c, 0f, 0f);
            var tx = Text(g.rectTransform, text.ToUpperInvariant(), GlassText.Caption, TextAlignmentOptions.Center, "Text");
            Style(tx, t.sizeBadge, t.tagText, true, GlassTokens.Em(3f, t.sizeBadge));
            float w = tx.GetPreferredValues(tx.text).x + 24f;
            g.rectTransform.sizeDelta = new Vector2(w, 28f); UiKit.Stretch(tx.rectTransform);
            var le = g.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = 28f;
            return g;
        }

        // ================================================================================== player chip (top bar, right)
        /// <summary>Right end of the top bar: name 24 bold, "LVL 27 - 48,250 PX" 20 (PX in light gold) and the crest. Anchored to the top right of <paramref name="parent"/>.</summary>
        public static RectTransform PlayerChip(Transform parent, string name, string level, string px, Sprite crest)
        {
            var t = T;
            var r = UiKit.Rect(parent, "PlayerChip");
            UiKit.Anchor(r, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-50f, -22f), new Vector2(430f, 68f));
            float cx = 0f;
            var img = GlassKit.Plate(r, "Crest", Color.white, crest); img.preserveAspect = true; img.enabled = crest != null;
            UiKit.Anchor(img.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(68f, 60f));
            cx = crest != null ? 84f : 0f;
            var nm = Text(r, (name ?? "").ToUpperInvariant(), GlassText.Heading, TextAlignmentOptions.BottomRight, "Name");
            Style(nm, 24f, t.textPrimary, true, GlassTokens.Em(2f, 24f));
            UiKit.Anchor(nm.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-cx, 0f), new Vector2(340f, 34f)); UiKit.Fit(nm, 20f);
            string sub = (level ?? "").ToUpperInvariant() + (string.IsNullOrEmpty(px) ? "" : "  <color=#" + ColorUtility.ToHtmlStringRGB(t.goldHi) + ">•  " + px.ToUpperInvariant() + "</color>");
            var sb = Text(r, sub, GlassText.Caption, TextAlignmentOptions.TopRight, "Sub");
            Style(sb, 20f, t.textMuted, true, GlassTokens.Em(2f, 20f)); sb.richText = true;
            UiKit.Anchor(sb.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-cx, 4f), new Vector2(340f, 30f)); UiKit.Fit(sb, 20f);
            return r;
        }

        // ================================================================================== ticker / footer entry points
        /// <summary>
        /// The news ticker band: 46 high, full width, directly above the footer (bottom offset 62), gold NEWS block and the scrolling line (96 units/s, diamond separators, pauses while a dialog is open, hidden when
        /// there are no headlines). Parent = the screen root. In the Glass look it is the glass ticker with the "LEAGUE WIRE" chip at the same place.
        /// </summary>
        public static GlassTicker Ticker(RectTransform parent, IList<string> headlines, string label = "NEWS")
        {
            var t = T;
            float h = t.flat ? t.tickerHeight : 46f, bottom = t.flat ? t.footerHeight : 62f;
            var host = UiKit.Rect(parent, "TickerHost");
            host.anchorMin = new Vector2(0f, 0f); host.anchorMax = new Vector2(1f, 0f); host.pivot = new Vector2(0.5f, 0f);
            host.offsetMin = new Vector2(0f, bottom); host.offsetMax = new Vector2(0f, bottom + h);
            var tk = GlassTicker.Build(host, label, h, 20f);
            tk.Host = host;
            tk.SetHeadlines(headlines);
            return tk;
        }

        /// <summary>A toast-sized message: Direction 16 gold left bar, charcoal body, 24 unit text. Convenience over <see cref="GlassToast.Create"/> (in 0.2 s, hold, out 0.2 s).</summary>
        public static GlassToast Toast(Transform parent, float bottom = 150f) { return GlassToast.Create(parent, bottom); }
    }
}

namespace PixFootball.UI.Glass
{
    /// <summary>Marker: this Image is already drawn the way it should look, so <see cref="GlassSkin"/> leaves it alone (kit components, Tile bands and plates).</summary>
    public sealed class GlassNoSkin : MonoBehaviour { }

    public static partial class GlassKit
    {
        /// <summary>A flat Image plate (colour and optional sprite) that the glass skin never touches: the Tile look's bands, scrims and gradient backs. Same shape as <see cref="UiKit.Panel"/>.</summary>
        public static Image Plate(Transform parent, string name, Color color, Sprite sprite = null)
        {
            var img = UiKit.Panel(parent, name, color, sprite);
            img.gameObject.AddComponent<GlassNoSkin>();
            return img;
        }
    }
}
