using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.Presentation;
using PixFootball.Sim;
using Team = PixFootball.Core.Team;

namespace PixFootball.Tests
{
    public class StatsTests
    {
        sealed class Game
        {
            public MatchFlow Flow; public GameTracker Tracker; public int Plays;
            public readonly List<PlayOutcome> Outcomes = new List<PlayOutcome>();
            public readonly List<PlayDigest> Digests = new List<PlayDigest>();
        }

        static Game PlayGame(int seed, int snaps)
        {
            var cfg = new MatchConfig { QuarterMinutes = 5, RunoffScale = 0.5f, CpuVsCpu = true, Seed = seed };
            var home = MatchData.TeamByKey("buffalo_ironhorns"); var away = MatchData.TeamByKey("miami_reefrunners");
            var g = new Game { Flow = new MatchFlow(cfg, home, away, Team.Away, new Rng(seed)) };
            g.Tracker = new GameTracker(home, away, g.Flow.Abbr(Team.Home), g.Flow.Abbr(Team.Away), cfg.QuarterMinutes);
            int guard = 0;
            while (g.Plays < snaps && !g.Flow.State.IsFinal && guard++ < snaps * 4)
            {
                var pc = g.Flow.PrepareSnap();
                if (pc.Changed) { if (g.Flow.State.IsFinal) break; continue; }
                Play off, def; g.Flow.CpuCalls(out off, out def);
                var sim = g.Flow.CreateSim(off, def, true);
                g.Tracker.BeginPlay(g.Flow.State);
                sim.RunToCompletion();
                var o = g.Flow.Finish(sim);
                var map = new Dictionary<int, Player>(); for (int i = 0; i < 22; i++) if (sim.Agents[i].Player != null) map[sim.Agents[i].Player.Id] = sim.Agents[i].Player;
                var d = g.Tracker.RecordPlay(o, sim.Kind, id => { Player p; return map.TryGetValue(id, out p) ? p : null; }, g.Flow.State);
                g.Outcomes.Add(o); g.Digests.Add(d); g.Plays++;
            }
            return g;
        }

        // ------------------------------------------------------------------------------------------- box score
        [Test]
        public void TeamBoxScoresMatchTheFlowsOwnTeamStats()
        {
            var g = PlayGame(31, 70);
            Assert.Greater(g.Plays, 40);
            var hs = g.Flow.HomeStats; var hb = g.Tracker.HomeBox;
            var asx = g.Flow.AwayStats; var ab = g.Tracker.AwayBox;
            Assert.AreEqual(hs.RushYards, hb.RushYds, "home rushing"); Assert.AreEqual(asx.RushYards, ab.RushYds, "away rushing");
            Assert.AreEqual(hs.PassYards, hb.PassYds - hb.SackYdsLost, "home net passing"); Assert.AreEqual(asx.PassYards, ab.PassYds - ab.SackYdsLost, "away net passing");
            Assert.AreEqual(hs.Sacks, hb.SacksMade); Assert.AreEqual(asx.Sacks, ab.SacksMade);
            Assert.AreEqual(hs.Turnovers, hb.Giveaways); Assert.AreEqual(asx.Turnovers, ab.Giveaways);
            Assert.AreEqual(hb.Giveaways, ab.Takeaways); Assert.AreEqual(ab.Giveaways, hb.Takeaways);
            Assert.AreEqual(hs.Penalties, hb.Penalties); Assert.AreEqual(asx.Penalties, ab.Penalties);
            Assert.AreEqual(hs.FirstDowns, hb.FirstDowns); Assert.AreEqual(asx.FirstDowns, ab.FirstDowns);
        }

        [Test]
        public void RedZoneTripsAndTouchdownsAreCounted()
        {
            // B-050: the live tracker never touched RedZoneTrips / RedZoneTds, so the halftime and final stat card always said 0/0
            int trips = 0, tds = 0;
            foreach (int seed in new[] { 41, 42, 43, 44 })
            {
                var g = PlayGame(seed, 110);
                foreach (var b in new[] { g.Tracker.HomeBox, g.Tracker.AwayBox })
                {
                    Assert.LessOrEqual(b.RedZoneTds, b.RedZoneTrips, "a touchdown needs a trip");
                    trips += b.RedZoneTrips; tds += b.RedZoneTds;
                }
            }
            Assert.Greater(trips, 0, "four simulated games reach the red zone");
            Assert.Greater(tds, 0, "and score from it");
        }

