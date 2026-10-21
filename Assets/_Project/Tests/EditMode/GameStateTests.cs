using System;
using NUnit.Framework;
using PixFootball.Core;

namespace PixFootball.Tests
{
    public class GameStateTests
    {
        /// <summary>Pro game after the opening kickoff: Home ball at its own 30, 1st and 10.</summary>
        private static GameState Start(RuleSet rules = null)
        {
            var g = new GameState(rules ?? RuleSet.Pro());
            Assert.IsTrue(g.Apply(PlayResult.Kickoff(70, touchback: true)));
            return g;
        }

        [Test]
        public void OpeningKickoffTouchback()
        {
            var g = Start();
            Assert.AreEqual(GamePhase.Scrimmage, g.Phase);
            Assert.AreEqual(Team.Home, g.Possession);
            Assert.AreEqual(30, g.BallSpot);
            Assert.AreEqual(1, g.Down);
            Assert.AreEqual(10, g.Distance);
            var c = Start(RuleSet.College());
            Assert.AreEqual(25, c.BallSpot);
        }

        [Test]
        public void GainMovesBallAndAdvancesDown()
        {
            var g = Start();
            g.Apply(PlayResult.Run(3));
            Assert.AreEqual(33, g.BallSpot);
            Assert.AreEqual(2, g.Down);
            Assert.AreEqual(7, g.Distance);
            g.Apply(PlayResult.Pass(-2));
            Assert.AreEqual(31, g.BallSpot);
            Assert.AreEqual(3, g.Down);
            Assert.AreEqual(9, g.Distance);
        }

        [Test]
        public void FirstDownConversion()
        {
            var g = Start();
            g.Down = 3; g.Distance = 5;
            g.Apply(PlayResult.Run(6));
            Assert.AreEqual(Team.Home, g.Possession);
            Assert.AreEqual(1, g.Down);
            Assert.AreEqual(10, g.Distance);
            Assert.AreEqual(36, g.BallSpot);
        }

        [Test]
        public void ExactYardsToGoIsAFirstDown()
        {
            var g = Start();
            g.Apply(PlayResult.Pass(10));
            Assert.AreEqual(1, g.Down);
            Assert.AreEqual(40, g.BallSpot);
        }

        [Test]
        public void TurnoverOnDowns()
        {
            var g = Start();
            g.BallSpot = 50; g.Down = 4; g.Distance = 5;
            g.Apply(PlayResult.Run(2));
            Assert.AreEqual(Team.Away, g.Possession);
            Assert.AreEqual(48, g.BallSpot);
            Assert.AreEqual(1, g.Down);
            Assert.AreEqual(10, g.Distance);
        }

        [Test]
        public void IncompleteOnFourthIsTurnoverOnDowns()
        {
            var g = Start();
            g.BallSpot = 40; g.Down = 4; g.Distance = 3;
            g.Apply(PlayResult.Incomplete());
            Assert.AreEqual(Team.Away, g.Possession);
            Assert.AreEqual(60, g.BallSpot);
        }

        [Test]
        public void TouchdownThenPatFlow()
        {
            var g = Start();
            g.BallSpot = 95;
            g.Apply(PlayResult.Run(5));
            Assert.AreEqual(6, g.HomeScore);
            Assert.AreEqual(GamePhase.PointAfter, g.Phase);
            Assert.AreEqual(85, g.BallSpot);
            Assert.IsFalse(g.RequiresTwoPoint);
            g.Apply(PlayResult.Extra(true));
            Assert.AreEqual(7, g.HomeScore);
            Assert.AreEqual(GamePhase.Kickoff, g.Phase);
            Assert.AreEqual(Team.Home, g.Possession); // scoring team kicks
            Assert.AreEqual(35, g.KickSpot);
            g.Apply(PlayResult.Kickoff(62, returnYards: 20));
            Assert.AreEqual(Team.Away, g.Possession);
            Assert.AreEqual(GamePhase.Scrimmage, g.Phase);
            Assert.AreEqual(100 - 97 + 20, g.BallSpot);
        }

