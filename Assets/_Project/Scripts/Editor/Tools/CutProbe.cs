using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.12.0 GAIT: cut audit. Every frame of every running body goes in (final pose); at the end of a play each body's series is searched for direction changes of the SIMULATED path of more than 45 degrees within 0.6 s
    /// (speed over 2 m/s throughout). For each one: is a foot planted on the turf (sole lower than 3 cm, 6 frames or more, overlapping +-0.15 s around the middle of the turn) and does it hold (drift of the planted point under 4 cm: the smaller of the
    /// ankle and the ball of the foot, which may pivot), is it the outside foot, and how far the pelvis drops into the cut (mean pelvis height 0.7-0.3 s before against +-0.1 s around the middle).
    /// </summary>
    public static class CutProbe
    {
        public struct Fr { public Vector2 Vel; public float Speed; public bool Valid; public Vector3 AL, AR, TL, TR; public float SoleL, SoleR, HipsY; public bool FlagL, FlagR; public float RigW, MmLayer; public string Clip; public AgentAction Act; }
        sealed class Series { public string Pos; public bool Carry; public readonly List<Fr> F = new List<Fr>(); }
        sealed class Ev
        {
            public string Pos, Group; public float Angle, Speed, Dur, Drift, OutDrift, Drop, DropMin, SpeedAfter; public float Da, Dt, DriftLoose, DriftShort; public int Len; public bool Planted, PlantedOutside, HaveDrop, FlagPlanted, Control, MmOwn; public string Act, Clip; public int Play;
        }

        const float OnTurf = 0.025f, OnTurfGeo = 0.015f, DriftMax = 0.04f; static float _thr = OnTurf; static bool _geo; const int MinFrames = 6; static int _minFrames = 6, Half = 9;
        static readonly Dictionary<int, Series> Cur = new Dictionary<int, Series>();
        static readonly List<Ev> Events = new List<Ev>();
        static int _play;

        public static void Reset() { Cur.Clear(); Events.Clear(); Trace.Clear(); _play = 0; }

        public static void Feed(int agent, string pos, bool carry, Fr f)
        {
            Series s; if (!Cur.TryGetValue(agent, out s)) Cur[agent] = s = new Series { Pos = pos, Carry = carry };
            s.Carry = s.Carry || carry; s.F.Add(f);
        }

        public static void EndPlay()
        {
            _play++;
            foreach (var kv in Cur) Analyze(kv.Value);
            Cur.Clear();
        }

        static float Hdg(Fr f) { return Mathf.Atan2(f.Vel.x, f.Vel.y) * Mathf.Rad2Deg; }

        static void Analyze(Series s)
        {
            var F = s.F; int n = F.Count; if (n < 60) return;
            var hdg = new float[n]; for (int k = 0; k < n; k++) hdg[k] = Hdg(F[k]);
            var skip = new bool[n]; int i = 0;
            while (i < n - 1)
            {
                if (!(F[i].Valid && F[i].Speed > 2.5f)) { i++; continue; }
                int j = i; bool found = false;
                for (int k = i + 1; k <= Math.Min(n - 1, i + 36); k++)
                {
                    if (!(F[k].Valid && F[k].Speed > 2.0f)) break;
                    if (Mathf.Abs(Mathf.DeltaAngle(hdg[i], hdg[k])) > 45f) { found = true; j = k; break; }
                }
                if (!found) { i++; continue; }
                while (j + 6 < n && F[j + 6].Valid && F[j + 6].Speed > 2.0f && Mathf.Abs(Mathf.DeltaAngle(hdg[j], hdg[j + 6])) > 12f && j - i < 54) j++;
                float total = Mathf.DeltaAngle(hdg[i], hdg[j]);
                int m = i; for (int k = i; k <= j; k++) if (Mathf.Abs(Mathf.DeltaAngle(hdg[i], hdg[k])) >= 0.5f * Mathf.Abs(total)) { m = k; break; }
                Evaluate(s, m, i, j, total);
                for (int k = Math.Max(0, i - 40); k <= Math.Min(n - 1, j + 40); k++) skip[k] = true;
                i = j + 12;
            }
            // control: straight running at speed (heading within 10 degrees over +-0.3 s, no cut near), the same windows, so a planted foot of a cut can be compared with a planted foot of a straight stride
            for (int m = 45; m < n - 45; m += 24)
            {
                bool ok = true; for (int k = m - 18; k <= m + 18 && ok; k++) ok = F[k].Valid && F[k].Speed > 3f && !skip[k];
                if (!ok || Mathf.Abs(Mathf.DeltaAngle(hdg[m - 18], hdg[m + 18])) > 10f) continue;
                Evaluate(s, m, m - 18, m + 18, 0f, true);
            }
        }

        // runs of "sole on the turf" for one foot; returns the best drift of a run of at least MinFrames overlapping [lo, hi]
        static float BestDrift(List<Fr> F, bool left, int lo, int hi)
        {
            int n = F.Count; float best = 9f; BestDa = BestDt = 9f; BestLen = 0; int a = Math.Max(0, lo - 30), b = Math.Min(n - 1, hi + 30);
            int k = a;
            while (k <= b)
            {
                if (!On(F[k], left)) { k++; continue; }
                int s = k; while (k <= b && On(F[k], left)) k++;
                int e = k - 1;
                if (e - s + 1 < _minFrames || e < lo || s > hi) continue;
                int from = Math.Min(e - 2, s + (e - s + 1 >= 9 ? 2 : 1));
                Vector2 am = Vector2.zero, tm = Vector2.zero; int c = 0;
                for (int q = from; q <= e; q++) { am += XZ(left ? F[q].AL : F[q].AR); tm += XZ(left ? F[q].TL : F[q].TR); c++; }
                if (c < 3) continue;
                am /= c; tm /= c; float da = 0f, dt = 0f;
                for (int q = from; q <= e; q++) { da = Mathf.Max(da, (XZ(left ? F[q].AL : F[q].AR) - am).magnitude); dt = Mathf.Max(dt, (XZ(left ? F[q].TL : F[q].TR) - tm).magnitude); }
                if (Mathf.Min(da, dt) < best) { best = Mathf.Min(da, dt); BestDa = da; BestDt = dt; BestLen = e - s + 1; }
            }
            return best;
        }
        static float BestDa, BestDt; static int BestLen;
        static bool On(Fr f, bool left) { return f.Valid && (left ? f.SoleL : f.SoleR) < _thr && (_geo || (left ? f.FlagL : f.FlagR)); }
        static Vector2 XZ(Vector3 v) { return new Vector2(v.x, v.z); }

        static readonly StringBuilder Trace = new StringBuilder();
        static void Dump(Series s, int m, Ev e, float total)
        {
            var F = s.F; var ci = CultureInfo.InvariantCulture;
            Trace.AppendLine("== play " + e.Play + " act " + e.Act + " mm " + e.MmOwn + " clip " + e.Clip + " " + s.Pos + " turn " + total.ToString("0") + " deg, dur " + e.Dur.ToString("0.00", ci) + " s, speed in " + e.Speed.ToString("0.0", ci) + " out " + e.SpeedAfter.ToString("0.0", ci) + ", drift " + (e.Drift * 100f).ToString("0.0", ci) + " cm");
            Trace.AppendLine("  k   speed  hdg    soleL  soleR  flL flR  ankL(x,z rel mid)   ankR(x,z)    hipsY");
            Vector3 oL = F[m].AL, oR = F[m].AR;
            for (int k = Math.Max(0, m - 21); k <= Math.Min(F.Count - 1, m + 21); k += 3)
                Trace.AppendLine("  " + (k - m).ToString().PadLeft(3) + "  " + F[k].Speed.ToString("0.0", ci).PadLeft(5) + "  " + Hdg(F[k]).ToString("0").PadLeft(5) + "  " + (F[k].SoleL * 100f).ToString("0.0", ci).PadLeft(5) + "  " + (F[k].SoleR * 100f).ToString("0.0", ci).PadLeft(5) + "   " + (F[k].FlagL ? 1 : 0) + "   " + (F[k].FlagR ? 1 : 0) + "   " + ((F[k].AL.x - oL.x) * 100f).ToString("0").PadLeft(4) + "," + ((F[k].AL.z - oL.z) * 100f).ToString("0").PadLeft(4) + "       " + ((F[k].AR.x - oR.x) * 100f).ToString("0").PadLeft(4) + "," + ((F[k].AR.z - oR.z) * 100f).ToString("0").PadLeft(4) + "   " + F[k].HipsY.ToString("0.00", ci));
        }

        static bool FlagRun(List<Fr> F, bool left, int lo, int hi)
        {
            int run = 0;
            for (int k = Math.Max(0, lo - 30); k <= Math.Min(F.Count - 1, hi + 30); k++)
            {
                if (F[k].Valid && (left ? F[k].FlagL : F[k].FlagR)) { run++; if (run >= MinFrames && k >= lo) return true; }
                else { if (run >= MinFrames && k - 1 >= lo) return true; run = 0; }
            }
            return false;
        }

        static void Evaluate(Series s, int m, int i, int j, float total, bool control = false)
        {
            var F = s.F; int n = F.Count;
            int lo = Math.Max(0, m - Half), hi = Math.Min(n - 1, m + Half);
            float dl = BestDrift(F, true, lo, hi); float daL = BestDa, dtL = BestDt; int lenL = BestLen; float dr = BestDrift(F, false, lo, hi); float daR = BestDa, dtR = BestDt; int lenR = BestLen;
            bool outsideLeft = total > 0f;      // a right turn (heading up) is made over the left foot
            float dOut = outsideLeft ? dl : dr;
            var e = new Ev { Pos = s.Pos, Group = s.Carry ? "Carry" : s.Pos == "QB" ? "QB" : "Free", Angle = Mathf.Abs(total), Speed = F[Math.Max(0, i)].Speed, Dur = (j - i) / 60f, Play = _play };
            { bool lbest = dl <= dr; e.Da = lbest ? daL : daR; e.Dt = lbest ? dtL : dtR; e.Len = lbest ? lenL : lenR; }
            _thr = OnTurfGeo; _geo = true; e.DriftLoose = Mathf.Min(BestDrift(F, true, lo, hi), BestDrift(F, false, lo, hi)); _thr = OnTurf; _geo = false;
            _minFrames = 4; e.DriftShort = Mathf.Min(BestDrift(F, true, lo, hi), BestDrift(F, false, lo, hi)); _minFrames = MinFrames;
            e.Drift = Mathf.Min(dl, dr); e.OutDrift = dOut; e.Planted = e.Drift < DriftMax; e.PlantedOutside = control ? e.Planted : dOut < DriftMax; e.Control = control; e.FlagPlanted = FlagRun(F, true, lo, hi) || FlagRun(F, false, lo, hi);
            e.SpeedAfter = F[Math.Min(n - 1, j)].Speed; e.MmOwn = F[m].MmLayer > 0.5f && F[m].RigW < 0.3f; e.Act = F[m].Act.ToString(); e.Clip = F[m].Clip;
            float refSum = 0f; int rc = 0; for (int k = Math.Max(0, m - 42); k <= m - 18; k++) if (F[k].Valid) { refSum += F[k].HipsY; rc++; }
            float cutSum = 0f; int cc = 0; float mn = 9f; for (int k = Math.Max(0, m - 6); k <= Math.Min(n - 1, m + 6); k++) if (F[k].Valid) { cutSum += F[k].HipsY; cc++; }
            for (int k = Math.Max(0, m - 12); k <= Math.Min(n - 1, m + 12); k++) if (F[k].Valid) mn = Mathf.Min(mn, F[k].HipsY);
            if (rc >= 12 && cc >= 8) { float rf = refSum / rc; e.Drop = rf - cutSum / cc; e.DropMin = rf - mn; e.HaveDrop = true; }
            Events.Add(e);
            if (!control && !e.Planted && Trace.Length < 60000) Dump(s, m, e, total);
        }

        static List<Ev> Cuts() { return Events.Where(e => !e.Control).ToList(); }
        static float Q(List<float> l, float p) { return FormProbe.P(l, p); }
        static string F1(float v) { return FormProbe.F(v); }
        static string Pct(int a, int b) { return b == 0 ? "-" : (100f * a / b).ToString("0", CultureInfo.InvariantCulture) + " % (" + a + " / " + b + ")"; }

        public static string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# v0.12.0 GAIT: cut audit (direction changes of the simulated path > 45 deg within 0.6 s, speed > 2 m/s)\n");
            sb.AppendLine("Planted = a foot that FootPlant holds (planted flag) with its sole lower than 2.5 cm for 6 frames or more overlapping +-0.15 s around the middle of the turn, whose planted point (ankle or ball) drifts less than 4 cm. Outside foot = left for a right turn. Hip drop = mean pelvis height 0.7-0.3 s before minus the mean +-0.1 s around the middle of the turn (m).\n");
            Table(sb, "all", Cuts()); Table(sb, "control: straight running at speed (same windows)", Events.Where(e => e.Control).ToList());
            foreach (var g in Cuts().GroupBy(e => e.Group).OrderBy(x => x.Key)) Table(sb, g.Key, g.ToList());
            Table(sb, "legs owned by the motion matcher (layer > 0.5, no action clip > 0.3)", Cuts().Where(e => e.MmOwn).ToList());
            { var mo = Cuts().Where(e => e.MmOwn).ToList(); var nr = mo.Where(e => e.Drift >= 9f).ToList(); var iv = mo.Where(e => e.Drift < 9f && !e.Planted).ToList(); sb.AppendLine("matcher-owned cuts that fail: " + (mo.Count - mo.Count(e => e.Planted)) + " of " + mo.Count + "; no foot on the turf for 6+ frames in the window: " + nr.Count + "; a planted interval but drift >= 4 cm: " + iv.Count + " (median ankle drift " + F1(Q(iv.Select(e => e.Da * 100f).ToList(), 0.5f)) + " cm, ball drift " + F1(Q(iv.Select(e => e.Dt * 100f).ToList(), 0.5f)) + " cm, interval " + F1(Q(iv.Select(e => (float)e.Len).ToList(), 0.5f)) + " frames)"); }
            Table(sb, "legs owned by an action clip or the animator", Cuts().Where(e => !e.MmOwn).ToList());
            sb.AppendLine("\n## by sim action at the middle of the turn\n");
            sb.AppendLine("| action | events | planted foot (drift < 4 cm) | outside foot planted | median drift cm | median hip drop cm | hip drop >= 3 cm | median speed in / out m/s |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var g in Cuts().GroupBy(e => e.Act).OrderBy(x => x.Key)) Row(sb, g.Key, g.ToList());
            sb.AppendLine("\n## by turn angle\n");
            sb.AppendLine("| angle | events | planted foot (drift < 4 cm) | outside foot planted | median drift cm | median hip drop cm | hip drop >= 3 cm | median speed in / out m/s |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var bk in new[] { new[] { 45f, 70f }, new[] { 70f, 110f }, new[] { 110f, 150f }, new[] { 150f, 181f } })
            {
                var l = Cuts().Where(e => e.Angle >= bk[0] && e.Angle < bk[1]).ToList(); Row(sb, bk[0].ToString("0") + "-" + bk[1].ToString("0"), l);
            }
            sb.AppendLine("\n## by position\n");
            sb.AppendLine("| position | events | planted foot (drift < 4 cm) | outside foot planted | median drift cm | median hip drop cm | hip drop >= 3 cm | median speed in / out m/s |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|");
            foreach (var g in Cuts().GroupBy(e => e.Pos).OrderBy(x => x.Key)) Row(sb, g.Key, g.ToList());
            return sb.ToString();
        }

        static void Table(StringBuilder sb, string name, List<Ev> l)
        {
            var dr = l.Where(e => e.Drift < 9f).Select(e => e.Drift * 100f).ToList(); var hd = l.Where(e => e.HaveDrop).ToList();
            sb.AppendLine("**" + name + "**: " + l.Count + " direction changes; planted foot (drift < 4 cm) " + Pct(l.Count(e => e.Planted), l.Count) + "; outside foot planted " + Pct(l.Count(e => e.PlantedOutside), l.Count) + "; (4+ frame plants instead of 6+: " + Pct(l.Count(e => e.DriftShort < DriftMax), l.Count) + ")"
                + "; (geometry only, ignoring the FootPlant flag, sole < 1.5 cm: " + Pct(l.Count(e => e.DriftLoose < DriftMax), l.Count) + "); FootPlant flag held 6+ frames " + Pct(l.Count(e => e.FlagPlanted), l.Count)
                + "; median drift of the best planted point " + F1(Q(dr, 0.5f)) + " cm (p95 " + F1(Q(dr, 0.95f)) + "); hip drop median " + F1(Q(hd.Select(e => e.Drop * 100f).ToList(), 0.5f)) + " cm (p25 "
                + F1(Q(hd.Select(e => e.Drop * 100f).ToList(), 0.25f)) + ", p75 " + F1(Q(hd.Select(e => e.Drop * 100f).ToList(), 0.75f)) + "), deepest sample " + F1(Q(hd.Select(e => e.DropMin * 100f).ToList(), 0.5f)) + " cm\n");
        }

        static void Row(StringBuilder sb, string name, List<Ev> l)
        {
            var dr = l.Where(e => e.Drift < 9f).Select(e => e.Drift * 100f).ToList(); var hd = l.Where(e => e.HaveDrop).ToList();
            sb.AppendLine("| " + name + " | " + l.Count + " | " + Pct(l.Count(e => e.Planted), l.Count) + " | " + Pct(l.Count(e => e.PlantedOutside), l.Count) + " | " + F1(Q(dr, 0.5f)) + " | " + F1(Q(hd.Select(e => e.Drop * 100f).ToList(), 0.5f)) + " | " + Pct(hd.Count(e => e.Drop >= 0.03f), hd.Count)
                + " | " + F1(Q(l.Select(e => e.Speed).ToList(), 0.5f)) + " / " + F1(Q(l.Select(e => e.SpeedAfter).ToList(), 0.5f)) + " |");
        }

        public static void Write(string dir)
        {
            System.IO.File.WriteAllText(dir + "/cut_audit.md", Report());
            var sb = new StringBuilder("play,pos,group,angleDeg,speed,speedAfter,durS,driftCm,outsideDriftCm,planted,plantedOutside,hipDropCm,hipDropMinCm\n");
            var ci = CultureInfo.InvariantCulture;
            foreach (var e in Events)
                sb.Append(e.Play).Append(',').Append(e.Pos).Append(',').Append(e.Group).Append(',').Append(e.Angle.ToString("0")).Append(',').Append(e.Speed.ToString("0.0", ci)).Append(',').Append(e.SpeedAfter.ToString("0.0", ci)).Append(',').Append(e.Dur.ToString("0.00", ci)).Append(',')
                    .Append((e.Drift * 100f).ToString("0.0", ci)).Append(',').Append((e.OutDrift * 100f).ToString("0.0", ci)).Append(',').Append(e.Planted ? 1 : 0).Append(',').Append(e.PlantedOutside ? 1 : 0).Append(',')
                    .Append(e.HaveDrop ? (e.Drop * 100f).ToString("0.0", ci) : "").Append(',').Append(e.HaveDrop ? (e.DropMin * 100f).ToString("0.0", ci) : "").AppendLine();
            System.IO.File.WriteAllText(dir + "/cut_events.csv", sb.ToString());
            System.IO.File.WriteAllText(dir + "/cut_trace_failed.txt", Trace.ToString());
        }
    }
}
