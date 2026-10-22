using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>A titled glass card: the header caps title with the pack's hairline rule, and a body rectangle to fill.</summary>
    public sealed class GlassCard
    {
        public RectTransform Root, Body;
        public GlassGraphic Surface;
        public TextMeshProUGUI Title;
        public Image Rule;
    }

    /// <summary>Compound widgets of the glass library: cards, chips, stat tiles, rating bars, table rows, nav bar, controller footer, crest tiles, dropdown, dialog, toast and tooltip.</summary>
    public static class GlassWidgets
    {
        static GlassTokens T { get { return GlassTheme.T; } }

        // ================================================================================== card
        public static GlassCard Card(Transform parent, string title, float width, float height, string name = null, bool shadow = true, float density = 1f)
        {
            var t = T;
            var c = new GlassCard();
            c.Surface = GlassKit.Panel(parent, name ?? "Card_" + title, density, shadow);
            c.Root = c.Surface.rectTransform; c.Root.sizeDelta = new Vector2(width, height);
            var le = c.Root.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            float pad = t.panelPadding;
            float headH = 0f;
            if (!string.IsNullOrEmpty(title))
            {
                c.Title = GlassKit.Eyebrow(c.Root, title);
                c.Title.color = t.textPrimary;
                UiKit.Anchor(c.Title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(pad, -pad * 0.55f), new Vector2(width - pad * 2f, 36f));
                var rule = GlassKit.Rule(c.Root); c.Rule = rule;
                var rr = rule.rectTransform; rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f); rr.pivot = new Vector2(0.5f, 1f);
                rr.anchoredPosition = new Vector2(0f, -(pad * 0.55f + 44f)); rr.sizeDelta = new Vector2(-pad * 2f, 1.4f);
                headH = pad * 0.55f + 44f + 12f;
            }
            c.Body = UiKit.Rect(c.Root, "Body");
            c.Body.anchorMin = Vector2.zero; c.Body.anchorMax = Vector2.one;
            c.Body.offsetMin = new Vector2(pad, pad * 0.7f); c.Body.offsetMax = new Vector2(-pad, -(headH > 0f ? headH : pad * 0.7f));
            return c;
        }

        // ================================================================================== chips / tiles / bars
        /// <summary>Small status pill ("READY", "LIVE", "HOME - YOU"): glass, orange text and edge when <paramref name="accent"/>.</summary>
        public static GlassGraphic Chip(Transform parent, string text, bool accent = false, float height = 34f, float fontSize = 18f)
        {
            var t = T;
            var g = GlassKit.Surface(parent, "Chip_" + text, GlassKind.Glass, height * 0.5f, false, 1.1f);
            if (accent) { g.SetFocus(0.85f); g.GlowScale = 0.22f; }
            var tx = GlassKit.Text(g.rectTransform, text.ToUpperInvariant(), GlassText.Caption, TextAlignmentOptions.Center, "Text");
            GlassKit.Style(tx, fontSize, accent ? t.accent : t.textPrimary, true, 2f);
            float w = tx.GetPreferredValues().x + 30f;
            g.rectTransform.sizeDelta = new Vector2(w, height);
            UiKit.Stretch(tx.rectTransform, 4f, 0f, 4f, 0f);
            var le = g.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = height;
            return g;
        }

        /// <summary>Stat tile: a big value over a small caps caption in a dark inset well (the pack's "4-1 RECORD / 87 TEAM OVR" strip cells).</summary>
        public static RectTransform StatTile(Transform parent, string value, string caption, float width = 150f, float height = 96f, bool accentValue = false)
        {
            var t = T;
            var w = GlassKit.Well(parent, "Stat_" + caption);
            w.rectTransform.sizeDelta = new Vector2(width, height);
            var le = w.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var v = GlassKit.Text(w.rectTransform, value, GlassText.Heading, TextAlignmentOptions.Center, "Value");
            GlassKit.Style(v, Mathf.Min(44f, height * 0.46f), accentValue ? t.accent : t.textPrimary, true);
            UiKit.Anchor(v.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -height * 0.1f), new Vector2(0f, height * 0.52f));
            v.rectTransform.anchorMin = new Vector2(0f, 1f); v.rectTransform.anchorMax = new Vector2(1f, 1f); v.rectTransform.pivot = new Vector2(0.5f, 1f); v.rectTransform.sizeDelta = new Vector2(0f, height * 0.52f);
            UiKit.Fit(v, 16f);
            var cp = GlassKit.Eyebrow(w.rectTransform, caption, TextAlignmentOptions.Center, "Caption");
            GlassKit.Style(cp, 16f, t.textSecondary, true, 2f);
            cp.rectTransform.anchorMin = new Vector2(0f, 0f); cp.rectTransform.anchorMax = new Vector2(1f, 0f); cp.rectTransform.pivot = new Vector2(0.5f, 0f); cp.rectTransform.anchoredPosition = new Vector2(0f, height * 0.1f); cp.rectTransform.sizeDelta = new Vector2(0f, height * 0.3f);
            UiKit.Fit(cp, 11f);
            return w.rectTransform;
        }

        /// <summary>Rating bar row: caption, slim bar (orange when <paramref name="accent"/>, silver otherwise) and the number on the right.</summary>
        public static GlassBar RatingBar(Transform parent, string label, float value01, string valueText, float width = 400f, bool accent = true, float height = 36f)
        {
            var t = T;
            var row = UiKit.Rect(parent, "Rate_" + label); row.sizeDelta = new Vector2(width, height);
            var le = row.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var lbl = GlassKit.Eyebrow(row, label); GlassKit.Style(lbl, 18f, t.textSecondary, true, 2f);
            lbl.rectTransform.anchorMin = new Vector2(0f, 0f); lbl.rectTransform.anchorMax = new Vector2(0f, 1f); lbl.rectTransform.pivot = new Vector2(0f, 0.5f);
            lbl.rectTransform.anchoredPosition = Vector2.zero; lbl.rectTransform.sizeDelta = new Vector2(width * 0.3f, 0f); UiKit.Fit(lbl, 12f);
            var val = GlassKit.Text(row, valueText, GlassText.Label, TextAlignmentOptions.MidlineRight, "Value"); GlassKit.Style(val, 22f, t.textPrimary, true);
            val.rectTransform.anchorMin = new Vector2(1f, 0f); val.rectTransform.anchorMax = new Vector2(1f, 1f); val.rectTransform.pivot = new Vector2(1f, 0.5f);
            val.rectTransform.anchoredPosition = Vector2.zero; val.rectTransform.sizeDelta = new Vector2(width * 0.16f, 0f);
            var bar = GlassKit.ProgressBar(row, value01, width * 0.5f, 8f, accent, "Bar");
            var br = (RectTransform)bar.transform;
            var ble = bar.GetComponent<LayoutElement>(); if (ble != null) UnityEngine.Object.Destroy(ble);
            br.anchorMin = br.anchorMax = new Vector2(0.5f, 0.5f); br.pivot = new Vector2(0.5f, 0.5f); br.anchoredPosition = new Vector2(width * 0.04f, 0f); br.sizeDelta = new Vector2(width * 0.5f, 8f);
            return bar;
        }

        /// <summary>Table row: left value, centred caps label, right value (halftime / postgame comparison rows). Zebra via <paramref name="alt"/>.</summary>
        public static RectTransform CompareRow(Transform parent, string left, string label, string right, float width, float height, bool alt, Color? leftColor = null, Color? rightColor = null)
        {
            var t = T;
            var r = UiKit.Rect(parent, "Row_" + label); r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            if (alt)
            {
                var bg = GlassKit.Surface(r, "Zebra", GlassKind.Well, t.cornerRadiusSmall, false, 0.6f); UiKit.Stretch(bg.rectTransform);
            }
            var l = GlassKit.Text(r, left, GlassText.Body, TextAlignmentOptions.MidlineLeft, "Left"); GlassKit.Style(l, Mathf.Min(30f, height * 0.5f), leftColor ?? t.textPrimary, true);
            l.rectTransform.anchorMin = new Vector2(0f, 0f); l.rectTransform.anchorMax = new Vector2(0.3f, 1f); l.rectTransform.offsetMin = new Vector2(24f, 0f); l.rectTransform.offsetMax = Vector2.zero;
            var m = GlassKit.Eyebrow(r, label, TextAlignmentOptions.Center, "Label"); GlassKit.Style(m, Mathf.Min(22f, height * 0.4f), t.textSecondary, true, 2.5f);
            m.rectTransform.anchorMin = new Vector2(0.3f, 0f); m.rectTransform.anchorMax = new Vector2(0.7f, 1f); m.rectTransform.offsetMin = Vector2.zero; m.rectTransform.offsetMax = Vector2.zero; UiKit.Fit(m, 12f);
            var rr = GlassKit.Text(r, right, GlassText.Body, TextAlignmentOptions.MidlineRight, "Right"); GlassKit.Style(rr, Mathf.Min(30f, height * 0.5f), rightColor ?? t.textPrimary, true);
            rr.rectTransform.anchorMin = new Vector2(0.7f, 0f); rr.rectTransform.anchorMax = new Vector2(1f, 1f); rr.rectTransform.offsetMin = Vector2.zero; rr.rectTransform.offsetMax = new Vector2(-24f, 0f);
            return r;
        }

        // ================================================================================== shades
        static Sprite _grad;
        /// <summary>A 1 x 64 vertical gradient sprite (white, alpha 1 -> 0) generated once: used for the top / bottom shade that keeps headers readable over a bright 3D scene.</summary>
        public static Sprite GradientSprite()
        {
            if (_grad != null) return _grad;
            var tex = new Texture2D(1, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int y = 0; y < 64; y++) { float a = 1f - y / 63f; a = a * a * (3f - 2f * a); tex.SetPixel(0, y, new Color(1f, 1f, 1f, a)); }
            tex.Apply();
            _grad = Sprite.Create(tex, new Rect(0, 0, 1, 64), new Vector2(0.5f, 0.5f), 100f);
            _grad.hideFlags = HideFlags.HideAndDontSave;
            return _grad;
        }

        /// <summary>Soft dark gradient along the top (or bottom) edge of the screen.</summary>
        public static Image EdgeShade(Transform parent, bool top, float height, float alpha, string name = null)
        {
            var r = UiKit.Rect(parent, name ?? (top ? "TopShade" : "BottomShade"));
            var img = r.gameObject.AddComponent<Image>(); img.sprite = GradientSprite(); img.color = new Color(0.04f, 0.045f, 0.055f, alpha); img.raycastTarget = false;
            r.anchorMin = new Vector2(0f, top ? 1f : 0f); r.anchorMax = new Vector2(1f, top ? 1f : 0f); r.pivot = new Vector2(0.5f, top ? 1f : 0f);
            r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(0f, height);
            // the sprite fades from opaque (row 0 = bottom) to clear (top): flip so opaque sits on the screen edge
            r.localScale = top ? new Vector3(1f, -1f, 1f) : Vector3.one;
            return img;
        }

        static Sprite _hgrad;
        /// <summary>A 64 x 1 horizontal gradient sprite (opaque on the left, clear on the right).</summary>
        public static Sprite HGradientSprite()
        {
            if (_hgrad != null) return _hgrad;
            var tex = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            for (int x = 0; x < 64; x++) { float a = 1f - x / 63f; a = a * a * (3f - 2f * a); tex.SetPixel(x, 0, new Color(1f, 1f, 1f, a)); }
            tex.Apply();
            _hgrad = Sprite.Create(tex, new Rect(0, 0, 64, 1), new Vector2(0.5f, 0.5f), 100f);
            _hgrad.hideFlags = HideFlags.HideAndDontSave;
            return _hgrad;
        }

        /// <summary>Soft dark ramp on the left or right edge of the screen (keeps a menu column readable over a bright scene).</summary>
        public static Image SideShade(Transform parent, bool left, float width, float alpha, string name = null)
        {
            var r = UiKit.Rect(parent, name ?? (left ? "LeftShade" : "RightShade"));
            var img = r.gameObject.AddComponent<Image>(); img.sprite = HGradientSprite(); img.color = new Color(0.04f, 0.045f, 0.055f, alpha); img.raycastTarget = false;
            r.anchorMin = new Vector2(left ? 0f : 1f, 0f); r.anchorMax = new Vector2(left ? 0f : 1f, 1f); r.pivot = new Vector2(left ? 0f : 1f, 0.5f);
            r.anchoredPosition = Vector2.zero; r.sizeDelta = new Vector2(width, 0f);
            r.localScale = left ? Vector3.one : new Vector3(-1f, 1f, 1f);
            return img;
        }

        // ================================================================================== nav bar
        public sealed class NavBar { public RectTransform Root; public GlassTabs Tabs; public TextMeshProUGUI Wordmark, Section; }

        /// <summary>Top navigation: the PIX FOOTBALL wordmark (orange accent), a thin divider and the section name, optional tabs on the right. Floats over the scene on a soft top shade.</summary>
        public static NavBar TopBar(Transform parent, string section, string[] tabs = null, int selected = 0, Action<int> onTab = null, bool bumpers = false)
        {
            var t = T;
            var nb = new NavBar();
            GlassWidgets.EdgeShade(parent, true, 200f, 0.8f);
            var bar = UiKit.Rect(parent, "TopBar"); nb.Root = bar;
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f); bar.pivot = new Vector2(0.5f, 1f); bar.anchoredPosition = new Vector2(0f, -t.safeMarginY); bar.sizeDelta = new Vector2(-t.safeMarginX * 2f, 76f);
            var mark = GlassKit.Text(bar, "<color=#" + ColorUtility.ToHtmlStringRGB(t.accent) + ">PIX</color> FOOTBALL", GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Wordmark");
            GlassKit.Style(mark, 42f, t.textPrimary, true, 4f); nb.Wordmark = mark;
            mark.rectTransform.anchorMin = new Vector2(0f, 0f); mark.rectTransform.anchorMax = new Vector2(0f, 1f); mark.rectTransform.pivot = new Vector2(0f, 0.5f); mark.rectTransform.anchoredPosition = Vector2.zero; mark.rectTransform.sizeDelta = new Vector2(330f, 0f);
            float x = mark.GetPreferredValues().x + 40f;
            if (!string.IsNullOrEmpty(section))
            {
                var div = GlassKit.Bar(bar, "Divider", new Color(t.edgeA.r, t.edgeA.g, t.edgeA.b, 0.4f), 1.5f, 38f);
                UiKit.Anchor(div.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(1.5f, 38f));
                var sec = GlassKit.Eyebrow(bar, section); GlassKit.Style(sec, 22f, t.textSecondary, true, 3.5f); nb.Section = sec;
                UiKit.Anchor(sec.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x + 28f, 0f), new Vector2(520f, 40f));
            }
            if (tabs != null && tabs.Length > 0)
            {
                var tr = GlassKit.Tabs(bar, tabs, selected, onTab);
                tr.BumperPaging = bumpers; nb.Tabs = tr;
                var rt = (RectTransform)tr.transform;
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f); rt.pivot = new Vector2(1f, 0.5f); rt.anchoredPosition = Vector2.zero;
                var fit = rt.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            return nb;
        }

        // ================================================================================== footer
        public sealed class Footer { public RectTransform Root; public PromptFooter Prompts; public TextMeshProUGUI Right; }

        /// <summary>Bottom bar: controller glyph (or keycap) prompts on the left, a caption on the right (version). Sits on a soft bottom shade, no hard panel.</summary>
        public static Footer BottomBar(Transform parent, string rightText = null)
        {
            var t = T;
            var f = new Footer();
            EdgeShade(parent, false, 190f, 0.92f);
            var bar = UiKit.Rect(parent, "Footer"); f.Root = bar;
            bar.anchorMin = new Vector2(0f, 0f); bar.anchorMax = new Vector2(1f, 0f); bar.pivot = new Vector2(0.5f, 0f); bar.anchoredPosition = new Vector2(0f, t.safeMarginY * 0.7f); bar.sizeDelta = new Vector2(-t.safeMarginX * 2f, 64f);
            var rule = GlassKit.Rule(bar, "FooterRule", 0.6f);
            var rr = rule.rectTransform; rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f); rr.pivot = new Vector2(0.5f, 0f); rr.anchoredPosition = new Vector2(0f, 6f); rr.sizeDelta = new Vector2(0f, 1.4f);
            var row = UiKit.Rect(bar, "Prompts");
            row.anchorMin = new Vector2(0f, 0f); row.anchorMax = new Vector2(0.78f, 1f); row.offsetMin = Vector2.zero; row.offsetMax = Vector2.zero;
            f.Prompts = PromptFooter.Attach(row, 44f, 24f, 44f);
            if (rightText != null)
            {
                var rt = GlassKit.Text(bar, rightText, GlassText.Caption, TextAlignmentOptions.MidlineRight, "Right"); f.Right = rt;
                GlassKit.Style(rt, 20f, t.textSecondary, false, 2f);
                UiKit.Anchor(rt.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(560f, 40f));
            }
            return f;
        }

        // ================================================================================== crest tile (team select grid)

        public static GlassCrestTile MakeCrestTile(Transform parent, int index, Sprite crest, string abbr, string label, Color teamColor, Action onClick, float width = 150f, float height = 150f)
        {
            var t = T;
            var r = UiKit.Rect(parent, "Crest_" + abbr); r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var g = r.gameObject.AddComponent<GlassGraphic>(); g.raycastTarget = true; g.Radius = t.cornerRadius; g.Shadow = 0.6f; g.Density = 1f;
            var ct = r.gameObject.AddComponent<GlassCrestTile>(); ct.Surface = g; ct.Index = index;
            var btn = r.gameObject.AddComponent<Button>(); btn.targetGraphic = g; btn.transition = Selectable.Transition.None; ct.Button = btn;
            // team colour wash along the bottom of the tile
            var wash = GlassKit.Solid(r, "Wash", new Color(teamColor.r, teamColor.g, teamColor.b, 0.55f), 3f);
            UiKit.Anchor(wash.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 10f), new Vector2(width - 40f, 4f)); ct.Wash = wash;
            float cs = Mathf.Min(width, height) * 0.52f;
            var img = UiKit.Panel(r, "Crest", Color.white, crest); img.preserveAspect = true; img.enabled = crest != null;
            UiKit.Anchor(img.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -height * 0.07f), new Vector2(cs, cs)); ct.Crest = img;
            var nm = GlassKit.Eyebrow(r, label, TextAlignmentOptions.Center, "Name"); GlassKit.Style(nm, 22f, t.textSecondary, true, 1.5f);
            UiKit.Anchor(nm.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(width - 12f, 30f)); UiKit.Fit(nm, 20f); ct.Name = nm;
            // role tag (HOME / AWAY) top left
            var chip = GlassKit.Surface(r, "RoleChip", GlassKind.Glass, 15f, false, 1.4f);
            UiKit.Anchor(chip.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(92f, 32f)); ct.TagChip = chip;
            var tag = GlassKit.Eyebrow(chip.rectTransform, "", TextAlignmentOptions.Center, "Tag"); GlassKit.Style(tag, 20f, t.textPrimary, true, 1.5f); UiKit.Stretch(tag.rectTransform); ct.Tag = tag;
            chip.gameObject.SetActive(false);
            var f = r.gameObject.AddComponent<GlassFocusable>();
            f.Surface = g; f.Label = nm; f.FocusScale = 1.06f; f.LabelNormal = t.textSecondary; f.LabelFocus = Color.white;
            btn.onClick.AddListener(UiAudio.Click);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return ct;
        }

        // ================================================================================== dialog
        public sealed class DialogParts { public RectTransform Box; public TextMeshProUGUI Title, Message; public Button Yes, No; }

        /// <summary>The glass modal used by UiDialog: scrim, dense glass box with a header rule, message and two buttons (primary + secondary).</summary>
        public static DialogParts BuildDialog(RectTransform root, string title, string message, string yesLabel, string noLabel, Action yes, Action no)
        {
            var t = T;
            var p = new DialogParts();
            var scrim = UiKit.Panel(root, "Scrim", new Color(0.02f, 0.025f, 0.035f, 0.74f)); UiKit.Stretch(scrim.rectTransform); scrim.raycastTarget = true;
            var box = GlassKit.Panel(root, "Box", 1.7f, true, t.cornerRadiusLarge); p.Box = box.rectTransform;
            UiKit.Anchor(p.Box, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 440f));
            box.SetFocus(0.0f);
            p.Title = GlassKit.Text(p.Box, title.ToUpperInvariant(), GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Title");
            GlassKit.Style(p.Title, 40f, t.textPrimary, true, 4f);
            UiKit.Anchor(p.Title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -36f), new Vector2(804f, 64f)); UiKit.Fit(p.Title, 24f);
            var rule = GlassKit.Rule(p.Box); var rr = rule.rectTransform; rr.anchorMin = new Vector2(0f, 1f); rr.anchorMax = new Vector2(1f, 1f); rr.pivot = new Vector2(0.5f, 1f); rr.anchoredPosition = new Vector2(0f, -112f); rr.sizeDelta = new Vector2(-96f, 1.4f);
            p.Message = GlassKit.Text(p.Box, message, GlassText.Body, TextAlignmentOptions.TopLeft, "Message");
            GlassKit.Style(p.Message, 28f, t.textSecondary, false, 0f); p.Message.textWrappingMode = TextWrappingModes.Normal;
            UiKit.Anchor(p.Message.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(48f, -136f), new Vector2(804f, 160f));
            var row = UiKit.Rect(p.Box, "Buttons"); UiKit.Anchor(row, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 44f), new Vector2(804f, 76f));
            var h = row.gameObject.AddComponent<HorizontalLayoutGroup>(); h.spacing = 24f; h.childAlignment = TextAnchor.MiddleCenter; h.childControlWidth = false; h.childControlHeight = false; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            p.Yes = GlassKit.Button(row, yesLabel, yes, GlassButtonStyle.Primary, 390f, 76f);
            p.No = GlassKit.Button(row, noLabel, no, GlassButtonStyle.Secondary, 390f, 76f);
            return p;
        }

        // ================================================================================== dropdown
        /// <summary>A closed field showing the current value; A opens a glass popover list (up / down + A, B closes). Built for long lists (resolutions).</summary>
        public static GlassDropdown Dropdown(Transform parent, string label, string[] options, int index, Action<int> onChanged, float width = 760f, float height = 64f)
        {
            var t = T;
            var r = UiKit.Rect(parent, "Drop_" + label); r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var g = r.gameObject.AddComponent<GlassGraphic>(); g.raycastTarget = true; g.Radius = t.cornerRadiusSmall; g.Density = 0.7f; g.Shadow = 0f;
            var d = r.gameObject.AddComponent<GlassDropdown>(); d.targetGraphic = g; d.transition = Selectable.Transition.None;
            var n = GlassKit.Text(r, label.ToUpperInvariant(), GlassText.Label, TextAlignmentOptions.MidlineLeft, "Name");
            GlassKit.Style(n, 24f, t.textSecondary, true, t.trackingCaps * 0.6f); UiKit.Stretch(n.rectTransform, 28f, 0f, width * 0.5f, 0f); UiKit.Fit(n, 18f);
            var v = GlassKit.Text(r, "", GlassText.Label, TextAlignmentOptions.MidlineRight, "Value"); GlassKit.Style(v, 26f, t.textPrimary, true);
            v.rectTransform.anchorMin = new Vector2(1f, 0f); v.rectTransform.anchorMax = new Vector2(1f, 1f); v.rectTransform.pivot = new Vector2(1f, 0.5f);
            v.rectTransform.offsetMin = new Vector2(-width * 0.5f + 20f, 0f); v.rectTransform.offsetMax = new Vector2(-64f, 0f); UiKit.Fit(v, 16f);
            var chev = GlassKit.Icon(r, "ui_arrow_down", 26f, t.textMuted, "Chevron");
            UiKit.Anchor(chev.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(24f, 24f));
            var f = r.gameObject.AddComponent<GlassFocusable>();
            f.Surface = g; f.Label = n; f.FocusScale = 1.01f; f.LabelNormal = t.textSecondary; f.LabelFocus = Color.white; f.Icon = chev; f.IconNormal = t.textMuted; f.IconFocus = t.accent;
            d.ValueLabel = v;
            d.SetOptions(options, index);
            d.Changed = onChanged;
            return d;
        }
    }



}