        [Test]
        public void MissedPatAndTwoPoint()
        {
            var g = Start();
            g.BallSpot = 95;
            g.Apply(PlayResult.Run(5));
            g.Apply(PlayResult.Extra(false));
            Assert.AreEqual(6, g.HomeScore);

            var h = Start();
            h.BallSpot = 95;
            h.Apply(PlayResult.Run(5));
            h.Apply(PlayResult.TwoPointTry(true));
            Assert.AreEqual(8, h.HomeScore);
            Assert.AreEqual(GamePhase.Kickoff, h.Phase);
        }

        [Test]
        public void CollegePatIsSnappedFromThree()
        {
            var g = Start(RuleSet.College());
            g.BallSpot = 90;
            g.Apply(PlayResult.Run(10));
            Assert.AreEqual(97, g.BallSpot);
        }

        [Test]
        public void ReturnTouchdownOnInterception()
        {
            var g = Start();
            g.Apply(PlayResult.Intercept(10, 55, touchdown: true));
            Assert.AreEqual(6, g.AwayScore);
            Assert.AreEqual(GamePhase.PointAfter, g.Phase);
            Assert.AreEqual(Team.Away, g.Possession);
        }

        [Test]
        public void Safety()
        {
            var g = Start();
            g.BallSpot = 3;
            g.Apply(PlayResult.Sack(5));
            Assert.AreEqual(2, g.AwayScore);
            Assert.AreEqual(GamePhase.Kickoff, g.Phase);
            Assert.AreEqual(Team.Home, g.Possession); // team that conceded kicks
            Assert.IsTrue(g.FreeKick);
            Assert.AreEqual(20, g.KickSpot);
            g.Apply(PlayResult.Kickoff(50, returnYards: 10));
            Assert.AreEqual(Team.Away, g.Possession);
            Assert.AreEqual(100 - 70 + 10, g.BallSpot);
        }

        [Test]
        public void GoalToGoDistance()
        {
            var g = Start();
            g.BallSpot = 82; g.Down = 1; g.Distance = 10;
            g.Apply(PlayResult.Run(10));
            Assert.AreEqual(92, g.BallSpot);
            Assert.AreEqual(1, g.Down);
            Assert.AreEqual(8, g.Distance);
            Assert.IsTrue(g.IsGoalToGo);
            g.Apply(PlayResult.Run(3));
            Assert.AreEqual(2, g.Down);
            Assert.AreEqual(5, g.Distance);
            Assert.IsTrue(g.IsGoalToGo);
        }

        [Test]
        public void InterceptionAndFumbleChangePossessionAtSpot()
        {
            var g = Start();
            g.BallSpot = 50;
            g.Apply(PlayResult.Intercept(10, 5));
            Assert.AreEqual(Team.Away, g.Possession);
            Assert.AreEqual(100 - 60 + 5, g.BallSpot);
            Assert.AreEqual(1, g.Down);

            var f = Start();
            f.BallSpot = 50;
            f.Apply(PlayResult.FumbleLost(-3, 0));
            Assert.AreEqual(Team.Away, f.Possession);
            Assert.AreEqual(53, f.BallSpot);

            var r = Start();
            r.Down = 2; r.Distance = 7;
            r.Apply(PlayResult.FumbleRecovered(2));
            Assert.AreEqual(Team.Home, r.Possession);
            Assert.AreEqual(3, r.Down);
        }

        [Test]
        public void InterceptionInEndZoneIsTouchback()
        {
            var g = Start();
            g.BallSpot = 90;
            g.Apply(PlayResult.Intercept(12, 0));
            Assert.AreEqual(Team.Away, g.Possession);
            Assert.AreEqual(20, g.BallSpot);
        }

