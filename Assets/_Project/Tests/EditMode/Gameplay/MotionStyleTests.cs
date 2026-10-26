using NUnit.Framework;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.13.0 NATURAL-2: the personal choice among the matcher's takes (<see cref="MotionStyle"/>) and the builder's list format. Pure data, no licensed file needed.</summary>
    public class MotionStyleTests
    {
        static MotionDb Make(params string[] names)
        {
            var db = ScriptableObject.CreateInstance<MotionDb>();
            for (int i = 0; i < names.Length; i++) db.clips.Add(new MotionDb.Clip { name = names[i], first = i * 4, count = 4, loop = true, speed = 3f });
            db.clips[1].mirrorOf = 0; db.clips[0].mirrorOf = 1;      // names[1] = mirror twin of names[0]
            return db;
        }

        [Test]
        public void FamiliesAreReadFromTheTakeNames()
        {
            Assert.AreEqual(1, MotionStyle.FamilyOf("MOB1_Jog_F_Loop_M"));
            Assert.AreEqual(0, MotionStyle.FamilyOf("Run_Medium_Run"));
            Assert.AreEqual(3, MotionStyle.FamilyOf("S100_Heavyset_FR_loop03"));
            for (int f = 2; f < MotionStyle.Families.Length; f++) Assert.AreEqual(f, MotionStyle.FamilyOf("S100_" + MotionStyle.Families[f] + "_BR_loop00_M"), MotionStyle.Families[f]);
            Assert.AreEqual(0, MotionStyle.FamilyOf("S100_Unknown_FR_loop00"));
            Assert.AreEqual(0, MotionStyle.FamilyOf(null));
        }

        [Test]
        public void ThePersonalCostIsFixedBoundedAndSharedWithTheMirrorTwin()
        {
            var db = Make("A", "A_M", "B", "C");
            var a = new float[4]; var b = new float[4];
            MotionStyle.Personal(db, 12345, PlayerRole.WR, a); MotionStyle.Personal(db, 12345, PlayerRole.WR, b);
            for (int i = 0; i < 4; i++) { Assert.AreEqual(a[i], b[i], "the same player always has the same choice"); Assert.GreaterOrEqual(a[i], 0f); Assert.LessOrEqual(a[i], MotionStyle.Spread + 1e-4f); }
            Assert.AreEqual(a[0], a[1], 1e-6f, "a take and its mirror twin share the draw");
            var c = new float[4]; MotionStyle.Personal(db, 54321, PlayerRole.WR, c);
            Assert.IsTrue(Mathf.Abs(a[2] - c[2]) > 1e-4f || Mathf.Abs(a[3] - c[3]) > 1e-4f, "two players differ");
        }

        [Test]
        public void TwoFavouriteFamiliesAreDistinctAndRoleWeighted()
        {
            int heavy = 0, rocket = 0;
            for (int id = 1; id <= 600; id++)
            {
                int a, b; MotionStyle.Favourites(id, PlayerRole.OL, out a, out b);
                Assert.AreNotEqual(a, b); Assert.That(a, Is.InRange(2, MotionStyle.Families.Length - 1)); Assert.That(b, Is.InRange(2, MotionStyle.Families.Length - 1));
                if (a == 3 || b == 3) heavy++;
                if (a == 9 || b == 9) rocket++;
            }
            Assert.Greater(heavy, rocket * 2, "linemen like the heavyset gait far more than the rocket one");
        }

        [Test]
        public void TheFamilyBonusMakesAFavouriteFamilyCheaper()
        {
            var names = new string[2 * (MotionStyle.Families.Length - 2)];
            for (int f = 2; f < MotionStyle.Families.Length; f++) { names[2 * (f - 2)] = "S100_" + MotionStyle.Families[f] + "_FR_loop00"; names[2 * (f - 2) + 1] = "S100_" + MotionStyle.Families[f] + "_FR_loop01"; }
            var db = ScriptableObject.CreateInstance<MotionDb>();
            for (int i = 0; i < names.Length; i++) db.clips.Add(new MotionDb.Clip { name = names[i], first = i * 4, count = 4, loop = true });
            int fa, fb; MotionStyle.Favourites(777, PlayerRole.DB, out fa, out fb);
            var p = new float[names.Length]; MotionStyle.Personal(db, 777, PlayerRole.DB, p);
            float fav = 0f, other = 0f; int nf = 0, no = 0;
            for (int i = 0; i < names.Length; i++) { int fam = MotionStyle.FamilyOf(names[i]); if (fam == fa || fam == fb) { fav += p[i]; nf++; } else { other += p[i]; no++; } }
            Assert.Less(fav / nf, other / no, "his two families are cheaper on average");
        }

        [Test]
        public void RecentUseCostsALittleAndDecays()
        {
            var db = Make("A", "A_M", "B", "C"); MotionStyle.ResetRecent();
            var personal = new float[4]; var tot = new float[4];
            MotionStyle.Personal(db, 9, PlayerRole.RB, personal);
            MotionStyle.Total(db, personal, tot);
            for (int i = 0; i < 4; i++) Assert.AreEqual(personal[i], tot[i], 1e-6f, "nothing used yet");
            MotionStyle.NoteUse(db, 2);
            MotionStyle.Total(db, personal, tot);
            Assert.Greater(tot[2], personal[2] + 0.5f * MotionStyle.RecentK);
            Assert.AreEqual(personal[3], tot[3], 1e-6f);
            for (int k = 0; k < 20; k++) MotionStyle.NoteUse(db, 2);
            MotionStyle.Total(db, personal, tot);
            Assert.LessOrEqual(tot[2] - personal[2], MotionStyle.RecentK * MotionStyle.RecentCap + 1e-4f, "a take that was entered often is capped");
            MotionStyle.ResetRecent();
        }

        [Test]
        public void SearchAddsTheBiasPerClip()
        {
            var db = ScriptableObject.CreateInstance<MotionDb>();
            db.feat = new float[8 * MotionDb.Dim]; db.vel = new float[16]; db.yawRate = new float[8]; db.contact = new byte[8]; db.clipOf = new int[8];
            db.mean = new float[MotionDb.Dim]; db.scale = new float[MotionDb.Dim]; for (int i = 0; i < MotionDb.Dim; i++) db.scale[i] = 1f;
            for (int c = 0; c < 2; c++)
            {
                db.clips.Add(new MotionDb.Clip { name = "c" + c, first = c * 4, count = 4, loop = true, speed = 3f });
                for (int k = 0; k < 4; k++) { int r = c * 4 + k; db.clipOf[r] = c; db.vel[r * 2 + 1] = 3f; db.feat[r * MotionDb.Dim + 11] = 3f; }
            }
            var q = new float[MotionDb.Dim]; q[11] = 3f;
            float best; int row = db.Search(q, 3f, 0.15f, 6f, 0, 0, out best);
            Assert.AreEqual(0, db.clipOf[row], "equal clips: the first wins");
            row = db.Search(q, 3f, 0.15f, 6f, 0, 0, out best, -1, 0f, new[] { 0.4f, 0f });
            Assert.AreEqual(1, db.clipOf[row], "a cost on clip 0 sends the search to clip 1");
            Assert.AreEqual(0f, best, 1e-5f);
            row = db.Search(q, 3f, 0.15f, 6f, 0, 0, out best, -1, 0f, new[] { 0.4f, 0.9f });
            Assert.AreEqual(0, db.clipOf[row]);
            Assert.AreEqual(0.4f, best, 1e-5f);
        }
    }
}
