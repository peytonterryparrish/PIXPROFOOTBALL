using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    public enum GlassText { Caption, Label, Body, Heading, Title, Hero }
    public enum GlassButtonStyle { Primary, Secondary, Accent, Ghost }

    /// <summary>
    /// Code-built factory for the glass component library (the project builds all UI in code; the same factory bakes the prefabs under Assets/_Project/UI/Glass/Prefabs through GlassPrefabBuilder).
    /// Primitives and basic controls live here; the compound widgets (modal, toast, tooltip, cards, bars, nav) are in <see cref="GlassWidgets"/>. Everything reads <see cref="GlassTheme"/>.
    /// </summary>
    public static class GlassKit
    {
        public static GlassTokens T { get { return GlassTheme.T; } }

        // ================================================================================== text
        public static TextMeshProUGUI Text(Transform parent, string text, GlassText style = GlassText.Label, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, string name = "Text")
        {
            var t = T; var theme = GlassTheme.Current;
            var r = UiKit.Rect(parent, name);
            var tm = r.gameObject.AddComponent<TextMeshProUGUI>();
            bool bold = style == GlassText.Heading || style == GlassText.Title || style == GlassText.Hero;
            tm.font = bold ? theme.Title : theme.Body;
            switch (style)
            {
                case GlassText.Caption: tm.fontSize = t.sizeCaption; tm.color = t.textSecondary; tm.characterSpacing = 2f; break;
                case GlassText.Label: tm.fontSize = t.sizeLabel; tm.color = t.textPrimary; break;
                case GlassText.Body: tm.fontSize = t.sizeBody; tm.color = t.textPrimary; break;
                case GlassText.Heading: tm.fontSize = t.sizeHeading; tm.color = t.textPrimary; tm.characterSpacing = t.trackingCaps; break;
                case GlassText.Title: tm.fontSize = t.sizeTitle; tm.color = t.textPrimary; tm.characterSpacing = t.trackingTitle; break;
                default: tm.fontSize = t.sizeHero; tm.color = t.textPrimary; tm.characterSpacing = t.trackingTitle; break;
            }
            tm.text = text; tm.alignment = align; tm.raycastTarget = false;
            tm.textWrappingMode = TextWrappingModes.NoWrap; tm.overflowMode = TextOverflowModes.Overflow;
            return tm;
        }

        /// <summary>Bold caption-size caps label (the pack's small section titles: "FRANCHISE OVERVIEW").</summary>
        public static TextMeshProUGUI Eyebrow(Transform parent, string text, TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft, string name = "Eyebrow")
        {
            var tm = Text(parent, text.ToUpperInvariant(), GlassText.Caption, align, name);
            tm.font = GlassTheme.Current.Title; tm.characterSpacing = T.trackingCaps; tm.color = T.textSecondary;
            return tm;
        }

        public static TextMeshProUGUI Style(TextMeshProUGUI tm, float size, Color color, bool bold = false, float tracking = 0f)
        {
            tm.font = bold ? GlassTheme.Current.Title : GlassTheme.Current.Body; tm.fontSize = size; tm.color = color; tm.characterSpacing = tracking;
            return tm;
        }

        // ================================================================================== surfaces
        public static GlassGraphic Surface(Transform parent, string name, GlassKind kind = GlassKind.Glass, float radius = -1f, bool shadow = false, float density = 1f)
        {
            var r = UiKit.Rect(parent, name);
            var g = r.gameObject.AddComponent<GlassGraphic>();
            g.Kind = kind; g.Radius = radius; g.Shadow = shadow ? 1f : 0f; g.Density = density; g.raycastTarget = false;
            return g;
        }

        /// <summary>A glass panel sized by the caller (anchors / layout). <paramref name="density"/> 1 = the pack's airy glass; &gt; 1 denser (modals, hero cards).</summary>
        public static GlassGraphic Panel(Transform parent, string name = "Panel", float density = 1f, bool shadow = true, float radius = -1f)
        {
            return Surface(parent, name, GlassKind.Glass, radius, shadow, density);
        }

        /// <summary>The dark inset well used for tracks, fields and stat tiles.</summary>
        public static GlassGraphic Well(Transform parent, string name = "Well", float radius = -1f)
        {
            return Surface(parent, name, GlassKind.Well, radius < 0f ? T.cornerRadiusSmall : radius, false, 1f);
        }

        /// <summary>A flat tinted block (team colour wash, status chip): the vertex colour is the fill.</summary>
        public static GlassGraphic Solid(Transform parent, string name, Color color, float radius = -1f, float edge = 0f)
        {
            var g = Surface(parent, name, GlassKind.Solid, radius, false, 1f);
            g.color = color; g.EdgeWidth = edge; g.Sheen = 0.5f;
            return g;
        }

        /// <summary>A 1.2 unit horizontal hairline (the pack's header rule) in the edge grey.</summary>
        public static Image Rule(Transform parent, string name = "Rule", float alpha = 0.55f)
        {
            var r = UiKit.Rect(parent, name);
            var img = r.gameObject.AddComponent<Image>(); img.raycastTarget = false;
            var c = T.edgeA; c.a = alpha * 0.55f; img.color = c;
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredHeight = 1.4f; le.minHeight = 1.4f; le.flexibleWidth = 1f;
            r.sizeDelta = new Vector2(0f, 1.4f);
            return img;
        }

        public static Image Bar(Transform parent, string name, Color color, float w, float h)
        {
            var r = UiKit.Rect(parent, name); r.sizeDelta = new Vector2(w, h);
            var img = r.gameObject.AddComponent<Image>(); img.color = color; img.raycastTarget = false;
            return img;
        }

        /// <summary>Icon image from the registry (null sprite = the image stays disabled but keeps its layout size).</summary>
        public static Image Icon(Transform parent, string key, float size, Color? tint = null, string name = null)
        {
            var r = UiKit.Rect(parent, name ?? "Icon_" + key);
            r.sizeDelta = new Vector2(size, size);
            var img = r.gameObject.AddComponent<Image>();
            var s = GlassIcons.Get(key);
            img.sprite = s; img.enabled = s != null; img.preserveAspect = true; img.raycastTarget = false;
            img.color = tint ?? T.lightGrey;
            r.gameObject.AddComponent<GlassIconKey>().Key = key;
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = size; le.preferredHeight = size; le.minWidth = size; le.minHeight = size;
            return img;
        }

        public static LayoutElement Size(GameObject go, float w, float h)
        {
            var le = UiKit.GetOrAdd<LayoutElement>(go);
            if (w >= 0f) le.preferredWidth = w; if (h >= 0f) le.preferredHeight = h;
            var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(w >= 0f ? w : rt.sizeDelta.x, h >= 0f ? h : rt.sizeDelta.y);
            return le;
        }

        public static HorizontalLayoutGroup Row(Transform parent, string name, float spacing = 12f, TextAnchor align = TextAnchor.MiddleLeft, RectOffset pad = null)
        {
            var r = UiKit.Rect(parent, name);
            var h = r.gameObject.AddComponent<HorizontalLayoutGroup>();
            h.spacing = spacing; h.childAlignment = align; h.childControlWidth = true; h.childControlHeight = true; h.childForceExpandWidth = false; h.childForceExpandHeight = false;
            if (pad != null) h.padding = pad;
            return h;
        }

        public static VerticalLayoutGroup Column(Transform parent, string name, float spacing = 8f, TextAnchor align = TextAnchor.UpperLeft, RectOffset pad = null)
        {
            var r = UiKit.Rect(parent, name);
            var v = r.gameObject.AddComponent<VerticalLayoutGroup>();
            v.spacing = spacing; v.childAlignment = align; v.childControlWidth = true; v.childControlHeight = true; v.childForceExpandWidth = false; v.childForceExpandHeight = false;
            if (pad != null) v.padding = pad;
            return v;
        }

        public static void Spacer(Transform parent, float w, float h, float flexW = 0f, float flexH = 0f)
        {
            var r = UiKit.Rect(parent, "Spacer");
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = h; le.flexibleWidth = flexW; le.flexibleHeight = flexH;
        }

        // ================================================================================== button
        /// <summary>
        /// Button: Primary = silver action gradient with dark type (one per screen), Secondary = glass, Accent = orange fill, Ghost = text only until focused. The surface eases to the orange focus
        /// edge on gamepad focus / hover, the click plays the UI sound. Returns the Button (a Selectable: gamepad navigation works through the EventSystem).
        /// </summary>
        public static Button Button(Transform parent, string label, Action onClick, GlassButtonStyle style = GlassButtonStyle.Secondary, float width = 320f, float height = 64f, string iconKey = null, string name = null, float fontSize = 0f)
        {
            var t = T;
            var r = UiKit.Rect(parent, name ?? "Btn_" + label);
            r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var g = r.gameObject.AddComponent<GlassGraphic>();
            g.raycastTarget = true; g.Radius = Mathf.Min(t.cornerRadius, height * 0.5f); g.Shadow = style == GlassButtonStyle.Ghost ? 0f : 0.7f;
            g.Kind = style == GlassButtonStyle.Primary ? GlassKind.Action : style == GlassButtonStyle.Accent ? GlassKind.Accent : GlassKind.Glass;
            if (style == GlassButtonStyle.Ghost) { g.EdgeWidth = t.edgeWidth; var c = g.color; c.a = 0f; g.color = c; }
            var btn = r.gameObject.AddComponent<Button>();
            btn.targetGraphic = g; btn.transition = Selectable.Transition.None;

            Color textCol = style == GlassButtonStyle.Primary || style == GlassButtonStyle.Accent ? t.textOnAction : t.textPrimary;
            float fs = fontSize > 0f ? fontSize : Mathf.Clamp(height * 0.38f, 20f, 40f);
            TextMeshProUGUI txt;
            Image icon = null;
            if (!string.IsNullOrEmpty(iconKey))
            {
                float isz = Mathf.Min(height * 0.5f, 40f);
                icon = Icon(r, iconKey, isz, textCol);
                UiKit.Anchor(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(height * 0.4f, 0f), new Vector2(isz, isz));
                txt = Text(r, label, GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Label");
                Style(txt, fs, textCol, true, t.trackingCaps * 0.6f);
                UiKit.Stretch(txt.rectTransform, height * 0.4f + isz + 14f, 0f, 14f, 0f);
            }
            else
            {
                txt = Text(r, label, GlassText.Heading, TextAlignmentOptions.Center, "Label");
                Style(txt, fs, textCol, true, t.trackingCaps * 0.6f);
                UiKit.Stretch(txt.rectTransform, 12f, 0f, 12f, 0f);
            }
            UiKit.Fit(txt, fs * 0.6f);

            var f = r.gameObject.AddComponent<GlassFocusable>();
            f.Surface = g; f.Label = txt; f.Icon = icon; f.FocusScale = t.focusScale;
            f.KeepLabelColor = style == GlassButtonStyle.Primary || style == GlassButtonStyle.Accent;
            f.LabelNormal = style == GlassButtonStyle.Ghost ? t.textSecondary : t.textPrimary; f.LabelFocus = Color.white;
            f.IconNormal = textCol; f.IconFocus = f.KeepLabelColor ? textCol : Color.white;
            if (style == GlassButtonStyle.Ghost) { f.GhostSurface = true; f.FocusBrighten = 0f; }
            btn.onClick.AddListener(UiAudio.Click);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        /// <summary>Wide menu row for the main menu / pause rail: icon in a glass well, caption, chevron; focus = orange edge, accent rail and a nudge to the right.</summary>
        public static Button MenuItem(Transform parent, string label, string iconKey, Action onClick, float width = 560f, float height = 88f, string name = null)
        {
            var t = T;
            var r = UiKit.Rect(parent, name ?? "Menu_" + label);
            r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var g = r.gameObject.AddComponent<GlassGraphic>(); g.raycastTarget = true; g.Radius = t.cornerRadius; g.Shadow = 0.8f;
            var btn = r.gameObject.AddComponent<Button>(); btn.targetGraphic = g; btn.transition = Selectable.Transition.None;

            float pad = height * 0.18f, box = height - pad * 2f;
            var well = Well(r, "IconWell", t.cornerRadiusSmall);
            UiKit.Anchor(well.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(pad, 0f), new Vector2(box, box));
            Image icon = null;
            if (!string.IsNullOrEmpty(iconKey))
            {
                icon = Icon(well.rectTransform, iconKey, box * 0.62f, t.lightGrey);
                UiKit.Anchor(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(box * 0.62f, box * 0.62f));
            }
            var txt = Text(r, label, GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Label");
            Style(txt, Mathf.Clamp(height * 0.36f, 24f, 38f), t.textSecondary, true, t.trackingCaps);
            UiKit.Stretch(txt.rectTransform, pad * 2f + box + 8f, 0f, 70f, 0f);
            UiKit.Fit(txt, 22f);
            var chev = Icon(r, "ui_arrow_right", box * 0.5f, t.textSecondary);
            UiKit.Anchor(chev.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-pad - 6f, 0f), new Vector2(box * 0.5f, box * 0.5f));
            var rail = Solid(r, "Rail", t.accent, 2f);
            UiKit.Anchor(rail.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(-1f, 0f), new Vector2(4f, height * 0.5f));
            var rc = rail.color; rc.a = 0f; rail.color = rc;

            var f = r.gameObject.AddComponent<GlassFocusable>();
            f.Surface = g; f.Label = txt; f.Icon = chev; f.Rail = rail; f.FocusScale = 1.015f;
            f.LabelNormal = t.textSecondary; f.LabelFocus = Color.white; f.IconNormal = t.textMuted; f.IconFocus = t.accent;
            btn.onClick.AddListener(UiAudio.Click);
            if (onClick != null) btn.onClick.AddListener(() => onClick());
            return btn;
        }

        // ================================================================================== tabs
        /// <summary>Underline tabs (the pack's navigation): the active tab is white with an orange underline, the others grey; a focused tab gets a soft glass chip. Left / right moves, A selects.</summary>
        public static GlassTabs Tabs(Transform parent, string[] names, int selected, Action<int> onChanged, float tabHeight = 56f, float fontSize = 24f, string name = "Tabs")
        {
            var t = T;
            var row = Row(parent, name, 6f, TextAnchor.MiddleLeft);
            var tabs = row.gameObject.AddComponent<GlassTabs>();
            tabs.Buttons = new Button[names.Length]; tabs.Labels = new TextMeshProUGUI[names.Length]; tabs.Underlines = new Image[names.Length]; tabs.Chips = new GlassGraphic[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                int idx = i;
                var r = UiKit.Rect(row.transform, "Tab_" + names[i]);
                var lbl = Text(r, names[i].ToUpperInvariant(), GlassText.Heading, TextAlignmentOptions.Center, "Label");
                Style(lbl, fontSize, t.textSecondary, true, t.trackingCaps * 0.7f);
                float w = Mathf.Max(96f, lbl.GetPreferredValues().x + 44f);
                r.sizeDelta = new Vector2(w, tabHeight);
                var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = w; le.preferredHeight = tabHeight;
                var chip = r.gameObject.AddComponent<GlassGraphic>(); chip.raycastTarget = true; chip.Radius = t.cornerRadiusSmall; chip.Shadow = 0f;
                var cc = chip.color; cc.a = 0f; chip.color = cc;
                UiKit.Stretch(lbl.rectTransform, 6f, 4f, 6f, 8f);
                var ul = Bar(r, "Underline", t.accent, 0f, 3f);
                UiKit.Anchor(ul.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 6f), new Vector2(w - 44f, 3f));
                var b = r.gameObject.AddComponent<Button>(); b.targetGraphic = chip; b.transition = Selectable.Transition.None;
                var f = r.gameObject.AddComponent<GlassFocusable>();
                f.Surface = chip; f.Label = lbl; f.GhostSurface = true; f.FocusScale = 1f; f.FocusBrighten = 0f; f.KeepLabelColor = true; f.PlaySound = true;
                b.onClick.AddListener(() => { UiAudio.Click(); tabs.SetIndex(idx, true); });
                tabs.Buttons[i] = b; tabs.Labels[i] = lbl; tabs.Underlines[i] = ul; tabs.Chips[i] = chip;
            }
            tabs.Changed = onChanged;
            tabs.SetIndex(Mathf.Clamp(selected, 0, names.Length - 1), false);
            return tabs;
        }

        // ================================================================================== toggle
        /// <summary>Settings-style row with a pill switch (orange knob when on). Left / right or A flips it.</summary>
        public static GlassToggle ToggleRow(Transform parent, string label, bool value, Action<bool> onChanged, float width = 760f, float height = 64f)
        {
            var t = T;
            var r = UiKit.Rect(parent, "Toggle_" + label);
            r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var g = r.gameObject.AddComponent<GlassGraphic>(); g.raycastTarget = true; g.Radius = t.cornerRadiusSmall; g.Density = 0.7f; g.Shadow = 0f;
            var tg = r.gameObject.AddComponent<GlassToggle>();
            tg.targetGraphic = g; tg.transition = Selectable.Transition.None;
            var txt = Text(r, label.ToUpperInvariant(), GlassText.Label, TextAlignmentOptions.MidlineLeft, "Label");
            Style(txt, 24f, t.textSecondary, true, t.trackingCaps * 0.6f); UiKit.Stretch(txt.rectTransform, 28f, 0f, 150f, 0f); UiKit.Fit(txt, 18f);

            var track = Surface(r, "Track", GlassKind.Well, 17f, false, 1f);
            UiKit.Anchor(track.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(72f, 34f));
            var fill = Surface(track.rectTransform, "On", GlassKind.Accent, 17f, false, 1f); UiKit.Stretch(fill.rectTransform);
            var knob = Surface(track.rectTransform, "Knob", GlassKind.Action, 13f, true, 1f);
            UiKit.Anchor(knob.rectTransform, new Vector2(0f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(17f, 0f), new Vector2(26f, 26f));
            knob.EdgeWidth = 0f;
            tg.Track = track; tg.OnFill = fill; tg.Knob = knob.rectTransform; tg.Label = txt;
            var f = r.gameObject.AddComponent<GlassFocusable>();
            f.Surface = g; f.Label = txt; f.FocusScale = 1.01f; f.LabelNormal = t.textSecondary; f.LabelFocus = Color.white; f.PlaySound = true;
            tg.Set(value, false);
            tg.Changed = onChanged;
            return tg;
        }

        // ================================================================================== slider
        /// <summary>Settings-style slider row: label, a slim well track with an orange fill and a silver knob, the value as percent. Left / right moves the value, like the classic slider.</summary>
        public static Slider SliderRow(Transform parent, string label, float value, Action<float> onChanged, float width = 760f, float height = 64f, string valueFormat = null)
        {
            var t = T;
            var r = UiKit.Rect(parent, "Slider_" + label);
            r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var g = r.gameObject.AddComponent<GlassGraphic>(); g.raycastTarget = true; g.Radius = t.cornerRadiusSmall; g.Density = 0.7f; g.Shadow = 0f;
            var txt = Text(r, label.ToUpperInvariant(), GlassText.Label, TextAlignmentOptions.MidlineLeft, "Label");
            Style(txt, 24f, t.textSecondary, true, t.trackingCaps * 0.6f); UiKit.Stretch(txt.rectTransform, 28f, 0f, width * 0.55f, 0f); UiKit.Fit(txt, 18f);
            var slider = r.gameObject.AddComponent<Slider>();

            float trackW = width * 0.36f;
            var track = Surface(r, "Track", GlassKind.Well, 5f, false, 1f);
            UiKit.Anchor(track.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-110f, 0f), new Vector2(trackW, 10f));
            var fillArea = UiKit.Rect(track.rectTransform, "FillArea"); UiKit.Stretch(fillArea);
            var fill = Surface(fillArea, "Fill", GlassKind.Accent, 5f, false, 1f);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(0f, 1f); fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = Vector2.zero;
            var handleArea = UiKit.Rect(track.rectTransform, "HandleArea"); UiKit.Stretch(handleArea, 14f, 0f, 14f, 0f);
            var knob = Surface(handleArea, "Knob", GlassKind.Action, 14f, true, 1f); knob.EdgeWidth = 0f; knob.raycastTarget = true;
            knob.rectTransform.sizeDelta = new Vector2(28f, 28f);
            var val = Text(r, "", GlassText.Label, TextAlignmentOptions.MidlineRight, "Value"); Style(val, 24f, t.textPrimary, true);
            UiKit.Anchor(val.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(84f, height));

            slider.fillRect = fill.rectTransform; slider.handleRect = knob.rectTransform; slider.targetGraphic = knob;
            slider.direction = Slider.Direction.LeftToRight; slider.minValue = 0f; slider.maxValue = 1f; slider.wholeNumbers = false; slider.value = value;
            slider.transition = Selectable.Transition.None;
            Func<float, string> fmt = x => valueFormat == null ? Mathf.RoundToInt(x * 100f) + "%" : string.Format(valueFormat, x);
            val.text = fmt(value);
            slider.onValueChanged.AddListener(x => { val.text = fmt(x); if (onChanged != null) onChanged(x); });
            var f = r.gameObject.AddComponent<GlassFocusable>();
            f.Surface = g; f.Label = txt; f.FocusScale = 1.01f; f.LabelNormal = t.textSecondary; f.LabelFocus = Color.white; f.PlaySound = true;
            return slider;
        }

        // ================================================================================== stepper (left / right value selector)
        /// <summary>
        /// Value selector row: label, chevrons and the current value. Built on the classic <see cref="OptionSelector"/> so Left / Right, A, LB / RB paging and pointer clicks behave exactly as before.
        /// </summary>
        public static OptionSelector Stepper(Transform parent, string label, string[] options, int index, Action<int> onChanged, float width = 760f, float height = 64f)
        {
            var t = T;
            var r = UiKit.Rect(parent, "Opt_" + label);
            r.sizeDelta = new Vector2(width, height);
            var le = r.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var g = r.gameObject.AddComponent<GlassGraphic>(); g.raycastTarget = true; g.Radius = t.cornerRadiusSmall; g.Density = 0.7f; g.Shadow = 0f;
            var sel = r.gameObject.AddComponent<OptionSelector>();
            sel.targetGraphic = g; sel.transition = Selectable.Transition.None; sel.PlainValue = true;
            var n = Text(r, label.ToUpperInvariant(), GlassText.Label, TextAlignmentOptions.MidlineLeft, "Name");
            Style(n, 24f, t.textSecondary, true, t.trackingCaps * 0.6f); UiKit.Stretch(n.rectTransform, 28f, 0f, width * 0.5f + 14f, 0f); UiKit.Fit(n, 16f);
            float chevSize = 22f;
            var left = Icon(r, "ui_arrow_left", chevSize, t.textMuted, "ChevL");
            UiKit.Anchor(left.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-width * 0.5f + 24f, 0f), new Vector2(chevSize, chevSize));
            var right = Icon(r, "ui_arrow_right", chevSize, t.textMuted, "ChevR");
            UiKit.Anchor(right.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-24f, 0f), new Vector2(chevSize, chevSize));
            var v = Text(r, "", GlassText.Label, TextAlignmentOptions.Center, "Value");
            Style(v, 26f, t.textPrimary, true);
            v.rectTransform.anchorMin = new Vector2(1f, 0f); v.rectTransform.anchorMax = new Vector2(1f, 1f); v.rectTransform.pivot = new Vector2(1f, 0.5f);
            v.rectTransform.offsetMin = new Vector2(-width * 0.5f + 48f, 0f); v.rectTransform.offsetMax = new Vector2(-24f - chevSize - 8f, 0f);
            UiKit.Fit(v, 16f);
            sel.NameLabel = n; sel.ValueLabel = v;
            sel.SetOptions(options, index);
            if (onChanged != null) sel.Changed += onChanged;
            var f = r.gameObject.AddComponent<GlassFocusable>();
            f.Surface = g; f.Label = n; f.FocusScale = 1.01f; f.LabelNormal = t.textSecondary; f.LabelFocus = Color.white; f.PlaySound = false;   // OptionSelector plays its own move sound
            f.Icon = null;
            var chev = r.gameObject.AddComponent<GlassChevronTint>(); chev.Left = left; chev.Right = right; chev.Focus = f; chev.Selector = sel;
            return sel;
        }

        // ================================================================================== progress
        public static GlassBar ProgressBar(Transform parent, float value, float width = 320f, float height = 10f, bool accent = true, string name = "Progress")
        {
            var t = T;
            var track = Surface(parent, name, GlassKind.Well, height * 0.5f, false, 1f);
            track.rectTransform.sizeDelta = new Vector2(width, height);
            var le = track.gameObject.AddComponent<LayoutElement>(); le.preferredWidth = width; le.preferredHeight = height;
            var fillArea = UiKit.Rect(track.rectTransform, "FillArea"); UiKit.Stretch(fillArea);
            var fill = Surface(fillArea, "Fill", accent ? GlassKind.Accent : GlassKind.Action, height * 0.5f, false, 1f);
            fill.rectTransform.anchorMin = Vector2.zero; fill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(value), 1f); fill.rectTransform.offsetMin = Vector2.zero; fill.rectTransform.offsetMax = Vector2.zero;
            fill.EdgeWidth = 0f;
            var bar = track.gameObject.AddComponent<GlassBar>(); bar.Fill = fill.rectTransform; bar.Value = Mathf.Clamp01(value);
            return bar;
        }
    }
}