        [Test]
        public void PuntFlipsFieldAndReturns()
        {
            var g = Start();
            g.BallSpot = 30; g.Down = 4; g.Distance = 8;
            g.Apply(PlayResult.Punt(45, 10));
            Assert.AreEqual(Team.Away, g.Possession);
            Assert.AreEqual(100 - 75 + 10, g.BallSpot);
            Assert.AreEqual(1, g.Down);

            var t = Start();
            t.BallSpot = 70; t.Down = 4;
            t.Apply(PlayResult.Punt(50));
            Assert.AreEqual(Team.Away, t.Possession);
            Assert.AreEqual(20, t.BallSpot);
        }

        [Test]
        public void FieldGoalMadeAndMissed()
        {
            var g = Start();
            g.BallSpot = 80; g.Down = 4;
            g.Apply(PlayResult.FieldGoal(true));
            Assert.AreEqual(3, g.HomeScore);
            Assert.AreEqual(GamePhase.Kickoff, g.Phase);
            Assert.AreEqual(Team.Home, g.Possession);

            var m = Start();
            m.BallSpot = 80; m.Down = 4;
            m.Apply(PlayResult.FieldGoal(false));
            Assert.AreEqual(Team.Away, m.Possession);
            Assert.AreEqual(100 - 73, m.BallSpot); // pro: spot of the kick (7 yards back)

            var c = Start(RuleSet.College());
            c.BallSpot = 80; c.Down = 4;
            c.Apply(PlayResult.FieldGoal(false));
            Assert.AreEqual(Team.Away, c.Possession);
            Assert.AreEqual(20, c.BallSpot); // college: spot of the snap, not inside the 20
        }

        [Test]
        public void KneelLosesAYard()
        {
            var g = Start();
            g.Apply(PlayResult.KneelDown());
            Assert.AreEqual(29, g.BallSpot);
            Assert.AreEqual(2, g.Down);
        }

        [Test]
        public void SpikeUsesADownAndStopsTheClock()
        {
            var g = Start();
            g.Apply(PlayResult.SpikeBall());
            Assert.AreEqual(2, g.Down);
            Assert.AreEqual(30, g.BallSpot);
            Assert.IsFalse(g.ClockRunning);
            Assert.AreEqual(0f, g.PendingRunoff);
        }

        [Test]
        public void WrongPhasePlayThrows()
        {
            var g = new GameState(RuleSet.Pro());
            Assert.Throws<InvalidOperationException>(() => g.Apply(PlayResult.Run(3)));
        }

        [Test]
        public void CloneIsIndependent()
        {
            var g = Start();
            var c = g.Clone();
            c.Apply(PlayResult.Run(5));
            Assert.AreEqual(30, g.BallSpot);
            Assert.AreEqual(35, c.BallSpot);
        }
    }

    public class ClockAndPeriodTests
    {
        private static GameState Start(RuleSet rules = null)
        {
            var g = new GameState(rules ?? RuleSet.Pro());
            g.Apply(PlayResult.Kickoff(70, touchback: true));
            g.PendingRunoff = 0f; // isolate each test from the kickoff's restart runoff
            return g;
        }

        [Test]
        public void QuarterEndsAndClockResets()
        {
            var g = Start();
            g.ClockSeconds = 3f;
            g.Apply(PlayResult.Run(2, 5f));
            Assert.AreEqual(2, g.Quarter);
            Assert.AreEqual(900f, g.ClockSeconds);
            Assert.AreEqual(GamePhase.Scrimmage, g.Phase);
            Assert.AreEqual(Team.Home, g.Possession);
        }

        [Test]
        public void HalftimeSwapsKickoffAndResetsTimeouts()
        {
            var g = Start();
            g.Quarter = 2; g.ClockSeconds = 3f;
            g.HomeTimeouts = 1; g.AwayTimeouts = 0;
            g.TwoMinuteWarningFirstHalf = true;
            g.Apply(PlayResult.Run(2, 5f));
            Assert.AreEqual(3, g.Quarter);
            Assert.AreEqual(GamePhase.Kickoff, g.Phase);
            Assert.AreEqual(Team.Home, g.Possession); // Away kicked off the game, so Home kicks the 2nd half
            Assert.AreEqual(3, g.HomeTimeouts);
            Assert.AreEqual(3, g.AwayTimeouts);
            g.Apply(PlayResult.Kickoff(70, touchback: true));
            Assert.AreEqual(Team.Away, g.Possession);
        }

