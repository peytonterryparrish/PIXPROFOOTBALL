using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>
    /// v0.11.0 QB (playtest note 9): ball flight and receiver ball tracking measured over many simulated passes.
    /// Catch-point error = distance (m) between the ball and the targeted receiver on the step the ball comes down through his reach (2.4 m) or is caught;
    /// facing error = angle between his facing and the direction to the ball 0.3 s earlier; release height / flight time by throw style are logged for the log file.
    /// </summary>
    public class QbBallTrackTests
    {
        const float Yd = 0.9144f;

        sealed class Rec
        {
            public float ErrM = 99f, FaceErr, Zrel, Speed, FlightT, Dist; public ThrowStyle Style; public bool Complete, Picked, Tipped; public bool OnRun; public int Target; public float ReleaseHeight;
            public float AimErrM, NeedRatio, TTL, LastT, EndDxy, RSpeed, RVmax; public bool Early; public string Dbg = ""; public float ArrivalLeadM;      // distance the receiver still had to cover (to the ball) when the pass was released, minus the distance he covers by himself: how far the ball asked him to move
        }

        static List<Play> PassPlays()
        {
            var l = new List<Play>();
            foreach (Play p in SimTestKit.Playbook.AllPlays)
            {
                if (p.Side != PlaySide.Offense) continue;
                var qa = p.Assignments.Length > 0 ? p.Assignments[0] as QbAssignment : null;
                if (qa == null) continue;
                if (qa.Action == QbAction.DropbackPass || qa.Action == QbAction.QuickPass || qa.Action == QbAction.Rollout || qa.Action == QbAction.Boot || qa.Action == QbAction.PlayActionFake) l.Add(p);
            }
            return l;
        }

        static List<Rec> Collect(int n, int seedBase, out int plays, out int attempts, out int completions, out double yards, out int sacks, out int scrambles)
        {
            var list = new List<Rec>(); var pp = PassPlays();
            var defs = SimTestKit.Playbook.AllPlays.Where(p => p.Side == PlaySide.Defense && (p.Category == PlayCategory.ZoneCoverage || p.Category == PlayCategory.ManCoverage) && p.FormationName == "4-3 Base").ToList();
            plays = attempts = completions = sacks = scrambles = 0; yards = 0;
            for (int i = 0; i < n; i++)
            {
                int seed = seedBase + i;
                var rr = new Rng(seed * 31 + 7);
                Play off = pp[i % pp.Count];
                Formation of = SimTestKit.Playbook.FindFormation(off.FormationName);
                Play def = defs[rr.Range(0, defs.Count)];
                var sim = SimTestKit.Run(off, def, 75, 75, seed);
                SimTestKit.StepToLive(sim);
                var none = new ControlInput();
                Rec rec = null; bool released = false; float tRel = 0f; var hist = new Queue<float>(); int guard = 0; bool scr = false;
                float releaseSpeedQb = 0f;
                while (sim.Phase != PlayPhase.Dead && guard++ < 2400)
                {
                    sim.Step(none);
                    if (sim.Agents[0].QbState == QbPhase.Scramble) scr = true;
                    if (!released && sim.PassIsThrown && sim.Ball.Intended >= 1 && sim.Ball.State == BallState.Pass)
                    {
                        released = true; tRel = sim.Time;
                        var q = sim.Agents[0]; var t = sim.Agents[sim.Ball.Intended];
                        rec = new Rec { Target = sim.Ball.Intended, Style = sim.LastThrowStyle, ReleaseHeight = sim.Ball.Pos.Z, OnRun = q.Speed > 3.3f / Yd, Dist = (t.Pos - q.Pos).Length * Yd };
                        releaseSpeedQb = q.Speed;
                        rec.AimErrM = sim.LastThrowAimError * Yd;
                        { Vec3 at; float tt; if (!BallPhysics.PredictAtHeight(sim.Ball.Pos, sim.Ball.Vel, 1.6f, out at, out tt)) tt = 0.3f; float dd = Math.Max(0f, (at.XY - t.Pos).Length - 0.9f); rec.TTL = tt; rec.NeedRatio = dd / Math.Max(0.1f, tt) / Math.Max(1f, t.Body.VMax); }
                    }
                    if (rec != null && sim.Ball.State == BallState.Pass) { rec.LastT = sim.Time - tRel; rec.EndDxy = (sim.Ball.Pos.XY - sim.Agents[rec.Target].Pos).Length * Yd; }
                    if (rec != null && sim.Ball.State == BallState.Pass && sim.Ball.FlightTime > 0.05f)
                    {
                        // closest approach of the ball to the targeted receiver while it is inside his reach (below 2.6 m): the catch-point error of the throw plus his route to it
                        var t = sim.Agents[rec.Target];
                        float dxy = (sim.Ball.Pos.XY - t.Pos).Length * Yd;
                        if (sim.Ball.Pos.Z * Yd <= 2.6f && dxy < rec.ErrM)
                        {
                            rec.ErrM = dxy; rec.Zrel = 1f; rec.FlightT = sim.Time - tRel; rec.RSpeed = t.Speed; rec.RVmax = t.Body.VMax; rec.Dbg = t.State + " fallen " + t.Body.FallenTime.ToString("0.0") + " jam " + t.JamTime.ToString("0.0") + " routeDone " + t.RouteDone + " commit " + (sim.Time < t.CommitUntil ? t.CommitSpeedFrac.ToString("0.00") : "-") + " route " + (t.Assign is RouteAssignment ? ((RouteAssignment)t.Assign).Route.ToString() : t.Assign == null ? "none" : t.Assign.GetType().Name);
                            Vec2 toBall = (sim.Ball.Pos.XY - t.Pos);
                            rec.FaceErr = toBall.LenSq() < 0.04f ? 0f : (float)(SimMath.AngleBetween(t.Facing, toBall.Norm()) * 180f / Math.PI);
                        }
                    }
                }
                if (sim.Phase != PlayPhase.Dead) continue;
                var res = sim.Result; if (res == null) continue;
                plays++;
                if (res.Type == PlayType.Sack) sacks++;
                if (scr) scrambles++;
                if (rec != null && rec.Zrel > 0f)
                {
                    attempts++;
                    if (res.Type == PlayType.Pass) { completions++; rec.Complete = true; }
                    if (res.Type == PlayType.Interception) rec.Picked = true;
                    list.Add(rec);
                }
                else if (res.Type == PlayType.Pass || res.Type == PlayType.Incomplete || res.Type == PlayType.Interception) { attempts++; if (res.Type == PlayType.Pass) completions++; }
                if (res.Type == PlayType.Pass || res.Type == PlayType.Sack) yards += res.Yards;
            }
            return list;
        }

        static string P(IEnumerable<float> xs)
        {
            var l = xs.OrderBy(x => x).ToList(); if (l.Count == 0) return "n=0";
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, "n={0} mean {1:F2} p50 {2:F2} p90 {3:F2} max {4:F2}", l.Count, l.Average(), l[l.Count / 2], l[Math.Min(l.Count - 1, (int)(0.9 * l.Count))], l[l.Count - 1]);
        }



        [Test] public void LegacyV10ForComparison() { bool old = QbV11.Legacy; QbV11.Legacy = true; try { Report("v10"); } finally { QbV11.Legacy = old; } }

        [Test] public void ReceiversTrackTheBallToTheCatchPoint() { Report("v11"); }

        static void Report(string tag0) { Report(tag0 + "A", 424000); Report(tag0 + "B", 760000); }

        static void Report(string tag, int seedBase)

        {
            int plays, att, comp, sacks, scr; double yards;
            var recs = Collect(900, seedBase, out plays, out att, out comp, out yards, out sacks, out scr);
            var ok = recs.Where(r => r.Zrel > 0f).ToList();
            TestContext.WriteLine(tag + " QBTRACK plays " + plays + " attempts " + att + " completions " + comp + " (" + (100.0 * comp / Math.Max(1, att)).ToString("0.0") + "%) yards/attempt (completed+sacks) " + (yards / Math.Max(1, att)).ToString("0.00") + " sacks " + sacks + " scrambles " + scr);
            TestContext.WriteLine(tag + " QBTRACK catch-point error m (all targets): " + P(ok.Select(r => r.ErrM)));
            TestContext.WriteLine(tag + " QBTRACK catch-point error m (completed): " + P(ok.Where(r => r.Complete).Select(r => r.ErrM)));
            TestContext.WriteLine(tag + " QBTRACK catch-point error m (incomplete): " + P(ok.Where(r => !r.Complete && !r.Picked).Select(r => r.ErrM)));
            TestContext.WriteLine(tag + " QBTRACK facing error to the ball deg (at the catch point step): " + P(ok.Select(r => r.FaceErr)));
            TestContext.WriteLine(tag + " QBTRACK within 1.0 m: " + (100.0 * ok.Count(r => r.ErrM <= 1.0f) / Math.Max(1, ok.Count)).ToString("0.0") + "%  within 2.0 m: " + (100.0 * ok.Count(r => r.ErrM <= 2.0f) / Math.Max(1, ok.Count)).ToString("0.0") + "%");
            { foreach (var r0 in ok) r0.Early = r0.LastT < 0.8f * r0.TTL - 0.08f; var miss = ok.Where(r => r.ErrM > 1.0f).ToList(); var cut = miss.Where(r => r.Early).ToList(); TestContext.WriteLine(tag + " QBTRACK misses cut short (tipped / deflected / taken in the air before the catch point): " + cut.Count + " of " + miss.Count + "; the rest (the ball reached the catch point): " + (miss.Count - cut.Count) + ", error m " + P(miss.Where(r => !r.Early).Select(r => r.ErrM)) + ", aim error " + P(miss.Where(r => !r.Early).Select(r => r.AimErrM)) + ", needed speed ratio " + P(miss.Where(r => !r.Early).Select(r => r.NeedRatio))); var unreach = miss.Where(r => r.NeedRatio > 1.0f).ToList(); var reach = miss.Where(r => r.NeedRatio <= 1.0f && !r.Early).ToList();
              TestContext.WriteLine(tag + " QBTRACK misses >1 m: " + miss.Count + " of " + ok.Count + "; ball out of his reach (needed speed > his top speed to the landing point): " + unreach.Count + "; reachable but missed: " + reach.Count + " (completed " + reach.Count(r => r.Complete) + ", intercepted " + reach.Count(r => r.Picked) + "); aim error of the throw m, misses " + P(miss.Select(r => r.AimErrM)) + ", all " + P(ok.Select(r => r.AimErrM)));
              foreach (var r1 in reach.Where(x => x.ErrM < 3.5f).Take(14)) TestContext.WriteLine(tag + " QBTRACK sample miss err " + r1.ErrM.ToString("0.0") + " aim " + r1.AimErrM.ToString("0.0") + " need " + r1.NeedRatio.ToString("0.00") + " speed " + (r1.RSpeed * Yd).ToString("0.0") + "/" + (r1.RVmax * Yd).ToString("0.0") + " m/s ttl " + r1.TTL.ToString("0.0") + " " + r1.Dbg);
              TestContext.WriteLine(tag + " QBTRACK reachable misses: error m " + P(reach.Select(r => r.ErrM)) + ", aim error " + P(reach.Select(r => r.AimErrM)) + ", needed-speed ratio " + P(reach.Select(r => r.NeedRatio)) + ", flight s " + P(reach.Select(r => r.TTL))); }
            foreach (ThrowStyle st in new[] { ThrowStyle.Bullet, ThrowStyle.Touch, ThrowStyle.Lob })
            {
                var s = ok.Where(r => r.Style == st).ToList();
                TestContext.WriteLine(tag + " QBTRACK style " + st + ": n " + s.Count + ", distance m " + P(s.Select(r => r.Dist)) + ", flight s " + P(s.Select(r => r.FlightT)) + ", error m " + P(s.Select(r => r.ErrM)));
            }
            Assert.Greater(ok.Count, 300);
        }
    }
}
