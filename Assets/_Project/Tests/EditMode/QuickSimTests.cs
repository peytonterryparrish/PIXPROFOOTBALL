using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    public class QuickSimTests
    {
        private static ProLeague _lg;
        private static ProLeague Lg { get { return _lg ?? (_lg = LeagueFactory.CreatePro(7)); } }

        private sealed class Sample
        {
            public int Games;
            public double Pts, PassYds, RushYds, Sacks, Ints, Plays, PassAtt, Cmp, RushAtt, Fum, Pen, Fg, FgAtt, Punts, Tds;
            public int HomeWins, Ties, Ot;
        }

        private static Sample _sample;
        private static Sample Run1000()
        {
            if (_sample != null) return _sample;
            var s = new Sample();
            var rng = new Rng(99);
            RuleSet rules = RuleSet.Pro();
            while (s.Games < 1000)
            {
                Team h = Lg.Teams[rng.Range(0, 32)], a = Lg.Teams[rng.Range(0, 32)];
                if (h == a) continue;
                GameResult r = QuickSim.SimGame(h, a, rules, rng);
                s.Games++;
                foreach (TeamBoxScore b in new[] { r.Home, r.Away })
                {
                    s.Pts += b.Score; s.PassYds += b.PassYds; s.RushYds += b.RushYds; s.Sacks += b.SacksMade; s.Ints += b.InterceptionsMade;
                    s.Plays += b.Plays; s.PassAtt += b.PassAtt; s.Cmp += b.PassCmp; s.RushAtt += b.RushAtt; s.Fum += b.FumblesLost;
                    s.Pen += b.Penalties; s.Fg += b.FgMade; s.FgAtt += b.FgAtt; s.Punts += b.Punts; s.Tds += b.PassTd + b.RushTd;
                }
                if (r.HomeScore > r.AwayScore) s.HomeWins++;
                if (r.Tie) s.Ties++;
                if (r.Overtime) s.Ot++;
            }
            _sample = s;
            return s;
        }

        // -------------------------------------------------------------- statistical bands over 1000 games
        [Test] public void AveragePointsPerTeam() { var s = Run1000(); double v = s.Pts / (2.0 * s.Games); Assert.That(v, Is.InRange(21.0, 24.0), "points/team " + v); }
        [Test] public void PassYardsPerTeam() { var s = Run1000(); double v = s.PassYds / (2.0 * s.Games); Assert.That(v, Is.InRange(210.0, 240.0), "pass yds/team " + v); }
        [Test] public void RushYardsPerTeam() { var s = Run1000(); double v = s.RushYds / (2.0 * s.Games); Assert.That(v, Is.InRange(105.0, 125.0), "rush yds/team " + v); }
        [Test] public void SacksPerTeam() { var s = Run1000(); double v = s.Sacks / (2.0 * s.Games); Assert.That(v, Is.InRange(2.0, 2.7), "sacks/team " + v); }
        [Test] public void InterceptionsPerTeam() { var s = Run1000(); double v = s.Ints / (2.0 * s.Games); Assert.That(v, Is.InRange(0.65, 1.0), "INT/team " + v); }
        [Test] public void PlaysPerTeam() { var s = Run1000(); double v = s.Plays / (2.0 * s.Games); Assert.That(v, Is.InRange(60.0, 66.0), "plays/team " + v); }
        // 1000 games carry about +-1.6 points of sampling noise around the true ~56 percent
        [Test] public void HomeWinPercentage() { var s = Run1000(); double v = 100.0 * s.HomeWins / s.Games; Assert.That(v, Is.InRange(50.0, 60.0), "home win% " + v); }

        [Test]
        public void SecondaryRatesLookLikeFootball()
        {
            var s = Run1000();
            double n = 2.0 * s.Games;
            Assert.That(s.Cmp / s.PassAtt, Is.InRange(0.58, 0.68), "completion %");
            Assert.That(s.RushYds / s.RushAtt, Is.InRange(3.9, 4.9), "yards per carry");
            Assert.That(s.Fum / n, Is.InRange(0.15, 0.7), "fumbles lost/team");
            Assert.That(s.Pen / n, Is.InRange(3.5, 8.0), "penalties/team");
            Assert.That(s.Fg / s.FgAtt, Is.InRange(0.72, 0.92), "FG %");
            Assert.That(s.Punts / n, Is.InRange(3.0, 6.0), "punts/team");
            Assert.That(s.Ties, Is.LessThan(s.Games * 0.03), "ties");
            Assert.That(s.Ot, Is.GreaterThan(0));
        }

        [Test]
        public void TenPointOverallEdgeWinsAboutEightyPercent()
        {
            var rng = new Rng(5);
            int wins = 0, n = 1200;
            RuleSet rules = RuleSet.Pro();
            for (int i = 0; i < n; i++)
            {
                Team baseTeam = Lg.Teams[rng.Range(0, 32)];
                Team strong = LeagueTestUtil.CloneWithBoost(baseTeam, 10, 500);
                bool strongHome = i % 2 == 0;
                GameResult r = strongHome ? QuickSim.SimGame(strong, baseTeam, rules, rng) : QuickSim.SimGame(baseTeam, strong, rules, rng);
                int ss = strongHome ? r.HomeScore : r.AwayScore, bs = strongHome ? r.AwayScore : r.HomeScore;
                if (ss > bs) wins++;
            }
            double pct = 100.0 * wins / n;
            Assert.That(pct, Is.InRange(73.0, 88.0), "+10 OVR win% " + pct);
        }

        [Test]
        public void StrongerTeamsWinMoreOverARealSeasonSample()
        {
            var rng = new Rng(21);
            var ordered = Lg.Teams.OrderBy(t => t.TeamOverall()).ToList();
            var top = ordered.Skip(24).ToList(); var bottom = ordered.Take(8).ToList();
            int topWins = 0, n = 400;
            for (int i = 0; i < n; i++)
            {
                Team a = top[rng.Range(0, top.Count)], b = bottom[rng.Range(0, bottom.Count)];
                GameResult r = i % 2 == 0 ? QuickSim.SimGame(a, b, RuleSet.Pro(), rng) : QuickSim.SimGame(b, a, RuleSet.Pro(), rng);
                if (r.WinnerTeamId == a.Id) topWins++;
            }
            Assert.Greater(topWins / (double)n, 0.6);
        }

        [Test]
        public void SimulatesAtLeastFiveHundredGamesPerSecond()
        {
            var rng = new Rng(3);
            // warm up the JIT
            for (int i = 0; i < 30; i++) QuickSim.SimGame(Lg.Teams[i % 32], Lg.Teams[(i + 5) % 32], RuleSet.Pro(), rng);
            int games = 400;
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < games; i++) QuickSim.SimGame(Lg.Teams[i % 32], Lg.Teams[(i * 7 + 3) % 32 == i % 32 ? (i + 1) % 32 : (i * 7 + 3) % 32], RuleSet.Pro(), rng);
            sw.Stop();
            double perSec = games / sw.Elapsed.TotalSeconds;
            Assert.GreaterOrEqual(perSec, 500.0, "games per second: " + perSec);
        }

        // -------------------------------------------------------------- determinism and box score integrity
        [Test]
        public void SameSeedGivesIdenticalGames()
        {
            for (int k = 0; k < 10; k++)
            {
                GameResult a = QuickSim.SimGame(Lg.Teams[k], Lg.Teams[k + 10], RuleSet.Pro(), new Rng(1000 + k));
                GameResult b = QuickSim.SimGame(Lg.Teams[k], Lg.Teams[k + 10], RuleSet.Pro(), new Rng(1000 + k));
                Assert.AreEqual(LeagueTestUtil.Fingerprint(a), LeagueTestUtil.Fingerprint(b));
                Assert.AreEqual(a.Drives.Count, b.Drives.Count);
            }
            GameResult c = QuickSim.SimGame(Lg.Teams[0], Lg.Teams[10], RuleSet.Pro(), new Rng(5));
            GameResult d = QuickSim.SimGame(Lg.Teams[0], Lg.Teams[10], RuleSet.Pro(), new Rng(6));
            Assert.AreNotEqual(LeagueTestUtil.Fingerprint(c), LeagueTestUtil.Fingerprint(d));
        }

        [Test]
        public void BoxScoreTotalsAreInternallyConsistent()
        {
            var rng = new Rng(77);
            int puntDrives = 0;
            for (int g = 0; g < 60; g++)
            {
                Team h = Lg.Teams[g % 32], a = Lg.Teams[(g * 5 + 1) % 32 == g % 32 ? (g + 3) % 32 : (g * 5 + 1) % 32];
                GameResult r = QuickSim.SimGame(h, a, RuleSet.Pro(), rng);
                foreach (TeamBoxScore b in new[] { r.Home, r.Away })
                {
                    var lines = r.PlayerLines.Where(l => l.TeamId == b.TeamId).Select(l => l.Stats).ToList();
                    Assert.AreEqual(b.Score, b.ByQuarter.Sum() + b.OvertimeScore, "quarter scoring");
                    Assert.AreEqual(b.PassYds, lines.Sum(s => s.PassYds), "pass yards vs passers");
                    Assert.AreEqual(b.PassYds, lines.Sum(s => s.RecYds), "pass yards vs receivers");
                    Assert.AreEqual(b.PassCmp, lines.Sum(s => s.Rec));
                    Assert.AreEqual(b.PassAtt, lines.Sum(s => s.PassAtt));
                    Assert.AreEqual(b.RushYds, lines.Sum(s => s.RushYds), "rush yards");
                    Assert.AreEqual(b.PassTd, lines.Sum(s => s.PassTd));
                    Assert.AreEqual(b.PassTd, lines.Sum(s => s.RecTd));
                    Assert.AreEqual(b.RushTd, lines.Sum(s => s.RushTd));
                    Assert.AreEqual(b.FgMade, lines.Sum(s => s.FgMade));
                    Assert.AreEqual(b.Sacked, lines.Sum(s => s.Sacked));
                    TeamBoxScore opp = b == r.Home ? r.Away : r.Home;
                    var oppLines = r.PlayerLines.Where(l => l.TeamId == opp.TeamId).Select(l => l.Stats).ToList();
                    Assert.AreEqual(b.Sacked, oppLines.Sum(s => s.Sacks), "sacks allowed vs recorded");
                    Assert.AreEqual(b.PassInt, oppLines.Sum(s => s.Interceptions), "interceptions");
                    Assert.LessOrEqual(b.PassCmp, b.PassAtt);
                    Assert.LessOrEqual(b.ThirdDownConv, b.ThirdDownAtt);
                    Assert.GreaterOrEqual(b.TimeOfPossessionSeconds, 0);
                }
                Assert.AreEqual(r.HomeScore, r.Scoring.Where(s => s.TeamId == h.Id).Sum(s => s.Points));
                Assert.AreEqual(r.AwayScore, r.Scoring.Where(s => s.TeamId == a.Id).Sum(s => s.Points));
                Assert.GreaterOrEqual(r.Drives.Count, 14);
                Assert.IsTrue(r.Drives.All(d => !string.IsNullOrEmpty(d.Result)));
                puntDrives += r.Drives.Count(d => d.Result == "Punt");   // a shootout can have no punt at all, so punts are checked over the whole sample
                int driveSum = r.Drives.Sum(d => d.Points), total = r.HomeScore + r.AwayScore;
                Assert.LessOrEqual(driveSum, total, "drive points");
                Assert.GreaterOrEqual(driveSum, total - 24, "points outside drives are rare (returns, safeties)");
                Assert.Greater(r.Home.TimeOfPossessionSeconds + r.Away.TimeOfPossessionSeconds, 3000);
            }
            Assert.Greater(puntDrives, 100, "punts over the sample");
        }


        [Test]
        public void StatLinesAreAttributedToRealRosterPlayers()
        {
            GameResult r = QuickSim.SimGame(Lg.Teams[2], Lg.Teams[20], RuleSet.Pro(), new Rng(41));
            Assert.Greater(r.PlayerLines.Count, 35);
            foreach (PlayerGameLine l in r.PlayerLines)
            {
                Team t = Lg.Teams[l.TeamId];
                Player p = t.GetPlayer(l.PlayerId);
                Assert.IsNotNull(p, l.Name);
                Assert.AreEqual(p.Name, l.Name);
                Assert.AreEqual(1, l.Stats.Games);
            }
            // a starting QB throws, a kicker kicks, defenders tackle
            Assert.IsTrue(r.PlayerLines.Any(l => l.Pos == Position.QB && l.Stats.PassAtt > 10));
            Assert.IsTrue(r.PlayerLines.Any(l => l.Pos == Position.K && (l.Stats.XpAtt > 0 || l.Stats.FgAtt > 0)));
            Assert.IsTrue(r.PlayerLines.Any(l => PositionGroup.IsDefense(l.Pos) && l.Stats.Tackles > 3));
        }

        [Test]
        public void GameEndsWithAWinnerOrAnOvertimeTieUnderProRules()
        {
            var rng = new Rng(123);
            int checkedOt = 0;
            for (int i = 0; i < 300; i++)
            {
                GameResult r = QuickSim.SimGame(Lg.Teams[i % 32], Lg.Teams[(i + 9) % 32], RuleSet.Pro(), rng);
                if (r.Overtime) { checkedOt++; Assert.AreEqual(1, r.OvertimePeriods); }
                if (r.Tie) Assert.IsTrue(r.Overtime, "a tie can only come after overtime");
            }
            Assert.Greater(checkedOt, 3);
        }

        [Test]
        public void ForcedWinnerGamesNeverTie()
        {
            var rng = new Rng(55);
            for (int i = 0; i < 300; i++)
            {
                GameResult r = QuickSim.SimGame(Lg.Teams[i % 32], Lg.Teams[(i + 1) % 32], RuleSet.Pro(), rng, new SimOptions { ForceWinner = true });
                Assert.IsFalse(r.Tie, "forced winner game tied");
            }
        }

        [Test]
        public void CollegeRulesNeverTieAndUseCollegeOvertime()
        {
            CollegeWorld w = LeagueTestUtil.LoadCollege(12);
            var rng = new Rng(8);
            int ot = 0;
            for (int i = 0; i < 300; i++)
            {
                Team a = w.Schools[i % 138].Team, b = w.Schools[(i * 3 + 1) % 138 == i % 138 ? (i + 2) % 138 : (i * 3 + 1) % 138].Team;
                GameResult r = QuickSim.SimGame(a, b, RuleSet.College(), rng);
                Assert.IsFalse(r.Tie);
                if (r.Overtime) ot++;
            }
            Assert.Greater(ot, 2);
        }

        [Test]
        public void PenaltiesComeFromTheCoreCatalogAndAreLogged()
        {
            var rng = new Rng(3);
            int pens = 0;
            for (int i = 0; i < 20; i++)
            {
                GameResult r = QuickSim.SimGame(Lg.Teams[i], Lg.Teams[i + 11], RuleSet.Pro(), rng, new SimOptions { KeepPlayLog = true });
                pens += r.Home.Penalties + r.Away.Penalties;
                Assert.Greater(r.PlayLog.Count, 100);
                foreach (string line in r.PlayLog.Where(l => l.Contains("Penalty:")))
                {
                    string name = line.Substring(line.IndexOf("Penalty:") + 9);
                    name = name.Substring(0, name.LastIndexOf(" on "));
                    Assert.IsTrue(Penalty.All.Any(p => p.Name == name), "unknown penalty '" + name + "'");
                }
            }
            Assert.Greater(pens, 100);
        }

        [Test]
        public void InjuredPlayersDoNotPlay()
        {
            ProLeague lg = LeagueFactory.CreatePro(7);
            Team t = lg.Teams[4];
            Player qb = t.GetPlayer(t.Depth.StarterId(Position.QB));
            Player rb = t.GetPlayer(t.Depth.StarterId(Position.RB));
            qb.Injury.WeeksOut = 5; rb.Injury.WeeksOut = 3;
            GameResult r = QuickSim.SimGame(t, lg.Teams[9], RuleSet.Pro(), new Rng(2));
            Assert.IsNull(r.FindLine(qb.Id));
            Assert.IsNull(r.FindLine(rb.Id));
            Assert.Greater(r.PlayerLines.Where(l => l.TeamId == t.Id).Sum(l => l.Stats.PassAtt), 15, "backup QB plays");
        }

        [Test]
        public void SimulationDoesNotMutateRosters()
        {
            ProLeague lg = LeagueFactory.CreatePro(7);
            long before = LeagueTestUtil.Fingerprint(lg);
            var rng = new Rng(9);
            for (int i = 0; i < 20; i++) QuickSim.SimGame(lg.Teams[i], lg.Teams[i + 5], RuleSet.Pro(), rng);
            Assert.AreEqual(before, LeagueTestUtil.Fingerprint(lg));
        }

        [Test]
        public void ArchetypesChangeProduction()
        {
            // identical team except a scrambling QB: more QB rushing yards than the pocket passer
            var rng = new Rng(14);
            ProLeague lg = LeagueFactory.CreatePro(7);
            Team baseTeam = lg.Teams[3];
            Player qbBase = baseTeam.GetPlayer(baseTeam.Depth.StarterId(Position.QB));
            Team a = LeagueTestUtil.CloneWithBoost(baseTeam, 0, 600), b = LeagueTestUtil.CloneWithBoost(baseTeam, 0, 601);
            Player qa = a.Roster.First(p => p.Id == qbBase.Id), qbb = b.Roster.First(p => p.Id == qbBase.Id);
            qa.Ratings.Speed = 55; qa.Ratings.Elusiveness = 40; qa.Ratings.Agility = 50;
            qbb.Ratings.Speed = 92; qbb.Ratings.Elusiveness = 90; qbb.Ratings.Agility = 90;
            int rushA = 0, rushB = 0;
            for (int i = 0; i < 80; i++)
            {
                GameResult ra = QuickSim.SimGame(a, lg.Teams[12], RuleSet.Pro(), rng);
                GameResult rb = QuickSim.SimGame(b, lg.Teams[12], RuleSet.Pro(), rng);
                rushA += ra.FindLine(qbBase.Id) != null ? ra.FindLine(qbBase.Id).Stats.RushYds : 0;
                rushB += rb.FindLine(qbBase.Id) != null ? rb.FindLine(qbBase.Id).Stats.RushYds : 0;
            }
            Assert.Greater(rushB, rushA + 40);
        }
    }
}
