using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>One quarterback clip, measured (v0.13 QB-MOCAP). Angles in degrees (game frame, 0 = +Z, + = to the right), distances in metres, speeds in m/s, times in clip seconds.</summary>
    public sealed class QbRow
    {
        public string name = "", keys = ""; public float length; public int frames;
        // pose at the start / in the middle (stance clips)
        public float hipsH0, lean0, handsY0, hipsHMid, leanMid, handsYMid, handDist0;
        // facing: hips (hip line) and chest (shoulder line) yaw at the start and the end, and the largest excursion from the start
        public float hipYaw0, hipYaw1, chestYaw0, chestYaw1, hipExcursion, chestExcursion;
        // root
        public float travel, path, travelHeading, meanSpeed, maxSpeed, moveT0, moveT1, moveDist;
        // hands: the faster hand (R / L) and the moment of its peak (release), the facing at it
        public char hand = '?'; public float relT, relSpeed, relSpeedOther, chestYawRel, hipYawRel, handHeadingRel, handHeightRel;
        // both hands together (catch / secure): moment, distance
        public float togetherT, togetherD;
        // feet
        public float airPct, bothAirPct, slideMean, slidePct, plantedPct, penPct, floatCm, plantDisp;
        // QA verdict
        public float score, popMax, popFrames, waistTwist, torsoTwist, headTwist; public int tposeFrames; public bool reject; public string faults = "";
        // loop closure: pose distance between the last and the first frame (0 = seamless)
        public float seam;

        public static string Header()
        {
            return "name,keys,length,frames,hipsH0,lean0,handsY0,hipsHMid,leanMid,handsYMid,handDist0,hipYaw0,hipYaw1,chestYaw0,chestYaw1,hipExc,chestExc,travel,path,travelHeading,meanSpeed,maxSpeed,moveT0,moveT1,moveDist,hand,relT,relSpeed,relSpeedOther,chestYawRel,hipYawRel,handHeadingRel,handHeightRel,togetherT,togetherD,airPct,bothAirPct,slideMean,slidePct,plantedPct,penPct,floatCm,plantDisp,score,popMax,popFrames,waistTwist,torsoTwist,headTwist,tposeFrames,reject,faults,seam";
        }

        public string Csv()
        {
            Func<float, string> F = v => v.ToString("0.###", CultureInfo.InvariantCulture);
            return string.Join(",", new[]
            {
                "\"" + name + "\"", "\"" + keys + "\"", F(length), frames.ToString(), F(hipsH0), F(lean0), F(handsY0), F(hipsHMid), F(leanMid), F(handsYMid), F(handDist0), F(hipYaw0), F(hipYaw1), F(chestYaw0), F(chestYaw1), F(hipExcursion), F(chestExcursion),
                F(travel), F(path), F(travelHeading), F(meanSpeed), F(maxSpeed), F(moveT0), F(moveT1), F(moveDist), hand.ToString(), F(relT), F(relSpeed), F(relSpeedOther), F(chestYawRel), F(hipYawRel), F(handHeadingRel), F(handHeightRel), F(togetherT), F(togetherD),
                F(airPct), F(bothAirPct), F(slideMean), F(slidePct), F(plantedPct), F(penPct), F(floatCm), F(plantDisp), F(score), F(popMax), F(popFrames), F(waistTwist), F(torsoTwist), F(headTwist), tposeFrames.ToString(), reject ? "1" : "0", "\"" + (faults ?? "").Replace("\"", "'") + "\"", F(seam)
            });
        }
    }

    /// <summary>
    /// v0.13.0 QB-MOCAP: measures every quarterback clip once (facing excursion, root travel, the hand that releases and its moment, the facing at the release, hands together, hip height / lean of the start, foot air time,
    /// plus the ANIMLIB QA row) and writes <c>Logs/qb/clip_audit.csv</c>. The catalog builder (<see cref="QbClipCatalog"/>) reads the same rows to decide which clip plays in which family.
    /// Batch: <c>-executeMethod PixFootball.EditorTools.QbClipAudit.Run</c>.
    /// </summary>
    public static class QbClipAudit
    {
        public const string OutDir = "Logs/qb";
        /// <summary>Names the audit treats as quarterback clips (Clean/*.anim).</summary>
        public static readonly Regex QbName = new Regex(@"qb|quarterback|pixstance_qb|pixqb", RegexOptions.IgnoreCase);
        const float Rate = 30f;

        public static void Run()
        {
            BatchWatchdog.Arm(60);
            Directory.CreateDirectory(OutDir);
            AssetDatabase.Refresh();
            var keys = LibKeys();
            var rig = AnimRig.CreateMedium(); if (rig == null) { Done(1); return; }
            var sb = new StringBuilder(QbRow.Header() + "\n");
            int n = 0, bad = 0;
            foreach (var c in QbClips())
            {
                try
                {
                    string k; keys.TryGetValue(c.name, out k);
                    var r = Measure(rig, c, k ?? "");
                    sb.AppendLine(r.Csv());
                }
                catch (Exception ex) { bad++; Debug.LogWarning("[QbClipAudit] " + c.name + ": " + ex.Message); }
                if (++n % 50 == 0) { Debug.Log("[QbClipAudit] " + n); File.WriteAllText(OutDir + "/clip_audit.csv", sb.ToString()); }
            }
            rig.Dispose();
            File.WriteAllText(OutDir + "/clip_audit.csv", sb.ToString());
            Debug.Log("[QbClipAudit] rows " + n + " failed " + bad + " -> " + OutDir + "/clip_audit.csv");
            Done(0);
        }

        static void Done(int code) { if (Application.isBatchMode) EditorApplication.Exit(code); }

        /// <summary>Library keys per clip name ('+' separated).</summary>
        public static Dictionary<string, string> LibKeys()
        {
            var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            ArtLibrary.ResetCache();
            var lib = ArtLibrary.Load();
            if (lib != null)
                foreach (var e in lib.clips)
                {
                    if (e == null || e.clip == null) continue;
                    List<string> l; if (!d.TryGetValue(e.clip.name, out l)) d[e.clip.name] = l = new List<string>();
                    if (!l.Contains(e.key)) l.Add(e.key);
                }
            return d.ToDictionary(k => k.Key, k => string.Join("+", k.Value), StringComparer.Ordinal);
        }

        /// <summary>Every quarterback clip of the Clean folder (and the loose QB assets), once per name.</summary>
        public static IEnumerable<AnimationClip> QbClips()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string g in AssetDatabase.FindAssets("t:AnimationClip", new[] { "Assets/_Licensed/DraftFever/Animations/Clean", "Assets/_Licensed/Resources/MirroredClips" }).OrderBy(x => AssetDatabase.GUIDToAssetPath(x), StringComparer.Ordinal))
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (p.Contains("/_rejected/")) continue;
                var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(p);
                if (c == null || !QbName.IsMatch(c.name) || !seen.Add(c.name)) continue;
                yield return c;
            }
        }

        static float YawOf(Vector3 v) { return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }

        public static QbRow Measure(AnimRig rig, AnimationClip clip, string keys)
        {
            var r = new QbRow { name = clip.name, keys = keys, length = clip.length };
            float dur = Mathf.Min(clip.length, 30f);
            var fr = rig.Sample(clip, 0f, dur, Rate);
            int N = fr.Count; r.frames = N;
            if (N < 3) return r;
            // ground = lowest toe of the clip
            float ground = float.MaxValue;
            for (int i = 0; i < N; i++) ground = Mathf.Min(ground, Mathf.Min(fr[i].P[AnimRig.LT].y, fr[i].P[AnimRig.RT].y));
            Func<int, float> hipYaw = i => YawOf(AnimRig.Forward(fr[i]));
            Func<int, float> chestYaw = i => YawOf(AnimRig.ShoulderForward(fr[i]));
            // start / mid pose
            int mid = Mathf.Clamp(Mathf.RoundToInt(N * 0.4f), 0, N - 1);
            r.hipsH0 = fr[0].P[AnimRig.Hips].y - ground; r.lean0 = Vector3.Angle(fr[0].P[AnimRig.Head] - fr[0].P[AnimRig.Hips], Vector3.up);
            r.handsY0 = (fr[0].P[AnimRig.LH].y + fr[0].P[AnimRig.RH].y) * 0.5f - ground; r.handDist0 = Vector3.Distance(fr[0].P[AnimRig.LH], fr[0].P[AnimRig.RH]);
            r.hipsHMid = fr[mid].P[AnimRig.Hips].y - ground; r.leanMid = Vector3.Angle(fr[mid].P[AnimRig.Head] - fr[mid].P[AnimRig.Hips], Vector3.up);
            r.handsYMid = (fr[mid].P[AnimRig.LH].y + fr[mid].P[AnimRig.RH].y) * 0.5f - ground;
            // facing
            r.hipYaw0 = hipYaw(0); r.hipYaw1 = hipYaw(N - 1); r.chestYaw0 = chestYaw(0); r.chestYaw1 = chestYaw(N - 1);
            for (int i = 0; i < N; i++) { r.hipExcursion = Mathf.Max(r.hipExcursion, Mathf.Abs(Mathf.DeltaAngle(r.hipYaw0, hipYaw(i)))); r.chestExcursion = Mathf.Max(r.chestExcursion, Mathf.Abs(Mathf.DeltaAngle(r.chestYaw0, chestYaw(i)))); }
            // root
            Vector3 d = fr[N - 1].P[AnimRig.Hips] - fr[0].P[AnimRig.Hips]; d.y = 0f;
            r.travel = d.magnitude; r.travelHeading = r.travel > 0.2f ? Mathf.DeltaAngle(r.hipYaw0, YawOf(d)) : 0f;
            var sp = new float[N];
            for (int i = 1; i < N; i++) { Vector3 s = fr[i].P[AnimRig.Hips] - fr[i - 1].P[AnimRig.Hips]; s.y = 0f; sp[i] = s.magnitude * Rate; r.path += s.magnitude; r.maxSpeed = Mathf.Max(r.maxSpeed, sp[i]); }
            r.meanSpeed = r.path / Mathf.Max(0.1f, dur);
            // the moving window: first / last frame above 0.6 m/s (smoothed over 5 frames)
            int m0 = -1, m1 = -1;
            for (int i = 2; i < N - 2; i++) { float s5 = (sp[i - 2] + sp[i - 1] + sp[i] + sp[i + 1] + sp[i + 2]) / 5f; if (s5 > 0.6f) { if (m0 < 0) m0 = i; m1 = i; } }
            if (m0 >= 0) { r.moveT0 = m0 / Rate; r.moveT1 = m1 / Rate; Vector3 dm = fr[m1].P[AnimRig.Hips] - fr[m0].P[AnimRig.Hips]; dm.y = 0f; r.moveDist = dm.magnitude; }
            // hands relative to the hips: the faster one in [15 %, 92 %] is the throwing hand, its peak the release
            float bestR = 0f, bestL = 0f; int iR = -1, iL = -1;
            int a = Mathf.Max(2, Mathf.RoundToInt(N * 0.15f)), b = Mathf.Min(N - 2, Mathf.RoundToInt(N * 0.92f));
            for (int i = a; i < b; i++)
            {
                Vector3 vr = ((fr[i + 1].P[AnimRig.RH] - fr[i + 1].P[AnimRig.Hips]) - (fr[i - 1].P[AnimRig.RH] - fr[i - 1].P[AnimRig.Hips])) * (Rate / 2f);
                Vector3 vl = ((fr[i + 1].P[AnimRig.LH] - fr[i + 1].P[AnimRig.Hips]) - (fr[i - 1].P[AnimRig.LH] - fr[i - 1].P[AnimRig.Hips])) * (Rate / 2f);
                if (vr.magnitude > bestR) { bestR = vr.magnitude; iR = i; }
                if (vl.magnitude > bestL) { bestL = vl.magnitude; iL = i; }
            }
            int ir = bestR >= bestL ? iR : iL;
            if (ir > 0)
            {
                r.hand = bestR >= bestL ? 'R' : 'L'; r.relSpeed = Mathf.Max(bestR, bestL); r.relSpeedOther = Mathf.Min(bestR, bestL); r.relT = ir / Rate;
                r.chestYawRel = chestYaw(ir); r.hipYawRel = hipYaw(ir);
                int hb = r.hand == 'R' ? AnimRig.RH : AnimRig.LH;
                Vector3 hv = (fr[ir + 1].P[hb] - fr[ir - 1].P[hb]); hv.y = 0f;
                r.handHeadingRel = hv.sqrMagnitude > 1e-6f ? YawOf(hv) : r.chestYawRel;
                r.handHeightRel = fr[ir].P[hb].y - ground;
            }
            // hands together
            float bd = float.MaxValue; int bi = 0;
            for (int i = Mathf.RoundToInt(N * 0.05f); i < N - 1; i++) { float dd = Vector3.Distance(fr[i].P[AnimRig.LH], fr[i].P[AnimRig.RH]); if (dd < bd) { bd = dd; bi = i; } }
            r.togetherT = bi / Rate; r.togetherD = bd;
            // feet
            int air = 0, both = 0; float sl = 0f; int slN = 0;
            for (int i = 0; i < N; i++)
            {
                bool la = fr[i].P[AnimRig.LF].y - ground > 0.16f, ra = fr[i].P[AnimRig.RF].y - ground > 0.16f;
                if (la || ra) air++; if (la && ra) both++;
            }
            r.airPct = 100f * air / N; r.bothAirPct = 100f * both / N;
            // loop seam: pose distance between the last and the first frame (hips-relative joint positions)
            float seam = 0f;
            for (int j = 0; j < AnimRig.NB; j++) seam += ((fr[N - 1].P[j] - fr[N - 1].P[AnimRig.Hips]) - (fr[0].P[j] - fr[0].P[AnimRig.Hips])).magnitude;
            r.seam = seam / AnimRig.NB;
            // the shared QA row
            var q = ClipQa.Analyse(rig, clip, keys);
            r.slideMean = q.slideMean; r.slidePct = q.slidePct; r.plantedPct = q.plantedPct; r.penPct = q.penPct; r.floatCm = q.floatCm; r.plantDisp = q.plantDisp;
            r.score = q.score; r.popMax = q.popMax; r.popFrames = q.popFrames; r.waistTwist = q.waistTwist; r.torsoTwist = q.torsoTwist; r.headTwist = q.headTwist; r.tposeFrames = q.tposeFrames; r.reject = q.reject; r.faults = q.faults;
            return r;
        }
    }
}

