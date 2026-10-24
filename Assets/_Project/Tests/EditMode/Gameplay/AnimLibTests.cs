using NUnit.Framework;
using PixFootball.Art;
using PixFootball.EditorTools;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.10.0 ANIMLIB: the clip QA verdict, the torso limit pass, the clip classification and the tag table lookup.</summary>
    public class AnimLibTests
    {
        static QaRow Clean()
        {
            return new QaRow { name = "x", length = 2f, slidePct = 3f, slideMean = 0.05f, penPct = 0f, floatCm = 0f, waistTwist = 20f, torsoTwist = 20f, torsoBend = 30f, headTwist = 20f, kneeHyper = 0f, popFrames = 0f, fingerStatic = false };
        }

        [Test]
        public void ACleanClipScoresHighAndIsNotRejected()
        {
            var r = Clean(); ClipQa.Verdict(r);
            Assert.GreaterOrEqual(r.score, 95f);
            Assert.IsFalse(r.reject);
            Assert.AreEqual("", r.faults);
        }

        [Test]
        public void SlidingFeetPopsAndTwistAreFlaggedAndCostScore()
        {
            var r = Clean(); r.slidePct = 80f; r.slideMean = 0.9f; r.waistTwist = 130f; r.popFrames = 9f;
            ClipQa.Verdict(r);
            Assert.IsTrue(r.fSlide); Assert.IsTrue(r.fTwist); Assert.IsTrue(r.fSkel);
            StringAssert.Contains("slide", r.faults); StringAssert.Contains("waist-twist", r.faults); StringAssert.Contains("pops", r.faults);
            Assert.Less(r.score, 60f);
            Assert.IsTrue(r.reject, "eight or more bone pops cannot be fixed by the cleaner");
        }

        [Test]
        public void ClipsWithoutFingerDataAreAHandFault()
        {
            var r = Clean(); r.fingerStatic = true; ClipQa.Verdict(r);
            Assert.IsTrue(r.fHand); StringAssert.Contains("no-finger-data", r.faults);
        }

        [Test]
        public void TorsoLimitCompressesTheExcessTwistAndLeavesNormalPosesAlone()
        {
            var s = PoseSeq.Alloc(4, 30f, "t");
            int sp = ClipQa.MuscleIndex("Spine Twist Left-Right"), ch = ClipQa.MuscleIndex("Chest Twist Left-Right"), up = ClipQa.MuscleIndex("UpperChest Twist Left-Right");
            for (int i = 0; i < 4; i++) { s.RQ[i] = Quaternion.identity; }
            s.M[sp][0] = 0.3f; s.M[ch][0] = 0.2f;                 // 12 + 8 degrees: untouched
            s.M[sp][1] = 1.4f; s.M[ch][1] = 1.4f; s.M[up][1] = 1.4f; // 56 + 56 + 28 = 140 degrees
            float before0 = s.M[sp][0];
            int n = ClipPolish.Limit(s, new[] { "Spine Twist Left-Right", "Chest Twist Left-Right", "UpperChest Twist Left-Right" }, 72f);
            Assert.AreEqual(1, n);
            Assert.AreEqual(before0, s.M[sp][0], 1e-5f);
            float sum = ClipQa.Deg(sp, s.M[sp][1]) + ClipQa.Deg(ch, s.M[ch][1]) + ClipQa.Deg(up, s.M[up][1]);
            Assert.Less(sum, 100f); Assert.Greater(sum, 72f);
        }

        [Test]
        public void ClassificationAssignsCategoryAndPositions()
        {
            string cat, roles;
            AnimLibTags.Classify("qb_cadence_under", "x", out cat, out roles); Assert.AreEqual("QB", cat); Assert.AreEqual("QB", roles);
            AnimLibTags.Classify("tackle_wrap", "x", out cat, out roles); Assert.AreEqual("tackle", cat);
            AnimLibTags.Classify("kickoff_att_idle", "x", out cat, out roles); Assert.AreEqual("huddle", cat);
            AnimLibTags.Classify("catch_high", "x", out cat, out roles); Assert.AreEqual("catch", cat); StringAssert.Contains("WR", roles);
            AnimLibTags.Classify("ref_walk", "x", out cat, out roles); Assert.AreEqual("REF", cat);
            AnimLibTags.Classify("celebrate_big", "x", out cat, out roles); Assert.AreEqual("celebration", cat);
            AnimLibTags.Classify("", "BGS__BGS_OLM_Shuffle_CStance_L_Loop", out cat, out roles); Assert.IsNotNull(cat);
        }

        [Test]
        public void TagTableFindsClipsByNameAndKey()
        {
            var t = ScriptableObject.CreateInstance<ClipTagTable>();
            t.entries.Add(new ClipTagTable.Entry { clip = "a", key = "k1", keys = "k1+k2", score = 90f });
            t.entries.Add(new ClipTagTable.Entry { clip = "b", key = "k2", keys = "k2", score = 40f, reject = true });
            ClipTagTable.Entry e;
            Assert.IsTrue(t.TryGet("a", out e)); Assert.AreEqual(90f, e.score);
            Assert.IsFalse(t.TryGet("zzz", out e));
            Assert.AreEqual(2, t.OfKey("k2").Count); Assert.AreEqual(1, t.OfKey("k1").Count); Assert.AreEqual(0, t.OfKey("none").Count);
            Object.DestroyImmediate(t);
        }

        // ---- v0.11.0 ----
        [Test]
        public void FingerCurvesAreWrittenUnderTheAnimatorNames()
        {
            // the v0.8-v0.10 bug: curves named "Left Index 1 Stretched" are ignored by the Animator, so every cleaned clip had bind-pose fingers
            var s = PoseSeq.Alloc(4, 30f, "t"); for (int i = 0; i < 4; i++) s.RQ[i] = Quaternion.identity;
            int mi = PoseSeq.Index("Left Index 1 Stretched"); Assert.GreaterOrEqual(mi, 0);
            for (int i = 0; i < 4; i++) s.M[mi][i] = -0.6f;
            Assert.AreEqual("LeftHand.Index.1 Stretched", PoseSeq.CurveName(mi));
            Assert.AreEqual("RightHand.Thumb.Spread", PoseSeq.CurveName(PoseSeq.Index("Right Thumb Spread")));
            Assert.AreEqual("Spine Front-Back", PoseSeq.CurveName(PoseSeq.Index("Spine Front-Back")));
            var clip = s.ToClip("t");
            bool animatorName = false, wrongName = false;
            foreach (var b in UnityEditor.AnimationUtility.GetCurveBindings(clip)) { if (b.propertyName == "LeftHand.Index.1 Stretched") animatorName = true; if (b.propertyName == "Left Index 1 Stretched") wrongName = true; }
            Assert.IsTrue(animatorName); Assert.IsFalse(wrongName);
            var back = PoseSeq.FromClip(clip, 0f, clip.length, 30f);
            Assert.AreEqual(-0.6f, back.M[mi][2], 0.02f);
            Object.DestroyImmediate(clip);
        }

        [Test]
        public void WristClampKeepsTheMusclesInsideTheAvatarRange()
        {
            var s = PoseSeq.Alloc(5, 30f, "t");
            int m = PoseSeq.Index("Left Hand In-Out");
            s.M[m][0] = 0.2f; s.M[m][1] = 1.4f; s.M[m][2] = -1.8f; s.M[m][3] = 0.99f; s.M[m][4] = 0f;
            ClipPolish2.WristClamp(s);
            for (int i = 0; i < 5; i++) Assert.LessOrEqual(Mathf.Abs(s.M[m][i]), 0.99f + 1e-4f);
            Assert.AreEqual(0.2f, s.M[m][0], 1e-4f);
            Assert.AreEqual(0f, s.M[m][4], 1e-4f);
        }

        [Test]
        public void DeadCutKeepsTheLeadAndRefusesToCutAShortTake()
        {
            var q = new QaRow { length = 6f, deadStart = 2f, deadEnd = 0.2f };
            float a, b; ClipPolish2.DeadCut(q, out a, out b);
            Assert.AreEqual(1.75f, a, 1e-3f); Assert.AreEqual(0f, b, 1e-3f);
            q = new QaRow { length = 1.2f, deadStart = 0.9f, deadEnd = 1.0f };
            ClipPolish2.DeadCut(q, out a, out b);
            Assert.AreEqual(0f, a); Assert.AreEqual(0f, b);
        }

        [Test]
        public void PlantsAreFoundWhereTheSoleStaysOnTheTurf()
        {
            var sole = new[] { new float[20], new float[20] };
            for (int i = 0; i < 20; i++) { sole[0][i] = (i >= 5 && i <= 12) ? 0.005f : 0.3f; sole[1][i] = 0.3f; }
            var p = Plants.Find(sole, 0f, 1f / 30f);
            Assert.AreEqual(1, p.Count); Assert.AreEqual(0, p[0].side); Assert.LessOrEqual(p[0].i0, 6); Assert.GreaterOrEqual(p[0].i1, 11);
        }

        [Test]
        public void NewMocapHelpersReturnAnEmptyPlanWhenTheLibraryHasNoClip()
        {
            Assert.IsFalse(PixFootball.Gameplay.AnimKit.Mocap("no_such_key", null, 1).Valid);
        }
    }
}
