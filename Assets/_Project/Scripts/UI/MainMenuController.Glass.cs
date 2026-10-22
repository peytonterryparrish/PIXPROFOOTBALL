using PixFootball.Gameplay;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>The glass version of the main menu: a soft ramp over the live stadium, the nav bar, a hero line, four glass menu rows and a description card (Glass theme; see Docs/UIStyleGuide_Glass.md).</summary>
    public partial class MainMenuController
    {
        GlassWidgets.NavBar _glassNav;
        /// <summary>Glass theme: the section name in the nav bar follows the open screen.</summary>
        void GlassSection(string s) { if (_glassNav != null && _glassNav.Section != null) _glassNav.Section.text = s; }

        void BuildGlassUi()
        {
            var t = GlassTheme.T;
            var canvas = UiKit.CreateCanvas("MenuCanvas", 10, transform);
            var root = (RectTransform)canvas.transform;
            _canvasRoot = root;

            GlassWidgets.SideShade(root, true, 1300f, 0.82f, "LeftShade");
            _glassNav = GlassWidgets.TopBar(root, "MAIN MENU");

            // ---- main menu column ----
            var menu = UiKit.Rect(root, "MainMenu"); UiKit.Stretch(menu); _menuRoot = menu.gameObject;
            // the column is laid out for a 1080 high canvas; the FitScale host shrinks it on shorter canvases (21:9, 4:3 with a letterboxed match height) so the description card never runs into the footer
            var fitHost = UiKit.Rect(menu, "FitHost"); UiKit.Stretch(fitHost, 0f, 96f, 0f, 0f);
            var block = UiKit.Rect(fitHost, "Block"); block.anchorMin = block.anchorMax = new Vector2(0f, 1f); block.pivot = new Vector2(0f, 1f); block.anchoredPosition = Vector2.zero; block.sizeDelta = new Vector2(1100f, 960f);
            var fs = fitHost.gameObject.AddComponent<FitScale>(); fs.Block = block; fs.DesignSize = new Vector2(1100f, 960f);
            float x = t.safeMarginX;
            var eyebrow = GlassKit.Eyebrow(block, "VERSION " + GameplayInfo.Version + "   -   NIGHT GAME", TextAlignmentOptions.MidlineLeft, "Build");
            GlassKit.Style(eyebrow, 22f, t.textSecondary, true, 4f);
            UiKit.Anchor(eyebrow.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -176f), new Vector2(700f, 32f));
            var hero = GlassKit.Text(block, "PLAY\n<color=#" + ColorUtility.ToHtmlStringRGB(t.accent) + ">FOOTBALL</color>", GlassText.Hero, TextAlignmentOptions.TopLeft, "Hero");
            GlassKit.Style(hero, 88f, t.textPrimary, true, 3f); hero.textWrappingMode = TextWrappingModes.Normal; hero.lineSpacing = -8f;
            UiKit.Anchor(hero.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -214f), new Vector2(900f, 190f));

            var col = GlassKit.Column(block, "Buttons", 12f, TextAnchor.UpperLeft);
            var cr = (RectTransform)col.transform;
            UiKit.Anchor(cr, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -432f), new Vector2(620f, 400f));
            _firstButton = GlassKit.MenuItem(cr, "PLAY NOW", "fb_football_laces", OpenTeamSelect, 620f, 88f);
            var roam = GlassKit.MenuItem(cr, "FREE ROAM", "fb_field_compass", () => StartWithFade(GameSession.StartFreeRoam), 620f, 88f);
            var settingsBtn = GlassKit.MenuItem(cr, "SETTINGS", "ui_settings", OpenSettings, 620f, 88f);
            var quit = GlassKit.MenuItem(cr, "QUIT", "ui_close", ConfirmQuit, 620f, 88f);
            _settingsBtn = settingsBtn;

            // description of the highlighted entry
            var card = GlassKit.Panel(block, "DescCard", 1f, true);
            UiKit.Anchor(card.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -846f), new Vector2(620f, 104f));
            _desc = GlassKit.Text(card.rectTransform, "", GlassText.Body, TextAlignmentOptions.MidlineLeft, "Desc");
            GlassKit.Style(_desc, 24f, t.textSecondary, false, 0f); _desc.textWrappingMode = TextWrappingModes.Normal;
            UiKit.Stretch(_desc.rectTransform, 28f, 8f, 28f, 8f);
            Describe(_firstButton, "Pick two teams, the rules and the weather, then play a full game.");
            Describe(roam, "Run around the empty stadium: test the moves, the cameras and the lighting.");
            Describe(settingsBtn, "Video, gameplay, camera, broadcast and audio options.");
            Describe(quit, "Close the game.");
            _desc.text = "Pick two teams, the rules and the weather, then play a full game.";

            // ---- settings and team select (same panels as Classic; they build their glass layout themselves) ----
            _settings = SettingsPanel.Build(root, CloseSettings); _settings.OnReset = RebuildSettings;
            _settingsRoot = _settings.gameObject; _settingsRoot.SetActive(false);
            _teamSelect = TeamSelectPanel.Build(root, CloseTeamSelect, StartMatchWithFade);
            _teamRoot = _teamSelect.gameObject; _teamRoot.SetActive(false);

            // ---- footer prompts ----
            var foot = GlassWidgets.BottomBar(root, "PIX FOOTBALL  v" + GameplayInfo.Version);
            _footer = foot.Prompts;
            SetFooter(false);
        }
    }
}
