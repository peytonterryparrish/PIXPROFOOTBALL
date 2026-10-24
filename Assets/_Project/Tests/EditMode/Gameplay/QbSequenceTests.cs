using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.10.0 QB: the choreographed sequence (poses, planner, timing table, kick geometry).</summary>
    public class QbSequenceTests
    {
        [Test]
        public void CrouchEndsExactlyOnTheUnderCenterStance()
        {
            var end = StancePoses.QbCrouch(1f, false);
            var stance = StancePoses.QbUcDeep(StancePoses.For(new StanceSpec { Kind = StanceKind.QbUnderCenter }, PlayerRole.QB));      // v0.11.0 QB (B-061): the deeper coaching-card stance
            Assert.AreEqual(stance.Hips, end.Hips, 1e-4f); Assert.AreEqual(stance.Lean, end.Lean, 1e-4f);
            Assert.AreEqual(stance.RHand, end.RHand); Assert.AreEqual(stance.RX, end.RX, 1e-4f);
            // and starts upright: no pop into the crouch
            var start = StancePoses.QbCrouch(0f, false);
            Assert.AreEqual(StancePoses.QbUpright().Hips, start.Hips, 1e-4f);
        }

        [Test]
        public void SurveyPointsOnlyInTheMiddleOfItsWindow()
        {
            float pw; StancePoses.QbSurvey(0.05f, 1.9f, out pw); Assert.AreEqual(0f, pw, 1e-3f);
            StancePoses.QbSurvey(1.9f * 0.75f, 1.9f, out pw); Assert.Greater(pw, 0.95f);
            StancePoses.QbSurvey(1.88f, 1.9f, out pw); Assert.Less(pw, 0.05f);
        }

        [Test]
        public void SecureStartsOnTheCrouchHandsAndEndsAtTheBelly()
        {
            var a = StancePoses.QbSecure(0f); var b = StancePoses.QbSecure(1f);
            Assert.Greater(a.Hips, 0.70f); Assert.Less(a.Hips, b.Hips + 1e-4f);      // v0.11.0 QB (B-061): the crouch is deeper (hips 0.74)
            Assert.Greater(a.RPos.z, b.RPos.z, "the ball comes in toward the body");
        }

        [Test]
        public void TimingTableHasTheWindowsTheSimulationPlansAround()
        {
            Assert.AreEqual(0.20f, QbSeqTiming.Secure(false), 1e-4f); Assert.AreEqual(0.10f, QbSeqTiming.Secure(true), 1e-4f);
            Assert.Greater(QbSeqTiming.ScrambleStartSeconds, QbSeqTiming.ScrambleFirstSeconds);
            Assert.Less(QbSeqTiming.OpenDegrees, 100f, "he never turns his back on the field");
        }

        [Test]
        public void PlannerPivotKeepsTheToesWhereTheyAre()
        {
            var g = new ProcGait { Ankle = 0.09f };
            Vector3 root = Vector3.zero; var restR = new Vector3(0.2f, 0f, 0f); var restL = new Vector3(-0.2f, 0f, 0f);
            g.Reset(root, 0f, restR, restL, 0f);
            Vector3 toe0 = g.R.Pos + new Vector3(Mathf.Sin(g.R.Yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(g.R.Yaw * Mathf.Deg2Rad)) * g.BallOfFoot;
            for (int i = 0; i < 12; i++) g.Update(1f / 60f, root, Vector3.zero, 70f, restR, restL, 0f);
            float y = g.R.Yaw; Vector3 toe1 = g.R.Pos + new Vector3(Mathf.Sin(y * Mathf.Deg2Rad), 0f, Mathf.Cos(y * Mathf.Deg2Rad)) * g.BallOfFoot;
            if (!g.R.Swing) Assert.Less((toe1 - toe0).magnitude, 0.02f, "a planted foot turns on the ball of the foot");
        }

        [Test]
        public void FeetNeverCrossDuringALongBackwardDrop()
        {
            var g = new ProcGait { Ankle = 0.09f, StepScale = 1.9f };
            Vector3 root = Vector3.zero; var restR = new Vector3(0.2f, 0f, 0f); var restL = new Vector3(-0.2f, 0f, 0f);
            g.Reset(root, 0f, restR, restL, 0f);
            float yaw = 60f; Vector3 vel = new Vector3(0f, 0f, -3.5f);
            for (int i = 0; i < 120; i++)
            {
                root += vel / 60f; g.Update(1f / 60f, root, vel, yaw, restR, restL, 0f);
                Vector3 right = Vector3.Cross(Vector3.up, new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad)));
                if (!g.R.Swing && !g.L.Swing) Assert.GreaterOrEqual(Vector3.Dot(g.R.Pos - g.L.Pos, right), 0.10f, "frame " + i);
            }
        }

        [Test]
        public void KickGeometryHasThePuntContactBesideTheBodyLine()
        {
            Assert.Greater(KickGeometry.PuntContactAsideYd, 0.2f);
            Assert.LessOrEqual(KickGeometry.TeeHeightYd, 0.30f);
        }

        [Test]
        public void KickPlanBiasOnlyShiftsThePunt()
        {
            Assert.Less(Mathf.Abs(AnimKit.KickEtaBias(PlayKind.Kickoff)), 0.05f); Assert.Less(Mathf.Abs(AnimKit.KickEtaBias(PlayKind.FieldGoal)), 0.05f);
            Assert.Greater(AnimKit.KickEtaBias(PlayKind.Punt), 0f);
        }
    }
}