        [Test]
        public void ScoringEventsAddUpToTheScore()
        {
            var g = PlayGame(32, 90);
            var s = g.Flow.State;
            int home = 0, away = 0;
            foreach (var e in g.Tracker.Scoring) { if (e.TeamId == g.Flow.Home.Id) home += e.Points; else away += e.Points; }
            Assert.AreEqual(s.HomeScore, home); Assert.AreEqual(s.AwayScore, away);
            Assert.AreEqual(s.HomeScore, g.Tracker.HomeBox.Score); Assert.AreEqual(s.AwayScore, g.Tracker.AwayBox.Score);
        }

        [Test]
        public void PlayerLinesAddUpToTheTeamTotals()
        {
            var g = PlayGame(33, 80);
            foreach (int idx in new[] { 0, 1 })
            {
                var box = g.Tracker.BoxOf(idx); int tid = g.Tracker.TeamOf(idx).Id;
                int passYds = 0, passAtt = 0, rushYds = 0, rushAtt = 0, rec = 0, recYds = 0, sacks = 0;
                foreach (var l in g.Tracker.Lines.Values.Where(x => x.TeamId == tid)) { passYds += l.Stats.PassYds; passAtt += l.Stats.PassAtt; rushYds += l.Stats.RushYds; rushAtt += l.Stats.RushAtt; rec += l.Stats.Rec; recYds += l.Stats.RecYds; sacks += l.Stats.Sacks; }
                Assert.AreEqual(box.PassYds, passYds, "pass yards team " + idx); Assert.AreEqual(box.PassAtt, passAtt, "pass attempts");
                Assert.AreEqual(box.RushYds, rushYds, "rush yards"); Assert.AreEqual(box.RushAtt, rushAtt, "rush attempts");
                Assert.AreEqual(box.PassCmp, rec, "completions equal receptions"); Assert.AreEqual(box.PassYds, recYds, "receiving yards equal passing yards");
                Assert.AreEqual(g.Tracker.BoxOf(1 - idx).SacksMade, g.Tracker.Lines.Values.Where(x => x.TeamId != tid).Sum(x => x.Stats.Sacks));
            }
        }

        // ------------------------------------------------------------------------------------------- drives
        [Test]
        public void DrivesAreCompleteAndConsistent()
        {
            var g = PlayGame(34, 90);
            var t = g.Tracker;
            Assert.Greater(t.Drives.Count, 6);
            int totalPlays = 0;
            for (int i = 0; i < t.Drives.Count; i++)
            {
                var d = t.Drives[i];
                if (d == t.Current) continue;   // the drive still in progress
                Assert.IsFalse(string.IsNullOrEmpty(d.Result), "drive " + i + " has no result");
                Assert.GreaterOrEqual(d.Plays, 1); Assert.GreaterOrEqual(d.Seconds, 0);
                Assert.LessOrEqual(d.Seconds, 15 * 60);
                totalPlays += d.Plays;
            }
            Assert.Greater(totalPlays, 40);
            // consecutive drives alternate possession unless the first one ended a half
            int swaps = 0; for (int i = 1; i < t.Drives.Count; i++) if (t.Drives[i].TeamId != t.Drives[i - 1].TeamId) swaps++;
            Assert.Greater(swaps, t.Drives.Count / 2);
            // time of possession: both teams hold the ball for a sensible share of the game
            int topH = t.HomeBox.TimeOfPossessionSeconds, topA = t.AwayBox.TimeOfPossessionSeconds;
            Assert.Greater(topH + topA, 200); Assert.Greater(topH, 0); Assert.Greater(topA, 0);
            // touchdown drives carry their points
            foreach (var d in t.Drives.Where(x => x.Result == "Touchdown")) Assert.GreaterOrEqual(d.Points, 6);
            foreach (var d in t.Drives.Where(x => x.Result == "Field Goal")) Assert.AreEqual(3, d.Points);
        }

