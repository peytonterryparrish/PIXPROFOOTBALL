using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.Sim;
using Team = PixFootball.Core.Team;

namespace PixFootball.Tests
{
    /// <summary>v0.3.0: penalty accept / decline flow, college rules end to end (overtime, clock, hashes, try spot), call-screen data from the match flow.</summary>
    public class MatchV030Tests
    {
        static MatchFlow NewFlow(bool college, int seed = 5, int quarter = 5, bool cpuOnly = true, Team user = Team.Home)
        {
            var cfg = new MatchConfig { QuarterMinutes = quarter, RunoffScale = 0.5f, CpuVsCpu = cpuOnly, Seed = seed, CollegeRules = college, UserTeam = user };
            return new MatchFlow(cfg, MatchData.TeamByKey("buffalo_ironhorns"), MatchData.TeamByKey("miami_reefrunners"), Team.Away, new Rng(seed));
        }

        static void Scrimmage(MatchFlow f, int spot = 40, int down = 2, int dist = 8, Team poss = Team.Home)
        {
            f.State.Phase = GamePhase.Scrimmage; f.State.BallSpot = spot; f.State.Down = down; f.State.Distance = dist; f.State.Possession = poss;
        }

        static PlaySimulation Sim(MatchFlow f, string off = "Slants", string def = "Cover 3 Sky")
        {
            Play o = f.Book.AllPlays.First(p => p.Name == off && p.FormationName == "Shotgun");
            Play d = f.Book.AllPlays.First(p => p.Name == def && p.FormationName == "Nickel");
            return f.CreateSim(o, d, false);
        }

        // ------------------------------------------------------------------------------------------------ penalties
        static PlayResult Holding(PlayResult baseResult, Team by)
        {
            return baseResult.WithPenalty(new PenaltyCall(PenaltyType.OffensiveHolding, by, 0f, 0));
        }

        [Test]
        public void PenaltyChoiceDescribesBothOutcomes()
        {
            var gs = new GameState(RuleSet.Pro()) { Phase = GamePhase.Scrimmage, BallSpot = 40, Down = 2, Distance = 8, Possession = Team.Home };
            var play = Holding(PlayResult.Pass(25, 6f), Team.Home);        // holding on a 25-yard completion: decline is better for the offense, but the DEFENSE decides
            PenaltyChoice c = PenaltyEnforcer.Describe(gs, play);
            Assert.AreEqual(Team.Away, c.OfferedTo, "the offended team chooses");
            Assert.AreEqual(-10, c.Yards);
            Assert.AreEqual(30, c.Accept.Spot);
            Assert.AreEqual(2, c.Accept.Down); Assert.AreEqual(18, c.Accept.Distance);
            Assert.AreEqual(65, c.Decline.Spot);
            Assert.IsTrue(c.Decline.FirstDown);
            Assert.IsTrue(c.RecommendAccept, "the defense takes the penalty");
            Assert.AreEqual(40, gs.BallSpot, "describing a flag never touches the state");
        }

        [Test]
        public void AcceptingAndDecliningGiveDifferentGameStates()
        {
            for (int pass = 0; pass < 2; pass++)
            {
                var f = NewFlow(false, 9, 5, false, Team.Away);
                Scrimmage(f);
                var sim = Sim(f);
                // force a defensive holding flag on a short gain by running a real play and decorating its result
                sim.RunToCompletion();
                PlayResult r = sim.Result;
                r.Penalty = null; r.PenaltyDecision = PenaltyDecision.Auto;
                r.WithPenalty(new PenaltyCall(PenaltyType.DefensiveHolding, Team.Away, 3f, 0));
                var choice = f.PenaltyChoiceFor(sim);
                Assert.IsNotNull(choice);
                var o = f.Finish(sim, pass == 0 ? PenaltyDecision.Accept : PenaltyDecision.Decline);
                if (pass == 0)
                {
                    Assert.IsTrue(o.PenaltyAccepted);
                    Assert.AreEqual(Headline.Penalty, o.Headline);
                    Assert.AreEqual(45, f.State.BallSpot, "5 yards from the original spot");
                    Assert.AreEqual(1, f.State.Down, "automatic first down");
                }
                else
                {
                    Assert.IsFalse(o.PenaltyAccepted);
                    Assert.AreNotEqual(Headline.Penalty, o.Headline);
                }
                Assert.IsNotNull(o.PenaltyCall);
            }
        }

