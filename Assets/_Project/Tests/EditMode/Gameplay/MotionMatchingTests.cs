using NUnit.Framework;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.10.0 MOTION: the pure parts of the locomotion matcher (database search, loop wrap, stride-limit penalty).</summary>
    public class MotionMatchingTests
    {
        static MotionDb Make(params (int count, bool loop, float vz)[] clips)
        {
            var db = ScriptableObject.CreateInstance<MotionDb>();
            int rows = 0; foreach (var c in clips) rows += c.count;
            db.feat = new float[rows * MotionDb.Dim]; db.vel = new float[rows * 2]; db.yawRate = new float[rows]; db.contact = new byte[rows]; db.clipOf = new int[rows];
            db.mean = new float[MotionDb.Dim]; db.scale = new float[MotionDb.Dim]; for (int i = 0; i < MotionDb.Dim; i++) db.scale[i] = 1f;
            int r = 0, ci = 0;
            foreach (var c in clips)
            {
                db.clips.Add(new MotionDb.Clip { name = "c" + ci, first = r, count = c.count, loop = c.loop, speed = c.vz });
                for (int k = 0; k < c.count; k++, r++)
                {
                    db.clipOf[r] = ci; db.vel[r * 2 + 1] = c.vz;
                    db.feat[r * MotionDb.Dim + 11] = c.vz;            // root velocity z
                    db.feat[r * MotionDb.Dim + 0] = k * 0.1f;         // a pose feature that tells the rows of one clip apart
                }
                ci++;
            }
            return db;
        }

        [Test]
        public void SearchFindsTheNearestRow()
        {
            var db = Make((5, true, 3f), (5, true, 6f));
            var q = new float[MotionDb.Dim]; q[11] = 5.8f; q[0] = 0.3f;
            float best; int row = db.Search(q, 5.8f, 0.15f, 6f, 0, 0, out best);
            Assert.AreEqual(5 + 3, row);
            Assert.Less(best, 0.1f);
        }

        [Test]
        public void MaskedSearchIgnoresThePoseDimensions()
        {
            var db = Make((5, true, 3f), (5, true, 6f));
            var q = new float[MotionDb.Dim]; q[11] = 3f; q[0] = 99f;       // the pose says nothing sensible
            float best; int row = db.Search(q, 3f, 0.15f, 6f, 10, 0, out best);
            Assert.AreEqual(0, db.clipOf[row]);
            Assert.Less(best, 1e-4f);
        }

        [Test]
        public void SpeedPenaltyIsZeroInsideTheStrideLimitAndGrowsOutside()
        {
            var db = Make((4, true, 5f));
            Assert.AreEqual(0f, db.SpeedPenalty(0, 5.5f, 0.15f, 6f));          // +10 percent
            Assert.AreEqual(0f, db.SpeedPenalty(0, 4.4f, 0.15f, 6f));          // -12 percent
            float a = db.SpeedPenalty(0, 6.5f, 0.15f, 6f), b = db.SpeedPenalty(0, 7.5f, 0.15f, 6f);
            Assert.Greater(a, 0f); Assert.Greater(b, a);
            Assert.AreEqual(0f, db.SpeedPenalty(0, 0.3f, 0.15f, 6f));          // standing: no gait speed to match
        }

        [Test]
        public void LoopsWrapAndOneShotsEnd()
        {
            var db = Make((4, true, 3f), (4, false, 3f));
            Assert.AreEqual(1, db.Next(0)); Assert.AreEqual(0, db.Next(3));    // loop: the last row continues at the first
            Assert.AreEqual(5, db.Next(4)); Assert.AreEqual(-1, db.Next(7));   // one-shot: no continuation after the last row
        }

        [Test]
        public void FeatureNormalisationRoundTrips()
        {
            var db = Make((2, true, 3f));
            for (int i = 0; i < MotionDb.Dim; i++) { db.mean[i] = i * 0.5f; db.scale[i] = 2f; }
            var raw = new float[MotionDb.Dim]; var n = new float[MotionDb.Dim];
            for (int i = 0; i < MotionDb.Dim; i++) raw[i] = i;
            db.Normalise(raw, n);
            for (int i = 0; i < MotionDb.Dim; i++) Assert.AreEqual((i - i * 0.5f) * 2f, n[i], 1e-5f);
        }

        // ---- v0.11.0

        [Test]
        public void TransitionTakesHaveNoSpeedPenalty()
        {
            // a stop take changes its speed through the clip: the root velocity and path features decide, the speed band does not (a loop is penalised outside +-15 percent)
            var db = Make((5, true, 3f), (5, false, 6f));
            db.clips[1].kind = (int)MotionDb.Kind.Stop;
            Assert.AreEqual(0f, db.SpeedPenalty(5, 2f, 0.15f, 6f), 1e-6f);
            Assert.Greater(db.SpeedPenalty(0, 5f, 0.15f, 6f), 0f);
        }

        [Test]
        public void ForbiddenKindsAreNotSearched()
        {
            var db = Make((5, true, 3f), (5, false, 3f));
            db.clips[1].kind = (int)MotionDb.Kind.Start;
            var q = new float[MotionDb.Dim]; q[11] = 3f; q[0] = 0.2f;
            float best; int row = db.Search(q, 3f, 0.15f, 6f, 0, MotionDb.TransitionKinds, out best);
            Assert.Less(row, 5, "a start take must not be picked while transitions are forbidden");
        }

        [Test]
        public void StagingDatabasePathFollowsTheCommandLine()
        {
            Assert.AreEqual(MotionDb.EditorPath, MotionDb.AssetPath);      // no -pixMmDb in the test run: the shipped database path
        }

        [Test]
        public void FinalIkDoorIsSafeWithoutABody()
        {
            Assert.DoesNotThrow(() => { PixIk.Hand(null, true, Vector3.zero, Quaternion.identity); PixIk.Foot(null, false, Vector3.zero, Quaternion.identity); PixIk.Body(null, Vector3.zero); PixIk.Late(null, 0.016f); PixIk.Release(null); });
        }
    }
}
