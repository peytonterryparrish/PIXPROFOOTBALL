using NUnit.Framework;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.9.0 LOCOMOTION (LOCO-007): the pure math of the gait synthesizer and the library container.</summary>
    public class LocomotionGaitTests
    {
        static readonly float[] Ladder = { 1.7f, 2.8f, 4.1f, 5.0f, 7.5f, 8.0f };

        [Test]
        public void BracketPicksNeighboursAndNeverJumps()
        {
            int lo; float w;
            GaitMath.Bracket(Ladder, 0.5f, out lo, out w); Assert.AreEqual(0, lo); Assert.AreEqual(0f, w);
            GaitMath.Bracket(Ladder, 9f, out lo, out w); Assert.AreEqual(5, lo); Assert.AreEqual(0f, w);
            float prev = 0f; int prevLo = 0;
            for (float v = 1.7f; v <= 8f; v += 0.01f)
            {
                GaitMath.Bracket(Ladder, v, out lo, out w);
                Assert.That(w, Is.InRange(0f, 1f));
                // continuous: the pair (lower, weight) moves smoothly (a handover from one pair to the next happens at weight 1 -> 0)
                float pos = lo + w; Assert.GreaterOrEqual(pos + 1e-4f, prev); Assert.LessOrEqual(pos - prev, 0.2f); prev = pos; prevLo = lo;
            }
            Assert.AreEqual(4, prevLo);
        }

        [Test]
        public void OneCyclePlaysAloneInsideItsRateBand()
        {
            int lo; float w;
            var ladder = new[] { 2f, 3f, 4.5f, 7f };      // neighbours at least 1.27 apart: the rate bands do not overlap
            for (int i = 0; i < ladder.Length; i++)
                foreach (float f in new[] { 0.92f, 1f, 1.08f })
                {
                    float v = ladder[i] * f;
                    GaitMath.Bracket(ladder, v, out lo, out w);
                    int solo = w < 0.5f ? lo : lo + 1; float weight = w < 0.5f ? 1f - w : w;
                    Assert.AreEqual(i, solo, "speed " + v); Assert.Greater(weight, 0.98f, "speed " + v);
                }
        }

        [Test]
        public void PhaseMapIsIdentityForOneCycleAndMonotonic()
        {
            for (float p = 0f; p < 1f; p += 0.01f) Assert.AreEqual(p, GaitMath.MapPhase(p, 0.3f, 0.5f, 0.8f, 0.3f, 0.5f, 0.8f), 1e-4f);
            float prev = -1f;
            for (float p = 0f; p < 1f; p += 0.005f)
            {
                float m = GaitMath.MapPhase(p, 0.3f, 0.5f, 0.8f, 0.2f, 0.6f, 0.9f);
                Assert.GreaterOrEqual(m, prev - 1e-5f); prev = m;
            }
            // the events land on each other: shared left lift-off (0.3) is this cycle's 0.2, right plant (0.5) its 0.6, right lift-off (0.8) its 0.9
            Assert.AreEqual(0.2f, GaitMath.MapPhase(0.3f, 0.3f, 0.5f, 0.8f, 0.2f, 0.6f, 0.9f), 1e-4f);
            Assert.AreEqual(0.6f, GaitMath.MapPhase(0.5f, 0.3f, 0.5f, 0.8f, 0.2f, 0.6f, 0.9f), 1e-4f);
            Assert.AreEqual(0.9f, GaitMath.MapPhase(0.8f, 0.3f, 0.5f, 0.8f, 0.2f, 0.6f, 0.9f), 1e-4f);
        }

        [Test]
        public void RateStaysInsideTheBandInsideTheLadderAndWarpTakesTheRest()
        {
            float rate, warp;
            GaitMath.RateAndWarp(5.0f, 4.0f, false, false, out rate, out warp);
            Assert.AreEqual(1.12f, rate, 1e-4f); Assert.AreEqual(5.0f / 4.0f / 1.12f, warp, 1e-3f);
            GaitMath.RateAndWarp(5.0f, 5.2f, false, false, out rate, out warp);
            Assert.AreEqual(5.0f / 5.2f, rate, 1e-4f); Assert.AreEqual(1f, warp, 1e-4f);
            // beyond the fastest cycle the band widens (nobody to hand over to)
            GaitMath.RateAndWarp(9.6f, 8.0f, false, true, out rate, out warp);
            Assert.AreEqual(1.2f, rate, 1e-4f); Assert.AreEqual(1f, warp, 1e-3f);
        }

        [Test]
        public void SpeedAgnostic_NoPaceConstantsInTheGaitMath()
        {
            // the same speed ratio gives the same weights whatever the absolute pace is
            int lo1, lo2; float w1, w2;
            GaitMath.Bracket(new[] { 2f, 4f, 6f }, 4.7f, out lo1, out w1);
            GaitMath.Bracket(new[] { 2f * 0.8f, 4f * 0.8f, 6f * 0.8f }, 4.7f * 0.8f, out lo2, out w2);
            Assert.AreEqual(lo1, lo2); Assert.AreEqual(w1, w2, 1e-4f);
        }

        [Test]
        public void LibraryAngleIsFromTheDirectionVector()
        {
            var c = new LocoCycleLibrary.Cycle { dir = new Vector2(-0.4f, 0.9165f) };
            Assert.AreEqual(-23.6f, c.Angle, 0.3f);
            Assert.AreEqual(0f, new LocoCycleLibrary.Cycle().Angle, 1e-4f);
        }

        [Test]
        public void ArmSwingGrowsWithSpeedFromJogToSprint()
        {
            Assert.AreEqual(0.9f, BodyMotion.ArmSwingFactor(1f), 1e-4f);
            Assert.AreEqual(1.25f, BodyMotion.ArmSwingFactor(9f), 1e-4f);
            float prev = 0f;
            for (float v = 0f; v <= 10f; v += 0.25f) { float f = BodyMotion.ArmSwingFactor(v); Assert.GreaterOrEqual(f, prev - 1e-6f); prev = f; }
        }

        [Test]
        public void BrakingLeansTheChestBackFurtherThanBefore()
        {
            Assert.AreEqual(9f, LeanMath.PitchDegrees(40f, 5f), 1e-4f);
            Assert.AreEqual(-11.7f, LeanMath.PitchDegrees(-40f, 5f), 1e-3f);
            Assert.AreEqual(-4.5f, LeanMath.PitchDegrees(-5f, 5f), 1e-3f);
        }

        [Test]
        public void ActionGroundSparesGenuineJumpsAndEnforcesEverythingElse()
        {
            foreach (var a in new[] { PixFootball.Sim.AgentAction.Jump, PixFootball.Sim.AgentAction.Dive, PixFootball.Sim.AgentAction.Hurdle, PixFootball.Sim.AgentAction.Tackled, PixFootball.Sim.AgentAction.Fallen })
                Assert.IsTrue(SimAgentView.AirborneAction(a), a.ToString());
            foreach (var a in new[] { PixFootball.Sim.AgentAction.Throw, PixFootball.Sim.AgentAction.Handoff, PixFootball.Sim.AgentAction.Catch, PixFootball.Sim.AgentAction.Kick, PixFootball.Sim.AgentAction.Celebrate, PixFootball.Sim.AgentAction.Block, PixFootball.Sim.AgentAction.Idle, PixFootball.Sim.AgentAction.Stance })
                Assert.IsFalse(SimAgentView.AirborneAction(a), a.ToString());
            Assert.IsTrue(SimAgentView.AirborneClip("CatchNormalFrontCJump"));
            Assert.IsTrue(SimAgentView.AirborneClip("pixcelebrate_leap_01"));
            Assert.IsFalse(SimAgentView.AirborneClip("kick_fg_yan_02"));
            Assert.IsFalse(SimAgentView.AirborneClip(""));
        }

        // ---------------------------------------------------------------------------------------- round 3: leg quality (LOCO-016)
        [Test]
        public void FootYawComesFromTheLateralAxisAndSurvivesASteeplyPitchedFoot()
        {
            foreach (float pitch in new[] { 0f, 30f, 60f, 85f })
                foreach (float yaw in new[] { -25f, 0f, 12f, 40f })
                {
                    float got;
                    Assert.IsTrue(FootPlant.YawOf(Quaternion.Euler(pitch, yaw, 0f), Vector3.right, Vector3.forward, out got), "pitch " + pitch);
                    Assert.AreEqual(yaw, got, 0.05f, "pitch " + pitch + " yaw " + yaw);
                }
            float y;
            Assert.IsFalse(FootPlant.YawOf(Quaternion.Euler(0f, 0f, 85f), Vector3.right, Vector3.forward, out y));      // rolled onto its side: no heading
        }

        [Test]
        public void FootRollIsTheTwistAboutTheLongAxisAndIgnoresPitchAndYaw()
        {
            float roll; Vector3 fwd;
            Assert.IsTrue(FootPlant.RollOf(Quaternion.Euler(25f, 33f, 0f), Vector3.right, Vector3.forward, out roll, out fwd));
            Assert.AreEqual(0f, roll, 0.05f);
            Assert.IsTrue(FootPlant.RollOf(Quaternion.Euler(25f, 33f, 12f), Vector3.right, Vector3.forward, out roll, out fwd));
            Assert.AreEqual(12f, Mathf.Abs(roll), 0.5f);
            Assert.IsFalse(FootPlant.RollOf(Quaternion.Euler(80f, 0f, 0f), Vector3.right, Vector3.forward, out roll, out fwd));    // toe straight down: roll is meaningless
        }

        [Test]
        public void TwistIsTheRotationAboutTheBoneAxisAndIgnoresFlexion()
        {
            Vector3 axis = Vector3.down;                 // a shin hangs down in its own frame
            Quaternion parent = Quaternion.Euler(10f, 40f, 5f);
            Assert.AreEqual(0f, FootPlant.TwistOf(parent, parent, axis, Quaternion.identity), 0.01f);
            Quaternion twist = Quaternion.AngleAxis(33f, axis);
            Assert.AreEqual(33f, FootPlant.TwistOf(parent, parent * twist, axis, Quaternion.identity), 0.05f);
            Quaternion flex = Quaternion.AngleAxis(70f, Vector3.right);       // knee bend: a rotation about an axis across the bone
            Assert.AreEqual(0f, FootPlant.TwistOf(parent, parent * flex, axis, Quaternion.identity), 0.05f);
            Assert.AreEqual(-21f, FootPlant.TwistOf(parent, parent * flex * Quaternion.AngleAxis(-21f, axis), axis, Quaternion.identity), 0.05f);
            Quaternion bind = Quaternion.Euler(0f, 0f, 12f);                   // a bone that rests rotated against its parent: twist is measured from there
            Assert.AreEqual(0f, FootPlant.TwistOf(parent, parent * bind, axis, bind), 0.05f);
        }
    }
}