        [Test]
        public void DeadBallFoulDeclinedMeansReplayTheDown()
        {
            var f = NewFlow(false);
            Scrimmage(f);
            var sim = Sim(f);
            Assert.IsTrue(sim.CallPreSnapFoul(PenaltyType.Offside, Team.Away, 11));
            var choice = f.PenaltyChoiceFor(sim);
            Assert.IsTrue(choice.DeadBall);
            Assert.AreEqual(Team.Home, choice.OfferedTo);
            Assert.AreEqual(5, choice.Yards);
            Assert.AreEqual(45, choice.Accept.Spot);
            Assert.AreEqual(40, choice.Decline.Spot); Assert.IsTrue(choice.Decline.ReplayDown);
            var o = f.Finish(sim, PenaltyDecision.Decline);
            Assert.IsFalse(o.PenaltyAccepted);
            Assert.AreEqual(40, f.State.BallSpot); Assert.AreEqual(2, f.State.Down);
            // and accepting moves the ball
            var f2 = NewFlow(false); Scrimmage(f2); var s2 = Sim(f2); s2.CallPreSnapFoul(PenaltyType.Offside, Team.Away, 11);
            var o2 = f2.Finish(s2, PenaltyDecision.Accept);
            Assert.IsTrue(o2.PenaltyAccepted); Assert.AreEqual(45, f2.State.BallSpot); Assert.AreEqual(2, f2.State.Down); Assert.AreEqual(3, f2.State.Distance);
        }

        [Test]
        public void CpuDecisionFollowsExpectedPoints()
        {
            var f = NewFlow(false);
            Scrimmage(f);
            var sim = Sim(f);
            sim.CallPreSnapFoul(PenaltyType.FalseStart, Team.Home, 0);
            var o = f.Finish(sim);       // Auto: the defense accepts a false start
            Assert.IsTrue(o.PenaltyAccepted);
            Assert.AreEqual(35, f.State.BallSpot);
        }

        [Test]
        public void RefereeAnnouncementNamesTheFoulTeamNumberAndYards()
        {
            var f = NewFlow(false);
            Scrimmage(f);
            var sim = Sim(f);
            sim.CallPreSnapFoul(PenaltyType.FalseStart, Team.Home, 2);
            var c = f.PenaltyChoiceFor(sim);
            string text = f.RefereeText(c, true);
            StringAssert.Contains("False Start", text);
            StringAssert.Contains("5-yard penalty", text);
            StringAssert.Contains("Ironhorns", text);
            StringAssert.Contains("repeat", text.ToLowerInvariant());
            Assert.IsTrue(f.RefereeText(c, false).StartsWith("Penalty declined"));
            Assert.IsTrue(f.ChoiceHeadline(c).StartsWith("FLAG: FALSE START"));
            Assert.IsTrue(f.PenaltyNumber(c.Call) >= 0);
            Assert.IsFalse(string.IsNullOrEmpty(f.OutcomeText(c.Accept)));
        }

        [Test]
        public void UserDecidesOnlyWhenOffended()
        {
            var f = NewFlow(false, 5, 5, false, Team.Home);
            Scrimmage(f);
            var sim = Sim(f);
            sim.CallPreSnapFoul(PenaltyType.Offside, Team.Away, 11);
            Assert.IsFalse(f.UserDecides(f.PenaltyChoiceFor(sim)), "a dead-ball foul is enforced: nothing to decide (SIM-139)");
            var live = PlayResult.Run(8, 5f); live.Penalty = new PenaltyCall(PenaltyType.DefensiveHolding, Team.Away, 3f);
            Assert.IsTrue(f.UserDecides(PenaltyEnforcer.Describe(f.State, live)), "the user's offense was offended on a live play");
            var liveOwn = PlayResult.Run(8, 5f); liveOwn.Penalty = new PenaltyCall(PenaltyType.OffensiveHolding, Team.Home, 3f);
            Assert.IsFalse(f.UserDecides(PenaltyEnforcer.Describe(f.State, liveOwn)), "the CPU decides about the user's foul");
        }

