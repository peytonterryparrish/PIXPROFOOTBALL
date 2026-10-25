using NUnit.Framework;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.12.0 CONTACT: the legs of an engaged body stay the stepper's after Final IK moved the pelvis, the pad hands sit on the front of the pads, and the pre-snap walk-in pace follows the time there is.</summary>
    public class ContactV012Tests
    {
        static Transform[] Leg(GameObject root)
        {
            var hips = new GameObject("hips").transform; hips.SetParent(root.transform, false); hips.localPosition = new Vector3(0f, 1f, 0f);
            var up = new GameObject("up").transform; up.SetParent(hips, false);
            var lo = new GameObject("lo").transform; lo.SetParent(up, false); lo.localPosition = new Vector3(0f, -0.45f, 0f);
            var ft = new GameObject("ft").transform; ft.SetParent(lo, false); ft.localPosition = new Vector3(0f, -0.45f, 0f);
            return new[] { hips, up, lo, ft };
        }

        [Test]
        public void TheAnkleGoesBackOnItsAnchorAfterTheHandSolveMovedThePelvis()
        {
            var root = new GameObject("leg");
            try
            {
                var c = Leg(root); Transform hips = c[0], up = c[1], lo = c[2], ft = c[3];
                Vector3 anchor = new Vector3(0.10f, 0.14f, 0.12f); Vector3 pole = Vector3.forward;
                LimbIK.Apply(up, lo, ft, anchor, pole, 1f);
                Assert.Less(Vector3.Distance(ft.position, anchor), 0.003f, "the stepper's solve puts the ankle on the anchor");
                Quaternion footWorld = Quaternion.Euler(0f, 25f, 0f); ft.rotation = footWorld;
                // Final IK reaches with the hands: the pelvis is moved and turned, the legs ride with it and the foot swings off its anchor
                hips.position += new Vector3(0.07f, -0.03f, 0.06f); hips.rotation = Quaternion.Euler(8f, 12f, -5f) * hips.rotation;
                Assert.Greater(Vector3.Distance(ft.position, anchor), 0.05f, "(the test needs a foot that has been dragged off)");
                EngageStepper.ReapplyLeg(up, lo, ft, anchor, pole, footWorld);
                Assert.Less(Vector3.Distance(ft.position, anchor), 0.003f, "the ankle is back on the anchor");
                Assert.Less(Quaternion.Angle(ft.rotation, footWorld), 0.1f, "the foot keeps the world rotation it had");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void AnAnchorBeyondTheLegsReachIsClampedNotStretched()
        {
            var root = new GameObject("leg");
            try
            {
                var c = Leg(root); Transform up = c[1], lo = c[2], ft = c[3];
                Vector3 far = new Vector3(0.9f, 0.1f, 0f);
                EngageStepper.ReapplyLeg(up, lo, ft, far, Vector3.forward, Quaternion.identity);
                float len = Vector3.Distance(up.position, lo.position) + Vector3.Distance(lo.position, ft.position);
                Assert.AreEqual(0.9f, len, 0.002f, "the bones keep their length");
                Assert.Less(Vector3.Distance(up.position, ft.position), 0.9f + 1e-3f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void PadHandsLieOnTheFrontOfTheOtherMansPads()
        {
            Vector3 blocker = new Vector3(0f, 1.3f, 0f), defender = new Vector3(0f, 1.3f, 0.8f);
            Vector3 r = PlayStage.PadTarget(blocker, defender, +1, true), l = PlayStage.PadTarget(blocker, defender, -1, true);
            Assert.Less(r.z, defender.z, "in front of the chest, not through it"); Assert.Greater(r.z, blocker.z + 0.3f, "and not on the blocker's own body");
            Assert.Greater(r.x, 0.05f, "the blocker's right hand is on his right of the sternum (+x when he faces +z)"); Assert.Less(l.x, -0.05f);
            Assert.AreEqual(r.z, l.z, 1e-5f);
            // the defender's hands are on the blocker's shoulders: higher and wider
            Vector3 dr = PlayStage.PadTarget(defender, blocker, +1, false);
            Assert.Greater(dr.y, defender.y - 0.05f); Assert.Greater(Mathf.Abs(dr.x), Mathf.Abs(r.x) - 1e-4f);
            Assert.AreEqual(blocker, PlayStage.PadTarget(blocker, blocker, +1, true), "two chests on the same spot: no direction, the target is the chest");
        }

        [Test]
        public void TheWalkInPaceIsTheDistanceOverTheTimeLeftBetweenAWalkAndAJog()
        {
            Assert.AreEqual(16f / 5f, PlayStage.WalkInPace(16f, 5f, 3f), 1e-4f, "16 m in 5 s is 3.2 m/s");
            Assert.AreEqual(PlayStage.WalkInMinPace, PlayStage.WalkInPace(3f, 20f, 3f), 1e-4f, "plenty of time: still a walk the locomotion has a gait for");
            Assert.AreEqual(PlayStage.WalkInMaxPace, PlayStage.WalkInPace(60f, 4f, 3f), 1e-4f, "no time: a jog, never a run");
            Assert.Greater(PlayStage.WalkInPace(16f, 4.5f, 3f), PlayStage.WalkInPace(16f, 5f, 3f), "less time, faster");
        }

        [Test]
        public void TheWalkInSlowsDownToStopOnItsSpotAndNeverExceedsAJogAtAnyDistanceOrTime()
        {
            float prev = 99f;
            for (float d = 6f; d >= 0.05f; d -= 0.05f)
            {
                float p = PlayStage.WalkInPace(d, 1.5f, PlayStage.WalkInDecel);
                Assert.LessOrEqual(p, prev + 1e-4f, "never speeds up on the approach (d " + d + ")"); prev = p;
                Assert.LessOrEqual(p, Mathf.Sqrt(2f * PlayStage.WalkInDecel * d) + PlayStage.WalkInMinPace, "the pace allows a stop within the decel (d " + d + ")");
            }
            for (float d = 0f; d < 80f; d += 3.7f)
                for (float t = -2f; t < 20f; t += 1.3f) Assert.LessOrEqual(PlayStage.WalkInPace(d, t, PlayStage.WalkInDecel), PlayStage.WalkInMaxPace + 1e-4f);
        }
    }
}
