using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>
    /// v0.8.0 harness for the ability tests: paired experiments (the same seed, the same rosters, with and without one ability on one player) and a
    /// small statistics helper. A trial returns a numerator and a denominator (for example "juke won, juke attempted"); the result is the ratio of sums
    /// for each arm and a standard error from the per-trial residuals.
    /// </summary>
    internal static class AbilityLab
    {
        public struct Tally { public double X, N; public Tally(double x, double n) { X = x; N = n; } }

        public sealed class Arm
        {
            public double X, N, Var;
            public double Rate { get { return N > 0 ? X / N : 0; } }
            public double Se { get { return N > 0 ? Math.Sqrt(Var) / N : 0; } }
        }

        public sealed class Result
        {
            public string Name;
            public Arm Base, With;
            /// <summary>Standard error of the difference when both arms ran the same seeds (the per-seed noise is shared, so it is much smaller than the unpaired figure).</summary>
            public double PairedSe;
            public double Delta { get { return With.Rate - Base.Rate; } }
            public double Relative { get { return Base.Rate != 0 ? Delta / Base.Rate : 0; } }
            public double Se { get { return PairedSe > 0 ? PairedSe : Math.Sqrt(With.Se * With.Se + Base.Se * Base.Se); } }
            public double Z { get { return Se > 0 ? Delta / Se : 0; } }
            public override string ToString()
            {
                return Name + ": base " + Base.Rate.ToString("0.000") + " (n=" + Base.N.ToString("0") + ")  with " + With.Rate.ToString("0.000") + " (n=" + With.N.ToString("0")
                       + ")  delta " + (Delta >= 0 ? "+" : "") + Delta.ToString("0.000") + " (" + (Relative >= 0 ? "+" : "") + (100 * Relative).ToString("0.0") + "%, z " + Z.ToString("0.0") + ")";
            }
        }

        public static void Give(Player p, string id, int tier)
        {
            p.Abilities.RemoveAll(a => a.Id == id);
            p.Abilities.Add(new PlayerAbility { Id = id, Tier = tier });
        }

        public static int SlotOf(Formation f, Position pos, int nth = 0)
        {
            int seen = 0;
            for (int i = 0; i < f.Slots.Length; i++)
                if (f.Slots[i].Position == pos) { if (seen == nth) return i; seen++; }
            return -1;
        }

        public static int SlotOfGroup(Formation f, Func<Position, bool> test, int nth = 0)
        {
            int seen = 0;
            for (int i = 0; i < f.Slots.Length; i++)
                if (test(f.Slots[i].Position)) { if (seen == nth) return i; seen++; }
            return -1;
        }

        public delegate void Mutate(Player[] offense, Player[] defense, Formation offForm, Formation defForm);

        /// <summary>Builds a play like <see cref="SimTestKit.Run"/> but lets the experiment edit the two rosters (the same seed always yields the same base rosters).</summary>
        public static PlaySimulation Build(Play off, Play def, int offOvr, int defOvr, int seed, Mutate mutate = null, GameState gs = null, PlaySimOptions opts = null, RuleSet rules = null)
        {
            rules = rules ?? RuleSet.Pro();
            gs = gs ?? SimTestKit.Scrimmage(rules);
            var rng = new Rng(seed);
            var sim = new PlaySimulation();
            Formation of = SimTestKit.Playbook.FindFormation(off.FormationName);
            Formation df = SimTestKit.Playbook.FindFormation(def.FormationName);
            Player[] ro = SimTestKit.Roster(of, offOvr, rng);
            Player[] rd = SimTestKit.Roster(df, defOvr, rng);
            if (mutate != null) mutate(ro, rd, of, df);
            sim.Setup(gs, off, def, ro, rd, rules, rng.Fork(), opts ?? new PlaySimOptions { Penalties = false });
            return sim;
        }

        public static Play[] Offense(string baseName)
        {
            var l = new List<Play>();
            foreach (Play p in SimTestKit.Playbook.AllPlays)
            {
                if (p.Side != PlaySide.Offense) continue;
                string n = p.Name;
                if (n.EndsWith(" Right")) n = n.Substring(0, n.Length - 6); else if (n.EndsWith(" Left")) n = n.Substring(0, n.Length - 5);
                if (n == baseName) l.Add(p);
            }
            if (l.Count == 0) throw new ArgumentException("No offensive concept " + baseName);
            return l.ToArray();
        }

        public static Result Pair(string name, int trials, int seedBase, Func<int, bool, Tally> trial)
        {
            var b = new Tally[trials];
            var w = new Tally[trials];
            Parallel.For(0, trials, i =>
            {
                b[i] = trial(seedBase + i, false);
                w[i] = trial(seedBase + i, true);
            });
            Arm ab = Fold(b), aw = Fold(w);
            double v = 0;
            for (int i = 0; i < trials; i++)
            {
                double infl = (ab.N > 0 ? (w[i].X - aw.Rate * w[i].N) / Math.Max(1e-9, aw.N) : 0) - (ab.N > 0 ? (b[i].X - ab.Rate * b[i].N) / Math.Max(1e-9, ab.N) : 0);
                v += infl * infl;
            }
            return new Result { Name = name, Base = ab, With = aw, PairedSe = Math.Sqrt(v) };
        }

        private static Arm Fold(Tally[] t)
        {
            double x = 0, n = 0;
            for (int i = 0; i < t.Length; i++) { x += t[i].X; n += t[i].N; }
            double r = n > 0 ? x / n : 0, v = 0;
            for (int i = 0; i < t.Length; i++) { double d = t[i].X - r * t[i].N; v += d * d; }
            return new Arm { X = x, N = n, Var = v };
        }

        /// <summary>
        /// A staged jump ball: receiver (offense slot 10) and one defender (defense slot <paramref name="defSlot"/>, keeping his coverage assignment) a step apart under a ball at the
        /// given height. Returns true when the receiver comes down with it. The edits run on the two players before the play is set up.
        /// </summary>
        public static bool JumpBall(int seed, Play defense, int defSlot, Action<Player> editReceiver, Action<Player> editDefender, float ballZ = 2.5f)
        {
            PlaySimulation sim = Build(SimTestKit.Offense("Four Verticals", "Shotgun"), defense, 75, 75, seed, (ro, rd, of, df) =>
            {
                if (editReceiver != null) editReceiver(ro[10]);
                if (editDefender != null) editDefender(rd[defSlot]);
            });
            SimTestKit.StepToLive(sim);
            SimTestKit.ParkAll(sim, 2f);
            SimAgent rec = sim.Agents[10];
            SimTestKit.Place(rec, 27f, 48f); rec.Body.Facing = new Vec2(0f, -1f);
            SimAgent d = sim.Agents[11 + defSlot];
            SimTestKit.Place(d, 27.5f, 48.4f); d.Body.Facing = new Vec2(0f, -1f);
            if (d.Assign is CoverageAssignment && ((CoverageAssignment)d.Assign).IsMan) d.CoverTarget = rec.Id;   // a man defender sticks to the staged receiver
            sim.Qb.HasBall = false; sim.Qb.QbState = QbPhase.Done;
            Vec3 from = new Vec3(27f, 42f, 1.9f), to = new Vec3(27f, 48f, ballZ);
            Vec3 dv = to - from;
            float t = Math.Max(0.05f, dv.XY.Length / 20f);
            sim.Ball.Launch(BallState.Pass, from, new Vec3(dv.X / t, dv.Y / t, (dv.Z + 0.5f * SimMath.Gravity * t * t) / t), 0);
            sim.OnPassReleased(sim.Qb, rec, false);
            for (int i = 0; i < 70 && sim.Phase == PlayPhase.Live; i++)
            {
                sim.Step(default(ControlInput));
                if (sim.PassCompleted || sim.Turnover || sim.Ball.State == BallState.Dead) break;
            }
            return sim.PassCompleted;
        }

        /// <summary>Steps a play with no input until it is dead (or the guard trips).</summary>
        public static PlayResult Finish(PlaySimulation sim) { return sim.RunToCompletion(); }
    }
}