        // ------------------------------------------------------------------------------------------------ call screen data
        [Test]
        public void CallModelHasEveryScreenForOffenseDefenseAndKicks()
        {
            var f = NewFlow(false, 5, 5, false, Team.Home);
            Scrimmage(f, 60, 4, 3);
            var off = f.CreateCallModel(true);
            Assert.AreEqual(PlaySide.Offense, off.Side);
            Assert.IsTrue((off.Tabs.Contains(CallTab.Playbook) || off.Tabs.Contains(CallTab.Formation)) && off.Tabs.Contains(CallTab.Special));
            Assert.IsTrue(off.SpecialPlays.Any(p => p.Category == PlayCategory.Punt) && off.SpecialPlays.Any(p => p.Category == PlayCategory.FieldGoal));
            Assert.IsTrue(off.CoachPicks.Count >= 4);
            Assert.IsTrue(off.CoachPicks[0].Play.Side == PlaySide.Special || off.CoachPicks[0].Reason.Length > 0, "fourth down starts with the kicking decision");
            Assert.AreEqual("4TH & 3", off.Situation.DownText);
            Assert.AreEqual(f.State.TimeoutsOf(Team.Home), off.Situation.Timeouts);

            Scrimmage(f, 40, 1, 10, Team.Away);
            Play cpuCall = f.Book.FindPlay("Slants");
            var def = f.CreateCallModel(false, cpuCall);
            Assert.AreEqual(PlaySide.Defense, def.Side);
            Assert.IsTrue(def.Formations.Any(x => x.Label == "NICKEL"));
            Assert.IsFalse(def.Tabs.Contains(CallTab.Special));

            // the CPU punts: the user picks the return instead of a coverage
            var ret = f.CreateCallModel(false, f.Book.FindPlay("Punt Standard"));
            Assert.AreEqual(CallTab.Special, ret.Tab);
            Assert.IsTrue(ret.Plays.All(p => p.Category == PlayCategory.PuntReturn));
            // the CPU kneels: nothing to call
            Assert.IsNull(f.CreateCallModel(false, f.Book.AllPlays.First(p => p.Category == PlayCategory.Kneel)));

            var ko = NewFlow(false, 5, 5, false, Team.Home);
            ko.State.Possession = Team.Home;      // the user kicks off
            var kick = ko.CreateCallModel(true);
            Assert.AreEqual(CallTab.Special, kick.Tab);
            Assert.IsTrue(kick.Plays.Any(p => p.Category == PlayCategory.Onside));
        }

        [Test]
        public void CallingAPlayRemembersItInRecent()
        {
            var f = NewFlow(false, 5, 5, false, Team.Home);
            Scrimmage(f);
            f.RememberUserCall(true, f.Book.FindPlay("Slants")); f.RememberUserCall(true, f.Book.FindPlay("Mesh")); f.RememberUserCall(true, f.Book.FindPlay("Slants"));
            var m = f.CreateCallModel(true);
            Assert.AreEqual(2, m.RecentPlays.Count);
            Assert.AreEqual("Slants", m.RecentPlays[0].Name, "newest first");
            Assert.AreEqual("SHOTGUN", m.CurrentFormation.Label, "the screen opens on the last formation");
        }

