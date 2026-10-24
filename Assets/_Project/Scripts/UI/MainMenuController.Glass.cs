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
    /// The themed (Glass / Tile) version of the main menu: a tile hub (top tabs HOME / PLAY / SETTINGS, a hero PLAY NOW tile, mixed-size tiles, locked "coming soon" tiles, a league news ticker, controller legend).
    /// v0.11: every game is a watch game (CPU against CPU), there is no menu player and no college / Road to Heisman mode. See <see cref="MainMenuHub"/>, <see cref="MenuHubPlan"/> and the direction 16 spec.
    /// </summary>
    public partial class MainMenuController
    {
        GlassWidgets.NavBar _glassNav;
        MainMenuHub _hub;

        /// <summary>The 3D menu player only belongs to the Classic menu: the tile hubs fill the screen (the crests of the matchup are the picture).</summary>
        bool StageInMenu { get { return !UiTheme.IsGlass; } }

        /// <summary>Themed: the section name in the top bar follows the open screen, the hub tabs only show on the hub, and the stage framing follows the content on the screen.</summary>
        void GlassSection(string s)
        {
            if (_glassNav != null && _glassNav.Section != null) _glassNav.Section.text = s;
            if (_glassNav != null && _glassNav.Tabs != null) { var tabsGo = _glassNav.Tabs.BarRoot != null ? _glassNav.Tabs.BarRoot.gameObject : _glassNav.Tabs.gameObject; tabsGo.SetActive(s == "MAIN MENU"); }
            if (_glassNav != null && _glassNav.Section != null && _glassNav.Tabs != null && GlassTheme.T.flat) _glassNav.Section.gameObject.SetActive(s != "MAIN MENU");
            var st = GetComponent<GlassStage>();
            if (st != null) st.ContentRight = s == "MAIN MENU" ? HubContentRight : s == "SETTINGS" ? SettingsContentRight : s == "MATCHUP" ? HubContentRight : TeamContentRight;
            SetScreenFooter(s);
        }

        const float HubContentRight = 96f + 1212f, SettingsContentRight = 96f + 860f, TeamContentRight = 96f + 1728f;

        /// <summary>The legend of the screens this partial owns (the career and franchise screens set their own through their hosts): B BACK where it goes back, X swaps the team slot, LB RB switch tabs / pages.</summary>
        void SetScreenFooter(string section)
        {
            if (_footer == null) return;
            var l = new List<PromptItem>();
            switch (section)
            {
                case "MAIN MENU": SetHubFooter(); return;
                case "TEAM SELECT":
                    l.Add(PromptFooter.Item("glyph_a", "SELECT")); l.Add(PromptFooter.Item("glyph_b", "BACK")); l.Add(PromptFooter.Item("glyph_x", "SWAP HOME / AWAY"));
                    l.Add(new PromptItem(new[] { "glyph_lb", "glyph_rb" }, "TEAMS / OPTIONS")); l.Add(PromptFooter.Item("glyph_analog_all", "NAVIGATE")); break;
                case "MATCHUP":
                    l.Add(PromptFooter.Item("glyph_a", "SELECT")); l.Add(PromptFooter.Item("glyph_b", "BACK")); l.Add(PromptFooter.Item("glyph_analog_all", "NAVIGATE")); break;
                case "SETTINGS":
                    l.Add(PromptFooter.Item("glyph_a", "SELECT")); l.Add(PromptFooter.Item("glyph_b", "BACK"));
                    l.Add(new PromptItem(new[] { "glyph_lb", "glyph_rb" }, "PREV / NEXT PAGE")); l.Add(PromptFooter.Item("glyph_analog_all", "NAVIGATE")); break;
                default: return;
            }
            _footer.SetItems(l);
        }

        /// <summary>Tile top bar: a section label where the tabs are hidden (the sub screens) and the player chip (coach, last team and crest) at the right end.</summary>
        void DressTopBar(GlassTokens t, IReadOnlyList<TeamInfo> teams)
        {
            if (_glassNav == null || _glassNav.Root == null) return;
            if (_glassNav.Section == null)
            {
                var sec = GlassKit.Text(_glassNav.Root, "", GlassText.Heading, TextAlignmentOptions.Center, "Section");
                GlassKit.Style(sec, t.sizeTab, t.textTab, true, GlassTokens.Em(3f, t.sizeTab));
                UiKit.Anchor(sec.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(900f, 54f));
                sec.gameObject.SetActive(false);
                _glassNav.Section = sec;
            }
            CareerSlotMeta latest = null;
            try { latest = CareerSession.Latest(); } catch { }
            var team = latest != null ? TeamCatalog.ById(latest.TeamKey) : TeamCatalog.ById(TeamCatalog.SavedTeamId);
            GlassWidgets.SetPlayerChip(_glassNav, "Coach", team != null ? team.Name : "", null, team != null ? UiKit.Sprite("crest_" + team.Id) : null);
        }

        void BuildGlassUi()
        {
            var t = GlassTheme.T;
            var canvas = UiKit.CreateCanvas("MenuCanvas", 10, transform);
            var root = (RectTransform)canvas.transform;
            _canvasRoot = root;

            // the last setup (or the defaults) decides the hero tile's crests
            var teams = TeamCatalog.Teams;
            var cfg = TeamSelectPanel.InitialConfig(teams, PixSettings.LoadMatchConfig(), MatchData.Current, TeamCatalog.SavedTeamId);
            _hubHomeId = cfg.HomeId;

            bool tile = t.flat;
            if (tile) GlassKit.TileBackdrop(root);   // Direction 16: the charcoal screen backdrop covers the 3D scene
            else GlassWidgets.SideShade(root, true, 1500f, 0.62f, "LeftShade");
            _glassNav = GlassWidgets.TopBar(root, "MAIN MENU", MainMenuHub.TabNames, 0, i => { if (_hub != null) _hub.SetPage(i); }, true);
            if (tile) DressTopBar(t, teams);

            BuildHub(root, 0);
            // ---- settings and team select (same panels as Classic; they build their themed layout themselves)
            _settings = SettingsPanel.Build(root, CloseSettings); _settings.OnReset = RebuildSettings;
            _settingsRoot = _settings.gameObject; _settingsRoot.SetActive(false);
            _teamSelect = TeamSelectPanel.Build(root, CloseTeamSelect, StartMatchWithFade);
            _teamSelect.OnHomeChanged = DressStage;
            _teamSelect.OnScreen = GlassSection;
            _teamRoot = _teamSelect.gameObject; _teamRoot.SetActive(false);

            BuildCareerUi(root);

            // ---- footer prompts
            var foot = GlassWidgets.BottomBar(root, "PIX PRO FOOTBALL  v" + GameplayInfo.Version);
            _footer = foot.Prompts;
            SetFooter(false);
            ReopenFranchiseIfReturning();   // a franchise game just ended: back to the franchise hub
        }

        /// <summary>Builds (or rebuilds) the tile hub on <paramref name="page"/>: the art tiles' crests come from the last exhibition setup, the FRANCHISE tile from the newest save.</summary>
        void BuildHub(RectTransform root, int page)
        {
            var teams = TeamCatalog.Teams;
            var stored = PixSettings.LoadMatchConfig();
            var cfg = TeamSelectPanel.InitialConfig(teams, stored, MatchData.Current, TeamCatalog.SavedTeamId);
            var home = TeamCatalog.ById(cfg.HomeId); var away = TeamCatalog.ById(cfg.AwayId);
            List<string> headlines = null;
            var top = new List<PixFootball.League.Team>();
            try { headlines = MenuHubModel.Headlines(MatchData.League); top = MenuHubModel.TopTeams(MatchData.League, 4); }
            catch (System.Exception e) { Debug.LogWarning("[PIX] menu headlines: " + e.Message); }

            CareerSlotMeta latest = null;
            try { latest = CareerSession.Latest(); } catch (System.Exception e) { Debug.LogWarning("[PIX] career tile: " + e.Message); }
            var careerTeam = latest != null ? TeamCatalog.ById(latest.TeamKey) : TeamCatalog.ById(TeamCatalog.SavedTeamId);

            var ctx = new HubContext
            {
                PlayNow = OpenTeamSelect, Rematch = OpenRematch,
                FreeRoam = () => StartWithFade(GameSession.StartFreeRoam),
                Quit = ConfirmQuit,
                Settings = OpenSettingsAt,
                Career = OpenCareerStart, CareerLine = CareerTileSubtitle(), CareerBadge = latest != null ? "RESUME" : null,
                Parallax = v => { var st = GetComponent<GlassStage>(); if (st != null) st.Parallax = v; },
                CrestAway = away != null ? UiKit.Sprite("crest_" + away.Id) : null, CrestHome = home != null ? UiKit.Sprite("crest_" + home.Id) : null,
                NameAway = away != null ? away.Nickname.ToUpperInvariant() : "", NameHome = home != null ? home.Nickname.ToUpperInvariant() : "",
                ColorAway = away != null ? away.PrimaryUi : Color.gray, ColorHome = home != null ? home.PrimaryUi : Color.gray,
                HasRematch = stored != null,
                RematchLine = away != null && home != null ? MenuHubModel.RematchLine(away.Abbr, home.Abbr, cfg.QuarterMinutes + " MIN QUARTERS") : "",
                CareerCrest = careerTeam != null ? UiKit.Sprite("crest_" + careerTeam.Id) : null, CareerColor = careerTeam != null ? careerTeam.PrimaryUi : Color.gray,
                RankCrests = top.Select(x => UiKit.Sprite("crest_" + x.Key)).ToList(), RankNames = top.Select(x => x.Nickname).ToList(),
                Headlines = headlines,
                WatchMode = WatchMode.Enabled, WatchLine = "Controller input: " + (WatchMode.UserControl ? "On (play)" : "Off (watch)")
            };
            _hub = MainMenuHub.Build(root, _glassNav, ctx);
            _hub.SetPage(page);
            _menuRoot = _hub.Root.gameObject; _firstButton = _hub.First; _settingsBtn = _hub.SettingsTile;
        }

        /// <summary>QUICK REMATCH: the matchup card of the last setup (B goes back to the team select, one more B to the hub).</summary>
        void OpenRematch()
        {
            if (_teamSelect == null) return;
            OpenTeamSelect();
            _teamSelect.OpenMatchup();
        }

        /// <summary>Hub footer: A selects, LB RB switch the tab, the stick / d-pad navigates (the sub screens show BACK instead of the tab hint).</summary>
        void SetHubFooter()
        {
            if (_footer == null) return;
            _footer.Set(PromptFooter.Item("glyph_a", "SELECT"), new PromptItem(new[] { "glyph_lb", "glyph_rb" }, "PREV / NEXT TAB"), PromptFooter.Item("glyph_analog_all", "NAVIGATE"));
        }
    }
}
