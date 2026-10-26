using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.13.0 STANCE-HEAD: the line stances have a hand on the turf, the head limits are tighter than v0.12's, the sole / bone rules of the feet detector.</summary>
    public class StanceHeadV013Tests
    {
        static StanceSpec LineSpec(Position pos, float relX, bool shortYardage = false)
        {
            bool was = StanceRules.ShortYardage; StanceRules.ShortYardage = shortYardage;
            try { return StanceRules.ForBasic(pos, SimSide.Offense, PlayKind.Scrimmage, "Singleback", relX, 0f, false, false); }
            finally { StanceRules.ShortYardage = was; }
        }

        [Test]
        public void EveryOffensiveLineThreePointHasOneHandOnTheTurfAndTheFreeArmOnTheThigh()
        {
            foreach (var spec in new[] { LineSpec(Position.LT, -6f), LineSpec(Position.RT, 6f), LineSpec(Position.LG, -3f), LineSpec(Position.RG, 3f), LineSpec(Position.LT, -6f, true) })
            {
                Assert.AreEqual(StanceKind.ThreePoint, spec.Kind);
                var p = StancePoses.For(spec, PlayerRole.OL);
                bool down = (p.RHand == HandMode.Ground && p.LHand == HandMode.Thigh) || (p.LHand == HandMode.Ground && p.RHand == HandMode.Thigh);
                Assert.IsTrue(down, "one hand on the turf, the other forearm on the thigh (spec Ol " + spec.Ol + ")");
                Assert.LessOrEqual(Mathf.Min(p.RY, p.LY), 0.1f, "the down hand is at the turf");
                Assert.GreaterOrEqual(p.MaxLean, 100f, "the lean may deepen until the shoulder reaches the turf");
            }
        }

        [Test]
        public void TheTackleIsTheMostUprightOfTheLineAndNoOneIsAsLowAsTheCentre()
        {
            var t = StancePoses.For(LineSpec(Position.LT, -6f), PlayerRole.OL);
            var g = StancePoses.For(LineSpec(Position.LG, -3f), PlayerRole.OL);
            var c = StancePoses.For(LineSpec(Position.C, 0f), PlayerRole.OL);
            Assert.Less(t.Lean, g.Lean); Assert.Less(g.Lean, c.Lean);
            Assert.Greater(t.Hips, g.Hips); Assert.Greater(g.Hips, c.Hips);
        }

        [Test]
        public void TheHeadMayTurnLessAndSlowerThanInV012()
        {
            Assert.LessOrEqual(NaturalBody.HeadMax, 55f, "v0.12 allowed 70 degrees (p99 68.6)");
            Assert.LessOrEqual(NaturalBody.HeadRate, 420f, "v0.12 allowed 650 degrees per second");
            for (float a = 0f; a <= 180f; a += 5f)
            {
                float lim = NaturalLook.SoftLimit(a, NaturalBody.HeadMax);
                Assert.LessOrEqual(lim, NaturalBody.HeadMax + 1e-3f, "soft limit of " + a);
                Assert.GreaterOrEqual(lim, Mathf.Min(a, NaturalBody.HeadMax * 0.8f) - 1e-3f, "a small turn is left alone");
                Assert.AreEqual(-lim, NaturalLook.SoftLimit(-a, NaturalBody.HeadMax), 1e-4f, "symmetric");
            }
        }

        [Test]
        public void TheReceiverStanceLookStaysInsideTheHeadLimit()
        {
            foreach (var kind in new[] { StanceKind.ReceiverSplit, StanceKind.ReceiverSlot, StanceKind.Gunner })
            {
                var p = StancePoses.For(new StanceSpec { Kind = kind }, PlayerRole.WR);
                Assert.LessOrEqual(Mathf.Abs(p.HeadYaw), NaturalBody.HeadMax * 0.8f, kind + ": the stance look is a glance, not a cranked neck");
            }
        }
    }
}