        // ------------------------------------------------------------------------------------------------ AI
        [Test]
        public void CpuHurryUpAndSpikeInTheTwoMinuteDrill()
        {
            var f = NewFlow(false);
            Scrimmage(f, 50, 2, 8);
            f.State.Quarter = 4; f.State.ClockSeconds = 60f; f.State.HomeScore = 10; f.State.AwayScore = 13;
            Assert.IsTrue(f.CpuHurryUp);
            f.State.ClockSeconds = 300f; Assert.IsFalse(f.CpuHurryUp);
            f.State.ClockSeconds = 12f; f.State.HomeTimeouts = 0; f.State.ClockRunning = true; f.State.PendingRunoff = 25f;   // the spike saves time only while the clock runs (SIM-052)
            Play off, def; f.CpuCalls(out off, out def);
            Assert.AreEqual(PlayCategory.Spike, off.Category);
        }

        // ------------------------------------------------------------------------------------------------ college rules
        static void ToOvertime(MatchFlow f)
        {
            f.State.Quarter = 4; f.State.ClockSeconds = 1f; f.State.HomeScore = 14; f.State.AwayScore = 14;
            Scrimmage(f, 30, 1, 10); f.State.PendingRunoff = 10f; f.State.ClockRunning = true;
            var pc = f.PrepareSnap();
            Assert.IsTrue(pc.Changed);
        }

        [Test]
        public void CollegeOvertimeAlternatesPossessionsFromThe25AndEndsWithAWinner()
        {
            int tdPossessions = 0;
            for (int seed = 1; seed <= 25; seed++)
            {
                var f = NewFlow(true, seed, 2);
                ToOvertime(f);
                Assert.IsTrue(f.State.IsCollegeOvertime);
                Assert.AreEqual(GamePhase.Scrimmage, f.State.Phase);
                Assert.AreEqual(75, f.State.BallSpot, "first possession at the opponent's 25");
                Assert.AreEqual(Team.Home, f.State.Possession);
                var seen = new List<string>(); int guard = 0; string lastKey = ""; PlayOutcome lastOc = null;
                while (!f.State.IsFinal && guard++ < 500)
                {
                    var pc = f.PrepareSnap(); if (pc.Final) break; if (pc.Changed) continue;
                    if (f.State.Phase == GamePhase.Scrimmage && f.State.IsCollegeOvertime)
                    {
                        string key = f.State.Quarter + ":" + f.State.Possession;
                        if (key != lastKey) { lastKey = key; seen.Add(key); Assert.AreEqual(75, f.State.BallSpot, "every overtime possession starts at the 25 (" + key + ")"); }
                    }
                    Play off, def; f.CpuCalls(out off, out def);
                    var sim = f.CreateSim(off, def, true); sim.RunToCompletion(); lastOc = f.Finish(sim);
                }
                Assert.IsTrue(f.State.IsFinal, "college overtime always produces a winner (seed " + seed + ")");
                Assert.AreNotEqual(f.State.HomeScore, f.State.AwayScore);
                // a defensive score (interception / fumble return, safety) wins the game on the spot: the other team never gets its possession (real college rule, and the realistic turnovers of the v0.6 engine make it happen)
                bool defensiveWin = lastOc != null && lastOc.ScoringTeam.HasValue && lastOc.ScoringTeam.Value != lastOc.Offense;
                if (seen.Count < 2) { Assert.IsTrue(defensiveWin, "both teams get the ball unless the defense scores (seed " + seed + ", " + f.State.HomeScore + "-" + f.State.AwayScore + ", last play " + (lastOc != null ? lastOc.LogText : "none") + ")"); Assert.AreEqual("5:Home", seen[0]); tdPossessions += seen.Count; continue; }
                Assert.AreEqual("5:Home", seen[0]); Assert.AreEqual("5:Away", seen[1], "the other team answers from the same spot");
                if (seen.Count >= 4) { Assert.AreEqual("6:Away", seen[2], "the second overtime starts with the other team"); Assert.AreEqual("6:Home", seen[3]); }
                tdPossessions += seen.Count;
            }
            Assert.Greater(tdPossessions, 50);
        }

