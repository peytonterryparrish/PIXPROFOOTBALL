using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.Art;
using PixFootball.EditorTools;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.13.0 QB-MOCAP: the draw of the quarterback's mocap windows (no repeat, deterministic), the catalog table and the library rules that make every quarterback take reachable.</summary>
    public class QbMocapTests
    {
        static QbClipTable.Entry E(string clip, string fam, float q = 80f) { return new QbClipTable.Entry { clip = clip, family = fam, quality = q, a = 0f, b = 2f }; }

        [Test]
        public void TheBagDrawsEveryClipOnceBeforeAnyRepeat()
        {
            QbMc.ResetBags();
            var pool = new List<QbClipTable.Entry> { E("a", "x"), E("b", "x"), E("c", "x"), E("d", "x") };
            var seen = new HashSet<string>();
            for (int i = 0; i < 4; i++) Assert.IsTrue(seen.Add(QbMc.Pick("t", pool, i * 13 + 5).clip), "a clip came twice before the bag was empty");
            Assert.AreEqual(4, seen.Count);
            Assert.IsNotNull(QbMc.Pick("t", pool, 99), "the bag refills");
            QbMc.ResetBags();
        }

        [Test]
        public void TheDrawIsTheSameForTheSameSeeds()
        {
            var pool = new List<QbClipTable.Entry>(); for (int i = 0; i < 9; i++) pool.Add(E("c" + i, "x"));
            QbMc.ResetBags(); var first = new List<string>(); for (int i = 0; i < 9; i++) first.Add(QbMc.Pick("s", pool, QbMc.Seed(i, 7)).clip);
            QbMc.ResetBags(); var second = new List<string>(); for (int i = 0; i < 9; i++) second.Add(QbMc.Pick("s", pool, QbMc.Seed(i, 7)).clip);
            CollectionAssert.AreEqual(first, second);
            Assert.AreEqual(9, new HashSet<string>(first).Count, "nine draws of a nine clip pool are nine different clips");
            QbMc.ResetBags();
        }

        [Test]
        public void ThePoolKeepsTheFamiliesAndDropsLowQuality()
        {
            var t = ScriptableObject.CreateInstance<QbClipTable>();
            t.entries.Add(E("good", QbFam.Pocket, 90f)); t.entries.Add(E("bad", QbFam.Pocket, 10f)); t.entries.Add(E("other", QbFam.StanceSg, 70f)); t.entries.Add(E("snap", QbFam.SnapSg, 70f));
            var p = QbMc.Pool(t, null, QbFam.Pocket, QbFam.StanceSg);
            Assert.AreEqual(2, p.Count); Assert.AreEqual("good", p[0].clip); Assert.AreEqual("other", p[1].clip);
            var onlyLong = QbMc.Pool(t, e => e.clip == "other", QbFam.Pocket, QbFam.StanceSg);
            Assert.AreEqual(1, onlyLong.Count);
            Object.DestroyImmediate(t);
        }

        [Test]
        public void TheSeedIsStableAndSpreads()
        {
            Assert.AreEqual(QbMc.Seed(12, 3), QbMc.Seed(12, 3));
            Assert.AreNotEqual(QbMc.Seed(12, 3), QbMc.Seed(13, 3));
            Assert.AreNotEqual(QbMc.Seed(12, 3), QbMc.Seed(12, 4));
            Assert.GreaterOrEqual(QbMc.Seed(-5, 9), 0);
        }

        [Test]
        public void EveryQuarterbackTakeNameHasAKeyInTheQbRules()
        {
            string[] names =
            {
                "BONES_QB_pass_quick_bullet_001_s01", "BONES_QB_shotgun_to_catch_center_001_s02", "BONES_QB_undercenter_to_catch_loop_002", "BONES_QB_random_pocket_001_s03", "BONES_QB_neutral_001", "BONES_QB_run_forward_pass_001_s03",
                "BONES_QB_ready_to_lookaround_001_s02", "BONES_QB_FakeHandoff_to_Roll_Left_001_s01_M", "BONES_QB_hail_mary_004_s04", "BONES_QB_kneel_001_s01", "BONES_QB_Spike_001_s03", "BONES_QB_slide_001_s02", "BONES_QB_celebration_002_s01",
                "BGS__QB_PassShovel", "BGS__QB_Pass_ShortLob_front", "BGS__QB_5StepDropback_into_stop", "YAN__scrim_QBShotgun_cadence_02", "YAN__scrim_QBUndrCtr_cadence_04", "QBSneak__QB_Sneak_001_A",
                "YAN__QB_undercener_catch_to_5_step_dropback_001", "QB_UNDERCENTER_SNAP_001", "pixqb_throw_set_passquickbullet001s01_0", "Bones_QB_dive_002_s02", "QB_pumpfake_pass_006_s01", "QuarterbackScrambleFwdLhand", "QBSidestepLeft",
                "BGS__BGS_QB_cadence_03_Clap", "QB_toss_003", "QB_falling_throw_006_s01_B", "RAW__QB_falling_throw_004_s01_A"
            };
            var missing = new List<string>();
            foreach (string n in names) if (ArtLibraryBuilder.QbKeyOf(n) == null) missing.Add(n);
            Assert.IsEmpty(missing, "no QB rule claims: " + string.Join(", ", missing.ToArray()));
        }

        [Test]
        public void TheQbRulesDoNotClaimOtherPositionsTakes()
        {
            foreach (string n in new[] { "Tackle_Wrap_001_A", "3pstance_aggressive_001", "RAW__Ball_Carrier_Juke_001", "CatchNormalFrontCMid", "DLINE_BullRushC_001_A", "Motus_Run_Fwd_01" })
                Assert.IsNull(ArtLibraryBuilder.QbKeyOf(n), n);
        }

        [Test]
        public void CatalogRowsAreWellFormed()
        {
            QbClipTable.ResetCache();
            var t = QbClipTable.Load();
            if (t == null || t.entries.Count == 0) Assert.Ignore("no PixQbClips table in this checkout (licensed data)");
            foreach (var e in t.entries)
            {
                Assert.IsFalse(string.IsNullOrEmpty(e.clip), "clip name");
                Assert.Less(e.a, e.b, e.clip + " window");
                Assert.GreaterOrEqual(e.quality, 0f); Assert.LessOrEqual(e.quality, 100f);
                if (e.sync > 0f) { Assert.GreaterOrEqual(e.sync, e.a, e.clip + " sync before its window"); Assert.LessOrEqual(e.sync, e.b, e.clip + " sync after its window"); }
                if (e.family == QbFam.ThrowSet || e.family == QbFam.ThrowRun || e.family == QbFam.ThrowOff) { Assert.Greater(e.sync, 0f, e.clip + " throw without a release"); Assert.IsTrue(e.hand == 'R' || e.hand == 'L', e.clip + " throw without a hand"); }
                if (e.path != null) Assert.AreEqual(0, e.path.Length % 2, e.clip + " path pairs");
            }
        }
    }
}
