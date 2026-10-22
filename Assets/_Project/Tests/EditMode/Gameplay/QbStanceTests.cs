using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.9.0 QBSTANCE: the authored stance poses, the stance rules with the play context, the footfall planner (a foot is always on the turf), the pose blend.</summary>
    public class QbStanceTests
    {
        static StanceSpec Spec(StanceKind k, bool mirror = false, bool left = false, bool defense = false) { return new StanceSpec { Kind = k, Mirror = mirror, IsLeftOfBall = left, Defense = defense }; }

        // ------------------------------------------------------------------------------------------------- poses
        [Test]
        public void EveryDrivenStanceKindHasAValidPose()
        {
            foreach (StanceKind k in System.Enum.GetValues(typeof(StanceKind)))
            {
                if (k == StanceKind.Upright || k == StanceKind.Holder) continue;
                var p = StancePoses.For(Spec(k), k == StanceKind.ThreePoint ? PlayerRole.OL : PlayerRole.QB);
                Assert.IsTrue(p.Valid, k.ToString());
                Assert.Greater(p.Hips, 0.4f, k.ToString()); Assert.Less(p.Hips, 1.1f, k.ToString());
                Assert.Greater(Mathf.Abs(p.RX - p.LX), 0.2f, k + ": a real base");
            }
        }

        [Test]
        public void LineStancesPutAHandOnTheTurfWithTheHipsAtLeastLevelWithTheShoulders()
        {
            foreach (var role in new[] { PlayerRole.OL, PlayerRole.DL })
            {
                var p = StancePoses.For(Spec(StanceKind.ThreePoint), role);
                Assert.AreEqual(HandMode.Ground, p.RHand, role.ToString());
                Assert.Less(p.RY, 0.12f, "wrist within 12 cm of the turf");
                Assert.GreaterOrEqual(p.Lean, 85f, role + ": back flat, hips level with or above the shoulders");
                Assert.Less(p.RZ, p.LZ, role + ": the down-hand side foot is the back foot");
            }
            var four = StancePoses.For(Spec(StanceKind.FourPoint), PlayerRole.DL);
            Assert.AreEqual(HandMode.Ground, four.RHand); Assert.AreEqual(HandMode.Ground, four.LHand);
        }

        [Test]
        public void MirroredStanceIsTheMirrorImage()
        {
            var p = StancePoses.For(Spec(StanceKind.ThreePoint, false), PlayerRole.OL);
            var m = StancePoses.For(Spec(StanceKind.ThreePoint, true), PlayerRole.OL);
            Assert.AreEqual(HandMode.Ground, m.LHand); Assert.AreEqual(HandMode.Thigh, m.RHand);
            Assert.AreEqual(p.RZ, m.LZ, 1e-5f); Assert.AreEqual(p.RX, -m.LX, 1e-5f);
            var back = m.Mirrored();
            Assert.AreEqual(p.RX, back.RX, 1e-5f); Assert.AreEqual(p.RHand, back.RHand);
        }

        [Test]
        public void ReceiverStanceIsStaggeredAFootOrMoreWithTheBackHeelUpAndTheOutsideFootBack()
        {
            foreach (bool left in new[] { false, true })
            {
                var p = StancePoses.For(Spec(StanceKind.ReceiverSplit, false, left), PlayerRole.WR);
                Assert.GreaterOrEqual(p.Stagger, 0.30f, "stagger at least 0.30 m (E-050)");
                float backZ = Mathf.Min(p.RZ, p.LZ);
                Assert.Greater(Mathf.Max(p.RHeel, p.LHeel), 0.04f, "back heel off the ground");
                // outside foot back: the left receiver's left foot, the right receiver's right foot
                if (left) Assert.Less(p.LZ, p.RZ); else Assert.Less(p.RZ, p.LZ);
                Assert.Less(Mathf.Abs(p.Lean - 22f), 6f);
            }
        }

        [Test]
        public void PressAndOffCornersAreDifferentStances()
        {
            var press = StancePoses.For(Spec(StanceKind.CornerPress, false, false, true), PlayerRole.DB);
            var off = StancePoses.For(Spec(StanceKind.CornerOff, false, false, true), PlayerRole.DB);
            Assert.AreEqual(HandMode.Chest, press.RHand, "hands up at the chest to jam");
            Assert.AreNotEqual(HandMode.Chest, off.RHand);
            Assert.Less(press.Stagger, 0.12f, "squared up");
            Assert.GreaterOrEqual(off.Stagger, 0.22f, "the outside foot back");
            Assert.Less(off.Hips, press.Hips, "the off corner sits lower, in the chair");
        }

        [Test]
        public void QuarterbackUnderCenterCrouchesWithHandsUnderTheCenter()
        {
            var p = StancePoses.For(Spec(StanceKind.QbUnderCenter), PlayerRole.QB);
            Assert.That(p.Lean, Is.InRange(25f, 36f), "trunk 25-35 degrees");
            Assert.AreEqual(HandMode.Anchor, p.RHand); Assert.AreEqual(HandMode.Anchor, p.LHand);
            Assert.Less(p.ToeOut, 0f, "slightly pigeon-toed");
            Assert.Less(p.Hips, 0.86f);
            var sg = StancePoses.For(Spec(StanceKind.QbShotgun), PlayerRole.QB);
            Assert.That(sg.Lean, Is.InRange(10f, 22f)); Assert.AreEqual(HandMode.Chest, sg.RHand);
            Assert.Less(sg.Hips, 0.95f, "knees flexed: hips below the old 1.06 idle");
        }

        [Test]
        public void ThrowPoseClosesTheFrontShoulderThenOpensItAfterTheHipsAndStride()
        {
            var load = StancePoses.QbThrow(0.55f, false);
            var rel = StancePoses.QbThrow(1f, false);
            var fol = StancePoses.QbThrow(1.4f, false);
            Assert.Greater(load.UpperYaw, 25f, "front shoulder closed while loaded");
            Assert.Less(Mathf.Abs(rel.UpperYaw), 8f, "shoulders square at release");
            Assert.Less(fol.UpperYaw, -10f, "the shoulders finish across the body");
            Assert.Greater(load.LZ, 0.2f, "the front foot is out toward the target before the release");
            Assert.That(rel.Lean, Is.InRange(12f, 22f), "spine near vertical at release");
            Assert.AreEqual(HandMode.Cock, rel.RHand, "the throwing hand is driven (cocked by the ear, high at release)"); Assert.Greater(rel.RPos.y, 0.3f, "the release is high");
        }

        [Test]
        public void PoseBlendInterpolatesNumbersAndSwitchesModesAtTheMiddle()
        {
            var a = StancePoses.For(Spec(StanceKind.QbUnderCenter), PlayerRole.QB);
            var b = StancePoses.For(Spec(StanceKind.QbPocket), PlayerRole.QB);
            var m = StancePoses.Lerp(a, b, 0.5f);
            Assert.AreEqual((a.Lean + b.Lean) * 0.5f, m.Lean, 1e-4f);
            Assert.AreEqual(a.RHand, StancePoses.Lerp(a, b, 0.2f).RHand);
            Assert.AreEqual(b.RHand, StancePoses.Lerp(a, b, 0.8f).RHand);
        }

        // ------------------------------------------------------------------------------------------------- rules with context
        [Test]
        public void CornerPressFollowsTheCallNotTheDepth()
        {
            var soft = StanceRules.For(Position.CB, SimSide.Defense, PlayKind.Scrimmage, "4-3", -17f, 2.0f, false, false, new StanceContext { PressKnown = true, Press = false });
            Assert.AreEqual(StanceKind.CornerOff, soft.Kind, "a Cover 3 corner at 2 yd is not in a jam stance");
            var press = StanceRules.For(Position.CB, SimSide.Defense, PlayKind.Scrimmage, "4-3", -17f, 1.0f, false, false, new StanceContext { PressKnown = true, Press = true });
            Assert.AreEqual(StanceKind.CornerPress, press.Kind);
            Assert.IsTrue(press.Defense);
            var far = StanceRules.For(Position.CB, SimSide.Defense, PlayKind.Scrimmage, "4-3", -17f, 4.0f, false, false, new StanceContext { PressKnown = true, Press = true });
            Assert.AreEqual(StanceKind.CornerOff, far.Kind, "a press call from 4 yd is an off stance");
        }

        [Test]
        public void OffensiveLineGoesToTwoPointOnlyInAPassSetFromTheShotgun()
        {
            var pass = StanceRules.For(Position.LT, SimSide.Offense, PlayKind.Scrimmage, "11", -3f, 0f, true, false, new StanceContext { PassPlay = true });
            Assert.AreEqual(StanceKind.OlTwoPoint, pass.Kind);
            Assert.AreEqual(StanceKind.ThreePoint, StanceRules.For(Position.LT, SimSide.Offense, PlayKind.Scrimmage, "11", -3f, 0f, true, false, new StanceContext { RunPlay = true }).Kind);
            Assert.AreEqual(StanceKind.ThreePoint, StanceRules.For(Position.LT, SimSide.Offense, PlayKind.Scrimmage, "11", -3f, 0f, false, false, new StanceContext { PassPlay = true }).Kind, "under centre stays three point");
            Assert.AreEqual(StanceKind.CenterSnap, StanceRules.For(Position.C, SimSide.Offense, PlayKind.Scrimmage, "11", 0f, 0f, true, false, new StanceContext { PassPlay = true }).Kind, "the centre keeps his hand on the ball");
            Assert.AreEqual(-1f, pass.Tell, "pass set: sit back, light hand");
        }

        [Test]
        public void KickingTeamStancesAreTheirOwn()
        {
            Assert.AreEqual(StanceKind.LongSnapper, StanceRules.For(Position.C, SimSide.Offense, PlayKind.Punt, "Punt", 0f, 0f, false, false).Kind);
            Assert.AreEqual(StanceKind.LongSnapper, StanceRules.For(Position.C, SimSide.Offense, PlayKind.FieldGoal, "FieldGoal", 0f, 0f, false, false).Kind);
            Assert.AreEqual(StanceKind.Punter, StanceRules.For(Position.P, SimSide.Offense, PlayKind.Punt, "Punt", 0f, -14f, false, false).Kind);
            Assert.AreEqual(StanceKind.Kicker, StanceRules.For(Position.K, SimSide.Offense, PlayKind.FieldGoal, "FieldGoal", -1.5f, -9f, false, false).Kind);
            Assert.AreEqual(StanceKind.KickoffRunner, StanceRules.For(Position.LOLB, SimSide.Offense, PlayKind.Kickoff, "Kickoff", -5f, -1f, false, false).Kind);
            Assert.AreEqual(StanceKind.KickoffReceive, StanceRules.For(Position.TE, SimSide.Defense, PlayKind.Kickoff, "KickoffReturn", -10f, 40f, false, false).Kind);
        }

        [Test]
        public void EdgeRusherTiltsTowardTheQuarterbackAndGoalLineFrontsAreFourPoint()
        {
            var le = StanceRules.For(Position.LE, SimSide.Defense, PlayKind.Scrimmage, "4-3", -7.5f, 1.2f, false, false, new StanceContext());
            var re = StanceRules.For(Position.RE, SimSide.Defense, PlayKind.Scrimmage, "4-3", 7.5f, 1.2f, false, false, new StanceContext());
            Assert.Greater(Mathf.Abs(le.Tilt), 12f); Assert.AreEqual(-Mathf.Sign(le.Tilt), Mathf.Sign(re.Tilt), "the two edges tilt in to the middle from opposite sides");
            var gl = StanceRules.For(Position.DT, SimSide.Defense, PlayKind.Scrimmage, "4-4-3", 1.5f, 1.0f, false, false, new StanceContext { GoalLine = true });
            Assert.AreEqual(StanceKind.FourPoint, gl.Kind);
            var glDe = StanceRules.For(Position.LE, SimSide.Defense, PlayKind.Scrimmage, "4-4-3", -3.5f, 1.0f, false, false, new StanceContext { GoalLine = true });
            Assert.AreEqual(StanceKind.FourPoint, glDe.Kind, "a goal-line front has both hands down on the line");
        }

        // ------------------------------------------------------------------------------------------------- footfall planner
        static void Run(ProcGait g, float seconds, Vector3 vel, ref Vector3 root, float yaw, System.Action<float> each = null)
        {
            Vector3 rr = new Vector3(0.2f, 0f, -0.05f), ll = new Vector3(-0.2f, 0f, 0.05f);
            float dt = 1f / 60f;
            Vector3 pr = g.R.Pos, pl = g.L.Pos; bool wasRP = !g.R.Swing, wasLP = !g.L.Swing;
            for (float t = 0f; t < seconds; t += dt)
            {
                root += vel * dt;
                g.Update(dt, root, vel, yaw, rr, ll, 0f);
                Assert.IsFalse(g.R.Swing && g.L.Swing, "never both feet in the air (t=" + t.ToString("0.00") + ")");
                if (wasRP && !g.R.Swing) Assert.Less((g.R.Pos - pr).magnitude, 1e-4f, "a planted foot does not move");
                if (wasLP && !g.L.Swing) Assert.Less((g.L.Pos - pl).magnitude, 1e-4f, "a planted foot does not move");
                Assert.Less(new Vector2(g.R.Pos.x - root.x, g.R.Pos.z - root.z).magnitude, 1.25f, "the foot stays within reach of the body");
                Assert.Less(new Vector2(g.L.Pos.x - root.x, g.L.Pos.z - root.z).magnitude, 1.25f, "the foot stays within reach of the body");
                pr = g.R.Pos; pl = g.L.Pos; wasRP = !g.R.Swing; wasLP = !g.L.Swing;
                if (each != null) each(t);
            }
        }

        [Test]
        public void FootfallPlannerKeepsAFootOnTheTurfAtEverySpeedAndDirection()
        {
            foreach (var v in new[] { new Vector3(0f, 0f, 1.2f), new Vector3(0f, 0f, 3.5f), new Vector3(0f, 0f, 5.5f), new Vector3(0f, 0f, -3.0f), new Vector3(2.2f, 0f, 0f), new Vector3(-3.0f, 0f, 1.0f), new Vector3(1.5f, 0f, -2.5f) })
            {
                var g = new ProcGait();
                Vector3 root = Vector3.zero;
                g.Reset(root, 0f, new Vector3(0.2f, 0f, -0.05f), new Vector3(-0.2f, 0f, 0.05f), 0f);
                Run(g, 3.0f, v, ref root, 0f);
                Assert.Greater(g.Steps, 3, "steps happen while he travels " + v);
            }
        }

        [Test]
        public void FootfallPlannerSettlesBothFeetToTheStanceWhenTheBodyStops()
        {
            var g = new ProcGait(); Vector3 root = Vector3.zero;
            g.Reset(root, 0f, new Vector3(0.2f, 0f, -0.05f), new Vector3(-0.2f, 0f, 0.05f), 0f);
            Run(g, 1.5f, new Vector3(0f, 0f, 4f), ref root, 0f);
            Run(g, 1.2f, Vector3.zero, ref root, 0f);
            Assert.IsFalse(g.R.Swing); Assert.IsFalse(g.L.Swing);
            Vector3 rest = root + new Vector3(0.2f, 0f, -0.05f);
            Assert.Less(new Vector2(g.R.Pos.x - rest.x, g.R.Pos.z - rest.z).magnitude, 0.18f, "right foot back under the stance");
            Assert.Less(g.R.Pos.y, 0.101f); Assert.Less(g.L.Pos.y, 0.101f);
        }

        [Test]
        public void FootfallPlannerFollowsAReversePivot()
        {
            // the body turns 180 degrees in a quarter second while it starts to travel: the feet turn with it without ever leaving the turf together
            var g = new ProcGait(); Vector3 root = Vector3.zero; float yaw = 0f;
            g.Reset(root, 0f, new Vector3(0.2f, 0f, -0.05f), new Vector3(-0.2f, 0f, 0.05f), 0f);
            float dt = 1f / 60f;
            for (float t = 0f; t < 1.0f; t += dt)
            {
                yaw = Mathf.MoveTowardsAngle(yaw, 180f, 720f * dt);
                Vector3 vel = new Vector3(0f, 0f, -3.5f * Mathf.Clamp01((t - 0.1f) / 0.3f));
                root += vel * dt;
                g.Update(dt, root, vel, yaw, new Vector3(0.2f, 0f, -0.05f), new Vector3(-0.2f, 0f, 0.05f), 0f);
                Assert.IsFalse(g.R.Swing && g.L.Swing);
            }
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(g.R.Yaw, 180f)), 60f, "the planted feet followed the body around");
            Assert.Less(Mathf.Abs(Mathf.DeltaAngle(g.L.Yaw, 180f)), 60f);
        }
    }
}
