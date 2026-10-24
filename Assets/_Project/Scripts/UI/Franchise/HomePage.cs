using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.UI.Glass;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.UI.Franchise
{
    /// <summary>
    /// The HOME tab (direction 16, the franchise hub mock): the hero tile with the matchup card of the next game (or the current step of the offseason with its blockers) and the Play / Sim buttons,
    /// three status tiles under it (team, cap and roster, inbox) and at the right the standings panel (division / conference / league tabs) over the owner's goals.
    /// Reference: the franchise hub of the console football sims, art and layout ours. 900 unit left column, 870 unit right column, 30 gutter.
    /// </summary>
    public sealed class HomePage : FranchisePage
    {
        const float LeftW = 900f, RightX = 930f, RightW = 870f, HeroH = 410f, TileGap = 20f, TileY = HeroH + 20f;

        RectTransform _heroHost, _standHost, _ownerHost;
        readonly RectTransform[] _tileHosts = new RectTransform[3];
        readonly List<Button> _heroButtons = new List<Button>();
        readonly List<Button> _tiles = new List<Button>();
        GlassTabs _scopeTabs;
        int _scope;
        HeroModel _hero;

        float TileW { get { return (LeftW - 2f * TileGap) / 3f; } }
        float TileH { get { return Lay.H - TileY; } }

        protected override void Build(RectTransform root)
        {
            _heroHost = Lay.Group(root, "Hero", 0f, 0f, LeftW, HeroH);
            for (int i = 0; i < 3; i++) _tileHosts[i] = Lay.Group(root, "StatusTile" + i, i * (TileW + TileGap), TileY, TileW, TileH);
            _standHost = Lay.Group(root, "Standings", RightX, 0f, RightW, 460f);
            _ownerHost = Lay.Group(root, "Owner", RightX, 480f, RightW, Lay.H - 480f);
        }

        public override Selectable Entry { get { return _heroButtons.Count > 0 ? _heroButtons[0] : null; } }

        public override PromptItem[] Prompts() { return new PromptItem[0]; }

        public override void OnShow()
        {
            Ctl.AbsorbPending();
            Refresh();
            // a game that came back from the match: show its box score once
            var msg = CareerSession.LastMessage;
            if (!string.IsNullOrEmpty(msg) && CareerSession.Active)
            {
                CareerSession.LastMessage = "";
                Shell.Toast(msg, "ui_info");
                var g = LastUserGame();
                var box = g != null ? Ctl.BoxOf(g) : null;
                if (box != null && g.Played && g.Week == Ctl.Week) BoxScoreView.Show(Shell, Save, box, "Your game", "CONTINUE", null);
            }
        }

        ScheduledGame LastUserGame() { return CareerFlow.UserGame(Save); }

        public override void Refresh()
        {
            if (!Built) return;
            _hero = Ctl.Hero();
            string keep = FocusedName();
            var home = Ctl.Home();
            BuildHero(_hero);
            BuildStatusTiles(home);
            BuildStandings();
            BuildOwner(home);
            WireNavigation();
            if (keep != null) Shell.FocusNextFrame(FindByName(keep));
        }

        string FocusedName()
        {
            var cur = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            if (cur == null || !cur.transform.IsChildOf(Root)) return null;
            return cur.name;
        }

        Selectable FindByName(string name)
        {
            foreach (var b in _heroButtons) if (b != null && b.name == name) return b;
            foreach (var b in _tiles) if (b != null && b.name == name) return b;
            if (_scopeTabs != null) foreach (var b in _scopeTabs.Buttons) if (b != null && b.name == name) return b;
            return Entry;
        }

        static void ClearChildren(RectTransform r) { for (int i = r.childCount - 1; i >= 0; i--) UiKit.Kill(r.GetChild(i).gameObject); }

        /// <summary>Pad navigation: hero buttons in a row, the tiles under them, the standings tabs to the right of both; up from the hero goes to the tab bar.</summary>
        void WireNavigation()
        {
            Selectable tabBar = Shell != null && Shell.Tabs != null && Shell.Tabs.Buttons.Length > 0 ? Shell.Tabs.Buttons[Mathf.Clamp(Shell.TabIndex, 0, Shell.Tabs.Buttons.Length - 1)] : null;
            Selectable scope0 = _scopeTabs != null && _scopeTabs.Buttons.Length > 0 ? _scopeTabs.Buttons[0] : null;
            for (int i = 0; i < _heroButtons.Count; i++)
                NavWire.Set(_heroButtons[i], tabBar, _tiles.Count > 0 ? _tiles[Mathf.Min(i, _tiles.Count - 1)] : null, i > 0 ? _heroButtons[i - 1] : null, i + 1 < _heroButtons.Count ? _heroButtons[i + 1] : scope0);
            for (int i = 0; i < _tiles.Count; i++)
                NavWire.Set(_tiles[i], _heroButtons.Count > 0 ? _heroButtons[Mathf.Min(i, _heroButtons.Count - 1)] : tabBar, null, i > 0 ? _tiles[i - 1] : null, i + 1 < _tiles.Count ? _tiles[i + 1] : scope0);
            if (_scopeTabs != null)
            {
                var last = _tiles.Count > 0 ? _tiles[_tiles.Count - 1] : (_heroButtons.Count > 0 ? _heroButtons[_heroButtons.Count - 1] : null);
                for (int i = 0; i < _scopeTabs.Buttons.Length; i++)
                    NavWire.Set(_scopeTabs.Buttons[i], tabBar, null, i > 0 ? _scopeTabs.Buttons[i - 1] : last, i + 1 < _scopeTabs.Buttons.Length ? _scopeTabs.Buttons[i + 1] : null);
            }
        }

        // ------------------------------------------------------------------ hero
        void BuildHero(HeroModel h)
        {
            ClearChildren(_heroHost); _heroButtons.Clear();
            var t = GlassTheme.T;
            var card = GlassKit.Surface(_heroHost, "HeroCard", GlassKind.Glass, t.cornerRadiusLarge, true, 1.25f); Lay.Place(card.rectTransform, 0f, 0f, LeftW, HeroH);
            var cr = card.rectTransform;
            if (h.IsGame && h.Next != null && h.Next.Game != null) BuildMatchup(cr, h);
            else BuildStep(cr, h);

            // ---- buttons: primary (gold), the secondary action, the sim options
            float by = HeroH - 82f, bh = 66f;
            float pw = h.IsGame ? 300f : 400f, sw = 230f, mw = 170f;
            var primary = GlassKit.Button(cr, h.Primary, OnPrimary, GlassButtonStyle.Primary, pw, bh, "ui_arrow_right", "PrimaryBtn", 26f);
            Lay.Place((RectTransform)primary.transform, 30f, by, pw, bh);
            _heroButtons.Add(primary);
            float x = 30f + pw + 14f;
            if (!string.IsNullOrEmpty(h.Secondary))
            {
                var sec = GlassKit.Button(cr, h.Secondary, OnSecondary, GlassButtonStyle.Secondary, sw, bh, null, "SecondaryBtn", 22f);
                Lay.Place((RectTransform)sec.transform, x, by, sw, bh); _heroButtons.Add(sec); x += sw + 14f;
            }
            var sim = GlassKit.Button(cr, "SIM OPTIONS", OnSimOptions, GlassButtonStyle.Secondary, mw, bh, null, "SimBtn", 20f);
            Lay.Place((RectTransform)sim.transform, x, by, mw, bh); _heroButtons.Add(sim);
            if (Application.isPlaying) GlassSkin.Apply(cr);
        }

        void BuildMatchup(RectTransform cr, HeroModel h)
        {
            var t = GlassTheme.T;
            var n = h.Next;
            LeagueTeam user = Save.UserTeam, opp = Save.Pro.GetTeam(n.OpponentId);
            LeagueTeam home = n.Home ? user : opp, away = n.Home ? opp : user;
            var homeR = n.Home ? n.You : n.Them; var awayR = n.Home ? n.Them : n.You;
            string homeRec = n.Home ? Ctl.Home().Record : n.OpponentRecord, awayRec = n.Home ? n.OpponentRecord : Ctl.Home().Record;

            Lay.Caps(cr, n.Label.StartsWith("Week") ? h.Eyebrow : h.Eyebrow + "  -  " + n.Label.ToUpperInvariant(), 30f, 20f, 560f, 30f, t.accent, 20f);
            var chip = GlassWidgets.Chip(cr, n.Home ? "HOME GAME" : "ROAD GAME", false, 34f, 20f); Lay.Unlayout(chip.gameObject); Lay.Place(chip.rectTransform, LeftW - 30f - 200f, 16f, 200f, 34f);

            float colW = 250f, mid = LeftW * 0.5f;
            SideBlock(cr, away, awayR, awayRec, 30f, colW, away == user, homeR);
            SideBlock(cr, home, homeR, homeRec, LeftW - 30f - colW, colW, home == user, awayR);

            // middle: AT, kickoff, venue, conditions
            float mw = 330f, mx = mid - mw * 0.5f;
            Lay.Text(cr, "AT", 46f, t.accent, mx, 56f, mw, 56f, true, TextAlignmentOptions.Center, 6f, "At");
            var setup = CareerFlow.SetupUserGame(Save);
            WeatherChoice w = WeatherChoice.Clear; TimeChoice tm = TimeChoice.Day;
            if (setup != null) CareerSession.PickConditions(setup.WeatherSeed, setup.Home, n.Week, out w, out tm);
            Lay.Caps(cr, "KICKOFF", mx, 114f, mw, 24f, null, 20f, TextAlignmentOptions.Center);
            Lay.Text(cr, MenuHubModel.KickoffTime(tm), 34f, t.textPrimary, mx, 136f, mw, 42f, true, TextAlignmentOptions.Center, 1f, "Time");
            Lay.Caps(cr, "VENUE", mx, 184f, mw, 24f, null, 20f, TextAlignmentOptions.Center);
            var ven = Lay.Text(cr, MenuHubModel.VenueName(home).ToUpperInvariant(), 22f, t.textPrimary, mx, 208f, mw, 58f, true, TextAlignmentOptions.Top, 1f, "Venue", false);
            ven.textWrappingMode = TextWrappingModes.Normal; UiKit.Fit(ven, Lay.MinText);
            Lay.Caps(cr, "CONDITIONS", mx, 268f, mw, 24f, null, 20f, TextAlignmentOptions.Center);
            var cond = Lay.Text(cr, MenuHubModel.WeatherLine(w, tm, home), 20f, t.accent, mx - 10f, 292f, mw + 20f, 30f, true, TextAlignmentOptions.Top, 1f, "Cond", false);
            UiKit.Fit(cond, Lay.MinText);
            for (int k = 0; k < 2; k++)
            {
                var rl = GlassKit.Bar(cr, "Divider" + k, new Color(1f, 1f, 1f, 0.10f), 1.4f, 300f);
                Lay.Place(rl.rectTransform, mid + (k == 0 ? -mw * 0.5f - 8f : mw * 0.5f + 8f), 58f, 1.4f, 262f);
            }
        }

        void SideBlock(RectTransform cr, LeagueTeam team, TeamRatingsView r, string record, float x, float w, bool you, TeamRatingsView other)
        {
            var t = GlassTheme.T;
            var wash = GlassKit.Solid(cr, "Wash", TeamUi.Tint(team, 0.17f), t.cornerRadius);
            Lay.Place(wash.rectTransform, x - 6f, 54f, w + 12f, 268f);
            TeamUi.CrestImage(cr, team, 92f, x + w * 0.5f - 46f, 58f, "Crest");
            if (you)
            {
                var yc = GlassWidgets.Chip(cr, "YOU", true, 30f, 20f); Lay.Unlayout(yc.gameObject); Lay.Place(yc.rectTransform, x + 4f, 62f, 66f, 30f);
            }
            Lay.Caps(cr, team.City, x, 152f, w, 24f, null, 20f, TextAlignmentOptions.Center);
            var nick = Lay.Text(cr, team.Nickname.ToUpperInvariant(), 34f, t.textPrimary, x, 174f, w, 42f, true, TextAlignmentOptions.Center, 1f, "Nick");
            Lay.Text(cr, "RECORD  " + record, 20f, t.textSecondary, x, 216f, w, 26f, true, TextAlignmentOptions.Center, 2f, "Record");
            string[] caps = { "OVR", "OFF", "DEF" };
            int[] mine = { r.Ovr, r.Off, r.Def }, theirs = { other.Ovr, other.Off, other.Def };
            float tw = 76f, gap = 8f, x0 = x + (w - (3 * tw + 2 * gap)) * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                var well = GlassKit.Well(cr, "Stat" + caps[i], t.cornerRadiusSmall); Lay.Place(well.rectTransform, x0 + i * (tw + gap), 250f, tw, 64f);
                Lay.Text(well.rectTransform, mine[i].ToString(), 30f, mine[i] > theirs[i] ? t.accent : t.textPrimary, 0f, 0f, tw, 38f, true, TextAlignmentOptions.Center, 0f, "V");
                Lay.Caps(well.rectTransform, caps[i], 0f, 38f, tw, 24f, null, 20f, TextAlignmentOptions.Center);
            }
        }

        void BuildStep(RectTransform cr, HeroModel h)
        {
            var t = GlassTheme.T;
            Lay.Caps(cr, h.Eyebrow, 30f, 24f, 800f, 30f, t.accent, 20f);
            Lay.Text(cr, h.Title.ToUpperInvariant(), 56f, t.textPrimary, 30f, 58f, LeftW - 60f, 72f, true, TextAlignmentOptions.MidlineLeft, 2f, "Title");
            var sub = GlassKit.Text(cr, h.Subtitle, GlassText.Body, TextAlignmentOptions.TopLeft, "Sub"); GlassKit.Style(sub, 24f, t.textSecondary, false, 0f); sub.textWrappingMode = TextWrappingModes.Normal; sub.overflowMode = TextOverflowModes.Ellipsis;
            Lay.Place(sub.rectTransform, 32f, 134f, LeftW - 64f, 76f);
            if (h.Steps > 0)
            {
                Lay.Caps(cr, "OFFSEASON  -  STEP " + h.Step + " OF " + h.Steps, 32f, 214f, 560f, 26f, null, 20f);
                float pw = (LeftW - 64f) / h.Steps;
                for (int i = 0; i < h.Steps; i++)
                {
                    var pip = GlassKit.Solid(cr, "Pip" + i, i < h.Step ? t.accent : new Color(1f, 1f, 1f, 0.12f), 0f);
                    Lay.Place(pip.rectTransform, 32f + i * pw, 246f, pw - 8f, 8f);
                }
            }
            // blockers
            float y = h.Steps > 0 ? 268f : 226f;
            if (h.Blockers.Count > 0)
            {
                Lay.Caps(cr, "BEFORE YOU CONTINUE", 32f, y, 500f, 26f, t.warning, 20f);
                for (int i = 0; i < h.Blockers.Count && i < 2; i++)
                    Lay.Text(cr, h.Blockers[i], 22f, t.textPrimary, 32f, y + 28f + i * 30f, LeftW - 64f, 30f, false, TextAlignmentOptions.MidlineLeft, 0f, "Blocker" + i);
            }
            else if (h.IsBye) Lay.Caps(cr, "NO BLOCKERS - READY WHEN YOU ARE", 32f, y, 700f, 26f, t.success, 20f);
        }

        // ------------------------------------------------------------------ actions
        void OnPrimary()
        {
            if (_hero == null) return;
            switch (_hero.PrimaryAction)
            {
                case HeroAction.PlayGame: PlayGame(); break;
                case HeroAction.OpenResign: Shell.ShowTab(FranchiseNav.Actions, FranchiseNav.ReSign); break;
                case HeroAction.OpenFreeAgency: Shell.ShowTab(FranchiseNav.Actions, FranchiseNav.FreeAgency); break;
                case HeroAction.OpenDraft: Shell.ShowOverlay(new DraftPage()); break;
                case HeroAction.OpenRoster: Shell.ShowTab(FranchiseNav.Team, FranchiseNav.Roster); break;
                case HeroAction.OpenStaff: Shell.ShowTab(FranchiseNav.Actions, FranchiseNav.Staff); break;
                case HeroAction.OpenAwards: Shell.ShowTab(FranchiseNav.League, FranchiseNav.Awards); break;
                default: AdvanceWithChecks(); break;
            }
        }

        void OnSecondary()
        {
            switch (Save.Phase)
            {
                case CareerPhase.Preseason: Shell.ShowTab(FranchiseNav.Team, FranchiseNav.Roster); return;
                case CareerPhase.RegularSeason:
                case CareerPhase.Playoffs:
                case CareerPhase.PixBowl:
                    if (_hero != null && _hero.IsGame) SimGame(); else AdvanceWithChecks();
                    return;
                case CareerPhase.Awards: Shell.ShowTab(FranchiseNav.League, FranchiseNav.Awards); return;
                case CareerPhase.TrainingCamp: Shell.ShowTab(FranchiseNav.Actions, FranchiseNav.Staff); return;
                default: AdvanceWithChecks(); return;
            }
        }

        void OnSimOptions()
        {
            var items = new List<SheetItem>();
            foreach (var o in Ctl.SimOptions())
            {
                var opt = o;
                items.Add(new SheetItem(opt.Label, () => Shell.Busy("Simulating", () => { opt.Run(); Shell.Toast(Ctl.Message); }), opt.Hint, opt.Enabled));
            }
            ActionSheet.Show(Shell.Root, "Sim options", CareerText.PhaseLabel(Save) + ". Your roster and depth chart are used as they are.", items);
        }

        void PlayGame()
        {
            if (!Ctl.UserHasGame) { Shell.Toast("There is no game to play."); return; }
            var blockers = Ctl.Blockers();
            string err;
            if (Shell.Host != null && Shell.Host.Leave != null)
                Shell.Host.Leave(() => { if (!Ctl.PlayNext(out err)) Shell.Toast(err); });
            else if (!Ctl.PlayNext(out err)) Shell.Toast(err);
        }

        void SimGame()
        {
            Shell.Busy("Simulating the game", () =>
            {
                var r = Ctl.SimUserGameNow();
                if (r == null) { Shell.Toast("Nothing to simulate."); return; }
                BoxScoreView.Show(Shell, Save, r, "Simulated game", "CONTINUE", () => Shell.Busy("Finishing the week", () => { Ctl.Advance(); Shell.Toast(Ctl.Message); }));
            });
        }

        void AdvanceWithChecks()
        {
            var b = Ctl.Blockers();
            if (b.Count > 0)
            {
                var items = new List<SheetItem>
                {
                    new SheetItem("Resolve it for me", () => Shell.Busy("Resolving", () => { var left = Ctl.AutoResolve(); if (left.Count > 0) Shell.Toast(left[0]); else { Ctl.Advance(); Shell.Toast(Ctl.Message); } }), "the way a CPU team would"),
                    new SheetItem("I will fix it myself", null)
                };
                ActionSheet.Show(Shell.Root, "Before you continue", b[0], items);
                return;
            }
            Shell.Busy("Advancing", () =>
            {
                var r = Ctl.Advance();
                if (!r.Ok) { Shell.Toast(Ctl.Message); return; }
                if (Save.Phase == CareerPhase.TrainingCamp && ShowCampResults()) return;
                if (r.Week != null && r.Week.UserResult != null) BoxScoreView.Show(Shell, Save, r.Week.UserResult, "Week " + r.Week.Week + " - your game", "CONTINUE", () => Shell.Toast("Week " + r.Week.Week + " is in the books."));
                else Shell.Toast(Ctl.Message);
            });
        }

        /// <summary>Training camp results of the user's team: the biggest risers and fallers and the ability changes.</summary>
        bool ShowCampResults()
        {
            var mine = Save.LastCamp.FindAll(c => c.TeamId == Save.UserTeamId);
            if (mine.Count == 0) return false;
            mine.Sort((a, b) => (b.OvrAfter - b.OvrBefore).CompareTo(a.OvrAfter - a.OvrBefore));
            var lines = new List<string> { "#BIGGEST GAINS" };
            for (int i = 0; i < mine.Count && i < 5; i++) lines.Add(CampLine(mine[i]));
            lines.Add("#BIGGEST DROPS");
            for (int i = mine.Count - 1; i >= 0 && i >= mine.Count - 5 && mine[i].OvrAfter < mine[i].OvrBefore; i--) lines.Add(CampLine(mine[i]));
            var changes = mine.FindAll(c => !string.IsNullOrEmpty(c.AbilityChange));
            if (changes.Count > 0) { lines.Add("#ABILITY CHANGES"); for (int i = 0; i < changes.Count && i < 4; i++) lines.Add(changes[i].Name + ": " + changes[i].AbilityChange); }
            InfoModal.Show(Shell, "Training camp results", "Camp " + (Save.Year + 1), lines);
            return true;
        }

        static string CampLine(CampResult c)
        {
            int d = c.OvrAfter - c.OvrBefore;
            return c.Pos + " " + c.Name + " (" + c.Age + "):  " + c.OvrBefore + " to " + c.OvrAfter + "  (" + (d > 0 ? "+" : "") + d + ")";
        }

        // ------------------------------------------------------------------ status tiles
        void BuildStatusTiles(HubHome home)
        {
            _tiles.Clear();
            var t = GlassTheme.T; var u = Save.UserTeam;
            foreach (var h in _tileHosts) ClearChildren(h);
            float tw = TileW, th = TileH, pad = 20f, iw = tw - pad * 2f;
            RectTransform body;

            // TEAM
            var b0 = TileKit.Tile(_tileHosts[0], "TileTeam", 0f, 0f, tw, th, () => Shell.ShowTab(FranchiseNav.Team, FranchiseNav.Roster), out body);
            Lay.Caps(body, "MY TEAM", pad, 14f, iw, 26f, t.textSecondary, 20f);
            Lay.Text(body, home.Record, 54f, t.textPrimary, pad, 40f, iw, 64f, true, TextAlignmentOptions.MidlineLeft, 1f, "Rec");
            string[] caps = { "OVR", "OFF", "DEF" };
            var ratings = CareerViews.Ratings(u);
            int[] vals = { ratings.Ovr, ratings.Off, ratings.Def };
            float ww = (iw - 16f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var well = GlassKit.Well(body, "St" + caps[i], t.cornerRadiusSmall); Lay.Place(well.rectTransform, pad + i * (ww + 8f), 116f, ww, 66f);
                Lay.Text(well.rectTransform, vals[i].ToString(), 30f, Fmt.OvrColor(vals[i]), 0f, 0f, ww, 40f, true, TextAlignmentOptions.Center, 0f, "V");
                Lay.Caps(well.rectTransform, caps[i], 0f, 40f, ww, 24f, null, 20f, TextAlignmentOptions.Center);
            }
            Lay.Text(body, "Morale " + home.TeamMorale, 22f, t.textPrimary, pad, 196f, iw, 30f, true, TextAlignmentOptions.MidlineLeft, 0f, "Morale");
            Lay.Text(body, home.PhaseLabel, 20f, t.textSecondary, pad, 226f, iw, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Phase");
            Lay.Text(body, Fmt.Plural(Save.Pro.History.Count, "season", "seasons") + " played", 20f, t.textMuted, pad, 256f, iw, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Seasons");
            _tiles.Add(b0);

            // CAP AND ROSTER
            var cap = CapManager.Report(Save, u);
            var b1 = TileKit.Tile(_tileHosts[1], "TileCap", 0f, 0f, tw, th, () => Shell.ShowTab(FranchiseNav.Actions, FranchiseNav.Cap), out body);
            Lay.Caps(body, "CAP SPACE", pad, 14f, iw, 26f, t.textSecondary, 20f);
            Lay.Text(body, Fmt.MoneyShort(cap.Space), 46f, cap.Space < 0 ? t.danger : t.textPrimary, pad, 40f, iw, 60f, true, TextAlignmentOptions.MidlineLeft, 0f, "Space");
            var bar = GlassKit.ProgressBar(body, Mathf.Clamp01((float)cap.Used / Mathf.Max(1f, cap.Cap)), iw, 10f, true, "CapBar"); Lay.Unlayout(bar.gameObject); Lay.Place((RectTransform)bar.transform, pad, 112f, iw, 10f);
            Lay.Text(body, "Used " + Fmt.MoneyShort(cap.Used) + " of " + Fmt.MoneyShort(cap.Cap), 20f, t.textSecondary, pad, 130f, iw, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Used");
            Lay.Text(body, "Dead money " + Fmt.MoneyShort(cap.Dead), 20f, t.textSecondary, pad, 158f, iw, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Dead");
            Lay.Text(body, "Roster " + cap.ActiveCount + "/" + RosterRules.ActiveLimit, 20f, t.textPrimary, pad, 186f, iw, 28f, true, TextAlignmentOptions.MidlineLeft, 0f, "Roster");
            Lay.Text(body, "Squad " + cap.PracticeSquadCount + "/" + RosterRules.PracticeSquadLimit + "   IR " + cap.InjuredReserveCount, 20f, t.textSecondary, pad, 214f, iw, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Counts");
            Lay.Text(body, "Next year " + Fmt.MoneyShort(cap.CommittedNextYear), 20f, t.textMuted, pad, 244f, iw, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Next");
            _tiles.Add(b1);

            // INBOX
            var b2 = TileKit.Tile(_tileHosts[2], "TileInbox", 0f, 0f, tw, th, () =>
            {
                if (home.PendingTradeOffers > 0) Shell.ShowTab(FranchiseNav.Actions, FranchiseNav.Trades);
                else if (home.InjuredStarters > 0) Shell.ShowTab(FranchiseNav.Team, FranchiseNav.Squad);
                else Shell.ShowTab(FranchiseNav.News);
            }, out body);
            Lay.Caps(body, "INBOX", pad, 14f, iw, 26f, t.textSecondary, 20f);
            InboxLine(body, 52f, home.PendingTradeOffers.ToString(), home.PendingTradeOffers == 1 ? "trade offer" : "trade offers", home.PendingTradeOffers > 0, iw);
            InboxLine(body, 118f, home.InjuredStarters.ToString(), home.InjuredStarters == 1 ? "injured starter" : "injured starters", home.InjuredStarters > 0, iw);
            InboxLine(body, 184f, home.LegacyScore.ToString(), "legacy " + home.LegacyTier, false, iw);
            _tiles.Add(b2);

            if (Application.isPlaying) foreach (var h in _tileHosts) GlassSkin.Apply(h);
        }

        static void InboxLine(RectTransform body, float y, string big, string small, bool hot, float iw)
        {
            var t = GlassTheme.T;
            Lay.Text(body, big, 40f, hot ? t.accent : t.textPrimary, 20f, y, 90f, 52f, true, TextAlignmentOptions.MidlineLeft, 0f, "Big");
            Lay.Text(body, small, 20f, t.textSecondary, 112f, y + 6f, iw - 92f, 40f, false, TextAlignmentOptions.MidlineLeft, 0f, "Small");
        }

        // ------------------------------------------------------------------ standings and owner
        const float StandRowH = 52f;

        List<StandingsRow> ScopeRows(int scope, out string group)
        {
            var u = Save.UserTeam; group = "";
            if (Save.Season == null || Save.Season.IsEmpty) return new List<StandingsRow>();
            if (scope == 0) { group = TeamUi.DivisionName(Save.Pro, u).ToUpperInvariant(); return Save.Season.DivisionStandings(u.DivisionId); }
            if (scope == 1)
            {
                int conf = 0; string cname = "";
                foreach (var c in Save.Pro.Conferences) foreach (var d in c.Divisions) if (d.Id == u.DivisionId) { conf = c.Id; cname = c.Name; }
                group = cname.ToUpperInvariant() + " CONFERENCE";
                return Save.Season.ConferenceStandings(conf);
            }
            group = "ALL TEAMS";
            var all = Save.Season.Standings();
            all.Sort((a, b) => { int c = b.Pct.CompareTo(a.Pct); return c != 0 ? c : (b.PF - b.PA).CompareTo(a.PF - a.PA); });
            return all;
        }

        void BuildStandings()
        {
            ClearChildren(_standHost); _scopeTabs = null;
            var t = GlassTheme.T; var u = Save.UserTeam;
            string group; var all = ScopeRows(_scope, out group);
            // the rows shown: the top of the list, and the user's own row when he is below the cut
            int max = _scope == 0 ? 4 : 6;
            var rows = new List<int>();
            for (int i = 0; i < all.Count && rows.Count < max; i++) rows.Add(i);
            int mineAt = all.FindIndex(r => r.TeamId == u.Id);
            if (mineAt >= max && rows.Count > 0) rows[rows.Count - 1] = mineAt;
            float headH = 64f, groupH = 44f, colH = 40f;
            float h = headH + groupH + colH + Mathf.Max(1, rows.Count) * StandRowH + 16f;
            _standHost.sizeDelta = new Vector2(RightW, h);
            var panel = GlassKit.Panel(_standHost, "StandCard", 1.15f, true, t.cornerRadiusLarge); Lay.Place(panel.rectTransform, 0f, 0f, RightW, h);
            var pr = panel.rectTransform;
            Lay.Head(pr, "League standings", 0f, 0f, RightW, headH, null, 26f);
            _scopeTabs = GlassKit.StripTabs(pr, new[] { "DIVISION", "CONFERENCE", "LEAGUE" }, _scope, i => { _scope = i; Ctl.Touch(); }, "ScopeTabs");
            _scopeTabs.BumperPaging = false;
            var tr = (RectTransform)_scopeTabs.transform; tr.anchorMin = tr.anchorMax = new Vector2(1f, 1f); tr.pivot = new Vector2(1f, 1f); tr.anchoredPosition = new Vector2(-12f, -10f);
            var fit = tr.gameObject.AddComponent<ContentSizeFitter>(); fit.horizontalFit = ContentSizeFitter.FitMode.PreferredSize; fit.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            for (int i = 0; i < _scopeTabs.Buttons.Length; i++) _scopeTabs.Buttons[i].name = "Scope" + i;
            if (Application.isPlaying) GlassSkin.Apply(pr);

            float y = headH + 8f;
            var gh = GlassKit.Solid(pr, "GroupHead", new Color(0f, 0f, 0f, 0.22f), 0f); Lay.Place(gh.rectTransform, 16f, y, RightW - 32f, groupH);
            Lay.Text(gh.rectTransform, group, 24f, t.accent, 16f, 0f, RightW - 64f, groupH, true, TextAlignmentOptions.MidlineLeft, 4f, "Group");
            y += groupH;
            float[] cx = { 372f, 442f, 512f, 596f, 676f, 756f };   // W L PCT PF PA STRK (right aligned inside cw)
            float[] cw = { 56f, 56f, 80f, 70f, 70f, 80f };
            string[] heads = { "W", "L", "PCT", "PF", "PA", "STRK" };
            for (int c = 0; c < heads.Length; c++) Lay.Text(pr, heads[c], 20f, t.textMuted, cx[c], y, cw[c], colH, true, TextAlignmentOptions.MidlineRight, 3f, "H" + c);
            y += colH;
            if (rows.Count == 0) { Lay.Text(pr, "The schedule is not out yet.", 22f, t.textMuted, 24f, y + 6f, RightW - 48f, 40f, false, TextAlignmentOptions.MidlineLeft, 0f, "None"); return; }
            foreach (int idx in rows)
            {
                var r = all[idx]; var team = Save.Pro.GetTeam(r.TeamId); bool you = r.TeamId == u.Id;
                if (you) { var hl = GlassKit.Solid(pr, "You", new Color(t.accent.r, t.accent.g, t.accent.b, 0.16f), 0f); Lay.Place(hl.rectTransform, 16f, y, RightW - 32f, StandRowH); }
                else if (idx % 2 == 1) { var zb = GlassKit.Solid(pr, "Zebra", new Color(1f, 1f, 1f, 0.03f), 0f); Lay.Place(zb.rectTransform, 16f, y, RightW - 32f, StandRowH); }
                Color nameC = you ? t.accent : t.textPrimary;
                Lay.Text(pr, (idx + 1).ToString(), 22f, t.textMuted, 24f, y, 40f, StandRowH, true, TextAlignmentOptions.MidlineLeft, 0f, "Rk");
                TeamUi.CrestImage(pr, team, 38f, 66f, y + 7f, "C");
                Lay.Text(pr, team != null ? team.Nickname.ToUpperInvariant() : r.Name, 24f, nameC, 116f, y, 250f, StandRowH, true, TextAlignmentOptions.MidlineLeft, 1f, "Nm");
                Color num = you ? t.accent : t.textPrimary;
                Lay.Text(pr, r.W.ToString(), 24f, num, cx[0], y, cw[0], StandRowH, true, TextAlignmentOptions.MidlineRight, 0f, "W");
                Lay.Text(pr, r.L.ToString(), 24f, num, cx[1], y, cw[1], StandRowH, true, TextAlignmentOptions.MidlineRight, 0f, "L");
                Lay.Text(pr, r.Pct.ToString(".000", System.Globalization.CultureInfo.InvariantCulture), 24f, num, cx[2], y, cw[2], StandRowH, true, TextAlignmentOptions.MidlineRight, 0f, "Pct");
                Lay.Text(pr, r.PF.ToString(), 24f, num, cx[3], y, cw[3], StandRowH, true, TextAlignmentOptions.MidlineRight, 0f, "PF");
                Lay.Text(pr, r.PA.ToString(), 24f, num, cx[4], y, cw[4], StandRowH, true, TextAlignmentOptions.MidlineRight, 0f, "PA");
                string strk = r.Streak > 0 ? "W" + r.Streak : r.Streak < 0 ? "L" + (-r.Streak) : "-";
                Lay.Text(pr, strk, 24f, r.Streak > 0 ? t.success : r.Streak < 0 ? t.danger : t.textMuted, cx[5], y, cw[5], StandRowH, true, TextAlignmentOptions.MidlineRight, 0f, "Strk");
                y += StandRowH;
            }
        }

        void BuildOwner(HubHome home)
        {
            ClearChildren(_ownerHost);
            var t = GlassTheme.T;
            float y0 = _standHost.sizeDelta.y + 20f, h = Lay.H - y0;
            _ownerHost.anchoredPosition = new Vector2(RightX, -y0); _ownerHost.sizeDelta = new Vector2(RightW, h);
            var panel = GlassKit.Panel(_ownerHost, "OwnerCard", 1.15f, true, t.cornerRadiusLarge); Lay.Place(panel.rectTransform, 0f, 0f, RightW, h);
            var pr = panel.rectTransform;
            Lay.Head(pr, "Owner goals", 0f, 0f, RightW, 56f, home.JobSecurity.ToUpperInvariant(), 26f);
            var sp = StackPanel.Create(pr, "Stack", 28f, 68f, RightW - 56f, h - 68f - 8f);
            sp.Bar("Owner trust", home.OwnerSatisfaction / 100f, home.OwnerSatisfaction.ToString(), true, 38f);
            if (home.Goals.Count == 0) sp.Para("The owner has not set goals for this year yet.", 22f);
            foreach (var g in home.Goals)
            {
                if (!sp.Fits(38f + 36f)) break;   // keep room for the legacy line
                string mark = g.Resolved ? (g.Met ? "MET" : "MISSED") : (g.Target > 0 ? g.Actual + " / " + g.Target : "");
                Color c = g.Resolved ? (g.Met ? t.success : t.danger) : t.textSecondary;
                sp.Line(g.Text, mark, c, 36f, 22f);
            }
            if (sp.Fits(38f)) sp.Line("Legacy", home.LegacyScore + "  " + home.LegacyTier.ToUpperInvariant(), t.accent, 38f, 22f);
        }
    }
}
