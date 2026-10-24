using System.Collections.Generic;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// The living style sheet of Direction 16 "Tile Hub Modern" (Docs/QA/v0110/ui_wp1.md, shots in D:\PIXFootball\Renders\ui16\): five pages built from the same factory the screens use. Pure presentation, no game state,
    /// procedural art only (no licensed or third-party pictures).
    /// Pages: 0 hub, 1 franchise (hero, schedule, standings), 2 play-call strip, 3 controls (option rows, buttons, rows, modal, toast), 4 states (rest / focus / pressed / locked / disabled).
    /// </summary>
    public static partial class GlassGallery
    {
        public const int TilePages = 5;
        static readonly string[] Wire = { "Ironhorns lock up top seed with Week 14 win", "Stormguard QB cleared to start Sunday", "Trade deadline: Goldclaws land veteran edge rusher", "Skybolts clinch division for third straight season", "Reefrunners sign rookie corner to a four-year deal" };

        /// <summary>Builds a Tile gallery page on its own canvas (the caller forces the Tile theme first).</summary>
        public static RectTransform BuildTile(Transform parent, int page)
        {
            var canvas = UiKit.CreateCanvas("TileGallery" + page, 10, parent);
            var root = (RectTransform)canvas.transform;
            GlassGraphic.Prepare(canvas);
            GlassKit.TileBackdrop(root);
            switch (page)
            {
                case 0: TileHub(root); break;
                case 1: TileFranchise(root); break;
                case 2: TilePlayCall(root); break;
                case 3: TileControls(root); break;
                default: TileStates(root); break;
            }
            return root;
        }

        // ------------------------------------------------------------------ art stand-ins (procedural: gradients, stripes and soft blobs)
        public static Texture2D Plate(int seed, Color a, Color b, int w = 512, int h = 288)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.HideAndDontSave };
            var rnd = new System.Random(seed);
            float cx = 0.25f + (float)rnd.NextDouble() * 0.5f, cy = 0.2f + (float)rnd.NextDouble() * 0.4f;
            var px = new Color[w * h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                float u = x / (float)w, v = y / (float)h;
                float d = Mathf.Sqrt((u - cx) * (u - cx) * 1.6f + (v - cy) * (v - cy));
                float k = Mathf.Clamp01(1f - d * 1.6f);
                float stripe = 0.5f + 0.5f * Mathf.Sin((u * 1.2f + v) * 38f + seed);
                var c = Color.Lerp(a, b, v * 0.8f + stripe * 0.08f);
                c = Color.Lerp(c, Color.Lerp(b, Color.white, 0.35f), k * 0.55f);
                c.a = 1f; px[y * w + x] = c;
            }
            tex.SetPixels(px); tex.Apply();
            return tex;
        }

        static void Foot(RectTransform root, string right, params PromptItem[] items)
        {
            var foot = GlassWidgets.BottomBar(root, right);
            foot.Prompts.Set(items);
            GlassKit.Ticker(root, Wire);
        }

        static HubTile T(RectTransform block, string title, string sub, float x, float y, float w, float h, string badge = null, string icon = null, Texture art = null, bool locked = false, bool hero = false, bool flat = false, bool focus = false, Vector2? focal = null)
        {
            var spec = new HubTileSpec { Title = title, Subtitle = sub, Badge = badge, IconKey = icon, ArtTexture = art, Locked = locked, Hero = hero, Flat = flat, Pos = new Vector2(x, y), Size = new Vector2(w, h), ArtFocal = focal ?? new Vector2(0.5f, 0.4f), Click = () => { } };
            var tile = GlassKit.Tile(block, spec, 0f, null);
            var rev = tile.Cell.GetComponent<GlassReveal>(); if (rev != null) Object.DestroyImmediate(rev);
            var cg = tile.Cell.GetComponent<CanvasGroup>(); if (cg != null) cg.alpha = 1f;
            if (focus && tile.Button != null) tile.Button.GetComponent<GlassFocusable>().ShowState(1f, true);
            return tile;
        }

        static RectTransform Block(RectTransform root, Vector2 size, float top = 140f)
        {
            var host = UiKit.Rect(root, "FitHost");
            host.anchorMin = Vector2.zero; host.anchorMax = Vector2.one; host.offsetMin = new Vector2(60f, TileMetrics.BottomClear + 14f); host.offsetMax = new Vector2(-60f, -top);
            var block = UiKit.Rect(host, "Block");
            block.anchorMin = block.anchorMax = new Vector2(0.5f, 0.5f); block.pivot = new Vector2(0.5f, 0.5f); block.anchoredPosition = Vector2.zero; block.sizeDelta = size;
            var fs = host.gameObject.AddComponent<FitScale>(); fs.Block = block; fs.DesignSize = size;
            return block;
        }

        // ------------------------------------------------------------------ page 0: hub
        static void TileHub(RectTransform root)
        {
            var nav = GlassWidgets.TopBar(root, "MAIN MENU", new[] { "Home", "Play", "Share", "Customize", "Store" }, 0, null, true);
            GlassWidgets.SetPlayerChip(nav, "Coach Marlow", "Lvl 27", "48,250 PX", null);
            var block = Block(root, new Vector2(1800f, 784f));
            var navy = new Color(0.10f, 0.16f, 0.30f); var green = new Color(0.10f, 0.30f, 0.16f); var steel = new Color(0.20f, 0.22f, 0.28f);
            T(block, "Play Now", "Jump into a quick match", 0, 0, 780, 470, "FEATURED", null, Plate(1, navy, steel), false, true, false, true, new Vector2(0.5f, 0.35f));
            T(block, "Franchise", "Season 3 \u2022 Week 6", 0, 492, 380, 292, "RESUME", null, Plate(2, green, new Color(0.06f, 0.18f, 0.09f)));
            T(block, "Superstar", "Coming soon", 400, 492, 380, 292, null, "fb_helmet_side", null, true);
            T(block, "Online Head to Head", "7 people are playing", 800, 0, 480, 290, "LIVE", null, Plate(3, new Color(0.14f, 0.28f, 0.14f), new Color(0.05f, 0.1f, 0.06f)));
            T(block, "Mini Games", "Skills & drills", 800, 312, 230, 288, null, "fb_goalpost", null, true, false, true);
            T(block, "Trophy Room", "3 rings \u2022 12 banners", 1050, 312, 230, 288, null, null, Plate(4, new Color(0.45f, 0.32f, 0.10f), new Color(0.12f, 0.09f, 0.05f)));
            T(block, "My PIX", "Create a team, uniform or play", 800, 622, 480, 162, "NEW", "fb_helmet_side", null, false, false, true);
            T(block, "Weekly Challenge", "Win 3 games with a passing offense", 1300, 0, 500, 250, "+2,500 PX", null, Plate(5, new Color(0.28f, 0.12f, 0.16f), new Color(0.07f, 0.07f, 0.12f)));
            T(block, "Settings", "Controls & sound", 1300, 272, 240, 238, null, "fb_coach_headset", null, false, false, true);
            T(block, "Practice", "Drills & plays", 1560, 272, 240, 238, null, "fb_depth_chart", null, false, false, true);
            T(block, "Share & Community", "Replays, highlights and shared uniforms", 1300, 532, 500, 252, null, "fb_film_reel", null, false, false, true);
            Foot(root, "PIX PRO FOOTBALL  \u2022  WEEK 6", PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_b", "BACK"), PromptFooter.Item("glyph_x", "STORE"), PromptFooter.Item("glyph_y", "PROFILE"), PromptFooter.Item("glyph_lb", "PREV TAB"), PromptFooter.Item("glyph_rb", "NEXT TAB"));
        }

        // ------------------------------------------------------------------ page 1: franchise
        static void TileFranchise(RectTransform root)
        {
            var nav = GlassWidgets.TopBar(root, "FRANCHISE", new[] { "Home", "News", "Actions", "Owner", "Team", "League" }, 0, null, true);
            GlassWidgets.SetPlayerChip(nav, "Coach Marlow", "Lvl 27", "48,250 PX", null);
            var t = GlassTheme.T;
            var block = Block(root, new Vector2(1800f, 790f), 132f);
            var head = GlassKit.Text(block, "CONNECTED FRANCHISE   <size=22><color=#" + ColorUtility.ToHtmlStringRGB(t.textMuted) + ">SEASON 3  \u2022  WEEK 6  \u2022  IRONHORNS</color></size>", GlassText.Heading, TextAlignmentOptions.TopLeft, "Head");
            GlassKit.Style(head, t.sizePanelTitle, Color.white, true, GlassTokens.Em(6f, t.sizePanelTitle)); head.richText = true;
            UiKit.Anchor(head.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(1500f, 44f));
            T(block, "Advance to Week 7", "Next up: Sunday 4:25 PM vs Reefrunners \u2022 Highmark Field", 0, 54, 900, 420, "PRIMARY ACTION", null, Plate(6, new Color(0.10f, 0.30f, 0.14f), new Color(0.04f, 0.12f, 0.06f)), false, true, false, true, new Vector2(0.5f, 0.45f));
            T(block, "Schedule", "Week 7 vs MIA \u2022 Week 8 @ NYS", 0, 496, 290, 262, null, "fb_calendar_grid", null, false, false, true);
            T(block, "Media Statements", "3 new press requests", 310, 496, 290, 262, "3", null, Plate(7, new Color(0.25f, 0.14f, 0.12f), new Color(0.07f, 0.07f, 0.1f)));
            T(block, "Goals", "Win East \u2022 Make playoffs", 620, 496, 280, 262, null, "fb_achievement_medal", null, false, false, true);
            var card = GlassWidgets.Card(block, "LEAGUE STANDINGS", 870f, 700f, "Standings", true, 1f, true);
            UiKit.Anchor(card.Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(930f, -54f), new Vector2(870f, 700f));
            var col = GlassKit.Column(card.Body, "Groups", 6f, TextAnchor.UpperLeft); UiKit.Stretch((RectTransform)col.transform);
            var hd = new[] { "", "W", "L", "PCT", "PF", "PA", "STRK" }; var wt = new[] { 3.6f, 0.7f, 0.7f, 1f, 0.9f, 0.9f, 0.9f };
            GlassKit.TableBlock(col.transform, "Liberty East", hd, new[] { new[] { "1   IRONHORNS", "5", "1", ".833", "106", "95", "W1" }, new[] { "2   SENTINELS", "5", "1", ".833", "127", "102", "L2" }, new[] { "3   REEFRUNNERS", "2", "4", ".333", "92", "132", "W3" }, new[] { "4   SKYBOLTS", "2", "4", ".333", "129", "89", "L4" } }, wt, 818f, 0);
            GlassKit.TableBlock(col.transform, "Liberty North", hd, new[] { new[] { "1   FOUNDRY", "5", "1", ".833", "102", "103", "W1" }, new[] { "2   NIGHTWINGS", "3", "3", ".500", "98", "93", "L2" }, new[] { "3   EMBERCATS", "2", "4", ".333", "156", "90", "W3" } }, wt, 818f, -1);
            Foot(root, "SEASON 3  \u2022  WEEK 6  \u2022  TRADE DEADLINE IN 2 WEEKS", PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_b", "BACK"), PromptFooter.Item("glyph_x", "ROSTER"), PromptFooter.Item("glyph_y", "TRADE BLOCK"), PromptFooter.Item("glyph_lb", "PREV TAB"), PromptFooter.Item("glyph_rb", "NEXT TAB"));
        }

        // ------------------------------------------------------------------ page 2: play-call strip
        static void TilePlayCall(RectTransform root)
        {
            var t = GlassTheme.T;
            // stand-in for the live field behind the strip
            var field = GlassKit.ArtCover(root, null, Plate(9, new Color(0.14f, 0.34f, 0.16f), new Color(0.05f, 0.14f, 0.07f), 1024, 576), 1920f, 1080f, new Vector2(0.5f, 0.4f), 1f, "Field");
            var fr = field.rectTransform; fr.anchorMin = Vector2.zero; fr.anchorMax = Vector2.one; fr.offsetMin = fr.offsetMax = Vector2.zero; fr.sizeDelta = Vector2.zero;
            // scorebug and weather
            var bug = GlassKit.Solid(root, "Scorebug", new Color(0.047f, 0.051f, 0.063f, 0.88f), 0f, 1f);
            UiKit.Anchor(bug.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -26f), new Vector2(720f, 62f));
            var bt = GlassKit.Text(bug.rectTransform, "BUF <color=#" + ColorUtility.ToHtmlStringRGB(t.goldHi) + ">14</color>     MIA <color=#" + ColorUtility.ToHtmlStringRGB(t.goldHi) + ">10</color>     <size=24><color=#C1C5CF>3RD  4:32  \u2022  2ND & 6</color></size>", GlassText.Heading, TextAlignmentOptions.MidlineLeft, "Text");
            GlassKit.Style(bt, 32f, Color.white, true, 0f); bt.richText = true; UiKit.Stretch(bt.rectTransform, 24f, 0f, 12f, 0f);
            var wx = GlassKit.Solid(root, "Weather", new Color(0.047f, 0.051f, 0.063f, 0.94f), 0f, 1f);
            UiKit.Anchor(wx.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -22f), new Vector2(350f, 112f));
            var w1 = GlassKit.Text(wx.rectTransform, "NIGHT \u2022 CLEAR", GlassText.Heading, TextAlignmentOptions.TopLeft, "W1"); GlassKit.Style(w1, 24f, Color.white, true, GlassTokens.Em(2f, 24f)); UiKit.Anchor(w1.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -22f), new Vector2(210f, 32f));
            var w2 = GlassKit.Text(wx.rectTransform, "62\u00B0F  WIND CALM", GlassText.Caption, TextAlignmentOptions.TopLeft, "W2"); GlassKit.Style(w2, 20f, t.textMuted, true, GlassTokens.Em(1.5f, 20f)); UiKit.Anchor(w2.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -60f), new Vector2(220f, 30f));
            var w3 = GlassKit.Text(wx.rectTransform, "0:24", GlassText.Title, TextAlignmentOptions.MidlineRight, "Clock"); GlassKit.Style(w3, 44f, t.goldHi, true, 0f); UiKit.Anchor(w3.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-22f, 0f), new Vector2(110f, 60f));
            // side list (right)
            var side = GlassKit.Solid(root, "SidePanel", new Color(0.047f, 0.051f, 0.063f, 0.94f), 0f, 2f);
            UiKit.Anchor(side.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-40f, -146f), new Vector2(480f, 58f + 3 * 126f + 48f));
            var sh = GlassKit.Solid(side.rectTransform, "Head", t.panelHeadAlt, 0f, 0f); sh.rectTransform.anchorMin = new Vector2(0f, 1f); sh.rectTransform.anchorMax = new Vector2(1f, 1f); sh.rectTransform.pivot = new Vector2(0.5f, 1f); sh.rectTransform.offsetMin = new Vector2(2f, -60f); sh.rectTransform.offsetMax = new Vector2(-2f, -2f);
            var st = GlassKit.Text(sh.rectTransform, "GUN DOUBLES OFFSET WK", GlassText.Heading, TextAlignmentOptions.Center, "Title"); GlassKit.Style(st, 26f, Color.white, true, GlassTokens.Em(3f, 26f)); UiKit.Stretch(st.rectTransform); UiKit.Fit(st, 22f);
            string[][] rows = { new[] { "PASS", "Slant", "Personnel: 1RB | 1TE | 3WR", "Concept: Slant" }, new[] { "RUN", "Inside Zone", "Personnel: 1RB | 1TE | 3WR", "Concept: Inside Zone" }, new[] { "PASS", "PA Bubble Y Over", "Personnel: 1RB | 1TE | 3WR", "Concept: PA Bubble" } };
            for (int i = 0; i < 3; i++)
            {
                var row = GlassKit.ListRow(side.rectTransform, rows[i][1], rows[i][2] + "\n" + rows[i][3], 476f, 126f, null, true, "Row" + i);
                row.SetTag(rows[i][0], rows[i][0] == "RUN" ? TagKind.Run : TagKind.Pass);
                var rr = (RectTransform)row.transform; rr.anchorMin = rr.anchorMax = new Vector2(0.5f, 1f); rr.pivot = new Vector2(0.5f, 1f); rr.anchoredPosition = new Vector2(0f, -(60f + i * 126f));
                row.LayoutOff();
                var th = GlassKit.Plate(row.Thumb, "Diagram", Color.white, GlassKit.VGradient(t.fieldTop, t.fieldBottom)); UiKit.Stretch(th.rectTransform);
                if (i == 0) row.Focus.ShowState(1f, true);
            }
            var legend = UiKit.Rect(side.rectTransform, "Legend"); legend.anchorMin = new Vector2(0f, 0f); legend.anchorMax = new Vector2(1f, 0f); legend.pivot = new Vector2(0.5f, 0f); legend.sizeDelta = new Vector2(-4f, 48f); legend.anchoredPosition = new Vector2(0f, 2f);
            var lbg = GlassKit.Plate(legend, "Bg", t.panelHead); UiKit.Stretch(lbg.rectTransform);
            var lrow = UiKit.Rect(legend, "Prompts"); UiKit.Stretch(lrow, 18f, 0f, 18f, 0f);
            PromptFooter.Attach(lrow, 30f, 20f, 26f).Set(PromptFooter.Item("glyph_x", "FLIP PLAY"), PromptFooter.Item("glyph_y", "SUBS"), PromptFooter.Item("glyph_rt", "RANDOM"));
            // strip
            var strip = GlassKit.Plate(root, "Strip", Color.white, GlassKit.VGradient(new Color(0.039f, 0.043f, 0.051f, 0.94f), new Color(0.055f, 0.059f, 0.071f, 0.72f)));
            strip.rectTransform.anchorMin = new Vector2(0f, 0f); strip.rectTransform.anchorMax = new Vector2(1f, 0f); strip.rectTransform.pivot = new Vector2(0.5f, 0f); strip.rectTransform.anchoredPosition = Vector2.zero; strip.rectTransform.sizeDelta = new Vector2(0f, 440f);
            var rowH = GlassKit.Row(root, "StripTabsRow", 10f, TextAnchor.MiddleLeft); var tabRow = (RectTransform)rowH.transform;
            tabRow.anchorMin = tabRow.anchorMax = new Vector2(0f, 1f); tabRow.pivot = new Vector2(0f, 1f); tabRow.anchoredPosition = new Vector2(50f, -652f);
            var rfit = tabRow.gameObject.AddComponent<ContentSizeFitter>(); rfit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; rfit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var lb = GlassKit.GlyphCap(tabRow, "LB", 32f); var lble = lb.gameObject.AddComponent<LayoutElement>(); lble.preferredWidth = lb.sizeDelta.x; lble.preferredHeight = 32f;
            var tabs = GlassKit.StripTabs(tabRow, new[] { "Suggestions", "Gameplan", "Player", "Formation", "Concept", "Play Type", "Personnel", "Favorites" }, 0, null);
            var rb = GlassKit.GlyphCap(tabRow, "RB", 32f); var rble = rb.gameObject.AddComponent<LayoutElement>(); rble.preferredWidth = rb.sizeDelta.x; rble.preferredHeight = 32f;
            // cards
            string[] names = { "PA Cross", "Inside Zone", "Slant" }; var tags = new[] { TagKind.Pass, TagKind.Run, TagKind.Pass }; string[] forms = { "Singleback Ace", "Gun Doubles", "Gun Doubles" };
            for (int i = 0; i < 3; i++)
            {
                var pc = GlassKit.PlayCard(root, names[i], tags[i], forms[i], 406f, 286f, null);
                pc.Root.anchorMin = pc.Root.anchorMax = new Vector2(0f, 1f); pc.Root.pivot = new Vector2(0f, 1f); pc.Root.anchoredPosition = new Vector2(70f + i * 438f, i == 0 ? -722f : -728f);
                pc.Root.GetComponent<LayoutElement>().ignoreLayout = true;
                if (i == 0) pc.Focus.ShowState(1f, true);
            }
            // selected play panel
            var sel = GlassKit.Panel(root, "SelectedPlay", 1f, true); sel.Kind = GlassKind.Panel;
            UiKit.Anchor(sel.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-50f, 62f + 20f), new Vector2(498f, 286f)); sel.rectTransform.anchorMin = sel.rectTransform.anchorMax = new Vector2(1f, 1f); sel.rectTransform.pivot = new Vector2(1f, 1f); sel.rectTransform.anchoredPosition = new Vector2(-50f, -722f);
            var s1 = GlassKit.Eyebrow(sel.rectTransform, "SELECTED PLAY"); GlassKit.Style(s1, 20f, t.gold, true, GlassTokens.Em(3f, 20f)); UiKit.Anchor(s1.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -16f), new Vector2(440f, 28f));
            var s2 = GlassKit.Text(sel.rectTransform, "PA CROSS", GlassText.Title, TextAlignmentOptions.TopLeft, "Name"); GlassKit.Style(s2, t.sizeSelectedPlay, Color.white, true, 0f); UiKit.Anchor(s2.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -40f), new Vector2(450f, 58f));
            var s3 = GlassKit.Text(sel.rectTransform, "Singleback Ace \u2022 12 Personnel \u2022 Play Action", GlassText.Body, TextAlignmentOptions.TopLeft, "Meta"); GlassKit.Style(s3, 20f, new Color(0.682f, 0.698f, 0.737f), false, 0f); UiKit.Anchor(s3.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -94f), new Vector2(450f, 28f)); UiKit.Fit(s3, 20f);
            var bars = GlassKit.Column(sel.rectTransform, "Bars", 4f); UiKit.Anchor((RectTransform)bars.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -128f), new Vector2(450f, 120f));
            GlassWidgets.RatingBar(bars.transform, "Run/Pass balance", 0.38f, "38", 450f, true, 36f); GlassWidgets.RatingBar(bars.transform, "Deep threat", 0.74f, "74", 450f, true, 36f); GlassWidgets.RatingBar(bars.transform, "Protection", 0.82f, "82", 450f, true, 36f);
            var bf = GlassKit.Eyebrow(sel.rectTransform, "BEATS MAN COVERAGE \u2022 STRUGGLES VS BLITZ"); GlassKit.Style(bf, 20f, t.textMuted, true, GlassTokens.Em(1.5f, 20f)); UiKit.Anchor(bf.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 14f), new Vector2(450f, 28f)); UiKit.Fit(bf, 20f);
            // footer without ticker
            var foot = GlassWidgets.BottomBar(root, "PLAY CLOCK 0:24");
            foot.Prompts.Set(PromptFooter.Item("glyph_a", "SELECT PLAY"), PromptFooter.Item("glyph_x", "FLIP PLAY"), PromptFooter.Item("glyph_y", "AUDIBLE"), PromptFooter.Item("glyph_rt", "TEMPO"), PromptFooter.Item("glyph_b", "BACK"));
        }

        // ------------------------------------------------------------------ page 3: controls
        static void TileControls(RectTransform root)
        {
            var t = GlassTheme.T;
            GlassWidgets.TopBar(root, "SETTINGS", new[] { "Video", "Gameplay", "Camera", "Controls", "Audio" }, 1, null, true);
            var card = GlassWidgets.Card(root, "OPTION ROWS", 860f, 560f, "Options", true, 1f, true);
            UiKit.Anchor(card.Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(60f, -142f), new Vector2(860f, 560f));
            var col = GlassKit.Column(card.Body, "Rows", 4f); UiKit.Stretch((RectTransform)col.transform);
            GlassKit.Stepper(col.transform, "Graphics quality", new[] { "Low", "Medium", "High", "Ultra" }, 2, null, 810f, 64f).GetComponent<GlassFocusable>().ShowState(1f, true);
            GlassWidgets.Dropdown(col.transform, "Resolution", new[] { "1920 x 1080", "2560 x 1440", "3840 x 2160" }, 1, null, 810f, 64f);
            GlassKit.ToggleRow(col.transform, "Controller rumble", true, null, 810f, 64f);
            GlassKit.SliderRow(col.transform, "Master volume", 0.8f, null, 810f, 64f);
            GlassKit.SliderRow(col.transform, "Crowd volume", 0.45f, null, 810f, 64f);
            GlassKit.ToggleRow(col.transform, "Camera shake", false, null, 810f, 64f);
            // buttons + tags + chips
            var bc = GlassWidgets.Card(root, "BUTTONS AND TAGS", 900f, 230f, "Buttons", true, 1f);
            UiKit.Anchor(bc.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-60f, -142f), new Vector2(900f, 230f));
            var br = GlassKit.Row(bc.Body, "Row", 18f, TextAnchor.MiddleLeft); UiKit.Stretch((RectTransform)br.transform);
            GlassKit.Button(br.transform, "PRIMARY", null, GlassButtonStyle.Primary, 210f, 64f); GlassKit.Button(br.transform, "SECONDARY", null, GlassButtonStyle.Secondary, 230f, 64f); GlassKit.Button(br.transform, "GHOST", null, GlassButtonStyle.Ghost, 140f, 64f);
            var tg = GlassKit.Row(bc.Body, "Tags", 12f, TextAnchor.MiddleLeft); UiKit.Anchor((RectTransform)tg.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 4f), new Vector2(820f, 36f));
            GlassKit.TagChip(tg.transform, "PASS", TagKind.Pass); GlassKit.TagChip(tg.transform, "RUN", TagKind.Run); GlassWidgets.Chip(tg.transform, "FEATURED", true); GlassWidgets.Chip(tg.transform, "LIVE", false);
            // list rows
            var lc = UiKit.Rect(root, "Rows"); UiKit.Anchor(lc, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-60f, -392f), new Vector2(900f, 260f));
            for (int i = 0; i < 2; i++)
            {
                var lr = GlassKit.ListRow(lc, i == 0 ? "Depth chart" : "Free agency", i == 0 ? "Starters and backups by position" : "12 players available \u2022 $24.8M cap space", 900f, 124f, null, false, "R" + i);
                var rr = (RectTransform)lr.transform; rr.anchorMin = rr.anchorMax = new Vector2(0f, 1f); rr.pivot = new Vector2(0f, 1f); rr.anchoredPosition = new Vector2(0f, -i * 128f); lr.LayoutOff();
                if (i == 0) lr.Focus.ShowState(1f, true); else lr.SetTag("NEW", TagKind.Run);
            }
            // modal + toast
            var toast = GlassToast.Create(root, 150f); toast.SetRest(new Vector2(-640f, 150f));
            toast.ShowNow("GAME SAVED  \u2022  your franchise is up to date", "ui_check");
            var md = GlassWidgets.Modal(root, "KEEP THESE SETTINGS?", 900f, 300f);
            md.Scrim.gameObject.SetActive(false);
            md.Box.anchorMin = md.Box.anchorMax = new Vector2(1f, 0f); md.Box.pivot = new Vector2(1f, 0f); md.Box.anchoredPosition = new Vector2(-60f, 140f);
            var mt = GlassKit.Text(md.Body, "If the picture is unreadable, do nothing: the previous mode comes back by itself.", GlassText.Body, TextAlignmentOptions.TopLeft, "Msg"); GlassKit.Style(mt, 26f, t.textSecondary, false, 0f); mt.textWrappingMode = TextWrappingModes.Normal; UiKit.Stretch(mt.rectTransform, 0f, 100f, 0f, 0f);
            var mr = GlassKit.Row(md.Body, "Btns", 20f, TextAnchor.LowerLeft); UiKit.Anchor((RectTransform)mr.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(800f, 70f));
            GlassKit.Button(mr.transform, "KEEP", null, GlassButtonStyle.Primary, 260f, 64f).GetComponent<GlassFocusable>().ShowState(1f, true); GlassKit.Button(mr.transform, "REVERT", null, GlassButtonStyle.Secondary, 260f, 64f);
            Foot(root, "SETTINGS  \u2022  AUDIO", PromptFooter.Item("glyph_a", "CHANGE"), PromptFooter.Item("glyph_b", "BACK"), PromptFooter.Item("glyph_lb", "PREV PAGE"), PromptFooter.Item("glyph_rb", "NEXT PAGE"));
        }

        // ------------------------------------------------------------------ page 4: states
        static void TileStates(RectTransform root)
        {
            var t = GlassTheme.T;
            GlassWidgets.TopBar(root, "STATES", new[] { "Idle", "Focus", "Active", "Disabled" }, 2, null, true);
            var block = Block(root, new Vector2(1800f, 790f), 132f);
            var navy = new Color(0.10f, 0.16f, 0.30f); var steel = new Color(0.20f, 0.22f, 0.28f);
            string[] lab = { "REST", "FOCUS", "PRESSED", "LOCKED", "ART" };
            for (int i = 0; i < 5; i++)
            {
                var lbl = GlassKit.Eyebrow(block, lab[i]); GlassKit.Style(lbl, 20f, t.textMuted, true, GlassTokens.Em(3f, 20f)); UiKit.Anchor(lbl.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i * 360f, 0f), new Vector2(300f, 26f));
                var tile = T(block, "Franchise", "Season 3 \u2022 Week 6", i * 360f, 36f, 330f, 252f, i == 4 ? "RESUME" : null, i == 4 ? null : "fb_franchise_hub", i == 4 ? Plate(21, navy, steel) : null, i == 3, false, i != 4, i == 1);
                if (i == 2 && tile.Button != null) { tile.Button.GetComponent<GlassFocusable>().ShowState(1f, true); tile.Button.transform.localScale = new Vector3(t.pressScale, t.pressScale, 1f); }
            }
            for (int i = 0; i < 4; i++)
            {
                string[] rl = { "REST", "FOCUS", "SELECTED", "DISABLED" };
                var lr = GlassKit.ListRow(block, "Play name", "Personnel: 1RB | 1TE | 3WR", 420f, 124f, null, false, "State" + i);
                var rr = (RectTransform)lr.transform; rr.anchorMin = rr.anchorMax = new Vector2(0f, 1f); rr.pivot = new Vector2(0f, 1f); rr.anchoredPosition = new Vector2(i * 440f, -340f); lr.LayoutOff();
                lr.SetTag(rl[i], i % 2 == 0 ? TagKind.Pass : TagKind.Run);
                if (i == 1) lr.Focus.ShowState(1f, true); if (i == 2) lr.Selected = true; if (i == 2) lr.Focus.ShowState(1f, true); if (i == 3) lr.Focus.ShowState(0f, false);
            }
            for (int i = 0; i < 4; i++)
            {
                var b = GlassKit.Button(block, i == 3 ? "DISABLED" : i == 1 ? "FOCUSED" : "PRIMARY", null, i == 2 ? GlassButtonStyle.Secondary : GlassButtonStyle.Primary, 280f, 72f);
                var rr = (RectTransform)b.transform; rr.anchorMin = rr.anchorMax = new Vector2(0f, 1f); rr.pivot = new Vector2(0f, 1f); rr.anchoredPosition = new Vector2(i * 330f, -530f);
                var f = b.GetComponent<GlassFocusable>(); if (i == 1) f.ShowState(1f, true); if (i == 3) f.ShowState(0f, false);
            }
            var c1 = GlassKit.Stepper(block, "Camera", new[] { "Broadcast", "Sideline" }, 0, null, 800f, 64f); var r1 = (RectTransform)c1.transform; r1.anchorMin = r1.anchorMax = new Vector2(0f, 1f); r1.pivot = new Vector2(0f, 1f); r1.anchoredPosition = new Vector2(0f, -640f); c1.GetComponent<GlassFocusable>().ShowState(1f, true);
            var c2 = GlassKit.ToggleRow(block, "Rumble", true, null, 800f, 64f); var r2 = (RectTransform)c2.transform; r2.anchorMin = r2.anchorMax = new Vector2(0f, 1f); r2.pivot = new Vector2(0f, 1f); r2.anchoredPosition = new Vector2(900f, -640f);
            Foot(root, "STATES", PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_b", "BACK"));
        }
    }
}
