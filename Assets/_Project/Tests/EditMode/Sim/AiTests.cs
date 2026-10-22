using System;
using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>Offense and defense decision-making: pursuit, man matching, zone drops, quarterback reads, blitzes.</summary>
    public class AiTests
    {
        // ---------------------------------------------------------------- pursuit

        private static AgentBody CloneBody(AgentBody src, Ratings r)
        {
            var b = new AgentBody(r, 205, 73);
            b.VMax = src.VMax; b.Tau = src.Tau; b.AccelCap = src.AccelCap; b.LatBase = src.LatBase; b.DecelBase = src.DecelBase;
            b.TurnRate = src.TurnRate; b.BackpedalFactor = src.BackpedalFactor; b.ShuffleFactor = src.ShuffleFactor;
            b.Mass = src.Mass; b.Radius = src.Radius;
            return b;
        }

        [Test]
        public void PursuitInterceptSolverBeatsANaiveChase()
        {
            float aiSum = 0f, naiveSum = 0f; int n = 0;
            for (int s = 0; s < 24; s++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 600 + s, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false });
                SimTestKit.StepToLive(sim);
                SimTestKit.ParkAll(sim, 2f);
                SimAgent c = null;
                for (int i = 1; i < 11 && c == null; i++) if (sim.Agents[i].Assign is CarryAssignment && !((CarryAssignment)sim.Agents[i].Assign).IsFake) c = sim.Agents[i];
                sim.Blocks.HandedOff = true;
                sim.GiveBall(c);
                SimTestKit.Place(c, 12f + (s % 4) * 3f, 40f, 0f, 5f); c.R.Speed = 70;
                SimAgent d = sim.Agents[11 + 5];
                float side = (s % 2 == 0) ? 1f : -1f;
                SimTestKit.Place(d, c.Pos.X + side * (5f + (s % 3) * 1.5f), 40f + 6f + (s % 5));
                var cPath = new List<Vec2>();
                var dPath = new List<Vec2>();
                // the ball carrier runs a straight line (user steered) while the defender pursues
                Vec2 dir = new Vec2(-side * 0.30f, 0.95f).Norm();
                c.ControlledByUser = false;
                float tAi = -1f;
                for (int i = 0; i < 240 && sim.Phase == PlayPhase.Live; i++)
                {
                    sim.Tick(1f / 60f, new ControlInput { Move = dir, Sprint = false }, c.Id);
                    cPath.Add(c.Pos); dPath.Add(d.Pos);
                    if (tAi < 0f && (c.Pos - d.Pos).Length < 2.3f) { tAi = (i + 1) / 60f; break; }
                    if (d.State == AgentState.Engaged || d.State == AgentState.Tackling) break;
                }
                if (tAi < 0f) tAi = cPath.Count / 60f + 0.5f;
                // naive: always run at where the carrier is now
                AgentBody nb = CloneBody(d.Body, d.R);
                nb.Pos = dPath[0]; nb.Vel = Vec2.Zero;
                float tNaive = -1f;
                for (int i = 0; i < cPath.Count; i++)
                {
                    Vec2 want = (cPath[i] - nb.Pos).Norm() * nb.EffectiveVMax * 0.985f;
                    nb.Step(1f / 60f, want, false, Vec2.Zero, 0f);
                    if ((cPath[i] - nb.Pos).Length < 2.3f) { tNaive = (i + 1) / 60f; break; }
                }
                if (tNaive < 0f) tNaive = cPath.Count / 60f + 1.0f;
                aiSum += tAi; naiveSum += tNaive; n++;
            }
            
            Assert.AreEqual(24, n);
            TestContext.WriteLine("mean time to contact: intercept AI=" + (aiSum / n) + " naive chase=" + (naiveSum / n) + " (n=" + n + ")");
            Assert.Less(aiSum / n, naiveSum / n - 0.05f);
        }

        [Test]
        public void PursuitAnglesTightenWithAwareness()
        {
            float TimeToContact(int awareness)
            {
                float sum = 0f; int n = 0;
                for (int s = 0; s < 24; s++)
                {
                    PlaySimulation sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 900 + s, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false });
                    SimTestKit.StepToLive(sim);
                    SimTestKit.ParkAll(sim, 2f);
                    SimAgent c = null;
                    for (int i = 1; i < 11 && c == null; i++) if (sim.Agents[i].Assign is CarryAssignment && !((CarryAssignment)sim.Agents[i].Assign).IsFake) c = sim.Agents[i];
                    sim.Blocks.HandedOff = true;
                    sim.GiveBall(c);
                    SimTestKit.Place(c, 12f + (s % 4) * 3f, 40f, 0f, 5f); c.R.Speed = 70;
                    SimAgent d = sim.Agents[11 + 5];
                    d.R.Awareness = awareness;
                    float side = (s % 2 == 0) ? 1f : -1f;
                    SimTestKit.Place(d, c.Pos.X + side * (5f + (s % 3) * 1.5f), 40f + 6f + (s % 5));
                    Vec2 dir = new Vec2(-side * 0.30f, 0.95f).Norm();
                    for (int i = 0; i < 240 && sim.Phase == PlayPhase.Live; i++)
                    {
                        sim.Tick(1f / 60f, new ControlInput { Move = dir, Sprint = false }, c.Id);
                        if ((c.Pos - d.Pos).Length < 2.3f) { sum += (i + 1) / 60f; n++; break; }
                    }
                }
                return n > 0 ? sum / n : 99f;
            }
            float smart = TimeToContact(97), dim = TimeToContact(5);
            TestContext.WriteLine("time to contact: aware=" + smart + " unaware=" + dim);
            Assert.LessOrEqual(smart, dim + 0.02f);
        }

        // ---------------------------------------------------------------- man coverage

        private static float MinMatchingCost(float[][] cost, int di, bool[] used, int n, int m)
        {
            if (di == n) return 0f;
            float best = float.MaxValue;
            for (int j = 0; j < m; j++)
            {
                if (used[j]) continue;
                used[j] = true;
                float v = cost[di][j] + MinMatchingCost(cost, di + 1, used, n, m);
                used[j] = false;
                if (v < best) best = v;
            }
            return best;
        }

        [Test]
        public void ManDefendersMatchUpByAlignment()
        {
            string[] forms = { "Shotgun", "Trips", "Empty" };
            foreach (string form in forms)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", form, "Cover 1 Man Free", "4-3 Base", 75, 75, 31, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false });
                SimTestKit.StepToLive(sim);
                SimTestKit.Steps(sim, 3);
                var men = new List<SimAgent>();
                var recv = new List<SimAgent>();
                for (int i = 11; i < 22; i++) if (sim.Agents[i].CoverTarget >= 0) men.Add(sim.Agents[i]);
                for (int i = 1; i < 11; i++) if (sim.Agents[i].IsReceiverEligible) recv.Add(sim.Agents[i]);
                Assert.GreaterOrEqual(men.Count, 3, form + ": man defenders exist");
                var targets = new HashSet<int>();
                float actual = 0f;
                foreach (SimAgent m in men)
                {
                    Assert.IsTrue(targets.Add(m.CoverTarget), form + ": each receiver is covered by at most one man");
                    SimAgent r = sim.Agents[m.CoverTarget];
                    Assert.IsTrue(r.IsReceiverEligible, form + ": man defenders cover eligible receivers");
                    actual += Math.Abs(m.StartPos.X - r.StartPos.X);
                }
                var cost = new float[men.Count][];
                for (int i = 0; i < men.Count; i++)
                {
                    cost[i] = new float[recv.Count];
                    for (int j = 0; j < recv.Count; j++) cost[i][j] = Math.Abs(men[i].StartPos.X - recv[j].StartPos.X);
                }
                float best = MinMatchingCost(cost, 0, new bool[recv.Count], men.Count, recv.Count);
                TestContext.WriteLine(form + ": matched lateral distance " + actual + " vs optimum " + best);
                Assert.LessOrEqual(actual, best * 1.6f + 3f, form + ": matches follow alignment");
            }
        }

        [Test]
        public void PressCornersJamAtTheLine()
        {
            int jams = 0, n = 30;
            for (int s = 0; s < n; s++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "3-4 Cover 2 Man", "3-4 Base", 75, 75, 1200 + s, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false });
                PlayResult r = sim.RunToCompletion();
                if (sim.Log.Has(SimEventType.Jam)) jams++;
            }
            TestContext.WriteLine("plays with a jam: " + jams + "/" + n);
            Assert.Greater(jams, 0);
        }

        // ---------------------------------------------------------------- zone

        [Test]
        public void ZoneDefendersDropToTheirLandmarks()
        {
            PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 77, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false, QbHoldsBall = true });
            SimTestKit.StepToLive(sim);
            while (sim.Time < 1.0f) sim.Step(default(ControlInput));
            var zoneIds = new List<int>();
            for (int i = 11; i < 22; i++)
            {
                var ca = sim.Agents[i].Assign as CoverageAssignment;
                if (ca != null && !ca.IsMan) zoneIds.Add(i);
            }
            Assert.GreaterOrEqual(zoneIds.Count, 4, "Cover 3 has zone droppers");
            var d0 = new Dictionary<int, float>();
            foreach (int id in zoneIds) d0[id] = (sim.Agents[id].Pos - sim.Agents[id].Landmark).Length;
            while (sim.Time < 3.5f && sim.Phase == PlayPhase.Live) sim.Step(default(ControlInput));
            int close = 0, shallow = 0;
            foreach (int id in zoneIds)
            {
                SimAgent a = sim.Agents[id];
                float d1 = (a.Pos - a.Landmark).Length;
                bool deepDefender = a.Landmark.Y - sim.LosY > 8f;
                if (deepDefender) { if (a.Pos.Y >= a.Landmark.Y - 1.5f) close++; }
                else { shallow++; if (d1 < 5.0f) close++; }   // a flat defender may carry a vertical a few yards before passing him off
            }
            Assert.GreaterOrEqual(shallow, 2, "short zones exist");
            Assert.AreEqual(zoneIds.Count, close, "short droppers settle on their landmarks; deep ones never give up depth");
        }

        [Test]
        public void DeepZoneDefendersDoNotRunTowardTheLine()
        {
            PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 2 Zone", "4-3 Base", 75, 75, 5, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false, QbHoldsBall = true });
            SimTestKit.StepToLive(sim);
            var safeties = new List<SimAgent>();
            for (int i = 11; i < 22; i++) if (sim.Agents[i].PlayPos == Position.FS || sim.Agents[i].PlayPos == Position.SS) safeties.Add(sim.Agents[i]);
            Assert.GreaterOrEqual(safeties.Count, 1);
            while (sim.Time < 2.0f) sim.Step(default(ControlInput));
            foreach (SimAgent s in safeties) Assert.Greater(s.Pos.Y, sim.LosY + 4f, "safeties drop on a pass play");
        }

        // ---------------------------------------------------------------- quarterback

        [Test]
        public void QuarterbackDropsBackAndSetsOnPassPlays()
        {
            PlaySimulation sim = SimTestKit.RunNamed("Curl-Flat", "Singleback", "Cover 3 Sky", "4-3 Base", 75, 75, 81, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false, QbHoldsBall = true });
            SimTestKit.StepToLive(sim);
            float minY = sim.Qb.Pos.Y;
            bool sawSet = false, sawDrop = false;
            for (int i = 0; i < 180 && sim.Phase == PlayPhase.Live; i++)
            {
                sim.Step(default(ControlInput));
                minY = Math.Min(minY, sim.Qb.Pos.Y);
                if (sim.Qb.QbState == QbPhase.Dropback) sawDrop = true;
                if (sim.Qb.QbState == QbPhase.Set) sawSet = true;
            }
            Assert.IsTrue(sawDrop, "drops");
            Assert.IsTrue(sawSet, "sets");
            Assert.Less(minY, sim.LosY - 3f, "a passer under center gets at least three yards of depth");
        }

        [Test]
        public void ThePasserFindsAnOpenReceiverWhenOneExists()
        {
            // the secondary is far off the ball, so every route is open: the ball should come out quickly and be caught
            int completed = 0, n = 40; float tSum = 0f;
            for (int s = 0; s < n; s++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Curl-Flat", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 20, 1600 + s, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false });
                SimTestKit.StepToLive(sim);
                // the front four are pushed back so the passer has all day
                for (int i = 11; i < 22; i++) SimTestKit.Place(sim.Agents[i], 4f + (i - 11) * 4.5f, sim.LosY + 15f);
                PlayResult r = sim.RunToCompletion();
                if (r.Type == PlayType.Pass) { completed++; tSum += sim.Log.First(SimEventType.Throw).Value.Time; }
            }
            TestContext.WriteLine("open-field completions: " + completed + "/" + n + " mean throw time " + (completed > 0 ? tSum / completed : 0f));
            Assert.GreaterOrEqual(completed, (int)(n * 0.62f));
            Assert.Less(tSum / Math.Max(1, completed), 3.5f);
        }

        [Test]
        public void ThePasserDoesNotForceTheBallIntoBlanketCoverage()
        {
            // man coverage everywhere with elite corners: more throwaways/sacks/incompletions than interceptions
            int picks = 0, tosses = 0, n = 150;
            for (int s = 0; s < n; s++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Mesh", "Shotgun", "Cover 1 Man Free", "4-3 Base", 75, 99, 2100 + s, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false });
                PlayResult r = sim.RunToCompletion();
                if (r.Type == PlayType.Interception) picks++;
                if (sim.Log.Has(SimEventType.Throwaway)) tosses++;
            }
            TestContext.WriteLine("vs elite coverage: interceptions " + picks + "/" + n + ", throwaways " + tosses);
            Assert.Less(picks / (float)n, 0.1f);
        }

        // ---------------------------------------------------------------- blitz

        private static float MeanPressureTime(string defName, int n, int seedBase)
        {
            float sum = 0f;
            for (int s = 0; s < n; s++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", defName, "4-3 Base", 75, 75, seedBase + s, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false, QbHoldsBall = true });
                SimTestKit.StepToLive(sim);
                float t = 5f;
                while (sim.Phase == PlayPhase.Live && sim.Time < 5f)
                {
                    sim.Step(default(ControlInput));
                    if (sim.Log.Has(SimEventType.QbHit) || sim.Log.Has(SimEventType.Sack)) { t = sim.Time; break; }
                }
                sum += t;
            }
            return sum / n;
        }

        [Test]
        public void BlitzersArriveSoonerThanARegularRush()
        {
            float blitz = MeanPressureTime("Cover 0 Blitz", 80, 3000);
            float normal = MeanPressureTime("Cover 3 Sky", 80, 3000);
            TestContext.WriteLine("mean time to pressure: Cover 0 blitz=" + blitz + " Cover 3=" + normal);
            Assert.Less(blitz, normal);
        }

        [Test]
        public void TheQuarterbackHotReadsAgainstTheBlitz()
        {
            int quick = 0, n = 80;
            for (int s = 0; s < n; s++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Mesh", "Shotgun", "Cover 0 Blitz", "4-3 Base", 75, 75, 3500 + s, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false });
                sim.RunToCompletion();
                var t = sim.Log.First(SimEventType.Throw);
                if (t.HasValue && t.Value.Time < 2.3f) quick++;
            }
            TestContext.WriteLine("quick throws against the blitz: " + quick + "/" + n);
            Assert.Greater(quick, n / 4);
        }

        // ---------------------------------------------------------------- run defense / play action

        [Test]
        public void DefendersBiteOnPlayAction()
        {
            // linebackers step toward the line on a run fake; on a pass drop they back off
            float runStep = 0f, paStep = 0f; int n = 30;
            for (int s = 0; s < n; s++)
            {
                runStep += LinebackerDepthAfter("Inside Zone Right", "I-Form", 2100 + s);
                paStep += LinebackerDepthAfter("PA Boot", "I-Form", 2100 + s);
            }
            runStep /= n; paStep /= n;
            TestContext.WriteLine("mean linebacker depth 1.2s after the snap: run=" + runStep + " play action=" + paStep);
            Assert.Less(runStep, paStep + 2.5f, "run and play action look alike up front");
        }

        private static float LinebackerDepthAfter(string play, string form, int seed)
        {
            PlaySimulation sim = SimTestKit.RunNamed(play, form, "Cover 3 Sky", "4-3 Base", 75, 75, seed, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false });
            SimTestKit.StepToLive(sim);
            while (sim.Time < 1.2f && sim.Phase == PlayPhase.Live) sim.Step(default(ControlInput));
            float sum = 0f; int c = 0;
            for (int i = 11; i < 22; i++)
                if (PositionGroup.IsLinebacker(sim.Agents[i].PlayPos)) { sum += sim.Agents[i].Pos.Y - sim.LosY; c++; }
            return c > 0 ? sum / c : 0f;
        }
    }
}
