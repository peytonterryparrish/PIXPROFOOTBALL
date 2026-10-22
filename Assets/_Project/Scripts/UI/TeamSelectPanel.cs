using System;
using System.Collections.Generic;
using System.Linq;
using PixFootball.Gameplay;
using PixFootball.League;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PixFootball.UI
{
    /// <summary>
    /// "Play Now" setup: pick the home and away teams from the 32 pro teams (crest, colours, overall rating), quarter length, rules, CPU difficulty, which side you play, weather and
    /// time of day, then kick off. Two live preview cards on the right (HOME left, AWAY right, the same order as the option rows), option rows on the left (full gamepad navigation).
    /// Both blocks scale down to fit short or narrow canvases (4:3, 21:9). The whole setup is remembered between launches; nothing is stored until KICK OFF.
    /// </summary>
    public partial class TeamSelectPanel : MonoBehaviour, ICancelHandler
    {
        public Action OnBack;
        public Action<MatchConfig> OnStart;
        public Selectable First;
        /// <summary>Glass: told the id of the home team whenever it changes (the menu player puts on its kit) and the name of the screen that is showing ("TEAM SELECT" / "MATCHUP").</summary>
        public Action<string> OnHomeChanged, OnScreen;

        sealed class Card { public Image Wash, Crest; public TextMeshProUGUI Role, City, Nick, Overall, OvrLabel; public Image[] Chips = new Image[3]; public Image Bar; }
        Card _home, _away;
        OptionSelector _homeSel, _awaySel, _quarterSel, _rulesSel, _diffSel, _sideSel, _weatherSel, _timeSel;
        List<TeamInfo> _teams;
        MatchConfig _cfg;
        TextMeshProUGUI _summary;

        public static TeamSelectPanel Build(Transform parent, Action onBack, Action<MatchConfig> onStart = null)
        {
            var root = UiKit.Rect(parent, "TeamSelectPanel");
            var p = root.gameObject.AddComponent<TeamSelectPanel>();
            p.OnBack = onBack; p.OnStart = onStart;
            UiKit.Stretch(root);
            p.Make(root);
            return p;
        }

        static string Ovr(string key) { var t = MatchData.TeamByKey(key); return MatchData.Overall(t).ToString(); }

        /// <summary>The setup to start from: the last one played (stored at KICK OFF), else the current in-memory one; ids that no longer exist are repaired.</summary>
        public static MatchConfig InitialConfig(IReadOnlyList<TeamInfo> teams, MatchConfig stored, MatchConfig fallback, string savedTeamId)
        {
            var cfg = (stored ?? fallback ?? new MatchConfig()).Clone();
            Func<string, bool> has = id => !string.IsNullOrEmpty(id) && teams.Any(t => t.Id == id);
            if (!has(cfg.HomeId)) cfg.HomeId = has(savedTeamId) ? savedTeamId : teams[0].Id;
            if (!has(cfg.AwayId) || cfg.HomeId == cfg.AwayId)
            {
                int hi = 0; for (int i = 0; i < teams.Count; i++) if (teams[i].Id == cfg.HomeId) hi = i;
                cfg.AwayId = teams[(hi + 1) % teams.Count].Id;
            }
            if (cfg.QuarterMinutes <= 0 || Array.IndexOf(MatchConfig.QuarterChoices, cfg.QuarterMinutes) < 0) cfg.QuarterMinutes = 5;
            cfg.CpuVsCpu = false; cfg.Seed = 0;
            return cfg;
        }

        void Make(RectTransform root)
        {
            _teams = TeamCatalog.Teams.ToList();
            _cfg = InitialConfig(_teams, PixSettings.LoadMatchConfig(), MatchData.Current, TeamCatalog.SavedTeamId);
            if (UiTheme.IsGlass) { MakeGlass(root); return; }
            var back = UiKit.Panel(root, "Backdrop", new Color(0.05f, 0.06f, 0.1f, 0.97f)); UiKit.Stretch(back.rectTransform);

            var title = UiKit.Label(root, "TEAM SELECT", 64, UiColors.TextPrimary, true, TextAlignmentOptions.BottomLeft, "Title");
            UiKit.Anchor(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(110, -40), new Vector2(760, 84));
            var rule = UiKit.Panel(root, "TitleRule", UiColors.Gold); UiKit.Anchor(rule.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(116, -128), new Vector2(200, 6));
            _summary = UiKit.Label(root, "", 24, UiColors.TextSecondary, true, TextAlignmentOptions.TopLeft, "Summary");
            UiKit.Anchor(_summary.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(116, -144), new Vector2(1100, 34)); _summary.characterSpacing = 1.5f;

            // option column: a stretched host that shrinks the 800 x 700 design block to the room that is left
            var colHost = UiKit.Rect(root, "ColumnHost");
            colHost.anchorMin = new Vector2(0f, 0f); colHost.anchorMax = new Vector2(0.52f, 1f); colHost.offsetMin = new Vector2(110, 90); colHost.offsetMax = new Vector2(-10, -188);
            var col = UiKit.Rect(colHost, "Column");
            col.anchorMin = col.anchorMax = new Vector2(0, 1); col.pivot = new Vector2(0, 1); col.anchoredPosition = Vector2.zero; col.sizeDelta = new Vector2(800, 700);
            var fit = colHost.gameObject.AddComponent<FitScale>(); fit.Block = col; fit.DesignSize = new Vector2(800, 700);
            var v = col.gameObject.AddComponent<VerticalLayoutGroup>(); v.spacing = 6; v.childAlignment = TextAnchor.UpperLeft; v.childControlWidth = false; v.childControlHeight = false; v.childForceExpandWidth = false; v.childForceExpandHeight = false;

            string[] names = _teams.Select(t => t.Name).ToArray();
            const float rowH = 58f;
            _homeSel = UiControls.Selector(col, "Home team", names, TeamCatalog.IndexOf(_cfg.HomeId), i => { _cfg.HomeId = _teams[i].Id; Refresh(); }, 800, rowH);
            _awaySel = UiControls.Selector(col, "Away team", names, TeamCatalog.IndexOf(_cfg.AwayId), i => { _cfg.AwayId = _teams[i].Id; Refresh(); }, 800, rowH);
            int qi = Array.IndexOf(MatchConfig.QuarterChoices, _cfg.QuarterMinutes); if (qi < 0) qi = 2;
            _quarterSel = UiControls.Selector(col, "Quarter length", MatchConfig.QuarterChoices.Select(m => m + " min").ToArray(), qi, i => { _cfg.QuarterMinutes = MatchConfig.QuarterChoices[i]; Refresh(); }, 800, rowH);
            _rulesSel = UiControls.Selector(col, "Rules", new[] { "Pro", "College" }, _cfg.CollegeRules ? 1 : 0, i => { _cfg.CollegeRules = i == 1; Refresh(); }, 800, rowH);
            _diffSel = UiControls.Selector(col, "Difficulty", new[] { "Rookie", "Pro", "All-Pro" }, (int)_cfg.Difficulty, i => { _cfg.Difficulty = (Difficulty)i; Refresh(); }, 800, rowH);
            _sideSel = UiControls.Selector(col, "You play as", new[] { "Home team", "Away team" }, _cfg.UserTeam == PixFootball.Core.Team.Home ? 0 : 1, i => { _cfg.UserTeam = i == 0 ? PixFootball.Core.Team.Home : PixFootball.Core.Team.Away; Refresh(); }, 800, rowH);
            // weather and time of day are part of the setup and only stored when the game starts (backing out leaves the saved choice alone)
            _weatherSel = UiControls.Selector(col, "Weather", PixFootball.Core.WeatherPresets.WeatherChoiceNames, (int)_cfg.Weather, i => { _cfg.Weather = (PixFootball.Core.WeatherChoice)i; Refresh(); }, 800, rowH);
            _timeSel = UiControls.Selector(col, "Time of day", PixFootball.Core.WeatherPresets.TimeChoiceNames, (int)_cfg.Time, i => { _cfg.Time = (PixFootball.Core.TimeChoice)i; Refresh(); }, 800, rowH);
            var gap = UiKit.Rect(col, "Gap"); gap.sizeDelta = new Vector2(10, 14); gap.gameObject.AddComponent<LayoutElement>().preferredHeight = 14;
            var start = UiKit.MenuButton(col, "KICK OFF", KickOff, 800, 80, 40);
            var primary = UiKit.Panel(start.transform as RectTransform, "PrimaryRule", UiColors.Gold); UiKit.Anchor(primary.rectTransform, new Vector2(0, 0), new Vector2(0, 0), Vector2.zero, new Vector2(0, 5)); primary.rectTransform.anchorMax = new Vector2(1, 0); primary.rectTransform.sizeDelta = new Vector2(0, 5);
            UiKit.MenuButton(col, "BACK", () => { if (OnBack != null) OnBack(); }, 800, 58, 30);
            First = _homeSel;

            // preview cards: HOME on the left, AWAY on the right (same order as the rows), VS on its own backdrop in the gutter
            var cardHost = UiKit.Rect(root, "CardsHost");
            cardHost.anchorMin = new Vector2(0.52f, 0f); cardHost.anchorMax = new Vector2(1f, 1f); cardHost.offsetMin = new Vector2(0, 90); cardHost.offsetMax = new Vector2(-50, -90);
            var cards = UiKit.Rect(cardHost, "Cards");
            cards.anchorMin = cards.anchorMax = new Vector2(0.5f, 0.5f); cards.pivot = new Vector2(0.5f, 0.5f); cards.anchoredPosition = Vector2.zero; cards.sizeDelta = new Vector2(900, 660);
            var fit2 = cardHost.gameObject.AddComponent<FitScale>(); fit2.Block = cards; fit2.DesignSize = new Vector2(900, 660);
            _home = MakeCard(cards, "HOME", new Vector2(-235, 0));
            _away = MakeCard(cards, "AWAY", new Vector2(235, 0));
            var vsBg = UiKit.Panel(cards, "VsBack", UiColors.BgDeep); UiKit.Anchor(vsBg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96, 96));
            var vs = UiKit.Label(vsBg.rectTransform, "VS", 48, UiColors.Gold, true, TextAlignmentOptions.Center, "VS"); UiKit.Stretch(vs.rectTransform);
            Refresh();
        }

        Card MakeCard(RectTransform root, string role, Vector2 offset)
        {
            var c = new Card();
            var r = UiKit.Rect(root, role + "Card");
            UiKit.Anchor(r, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), offset, new Vector2(380, 640));
            var bg = UiKit.Panel(r, "Bg", UiColors.BgPanel); UiKit.Stretch(bg.rectTransform);
            c.Wash = UiKit.Panel(r, "Wash", new Color(1, 1, 1, 0.55f)); UiKit.Stretch(c.Wash.rectTransform);
            c.Bar = UiKit.Panel(r, "Bar", UiColors.Gold); UiKit.Anchor(c.Bar.rectTransform, new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, new Vector2(380, 8)); c.Bar.rectTransform.anchorMax = new Vector2(1, 1); c.Bar.rectTransform.sizeDelta = new Vector2(0, 8);
            c.Role = UiKit.Label(r, role, 28, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "Role");
            UiKit.Anchor(c.Role.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(340, 40)); c.Role.characterSpacing = 6f;
            c.Crest = UiKit.Panel(r, "Crest", Color.white); c.Crest.preserveAspect = true;
            UiKit.Anchor(c.Crest.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -78), new Vector2(240, 240));
            c.City = UiKit.Label(r, "", 30, UiColors.TextSecondary, true, TextAlignmentOptions.Center, "City");
            UiKit.Anchor(c.City.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -336), new Vector2(360, 40)); c.City.characterSpacing = 3f; UiKit.Fit(c.City, 20);
            c.Nick = UiKit.Label(r, "", 46, Color.white, true, TextAlignmentOptions.Center, "Nick");
            UiKit.Anchor(c.Nick.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -376), new Vector2(370, 64)); c.Nick.characterSpacing = 2f; UiKit.Fit(c.Nick, 26);
            for (int i = 0; i < 3; i++)
            {
                c.Chips[i] = UiKit.Panel(r, "Chip" + i, Color.white);
                UiKit.Anchor(c.Chips[i].rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(-56 + i * 56, -464), new Vector2(46, 22));
            }
            c.OvrLabel = UiKit.Label(r, "OVERALL", 22, UiColors.TextMuted, true, TextAlignmentOptions.Center, "OvrLabel");
            UiKit.Anchor(c.OvrLabel.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -506), new Vector2(300, 30)); c.OvrLabel.characterSpacing = 4f;
            c.Overall = UiKit.Label(r, "80", 88, UiColors.Gold, true, TextAlignmentOptions.Center, "Overall");
            UiKit.Anchor(c.Overall.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -534), new Vector2(300, 100));
            return c;
        }

        /// <summary>Parses a team colour; a bad tertiary falls back to the secondary so the chip row always has three chips (QA-897).</summary>
        public static Color ParseColor(string hex, Color fallback)
        {
            Color c;
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out c)) return c;
            return fallback;
        }

        static readonly HashSet<string> _warned = new HashSet<string>();

        void Fill(Card c, string id)
        {
            var t = TeamCatalog.ById(id);
            if (t == null) return;
            var sp = UiKit.Sprite("crest_" + t.Id); c.Crest.sprite = sp; c.Crest.enabled = sp != null;
            c.City.text = t.City.ToUpperInvariant(); c.Nick.text = t.Nickname.ToUpperInvariant();
            Color p = t.PrimaryUi, s = t.SecondaryUi;
            Color tt;
            if (!ColorUtility.TryParseHtmlString(t.TertiaryHex ?? "", out tt)) { if (_warned.Add(t.Id)) Debug.LogWarning("[PIX] team " + t.Id + " has an unreadable tertiary colour '" + t.TertiaryHex + "'"); tt = s; }
            c.Wash.color = new Color(p.r, p.g, p.b, 0.42f);
            c.Bar.color = p;
            c.Chips[0].color = p; c.Chips[1].color = s; c.Chips[2].color = tt;
            c.Overall.text = Ovr(id);
        }

        /// <summary>The one-line summary under the title, all caps like every other label.</summary>
        public static string SummaryText(MatchConfig c, bool sameTeam)
        {
            string s = c.QuarterMinutes + " MIN QUARTERS  -  " + (c.CollegeRules ? "COLLEGE" : "PRO") + " RULES  -  CPU " + (c.Difficulty == Difficulty.AllPro ? "ALL-PRO" : c.Difficulty.ToString().ToUpperInvariant())
                       + "  -  " + PixFootball.Core.WeatherPresets.WeatherChoiceNames[(int)c.Weather].ToUpperInvariant() + ", " + PixFootball.Core.WeatherPresets.TimeChoiceNames[(int)c.Time].ToUpperInvariant();
            if (sameTeam) s += "  -  PICK TWO DIFFERENT TEAMS";
            return s;
        }

        void Refresh()
        {
            if (_glass) { RefreshGlass(); return; }
            if (_home == null) return;
            Fill(_home, _cfg.HomeId); Fill(_away, _cfg.AwayId);
            _home.Role.text = _cfg.UserTeam == PixFootball.Core.Team.Home ? "HOME  -  YOU" : "HOME  -  CPU";
            _away.Role.text = _cfg.UserTeam == PixFootball.Core.Team.Away ? "AWAY  -  YOU" : "AWAY  -  CPU";
            _home.Role.color = _cfg.UserTeam == PixFootball.Core.Team.Home ? UiColors.Gold : UiColors.TextSecondary;
            _away.Role.color = _cfg.UserTeam == PixFootball.Core.Team.Away ? UiColors.Gold : UiColors.TextSecondary;
            _summary.text = SummaryText(_cfg, _cfg.HomeId == _cfg.AwayId);
        }

        /// <summary>KICK OFF on team select. Glass: opens the matchup card first (one more A starts the game); Classic starts at once.</summary>
        void KickOff()
        {
            if (_cfg.HomeId == _cfg.AwayId)
            {
                _cfg.AwayId = _teams[(TeamCatalog.IndexOf(_cfg.HomeId) + 1) % _teams.Count].Id;
                if (_awaySel != null) _awaySel.SetOptions(_awaySel.Options, TeamCatalog.IndexOf(_cfg.AwayId));
                Refresh();
            }
            if (_glass && _matchup != null) { ShowMatchup(); return; }
            StartGame();
        }

        void StartGame()
        {
            _cfg.Seed = 0; _cfg.CpuVsCpu = false;
            TeamCatalog.SavedTeamId = _cfg.UserTeam == PixFootball.Core.Team.Home ? _cfg.HomeId : _cfg.AwayId;
            PixSettings.WeatherSetting = (int)_cfg.Weather; PixSettings.TimeSetting = (int)_cfg.Time;
            PixSettings.StoreMatchConfig(_cfg); PixSettings.Save();
            MatchData.Current = _cfg;
            if (OnStart != null) OnStart(_cfg); else GameSession.StartMatch(_cfg);
        }

        public void OnCancel(BaseEventData e)
        {
            if (_glass && _matchup != null && _matchup.Visible) { HideMatchup(); return; }   // B on the matchup card goes back to team select
            if (OnBack != null) OnBack();
        }
    }
}
