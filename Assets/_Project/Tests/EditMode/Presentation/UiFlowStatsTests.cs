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
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>UI-FLOW data/stats pass: GameTracker accounting (drives, penalties, fumbles, scoring clock, mirror matches), final-screen team stats and the line score.</summary>
    public class UiFlowStatsTests
    {
        // ------------------------------------------------------------------------------------------- helpers
        static LeagueTeam MakeTeam(int id, string city, params int[] playerIds)
        {
            var t = new LeagueTeam { Id = id, City = city, Nickname = "Testers", Abbreviation = city.Substring(0, 3).ToUpperInvariant() };
            foreach (int pid in playerIds) t.AddPlayer(new Player { Bio = new PlayerBio { Id = pid, FirstName = "P" + pid, LastName = "Q" + pid, JerseyNumber = pid % 99 }, Position = Position.WR });
            return t;
        }

        static GameState State(int quarter, float clock, int down = 1, int dist = 10, int spot = 30, Team poss = Team.Home)
        {
            var s = new GameState(RuleSet.Pro());
            s.Phase = GamePhase.Scrimmage; s.Quarter = quarter; s.ClockSeconds = clock; s.Down = down; s.Distance = dist; s.BallSpot = spot; s.Possession = poss;
            return s;
        }

        static GameTracker NewTracker(out LeagueTeam home, out LeagueTeam away)
        {
            home = MakeTeam(1, "Alpha", 11, 12, 13); away = MakeTeam(2, "Bravo", 21, 22, 23);
            return new GameTracker(home, away, "ALP", "BRA", 5);
        }

        static Func<int, Player> Lookup(params LeagueTeam[] teams)
        {
            return id => { foreach (var t in teams) { var p = t.GetPlayer(id); if (p != null) return p; } return null; };
        }

        static PlayOutcome Outcome(PlayResult r, Team offense, int qBefore, int qAfter, Headline h = Headline.None)
        {
            return new PlayOutcome { Result = r, Kind = PlayKind.Scrimmage, Offense = offense, QuarterBefore = qBefore, QuarterAfter = qAfter, Headline = h, Applied = true };
        }

        // ------------------------------------------------------------------------------------------- QA-834 drives across quarter breaks
        [Test]
        public void ADriveCarriesAcrossQuarterBreaksAndOnlyEndsAtTheHalf()
        {
            LeagueTeam h, a; var t = NewTracker(out h, out a); var look = Lookup(h, a);
            t.BeginPlay(State(1, 6f));
            t.RecordPlay(Outcome(PlayResult.Run(4, 6f, false, 11, 21), Team.Home, 1, 2), PlayKind.Scrimmage, look, State(2, 900f, 2, 6, 34));
            Assert.AreEqual(1, t.Drives.Count);
            Assert.IsNotNull(t.Current, "the open drive survives the Q1 -> Q2 break");
            Assert.IsTrue(string.IsNullOrEmpty(t.Drives[0].Result), "no END OF QUARTER result");
            Assert.AreEqual(1, t.HomeBox.Drives);

            t.BeginPlay(State(2, 880f, 2, 6, 34));
            t.RecordPlay(Outcome(PlayResult.Run(3, 6f, false, 11, 21), Team.Home, 2, 2), PlayKind.Scrimmage, look, State(2, 860f, 3, 3, 37));
            Assert.AreEqual(1, t.Drives.Count, "still the same drive");
            Assert.AreEqual(2, t.Drives[0].Plays);

            t.BeginPlay(State(2, 5f, 3, 3, 37));
            t.RecordPlay(Outcome(PlayResult.Run(2, 5f, false, 11, 21), Team.Home, 2, 3), PlayKind.Scrimmage, look, State(3, 900f, 4, 1, 39));
            Assert.IsNull(t.Current, "the half ends the drive");
            Assert.AreEqual("End of Half", t.Drives[0].Result);
            Assert.AreEqual(1, t.Drives.Count);
        }

        [Test]
        public void EndOfRegulationIntoOvertimeIsNotCalledEndOfHalf()
        {
            LeagueTeam h, a; var t = NewTracker(out h, out a);
            t.BeginPlay(State(4, 4f));
            t.RecordPlay(Outcome(PlayResult.Run(1, 4f, false, 11, 21), Team.Home, 4, 5), PlayKind.Scrimmage, Lookup(h, a), State(5, 600f));
            Assert.AreEqual("End of Regulation", t.Drives[0].Result);
            Assert.AreEqual("END OF REGULATION", OverlayText.DriveResult(t.Drives[0]));
        }

        [Test]
        public void ElapsedTimeUsesTheRealOvertimeLength()
        {
            LeagueTeam h, a; var t = NewTracker(out h, out a);
            t.OvertimeSeconds = 300;
            Assert.AreEqual(4 * 300, t.Elapsed(5, 300f));
            Assert.AreEqual(4 * 300 + 120, t.Elapsed(5, 180f));
            Assert.AreEqual(4 * 300 + 300 + 60, t.Elapsed(6, 240f));
        }

        // ------------------------------------------------------------------------------------------- QA-839/840 penalties
        [Test]
        public void AcceptedPenaltyBooksTheEnforcedYardsNotTheNominalOnes()
        {
            LeagueTeam h, a; var t = NewTracker(out h, out a);
            var call = new PenaltyCall(PenaltyType.OffensiveHolding, Team.Home, 0f, 12);
            var r = PlayResult.Run(5, 5f, false, 11, 21); r.Penalty = call;
            var o = Outcome(r, Team.Home, 1, 1, Headline.Penalty); o.PenaltyAccepted = true; o.PenaltyYards = 7;   // half the distance to the goal, say
            t.BeginPlay(State(1, 500f, 1, 10, 14));
            t.RecordPlay(o, PlayKind.Scrimmage, Lookup(h, a), State(1, 495f, 1, 17, 7));
            Assert.AreEqual(1, t.HomeBox.Penalties);
            Assert.AreEqual(7, t.HomeBox.PenaltyYds, "the enforced yards, not Def.ProYards (10)");
            Assert.AreEqual(1, t.Line(12).Stats.Penalties);
            Assert.AreEqual(0, t.HomeBox.RushAtt, "the nullified play counts for nothing");
        }

        [Test]
        public void AcceptedPenaltyWithoutACallDoesNotThrow()
        {
            LeagueTeam h, a; var t = NewTracker(out h, out a);
            var o = Outcome(PlayResult.Run(5, 5f, false, 11, 21), Team.Home, 1, 1, Headline.Penalty); o.PenaltyAccepted = true;
            t.BeginPlay(State(1, 500f));
            Assert.DoesNotThrow(() => t.RecordPlay(o, PlayKind.Scrimmage, Lookup(h, a), State(1, 495f)));
            Assert.AreEqual(0, t.HomeBox.Penalties + t.AwayBox.Penalties);
        }

        [Test]
        public void DefensivePenaltyFirstDownIsCounted()
        {
            LeagueTeam h, a; var t = NewTracker(out h, out a);
            var r = PlayResult.Incomplete(4f, 11, 12); r.Penalty = new PenaltyCall(PenaltyType.DefensiveHolding, Team.Away, 0f, 22);
            var o = Outcome(r, Team.Home, 1, 1, Headline.Penalty); o.PenaltyAccepted = true; o.PenaltyYards = 5; o.PenaltyFirstDown = true; o.FirstDownGained = true;
            t.BeginPlay(State(1, 500f, 3, 8, 40));
            t.RecordPlay(o, PlayKind.Scrimmage, Lookup(h, a), State(1, 495f, 1, 10, 45));
            Assert.AreEqual(1, t.AwayBox.Penalties); Assert.AreEqual(5, t.AwayBox.PenaltyYds);
            Assert.AreEqual(1, t.HomeBox.FirstDowns);
        }

        // ------------------------------------------------------------------------------------------- QA-835/836 fumbles
        [Test]
        public void AFumbledCarryKeepsItsAttemptAndYards()
        {
            LeagueTeam h, a; var t = NewTracker(out h, out a);
            var r = PlayResult.FumbleRecovered(6, 5f); r.RusherId = 11; r.TacklerId = 21;
            t.BeginPlay(State(1, 500f));
            t.RecordPlay(Outcome(r, Team.Home, 1, 1, Headline.Fumble), PlayKind.Scrimmage, Lookup(h, a), State(1, 495f, 2, 4, 36));
            Assert.AreEqual(1, t.HomeBox.RushAtt); Assert.AreEqual(6, t.HomeBox.RushYds);
            var l = t.Line(11);
            Assert.AreEqual(1, l.Stats.RushAtt); Assert.AreEqual(6, l.Stats.RushYds); Assert.AreEqual(1, l.Stats.Fumbles); Assert.AreEqual(0, l.Stats.FumblesLost);
            Assert.AreEqual(1, t.Line(21).Stats.ForcedFumbles, "the tackler gets the forced fumble");
            Assert.AreEqual(6, t.Drives[0].Yards);
        }

        [Test]
        public void ALostFumbleCreditsTheQuarterbackAndTheRecoverer()
        {
            LeagueTeam h, a; var t = NewTracker(out h, out a);
            var r = PlayResult.FumbleLost(-3, 0); r.PasserId = 12; r.DefenderId = 22; r.TacklerId = 23;
            var o = Outcome(r, Team.Home, 1, 1, Headline.FumbleLost); o.PossessionChanged = true;
            t.BeginPlay(State(1, 500f));
            t.RecordPlay(o, PlayKind.Scrimmage, Lookup(h, a), State(1, 495f, 1, 10, 67, Team.Away));
            Assert.AreEqual(1, t.Line(12).Stats.Fumbles); Assert.AreEqual(1, t.Line(12).Stats.FumblesLost);
            Assert.AreEqual(1, t.Line(22).Stats.FumbleRecoveries);
            Assert.AreEqual(1, t.HomeBox.FumblesLost); Assert.AreEqual(1, t.AwayBox.Takeaways);
            Assert.AreEqual("Fumble", t.Drives[0].Result);
        }

        [Test]
        public void FumbleBookingRuleIsConsistent()
        {
            int carrier;
            Assert.AreEqual(FumbleBooking.Rush, MatchFlow.FumbleBookingOf(new PlayResult { Type = PlayType.Fumble, RusherId = 5 }, out carrier)); Assert.AreEqual(5, carrier);
            Assert.AreEqual(FumbleBooking.Pass, MatchFlow.FumbleBookingOf(new PlayResult { Type = PlayType.Fumble, PasserId = 4, ReceiverId = 8 }, out carrier)); Assert.AreEqual(8, carrier);
            Assert.AreEqual(FumbleBooking.Rush, MatchFlow.FumbleBookingOf(new PlayResult { Type = PlayType.Fumble, PasserId = 4 }, out carrier)); Assert.AreEqual(4, carrier);
            Assert.AreEqual(FumbleBooking.None, MatchFlow.FumbleBookingOf(new PlayResult { Type = PlayType.Fumble }, out carrier)); Assert.AreEqual(0, carrier);
        }

        // ------------------------------------------------------------------------------------------- QA-837/838 extra points and score clock
        [Test]
        public void ExtraPointGoesToTheDriveThatScoredAndTheScoreKeepsItsClock()
        {
            LeagueTeam h, a; var t = NewTracker(out h, out a); var look = Lookup(h, a);
            // home drive: a 20 yard touchdown on the very last play of the first quarter
            var td = PlayResult.Run(20, 6f, false, 11, 21); td.Touchdown = true;
            var o = Outcome(td, Team.Home, 1, 2, Headline.Touchdown); o.ScoringTeam = Team.Home; o.HomePoints = 6; o.Text = "TOUCHDOWN";
            var after = State(2, 900f, 1, 10, 0); after.HomeScore = 6; after.Phase = GamePhase.PointAfter;
            t.BeginPlay(State(1, 4f, 1, 10, 80));
            t.RecordPlay(o, PlayKind.Scrimmage, look, after);
            Assert.AreEqual(1, t.Scoring.Count);
            Assert.AreEqual(1, t.Scoring[0].Quarter);
            Assert.AreEqual(0, t.Scoring[0].ClockSeconds, "scored with no time left in Q1, not at the 15:00 of the next quarter");
            Assert.AreEqual(6, t.Drives[0].Points);

            // then a pick-six by the away team on the next series, and an away extra point: it must not be booked to the home drive
            var ex = PlayResult.Extra(true); ex.KickerId = 22;
            var oe = new PlayOutcome { Result = ex, Kind = PlayKind.Scrimmage, Offense = Team.Home, QuarterBefore = 2, QuarterAfter = 2, Headline = Headline.ExtraPoint, ScoringTeam = Team.Home, HomePoints = 1 };
            var after2 = State(2, 900f); after2.HomeScore = 7;
            t.RecordPlay(oe, PlayKind.FieldGoal, look, after2);
            Assert.AreEqual(7, t.Drives[0].Points, "the extra point is part of the touchdown drive");
        }

        [Test]
        public void ExtraPointAfterADefensiveTouchdownIsNotBookedToAnOffensiveDrive()
        {
            LeagueTeam h, a; var t = NewTracker(out h, out a); var look = Lookup(h, a);
            // home drive that ends with a punt
            t.BeginPlay(State(1, 800f));
            t.RecordPlay(Outcome(PlayResult.Run(2, 6f, false, 11, 21), Team.Home, 1, 1), PlayKind.Scrimmage, look, State(1, 790f, 2, 8, 32));
            // away picks off a pass and scores
            var pick = PlayResult.Intercept(10, 40, true); pick.PasserId = 12; pick.DefenderId = 22; pick.Turnover = true;
            var o = Outcome(pick, Team.Home, 1, 1, Headline.Touchdown); o.ScoringTeam = Team.Away; o.AwayPoints = 6; o.PossessionChanged = true; o.Text = "TOUCHDOWN";
            var after = State(1, 780f, 1, 10, 0, Team.Away); after.AwayScore = 6; after.Phase = GamePhase.PointAfter;
            t.BeginPlay(State(1, 790f, 2, 8, 32));
            t.RecordPlay(o, PlayKind.Scrimmage, look, after);
            var ex = PlayResult.Extra(true); ex.KickerId = 23;
            var oe = new PlayOutcome { Result = ex, Kind = PlayKind.Scrimmage, Offense = Team.Away, QuarterBefore = 1, QuarterAfter = 1, Headline = Headline.ExtraPoint, ScoringTeam = Team.Away, AwayPoints = 1 };
            var after2 = State(1, 780f, 1, 10, 0, Team.Away); after2.AwayScore = 7;
            t.RecordPlay(oe, PlayKind.FieldGoal, look, after2);
            Assert.AreEqual(0, t.Drives.Sum(d => d.Points), "no offensive drive scored; neither the 6 nor the 1 belongs to the home drive");
            Assert.AreEqual(7, t.AwayBox.Score);
        }

        // ------------------------------------------------------------------------------------------- QA-841 snapshot reset
        [Test]
        public void ThePreSnapSnapshotIsNotReusedByTheNextPlay()
        {
            LeagueTeam h, a; var t = NewTracker(out h, out a); var look = Lookup(h, a);
            t.BeginPlay(State(1, 500f, 3, 4, 40));
            t.RecordPlay(Outcome(PlayResult.Run(2, 5f, false, 11, 21), Team.Home, 1, 1), PlayKind.Scrimmage, look, State(1, 495f, 4, 2, 42));
            Assert.AreEqual(1, t.HomeBox.ThirdDownAtt);
            // no BeginPlay this time: the old 3rd down must not be counted again
            t.RecordPlay(Outcome(PlayResult.Run(2, 5f, false, 11, 21), Team.Home, 1, 1), PlayKind.Scrimmage, look, State(1, 490f, 1, 10, 44));
            Assert.AreEqual(1, t.HomeBox.ThirdDownAtt, "third down counted once");
            Assert.AreEqual(2, t.Drives[0].Plays);
        }

        // ------------------------------------------------------------------------------------------- QA-842 mirror match
        [Test]
        public void MirrorMatchPlayersBelongToTheirOwnRoster()
        {
            var home = MakeTeam(7, "Mirror", 31, 32); var away = MakeTeam(7, "Mirror", 41, 42);
            var t = new GameTracker(home, away, "MIR", "MIR", 5);
            Assert.AreEqual(0, t.TeamIdxOfPlayer(home.GetPlayer(31)));
            Assert.AreEqual(1, t.TeamIdxOfPlayer(away.GetPlayer(41)));
            var look = Lookup(home, away);
            t.BeginPlay(State(1, 500f));
            t.RecordPlay(Outcome(PlayResult.Run(4, 5f, false, 31, 41), Team.Home, 1, 1), PlayKind.Scrimmage, look, State(1, 495f, 2, 6, 34));
            Assert.AreEqual(1, t.Leaders(0, "rush", 3).Count);
            Assert.AreEqual(0, t.Leaders(1, "rush", 3).Count, "the away team has no rusher");
            Assert.AreEqual(1, t.Leaders(1, "def", 3).Count, "the away tackler is on the away side");
            Assert.AreEqual(1, t.TeamIdxOfLine(t.Line(41)));
        }

        // ------------------------------------------------------------------------------------------- overlay text
        [Test]
        public void ExtraPointOnlyKickerGetsAStatLine()
        {
            Assert.AreEqual("3/3 XP", OverlayText.StatLine(new StatLine { XpAtt = 3, XpMade = 3 }, Position.K));
            Assert.AreEqual("2/3 FG, 3/3 XP", OverlayText.StatLine(new StatLine { FgAtt = 3, FgMade = 2, XpAtt = 3, XpMade = 3 }, Position.K));
            var tab = StatTable.For(new StatLine { XpAtt = 2, XpMade = 2 }, Position.K);
            Assert.IsNotNull(tab); Assert.AreEqual(StatTableKind.Kicker, tab.Kind);
            Assert.AreEqual("2/2", tab.Values[1]);
        }

        [Test]
        public void PlayerOfTheGameLineHasNoDanglingSeparator()
        {
            var k = new Player { Bio = new PlayerBio { Id = 5, FirstName = "A", LastName = "B", JerseyNumber = 3 }, Position = Position.K };
            Assert.AreEqual("K - ALPHA TESTERS", OverlayText.PotgSubLine(k, "Alpha Testers", new StatLine()));
            Assert.AreEqual("K - ALPHA TESTERS - 3/3 XP", OverlayText.PotgSubLine(k, "Alpha Testers", new StatLine { XpAtt = 3, XpMade = 3 }));
            Assert.AreEqual("A - C", OverlayText.JoinNonEmpty(" - ", "A", "", "C", null));
            Assert.AreEqual("3/10", OverlayText.Ratio(3, 10)); Assert.AreEqual("-", OverlayText.Ratio(0, 0));
            Assert.AreEqual(4, OverlayText.CompareExtra(new TeamBoxScore(), new TeamBoxScore()).Count);
        }

        [Test]
        public void CommentaryNeverPrintsEmptyNames()
        {
            var o = new PlayOutcome { Result = PlayResult.Pass(12, 6f, false, 1, 2), Headline = Headline.Touchdown, Offense = Team.Home };
            o.Result.Touchdown = true;
            var line = CommentaryWriter.Summarize(o, id => null, 1, "5:00", 3);
            StringAssert.Contains("THE QUARTERBACK", line.Text); StringAssert.Contains("THE RECEIVER", line.Text);
            StringAssert.DoesNotContain("  ", line.Text);
            var sf = CommentaryWriter.Summarize(new PlayOutcome { Result = PlayResult.FumbleLost(-2), Headline = Headline.FumbleLost, Offense = Team.Home }, id => null, 1, "5:00", 4);
            StringAssert.Contains("THE DEFENDER", sf.Text);
            var safety = CommentaryWriter.Summarize(new PlayOutcome { Result = PlayResult.Sack(3, 5f, 1, 2), Headline = Headline.Safety, Offense = Team.Home }, id => null, 1, "5:00", 5);
            Assert.AreEqual(CommentaryKind.Safety, safety.Kind, "a safety is not styled as a touchdown");
            var bus = new CommentaryBus(); var sink = new List<CommentaryLine>();
            bus.Emit(new CommentaryLine { Text = "" });
            Assert.AreEqual(0, bus.History.Count, "an empty line is dropped");
        }

        // ------------------------------------------------------------------------------------------- final screen + line score (full games)
        sealed class Run
        {
            public MatchFlow Flow; public GameTracker Tracker; public readonly List<PlayOutcome> Outcomes = new List<PlayOutcome>(); public readonly List<int[]> Before = new List<int[]>();
        }

        static Run PlayWholeGame(int seed, bool college)
        {
            var cfg = new MatchConfig { QuarterMinutes = 2, RunoffScale = 0.5f, CpuVsCpu = true, Seed = seed, CollegeRules = college };
            var home = MatchData.TeamByKey("buffalo_ironhorns"); var away = MatchData.TeamByKey("miami_reefrunners");
            var run = new Run { Flow = new MatchFlow(cfg, home, away, Team.Away, new Rng(seed)) };
            run.Tracker = new GameTracker(home, away, run.Flow.Abbr(Team.Home), run.Flow.Abbr(Team.Away), cfg.QuarterMinutes);
            int guard = 0;
            while (!run.Flow.State.IsFinal && guard++ < 900)
            {
                var pc = run.Flow.PrepareSnap();
                if (pc.Changed) { if (run.Flow.State.IsFinal) break; continue; }
                Play off, def; run.Flow.CpuCalls(out off, out def);
                var sim = run.Flow.CreateSim(off, def, true);
                run.Tracker.BeginPlay(run.Flow.State);
                int down0 = run.Flow.State.Down, dist0 = run.Flow.State.Distance;
                sim.RunToCompletion();
                var o = run.Flow.Finish(sim);
                var map = new Dictionary<int, Player>(); for (int i = 0; i < 22; i++) if (sim.Agents[i].Player != null) map[sim.Agents[i].Player.Id] = sim.Agents[i].Player;
                run.Tracker.RecordPlay(o, sim.Kind, id => { Player p; return map.TryGetValue(id, out p) ? p : null; }, run.Flow.State);
                run.Outcomes.Add(o); run.Before.Add(new[] { down0, dist0 });
            }
            return run;
        }

        [Test]
        public void LineScoreAddsUpToTheFinalScoreInProAndCollegeGames()
        {
            foreach (bool college in new[] { false, true })
                for (int seed = 5; seed < 7; seed++)
                {
                    var run = PlayWholeGame(seed, college);
                    var f = run.Flow;
                    Assert.IsTrue(f.State.IsFinal, "game finished (seed " + seed + ")");
                    Assert.AreEqual(f.State.HomeScore, f.HomeByQuarter.Sum(), "home line score, college=" + college + " seed " + seed);
                    Assert.AreEqual(f.State.AwayScore, f.AwayByQuarter.Sum(), "away line score, college=" + college + " seed " + seed);
                    Assert.IsTrue(f.LineScoreMatchesScore());
                    Assert.AreEqual(0, f.HomeByQuarter[0] + f.AwayByQuarter[0], "nothing in the unused slot 0");
                }
        }

        [Test]
        public void FinalScreenTeamStatsUseEnforcedPenaltyYardsAndTheLineToGain()
        {
            foreach (bool college in new[] { false, true })
            {
                var run = PlayWholeGame(11, college);
                var f = run.Flow;
                int flags = 0, flagYards = 0;
                for (int i = 0; i < run.Outcomes.Count; i++)
                {
                    var o = run.Outcomes[i]; int down0 = run.Before[i][0], dist0 = run.Before[i][1];
                    var r = o.Result;
                    if (o.PenaltyAccepted)
                    {
                        flags++; flagYards += o.PenaltyYards;
                        Assert.GreaterOrEqual(o.PenaltyYards, 0, "an accepted flag always has its enforced yardage");
                        if (r.Penalty != null && r.Penalty.Type != PenaltyType.IllegalBlockInBack)
                            Assert.LessOrEqual(o.PenaltyYards, r.Penalty.Def.YardsFor(f.Rules, r.Penalty.FoulSpotYards), "never more than the yardage the rule set prescribes (college=" + college + ")");
                        continue;
                    }
                    bool kept = !r.Turnover && !o.PossessionChanged;
                    if (kept && (r.Type == PlayType.Run || r.Type == PlayType.Pass) && dist0 > 0 && r.Yards >= dist0)
                        Assert.IsTrue(o.FirstDownGained, "yards " + r.Yards + " on " + down0 + " & " + dist0 + " is a first down");
                    if (r.Turnover) Assert.IsFalse(o.FirstDownGained, "no first down on a turnover");
                }
                Assert.AreEqual(f.HomeStats.Penalties + f.AwayStats.Penalties, flags);
                Assert.AreEqual(f.HomeStats.PenaltyYards + f.AwayStats.PenaltyYards, flagYards);
                // the broadcast tracker and the final screen agree on every shared figure
                Assert.AreEqual(f.HomeStats.PenaltyYards, run.Tracker.HomeBox.PenaltyYds); Assert.AreEqual(f.AwayStats.PenaltyYards, run.Tracker.AwayBox.PenaltyYds);
                Assert.AreEqual(f.HomeStats.FirstDowns, run.Tracker.HomeBox.FirstDowns, "home first downs"); Assert.AreEqual(f.AwayStats.FirstDowns, run.Tracker.AwayBox.FirstDowns, "away first downs");
                Assert.AreEqual(f.HomeStats.RushYards, run.Tracker.HomeBox.RushYds); Assert.AreEqual(f.AwayStats.RushYards, run.Tracker.AwayBox.RushYds);
                Assert.AreEqual(f.HomeStats.PassYards, run.Tracker.HomeBox.NetPassYards); Assert.AreEqual(f.AwayStats.PassYards, run.Tracker.AwayBox.NetPassYards);
                Assert.AreEqual(f.HomeStats.ThirdDownAtt, run.Tracker.HomeBox.ThirdDownAtt); Assert.AreEqual(f.HomeStats.ThirdDownConv, run.Tracker.HomeBox.ThirdDownConv);
                Assert.AreEqual(f.AwayStats.PassAtt, run.Tracker.AwayBox.PassAtt); Assert.AreEqual(f.AwayStats.PassComp, run.Tracker.AwayBox.PassCmp);
                // 100+ yards on the board means first downs on the board (the audit saw 187 yards with 1 first down)
                foreach (var s in new[] { f.HomeStats, f.AwayStats })
                    if (s.TotalYards >= 150) Assert.GreaterOrEqual(s.FirstDowns, 2, "a team with " + s.TotalYards + " yards has first downs");
            }
        }

        // ------------------------------------------------------------------------------------------- QA-1004 team catalog loader
        [Test]
        public void TeamCatalogParsesWithBomAndQuotesAndFallsBackToOneTeam()
        {
            string csv = "﻿\"id\",\"city\",\"nickname\",\"name\",\"conference\",\"division\",\"primary\",\"secondary\",\"tertiary\",\"logo\",\"abbr\"\r\n" +
                         "\"x_one\",\"Alpha, City\",\"Owls\",\"Alpha, City Owls\",\"Liberty Conference\",\"East\",\"#112233\",\"#445566\",\"#FFFFFF\",\"ProV6/x_one\",\"ALP\"\r\n" +
                         "\"x_two\",\"Bravo\",\"Bats\",\"Bravo Bats\",\"Liberty Conference\",\"East\",\"#AABBCC\",\"#DDEEFF\",\"#FFFFFF\",\"ProV6/x_two\",\"BRA\"\r\n";
            var list = TeamCatalog.Parse(csv);
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("x_one", list[0].Id, "the BOM does not leak into the first id");
            Assert.AreEqual("Alpha, City", list[0].City, "quoted commas stay in the field");
            Assert.AreEqual("ALP", list[0].Abbr); Assert.AreEqual("#AABBCC", list[1].PrimaryHex);
            var fallback = TeamCatalog.Parse("");
            Assert.AreEqual(1, fallback.Count); Assert.AreEqual(TeamCatalog.DefaultTeamId, fallback[0].Id); Assert.AreEqual("BUF", fallback[0].Abbr);
            Assert.AreEqual(1, TeamCatalog.Parse(null).Count);
            // the old column layout without an abbr column still parses
            var old = TeamCatalog.Parse("\"id\",\"city\",\"nickname\",\"name\",\"conference\",\"division\",\"primary\",\"secondary\",\"tertiary\",\"logo\"\n\"a\",\"A\",\"B\",\"A B\",\"C\",\"East\",\"#000000\",\"#111111\",\"#FFFFFF\",\"L\"\n");
            Assert.AreEqual(1, old.Count); Assert.AreEqual("", old[0].Abbr);
        }

        [Test]
        public void FieldPositionIsWrittenOneWay()
        {
            Assert.AreEqual("BUF 29", MatchFlow.FieldPositionText(29, "BUF", "MIA"));
            Assert.AreEqual("MIA 33", MatchFlow.FieldPositionText(67, "BUF", "MIA"));
            Assert.AreEqual("50", MatchFlow.FieldPositionText(50, "BUF", "MIA"));
            var o = new PlayOutcome { Result = PlayResult.Run(4) };
            Assert.IsNotEmpty(o.LogText); StringAssert.Contains("RUN", o.LogText);
            Assert.AreEqual("TOUCHDOWN", new PlayOutcome { Result = PlayResult.Run(4), Text = "TOUCHDOWN" }.LogText);
        }
    }
}
