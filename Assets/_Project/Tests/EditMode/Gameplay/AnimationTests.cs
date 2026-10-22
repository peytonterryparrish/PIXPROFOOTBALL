using NUnit.Framework;
using PixFootball.Art;
using PixFootball.EditorTools;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.8 animation pass: the muscle-space cleanup operations (PoseSeq / ClipCleaner), naming and import rules.</summary>
    public class AnimationTests
    {
        static PoseSeq Sine(int n, float rate, float freq, int muscle = 3)
        {
            var s = PoseSeq.Alloc(n, rate, "sine");
            for (int i = 0; i < n; i++) { s.M[muscle][i] = 0.5f * Mathf.Sin(2f * Mathf.PI * freq * i / rate); s.RQ[i] = Quaternion.identity; s.RT[i] = new Vector3(0f, 1f, i * 0.05f); }
            return s;
        }

        [Test]
        public void DespikeRemovesASingleFrameSpikeAndKeepsTheSignal()
        {
            var s = Sine(60, 30f, 0.25f);
            float before = s.M[3][30];
            s.M[3][30] = before + 0.9f;
            int fixes = s.Despike(0.2f);
            Assert.GreaterOrEqual(fixes, 1);
            Assert.AreEqual(before, s.M[3][30], 0.08f);
        }

        [Test]
        public void GaussianReducesJitterButKeepsTheSlowMotion()
        {
            var s = Sine(120, 60f, 1f);
            var noisy = s.Clone();
            for (int i = 0; i < noisy.N; i++) noisy.M[3][i] += (i % 2 == 0 ? 0.08f : -0.08f);
            noisy.Gaussian(0.03f);
            float err = 0f; for (int i = 10; i < 110; i++) err += Mathf.Abs(noisy.M[3][i] - s.M[3][i]);
            Assert.Less(err / 100f, 0.02f, "the filter should remove the 30 Hz jitter");
        }

        [Test]
        public void ClampKeepsMusclesInsideTheHumanLimit()
        {
            var s = Sine(30, 30f, 1f); s.M[3][5] = 1.7f; s.M[3][6] = -1.4f;
            int c = s.ClampMuscles(1f);
            Assert.AreEqual(2, c);
            Assert.AreEqual(1f, s.M[3][5], 1e-5f); Assert.AreEqual(-1f, s.M[3][6], 1e-5f);
        }

        [Test]
        public void MirrorSwapsLeftAndRightAndFlipsTheRootAcrossTheBody()
        {
            int l = PoseSeq.Index("Left Upper Leg Front-Back"), r = PoseSeq.Index("Right Upper Leg Front-Back");
            Assert.GreaterOrEqual(l, 0); Assert.GreaterOrEqual(r, 0);
            var s = PoseSeq.Alloc(10, 30f, "m");
            for (int i = 0; i < 10; i++) { s.M[l][i] = 0.6f; s.M[r][i] = -0.2f; s.RQ[i] = Quaternion.identity; s.RT[i] = new Vector3(0.3f * i, 1f, 0f); }
            var m = s.MirrorAboutBody();
            Assert.AreEqual(-0.2f, m.M[l][4], 1e-4f);
            Assert.AreEqual(0.6f, m.M[r][4], 1e-4f);
            Assert.Less(m.RT[5].x, 0f, "a body moving to its right now moves to its left");
        }

        [Test]
        public void LoopFinderFindsTheCycleLength()
        {
            var s = Sine(300, 60f, 2f);   // period 0.5 s = 30 frames
            int start, len; float cost;
            ClipCleaner.FindLoop(s, 0.4f, 1.2f, out start, out len, out cost);
            Assert.Greater(len, 0);
            Assert.AreEqual(0, len % 30, "the loop length is a whole number of periods");
            Assert.Less(cost, 0.05f);
        }

        [Test]
        public void MakeLoopCrossFadesTheSeam()
        {
            var s = Sine(300, 60f, 0.7f);
            var loop = PoseSeq.MakeLoop(s, 40, 120, 12);
            Assert.AreEqual(120, loop.N);
            Assert.IsTrue(loop.Loop);
            // the last frame is a blend toward the frame just before the loop start: continuous with frame 0
            Assert.AreEqual(s.M[3][39 + 0], loop.M[3][119], 0.12f);
        }

        [Test]
        public void TrimKeepsTheRequestedWindow()
        {
            var s = Sine(120, 60f, 1f);
            var t = s.Trim(0.5f, 1.5f);
            Assert.AreEqual(61, t.N);
            Assert.AreEqual(s.M[3][30], t.M[3][0], 1e-5f);
        }

        [TestCase("Foo_A", "_M", "Foo_M_A")]
        [TestCase("Foo_B", "_s01", "Foo_s01_B")]
        [TestCase("Foo", "_M", "Foo_M")]
        public void TaggedNamesKeepThePairSuffixLast(string name, string tag, string expected)
        {
            Assert.AreEqual(expected, ClipCleaner.TaggedName(name, tag));
        }

        [TestCase("BGS__BGS_Sprint09_F_To_Stand_Relaxed", false)]
        [TestCase("BGS__MOB1_Run_L_90_BC", false)]
        [TestCase("BGS__MOB1_Jog_F_Loop_BC", true)]
        [TestCase("Transition_Stand_Idle_to_Run_135_deg_Right", false)]
        [TestCase("JogCycleJR", true)]
        [TestCase("RunCycleLeon_2", true)]
        [TestCase("BGS__BGS_FIX_MCU_am_Stand_Idle_05_HandsHips_Loop", true)]
        [TestCase("CatchNormalBackCHigh", false)]
        [TestCase("YAN__K_kickoff_kick_00_Rework", false)]
        public void LoopFlagsFollowTheClipName(string name, bool loop)
        {
            Assert.AreEqual(loop, AnimationImportSettings.IsLoopName(name));
        }

        [Test]
        public void NewAnimationKeysAreRegisteredConstants()
        {
            Assert.AreEqual("kick_holder", ArtKeys.KickHolder);
            Assert.AreEqual("bc_stop", ArtKeys.BcStop);
            Assert.AreEqual("ol_pocket_shuffle", ArtKeys.OlPocketShuffle);
            Assert.AreEqual("getup_face_up", ArtKeys.GetUpFaceUp);
            int n = 0; foreach (var f in typeof(ArtKeys).GetFields()) if (f.IsLiteral) n++;
            Assert.GreaterOrEqual(n, 190);
        }
    }
}
