using NUnit.Framework;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.12.0 NATURAL: per-player movement style (seeded, bounded, varied) and the look distribution (limits, rates).</summary>
    public class NaturalTests
    {
        [Test]
        public void StyleIsDeterministicPerPlayer()
        {
            var a = NaturalStyle.For(4321, PlayerSize.Medium, PlayerRole.WR); var b = NaturalStyle.For(4321, PlayerSize.Medium, PlayerRole.WR);
            Assert.AreEqual(a.Tempo, b.Tempo); Assert.AreEqual(a.ArmSwing, b.ArmSwing); Assert.AreEqual(a.BreathHz, b.BreathHz); Assert.AreEqual(a.ShiftPeriod, b.ShiftPeriod);
            var c = NaturalStyle.For(4322, PlayerSize.Medium, PlayerRole.WR);
            Assert.AreNotEqual(a.Tempo, c.Tempo, "a neighbour has his own tempo");
        }

        [Test]
        public void StyleStaysInBoundsAndVariesAcrossPlayers()
        {
            float tMin = 9f, tMax = 0f, aMin = 9f, aMax = 0f, cMin = 9f;
            for (int id = 1; id <= 400; id++)
            {
                foreach (var sz in new[] { PlayerSize.Small, PlayerSize.Medium, PlayerSize.Large })
                {
                    var s = NaturalStyle.For(id, sz, PlayerRole.DB);
                    Assert.That(s.Tempo, Is.InRange(0.87f, 1.13f)); Assert.That(s.ArmSwing, Is.InRange(0.8f, 1.35f)); Assert.GreaterOrEqual(s.Counter, 0.9f); Assert.LessOrEqual(s.Counter, 1.6f);
                    Assert.That(s.HeadStab, Is.InRange(0.45f, 0.9f)); Assert.That(s.Posture, Is.InRange(-4f, 5f)); Assert.That(s.BreathHz, Is.InRange(0.18f, 0.36f)); Assert.That(s.ShiftPeriod, Is.InRange(5f, 11.5f));
                    tMin = Mathf.Min(tMin, s.Tempo); tMax = Mathf.Max(tMax, s.Tempo); aMin = Mathf.Min(aMin, s.ArmSwing); aMax = Mathf.Max(aMax, s.ArmSwing); cMin = Mathf.Min(cMin, s.Counter);
                }
            }
            Assert.Greater(tMax - tMin, 0.10f, "tempo spreads over the roster"); Assert.Greater(aMax - aMin, 0.3f, "arm swing spreads over the roster");
        }

        [Test]
        public void LinemenAreSlowerAndLessArmedThanReceivers()
        {
            float lt = 0f, st = 0f, la = 0f, sa = 0f;
            for (int id = 1; id <= 300; id++) { var l = NaturalStyle.For(id, PlayerSize.Large, PlayerRole.OL); var s = NaturalStyle.For(id, PlayerSize.Small, PlayerRole.WR); lt += l.Tempo; st += s.Tempo; la += l.ArmSwing; sa += s.ArmSwing; }
            Assert.Less(lt, st); Assert.Less(la, sa);
        }

        [Test]
        public void TempoWanderIsSmallAndNeverStops()
        {
            var s = NaturalStyle.For(77, PlayerSize.Medium, PlayerRole.LB);
            float lo = 9f, hi = 0f;
            for (float t = 0f; t < 30f; t += 0.1f) { float x = s.TempoAt(t); lo = Mathf.Min(lo, x); hi = Mathf.Max(hi, x); }
            Assert.Greater(hi - lo, 0.01f, "he is never at exactly one cadence"); Assert.Less(hi - lo, 0.14f);
        }

        [Test]
        public void SoftLimitIsBoundedMonotonicAndSigned()
        {
            Assert.AreEqual(20f, NaturalLook.SoftLimit(20f, 70f), 1e-4f, "inside the knee nothing changes");
            Assert.AreEqual(-20f, NaturalLook.SoftLimit(-20f, 70f), 1e-4f);
            float prev = 0f;
            for (float a = 0f; a <= 400f; a += 5f) { float r = NaturalLook.SoftLimit(a, 70f); Assert.GreaterOrEqual(r, prev - 1e-4f); Assert.LessOrEqual(r, 70f + 1e-3f); prev = r; }
            Assert.Less(NaturalLook.SoftLimit(179f, 35f), 35.001f); Assert.Less(NaturalLook.SoftLimit(-179f, 35f), 0f);
        }

        [Test]
        public void ShareGivesTheNeckFirstAndNeverPastTheLimits()
        {
            var small = NaturalLook.Share(30f);
            Assert.Greater(small.z, small.y); Assert.Greater(small.y, small.x); Assert.AreEqual(30f, small.x + small.y + small.z, 1e-3f, "a small look is fully reached");
            for (float g = -250f; g <= 250f; g += 5f)
            {
                var s = NaturalLook.Share(g);
                Assert.LessOrEqual(Mathf.Abs(s.z), NaturalLook.NeckMax + 1e-3f); Assert.LessOrEqual(Mathf.Abs(s.y), NaturalLook.ChestMax + 1e-3f); Assert.LessOrEqual(Mathf.Abs(s.x), NaturalLook.PelvisMax + 1e-3f);
                Assert.GreaterOrEqual(s.z * g, -1e-4f); Assert.GreaterOrEqual(s.y * g, -1e-4f); Assert.GreaterOrEqual(s.x * g, -1e-4f);
            }
            var big = NaturalLook.Share(200f);
            Assert.AreEqual(NaturalLook.NeckMax + NaturalLook.ChestMax + NaturalLook.PelvisMax, big.x + big.y + big.z, 1e-3f, "past the sum of the limits the rest is not reached");
        }

        [Test]
        public void TurnBuildsUpSettlesAndNeverExceedsItsRate()
        {
            float dt = 1f / 60f, yaw = 0f, vel = 0f, prevVel = 0f, peak = 0f, maxAccel = 0f; int frames = 0;
            while (Mathf.Abs(Mathf.DeltaAngle(yaw, 180f)) > 0.01f && frames < 600)
            {
                yaw = NaturalLook.TurnStep(yaw, 180f, ref vel, 400f, 4200f, dt); frames++;
                peak = Mathf.Max(peak, Mathf.Abs(vel)); maxAccel = Mathf.Max(maxAccel, Mathf.Abs(vel - prevVel) / dt); prevVel = vel;
            }
            Assert.Less(frames, 120, "a half turn takes well under two seconds"); Assert.Greater(frames, 20, "and not a snap");
            Assert.LessOrEqual(peak, 400f + 1e-2f); Assert.LessOrEqual(maxAccel, 4200f + 1f, "the rate builds up and falls off, it does not jump");
            Assert.AreEqual(180f, Mathf.Repeat(yaw, 360f), 0.02f);
            // a sign change of the target brakes through zero instead of reversing in one frame
            yaw = 0f; vel = 0f; for (int i = 0; i < 12; i++) yaw = NaturalLook.TurnStep(yaw, 90f, ref vel, 400f, 4200f, dt);
            float v0 = vel; yaw = NaturalLook.TurnStep(yaw, -90f, ref vel, 400f, 4200f, dt);
            Assert.Greater(Mathf.Abs(v0), 100f); Assert.Less(Mathf.Abs(vel - v0), 4200f * dt + 1e-2f);
        }

        [Test]
        public void FollowNeverExceedsTheRate()
        {
            float x = 0f; float dt = 1f / 60f;
            for (int i = 0; i < 30; i++) { float n = NaturalLook.Follow(x, 400f, NaturalLook.NeckRate, dt); Assert.LessOrEqual(Mathf.Abs(n - x), NaturalLook.NeckRate * dt + 1e-4f); x = n; }
            Assert.AreEqual(NaturalLook.NeckRate * dt * 30f, x, 1e-3f);
            Assert.AreEqual(5f, NaturalLook.Follow(4.9f, 5f, 600f, dt), 1e-5f, "arrives exactly");
        }
    }
}
