using System;
using NUnit.Framework;
using PixFootball.Core;

namespace PixFootball.Tests
{
    public class PenaltyTests
    {
        private static GameState Start(RuleSet rules = null)
        {
            var g = new GameState(rules ?? RuleSet.Pro());
            g.Apply(PlayResult.Kickoff(70, touchback: true));
            g.PendingRunoff = 0f;
            return g;
        }

        [Test]
        public void CatalogHasAllRequiredPenalties()
        {
            foreach (PenaltyType t in Enum.GetValues(typeof(PenaltyType)))
            {
                Penalty p = Penalty.Get(t);
                Assert.IsNotNull(p);
                Assert.IsFalse(string.IsNullOrEmpty(p.Name));
            }
            Assert.AreEqual(22, Penalty.All.Count);   // v0.9.0 (E-345): + RoughingTheKicker, RunningIntoKicker, KickCatchInterference; v0.12.0 (RULES): + IllegalShift, IllegalMotion, NeutralZoneInfraction
            Assert.IsTrue(Penalty.Get(PenaltyType.FalseStart).PreSnap);
            Assert.IsTrue(Penalty.Get(PenaltyType.IntentionalGrounding).LossOfDown);
            Assert.IsTrue(Penalty.Get(PenaltyType.DefensiveHolding).AutomaticFirstDown);
            Assert.IsTrue(Penalty.Get(PenaltyType.DefensivePassInterference).SpotFoul);
            Assert.IsFalse(Penalty.Get(PenaltyType.Offside).AutomaticFirstDown);
        }

        [Test]
        public void FalseStartIsFiveYardsAndReplaysTheDown()
        {
            var g = Start();
            g.BallSpot = 40; g.Down = 2; g.Distance = 6;
            g.Apply(PlayResult.PenaltyOnly(PenaltyCall.For(PenaltyType.FalseStart, g)));
            Assert.AreEqual(35, g.BallSpot);
            Assert.AreEqual(2, g.Down);
            Assert.AreEqual(11, g.Distance);
            Assert.IsFalse(g.ClockRunning);
        }

        [Test]
        public void TheV012PreSnapFoulsAreFiveYardsAndReplayTheDown()
        {
            foreach (var t in new[] { PenaltyType.IllegalShift, PenaltyType.IllegalMotion, PenaltyType.NeutralZoneInfraction })
            {
                Assert.IsTrue(Penalty.Get(t).PreSnap, t.ToString());
                var g = Start();
                g.BallSpot = 40; g.Down = 2; g.Distance = 6;
                g.Apply(PlayResult.PenaltyOnly(PenaltyCall.For(t, g)));
                bool offense = Penalty.Get(t).Side == PenaltySide.Offense;
                Assert.AreEqual(offense ? 35 : 45, g.BallSpot, t.ToString());
                Assert.AreEqual(2, g.Down, t.ToString());
                Assert.AreEqual(offense ? 11 : 1, g.Distance, t.ToString());
            }
        }

        [Test]
        public void OffensiveHoldingTenYards()
        {
            var g = Start();
            g.BallSpot = 40; g.Down = 2; g.Distance = 5;
            g.Apply(PlayResult.PenaltyOnly(PenaltyCall.For(PenaltyType.OffensiveHolding, g)));
            Assert.AreEqual(30, g.BallSpot);
            Assert.AreEqual(2, g.Down);
            Assert.AreEqual(15, g.Distance);
        }

        [Test]
        public void DefensiveHoldingIsAutomaticFirstDown()
        {
            var g = Start();
            g.BallSpot = 40; g.Down = 3; g.Distance = 12;
            g.Apply(PlayResult.PenaltyOnly(PenaltyCall.For(PenaltyType.DefensiveHolding, g)));
            Assert.AreEqual(45, g.BallSpot);
            Assert.AreEqual(1, g.Down);
            Assert.AreEqual(10, g.Distance);

            var c = Start(RuleSet.College());
            c.BallSpot = 40; c.Down = 3; c.Distance = 12;
            c.Apply(PlayResult.PenaltyOnly(PenaltyCall.For(PenaltyType.DefensiveHolding, c)));
            Assert.AreEqual(50, c.BallSpot);
            Assert.AreEqual(1, c.Down);
        }

