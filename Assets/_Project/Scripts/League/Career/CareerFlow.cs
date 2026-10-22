using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    public sealed class CareerOptions
    {
        public string Name = "PIX Football Career";
        public int Seed = 1;
        public int UserTeamId;
        /// <summary>Teams from teams.csv (null = the generated fallback league).</summary>
        public IReadOnlyList<ProTeamDef> Teams;
        /// <summary>Schools and conference/bowl data for the college world (null = no college world, a synthetic draft class is used).</summary>
        public IReadOnlyList<SchoolDef> Schools;
        public IReadOnlyList<ConfBowlDef> Bowls;
        public CareerSettings Settings = new CareerSettings();
    }

    /// <summary>Everything the Unity match needs to play one of the user's games.</summary>
    public sealed class GameSetup
    {
        public ScheduledGame Game;
        public Team Home, Away;
        public bool UserIsHome;
        public RuleSet Rules;
        public SimOptions Options;
        public string Title = "";
        public int WeatherSeed;
    }

    public sealed class WeekReport
    {
        public int Year, Week;
        public string Label = "";
        public ScheduledGame UserGame;
        public GameResult UserResult;
        public List<GameResult> Results = new List<GameResult>();
        public List<InjuryEvent> Injuries = new List<InjuryEvent>();
        public List<RecordEntry> RecordsBroken = new List<RecordEntry>();
        public List<NewsItem> News = new List<NewsItem>();
        public bool SeasonComplete;
    }

    public sealed class StepResult
    {
        public bool Ok;
        public CareerPhase From, To;
        public List<string> Blockers = new List<string>();
        public List<string> Messages = new List<string>();
        public WeekReport Week;
        public FaDayReport FreeAgencyDay;
        public bool PhaseChanged { get { return From != To; } }
    }

    /// <summary>
    /// The franchise calendar. Phases: Preseason, regular season weeks, playoffs, PIX Bowl, then the offseason: awards, retirements, re-sign
    /// window, combine and scouting, draft, rookie signing, free agency, training camp, and back to the next preseason. Every call to
    /// <see cref="Advance"/> performs the next step (one week, one free-agency day, one phase change); <see cref="Blockers"/> says what the user must fix first.
    /// </summary>
    public static class CareerFlow
    {
        // ================================================================ creation
        public static CareerSave NewCareer(CareerOptions o)
        {
            ProLeague pro = o.Teams != null && o.Teams.Count > 0 ? LeagueFactory.CreatePro(o.Seed, o.Teams, o.Schools) : LeagueFactory.CreatePro(o.Seed);
            CollegeWorld college = o.Schools != null && o.Schools.Count > 0 && o.Settings.SimCollege ? CollegeFactory.CreateWorld(o.Seed ^ 0x5A5A5A, o.Schools, o.Bowls) : null;
            var s = new CareerSave
            {
                Name = o.Name, Seed = o.Seed, StartYear = pro.Year, UserTeamId = Math.Max(0, Math.Min(pro.Teams.Count - 1, o.UserTeamId)),
                League = new LeagueSave { Pro = pro, College = college }, Settings = o.Settings ?? new CareerSettings(), Phase = CareerPhase.Preseason
            };
            s.SeedRng(o.Seed);
            Rng rng = s.NewRng();
            pro.Season.InjuriesEnabled = s.Settings.InjuriesEnabled;
            CapManager.ApplyCap(s, s.StartYear);

            var used = CareerFactory.UsedNames(s);
            // abilities for everybody already in the league
            Rng arng = rng.Fork();
            for (int t = 0; t < pro.Teams.Count; t++)
                for (int i = 0; i < pro.Teams[t].Roster.Count; i++) AbilityCatalog.AssignInitial(pro.Teams[t].Roster[i], arng);
            for (int i = 0; i < pro.FreeAgents.Count; i++) AbilityCatalog.AssignInitial(pro.FreeAgents[i], arng);

            // a practice squad for every team, then the cap
            for (int t = 0; t < pro.Teams.Count; t++)
            {
                Team team = pro.Teams[t];
                RosterManager.FillPracticeSquad(s, team, rng.Fork(), used);
                int guard = 0;
                while (team.CapUsed > team.SalaryCap && guard++ < 6) CapManager.AutoFix(s, team);
                while (team.Roster.Count < RosterRules.ActiveLimit && RosterManager.FillOne(s, team, rng, used, false)) { }
                DepthManager.Refresh(s, team);
            }

            TradeEngine.EnsurePicks(s);
            StaffManager.InitContracts(s, rng.Fork());
            StaffManager.GeneratePool(s, rng.Fork());
            for (int t = 0; t < pro.Teams.Count; t++) MoraleEngine.StateOf(s, t).Value = 65;
            OwnerEngine.SetGoals(s);
            HistoryEngine.SnapshotRecords(s);
            Team user = s.UserTeam;
            NewsWriter.Post(s, NewsKind.Season, "Welcome to the " + user.FullName, "You are the new general manager of the " + user.FullName + ". The " + s.Year + " season starts soon.", 3, user.Id);
            return s;
        }

        // ================================================================ queries
        public static ScheduledGame UserGame(CareerSave s)
        {
            if (s.Season == null || s.Season.Phase == SeasonPhase.Complete) return null;
            var games = s.Season.GamesOfWeek(s.Season.Week);
            for (int i = 0; i < games.Count; i++) if (games[i].Involves(s.UserTeamId)) return games[i];
            return null;
        }

        public static bool InSeason(CareerSave s)
        {
            return s.Phase == CareerPhase.RegularSeason || s.Phase == CareerPhase.Playoffs || s.Phase == CareerPhase.PixBowl;
        }

        /// <summary>Every game of the current week (the user can play, watch or simulate any of them).</summary>
        public static List<ScheduledGame> GamesThisWeek(CareerSave s)
        {
            if (s.Season == null || s.Season.Phase == SeasonPhase.Complete) return new List<ScheduledGame>();
            return s.Season.GamesOfWeek(s.Season.Week);
        }

        public static ScheduledGame FindGame(CareerSave s, int gameId)
        {
            var games = GamesThisWeek(s);
            for (int i = 0; i < games.Count; i++) if (games[i].Id == gameId) return games[i];
            return null;
        }

        /// <summary>Everything the Unity match needs to play (or spectate) one game of the current week. Null when the game is not scheduled now or is already played.</summary>
        public static GameSetup SetupGame(CareerSave s, int gameId)
        {
            ScheduledGame g = FindGame(s, gameId);
            if (g == null || g.Played) return null;
            var gs = new GameSetup { Game = g, Home = s.Pro.GetTeam(g.HomeId), Away = s.Pro.GetTeam(g.AwayId), Rules = s.Season.Rules };
            gs.UserIsHome = g.HomeId == s.UserTeamId;
            gs.Options = new SimOptions { Neutral = g.Neutral, ForceWinner = g.Kind != GameKind.Regular, HomeAdvantage = true };
            gs.Title = (string.IsNullOrEmpty(g.Label) ? "Week " + g.Week : g.Label) + ": " + gs.Away.Nickname + " at " + gs.Home.Nickname;
            gs.WeatherSeed = unchecked(s.Seed * 31 + s.Year * 17 + g.Week * 7 + g.Id);
            return gs;
        }

        /// <summary>The user's game of the current week for the Unity match (null on a bye, when the user is out, or when it is already played).</summary>
        public static GameSetup SetupUserGame(CareerSave s)
        {
            ScheduledGame g = UserGame(s);
            return g == null ? null : SetupGame(s, g.Id);
        }

        /// <summary>Records the result of a game that was played in the Unity match (any game of the week). Call <see cref="Advance"/> afterwards to finish the week.</summary>
        public static OpResult SubmitGame(CareerSave s, int gameId, GameResult r)
        {
            ScheduledGame g = FindGame(s, gameId);
            if (g == null) return OpResult.Fail("That game is not scheduled this week.");
            if (g.Played) return OpResult.Fail("The game was already played.");
            if (!s.Season.ApplyExternalResult(g.Id, r, s.NewRng())) return OpResult.Fail("The result does not match the scheduled game.");
            s.PendingResults.Add(r);
            return OpResult.Success();
        }

        public static OpResult SubmitUserGame(CareerSave s, GameResult r)
        {
            ScheduledGame g = UserGame(s);
            if (g == null) return OpResult.Fail("The user has no game this week.");
            return SubmitGame(s, g.Id, r);
        }

        /// <summary>Simulates one game of the week now with the quick simulation (to read its box score before the week advances).</summary>
        public static GameResult SimGame(CareerSave s, int gameId)
        {
            ScheduledGame g = FindGame(s, gameId);
            if (g == null || g.Played) return null;
            GameResult r = s.Season.SimOneGame(g.Id, s.NewRng());
            if (r != null) s.PendingResults.Add(r);
            return r;
        }

        // ================================================================ blockers
        /// <summary>What the user has to do before <see cref="Advance"/> can move on (empty when it can).</summary>
        public static List<string> Blockers(CareerSave s)
        {
            var b = new List<string>();
            Team u = s.UserTeam;
            switch (s.Phase)
            {
                case CareerPhase.Preseason:
                    {
                        var issues = RosterManager.Validate(s, u, true);
                        for (int i = 0; i < issues.Count; i++) if (issues[i].Blocking) b.Add(issues[i].Text);
                        break;
                    }
                case CareerPhase.ReSign:
                    if (u.CapUsed > u.SalaryCap) b.Add("Over the salary cap by " + CareerText.Money(u.CapUsed - u.SalaryCap) + ": restructure or release players before free agency.");
                    break;
                case CareerPhase.Draft:
                    if (CareerDraft.UserOnClock(s)) b.Add("You are on the clock (pick #" + CareerDraft.Current(s).Overall + "): make your pick or use auto-pick.");
                    break;
                case CareerPhase.FreeAgency:
                    if (FreeAgency.IsDone(s))
                    {
                        if (u.CapUsed > u.SalaryCap) b.Add("Over the salary cap by " + CareerText.Money(u.CapUsed - u.SalaryCap) + ".");
                        if (u.Roster.Count > RosterRules.OffseasonLimit) b.Add("Offseason roster limit is " + RosterRules.OffseasonLimit + ".");
                    }
                    break;
                case CareerPhase.TrainingCamp:
                    {
                        var vac = StaffManager.Vacancies(u);
                        for (int i = 0; i < vac.Count; i++) b.Add("Hire a " + StaffManager.RoleName(vac[i]) + ".");
                        if (u.CapUsed > u.SalaryCap) b.Add("Over the salary cap by " + CareerText.Money(u.CapUsed - u.SalaryCap) + ".");
                        break;
                    }
            }
            return b;
        }

        // ================================================================ the step
        public static StepResult Advance(CareerSave s)
        {
            var r = new StepResult { From = s.Phase, To = s.Phase };
            r.Blockers = Blockers(s);
            if (r.Blockers.Count > 0) { r.Ok = false; return r; }
            Rng rng = s.NewRng();
            switch (s.Phase)
            {
                case CareerPhase.Preseason: StartSeason(s, rng, r); break;
                case CareerPhase.RegularSeason:
                case CareerPhase.Playoffs:
                case CareerPhase.PixBowl: AdvanceWeek(s, rng, r); break;
                case CareerPhase.Awards: ToRetirements(s, rng, r); break;
                case CareerPhase.Retirements: ToReSign(s, rng, r); break;
                case CareerPhase.ReSign: ToScouting(s, rng, r); break;
                case CareerPhase.Scouting: ToDraft(s, rng, r); break;
                case CareerPhase.Draft: AdvanceDraft(s, rng, r); break;
                case CareerPhase.RookieSigning: ToFreeAgency(s, rng, r); break;
                case CareerPhase.FreeAgency: AdvanceFreeAgency(s, rng, r); break;
                case CareerPhase.TrainingCamp: ToPreseason(s, rng, r); break;
            }
            r.To = s.Phase;
            r.Ok = true;
            return r;
        }

        // ---------------------------------------------------------------- preseason -> season
        private static void StartSeason(CareerSave s, Rng rng, StepResult r)
        {
            var used = CareerFactory.UsedNames(s);
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                Team team = s.Pro.Teams[t];
                if (team.Id == s.UserTeamId)
                {
                    DepthManager.Refresh(s, team);
                    continue;
                }
                RosterManager.FinalCuts(s, team, rng.Fork(), used);
            }
            RosterManager.ProcessWaivers(s, rng.Fork());
            s.Pro.Season.InjuriesEnabled = s.Settings.InjuriesEnabled;
            s.Phase = CareerPhase.RegularSeason;
            HistoryEngine.SnapshotRecords(s);
            r.Messages.Add(s.Year + " season: week 1.");
        }

        // ---------------------------------------------------------------- weeks
        private static void AdvanceWeek(CareerSave s, Rng rng, StepResult r)
        {
            Season season = s.Season;
            var report = new WeekReport { Year = s.Year, Week = season.Week, Label = CareerText.PhaseLabel(s) };
            ScheduledGame ug = UserGame(s);
            report.UserGame = ug;
            int newsStart = s.News.Count;
            var used = CareerFactory.UsedNames(s);

            // ---- before the games
            RosterManager.ProcessWaivers(s, rng.Fork());
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                Team team = s.Pro.Teams[t];
                if (team.Id == s.UserTeamId)
                {
                    if (s.Settings.AutoInjuredReserve) AutoIr(s, team);
                    DepthManager.Refresh(s, team);
                    continue;
                }
                RosterManager.MaintainCpuTeam(s, team, rng.Fork(), used);
            }
            TradeEngine.ExpireOffers(s);
            if (s.Phase == CareerPhase.RegularSeason && TradeEngine.IsOpen(s))
            {
                TradeEngine.SimulateCpuTrades(s, rng.Fork(), 1, 25);
                TradeEngine.GenerateOffers(s, rng.Fork());
            }

            // ---- the games
            var pending = new List<GameResult>(s.PendingResults);
            s.PendingResults.Clear();
            season.SimWeek();
            if (s.College != null && s.Settings.SimCollege && s.College.Season != null && !s.College.Season.IsComplete) s.College.Season.SimWeek();

            // ---- after the games
            var results = new List<GameResult>();
            results.AddRange(pending);
            results.AddRange(season.LastWeekResults);
            report.Results = results;
            for (int i = 0; i < results.Count; i++)
            {
                GameResult g = results[i];
                if (g.HomeTeamId == s.UserTeamId || g.AwayTeamId == s.UserTeamId) report.UserResult = g;
                for (int k = 0; k < g.Injuries.Count; k++)
                {
                    GameInjury gi = g.Injuries[k];
                    Team t = s.Pro.GetTeam(gi.TeamId);
                    Player p = t.GetPlayer(gi.PlayerId);
                    if (p == null) continue;
                    bool starter = Offseason.IsStarter(t, p);
                    var ev = new InjuryEvent { Year = s.Year, Week = report.Week, PlayerId = p.Id, Name = p.Name, TeamId = t.Id, Pos = p.Position, Type = gi.Type, Weeks = gi.Weeks, Starter = starter };
                    report.Injuries.Add(ev);
                    s.InjuryLog.Add(ev);
                    NewsWriter.Injury(s, t, p, gi.Type, gi.Weeks, starter);
                }
                Upset(s, g);
            }
            if (season.Phase == SeasonPhase.Complete)
            {
                for (int i = 0; i < results.Count; i++)
                {
                    ScheduledGame fin = null;
                    for (int q = season.Games.Count - 1; q >= 0; q--) if (season.Games[q].Kind == GameKind.Final) { fin = season.Games[q]; break; }
                    if (fin != null && results[i].HomeTeamId == fin.HomeId && results[i].AwayTeamId == fin.AwayId) { CaptureBowlMvp(s, results[i]); break; }
                }
            }
            if (s.InjuryLog.Count > 400) s.InjuryLog.RemoveRange(0, s.InjuryLog.Count - 300);
            RosterManager.HealReserves(s);
            report.RecordsBroken = HistoryEngine.DetectBrokenRecords(s);
            MoraleEngine.UpdateWeek(s, rng.Fork());

            // ---- the calendar
            if (season.Phase == SeasonPhase.Complete)
            {
                report.SeasonComplete = true;
                FinishSeason(s, rng, r);
            }
            else if (season.Phase == SeasonPhase.Postseason) s.Phase = season.PostseasonStage >= 4 ? CareerPhase.PixBowl : CareerPhase.Playoffs;
            else s.Phase = CareerPhase.RegularSeason;

            for (int i = newsStart; i < s.News.Count; i++) report.News.Add(s.News[i]);
            r.Week = report;
            r.Messages.Add(report.Label + " complete.");
        }

        private static void AutoIr(CareerSave s, Team team)
        {
            for (int i = team.Roster.Count - 1; i >= 0; i--)
            {
                Player p = team.Roster[i];
                if (p.Injury.IsInjured && p.Injury.WeeksOut >= 6) RosterManager.PlaceOnIR(s, team, p.Id);
            }
        }

        private static void Upset(CareerSave s, GameResult g)
        {
            if (g.Tie) return;
            Team w = s.Pro.GetTeam(g.WinnerTeamId), l = s.Pro.GetTeam(g.LoserTeamId);
            if (w == null || l == null) return;
            int diff = l.TeamOverall() - w.TeamOverall();
            if (diff >= 9 && Math.Abs(g.HomeScore - g.AwayScore) >= 3)
                NewsWriter.Post(s, NewsKind.Upset, w.Nickname + " stun the " + l.Nickname + " " + Math.Max(g.HomeScore, g.AwayScore) + "-" + Math.Min(g.HomeScore, g.AwayScore),
                    "The " + w.Nickname + " (" + w.TeamOverall() + " OVR) upset the " + l.Nickname + " (" + l.TeamOverall() + " OVR).", diff >= 14 ? 4 : 3, w.Id, l.Id);
        }

        private static void CaptureBowlMvp(CareerSave s, GameResult g)
        {
            Team winner = s.Pro.GetTeam(g.WinnerTeamId);
            if (winner == null) return;
            Player best = null; float bestV = -1f;
            for (int i = 0; i < g.PlayerLines.Count; i++)
            {
                PlayerGameLine l = g.PlayerLines[i];
                if (l.TeamId != winner.Id) continue;
                float v = AwardsCalculator.OffenseValue(l.Stats) + AwardsCalculator.DefenseValue(l.Stats) * 2.5f + l.Stats.FgMade * 1.5f;
                if (v > bestV) { Player p = winner.GetPlayer(l.PlayerId); if (p != null) { bestV = v; best = p; } }
            }
            s.BowlScoreText = Math.Max(g.HomeScore, g.AwayScore) + "-" + Math.Min(g.HomeScore, g.AwayScore);
            if (best != null) { s.BowlMvpId = best.Id; s.BowlMvpName = best.Name; s.BowlMvpTeamId = winner.Id; }
        }

        private static void FinishSeason(CareerSave s, Rng rng, StepResult r)
        {
            // the college world finishes its own season alongside
            if (s.College != null && s.College.Season != null) s.College.Season.SimToEnd();
            HistoryEngine.FinalizeSeason(s, rng.Fork());
            s.Phase = CareerPhase.Awards;
            r.Messages.Add("The " + s.Year + " season is over.");
        }

        // ---------------------------------------------------------------- awards -> retirements
        private static void ToRetirements(CareerSave s, Rng rng, StepResult r)
        {
            int retired = 0;
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                Team team = s.Pro.Teams[t];
                var all = new List<Player>(CapManager.AllUnderContract(team));
                for (int i = 0; i < all.Count; i++)
                {
                    Player p = all[i];
                    p.Bio.Age++;   // the probability is for the age he is about to turn
                    float prob = Retirement.Probability(p, false);
                    p.Bio.Age--;
                    if (!rng.Chance(prob)) continue;
                    int seasons = Seasons(p);
                    NewsWriter.Retirement(s, team, p, seasons);
                    HistoryEngine.RecordRetirement(s, p, team.Id);
                    Ledger.Tx(s, TransactionKind.Retirement, team.Id, p, 0, p.Name + " retired.");
                    RosterManager.RemoveFromTeam(team, p);
                    Offseason.Retire(s.Pro, p);
                    retired++;
                }
                DepthManager.Refresh(s, team);
            }
            var pool = s.Pro.FreeAgents;
            for (int i = pool.Count - 1; i >= 0; i--)
            {
                Player p = pool[i];
                p.Bio.Age++;
                bool junk = p.Overall < 42 || (p.Bio.Age >= 35 && p.Overall < 62);
                bool retire = junk || Retirement.Probability(p, true) > 0f && rng.Chance(Retirement.Probability(p, true));
                p.Bio.Age--;
                if (!retire) continue;
                if (Seasons(p) > 0) HistoryEngine.RecordRetirement(s, p, RosterManager.PriorTeamOf(s, p.Id));
                Offseason.Retire(s.Pro, p);
                pool.RemoveAt(i);
                retired++;
            }
            List<HofEntry> hof = HistoryEngine.InductClass(s);
            s.Phase = CareerPhase.Retirements;
            r.Messages.Add(retired + " players retired; " + hof.Count + " elected to the Hall of Fame.");
        }

        private static int Seasons(Player p)
        {
            int n = 0;
            for (int i = 0; i < p.Career.Count; i++) if (!p.Career[i].Postseason && p.Career[i].Games > 0) n++;
            return n;
        }

        // ---------------------------------------------------------------- retirements -> re-sign window
        private static void ToReSign(CareerSave s, Rng rng, StepResult r)
        {
            int newYear = s.Year + 1;
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                Team team = s.Pro.Teams[t];
                team.DeadCap = team.DeadCapNext;
                team.DeadCapNext = 0;
                foreach (Player p in CapManager.AllUnderContract(team))
                {
                    bool ps = team.PracticeSquad.Contains(p);
                    p.Contract.AdvanceYear();
                    if (ps && p.Contract.YearsLeft <= 0) { p.Contract = CapMath.PracticeSquad(s, newYear); }
                }
                // squad members that are too old for it are let go
                for (int i = team.PracticeSquad.Count - 1; i >= 0; i--)
                {
                    Player p = team.PracticeSquad[i];
                    if (p.Experience + 1 > RosterRules.PracticeSquadMaxExperience || p.Bio.Age >= 27) { team.PracticeSquad.RemoveAt(i); p.Contract = new Contract(); p.TeamId = -1; s.Pro.FreeAgents.Add(p); }
                }
            }
            CapManager.ApplyCap(s, newYear);
            // waivers clear into the pool
            RosterManager.ProcessWaivers(s, rng.Fork());
            for (int i = 0; i < s.Waivers.Count; i++) { Player p = s.Waivers[i].Player; p.Contract = new Contract(); s.Pro.FreeAgents.Add(p); }
            s.Waivers.Clear();
            TradeEngine.ExpireOffers(s, true);
            s.FaOffers.Clear();
            StaffManager.NewYear(s, rng.Fork());
            MoraleEngine.NewYear(s, rng.Fork());
            s.Phase = CareerPhase.ReSign;
            r.Messages.Add("The new league year begins: cap " + CareerText.Money(CapMath.CapFor(s, newYear)) + ". Re-sign your expiring players.");
        }

        // ---------------------------------------------------------------- re-sign window -> scouting
        private static void ToScouting(CareerSave s, Rng rng, StepResult r)
        {
            Negotiator.CpuReSign(s, rng.Fork());
            int gone = Negotiator.ReleaseUnsigned(s, s.UserTeam);
            // everybody must be cap compliant when free agency is about to start
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                Team team = s.Pro.Teams[t];
                int guard = 0;
                while (team.CapUsed > team.SalaryCap && guard++ < 6) CapManager.AutoFix(s, team);
                DepthManager.Refresh(s, team);
            }
            CareerDraft.PrepareClass(s, rng.Fork());
            s.Phase = CareerPhase.Scouting;
            r.Messages.Add(gone + " of your players left for free agency. The draft class of " + s.Pro.DraftClass.Count + " prospects is ready to scout.");
        }

        // ---------------------------------------------------------------- scouting -> draft
        private static void ToDraft(CareerSave s, Rng rng, StepResult r)
        {
            TradeEngine.SimulateCpuTrades(s, rng.Fork(), 4, 60);
            CareerDraft.Build(s);
            s.Phase = CareerPhase.Draft;
            r.Messages.Add("The draft is open: " + s.Draft.Slots.Count + " picks.");
        }

        private static void AdvanceDraft(CareerSave s, Rng rng, StepResult r)
        {
            var made = CareerDraft.SimToUserPick(s, rng.Fork());
            r.Messages.Add(made.Count + " picks made.");
            if (s.Draft.Finished)
            {
                CareerDraft.FinishDraft(s, rng.Fork());
                s.Phase = CareerPhase.RookieSigning;
                r.Messages.Add("The draft is complete.");
            }
            else r.Messages.Add("You are on the clock with pick #" + CareerDraft.Current(s).Overall + ".");
        }

        // ---------------------------------------------------------------- rookie signing -> free agency
        private static void ToFreeAgency(CareerSave s, Rng rng, StepResult r)
        {
            Team u = s.UserTeam;
            int signed = CareerDraft.SignAllRookies(s, u.Id);
            var left = CareerDraft.UnsignedRights(s, u.Id);
            for (int i = 0; i < left.Count; i++)
            {
                Player p = u.FindAnyPlayer(left[i].PlayerId);
                if (p == null) continue;
                RosterManager.RemoveFromTeam(u, p);
                p.TeamId = -1; p.Contract = new Contract();
                s.Pro.FreeAgents.Add(p);
                left[i].Signed = true;
                r.Messages.Add(p.Name + " could not be signed (cap) and became a free agent.");
            }
            DepthManager.Refresh(s, u);
            s.RookieRights.RemoveAll(x => x.Signed);
            FreeAgency.Open(s);
            s.Phase = CareerPhase.FreeAgency;
            r.Messages.Add(signed + " rookies signed. Free agency opens.");
        }

        private static void AdvanceFreeAgency(CareerSave s, Rng rng, StepResult r)
        {
            if (!FreeAgency.IsDone(s))
            {
                FaDayReport day = FreeAgency.AdvanceDay(s, rng.Fork());
                r.FreeAgencyDay = day;
                r.Messages.Add("Day " + day.Day + ": " + day.Signings.Count + " signings.");
                return;
            }
            ToTrainingCamp(s, rng, r);
        }

        /// <summary>Plays out all remaining free agency days.</summary>
        public static StepResult SkipFreeAgency(CareerSave s)
        {
            var r = new StepResult { From = s.Phase, To = s.Phase, Ok = true };
            if (s.Phase != CareerPhase.FreeAgency) return r;
            FreeAgency.FinishAll(s, s.NewRng());
            r.Messages.Add("Free agency bidding is over.");
            return r;
        }

        // ---------------------------------------------------------------- free agency -> training camp (progression)
        private static void ToTrainingCamp(CareerSave s, Rng rng, StepResult r)
        {
            s.FaOffers.Clear();
            s.FaOpen = false;
            s.LastCamp.Clear();
            // cap deadline: everybody is under the cap before camp (the CPU fixes itself, the user was blocked above)
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                Team team = s.Pro.Teams[t];
                int cg = 0;
                while (team.CapUsed > team.SalaryCap && cg++ < 8) CapManager.AutoFix(s, team);
            }
            Rng prng = rng.Fork();
            int userChanges = 0;
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                Team team = s.Pro.Teams[t];
                bool user = team.Id == s.UserTeamId;
                var all = new List<Player>(CapManager.AllUnderContract(team));
                for (int i = 0; i < all.Count; i++)
                {
                    Player p = all[i];
                    if (p.DraftYear == s.Year && p.Experience == 0) { p.Injury.Heal(30); continue; }   // this year's class: no progression before their first season
                    int before = p.Overall;
                    int dev = StaffManager.DevelopmentRating(team, p);
                    Progression.Advance(p, prng, dev);
                    p.Injury.Heal(30);
                    if (p.Injury.WeeksOut <= 0) p.Injury.WeeksOut = 0;
                    string ab = AbilityCatalog.Progress(p, prng, Math.Max(0, (dev - 65) / 2));
                    if (user)
                    {
                        s.LastCamp.Add(new CampResult { PlayerId = p.Id, TeamId = team.Id, Name = p.Name, Pos = p.Position, Age = p.Age, OvrBefore = before, OvrAfter = p.Overall, AbilityChange = ab });
                        userChanges++;
                    }
                }
            }
            for (int i = 0; i < s.Pro.FreeAgents.Count; i++)
            {
                Player p = s.Pro.FreeAgents[i];
                if (p.DraftYear == s.Year && p.Experience == 0) continue;
                Progression.Advance(p, prng, 60);
                p.Injury.Heal(30);
                AbilityCatalog.Progress(p, prng, 0);
            }
            // healed players come back from injured reserve for the new season
            for (int t = 0; t < s.Pro.Teams.Count; t++)
            {
                Team team = s.Pro.Teams[t];
                for (int i = team.InjuredReserve.Count - 1; i >= 0; i--)
                {
                    Player p = team.InjuredReserve[i];
                    if (p.Injury.IsInjured) continue;
                    team.InjuredReserve.RemoveAt(i);
                    p.Injury.IrWeeks = -1;
                    Offseason.AssignJersey(team, p, prng);
                    team.AddPlayer(p);
                }
            }
            // the market stays a sensible size
            var pool = s.Pro.FreeAgents;
            if (pool.Count > 700)
            {
                pool.Sort((a, b) => Offseason.Value(b).CompareTo(Offseason.Value(a)));
                for (int i = pool.Count - 1; i >= 500; i--) { Offseason.Retire(s.Pro, pool[i]); pool.RemoveAt(i); }
            }
            for (int t = 0; t < s.Pro.Teams.Count; t++) DepthManager.Refresh(s, s.Pro.Teams[t]);
            s.Phase = CareerPhase.TrainingCamp;
            r.Messages.Add("Training camp: " + userChanges + " of your players progressed.");
        }

        // ---------------------------------------------------------------- training camp -> next preseason
        private static void ToPreseason(CareerSave s, Rng rng, StepResult r)
        {
            ProLeague pro = s.Pro;
            // the rest of the staff business, then the calendar rolls
            pro.Year++;
            pro.Season = Season.CreatePro(pro, pro.Year, rng.Fork());
            pro.Season.InjuriesEnabled = s.Settings.InjuriesEnabled;
            pro.Bind();
            CapManager.ApplyCap(s, s.Year);
            TradeEngine.EnsurePicks(s);
            s.Draft = new DraftState { Year = s.Year };
            s.BowlMvpId = 0; s.BowlMvpName = ""; s.BowlMvpTeamId = -1; s.BowlScoreText = "";
            s.Negotiations.RemoveAll(n => n.Year < s.Year - 1);
            OwnerEngine.SetGoals(s);
            HistoryEngine.SnapshotRecords(s);
            s.Phase = CareerPhase.Preseason;
            r.Messages.Add("Preseason " + s.Year + ": cut down to " + RosterRules.ActiveLimit + " before week 1.");
        }

        // ================================================================ auto pilot
        /// <summary>
        /// Resolves everything the user would normally decide, the way a CPU team would. Used by "sim to next season" and by the soak test.
        /// After it returns, <see cref="Blockers"/> is empty (or the remaining blockers are returned).
        /// </summary>
        public static List<string> AutoResolveUser(CareerSave s)
        {
            Team u = s.UserTeam;
            Rng rng = s.NewRng();
            var used = CareerFactory.UsedNames(s);
            switch (s.Phase)
            {
                case CareerPhase.Preseason:
                    RosterManager.FinalCuts(s, u, rng, used);
                    break;
                case CareerPhase.ReSign:
                    Negotiator.ReSignTeam(s, u, rng);
                    {
                        int guard = 0;
                        while (u.CapUsed > u.SalaryCap && guard++ < 6) CapManager.AutoFix(s, u);
                    }
                    break;
                case CareerPhase.RegularSeason:
                case CareerPhase.Playoffs:
                case CareerPhase.PixBowl:
                    RosterManager.MaintainCpuTeam(s, u, rng, used);
                    break;
                case CareerPhase.Scouting:
                    break;
                case CareerPhase.Draft:
                    { int dg = 0; while (CareerDraft.UserOnClock(s) && dg++ < 300) CareerDraft.AutoPick(s, rng); }
                    break;
                case CareerPhase.FreeAgency:
                    if (!FreeAgency.IsDone(s)) FreeAgency.AutoBid(s, u, rng);
                    else
                    {
                        int guard = 0;
                        while (u.CapUsed > u.SalaryCap && guard++ < 6) CapManager.AutoFix(s, u);
                        RosterManager.CutDownTo(s, u, RosterRules.OffseasonLimit, false);
                    }
                    break;
                case CareerPhase.TrainingCamp:
                    StaffManager.HireBest(s, u);
                    {
                        int guard = 0;
                        while (u.CapUsed > u.SalaryCap && guard++ < 6) CapManager.AutoFix(s, u);
                    }
                    break;
            }
            return Blockers(s);
        }

        /// <summary>Advances (resolving user blockers automatically) until the phase is reached; returns the number of steps taken.</summary>
        public static int AdvanceUntil(CareerSave s, CareerPhase target, int maxSteps = 400)
        {
            int steps = 0;
            while (s.Phase != target && steps < maxSteps)
            {
                List<string> b = AutoResolveUser(s);
                int tries = 0;
                while (b.Count > 0 && tries++ < 5) b = AutoResolveUser(s);
                if (b.Count > 0) throw new InvalidOperationException("Could not resolve: " + string.Join("; ", b.ToArray()));
                StepResult r = Advance(s);
                if (!r.Ok) throw new InvalidOperationException("Blocked: " + string.Join("; ", r.Blockers.ToArray()));
                steps++;
            }
            return steps;
        }

        /// <summary>Plays a whole year with the auto pilot: from any phase to the next preseason.</summary>
        public static int SimToNextSeason(CareerSave s)
        {
            int startYear = s.Year;
            int steps = 0;
            if (s.Phase == CareerPhase.Preseason) steps += AdvanceUntil(s, CareerPhase.RegularSeason);
            steps += AdvanceUntil(s, CareerPhase.Preseason);
            return steps;
        }
    }
}
