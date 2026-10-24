using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>A play card (Direction 16): 406 x 286 panel, dark 50 unit header with the PASS / RUN tag, the play name and the formation, and a field well (green gradient, 7% yard lines) where the diagram is drawn.</summary>
    public sealed class GlassPlayCard
    {
        public RectTransform Root, Field, Header;
        public GlassGraphic Surface, Tag;
        public TextMeshProUGUI Title, Formation, TagText;
        public Button Button; public GlassFocusable Focus;
    }

    public static partial class GlassKit
    {
        // ================================================================================== strip tabs (play call categories)
        /// <summary>
        /// Underline tabs of the play-call strip: 22 unit bold caps, grey idle, light gold when active with a 4 unit gold underline, 44 high. Same GlassTabs API (LB / RB paging optional).
        /// </summary>
        public static GlassTabs StripTabs(Transform parent, string[] names, int selected, Action<int> onChanged, string name = "StripTabs")
        {
            var t = T;
            if (!t.flat) return Tabs(parent, names, selected, onChanged, 56f, 22f, name);
            var row = Row(parent, name, 2f, TextAnchor.MiddleLeft);
            var tabs = row.gameObject.AddComponent<GlassTabs>();
            tabs.UseCustomColors = true; tabs.ActiveColor = t.goldHi; tabs.IdleColor = new Color(0.663f, 0.678f, 0.718f, 1f);   // #A9ADB7
            tabs.Buttons = new Button[names.Length]; tabs.Labels = new TextMeshProUGUI[names.Length]; tabs.Underlines = new Image[names.Length]; tabs.Chips = new GlassGraphic[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var r = UiKit.Rect(row.transform, "Tab_" + names[i]);
                var lbl = Text(r, names[i].ToUpperInvariant(), GlassText.Heading, TextAlignmentOptions.Center, "Label");
                Style(lbl, 22f, tabs.IdleColor, true, GlassTokens.Em(2f, 22f));
                float w = Mathf.Ceil(lbl.GetPreferredValues(lbl.text).x) + 44f;
                r.sizeDelta = new Vector2(w, 44f);
                var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = 44f;
                var hit = r.gameObject.AddComponent<Image>(); hit.color = new Color(1f, 1f, 1f, 0f); hit.raycastTarget = true;
                UiKit.Stretch(lbl.rectTransform, 4f, 4f, 4f, 0f);
                var ul = Bar(r, "Underline", t.gold, w, 4f);
                ul.rectTransform.anchorMin = new Vector2(0f, 0f); ul.rectTransform.anchorMax = new Vector2(1f, 0f); ul.rectTransform.pivot = new Vector2(0.5f, 0f); ul.rectTransform.anchoredPosition = Vector2.zero; ul.rectTransform.sizeDelta = new Vector2(0f, 4f);
                var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = hit; b.transition = Selectable.Transition.None;
                var hl = Surface(r, "Focus", GlassKind.Glass, 0f, false, 0.7f); hl.EdgeWidth = 0f; hl.raycastTarget = false; UiKit.Stretch(hl.rectTransform);
                hl.rectTransform.SetAsFirstSibling();
                var f = r.gameObject.AddComponent<GlassFocusable>();
                f.Surface = hl; f.GhostSurface = true; f.FocusScale = 1f; f.FocusBrighten = 0f; f.KeepLabelColor = true; f.PlaySound = true;
                b.onClick.AddListener(() => { UiAudio.Click(); tabs.SetIndex(idx, true); });
                tabs.Buttons[i] = b; tabs.Labels[i] = lbl; tabs.Underlines[i] = ul; tabs.Chips[i] = hl;
            }
            tabs.Changed = onChanged;
            tabs.SetIndex(Mathf.Clamp(selected, 0, names.Length - 1), false);
            return tabs;
        }

        // ================================================================================== static table block (standings, schedule panels)
        /// <summary>
        /// A non-interactive table block as in the mock's standings panel: a dark group head (24 unit gold-light caps), a header row (20 unit muted caps), 50 unit rows with hairlines, one optional highlighted row
        /// (gold 16% tint, light gold text). Column 0 is left aligned, the others centred. Returns the block (width x computed height). Interactive lists use <c>PixFootball.UI.Franchise.GlassTable</c>.
        /// </summary>
        public static RectTransform TableBlock(Transform parent, string groupTitle, string[] headers, string[][] rows, float[] weights, float width, int highlightRow = -1)
        {
            var t = T;
            float headH = string.IsNullOrEmpty(groupTitle) ? 0f : 50f, colHeadH = headers != null ? 44f : 0f, rowH = t.tableRowHeight;
            float h = headH + colHeadH + rows.Length * rowH + 8f;
            var root = UiKit.Rect(parent, "Table_" + groupTitle); root.sizeDelta = new Vector2(width, h);
            var le = root.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = h;
            float total = 0f; foreach (float w in weights) total += w;
            float y = 0f;
            if (headH > 0f)
            {
                var gh = GlassKit.Plate(root, "GroupHead", t.panelHeadAlt);
                UiKit.Anchor(gh.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(width, headH));
                var gt = Text(gh.rectTransform, groupTitle.ToUpperInvariant(), GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Title");
                Style(gt, t.sizeGroupTitle, t.goldHi, true, GlassTokens.Em(4f, t.sizeGroupTitle)); UiKit.Stretch(gt.rectTransform, 16f, 0f, 16f, 0f);
                y = headH;
            }
            float padX = 14f;
            Func<int, float> xOf = c => { float x = padX; for (int k = 0; k < c; k++) x += (width - padX * 2f) * weights[k] / total; return x; };
            Func<int, float> wOf = c => (width - padX * 2f) * weights[c] / total;
            if (headers != null)
            {
                for (int c = 0; c < headers.Length; c++)
                {
                    var hx = Text(root, headers[c].ToUpperInvariant(), GlassText.Caption, c == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center, "H" + c);
                    Style(hx, t.sizeTableHead, t.textMuted, true, GlassTokens.Em(3f, t.sizeTableHead));
                    UiKit.Anchor(hx.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(xOf(c), -y), new Vector2(wOf(c), colHeadH));
                }
                var rule = Bar(root, "HeadRule", t.borderPanel, width, 1f);
                rule.rectTransform.anchorMin = rule.rectTransform.anchorMax = new Vector2(0f, 1f); rule.rectTransform.pivot = new Vector2(0f, 1f); rule.rectTransform.anchoredPosition = new Vector2(0f, -(y + colHeadH)); rule.rectTransform.sizeDelta = new Vector2(width, 1f);
                y += colHeadH;
            }
            for (int r = 0; r < rows.Length; r++)
            {
                bool me = r == highlightRow;
                var bg = GlassKit.Plate(root, "Row" + r, me ? new Color(t.gold.r, t.gold.g, t.gold.b, 0.16f) : new Color(0f, 0f, 0f, 0f));
                UiKit.Anchor(bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -y), new Vector2(width, rowH));
                var hl = Bar(bg.rectTransform, "Line", t.hairline, width, 1f);
                hl.rectTransform.anchorMin = new Vector2(0f, 0f); hl.rectTransform.anchorMax = new Vector2(1f, 0f); hl.rectTransform.pivot = new Vector2(0.5f, 0f); hl.rectTransform.anchoredPosition = Vector2.zero; hl.rectTransform.sizeDelta = new Vector2(0f, 1f);
                for (int c = 0; c < rows[r].Length && c < weights.Length; c++)
                {
                    var tx = Text(bg.rectTransform, rows[r][c], GlassText.Body, c == 0 ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.Center, "C" + c);
                    Style(tx, t.sizeTableBody, me ? t.goldHi : t.textPrimary, true, 0f);
                    UiKit.Anchor(tx.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(xOf(c), 0f), new Vector2(wOf(c), rowH)); UiKit.Fit(tx, 20f);
                }
                y += rowH;
            }
            return root;
        }

        // ================================================================================== play card
        /// <summary>
        /// Play card of the play-call strip (406 x 286 by default, <see cref="TileMetrics"/>). Fill <see cref="GlassPlayCard.Field"/> with the live diagram. Focus = 4 unit gold border, glow, 1.04 scale. In the Glass look
        /// it is a glass card with the same parts.
        /// </summary>
        public static GlassPlayCard PlayCard(Transform parent, string title, TagKind tag, string formation, float width = 406f, float height = 286f, Action onClick = null)
        {
            var t = T;
            var pc = new GlassPlayCard();
            var r = UiKit.Rect(parent, "PlayCard_" + title); pc.Root = r; r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var g = r.gameObject.AddComponent<GlassGraphic>(); pc.Surface = g; g.raycastTarget = true; g.Shadow = 1f;
            if (t.flat) { g.Kind = GlassKind.Panel; g.Radius = 0f; g.EdgeWidth = t.restEdge; g.FocusEdge = t.focusEdge; } else { g.Radius = t.cornerRadius; }
            pc.Button = r.gameObject.AddComponent<Button>(); pc.Button.targetGraphic = g; pc.Button.transition = Selectable.Transition.None;
            float headH = 50f;
            var head = t.flat ? (Graphic)Solid(r, "Header", t.panelHead, 0f, 0f) : Surface(r, "Header", GlassKind.Well, t.cornerRadiusSmall, false, 1f);
            pc.Header = head.rectTransform;
            head.rectTransform.anchorMin = new Vector2(0f, 1f); head.rectTransform.anchorMax = new Vector2(1f, 1f); head.rectTransform.pivot = new Vector2(0.5f, 1f);
            head.rectTransform.offsetMin = new Vector2(2f, -(headH + 2f)); head.rectTransform.offsetMax = new Vector2(-2f, -2f);
            pc.Tag = TagChip(head.rectTransform, tag == TagKind.Pass ? "PASS" : tag == TagKind.Run ? "RUN" : "PLAY", tag);
            pc.Tag.rectTransform.anchorMin = pc.Tag.rectTransform.anchorMax = new Vector2(0f, 0.5f); pc.Tag.rectTransform.pivot = new Vector2(0f, 0.5f); pc.Tag.rectTransform.anchoredPosition = new Vector2(14f, 0f);
            float tagW = pc.Tag.rectTransform.sizeDelta.x;
            pc.Title = Text(head.rectTransform, (title ?? "").ToUpperInvariant(), GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Title");
            Style(pc.Title, t.sizeCardTitle, t.textPrimary, true, GlassTokens.Em(1f, t.sizeCardTitle));
            pc.Title.rectTransform.anchorMin = Vector2.zero; pc.Title.rectTransform.anchorMax = Vector2.one; pc.Title.rectTransform.offsetMin = new Vector2(14f + tagW + 12f, 0f); pc.Title.rectTransform.offsetMax = new Vector2(-(formation != null ? 150f : 12f), 0f); UiKit.Fit(pc.Title, 22f);
            pc.Formation = Text(head.rectTransform, formation ?? "", GlassText.Caption, TextAlignmentOptions.MidlineRight, "Formation");
            Style(pc.Formation, t.sizeSub, t.textTab, false, 0f);
            pc.Formation.rectTransform.anchorMin = new Vector2(1f, 0f); pc.Formation.rectTransform.anchorMax = new Vector2(1f, 1f); pc.Formation.rectTransform.pivot = new Vector2(1f, 0.5f); pc.Formation.rectTransform.anchoredPosition = new Vector2(-14f, 0f); pc.Formation.rectTransform.sizeDelta = new Vector2(150f, 0f); UiKit.Fit(pc.Formation, 20f);
            // field well
            var field = GlassKit.Plate(r, "Field", Color.white, VGradient(t.fieldTop, t.fieldBottom));
            pc.Field = field.rectTransform;
            field.rectTransform.anchorMin = Vector2.zero; field.rectTransform.anchorMax = Vector2.one; field.rectTransform.offsetMin = new Vector2(10f, 10f); field.rectTransform.offsetMax = new Vector2(-10f, -(headH + 8f));
            int lines = Mathf.Max(2, Mathf.FloorToInt((height - headH - 18f) / 28f));
            for (int i = 1; i < lines; i++)
            {
                var ln = GlassKit.Plate(field.rectTransform, "Yard" + i, new Color(1f, 1f, 1f, 0.07f));
                ln.rectTransform.anchorMin = new Vector2(0f, 0f); ln.rectTransform.anchorMax = new Vector2(1f, 0f); ln.rectTransform.pivot = new Vector2(0.5f, 0.5f);
                ln.rectTransform.anchoredPosition = new Vector2(0f, i * 28f); ln.rectTransform.sizeDelta = new Vector2(0f, 2f);
            }
            var f = r.gameObject.AddComponent<GlassFocusable>();
            f.Surface = g; f.FocusScale = t.flat ? t.cardScale : t.focusScale; f.FocusBrighten = 0f; f.RaiseTarget = r;
            pc.Focus = f;
            pc.Button.onClick.AddListener(UiAudio.Click);
            if (onClick != null) pc.Button.onClick.AddListener(() => onClick());
            return pc;
        }
    }
}