        [Test]
        public void PlayRunningOutTheClockWhileScoringStillAllowsPat()
        {
            var g = Start();
            g.Quarter = 2; g.ClockSeconds = 3f; g.BallSpot = 95;
            g.Apply(PlayResult.Run(5, 5f));
            Assert.AreEqual(2, g.Quarter);
            Assert.AreEqual(GamePhase.PointAfter, g.Phase);
            g.Apply(PlayResult.Extra(true));
            Assert.AreEqual(3, g.Quarter);
            Assert.AreEqual(7, g.HomeScore);
            Assert.AreEqual(GamePhase.Kickoff, g.Phase);
            Assert.AreEqual(Team.Home, g.Possession);
        }

        [Test]
        public void RegulationEndsWithWinner()
        {
            var g = Start();
            g.Quarter = 4; g.ClockSeconds = 2f; g.HomeScore = 17; g.AwayScore = 10;
            g.TwoMinuteWarningSecondHalf = true;
            g.Apply(PlayResult.Run(3, 5f));
            Assert.AreEqual(GamePhase.Final, g.Phase);
            Assert.AreEqual(Team.Home, g.Winner);
            Assert.IsFalse(g.Apply(PlayResult.Run(3)));
        }

        [Test]
        public void TieAtEndOfRegulationStartsOvertime()
        {
            var g = Start();
            g.Quarter = 4; g.ClockSeconds = 2f; g.HomeScore = 10; g.AwayScore = 10;
            g.TwoMinuteWarningSecondHalf = true;
            g.Apply(PlayResult.Run(3, 5f));
            Assert.AreEqual(5, g.Quarter);
            Assert.AreEqual(GamePhase.Kickoff, g.Phase);
            Assert.AreEqual(Team.Away, g.Possession); // kicker; Home receives by default
            Assert.AreEqual(600f, g.ClockSeconds);
            Assert.AreEqual(2, g.HomeTimeouts);
        }

        [Test]
        public void ManualOvertimeStart()
        {
            var g = Start();
            g.AutoStartOvertime = false;
            g.Quarter = 4; g.ClockSeconds = 2f; g.HomeScore = 3; g.AwayScore = 3;
            g.TwoMinuteWarningSecondHalf = true;
            g.Apply(PlayResult.Run(3, 5f));
            Assert.AreEqual(GamePhase.Overtime, g.Phase);
            g.StartOvertime(Team.Away);
            Assert.AreEqual(Team.Home, g.Possession); // Home kicks to Away
        }

        [Test]
        public void ProOvertimeTouchdownOnFirstPossessionWins()
        {
            var g = Start();
            g.Quarter = 4; g.ClockSeconds = 1f; g.HomeScore = 10; g.AwayScore = 10;
            g.TwoMinuteWarningSecondHalf = true;
            g.Apply(PlayResult.Run(1, 5f));
            g.Apply(PlayResult.Kickoff(70, touchback: true));
            Assert.AreEqual(Team.Home, g.Possession);
            g.BallSpot = 80;
            g.Apply(PlayResult.Run(20));
            Assert.AreEqual(GamePhase.Final, g.Phase);
            Assert.AreEqual(Team.Home, g.Winner);
            Assert.AreEqual(16, g.HomeScore);
        }

        private static GameState ProOtAfterKickoff()
        {
            var g = Start();
            g.Quarter = 4; g.ClockSeconds = 1f; g.HomeScore = 10; g.AwayScore = 10;
            g.TwoMinuteWarningSecondHalf = true;
            g.Apply(PlayResult.Run(1, 5f));
            g.Apply(PlayResult.Kickoff(70, touchback: true));
            return g;
        }