namespace PixFootball.EditorTools
{
    /// <summary>QA probe: what the match shows of a clip's travel (root motion off).</summary>
    public static class QbRootProbe
    {
        public static void Run()
        {
            BatchWatchdog.Arm(10);
            var rig = AnimRig.CreateMedium();
            var sb = new System.Text.StringBuilder();
            foreach (string n in new[] { "BONES_QB_ready_to_run_left_001_s01", "BONES_QB_run_right_pass_001_s01", "BONES_QB_pass_quick_bullet_001_s01", "QB_undercenter_catch_to_ 5_step_dropback_001", "BONES_QB_ready_to_stepforward_001_s01" })
            {
                var c = QbClipAudit.QbClips().FirstOrDefault(x => x.name == n); if (c == null) { sb.AppendLine(n + " missing"); continue; }
                foreach (bool rm in new[] { true, false })
                {
                    rig.Anim.applyRootMotion = rm;
                    var fr = rig.Sample(c, 0f, Mathf.Min(c.length, 8f), 30f);
                    var h0 = fr[0].P[AnimRig.Hips]; var h1 = fr[fr.Count - 1].P[AnimRig.Hips];
                    float mx = 0f; foreach (var f in fr) { var d = f.P[AnimRig.Hips] - h0; d.y = 0; mx = Mathf.Max(mx, d.magnitude); }
                    sb.AppendLine(n + " rootMotion=" + rm + " len=" + c.length.ToString("0.0") + " hips start " + h0 + " end " + h1 + " maxXZ " + mx.ToString("0.00"));
                }
            }
            rig.Dispose();
            System.IO.File.WriteAllText(QbClipAudit.OutDir + "/root_probe.txt", sb.ToString());
            Debug.Log("[QbRootProbe]\n" + sb);
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
    }
}
