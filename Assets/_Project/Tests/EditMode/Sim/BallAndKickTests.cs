using System;
using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>Ball physics (gravity, drag, bounces), pass launch solving, and the kicking game.</summary>
    public class BallAndKickTests
    {
        private const float Dt = 1f / 60f;

        // ---------------------------------------------------------------- ballistics

        private static float HeightAtRun(Vec3 from, Vec3 vel, Vec2 dir, float run)
        {
            Vec3 p = from, v = vel;
            for (int i = 0; i < 2000; i++)
            {
                Vec3 pp = p;
                BallPhysics.Advance(ref p, ref v, Dt);
                float prog = (p.X - from.X) * dir.X + (p.Y - from.Y) * dir.Y;
                if (prog >= run)
                {
                    float prev = (pp.X - from.X) * dir.X + (pp.Y - from.Y) * dir.Y;
                    float f = (run - prev) / Math.Max(1e-5f, prog - prev);
                    return pp.Z + (p.Z - pp.Z) * f;
                }
            }
            return -99f;
        }

        [Test]
        public void SolvedLaunchesPassThroughTheirTargetDespiteDrag()
        {
            var from = new Vec3(20f, 30f, 1.9f);
            foreach (float dist in new[] { 8f, 15f, 25f, 40f, 55f })
            {
                foreach (bool high in new[] { false, true })
                {
                    if (high && dist < 20f) continue; // short lobs are near-vertical and not a real throw
                    float speed = high ? (float)Math.Sqrt(SimMath.Gravity * dist / 0.94f) * 1.1f : 33f;
                    var to = new Vec3(20f + 5f, 30f + dist, 1.6f);
                    Vec3 vel; float tf;
                    bool ok = BallPhysics.SolveLaunch(from, to, speed, high, out vel, out tf);
                    if (!ok) continue; // out of range for the speed: covered by the range test
                    Vec2 dir = (to.XY - from.XY).Norm();
                    float h = HeightAtRun(from, vel, dir, (to.XY - from.XY).Length);
                    Assert.AreEqual(1.6f, h, 0.15f, "dist " + dist + (high ? " lob" : " flat"));
                    Assert.Greater(tf, 0.2f);
                }
            }
        }

        [Test]
        public void DragShortensTheFlight()
        {
            // same launch with and without drag: the draggy ball lands short
            Vec3 pos = new Vec3(0f, 0f, 1.9f), vel = new Vec3(0f, 25f, 12f);
            Vec3 p1 = pos, v1 = vel;
            float tVacuum = 0f;
            Vec3 p2 = pos; Vec3 v2 = vel;
            for (int i = 0; i < 600; i++)
            {
                // vacuum
                v1 = new Vec3(v1.X, v1.Y, v1.Z - SimMath.Gravity * Dt);
                p1 = p1 + v1 * Dt;
                BallPhysics.Advance(ref p2, ref v2, Dt);
                if (p1.Z <= 0f && tVacuum == 0f) tVacuum = i * Dt;
                if (p1.Z <= 0f && p2.Z <= 0f) break;
            }
            Assert.Less(p2.Y, p1.Y - 3f, "air resistance takes yards off a 50-yard throw");
        }

        [Test]
        public void ThrowStylesTradeSpeedForArc()
        {
            var from = new Vec3(26f, 30f, 1.95f);
            var to = new Vec3(26f, 60f, 1.7f); // 30-yard throw
            float tBullet, tTouch, tLob, apexB, apexT, apexL;
            float Flight(ThrowStyle s, out float apex)
            {
                bool high;
                float v = OffenseAI.StyleSpeed(s, 30f, 85, out high);
                Vec3 vel; float tf;
                BallPhysics.SolveLaunch(from, to, v, high, out vel, out tf);
                apex = 0f;
                Vec3 p = from, w = vel;
                for (int i = 0; i < 400; i++)
                {
                    BallPhysics.Advance(ref p, ref w, Dt);
                    apex = Math.Max(apex, p.Z);
                    if (p.Y >= to.Y) break;
                }
                return tf;
            }
            tBullet = Flight(ThrowStyle.Bullet, out apexB);
            tTouch = Flight(ThrowStyle.Touch, out apexT);
            tLob = Flight(ThrowStyle.Lob, out apexL);
            Assert.Less(tBullet, tTouch);
            Assert.Less(tTouch, tLob);
            Assert.Less(apexB, apexT);
            Assert.Less(apexT, apexL);
            Assert.Greater(apexL, 8f, "a lob goes up well over a defender's head");
        }

        [Test]
        public void ThrowPowerSetsLaunchSpeedAndRange()
        {
            Assert.Less(BallPhysics.MaxThrowSpeed(40), BallPhysics.MaxThrowSpeed(70));
            Assert.Less(BallPhysics.MaxThrowSpeed(70), BallPhysics.MaxThrowSpeed(99));
            var from = new Vec3(26f, 30f, 1.95f);
            var far = new Vec3(26f, 100f, 1.7f); // a 70-yard bomb
            Vec3 vel; float tf;
            Assert.IsTrue(BallPhysics.SolveLaunch(from, far, BallPhysics.MaxThrowSpeed(99), true, out vel, out tf), "a cannon arm reaches 70 yards");
            Assert.IsFalse(BallPhysics.SolveLaunch(from, far, BallPhysics.MaxThrowSpeed(30), true, out vel, out tf), "a weak arm cannot");
        }

        // ---------------------------------------------------------------- bouncing

        private static List<float> Bounce(int seed, out float restTime)
        {
            var ball = new BallSim(new Rng(seed));
            ball.MakeLoose(new Vec3(10f, 10f, 3f), new Vec3(3f, 4f, 0f));
            var apexes = new List<float>();
            float apex = 0f; bool wasUp = false;
            restTime = -1f;
            for (int i = 0; i < 60 * 12; i++)
            {
                float vz = ball.Vel.Z;
                ball.Step(Dt);
                if (ball.Pos.Z > apex) apex = ball.Pos.Z;
                if (ball.GroundContactThisStep && !ball.AtRest) { apexes.Add(apex); apex = 0f; wasUp = true; }
                if (ball.AtRest) { restTime = (i + 1) * Dt; break; }
            }
            if (!wasUp) apexes.Add(apex);
            return apexes;
        }

        [Test]
        public void LooseBallBouncesLowerEachTimeAndComesToRest()
        {
            float rest;
            List<float> apexes = Bounce(5, out rest);
            Assert.Greater(apexes.Count, 1, "it bounces");
            Assert.Less(apexes[apexes.Count - 1], apexes[0] + 0.01f);
            Assert.Greater(rest, 0f, "it stops");
            Assert.Less(rest, 10f);
        }

        [Test]
        public void FootballBouncesAreIrregularButDeterministicPerSeed()
        {
            float r1, r2, r3;
            var a = Bounce(11, out r1); var b = Bounce(11, out r2); var c = Bounce(12, out r3);
            Assert.AreEqual(r1, r2, 1e-6f);
            Assert.AreEqual(a.Count, b.Count);
            bool different = r1 != r3 || a.Count != c.Count;
            for (int i = 0; i < Math.Min(a.Count, c.Count) && !different; i++) different = Math.Abs(a[i] - c[i]) > 1e-4f;
            Assert.IsTrue(different, "other seeds bounce differently");

            // lateral kick: bounces scatter the travel direction
            var yaw = new List<float>();
            for (int s = 0; s < 40; s++)
            {
                var ball = new BallSim(new Rng(100 + s));
                ball.MakeLoose(new Vec3(10f, 10f, 2.5f), new Vec3(0f, 6f, 0f));
                for (int i = 0; i < 90; i++) { ball.Step(Dt); if (ball.Bounces >= 2) break; }
                yaw.Add(ball.Vel.X);
            }
            float min = float.MaxValue, max = float.MinValue;
            foreach (float y in yaw) { min = Math.Min(min, y); max = Math.Max(max, y); }
            Assert.Greater(max - min, 0.3f, "football bounces wander sideways");
        }

        // ---------------------------------------------------------------- goal posts

        [Test]
        public void GoalPostGeometryIsTheSameForProAndCollege()
        {
            Assert.AreEqual(18.5f, GoalPost.WidthFeet, 1e-4f);
            Assert.AreEqual(18.5f / 3f, GoalPost.WidthYards, 1e-4f);
            Assert.AreEqual(10f / 3f, GoalPost.CrossbarYards, 1e-4f);
            float cx = FieldGeometry.CenterX;
            Assert.IsTrue(GoalPost.IsGood(cx, 6f), "dead center, well above the bar");
            Assert.IsTrue(GoalPost.IsGood(cx + 2.9f, 5f), "just inside the right upright");
            Assert.IsFalse(GoalPost.IsGood(cx + 3.3f, 5f), "wide right");
            Assert.IsFalse(GoalPost.IsGood(cx - 3.3f, 5f), "wide left");
            Assert.IsFalse(GoalPost.IsGood(cx, 3.0f), "under the crossbar");
            Assert.IsFalse(GoalPost.IsGood(cx, 0.5f), "short");
            Assert.IsFalse(GoalPost.IsGood(cx, 45f), "over the top of the uprights");
            // both rule sets use the same posts
            Assert.AreEqual(RuleSet.Pro().HashWidthFeet < RuleSet.College().HashWidthFeet, true);
        }

        // ---------------------------------------------------------------- field goals

        private static float FieldGoalRate(int spot, int power, int accuracy, int trials, int seedBase, RuleSet rules = null)
        {
            rules = rules ?? RuleSet.Pro();
            int good = 0;
            for (int s = 0; s < trials; s++)
            {
                var gs = SimTestKit.Scrimmage(rules, spot, 4, 8);
                var rng = new Rng(seedBase + s);
                var off = SimTestKit.Special("Field Goal"); var def = SimTestKit.Special("Field Goal Block Middle");
                var ro = SimTestKit.Roster(SimTestKit.Playbook.FindFormation(off.FormationName), 75, rng);
                var rd = SimTestKit.Roster(SimTestKit.Playbook.FindFormation(def.FormationName), 75, rng);
                ro[0].Ratings.KickPower = power; ro[0].Ratings.KickAccuracy = accuracy;
                var sim = new PlaySimulation();
                sim.Setup(gs, off, def, ro, rd, rules, rng.Fork(), new PlaySimOptions { Penalties = false });
                PlayResult r = sim.RunToCompletion();
                Assert.AreEqual(PlayType.FieldGoal, r.Type);
                if (r.Good) good++;
            }
            return good / (float)trials;
        }

        [Test]
        public void FieldGoalSuccessFallsWithDistance()
        {
            float close = FieldGoalRate(90, 80, 80, 60, 1000);   // ~27 yards
            float mid = FieldGoalRate(75, 80, 80, 60, 2000);     // ~42 yards
            float far = FieldGoalRate(63, 80, 80, 60, 3000);     // ~54 yards
            TestContext.WriteLine("FG rates 27/42/54 yd: " + close + " " + mid + " " + far);
            Assert.GreaterOrEqual(close, 0.88f);
            Assert.Less(far, close - 0.12f);
            Assert.Less(far, mid + 0.02f);
            Assert.GreaterOrEqual(mid, 0.6f);
        }

        [Test]
        public void KickPowerExtendsRangeAndAccuracyKeepsItStraight()
        {
            float strong = FieldGoalRate(60, 97, 85, 60, 4000); // ~57 yards
            float weak = FieldGoalRate(60, 45, 85, 60, 4000);
            Assert.Greater(strong, weak + 0.25f, "a big leg makes the long ones");
            float accurate = FieldGoalRate(72, 85, 97, 80, 5000);
            float wild = FieldGoalRate(72, 85, 30, 80, 5000);
            Assert.Greater(accurate, wild + 0.15f, "accuracy decides the make");
        }

        [Test]
        public void CollegeAndProFieldGoalsUseTheSameGoalPosts()
        {
            float pro = FieldGoalRate(78, 80, 80, 60, 6000, RuleSet.Pro());
            float college = FieldGoalRate(78, 80, 80, 60, 6000, RuleSet.College());
            Assert.AreEqual(pro, college, 0.2f);
        }

        [Test]
        public void ExtraPointsAreUsuallyGood()
        {
            int good = 0, n = 80;
            for (int s = 0; s < n; s++)
            {
                var rules = RuleSet.Pro();
                var sim = SimTestKit.Kick("Extra Point", "Field Goal Block Middle", 7000 + s, SimTestKit.PointAfter(rules));
                PlayResult r = sim.RunToCompletion();
                Assert.AreEqual(PlayType.PAT, r.Type);
                if (r.Good) good++;
            }
            Assert.GreaterOrEqual(good / (float)n, 0.85f);
        }

        [Test]
        public void FieldGoalsEmitKickEventsAndResolveAtThePosts()
        {
            var gs = SimTestKit.Scrimmage(RuleSet.Pro(), 80, 4, 5);
            var sim = SimTestKit.Kick("Field Goal", "Field Goal Block Middle", 31, gs);
            sim.RunToCompletion();
            Assert.IsTrue(sim.Log.Has(SimEventType.Kick));
            Assert.IsTrue(sim.Log.Has(SimEventType.FieldGoalGood) || sim.Log.Has(SimEventType.FieldGoalMissed) || sim.Log.Has(SimEventType.KickBlocked));
        }

        // ---------------------------------------------------------------- punts and kickoffs

        [Test]
        public void PuntsTravelRealisticDistanceAndHangTime()
        {
            var rules = RuleSet.Pro();
            var gross = new List<int>(); var hang = new List<float>();
            for (int s = 0; s < 40; s++)
            {
                var gs = SimTestKit.Scrimmage(rules, 30, 4, 8);
                var sim = SimTestKit.Kick("Punt Standard", "Punt Return Set", 8000 + s, gs, 75, 75, rules);
                PlayResult r = sim.RunToCompletion();
                if (r.Type != PlayType.Punt || sim.KickBlocked) continue;
                gross.Add(r.Yards);
                var kick = sim.Log.First(SimEventType.Kick);
                float tLand = -1f;
                foreach (var e in sim.Log.Events)
                    if (e.Time > kick.Value.Time && (e.Type == SimEventType.Catch || e.Type == SimEventType.KickLanded || e.Type == SimEventType.Drop)) { tLand = e.Time; break; }
                if (tLand > 0f) hang.Add(tLand - kick.Value.Time);
            }
            Assert.Greater(gross.Count, 25);
            float avg = 0f; foreach (int g in gross) avg += g; avg /= gross.Count;
            float avgHang = 0f; foreach (float h in hang) avgHang += h; avgHang /= Math.Max(1, hang.Count);
            TestContext.WriteLine("punt gross " + avg + " hang " + avgHang);
            Assert.That(avg, Is.InRange(38f, 58f));
            Assert.That(avgHang, Is.InRange(3.4f, 5.6f));
        }

        [Test]
        public void StrongerPuntersKickFarther()
        {
            float Avg(int power, int seedBase)
            {
                float total = 0f; int n = 0;
                for (int s = 0; s < 30; s++)
                {
                    var rules = RuleSet.Pro();
                    var off = SimTestKit.Special("Punt Standard"); var def = SimTestKit.Special("Punt Return Set");
                    var rng = new Rng(seedBase + s);
                    var ro = SimTestKit.Roster(SimTestKit.Playbook.FindFormation(off.FormationName), 75, rng);
                    var rd = SimTestKit.Roster(SimTestKit.Playbook.FindFormation(def.FormationName), 75, rng);
                    ro[0].Ratings.KickPower = power;
                    var sim = new PlaySimulation();
                    sim.Setup(SimTestKit.Scrimmage(rules, 30, 4, 8), off, def, ro, rd, rules, rng.Fork(), new PlaySimOptions { Penalties = false });
                    PlayResult r = sim.RunToCompletion();
                    if (sim.KickBlocked) continue;
                    total += r.Yards; n++;
                }
                return total / Math.Max(1, n);
            }
            Assert.Greater(Avg(95, 9000), Avg(40, 9000) + 5f);
        }

        [Test]
        public void KickoffsGoDeepAndOnsideKicksStayShort()
        {
            var rules = RuleSet.Pro();
            float deepTotal = 0f; int touchbacks = 0, n = 30;
            for (int s = 0; s < n; s++)
            {
                var gs = new GameState(rules, Core.Team.Away);
                var sim = SimTestKit.Kick("Kickoff Deep Middle", "Kick Return Middle Wedge", 9500 + s, gs, 75, 75, rules);
                PlayResult r = sim.RunToCompletion();
                Assert.AreEqual(PlayType.Kickoff, r.Type);
                deepTotal += r.Yards;
                if (r.Touchback) touchbacks++;
            }
            Assert.That(deepTotal / n, Is.InRange(55f, 80f));
            Assert.Greater(touchbacks, 2);

            float onsideTotal = 0f;
            for (int s = 0; s < n; s++)
            {
                var gs = new GameState(rules, Core.Team.Away);
                var sim = SimTestKit.Kick("Onside Kick Short", "Kick Return Middle Wedge", 9700 + s, gs, 75, 75, rules);
                PlayResult r = sim.RunToCompletion();
                onsideTotal += r.Yards;
            }
            Assert.Less(onsideTotal / n, 25f, "the onside kick is a short, bouncing kick");
        }

        [Test]
        public void KickResultsAreAcceptedByTheGameState()
        {
            var rules = RuleSet.Pro();
            for (int s = 0; s < 20; s++)
            {
                var ko = new GameState(rules, Core.Team.Away);
                var sim = SimTestKit.Kick("Kickoff Deep Left", "Kick Return Left", 10000 + s, ko.Clone(), 75, 75, rules);
                PlayResult r = sim.RunToCompletion();
                Assert.DoesNotThrow(() => ko.Apply(r), "kickoff " + s);
                Assert.IsTrue(ko.Phase == GamePhase.Scrimmage || ko.Phase == GamePhase.PointAfter);

                var pg = SimTestKit.Scrimmage(rules, 35, 4, 7);
                var psim = SimTestKit.Kick("Punt Standard", "Punt Return Safe", 10100 + s, pg.Clone(), 75, 75, rules);
                PlayResult pr = psim.RunToCompletion();
                Assert.DoesNotThrow(() => pg.Apply(pr), "punt " + s);

                var fg = SimTestKit.Scrimmage(rules, 78, 4, 6);
                var fsim = SimTestKit.Kick("Field Goal", "Field Goal Block Edge", 10200 + s, fg.Clone(), 75, 75, rules);
                PlayResult fr = fsim.RunToCompletion();
                Assert.DoesNotThrow(() => fg.Apply(fr), "field goal " + s);
            }
        }
    }
}
