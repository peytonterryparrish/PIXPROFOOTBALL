using NUnit.Framework;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.13.0 NATURAL-3: the trunk angular speed cap (<see cref="SpineRate"/>) and contact-aware take switching (<see cref="MotionDb.Search"/> contact penalty). Pure data, no licensed file needed.</summary>
    public class Natural3Tests
    {
        [Test]
        public void TheCapTurnsTheChestTowardsItsTargetInSmallStepsAndNeverOvershoots()
        {
            Quaternion prev = Quaternion.identity, target = Quaternion.Euler(0f, 60f, 0f);
            float maxDeg = SpineRate.CapDegS / 60f;
            Quaternion cur = prev; int frames = 0;
            while (Quaternion.Angle(cur, target) > 0.01f && frames < 20)
            {
                Quaternion next = SpineRate.Limit(cur, target, maxDeg);
                Assert.LessOrEqual(Quaternion.Angle(cur, next), maxDeg + 0.05f, "no frame turns faster than the cap");
                Assert.LessOrEqual(Quaternion.Angle(next, target), Quaternion.Angle(cur, target) + 1e-4f, "never away from the target");
                cur = next; frames++;
            }
            Assert.That(frames, Is.InRange(3, 5), "a 60 degree pop becomes a three- or four-frame swing");
            Assert.AreEqual(0f, Quaternion.Angle(cur, target), 0.02f);
        }

        [Test]
        public void BelowTheCapTheChestIsLeftAlone()
        {
            Quaternion prev = Quaternion.Euler(10f, 20f, 5f), now = Quaternion.Euler(10f, 35f, 5f);
            Assert.AreEqual(0f, Quaternion.Angle(now, SpineRate.Limit(prev, now, SpineRate.CapDegS / 60f)), 1e-3f);
            Assert.Less(SpineRate.CapDegS, 1400f, "the cap sits under the audit's pop line");
        }

        [Test]
        public void ContactPenaltyKeepsAPlantedFootPlantedAcrossASwitch()
        {
            var db = ScriptableObject.CreateInstance<MotionDb>();
            db.feat = new float[8 * MotionDb.Dim]; db.vel = new float[16]; db.yawRate = new float[8]; db.contact = new byte[8]; db.clipOf = new int[8];
            db.mean = new float[MotionDb.Dim]; db.scale = new float[MotionDb.Dim]; for (int i = 0; i < MotionDb.Dim; i++) db.scale[i] = 1f;
            for (int c = 0; c < 2; c++)
            {
                db.clips.Add(new MotionDb.Clip { name = "c" + c, first = c * 4, count = 4, loop = true, speed = 3f });
                for (int k = 0; k < 4; k++) { int r = c * 4 + k; db.clipOf[r] = c; db.vel[r * 2 + 1] = 3f; db.feat[r * MotionDb.Dim + 11] = 3f; db.contact[r] = (byte)(c == 0 ? 2 : 1); }      // clip 0: right foot down, clip 1: left foot down
            }
            var q = new float[MotionDb.Dim]; q[11] = 3f;
            float best; int row = db.Search(q, 3f, 0.15f, 6f, 0, 0, out best);
            Assert.AreEqual(0, db.clipOf[row], "no contact information: the first wins");
            row = db.Search(q, 3f, 0.15f, 6f, 0, 0, out best, -1, 0f, null, 0, 0f, 1, 4f, -1);      // the left foot is down now
            Assert.AreEqual(1, db.clipOf[row], "a row with the left foot down is preferred");
            Assert.AreEqual(0f, best, 1e-5f);
            row = db.Search(q, 3f, 0.15f, 6f, 0, 0, out best, -1, 0f, null, 0, 0f, 1, 4f, 0);      // the playing take (clip 0) is never penalised
            Assert.AreEqual(0, db.clipOf[row]);
            Assert.AreEqual(0f, best, 1e-5f, "the playing take pays nothing");
            row = db.Search(q, 3f, 0.15f, 6f, 0, 0, out best, -1, 0f, null, 0, 0f, 3, 4f, -1);      // both feet down: either take drops one foot, they tie and the first wins
            Assert.AreEqual(4f, best, 1e-5f, "one foot of two differs: one penalty");
        }
    }
}