        [Test]
        public void EveryFinishedDriveYieldsASummaryDigest()
        {
            var g = PlayGame(35, 60);
            int ended = g.Digests.Count(d => d.EndedDrive != null);
            Assert.AreEqual(g.Tracker.Drives.Count - (g.Tracker.Current != null ? 1 : 0), ended);
            foreach (var d in g.Digests.Where(x => x.EndedDrive != null)) { var text = OverlayText.DriveHeadline(d.EndedDrive); StringAssert.Contains("PLAY", text); StringAssert.Contains("YARD", text); Assert.IsNotEmpty(OverlayText.DriveResult(d.EndedDrive)); }
        }

        // ------------------------------------------------------------------------------------------- overlay data
        [Test]
        public void StatLinesReadLikeABroadcast()
        {
            var qb = new StatLine { PassAtt = 24, PassCmp = 18, PassYds = 231, PassTd = 2 };
            Assert.AreEqual("18/24, 231 YDS, 2 TD", OverlayText.StatLine(qb, Position.QB));
            Assert.AreEqual("18/24, 231 YDS, 2 TD, 1 INT", OverlayText.StatLine(new StatLine { PassAtt = 24, PassCmp = 18, PassYds = 231, PassTd = 2, PassInt = 1 }, Position.QB));
            Assert.AreEqual("14 CAR, 88 YDS, 1 TD", OverlayText.StatLine(new StatLine { RushAtt = 14, RushYds = 88, RushTd = 1 }, Position.RB));
            Assert.AreEqual("5 REC, 92 YDS", OverlayText.StatLine(new StatLine { Rec = 5, RecYds = 92 }, Position.WR));
            Assert.AreEqual("6 TKL, 2 SACKS", OverlayText.StatLine(new StatLine { Tackles = 6, Sacks = 2 }, Position.LE));
            Assert.AreEqual("2/3 FG, 3/3 XP", OverlayText.StatLine(new StatLine { FgAtt = 3, FgMade = 2, XpAtt = 3, XpMade = 3 }, Position.K));
            Assert.AreEqual("", OverlayText.StatLine(new StatLine(), Position.WR));
            var p = new Player { Bio = new PlayerBio { FirstName = "Jamal", LastName = "Doe", JerseyNumber = 12 }, Position = Position.QB };
            Assert.AreEqual("#12 J. DOE", OverlayText.Heading(p));
            Assert.AreEqual("#12 J. DOE - 18/24, 231 YDS, 2 TD", OverlayText.PlayerLine(p, qb));
            Assert.AreEqual("J. DOE", OverlayText.ShortName("Jamal Doe"));
        }

        [Test]
        public void DriveSummaryAndComparisonRowsComeFromTheBoxScore()
        {
            var d = new DriveRecord { Plays = 9, Yards = 72, Seconds = 271, Result = "Touchdown", Points = 7 };
            Assert.AreEqual("9 PLAYS - 72 YARDS - 4:31", OverlayText.DriveHeadline(d));
            Assert.AreEqual("TOUCHDOWN", OverlayText.DriveResult(d));
            Assert.IsTrue(OverlayText.WorthSummary(d)); Assert.IsFalse(OverlayText.WorthSummary(new DriveRecord { Plays = 1, Result = "Punt" }));
            var home = new TeamBoxScore { PassYds = 210, SackYdsLost = 10, RushYds = 95, FirstDowns = 17, ThirdDownAtt = 10, ThirdDownConv = 6, Giveaways = 1, Penalties = 4, PenaltyYds = 35, TimeOfPossessionSeconds = 1805 };
            var away = new TeamBoxScore { PassYds = 150, RushYds = 60, FirstDowns = 11, ThirdDownAtt = 0, Penalties = 2, PenaltyYds = 20, TimeOfPossessionSeconds = 1000 };
            var rows = OverlayText.Compare(home, away);
            Assert.AreEqual(8, rows.Count);
            Assert.AreEqual("295", rows[0].Home); Assert.AreEqual("210", rows[0].Away);
            Assert.AreEqual("6/10", rows[4].Home); Assert.AreEqual("-", rows[4].Away);
            Assert.AreEqual("4 - 35", rows[6].Home); Assert.AreEqual("30:05", rows[7].Home);
            Assert.AreEqual(3, OverlayText.CompareShort(home, away).Count);
            Assert.AreEqual("2ND & 7", OverlayText.DownAndDistance(2, 7, false)); Assert.AreEqual("1ST & GOAL", OverlayText.DownAndDistance(1, 4, true));
        }