        [Test]
        public void OffsideDoesNotGiveAFirstDownUnlessYardageDoes()
        {
            var g = Start();
            g.BallSpot = 40; g.Down = 3; g.Distance = 8;
            g.Apply(PlayResult.PenaltyOnly(PenaltyCall.For(PenaltyType.Offside, g)));
            Assert.AreEqual(45, g.BallSpot);
            Assert.AreEqual(3, g.Down);
            Assert.AreEqual(3, g.Distance);

            var h = Start();
            h.BallSpot = 40; h.Down = 3; h.Distance = 4;
            h.Apply(PlayResult.PenaltyOnly(PenaltyCall.For(PenaltyType.Offside, h)));
            Assert.AreEqual(1, h.Down);
            Assert.AreEqual(10, h.Distance);
        }

        [Test]
        public void HalfTheDistanceToTheGoalAgainstOffense()
        {
            var g = Start();
            g.BallSpot = 8; g.Down = 1; g.Distance = 10;
            EnforcementResult r = PenaltyEnforcer.Enforce(g, PenaltyCall.For(PenaltyType.OffensiveHolding, g));
            Assert.IsTrue(r.HalfDistance);
            Assert.AreEqual(4, g.BallSpot);
            Assert.AreEqual(-4, r.Yards);
            Assert.AreEqual(14, g.Distance);
        }

        [Test]
        public void HalfTheDistanceToTheGoalAgainstDefense()
        {
            var g = Start();
            g.BallSpot = 95; g.Down = 1; g.Distance = 5;
            EnforcementResult r = PenaltyEnforcer.Enforce(g, PenaltyCall.For(PenaltyType.Offside, g));
            Assert.IsTrue(r.HalfDistance);
            Assert.AreEqual(97, g.BallSpot);
            Assert.AreEqual(1, g.Down);
            Assert.AreEqual(3, g.Distance);
        }

        [Test]
        public void PersonalFoulOnDefenseGivesFifteenAndFirstDown()
        {
            var g = Start();
            g.BallSpot = 20; g.Down = 3; g.Distance = 15;
            g.Apply(PlayResult.PenaltyOnly(PenaltyCall.For(PenaltyType.RoughingThePasser, g)));
            Assert.AreEqual(35, g.BallSpot);
            Assert.AreEqual(1, g.Down);
        }

        [Test]
        public void FaceMaskOnOffenseIsNotAnAutomaticFirstDown()
        {
            var g = Start();
            g.BallSpot = 50; g.Down = 2; g.Distance = 4;
            g.Apply(PlayResult.PenaltyOnly(new PenaltyCall(PenaltyType.FaceMask, Team.Home)));
            Assert.AreEqual(35, g.BallSpot);
            Assert.AreEqual(2, g.Down);
            Assert.AreEqual(19, g.Distance);
        }

        [Test]
        public void IntentionalGroundingLosesDownAndTenYards()
        {
            var g = Start();
            g.BallSpot = 40; g.Down = 2; g.Distance = 10;
            g.Apply(PlayResult.Incomplete().WithPenalty(PenaltyCall.For(PenaltyType.IntentionalGrounding, g, -3f), PenaltyDecision.Accept));
            Assert.AreEqual(30, g.BallSpot);
            Assert.AreEqual(3, g.Down);
            Assert.AreEqual(20, g.Distance);
        }

        [Test]
        public void IntentionalGroundingOnFourthIsTurnoverOnDowns()
        {
            var g = Start();
            g.BallSpot = 40; g.Down = 4; g.Distance = 10;
            g.Apply(PlayResult.Incomplete().WithPenalty(PenaltyCall.For(PenaltyType.IntentionalGrounding, g, -3f), PenaltyDecision.Accept));
            Assert.AreEqual(Team.Away, g.Possession);
            Assert.AreEqual(70, g.BallSpot);
        }

