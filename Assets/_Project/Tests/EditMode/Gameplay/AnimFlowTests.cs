using NUnit.Framework;
using UnityEngine;
using PixFootball.Art;
using PixFootball.Gameplay;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>v0.7.0 Track A: foot toe-out clamp (A-001), the commit-window priorities and the new animation keys.</summary>
    public class AnimFlowTests
    {
        [TestCase(0f, true, 0f)]
        [TestCase(10f, false, 0f)]            // right foot toe-out 10 degrees: inside the clamp
        [TestCase(60f, false, -47f)]          // right foot splayed 60 degrees: pulled back to 13
        [TestCase(-60f, true, 47f)]           // left foot splayed 60 degrees (negative angle = to the left): pulled back to 13
        [TestCase(-30f, false, 23f)]          // right foot toe-in 30 degrees: pulled back to 7
        [TestCase(30f, true, -23f)]           // left foot toe-in 30 degrees
        public void ToeCorrectionKeepsTheFootNearTheLineOfTravel(float angle, bool left, float expected)
        {
            Assert.AreEqual(expected, BodyMotion.ToeCorrection(angle, left), 0.01f);
        }

        [Test]
        public void ToeCorrectionNeverMakesAnInsideFootWorse()
        {
            for (float a = -90f; a <= 90f; a += 5f)
                foreach (bool left in new[] { true, false })
                {
                    float c = BodyMotion.ToeCorrection(a, left);
                    float outward = left ? -a : a, after = outward + (left ? -c : c);
                    Assert.LessOrEqual(after, BodyMotion.MaxToeOut + 0.01f);
                    Assert.GreaterOrEqual(after, -BodyMotion.MaxToeIn - 0.01f);
                    if (outward >= -BodyMotion.MaxToeIn && outward <= BodyMotion.MaxToeOut) Assert.AreEqual(0f, c, 0.001f);
                }
        }

        [Test]
        public void CommittedActionsOutrankTheLocomotionThatFollowsThem()
        {
            foreach (var a in new[] { AgentAction.Throw, AgentAction.Catch, AgentAction.Handoff })
            {
                Assert.Greater(SimAgentView.Priority(a), SimAgentView.Priority(AgentAction.Run));
                Assert.Greater(SimAgentView.Priority(a), SimAgentView.Priority(AgentAction.Idle));
                Assert.Greater(SimAgentView.Priority(a), SimAgentView.Priority(AgentAction.Engaged), "a block engagement does not cut a throw");
                Assert.Less(SimAgentView.Priority(a), SimAgentView.Priority(AgentAction.Tackled), "a tackle always interrupts");
                Assert.Less(SimAgentView.Priority(a), SimAgentView.Priority(AgentAction.Fallen));
            }
            Assert.Greater(SimAgentView.Priority(AgentAction.Celebrate), SimAgentView.Priority(AgentAction.Throw));
            Assert.Less(SimAgentView.Priority(AgentAction.Stance), SimAgentView.Priority(AgentAction.Idle));
        }

        [Test]
        public void StrideTrimIsContinuousAndOnlyTouchesPlainForwardRunning()
        {
            float prev = LocomotionAnimator.StrideTrim(0f, false, false);
            for (float v = 0.1f; v <= 9f; v += 0.1f)
            {
                float t = LocomotionAnimator.StrideTrim(v, false, false);
                Assert.LessOrEqual(Mathf.Abs(t - prev), 0.03f, "no jump in the playback rate at " + v);
                Assert.That(t, Is.InRange(1f, 1.25f));
                prev = t;
            }
            Assert.AreEqual(1f, LocomotionAnimator.StrideTrim(5f, true, false), "ball carriers are already matched");
            Assert.AreEqual(1f, LocomotionAnimator.StrideTrim(5f, false, true), "back-pedal is not scaled");
            Assert.Greater(LocomotionAnimator.StrideTrim(5f, false, false), 1.1f);
        }

        [Test]
        public void NewAnimationKeysAreConstantsOfTheArtKeys()
        {
            Assert.AreEqual("pixqb_throw_set", ArtKeys.PixQbThrowSet);
            Assert.AreEqual("pixqb_throw_run", ArtKeys.PixQbThrowRun);
            Assert.AreEqual("pixqb_throw_away", ArtKeys.PixQbThrowAway);
            Assert.AreEqual("pixstance_wr", ArtKeys.PixStanceWr);
            Assert.AreEqual("pixstance_qb_under", ArtKeys.PixStanceQbUnder);
        }

        [Test]
        public void StanceKeyListsStartWithTheDerivedStances()
        {
            Assert.AreEqual(ArtKeys.PixStanceWr, StanceRules.ClipKeys(StanceKind.ReceiverSplit, PlayerRole.WR)[0]);
            Assert.AreEqual(ArtKeys.PixStanceQbUnder, StanceRules.ClipKeys(StanceKind.QbUnderCenter, PlayerRole.QB)[0]);
            Assert.AreEqual(ArtKeys.PixStanceCbPress, StanceRules.ClipKeys(StanceKind.CornerPress, PlayerRole.DB)[0]);
            Assert.AreEqual(ArtKeys.PixStanceCbOff, StanceRules.ClipKeys(StanceKind.CornerOff, PlayerRole.DB)[0]);
            Assert.AreEqual(ArtKeys.PixStanceRb, StanceRules.ClipKeys(StanceKind.BackTwoPoint, PlayerRole.RB)[0]);
        }
    }
}
