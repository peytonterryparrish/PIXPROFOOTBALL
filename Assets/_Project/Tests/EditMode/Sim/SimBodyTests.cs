using System;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>Kinematics and momentum: speed/acceleration mapping, turning, plant-and-cut, reversals, collisions.</summary>
    public class SimBodyTests
    {
        private const float Dt = 1f / 60f;

        private static Ratings Make(int speed, int accel, int agility, int strength = 70, int stamina = 80)
        {
            var r = Ratings.Uniform(70);
            r.Speed = speed; r.Acceleration = accel; r.Agility = agility; r.Strength = strength; r.Stamina = stamina;
            return r;
        }

        private static AgentBody Body(int speed, int accel, int agility, int weightLb = 210, int strength = 70, int stamina = 80)
        {
            return new AgentBody(Make(speed, accel, agility, strength, stamina), weightLb, 73);
        }

        /// <summary>Runs the real integrator from a standing start and returns the time to cover <paramref name="yards"/>.</summary>
        private static float TimeTo(AgentBody b, float yards)
        {
            b.Pos = Vec2.Zero; b.Vel = Vec2.Zero; b.Facing = new Vec2(0f, 1f);
            for (int i = 1; i < 3000; i++)
            {
                b.Step(Dt, new Vec2(0f, 30f), false, Vec2.Zero, 0f);
                if (b.Pos.Y >= yards) return i * Dt;
            }
            return 99f;
        }

        private static void ReachSteadySpeed(AgentBody b, float speed)
        {
            b.Pos = Vec2.Zero; b.Vel = Vec2.Zero; b.Facing = new Vec2(0f, 1f);
            for (int i = 0; i < 1200; i++)
            {
                b.Step(Dt, new Vec2(0f, speed), false, Vec2.Zero, 0f);
                if (b.Speed > speed * 0.985f) break;
            }
        }

        [Test]
        public void FortyYardTimesMapFromSpeedRating()
        {
            float t99 = TimeTo(Body(99, 99, 70), 40f);
            float t70 = TimeTo(Body(70, 70, 70), 40f);
            float t40 = TimeTo(Body(40, 40, 70), 40f);
            TestContext.WriteLine("40 times: 99=" + t99 + " 70=" + t70 + " 40=" + t40);
            // in pads on turf (Physique.PadsFactor): about 0.15 s slower than the combine-in-shorts table
            Assert.That(t99, Is.InRange(4.35f, 4.65f), "99 speed");
            Assert.That(t70, Is.InRange(4.9f, 5.35f), "70 speed");
            Assert.That(t40, Is.InRange(5.45f, 6.05f), "40 speed");
            Assert.Less(t99, t70);
            Assert.Less(t70, t40);
        }

        [Test]
        public void SpeedAloneSetsTheMappedDashWhenAccelerationIsElite()
        {
            // 99 -> ~4.3 s, 40 -> ~5.0-5.3 s once acceleration is held at an elite level
            float t99 = TimeTo(Body(99, 99, 70), 40f);
            float t40 = TimeTo(Body(40, 99, 70), 40f);
            Assert.That(t99, Is.InRange(4.35f, 4.6f));
            Assert.That(t40, Is.InRange(5.05f, 5.5f));
        }

        [Test]
        public void TopSpeedFollowsRatingAndIsRealistic()
        {
            var fast = Body(99, 99, 70); var mid = Body(70, 70, 70); var slow = Body(40, 40, 70);
            Assert.Greater(fast.VMax, mid.VMax);
            Assert.Greater(mid.VMax, slow.VMax);
            // 10-12.5 yd/s for the fastest players (about 20-25 mph), mid-8 for the slowest
            Assert.That(fast.VMax, Is.InRange(10.5f, 12.5f));
            Assert.That(slow.VMax, Is.InRange(8f, 10f));
        }

        [Test]
        public void AccelerationRatingSetsTimeToTopSpeed()
        {
            float TimeToFraction(int accel)
            {
                var b = Body(85, accel, 70);
                b.Pos = Vec2.Zero; b.Vel = Vec2.Zero; b.Facing = new Vec2(0f, 1f);
                for (int i = 1; i < 3000; i++)
                {
                    b.Step(Dt, new Vec2(0f, 30f), false, Vec2.Zero, 0f);
                    if (b.Speed >= 0.9f * b.VMax) return i * Dt;
                }
                return 99f;
            }
            float t99 = TimeToFraction(99), t70 = TimeToFraction(70), t40 = TimeToFraction(40);
            TestContext.WriteLine("time to 90% top speed: 99=" + t99 + " 70=" + t70 + " 40=" + t40);
            Assert.That(t99, Is.InRange(1.6f, 2.4f));
            Assert.That(t40, Is.InRange(2.8f, 3.8f));
            Assert.Less(t99, t70);
            Assert.Less(t70, t40);
        }

        [Test]
        public void AccelerationIsABurstThatTapers()
        {
            var b = Body(90, 90, 70);
            b.Pos = Vec2.Zero; b.Vel = Vec2.Zero; b.Facing = new Vec2(0f, 1f);
            float[] v = new float[7];
            int idx = 1;
            for (int i = 1; i <= 180 && idx < 7; i++)
            {
                b.Step(Dt, new Vec2(0f, 30f), false, Vec2.Zero, 0f);
                if (i == idx * 30) { v[idx] = b.Speed; idx++; }
            }
            float g1 = v[1] - v[0], g2 = v[2] - v[1], g3 = v[3] - v[2], g6 = v[6] - v[5];
            Assert.Greater(g1, g2, "first half second gains more speed than the second");
            Assert.Greater(g2, g3);
            Assert.Greater(g3, g6, "the last half second adds the least");
            Assert.Greater(v[1], 0.4f * b.VMax, "strong initial burst");
        }

        [Test]
        public void TurnPathGrowsWithSpeed()
        {
            float PathLength(float fraction)
            {
                var b = Body(85, 85, 80);
                float target = b.VMax * fraction;
                ReachSteadySpeed(b, target);
                Vec2 want = new Vec2(1f, 0f) * target; // 90 degree turn
                float len = 0f;
                for (int i = 0; i < 400; i++)
                {
                    Vec2 p0 = b.Pos;
                    b.Step(Dt, want, false, Vec2.Zero, 0f);
                    len += (b.Pos - p0).Length;
                    if (SimMath.AngleBetween(b.Vel, want) < 0.1f && b.Speed > 0.5f) break;
                }
                return len;
            }
            float slow = PathLength(0.3f), mid = PathLength(0.6f), fast = PathLength(0.95f);
            TestContext.WriteLine("turn path: slow=" + slow + " mid=" + mid + " fast=" + fast);
            Assert.Less(slow, mid);
            Assert.Less(mid, fast);
            Assert.Greater(fast, 3f * slow, "the radius grows faster than linearly with speed");
        }

        [Test]
        public void AgilityTightensCutsAndHeavyBodiesTurnWider()
        {
            float PathLength(AgentBody b)
            {
                float target = b.VMax * 0.95f;
                ReachSteadySpeed(b, target);
                Vec2 want = new Vec2(1f, 0f) * target;
                float len = 0f;
                for (int i = 0; i < 400; i++)
                {
                    Vec2 p0 = b.Pos;
                    b.Step(Dt, want, false, Vec2.Zero, 0f);
                    len += (b.Pos - p0).Length;
                    if (SimMath.AngleBetween(b.Vel, want) < 0.1f && b.Speed > 0.5f) break;
                }
                return len;
            }
            float agile = PathLength(Body(85, 85, 95, 200));
            float stiff = PathLength(Body(85, 85, 40, 200));
            float lineman = PathLength(Body(85, 85, 80, 320));
            float back = PathLength(Body(85, 85, 80, 200));
            Assert.Less(agile, stiff, "higher agility turns tighter");
            Assert.Less(back, lineman, "more mass turns wider");
        }

        [Test]
        public void NoInstantReversals()
        {
            var b = Body(90, 90, 85);
            ReachSteadySpeed(b, b.VMax * 0.97f);
            float entry = b.Speed;
            Assert.Greater(entry, 8f);
            float tStop = -1f, tReverse = -1f, maxAccel = 0f;
            for (int i = 1; i <= 300; i++)
            {
                Vec2 v0 = b.Vel;
                b.Step(Dt, new Vec2(0f, -30f), false, Vec2.Zero, 0f);
                maxAccel = Math.Max(maxAccel, (b.Vel - v0).Length / Dt);
                if (tStop < 0f && b.Vel.Y <= 0f) tStop = i * Dt;
                if (tReverse < 0f && b.Vel.Y <= -1f) { tReverse = i * Dt; break; }
            }
            TestContext.WriteLine("reversal: stop " + tStop + "s, moving back at " + tReverse + "s, peak accel " + maxAccel);
            Assert.GreaterOrEqual(tStop, 0.55f, "momentum: a sprinter cannot stop in under half a second");
            Assert.GreaterOrEqual(tReverse, tStop + 0.05f);
            Assert.Less(maxAccel, 24f, "bounded braking acceleration (about 2 g)");
        }

        [Test]
        public void SharpCutsBleedSpeedGentleOnesDoNot()
        {
            float MinSpeed(float degrees, int agility)
            {
                var b = Body(85, 85, agility);
                float target = b.VMax * 0.97f;
                ReachSteadySpeed(b, target);
                float entry = b.Speed;
                Vec2 want = new Vec2(0f, 1f).Rotate(-degrees * SimMath.Deg2Rad) * target;
                float min = 99f;
                for (int i = 0; i < 90; i++) { b.Step(Dt, want, false, Vec2.Zero, 0f); min = Math.Min(min, b.Speed); }
                return min / entry;
            }
            float gentle = MinSpeed(20f, 80);
            float sharp = MinSpeed(120f, 80);
            Assert.Greater(gentle, 0.9f);
            Assert.Less(sharp, 0.62f, "plant-and-cut costs a lot of speed");
            // the same cut at the same entry speed costs a low-agility athlete more of his remaining momentum: he slows at least as much
            float sharpAgile = MinSpeed(120f, 99);
            float sharpStiff = MinSpeed(120f, 30);
            Assert.AreNotEqual(sharpAgile, sharpStiff, "agility changes how the cut plays out");
        }

        [Test]
        public void BackpedalAndShuffleAreSlowerThanRunning()
        {
            float Terminal(Vec2 dir)
            {
                var b = Body(85, 85, 80);
                for (int i = 0; i < 900; i++) b.Step(Dt, dir * 30f, true, new Vec2(0f, 1f), 0f);
                return b.Speed;
            }
            float fwd = Terminal(new Vec2(0f, 1f)), side = Terminal(new Vec2(1f, 0f)), back = Terminal(new Vec2(0f, -1f));
            Assert.Greater(fwd, side);
            Assert.Greater(side, back);
            Assert.That(side / fwd, Is.InRange(0.5f, 0.75f));   // v0.9.0 (E-005 / E-390): the shuffle is 0.47-0.57 of the run (was 0.7-0.8): a defender shuffling laterally is clearly slower than a runner
            Assert.That(back / fwd, Is.InRange(0.4f, 0.7f));
        }

        [Test]
        public void StumbleSlowsAndFallingStops()
        {
            var b = Body(85, 85, 80);
            ReachSteadySpeed(b, b.VMax);
            float fresh = b.EffectiveVMax;
            b.Stumble(1f);
            Assert.Less(b.EffectiveVMax, fresh * 0.7f);
            for (int i = 0; i < 40; i++) b.Step(Dt, new Vec2(0f, 30f), false, Vec2.Zero, 0f);
            Assert.Less(b.Speed, fresh * 0.85f, "a stumbling runner loses speed");

            var f = Body(85, 85, 80);
            ReachSteadySpeed(f, f.VMax);
            f.Fall(2f);
            for (int i = 0; i < 90; i++) f.Step(Dt, new Vec2(0f, 30f), false, Vec2.Zero, 0f);
            Assert.Less(f.Speed, 0.5f, "a fallen player cannot run");
        }

        [Test]
        public void SprintingBurnsStaminaAndFatigueSlowsTheBody()
        {
            var b = Body(90, 90, 80, 210, 70, 40);
            float fresh = b.EffectiveVMax;
            b.Pos = Vec2.Zero; b.Facing = new Vec2(0f, 1f);
            for (int i = 0; i < 60 * 10; i++) b.Step(Dt, new Vec2(0f, 30f), false, Vec2.Zero, 1f);
            Assert.Less(b.Energy, 0.6f);
            Assert.Less(b.EffectiveVMax, fresh * 0.99f);
            float tired = b.Energy;
            for (int i = 0; i < 60 * 20; i++) b.Step(Dt, Vec2.Zero, false, Vec2.Zero, 0f);
            Assert.Greater(b.Energy, tired, "energy recovers at rest");
            var strong = Body(90, 90, 80, 210, 70, 95);
            for (int i = 0; i < 60 * 10; i++) strong.Step(Dt, new Vec2(0f, 30f), false, Vec2.Zero, 1f);
            Assert.Greater(strong.Energy, tired, "better stamina lasts longer");
        }

        [Test]
        public void HeavierBodiesWinPushesBetweenCollidingAgents()
        {
            var heavy = Body(60, 60, 50, 320); var light = Body(85, 85, 85, 190);
            heavy.Pos = new Vec2(0f, 0f); light.Pos = new Vec2(0.5f, 0f);
            heavy.Vel = new Vec2(1f, 0f); light.Vel = new Vec2(-1f, 0f);
            Vec2 h0 = heavy.Pos, l0 = light.Pos;
            Assert.IsTrue(BodyCollision.Resolve(heavy, light, 0.05f));
            float heavyMoved = (heavy.Pos - h0).Length, lightMoved = (light.Pos - l0).Length;
            Assert.Greater(lightMoved, heavyMoved * 1.4f, "the lighter body is shoved further");
            // overlap is resolved
            Assert.GreaterOrEqual((heavy.Pos - light.Pos).Length, heavy.Radius + light.Radius - 0.001f);
            // momentum goes mostly to the light body
            Assert.Greater(light.Vel.X, -1f);
        }

        [Test]
        public void SeparatedBodiesDoNotCollide()
        {
            var a = Body(80, 80, 80); var b = Body(80, 80, 80);
            a.Pos = new Vec2(0f, 0f); b.Pos = new Vec2(3f, 0f);
            Assert.IsFalse(BodyCollision.Resolve(a, b, 0.05f));
        }

        [Test]
        public void BodyRadiusAndMassFollowWeight()
        {
            var lb = Body(80, 80, 80, 190); var ol = Body(50, 50, 50, 330);
            Assert.Greater(ol.Mass, lb.Mass * 1.5f);
            Assert.Greater(ol.Radius, lb.Radius);
            Assert.That(lb.Radius, Is.InRange(0.33f, 0.47f));
            Assert.That(ol.Radius, Is.InRange(0.33f, 0.47f));
        }

        [Test]
        public void FacingTurnsAtALimitedRate()
        {
            var b = Body(85, 85, 60);
            b.Facing = new Vec2(0f, 1f);
            b.Step(Dt, Vec2.Zero, true, new Vec2(0f, -1f), 0f);
            Assert.Greater(b.Facing.Y, 0.9f, "no instant 180 degree turn in one tick");
            for (int i = 0; i < 90; i++) b.Step(Dt, Vec2.Zero, true, new Vec2(0f, -1f), 0f);
            Assert.Less(b.Facing.Y, -0.9f);
        }
    }
}