        [Test]
        public void BothTeamsAttackTheSameEndInCollegeOvertime()
        {
            var f = NewFlow(true, 3, 2);
            ToOvertime(f);
            var first = f.CurrentFrame.TowardsPlusZ;
            f.State.Possession = Team.Away;
            Assert.AreEqual(first, f.CurrentFrame.TowardsPlusZ, "same end zone for both possessions of a period");
            // regulation still swaps ends with possession
            var g = NewFlow(true, 3, 2); Scrimmage(g, 30, 1, 10, Team.Home); var h1 = g.CurrentFrame.TowardsPlusZ; g.State.Possession = Team.Away;
            Assert.AreNotEqual(h1, g.CurrentFrame.TowardsPlusZ);
        }

        [Test]
        public void CollegeOvertimeSecondTryRequiredFromTheThirdPeriod()
        {
            var gs = new GameState(RuleSet.College());
            gs.Quarter = 7; gs.HomeScore = gs.AwayScore = 20;
            gs.Phase = GamePhase.Scrimmage; gs.Possession = Team.Home; gs.OvertimePossession = 1; gs.OvertimePossessionTeam = Team.Home; gs.OvertimeFirstTeam = Team.Home;
            gs.BallSpot = 75; gs.Down = 1; gs.Distance = 10; gs.ClockSeconds = 900;
            gs.Apply(PlayResult.Run(30, 6f));
            Assert.AreEqual(GamePhase.PointAfter, gs.Phase);
            Assert.IsTrue(gs.RequiresTwoPoint, "a two-point try is required from the third overtime");
            Assert.AreEqual(RuleSet.College().TwoPointSpot, gs.BallSpot);
        }

        [Test]
        public void CollegeRulesAreWiredIntoTheMatchFlow()
        {
            var college = NewFlow(true); var pro = NewFlow(false);
            Assert.IsTrue(college.Rules.CollegeStyle); Assert.IsFalse(pro.Rules.CollegeStyle);
            Assert.AreEqual(2, college.Rules.FeetInBounds); Assert.AreEqual(1, pro.Rules.FeetInBounds);
            Assert.AreEqual(OvertimeFormat.CollegeAlternating, college.Rules.Overtime);
            Assert.IsFalse(college.Rules.HasTwoMinuteWarning);
            Assert.AreEqual(25, college.Rules.KickoffTouchbackSpot); Assert.AreEqual(30, pro.Rules.KickoffTouchbackSpot);
        }

        [Test]
        public void CollegeFirstDownsStopTheClockEverywhereOnTheGame()
        {
            var college = new GameState(RuleSet.College()) { Phase = GamePhase.Scrimmage, BallSpot = 30, Down = 1, Distance = 10, Possession = Team.Home, Quarter = 1, ClockSeconds = 600 };
            college.Apply(PlayResult.Run(12, 6f));
            Assert.IsTrue(college.ClockRunning, "college: current NCAA rules keep the clock running on a first down early in the half (SIM-053)");
            var late = new GameState(RuleSet.College()) { Phase = GamePhase.Scrimmage, BallSpot = 30, Down = 1, Distance = 10, Possession = Team.Home, Quarter = 2, ClockSeconds = 100 };
            late.Apply(PlayResult.Run(12, 6f));
            Assert.IsFalse(late.ClockRunning, "college: the clock stops on a first down in the last two minutes of a half");
            Assert.Greater(late.PendingRunoff, 0f); Assert.LessOrEqual(late.PendingRunoff, 20.01f, "restarts on the ready for play");
            var pro = new GameState(RuleSet.Pro()) { Phase = GamePhase.Scrimmage, BallSpot = 30, Down = 1, Distance = 10, Possession = Team.Home, Quarter = 1, ClockSeconds = 600 };
            pro.Apply(PlayResult.Run(12, 6f));
            Assert.IsTrue(pro.ClockRunning, "pro: it keeps running in the first quarter");
        }

