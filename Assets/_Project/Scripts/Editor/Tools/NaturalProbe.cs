using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.12.0 NATURAL: "does anybody move like a robot" as numbers, on the FINAL pose, every frame (fed by EngineAudit.Sample, observation only; report <c>natural.md</c>).
    ///  * gait phase correlation of neighbours of the same group (cycles from the foot separation along the travel direction; mean resultant length R of the phase difference per pair, share of frames in step);
    ///  * style variance across players (stride length at the same speed, step rate, arm swing, shoulder counter-rotation, head wobble) as a coefficient of variation, and the step-to-step tempo variation inside a run;
    ///  * rigid upper body: arm swing rms (deg), counter-rotation rms (deg), head yaw wobble (deg), head bob (cm);
    ///  * standing: angular speed of the chest, head and pelvis of a body that stands (deg/s), share of 2 s windows that are frozen;
    ///  * look distribution: head over chest, chest over pelvis, pelvis over the facing / travel: p50 / p99 / max (deg), rates (deg/s), frames beyond the limits of <see cref="NaturalLook"/>.
    /// </summary>
    public static class NaturalProbe
    {
        sealed class Ser
        {
            public string Grp, Team; public int Id; public int Play, Agent;
            public Vector3 PrevHipsPos; public bool Have;
            public float LastT = -9f;
            // gait cycle
            public int SepSign; public float LastStart = -9f, PrevStart = -9f; public List<float> Periods = new List<float>();
            // run segment series (60 Hz)
            public List<float> ArmL = new List<float>(), ArmR = new List<float>(), Rel = new List<float>(), HeadYaw = new List<float>(), HeadY = new List<float>(), Speed = new List<float>();
            public float RunTime, RunDist;
            // twists for rates
            public float PrevNeck, PrevChest, PrevPelvis; public bool HaveTw;
            // standing
            public List<Quaternion> ChestQ = new List<Quaternion>(), HeadQ = new List<Quaternion>(), HipsQ = new List<Quaternion>();
        }

        static readonly Dictionary<long, Ser> S = new Dictionary<long, Ser>();
        static readonly Dictionary<string, List<float>> V = new Dictionary<string, List<float>>();
        static readonly Dictionary<string, int> Cnt = new Dictionary<string, int>();
        // phase frames: key play*100000 + tIndex -> agent states
        sealed class PF { public float[] Phase = new float[22]; public Vector3[] Pos = new Vector3[22]; public string[] Grp = new string[22]; public string[] Team = new string[22]; public PF() { for (int i = 0; i < 22; i++) Phase[i] = float.NaN; } }
        static readonly Dictionary<long, PF> Frames = new Dictionary<long, PF>();
        sealed class Pair { public double C, Sn; public int N, InStep; }
        static readonly Dictionary<string, Pair> Pairs = new Dictionary<string, Pair>();
        static readonly Dictionary<string, Dictionary<int, List<float>>> Players = new Dictionary<string, Dictionary<int, List<float>>>();
        const float Dt = 1f / 60f;

        public static void Reset() { S.Clear(); V.Clear(); Cnt.Clear(); Frames.Clear(); Pairs.Clear(); Players.Clear(); }
        static void Add(string k, float v) { List<float> l; if (!V.TryGetValue(k, out l)) V[k] = l = new List<float>(); l.Add(v); }
        static void Inc(string k) { int n; Cnt.TryGetValue(k, out n); Cnt[k] = n + 1; }
        static void PlayerAdd(string metric, int id, float v) { Dictionary<int, List<float>> d; if (!Players.TryGetValue(metric, out d)) Players[metric] = d = new Dictionary<int, List<float>>(); List<float> l; if (!d.TryGetValue(id, out l)) d[id] = l = new List<float>(); l.Add(v); }

        static float Pct(List<float> l, float p) { if (l == null || l.Count == 0) return float.NaN; var a = l.OrderBy(x => x).ToList(); return a[Mathf.Clamp((int)(p * (a.Count - 1) + 0.5f), 0, a.Count - 1)]; }
        static string F(float v, string f = "0.0") { return float.IsNaN(v) ? "-" : v.ToString(f, CultureInfo.InvariantCulture); }

        /// <summary>One body, one frame. <paramref name="valid"/> = not a contact take, not down, not after the whistle's fall.</summary>
        public static void Feed(int play, int agent, SimAgentView v, string grp, string team, string action, bool carry, float speedMs, Vector3 velUnity, Vector3 simFacing, float t, bool valid, int frameIndex)
        {
            if (v == null || v.Nat == null || !v.Nat.Ok || v.Avatar == null || v.Avatar.Animator == null) return;
            long key = (long)play * 100 + agent;
            Ser s; if (!S.TryGetValue(key, out s)) S[key] = s = new Ser { Grp = grp, Team = team, Id = v.IdentityId > 0 ? v.IdentityId : 90000 + agent, Play = play, Agent = agent };
            var an = v.Avatar.Animator;
            var nat = v.Nat;
            float gy = v.transform.position.y;
            if (!valid) { s.Have = false; s.HaveTw = false; FlushStanding(s); return; }
            bool loco = action == "Run" || action == "Sprint" || action == "Route" || action == "Carry";
            bool bp = action == "Backpedal" || action == "Shuffle";
            bool run = loco && speedMs > 3.0f;
            bool stand = action == "Idle" && speedMs < 0.3f;
            string cat = run ? "run" : bp && speedMs > 1f ? "bp" : stand ? "stand" : speedMs > 0.6f ? "walk" : "other";
            Vector3 td = velUnity; td.y = 0f; if (td.sqrMagnitude > 1e-4f) td.Normalize(); else td = v.Forward;
            // ---- twists
            float neck = nat.HeadOverChest, chest = nat.ChestOverPelvis, pelvis = nat.PelvisYaw;
            float refYaw = run ? Mathf.Atan2(td.x, td.z) * Mathf.Rad2Deg : Mathf.Atan2(simFacing.x, simFacing.z) * Mathf.Rad2Deg;
            float pel = Mathf.DeltaAngle(refYaw, pelvis);
            Add(cat + " |head over chest| deg", Mathf.Abs(neck)); Add(cat + " |chest over pelvis| deg", Mathf.Abs(chest)); Add(cat + " |pelvis over " + (run ? "travel" : "facing") + "| deg", Mathf.Abs(pel));
            Add("all |head over chest| deg", Mathf.Abs(neck)); Add("all |chest over pelvis| deg", Mathf.Abs(chest));
            if (Mathf.Abs(neck) > NaturalLook.NeckMax) { Inc("frames head over chest > " + NaturalLook.NeckMax.ToString("0") + " (" + cat + ")"); Inc("  head>70 by action " + action); }
            if (Mathf.Abs(chest) > NaturalLook.ChestMax) { Inc("frames chest over pelvis > " + NaturalLook.ChestMax.ToString("0") + " (" + cat + ")"); Inc("  chest>35 by action " + action); }
            Inc("  frames by action " + action);
            if (nat.DbgLookActive) { Inc("look layer active frames (neck" + (nat.DbgChestOn ? "+chest" : " only") + ")"); if (Mathf.Abs(nat.DbgLookChest) > 0.02f || Mathf.Abs(nat.DbgLookNeck) > 0.02f) Inc("look layer corrected frames"); if (Mathf.Abs(neck) > NaturalLook.NeckMax) Inc("  head>70 while look layer active, by action " + action); if (nat.DbgChestOn && Mathf.Abs(chest) > NaturalLook.ChestMax) Inc("  chest>35 while the chest limit is on, by action " + action); if (nat.DbgChestOn) Inc("chest-limit frames"); }
            if (s.HaveTw && Mathf.Abs(Mathf.DeltaAngle(s.PrevPelvis, pelvis)) / Dt > 900f) Inc("  pelvis>900 by action " + action);
            if (Mathf.Abs(pel) > 45f) Inc("frames pelvis over " + (run ? "travel" : "facing") + " > 45 (" + cat + ")");
            Inc("frames (" + cat + ")"); Inc("frames (all)");
            if (s.HaveTw)
            {
                float rn = Mathf.Abs(Mathf.DeltaAngle(s.PrevNeck, neck)) / Dt, rc = Mathf.Abs(Mathf.DeltaAngle(s.PrevChest, chest)) / Dt, rp = Mathf.Abs(Mathf.DeltaAngle(s.PrevPelvis, pelvis)) / Dt;
                Add(cat + " head-over-chest rate deg/s", rn); Add(cat + " chest-over-pelvis rate deg/s", rc); Add(cat + " pelvis rate deg/s", rp);
                Add("all head-over-chest rate deg/s", rn); Add("all chest-over-pelvis rate deg/s", rc); Add("all pelvis rate deg/s", rp);
                if (rp > 900f) Inc("pelvis rate > 900 deg/s (" + grp + ")");
                if (rn > NaturalLook.NeckRate) Inc("head-over-chest rate > " + NaturalLook.NeckRate.ToString("0") + " (" + cat + ")");
                if (rc > NaturalLook.ChestRate) Inc("chest-over-pelvis rate > " + NaturalLook.ChestRate.ToString("0") + " (" + cat + ")");
            }
            s.PrevNeck = neck; s.PrevChest = chest; s.PrevPelvis = pelvis; s.HaveTw = true;
            // ---- standing micro-motion
            if (Mathf.Abs(nat.DbgBreath) > 0.005f || Mathf.Abs(nat.DbgSway) > 0.005f) Inc("idle layer active frames (" + cat + ")");
            if (stand)
            {
                s.ChestQ.Add(an.GetBoneTransform(HumanBodyBones.Chest) != null ? an.GetBoneTransform(HumanBodyBones.Chest).rotation : Quaternion.identity);
                s.HeadQ.Add(an.GetBoneTransform(HumanBodyBones.Head).rotation); s.HipsQ.Add(an.GetBoneTransform(HumanBodyBones.Hips).rotation);
            }
            else FlushStanding(s);
            if (!run) { FlushRun(s, false); return; }
            // ---- running: arm swing, counter-rotation, head, foot separation
            Transform lf = an.GetBoneTransform(HumanBodyBones.LeftFoot), rf = an.GetBoneTransform(HumanBodyBones.RightFoot);
            Transform lua = an.GetBoneTransform(HumanBodyBones.LeftUpperArm), rua = an.GetBoneTransform(HumanBodyBones.RightUpperArm), lla = an.GetBoneTransform(HumanBodyBones.LeftLowerArm), rla = an.GetBoneTransform(HumanBodyBones.RightLowerArm);
            if (lf == null || rf == null || lua == null || rua == null || lla == null || rla == null) return;
            if (t - s.LastT > 0.1f && s.LastT > -5f) FlushRun(s, false);
            s.LastT = t;
            float sep = Vector3.Dot(lf.position - rf.position, td);
            int sg = sep > 0.04f ? 1 : sep < -0.04f ? -1 : s.SepSign;
            if (sg != s.SepSign)
            {
                if (s.SepSign == -1 && sg == 1) { s.PrevStart = s.LastStart; s.LastStart = t; if (s.PrevStart > 0f) { float per = s.LastStart - s.PrevStart; if (per > 0.2f && per < 1.0f) s.Periods.Add(per); } }
                s.SepSign = sg;
            }
            Vector3 right = Vector3.Cross(Vector3.up, td);
            Vector3 vl = lla.position - lua.position, vr = rla.position - rua.position;
            s.ArmL.Add(Mathf.Atan2(Vector3.Dot(vl, td), -vl.y) * Mathf.Rad2Deg); s.ArmR.Add(Mathf.Atan2(Vector3.Dot(vr, td), -vr.y) * Mathf.Rad2Deg);
            s.Rel.Add(chest); s.HeadYaw.Add(nat.HeadYaw); s.HeadY.Add(an.GetBoneTransform(HumanBodyBones.Head).position.y - gy); s.Speed.Add(speedMs);
            s.RunTime += Dt; s.RunDist += speedMs * Dt;
            // phase for the pair statistics
            float ph = float.NaN;
            if (s.LastStart > 0f && s.PrevStart > 0f) { float per = s.LastStart - s.PrevStart; if (per > 0.2f && per < 1.0f && t - s.LastStart < per * 1.5f) ph = Mathf.Clamp01((t - s.LastStart) / per) % 1f; }
            long fk = (long)play * 100000 + frameIndex;
            PF pf; if (!Frames.TryGetValue(fk, out pf)) Frames[fk] = pf = new PF();
            pf.Phase[agent] = ph; pf.Pos[agent] = v.transform.position; pf.Grp[agent] = grp; pf.Team[agent] = team;
        }

        static void FlushStanding(Ser s)
        {
            int n = s.ChestQ.Count;
            if (n >= 120)
            {
                float sc = 0f, sh = 0f, sp = 0f; float maxC = 0f, maxH = 0f;
                for (int i = 1; i < n; i++) { sc += Quaternion.Angle(s.ChestQ[i], s.ChestQ[i - 1]); sh += Quaternion.Angle(s.HeadQ[i], s.HeadQ[i - 1]); sp += Quaternion.Angle(s.HipsQ[i], s.HipsQ[i - 1]); }
                for (int i = 0; i < n; i += 4) { maxC = Mathf.Max(maxC, Quaternion.Angle(s.ChestQ[i], s.ChestQ[0])); maxH = Mathf.Max(maxH, Quaternion.Angle(s.HeadQ[i], s.HeadQ[0])); }
                float secs = (n - 1) * Dt;
                Add("stand chest deg/s", sc / secs); Add("stand head deg/s", sh / secs); Add("stand pelvis deg/s", sp / secs);
                Add("stand chest range deg", maxC); Add("stand head range deg", maxH);
                Inc("stand windows"); if (maxC < 0.7f && maxH < 1.2f) Inc("stand windows frozen (chest < 0.7, head < 1.2 deg range)");
                PlayerAdd("stand chest deg/s", s.Id, sc / secs);
            }
            s.ChestQ.Clear(); s.HeadQ.Clear(); s.HipsQ.Clear();
        }

        static float Rms(List<float> l, float tau)
        {
            if (l.Count < 30) return float.NaN;
            float mean = l[0], k = 1f - Mathf.Exp(-Dt / tau); double acc = 0;
            for (int i = 0; i < l.Count; i++) { mean += (l[i] - mean) * k; double d = l[i] - mean; acc += d * d; }
            return (float)Math.Sqrt(acc / l.Count);
        }
        static float RmsAngle(List<float> l, float tau)
        {
            if (l.Count < 30) return float.NaN;
            float mean = l[0], k = 1f - Mathf.Exp(-Dt / tau); double acc = 0;
            for (int i = 0; i < l.Count; i++) { mean += Mathf.DeltaAngle(mean, l[i]) * k; double d = Mathf.DeltaAngle(mean, l[i]); acc += d * d; }
            return (float)Math.Sqrt(acc / l.Count);
        }

        static void FlushRun(Ser s, bool end)
        {
            if (s.RunTime >= 1.2f)
            {
                float vmean = s.RunDist / s.RunTime;
                var sp = new List<float>(s.Speed); float spread = Pct(sp, 0.9f) - Pct(sp, 0.1f);
                bool steady = spread < 1.2f;
                float armL = Rms(s.ArmL, 0.35f), armR = Rms(s.ArmR, 0.35f), arm = float.IsNaN(armL) || float.IsNaN(armR) ? float.NaN : 0.5f * (armL + armR);
                float ctr = Rms(s.Rel, 0.45f), hy = RmsAngle(s.HeadYaw, 0.45f), hb = Rms(s.HeadY, 0.5f) * 100f;
                string band = vmean >= 7f ? "v7+" : vmean >= 5.5f ? "v5.5-7" : "v3.5-5.5";
                if (!float.IsNaN(arm)) { Add("run arm swing rms deg (" + band + ")", arm); PlayerAdd("arm " + band, s.Id, arm); }
                if (!float.IsNaN(ctr)) { Add("run shoulder counter-rotation rms deg (" + band + ")", ctr); PlayerAdd("counter " + band, s.Id, ctr); }
                if (!float.IsNaN(hy)) { Add("run head yaw wobble rms deg (" + band + ")", hy); PlayerAdd("headyaw " + band, s.Id, hy); }
                if (!float.IsNaN(hb)) Add("run head bob rms cm (" + band + ")", hb);
                if (spread < 2.2f && s.Periods.Count >= 2)
                {
                    float mp = s.Periods.Average();
                    float stepRate = 2f / mp;
                    Add("run step rate steps/s (" + band + ")", stepRate); PlayerAdd("steprate " + band, s.Id, stepRate);
                    float stride = vmean / stepRate; Add("run stride length m (" + band + ")", stride); PlayerAdd("stride " + band, s.Id, stride);
                    double var = 0; foreach (var p in s.Periods) var += (p - mp) * (p - mp); float cv = (float)Math.Sqrt(var / s.Periods.Count) / mp * 100f;
                    Add("run cycle period CV % inside a steady run", cv);
                }
            }
            s.ArmL.Clear(); s.ArmR.Clear(); s.Rel.Clear(); s.HeadYaw.Clear(); s.HeadY.Clear(); s.Speed.Clear(); s.RunTime = 0f; s.RunDist = 0f; s.Periods.Clear(); s.SepSign = 0; s.LastStart = s.PrevStart = -9f;
        }

        static string Cls(string g) { return g == "WR" || g == "TE" || g == "RB" ? "skill" : g == "CB" || g == "S" ? "DB" : g; }

        public static void EndPlay()
        {
            foreach (var s in S.Values) { FlushRun(s, true); FlushStanding(s); }
            S.Clear();
            foreach (var kv in Frames)
            {
                var f = kv.Value;
                for (int i = 0; i < 22; i++)
                {
                    if (float.IsNaN(f.Phase[i])) continue;
                    for (int j = i + 1; j < 22; j++)
                    {
                        if (float.IsNaN(f.Phase[j]) || f.Team[i] != f.Team[j] || Cls(f.Grp[i]) != Cls(f.Grp[j])) continue;
                        Vector3 d = f.Pos[i] - f.Pos[j]; d.y = 0f; if (d.magnitude > 20f) continue;
                        string pk = (kv.Key / 100000) + ":" + i + ":" + j + ":" + Cls(f.Grp[i]);
                        Pair p; if (!Pairs.TryGetValue(pk, out p)) Pairs[pk] = p = new Pair();
                        float dp = (f.Phase[i] - f.Phase[j]) * 2f * Mathf.PI;
                        p.C += Mathf.Cos(dp); p.Sn += Mathf.Sin(dp); p.N++;
                        float fr = Mathf.Abs(Mathf.DeltaAngle(0f, dp * Mathf.Rad2Deg)) / 360f;
                        if (fr < 0.10f) p.InStep++;
                    }
                }
            }
            Frames.Clear();
        }

        static float Cv(Dictionary<int, List<float>> d, int minSamples = 1)
        {
            var means = d.Where(k => k.Value.Count >= minSamples).Select(k => k.Value.Average()).ToList();
            if (means.Count < 4) return float.NaN;
            float m = means.Average(); double v = 0; foreach (var x in means) v += (x - m) * (x - m);
            return (float)Math.Sqrt(v / means.Count) / Mathf.Max(1e-4f, Mathf.Abs(m)) * 100f;
        }

        public static string Report()
        {
            var sb = new StringBuilder();
            sb.AppendLine("## NaturalProbe");
            // phase correlation
            var good = Pairs.Values.Where(p => p.N >= 60).ToList();
            sb.AppendLine("### Gait phase correlation between same-group neighbours (< 20 m, both running > 3 m/s, >= 1 s together)");
            if (good.Count > 0)
            {
                var rs = good.Select(p => (float)(Math.Sqrt(p.C * p.C + p.Sn * p.Sn) / p.N)).ToList();
                long frames = good.Sum(p => (long)p.N), inStep = good.Sum(p => (long)p.InStep);
                sb.AppendLine("pairs " + good.Count + ", pair-frames " + frames + "; R (1 = locked, 0 = free) mean " + F(rs.Average(), "0.000") + ", median " + F(Pct(rs, 0.5f), "0.000") + ", p90 " + F(Pct(rs, 0.9f), "0.000") + "; pairs with R > 0.5: " + F(100f * rs.Count(r => r > 0.5f) / rs.Count, "0.0") + " %; frames within 0.10 cycle of each other (chance 20 %): " + F(100f * inStep / Math.Max(1, frames), "0.0") + " %");
            }
            else sb.AppendLine("no pairs");
            sb.AppendLine();
            sb.AppendLine("### Distributions (per frame or per run)");
            sb.AppendLine("| metric | n | p10 | p50 | p90 | p99 | max |");
            sb.AppendLine("|---|---|---|---|---|---|---|");
            foreach (var kv in V.OrderBy(k => k.Key))
            {
                var l = kv.Value; if (l.Count < 5) continue;
                sb.AppendLine("| " + kv.Key + " | " + l.Count + " | " + F(Pct(l, 0.1f), "0.00") + " | " + F(Pct(l, 0.5f), "0.00") + " | " + F(Pct(l, 0.9f), "0.00") + " | " + F(Pct(l, 0.99f), "0.00") + " | " + F(l.Max(), "0.00") + " |");
            }
            sb.AppendLine();
            sb.AppendLine("### Counts");
            foreach (var kv in Cnt.OrderBy(k => k.Key)) sb.AppendLine("- " + kv.Key + ": " + kv.Value);
            sb.AppendLine();
            sb.AppendLine("### Style variance across players (coefficient of variation of the per-player means, %; a player = one identity id, CV needs >= 4 of them)");
            sb.AppendLine("| metric | players | mean | CV % |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var kv in Players.OrderBy(k => k.Key))
            {
                var means = kv.Value.Where(k => k.Value.Count >= 1).Select(k => k.Value.Average()).ToList();
                if (means.Count < 1) continue;
                sb.AppendLine("| " + kv.Key + " | " + means.Count + " | " + F(means.Average(), "0.00") + " | " + F(Cv(kv.Value), "0.0") + " |");
            }
            return sb.ToString();
        }
    }
}
