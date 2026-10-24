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
    /// v0.11.0 ANIMLIB batch entry points. Lists are read from Logs/animlib/ (written by the driver scripts):
    ///   live_clips.txt  - clip names the game can play                        QaLive   -> v11_qa_<tag>.csv
    ///   v11_test.txt    - a handful of clips for the foot IK experiments      IkTest   -> v11_iktest.csv
    /// Run with  -executeMethod PixFootball.EditorTools.AnimLibV11.QaLive -pixTag before
    /// </summary>
    public static class AnimLibV11
    {
        public const string OutDir = "Logs/animlib";

        static string Arg(string key, string def)
        {
            var a = Environment.GetCommandLineArgs();
            for (int i = 0; i < a.Length - 1; i++) if (a[i] == key) return a[i + 1];
            return def;
        }

        public static List<string> Names(string file)
        {
            if (!File.Exists(file)) return new List<string>();
            return File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("#")).Distinct().ToList();
        }

        static string _loadDir;
        static AnimationClip Load(string name) { return AssetDatabase.LoadAssetAtPath<AnimationClip>(_loadDir == null ? ClipCleaner.PathOf(name) : _loadDir + "/" + ClipCleaner.SafeName(name) + ".anim"); }

        static Dictionary<string, string> LibKeys()
        {
            var d = new Dictionary<string, List<string>>(StringComparer.Ordinal);
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

        /// <summary>QA of the clips in Logs/animlib/<-pixList>.txt (default live_clips) -> Logs/animlib/v11_qa_<-pixTag>.csv</summary>
        public static void QaLive()
        {
            BatchWatchdog.Arm(120);
            AssetDatabase.Refresh();
            Directory.CreateDirectory(OutDir);
            string tag = Arg("-pixTag", "before"), list = Arg("-pixList", "live_clips"); _loadDir = Arg("-pixDir", null);
            var names = Names(OutDir + "/" + list + ".txt");
            var keys = LibKeys();
            var rig = FootAxes.NewRig(); if (rig == null) { AnimLibBatch.Done(); return; }
            var sb = new StringBuilder(QaRow.Header() + "\n"); int n = 0, miss = 0;
            foreach (string nm in names)
            {
                var c = Load(nm); if (c == null || c.length > 24f) { miss++; continue; }
                try { string k; keys.TryGetValue(nm, out k); var r = ClipQa.Analyse(rig, c, k ?? ""); r.source = AssetDatabase.GetAssetPath(c); sb.AppendLine(r.Csv()); }
                catch (Exception ex) { Debug.LogWarning("[AnimLibV11] qa " + nm + ": " + ex.Message); }
                if (++n % 100 == 0) { Debug.Log("[AnimLibV11] qa " + n); File.WriteAllText(OutDir + "/v11_qa_" + tag + ".csv", sb.ToString()); }
            }
            rig.Dispose();
            File.WriteAllText(OutDir + "/v11_qa_" + tag + ".csv", sb.ToString());
            Debug.Log("[AnimLibV11] qa rows " + n + " missing " + miss + " -> v11_qa_" + tag + ".csv");
            AnimLibBatch.Done();
        }

        static bool ConstantXZ(PoseSeq seq)
        {
            for (int i = 1; i < seq.N; i++) if (Mathf.Abs(seq.RT[i].x - seq.RT[0].x) > 1e-3f || Mathf.Abs(seq.RT[i].z - seq.RT[0].z) > 1e-3f) return false;
            return true;
        }

        /// <summary>Foot lock + level foot only (no other stage) over the clips of Logs/animlib/(-pixList).txt, written (and checked against the stored result) when the leg metrics improve. Log v11_foot_(-pixTag).csv.</summary>
        public static void FootPass()
        {
            BatchWatchdog.Arm(240);
            AssetDatabase.Refresh();
            Directory.CreateDirectory(OutDir);
            string tag = Arg("-pixTag", "foot"), list = Arg("-pixList", "all_clean");
            var rig = FootAxes.NewRig(); if (rig == null) { AnimLibBatch.Done(); return; }
            var log = new StringBuilder("name,accepted,scoreB,scoreA,dispB,dispA,rollB,rollA,yawB,yawA,toeB,toeA,note\n");
            int n = 0, wrote = 0;
            Func<float, string> F = v => v.ToString("0.##");
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string nm in Names(OutDir + "/" + list + ".txt"))
                {
                    try
                    {
                        var w = Prep(rig, nm); if (w == null) continue;
                        FootIk.Last = null; QaRow q1; string note;
                        bool ok = FootIk.Try(rig, w.seq, w.q0, out q1, out note);
                        var ls = FootIk.Last ?? new FootIk.Stat();
                        if (ok) { w.q0 = q1; w.changed = true; w.ik = true; WriteChecked(rig, w); wrote++; }
                        log.AppendLine(string.Join(",", new[] { "\"" + nm + "\"", ok ? "1" : "0", F(w.qOrig.score), F((q1 ?? w.qOrig).score), F(ls.dispB), F(ls.dispA), F(ls.rollB), F(ls.rollA), F(ls.yawB), F(ls.yawA), F(ls.toeB), F(ls.toeA), "\"" + note + (w.notes.Length > 0 ? " " + w.notes : "") + "\"" }));
                    }
                    catch (Exception ex) { Debug.LogWarning("[AnimLibV11] foot " + nm + ": " + ex.Message); }
                    if (++n % 50 == 0) { Debug.Log("[AnimLibV11] foot " + n + " wrote " + wrote); File.WriteAllText(OutDir + "/v11_foot_" + tag + ".csv", log.ToString()); }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            rig.Dispose();
            AssetDatabase.SaveAssets();
            File.WriteAllText(OutDir + "/v11_foot_" + tag + ".csv", log.ToString());
            Debug.Log("[AnimLibV11] foot pass clips=" + n + " written=" + wrote);
            AnimLibBatch.Done();
        }
        // ------------------------------------------------------------------------------------------------ the full v0.11 polish pipeline
        static readonly System.Text.RegularExpressions.Regex PairRx = new System.Text.RegularExpressions.Regex(@"^(.*)_(A|B|BC|Def)(_M)?$");

        /// <summary>Groups of clip names that are two actors of one take: (A, B) or (BC, Def) with the same stem and mirror suffix. Singles come back as one-element groups.</summary>
        static List<List<string>> Groups(List<string> names)
        {
            var set = new HashSet<string>(names); var done = new HashSet<string>(); var res = new List<List<string>>();
            foreach (string n in names)
            {
                if (done.Contains(n)) continue;
                var m = PairRx.Match(n); var g = new List<string> { n }; done.Add(n);
                if (m.Success)
                {
                    string role = m.Groups[2].Value, stem = m.Groups[1].Value, mir = m.Groups[3].Value;
                    string other = role == "A" ? "B" : role == "B" ? "A" : role == "BC" ? "Def" : "BC";
                    string twin = stem + "_" + other + mir;
                    if (set.Contains(twin) && !done.Contains(twin)) { g.Add(twin); done.Add(twin); }
                }
                res.Add(g);
            }
            return res;
        }

        sealed class Work { public string name; public PoseSeq seq, orig; public QaRow q0, qOrig; public bool changed, ik; public string notes = ""; }

        static Work Prep(AnimRig rig, string nm)
        {
            var c = Load(nm); if (c == null || c.length > 24f || c.length < 0.3f) return null;
            var seq = PoseSeq.FromClip(c, 0f, c.length, 30f); seq.Name = c.name; seq.Loop = c.isLooping; seq.InPlace = ConstantXZ(seq);
            var q = ClipQa.Analyse(rig, seq); return new Work { name = nm, seq = seq, orig = CloneOf(seq), q0 = q, qOrig = q };
        }

        static bool Accept(QaRow q0, QaRow q1, float slack = 0.5f) { return q1.score >= q0.score - slack && q1.popFrames <= q0.popFrames + 0.5f; }

        static PoseSeq CloneOf(PoseSeq seq) { var s = seq.Clone(); s.Name = seq.Name; s.InPlace = seq.InPlace; s.Loop = seq.Loop; return s; }

        /// <summary>The fix stages: T-pose bridge, wrist limit, floor lift and dead-time cut (the group's shared values), the foot IK, the hands.</summary>
        static void Stage(AnimRig rig, Work w, float lift, float cut0, float cut1, bool paired, Func<AnimRig, Work, bool> handFn)
        {
            var seq = w.seq; var q0 = w.q0;
            // 1) T-pose frames inside the take
            if (q0.tposeFrames > 0)
            {
                var s = CloneOf(seq);
                int n = ClipPolish2.TPoseRepair(rig, s);
                if (n > 0) { var q1 = ClipQa.Analyse(rig, s); if (q1.tposeFrames < q0.tposeFrames && Accept(q0, q1)) { Copy(s, seq); q0 = q1; w.changed = true; w.notes += " tpose:" + n; } }
            }
            // 2) wrist / forearm limits
            if (q0.wristOverPct > 0.2f || q0.forearmTwistMax > 100f)
            {
                var s = CloneOf(seq);
                int n = ClipPolish2.WristClamp(s);
                if (n > 0) { var q1 = ClipQa.Analyse(rig, s); if (q1.wristOverPct < q0.wristOverPct - 0.1f && Accept(q0, q1)) { Copy(s, seq); q0 = q1; w.changed = true; w.notes += " wrist:" + n; } }
            }
            // 3) body sunk into the turf
            if (lift > 0.02f)
            {
                var s = CloneOf(seq);
                ClipPolish2.Lift(rig, s, lift);
                var q1 = ClipQa.Analyse(rig, s);
                if (q1.penPct <= q0.penPct && Accept(q0, q1)) { Copy(s, seq); q0 = q1; w.changed = true; w.notes += " lift:" + (lift * 100f).ToString("0") + "cm"; }
            }
            // 4) dead time at the ends (the same cut from both actors of a pair; root not re-based for pairs, they share an origin)
            if ((cut0 > 0.05f || cut1 > 0.05f) && !seq.Loop)
            {
                float dur = seq.Duration; float a = cut0 > 0.05f ? cut0 : 0f, b = dur - (cut1 > 0.05f ? cut1 : 0f);
                if (b - a >= 0.8f)
                {
                    var tr = seq.Trim(a, b); tr.Name = seq.Name; tr.Loop = seq.Loop; tr.InPlace = seq.InPlace;
                    if (a > 0f && !paired) tr.RebaseRoot(false);
                    var q1 = ClipQa.Analyse(rig, tr);
                    if (Accept(q0, q1, 2f)) { seq = tr; w.seq = tr; q0 = q1; w.changed = true; w.notes += " trim:" + a.ToString("0.00") + "+" + (dur - b).ToString("0.00"); }
                }
            }
            // 5) foot lock + level foot
            { QaRow q4; string note; if (FootIk.Try(rig, seq, q0, out q4, out note)) { q0 = q4; w.changed = true; w.ik = true; w.notes += " " + note; } else if (note.Contains("REJECTED")) w.notes += " ik-" + note.Substring(note.IndexOf("REJECTED")); }
            // 6) hands
            if (handFn != null) { w.seq = seq; if (handFn(rig, w)) { w.changed = true; } }
            w.q0 = q0; w.seq = seq;
        }

        static void Copy(PoseSeq from, PoseSeq to) { for (int m = 0; m < from.M.Length; m++) Array.Copy(from.M[m], to.M[m], from.N); Array.Copy(from.RT, to.RT, from.N); Array.Copy(from.RQ, to.RQ, from.N); }

        /// <summary>Writes a processed clip and checks the stored result with the QA: if the file scores worse than what was measured on the sequence (simplification overshoot, a muscle read-back that flips), it is written again nearly lossless; if that is still worse, the original sequence goes back.</summary>
        static void WriteChecked(AnimRig rig, Work w)
        {
            ClipCleaner.WriteSeq(w.seq, w.ik ? 0.006f : 0.015f, w.ik ? 0.0012f : -1f);
            var c = Load(w.name); if (c == null) return;
            var q = ClipQa.Analyse(rig, c);
            bool bad = q.score < w.q0.score - 6f || q.popFrames > w.q0.popFrames + 0.5f || (w.ik && q.slidePct > w.q0.slidePct + 15f);
            if (!bad) return;
            ClipCleaner.WriteSeq(w.seq, 0.002f, 0.0006f);
            q = ClipQa.Analyse(rig, Load(w.name));
            bad = q.score < w.q0.score - 6f || q.popFrames > w.q0.popFrames + 0.5f || (w.ik && q.slidePct > w.q0.slidePct + 15f);
            if (!bad) { w.notes += " rewritten-lossless"; return; }
            ClipCleaner.WriteSeq(w.orig, 0.004f);
            w.ik = false; w.notes += " ROLLBACK(" + w.q0.score.ToString("0") + "/" + q.score.ToString("0") + " pops " + w.q0.popFrames + "/" + q.popFrames + ")"; w.q0 = w.qOrig;
        }

        /// <summary>The whole v0.11 cleanup over Logs/animlib/(-pixList).txt (default all_clean), clip by clip, pairs together; writes Clean/*.anim and Logs/animlib/v11_pipe_(-pixTag).csv. -pixHands 1 also rewrites the hands.</summary>
        public static void Pipeline()
        {
            BatchWatchdog.Arm(300);
            AssetDatabase.Refresh();
            Directory.CreateDirectory(OutDir);
            string tag = Arg("-pixTag", "pipe"), list = Arg("-pixList", "all_clean"); bool hands = Arg("-pixHands", "1") == "1";
            var names = Names(OutDir + "/" + list + ".txt");
            var rig = FootAxes.NewRig(); if (rig == null) { AnimLibBatch.Done(); return; }
            var log = new StringBuilder("name,changed,ik,scoreB,scoreA,slideB,slideA,penB,penA,wristB,wristA,notes\n");
            int n = 0, wrote = 0;
            Func<float, string> F = v => v.ToString("0.##");
            Func<AnimRig, Work, bool> handFn = null;
            if (hands) handFn = (r, w2) => { string note = FingerTransplant.Transplant(w2.seq, w2.name); if (note.StartsWith("fingers:mocap")) { w2.notes += " " + note; return true; } if (note != "") w2.notes += " " + note; ClipPolish.HandsOnly(r, w2.seq); w2.notes += " hands:authored"; return true; };
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var g in Groups(names))
                {
                    try
                    {
                        var ws = g.Select(nm => Prep(rig, nm)).Where(x => x != null).ToList();
                        if (ws.Count == 0) continue;
                        bool paired = ws.Count == 2;
                        // shared values of a pair: the smaller lift, the smaller dead cuts
                        var lifts = new List<float>(); var c0 = new List<float>(); var c1 = new List<float>();
                        foreach (var w in ws)
                        {
                            float hm; lifts.Add(ClipPolish2.FloorLift(rig, w.seq, out hm));
                            float a, b; ClipPolish2.DeadCut(w.q0, out a, out b); c0.Add(a); c1.Add(b);
                        }
                        float lift = lifts.Min(), cut0 = c0.Min(), cut1 = c1.Min();
                        foreach (var w in ws)
                        {
                            var q0 = w.q0;
                            Stage(rig, w, lift, cut0, cut1, paired, handFn);
                            if (w.changed) { WriteChecked(rig, w); wrote++; }
                            var qa = w.q0;
                            log.AppendLine(string.Join(",", new[] { "\"" + w.name + "\"", w.changed ? "1" : "0", w.ik ? "1" : "0", F(q0.score), F(qa.score), F(q0.slidePct), F(qa.slidePct), F(q0.penPct), F(qa.penPct), F(q0.wristOverPct), F(qa.wristOverPct), "\"" + w.notes.Trim() + "\"" }));
                            n++;
                        }
                    }
                    catch (Exception ex) { Debug.LogWarning("[AnimLibV11] pipeline " + g[0] + ": " + ex.Message + "\n" + ex.StackTrace); }
                    if (n % 50 < 2) { Debug.Log("[AnimLibV11] pipeline " + n + " wrote " + wrote); File.WriteAllText(OutDir + "/v11_pipe_" + tag + ".csv", log.ToString()); }
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            rig.Dispose();
            AssetDatabase.SaveAssets();
            File.WriteAllText(OutDir + "/v11_pipe_" + tag + ".csv", log.ToString());
            Debug.Log("[AnimLibV11] pipeline clips=" + n + " written=" + wrote);
            AnimLibBatch.Done();
        }


        /// <summary>Does re-simplifying a clip hurt it? For each clip of v11_test.txt: QA of the stored clip, then of the resampled clip simplified at 0.015 / 0.006 / 0.002, with and without the foot IK.</summary>
        public static void SimplifyTest()
        {
            BatchWatchdog.Arm(20);
            var rig = FootAxes.NewRig(); if (rig == null) { AnimLibBatch.Done(); return; }
            foreach (string nm in Names(OutDir + "/v11_test.txt"))
            {
                var c = Load(nm); if (c == null) continue;
                var q0 = ClipQa.Analyse(rig, c);
                var seq = PoseSeq.FromClip(c, 0f, c.length, 30f); seq.Name = c.name; seq.Loop = c.isLooping; seq.InPlace = ConstantXZ(seq);
                string tnote = FingerTransplant.Transplant(seq, nm); var sb = new StringBuilder(nm + " " + tnote + " stored pops " + q0.popFrames + " score " + q0.score.ToString("0"));
                var qraw = ClipQa.Analyse(rig, seq); sb.Append(" | seq pops " + qraw.popFrames + " score " + qraw.score.ToString("0") + " [" + qraw.faults + "]");
                foreach (float tol in new[] { 0.015f, 0.006f, 0.002f })
                {
                    var c2 = seq.ToClip("t"); ClipCleaner.Simplify(c2, tol);
                    var q = ClipQa.Analyse(rig, c2); sb.Append(" | tol " + tol + " pops " + q.popFrames + " score " + q.score.ToString("0") + " [" + q.faults + "] fo " + q.fingerOverPct.ToString("0.0"));
                    UnityEngine.Object.DestroyImmediate(c2);
                }
                Debug.Log("[AnimLibV11] " + sb);
            }
            AnimLibBatch.Done();
        }

        /// <summary>Before / after stick contact sheet of the clips in Logs/animlib/top24.txt: v0.10 clip (Assets/AnimBackup/v0100) on the left, the clip now on the right, one row per clip.</summary>
        public static void ContactSheets()
        {
            BatchWatchdog.Arm(20);
            var names = Names(OutDir + "/top24.txt"); var items = new List<string>();
            foreach (string n in names) { items.Add("path:" + FootShots.BackupDir + "/" + ClipCleaner.SafeName(n) + ".anim"); items.Add("path:" + ClipCleaner.PathOf(n)); }
            string dir = Directory.Exists("D:/") ? "D:/PIXFootball/Renders/animlib_v0110" : "Logs/animlib_sheets"; Directory.CreateDirectory(dir);
            for (int part = 0; part < 2; part++) StickSheet.RenderGrid(items.Skip(part * 24).Take(24).ToList(), dir + "/contact_sheet_top24_before_after_" + (part + 1) + ".png", 2, 6, 4f);
            AnimLibBatch.Done();
        }

        /// <summary>The forced limits (ClipPolish.Force: tighter twist / bend / head limits, pop limiter, knee / elbow clamp) over Logs/animlib/(-pixList).txt, written when applied. Log v11_force.csv.</summary>
        public static void ForceFix()
        {
            BatchWatchdog.Arm(60);
            AssetDatabase.Refresh();
            string list = Arg("-pixList", "force_list");
            var rig = FootAxes.NewRig(); if (rig == null) { AnimLibBatch.Done(); return; }
            var log = new StringBuilder("name,result\n"); int n = 0, wrote = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string nm in Names(OutDir + "/" + list + ".txt"))
                {
                    var c = Load(nm); if (c == null || c.length > 24f) continue;
                    var seq = PoseSeq.FromClip(c, 0f, c.length, 30f); seq.Name = c.name; seq.Loop = c.isLooping; seq.InPlace = ConstantXZ(seq);
                    string note = ClipPolish.Force(rig, seq); n++;
                    if (note.StartsWith("forced")) { ClipCleaner.WriteSeq(seq, 0.01f, 0.004f); wrote++; }
                    log.AppendLine("\"" + nm + "\",\"" + note + "\"");
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            rig.Dispose(); AssetDatabase.SaveAssets();
            File.WriteAllText(OutDir + "/v11_force.csv", log.ToString());
            Debug.Log("[AnimLibV11] force clips=" + n + " written=" + wrote);
            AnimLibBatch.Done();
        }

        /// <summary>Per-plant before / after report: the plants of the v0.10 clip (Assets/AnimBackup/v0100) are located, and the same frames are measured in the clip now (ankle displacement, ankle roll while flat, foot yaw drift, toe-out). Plants whose old displacement is within 12 cm are the ones the foot lock pins. -pixList names (default live2_clips), writes v11_plants_(-pixTag).csv.</summary>
        public static void PlantReport()
        {
            BatchWatchdog.Arm(60);
            AssetDatabase.Refresh();
            string tag = Arg("-pixTag", "plants"), list = Arg("-pixList", "live2_clips");
            var rig = FootAxes.NewRig(); if (rig == null) { AnimLibBatch.Done(); return; }
            var sb = new StringBuilder("name,side,i0,i1,dispB,dispA,rollB,rollA,yawB,yawA,toeB,toeA,len\n"); int clips = 0, plants = 0;
            foreach (string nm in Names(OutDir + "/" + list + ".txt"))
            {
                var after = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipCleaner.PathOf(nm));
                var before = AssetDatabase.LoadAssetAtPath<AnimationClip>(FootShots.BackupDir + "/" + ClipCleaner.SafeName(nm) + ".anim");
                if (after == null || before == null || before.length > 24f || Mathf.Abs(after.length - before.length) > 0.05f) continue;
                var fb = rig.Sample(before, 0f, before.length, 30f); var fa = rig.Sample(after, 0f, after.length, 30f);
                if (fa.Count != fb.Count) continue;
                var sole = Plants.Soles(fb); float ground = Plants.Ground(sole);
                var pl = Plants.Find(sole, ground, 1f / 30f); var cores = LegMetrics.Cores(pl);
                clips++;
                for (int k = 0; k < pl.Count; k++)
                {
                    var one = new List<int[]> { cores[k] };
                    var b = LegMetrics.CoreMetrics(one, fb, ground); var a = LegMetrics.CoreMetrics(one, fa, ground);
                    sb.AppendLine(string.Join(",", new[] { "\"" + nm + "\"", pl[k].side.ToString(), cores[k][1].ToString(), cores[k][2].ToString(), b.disp.ToString("0.##"), a.disp.ToString("0.##"), b.roll.ToString("0.##"), a.roll.ToString("0.##"), b.yaw.ToString("0.##"), a.yaw.ToString("0.##"), b.toe.ToString("0.##"), a.toe.ToString("0.##"), (cores[k][2] - cores[k][1] + 1).ToString() })); plants++;
                }
            }
            rig.Dispose();
            File.WriteAllText(OutDir + "/v11_plants_" + tag + ".csv", sb.ToString());
            Debug.Log("[AnimLibV11] plant report clips=" + clips + " plants=" + plants);
            AnimLibBatch.Done();
        }

        /// <summary>Per-frame sole heights (cm) and hips height of the clips in v11_test.txt, to the log (every 3rd frame).</summary>
        public static void SoleDump()
        {
            BatchWatchdog.Arm(10);
            var rig = FootAxes.NewRig(); if (rig == null) { AnimLibBatch.Done(); return; }
            foreach (string nm in Names(OutDir + "/v11_test.txt"))
            {
                var c = Load(nm); if (c == null) continue;
                var fr = rig.Sample(c, 0f, Mathf.Min(c.length, 8f), 30f); var sb = new StringBuilder("[SoleDump] " + nm + " len " + c.length.ToString("0.00") + "\n");
                for (int i = 0; i < fr.Count; i += 3) sb.Append(" f" + i + " L" + (ClipQa.Sole(fr[i], true) * 100f).ToString("0") + " R" + (ClipQa.Sole(fr[i], false) * 100f).ToString("0") + " hip" + (fr[i].P[AnimRig.Hips].y * 100f).ToString("0") + (i % 18 == 15 ? "\n" : ""));
                Debug.Log(sb.ToString());
            }
            AnimLibBatch.Done();
        }

        /// <summary>B-009 data for the choreography: for every clip of Logs/animlib/(-pixList).txt the time the hips first drop under 0.40 m and stay under 0.45 m for 0.5 s (the take goes down: pancake, fall, tackle), the lowest hips and the length. Docs/QA/v0110/animlib_lying_clips.csv.</summary>
        public static void LyingReport()
        {
            BatchWatchdog.Arm(40);
            string list = Arg("-pixList", "all_clean");
            var rig = FootAxes.NewRig(); if (rig == null) { AnimLibBatch.Done(); return; }
            var sb = new StringBuilder("clip,lieT,hipsMinM,length,hipsEndM\n"); int n = 0;
            foreach (string nm in Names(OutDir + "/" + list + ".txt"))
            {
                var c = Load(nm); if (c == null || c.length > 24f) continue;
                var fr = rig.Sample(c, 0f, c.length, 30f); float lieT = -1f, mn = 9f;
                for (int i = 0; i < fr.Count; i++) mn = Mathf.Min(mn, fr[i].P[AnimRig.Hips].y);
                for (int i = 0; i < fr.Count - 15 && lieT < 0f; i++)
                {
                    if (fr[i].P[AnimRig.Hips].y >= 0.40f) continue; bool stay = true;
                    for (int k = i; k <= i + 15; k++) if (fr[k].P[AnimRig.Hips].y > 0.45f) { stay = false; break; }
                    if (stay) lieT = i / 30f;
                }
                if (lieT < 0f) continue;
                sb.AppendLine("\"" + nm + "\"," + lieT.ToString("0.00") + "," + mn.ToString("0.00") + "," + c.length.ToString("0.00") + "," + fr[fr.Count - 1].P[AnimRig.Hips].y.ToString("0.00")); n++;
            }
            rig.Dispose();
            Directory.CreateDirectory("Docs/QA/v0110");
            File.WriteAllText("Docs/QA/v0110/animlib_lying_clips.csv", sb.ToString());
            Debug.Log("[AnimLibV11] lying clips " + n);
            AnimLibBatch.Done();
        }

        /// <summary>Where do the bone pops come from? For the clips of v11_test.txt: at every frame where one of the 11 QA bones turns more than 50 deg, the bone, the angle and the muscles that jumped most between the two frames (and the root rotation step).</summary>
        public static void PopProbe()
        {
            BatchWatchdog.Arm(15);
            var rig = FootAxes.NewRig(); if (rig == null) { AnimLibBatch.Done(); return; }
            int[] popIds = { AnimRig.Spine, AnimRig.Chest, AnimRig.Head, AnimRig.LUA, AnimRig.LLA, AnimRig.RUA, AnimRig.RLA, AnimRig.LUL, AnimRig.LLL, AnimRig.RUL, AnimRig.RLL };
            string[] nm2 = { "Spine", "Chest", "Head", "LUA", "LLA", "RUA", "RLA", "LUL", "LLL", "RUL", "RLL" };
            foreach (string nm in Names(OutDir + "/v11_test.txt"))
            {
                var c = Load(nm); if (c == null) continue;
                var seq = PoseSeq.FromClip(c, 0f, c.length, 30f); var fr = rig.Sample(c, 0f, c.length, 30f); var sb = new StringBuilder("[PopProbe] " + nm + "\n");
                var names = PoseSeq.MuscleNames;
                for (int i = 1; i < fr.Count && i < seq.N; i++)
                    for (int b = 0; b < popIds.Length; b++)
                    {
                        float a = Quaternion.Angle(fr[i - 1].R[popIds[b]], fr[i].R[popIds[b]]); if (a <= 50f) continue;
                        var top = new List<KeyValuePair<float, string>>();
                        for (int m = 0; m < 55; m++) { float d = Mathf.Abs(seq.M[m][i] - seq.M[m][i - 1]); if (d > 0.15f) top.Add(new KeyValuePair<float, string>(d, names[m] + " " + seq.M[m][i - 1].ToString("0.00") + ">" + seq.M[m][i].ToString("0.00"))); }
                        top.Sort((x, y) => y.Key.CompareTo(x.Key));
                        float rq = Quaternion.Angle(seq.RQ[i - 1], seq.RQ[i]);
                        sb.AppendLine("  f" + i + " " + nm2[b] + " " + a.ToString("0") + " deg | root rot step " + rq.ToString("0.0") + " | muscles: " + string.Join("; ", top.Take(4).Select(t => t.Value)));
                    }
                Debug.Log(sb.ToString());
            }
            AnimLibBatch.Done();
        }

        /// <summary>Own-tools attempt at the bone-pop clips (Logs/animlib/(-pixList).txt, default pop_list): the upper-leg muscles hard-limited to +-1 (a 180 deg thigh flip with no muscle jump = a value beyond the limit that wraps), single-frame root rotation steps bridged, then the per-muscle rate limiter; kept when the pops drop and the score does not. Log v11_popfix.csv.</summary>
        public static void PopFix()
        {
            BatchWatchdog.Arm(60);
            AssetDatabase.Refresh();
            string list = Arg("-pixList", "pop_list");
            var rig = FootAxes.NewRig(); if (rig == null) { AnimLibBatch.Done(); return; }
            var log = new StringBuilder("name,popsB,popsA,scoreB,scoreA,accepted,note\n"); int n = 0, fixedN = 0, wrote = 0;
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string nm in Names(OutDir + "/" + list + ".txt"))
                {
                    var w = Prep(rig, nm); if (w == null) continue; n++;
                    var s = CloneOf(w.seq); string note = "";
                    foreach (string side in new[] { "Left ", "Right " })
                        foreach (string mn in new[] { "Upper Leg Front-Back", "Upper Leg In-Out", "Upper Leg Twist In-Out" })
                        {
                            int m = ClipQa.MuscleIndex(side + mn); if (m < 0) continue; int c = 0;
                            for (int i = 0; i < s.N; i++) { float v = s.M[m][i]; if (Mathf.Abs(v) > 1f) { s.M[m][i] = Mathf.Sign(v); c++; } }
                            if (c > 0) note += " " + (side + mn).Replace(" ", "") + ":" + c;
                        }
                    int rb = 0;
                    for (int i = 1; i < s.N - 1; i++)
                        if (Quaternion.Angle(s.RQ[i - 1], s.RQ[i]) > 40f && Quaternion.Angle(s.RQ[i - 1], s.RQ[i + 1]) < 40f) { s.RQ[i] = Quaternion.Slerp(s.RQ[i - 1], s.RQ[i + 1], 0.5f); rb++; }
                    if (rb > 0) note += " rootbridge:" + rb;
                    var q1 = ClipQa.Analyse(rig, s);
                    if (q1.popFrames >= w.q0.popFrames && w.q0.popFrames >= 2f) { var s2 = CloneOf(s); int ch = ClipPolish.Pops(s2, 0.28f); var q2 = ClipQa.Analyse(rig, s2); if (q2.popFrames < q1.popFrames) { s = s2; q1 = q2; note += " ratelimit:" + ch; } }
                    bool ok = q1.popFrames < w.q0.popFrames && (q1.score >= w.q0.score - 2f || (q1.popFrames < 2f && q1.plantToe < w.q0.plantToe - 10f && q1.score >= w.q0.score - 22f));
                    if (ok) { Copy(s, w.seq); w.q0 = q1; w.changed = true; ClipCleaner.WriteSeq(w.seq, 0.01f, 0.004f); wrote++; if (q1.popFrames < 2f) fixedN++; }
                    log.AppendLine("\"" + nm + "\"," + w.qOrig.popFrames + "," + q1.popFrames + "," + w.qOrig.score.ToString("0") + "," + q1.score.ToString("0") + "," + (ok ? 1 : 0) + ",\"" + note.Trim() + "\"");
                }
            }
            finally { AssetDatabase.StopAssetEditing(); }
            rig.Dispose(); AssetDatabase.SaveAssets();
            File.WriteAllText(OutDir + "/v11_popfix.csv", log.ToString());
            Debug.Log("[AnimLibV11] popfix clips=" + n + " written=" + wrote + " now under 2 pops=" + fixedN);
            AnimLibBatch.Done();
        }

        /// <summary>The 2024 legacy-rig takes re-baked by Blender (Assets/_Licensed/DraftFever/Animations/Legacy2024/*.fbx): Humanoid import, the cleaner over every take (Logs/clean_in.txt is written from the folder).</summary>
        public static void LegacyImport()
        {
            BatchWatchdog.Arm(60);
            const string folder = "Assets/_Licensed/DraftFever/Animations/Legacy2024";
            AssetDatabase.Refresh();
            Debug.Log("[AnimLibV11] legacy import settings = " + AnimationImportSettings.ApplyFolder(folder));
            AssetDatabase.Refresh();
            AnimationImportSettings.ReportAvatars();
            var names = Directory.GetFiles(folder, "*.fbx").Select(p => Path.GetFileNameWithoutExtension(p)).ToList();
            File.WriteAllLines("Logs/clean_in.txt", names);
            CleanBatch.RunList();
            AssetDatabase.Refresh();
            var clean = names.Where(n => AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipCleaner.PathOf(n)) != null).ToList();
            File.WriteAllLines(OutDir + "/legacy_clean.txt", AssetDatabase.FindAssets("t:AnimationClip", new[] { ClipCleaner.CleanDir }).Select(g => Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(g))).Where(n => n.StartsWith("LEG__")).ToList());
            Debug.Log("[AnimLibV11] legacy takes " + names.Count + " cleaned " + clean.Count);
            AnimLibBatch.Done();
        }

        /// <summary>Foot IK experiment on Logs/animlib/v11_test.txt: before / after metrics, not written to disk.</summary>
        public static void IkTest()
        {
            BatchWatchdog.Arm(30);
            AssetDatabase.Refresh();
            Directory.CreateDirectory(OutDir);
            var rig = FootAxes.NewRig(); if (rig == null) { AnimLibBatch.Done(); return; }
            var sb = new StringBuilder("name,note,scoreB,scoreA,slideB,slideA,slideMeanB,slideMeanA,dispB,dispA,yawB,yawA,rollB,rollA,popsB,popsA,kneeB,kneeA,rtMax,rtMean,hipsDrift\n");
            foreach (string nm in Names(OutDir + "/v11_test.txt"))
            {
                var c = Load(nm); if (c == null) { Debug.LogWarning("[AnimLibV11] missing " + nm); continue; }
                try
                {
                    var q0 = ClipQa.Analyse(rig, c);
                    var seq = PoseSeq.FromClip(c, 0f, c.length, 30f); seq.Name = c.name; seq.Loop = c.isLooping;
                    float rtMax, rtMean; FootIk.RoundTrip(rig, seq, out rtMax, out rtMean);
                    var s2 = seq.Clone(); s2.Name = seq.Name;
                    var st = FootIk.Bake(rig, s2, !q0.treadmill);
                    var q1 = ClipQa.Analyse(rig, s2);
                    // hips drift: compare hips world positions before / after
                    float drift = 0f, maxCorr = 0f, maxCorrSpd = 0f; int flips = 0;
                    { var ca = seq.ToClip("a"); var cb = s2.ToClip("b"); var fa = rig.Sample(ca, 0f, (seq.N - 1) / 30f, 30f); var fb = rig.Sample(cb, 0f, (s2.N - 1) / 30f, 30f); UnityEngine.Object.DestroyImmediate(ca); UnityEngine.Object.DestroyImmediate(cb);
                      for (int i = 0; i < Mathf.Min(fa.Count, fb.Count); i++) { drift = Mathf.Max(drift, Vector3.Distance(fa[i].P[AnimRig.Hips], fb[i].P[AnimRig.Hips]));
                        foreach (int fi in new[] { AnimRig.LF, AnimRig.RF }) { Vector3 cd = fb[i].P[fi] - fa[i].P[fi]; maxCorr = Mathf.Max(maxCorr, cd.magnitude); if (i > 0) { Vector3 cp = fb[i - 1].P[fi] - fa[i - 1].P[fi]; maxCorrSpd = Mathf.Max(maxCorrSpd, (cd - cp).magnitude * 30f); } }
                        foreach (int[] ch in new[] { new[] { AnimRig.LUL, AnimRig.LLL, AnimRig.LF }, new[] { AnimRig.RUL, AnimRig.RLL, AnimRig.RF } }) if (i > 0) { Vector3 h1 = Vector3.Cross(fb[i].P[ch[1]] - fb[i].P[ch[0]], fb[i].P[ch[2]] - fb[i].P[ch[1]]), h0 = Vector3.Cross(fb[i - 1].P[ch[1]] - fb[i - 1].P[ch[0]], fb[i - 1].P[ch[2]] - fb[i - 1].P[ch[1]]); if (h1.magnitude > 0.02f && h0.magnitude > 0.02f && Vector3.Angle(h0, h1) > 40f) flips++; } } }
                    string popInfo = "";
                    { var ca = seq.ToClip("a"); var cb = s2.ToClip("b"); var fa = rig.Sample(ca, 0f, (seq.N - 1) / 30f, 30f); var fb = rig.Sample(cb, 0f, (s2.N - 1) / 30f, 30f); UnityEngine.Object.DestroyImmediate(ca); UnityEngine.Object.DestroyImmediate(cb);
                      for (int i = 1; i < Mathf.Min(fa.Count, fb.Count); i++) foreach (int fi in new[] { AnimRig.LF, AnimRig.RF }) { float da = Quaternion.Angle(fa[i - 1].R[fi], fa[i].R[fi]), db = Quaternion.Angle(fb[i - 1].R[fi], fb[i].R[fi]); if (db > 50f) popInfo += " f" + i + (fi == AnimRig.LF ? "L" : "R") + ":" + da.ToString("0") + "->" + db.ToString("0"); } }
                    if (popInfo.Length > 0) Debug.Log("[AnimLibV11]   pops " + nm + popInfo);
                    Func<float, string> F = v => v.ToString("0.##");
                    sb.AppendLine(string.Join(",", new[] { nm, "\"" + st.Note() + " | " + q0.faults + " -> " + q1.faults + "\"", F(q0.score), F(q1.score), F(q0.slidePct), F(q1.slidePct), F(q0.slideMean), F(q1.slideMean), F(q0.plantDisp), F(q1.plantDisp), F(q0.plantYaw), F(q1.plantYaw), F(q0.plantRoll), F(q1.plantRoll), F(q0.footPops), F(q1.footPops), F(q0.kneeHyper), F(q1.kneeHyper), F(rtMax), F(rtMean), F(drift * 100f) }));
                    Debug.Log("[AnimLibV11] " + nm + " " + st.Note() + " score " + q0.score.ToString("0") + "->" + q1.score.ToString("0") + " slide " + q0.slidePct.ToString("0") + "->" + q1.slidePct.ToString("0") + " disp " + q0.plantDisp.ToString("0.0") + "->" + q1.plantDisp.ToString("0.0") + " roll " + q0.plantRoll.ToString("0.0") + "->" + q1.plantRoll.ToString("0.0") + " yaw " + q0.plantYaw.ToString("0") + "->" + q1.plantYaw.ToString("0") + " rt " + rtMax.ToString("0.000") + "/" + rtMean.ToString("0.0000") + " hips " + (drift * 100f).ToString("0.0") + "cm corr " + (maxCorr * 100f).ToString("0.0") + "cm spd " + maxCorrSpd.ToString("0.00") + " flips " + flips);
                }
                catch (Exception ex) { Debug.LogWarning("[AnimLibV11] " + nm + ": " + ex); }
            }
            rig.Dispose();
            File.WriteAllText(OutDir + "/v11_iktest.csv", sb.ToString());
            AnimLibBatch.Done();
        }
    }
}
