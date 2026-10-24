using System.Collections.Generic;
using PixFootball.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI.Glass
{
    /// <summary>
    /// A living style sheet of the glass library (two pages: the "franchise hub" composition from the pack's style sample and a controls sheet). Built from the same factory the screens use; the
    /// QA renders and Docs/UIStyleGuide_Glass.md show it. Pure presentation: no game state is read or written.
    /// </summary>
    public static partial class GlassGallery
    {
        public static RectTransform Build(Transform parent, int page, bool livePortrait = false)
        {
            var canvas = UiKit.CreateCanvas("GlassGallery" + page, 10, parent);
            var root = (RectTransform)canvas.transform;
            GlassGraphic.Prepare(canvas);
            if (page == 0) BuildHub(root, livePortrait); else BuildControls(root);
            return root;
        }

        static void BuildHub(RectTransform root, bool livePortrait)
        {
            var t = GlassTheme.T;
            GlassWidgets.SideShade(root, true, 980f, 0.68f);
            var nav = GlassWidgets.TopBar(root, "FRANCHISE HUB", new[] { "Overview", "Roster", "Scouting", "Playbook", "Front Office" }, 0, null, true);

            // ---- left: hero + big actions
            var col = GlassKit.Column(root, "Left", 18f, TextAnchor.UpperLeft);
            var cr = (RectTransform)col.transform; UiKit.Anchor(cr, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(t.safeMarginX, -170f), new Vector2(560f, 800f));
            var eyebrow = GlassKit.Eyebrow(cr, "SEASON 2027 / WEEK 06"); GlassKit.Size(eyebrow.gameObject, 560f, 30f);
            var hero = GlassKit.Text(cr, "BUILD YOUR\nDYNASTY", GlassText.Hero, TextAlignmentOptions.TopLeft, "Hero"); GlassKit.Style(hero, 68f, t.textPrimary, true, 2f); hero.textWrappingMode = TextWrappingModes.Normal;
            GlassKit.Size(hero.gameObject, 560f, 150f);
            var sub = GlassKit.Text(cr, "A clean, modular football interface.", GlassText.Body, TextAlignmentOptions.TopLeft, "Sub"); GlassKit.Style(sub, 28f, t.textSecondary, false, 0f); GlassKit.Size(sub.gameObject, 560f, 40f);
            GlassKit.Spacer(cr, 10f, 14f);
            var go = GlassKit.Button(cr, "CONTINUE SEASON", null, GlassButtonStyle.Primary, 560f, 84f, "ui_arrow_right"); go.name = "Continue";
            GlassKit.Button(cr, "MANAGE ROSTER", null, GlassButtonStyle.Secondary, 560f, 84f, "ui_roster");
            GlassKit.MenuItem(cr, "SCOUTING CENTER", "fb_scout_binoculars", null, 560f, 112f);

            // ---- centre: overview + priorities
            var ov = GlassWidgets.Card(root, "FRANCHISE OVERVIEW", 470f, 250f);
            UiKit.Anchor(ov.Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(700f, -170f), new Vector2(540f, 250f));
            var tiles = GlassKit.Row(ov.Body, "Tiles", 14f, TextAnchor.MiddleCenter); UiKit.Stretch((RectTransform)tiles.transform);
            tiles.childForceExpandWidth = true; tiles.childForceExpandHeight = true; tiles.childControlWidth = true; tiles.childControlHeight = true;
            foreach (var kv in new[] { new[] { "4-1", "RECORD" }, new[] { "87", "TEAM OVR" }, new[] { "$24.8M", "CAP SPACE" } })
            {
                var tile = GlassWidgets.StatTile(tiles.transform, kv[0], kv[1], 150f, 120f, kv[1] == "CAP SPACE");
                tile.GetComponent<LayoutElement>().flexibleWidth = 1f;
            }

            var pr = GlassWidgets.Card(root, "WEEKLY PRIORITIES", 470f, 360f);
            UiKit.Anchor(pr.Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(690f, -450f), new Vector2(470f, 360f));
            var list = GlassKit.Column(pr.Body, "List", 10f); UiKit.Stretch((RectTransform)list.transform);
            GlassKit.ToggleRow(list.transform, "Review scouting", true, null, 414f, 62f);
            GlassKit.ToggleRow(list.transform, "Practice focus", false, null, 414f, 62f);
            GlassKit.ToggleRow(list.transform, "Depth chart", false, null, 414f, 62f);

            // ---- right: player card
            var home = TeamCatalog.Teams.Count > 0 ? TeamCatalog.Teams[0] : null;
            var pc = GlassPlayerCard.Create(root, "Marcus", "Reed", "QB  -  FIELD GENERAL", 92, new[] { "THP", "SAC", "AWR" }, new[] { 96, 91, 89 }, 640f, 520f, home != null ? home.PrimaryUi : (Color?)null);
            UiKit.Anchor(pc.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-t.safeMarginX, -170f), new Vector2(640f, 520f));
            if (livePortrait && home != null) pc.StartLivePortrait(home.Colors);

            // ---- right lower: chips and rating bars
            var info = GlassWidgets.Card(root, "MODULAR GLASS SURFACES", 640f, 250f);
            UiKit.Anchor(info.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-t.safeMarginX, -720f), new Vector2(640f, 250f));
            var hl = GlassKit.Text(info.Body, "Thin silver outlines. Soft reflections. Open spacing.", GlassText.Body, TextAlignmentOptions.TopLeft, "Line"); GlassKit.Style(hl, 26f, t.textPrimary, false, 0f); hl.textWrappingMode = TextWrappingModes.Normal;
            UiKit.Anchor(hl.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(584f, 70f));
            var chips = GlassKit.Row(info.Body, "Chips", 12f, TextAnchor.MiddleLeft);
            UiKit.Anchor((RectTransform)chips.transform, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 6f), new Vector2(584f, 40f));
            GlassWidgets.Chip(chips.transform, "READY", true); GlassWidgets.Chip(chips.transform, "FULLY SCOUTED"); GlassWidgets.Chip(chips.transform, "LIVE");

            // ---- footer
            var foot = GlassWidgets.BottomBar(root, "PIX FOOTBALL  -  GLASS UI");
            foot.Prompts.Set(PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_b", "BACK"), PromptFooter.Item("glyph_lb", "PREVIOUS"), PromptFooter.Item("glyph_rb", "NEXT"));
        }

        static void BuildControls(RectTransform root)
        {
            var t = GlassTheme.T;
            GlassWidgets.SideShade(root, true, 700f, 0.4f);
            GlassWidgets.TopBar(root, "CONTROLS", new[] { "Buttons", "Inputs", "Feedback" }, 1);

            // left: settings-style rows
            var card = GlassWidgets.Card(root, "SETTINGS ROWS", 820f, 560f);
            UiKit.Anchor(card.Root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(t.safeMarginX, -170f), new Vector2(820f, 560f));
            var col = GlassKit.Column(card.Body, "Rows", 8f); UiKit.Stretch((RectTransform)col.transform);
            GlassKit.Stepper(col.transform, "Graphics quality", new[] { "Low", "Medium", "High", "Ultra" }, 2, null, 764f, 64f);
            GlassWidgets.Dropdown(col.transform, "Resolution", new[] { "1920 x 1080", "2560 x 1440", "3840 x 2160" }, 1, null, 764f, 64f);
            GlassKit.ToggleRow(col.transform, "Controller rumble", true, null, 764f, 64f);
            GlassKit.SliderRow(col.transform, "Master volume", 0.8f, null, 764f, 64f);
            GlassKit.SliderRow(col.transform, "Crowd volume", 0.45f, null, 764f, 64f);
            GlassKit.ToggleRow(col.transform, "Camera shake", false, null, 764f, 64f);

            // right top: buttons
            var bc = GlassWidgets.Card(root, "BUTTONS", 800f, 230f);
            UiKit.Anchor(bc.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-t.safeMarginX, -170f), new Vector2(800f, 230f));
            var br = GlassKit.Row(bc.Body, "Row", 18f, TextAnchor.MiddleLeft); UiKit.Stretch((RectTransform)br.transform);
            GlassKit.Button(br.transform, "PRIMARY", null, GlassButtonStyle.Primary, 220f, 64f);
            GlassKit.Button(br.transform, "SECONDARY", null, GlassButtonStyle.Secondary, 220f, 64f);
            GlassKit.Button(br.transform, "ACCENT", null, GlassButtonStyle.Accent, 200f, 64f);
            GlassKit.Button(br.transform, "GHOST", null, GlassButtonStyle.Ghost, 100f, 64f);

            // right middle: rating bars + stat tiles
            var rc = GlassWidgets.Card(root, "RATING BARS", 800f, 270f);
            UiKit.Anchor(rc.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-t.safeMarginX, -430f), new Vector2(800f, 270f));
            var rcol = GlassKit.Column(rc.Body, "Bars", 10f); UiKit.Stretch((RectTransform)rcol.transform);
            GlassWidgets.RatingBar(rcol.transform, "Throw power", 0.96f, "96", 744f, true);
            GlassWidgets.RatingBar(rcol.transform, "Short accuracy", 0.91f, "91", 744f, true);
            GlassWidgets.RatingBar(rcol.transform, "Awareness", 0.74f, "74", 744f, false);
            GlassWidgets.RatingBar(rcol.transform, "Speed", 0.62f, "62", 744f, false);

            // right bottom: crest tiles
            var cc = GlassWidgets.Card(root, "TEAM CREST TILES", 800f, 220f);
            UiKit.Anchor(cc.Root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-t.safeMarginX, -720f), new Vector2(800f, 220f));
            var crow = GlassKit.Row(cc.Body, "Crests", 14f, TextAnchor.MiddleLeft); UiKit.Stretch((RectTransform)crow.transform);
            for (int i = 0; i < 4 && i < TeamCatalog.Teams.Count; i++)
            {
                var tm = TeamCatalog.Teams[i];
                var tile = GlassWidgets.MakeCrestTile(crow.transform, i, UiKit.Sprite("crest_" + tm.Id), tm.Abbr, tm.Abbr, tm.PrimaryUi, null, 160f, 120f);
                if (i == 0) tile.SetRole("HOME", true); else if (i == 1) tile.SetRole("AWAY", false);
            }

            // bottom left: toast, chips
            var toast = GlassToast.Create(root, 240f); toast.SetRest(new Vector2(-380f, 220f));
            toast.ShowNow("GAME SAVED  -  your franchise is up to date", "ui_check");
            var foot = GlassWidgets.BottomBar(root, "PIX FOOTBALL  -  GLASS UI");
            foot.Prompts.Set(PromptFooter.Item("glyph_a", "SELECT"), PromptFooter.Item("glyph_b", "BACK"), PromptFooter.Item("glyph_analog_all", "NAVIGATE"));
        }
    }
}
