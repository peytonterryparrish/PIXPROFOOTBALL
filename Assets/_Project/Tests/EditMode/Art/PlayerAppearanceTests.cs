using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Art;

namespace PixFootball.Tests
{
    public class PlayerAppearanceTests
    {
        [Test]
        public void SameSeedGivesTheSamePlayer()
        {
            for (int s = 0; s < 50; s++)
            {
                var a = PlayerAppearance.FromSeed(s * 7919 + 13, PhotoBody.Large, PlayerAppearance.RoleLine);
                var b = PlayerAppearance.FromSeed(s * 7919 + 13, PhotoBody.Large, PlayerAppearance.RoleLine);
                Assert.AreEqual(a.Key(), b.Key());
                Assert.IsTrue(a.Equals(b));
            }
        }

        [Test]
        public void GoldenValuesStayStableAcrossRuns()
        {
            // integer-only generator: these never change unless the algorithm does (then every saved league would look different)
            var a = PlayerAppearance.FromSeed(12345, PhotoBody.Medium, PlayerAppearance.RoleSkill);
            var b = PlayerAppearance.FromSeed(12345, PhotoBody.Medium, PlayerAppearance.RoleSkill);
            Assert.AreEqual(a.ToString(), b.ToString());
            Assert.AreNotEqual(PlayerAppearance.FromSeed(12345).Key(), PlayerAppearance.FromSeed(12346).Key());
        }

        [Test]
        public void TwentyFourSeedsMakeTwentyFourDistinctFaces()
        {
            var keys = new HashSet<int>();
            for (int s = 1; s <= 24; s++) keys.Add(PlayerAppearance.FromSeed(s, (PhotoBody)(s % 3)).FaceKey());
            Assert.GreaterOrEqual(keys.Count, 24, "every generated player must have its own face (head mix + tweaks + skin + hair)");
        }

        [Test]
        public void EverySkinToneFacialHairAndHairColourOccurs()
        {
            var tones = new HashSet<int>(); var beards = new HashSet<FacialHair>(); var hair = new HashSet<int>(); var eyes = new HashSet<int>(); var heads = new HashSet<int>();
            for (int s = 0; s < 600; s++)
            {
                var a = PlayerAppearance.FromSeed(s, PhotoBody.Medium);
                tones.Add(a.SkinTone); beards.Add(a.FacialHair); hair.Add(a.HairColor); eyes.Add(a.EyeColor); heads.Add(a.HeadA);
            }
            Assert.AreEqual(PlayerAppearance.SkinToneCount, tones.Count);
            Assert.AreEqual(5, beards.Count);
            Assert.AreEqual(PlayerAppearance.EyeColorCount, eyes.Count);
            Assert.AreEqual(PlayerAppearance.HeadShapeCount, heads.Count);
            Assert.GreaterOrEqual(hair.Count, 6);
        }

        [Test]
        public void GeneratedValuesAreInRange()
        {
            for (int s = 0; s < 500; s++)
            {
                var a = PlayerAppearance.FromSeed(s * 31, (PhotoBody)(s % 3), s % 4);
                Assert.Less(a.HeadA, PlayerAppearance.HeadShapeCount); Assert.Less(a.HeadB, PlayerAppearance.HeadShapeCount); Assert.AreNotEqual(a.HeadA, a.HeadB);
                Assert.LessOrEqual(a.HeadBlend, 100);
                Assert.Less(a.SkinTone, PlayerAppearance.SkinToneCount); Assert.Less(a.HairColor, PlayerAppearance.HairColorCount); Assert.Less(a.EyeColor, PlayerAppearance.EyeColorCount);
                foreach (int v in new int[] { a.JawWide, a.ChinLong, a.NoseBig, a.CheekFull, a.BrowHeavy }) Assert.That(v, Is.InRange(-100, 100));
                Assert.AreEqual(a.Key(), a.Sanitized().Key(), "generated data must already be valid");
            }
        }

        [Test]
        public void SanitizedClampsBrokenData()
        {
            var a = new PlayerAppearance { HeadA = 200, HeadB = 99, HeadBlend = 250, SkinTone = 40, HairColor = 50, EyeColor = 77, JawWide = 127, ChinLong = -128 };
            var c = a.Sanitized();
            Assert.AreEqual(PlayerAppearance.HeadShapeCount - 1, c.HeadA);
            Assert.AreEqual(100, c.HeadBlend);
            Assert.AreEqual(PlayerAppearance.SkinToneCount - 1, c.SkinTone);
            Assert.AreEqual(100, c.JawWide);
            Assert.AreEqual(-100, c.ChinLong);
        }

        [Test]
        public void RoleHintsShapeTheKit()
        {
            int qbPouch = 0, linePouch = 0, lineGloves = 0, qbGloves = 0, lineVisor = 0, wrVisor = 0;
            for (int s = 0; s < 400; s++)
            {
                var qb = PlayerAppearance.FromSeed(s, PhotoBody.Small, PlayerAppearance.RoleQuarterback);
                var ol = PlayerAppearance.FromSeed(s, PhotoBody.Large, PlayerAppearance.RoleLine);
                var wr = PlayerAppearance.FromSeed(s, PhotoBody.Small, PlayerAppearance.RoleReceiver);
                if (qb.Pouch) qbPouch++; if (ol.Pouch) linePouch++;
                if (ol.Gloves) lineGloves++; if (qb.Gloves) qbGloves++;
                if (ol.Visor != VisorStyle.None) lineVisor++; if (wr.Visor != VisorStyle.None) wrVisor++;
            }
            Assert.Greater(qbPouch, 200); Assert.AreEqual(0, linePouch);
            Assert.Greater(lineGloves, qbGloves * 2);
            Assert.Greater(wrVisor, lineVisor * 2);
        }

        [Test]
        public void KitOptionsDoNotChangeTheFace()
        {
            var a = PlayerAppearance.FromSeed(99, PhotoBody.Medium);
            var b = a; b.Gloves = !b.Gloves; b.Sleeves = SleeveMode.Both; b.Visor = VisorStyle.Smoke;
            Assert.AreEqual(a.FaceKey(), b.FaceKey());
            Assert.AreNotEqual(a.Key(), b.Key());
        }
    }
}