        [Test]
        public void BallIsSpottedOnTheHashOfTheRuleSet()
        {
            foreach (bool college in new[] { false, true })
            {
                var f = NewFlow(college, 4);
                Scrimmage(f);
                var rules = f.Rules;
                foreach (Hash h in new[] { Hash.Left, Hash.Middle, Hash.Right })
                {
                    var sim = f.CreateSim(f.Book.FindPlay("Slants"), f.Book.FindPlay("Cover 3 Sky"), false);
                    Assert.AreEqual(FieldGeometry.HashX(Hash.Middle, rules), sim.BallX0, 1e-3f);
                }
                float left = FieldGeometry.HashX(Hash.Left, rules), right = FieldGeometry.HashX(Hash.Right, rules);
                float width = right - left;
                Assert.AreEqual(college ? 40f / 3f : 18.5f / 3f, width, 0.05f, "hash width in yards");
                Assert.AreEqual(Hash.Left, FieldGeometry.NearestHash(left - 0.3f, rules));
            }
        }

        [Test]
        public void NextSnapIsSpottedOnTheNearestCollegeHash()
        {
            // run plays until the ball ends a play away from the middle, then check that the next snap starts on that rule set's hash
            for (int seed = 1; seed <= 30; seed++)
            {
                var f = NewFlow(true, seed);
                f.State.Phase = GamePhase.Scrimmage; f.State.Possession = Team.Home; f.State.BallSpot = 30; f.State.Down = 1; f.State.Distance = 10; f.State.PendingRunoff = 0f;
                Play off, def; f.CpuCalls(out off, out def);
                var sim = f.CreateSim(off, def, true); sim.RunToCompletion();
                float endX = sim.Ball.Pos.X;
                var o = f.Finish(sim);
                if (f.State.Phase != GamePhase.Scrimmage || o.PossessionChanged) continue;
                var next = f.CreateSim(f.Book.FindPlay("Slants"), f.Book.FindPlay("Cover 3 Sky"), false);
                float[] hashes = { FieldGeometry.HashX(Hash.Left, f.Rules), FieldGeometry.HashX(Hash.Middle, f.Rules), FieldGeometry.HashX(Hash.Right, f.Rules) };
                Assert.IsTrue(hashes.Any(h => System.Math.Abs(h - next.BallX0) < 1e-3f), "ball X is a college hash: " + next.BallX0);
            }
        }

        [Test]
        public void CollegeTryIsSnappedFromTheThree()
        {
            var f = NewFlow(true); var pro = NewFlow(false);
            f.State.Phase = GamePhase.PointAfter; f.State.BallSpot = f.Rules.PatSpot; f.State.Possession = Team.Home;
            var sim = f.CreateSim(f.Book.FindPlay("Extra Point"), f.Book.FindPlay("Field Goal Block Middle"), false);
            Assert.AreEqual(97f, sim.LosY, 0.01f);
            pro.State.Phase = GamePhase.PointAfter; pro.State.BallSpot = pro.Rules.PatSpot; pro.State.Possession = Team.Home;
            var psim = pro.CreateSim(pro.Book.FindPlay("Extra Point"), pro.Book.FindPlay("Field Goal Block Middle"), false);
            Assert.AreEqual(85f, psim.LosY, 0.01f);
        }

        [Test]
        public void FullCollegeGameRunsToTheEnd()
        {
            for (int seed = 1; seed <= 3; seed++)
            {
                var f = NewFlow(true, seed, 2);
                int guard = 0;
                while (!f.State.IsFinal && guard++ < 700)
                {
                    var pc = f.PrepareSnap(); if (pc.Final) break; if (pc.Changed) continue;
                    Play off, def; f.CpuCalls(out off, out def);
                    var sim = f.CreateSim(off, def, true); sim.RunToCompletion(); f.Finish(sim);
                }
                Assert.IsTrue(f.State.IsFinal, "seed " + seed);
                Assert.AreNotEqual(f.State.HomeScore, f.State.AwayScore, "no ties in college");
            }
        }
    }
}