        [Test]
        public void DefensivePassInterferenceProIsSpotFoul()
        {
            var g = Start();
            g.BallSpot = 30; g.Down = 3; g.Distance = 8;
            EnforcementResult r = PenaltyEnforcer.Enforce(g, PenaltyCall.For(PenaltyType.DefensivePassInterference, g, 27f));
            Assert.AreEqual(57, g.BallSpot);
            Assert.AreEqual(27, r.Yards);
            Assert.AreEqual(1, g.Down);
        }

        [Test]
        public void DefensivePassInterferenceCollegeCapsAtFifteen()
        {
            var g = Start(RuleSet.College());
            g.BallSpot = 30; g.Down = 3; g.Distance = 8;
            PenaltyEnforcer.Enforce(g, PenaltyCall.For(PenaltyType.DefensivePassInterference, g, 27f));
            Assert.AreEqual(45, g.BallSpot);
            Assert.AreEqual(1, g.Down);
        }

        [Test]
        public void DefensivePassInterferenceStopsAtTheOne()
        {
            var g = Start();
            g.BallSpot = 70; g.Down = 2; g.Distance = 10;
            PenaltyEnforcer.Enforce(g, PenaltyCall.For(PenaltyType.DefensivePassInterference, g, 45f));
            Assert.AreEqual(99, g.BallSpot);
            Assert.AreEqual(1, g.Distance);
        }

        [Test]
        public void OffensivePassInterferenceYardsByRuleSet()
        {
            var g = Start();
            g.BallSpot = 50;
            PenaltyEnforcer.Enforce(g, PenaltyCall.For(PenaltyType.OffensivePassInterference, g));
            Assert.AreEqual(40, g.BallSpot);
            var c = Start(RuleSet.College());
            c.BallSpot = 50;
            PenaltyEnforcer.Enforce(c, PenaltyCall.For(PenaltyType.OffensivePassInterference, c));
            Assert.AreEqual(35, c.BallSpot);
        }

        [Test]
        public void IllegalBlockInBackIsEnforcedFromTheSpotOfTheFoul()
        {
            var g = Start();
            g.BallSpot = 40;
            PenaltyEnforcer.Enforce(g, PenaltyCall.For(PenaltyType.IllegalBlockInBack, g, 12f));
            Assert.AreEqual(42, g.BallSpot);
        }

        [Test]
        public void DefenseAcceptsWhenPenaltyIsBetterThanThePlay()
        {
            var g = Start();
            g.BallSpot = 40; g.Down = 3; g.Distance = 5;
            PlayResult play = PlayResult.Run(3).WithPenalty(PenaltyCall.For(PenaltyType.DefensiveHolding, g));
            Assert.IsTrue(PenaltyEnforcer.ShouldAccept(g, play));
            g.Apply(play);
            Assert.AreEqual(45, g.BallSpot);
            Assert.AreEqual(1, g.Down);
        }

        [Test]
        public void DefenseAcceptsOffensiveHoldingThatWipesOutABigGain()
        {
            var g = Start();
            g.BallSpot = 40; g.Down = 2; g.Distance = 8;
            PlayResult play = PlayResult.Pass(20).WithPenalty(PenaltyCall.For(PenaltyType.OffensiveHolding, g));
            Assert.IsTrue(PenaltyEnforcer.ShouldAccept(g, play));
            g.Apply(play);
            Assert.AreEqual(30, g.BallSpot);
            Assert.AreEqual(2, g.Down);
            Assert.AreEqual(18, g.Distance);
        }

        [Test]
        public void DefenseDeclinesOffensiveHoldingOnASack()
        {
            var g = Start();
            g.BallSpot = 40; g.Down = 3; g.Distance = 8;
            PlayResult play = PlayResult.Sack(8).WithPenalty(PenaltyCall.For(PenaltyType.OffensiveHolding, g));
            Assert.IsFalse(PenaltyEnforcer.ShouldAccept(g, play));
            g.Apply(play);
            Assert.AreEqual(32, g.BallSpot);
            Assert.AreEqual(4, g.Down);
        }

