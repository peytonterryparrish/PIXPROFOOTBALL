using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PixFootball.Art;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>Per-clip cleanup recipe.</summary>
    public sealed class CleanPlan
    {
        public float TrimStart = -1f, TrimEnd = -1f;      // explicit seconds (<0 = none)
        public bool AutoTrim = true;                        // drop dead time / T-pose frames at the ends
        public bool FindLoop;                               // cut the best seamless cycle out of the take
        public float LoopMin = 0.5f, LoopMax = 1.6f;        // cycle length window (seconds)
        public bool Loop;                                   // result is a loop (closes the seam)
        public bool InPlace;                                // lock root XZ
        public bool FaceForward = true;                     // rotate so frame 0 faces +Z
        public float Despike = 0.2f;                        // muscle units
        public float Sigma = 0.03f, HandSigma = 0.09f;      // seconds
        public bool Clamp = true;
        public bool GroundFix = true;                       // lift / sink upright takes so the planted foot touches y = 0
        public bool Segment;                                // long raw take: cut out its active segments (each saved as <name>_sNN)
        public bool AlsoMirror;                             // also write a left/right mirrored copy <name>_M
        public float Speed = 1f;
        public bool Mirror;
        public float Rate = 30f;
        public string Note = "";
        public bool KeepRootFrame;                          // paired takes: both actors share one authored frame, never re-base
        public List<Vector2> Windows;                       // v0.10.0: explicit (start, end) segments for a long take (both actors of a pair are cut with the same windows)
        public bool Polish = true;                          // v0.10.0 ANIMLIB: QA-driven passes (torso / wrist limits, authored hands, foot lock) with a per-clip score
        public CleanPlan Copy() { return (CleanPlan)MemberwiseClone(); }
    }

    /// <summary>
    /// Mocap cleanup in Humanoid muscle space: trim (dead time, T-pose frames), despike, Gaussian jitter filter, human-limit clamp (hyperextension), loop cycle detection + seam
    /// closing, root rebase / in-place lock, mirroring, retiming. Output goes to Assets/_Licensed/DraftFever/Animations/Clean/ (originals are kept untouched).
    /// </summary>
    public static class ClipCleaner
    {
        public const string CleanDir = "Assets/_Licensed/DraftFever/Animations/Clean";
        static readonly string[] HandMuscles = { "Thumb", "Index", "Middle", "Ring", "Little", "Hand" };
        static readonly string[] HyperMuscles = { "Lower Leg Stretch", "Forearm Stretch" };
        static readonly string[] FingerMuscles = { "Thumb", "Index", "Middle", "Ring", "Little" };

        public static string SafeName(string n)
        {
            string s = n; int bar = s.LastIndexOf('|'); if (bar >= 0) s = s.Substring(bar + 1);
            return Regex.Replace(s, @"[^A-Za-z0-9_\-\.]+", "_");
        }

        public static string PathOf(string clipName) { return CleanDir + "/" + SafeName(clipName) + ".anim"; }

        // ---------------------------------------------------------------------------------------------------- plan
        static readonly string[] InPlaceWords = { "idle", "stance", "cadence", "neutral", "lookaround", "cheer", "celebrat", "freestyle", "kneel", "hike", "snap_to", "huddle", "bench" };

        public static CleanPlan PlanFor(string clipName, float length, bool isLoopLike)
        {
            string n = clipName.ToLowerInvariant();
            var p = new CleanPlan();
            p.Loop = isLoopLike;
            p.InPlace = InPlaceWords.Any(w => n.Contains(w)) && !n.Contains("transition") && !n.Contains("_to_");
            p.FaceForward = true;
            bool cycle = n.Contains("loop") || n.Contains("cycle");
            if (cycle && length > 3f) { p.FindLoop = true; p.LoopMin = 0.45f; p.LoopMax = 1.4f; }
            // a take named "loop" that runs for a minute is a held pose with breathing, not a 1 s cycle: keep a 2.5-8 s cut (a 0.4 s loop of a near-static pose looks frozen)
            if (cycle && length > 30f && !n.Contains("run") && !n.Contains("jog") && !n.Contains("sprint")) { p.FindLoop = true; p.LoopMin = 2.5f; p.LoopMax = 8f; }
            if (p.InPlace && p.Loop && length > 12f) { p.FindLoop = true; p.LoopMin = 3f; p.LoopMax = 9f; }
            if (p.Loop) p.AutoTrim = false;
            if (n.Contains("hit") || n.Contains("tackle") || n.Contains("sack") || n.Contains("bighit") || n.Contains("truck") || n.Contains("catch") || n.Contains("block") || n.Contains("pass") || n.Contains("throw"))
            { p.Sigma = 0.02f; p.Despike = 0.28f; }
            // two-actor takes must keep their authored relative timing: no trimming (ContactTable timings stay valid)
            if (Regex.IsMatch(n, @"(_a|_b)$") || n.StartsWith("tackle") || n.StartsWith("sack") || n.StartsWith("bighit") || n.StartsWith("collision")) { p.AutoTrim = false; p.FaceForward = false; p.KeepRootFrame = true; p.Note = "paired: no trim"; }
            return p;
        }

        // ---------------------------------------------------------------------------------------------------- run
        public sealed class Result { public float groundFix; public float heading, headingSpread; public string name, path, note; public float lenIn, lenOut; public int spikes, clamped; public float t0, t1; public QaRow qaBefore, qaAfter; public string polish = ""; }

        public static Result Clean(AnimRig rig, AnimationClip clip, CleanPlan plan, string outName = null)
        {
            var res = new Result { name = clip.name, lenIn = clip.length };
            float rate = plan.Rate;
            var seq = PoseSeq.FromClip(clip, 0f, Mathf.Min(clip.length, 120f), rate * 2f);       // oversample (60) for filtering, decimated on write
            seq.Name = outName ?? clip.name;
            seq.Loop = plan.Loop;
            float hipRatio = BodyGain(rig);     // metres of hip height per unit of RootT.y (the avatar scale), measured with a probe clip
            // 1) filters first so trimming sees clean data
            res.spikes = seq.Despike(plan.Despike);
            seq.Gaussian(plan.Sigma, null, HandMuscles);
            seq.Gaussian(plan.HandSigma, HandMuscles, null);
            if (plan.Clamp) res.clamped = seq.LimitMuscles(HyperMuscles, FingerMuscles);
            // 2) trim
            float t0 = 0f, t1 = seq.Duration;
            if (plan.TrimStart >= 0f) t0 = plan.TrimStart;
            if (plan.TrimEnd > 0f) t1 = Mathf.Min(t1, plan.TrimEnd);
            if (plan.AutoTrim && seq.Duration > 1.2f && plan.TrimStart < 0f && plan.TrimEnd <= 0f)
            {
                AutoTrimRange(seq, rig, clip, out t0, out t1);
            }
            if (!plan.KeepRootFrame && !plan.AutoTrim)
            {
                // segments cut from a long raw take (and explicit windows) still must not open / close in the calibration T-pose
                float te, tb; TposeEdges(rig, clip, out te, out tb);
                if (te > 0.05f && t0 < te + 0.08f) t0 = te + 0.08f;
                if (tb < clip.length && t1 > tb - 0.05f) t1 = Mathf.Max(t0 + 0.4f, tb - 0.05f);
            }
            if (t1 - t0 < 0.3f) { t0 = 0f; t1 = seq.Duration; }
            seq = seq.Trim(t0, t1);
            res.t0 = t0; res.t1 = t1;
            float winA = t0, winB = t1;     // original-clip window that the result is made of
            // 3) loop cut
            if (plan.FindLoop && seq.N > 20)
            {
                int bestS, bestL; float cost;
                FindLoop(seq, plan.LoopMin, plan.LoopMax, out bestS, out bestL, out cost);
                if (bestL > 0)
                {
                    winA = t0 + bestS / seq.Rate; winB = winA + bestL / seq.Rate;
                    seq = PoseSeq.MakeLoop(seq, bestS, bestL, Mathf.Min(8, bestS));
                    res.note += " loop[" + (bestS / seq.Rate).ToString("0.00") + "+" + (bestL / seq.Rate).ToString("0.00") + " c=" + cost.ToString("0.000") + "]";
                }
            }
            else if (plan.Loop && !plan.InPlace) { seq.CloseSeam(Mathf.RoundToInt(0.2f * seq.Rate)); }
            else if (plan.Loop) seq.CloseSeam(Mathf.RoundToInt(0.35f * seq.Rate));
            // 3b) ground contact, measured on the part of the ORIGINAL take that is really kept (a long stance take has standing / T-pose parts with a different ground reference)
            if (plan.GroundFix)
            {
                bool upright; float dy = GroundOffset(rig, clip, winA, winB, out upright);
                if (upright && Mathf.Abs(dy) > 0.025f && Mathf.Abs(dy) < 0.35f) { for (int i = 0; i < seq.N; i++) seq.RT[i].y -= dy / hipRatio; res.groundFix = dy; }
            }
            // 4) root: turn the whole take so that the body heading (hip line, measured on the real skeleton) at the start is +Z, like the STN ClipFacingTable expects; paired takes and spins keep their authored frame
            seq.InPlace = plan.InPlace;
            if (plan.FaceForward)
            {
                float h = MeasureHeading(rig, clip, winA, out res.headingSpread);
                res.heading = h;
                if (Mathf.Abs(h) > 0.5f) seq.RotateYaw(-h);
            }
            if (!plan.KeepRootFrame) seq.RebaseRoot(false);
            if (plan.Mirror) seq = plan.FaceForward ? seq.MirrorAbout(-PixFootball.Gameplay.PlayerAvatar.ModelYaw) : seq.MirrorAboutBody();     // the heading frame of a corrected take is the game +Z
            if (Mathf.Abs(plan.Speed - 1f) > 0.01f) seq = seq.Retime(plan.Speed);
            seq.Loop = plan.Loop || (plan.FindLoop);
            seq = seq.Resample(rate);
            res.lenOut = seq.Duration;
            if (plan.Polish && seq.N > 8 && seq.Duration < 40f)
            {
                try
                {
                    var pr = ClipPolish.Run(rig, ref seq, plan.InPlace || plan.KeepRootFrame);
                    res.qaBefore = pr.before; res.qaAfter = pr.after; res.polish = pr.notes;
                }
                catch (Exception ex) { Debug.LogWarning("[ClipCleaner] polish failed for " + clip.name + ": " + ex.Message); }
            }
            return res.Save(seq);
        }

        public static string TaggedName(string name, string tag)
        {
            foreach (string suf in new[] { "_AB", "_CO", "_A", "_B" })
                if (name.EndsWith(suf)) return name.Substring(0, name.Length - suf.Length) + tag + suf;
            return name + tag;
        }

        /// <summary>Cleans a clip into one or several assets: the clip itself, its active segments (long raw takes) and the mirrored copies.</summary>
        public static List<Result> CleanClip(AnimRig rig, AnimationClip clip, CleanPlan plan)
        {
            var list = new List<Result>();
            if (plan.Segment)
            {
                var segs = plan.Windows ?? FindSegments(clip, 0.9f, 10f, 4);
                int n = 0;
                foreach (var sg in segs)
                {
                    var p = plan.Copy(); p.Segment = false; p.TrimStart = sg.x; p.TrimEnd = sg.y; p.AutoTrim = false; p.Loop = false; p.FindLoop = false;
                    string nm = TaggedName(clip.name, "_s" + (++n).ToString("00"));
                    list.Add(Clean(rig, clip, p, nm));
                    if (plan.AlsoMirror) { var pm = p.Copy(); pm.Mirror = true; list.Add(Clean(rig, clip, pm, TaggedName(nm, "_M"))); }
                }
                return list;
            }
            list.Add(Clean(rig, clip, plan));
            if (plan.AlsoMirror) { var pm = plan.Copy(); pm.Mirror = true; list.Add(Clean(rig, clip, pm, TaggedName(clip.name, "_M"))); }
            return list;
        }

        /// <summary>Active (moving) stretches of a long raw take: (start, end) seconds, separated by rest gaps, each at most maxLen long.</summary>
        public static List<Vector2> FindSegments(AnimationClip clip, float minLen, float maxLen, int maxCount)
        {
            var s = PoseSeq.FromClip(clip, 0f, Mathf.Min(clip.length, 120f), 20f);
            int N = s.N; var e = new float[N];
            for (int i = 1; i < N; i++) { float a = 0f; for (int m = 0; m < 55; m++) a += Mathf.Abs(s.M[m][i] - s.M[m][i - 1]); Vector3 d = s.RT[i] - s.RT[i - 1]; d.y = 0f; e[i] = a + d.magnitude * s.Rate * 0.6f; }
            var sm = new float[N]; int w = 4;
            for (int i = 0; i < N; i++) { float a = 0f; int c = 0; for (int k = -w; k <= w; k++) { int j = i + k; if (j < 1 || j >= N) continue; a += e[j]; c++; } sm[i] = c > 0 ? a / c : 0f; }
            var sorted = (float[])sm.Clone(); Array.Sort(sorted);
            float thr = Mathf.Max(0.5f, sorted[(int)(N * 0.85f)] * 0.18f);
            var segs = new List<Vector2>(); int st = -1, lastActive = -1; int gap = Mathf.RoundToInt(1.0f * s.Rate);
            for (int i = 0; i < N; i++)
            {
                bool act = sm[i] > thr;
                if (act) { if (st < 0) st = i; lastActive = i; }
                else if (st >= 0 && i - lastActive > gap) { Add(segs, st, lastActive, s.Rate, minLen, maxLen); st = -1; }
            }
            if (st >= 0) Add(segs, st, lastActive, s.Rate, minLen, maxLen);
            if (segs.Count > maxCount) segs = segs.GetRange(0, maxCount);
            return segs;
        }

        static void Add(List<Vector2> segs, int a, int b, float rate, float minLen, float maxLen)
        {
            float t0 = Mathf.Max(0f, a / rate - 0.25f), t1 = b / rate + 0.35f;
            if (t1 - t0 < minLen) return;
            // very long activity (several repetitions in a row): cut it into maxLen pieces
            while (t1 - t0 > maxLen) { segs.Add(new Vector2(t0, t0 + maxLen)); t0 += maxLen; }
            if (t1 - t0 >= minLen) segs.Add(new Vector2(t0, t1));
        }

        /// <summary>Writes a processed sequence as Clean/NAME.anim (same simplification as the cleaner).</summary>
        public static string WriteSeq(PoseSeq seq, float tol = 0.015f, float legTol = -1f)
        {
            var clip = seq.ToClip(seq.Name);
            Simplify(clip, tol, legTol);
            Directory.CreateDirectory(CleanDir);
            string path = PathOf(seq.Name);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); }
            else AssetDatabase.CreateAsset(clip, path);
            return path;
        }

        static Result Save(this Result res, PoseSeq seq)
        {
            var clip = seq.ToClip(seq.Name);
            Simplify(clip, 0.015f);
            Directory.CreateDirectory(CleanDir);
            string path = PathOf(seq.Name);
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (existing != null) { EditorUtility.CopySerialized(clip, existing); EditorUtility.SetDirty(existing); }
            else AssetDatabase.CreateAsset(clip, path);
            res.path = path;
            return res;
        }

        /// <summary>Body heading in degrees (0 = +Z) at the start of the clip (circular mean of the first 0.6 s) and the spread of the heading over the whole clip.</summary>
        public static float MeasureHeading(AnimRig rig, AnimationClip clip, float t0, out float spread)
        {
            var fr = rig.Sample(clip, 0f, Mathf.Min(clip.length, 20f), 30f);
            float sx = 0f, sy = 0f; int n = 0;
            int a = Mathf.Clamp(Mathf.RoundToInt(t0 * 30f), 0, fr.Count - 1);
            for (int i = a; i < fr.Count && i < a + 4; i++) { float y = AnimRig.YawOf(AnimRig.Forward(fr[i])) * Mathf.Deg2Rad; sx += Mathf.Cos(y); sy += Mathf.Sin(y); n++; }
            float mean = n > 0 ? Mathf.Atan2(sy, sx) * Mathf.Rad2Deg : 0f;
            float mx = 0f;
            for (int i = 0; i < fr.Count; i += 2) mx = Mathf.Max(mx, Mathf.Abs(Mathf.DeltaAngle(mean, AnimRig.YawOf(AnimRig.Forward(fr[i])))));
            spread = mx;
            return mean;
        }

        /// <summary>Metres by which the lowest foot point of an upright take sits above the ground (10th percentile over the take, minus the resting toe height).</summary>
        public static float GroundOffset(AnimRig rig, AnimationClip clip, float tA, float tB, out bool upright)
        {
            var fr = rig.Sample(clip, Mathf.Max(0f, tA), Mathf.Min(clip.length, Mathf.Max(tA + 0.2f, tB)), 30f);
            var lows = new List<float>(); var hips = new List<float>();
            foreach (var f in fr)
            {
                lows.Add(Mathf.Min(Mathf.Min(f.P[AnimRig.LF].y, f.P[AnimRig.RF].y), Mathf.Min(f.P[AnimRig.LT].y, f.P[AnimRig.RT].y))); hips.Add(f.P[AnimRig.Hips].y);
            }
            lows.Sort(); hips.Sort();
            float hm = hips[hips.Count / 2], hx = hips[hips.Count - 1];
            upright = hm > 0.72f && hx < hm + 0.65f;
            return lows[(int)(lows.Count * 0.10f)] - 0.015f;
        }

        // ---------------------------------------------------------------------------------------------------- trimming
        static void AutoTrimRange(PoseSeq s, AnimRig rig, AnimationClip clip, out float t0, out float t1)
        {
            int N = s.N;
            var e = new float[N];
            for (int i = 1; i < N; i++)
            {
                float a = 0f;
                for (int m = 0; m < 55; m++) a += Mathf.Abs(s.M[m][i] - s.M[m][i - 1]);
                Vector3 d = s.RT[i] - s.RT[i - 1]; d.y = 0f;
                a += d.magnitude * s.Rate * 0.6f;
                e[i] = a;
            }
            // box smoothing
            var sm = new float[N]; int w = Mathf.Max(2, Mathf.RoundToInt(0.12f * s.Rate));
            for (int i = 0; i < N; i++) { float a = 0f; int c = 0; for (int k = -w; k <= w; k++) { int j = i + k; if (j < 1 || j >= N) continue; a += e[j]; c++; } sm[i] = c > 0 ? a / c : 0f; }
            var sorted = (float[])sm.Clone(); Array.Sort(sorted);
            float p90 = sorted[(int)(N * 0.9f)];
            float thr = Mathf.Max(0.35f, p90 * 0.12f);
            int a0 = 1; while (a0 < N - 2 && sm[a0] < thr) a0++;
            int b0 = N - 2; while (b0 > a0 && sm[b0] < thr) b0--;
            float lead = 0.12f, tail = 0.25f;
            t0 = Mathf.Max(0f, a0 / s.Rate - lead); t1 = Mathf.Min(s.Duration, b0 / s.Rate + tail);
            // T-pose lead-in / lead-out (calibration pose in the raw takes)
            try
            {
                var fr = rig.Sample(clip, 0f, Mathf.Min(clip.length, 60f), 30f);
                int n = fr.Count; int tp0 = 0; while (tp0 < n && AnimAudit.IsTPose(fr[tp0])) tp0++;
                int tp1 = n - 1; while (tp1 > 0 && AnimAudit.IsTPose(fr[tp1])) tp1--;
                if (tp0 > 2) t0 = Mathf.Max(t0, tp0 / 30f + 0.08f);
                if (tp1 < n - 3) t1 = Mathf.Min(t1, tp1 / 30f - 0.05f);
            }
            catch (Exception ex) { Debug.LogWarning("[ClipCleaner] T-pose detection failed for " + clip.name + ": " + ex.Message); }
        }

        static float _gain = -1f;
        /// <summary>Metres the hips move per unit of RootT.y (humanoid avatar scale of the Medium body): a probe clip (standing idle) is written twice, 0.1 apart, and evaluated on the rig.</summary>
        public static float BodyGain(AnimRig rig)
        {
            if (_gain > 0f) return _gain;
            try
            {
                var lib = ArtLibrary.Load();
                var idle = lib != null ? lib.Clip(ArtKeys.Idle) : null;
                if (idle != null)
                {
                    var s = PoseSeq.FromClip(idle, 0f, Mathf.Min(1f, idle.length), 30f);
                    var c0 = s.ToClip("gain0");
                    for (int i = 0; i < s.N; i++) s.RT[i].y += 0.1f;
                    var c1 = s.ToClip("gain1");
                    float h0 = rig.PoseAt(c0, 0.3f).P[AnimRig.Hips].y, h1 = rig.PoseAt(c1, 0.3f).P[AnimRig.Hips].y;
                    UnityEngine.Object.DestroyImmediate(c0); UnityEngine.Object.DestroyImmediate(c1);
                    float g = (h1 - h0) / 0.1f;
                    if (g > 0.2f && g < 3f) { _gain = g; Debug.Log("[ClipCleaner] body gain (hips metres per RootT.y) = " + g.ToString("0.000")); return _gain; }
                }
            }
            catch (Exception ex) { Debug.LogWarning("[ClipCleaner] gain probe failed: " + ex.Message); }
            _gain = 1f; return _gain;
        }

        /// <summary>Where the leading T-pose calibration frames end (0 = none) and where the trailing ones begin (clip length = none).</summary>
        public static void TposeEdges(AnimRig rig, AnimationClip clip, out float startEnd, out float endBegin)
        {
            startEnd = 0f; endBegin = clip.length;
            try
            {
                var fr = rig.Sample(clip, 0f, Mathf.Min(clip.length, 120f), 30f);
                int n = fr.Count; int tp0 = 0; while (tp0 < n && AnimAudit.IsTPose(fr[tp0])) tp0++;
                int tp1 = n - 1; while (tp1 > 0 && AnimAudit.IsTPose(fr[tp1])) tp1--;
                if (tp0 > 2) startEnd = tp0 / 30f;
                if (tp1 < n - 3 && clip.length <= 120f) endBegin = tp1 / 30f;
            }
            catch (Exception ex) { Debug.LogWarning("[ClipCleaner] T-pose detection failed for " + clip.name + ": " + ex.Message); }
        }

        // ---------------------------------------------------------------------------------------------------- loop finder
        public static void FindLoop(PoseSeq s, float minLen, float maxLen, out int bestStart, out int bestLen, out float bestCost)
        {
            bestStart = 0; bestLen = 0; bestCost = float.MaxValue;
            int minL = Mathf.Max(6, Mathf.RoundToInt(minLen * s.Rate)), maxL = Mathf.Min(s.N - 4, Mathf.RoundToInt(maxLen * s.Rate));
            if (maxL <= minL) return;
            int step = Mathf.Max(1, Mathf.RoundToInt(s.Rate / 30f));
            for (int L = minL; L <= maxL; L += step)
            {
                for (int i = 4; i + L < s.N - 1; i += step)
                {
                    float c = s.Dist(i, s, i + L, true);
                    // prefer cycles with constant heading change: penalise yaw difference of the body rotation
                    c += 0.002f * Mathf.Abs(Mathf.DeltaAngle(PoseSeq.Yaw(s.RQ[i]), PoseSeq.Yaw(s.RQ[i + L])));
                    c += 0.0004f * L / s.Rate;   // slight preference for the shorter cycle
                    if (c < bestCost) { bestCost = c; bestStart = i; bestLen = L; }
                }
            }
        }

        // ---------------------------------------------------------------------------------------------------- curve simplification
        public static void Simplify(AnimationClip clip, float tol, float legTol = -1f)
        {
            foreach (var b in AnimationUtility.GetCurveBindings(clip))
            {
                var c = AnimationUtility.GetEditorCurve(clip, b);
                var keys = c.keys;
                if (keys.Length <= 3) continue;
                string pn = b.propertyName;
                if (pn.StartsWith("RootQ")) continue;     // QA-923: never reduce the root quaternion components independently
                bool finger = pn.Contains("Thumb") || pn.Contains("Index") || pn.Contains("Middle") || pn.Contains("Ring") || pn.Contains("Little");
                float t = pn.StartsWith("RootT") ? 0.006f : (pn.StartsWith("RootQ") ? 0.003f : (finger ? 0.08f : ((legTol > 0f && (pn.Contains("Leg") || pn.Contains("Foot"))) ? legTol : tol)));
                var keep = new bool[keys.Length]; keep[0] = keep[keys.Length - 1] = true;
                Rdp(keys, 0, keys.Length - 1, t, keep);
                var nk = new List<Keyframe>();
                for (int i = 0; i < keys.Length; i++) if (keep[i]) nk.Add(keys[i]);
                // recompute tangents (central difference over the kept keys)
                for (int i = 0; i < nk.Count; i++)
                {
                    var k = nk[i]; int a = Mathf.Max(0, i - 1), z = Mathf.Min(nk.Count - 1, i + 1);
                    float dPrev = i > 0 ? (nk[i].value - nk[i - 1].value) / Mathf.Max(1e-5f, nk[i].time - nk[i - 1].time) : 0f, dNext = i < nk.Count - 1 ? (nk[i + 1].value - nk[i].value) / Mathf.Max(1e-5f, nk[i + 1].time - nk[i].time) : 0f;
                    float sl = i == 0 ? dNext : (i == nk.Count - 1 ? dPrev : (dPrev * dNext <= 0f ? 0f : 2f * dPrev * dNext / (dPrev + dNext)));      // v0.11: monotone (Fritsch-Butland) tangents: the Hermite curve between two kept keys never overshoots them (finger muscles saturate at +-1)
                    k.inTangent = sl; k.outTangent = sl; nk[i] = k;
                }
                AnimationUtility.SetEditorCurve(clip, b, new AnimationCurve(nk.ToArray()));
            }
        }

        static void Rdp(Keyframe[] k, int a, int b, float tol, bool[] keep)
        {
            if (b <= a + 1) return;
            float maxD = 0f; int idx = -1;
            for (int i = a + 1; i < b; i++)
            {
                float t = (k[i].time - k[a].time) / (k[b].time - k[a].time);
                float lin = Mathf.Lerp(k[a].value, k[b].value, t);
                float d = Mathf.Abs(k[i].value - lin);
                if (d > maxD) { maxD = d; idx = i; }
            }
            if (maxD > tol && idx > 0) { keep[idx] = true; Rdp(k, a, idx, tol, keep); Rdp(k, idx, b, tol, keep); }
        }
    }
}
