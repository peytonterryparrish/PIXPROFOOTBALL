using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>
    /// v0.10.0 CHOREO: the choreographer must not change what happens in football terms. 600 plays with the real measured clip library (exported from the project with
    /// <c>ChoreoClipBuilder.Export</c>; the test is ignored when the file is missing) against the same plays without it: yards, completions, sacks, turnovers, play length.
    /// </summary>
    [Category("Slow")]
    public class ChoreoCalibrationTests
    {
        const string LibPath = "D:/PIXFootball/Work/Choreo/choreo_lib.bin";

        sealed class Tally
        {
            public int Plays, Rushes, Passes, Completions, Sacks, Ints, Fumbles, Tackles;
            public double RushYards, PassYards, Seconds;
            public int Bookings, Steered; public float MaxSteer, MaxStep; public int[] Owned = new int[3], Excess = new int[3]; public List<float> Resid = new List<float>();
        }

        static Tally Run(IChoreoLibrary lib, int n)
        {
            var t = new Tally();
            string[] offs = { "Inside Zone Right", "Outside Zone Left", "Power Left", "Four Verticals", "Slants", "Curl-Flat", "Draw Right", "Mesh" };
            string[] forms = { "I-Form", "I-Form", "I-Form", "Shotgun", "Shotgun", "Shotgun", "Shotgun", "Shotgun" };
            string[] defs = { "Cover 3 Sky", "Cover 2 Zone", "Cover 1 Man Free", "Cover 4 Quarters" };
            string[] dforms = { "4-3 Base", "4-3 Base", "4-3 Base", "4-3 Base" };
            for (int s = 0; s < n; s++)
            {
                int k = s % offs.Length, d = (s / offs.Length) % defs.Length;
                PlaySimulation sim;
                try { sim = SimTestKit.RunNamed(offs[k], forms[k], defs[d], dforms[d], 75, 75, 7000 + s, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false, ChoreoLibrary = lib }); }
                catch (ArgumentException) { continue; }
                PlayResult r = sim.RunToCompletion();
                t.Plays++;
                bool pass = sim.OffPlay.IsPass;
                float yards = r != null ? r.Yards : 0f;
                if (pass) { t.Passes++; t.PassYards += yards; if (sim.PassCompleted) t.Completions++; } else { t.Rushes++; t.RushYards += yards; }
                if (sim.QbSack) t.Sacks++;
                if (sim.Turnover && sim.TurnoverInterception) t.Ints++;
                if (sim.Log.Has(SimEventType.Fumble)) t.Fumbles++;
                t.Tackles += sim.Log.CountOf(SimEventType.Tackle);
                t.Seconds += sim.Time;
                if (sim.Choreo != null) { for (int q = 0; q < 3; q++) { t.Owned[q] += sim.Choreo.Stats.OwnedByAge[q]; t.Excess[q] += sim.Choreo.Stats.ExcessTicks[q]; } t.Bookings += sim.Choreo.Stats.Bookings; t.Steered += sim.Choreo.Stats.SteeredTicks; t.MaxSteer = Math.Max(t.MaxSteer, sim.Choreo.Stats.MaxSteerRate); foreach (var b in sim.Choreo.Bookings) { if (b.Kind == ChoreoKind.Tackle && !b.Joiner) t.Resid.Add(Math.Max(b.ResidA.Length, b.ResidB.Length)); t.MaxStep = Math.Max(t.MaxStep, b.MaxStepErr); if (b.MaxStepErr > 0.4f) Console.WriteLine("BIGSTEP " + b + " step " + b.MaxStepErr.ToString("0.00") + " resid " + b.ResidA.Length.ToString("0.00") + "/" + b.ResidB.Length.ToString("0.00") + " warp " + b.WarpOut.ToString("0.00") + " slide " + b.SlideA.Length.ToString("0.0") + "/" + b.SlideB.Length.ToString("0.0") + " rate " + b.Rate.ToString("0.00") + " play " + s); } }
            }
            return t;
        }

        [Test]
        public void TheChoreographerDoesNotMoveTheFootballNumbers()
        {
            if (!File.Exists(LibPath)) Assert.Ignore("no exported clip library at " + LibPath);
            IChoreoLibrary lib;
            using (var f = File.OpenRead(LibPath)) lib = ChoreoLibraryIO.Read(f);
            Tally off = Run(null, 600), on = Run(lib, 600);
            Console.WriteLine("CHOREO CALIBRATION plays " + off.Plays + "/" + on.Plays);
            Console.WriteLine(string.Format("  rush yd/play   off {0:0.00} on {1:0.00}", off.RushYards / Math.Max(1, off.Rushes), on.RushYards / Math.Max(1, on.Rushes)));
            Console.WriteLine(string.Format("  pass yd/play   off {0:0.00} on {1:0.00}", off.PassYards / Math.Max(1, off.Passes), on.PassYards / Math.Max(1, on.Passes)));
            Console.WriteLine(string.Format("  completion     off {0:P1} on {1:P1}", off.Completions / (double)Math.Max(1, off.Passes), on.Completions / (double)Math.Max(1, on.Passes)));
            Console.WriteLine(string.Format("  sacks          off {0} on {1};  ints off {2} on {3};  fumbles off {4} on {5};  tackles off {6} on {7}", off.Sacks, on.Sacks, off.Ints, on.Ints, off.Fumbles, on.Fumbles, off.Tackles, on.Tackles));
            Console.WriteLine(string.Format("  seconds/play   off {0:0.00} on {1:0.00};  bookings {2}, steered ticks {3}, max steer {4:0.00} yd/s, max one-tick step {5:0.000} yd", off.Seconds / off.Plays, on.Seconds / on.Plays, on.Bookings, on.Steered, on.MaxSteer, on.MaxStep));
            { var rs = on.Resid.OrderBy(x => x).ToList(); if (rs.Count > 0) Console.WriteLine(string.Format("  placement residual of a tackle booking (yd): mean {0:0.00}, p50 {1:0.00}, p90 {2:0.00}, max {3:0.00}, n {4}", rs.Average(), rs[rs.Count / 2], rs[(int)(rs.Count * 0.9)], rs[rs.Count - 1], rs.Count)); }
            Console.WriteLine(string.Format("  floating by construction (visible step differs from the take's own root step by more than 5 cm): age under 0.3 s {0:P1} of {1}, 0.3 to 0.6 s {2:P1} of {3}, over 0.6 s {4:P1} of {5}", on.Excess[0] / (double)Math.Max(1, on.Owned[0]), on.Owned[0], on.Excess[1] / (double)Math.Max(1, on.Owned[1]), on.Owned[1], on.Excess[2] / (double)Math.Max(1, on.Owned[2]), on.Owned[2]));
            Assert.AreEqual(off.RushYards / off.Rushes, on.RushYards / on.Rushes, 0.45, "rushing yards per play");
            Assert.AreEqual(off.PassYards / off.Passes, on.PassYards / on.Passes, 0.9, "passing yards per play");
            Assert.AreEqual(off.Completions / (double)off.Passes, on.Completions / (double)on.Passes, 0.05, "completion rate");
            Assert.AreEqual(off.Seconds / off.Plays, on.Seconds / on.Plays, 0.35, "play length");
            Assert.LessOrEqual(on.MaxSteer, 2.3f);
            Assert.Greater(on.Bookings, 100);
        }

        [Test, Category("Slow"), Timeout(1800000)]      // 7 runs of 4000 plays: minutes, not part of the quick suite
        public void ALargeSampleOfPlaysAgrees()
        {
            if (!File.Exists(LibPath)) Assert.Ignore("no exported clip library at " + LibPath);
            IChoreoLibrary lib;
            using (var f = File.OpenRead(LibPath)) lib = ChoreoLibraryIO.Read(f);
            Tally off = Run(null, 4000), on = Run(lib, 4000);
            Console.WriteLine(string.Format("LARGE 4000 plays: rush yd off {0:0.00} on {1:0.00}; pass yd off {2:0.00} on {3:0.00}; completion off {4:P1} on {5:P1}; sacks off {6} on {7}; ints off {8} on {9}; fumbles off {10} on {11}; seconds off {12:0.00} on {13:0.00}", off.RushYards / off.Rushes, on.RushYards / on.Rushes, off.PassYards / off.Passes, on.PassYards / on.Passes, off.Completions / (double)off.Passes, on.Completions / (double)on.Passes, off.Sacks, on.Sacks, off.Ints, on.Ints, off.Fumbles, on.Fumbles, off.Seconds / off.Plays, on.Seconds / on.Plays));
            foreach (var v in new[] { "no steer", "no hand", "no steer, no hand", "nothing (forecast and booking only)", "no tackle steer", "no catch steer" })
            {
                Choreographer.DebugNoSteerTackle = v == "no tackle steer"; Choreographer.DebugNoSteerCatch = v == "no catch steer"; Choreographer.DebugNoSteer = v.Contains("no steer") || v.StartsWith("nothing"); Choreographer.DebugNoHand = v.Contains("no hand") || v.StartsWith("nothing"); Choreographer.DebugNoFollow = v.StartsWith("nothing");
                Tally nf = Run(lib, 4000);
                Console.WriteLine(string.Format("LARGE [{0}]: rush {1:0.00} pass {2:0.00} compl {3:P1} ints {4}", v, nf.RushYards / nf.Rushes, nf.PassYards / nf.Passes, nf.Completions / (double)nf.Passes, nf.Ints));
            }
            Choreographer.DebugNoSteer = Choreographer.DebugNoHand = Choreographer.DebugNoFollow = Choreographer.DebugNoSteerTackle = Choreographer.DebugNoSteerCatch = false;
            Assert.AreEqual(off.RushYards / off.Rushes, on.RushYards / on.Rushes, 0.40, "rushing yards per play");
            Assert.AreEqual(off.PassYards / off.Passes, on.PassYards / on.Passes, 0.85, "passing yards per play");
            Assert.AreEqual(off.Completions / (double)off.Passes, on.Completions / (double)on.Passes, 0.03, "completion rate");
        }

        struct Rec { public bool Pass, Complete, Sack, Int; public float Yards; public int Rolls, Books, Wins; }

        static Rec[] Records(IChoreoLibrary lib, int n)
        {
            string[] offs = { "Four Verticals", "Slants", "Curl-Flat", "Mesh" };
            string[] defs = { "Cover 3 Sky", "Cover 2 Zone", "Cover 1 Man Free", "Cover 4 Quarters" };
            var res = new Rec[n];
            for (int s = 0; s < n; s++)
            {
                int k = s % offs.Length, d = (s / offs.Length) % defs.Length;
                PlaySimulation sim;
                try { sim = SimTestKit.RunNamed(offs[k], "Shotgun", defs[d], "4-3 Base", 75, 75, 7000 + s, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false, ChoreoLibrary = lib }); }
                catch (ArgumentException) { continue; }
                PlayResult r = sim.RunToCompletion();
                res[s] = new Rec { Pass = sim.OffPlay.IsPass, Yards = r != null ? r.Yards : 0f, Complete = sim.PassCompleted, Sack = sim.QbSack, Int = sim.Turnover && sim.TurnoverInterception, Rolls = sim.Choreo != null ? sim.Choreo.Stats.DuelRolls : 0, Books = sim.Choreo != null ? sim.Choreo.Stats.DuelBookings : 0, Wins = sim.Choreo != null ? sim.Choreo.Stats.DuelCatches : 0 };
            }
            return res;
        }

        /// <summary>Where do the passing yards go? The same plays, choreography off against on, paired by seed: yards by outcome class (diagnostic, prints; asserts nothing).</summary>
        static void PairedStats(string name, Rec[] a, Rec[] b)
        {
            double s = 0, s2 = 0; int n = 0, diff = 0;
            for (int i = 0; i < a.Length; i++) { if (!a[i].Pass || !b[i].Pass) continue; double d = b[i].Yards - a[i].Yards; s += d; s2 += d * d; n++; if (Math.Abs(d) > 0.01) diff++; }
            double mean = s / n, sd = Math.Sqrt(Math.Max(0, s2 / n - mean * mean)), se = sd / Math.Sqrt(n);
            Console.WriteLine(string.Format("PASS YARDS SIGNIFICANCE [{0}] over {1} passes: mean {2:0.000} yd, standard error {3:0.000}, {4:0.0} sigma; plays that differ {5}", name, n, mean, se, mean / Math.Max(1e-9, se), diff));
        }

        /// <summary>Is the pass-yard tilt real? Paired mean and its standard error over a large sample, full choreography and with the steering off.</summary>
        [Test, Category("Slow"), Timeout(3600000)]
        public void IsThePassYardTiltReal()
        {
            if (!File.Exists(LibPath)) Assert.Ignore("no exported clip library at " + LibPath);
            IChoreoLibrary lib;
            using (var f = File.OpenRead(LibPath)) lib = ChoreoLibraryIO.Read(f);
            const int N = 9000;
            var off = Records(null, N); var on = Records(lib, N);
            PairedStats("all on", off, on);
            Choreographer.DebugNoSteer = true; var ns = Records(lib, N); Choreographer.DebugNoSteer = false; Choreographer.SteerSacks = true; var sk = Records(lib, N); Choreographer.SteerSacks = false; PairedStats("rushers steered toward the quarterback as well", off, sk);
            PairedStats("no steering", off, ns);
            PairedStats("steering effect (on against no steering)", ns, on);
            Choreographer.SteerCatchersOn = true; var sc = Records(lib, N); Choreographer.SteerCatchersOn = false; PairedStats("receivers steered toward the ball as well", off, sc);
            Choreographer.DebugNoSteerCatch = true; var nc = Records(lib, N); Choreographer.DebugNoSteerCatch = false; PairedStats("catcher steering off, pursuers on", off, nc);
            Choreographer.DebugNoSteerTackle = true; var nt = Records(lib, N); Choreographer.DebugNoSteerTackle = false; PairedStats("pursuer steering off, catchers on", off, nt);
        }

        [Test, Category("Slow"), Timeout(1800000)]
        public void WhereThePassingYardsGo()
        {
            if (!File.Exists(LibPath)) Assert.Ignore("no exported clip library at " + LibPath);
            IChoreoLibrary lib;
            using (var f = File.OpenRead(LibPath)) lib = ChoreoLibraryIO.Read(f);
            const int N = 3000;
            var off = Records(null, N); var on = Records(lib, N);
            int n = 0, same = 0, bothC = 0, offCOnN = 0, offNOnC = 0, sackToNo = 0, noToSack = 0, intDiff = 0; double dAll = 0, dBothC = 0, dOffCOnN = 0, dOffNOnC = 0, dSack = 0, dNoSack = 0, dRest = 0;
            for (int i = 0; i < N; i++)
            {
                if (!off[i].Pass || !on[i].Pass) continue;
                n++; double d = on[i].Yards - off[i].Yards; dAll += d;
                if (Math.Abs(d) < 0.01) same++;
                if (off[i].Sack != on[i].Sack) { if (off[i].Sack) { sackToNo++; dSack += d; } else { noToSack++; dNoSack += d; } continue; }
                if (off[i].Complete && on[i].Complete) { bothC++; dBothC += d; }
                else if (off[i].Complete && !on[i].Complete) { offCOnN++; dOffCOnN += d; }
                else if (!off[i].Complete && on[i].Complete) { offNOnC++; dOffNOnC += d; }
                else dRest += d;
                if (off[i].Int != on[i].Int) intDiff++;
            }
            Console.WriteLine(string.Format("PASS YARDS PAIRED over {0} passes: mean diff {1:0.000} yd; identical plays {2}", n, dAll / n, same));
            Console.WriteLine(string.Format("  both complete: {0} plays, sum {1:0.0}, per pass {2:0.000}", bothC, dBothC, dBothC / n));
            Console.WriteLine(string.Format("  complete off / not on: {0} plays, per pass {1:0.000}; not off / complete on: {2} plays, per pass {3:0.000}", offCOnN, dOffCOnN / n, offNOnC, dOffNOnC / n));
            Console.WriteLine(string.Format("  sack off / none on: {0}, per pass {1:0.000}; none off / sack on: {2}, per pass {3:0.000}", sackToNo, dSack / n, noToSack, dNoSack / n));
            Console.WriteLine(string.Format("  both incomplete / other: per pass {0:0.000}; interception flips {1}", dRest / n, intDiff));
            int oc = 0, nc = 0, oi = 0, ni = 0, oinc = 0, ninc = 0, rolls = 0, books = 0, wins = 0;
            for (int i = 0; i < N; i++) { if (!off[i].Pass || !on[i].Pass) continue; if (off[i].Complete) oc++; if (on[i].Complete) nc++; if (off[i].Int) oi++; if (on[i].Int) ni++; if (!off[i].Complete && !off[i].Int && !off[i].Sack) oinc++; if (!on[i].Complete && !on[i].Int && !on[i].Sack) ninc++; rolls += on[i].Rolls; books += on[i].Books; wins += on[i].Wins; }
            foreach (var v in new[] { "pursuer 0.5", "pursuer 0.25", "catcher 0.5", "pursuer 0.5 catcher 0.5", "no steer" })
            {
                Choreographer.PursuerSteerScale = v.StartsWith("pursuer 0.5") ? 0.5f : (v.StartsWith("pursuer 0.25") ? 0.25f : 1f); Choreographer.CatcherSteerScale = v.Contains("catcher 0.5") ? 0.5f : 1f; Choreographer.DebugNoSteer = v.Contains("no steer,") || v == "no steer" || v.StartsWith("no tackle booking"); Choreographer.DebugNoFollow = v.Contains("no follow") || v.StartsWith("no tackle"); Choreographer.DebugNoHand = v == "no hand" || v.StartsWith("no tackle");
                var vr = Records(lib, N); double dd = 0, dc = 0; int nn = 0, nb = 0;
                for (int i = 0; i < N; i++) { if (!off[i].Pass || !vr[i].Pass) continue; nn++; dd += vr[i].Yards - off[i].Yards; if (off[i].Complete && vr[i].Complete && off[i].Sack == vr[i].Sack) { nb++; dc += vr[i].Yards - off[i].Yards; } }
                Console.WriteLine(string.Format("PASS YARDS VARIANT [{0}]: mean diff {1:0.000} per pass, both-complete part {2:0.000} per pass", v, dd / nn, dc / nn));
            }
            Choreographer.DebugNoSteer = Choreographer.DebugNoFollow = Choreographer.DebugNoHand = Choreographer.DebugNoSteerCatch = Choreographer.DebugNoSteerTackle = false; Choreographer.PursuerSteerScale = Choreographer.CatcherSteerScale = 1f;
            Console.WriteLine(string.Format("PASS OUTCOMES over {0} passes, off / on: completions {1} / {2}, interceptions {3} / {4}, incomplete (drop, deflection) {5} / {6}; duels rolled ahead {7}, receiver wins {8}, pair takes booked {9}", n, oc, nc, oi, ni, oinc, ninc, rolls, wins, books));
        }

        [Test]
        public void SameSeedSameSequenceWithTheRealLibrary()
        {
            if (!File.Exists(LibPath)) Assert.Ignore("no exported clip library at " + LibPath);
            IChoreoLibrary lib;
            using (var f = File.OpenRead(LibPath)) lib = ChoreoLibraryIO.Read(f);
            for (int s = 0; s < 24; s++)
            {
                string a = Digest(lib, 8100 + s), b = Digest(lib, 8100 + s);
                Assert.AreEqual(a, b, "play " + s);
            }
        }

        static string Digest(IChoreoLibrary lib, int seed)
        {
            string[] offs = { "Inside Zone Right", "Four Verticals", "Slants", "Outside Zone Left" };
            string[] forms = { "I-Form", "Shotgun", "Shotgun", "I-Form" };
            int k = seed % 4;
            var sim = SimTestKit.RunNamed(offs[k], forms[k], "Cover 3 Sky", "4-3 Base", 75, 75, seed, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false, ChoreoLibrary = lib });
            sim.RunToCompletion();
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 22; i++) sb.Append(sim.Agents[i].Pos.X.ToString("0.0000")).Append(',').Append(sim.Agents[i].Pos.Y.ToString("0.0000")).Append(';');
            foreach (var b in sim.Choreo.Bookings) sb.Append(b.ToString()).Append(b.StartSim.ToString("0.000")).Append('|');
            sb.Append(sim.Log.Digest());
            return sb.ToString();
        }

        [Test]
        public void WhichMechanismMovesWhat()
        {
            if (!File.Exists(LibPath)) Assert.Ignore("no exported clip library at " + LibPath);
            IChoreoLibrary lib;
            using (var f = File.OpenRead(LibPath)) lib = ChoreoLibraryIO.Read(f);
            Tally off = Run(null, 600);
            Console.WriteLine("VARIANTS rush yd off {0:0.00} pass yd off {1:0.00} compl off {2:P1} sec off {3:0.00}", off.RushYards / off.Rushes, off.PassYards / off.Passes, off.Completions / (double)off.Passes, off.Seconds / off.Plays);
            foreach (var v in new[] { "all on", "no steer", "no follow", "no steer, no follow" })
            {
                Choreographer.DebugNoSteer = v.Contains("no steer"); Choreographer.DebugNoFollow = v.Contains("no follow");
                Tally on = Run(lib, 600);
                Console.WriteLine("VARIANTS [{0}] rush {1:0.00} pass {2:0.00} compl {3:P1} sec {4:0.00} sacks {5} ints {6} tackles {7}", v, on.RushYards / on.Rushes, on.PassYards / on.Passes, on.Completions / (double)on.Passes, on.Seconds / on.Plays, on.Sacks, on.Ints, on.Tackles);
            }
            Choreographer.DebugNoSteer = false; Choreographer.DebugNoFollow = false;
        }
    }
}
