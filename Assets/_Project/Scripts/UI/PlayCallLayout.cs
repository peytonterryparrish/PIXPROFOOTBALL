using System.Collections.Generic;
using PixFootball.Core;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>Every measured rectangle of the play-call screen for one canvas size (reference units, y measured from the top / bottom as each field says).</summary>
    public struct PlayCallRects
    {
        public float CanvasW, CanvasH;
        /// <summary>Left edge and width of the formation / set list.</summary>
        public float ListX, ListW;
        /// <summary>Left edge of the three-card block (and of the preview), its width and the single card width / height.</summary>
        public float CardsX, CardsW, CardW, CardGap, CardH;
        /// <summary>Top of the content block (below the tabs / breadcrumb) and its height down to the page counter.</summary>
        public float ContentTop, ContentH;
        /// <summary>Diagram height inside a card and the height left for the text under the tag.</summary>
        public float DiagramH, InfoH;
        /// <summary>Preview (formation / set stage): the pitch drawing and the text column beside it.</summary>
        public float PreviewH, PreviewDiagramW, PreviewDiagramH, PreviewTextX, PreviewTextW;
        /// <summary>Top bar: the team / title text, the score column and the left edge of the timeout button.</summary>
        public float TeamX, TeamW, ScoreX, ScoreW, TimeoutLeft;
        /// <summary>Footer: its height for the given number of prompt rows and the width its prompts may fill.</summary>
        public float FooterH, FooterAvail;
        /// <summary>Width the right-aligned type filters may use before they would run into the breadcrumb.</summary>
        public float FilterAvail;
    }

    /// <summary>
    /// The responsive layout of the play-call screen (QA-775 / 776 / 777 / 778 / 779): every size is computed from the canvas size, so the screen fills 4:3, 16:10, 16:9 and 21:9 canvases
    /// without cutting a card or a prompt off. Pure functions; no Unity objects are touched.
    /// </summary>
    public static class PlayCallLayout
    {
        public const float RefW = 1920f, RefH = 1080f, Margin = 80f;
        public const float TopBarH = 112f, ContentTopY = 262f, BaseFooterH = 84f, PromptRowH = 52f, PageTextBand = 56f;
        public const float MaxListW = 520f, MinListW = 400f, MaxCardW = 420f, MinCardW = 260f, MaxCardH = 760f, MinCardH = 420f;

        /// <summary>
        /// Size of the canvas in reference units for a screen, for a CanvasScaler in "match width or height" mode (the project uses 1920x1080 and 0.5).
        /// 4:3 gives about 1663 x 1247, 16:9 1920 x 1080, 21:9 about 2200 x 943.
        /// </summary>
        public static Vector2 CanvasSizeFor(float screenW, float screenH, float match = -1f)
        {
            if (screenW <= 0f || screenH <= 0f) return new Vector2(RefW, RefH);
            // v0.7: the project's scaler is "Expand" (match < 0): scale = the smaller of the two axis ratios, so the reference rectangle always fits; 0..1 = the old match-width-or-height maths
            float scale = match < 0f ? Mathf.Min(screenW / RefW, screenH / RefH) : Mathf.Pow(screenW / RefW, 1f - match) * Mathf.Pow(screenH / RefH, match);
            return new Vector2(screenW / scale, screenH / scale);
        }

        /// <summary>The root canvas' RectTransform of <paramref name="c"/> (null while it is not under a canvas); its rect is the canvas size in reference units.</summary>
        public static RectTransform RootCanvasRect(Component c)
        {
            var canvas = c != null ? c.GetComponentInParent<Canvas>() : null;
            return canvas != null ? (RectTransform)canvas.rootCanvas.transform : null;
        }

        /// <summary>Layout for a canvas of <paramref name="w"/> x <paramref name="h"/> reference units with <paramref name="footerRows"/> rows of footer prompts.</summary>
        public static PlayCallRects Compute(float w, float h, int footerRows = 1, float crumbWidth = 360f)
        {
            if (w < 800f) w = 800f;
            if (h < 600f) h = 600f;
            var r = new PlayCallRects { CanvasW = w, CanvasH = h };
            r.CardGap = 28f;
            r.ListW = Mathf.Clamp(MaxListW - Mathf.Max(0f, RefW - w) * 0.5f, MinListW, MaxListW);
            float gap = w >= 1900f ? 50f : 36f;
            float availBlock = w - 2f * Margin - r.ListW - gap;
            r.CardW = Mathf.Clamp(Mathf.Floor((availBlock - 2f * r.CardGap) / 3f), MinCardW, MaxCardW);
            r.CardsW = 3f * r.CardW + 2f * r.CardGap;
            float contentW = r.ListW + gap + r.CardsW;
            r.ListX = Mathf.Max(Margin, Mathf.Floor((w - contentW) * 0.5f));
            r.CardsX = r.ListX + r.ListW + gap;

            r.FooterH = BaseFooterH + Mathf.Max(0, footerRows - 1) * PromptRowH;
            r.FooterAvail = w - 2f * Margin;
            r.ContentTop = ContentTopY;
            r.ContentH = Mathf.Max(200f, h - ContentTopY - (r.FooterH + PageTextBand));
            r.CardH = Mathf.Clamp(r.ContentH, MinCardH, MaxCardH);
            // header 100 + tag zone 62 + reason 58 + at least 170 of text sit around the diagram
            r.DiagramH = Mathf.Clamp(r.CardH - 390f, 150f, 400f);
            r.InfoH = Mathf.Max(60f, r.CardH - (100f + r.DiagramH + 62f) - 58f);

            r.PreviewH = Mathf.Min(r.ContentH, 690f);
            r.PreviewDiagramW = Mathf.Clamp(Mathf.Floor(r.CardsW * 0.54f), 360f, 642f);
            r.PreviewDiagramH = Mathf.Clamp(r.PreviewH - 48f, 300f, 560f);
            r.PreviewTextX = 24f + r.PreviewDiagramW + 34f;
            r.PreviewTextW = Mathf.Max(240f, r.CardsW - r.PreviewTextX - 24f);

            // top bar, from the right: clock block (150) at the margin, the timeout button (300) 20 px left of it, the score column left of the button
            r.TimeoutLeft = w - 550f;
            r.ScoreW = 300f;
            r.ScoreX = r.TimeoutLeft - 24f - r.ScoreW;
            r.TeamX = Margin + 276f;
            r.TeamW = Mathf.Max(260f, r.ScoreX - 24f - r.TeamX);

            r.FilterAvail = Mathf.Clamp(w - 2f * Margin - crumbWidth - 24f, 300f, 1000f);
            return r;
        }

        /// <summary>
        /// Scale (0.7..1) that makes <paramref name="total"/> fit into <paramref name="avail"/>; 1 when it already fits. Used to shrink tab / filter chips instead of letting them overlap (QA-779).
        /// </summary>
        public static float FitScale(float total, float avail)
        {
            if (total <= avail || total <= 0f) return 1f;
            return Mathf.Clamp(avail / total, 0.7f, 1f);
        }
    }

    /// <summary>Line breaking for a row of footer / hint prompts (QA-775 / 786): too many prompts for the width go on a second row instead of running off the screen.</summary>
    public static class PromptWrap
    {
        /// <summary>
        /// Greedy break of items of the given widths into rows no wider than <paramref name="avail"/> (items are separated by <paramref name="spacing"/>). Returns the index of the first
        /// item of every row; a single item wider than the row stays alone on its row. At most <paramref name="maxRows"/> rows (the last row takes the rest).
        /// </summary>
        public static int[] Break(IList<float> widths, float spacing, float avail, int maxRows = 3)
        {
            var starts = new List<int> { 0 };
            if (widths == null || widths.Count == 0) return starts.ToArray();
            float x = 0f;
            for (int i = 0; i < widths.Count; i++)
            {
                float w = widths[i];
                float need = (x > 0f ? spacing : 0f) + w;
                if (x > 0f && x + need > avail && starts.Count < maxRows) { starts.Add(i); x = w; }
                else x += need;
            }
            return starts.ToArray();
        }

        /// <summary>
        /// Fills <paramref name="container"/> with prompts laid out in as many centred / left aligned rows as <paramref name="availWidth"/> requires and returns the row count.
        /// The container gets a VerticalLayoutGroup; every row is a HorizontalLayoutGroup built by <see cref="UiPrompts.FillRow(RectTransform, IList{PromptItem}, float, float, float)"/>.
        /// </summary>
        public static int Fill(RectTransform container, IList<PromptItem> items, float size, float font, float spacing, float availWidth, TextAnchor align = TextAnchor.MiddleCenter, int maxRows = 3)
        {
            for (int i = container.childCount - 1; i >= 0; i--) { var c = container.GetChild(i).gameObject; c.SetActive(false); Object.Destroy(c); }
            var vg = UiKit.GetOrAdd<VerticalLayoutGroup>(container.gameObject);
            vg.spacing = 6f; vg.childAlignment = align == TextAnchor.MiddleLeft ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;
            vg.childControlWidth = true; vg.childControlHeight = true; vg.childForceExpandWidth = true; vg.childForceExpandHeight = false;

            var first = UiKit.Rect(container, "Row0");
            UiPrompts.FillRow(first, items, size, font, spacing);
            var h0 = first.GetComponent<HorizontalLayoutGroup>(); if (h0 != null) h0.childAlignment = align;
            if (items.Count < 2) return 1;

            LayoutRebuilder.ForceRebuildLayoutImmediate(first);
            int n = first.childCount;
            var widths = new float[n];
            for (int i = 0; i < n; i++) widths[i] = LayoutUtility.GetPreferredWidth((RectTransform)first.GetChild(i));
            int[] starts = Break(widths, spacing, availWidth, maxRows);
            if (starts.Length < 2) return 1;

            var kids = new List<Transform>(n);
            for (int i = 0; i < n; i++) kids.Add(first.GetChild(i));
            for (int r = 1; r < starts.Length; r++)
            {
                var row = UiKit.Rect(container, "Row" + r);
                var hl = row.gameObject.AddComponent<HorizontalLayoutGroup>();
                hl.spacing = spacing; hl.childAlignment = align; hl.childControlWidth = true; hl.childControlHeight = true; hl.childForceExpandWidth = false; hl.childForceExpandHeight = false;
                int end = r + 1 < starts.Length ? starts[r + 1] : n;
                for (int i = starts[r]; i < end; i++) kids[i].SetParent(row, false);
            }
            return starts.Length;
        }
    }

    /// <summary>The small category chip of a play (RUN, PASS, PLAY ACTION, ZONE, GOAL LINE...): which word, which colour (QA-780).</summary>
    public static class PlayTags
    {
        public static string For(Play p)
        {
            switch (p.Category)
            {
                case PlayCategory.InsideRun: case PlayCategory.OutsideRun: case PlayCategory.Draw: case PlayCategory.QbRun: return "RUN";
                case PlayCategory.PlayAction: return "PLAY ACTION";
                case PlayCategory.Screen: case PlayCategory.ShortPass: case PlayCategory.MediumPass: case PlayCategory.DeepPass: return "PASS";
                case PlayCategory.ZoneCoverage: return "ZONE";
                case PlayCategory.Prevent: return "PREVENT";
                case PlayCategory.ManCoverage: return "MAN";
                case PlayCategory.Blitz: return "BLITZ";
                case PlayCategory.GoalLine: return "GOAL LINE";
                case PlayCategory.Punt: return "PUNT";
                case PlayCategory.FieldGoal: case PlayCategory.PointAfter: case PlayCategory.FieldGoalBlock: return "FG";
                case PlayCategory.Onside: return "ONSIDE";
                case PlayCategory.KickReturn: case PlayCategory.PuntReturn: return "RETURN";
                case PlayCategory.Kneel: case PlayCategory.Spike: return "CLOCK";
                default: return "KICK";
            }
        }

        public static Color ColorOf(string tag, Color fallback)
        {
            switch (tag)
            {
                case "RUN": return UiColors.Hex("#FA7805"); case "PASS": return UiColors.Hex("#054EED"); case "RPO": case "PLAY ACTION": return UiColors.Hex("#05ACED");
                case "ZONE": return UiColors.Hex("#00287D"); case "PREVENT": return UiColors.Hex("#3A5FA8"); case "MAN": return UiColors.Hex("#FFEA00");
                case "BLITZ": return UiColors.Hex("#C20000"); case "GOAL LINE": return UiColors.Hex("#7A2B0C");
                case "PUNT": return UiColors.Hex("#B845FF"); case "FG": return UiColors.Hex("#11F055");
                case "ONSIDE": return UiColors.Hex("#B8501F"); case "RETURN": return UiColors.Hex("#1F6F5C"); case "CLOCK": return UiColors.Hex("#4A4A55"); case "KICK": return UiColors.Hex("#3A3A3A");
                default: return fallback;
            }
        }

        public static Color ColorOf(string tag) { return ColorOf(tag, UiColors.Hex("#3A3A3A")); }

        public static Color TextColorOf(string tag) { return tag == "MAN" || tag == "FG" ? Color.black : Color.white; }

        /// <summary>Key of the tag sprite ("tag_run", "tag_play_action"); null-sprite tags fall back to a text chip.</summary>
        public static string SpriteKey(string tag) { return "tag_" + tag.ToLowerInvariant().Replace(' ', '_'); }
    }
}
