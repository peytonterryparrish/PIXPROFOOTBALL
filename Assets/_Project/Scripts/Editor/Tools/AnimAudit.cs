using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// Numeric audit of every clip registered in the ArtLibrary (and optionally the Clean/ outputs): foot sliding, ground penetration, jitter, pops, loop seams,
    /// T-pose frames, hyperextension, spine twist, head snaps, dead time, root drift and facing. Output: Logs/anim_audit/audit.csv (+ audit_clean.csv).
    /// Batch entry: PixFootball.EditorTools.AnimAudit.RunAll  (Logs/anim_audit_in.txt may hold a list of clip names, one per line, to restrict the run).
    /// </summary>
    public static class AnimAudit
    {
        public const float Rate = 30f;
        public const float MaxSeconds = 24f;
        public const string OutDir = "Logs/anim_audit";

        public class Row
        {
            public string name, keys, source;
            public float length, travel, speed, yaw0, yawEnd, travelAngle;
            public float hipMin, hipMax, footMinY, penPct, floatPct, slide, slidePct;
            public float jitter, popMax, popFrames, headSnap, twistMax, twistAvg;
            public int tposeStart, tposeEnd, tposeAll;
            public float kneeHyper, kneeHyperPct, muscleOver, muscleMax;
            public float deadStart, deadEnd, seamPose, seamRoot, idleDistStart, idleDistEnd, handsBelow, stanceSpeed, groundP10, hipMed;
            public bool loop, paired;
        }

        public static readonly string[] ExtraFolders = { "Assets/_Licensed/DraftFever/Animations/BoNES_Yan", "Assets/_Licensed/DraftFever/Animations/PIX_Authored", "Assets/_Licensed/DraftFever/Animations/Motus_Extra" };
        static readonly Dictionary<string, string> extraSrc = new Dictionary<string, string>();
        static readonly string[] LoopWordsPlay = { "idle", "walk", "jog", "run", "sprint", "backpedal", "strafe", "shuffle", "loop", "cycle", "cadence" };

        [MenuItem("PIX Football/Animation/Audit All Clips")]
        public static void RunAll()
        {
            BatchWatchdog.Arm(30);
            Directory.CreateDirectory(OutDir);
            var lib = ArtLibrary.Load();
            if (lib == null) { Debug.LogError("[AnimAudit] no library"); return; }
            var byName = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            var clipOf = new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            foreach (var e in lib.clips)
            {
                if (e.clip == null) continue;
                List<string> l; if (!byName.TryGetValue(e.clip.name, out l)) byName[e.clip.name] = l = new List<string>();
                if (!l.Contains(e.key)) l.Add(e.key);
                clipOf[e.clip.name] = e.clip;
            }
            foreach (string extra in ExtraFolders)
                foreach (string g in AssetDatabase.FindAssets("t:Model", new[] { extra }))
                    foreach (var c in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)).OfType<AnimationClip>())
                    {
                        if (c.name.StartsWith("__preview")) continue;
                        if (!byName.ContainsKey(c.name)) { byName[c.name] = new List<string> { "(unreg)" }; clipOf[c.name] = c; extraSrc[c.name] = AssetDatabase.GUIDToAssetPath(g); }
                    }
            HashSet<string> only = null;
            if (File.Exists("Logs/anim_audit_in.txt")) only = new HashSet<string>(File.ReadAllLines("Logs/anim_audit_in.txt").Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            string outName = only != null ? "audit_subset.csv" : "audit.csv";

            var rig = AnimRig.CreateMedium();
            if (rig == null) return;
            var rows = new List<Row>();
            var idlePose = lib.Clip(ArtKeys.Idle) != null ? rig.PoseAt(lib.Clip(ArtKeys.Idle), 0.5f) : default(AnimRig.Frame);
            int n = 0, total = byName.Count;
            foreach (var kv in byName.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (only != null && !only.Contains(kv.Key)) continue;
                n++;
                try
                {
                    var r = Analyse(rig, clipOf[kv.Key], idlePose);
                    r.keys = string.Join("+", kv.Value);
                    string src; var ce = lib.clips.FirstOrDefault(e => e.clip != null && e.clip.name == kv.Key); src = ce != null ? ce.source : (extraSrc.ContainsKey(kv.Key) ? extraSrc[kv.Key] : "");
                    r.source = src;
                    rows.Add(r);
                }
                catch (Exception e) { Debug.LogWarning("[AnimAudit] " + kv.Key + ": " + e.Message); }
                if (n % 50 == 0) Debug.Log("[AnimAudit] " + n + "/" + total);
            }
            rig.Dispose();
            WriteCsv(Path.Combine(OutDir, outName), rows);
            Debug.Log("[AnimAudit] done rows=" + rows.Count + " -> " + Path.Combine(OutDir, outName));
        }

        /// <summary>Audits clips of a folder (e.g. the Clean/ output) and writes audit_<tag>.csv.</summary>
        public static void RunFolder(string folder, string tag)
        {
            Directory.CreateDirectory(OutDir);
            var rig = AnimRig.CreateMedium(); if (rig == null) return;
            var lib = ArtLibrary.Load();
            var idlePose = rig.PoseAt(lib.Clip(ArtKeys.Idle), 0.5f);
            var rows = new List<Row>();
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { folder }))
            {
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GUIDToAssetPath(g));
                if (c == null) continue;
                var r = Analyse(rig, c, idlePose); r.keys = "clean"; r.source = AssetDatabase.GUIDToAssetPath(g);
                rows.Add(r);
            }
            rig.Dispose();
            WriteCsv(Path.Combine(OutDir, "audit_" + tag + ".csv"), rows);
        }

        public static Row Analyse(AnimRig rig, AnimationClip clip, AnimRig.Frame idle)
        {
            var r = new Row { name = clip.name, length = clip.length };
            string ln = clip.name.ToLowerInvariant();
            r.loop = clip.isLooping;
            r.paired = ln.EndsWith("_a") || ln.EndsWith("_b");
            float dur = Mathf.Min(clip.length, MaxSeconds);
            var fr = rig.Sample(clip, 0f, dur, Rate);
            int N = fr.Count;
            if (N < 3) return r;
            float dt = 1f / Rate;

            var fwd0 = AnimRig.Forward(fr[0]); var fwdN = AnimRig.Forward(fr[N - 1]);
            r.yaw0 = AnimRig.YawOf(fwd0); r.yawEnd = AnimRig.YawOf(fwdN);
            Vector3 d = fr[N - 1].P[AnimRig.Hips] - fr[0].P[AnimRig.Hips]; d.y = 0f;
            r.travel = d.magnitude; r.speed = r.travel / Mathf.Max(0.01f, dur);
            r.travelAngle = r.travel > 0.3f ? Mathf.DeltaAngle(AnimRig.YawOf(d), r.yaw0) : 0f;
            r.hipMin = 9f; r.hipMax = -9f; r.footMinY = 9f;
            float tw = 0f; int twN = 0;
            int pen = 0, flt = 0, planted = 0, slideFrames = 0; float slideSum = 0f;
            float rel = 0f; int relN = 0;
            var lows = new List<float>(); var hips = new List<float>();
            int kh = 0; float khMax = 0f;
            var energy = new float[N];
            var sumJerk = 0f; int jerkN = 0; int pops = 0; float popMax = 0f; float headMax = 0f;
            int[] ids = { AnimRig.Spine, AnimRig.Chest, AnimRig.Head, AnimRig.LUA, AnimRig.LLA, AnimRig.LH, AnimRig.RUA, AnimRig.RLA, AnimRig.RH, AnimRig.LUL, AnimRig.LLL, AnimRig.LF, AnimRig.RUL, AnimRig.RLL, AnimRig.RF };
            int handsBelowN = 0;
            for (int i = 0; i < N; i++)
            {
                var f = fr[i];
                float hy = f.P[AnimRig.Hips].y;
                r.hipMin = Mathf.Min(r.hipMin, hy); r.hipMax = Mathf.Max(r.hipMax, hy);
                float lf = Mathf.Min(f.P[AnimRig.LF].y, f.P[AnimRig.LT].y), rf = Mathf.Min(f.P[AnimRig.RF].y, f.P[AnimRig.RT].y);
                float mf = Mathf.Min(lf, rf);
                r.footMinY = Mathf.Min(r.footMinY, mf);
                lows.Add(mf); hips.Add(hy);
                if (mf < -0.035f) pen++;
                if (Mathf.Min(f.P[AnimRig.LF].y, f.P[AnimRig.RF].y) > 0.16f && hy > 0.6f) flt++;
                float tws = Mathf.Abs(Mathf.DeltaAngle(AnimRig.YawOf(AnimRig.Forward(f)), AnimRig.YawOf(AnimRig.ShoulderForward(f))));
                tw += tws; twN++; r.twistMax = Mathf.Max(r.twistMax, tws);
                float kl = AnimRig.KneeFlex(f, true), kr = AnimRig.KneeFlex(f, false);
                float kmin = Mathf.Min(kl, kr);
                if (kmin < -4f) { kh++; khMax = Mathf.Max(khMax, -kmin); }
                // T-pose
                if (IsTPose(f)) { if (f.t < 1.2f) r.tposeStart++; if (f.t > dur - 1.2f) r.tposeEnd++; r.tposeAll++; }
                if (f.P[AnimRig.LH].y < f.P[AnimRig.Hips].y - 0.7f && f.P[AnimRig.RH].y < f.P[AnimRig.Hips].y - 0.7f) handsBelowN++;
                if (i > 0)
                {
                    float e = 0f;
                    for (int b = 0; b < ids.Length; b++) { float a = Quaternion.Angle(fr[i - 1].R[ids[b]], f.R[ids[b]]); e += a; if (a > popMax && ids[b] != AnimRig.Spine) { } }
                    energy[i] = e;
                    for (int b = 0; b < ids.Length; b++)
                    {
                        float a = Quaternion.Angle(fr[i - 1].R[ids[b]], f.R[ids[b]]);
                        if (a > 45f) pops++;
                        popMax = Mathf.Max(popMax, a);
                    }
                    float hs = Quaternion.Angle(fr[i - 1].R[AnimRig.Head], f.R[AnimRig.Head]) / dt;
                    headMax = Mathf.Max(headMax, hs);
                    // sliding: lowest foot on the ground, horizontal speed
                    bool leftLow = f.P[AnimRig.LF].y < f.P[AnimRig.RF].y;
                    int fi = leftLow ? AnimRig.LF : AnimRig.RF;
                    float fy = f.P[fi].y;
                    if (fy < 0.13f && r.speed > 0.6f)
                    {
                        Vector3 v = (f.P[fi] - fr[i - 1].P[fi]) / dt; v.y = 0f;
                        Vector3 vh = (f.P[AnimRig.Hips] - fr[i - 1].P[AnimRig.Hips]) / dt; vh.y = 0f;
                        float rv = -Vector3.Dot(v - vh, AnimRig.Forward(f));
                        if (rv > 0.2f) { rel += rv; relN++; }     // only the backward-moving (stance) part of the planted frames: the swing foot passing low must not cancel it
                        planted++;
                        slideSum += v.magnitude;
                        if (v.magnitude > 0.5f) slideFrames++;
                    }
                }
                if (i > 1 && i < N - 1)
                {
                    // jerk: third difference of rotation (angle between successive delta rotations)
                    for (int b = 0; b < ids.Length; b++)
                    {
                        Quaternion d1 = Quaternion.Inverse(fr[i - 1].R[ids[b]]) * f.R[ids[b]];
                        Quaternion d0 = Quaternion.Inverse(fr[i - 2].R[ids[b]]) * fr[i - 1].R[ids[b]];
                        float acc = Quaternion.Angle(d0, d1);
                        sumJerk += acc; jerkN++;
                    }
                }
            }
            r.stanceSpeed = relN > 0 ? rel / relN : 0f;
            lows.Sort(); hips.Sort(); r.groundP10 = lows[(int)(lows.Count * 0.10f)]; r.hipMed = hips[hips.Count / 2];
            r.penPct = 100f * pen / N; r.floatPct = 100f * flt / N;
            r.twistAvg = twN > 0 ? tw / twN : 0f;
            r.kneeHyper = khMax; r.kneeHyperPct = 100f * kh / N;
            r.slide = planted > 0 ? slideSum / planted : 0f; r.slidePct = planted > 0 ? 100f * slideFrames / planted : 0f;
            r.jitter = jerkN > 0 ? sumJerk / jerkN : 0f;   // mean degrees of change between successive per-frame rotations (smooth motion is small)
            r.popMax = popMax; r.popFrames = pops; r.headSnap = headMax;
            r.handsBelow = 100f * handsBelowN / N;
            // dead time: energy below 12% of the running average of the busiest window
            float peak = 0f; for (int i = 1; i < N; i++) peak = Mathf.Max(peak, energy[i]);
            float thr = Mathf.Max(0.25f, peak * 0.10f);
            int s0 = 1; while (s0 < N && energy[s0] < thr) s0++;
            int e0 = N - 1; while (e0 > 0 && energy[e0] < thr) e0--;
            r.deadStart = s0 >= N - 1 ? 0f : s0 * dt; r.deadEnd = e0 <= 0 ? 0f : (N - 1 - e0) * dt;
            // seam
            r.seamPose = PoseDist(fr[0], fr[N - 1]);
            Vector3 v0 = (fr[1].P[AnimRig.Hips] - fr[0].P[AnimRig.Hips]) / dt, v1 = (fr[N - 1].P[AnimRig.Hips] - fr[N - 2].P[AnimRig.Hips]) / dt; v0.y = v1.y = 0f;
            r.seamRoot = (v0 - v1).magnitude;
            // distance to the neutral idle pose (hips-local rotations)
            if (idle.R != null) { r.idleDistStart = PoseDist(fr[0], idle); r.idleDistEnd = PoseDist(fr[N - 1], idle); }
            r.muscleOver = 0f; r.muscleMax = 0f;
            MuscleOver(clip, ref r.muscleOver, ref r.muscleMax);
            return r;
        }

        static string[] _muscleNames;
        static void MuscleOver(AnimationClip clip, ref float overPct, ref float maxAbs)
        {
            if (_muscleNames == null) _muscleNames = HumanTrait.MuscleName;
            var set = new HashSet<string>(_muscleNames);
            int keys = 0, over = 0;
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                if (b.type != typeof(Animator) || !set.Contains(b.propertyName)) continue;
                if (b.propertyName.Contains("Finger") || b.propertyName.Contains("Thumb") || b.propertyName.Contains("Index") || b.propertyName.Contains("Middle") || b.propertyName.Contains("Ring") || b.propertyName.Contains("Little")) continue;
                var c = AnimationUtility.GetEditorCurve(clip, b);
                if (c == null) continue;
                foreach (var k in c.keys) { keys++; float a = Mathf.Abs(k.value); if (a > 1.02f) over++; maxAbs = Mathf.Max(maxAbs, a); }
            }
            overPct = keys > 0 ? 100f * over / keys : 0f;
        }

        static float PoseDist(AnimRig.Frame a, AnimRig.Frame b)
        {
            int[] ids = { AnimRig.Spine, AnimRig.Chest, AnimRig.Head, AnimRig.LUA, AnimRig.LLA, AnimRig.RUA, AnimRig.RLA, AnimRig.LUL, AnimRig.LLL, AnimRig.RUL, AnimRig.RLL };
            Quaternion ha = Quaternion.Inverse(a.R[AnimRig.Hips]), hb = Quaternion.Inverse(b.R[AnimRig.Hips]);
            float s = 0f;
            foreach (int i in ids) s += Quaternion.Angle(ha * a.R[i], hb * b.R[i]);
            return s / ids.Length;
        }

        public static bool IsTPose(AnimRig.Frame f)
        {
            Quaternion inv = Quaternion.Inverse(f.R[AnimRig.Hips]);
            Vector3 l = (f.P[AnimRig.LLA] - f.P[AnimRig.LUA]).normalized, rr = (f.P[AnimRig.RLA] - f.P[AnimRig.RUA]).normalized;
            Vector3 lh = (f.P[AnimRig.LH] - f.P[AnimRig.LLA]).normalized, rh = (f.P[AnimRig.RH] - f.P[AnimRig.RLA]).normalized;
            // arms horizontal (near the world horizontal plane) pointing sideways from the body, elbows nearly straight
            bool la = Mathf.Abs(l.y) < 0.22f && Mathf.Abs(rr.y) < 0.22f;
            bool lat = Vector3.Dot(l, -rr) > 0.85f;     // opposite directions
            bool straight = Vector3.Angle(l, lh) < 14f && Vector3.Angle(rr, rh) < 14f;
            return la && lat && straight;
        }

        static void WriteCsv(string path, List<Row> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("name,keys,source,length,loop,travel,speed,yaw0,yawEnd,travelAngle,hipMin,hipMax,footMinY,penPct,floatPct,slide,slidePct,jitter,popMax,popFrames,headSnap,twistMax,twistAvg,tposeStart,tposeEnd,tposeAll,kneeHyper,kneeHyperPct,muscleOverPct,muscleMax,deadStart,deadEnd,seamPose,seamRoot,idleDistStart,idleDistEnd,handsBelowPct,stanceSpeed,groundP10,hipMed");
            foreach (var r in rows)
            {
                sb.AppendLine(string.Join(",", new[]
                {
                    Q(r.name), Q(r.keys), Q(r.source), F(r.length), r.loop ? "1" : "0", F(r.travel), F(r.speed), F(r.yaw0), F(r.yawEnd), F(r.travelAngle), F(r.hipMin), F(r.hipMax), F(r.footMinY),
                    F(r.penPct), F(r.floatPct), F(r.slide), F(r.slidePct), F(r.jitter), F(r.popMax), F(r.popFrames), F(r.headSnap), F(r.twistMax), F(r.twistAvg),
                    r.tposeStart.ToString(), r.tposeEnd.ToString(), r.tposeAll.ToString(), F(r.kneeHyper), F(r.kneeHyperPct), F(r.muscleOver), F(r.muscleMax), F(r.deadStart), F(r.deadEnd),
                    F(r.seamPose), F(r.seamRoot), F(r.idleDistStart), F(r.idleDistEnd), F(r.handsBelow), F(r.stanceSpeed), F(r.groundP10), F(r.hipMed)
                }));
            }
            File.WriteAllText(path, sb.ToString());
        }

        static string Q(string s) { return "\"" + (s ?? "").Replace("\"", "'") + "\""; }
        static string F(float v) { return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture); }
    }
}
