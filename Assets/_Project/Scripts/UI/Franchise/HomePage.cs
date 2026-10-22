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
    /// The HOME tab (the franchise hub): the hero tile with the matchup card of the next game (or the current step of the offseason with its blockers), the Play / Sim buttons, three status tiles
    /// (team, cap and roster, inbox), the division standings and the owner's goals. Reference: the franchise hub of the console football sims, art and layout ours.
    /// </summary>
    public sealed class HomePage : FranchisePage
    {
        const float LeftW = 1040f, RightX = 1064f, RightW = 664f, HeroH = 470f;

        RectTransform _heroHost, _standHost, _ownerHost;
        readonly RectTransform[] _tileHosts = new RectTransform[3];
        readonly List<Button> _heroButtons = new List<Button>();
        readonly List<Button> _tiles = new List<Button>();
        HeroModel _hero;

        protected override void Build(RectTransform root)
        {
            _heroHost = Lay.Group(root, "Hero", 0f, 0f, LeftW, HeroH);
            float tw = (LeftW - 32f) / 3f;
            for (int i = 0; i < 3; i++) _tileHosts[i] = Lay.Group(root, "StatusTile" + i, i * (tw + 16f), HeroH + 16f, tw, 246f);
            _standHost = Lay.Group(root, "Standings", RightX, 0f, RightW, 330f);
            _ownerHost = Lay.Group(root, "Owner", RightX, 346f, RightW, 386f);
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
            BuildHero(_hero);
            BuildStatusTiles(Ctl.Home());
            BuildStandings();
            BuildOwner(Ctl.Home());
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
            return Entry;
        }

        static void ClearChildren(RectTransform r) { for (int i = r.childCount - 1; i >= 0; i--) UiKit.Kill(r.GetChild(i).gameObject); }

        // ------------------------------------------------------------------ hero
        void BuildHero(HeroModel h)
        {
            ClearChildren(_heroHost); _heroButtons.Clear();
            var t = GlassTheme.T;
            var card = GlassKit.Panel(_heroHost, "HeroCard", 1.25f, true, t.cornerRadiusLarge); Lay.Place(card.rectTransform, 0f, 0f, LeftW, HeroH);
            var cr = card.rectTransform;
            if (h.IsGame && h.Next != null && h.Next.Game != null) BuildMatchup(cr, h);
            else BuildStep(cr, h);

            // ---- buttons
            float by = HeroH - 88f;
            var primary = GlassKit.Button(cr, h.Primary, OnPrimary, GlassButtonStyle.Primary, h.IsGame ? 420f : 470f, 72f, "ui_arrow_right", "PrimaryBtn", 28f);
            Lay.Place((RectTransform)primary.transform, 30f, by, h.IsGame ? 420f : 470f, 72f);
            _heroButtons.Add(primary);
            float x = 30f + (h.IsGame ? 420f : 470f) + 16f;
            if (!string.IsNullOrEmpty(h.Secondary))
            {
                var sec = GlassKit.Button(cr, h.Secondary, OnSecondary, GlassButtonStyle.Secondary, 270f, 72f, null, "SecondaryBtn", 24f);
                Lay.Place((RectTransform)sec.transform, x, by, 270f, 72f); _heroButtons.Add(sec); x += 286f;
            }
            var sim = GlassKit.Button(cr, "SIM OPTIONS", OnSimOptions, GlassButtonStyle.Secondary, 240f, 72f, null, "SimBtn", 22f);
            Lay.Place((RectTransform)sim.transform, x, by, 240f, 72f); _heroButtons.Add(sim);
            for (int i = 0; i < _heroButtons.Count; i++)
                NavWire.Set(_heroButtons[i], null, _tiles.Count > 0 ? _tiles[0] : null, i > 0 ? _heroButtons[i - 1] : null, i + 1 < _heroButtons.Count ? _heroButtons[i + 1] : null);
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

            Lay.Caps(cr, n.Label.StartsWith("Week") ? h.Eyebrow : h.Eyebrow + "  -  " + n.Label.ToUpperInvariant(), 30f, 22f, 700f, 30f, t.accent, 20f);
            var chip = GlassWidgets.Chip(cr, n.Home ? "HOME GAME" : "ROAD GAME", false, 34f, 18f); Lay.Unlayout(chip.gameObject); Lay.Place(chip.rectTransform, LeftW - 30f - 190f, 20f, 190f, 34f);

            float colW = 330f, mid = LeftW * 0.5f;
            SideBlock(cr, away, awayR, awayRec, 30f, colW, away == user, homeR);
            SideBlock(cr, home, homeR, homeRec, LeftW - 30f - colW, colW, home == user, awayR);

            // middle: AT, kickoff, venue, conditions
            Lay.Text(cr, "AT", 52f, t.accent, mid - 120f, 64f, 240f, 70f, true, TextAlignmentOptions.Center, 6f, "At");
            var setup = CareerFlow.SetupUserGame(Save);
            WeatherChoice w = WeatherChoice.Clear; TimeChoice tm = TimeChoice.Day;
            if (setup != null) CareerSession.PickConditions(setup.WeatherSeed, setup.Home, n.Week, out w, out tm);
            Lay.Caps(cr, "KICKOFF", mid - 120f, 140f, 240f, 24f, null, 17f, TextAlignmentOptions.Center);
            Lay.Text(cr, MenuHubModel.KickoffTime(tm), 34f, t.textPrimary, mid - 120f, 162f, 240f, 44f, true, TextAlignmentOptions.Center, 1f, "Time");
            Lay.Caps(cr, "VENUE", mid - 120f, 212f, 240f, 24f, null, 17f, TextAlignmentOptions.Center);
            var ven = Lay.Text(cr, MenuHubModel.VenueName(home).ToUpperInvariant(), 20f, t.textPrimary, mid - 130f, 236f, 260f, 56f, true, TextAlignmentOptions.Top, 1f, "Venue", false);
            ven.textWrappingMode = TextWrappingModes.Normal; UiKit.Fit(ven, 14f);
            Lay.Caps(cr, "CONDITIONS", mid - 120f, 294f, 240f, 24f, null, 17f, TextAlignmentOptions.Center);
            var cond = Lay.Text(cr, MenuHubModel.WeatherLine(w, tm, home), 20f, t.accent, mid - 130f, 318f, 260f, 40f, true, TextAlignmentOptions.Top, 1f, "Cond", false);
            cond.textWrappingMode = TextWrappingModes.Normal; UiKit.Fit(cond, 14f);
            for (int k = 0; k < 2; k++)
            {
                var rl = GlassKit.Bar(cr, "Divider" + k, new Color(t.edgeA.r, t.edgeA.g, t.edgeA.b, 0.25f), 1.4f, 300f);
                Lay.Place(rl.rectTransform, mid + (k == 0 ? -140f : 140f), 64f, 1.4f, 290f);
            }
        }

        void SideBlock(RectTransform cr, LeagueTeam team, TeamRatingsView r, string record, float x, float w, bool you, TeamRatingsView other)
        {
            var t = GlassTheme.T;
            var wash = GlassKit.Solid(cr, "Wash", TeamUi.Tint(team, 0.17f), t.cornerRadius);
            Lay.Place(wash.rectTransform, x - 6f, 58f, w + 12f, 296f);
            TeamUi.CrestImage(cr, team, 116f, x + w * 0.5f - 58f, 60f, "Crest");
            if (you)
            {
                var yc = GlassWidgets.Chip(cr, "YOU", true, 30f, 17f); Lay.Unlayout(yc.gameObject); Lay.Place(yc.rectTransform, x + 4f, 66f, 64f, 30f);
            }
            Lay.Caps(cr, team.City, x, 178f, w, 24f, null, 18f, TextAlignmentOptions.Center);
            var nick = Lay.Text(cr, team.Nickname.ToUpperInvariant(), 36f, t.textPrimary, x, 198f, w, 46f, true, TextAlignmentOptions.Center, 1f, "Nick");
            Lay.Text(cr, "RECORD  " + record, 20f, t.textSecondary, x, 244f, w, 28f, true, TextAlignmentOptions.Center, 2f, "Record");
            string[] caps = { "OVR", "OFF", "DEF" };
            int[] mine = { r.Ovr, r.Off, r.Def }, theirs = { other.Ovr, other.Off, other.Def };
            float tw = 98f, gap = 10f, x0 = x + (w - (3 * tw + 2 * gap)) * 0.5f;
            for (int i = 0; i < 3; i++)
            {
                var well = GlassKit.Well(cr, "Stat" + caps[i], t.cornerRadiusSmall); Lay.Place(well.rectTransform, x0 + i * (tw + gap), 280f, tw, 66f);
                Lay.Text(well.rectTransform, mine[i].ToString(), 32f, mine[i] > theirs[i] ? t.accent : t.textPrimary, 0f, 2f, tw, 40f, true, TextAlignmentOptions.Center, 0f, "V");
                Lay.Caps(well.rectTransform, caps[i], 0f, 40f, tw, 22f, null, 15f, TextAlignmentOptions.Center);
            }
        }

        void BuildStep(RectTransform cr, HeroModel h)
        {
            var t = GlassTheme.T;
            Lay.Caps(cr, h.Eyebrow, 30f, 28f, 800f, 30f, t.accent, 20f);
            Lay.Text(cr, h.Title.ToUpperInvariant(), 58f, t.textPrimary, 30f, 62f, LeftW - 60f, 76f, true, TextAlignmentOptions.MidlineLeft, 3f, "Title");
            var sub = GlassKit.Text(cr, h.Subtitle, GlassText.Body, TextAlignmentOptions.TopLeft, "Sub"); GlassKit.Style(sub, 26f, t.textSecondary, false, 0f); sub.textWrappingMode = TextWrappingModes.Normal; sub.overflowMode = TextOverflowModes.Ellipsis;
            Lay.Place(sub.rectTransform, 32f, 146f, LeftW - 64f, 90f);
            if (h.Steps > 0)
            {
                Lay.Caps(cr, "OFFSEASON  -  STEP " + h.Step + " OF " + h.Steps, 32f, 250f, 500f, 26f, null, 17f);
                float pw = (LeftW - 64f) / h.Steps;
                for (int i = 0; i < h.Steps; i++)
                {
                    var pip = GlassKit.Surface(cr, "Pip" + i, i < h.Step ? GlassKind.Accent : GlassKind.Well, 4f, false, 1f);
                    Lay.Place(pip.rectTransform, 32f + i * pw, 282f, pw - 8f, 8f);
                }
            }
            // blockers
            float y = h.Steps > 0 ? 306f : 260f;
            if (h.Blockers.Count > 0)
            {
                Lay.Caps(cr, "BEFORE YOU CONTINUE", 32f, y, 500f, 26f, t.warning, 17f);
                for (int i = 0; i < h.Blockers.Count && i < 3; i++)
                {
                    var b = Lay.Text(cr, h.Blockers[i], 21f, t.textPrimary, 32f, y + 28f + i * 30f, LeftW - 64f, 30f, false, TextAlignmentOptions.MidlineLeft, 0f, "Blocker" + i);
                }
            }
            else if (h.IsBye) Lay.Caps(cr, "NO BLOCKERS - READY WHEN YOU ARE", 32f, y, 700f, 26f, t.success, 17f);
        }

        // ------------------------------------------------------------------ actions
        void OnPrimary()
        {
            if (_hero == null) return;
            switch (_hero.PrimaryAction)
            {
                case HeroAction.PlayGame: PlayGame(); break;
                case HeroAction.OpenResign: Shell.ShowTab(FranchiseNav.Office, FranchiseNav.ReSign); break;
                case HeroAction.OpenFreeAgency: Shell.ShowTab(FranchiseNav.Office, FranchiseNav.FreeAgency); break;
                case HeroAction.OpenDraft: Shell.ShowOverlay(new DraftPage()); break;
                case HeroAction.OpenRoster: Shell.ShowTab(FranchiseNav.Team, FranchiseNav.Roster); break;
                case HeroAction.OpenStaff: Shell.ShowTab(FranchiseNav.Office, FranchiseNav.Staff); break;
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
                case CareerPhase.TrainingCamp: Shell.ShowTab(FranchiseNav.Office, FranchiseNav.Staff); return;
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
            float tw = (LeftW - 32f) / 3f;
            RectTransform body;

            // TEAM
            var b0 = TileKit.Tile(_tileHosts[0], "TileTeam", 0f, 0f, tw, 246f, () => Shell.ShowTab(FranchiseNav.Team, FranchiseNav.Roster), out body);
            Lay.Caps(body, "MY TEAM", 22f, 14f, tw - 44f, 26f, t.textSecondary, 17f);
            Lay.Text(body, home.Record, 50f, t.textPrimary, 22f, 40f, 170f, 62f, true, TextAlignmentOptions.MidlineLeft, 1f, "Rec");
            string[] caps = { "OVR", "OFF", "DEF" };
            var ratings = CareerViews.Ratings(u);
            int[] vals = { ratings.Ovr, ratings.Off, ratings.Def };
            for (int i = 0; i < 3; i++)
            {
                var well = GlassKit.Well(body, "St" + caps[i], t.cornerRadiusSmall); Lay.Place(well.rectTransform, 20f + i * 98f, 112f, 90f, 60f);
                Lay.Text(well.rectTransform, vals[i].ToString(), 30f, Fmt.OvrColor(vals[i]), 0f, 2f, 90f, 36f, true, TextAlignmentOptions.Center, 0f, "V");
                Lay.Caps(well.rectTransform, caps[i], 0f, 36f, 90f, 20f, null, 14f, TextAlignmentOptions.Center);
            }
            Lay.Text(body, "Morale " + home.TeamMorale + "   -   " + home.PhaseLabel, 19f, t.textSecondary, 22f, 188f, tw - 40f, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Morale");
            Lay.Text(body, Fmt.Plural(Save.Pro.History.Count, "season", "seasons") + " played", 19f, t.textMuted, 22f, 214f, tw - 40f, 26f, false, TextAlignmentOptions.MidlineLeft, 0f, "Seasons");
            _tiles.Add(b0);

            // CAP AND ROSTER
            var cap = CapManager.Report(Save, u);
            var b1 = TileKit.Tile(_tileHosts[1], "TileCap", 0f, 0f, tw, 246f, () => Shell.ShowTab(FranchiseNav.Office, FranchiseNav.Cap), out body);
            Lay.Caps(body, "CAP SPACE", 22f, 14f, tw - 44f, 26f, t.textSecondary, 17f);
            Lay.Text(body, Fmt.MoneyShort(cap.Space), 44f, cap.Space < 0 ? t.danger : t.textPrimary, 22f, 40f, tw - 44f, 58f, true, TextAlignmentOptions.MidlineLeft, 0f, "Space");
            var bar = GlassKit.ProgressBar(body, Mathf.Clamp01((float)cap.Used / Mathf.Max(1f, cap.Cap)), tw - 44f, 10f, true, "CapBar"); Lay.Unlayout(bar.gameObject); Lay.Place((RectTransform)bar.transform, 22f, 106f, tw - 44f, 10f);
            Lay.Text(body, "Used " + Fmt.MoneyShort(cap.Used) + " of " + Fmt.MoneyShort(cap.Cap), 19f, t.textSecondary, 22f, 124f, tw - 40f, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Used");
            Lay.Text(body, "Dead money " + Fmt.MoneyShort(cap.Dead), 19f, t.textSecondary, 22f, 152f, tw - 40f, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Dead");
            Lay.Text(body, "Roster " + cap.ActiveCount + "/" + RosterRules.ActiveLimit + "   Squad " + cap.PracticeSquadCount + "/" + RosterRules.PracticeSquadLimit + "   IR " + cap.InjuredReserveCount, 19f, t.textSecondary, 22f, 180f, tw - 40f, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Counts", true);
            Lay.Text(body, "Next year committed " + Fmt.MoneyShort(cap.CommittedNextYear), 19f, t.textMuted, 22f, 208f, tw - 40f, 28f, false, TextAlignmentOptions.MidlineLeft, 0f, "Next", true);
            _tiles.Add(b1);

            // INBOX
            var b2 = TileKit.Tile(_tileHosts[2], "TileInbox", 0f, 0f, tw, 246f, () =>
            {
                if (home.PendingTradeOffers > 0) Shell.ShowTab(FranchiseNav.Office, FranchiseNav.Trades);
                else if (home.InjuredStarters > 0) Shell.ShowTab(FranchiseNav.Team, FranchiseNav.Squad);
                else Shell.ShowTab(FranchiseNav.League, FranchiseNav.News);
            }, out body);
            Lay.Caps(body, "INBOX", 22f, 14f, tw - 44f, 26f, t.textSecondary, 17f);
            InboxLine(body, 46f, home.PendingTradeOffers.ToString(), home.PendingTradeOffers == 1 ? "trade offer waiting" : "trade offers waiting", home.PendingTradeOffers > 0);
            InboxLine(body, 96f, home.InjuredStarters.ToString(), home.InjuredStarters == 1 ? "injured starter" : "injured starters", home.InjuredStarters > 0);
            InboxLine(body, 146f, home.LegacyScore.ToString(), "legacy: " + home.LegacyTier, false);
            _tiles.Add(b2);

            for (int i = 0; i < _tiles.Count; i++)
                NavWire.Set(_tiles[i], _heroButtons.Count > 0 ? _heroButtons[0] : null, null, i > 0 ? _tiles[i - 1] : null, i + 1 < _tiles.Count ? _tiles[i + 1] : null);
            if (Application.isPlaying) foreach (var h in _tileHosts) GlassSkin.Apply(h);
        }

        static void InboxLine(RectTransform body, float y, string big, string small, bool hot)
        {
            var t = GlassTheme.T;
            Lay.Text(body, big, 38f, hot ? t.accent : t.textPrimary, 22f, y, 90f, 46f, true, TextAlignmentOptions.MidlineLeft, 0f, "Big");
            Lay.Text(body, small, 20f, t.textSecondary, 100f, y + 4f, 220f, 40f, false, TextAlignmentOptions.MidlineLeft, 0f, "Small");
        }

        // ------------------------------------------------------------------ standings and owner
        void BuildStandings()
        {
            ClearChildren(_standHost);
            var t = GlassTheme.T; var u = Save.UserTeam;
            var card = GlassWidgets.Card(_standHost, TeamUi.DivisionName(Save.Pro, u) + " STANDINGS", RightW, 330f, "StandCard"); Lay.Unlayout(card.Root.gameObject); Lay.Place(card.Root, 0f, 0f, RightW, 330f);
            var rows = Save.Season != null && !Save.Season.IsEmpty ? Save.Season.DivisionStandings(u.DivisionId) : new List<StandingsRow>();
            float y = 0f, w = card.Body.rect.width > 10f ? card.Body.rect.width : RightW - 56f;
            Lay.Caps(card.Body, "TEAM", 70f, y, 240f, 24f, null, 15f); Lay.Caps(card.Body, "W-L", w - 250f, y, 110f, 24f, null, 15f, TextAlignmentOptions.MidlineRight); Lay.Caps(card.Body, "PF / PA", w - 130f, y, 130f, 24f, null, 15f, TextAlignmentOptions.MidlineRight);
            y += 28f;
            for (int i = 0; i < rows.Count && i < 4; i++)
            {
                var r = rows[i]; var team = Save.Pro.GetTeam(r.TeamId); bool you = r.TeamId == u.Id;
                if (you) { var hl = GlassKit.Surface(card.Body, "You", GlassKind.Glass, 8f, false, 1.2f); hl.SetFocus(0.7f); hl.GlowScale = 0.15f; Lay.Place(hl.rectTransform, -8f, y, w + 16f, 54f); }
                Lay.Text(card.Body, (i + 1).ToString(), 24f, t.textSecondary, 4f, y, 30f, 54f, true, TextAlignmentOptions.MidlineLeft, 0f, "Rk");
                TeamUi.CrestImage(card.Body, team, 38f, 36f, y + 8f, "C");
                Lay.Text(card.Body, team != null ? team.Nickname : r.Name, 24f, you ? t.accent : t.textPrimary, 84f, y, 230f, 54f, true, TextAlignmentOptions.MidlineLeft, 0f, "Nm");
                Lay.Text(card.Body, r.Record, 24f, t.textPrimary, w - 250f, y, 110f, 54f, true, TextAlignmentOptions.MidlineRight, 0f, "Rec");
                Lay.Text(card.Body, r.PF + " / " + r.PA, 21f, t.textSecondary, w - 130f, y, 130f, 54f, false, TextAlignmentOptions.MidlineRight, 0f, "Pts");
                y += 56f;
            }
            if (rows.Count == 0) Lay.Text(card.Body, "The schedule is not out yet.", 22f, t.textMuted, 0f, 40f, w, 40f, false, TextAlignmentOptions.MidlineLeft, 0f, "None");
        }

        void BuildOwner(HubHome home)
        {
            ClearChildren(_ownerHost);
            var t = GlassTheme.T;
            var card = GlassWidgets.Card(_ownerHost, "OWNER GOALS", RightW, 386f, "OwnerCard"); Lay.Unlayout(card.Root.gameObject); Lay.Place(card.Root, 0f, 0f, RightW, 386f);
            float w = card.Body.rect.width > 10f ? card.Body.rect.width : RightW - 56f;
            var sp = StackPanel.Create(card.Body, "Stack", 0f, 0f, w, 300f);
            sp.Bar("Owner trust", home.OwnerSatisfaction / 100f, home.OwnerSatisfaction.ToString(), true);
            sp.Line("Job security", home.JobSecurity, home.OwnerSatisfaction < 35 ? t.danger : t.textPrimary);
            sp.Rule();
            if (home.Goals.Count == 0) sp.Para("The owner has not set goals for this year yet.", 21f);
            foreach (var g in home.Goals)
            {
                bool done = g.Resolved ? g.Met : g.Actual >= g.Target && g.Target > 0 && g.Id != "playoffs";
                string mark = g.Resolved ? (g.Met ? "MET" : "MISSED") : (g.Target > 0 ? g.Actual + " / " + g.Target : "");
                Color c = g.Resolved ? (g.Met ? t.success : t.danger) : t.textSecondary;
                sp.Line(g.Text, mark, c, 36f, 21f);
            }
            sp.Rule();
            sp.Line("Legacy", home.LegacyScore + "  " + home.LegacyTier.ToUpperInvariant(), t.accent);
        }
    }
}