        [Test]
        public void ProOvertimeFieldGoalGivesOtherTeamTheBall()
        {
            var g = ProOtAfterKickoff();
            g.BallSpot = 80; g.Down = 4;
            g.Apply(PlayResult.FieldGoal(true));
            Assert.AreNotEqual(GamePhase.Final, g.Phase);
            Assert.AreEqual(GamePhase.Kickoff, g.Phase);
            Assert.AreEqual(1, g.OvertimeStage);

            g.Apply(PlayResult.Kickoff(70, touchback: true));
            Assert.AreEqual(Team.Away, g.Possession);
            g.BallSpot = 80; g.Down = 4;
            g.Apply(PlayResult.FieldGoal(true)); // ties it up: sudden death
            Assert.AreEqual(2, g.OvertimeStage);
            Assert.AreNotEqual(GamePhase.Final, g.Phase);
            g.Apply(PlayResult.Kickoff(70, touchback: true));
            Assert.AreEqual(Team.Home, g.Possession);
            g.BallSpot = 70; g.Down = 4;
            g.Apply(PlayResult.FieldGoal(true));
            Assert.AreEqual(GamePhase.Final, g.Phase);
            Assert.AreEqual(Team.Home, g.Winner);
        }

        [Test]
        public void ProOvertimeSecondTeamFailingToScoreLosesAfterFieldGoal()
        {
            var g = ProOtAfterKickoff();
            g.BallSpot = 80; g.Down = 4;
            g.Apply(PlayResult.FieldGoal(true));
            g.Apply(PlayResult.Kickoff(70, touchback: true));
            g.Down = 4; g.Distance = 8; g.BallSpot = 30;
            g.Apply(PlayResult.Punt(40));
            Assert.AreEqual(GamePhase.Final, g.Phase);
            Assert.AreEqual(Team.Home, g.Winner);
        }

        [Test]
        public void ProOvertimeFirstPossessionPuntThenFieldGoalWins()
        {
            var g = ProOtAfterKickoff();
            g.Down = 4; g.Distance = 8; g.BallSpot = 30;
            g.Apply(PlayResult.Punt(40));
            Assert.AreEqual(GamePhase.Scrimmage, g.Phase);
            Assert.AreEqual(1, g.OvertimeStage);
            g.BallSpot = 80; g.Down = 4;
            g.Apply(PlayResult.FieldGoal(true));
            Assert.AreEqual(GamePhase.Final, g.Phase);
            Assert.AreEqual(Team.Away, g.Winner);
        }

        [Test]
        public void ProOvertimeExpiringTiedEndsInTie()
        {
            var g = ProOtAfterKickoff();
            g.ClockSeconds = 3f;
            g.Apply(PlayResult.Run(2, 5f));
            Assert.AreEqual(GamePhase.Final, g.Phase);
            Assert.IsNull(g.Winner);
        }

        [Test]
        public void ProOvertimeContinuesWhenTiesDisallowed()
        {
            var rules = RuleSet.Pro();
            rules.AllowTies = false;
            var g = new GameState(rules);
            g.Apply(PlayResult.Kickoff(70, touchback: true));
            g.Quarter = 4; g.ClockSeconds = 1f; g.TwoMinuteWarningSecondHalf = true;
            g.Apply(PlayResult.Run(1, 5f));
            g.Apply(PlayResult.Kickoff(70, touchback: true));
            g.ClockSeconds = 3f;
            g.Apply(PlayResult.Run(2, 5f));
            Assert.AreEqual(6, g.Quarter);
            Assert.AreEqual(GamePhase.Kickoff, g.Phase);
        }

        private static GameState CollegeOt()
        {
            var g = Start(RuleSet.College());
            g.Quarter = 4; g.ClockSeconds = 1f;
            g.Apply(PlayResult.Run(1, 5f));
            return g;
        }

        [Test]
        public void CollegeOvertimeStartsFromTheTwentyFive()
        {
            var g = CollegeOt();
            Assert.AreEqual(5, g.Quarter);
            Assert.AreEqual(GamePhase.Scrimmage, g.Phase);
            Assert.AreEqual(Team.Home, g.Possession);
            Assert.AreEqual(75, g.BallSpot);
            Assert.AreEqual(1, g.Down);
            Assert.AreEqual(1, g.OvertimePossession);
        }