        [Test]
        public void KickoffTemperatureIsStableAndPlausible()
        {
            int a = OverlayText.KickoffTemperature(123, true, "Buffalo"), b = OverlayText.KickoffTemperature(123, true, "Buffalo");
            Assert.AreEqual(a, b); Assert.That(a, Is.InRange(30, 80));
            Assert.LessOrEqual(OverlayText.KickoffTemperature(55, true, "Miami"), OverlayText.KickoffTemperature(55, false, "Miami"));
            var set = new HashSet<int>(); foreach (var city in new[] { "Buffalo", "Miami", "Denver", "Seattle", "Phoenix" }) set.Add(OverlayText.KickoffTemperature(9, false, city));
            Assert.Greater(set.Count, 1);
        }

        [Test]
        public void LeadersAndPlayerOfTheGameComeFromTheLines()
        {
            var g = PlayGame(36, 100);
            var t = g.Tracker;
            foreach (int idx in new[] { 0, 1 })
            {
                var pass = t.Leaders(idx, "pass", 3);
                for (int i = 1; i < pass.Count; i++) Assert.GreaterOrEqual(pass[i - 1].Stats.PassYds + pass[i - 1].Stats.PassTd * 20, pass[i].Stats.PassYds + pass[i].Stats.PassTd * 20);
                foreach (var l in pass) StringAssert.Contains("/", OverlayText.LeaderLine(l, "pass"));
            }
            var potg = t.PlayerOfTheGame(t.HomeBox.Score >= t.AwayBox.Score ? 0 : 1);
            Assert.IsNotNull(potg);
            foreach (var l in t.Lines.Values) Assert.GreaterOrEqual(GameTracker.Impact(potg) + (potg.TeamId == t.TeamOf(t.HomeBox.Score >= t.AwayBox.Score ? 0 : 1).Id ? 3f : 0f), GameTracker.Impact(l) + (l.TeamId == t.TeamOf(t.HomeBox.Score >= t.AwayBox.Score ? 0 : 1).Id ? 3f : 0f) - 1e-3f);
        }

        // ------------------------------------------------------------------------------------------- commentary hook
        sealed class Sink : ICommentaryHook { public readonly List<CommentaryLine> Lines = new List<CommentaryLine>(); public void OnCommentary(CommentaryLine l) { Lines.Add(l); } }

        [Test]
        public void CommentaryBusFeedsHooksWithPlayByPlayText()
        {
            var g = PlayGame(37, 20);
            var bus = new CommentaryBus(); var sink = new Sink(); bus.Register(sink); bus.Register(sink);
            var p = new Player { Bio = new PlayerBio { FirstName = "Jamal", LastName = "Doe", JerseyNumber = 12 }, Position = Position.QB };
            var q = new Player { Bio = new PlayerBio { FirstName = "Mo", LastName = "Roe", JerseyNumber = 81 }, Position = Position.WR };
            var o = new PlayOutcome { Result = PlayResult.Pass(18, 6f, false, 1, 2), Headline = Headline.FirstDown, Offense = Team.Home };
            var line = CommentaryWriter.Summarize(o, id => id == 1 ? p : q, 2, "4:12", 17);
            StringAssert.Contains("DOE", line.Text); StringAssert.Contains("ROE", line.Text); StringAssert.Contains("18 YARDS", line.Text); StringAssert.Contains("FIRST DOWN", line.Text);
            Assert.IsTrue(line.PlaySummary); Assert.AreEqual(17, line.PlayNumber);
            bus.Emit(line);
            Assert.AreEqual(1, sink.Lines.Count, "a hook registered twice hears a line once");
            var td = new PlayOutcome { Result = PlayResult.Run(7, 5f, false, 2, 1), Headline = Headline.Touchdown, Offense = Team.Home };
            td.Result.Touchdown = true;
            var l2 = CommentaryWriter.Summarize(td, id => q, 2, "3:00", 18);
            StringAssert.Contains("TOUCHDOWN", l2.Text); Assert.AreEqual(1f, l2.Priority);
            for (int i = 0; i < 60; i++) bus.Emit(line);
            Assert.LessOrEqual(bus.History.Count, bus.HistoryLimit);
            Assert.Greater(g.Plays, 10);
        }
    }
}
