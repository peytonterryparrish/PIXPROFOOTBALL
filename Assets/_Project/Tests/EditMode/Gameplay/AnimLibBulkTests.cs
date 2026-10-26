using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using PixFootball.Gameplay;

namespace PixFootball.Tests
{
    /// <summary>v0.13 (ANIMLIB-BULK): the no-repeat clip bag (every take of a pool is drawn before one repeats, the same (pool, seed) gets the same take, A / B twins share the index) and the contact take score counting every recent use.</summary>
    public class AnimLibBulkTests
    {
        bool _was;
        [SetUp] public void SetUp() { _was = ClipBag.Enabled; ClipBag.Enabled = true; ClipBag.Reset(); }
        [TearDown] public void TearDown() { ClipBag.Reset(); ClipBag.Enabled = _was; }

        static List<AnimationClip> Pool(int n) { var l = new List<AnimationClip>(); for (int i = 0; i < n; i++) l.Add(new AnimationClip { name = "c" + i }); return l; }
        static void Free(List<AnimationClip> l) { foreach (var c in l) Object.DestroyImmediate(c); }

        [Test]
        public void WalksTheWholePoolBeforeARepeat()
        {
            var pool = Pool(12);
            var seen = new HashSet<string>();
            for (int s = 1; s <= 12; s++) seen.Add(ClipBag.Draw("k", s * 7919, pool).name);
            Assert.AreEqual(12, seen.Count, "12 draws of 12 takes show 12 different takes");
            for (int s = 13; s <= 24; s++) ClipBag.Draw("k", s * 7919, pool);
            foreach (var c in pool) Assert.AreEqual(2, ClipBag.Uses(c.name), c.name);
            Free(pool);
        }

        [Test]
        public void TheSamePoolAndSeedAlwaysGetTheSameTake()
        {
            var pool = Pool(6);
            var a = ClipBag.Draw("k", 42, pool); var b = ClipBag.Draw("k", 42, pool);
            Assert.AreSame(a, b);
            Assert.AreEqual(1, ClipBag.Uses(a.name), "a repeated question is not a second use");
            Free(pool);
        }

        [Test]
        public void TwinsShareTheIndexAndDisabledFallsBackToTheSeed()
        {
            int a = ClipBag.Index("tackle_wrap", 99, 8), b = ClipBag.Index("tackle_wrap_b", 99, 8);
            Assert.AreEqual(a, b);
            ClipBag.Enabled = false;
            Assert.AreEqual(3, ClipBag.Index("x", 11, 8));
            var pool = Pool(5);
            Assert.AreSame(pool[2], ClipBag.Draw("k", 7, pool));
            Free(pool);
        }

        [Test]
        public void SeedZeroAndQuarterbackKeysKeepTheOldPick()
        {
            Assert.IsTrue(ClipBag.Skip("stance_safety", 0));
            Assert.IsTrue(ClipBag.Skip("qb_throw_bullet", 5));
            Assert.IsFalse(ClipBag.Skip("juke_left", 5));
        }

        [Test]
        public void EveryRecentUseOfATakeCostsScore()
        {
            var p = new PixFootball.Art.ContactTable.Pair { clipA = "TackleWrapFrontCHighDmo_A", clipB = "TackleWrapFrontCHighDmo_B", bearing = 0f };
            var s = new ContactSituation { Kind = ContactKind.Wrap, Bearing = 0f, Seed = 3, Avoid = new List<string>() };
            float none = TackleVariants.Score(p, s);
            s.Avoid = new List<string> { p.clipA };
            float once = TackleVariants.Score(p, s);
            s.Avoid = new List<string> { p.clipA, p.clipA, p.clipA, p.clipA };
            float many = TackleVariants.Score(p, s);
            Assert.Less(once, none); Assert.Less(many, once);
            Assert.AreEqual(none - once, 0.7f, 1e-4f);
        }

        [Test]
        public void PreSnapStancePoolsKeepTheSeedPick()
        {
            // v0.13 wave 2: the no-repeat walk is not applied to the stance pools (standing FACE +12 % in the wave-1 game)
            foreach (string k in new[] { "stance_cb_press", "stance_wr", "idle", "idle_alt", "dl_stance", "ol_stance", "cb_idle_m2m", "stance_wr_b" })
                Assert.IsTrue(ClipBag.Skip(k, 77), k);
            Assert.IsFalse(ClipBag.Skip("stance_to_sprint", 77));
            Assert.IsFalse(ClipBag.Skip("juke_left", 77));
        }

        [Test]
        public void StripTacklesAreBrokenTacklesAndContestedCatchesStayContested()
        {
            var strip = new PixFootball.Art.ContactTable.Pair { key = PixFootball.Art.ArtKeys.TackleStrip, clipA = "RAW__BC_StripTackle_Success_001_s01_A" };
            var fail = new PixFootball.Art.ContactTable.Pair { key = PixFootball.Art.ArtKeys.TackleStrip, clipA = "RAW__BC_StripTackle_Failure_001_s01_A" };
            var cc = new PixFootball.Art.ContactTable.Pair { key = PixFootball.Art.ArtKeys.CatchContested, clipA = "RAW__LB_ContestedCatchFront_CritSuccess_002_s01_A" };
            Assert.IsTrue(TackleVariants.Matches(strip, ContactKind.Broken), "a strip leaves both bodies standing: a broken tackle");
            Assert.IsFalse(TackleVariants.Matches(strip, ContactKind.Wrap), "nobody goes down in a strip");
            Assert.IsFalse(TackleVariants.Matches(fail, ContactKind.Broken), "a failed strip is not booked");
            Assert.IsTrue(TackleVariants.Matches(cc, ContactKind.Contested));
            Assert.IsFalse(TackleVariants.Matches(cc, ContactKind.Wrap));
        }
    }
}
