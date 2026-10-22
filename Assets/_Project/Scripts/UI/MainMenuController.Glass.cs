using System.Collections.Generic;
using System.Linq;
using PixFootball.Gameplay;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// The glass version of the main menu: a tile hub (top tabs HOME / PLAY / PRACTICE / OPTIONS, a hero tile, mixed-size tiles, locked "coming soon" tiles, a league news ticker, controller legend)
    /// over the live stadium with the menu player in the home team's kit. See <see cref="MainMenuHub"/> and Docs/UIStyleGuide_Glass.md.
    /// </summary>
    public partial class MainMenuController
    {
        GlassWidgets.NavBar _glassNav;
        MainMenuHub _hub;

        /// <summary>Glass theme: the section name in the nav bar follows the open screen, the hub tabs only show on the hub, and the stage framing follows the content on the screen.</summary>
        void GlassSection(string s)
        {
            if (_glassNav != null && _glassNav.Section != null) _glassNav.Section.text = s;
            if (_glassNav != null && _glassNav.Tabs != null) _glassNav.Tabs.gameObject.SetActive(s == "MAIN MENU");
            var st = GetComponent<GlassStage>();
            if (st != null) st.ContentRight = s == "MAIN MENU" ? HubContentRight : s == "SETTINGS" ? SettingsContentRight : s == "MATCHUP" ? HubContentRight : TeamContentRight;
        }

        const float HubContentRight = 96f + 1212f, SettingsContentRight = 96f + 860f, TeamContentRight = 96f + 1728f;

        void BuildGlassUi()
        {
            var t = GlassTheme.T;
            var canvas = UiKit.CreateCanvas("MenuCanvas", 10, transform);
            var root = (RectTransform)canvas.transform;
            _canvasRoot = root;

            // the last setup (or the defaults) decides the hero tile's crests and the kit of the menu player
            var teams = TeamCatalog.Teams;
            var cfg = TeamSelectPanel.InitialConfig(teams, PixSettings.LoadMatchConfig(), MatchData.Current, TeamCatalog.SavedTeamId);
            _hubHomeId = cfg.HomeId;

            GlassWidgets.SideShade(root, true, 1500f, 0.62f, "LeftShade");
            _glassNav = GlassWidgets.TopBar(root, "MAIN MENU", MainMenuHub.TabNames, 0, i => { if (_hub != null) _hub.SetPage(i); }, true);

            BuildHub(root, 0);
            // ---- settings and team select (same panels as Classic; they build their glass layout themselves)
            _settings = SettingsPanel.Build(root, CloseSettings); _settings.OnReset = RebuildSettings;
            _settingsRoot = _settings.gameObject; _settingsRoot.SetActive(false);
            _teamSelect = TeamSelectPanel.Build(root, CloseTeamSelect, StartMatchWithFade);
            _teamSelect.OnHomeChanged = DressStage;
            _teamSelect.OnScreen = GlassSection;
            _teamRoot = _teamSelect.gameObject; _teamRoot.SetActive(false);

            BuildCareerUi(root);

            // ---- footer prompts
            var foot = GlassWidgets.BottomBar(root, "PIX FOOTBALL  v" + GameplayInfo.Version);
            _footer = foot.Prompts;
            SetFooter(false);
            ReopenFranchiseIfReturning();   // a franchise game just ended: back to the franchise hub
        }

        /// <summary>Builds (or rebuilds) the tile hub on <paramref name="page"/>: the hero tile's crests come from the last exhibition setup, the CAREER tile from the newest save.</summary>
        void BuildHub(RectTransform root, int page)
        {
            var teams = TeamCatalog.Teams;
            var cfg = TeamSelectPanel.InitialConfig(teams, PixSettings.LoadMatchConfig(), MatchData.Current, TeamCatalog.SavedTeamId);
            var home = TeamCatalog.ById(cfg.HomeId); var away = TeamCatalog.ById(cfg.AwayId);
            List<string> headlines = null;
            try { headlines = MenuHubModel.Headlines(MatchData.League); }
            catch (System.Exception e) { Debug.LogWarning("[PIX] menu headlines: " + e.Message); }

            var ctx = new HubContext
            {
                PlayNow = OpenTeamSelect,
                FreeRoam = () => StartWithFade(GameSession.StartFreeRoam),
                Quit = ConfirmQuit,
                Settings = OpenSettingsAt,
                Career = OpenCareerStart, CareerSubtitle = CareerTileSubtitle(),
                Parallax = v => { var st = GetComponent<GlassStage>(); if (st != null) st.Parallax = v; },
                CrestAway = away != null ? UiKit.Sprite("crest_" + away.Id) : null, CrestHome = home != null ? UiKit.Sprite("crest_" + home.Id) : null,
                NameAway = away != null ? away.Nickname.ToUpperInvariant() : "", NameHome = home != null ? home.Nickname.ToUpperInvariant() : "",
                Headlines = headlines
            };
            _hub = MainMenuHub.Build(root, _glassNav, ctx);
            _hub.SetPage(page);
            _menuRoot = _hub.Root.gameObject; _firstButton = _hub.First; _settingsBtn = _hub.SettingsTile;
        }
        /// <summary>Hub footer: A selects, LB RB switch the tab, the stick / d-pad navigates (the sub screens show BACK instead of the tab hint).</summary>
        void SetHubFooter()
        {
            if (_footer == null) return;
            _footer.Set(PromptFooter.Item("glyph_a", "SELECT"), new PromptItem(new[] { "glyph_lb", "glyph_rb" }, "TABS"), PromptFooter.Item("glyph_analog_all", "NAVIGATE"));
        }
    }
}