        [Test]
        public void DefenseAcceptsOffensiveHoldingOnATouchdown()
        {
            var g = Start();
            g.BallSpot = 60; g.Down = 1; g.Distance = 10;
            PlayResult play = PlayResult.Run(40).WithPenalty(PenaltyCall.For(PenaltyType.OffensiveHolding, g));
            Assert.IsTrue(PenaltyEnforcer.ShouldAccept(g, play));
            g.Apply(play);
            Assert.AreEqual(0, g.HomeScore);
            Assert.AreEqual(50, g.BallSpot);
            Assert.AreEqual(1, g.Down);
        }

        [Test]
        public void DefenseKeepsAnInterceptionOverAnOffensiveFoul()
        {
            var g = Start();
            g.BallSpot = 40;
            PlayResult play = PlayResult.Intercept(12, 3).WithPenalty(PenaltyCall.For(PenaltyType.OffensiveHolding, g));
            Assert.IsFalse(PenaltyEnforcer.ShouldAccept(g, play));
            g.Apply(play);
            Assert.AreEqual(Team.Away, g.Possession);
        }

        [Test]
        public void OffenseAcceptsDefensiveFoulOnInterception()
        {
            var g = Start();
            g.BallSpot = 40;
            PlayResult play = PlayResult.Intercept(12, 3).WithPenalty(PenaltyCall.For(PenaltyType.DefensiveHolding, g));
            Assert.IsTrue(PenaltyEnforcer.ShouldAccept(g, play));
            g.Apply(play);
            Assert.AreEqual(Team.Home, g.Possession);
            Assert.AreEqual(1, g.Down);
        }

        [Test]
        public void DeclinedPenaltyLetsPlayStand()
        {
            var g = Start();
            g.BallSpot = 40;
            g.Apply(PlayResult.Run(5).WithPenalty(PenaltyCall.For(PenaltyType.DefensiveHolding, g), PenaltyDecision.Decline));
            Assert.AreEqual(45, g.BallSpot);
            Assert.AreEqual(2, g.Down);
        }

        [Test]
        public void ApplyBestOptionReportsDecision()
        {
            var g = Start();
            g.BallSpot = 40; g.Down = 3; g.Distance = 5;
            bool accepted = PenaltyEnforcer.ApplyBestOption(g, PlayResult.Incomplete().WithPenalty(PenaltyCall.For(PenaltyType.IllegalContact, g)));
            Assert.IsTrue(accepted);
            Assert.AreEqual(1, g.Down);
        }

        [Test]
        public void OffsettingPenaltiesReplayTheDown()
        {
            var g = Start();
            var a = new PenaltyCall(PenaltyType.OffensiveHolding, Team.Home);
            var b = new PenaltyCall(PenaltyType.DefensiveHolding, Team.Away);
            EnforcementResult r = PenaltyEnforcer.Offset(g, a, b);
            Assert.IsTrue(r.Offsetting);
            Assert.AreEqual(30, g.BallSpot);
        }

        [Test]
        public void AcceptedDefensiveFoulOnLastPlayGivesUntimedDown()
        {
            var g = Start();
            g.Quarter = 2; g.ClockSeconds = 4f; g.TwoMinuteWarningFirstHalf = true;
            g.BallSpot = 40; g.Down = 2; g.Distance = 8;
            g.Apply(PlayResult.Incomplete(5f).WithPenalty(PenaltyCall.For(PenaltyType.DefensivePassInterference, g, 15f)));
            Assert.AreEqual(2, g.Quarter);
            Assert.IsTrue(g.UntimedDown);
            g.Apply(PlayResult.Run(3, 5f));
            Assert.AreEqual(3, g.Quarter);
        }

        [Test]
        public void KickoffPenaltyMovesTheKickSpot()
        {
            var g = new GameState(RuleSet.Pro());
            g.Apply(PlayResult.PenaltyOnly(new PenaltyCall(PenaltyType.Offside, Team.Away)));
            Assert.AreEqual(30, g.KickSpot);
        }

        [Test]
        public void PenaltyCallForEitherSideRequiresExplicitTeam()
        {
            var g = Start();
            Assert.Throws<ArgumentException>(() => PenaltyCall.For(PenaltyType.FaceMask, g));
        }
    }
}
