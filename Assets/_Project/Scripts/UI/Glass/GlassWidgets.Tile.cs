using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>The parts of a modal frame: dim scrim, the box with its dark head, a body to fill and the title.</summary>
    public sealed class ModalParts
    {
        public RectTransform Scrim, Box, Head, Body; public TextMeshProUGUI Title; public GlassGraphic Surface;
    }

    /// <summary>Direction 16 versions of the compound widgets (cards, chips, stat tiles, top bar, footer, dialog, modal frame). Called from the same entry points as the glass ones when the Tile theme is active.</summary>
    public static partial class GlassWidgets
    {
        // ================================================================================== card (panel with dark head)
        static GlassCard TileCard(Transform parent, string title, float width, float height, string name, bool shadow, float density, bool goldRule)
        {
            var t = T;
            var c = new GlassCard();
            c.Surface = GlassKit.Panel(parent, name ?? "Card_" + title, 1f, shadow);
            c.Root = c.Surface.rectTransform; c.Root.sizeDelta = new Vector2(width, height);
            var le = c.Root.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            float pad = t.panelPadding, headH = 0f;
            if (!string.IsNullOrEmpty(title))
            {
                headH = t.tableHeadHeight + 4f;
                var head = GlassKit.Well(c.Root, "Head", 0f); head.EdgeWidth = 0f;
                head.rectTransform.anchorMin = new Vector2(0f, 1f); head.rectTransform.anchorMax = new Vector2(1f, 1f); head.rectTransform.pivot = new Vector2(0.5f, 1f);
                head.rectTransform.offsetMin = new Vector2(2f, -(headH + 2f)); head.rectTransform.offsetMax = new Vector2(-2f, -2f);
                c.Title = GlassKit.Text(head.rectTransform, title.ToUpperInvariant(), GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Title");
                GlassKit.Style(c.Title, t.sizePanelTitle, t.textPrimary, true, GlassTokens.Em(5f, t.sizePanelTitle));
                UiKit.Stretch(c.Title.rectTransform, 20f, 0f, 20f, 0f); UiKit.Fit(c.Title, 22f);
                var rule = GlassKit.Rule(c.Root); c.Rule = rule;
                if (goldRule) rule.color = t.gold;
                var rr = rule.rectTransform; rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f); rr.pivot = new Vector2(0.5f, 1f);
                rr.anchoredPosition = new Vector2(0f, -(headH + 2f)); rr.sizeDelta = new Vector2(-4f, goldRule ? 2f : 1f);
            }
            c.Body = UiKit.Rect(c.Root, "Body");
            c.Body.anchorMin = Vector2.zero; c.Body.anchorMax = Vector2.one;
            c.Body.offsetMin = new Vector2(pad, pad * 0.7f); c.Body.offsetMax = new Vector2(-pad, -(headH > 0f ? headH + 12f : pad * 0.7f));
            return c;
        }

        // ================================================================================== badge chip / stat tile
        static GlassGraphic TileChip(Transform parent, string text, bool accent, float height, float fontSize)
        {
            var t = T;
            fontSize = Mathf.Max(fontSize, t.sizeBadge);
            var g = GlassKit.Solid(parent, "Chip_" + text, new Color(0.039f, 0.043f, 0.055f, 0.82f), 0f, 1f);
            var tx = GlassKit.Text(g.rectTransform, text.ToUpperInvariant(), GlassText.Caption, TextAlignmentOptions.Center, "Text");
            GlassKit.Style(tx, fontSize, accent ? t.goldHi : t.textPrimary, true, GlassTokens.Em(3f, fontSize));
            float w = tx.GetPreferredValues(tx.text).x + 28f;
            g.rectTransform.sizeDelta = new Vector2(w, height);
            UiKit.Stretch(tx.rectTransform, 2f, 0f, 2f, 0f);
            var le = g.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = height;
            return g;
        }

        static RectTransform TileStatTile(Transform parent, string value, string caption, float width, float height, bool accentValue)
        {
            var t = T;
            var w = GlassKit.Well(parent, "Stat_" + caption, 0f);
            w.rectTransform.sizeDelta = new Vector2(width, height);
            var le = w.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var v = GlassKit.Text(w.rectTransform, value, GlassText.Heading, TextAlignmentOptions.Center, "Value");
            GlassKit.Style(v, Mathf.Min(44f, height * 0.44f), accentValue ? t.goldHi : t.textPrimary, true);
            v.rectTransform.anchorMin = new Vector2(0f, 1f); v.rectTransform.anchorMax = new Vector2(1f, 1f); v.rectTransform.pivot = new Vector2(0.5f, 1f);
            v.rectTransform.anchoredPosition = new Vector2(0f, -height * 0.08f); v.rectTransform.sizeDelta = new Vector2(0f, height * 0.5f);
            UiKit.Fit(v, 24f);
            var cp = GlassKit.Eyebrow(w.rectTransform, caption, TextAlignmentOptions.Center, "Caption");
            GlassKit.Style(cp, 20f, t.textMuted, true, GlassTokens.Em(3f, 20f));
            cp.rectTransform.anchorMin = new Vector2(0f, 0f); cp.rectTransform.anchorMax = new Vector2(1f, 0f); cp.rectTransform.pivot = new Vector2(0.5f, 0f);
            cp.rectTransform.anchoredPosition = new Vector2(0f, height * 0.08f); cp.rectTransform.sizeDelta = new Vector2(0f, height * 0.3f);
            UiKit.Fit(cp, 20f);
            return w.rectTransform;
        }

        // ================================================================================== top bar
        /// <summary>
        /// Direction 16 top bar: full-bleed 112 high band (gradient #0C0D0F to #15161A, 1 unit line under it), wordmark "PIX PRO / FOOTBALL" on the left, the skewed tab bar with LB / RB caps in the middle (25 unit
        /// tabs from six tabs up), and <see cref="SetPlayerChip"/> for the right end. Without tabs the section name is centred.
        /// </summary>
        static NavBar TileTopBar(Transform parent, string section, string[] tabs, int selected, Action<int> onTab, bool bumpers)
        {
            var t = T;
            var nb = new NavBar();
            var bar = UiKit.Rect(parent, "TopBar"); nb.Root = bar;
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f); bar.pivot = new Vector2(0.5f, 1f); bar.anchoredPosition = Vector2.zero; bar.sizeDelta = new Vector2(0f, t.topBarHeight);
            var bg = GlassKit.Plate(bar, "Band", Color.white, GlassKit.VGradient(t.topBarTop, t.topBarBottom)); UiKit.Stretch(bg.rectTransform);
            var line = GlassKit.Plate(bar, "Line", t.topBarLine);
            line.rectTransform.anchorMin = new Vector2(0f, 0f); line.rectTransform.anchorMax = new Vector2(1f, 0f); line.rectTransform.pivot = new Vector2(0.5f, 0f); line.rectTransform.anchoredPosition = Vector2.zero; line.rectTransform.sizeDelta = new Vector2(0f, 1f);
            var mark = GlassKit.Text(bar, "PIX PRO", GlassText.Heading, TextAlignmentOptions.TopLeft, "Wordmark");
            GlassKit.Style(mark, 40f, Color.white, true, GlassTokens.Em(2f, 40f)); nb.Wordmark = mark;
            UiKit.Anchor(mark.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(50f, -22f), new Vector2(300f, 54f));
            var sub = GlassKit.Text(bar, "FOOTBALL", GlassText.Caption, TextAlignmentOptions.TopLeft, "WordmarkSub");
            GlassKit.Style(sub, 20f, t.gold, true, GlassTokens.Em(9f, 20f));
            UiKit.Anchor(sub.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(52f, -66f), new Vector2(300f, 26f));
            if (tabs != null && tabs.Length > 0)
            {
                var tr = GlassKit.TabBar(bar, tabs, selected, onTab, tabs.Length > 5, bumpers);
                nb.Tabs = tr;
                var rt = tr.Bar;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f); rt.anchoredPosition = new Vector2(0f, -28f);
            }
            else if (!string.IsNullOrEmpty(section))
            {
                var sec = GlassKit.Text(bar, section.ToUpperInvariant(), GlassText.Heading, TextAlignmentOptions.Center, "Section"); nb.Section = sec;
                GlassKit.Style(sec, t.sizeTab, t.textTab, true, GlassTokens.Em(3f, t.sizeTab));
                UiKit.Anchor(sec.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(900f, 54f));
            }
            return nb;
        }

        /// <summary>Fills the player chip at the right end of a Direction 16 top bar (name, "LVL n" and PX, crest). No-op in the Glass look.</summary>
        public static RectTransform SetPlayerChip(NavBar nb, string name, string level, string px, Sprite crest)
        {
            if (nb == null || nb.Root == null || !T.flat) return null;
            return GlassKit.PlayerChip(nb.Root, name, level, px, crest);
        }

        // ================================================================================== footer
        /// <summary>Direction 16 footer: full-bleed 62 high #0A0B0D band, the live glyph legend on the left (A green, B red, X blue, Y gold discs; LB / RB grey caps), muted context text on the right.</summary>
        static Footer TileBottomBar(Transform parent, string rightText)
        {
            var t = T;
            var f = new Footer();
            var bar = UiKit.Rect(parent, "Footer"); f.Root = bar;
            bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(1f, 0f); bar.pivot = new Vector2(0.5f, 0f); bar.anchoredPosition = Vector2.zero; bar.sizeDelta = new Vector2(0f, t.footerHeight);
            var bg = GlassKit.Plate(bar, "Band", t.footerBg); UiKit.Stretch(bg.rectTransform);
            var row = UiKit.Rect(bar, "Prompts");
            row.anchorMin = new Vector2(0f, 0f); row.anchorMax = new Vector2(0.72f, 1f); row.offsetMin = new Vector2(50f, 0f); row.offsetMax = Vector2.zero;
            f.Prompts = PromptFooter.Attach(row, 34f, t.sizeLegend, 34f);
            var rt = GlassKit.Text(bar, rightText ?? "", GlassText.Caption, TextAlignmentOptions.MidlineRight, "Right"); f.Right = rt;
            GlassKit.Style(rt, 20f, t.textMuted, true, GlassTokens.Em(3f, 20f));
            rt.rectTransform.anchorMin = new Vector2(0.5f, 0f); rt.rectTransform.anchorMax = new Vector2(1f, 1f); rt.rectTransform.offsetMin = Vector2.zero; rt.rectTransform.offsetMax = new Vector2(-50f, 0f);
            UiKit.Fit(rt, 20f);
            return f;
        }

        // ================================================================================== dialog / modal
        /// <summary>A modal frame: dim scrim, panel box with a dark head (title) and an empty body rectangle. Direction 16; the Glass look gets the glass card.</summary>
        public static ModalParts Modal(RectTransform root, string title, float width, float height)
        {
            var t = T;
            var p = new ModalParts();
            var scrim = GlassKit.Plate(root, "Scrim", UiColors.Scrim); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true; p.Scrim = scrim.rectTransform;
            var card = Card(root, title, width, height, "Modal", true, 1.7f);
            p.Box = card.Root; p.Body = card.Body; p.Title = card.Title; p.Surface = card.Surface;
            UiKit.Anchor(p.Box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(width, height));
            p.Head = card.Title != null ? (RectTransform)card.Title.rectTransform.parent : null;
            return p;
        }

        static DialogParts TileDialog(RectTransform root, string title, string message, string yesLabel, string noLabel, Action yes, Action no)
        {
            var t = T;
            var p = new DialogParts();
            var m = Modal(root, title, 900f, 440f);
            p.Box = m.Box; p.Title = m.Title;
            m.Surface.Shadow = 1f;
            p.Message = GlassKit.Text(m.Body, message, GlassText.Body, TextAlignmentOptions.TopLeft, "Message");
            GlassKit.Style(p.Message, 28f, t.textSecondary, false, 0f); p.Message.textWrappingMode = TextWrappingModes.Normal;
            p.Message.rectTransform.anchorMin = new Vector2(0f, 0f); p.Message.rectTransform.anchorMax = new Vector2(1f, 1f); p.Message.rectTransform.offsetMin = new Vector2(0f, 96f); p.Message.rectTransform.offsetMax = new Vector2(0f, 0f);
            var row = UiKit.Rect(m.Box, "Buttons"); UiKit.Anchor(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 36f), new Vector2(804f, 76f));
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 24f; h.childAlignment = TextAnchor.MiddleCenter; h.childControlWidth = false; h.childControlHeight = false; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            p.Yes = GlassKit.Button(row, yesLabel, yes, GlassButtonStyle.Primary, 390f, 76f);
            p.No = GlassKit.Button(row, noLabel, no, GlassButtonStyle.Secondary, 390f, 76f);
            return p;
        }
    }
}