        [Test]
        public void CollegeOvertimeAlternatesAndTouchdownBeatsStop()
        {
            var g = CollegeOt();
            g.Apply(PlayResult.Run(25));
            Assert.AreEqual(GamePhase.PointAfter, g.Phase);
            Assert.AreEqual(97, g.BallSpot);
            g.Apply(PlayResult.Extra(true));
            Assert.AreEqual(Team.Away, g.Possession);
            Assert.AreEqual(75, g.BallSpot);
            Assert.AreEqual(GamePhase.Scrimmage, g.Phase);
            for (int i = 0; i < 4; i++) g.Apply(PlayResult.Incomplete());
            Assert.AreEqual(GamePhase.Final, g.Phase);
            Assert.AreEqual(Team.Home, g.Winner);
            Assert.AreEqual(7, g.HomeScore);
        }

        [Test]
        public void CollegeOvertimeTiedAfterBothPossessionsStartsNextPeriod()
        {
            var g = CollegeOt();
            g.BallSpot = 80; g.Down = 4; g.Distance = 10;
            g.Apply(PlayResult.FieldGoal(true));
            Assert.AreEqual(Team.Away, g.Possession);
            Assert.AreEqual(75, g.BallSpot);
            g.BallSpot = 80; g.Down = 4; g.Distance = 10;
            g.Apply(PlayResult.FieldGoal(true));
            Assert.AreEqual(GamePhase.Scrimmage, g.Phase);
            Assert.AreEqual(6, g.Quarter);
            Assert.AreEqual(Team.Away, g.Possession); // alternate who goes first
            Assert.AreEqual(75, g.BallSpot);
        }

        [Test]
        public void CollegeOvertimeRequiresTwoPointFromThirdPeriod()
        {
            var g = CollegeOt();
            g.Quarter = 7;
            g.Apply(PlayResult.Run(25));
            Assert.AreEqual(GamePhase.PointAfter, g.Phase);
            Assert.IsTrue(g.RequiresTwoPoint);
            Assert.AreEqual(97, g.BallSpot);
            Assert.Throws<InvalidOperationException>(() => g.Apply(PlayResult.Extra(true)));
            g.Apply(PlayResult.TwoPointTry(true));
            Assert.AreEqual(8, g.HomeScore);
            Assert.AreEqual(Team.Away, g.Possession);
        }

        [Test]
        public void CollegeInterceptionEndsOvertimePossession()
        {
            var g = CollegeOt();
            g.Apply(PlayResult.Intercept(5, 0));
            Assert.AreEqual(Team.Away, g.Possession);
            Assert.AreEqual(75, g.BallSpot);
            Assert.AreEqual(2, g.OvertimePossession);
        }

        [Test]
        public void CollegeClockStopsOnFirstDownButProDoesNot()
        {
            var c = Start(RuleSet.College());
            c.Apply(PlayResult.Run(12, 5f));
            Assert.IsFalse(c.ClockRunning);
            Assert.AreEqual(c.Rules.RestartOnReadyRunoff, c.PendingRunoff);

            var p = Start();
            p.Apply(PlayResult.Run(12, 5f));
            Assert.IsTrue(p.ClockRunning);
            Assert.AreEqual(p.Rules.NormalRunoff, p.PendingRunoff);

            var late = Start();
            late.Quarter = 4; late.ClockSeconds = 100f; late.TwoMinuteWarningSecondHalf = true;
            late.Apply(PlayResult.Run(12, 5f));
            Assert.IsFalse(late.ClockRunning, "pro first downs stop the clock inside the last two minutes");
        }

