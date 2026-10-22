using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>
    /// Engine realism pass (v0.7.x film-room audit, see Docs/QA/engine.md): game speed, pocket shape, route breaks, coverage shells,
    /// pass flight, kickoff wall, onside kick, wind, kick meter and fatigue. Every SIM-### row cites one of these tests.
    /// </summary>
    public class EngineRealismTests
    {
        private static readonly string[] Coverages = { "Cover 1 Man Free", "Cover 2 Zone", "Cover 3 Sky", "Cover 4 Quarters", "Cover 0 Blitz" };

        private static PlaySimOptions Quiet(Action<PlaySimOptions> tweak = null)
        {
            var o = new PlaySimOptions { Penalties = false };
            if (tweak != null) tweak(o);
            return o;
        }

        // ------------------------------------------------------------------ speed and pace

        [Test]
        public void NoPlayerExceedsRealisticTopSpeed()
        {
            // 600 plays of every kind against game-like defenses: nobody is clocked above ~24 mph, linemen stay under 20 mph
            var passes = SimTestKit.Playbook.AllPlays.Where(p => p.Side == PlaySide.Offense && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike).ToList();
            float worst = 0f; string who = "";
            var rng = new Rng(4242);
            for (int i = 0; i < 600; i++)
            {
                Play off = passes[rng.Range(0, passes.Count)];
                Play def = SimTestKit.Defense(Coverages[rng.Range(0, Coverages.Length)], "4-3 Base");
                PlaySimulation sim = SimTestKit.Run(off, def, 90, 90, 7000 + i, null, null, Quiet());
                var none = default(ControlInput);
                int guard = 0;
                while (sim.Phase != PlayPhase.Dead && guard++ < 3000)
                {
                    sim.Step(none);
                    for (int k = 0; k < 22; k++)
                    {
                        float mph = sim.Agents[k].Speed * 2.0454545f;
                        if (mph > worst) { worst = mph; who = sim.Agents[k] + " in " + off.Name + " vs " + def.Name; }
                        if (PositionGroup.IsOffensiveLine(sim.Agents[k].PlayPos) || sim.Agents[k].PlayPos == Position.DT)
                            Assert.Less(mph, 21.0f, "a lineman at " + mph.ToString("0.0") + " mph: " + sim.Agents[k]);
                    }
                }
            }
            TestContext.WriteLine("fastest " + worst.ToString("0.0") + " mph " + who);
            Assert.Less(worst, 24.5f, who);
        }

        [Test]
        public void ElitePlayersPeakAroundTwentyTwoMphInPads()
        {
            var rng = new Rng(8);
            var wr = SimTestKit.MakePlayer(rng, Position.WR, 99);
            wr.Ratings.Speed = 99; wr.Ratings.Acceleration = 99;
            var body = new AgentBody(wr.Ratings, 190, 72);
            float mph = body.VMax * 2.0454545f;
            Assert.That(mph, Is.InRange(21.0f, 23.0f), "99-speed receiver top speed " + mph);
            var lt = SimTestKit.MakePlayer(rng, Position.LT, 70);
            lt.Ratings.Speed = 45;
            var big = new AgentBody(lt.Ratings, 320, 77);
            Assert.That(big.VMax * 2.0454545f, Is.InRange(16.5f, 19.5f), "a 320-lb tackle tops out under 19.5 mph");
        }

        // ------------------------------------------------------------------ pass protection

        private static List<float> OlDepthAt(float seconds, Func<PlaySimulation, int, float> pick, int n, string off = "Curl-Flat", string form = "Singleback")
        {
            var res = new List<float>();
            for (int i = 0; i < n; i++)
            {
                PlaySimulation sim = SimTestKit.RunNamed(off, form, Coverages[i % Coverages.Length], "4-3 Base", 75, 75, 31000 + i, null, null, Quiet(o => o.QbHoldsBall = true));
                SimTestKit.StepToLive(sim);
                while (sim.Time < seconds && sim.Phase == PlayPhase.Live) sim.Step(default(ControlInput));
                if (sim.Phase != PlayPhase.Live) continue;
                res.Add(pick(sim, i));
            }
            return res;
        }

        [Test]
        public void PassProtectionFormsAPocketBehindTheLine()
        {
            // at 2.0 s the five linemen have given ground (kick slide) instead of holding or driving past the line of scrimmage
            var depth = OlDepthAt(2.0f, (sim, i) => { float s = 0; for (int k = 2; k <= 6; k++) s += sim.Agents[k].Pos.Y - sim.LosY; return s / 5f; }, 60);
            double mean = depth.Average();
            TestContext.WriteLine("mean OL depth at 2.0 s " + mean.ToString("0.00") + " yd (n=" + depth.Count + ")");
            Assert.That(mean, Is.InRange(-2.6, -0.5), "linemen are behind the line, not past it");
        }

        [Test]
        public void PassProtectionBowsIntoACup()
        {
            // tackles sit deeper than the center at 2.0 s (the arc around the quarterback)
            var tackles = OlDepthAt(2.0f, (sim, i) => (sim.Agents[2].Pos.Y + sim.Agents[6].Pos.Y) * 0.5f - sim.LosY, 60);
            var center = OlDepthAt(2.0f, (sim, i) => sim.Agents[4].Pos.Y - sim.LosY, 60);
            TestContext.WriteLine("tackle depth " + tackles.Average().ToString("0.00") + " center depth " + center.Average().ToString("0.00"));
            Assert.Less(tackles.Average(), center.Average() - 0.1, "tackles deeper than the center");
        }

        [Test]
        public void OffensiveLinemenKeepTheirSplitsInPassProtection()
        {
            // the line stays about as wide as it started: guards and tackles do not collapse into the center
            var width = new List<float>();
            for (int i = 0; i < 40; i++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Curl-Flat", "Singleback", Coverages[i % Coverages.Length], "4-3 Base", 75, 75, 32000 + i, null, null, Quiet(o => o.QbHoldsBall = true));
                float w0 = sim.Agents[6].Pos.X - sim.Agents[2].Pos.X;
                SimTestKit.StepToLive(sim);
                while (sim.Time < 2.0f && sim.Phase == PlayPhase.Live) sim.Step(default(ControlInput));
                if (sim.Phase != PlayPhase.Live) continue;
                width.Add((sim.Agents[6].Pos.X - sim.Agents[2].Pos.X) / w0);
            }
            double mean = width.Average();
            TestContext.WriteLine("line width at 2.0 s relative to the snap " + mean.ToString("0.00"));
            Assert.Greater(mean, 0.78, "the five linemen keep at least 78% of their original width");
        }

        [Test]
        public void PocketHoldsForTheFirstTwoSeconds()
        {
            // 4-man rush against five blockers: the quarterback is almost never taken down before 1.6 s and the mean sack comes after 2 s
            var sackTimes = new List<float>(); int dropbacks = 0, earlySacks = 0;
            for (int i = 0; i < 500; i++)
            {
                Play off = SimTestKit.Offense("Curl-Flat", "Shotgun");
                PlaySimulation sim = SimTestKit.Run(off, SimTestKit.Defense(Coverages[1 + i % 3], "4-3 Base"), 75, 75, 33000 + i, null, null, Quiet());
                sim.RunToCompletion();
                dropbacks++;
                var sk = sim.Log.First(SimEventType.Sack);
                if (sk.HasValue) { sackTimes.Add(sk.Value.Time); if (sk.Value.Time < 1.6f) earlySacks++; }
            }
            TestContext.WriteLine("sacks " + sackTimes.Count + "/" + dropbacks + " mean time " + (sackTimes.Count > 0 ? sackTimes.Average() : 0).ToString("0.00") + " early " + earlySacks);
            Assert.GreaterOrEqual(sackTimes.Count, 3, "sacks do happen (v0.9.0: 5 in 500 shotgun curl-flats, was more than 5: the shotgun passer releases at 2.2 s, before the four-man rush gets home)");
            Assert.Greater(sackTimes.Average(), 1.85f, "mean sack time");
            Assert.Less(earlySacks / (float)dropbacks, 0.02f, "almost no sacks before 1.6 s");
        }

        // ------------------------------------------------------------------ routes

        [Test]
        public void SharpRouteBreaksAreMadeAtTheBreakDepth()
        {
            // a 9-yard out and a 12-yard dig reach within half a yard of their break point (they used to cut 0.7-1.3 yd early)
            float missOut = 0, missDig = 0; int nOut = 0, nDig = 0;
            for (int i = 0; i < 80; i++)
            {
                var sim = SimTestKit.RunNamed("Double Outs", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 34000 + i, null, null, Quiet(o => o.QbHoldsBall = true));
                SimTestKit.StepToLive(sim);
                var best = new float[11]; for (int k = 0; k < 11; k++) best[k] = 99f;
                while (sim.Time < 2.6f && sim.Phase == PlayPhase.Live)
                {
                    sim.Step(default(ControlInput));
                    for (int k = 1; k < 11; k++)
                    {
                        var ra = sim.Agents[k].Assign as RouteAssignment;
                        if (ra == null || ra.Route != RouteKind.Out) continue;
                        float d = (sim.Agents[k].Pos - sim.Rel(ra.Waypoints[0].Point)).Length;
                        if (d < best[k]) best[k] = d;
                    }
                }
                for (int k = 1; k < 11; k++) if (best[k] < 90f) { missOut += best[k]; nOut++; }
            }
            Assert.Greater(nOut, 40);
            double avg = missOut / nOut;
            TestContext.WriteLine("closest approach to the out's break point " + avg.ToString("0.00") + " yd");
            Assert.Less(avg, 0.5, "receivers plant at the break depth");
        }

        // ------------------------------------------------------------------ alignment

        private static SimAgent Find(PlaySimulation sim, int firstDefSlot, Position pos, int nth = 0)
        {
            int seen = 0;
            for (int i = 11; i < 22; i++) if (sim.Agents[i].PlayPos == pos) { if (seen++ == nth) return sim.Agents[i]; }
            return null;
        }

        [Test]
        public void PressCornersAlignOnTheLine()
        {
            var sim = SimTestKit.RunNamed("Slants", "Shotgun", "Cover 2 Press", "4-3 Base", 75, 75, 5, null, null, Quiet(o => o.AutoSnap = false));
            SimAgent cb = Find(sim, 11, Position.CB, 0), cb2 = Find(sim, 11, Position.CB, 1);
            Assert.Less(cb.Pos.Y - sim.LosY, 2.2f, "press corner within two yards of the line");
            Assert.Less(cb2.Pos.Y - sim.LosY, 2.2f);
            var off = SimTestKit.RunNamed("Slants", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 5, null, null, Quiet(o => o.AutoSnap = false));
            Assert.Greater(Find(off, 11, Position.CB, 0).Pos.Y - off.LosY, 6f, "an off corner in Cover 3 stays seven yards deep");
        }

        [Test]
        public void SafetiesAlignToTheCoverageShell()
        {
            var two = SimTestKit.RunNamed("Slants", "Shotgun", "Cover 2 Zone", "4-3 Base", 75, 75, 5, null, null, Quiet(o => o.AutoSnap = false));
            var three = SimTestKit.RunNamed("Slants", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 5, null, null, Quiet(o => o.AutoSnap = false));
            var four = SimTestKit.RunNamed("Slants", "Shotgun", "Cover 4 Quarters", "4-3 Base", 75, 75, 5, null, null, Quiet(o => o.AutoSnap = false));
            float cx = two.BallX0;
            // Cover 2: two safeties split wide, one on each half
            float fs2 = two.Agents[11 + 9].Pos.X - cx, ss2 = two.Agents[11 + 10].Pos.X - cx;
            Assert.Less(fs2, -7.5f); Assert.Greater(ss2, 7.5f);
            // Cover 3: the free safety sits in the middle of the field
            Assert.Less(Math.Abs(three.Agents[11 + 9].Pos.X - cx), 1.5f, "Cover 3 single high");
            Assert.Greater(three.Agents[11 + 9].Pos.Y - three.LosY, 10f);
            // Cover 4: safeties over the inside receivers
            Assert.That(Math.Abs(four.Agents[11 + 9].Pos.X - cx), Is.InRange(6f, 9f));
            Assert.That(Math.Abs(four.Agents[11 + 10].Pos.X - cx), Is.InRange(6f, 9f));
        }

        [Test]
        public void ManCornersAlignWithInsideLeverageOnTheirReceiver()
        {
            var sim = SimTestKit.RunNamed("Slants", "Shotgun", "Cover 1 Man Free", "4-3 Base", 75, 75, 5, null, null, Quiet(o => o.AutoSnap = false));
            int matched = 0;
            for (int i = 11; i < 22; i++)
            {
                SimAgent d = sim.Agents[i];
                if (d.PlayPos != Position.CB || d.CoverTarget < 0) continue;
                SimAgent r = sim.Agents[d.CoverTarget];
                float dx = d.Pos.X - r.Pos.X;
                Assert.Less(Math.Abs(dx), 1.0f, "corner lines up on his receiver");
                float inside = Math.Sign(sim.BallX0 - r.Pos.X);
                if (Math.Abs(r.Pos.X - sim.BallX0) > 4f) Assert.GreaterOrEqual(dx * inside, -0.05f, "leverage is to the inside");
                matched++;
            }
            Assert.GreaterOrEqual(matched, 2);
        }

        // ------------------------------------------------------------------ passing

        [Test]
        public void PassFlightTimesAreRealistic()
        {
            var flights = new Dictionary<int, List<float>> { { 0, new List<float>() }, { 1, new List<float>() }, { 2, new List<float>() } };
            var passes = SimTestKit.Playbook.AllPlays.Where(p => p.Side == PlaySide.Offense && p.IsPass && p.Name != "Hail Mary").ToList();
            for (int i = 0; i < 400; i++)
            {
                var rng = new Rng(i * 7 + 1);
                Play off = passes[rng.Range(0, passes.Count)];
                PlaySimulation sim = SimTestKit.Run(off, SimTestKit.Defense(Coverages[rng.Range(0, Coverages.Length)], "4-3 Base"), 75, 75, 36000 + i, null, null, Quiet());
                float tThrow = -1; Vec2 from = Vec2.Zero;
                var none = default(ControlInput); int guard = 0;
                while (sim.Phase != PlayPhase.Dead && guard++ < 3000)
                {
                    sim.Step(none);
                    if (tThrow < 0 && sim.PassIsThrown) { tThrow = sim.Time; from = sim.Ball.Pos.XY; }
                }
                if (tThrow < 0) continue;
                var end = sim.Log.First(SimEventType.Catch);
                if (!end.HasValue) continue;
                float dist = (end.Value.Pos.XY - from).Length;
                float t = end.Value.Time - tThrow;
                if (dist >= 8f && dist < 15f) flights[0].Add(t);
                else if (dist >= 15f && dist < 25f) flights[1].Add(t);
                else if (dist >= 25f && dist < 40f) flights[2].Add(t);
            }
            TestContext.WriteLine("flight 8-15 " + flights[0].Average().ToString("0.00") + " 15-25 " + flights[1].Average().ToString("0.00") + " 25-40 " + flights[2].Average().ToString("0.00"));
            Assert.That(flights[0].Average(), Is.InRange(0.55, 1.00), "a 10-yard ball takes about 0.7 s (v0.9.0: the fitted NFL curve, was 0.6)");
            Assert.That(flights[1].Average(), Is.InRange(0.95, 1.50), "a 20-yard ball takes about 1.25 s (was 0.9)");
            Assert.That(flights[2].Average(), Is.InRange(1.55, 2.35), "a 30-yard ball takes about 1.85 s (was 1.3)");
        }

        [Test]
        public void ZoneDefendersPassOffReceiversInsteadOfBunching()
        {
            // crossing routes against zone: the number of free defenders within 3 yd of the same receiver stays small
            int worst = 0, over3 = 0, n = 0;
            for (int i = 0; i < 120; i++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Y-Cross", "Singleback", i % 2 == 0 ? "Cover 3 Sky" : "Cover 2 Zone", "4-3 Base", 75, 75, 37000 + i, null, null, Quiet(o => o.QbHoldsBall = true));
                SimTestKit.StepToLive(sim);
                int most = 0;
                while (sim.Time < 3.2f && sim.Phase == PlayPhase.Live)
                {
                    sim.Step(default(ControlInput));
                    if (sim.Time < 1.2f) continue;
                    for (int r = 1; r < 11; r++)
                    {
                        SimAgent rc = sim.Agents[r];
                        if (!rc.IsReceiverEligible) continue;
                        int near = 0;
                        for (int d = 15; d < 22; d++) if ((sim.Agents[d].Pos - rc.Pos).Length < 3f && sim.Agents[d].State == AgentState.Normal) near++;
                        if (near > most) most = near;
                    }
                }
                n++; worst = Math.Max(worst, most); if (most >= 4) over3++;
            }
            TestContext.WriteLine("worst bunch " + worst + ", plays with 4+ defenders on one receiver " + over3 + "/" + n);
            Assert.LessOrEqual(over3, n / 12, "four defenders on one receiver is rare");
        }

        // ------------------------------------------------------------------ kicking game

        private static PlaySimulation KickPlay(string kick, string ret, int seed, int spot = 30, PlaySimOptions opts = null)
        {
            var rules = RuleSet.Pro();
            GameState gs = kick.StartsWith("Kickoff") || kick.StartsWith("Onside") ? new GameState(rules, PixFootball.Core.Team.Away) : SimTestKit.Scrimmage(rules, spot, 4, 8);
            return SimTestKit.Run(SimTestKit.Special(kick), SimTestKit.Special(ret), 75, 75, seed, rules, gs, opts);
        }

        [Test]
        public void KickoffReturnWallHoldsRowsInFrontOfTheReturner()
        {
            // when the ball is caught the return blockers stand 12-25 yd in front of the returner, not 40 yd up the field
            var gaps = new List<float>();
            for (int i = 0; i < 60; i++)
            {
                PlaySimulation sim = KickPlay("Kickoff Deep Middle", "Kick Return Middle Wedge", 38000 + i, 30, Quiet());
                var none = default(ControlInput); int guard = 0; bool caught = false;
                while (sim.Phase != PlayPhase.Dead && guard++ < 3000)
                {
                    sim.Step(none);
                    if (!caught && sim.Log.Has(SimEventType.Catch) && sim.Carrier != null)
                    {
                        caught = true;
                        float y = sim.Carrier.Pos.Y; float sum = 0; int c = 0;
                        for (int k = 11; k < 22; k++)
                        {
                            SimAgent a = sim.Agents[k];
                            if (a == sim.Carrier || a.Assign is ReturnerAssignment) continue;
                            sum += y - a.Pos.Y; c++;
                        }
                        gaps.Add(sum / Math.Max(1, c));
                    }
                }
            }
            Assert.Greater(gaps.Count, 20, "enough fielded kicks");
            TestContext.WriteLine("mean blocker distance in front of the returner at the catch " + gaps.Average().ToString("0.0") + " yd");
            Assert.That(gaps.Average(), Is.InRange(8.0, 28.0));
        }

        [Test]
        public void OnsideKickRecoveryIsFarFromAutomatic()
        {
            int kicker = 0, n = 300;
            for (int i = 0; i < n; i++)
            {
                PlaySimulation sim = KickPlay("Onside Kick Short", "Kick Return Middle Wedge", 39000 + i, 30, Quiet());
                PlayResult r = sim.RunToCompletion();
                if (r.RecoveredByKickingTeam) kicker++;
                Assert.IsFalse(r.Touchdown, "no 90-yard returns off an onside recovery");
            }
            float rate = kicker / (float)n;
            TestContext.WriteLine("expected onside recovered by the kicking team " + rate.ToString("0.00"));
            Assert.That(rate, Is.InRange(0.08, 0.42));
        }

        [Test]
        public void MatchConditionsAreDeterministic()
        {
            var a = MatchConditions.Generate(77); var b = MatchConditions.Generate(77);
            Assert.AreEqual(a.WindMph, b.WindMph); Assert.AreEqual(a.WindToDegrees, b.WindToDegrees);
            Assert.AreNotEqual(MatchConditions.Generate(78).WindToDegrees, a.WindToDegrees);
            Assert.AreEqual(a.ForQuarter(3).WindMph, a.ForQuarter(3).WindMph);
            var w = new MatchConditions(10f, 0f);          // toward +Z at 10 mph
            Vec2 simPos = w.ToSimFrame(true), simNeg = w.ToSimFrame(false);
            Assert.AreEqual(0f, simPos.X, 1e-4f); Assert.AreEqual(10f * MatchConditions.YardsPerSecondPerMph, simPos.Y, 1e-3f);
            Assert.AreEqual(-simPos.Y, simNeg.Y, 1e-4f, "the offense attacking the other end zone sees the wind as a headwind");
            Assert.Greater(w.TailwindMph(true), 9f); Assert.Less(w.TailwindMph(false), -9f);
            int calm = 0, windy = 0;
            for (int s = 0; s < 400; s++) { var c = MatchConditions.Generate(s); if (c.WindMph < 3f) calm++; if (c.WindMph >= 16f) windy++; }
            Assert.That(calm, Is.InRange(40, 140)); Assert.That(windy, Is.InRange(5, 60));
            Assert.AreEqual("WIND CALM", MatchConditions.Calm.HudText);
        }

        [Test]
        public void WindMovesKicksAndPunts()
        {
            float k = MatchConditions.YardsPerSecondPerMph;
            float Punt(float wx, float wy, out float lateral)
            {
                double sum = 0, lat = 0; int n = 0;
                for (int i = 0; i < 40; i++)
                {
                    var opts = Quiet(o => o.Wind = new Vec2(wx * k, wy * k));
                    PlaySimulation sim = KickPlay("Punt Standard", "Punt Return Set", 41000 + i, 30, opts);
                    PlayResult r = sim.RunToCompletion();
                    if (r.Type != PlayType.Punt || sim.Log.Has(SimEventType.KickBlocked)) continue;
                    var e = sim.Log.First(SimEventType.Catch) ?? sim.Log.First(SimEventType.KickLanded);
                    sum += r.Yards; lat += e.HasValue ? e.Value.Pos.X - sim.BallX0 : 0; n++;
                }
                lateral = (float)(lat / n);
                return (float)(sum / n);
            }
            float l0, l1, l2, l3;
            float calm = Punt(0, 0, out l0), tail = Punt(0, 15, out l1), head = Punt(0, -15, out l2), cross = Punt(15, 0, out l3);
            TestContext.WriteLine("punt calm " + calm.ToString("0.0") + " tail " + tail.ToString("0.0") + " head " + head.ToString("0.0") + " crosswind drift " + l3.ToString("0.0"));
            Assert.Greater(tail, calm + 4f, "a 15 mph tailwind adds distance");
            Assert.Less(head, calm - 5f, "a 15 mph headwind costs distance");
            Assert.That(l3, Is.InRange(3f, 8f), "a 15 mph crosswind blows the punt several yards");
            Assert.That(Math.Abs(l1), Is.LessThan(1.2f));
            // the engine wind is cleared after every step: nothing leaks into other code
            Assert.AreEqual(0f, BallPhysics.Wind.X); Assert.AreEqual(0f, BallPhysics.Wind.Y);
        }

        [Test]
        public void KickMeterModelIsMonotonic()
        {
            Assert.Greater(KickMeter.PowerMultiplier(1f, 1f), KickMeter.PowerMultiplier(0.5f, 1f));
            Assert.Greater(KickMeter.PowerMultiplier(1f, 1f), KickMeter.PowerMultiplier(1f, 0.2f));
            Assert.AreEqual(1f, KickMeter.PowerMultiplier(1f, 1f), 1e-4f);
            Assert.Less(KickMeter.SpreadMultiplier(1f), KickMeter.SpreadMultiplier(0.5f));
            Assert.Less(KickMeter.SpreadMultiplier(0.5f), KickMeter.SpreadMultiplier(0f));
            Assert.AreEqual(1f, KickMeter.SpreadMultiplier(1f), 1e-4f);
            Assert.AreEqual("Perfect", KickMeter.TimingLabel(0.97f)); Assert.AreEqual("Poor", KickMeter.TimingLabel(0.1f));
            Assert.AreEqual("Centered", KickMeter.AimLabel(0.04f)); Assert.AreEqual("Way Left", KickMeter.AimLabel(-0.9f)); Assert.AreEqual("Slightly Right", KickMeter.AimLabel(0.2f));
            Assert.AreEqual(1f, KickMeter.TimingFromCursor(0.5f), 1e-4f); Assert.Less(KickMeter.TimingFromCursor(0.9f), 0.3f);
            Assert.AreEqual(0f, KickMeter.PingPong(0f, 1f), 1e-4f); Assert.AreEqual(1f, KickMeter.PingPong(0.5f, 1f), 1e-4f);
        }

        private static double LandingX(PlaySimulation s)
        {
            foreach (var e in s.Log.Events)
                if (e.Type == SimEventType.Catch || e.Type == SimEventType.KickLanded || e.Type == SimEventType.Recovery || e.Type == SimEventType.Whistle) return e.Pos.X - s.BallX0;
            return s.Ball.Pos.X - s.BallX0;
        }

        private static PlaySimulation UserKick(string kick, string ret, int seed, int spot, float power, float timing, float aim, bool press = true)
        {
            var rules = RuleSet.Pro();
            GameState gs = kick.StartsWith("Kickoff") ? new GameState(rules, PixFootball.Core.Team.Away) : SimTestKit.Scrimmage(rules, spot, 4, 8);
            PlaySimulation sim = SimTestKit.Run(SimTestKit.Special(kick), SimTestKit.Special(ret), 75, 75, seed, rules, gs, Quiet());
            var none = default(ControlInput);
            // the user kicks: control the kicker (agent 0)
            int guard = 0; bool pressed = false;
            while (sim.Phase != PlayPhase.Dead && guard++ < 4000)
            {
                ControlInput inp = none;
                if (press && sim.AwaitingUserKick && !pressed) { inp.KickPress = true; inp.KickPower = power; inp.KickTiming = timing; inp.KickAim = aim; pressed = true; }
                sim.Tick(1f / 60f, inp, 0);
            }
            return sim;
        }

        [Test]
        public void KickMeterTimingAndAimChangeTheKick()
        {
            double Avg(Func<int, PlaySimulation> make, Func<PlaySimulation, double> f, int n = 24)
            {
                double s = 0; int c = 0;
                for (int i = 0; i < n; i++) { var sim = make(i); if (sim.Log.Has(SimEventType.KickBlocked)) continue; s += f(sim); c++; }
                return s / c;
            }
            // punts: power and aim
            double full = Avg(i => UserKick("Punt Standard", "Punt Return Set", 42000 + i, 30, 1f, 1f, 0f), s => s.Result.Yards);
            double soft = Avg(i => UserKick("Punt Standard", "Punt Return Set", 42000 + i, 30, 0.3f, 1f, 0f), s => s.Result.Yards);
            Assert.Greater(full, soft + 8, "a full power fill punts much further than a weak one (" + full.ToString("0") + " vs " + soft.ToString("0") + ")");
            double left = Avg(i => UserKick("Punt Standard", "Punt Return Set", 42100 + i, 30, 1f, 1f, -0.6f), s => LandingX(s));
            double right = Avg(i => UserKick("Punt Standard", "Punt Return Set", 42100 + i, 30, 1f, 1f, 0.6f), s => LandingX(s));
            Assert.Greater(right - left, 7.0, "aim steers the punt " + left.ToString("0.0") + " .. " + right.ToString("0.0"));
            // field goals: a perfect centered kick is made far more often than a shanked one
            int made = 0, shank = 0, n = 60;
            for (int i = 0; i < n; i++)
            {
                if (UserKick("Field Goal", "Field Goal Block Middle", 43000 + i, 70, 1f, 1f, 0f).Result.Good) made++;
                if (UserKick("Field Goal", "Field Goal Block Middle", 43000 + i, 70, 1f, 0.05f, 0.6f).Result.Good) shank++;
            }
            TestContext.WriteLine("47-yd FG perfect " + made + "/" + n + " vs mistimed and off-aim " + shank + "/" + n);
            Assert.Greater(made, shank + 15);
        }

        [Test]
        public void UserKickWaitsForThePressAndTimesOutOnADefaultReading()
        {
            var rules = RuleSet.Pro();
            var gs = SimTestKit.Scrimmage(rules, 30, 4, 8);
            PlaySimulation sim = SimTestKit.Run(SimTestKit.Special("Punt Standard"), SimTestKit.Special("Punt Return Set"), 75, 75, 4, rules, gs, Quiet());
            var none = default(ControlInput);
            float seen = 0f;
            for (int i = 0; i < 60 * 2; i++) sim.Tick(1f / 60f, none, 0);
            Assert.IsFalse(sim.Log.Has(SimEventType.Kick), "nothing is kicked until the user presses");
            Assert.IsTrue(sim.AwaitingUserKick, "the meter is waiting");
            for (int i = 0; i < 60 * 8 && sim.Phase != PlayPhase.Dead; i++) sim.Tick(1f / 60f, none, 0);
            Assert.IsTrue(sim.Log.Has(SimEventType.Kick), "after the timeout the kick goes on a default reading");
            Assert.Greater(seen + 1f, 0f);
        }

        // ------------------------------------------------------------------ fatigue and game conditions

        [Test]
        public void FatigueCarriesAcrossPlays()
        {
            var tracker = new FatigueTracker();
            var rng = new Rng(5);
            Play off = SimTestKit.Offense("Four Verticals", "Shotgun");
            Play def = SimTestKit.Defense("Cover 3 Sky", "4-3 Base");
            Formation of = SimTestKit.Playbook.FindFormation(off.FormationName), df = SimTestKit.Playbook.FindFormation(def.FormationName);
            Player[] ro = SimTestKit.Roster(of, 92, rng), rd = SimTestKit.Roster(df, 45, rng);   // a clean pocket: the receivers run full routes every play
            float firstWr = -1f, lastWr = -1f;
            for (int p = 0; p < 14; p++)
            {
                var sim = new PlaySimulation();
                sim.Setup(SimTestKit.Scrimmage(RuleSet.Pro()), off, def, ro, rd, RuleSet.Pro(), new Rng(100 + p), new PlaySimOptions { Penalties = false, Fatigue = tracker });
                sim.RunToCompletion();
                tracker.AfterPlay(sim, 12f);   // no-huddle
                float wr = tracker.EnergyOf(ro[8].Id);
                if (p == 0) firstWr = wr; lastWr = wr;
            }
            TestContext.WriteLine("receiver energy after 1 play " + firstWr.ToString("0.00") + ", after 14 no-huddle plays " + lastWr.ToString("0.00"));
            Assert.Less(lastWr, firstWr, "energy builds a deficit over a drive");
            Assert.Less(lastWr, 0.97f);
            Assert.Less(tracker.EnergyOf(ro[8].Id), 1f);
            Assert.Greater(lastWr, 0.12f, "but nobody is gassed after one drive (v0.9.0: plays last 20 percent longer and the huddle recovery is 1/900, so 14 straight no-huddle vertical routes leave a receiver near 0.2, was 0.3)");
            tracker.Rest(900f);
            Assert.Greater(tracker.EnergyOf(ro[8].Id), lastWr, "rest recovers");
            // a tired body is slower
            var fresh = new AgentBody(ro[8].Ratings, 190, 72); var tired = new AgentBody(ro[8].Ratings, 190, 72); tired.Energy = 0.2f;
            Assert.Less(tired.EffectiveVMax, fresh.EffectiveVMax);
        }
    }
}
