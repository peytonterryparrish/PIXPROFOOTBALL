using System;
using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>Catch resolution from geometry: reach, duels, drops, deflections, interceptions, interference, sidelines, lead passes.</summary>
    public class CatchTests
    {
        private struct Outcome
        {
            public int Catches, Drops, Deflections, Interceptions, Fumbles, Incompletes, Penalties;
            public bool Completed;
            public bool Intercepted;
        }

        private sealed class Lab
        {
            public int RcvCatching = 80, RcvJumping = 75, RcvStrength = 70, RcvAwareness = 70;
            public int DefCatching = 60, DefCoverage = 70, DefJumping = 70, DefStrength = 70;
            public bool Defender = false; public bool DefFacesAway;
            public float DefDx, DefDy;
            public float BallSpeed = 22f;
            public float BallZ = 1.6f;
            public float LateralMiss = 0f;
            public float RcvX = 27f, RcvY = 48f;
            public RuleSet Rules = null;
            public bool LevelBall = true;
            public float Slick = 0f;
        }

        private static Outcome Trial(int seed, Lab lab)
        {
            RuleSet rules = lab.Rules ?? RuleSet.Pro();
            PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, seed, rules, null, lab.Slick > 0f ? new PlaySimOptions { BallSlickness = lab.Slick } : null);
            SimTestKit.StepToLive(sim);
            SimTestKit.ParkAll(sim, 2f);
            SimAgent r = sim.Agents[10];
            SimAgent d = sim.Agents[11 + 7];
            r.R.Catching = lab.RcvCatching; r.R.Jumping = lab.RcvJumping; r.R.Strength = lab.RcvStrength; r.R.Awareness = lab.RcvAwareness;
            d.R.Catching = lab.DefCatching; d.R.ZoneCoverage = lab.DefCoverage; d.R.ManCoverage = lab.DefCoverage; d.R.Jumping = lab.DefJumping; d.R.Strength = lab.DefStrength;
            SimTestKit.Place(r, lab.RcvX, lab.RcvY);
            r.Body.Facing = new Vec2(0f, -1f);
            if (lab.Defender) { SimTestKit.Place(d, lab.RcvX + lab.DefDx, lab.RcvY + lab.DefDy); if (lab.DefFacesAway) d.Body.Facing = new Vec2(0f, 1f); }
            // the quarterback is out of the picture
            sim.Qb.HasBall = false;
            sim.Qb.QbState = QbPhase.Done;
            Vec3 from = new Vec3(lab.RcvX + lab.LateralMiss, lab.RcvY - 6f, 1.9f);
            Vec3 to = new Vec3(lab.RcvX + lab.LateralMiss, lab.RcvY, lab.BallZ);
            Vec3 dv = to - from;
            float t = Math.Max(0.05f, dv.XY.Length / lab.BallSpeed);
            Vec3 vel = new Vec3(dv.X / t, dv.Y / t, (dv.Z + 0.5f * SimMath.Gravity * t * t) / t);
            sim.Ball.Launch(BallState.Pass, from, vel, 0);
            sim.OnPassReleased(sim.Qb, r, false);
            for (int i = 0; i < 70 && sim.Phase == PlayPhase.Live; i++)
            {
                sim.Step(default(ControlInput));
                if (sim.PassCompleted || sim.Turnover || sim.Ball.State == BallState.Dead) break;
            }
            var o = new Outcome();
            o.Catches = sim.Log.CountOf(SimEventType.Catch);
            o.Drops = sim.Log.CountOf(SimEventType.Drop);
            o.Deflections = sim.Log.CountOf(SimEventType.Deflection);
            o.Interceptions = sim.Log.CountOf(SimEventType.Interception);
            o.Fumbles = sim.Log.CountOf(SimEventType.Fumble);
            o.Incompletes = sim.Log.CountOf(SimEventType.Incomplete);
            o.Penalties = sim.Log.Has(SimEventType.Penalty) ? 1 : 0;
            o.Completed = sim.PassCompleted;
            o.Intercepted = sim.Turnover && sim.TurnoverInterception;
            return o;
        }

        private static float Rate(Lab lab, int trials, int seedBase, Func<Outcome, bool> pick)
        {
            int n = 0;
            for (int s = 0; s < trials; s++) if (pick(Trial(seedBase + s, lab))) n++;
            return n / (float)trials;
        }

        [Test]
        public void OpenReceiverMostlyCatchesAnAccuratePass()
        {
            var lab = new Lab { RcvCatching = 80 };
            float rate = Rate(lab, 150, 100, o => o.Completed);
            Assert.GreaterOrEqual(rate, 0.80f);
        }

        [Test]
        public void CatchingRatingDecidesHardCatches()
        {
            // a hot ball a yard off the hands
            var good = new Lab { RcvCatching = 97, BallSpeed = 34f, LateralMiss = 0.9f };
            var bad = new Lab { RcvCatching = 35, BallSpeed = 34f, LateralMiss = 0.9f };
            float g = Rate(good, 250, 300, o => o.Completed);
            float b = Rate(bad, 250, 300, o => o.Completed);
            TestContext.WriteLine("hard catches: good=" + g + " bad=" + b);
            Assert.Greater(g, b + 0.08f);
            Assert.Greater(Rate(bad, 250, 300, o => o.Drops > 0), Rate(good, 250, 300, o => o.Drops > 0), "poor hands drop more");
        }

        [Test]
        public void ContestedCatchProbabilityScalesWithBothPlayers()
        {
            var strongRcv = new Lab { Defender = true, DefDx = 0.9f, DefDy = 0.0f, RcvCatching = 95, RcvJumping = 90, RcvStrength = 85, DefCatching = 40, DefCoverage = 45, DefJumping = 45, DefStrength = 50 };
            var evenly = new Lab { Defender = true, DefDx = 0.9f, DefDy = 0.0f };
            var strongDef = new Lab { Defender = true, DefDx = 0.9f, DefDy = 0.0f, RcvCatching = 50, RcvJumping = 45, RcvStrength = 50, DefCatching = 92, DefCoverage = 95, DefJumping = 90, DefStrength = 85 };
            float a = Rate(strongRcv, 300, 500, o => o.Completed);
            float b = Rate(evenly, 300, 500, o => o.Completed);
            float c = Rate(strongDef, 300, 500, o => o.Completed);
            TestContext.WriteLine("contested catch rates: strong receiver=" + a + " even=" + b + " strong defender=" + c);
            Assert.Greater(a, b + 0.04f);
            Assert.Greater(b, c + 0.04f);
            float picksStrong = Rate(strongDef, 2500, 500, o => o.Intercepted);
            float picksWeak = Rate(strongRcv, 2500, 500, o => o.Intercepted);
            Assert.Greater(picksStrong, picksWeak, "better ball skills pick off more passes");
            float pbuStrong = Rate(strongDef, 300, 500, o => o.Deflections > 0);
            float pbuWeak = Rate(strongRcv, 300, 500, o => o.Deflections > 0);
            Assert.Greater(pbuStrong, pbuWeak, "better coverage breaks up more passes");
        }


        [Test]
        public void WetBallLowersTheCatchRateAndZeroSlicknessChangesNothing()
        {
            var dry = new Lab { RcvCatching = 70, BallSpeed = 30f, LateralMiss = 0.7f };
            var soaked = new Lab { RcvCatching = 70, BallSpeed = 30f, LateralMiss = 0.7f, Slick = 1f };
            var zero = new Lab { RcvCatching = 70, BallSpeed = 30f, LateralMiss = 0.7f, Slick = 0f };
            float d = Rate(dry, 300, 4100, o => o.Completed);
            float w = Rate(soaked, 300, 4100, o => o.Completed);
            float z = Rate(zero, 300, 4100, o => o.Completed);
            TestContext.WriteLine("catch rate dry=" + d + " soaked=" + w);
            Assert.Less(w, d, "a soaked ball is harder to catch");
            Assert.AreEqual(d, z, 1e-6f, "zero slickness is the calibrated simulation");
        }
        [Test]
        public void InterceptionsOnlyHappenWhenADefenderReachesTheBall()
        {
            var far = new Lab { Defender = true, DefDx = 14f, DefDy = 6f, DefCatching = 99, DefJumping = 99 };
            for (int s = 0; s < 250; s++)
            {
                Outcome o = Trial(700 + s, far);
                Assert.AreEqual(0, o.Interceptions, "seed " + s);
                Assert.AreEqual(0, o.Deflections, "seed " + s);
            }
            // a defender standing right in the throwing lane does get hands on it
            var lane = new Lab { Defender = true, DefDx = 0.15f, DefDy = -2.2f, DefCatching = 85, DefJumping = 85, BallSpeed = 20f };
            int touches = 0, picks = 0;
            for (int s = 0; s < 250; s++)
            {
                Outcome o = Trial(1000 + s, lane);
                if (o.Deflections > 0 || o.Interceptions > 0) touches++;
                if (o.Interceptions > 0) picks++;
            }
            Assert.Greater(touches, 20, "a defender in the lane tips or picks passes");
            Assert.Greater(picks, 0);
        }

        [Test]
        public void DefendersInFrontPickOffMoreThanTrailingOnes()
        {
            var front = new Lab { Defender = true, DefDx = 0.5f, DefDy = -1.2f, DefCatching = 85, DefCoverage = 85, RcvCatching = 70 };
            var trail = new Lab { Defender = true, DefDx = 0.5f, DefDy = 1.2f, DefCatching = 85, DefCoverage = 85, RcvCatching = 70 };
            float pf = Rate(front, 400, 1500, o => o.Intercepted);
            float pt = Rate(trail, 400, 1500, o => o.Intercepted);
            TestContext.WriteLine("pick rate in front=" + pf + " trailing=" + pt);
            Assert.Greater(pf, pt);
        }

        [Test]
        public void BallsAboveTheReachEnvelopeAreNotCaught()
        {
            var overhead = new Lab { BallZ = 4.6f, RcvJumping = 40 };
            float rate = Rate(overhead, 120, 2000, o => o.Completed);
            Assert.Less(rate, 0.1f);
            var chest = new Lab { BallZ = 1.5f };
            Assert.Greater(Rate(chest, 120, 2000, o => o.Completed), 0.8f);
        }

        [Test]
        public void JumpingExtendsTheReachForHighBalls()
        {
            var high = new Lab { BallZ = 3.15f, RcvJumping = 97, BallSpeed = 20f };
            var low = new Lab { BallZ = 3.15f, RcvJumping = 30, BallSpeed = 20f };
            float a = Rate(high, 200, 2500, o => o.Completed);
            float b = Rate(low, 200, 2500, o => o.Completed);
            TestContext.WriteLine("high ball: leaper=" + a + " flat-footed=" + b);
            Assert.Greater(a, b + 0.1f, "jumping now decides high balls (both used to score 0)");
        }

        [Test]
        public void ContactBehindTheReceiverCanDrawPassInterference()
        {
            var contact = new Lab { Defender = true, DefFacesAway = true, DefDx = 0.3f, DefDy = 0.9f, DefCoverage = 40, RcvCatching = 85 };
            int flags = 0, n = 500;
            for (int s = 0; s < n; s++) if (Trial(3000 + s, contact).Penalties > 0) flags++;
            Assert.Greater(flags, 0, "flags happen");
            Assert.Less(flags / (float)n, 0.3f, "but not on every contested ball");
        }

        [Test]
        public void ReceiversNeedFeetInBoundsOnTheSideline()
        {
            // one foot in bounds (pro) vs two feet (college): a receiver whose body is 0.3 yd inside the line
            var pro = new Lab { RcvX = 0.3f, RcvY = 48f, Rules = RuleSet.Pro(), RcvAwareness = 70 };
            var college = new Lab { RcvX = 0.3f, RcvY = 48f, Rules = RuleSet.College(), RcvAwareness = 70 };
            float p = Rate(pro, 80, 4000, o => o.Completed);
            float c = Rate(college, 80, 4000, o => o.Completed);
            Assert.Greater(p, 0.5f, "a pro receiver gets one foot down");
            Assert.Less(c, 0.2f, "college needs both feet");
            // well inside it is no issue under either rule
            var inside = new Lab { RcvX = 3f, RcvY = 48f, Rules = RuleSet.College() };
            Assert.Greater(Rate(inside, 80, 4000, o => o.Completed), 0.6f);
            // out of bounds is incomplete either way
            var outside = new Lab { RcvX = -0.8f, RcvY = 48f, Rules = RuleSet.Pro() };
            Assert.Less(Rate(outside, 60, 4000, o => o.Completed), 0.1f);
        }

        [Test]
        public void HitsInTrafficCanKnockTheBallLoose()
        {
            var traffic = new Lab { Defender = true, DefDx = 0.7f, DefDy = -0.3f, RcvCatching = 95, RcvJumping = 90, RcvStrength = 85, DefCatching = 40, DefCoverage = 55 };
            int fumbles = 0, completions = 0;
            for (int s = 0; s < 900; s++)
            {
                Outcome o = Trial(6000 + s, traffic);
                if (o.Completed) completions++;
                if (o.Fumbles > 0) fumbles++;
            }
            Assert.Greater(completions, 100);
            Assert.Greater(fumbles, 0, "catching it in a crowd costs the ball sometimes");
            Assert.Less(fumbles / (float)Math.Max(1, completions), 0.12f);
        }

        // ---------------------------------------------------------------- the quarterback leads receivers

        /// <summary>
        /// The user quarterback throws to the deepest eligible receiver with the secondary parked far away (a wide-open vertical);
        /// returns the closest the ball gets to him while it is low enough to catch. Receivers adjust to the ball, as in play.
        /// </summary>
        private static float LeadMiss(int accuracy, ThrowStyle style, int seed, out bool thrown)
        {
            var rules = RuleSet.Pro();
            var off = SimTestKit.Offense("Four Verticals", "Shotgun"); var def = SimTestKit.Defense("Cover 3 Sky", "4-3 Base");
            var rng = new Rng(seed);
            var ro = SimTestKit.Roster(SimTestKit.Playbook.FindFormation(off.FormationName), 75, rng);
            var rd = SimTestKit.Roster(SimTestKit.Playbook.FindFormation(def.FormationName), 20, rng);
            ro[0].Ratings.ThrowAccuracy = accuracy; ro[0].Ratings.ThrowPower = 99;
            var sim = new PlaySimulation();
            sim.Setup(SimTestKit.Scrimmage(rules), off, def, ro, rd, rules, rng.Fork(), new PlaySimOptions { Penalties = false, QbHoldsBall = true });
            SimTestKit.StepToLive(sim);
            while (sim.Time < 1.9f && sim.Phase == PlayPhase.Live) sim.Step(default(ControlInput));
            // the defense is parked far from the play so the receiver is wide open
            for (int i = 11; i < 22; i++) SimTestKit.Place(sim.Agents[i], 4f + (i - 11) * 4f, sim.LosY - 14f);
            int slot = -1; float deepest = -999f;
            for (int i = 1; i <= 10; i++) if (sim.Agents[i].IsReceiverEligible && sim.Agents[i].Pos.Y > deepest) { deepest = sim.Agents[i].Pos.Y; slot = i; }
            sim.Tick(1f / 60f, new ControlInput { ThrowToSlot = slot, ThrowStyle = style }, 0);
            SimAgent target = sim.Agents[slot];
            float best = float.MaxValue; thrown = false;
            for (int i = 0; i < 300 && sim.Phase == PlayPhase.Live; i++)
            {
                sim.Tick(1f / 60f, default(ControlInput), 0);
                if (sim.Ball.State == BallState.Pass)
                {
                    thrown = true;
                    float d = (sim.Ball.Pos.XY - target.Pos).Length;
                    if (sim.Ball.Pos.Z < 4f) best = Math.Min(best, d);
                }
                else if (thrown) break;
            }
            return best;
        }

        [Test]
        public void DeepBallsAreLedToTheReceiversArrivalPoint([Values(ThrowStyle.Auto, ThrowStyle.Bullet, ThrowStyle.Touch, ThrowStyle.Lob)] ThrowStyle style)
        {
            int ok = 0, n = 40;
            for (int s = 0; s < n; s++)
            {
                bool thrown;
                float best = LeadMiss(99, style, 8000 + s, out thrown);
                if (thrown && best < 1.5f) ok++;
            }
            TestContext.WriteLine("lead passes (" + style + ") within catch radius: " + ok + "/" + n);
            Assert.GreaterOrEqual(ok, (int)(n * 0.8f));
        }

        [Test]
        public void ThrowAccuracyShrinksTheErrorCone()
        {
            float Mean(int accuracy)
            {
                float sum = 0f; int c = 0;
                for (int s = 0; s < 60; s++)
                {
                    bool thrown;
                    float best = LeadMiss(accuracy, ThrowStyle.Auto, 9000 + s, out thrown);
                    if (thrown) { sum += Math.Min(best, 8f); c++; }
                }
                return sum / Math.Max(1, c);
            }
            float good = Mean(99), poor = Mean(20);
            TestContext.WriteLine("mean miss: accurate=" + good + " inaccurate=" + poor);
            Assert.Less(good + 0.04f, poor);
        }

        [Test]
        public void PressureWorsensTheThrow()
        {
            // same quarterback against a feeble and a ferocious rush: completion percentage drops under heat
            float Completion(int rushOvr, int blockOvr)
            {
                int comp = 0, att = 0;
                for (int s = 0; s < 120; s++)
                {
                    var rules = RuleSet.Pro();
                    var off = SimTestKit.Offense("Curl-Flat", "Shotgun"); var def = SimTestKit.Defense("Cover 3 Sky", "4-3 Base");
                    var rng = new Rng(11000 + s);
                    var ro = SimTestKit.Roster(SimTestKit.Playbook.FindFormation(off.FormationName), 75, rng);
                    var rd = SimTestKit.Roster(SimTestKit.Playbook.FindFormation(def.FormationName), 75, rng);
                    for (int i = 2; i <= 6; i++) { ro[i].Ratings.PassBlocking = blockOvr; ro[i].Ratings.Strength = Math.Min(99, blockOvr + 10); }
                    for (int i = 0; i < 4; i++) { rd[i].Ratings.BlockShedding = rushOvr; rd[i].Ratings.Strength = Math.Min(99, rushOvr + 5); }
                    var sim = new PlaySimulation();
                    sim.Setup(SimTestKit.Scrimmage(rules), off, def, ro, rd, rules, rng.Fork(), new PlaySimOptions { Penalties = false });
                    PlayResult r = sim.RunToCompletion();
                    if (r.Type == PlayType.Pass) { comp++; att++; }
                    else if (r.Type == PlayType.Incomplete || r.Type == PlayType.Interception || r.Type == PlayType.Sack) att++;
                }
                return comp / (float)Math.Max(1, att);
            }
            float clean = Completion(40, 95);
            float hot = Completion(95, 40);
            TestContext.WriteLine("completion clean=" + clean + " pressured=" + hot);
            Assert.Greater(clean, hot + 0.05f);
        }
    }
}