        [Test]
        public void ClockStopsOnIncompleteAndOutOfBounds()
        {
            var g = Start();
            g.Apply(PlayResult.Incomplete());
            Assert.IsFalse(g.ClockRunning);
            Assert.AreEqual(0f, g.PendingRunoff);
            Assert.AreEqual(25f, g.PlayClock);

            g.Apply(PlayResult.Run(3, 5f, outOfBounds: true));
            Assert.IsFalse(g.ClockRunning);
            g.Apply(PlayResult.Run(2, 5f));
            Assert.IsTrue(g.ClockRunning);
            Assert.AreEqual(40f, g.PlayClock);
        }

        [Test]
        public void OutOfBoundsLateInHalfRestartsOnSnap()
        {
            var g = Start();
            g.Quarter = 2; g.ClockSeconds = 90f; g.TwoMinuteWarningFirstHalf = true;
            g.Apply(PlayResult.Run(3, 5f, outOfBounds: true));
            Assert.AreEqual(0f, g.PendingRunoff);
        }

        [Test]
        public void RunoffBurnsBeforeNextSnap()
        {
            var g = Start();
            g.Apply(PlayResult.Run(3, 6f));
            float afterPlay = g.ClockSeconds;
            Assert.AreEqual(888f, afterPlay, 0.01f);
            g.Apply(PlayResult.Run(3, 6f));
            Assert.AreEqual(afterPlay - 30f - 6f, g.ClockSeconds, 0.01f);
        }

        [Test]
        public void TimeoutCancelsRunoff()
        {
            var g = Start();
            g.Apply(PlayResult.Run(3, 6f));
            Assert.IsTrue(g.CallTimeout(Team.Home));
            Assert.AreEqual(2, g.HomeTimeouts);
            Assert.AreEqual(0f, g.PendingRunoff);
            g.HomeTimeouts = 0;
            Assert.IsFalse(g.CallTimeout(Team.Home));
        }

        [Test]
        public void TwoMinuteWarningStopsClockOncePerHalf()
        {
            var g = Start();
            g.Quarter = 2; g.ClockSeconds = 125f;
            g.Apply(PlayResult.Run(3, 6f));
            Assert.IsTrue(g.TwoMinuteWarningFirstHalf);
            Assert.IsFalse(g.ClockRunning);
            Assert.AreEqual(119f, g.ClockSeconds, 0.01f);
            g.Apply(PlayResult.Run(3, 6f));
            Assert.IsTrue(g.ClockRunning);
        }

        [Test]
        public void TwoMinuteWarningStopsRunoffAtExactlyTwoMinutes()
        {
            var g = Start();
            g.Quarter = 4; g.ClockSeconds = 130f;
            g.Apply(PlayResult.Run(3, 1f));
            Assert.AreEqual(129f, g.ClockSeconds, 0.01f);
            Assert.IsTrue(g.ClockRunning);
            Assert.IsTrue(g.Apply(PlayResult.Run(3, 6f)));
            Assert.AreEqual(120f - 6f, g.ClockSeconds, 0.01f);
            Assert.IsTrue(g.TwoMinuteWarningSecondHalf);
        }

        [Test]
        public void CollegeHasNoTwoMinuteWarning()
        {
            var g = Start(RuleSet.College());
            g.Quarter = 2; g.ClockSeconds = 125f;
            g.Apply(PlayResult.Run(3, 6f));
            Assert.IsFalse(g.TwoMinuteWarningFirstHalf);
            Assert.IsTrue(g.ClockRunning);
        }

        [Test]
        public void RunClockToSnapCanEndThePeriod()
        {
            var g = Start();
            g.Apply(PlayResult.Run(3, 6f));
            g.ClockSeconds = 10f;
            Assert.IsTrue(g.RunClockToSnap());
            Assert.AreEqual(2, g.Quarter);
        }

        [Test]
        public void PlayAfterRunoffExpiresThePeriodIsNotApplied()
        {
            var g = Start();
            g.Apply(PlayResult.Run(3, 6f));
            g.ClockSeconds = 10f;
            int spot = g.BallSpot;
            Assert.IsFalse(g.Apply(PlayResult.Run(5)));
            Assert.AreEqual(spot, g.BallSpot);
            Assert.AreEqual(2, g.Quarter);
        }
    }
}
