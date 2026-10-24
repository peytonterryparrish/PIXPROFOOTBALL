using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>Regression tests for the league-engine audit fixes (QA-560 .. QA-622).</summary>
    public class LeagueEngineFixTests
    {
        // ------------------------------------------------------------------ shared fixtures
        private static ProLeague _offseasons;
        /// <summary>A standalone league taken through three full seasons and offseasons (one report per offseason).</summary>
        private static ProLeague ThreeOffseasons
        {
            get
            {
                if (_offseasons == null)
                {
                    ProLeague lg = LeagueFactory.CreatePro(52);
                    var rng = new Rng(4242);
                    for (int y = 0; y < 3; y++)
                    {
                        lg.Season.SimToEnd();
                        _seasonAwards.Add(new[] { lg.Season.MvpId, lg.Season.OpoyId, lg.Season.RoyId });
                        Offseason.Run(lg, null, rng);
                    }
                    _offseasons = lg;
                }
                return _offseasons;
            }
        }
        private static readonly List<int[]> _seasonAwards = new List<int[]>();

        private static List<GameResult> _games;
        /// <summary>200 pro games between assorted teams with the play log kept.</summary>
        private static List<GameResult> Games
        {
            get
            {
                if (_games == null)
                {
                    ProLeague lg = LeagueTestUtil.FreshPro(7);
                    var rng = new Rng(2024);
                    _games = new List<GameResult>();
                    for (int g = 0; g < 200; g++)
                    {
                        Team h = lg.Teams[g % 32], a = lg.Teams[(g * 7 + 3) % 32 == g % 32 ? (g + 5) % 32 : (g * 7 + 3) % 32];
                        _games.Add(QuickSim.SimGame(h, a, RuleSet.Pro(), rng, new SimOptions { KeepPlayLog = true }));
                    }
                }
                return _games;
            }
        }

        // ------------------------------------------------------------------ QA-560 / QA-612 roster additions
        [Test]
        public void JerseyNumbersStayUniqueOnEveryRosterAfterOffseasons()
        {
            foreach (Team t in ThreeOffseasons.Teams)
            {
                var dupes = t.Roster.GroupBy(p => p.Bio.JerseyNumber).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
                Assert.IsEmpty(dupes, t.FullName + " duplicate numbers: " + string.Join(",", dupes));
            }
        }

        [Test]
        public void AssignJerseyMovesAClashingNumberAndKeepsAFreeOne()
        {
            ProLeague lg = LeagueTestUtil.FreshPro(3);
            Team t = lg.Teams[0];
            Player other = t.Roster[1];
            Player p = t.Roster[0];
            t.RemovePlayer(p);
            p.Bio.JerseyNumber = other.Bio.JerseyNumber;
            Offseason.AssignJersey(t, p, new Rng(1));
            Assert.AreNotEqual(other.Bio.JerseyNumber, p.Bio.JerseyNumber);
            int free = 99;
            while (t.Roster.Any(r => r.Bio.JerseyNumber == free)) free--;
            p.Bio.JerseyNumber = free;
            Offseason.AssignJersey(t, p, new Rng(1));
            Assert.AreEqual(free, p.Bio.JerseyNumber);
        }

        [Test]
        public void NamesStayUniqueAcrossTheLeagueAfterOffseasons()
        {
            ProLeague lg = ThreeOffseasons;
            var names = new List<string>();
            foreach (Team t in lg.Teams)
            {
                names.AddRange(t.Roster.Select(p => p.Name));
                names.AddRange(t.Staff.Select(c => c.Name));
            }
            names.AddRange(lg.FreeAgents.Select(p => p.Name));
            var dupes = names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.IsEmpty(dupes, "duplicate names: " + string.Join("; ", dupes.Take(10)));
        }

        // ------------------------------------------------------------------ QA-561 / QA-562 cap
        [Test]
        public void ReleasingAGuaranteedContractLeavesDeadCap()
        {
            var team = new Team { Id = 0 };
            Contract c = Contract.Create(4, 12000000L, 0.30f, 0.80f, 2026);
            c.YearsLeft = 3;
            long dead = Offseason.DeadMoneyOf(c);
            Assert.Greater(dead, 5000000L);
            Offseason.AddDeadCap(team, c);
            Assert.Greater(team.DeadCap, 0L);
            Assert.Greater(team.DeadCapNext, 0L, "a multi-year deal spreads its dead money over two seasons");
            Assert.AreEqual(dead, team.DeadCap + team.DeadCapNext);
            c.YearsLeft = 0;
            Assert.AreEqual(0L, Offseason.DeadMoneyOf(c), "an expired contract costs nothing");
            Assert.Greater(team.CapUsed, 0L, "dead cap counts against the cap");
        }

        [Test]
        public void OffseasonCutsAccrueDeadCapAndStayUnderTheCap()
        {
            ProLeague lg = ThreeOffseasons;
            Assert.IsTrue(lg.Teams.All(t => t.CapUsed <= t.SalaryCap), "cap respected");
            Assert.IsTrue(lg.Teams.All(t => t.Roster.Count == LeagueRules.RosterSize), "roster size kept");
            Assert.Greater(lg.Teams.Max(t => t.DeadCap), 0L, "somebody carries dead cap");
        }

        [Test]
        public void CapEnforcementRestructuresBigDealsInsteadOfShavingEverySalary()
        {
            ProLeague lg = LeagueTestUtil.FreshPro(11);
            Team t = lg.Teams[4];
            // push the team 12M over the cap with a handful of big multi-year deals
            var big = t.Roster.OrderByDescending(p => p.Contract.CapHit).Take(8).ToList();
            foreach (Player p in big) p.Contract = Contract.Create(5, p.Contract.Aav + 3000000L, 0.1f, 0.5f, 2026);
            long over = t.CapUsed - t.SalaryCap;
            if (over < 12000000L) foreach (Player p in big) p.Contract = Contract.Create(5, p.Contract.Aav + 9000000L, 0.1f, 0.5f, 2026);
            Assert.Greater(t.CapUsed, t.SalaryCap);
            var aavBefore = t.Roster.ToDictionary(p => p.Id, p => p.Contract.Aav);
            var rep = new OffseasonReport();
            Offseason.EnforceCap(t, rep, lg);
            Assert.LessOrEqual(t.CapUsed, t.SalaryCap);
            Assert.Greater(rep.Restructures, 0);
            int shaved = t.Roster.Count(p => aavBefore.ContainsKey(p.Id) && p.Contract.Aav != aavBefore[p.Id]);
            Assert.AreEqual(0, shaved, "no salary was cut across the board");
            Assert.LessOrEqual(rep.Restructures, 40, "a few big deals are restructured, not hundreds of contracts");
        }

        [Test]
        public void RestructureLowersThisYearAndRaisesLaterYears()
        {
            Contract c = Contract.Create(4, 20000000L, 0.1f, 0.5f, 2026);
            long thisYear = c.CapHit, later = c.CapHits[3], total = c.CapHits.Sum();
            Offseason.Restructure(c);
            Assert.Less(c.CapHit, thisYear);
            Assert.Greater(c.CapHits[3], later);
            Assert.AreEqual(total, c.CapHits.Sum(), 4, "the total money is unchanged");
        }

        // ------------------------------------------------------------------ QA-563 injuries
        [Test]
        public void AFreshOneWeekInjuryCostsTheNextGame()
        {
            int cases = 0;
            for (int seed = 61; seed < 66 && cases < 4; seed++)
            {
                ProLeague lg = LeagueFactory.CreatePro(seed);
                Season s = lg.Season;
                for (int w = 1; w <= 15; w += 2)
                {
                    var hurtBefore = new HashSet<int>(lg.AllRosterPlayers().Where(p => p.Injury.IsInjured).Select(p => p.Id));
                    s.SimWeek();
                    var fresh = lg.AllRosterPlayers().Where(p => p.Injury.WeeksOut == 1 && !hurtBefore.Contains(p.Id)).ToList();
                    var next = s.GamesOfWeek(w + 1);
                    s.SimWeek();
                    foreach (Player p in fresh)
                    {
                        if (!next.Any(g => g.Involves(p.TeamId))) continue;   // bye week
                        cases++;
                        Assert.IsFalse(s.LastWeekResults.Any(r => r.PlayerLines.Any(l => l.PlayerId == p.Id)), p + " played the week after a one-week injury");
                        Assert.AreEqual(0, p.Injury.WeeksOut, "healed after the missed game");
                    }
                }
            }
            Assert.Greater(cases, 0, "the sample contained one-week injuries");
        }

        // ------------------------------------------------------------------ QA-565 / QA-566 / QA-567 awards
        [Test]
        public void RookieOfTheYearIsAwardedEverySeason()
        {
            Assert.IsNotNull(ThreeOffseasons);
            Assert.AreEqual(3, _seasonAwards.Count);
            for (int i = 0; i < _seasonAwards.Count; i++) Assert.AreNotEqual(0, _seasonAwards[i][2], "ROY of season " + i);
        }

        [Test]
        public void MvpIsNotAlwaysTheOffensivePlayerOfTheYear()
        {
            Assert.IsNotNull(ThreeOffseasons);
            // across several independent seasons at least one MVP differs from the OPOY winner
            int different = _seasonAwards.Count(a => a[0] != a[1]);
            if (different == 0)
            {
                for (int seed = 70; seed < 74 && different == 0; seed++)
                {
                    ProLeague lg = LeagueFactory.CreatePro(seed);
                    lg.Season.SimToEnd();
                    if (lg.Season.MvpId != lg.Season.OpoyId) different++;
                }
            }
            Assert.Greater(different, 0);
        }

        [Test]
        public void InterceptionLeadersAreListedMostFirst()
        {
            ProLeague lg = LeagueFactory.CreatePro(62);
            lg.Season.SimToEnd();
            var top = LeagueLeaders.Top(lg.Season, StatCategory.PassInt, 10);
            Assert.Greater(top.Count, 3);
            for (int i = 1; i < top.Count; i++) Assert.GreaterOrEqual(top[i - 1].Value, top[i].Value);
        }

        // ------------------------------------------------------------------ QA-568 / QA-569 pro schedule
        [Test]
        public void ProScheduleHasShortStreaksNoRematchesAndADivisionalFinale()
        {
            for (int seed = 1; seed <= 3; seed++)
            {
                ProLeague lg = LeagueTestUtil.FreshPro(seed);
                Season s = lg.Season;
                int maxStreak = 0, over3 = 0, rematches = 0;
                for (int t = 0; t < 32; t++)
                {
                    int streak = 0, prevOpp = -1, prevWeek = -9;
                    foreach (ScheduledGame g in s.GamesOfTeam(t))
                    {
                        streak = g.HomeId == t ? (streak > 0 ? streak + 1 : 1) : (streak < 0 ? streak - 1 : -1);
                        maxStreak = Math.Max(maxStreak, Math.Abs(streak));
                        if (Math.Abs(streak) > 3) over3++;
                        if (g.OpponentOf(t) == prevOpp && g.Week == prevWeek + 1) rematches++;
                        prevOpp = g.OpponentOf(t); prevWeek = g.Week;
                    }
                }
                Assert.LessOrEqual(maxStreak, 5, "longest home/away streak, seed " + seed);
                Assert.LessOrEqual(over3, 12, "games that stretch a streak past three, seed " + seed);
                Assert.AreEqual(0, rematches, "back-to-back rematches, seed " + seed);
                var finale = s.Games.Where(g => g.Week == 18).ToList();
                Assert.AreEqual(16, finale.Count);
                Assert.IsTrue(finale.All(g => lg.Teams[g.HomeId].DivisionId == lg.Teams[g.AwayId].DivisionId), "week 18 is all divisional, seed " + seed);
                for (int t = 0; t < 32; t++) Assert.AreEqual(17, s.RegularGameCount(t));
            }
        }

        [Test]
        public void SeventeenthGameHostAlternatesInsideEveryDivision()
        {
            ProLeague lg = LeagueTestUtil.FreshPro(5);
            Season s = lg.Season;
            // every team has 8 home games before the 17th game, so the nine-home teams are exactly the 17th-game hosts
            var nineHome = new HashSet<int>(Enumerable.Range(0, 32).Where(t => s.Games.Count(g => g.HomeId == t) == 9));
            Assert.AreEqual(16, nineHome.Count);
            foreach (Conference c in lg.Conferences)
                foreach (Division d in c.Divisions)
                    Assert.AreEqual(2, d.TeamIds.Count(id => nineHome.Contains(id)), "division " + d.Name + " has two 17th-game hosts");
        }

        // ------------------------------------------------------------------ QA-570 / QA-571 / QA-574 college
        [Test]
        public void CollegeConferenceGamesAreTheSameEverySeedAndBalancedAtHome()
        {
            CollegeWorld w = LeagueTestUtil.LoadCollege(8);
            List<Matchup> a = ScheduleGenerator.CollegeMatchups(w, new Rng(1));
            List<Matchup> b = ScheduleGenerator.CollegeMatchups(w, new Rng(999));
            Func<List<Matchup>, HashSet<string>> confPairs = list =>
            {
                var set = new HashSet<string>();
                foreach (Matchup m in list)
                    if (!m.NonConference && w.Schools[m.Home].ConferenceId >= 0 && w.Schools[m.Home].ConferenceId == w.Schools[m.Away].ConferenceId)
                        set.Add(m.Home + ">" + m.Away);
                return set;
            };
            CollectionAssert.AreEquivalent(confPairs(a), confPairs(b), "conference schedule does not depend on the random stream");
            foreach (Conference c in w.Conferences)
            {
                foreach (int id in c.TeamIds)
                {
                    int home = a.Count(m => m.Home == id && !m.NonConference && w.Schools[m.Away].ConferenceId == c.Id);
                    int away = a.Count(m => m.Away == id && !m.NonConference && w.Schools[m.Home].ConferenceId == c.Id);
                    Assert.LessOrEqual(Math.Abs(home - away), 1, w.Schools[id].Name + " home/away conference games " + home + "/" + away);
                }
            }
        }

        [Test]
        public void CollegeSeasonFitsFourteenEvenWeeksWithEveryGamePlaced()
        {
            for (int seed = 1; seed <= 2; seed++)
            {
                CollegeWorld w = LeagueTestUtil.LoadCollege(seed);
                Season s = w.Season;
                Assert.AreEqual(14, s.RegularWeeks);
                Assert.IsTrue(s.Games.All(g => g.Week >= 1 && g.Week <= 14), "every game has a week");
                foreach (var grp in s.Games.GroupBy(g => g.Week)) Assert.That(grp.Count(), Is.InRange(54, 64), "games in week " + grp.Key);
                for (int t = 0; t < w.Schools.Count; t++)
                {
                    var weeks = s.Games.Where(g => g.Involves(t)).Select(g => g.Week).ToList();
                    Assert.AreEqual(weeks.Count, weeks.Distinct().Count(), "one game per week for team " + t);
                    Assert.Less(weeks.Max() - weeks.Min() + 1 - weeks.Count, 100);
                    Assert.Greater(weeks.Max() - weeks.Min() + 1, weeks.Count, "team " + t + " has a bye inside its span");
                }
            }
        }

        [Test]
        public void EveryBowlTeamWonAtLeastFiveGames()
        {
            CollegeWorld w = LeagueTestUtil.LoadCollege(444);
            w.Season.SimToEnd();
            var bowls = w.Season.Games.Where(g => g.Kind == GameKind.Bowl).ToList();
            Assert.Greater(bowls.Count, 20);
            foreach (ScheduledGame g in bowls)
            {
                Assert.GreaterOrEqual(RegularWins(w.Season, g.HomeId), 5);
                Assert.GreaterOrEqual(RegularWins(w.Season, g.AwayId), 5);
            }
        }

        private static int RegularWins(Season s, int teamId)
        {
            return s.Games.Count(g => g.Kind == GameKind.Regular && g.Played && g.WinnerId == teamId);
        }

        // ------------------------------------------------------------------ QA-572 tie-breaks
        [Test]
        public void TiedTeamsAreSeparatedByStrengthOfVictoryBeforePointDifferential()
        {
            // A and B both finish 1-1 and never met. A's win came against a .800 team, B's against a .200 team, B has the better point differential.
            Func<int, int, int, int, int, StandingsRow> row = (id, w, l, pf, pa) => new StandingsRow { TeamId = id, W = w, L = l, PF = pf, PA = pa, DivisionId = -1, ConferenceId = 0 };
            StandingsRow A = row(1, 1, 1, 30, 40), B = row(2, 1, 1, 60, 20), X = row(3, 8, 2, 0, 0), Y = row(4, 2, 8, 0, 0), Z = row(5, 5, 5, 0, 0);
            var h2h = new Dictionary<long, float[]>();
            Action<int, int, float> add = (a, b, v) =>
            {
                long key = ((long)a << 32) | (uint)b;
                float[] arr;
                if (!h2h.TryGetValue(key, out arr)) { arr = new float[2]; h2h[key] = arr; }
                arr[0] += v; arr[1] += 1f;
            };
            add(1, 3, 1f); add(1, 5, 0f);      // A beat X, lost to Z
            add(2, 4, 1f); add(2, 5, 0f);      // B beat Y, lost to Z
            var all = new Dictionary<int, StandingsRow> { { 1, A }, { 2, B }, { 3, X }, { 4, Y }, { 5, Z } };
            List<StandingsRow> sorted = StandingsCalculator.Sort(new List<StandingsRow> { B, A }, h2h, false, all);
            Assert.AreEqual(1, sorted[0].TeamId, "A has the stronger victories");
            // without the strength table the old point-differential order applies
            Assert.AreEqual(2, StandingsCalculator.Sort(new List<StandingsRow> { A, B }, h2h, false)[0].TeamId);
        }

        // ------------------------------------------------------------------ QA-573 draft order
        [Test]
        public void DraftOrderPutsWildCardLosersBeforeByeTeamsThatLostTheirFirstGame()
        {
            ProLeague lg = LeagueFactory.CreatePro(321);
            lg.Season.SimToEnd();
            Season s = lg.Season;
            List<int> order = s.DraftOrder();
            var wildLosers = s.Games.Where(g => g.Kind != GameKind.Regular && g.Week == s.RegularWeeks + 1).Select(g => g.LoserId).ToList();
            var divLosers = s.Games.Where(g => g.Kind != GameKind.Regular && g.Week == s.RegularWeeks + 2).Select(g => g.LoserId).ToList();
            Assert.IsNotEmpty(wildLosers);
            Assert.IsNotEmpty(divLosers);
            int lastWild = wildLosers.Max(id => order.IndexOf(id)), firstDiv = divLosers.Min(id => order.IndexOf(id));
            Assert.Less(lastWild, firstDiv, "every wild-card loser picks before every divisional-round loser");
        }

        // ------------------------------------------------------------------ QA-576 / QA-577 rules
        [Test]
        public void RosterSlotsGiveEveryLineSpotABackupAndSumToFiftyThree()
        {
            Assert.AreEqual(2, LeagueRules.ProSlots(Position.RG));
            Assert.AreEqual(53, LeagueRules.RosterOrder.Sum(p => LeagueRules.ProSlots(p)));
            Assert.AreEqual(53, PositionBuckets.All.Sum(b => PositionBuckets.ProCount(b)));
            foreach (PositionBucket b in PositionBuckets.All)
                Assert.AreEqual(PositionBuckets.ProCount(b), LeagueRules.RosterOrder.Where(p => PositionBuckets.Of(p) == b).Sum(p => LeagueRules.ProSlots(p)), b.ToString());
            ProLeague lg = LeagueTestUtil.FreshPro(2);
            Assert.IsTrue(lg.Teams.All(t => t.CountAt(Position.RG) == 2 && t.Roster.Count == 53));
        }

        [Test]
        public void StartersCoverTheBasePersonnelTheOffenseAndDefenseUse()
        {
            Assert.AreEqual(4, LeagueRules.Starters(Position.WR));
            Assert.AreEqual(2, LeagueRules.Starters(Position.TE));
            Assert.AreEqual(4, LeagueRules.Starters(Position.CB));
            ProLeague lg = LeagueTestUtil.FreshPro(2);
            foreach (Team t in lg.Teams)
                foreach (Position p in LeagueRules.RosterOrder)
                    Assert.GreaterOrEqual(t.Depth.Get(p).Count, LeagueRules.Starters(p), t.FullName + " " + p);
        }

        // ------------------------------------------------------------------ QA-578 depth chart
        [Test]
        public void ABackupFillsOnlyOneDepthChartHole()
        {
            ProLeague lg = LeagueTestUtil.FreshPro(2);
            Team t = lg.Teams[7];
            // leave one wideout and no fullback so both positions have to borrow bodies
            foreach (Player p in t.Roster.Where(p => p.Position == Position.FB).ToList()) t.RemovePlayer(p);
            foreach (Player p in t.Roster.Where(p => p.Position == Position.WR).Skip(1).ToList()) t.RemovePlayer(p);
            DepthChartBuilder.Build(t);
            var borrowed = new List<int>();
            foreach (DepthSlot slot in t.Depth.Slots)
                foreach (int id in slot.PlayerIds)
                {
                    Player p = t.GetPlayer(id);
                    if (p != null && p.Position != slot.Pos) borrowed.Add(id);
                }
            Assert.Greater(borrowed.Count, 0);
            Assert.AreEqual(borrowed.Count, borrowed.Distinct().Count(), "no body is borrowed for two positions");
        }

        // ------------------------------------------------------------------ QA-579 / QA-580 / QA-581 generation
        [Test]
        public void VeteranDraftRoundsAreSpreadAcrossAllSevenRounds()
        {
            var rng = new Rng(5);
            int[] avg = new int[8], star = new int[8];
            for (int i = 0; i < 4000; i++) { avg[LeagueFactory.DraftRoundFor(70, rng)]++; star[LeagueFactory.DraftRoundFor(88, rng)]++; }
            for (int r = 1; r <= 7; r++) Assert.Greater(avg[r], 200, "round " + r);
            double firstRound = avg[1] / 4000.0;
            Assert.That(firstRound, Is.InRange(0.10, 0.25), "first-round share of an average starter " + firstRound);
            Assert.Greater(star[1], avg[1], "better players go earlier");
        }

        [Test]
        public void StandaloneDraftClassReachesTheNinetiesAndCoversEveryPosition()
        {
            ProLeague lg = LeagueTestUtil.FreshPro(9);
            var tops = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                List<Prospect> cls = ProspectGenerator.Standalone(lg, 2026, new Rng(100 + i), null);
                Assert.AreEqual(ProspectGenerator.DefaultClassSize, cls.Count);
                tops.Add(cls.Max(p => p.Player.Potential));
                Assert.GreaterOrEqual(cls.Count(p => p.Player.Position == Position.QB), 8, "QBs");
            }
            Assert.GreaterOrEqual(tops.Max(), 94, "the best prospect of some class reaches the mid-90s");
            Assert.GreaterOrEqual(tops.Average(), 90.0, "the best prospect of a class is a future star");
        }

        [Test]
        public void CollegeDraftClassReservesSlotsForEveryPositionGroup()
        {
            CollegeWorld w = LeagueTestUtil.LoadCollege(31);
            List<Prospect> cls = ProspectGenerator.FromCollege(w, 2026, new Rng(3));
            Assert.That(cls.Count, Is.InRange(ProspectGenerator.DefaultClassSize, ProspectGenerator.DefaultClassSize + 12), "340 plus the specialists added to reach their minimum");
            Assert.GreaterOrEqual(cls.Count(p => p.Player.Position == Position.QB), 12, "quarterbacks");
            Assert.GreaterOrEqual(cls.Count(p => p.Player.Position == Position.P), 3, "punters");
            Assert.GreaterOrEqual(cls.Count(p => p.Player.Position == Position.K), 3, "kickers");
            Assert.LessOrEqual(cls.Count(p => p.Player.Position == Position.CB), 45, "corners no longer flood the class");
        }

        // ------------------------------------------------------------------ QA-591 / QA-600 penalties
        [Test]
        public void PenaltiesLandOnTheRightPositionGroups()
        {
            var byPos = new Dictionary<Position, int>();
            foreach (GameResult r in Games)
                foreach (PlayerGameLine l in r.PlayerLines)
                {
                    int n;
                    byPos.TryGetValue(l.Pos, out n);
                    byPos[l.Pos] = n + l.Stats.Penalties;
                }
            Func<Position, int> of = p => byPos.ContainsKey(p) ? byPos[p] : 0;
            Assert.Greater(of(Position.WR) + of(Position.TE), 0, "receivers are flagged (offensive pass interference)");
            Assert.Greater(of(Position.CB) + of(Position.FS) + of(Position.SS), 20, "defensive backs are flagged (coverage fouls)");
            Assert.Greater(of(Position.QB), 0, "delay of game goes to the quarterback");
            int ol = of(Position.LT) + of(Position.LG) + of(Position.C) + of(Position.RG) + of(Position.RT);
            Assert.Greater(ol, 50);
            int dl = of(Position.LE) + of(Position.DT) + of(Position.RE);
            int backs = of(Position.CB) + of(Position.FS) + of(Position.SS);
            Assert.Greater(backs, dl / 3, "the defensive line no longer absorbs every defensive flag");
        }

        [Test]
        public void TeamPenaltyYardsEqualThePlayerPenaltyYards()
        {
            foreach (GameResult r in Games.Take(60))
                foreach (TeamBoxScore b in new[] { r.Home, r.Away })
                {
                    var lines = r.PlayerLines.Where(l => l.TeamId == b.TeamId).ToList();
                    Assert.AreEqual(b.Penalties, lines.Sum(l => l.Stats.Penalties));
                    Assert.AreEqual(b.PenaltyYds, lines.Sum(l => l.Stats.PenaltyYds));
                    Assert.GreaterOrEqual(b.PenaltyYds, 0);
                }
        }

        [Test]
        public void PenaltyYardsAreTheYardsActuallyEnforced()
        {
            // a defensive holding flag with the ball on the 4 is enforced half the distance to the goal, not the catalog 5 yards the old box score charged
            var s = new GameState(RuleSet.Pro(), Core.Team.Home);
            s.Phase = GamePhase.Scrimmage; s.Possession = Core.Team.Home;
            s.BallSpot = 96; s.Down = 1; s.Distance = 4;
            PenaltyCall call = PenaltyCall.For(PenaltyType.DefensiveHolding, s, 0f);
            int enforced = Math.Abs(PenaltyEnforcer.Enforce(s.Clone(), call).Yards);
            Assert.Less(enforced, call.Def.YardsFor(RuleSet.Pro(), 0f), "half the distance to the goal line");
            Assert.Greater(enforced, 0);
        }

        // ------------------------------------------------------------------ QA-592 / QA-593 / QA-598 punting
        [Test]
        public void TouchbackPuntsRecordTheirYards()
        {
            int punts = 0;
            foreach (GameResult r in Games)
            {
                Assert.IsFalse(r.PlayLog.Any(l => l.Contains("punt 0 yds")), "a punt logged zero yards");
                punts += r.Home.Punts + r.Away.Punts;
            }
            Assert.Greater(punts, 500);
        }

        [Test]
        public void GrossPuntAverageMatchesThePunterNotTheLineOfScrimmage()
        {
            long yds = 0, n = 0;
            foreach (GameResult r in Games) { yds += r.Home.PuntYds + r.Away.PuntYds; n += r.Home.Punts + r.Away.Punts; }
            double avg = (double)yds / n;
            Assert.That(avg, Is.InRange(42.0, 48.5), "gross punt average " + avg);
        }

        [Test]
        public void PuntReturnAttemptsExcludeKicksOutOfBounds()
        {
            foreach (GameResult r in Games)
            {
                int punts = r.Home.Punts + r.Away.Punts;
                int attempts = r.PlayerLines.Sum(l => l.Stats.PrAtt);
                Assert.LessOrEqual(attempts, punts, "more returns than punts");
            }
            // a punt out of bounds can never be a return: over the whole sample returns are clearly fewer than punts
            int totalPunts = Games.Sum(g => g.Home.Punts + g.Away.Punts), totalReturns = Games.Sum(g => g.PlayerLines.Sum(l => l.Stats.PrAtt));
            Assert.Less(totalReturns, totalPunts * 0.65);
        }

        // ------------------------------------------------------------------ QA-594 / QA-595 / QA-596 situational
        [Test]
        public void CollegeOvertimeGamesStillFinishWithPenaltiesOn()
        {
            CollegeWorld w = LeagueTestUtil.LoadCollege(12);
            var rng = new Rng(8);
            int ot = 0;
            for (int g = 0; g < 400; g++)
            {
                Team h = w.Schools[g % 100].Team, a = w.Schools[(g * 3 + 1) % 100 == g % 100 ? (g + 2) % 100 : (g * 3 + 1) % 100].Team;
                GameResult r = QuickSim.SimGame(h, a, RuleSet.College(), rng);
                Assert.AreNotEqual(r.HomeScore, r.AwayScore, "college games never tie");
                if (r.Overtime) ot++;
            }
            Assert.Greater(ot, 0);
        }

        [Test]
        public void CollegeExtraPointsAreKickedMoreOftenThanProOnes()
        {
            ProLeague lg = LeagueTestUtil.FreshPro(7);
            CollegeWorld w = LeagueTestUtil.LoadCollege(12);
            double pro = XpRate(lg.Teams.ToList(), RuleSet.Pro(), 400), col = XpRate(w.TeamList().Take(60).ToList(), RuleSet.College(), 400);
            Assert.Greater(col, pro, "college " + col + " vs pro " + pro);
        }

        private static double XpRate(List<Team> teams, RuleSet rules, int games)
        {
            var rng = new Rng(77);
            int made = 0, att = 0;
            for (int g = 0; g < games; g++)
            {
                Team h = teams[g % teams.Count], a = teams[(g * 3 + 1) % teams.Count == g % teams.Count ? (g + 2) % teams.Count : (g * 3 + 1) % teams.Count];
                GameResult r = QuickSim.SimGame(h, a, rules, rng);
                made += r.Home.XpMade + r.Away.XpMade; att += r.Home.XpAtt + r.Away.XpAtt;
            }
            return (double)made / att;
        }

        // ------------------------------------------------------------------ QA-601 / QA-605 / QA-611 box score
        [Test]
        public void TacklesAreSharedAndCornersTackle()
        {
            int total = 0, cb = 0;
            foreach (GameResult r in Games)
                foreach (PlayerGameLine l in r.PlayerLines) { total += l.Stats.Tackles; if (l.Pos == Position.CB) cb += l.Stats.Tackles; }
            double perTeam = total / (2.0 * Games.Count);
            Assert.That(perTeam, Is.InRange(55.0, 80.0), "tackles per team-game " + perTeam);
            Assert.Greater(cb / (2.0 * Games.Count), 8.0, "corner tackles per team-game");
        }

        [Test]
        public void TimeOfPossessionAddsUpToNearlyTheWholeGame()
        {
            double avg = Games.Where(g => !g.Overtime).Average(g => g.Home.TimeOfPossessionSeconds + g.Away.TimeOfPossessionSeconds);
            Assert.GreaterOrEqual(avg, 3525.0, "average total possession seconds " + avg);      // v0.11.0 SIM-FLOW (B-072): the touchback kickoff costs no clock, so a game leaves a little more than before unplayed
        }

        [Test]
        public void TwoPointConversionsAndSafetiesCountAsPlayerPoints()
        {
            var line = new StatLine { TwoPtMade = 1, Safeties = 1, XpMade = 1 };
            Assert.AreEqual(5, line.Points);
            var sum = new StatLine();
            sum.Add(line); sum.Add(line);
            Assert.AreEqual(10, sum.Points);
            int twoPts = 0, boxTwo = 0;
            foreach (GameResult r in Games)
            {
                twoPts += r.PlayerLines.Sum(l => l.Stats.TwoPtMade);
                boxTwo += r.Home.TwoPointMade + r.Away.TwoPointMade;
            }
            Assert.AreEqual(boxTwo, twoPts, "player two-point credit matches the team box score");
        }

        // ------------------------------------------------------------------ QA-614 / QA-615 / QA-610 data
        [Test]
        public void SchoolRegionComesFromTheStateCode()
        {
            Assert.AreEqual(Region.West, CollegeFactory.RegionOfState("CA", Region.Midwest));
            Assert.AreEqual(Region.Northeast, CollegeFactory.RegionOfState("ny", Region.Midwest));
            Assert.AreEqual(Region.Southwest, CollegeFactory.RegionOfState("TX", Region.Midwest));
            Assert.AreEqual(Region.Pacific, CollegeFactory.RegionOfState("OR", Region.Midwest));
            Assert.AreEqual(Region.Midwest, CollegeFactory.RegionOfState("", Region.Midwest));
            Assert.AreEqual(Region.Plains, CollegeFactory.RegionOfState("zz", Region.Plains));
            CollegeWorld w = LeagueTestUtil.LoadCollege(4);
            Assert.AreEqual(Region.West, w.Schools.First(s => s.State == "CA").Region);
        }

        [Test]
        public void CoachScoutingFollowsTheTeamQualityOfTheHire()
        {
            var rng = new Rng(3);
            double high = 0, low = 0;
            for (int i = 0; i < 400; i++)
            {
                high += LeagueFactory.CreateCoach(rng, new HashSet<string>(), CoachRole.HeadCoach, 1, 8f).Scouting;
                low += LeagueFactory.CreateCoach(rng, new HashSet<string>(), CoachRole.HeadCoach, 1, -8f).Scouting;
            }
            Assert.Greater(high / 400.0, low / 400.0 + 4.0);
        }

        [Test]
        public void FreeAgentPoolStaysDeepAndRetirementsAreRealPlayers()
        {
            ProLeague lg = ThreeOffseasons;
            Assert.Greater(lg.FreeAgents.Count, 150, "free agents");
            Assert.IsTrue(lg.RetiredPlayers.All(p => p.DraftRound > 0 || p.Experience > 3 || p.Career.Count > 0), "retired list holds only players who played");
        }


        [Test]
        public void HappyPlayersAreReSignedMoreOftenThanUnhappyOnes()
        {
            Func<int, int> reSigned = morale =>
            {
                ProLeague lg = LeagueFactory.CreatePro(21);
                foreach (Player p in lg.AllRosterPlayers()) p.Morale = morale;
                return Offseason.Run(lg, null, new Rng(5)).ReSigned;
            };
            int happy = reSigned(100), unhappy = reSigned(20);
            Assert.Greater(happy, unhappy, "re-signed with morale 100 vs 20");
        }

        [Test]
        public void KickReturnersAreChosenBySkillNotAlwaysTheFourthReceiver()
        {
            var returners = new Dictionary<Position, int>();
            foreach (GameResult r in Games)
                foreach (PlayerGameLine l in r.PlayerLines)
                    if (l.Stats.KrAtt > 0) { int n; returners.TryGetValue(l.Pos, out n); returners[l.Pos] = n + l.Stats.KrAtt; }
            Assert.IsTrue(returners.ContainsKey(Position.WR) && returners.ContainsKey(Position.RB), "returns by " + string.Join(",", returners.Keys));
        }

        [Test]
        public void SchemesMapToPlayStylesByName()
        {
            // QA-622: the Sim maps scheme -> style with an explicit switch; both enums must keep the same member names
            foreach (OffenseScheme s in Enum.GetValues(typeof(OffenseScheme)))
                Assert.IsTrue(Enum.IsDefined(typeof(PlayStyle), s.ToString()), s + " has a PlayStyle of the same name");
        }
    }
}
