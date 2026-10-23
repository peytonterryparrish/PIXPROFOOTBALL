using System;
using System.Collections.Generic;
using System.Linq;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>
    /// The coverage probe (v0.10.0). It records a play at 60 Hz and compares every defender, every tick, with the rule his assignment gives him:
    ///   zone   - he reaches his landmark on time, holds it (shaded a yard or two toward the ball) and leaves it only to carry a receiver who is working in his zone (or the receiver a quarters
    ///            pattern-match gives him) or on a play-action bite; he never idles away from it;
    ///   man    - he stays on his man (no target swaps), never loses him by 6.5 yd laterally or 7 yd deep, never stands still while his man runs; a man whose receiver blocks is a FREE defender
    ///            who rats the hook, takes an uncovered runner or (Cover 0) rushes;
    ///   rush   - he heads to his gap lane for the first 0.6 s, is never left standing in the open, and does not run past the quarterback;
    ///   blitz  - he waits for his delay, then comes;
    ///   spy    - he stays within 5 yd of the quarterback laterally at 1.5-11 yd depth.
    /// The window is the pass pocket (ball still with the quarterback behind the line, set or dropping) and the scramble behind the line. A defender-frame inside a window that breaks the rule is a
    /// violation; a "lost" defender is one who stands idle for 0.8 s away from every post he could hold, on any play. Everything is engine-free and deterministic.
    /// </summary>
    internal sealed class CoverageAuditStats
    {
        public long Frames, Plays, Lost, Swaps, Releases, Matches, FreeFrames;
        public Dictionary<string, long> Kinds = new Dictionary<string, long>(), Viol = new Dictionary<string, long>(), KindViol = new Dictionary<string, long>();
        public Dictionary<string, long> SchemeFrames = new Dictionary<string, long>(), SchemeViol = new Dictionary<string, long>();
        public Dictionary<string, long> PairFrames = new Dictionary<string, long>(), PairViol = new Dictionary<string, long>();
        public List<string> Examples = new List<string>();

        public long Violations { get { long v = 0; foreach (long x in Viol.Values) v += x; return v; } }
        public double Rate { get { return Frames == 0 ? 0.0 : (double)Violations / Frames; } }

        private static void Add(Dictionary<string, long> d, string k, long n = 1) { long v; d.TryGetValue(k, out v); d[k] = v + n; }

        public void Merge(CoverageAuditStats o)
        {
            Frames += o.Frames; Plays += o.Plays; Lost += o.Lost; Swaps += o.Swaps; Releases += o.Releases; Matches += o.Matches; FreeFrames += o.FreeFrames;
            foreach (var kv in o.Kinds) Add(Kinds, kv.Key, kv.Value);
            foreach (var kv in o.Viol) Add(Viol, kv.Key, kv.Value);
            foreach (var kv in o.KindViol) Add(KindViol, kv.Key, kv.Value);
            foreach (var kv in o.SchemeFrames) Add(SchemeFrames, kv.Key, kv.Value);
            foreach (var kv in o.SchemeViol) Add(SchemeViol, kv.Key, kv.Value);
            foreach (var kv in o.PairFrames) Add(PairFrames, kv.Key, kv.Value);
            foreach (var kv in o.PairViol) Add(PairViol, kv.Key, kv.Value);
            foreach (string e in o.Examples) if (Examples.Count < 300) Examples.Add(e);
        }

        internal void Count(Dictionary<string, long> d, string k, long n = 1) { Add(d, k, n); }
    }

    internal static class CoverageAudit
    {
        private sealed class D
        {
            public int LastTarget = -2; public bool Init, IdleCounted, Arrived; public float IdleT, Tarr = -1f, OutT;
            public readonly HashSet<string> Reported = new HashSet<string>();
        }

        private static bool IsDeep(ZoneRegion z) { return z.Kind == ZoneKind.DeepThird || z.Kind == ZoneKind.DeepHalf || z.Kind == ZoneKind.DeepMiddle || z.Kind == ZoneKind.Quarter; }

        /// <summary>Lateral shade a zone takes toward the quarterback (see DefenseAI.ZoneShade): 0.30 / 2.2 yd underneath, 0.14 / 1.5 yd deep.</summary>
        private static float Shade(ZoneRegion z, float dxQb) { float k = IsDeep(z) ? 0.14f : 0.30f, cap = IsDeep(z) ? 1.5f : 2.2f; return Math.Max(-cap, Math.Min(cap, dxQb * k)); }

        public static CoverageAuditStats Run(Play off, Play def, int seed, int offOvr = 78, int defOvr = 78)
        {
            var st = new CoverageAuditStats { Plays = 1 };
            var sim = SimTestKit.Run(off, def, offOvr, defOvr, seed, null, null, new PlaySimOptions { Penalties = false });
            var ds = new D[11]; for (int i = 0; i < 11; i++) ds[i] = new D();
            bool isPass = off.IsPass && off.Category != PlayCategory.Screen;
            bool pa = off.Category == PlayCategory.PlayAction;
            string scheme = def.Scheme + (def.Category == PlayCategory.Blitz ? "+blitz" : "");
            string pair = off.Name + "|" + def.Name;
            var none = default(ControlInput);
            int guard = 0;
            while (sim.Phase != PlayPhase.Dead && guard++ < 3000)
            {
                sim.Step(none);
                if (sim.Phase != PlayPhase.Live) continue;
                float t = sim.Time; var qb = sim.Agents[0];
                bool pocket = isPass && !sim.PassIsThrown && sim.Carrier == qb && !sim.QbHasCrossedLos && qb.QbState != QbPhase.Scramble && qb.QbState != QbPhase.Done;
                bool scramble = isPass && !sim.PassIsThrown && sim.Carrier == qb && qb.QbState == QbPhase.Scramble && qb.Pos.Y < sim.LosY - 0.75f && !sim.QbHasCrossedLos;
                bool window = pocket || scramble;
                for (int k = 0; k < 11; k++)
                {
                    SimAgent a = sim.Agents[11 + k]; D d = ds[k];
                    if (d.Init && d.LastTarget >= 0 && a.CoverTarget >= 0 && a.CoverTarget != d.LastTarget && window) { st.Swaps++; Violate(st, "M-swap", "Man", scheme, pair, off, def, sim, a, t, d, true, false); }
                    if (d.Init && a.IsFree && d.LastTarget >= 0 && a.CoverTarget < 0 && window) st.Releases++;
                    d.LastTarget = a.CoverTarget; d.Init = true;
                    if (a.State == AgentState.Down || a.Body.FallenTime > 0f || a.State == AgentState.Tackling) { d.IdleT = 0; continue; }
                    bool engaged = a.State == AgentState.Engaged;
                    // ---- lost defender: idle for 0.8 s away from every post he could hold, on any play
                    SimAgent car = sim.Carrier; float carDist = car != null ? (car.Pos - a.Pos).Length : 99f;
                    if (!engaged && a.Speed < 0.5f && a.Want.Length < 1.0f && t > 0.5f && carDist > 6f && !a.ControlledByUser && sim.Ball.State != BallState.Dead && !sim.EndPending && !AtPost(sim, a))
                    {
                        d.IdleT += PlaySimulation.Dt;
                        if (d.IdleT > 0.8f && !d.IdleCounted) { d.IdleCounted = true; st.Lost++; if (st.Examples.Count < 300) st.Examples.Add("LOST " + pair + "|" + off.FormationName + "/" + def.FormationName + "|" + a.PlayPos + " t=" + t.ToString("0.0") + " " + Describe(sim, a) + " seed " + seed + " slot " + k); }
                    }
                    else if (a.Speed >= 0.9f || engaged || a.Want.Length >= 1.5f) d.IdleT = 0;
                    if (!window) continue;
                    st.Frames++; st.Count(st.SchemeFrames, scheme); st.Count(st.PairFrames, pair);
                    string kind; string v = Classify(sim, a, d, t, pa, engaged, out kind);
                    st.Count(st.Kinds, kind);
                    if (kind == "Free") st.FreeFrames++;
                    if (v != null) Violate(st, v, kind, scheme, pair, off, def, sim, a, t, d, false, scramble);
                }
            }
            for (int k = 0; k < 11; k++) { SimAgent a = sim.Agents[11 + k]; st.Matches += a.RuleMatches; }
            return st;
        }

        private static void Violate(CoverageAuditStats st, string v, string kind, string scheme, string pair, Play off, Play def, PlaySimulation sim, SimAgent a, float t, D d, bool swap, bool scramble)
        {
            st.Count(st.Viol, scramble ? "scr:" + v : v); st.Count(st.KindViol, kind); st.Count(st.SchemeViol, scheme); st.Count(st.PairViol, pair);
            if (st.Examples.Count < 300 && !d.Reported.Contains(v)) { d.Reported.Add(v); st.Examples.Add(v + " " + pair + "|" + off.FormationName + "/" + def.FormationName + "|" + a.PlayPos + " t=" + t.ToString("0.00") + " " + Describe(sim, a)); }
        }

        private static string Describe(PlaySimulation sim, SimAgent a)
        {
            return "at " + (a.Pos.X - sim.BallX0).ToString("0.0") + "," + (a.Pos.Y - sim.LosY).ToString("0.0") + " lm " + (a.Landmark.X - sim.BallX0).ToString("0.0") + "," + (a.Landmark.Y - sim.LosY).ToString("0.0") + (a.IsFree ? " free" : "") + (a.CoverTarget >= 0 ? " man " + sim.Agents[a.CoverTarget].Label : "");
        }

        private static bool AtPost(PlaySimulation sim, SimAgent a)
        {
            var cov = a.Assign as CoverageAssignment;
            if (a.Assign is SpyAssignment) return Math.Abs(a.Pos.X - sim.Agents[0].Pos.X) < 3.5f && a.Pos.Y - sim.LosY > 2.5f && a.Pos.Y - sim.LosY < 10f;
            if (cov != null && a.Zone != null && (!cov.IsMan || a.IsFree)) return (a.Pos - a.Landmark).Length < 3.5f;
            if (cov != null && cov.IsMan && a.CoverTarget >= 0) return (sim.Agents[a.CoverTarget].Speed < 1.0f);   // his man is standing too
            return a.Assign is RushAssignment || a.Assign is BlitzAssignment ? (a.Pos - sim.Agents[0].Pos).Length < 3.5f : false;
        }

        /// <summary>The receiver a quarters pattern-match gives this defender (mirrors DefenseAI.QuartersMatch, geometry only).</summary>
        private static SimAgent MatchedReceiver(PlaySimulation sim, SimAgent a)
        {
            ZoneRegion z = a.Zone; var mc = a.Assign as CoverageAssignment;
            if (z == null || (z.Kind != ZoneKind.Quarter && !(mc != null && mc.Match && z.Kind == ZoneKind.DeepThird && Math.Abs(z.CenterX) > 12f))) return null;
            int side = z.CenterX < 0f ? -1 : 1; bool corner = Math.Abs(z.CenterX) > 12f;
            var rx = new List<SimAgent>();
            for (int i = 1; i < 11; i++) { SimAgent r = sim.Agents[i]; if (!r.IsReceiverEligible || r.PlayPos == Position.RB || r.PlayPos == Position.FB) continue; if (Math.Sign(r.StartPos.X - sim.BallX0) != side) continue; rx.Add(r); }
            rx.Sort((p, q) => Math.Abs(q.StartPos.X - sim.BallX0).CompareTo(Math.Abs(p.StartPos.X - sim.BallX0)));
            SimAgent r1 = rx.Count > 0 ? rx[0] : null, r2 = rx.Count > 1 ? rx[1] : null;
            return corner ? r1 : r2 ?? r1;
        }

        private static string Classify(PlaySimulation sim, SimAgent a, D d, float t, bool pa, bool engaged, out string kind)
        {
            float los = sim.LosY; var asg = a.Assign; var qb = sim.Agents[0];
            if (asg is RushAssignment)
            {
                kind = "Rush"; if (engaged) return null;
                var ra = (RushAssignment)asg; float dq = (qb.Pos - a.Pos).Length;
                if (t > 0.5f && a.Speed < 1.0f && a.Want.Length < 1.5f && dq > 3f) return "R-idle";
                if (t < 0.6f && t > 0.3f)
                {
                    float gx = sim.BallX0 + DefenseAI.GapOffset(DefenseAI.GapOf(a, ra));
                    if (Math.Abs(a.Pos.X - gx) > Math.Abs(a.StartPos.X - gx) + 1.2f) return "R-lane";
                }
                if (a.Pos.Y < qb.Pos.Y - 2.5f && qb.QbState != QbPhase.Scramble && Math.Abs(a.Pos.X - qb.Pos.X) > 2.5f && t > 1.0f) return "R-overrun";
                return null;
            }
            if (asg is BlitzAssignment)
            {
                kind = "Blitz"; if (engaged) return null;
                var ba = (BlitzAssignment)asg;
                if (t > ba.Delay + 0.5f && a.Speed < 1.0f && a.Want.Length < 1.5f && (qb.Pos - a.Pos).Length > 3f) return "B-idle";
                if (t < ba.Delay - 0.02f && (a.Pos - a.StartPos).Length > 2.2f) return "B-early";
                return null;
            }
            if (asg is SpyAssignment)
            {
                kind = "Spy";
                if (t > 1.2f && Math.Abs(a.Pos.X - qb.Pos.X) > 5f) return "S-off";
                if (t > 1.2f && (a.Pos.Y - los < 1.5f || a.Pos.Y - los > 11f)) return "S-depth";
                return null;
            }
            var cov = asg as CoverageAssignment;
            if (cov == null) { kind = "Other"; return null; }
            if (a.IsFree && a.CoverTarget < 0)
            {
                kind = "Free";
                if (sim.DefPlay.Scheme == CoverageScheme.Cover0 && sim.Time < 3f) return (t > a.FreeSince + 0.6f && a.Speed < 1.0f && a.Want.Length < 1.5f && (qb.Pos - a.Pos).Length > 3f && !engaged) ? "F-idle" : null;
                if ((a.Landmark - a.Pos).Length > 3.5f && t > a.FreeSince + 1.0f && a.Speed < 0.6f) return "F-idle";
                return null;
            }
            if (cov.IsMan && a.CoverTarget >= 0)
            {
                kind = "Man"; var r = sim.Agents[a.CoverTarget]; float sep = (r.Pos - a.Pos).Length;
                if (r.State == AgentState.Engaged || r.Assign is BlockAssignment) return null;
                float lat = Math.Abs(r.Pos.X - a.Pos.X), beh = r.Pos.Y - a.Pos.Y;
                if (t > 1.4f && !r.RouteDone && r.Pos.Y > los - 1f && ((lat > 6.5f && sep > 6.5f) || (beh > 7.0f && r.Pos.Y > los + 3f))) return "M-lost";
                if (t > 0.8f && sep > 3.5f && a.Speed < 0.8f && a.Want.Length < 1.5f && !r.RouteDone && r.Speed > 2f) return "M-idle";
                return null;
            }
            kind = "Zone"; ZoneRegion z = a.Zone; if (z == null) return "Z-nozone";
            Vec2 L = a.Landmark; bool deep = IsDeep(z);
            float shade = Shade(z, qb.Pos.X - sim.BallX0);
            Vec2 E = new Vec2(L.X + shade, L.Y);
            if (d.Tarr < 0f) { float d0 = (L - a.StartPos).Length; d.Tarr = 0.35f + d0 / Math.Max(2f, a.Body.EffectiveVMax * 0.78f) + 0.35f; }
            float tol = deep ? 4.0f : 3.0f;
            float dist = (a.Pos - E).Length;
            bool inside = dist <= tol;
            if (inside) d.Arrived = true;
            if (t < d.Tarr) return null;
            if (pa && t < 1.1f && a.PlayPos != Position.CB) return null;   // the play-action bite
            if (inside) { d.OutT = 0; return null; }
            // excuses: a receiver working in his zone (box expanded 1.5 yd) and within 6 yd of him, or the receiver his quarters match gives him
            float halfX = z.Width * 0.5f + 1.5f, halfY = z.Height * 0.5f + 1.5f;
            for (int i = 1; i < 11; i++)
            {
                SimAgent r = sim.Agents[i];
                if (!r.IsReceiverEligible || r.State == AgentState.Down) continue;
                float rx = r.Pos.X - L.X, ry = r.Pos.Y - L.Y;
                bool inZ = Math.Abs(rx) <= halfX && (deep ? ry >= -3f : Math.Abs(ry) <= halfY);
                if (inZ && (r.Pos - a.Pos).Length < 6f) return null;
            }
            SimAgent m = MatchedReceiver(sim, a);
            if (m != null && m.Pos.Y - los > 6f && (m.Pos - a.Pos).Length < 7f) return null;
            d.OutT += PlaySimulation.Dt;
            if (d.OutT < 0.3f) return null;
            if (a.Speed < 0.4f && a.Want.Length < 0.5f) return "Z-idle";
            if (!d.Arrived && t > d.Tarr + 0.6f) return "Z-late";
            return d.Arrived ? "Z-leave" : null;
        }

        // ------------------------------------------------------------------ batch
        public static CoverageAuditStats Batch(IList<Play> offAll, IList<Play> defAll, int n, int seedBase)
        {
            var total = new CoverageAuditStats(); object lk = new object();
            System.Threading.Tasks.Parallel.For(0, n, i =>
            {
                var rr = new Rng(i * 31 + 7);
                var off = offAll[rr.Range(0, offAll.Count)]; var def = defAll[rr.Range(0, defAll.Count)];
                var s = Run(off, def, seedBase + i);
                lock (lk) total.Merge(s);
            });
            return total;
        }

        public static string Report(CoverageAuditStats t)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("plays " + t.Plays + "  defender-frames " + t.Frames + "  violation frames " + t.Violations + " = " + (100.0 * t.Rate).ToString("0.000") + "%  lost defenders " + t.Lost + " (" + (100.0 * t.Lost / Math.Max(1, t.Plays)).ToString("0.00") + " per 100 plays)  swaps " + t.Swaps + "  free releases " + t.Releases + "  quarters matches " + t.Matches + "  free-defender frames " + t.FreeFrames);
            sb.AppendLine("-- by kind");
            foreach (var kv in t.Kinds.OrderByDescending(x => x.Value)) { long v; t.KindViol.TryGetValue(kv.Key, out v); sb.AppendLine("  " + kv.Key.PadRight(7) + kv.Value.ToString().PadLeft(10) + "  " + (100.0 * v / kv.Value).ToString("0.000") + "%"); }
            sb.AppendLine("-- by violation");
            foreach (var kv in t.Viol.OrderByDescending(x => x.Value)) sb.AppendLine("  " + kv.Key.PadRight(11) + kv.Value.ToString().PadLeft(9) + "  " + (100.0 * kv.Value / Math.Max(1, t.Frames)).ToString("0.000") + "% of frames");
            sb.AppendLine("-- by coverage");
            foreach (var kv in t.SchemeFrames.OrderBy(x => x.Key)) { long v; t.SchemeViol.TryGetValue(kv.Key, out v); sb.AppendLine("  " + kv.Key.PadRight(16) + kv.Value.ToString().PadLeft(9) + "  " + (100.0 * v / kv.Value).ToString("0.000") + "%"); }
            sb.AppendLine("-- worst pairings");
            foreach (var kv in t.PairFrames.Where(x => x.Value > 2500).Select(x => { long v; t.PairViol.TryGetValue(x.Key, out v); return new { x.Key, F = x.Value, V = v }; }).OrderByDescending(x => (double)x.V / x.F).Take(10)) sb.AppendLine("  " + kv.Key.PadRight(44) + (100.0 * kv.V / kv.F).ToString("0.00") + "%");
            sb.AppendLine("-- examples");
            foreach (string e in t.Examples.Take(40)) sb.AppendLine("  " + e);
            return sb.ToString();
        }
    }
}
