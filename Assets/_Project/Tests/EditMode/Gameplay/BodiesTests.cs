using System.Reflection;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.10.0 BODIES (playtest note 9): offensive line stances by position, the deep-stance neck, the joint-limit math, the ball leaving the hand.</summary>
    public class BodiesTests
    {
        static StanceSpec LineSpec(Position pos, float relX)
        {
            return StanceRules.ForBasic(pos, SimSide.Offense, PlayKind.Scrimmage, "Singleback", relX, 0f, false, false);
        }

        [Test]
        public void EveryOffensiveLineSlotGetsItsOwnStance()
        {
            Assert.AreEqual(1, LineSpec(Position.LT, -6f).Ol);
            Assert.AreEqual(1, LineSpec(Position.RT, 6f).Ol);
            Assert.AreEqual(2, LineSpec(Position.LG, -3f).Ol);
            Assert.AreEqual(2, LineSpec(Position.RG, 3f).Ol);
            Assert.AreEqual(3, LineSpec(Position.C, 0f).Ol);
            Assert.AreEqual(StanceKind.CenterSnap, LineSpec(Position.C, 0f).Kind);
            Assert.AreEqual(0, StanceRules.ForBasic(Position.LE, SimSide.Defense, PlayKind.Scrimmage, "4-3 Base", -3f, 1f, false, false).Ol, "the defence has no offensive line slot");
        }

        [Test]
        public void TacklesSitUprightGuardsLowerTheCentreIsDownOverTheBall()
        {
            var t = StancePoses.For(LineSpec(Position.LT, -6f), PlayerRole.OL);
            var g = StancePoses.For(LineSpec(Position.LG, -3f), PlayerRole.OL);
            var c = StancePoses.For(LineSpec(Position.C, 0f), PlayerRole.OL);
            Assert.IsTrue(t.Valid && g.Valid && c.Valid);
            Assert.Greater(t.Hips, g.Hips, "tackle hips above the guard's");
            Assert.Greater(g.Hips, c.Hips, "guard hips above the centre's");
            Assert.Less(t.Lean, g.Lean, "the tackle is the more upright");
            Assert.Less(g.Lean, c.Lean, "the centre is the lowest, back flat");
            Assert.GreaterOrEqual(c.Lean, 90f, "centre: back flat");
            Assert.Less(t.Lean, 60f, "tackle: a little more upright than the old three-point");
            // arms on the thighs for tackles and guards, hands on the ball for the centre
            foreach (var p in new[] { t, g }) { Assert.AreEqual(HandMode.Thigh, p.RHand); Assert.AreEqual(HandMode.Thigh, p.LHand); }
            Assert.AreEqual(HandMode.Anchor, c.RHand); Assert.AreEqual(HandMode.Anchor, c.LHand, "both hands on the ball");
            Assert.Less(c.RY, 0.3f, "the ball is on the turf");
        }

        [Test]
        public void StanceTellsStaySubtleOnTheLinemen()
        {
            var run = LineSpec(Position.LT, -6f); run.Tell = 1f;
            var pass = LineSpec(Position.LT, -6f); pass.Tell = -1f;
            float d = StancePoses.For(run, PlayerRole.OL).Lean - StancePoses.For(pass, PlayerRole.OL).Lean;
            Assert.Greater(d, 0f, "weight forward for a run");
            Assert.Less(d, 12f, "but only a few degrees");
        }

        [Test]
        public void BentOverStancesLookForwardNotUp()
        {
            foreach (var role in new[] { PlayerRole.OL, PlayerRole.DL })
            {
                var p = StancePoses.For(new StanceSpec { Kind = StanceKind.ThreePoint }, role);
                Assert.LessOrEqual(p.HeadPitch, 6f, role + ": the helmet faces forward, level, never up at the sky");
            }
            var four = StancePoses.For(new StanceSpec { Kind = StanceKind.FourPoint }, PlayerRole.DL);
            Assert.LessOrEqual(four.HeadPitch, 6f);
            Assert.LessOrEqual(StanceIK.NeckExtMax, 55f, "the neck stays inside the human range");
        }

        [Test]
        public void RollIsMeasuredAboutTheBoneAxisAndIgnoresSwing()
        {
            Vector3 axis = new Vector3(1f, 0f, 0f);
            var bind = Quaternion.Euler(10f, 20f, 30f);
            Assert.AreEqual(50f, BodySanity.Roll(bind, bind * Quaternion.AngleAxis(50f, axis), axis), 0.01f);
            Assert.AreEqual(-70f, BodySanity.Roll(bind, bind * Quaternion.AngleAxis(-70f, axis), axis), 0.01f);
            // a pure bend about another axis is no roll
            Assert.AreEqual(0f, BodySanity.Roll(bind, bind * Quaternion.AngleAxis(80f, Vector3.up), axis), 0.01f);
            // a bend followed by a roll: only the roll counts
            Assert.AreEqual(35f, BodySanity.Roll(bind, bind * Quaternion.AngleAxis(60f, Vector3.forward) * Quaternion.AngleAxis(35f, axis), axis), 0.5f);
            Assert.AreEqual(0f, BodySanity.Roll(bind, bind, axis), 1e-4f);
        }

        [Test]
        public void ArmIkRollsTheUpperArmSoTheElbowHingeLiesAcrossTheBend()
        {
            var root = new GameObject("arm");
            try
            {
                var up = new GameObject("up").transform; up.SetParent(root.transform, false);
                var lo = new GameObject("lo").transform; lo.SetParent(up, false); lo.localPosition = new Vector3(0.30f, 0f, 0f);
                var hd = new GameObject("hd").transform; hd.SetParent(lo, false); hd.localPosition = new Vector3(0.28f, 0f, 0f);
                // a T-pose arm along +x, the forearm folds forward (+z): hinge = (arm direction) x (forward), in the forearm's space
                Vector3 hinge = Vector3.Cross(Vector3.right, Vector3.forward).normalized;
                LimbIK.RegisterHinge(lo, hinge);
                try
                {
                    lo.localRotation = Quaternion.AngleAxis(20f, Vector3.right);          // the animation left the forearm rolled by 20 degrees
                    LimbIK.Apply(up, lo, hd, new Vector3(0.30f, 0f, 0.28f), Vector3.back * 0.5f, 1f);      // the elbow points back, the hand goes forward: a normal flexion
                    Vector3 axis = lo.position - up.position, e = hd.position - lo.position;
                    Vector3 n = Vector3.Cross(axis, e).normalized, h = (lo.rotation * hinge).normalized;
                    Assert.Greater(Mathf.Abs(Vector3.Dot(h, n)), 0.97f, "the hinge lies across the bending plane: h " + h + " n " + n + " axis " + axis + " e " + e + " hand " + hd.position);
                    Assert.Greater(Vector3.Dot(h, n), 0f, "and folds the right way (forward)");
                    Assert.Less(Vector3.Distance(hd.position, new Vector3(0.30f, 0f, 0.28f)), 0.03f, "the hand reached its target");
                }
                finally { LimbIK.UnregisterHinge(lo); }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void TheLimitsAreHumanRanges()
        {
            Assert.LessOrEqual(BodySanity.SpineTwistMax, 35f, "waist twist, outside throws");
            Assert.LessOrEqual(BodySanity.ForearmRollMax, 90f, "pronation range");
            Assert.LessOrEqual(BodySanity.HeadYawMax, 85f); Assert.LessOrEqual(BodySanity.HeadExtendMax, 110f);
            Assert.LessOrEqual(BodySanity.ElbowHyper, 6f); Assert.LessOrEqual(BodySanity.KneeHyper, 6f);
        }

        [Test]
        public void ASpikedBallBouncesAndLiesStillOnTheTurf()
        {
            var parent = new GameObject("BallTest");
            try
            {
                var b = BallFollower.Create(parent.transform);
                b.transform.position = new Vector3(3f, 1.4f, 2f);
                b.SetSpike(null, 1, 0f);
                Assert.IsTrue(b.IsSpiked);
                var step = typeof(BallFollower).GetMethod("SpikeStep", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(step);
                float minY = 9f; Vector3 last = b.transform.position;
                for (int i = 0; i < 600 && !b.SpikeAtRest; i++)
                {
                    step.Invoke(b, new object[] { 1f / 60f });
                    minY = Mathf.Min(minY, b.transform.position.y);
                    Assert.GreaterOrEqual(b.transform.position.y, BallFollower.RestHeight - 1e-3f, "never through the turf");
                    last = b.transform.position;
                }
                Assert.IsTrue(b.SpikeAtRest, "comes to rest within ten seconds");
                Assert.AreEqual(BallFollower.RestHeight, last.y, 0.01f);
                Assert.Less(new Vector2(last.x - 3f, last.z - 2f).magnitude, 4f, "stays near the scorer");
            }
            finally { Object.DestroyImmediate(parent); }
        }

        [Test]
        public void TheBallGoesToAnOfficialAndStaysInTheHand()
        {
            var parent = new GameObject("BallTest2"); var hand = new GameObject("Hand");
            try
            {
                var b = BallFollower.Create(parent.transform);
                hand.transform.position = new Vector3(1f, 1.0f, 1f);
                b.SetOfficial(hand.transform, Vector3.forward);
                Assert.IsTrue(b.IsWithOfficial);
                Assert.IsNull(b.Holder, "no player holds it");
            }
            finally { Object.DestroyImmediate(parent); Object.DestroyImmediate(hand); }
        }

        // ---------------------------------------------------------------- the 15 bodies: proportions and rig consistency
        static System.Collections.Generic.IEnumerable<GameObject> BodyPrefabs()
        {
            var lib = PixFootball.Art.ArtLibrary.Load();
            if (lib == null) yield break;
            foreach (PixFootball.Art.PhotoBody sz in System.Enum.GetValues(typeof(PixFootball.Art.PhotoBody)))
                foreach (PixFootball.Art.BodyBase bb in System.Enum.GetValues(typeof(PixFootball.Art.BodyBase)))
                {
                    var p = lib.Prefab(PixFootball.Art.PlayerAppearance.PrefabKey(sz, bb));
                    if (p != null) yield return p;
                }
        }

        static Transform Find(Transform t, string n)
        {
            if (t.name == n) return t;
            for (int i = 0; i < t.childCount; i++) { var r = Find(t.GetChild(i), n); if (r != null) return r; }
            return null;
        }

        [Test]
        public void EveryBodyHasAHumanUpperArm()
        {
            int n = 0;
            foreach (var p in BodyPrefabs())
            {
                n++;
                foreach (string s in new[] { "Left", "Right" })
                {
                    var a = Find(p.transform, "rig:" + s + "Arm"); var f = Find(p.transform, "rig:" + s + "ForeArm"); var h = Find(p.transform, "rig:" + s + "Hand");
                    Assert.IsNotNull(a); Assert.IsNotNull(f); Assert.IsNotNull(h);
                    float up = Vector3.Distance(a.position, f.position), fo = Vector3.Distance(f.position, h.position);
                    Assert.GreaterOrEqual(up / fo, 1.18f, p.name + " " + s + ": upper arm over forearm (human 1.25)");
                    Assert.Less(up / fo, 1.4f, p.name);
                    Assert.AreEqual(0.60f, up + fo, 0.06f, p.name + " " + s + ": shoulder to wrist");
                }
            }
            if (n == 0) Assert.Ignore("no photoreal prefabs in the art library");
        }

        [Test]
        public void EveryBonePositionMatchesItsMeshBindPose()
        {
            int checkedParts = 0;
            foreach (var p in BodyPrefabs())
                foreach (var smr in p.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var mesh = smr.sharedMesh; if (mesh == null) continue;
                    var bones = smr.bones; var bp = mesh.bindposes;
                    Assert.AreEqual(bp.Length, bones.Length, p.name + " " + smr.name + ": bones and bind poses");
                    for (int i = 0; i < bones.Length; i++)
                    {
                        if (bones[i] == null) continue;
                        Vector3 fromMesh = bp[i].inverse.MultiplyPoint3x4(Vector3.zero);
                        Vector3 fromRig = smr.transform.InverseTransformPoint(bones[i].position);
                        Assert.Less(Vector3.Distance(fromMesh, fromRig), 0.004f, p.name + " " + smr.name + " bone " + bones[i].name + ": the mesh was imported for a different rig (re-import the bodies and sync the prefabs)");
                    }
                    checkedParts++;
                }
            if (checkedParts == 0) Assert.Ignore("no photoreal prefabs in the art library");
        }

        [Test]
        public void EveryBodyCarriesTheFourHelperBonesAndTheirDriver()
        {
            int n = 0;
            foreach (var p in BodyPrefabs())
            {
                n++;
                foreach (string h in new[] { "rig:LeftUpLegHelper", "rig:RightUpLegHelper", "rig:LeftArmHelper", "rig:RightArmHelper" }) Assert.IsNotNull(Find(p.transform, h), p.name + " " + h);
                if (BodyHelperBones.Enabled) Assert.IsNotNull(p.GetComponentInChildren<BodyHelperBones>(true), p.name + ": driver"); // v0.10.0 round 3 retired the weighted helpers (Enabled = false); the bones stay, the driver is only required while on
            }
            if (n == 0) Assert.Ignore("no photoreal prefabs in the art library");
        }
    }
}
