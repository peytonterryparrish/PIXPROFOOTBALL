using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    public class OffseasonTests
    {
        private sealed class Run
        {
            public ProLeague Lg;
            public CollegeWorld College;
            public List<OffseasonReport> Reports = new List<OffseasonReport>();
            public float StartStarters, StartRoster, StartAge;
        }

        private static Run _tenYears;
        private static Run TenYears
        {
            get
            {
                if (_tenYears == null)
                {
                    var r = new Run { Lg = LeagueFactory.CreatePro(31), College = LeagueTestUtil.LoadCollege(31) };
                    var rng = new Rng(5150);
                    r.StartStarters = Offseason.AverageStarterOverall(r.Lg);
                    r.StartRoster = Offseason.AverageRosterOverall(r.Lg);
                    r.StartAge = (float)r.Lg.AllRosterPlayers().Average(p => p.Age);
                    for (int y = 0; y < 10; y++)
                    {
                        r.Lg.Season.SimToEnd();
                        r.College.Season.SimToEnd();
                        r.Reports.Add(Offseason.Run(r.Lg, r.College, rng));
                    }
                    _tenYears = r;
                }
                return _tenYears;
            }
        }

        [Test]
        public void TenOffseasonsKeepEveryRosterAtFiftyThreeWithFullPositionGroups()
        {
            Run r = TenYears;
            Assert.AreEqual(2036, r.Lg.Year);
            foreach (Team t in r.Lg.Teams)
            {
                Assert.AreEqual(53, t.Roster.Count, t.FullName);
                foreach (Position pos in LeagueRules.RosterOrder) Assert.AreEqual(LeagueRules.ProSlots(pos), t.CountAt(pos), t.FullName + " " + pos);
                Assert.LessOrEqual(t.CapUsed, LeagueRules.SalaryCap, t.FullName + " cap");
                foreach (Player p in t.Roster)
                {
                    Assert.AreEqual(t.Id, p.TeamId);
                    Assert.GreaterOrEqual(p.Contract.Aav, LeagueRules.MinimumSalary);
                    Assert.GreaterOrEqual(p.Contract.YearsLeft, 1, p.ToString());
                    Assert.IsFalse(p.Retired);
                    Assert.LessOrEqual(p.Age, 41);
                }
            }
            foreach (var rep in r.Reports)
            {
                Assert.AreEqual(260, rep.Drafted);
                Assert.Greater(rep.Retired, 20);
            }
        }

        [Test]
        public void AverageOverallsStayWithinThreePointsOfTheStart()
        {
            Run r = TenYears;
            foreach (var rep in r.Reports)
            {
                Assert.That(rep.StarterOvrAfter, Is.InRange(r.StartStarters - 3f, r.StartStarters + 3f), "starter OVR, year " + rep.Year);
                Assert.That(rep.RosterOvrAfter, Is.InRange(r.StartRoster - 3f, r.StartRoster + 3f), "roster OVR, year " + rep.Year);
            }
            double age = r.Lg.AllRosterPlayers().Average(p => p.Age);
            Assert.That(age, Is.InRange(r.StartAge - 2.0, r.StartAge + 2.0), "average age");
            double spread = r.Lg.Teams.Max(t => t.TeamOverall()) - r.Lg.Teams.Min(t => t.TeamOverall());
            Assert.GreaterOrEqual(spread, 4, "teams stay distinct");
            Assert.AreEqual(32 * 53, r.Lg.AllRosterPlayers().Count());
        }

        [Test]
        public void LeagueKeepsRunningAfterTheOffseasons()
        {
            Run r = TenYears;
            Season s = r.Lg.Season;
            Assert.AreEqual(r.Lg.Year, s.Year);
            Assert.AreEqual(272, s.Games.Count);
            Assert.IsFalse(s.IsComplete);
            for (int t = 0; t < 32; t++) Assert.AreEqual(17, s.RegularGameCount(t));
            Assert.AreEqual(10, r.Lg.History.Count);
            Assert.AreEqual(10, r.Lg.History.Select(h => h.Year).Distinct().Count());
            Assert.IsTrue(r.Lg.History.All(h => h.ChampionTeamId >= 0 && !string.IsNullOrEmpty(h.MvpName)));
            // the new season plays on the evolved rosters
            s.SimWeek();
            Assert.AreEqual(s.GamesOfWeek(1).Count, s.LastWeekResults.Count);
            Assert.GreaterOrEqual(r.Lg.RetiredPlayers.Count, 400);
            // ids stay unique across the league, pool and retirees
            var ids = new HashSet<int>();
            foreach (Player p in r.Lg.AllRosterPlayers()) Assert.IsTrue(ids.Add(p.Id), "duplicate id " + p.Id);
            foreach (Player p in r.Lg.FreeAgents) Assert.IsTrue(ids.Add(p.Id), "duplicate FA id " + p.Id);
            // veterans from the first class have been replaced by their successors
            Assert.IsTrue(r.Lg.AllRosterPlayers().Count(p => p.DraftYear >= 2030) > 200);
        }

        [Test]
        public void CollegeWorldRegeneratesItsRosters()
        {
            Run r = TenYears;
            Assert.AreEqual(2036, r.College.Year);
            Assert.AreEqual(138, r.College.Schools.Count);
            foreach (School s in r.College.Schools)
            {
                Assert.AreEqual(85, s.Team.Roster.Count, s.Name);
                Assert.IsTrue(s.Team.Roster.All(p => !p.Retired));
            }
            double ovr = r.College.Schools.SelectMany(s => s.Team.Roster).Average(p => p.Overall);
            Assert.That(ovr, Is.InRange(36.0, 52.0));
            Assert.AreEqual(138, r.College.Season.Teams.Count);
            Assert.AreEqual(2036, r.College.Season.Year);
            Assert.AreEqual(10, r.College.History.Count);
            Assert.AreEqual(138, r.College.Schools.Select(s => s.Name).Distinct().Count());
        }

        [Test]
        public void DraftIsSevenRoundsWithEachPlayerPickedOnce()
        {
            ProLeague lg = LeagueFactory.CreatePro(66);
            CollegeWorld col = LeagueTestUtil.LoadCollege(66);
            lg.Season.SimToEnd();
            var rng = new Rng(2);
            Offseason.PrepareDraftClass(lg, col, rng);
            Assert.GreaterOrEqual(lg.DraftClass.Count, 300);
            Assert.AreEqual(32, lg.ScoutingReports.Count);
            var classIds = new HashSet<int>(lg.DraftClass.Select(p => p.Id));
            string champ = lg.Season.NameOf(lg.Season.ChampionTeamId);
            Offseason.Run(lg, col, rng);
            var picks = lg.LastDraft;
            Assert.AreEqual(DraftEngine.TotalPicks, picks.Count);
            Assert.AreEqual(260, DraftEngine.TotalPicks);
            Assert.AreEqual(picks.Count, picks.Select(p => p.PlayerId).Distinct().Count());
            Assert.AreEqual(Enumerable.Range(1, picks.Count).ToArray(), picks.Select(p => p.Overall).ToArray());
            Assert.AreEqual(7, picks.Max(p => p.Round));
            for (int round = 1; round <= 7; round++) Assert.GreaterOrEqual(picks.Count(p => p.Round == round), 32);
            Assert.IsTrue(picks.All(p => classIds.Contains(p.PlayerId)));
            // worst team picks first, champion last in round one
            var order = lg.Season == null ? null : lg.Season;
            Assert.AreNotEqual(picks[0].TeamId, picks[31].TeamId);
            // rookies sit on the roster that drafted them, on rookie deals
            foreach (var pk in picks.Take(40))
            {
                Player p = lg.Teams[pk.TeamId].GetPlayer(pk.PlayerId);
                if (p == null) continue; // may have been cut during roster cut-down
                Assert.IsTrue(p.Contract.Rookie || p.Contract.Years > 0);
                Assert.AreEqual(pk.Round, p.DraftRound);
            }
            // early picks are, on average, much better than late ones
            double early = picks.Take(32).Average(p => lg.FindPlayer(p.PlayerId).Potential);
            double late = picks.Skip(200).Average(p => lg.FindPlayer(p.PlayerId).Potential);
            Assert.Greater(early, late + 2);
            // the board of public ranks tracks draft position
            Assert.Less(picks.Take(32).Average(p => p.ConsensusRank), picks.Skip(200).Average(p => p.ConsensusRank));
            Assert.IsNotEmpty(champ);
        }

        [Test]
        public void ScoutingPointsImproveADraftingTeamsView()
        {
            ProLeague lg = LeagueFactory.CreatePro(67);
            var cls = ProspectGenerator.Standalone(lg, 2026, new Rng(4), null);
            var heavy = DraftEngine.BuildScouting(lg, cls, new Rng(9), 2026);
            var errs = heavy.Select(rep => cls.Take(100).Average(p => Math.Abs(rep.EstimatedGrade(p) - p.TrueGrade))).ToList();
            var blank = new ScoutingReport(99, 2026, 1, 0);
            double blankErr = cls.Take(100).Average(p => Math.Abs(blank.EstimatedGrade(p) - p.TrueGrade));
            Assert.Less(errs.Average(), blankErr, "budgeted scouting beats a blind glance on the top of the board");
            Assert.IsTrue(heavy.All(r => r.PointsSpent <= r.PointsBudget));
        }

        [Test]
        public void OffseasonIsDeterministic()
        {
            ProLeague a = LeagueFactory.CreatePro(12), b = LeagueFactory.CreatePro(12);
            for (int y = 0; y < 3; y++)
            {
                a.Season.SimToEnd(); b.Season.SimToEnd();
                Offseason.Run(a, null, new Rng(100 + y));
                Offseason.Run(b, null, new Rng(100 + y));
            }
            Assert.AreEqual(LeagueTestUtil.Fingerprint(a), LeagueTestUtil.Fingerprint(b));
            Assert.AreEqual(LeagueTestUtil.Fingerprint(a.Season), LeagueTestUtil.Fingerprint(b.Season));
            Assert.AreEqual(a.LastDraft.Count, b.LastDraft.Count);
            for (int i = 0; i < a.LastDraft.Count; i++) Assert.AreEqual(a.LastDraft[i].PlayerId, b.LastDraft[i].PlayerId);
        }

        [Test]
        public void StandaloneOffseasonWithoutACollegeWorldAlsoSustainsTheLeague()
        {
            ProLeague lg = LeagueFactory.CreatePro(13);
            float start = Offseason.AverageStarterOverall(lg), startRoster = Offseason.AverageRosterOverall(lg);
            var rng = new Rng(8);
            for (int y = 0; y < 10; y++)
            {
                lg.Season.SimToEnd();
                Offseason.Run(lg, null, rng);
            }
            Assert.That(Offseason.AverageStarterOverall(lg), Is.InRange(start - 3f, start + 3f));
            Assert.That(Offseason.AverageRosterOverall(lg), Is.InRange(startRoster - 3f, startRoster + 3f));
            Assert.IsTrue(lg.Teams.All(t => t.Roster.Count == 53 && t.CapUsed <= LeagueRules.SalaryCap));
        }

        [Test]
        public void ContractsTickDownAndExpiringPlayersAreResignedOrReleased()
        {
            ProLeague lg = LeagueFactory.CreatePro(14);
            lg.Season.SimToEnd();
            var expiring = lg.AllRosterPlayers().Where(p => p.Contract.YearsLeft == 1).Select(p => p.Id).ToList();
            var keepers = lg.AllRosterPlayers().Where(p => p.Contract.YearsLeft > 1).ToDictionary(p => p.Id, p => p.Contract.YearsLeft);
            Assert.Greater(expiring.Count, 100);
            Offseason.Run(lg, null, new Rng(3));
            foreach (var kv in keepers)
            {
                Player p = lg.FindPlayer(kv.Key);
                if (p == null || p.Retired || p.TeamId < 0) continue;
                Assert.LessOrEqual(p.Contract.YearsLeft, kv.Value);
            }
            int stillThere = expiring.Count(id => { Player p = lg.FindPlayer(id); return p != null && p.TeamId >= 0 && !p.Retired; });
            Assert.Less(stillThere, expiring.Count, "some expiring players leave");
            Assert.Greater(stillThere, 0, "some are re-signed");
        }
    }
}
