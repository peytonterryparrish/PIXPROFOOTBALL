using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.10.0 ANIMLIB cleanup passes on a Humanoid muscle sequence, each one driven by a measurement (ClipQa):
    ///   torso   - spine/chest twist and bend, head turn: the excess over the human range is compressed
    ///   wrist   - wrist and forearm limits, forearm twist flips
    ///   hands   - none of the mocap has finger data (every finger muscle is 0 = straight board hands): a hand pose is authored per situation (relaxed, running fist, ball grip, open reach, wrap)
    ///   feet    - planted feet are locked to the ground by correcting the root travel (no slide), the soles never go under the turf
    /// Apply returns a note; the caller re-measures and keeps the result only when the QA score did not get worse.
    /// </summary>
    public static class ClipPolish
    {
        // ------------------------------------------------------------------------------------------------ driver
        public sealed class Result { public string notes = ""; public bool feet, torso, wrist, hands; public float travelBefore, travelAfter; public QaRow before, after; }

        /// <summary>
        /// Polishes seq in place (the passes are accepted one by one, only when the QA score does not get worse) and returns the QA rows before and after.
        /// </summary>
        public static Result Run(AnimRig rig, ref PoseSeq seq, bool inPlace)
        {
            var res = new Result();
            var q0 = ClipQa.Analyse(rig, seq);
            // an arms-out lead-in / tail (the calibration pose that stayed in the take, or a retarget glitch): the take starts where the arms come in, ends where they go out
            if (!ClipQa.IsPairedName(seq.Name) && !ClipQa.ArmsOutIsFine(seq.Name) && (q0.armsLead >= 0.1f || q0.armsTail >= 0.2f))
            {
                float dur = seq.Duration;
                float t0 = q0.armsLead >= 0.1f ? q0.armsLead + 0.07f : 0f, t1 = q0.armsTail >= 0.2f ? dur - q0.armsTail - 0.03f : dur;
                if (t1 - t0 >= 0.6f)
                {
                    var tr = seq.Trim(t0, t1); tr.Name = seq.Name; tr.Loop = seq.Loop; tr.InPlace = seq.InPlace;
                    if (t0 > 0f) tr.RebaseRoot(false);
                    seq = tr; res.notes += " trim:" + t0.ToString("0.00") + "+" + (dur - t1).ToString("0.00") + "s"; res.torso = true;
                    q0 = ClipQa.Analyse(rig, seq);
                }
            }
            res.before = q0;
            // hands first (they cannot make the pose worse), then the wrist limits, then the torso limit: each one is accepted on its own when the score does not drop
            {
                var s1 = seq.Clone(); s1.Name = seq.Name; var r1 = new Result();
                var clip = s1.ToClip(s1.Name);
                List<AnimRig.Frame> fr;
                try { fr = rig.Sample(clip, 0f, (s1.N - 1) / s1.Rate, s1.Rate); } finally { UnityEngine.Object.DestroyImmediate(clip); }
                Hands(s1, fr, r1);
                CopyInto(s1, seq); res.hands = r1.hands; res.notes += r1.notes;       // always kept: every library clip has straight board hands today
                q0 = ClipQa.Analyse(rig, seq);
            }
            foreach (int pass in new[] { 0, 1, 2 })
            {
                var s1 = seq.Clone(); s1.Name = seq.Name; var r1 = new Result();
                if (pass == 0) Wrist(s1, r1); else if (pass == 1) Torso(s1, r1); else if (q0.popFrames >= 2f) { if (Pops(s1, 0.28f) > 0) { r1.torso = true; r1.notes += " pops"; } }
                if (!(r1.wrist || r1.torso)) continue;
                var q1 = ClipQa.Analyse(rig, s1);
                if (q1.score >= q0.score - 0.5f && (pass < 2 || q1.popFrames < q0.popFrames)) { CopyInto(s1, seq); q0 = q1; res.wrist |= r1.wrist; res.torso |= r1.torso; res.notes += r1.notes; }
                else res.notes += (pass == 0 ? " wrist" : " torso") + "-pass:rejected(" + q0.score.ToString("0") + "->" + q1.score.ToString("0") + ")";
            }
            // floor level and heading (the import faults): a global shift of the body height / a turn about the vertical, accepted when the score does not drop
            if (q0.fFloor || q0.fYaw)
            {
                var s3 = seq.Clone(); s3.Name = seq.Name; bool did = false;
                if (q0.fFloor)
                {
                    var clipF = s3.ToClip(s3.Name); List<AnimRig.Frame> frF;
                    try { frF = rig.Sample(clipF, 0f, (s3.N - 1) / s3.Rate, s3.Rate); } finally { UnityEngine.Object.DestroyImmediate(clipF); }
                    float minHand = 9f; foreach (var fq in frF) minHand = Mathf.Min(minHand, Mathf.Min(fq.P[AnimRig.LH].y, fq.P[AnimRig.RH].y));
                    float shift = q0.groundCm / 100f;               // + floats, - sinks
                    if (shift > 0f && minHand - shift < 0.02f) shift = Mathf.Max(0f, minHand - 0.02f);   // never push a hand-down stance into the turf
                    if (Mathf.Abs(shift) > 0.01f) { float g = ClipCleaner.BodyGain(rig); for (int i = 0; i < s3.N; i++) s3.RT[i].y -= shift / g; did = true; res.notes += " floor:" + (shift * 100f).ToString("0.0") + "cm"; }
                }
                if (q0.fYaw) { s3.RotateYaw(-q0.yaw0); did = true; res.notes += " yaw:" + (-q0.yaw0).ToString("0") + "deg"; }
                if (did)
                {
                    var q3 = ClipQa.Analyse(rig, s3);
                    if (q3.score >= q0.score - 0.5f) { CopyInto(s3, seq); q0 = q3; res.torso = true; }
                    else res.notes += " floor/yaw:rejected";
                }
            }
            // foot pass: only for clips that travel, with a measured slide / ground fault
            if (!inPlace && (q0.fSlide || q0.fGround) && !q0.treadmill)
            {
                var s2 = seq.Clone(); s2.Name = seq.Name;
                var r2 = new Result();
                FootLock(rig, s2, r2);
                if (r2.feet)
                {
                    var q2 = ClipQa.Analyse(rig, s2);
                    bool better = q2.score > q0.score + 0.3f || (q2.slidePct < q0.slidePct - 8f && q2.score >= q0.score - 0.5f);
                    if (better) { CopyInto(s2, seq); q0 = q2; res.feet = true; res.travelBefore = r2.travelBefore; res.travelAfter = r2.travelAfter; res.notes += r2.notes + " slide " + res.before.slidePct.ToString("0") + "->" + q2.slidePct.ToString("0") + "%"; }
                    else res.notes += " feet:no-gain(" + q0.slidePct.ToString("0") + "->" + q2.slidePct.ToString("0") + "%)";
                }
                else res.notes += r2.notes;
            }
            res.after = q0;
            return res;
        }

        static void CopyInto(PoseSeq from, PoseSeq to)
        {
            for (int m = 0; m < from.M.Length; m++) Array.Copy(from.M[m], to.M[m], from.N);
            Array.Copy(from.RT, to.RT, from.N); Array.Copy(from.RQ, to.RQ, from.N);
        }

        // ------------------------------------------------------------------------------------------------ torso / head
        static void Torso(PoseSeq seq, Result res)
        {
            int changed = 0;
            changed += Hyper(seq);
            changed += Limit(seq, new[] { "Spine Twist Left-Right", "Chest Twist Left-Right", "UpperChest Twist Left-Right" }, 50f);      // BodySanity allows 46 for a throw
            changed += Limit(seq, new[] { "Spine Front-Back", "Chest Front-Back", "UpperChest Front-Back" }, 95f);
            changed += Limit(seq, new[] { "Spine Left-Right", "Chest Left-Right", "UpperChest Left-Right" }, 70f);
            changed += Limit(seq, new[] { "Neck Turn Left-Right", "Head Turn Left-Right" }, 80f);
            if (changed > 0) { res.torso = true; res.notes += " torso:" + changed; }
        }

        /// <summary>Bone pops: no body muscle may jump more than limit (muscle units) between two frames; a forward and a backward rate limiter are averaged, so a one or two frame jump becomes a short ramp.</summary>
        public static int Pops(PoseSeq seq, float limit)
        {
            int changed = 0;
            for (int m = 0; m < Mathf.Min(55, seq.M.Length); m++)
            {
                var v = seq.M[m]; int n = v.Length; if (n < 4) continue;
                var f = (float[])v.Clone(); var b = (float[])v.Clone();
                for (int i = 1; i < n; i++) f[i] = Mathf.Clamp(v[i], f[i - 1] - limit, f[i - 1] + limit);
                for (int i = n - 2; i >= 0; i--) b[i] = Mathf.Clamp(v[i], b[i + 1] - limit, b[i + 1] + limit);
                for (int i = 0; i < n; i++) { float x = 0.5f * (f[i] + b[i]); if (Mathf.Abs(x - v[i]) > 0.02f) changed++; v[i] = x; }
            }
            return changed;
        }

        /// <summary>Last resort for a clip that is still flagged on a live key: tighter torso / head limits, the pop limiter and the knee / elbow clamp, kept when the score does not drop by more than 3 and the pops do not increase.</summary>
        public static string Force(AnimRig rig, PoseSeq seq)
        {
            var q0 = ClipQa.Analyse(rig, seq);
            var s1 = seq.Clone(); s1.Name = seq.Name;
            Hyper(s1);
            Limit(s1, new[] { "Spine Twist Left-Right", "Chest Twist Left-Right", "UpperChest Twist Left-Right" }, 36f);
            Limit(s1, new[] { "Spine Front-Back", "Chest Front-Back", "UpperChest Front-Back" }, 80f);
            Limit(s1, new[] { "Neck Turn Left-Right", "Head Turn Left-Right" }, 70f);
            Pops(s1, 0.16f);
            var q1 = ClipQa.Analyse(rig, s1);
            string note = "pops " + q0.popFrames.ToString("0") + "->" + q1.popFrames.ToString("0") + " waist " + q0.waistTwist.ToString("0") + "->" + q1.waistTwist.ToString("0") + " torso " + q0.torsoTwist.ToString("0") + "->" + q1.torsoTwist.ToString("0") + " score " + q0.score.ToString("0") + "->" + q1.score.ToString("0");
            if (q1.score >= q0.score - 3f && q1.popFrames <= q0.popFrames) { CopyInto(s1, seq); return "forced: " + note; }
            return "kept: " + note;
        }

        /// <summary>A straight knee / elbow is muscle +1: nothing may go beyond +1.05 (the Hermite tangents of a simplified clip can overshoot the cleaner's limit).</summary>
        static int Hyper(PoseSeq seq)
        {
            int n = 0;
            foreach (string mn in new[] { "Left Lower Leg Stretch", "Right Lower Leg Stretch", "Left Forearm Stretch", "Right Forearm Stretch" })
            {
                int m = ClipQa.MuscleIndex(mn); if (m < 0) continue;
                for (int f = 0; f < seq.N; f++) if (seq.M[m][f] > 1.04f) { seq.M[m][f] = 1.04f; n++; }
            }
            return n;
        }

        /// <summary>The summed joint angle of the muscles may not exceed limit degrees: the excess is compressed (x0.35) and every muscle scaled by the same factor.</summary>
        public static int Limit(PoseSeq seq, string[] muscles, float limit)
        {
            var idx = muscles.Select(ClipQa.MuscleIndex).Where(i => i >= 0).ToArray();
            int n = 0;
            for (int f = 0; f < seq.N; f++)
            {
                float sum = 0f; foreach (int m in idx) sum += ClipQa.Deg(m, seq.M[m][f]);
                float a = Mathf.Abs(sum);
                if (a <= limit) continue;
                float target = limit + 0.35f * (a - limit);
                float k = target / a;
                foreach (int m in idx) seq.M[m][f] *= k;
                n++;
            }
            return n;
        }

        // ------------------------------------------------------------------------------------------------ wrist / forearm
        static void Wrist(PoseSeq seq, Result res)
        {
            int changed = 0;
            foreach (string side in new[] { "Left", "Right" })
            {
                foreach (string mn in new[] { side + " Hand Down-Up", side + " Hand In-Out" })
                {
                    int m = ClipQa.MuscleIndex(mn); if (m < 0) continue;
                    for (int f = 0; f < seq.N; f++) { float v = seq.M[m][f]; if (Mathf.Abs(v) > 1f) { seq.M[m][f] = Mathf.Sign(v) * (1f + 0.15f * (Mathf.Abs(v) - 1f)); changed++; } }
                    Smooth(seq.M[m], 0.05f * seq.Rate);
                }
                int ft = ClipQa.MuscleIndex(side + " Forearm Twist In-Out");
                if (ft >= 0)
                {
                    for (int f = 0; f < seq.N; f++) { float v = seq.M[ft][f]; if (Mathf.Abs(v) > 1.05f) { seq.M[ft][f] = Mathf.Sign(v) * (1f + 0.2f * (Mathf.Abs(v) - 1f)); changed++; } }
                    // a twist flip (one frame jumps by > 45 degrees) is spread over 5 frames
                    float lim = 45f / 90f;
                    for (int f = 1; f < seq.N; f++)
                        if (Mathf.Abs(seq.M[ft][f] - seq.M[ft][f - 1]) > lim) { Smooth(seq.M[ft], 2.5f); changed++; break; }
                }
            }
            if (changed > 0) { res.wrist = true; res.notes += " wrist:" + changed; }
        }

        static void Smooth(float[] v, float sigmaFrames)
        {
            if (sigmaFrames < 0.3f) return;
            int rad = Mathf.CeilToInt(sigmaFrames * 3f); var w = new float[2 * rad + 1]; float sum = 0f;
            for (int k = -rad; k <= rad; k++) { w[k + rad] = Mathf.Exp(-0.5f * k * k / (sigmaFrames * sigmaFrames)); sum += w[k + rad]; }
            var o = new float[v.Length];
            for (int i = 0; i < v.Length; i++) { float a = 0f; for (int k = -rad; k <= rad; k++) { int j = Mathf.Clamp(i + k, 0, v.Length - 1); a += v[j] * w[k + rad]; } o[i] = a / sum; }
            Array.Copy(o, v, v.Length);
        }

        // ------------------------------------------------------------------------------------------------ hands
        public enum Hand { Relaxed, Run, Grip, Open, Wrap, Flat }
        // per pose: prox, mid, dist stretch values for index, middle, ring, little; index spread; thumb1, thumb2, thumb3 stretch; thumb spread
        // (muscle value -1 = fully curled, 0 = straight, see AnimLibProbe: "Left Index 1 Stretched" -1 puts the fingertip closest to the wrist)
        static readonly float[][] Poses =
        {
            /* Relaxed */ new[] { -0.20f, -0.26f, -0.32f, -0.38f,   -0.26f, -0.32f, -0.38f, -0.44f,   -0.16f, -0.22f, -0.26f, -0.30f,    0.04f,  -0.05f, -0.12f, -0.10f,  0.0f },
            /* Run     */ new[] { -0.52f, -0.58f, -0.62f, -0.66f,   -0.60f, -0.66f, -0.70f, -0.74f,   -0.40f, -0.46f, -0.50f, -0.54f,    0.00f,  -0.20f, -0.30f, -0.25f,  0.0f },
            /* Grip    */ new[] { -0.34f, -0.40f, -0.44f, -0.48f,   -0.44f, -0.50f, -0.52f, -0.54f,   -0.30f, -0.34f, -0.36f, -0.38f,    0.00f,  -0.10f, -0.22f, -0.18f,  0.05f },      // fingers together around the ball
            /* Open    */ new[] { 0.30f, 0.30f, 0.26f, 0.22f,       0.22f, 0.22f, 0.18f, 0.16f,       0.14f, 0.14f, 0.12f, 0.10f,         0.22f,   0.15f, 0.10f, 0.10f,    0.2f },      // straight is about +0.4 (probe), a relaxed open hand a little under it
            /* Wrap    */ new[] { -0.46f, -0.50f, -0.54f, -0.58f,   -0.58f, -0.62f, -0.64f, -0.66f,   -0.40f, -0.44f, -0.46f, -0.48f,    0.00f,  -0.15f, -0.25f, -0.20f,  0.0f },
            /* Flat    */ new[] { 0.38f, 0.38f, 0.34f, 0.30f,       0.30f, 0.30f, 0.28f, 0.26f,       0.20f, 0.20f, 0.18f, 0.16f,         0.22f,   0.20f, 0.15f, 0.15f,    0.2f },
        };
        static readonly string[] Fingers = { "Index", "Middle", "Ring", "Little" };

        enum Role { Generic, Throw, QbHold, QbReady, Catch, Tackle, Block, StiffArm, Carry, Handoff, Stance, RunLike }

        static bool Has(string n, params string[] t) { foreach (string s in t) if (n.Contains(s)) return true; return false; }

        static Role RoleOf(string name)
        {
            string n = name.ToLowerInvariant();
            bool qb = Has(n, "qb_", "quarterback", "bones_qb");
            if (qb && Has(n, "pass", "throw", "pump", "toss", "shovel", "hail", "sidearm", "offbal")) return Role.Throw;
            if (qb && Has(n, "undercenter", "under_center", "ready", "shotgun", "cadence", "snap", "stance", "sneak")) return Role.QbReady;
            if (qb && Has(n, "drop", "scramble", "run", "sidestep", "hesitation", "lookaround", "slide", "kneel", "fake")) return Role.QbHold;
            if (Has(n, "catch", "intnormal", "interception", "touchback", "swatnormal")) return Role.Catch;
            if (Has(n, "tackle", "sack", "bighit", "collision", "strip")) return Role.Tackle;
            if (Has(n, "stiff")) return Role.StiffArm;
            if (Has(n, "handoff")) return Role.Handoff;
            if (Has(n, "block", "shove", "swim", "bull", "rip", "shed", "wrong_arm", "press_jam", "oline", "olm_", "trench", "dline", "pocket")) return Role.Block;
            if (Has(n, "stance")) return Role.Stance;
            if (Has(n, "_bc", "bc_", "carrier", "juke", "truck", "spin", "dive", "hurdle", "avoid", "dodge", "fumble", "mob1", "ankle")) return Role.Carry;
            if (Has(n, "run", "sprint", "jog", "shuffle", "backpedal", "strafe", "cb_", "slowdown", "turn", "transition")) return Role.RunLike;
            return Role.Generic;
        }

        static float[] Series(int n, float v) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }

        /// <summary>Only the hand pass (used when the authored poses were re-tuned): samples the clip, rewrites the finger muscles.</summary>
        public static void HandsOnly(AnimRig rig, PoseSeq seq)
        {
            var clip = seq.ToClip(seq.Name); List<AnimRig.Frame> fr;
            try { fr = rig.Sample(clip, 0f, (seq.N - 1) / seq.Rate, seq.Rate); } finally { UnityEngine.Object.DestroyImmediate(clip); }
            Hands(seq, fr, new Result());
        }

        static void Hands(PoseSeq seq, List<AnimRig.Frame> fr, Result res)
        {
            int N = seq.N; if (fr.Count < N) return;
            Role role = RoleOf(seq.Name);
            float dt = 1f / seq.Rate;
            // geometry
            Func<int, int, Vector3> P = (i, b) => fr[Mathf.Min(i, fr.Count - 1)].P[b];
            int[] hand = { AnimRig.LH, AnimRig.RH };
            var speed = new float[2][]; var chestDist = new float[2][]; var hy = new float[2][]; var headY = new float[N];
            for (int s = 0; s < 2; s++) { speed[s] = new float[N]; chestDist[s] = new float[N]; hy[s] = new float[N]; }
            for (int i = 0; i < N; i++)
            {
                headY[i] = P(i, AnimRig.Head).y;
                for (int s = 0; s < 2; s++)
                {
                    Vector3 rel = P(i, hand[s]) - P(i, AnimRig.Hips); Vector3 relP = P(Mathf.Max(0, i - 1), hand[s]) - P(Mathf.Max(0, i - 1), AnimRig.Hips);
                    speed[s][i] = (rel - relP).magnitude / dt; hy[s][i] = P(i, hand[s]).y;
                    chestDist[s][i] = Vector3.Distance(P(i, hand[s]), P(i, AnimRig.Chest));
                }
            }
            // key moments
            int peakI = 0; float pk = -1f; int peakSide = 1;
            for (int i = (int)(N * 0.12f); i < (int)(N * 0.92f); i++) for (int s = 0; s < 2; s++) if (speed[s][i] > pk) { pk = speed[s][i]; peakI = i; peakSide = s; }
            int reachI = N / 3; float minD = 9f;
            for (int i = (int)(N * 0.12f); i < (int)(N * 0.78f); i++) { float d = Vector3.Distance(P(i, AnimRig.LH), P(i, AnimRig.RH)); if (d < minD) { minD = d; reachI = i; } }
            int tuck = 0; { float a = 0f, b = 0f; for (int i = 0; i < N; i++) { a += chestDist[0][i]; b += chestDist[1][i]; } tuck = a <= b ? 0 : 1; }
            // mode weights per side: [mode][frame]
            var w = new float[2][][];
            for (int s = 0; s < 2; s++) { w[s] = new float[Poses.Length][]; for (int m = 0; m < Poses.Length; m++) w[s][m] = new float[N]; }
            Action<int, int, int, int, float> set = (s, m, from, to, v) => { for (int i = Mathf.Max(0, from); i < Mathf.Min(N, to); i++) w[s][m][i] = v; };
            for (int s = 0; s < 2; s++)
            {
                int other = 1 - s;
                switch (role)
                {
                    case Role.Throw:
                        if (s == peakSide) { set(s, (int)Hand.Grip, 0, peakI - 1, 1f); set(s, (int)Hand.Open, peakI - 1, peakI + (int)(0.12f * seq.Rate), 1f); set(s, (int)Hand.Relaxed, peakI + (int)(0.12f * seq.Rate), N, 1f); }
                        else { set(s, (int)Hand.Grip, 0, Mathf.Max(1, peakI - (int)(0.25f * seq.Rate)), 1f); set(s, (int)Hand.Relaxed, Mathf.Max(1, peakI - (int)(0.25f * seq.Rate)), N, 1f); }
                        break;
                    case Role.QbHold: set(s, (int)Hand.Grip, 0, N, 1f); break;
                    case Role.QbReady: set(s, (int)Hand.Open, 0, N, 0.55f); set(s, (int)Hand.Relaxed, 0, N, 0.45f); break;
                    case Role.Catch:
                        set(s, (int)Hand.Open, 0, Mathf.Max(1, reachI - (int)(0.05f * seq.Rate)), 1f); set(s, (int)Hand.Grip, Mathf.Max(1, reachI - (int)(0.05f * seq.Rate)), N, 1f); break;
                    case Role.Tackle:
                        set(s, (int)Hand.Open, 0, peakI, 1f); set(s, (int)Hand.Wrap, peakI, N, 1f); break;
                    case Role.Block:
                        set(s, (int)Hand.Open, 0, peakI + (int)(0.1f * seq.Rate), 1f); set(s, (int)Hand.Grip, peakI + (int)(0.1f * seq.Rate), N, 0.8f); set(s, (int)Hand.Open, peakI + (int)(0.1f * seq.Rate), N, 0.2f); break;
                    case Role.StiffArm:
                        if (s == peakSide) set(s, (int)Hand.Flat, 0, N, 1f); else set(s, (int)Hand.Grip, 0, N, 1f); break;
                    case Role.Handoff:
                        set(s, (int)Hand.Grip, 0, peakI, 1f); set(s, (int)Hand.Relaxed, peakI, N, 1f); break;
                    case Role.Carry:
                        if (s == tuck) set(s, (int)Hand.Grip, 0, N, 1f); else set(s, (int)Hand.Run, 0, N, 1f); break;
                    case Role.RunLike: set(s, (int)Hand.Run, 0, N, 1f); break;
                    case Role.Stance: set(s, (int)Hand.Relaxed, 0, N, 1f); break;
                    default: set(s, (int)Hand.Relaxed, 0, N, 1f); break;
                }
                // any hand on the turf is a flat spread hand; a hand held above the head is open
                for (int i = 0; i < N; i++)
                {
                    float flat = 0f; if (hy[s][i] < 0.22f) flat = 1f; else if (hy[s][i] < 0.34f) flat = (0.34f - hy[s][i]) / 0.12f;
                    float up = hy[s][i] > headY[i] + 0.12f ? 1f : 0f;
                    float over = Mathf.Max(flat, up * (role == Role.Throw || role == Role.Catch ? 0f : 1f));
                    if (over > 0f)
                    {
                        for (int m = 0; m < Poses.Length; m++) w[s][m][i] *= 1f - over;
                        w[s][(int)(flat >= up ? Hand.Flat : Hand.Open)][i] += over;
                    }
                }
                // normalise + smooth each weight track
                for (int m = 0; m < Poses.Length; m++) Smooth(w[s][m], 0.09f * seq.Rate);
                for (int i = 0; i < N; i++) { float sum = 0f; for (int m = 0; m < Poses.Length; m++) sum += w[s][m][i]; if (sum < 1e-4f) { w[s][(int)Hand.Relaxed][i] = 1f; sum = 1f; } for (int m = 0; m < Poses.Length; m++) w[s][m][i] /= sum; }
            }
            // write the muscles
            int written = 0;
            for (int s = 0; s < 2; s++)
            {
                string side = s == 0 ? "Left " : "Right ";
                for (int fi = 0; fi < 4; fi++)
                    for (int j = 0; j < 3; j++)
                    {
                        int mi = ClipQa.MuscleIndex(side + Fingers[fi] + " " + (j + 1) + " Stretched"); if (mi < 0) continue;
                        for (int i = 0; i < N; i++) { float v = 0f; for (int m = 0; m < Poses.Length; m++) v += w[s][m][i] * Poses[m][j * 4 + fi]; seq.M[mi][i] = v; }
                        written++;
                    }
                int sp = ClipQa.MuscleIndex(side + "Index Spread");
                string[] spreadNames = { "Index Spread", "Middle Spread", "Ring Spread", "Little Spread" };
                float[] spreadK = { 1f, 0.2f, -0.5f, -0.9f };
                for (int fi = 0; fi < 4; fi++)
                {
                    int ms = ClipQa.MuscleIndex(side + spreadNames[fi]); if (ms < 0) continue;
                    for (int i = 0; i < N; i++) { float v = 0f; for (int m = 0; m < Poses.Length; m++) v += w[s][m][i] * Poses[m][12]; seq.M[ms][i] = v * spreadK[fi]; }
                }
                string[] th = { "Thumb 1 Stretched", "Thumb 2 Stretched", "Thumb 3 Stretched", "Thumb Spread" };
                for (int t = 0; t < 4; t++)
                {
                    int mt = ClipQa.MuscleIndex(side + th[t]); if (mt < 0) continue;
                    for (int i = 0; i < N; i++) { float v = 0f; for (int m = 0; m < Poses.Length; m++) v += w[s][m][i] * Poses[m][13 + t]; seq.M[mt][i] = v; }
                }
            }
            res.hands = true; res.notes += " hands:" + role;
        }

        // ------------------------------------------------------------------------------------------------ feet
        static float[] _map; // world(x,z) = M * (rt.x, rt.z)  ->  m00 m01 m10 m11, plus the body gain
        static float _gain;

        /// <summary>World displacement of the hips per unit of RootT.x / RootT.z (the avatar scale and the instance rotation), measured with two probe clips.</summary>
        static void Calibrate(AnimRig rig)
        {
            if (_map != null) return;
            var m = new float[4];
            for (int ax = 0; ax < 2; ax++)
            {
                var s = PoseSeq.Alloc(31, 30f, "probe" + ax);
                for (int i = 0; i < 31; i++) { s.RQ[i] = Quaternion.identity; s.RT[i] = new Vector3(ax == 0 ? i / 30f : 0f, 0.5f, ax == 1 ? i / 30f : 0f); }
                var c = s.ToClip(s.Name);
                var fr = rig.Sample(c, 0f, 1f, 30f);
                UnityEngine.Object.DestroyImmediate(c);
                Vector3 d = fr[fr.Count - 1].P[AnimRig.Hips] - fr[0].P[AnimRig.Hips];
                m[ax == 0 ? 0 : 1] = d.x; m[ax == 0 ? 2 : 3] = d.z;     // columns: response to rt.x and rt.z
            }
            // m[0] = dx/drtx, m[2] = dz/drtx, m[1] = dx/drtz, m[3] = dz/drtz
            _map = m;
            Debug.Log("[ClipPolish] root map: dx/drtx=" + m[0].ToString("0.000") + " dz/drtx=" + m[2].ToString("0.000") + " dx/drtz=" + m[1].ToString("0.000") + " dz/drtz=" + m[3].ToString("0.000"));
        }

        static Vector2 WorldToRoot(Vector2 w)
        {
            float a = _map[0], b = _map[1], c = _map[2], d = _map[3];
            float det = a * d - b * c; if (Mathf.Abs(det) < 1e-5f) return Vector2.zero;
            return new Vector2((d * w.x - b * w.y) / det, (-c * w.x + a * w.y) / det);
        }

        static float Soft(float x, float a, float b) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        /// <summary>
        /// Keeps the planted foot still on the ground: where a foot is on the turf its horizontal velocity is removed from the root (cumulated, smoothed, bounded), so the root travel
        /// follows the steps instead of the other way round. Then lifts the body where a sole goes under the turf.
        /// </summary>
        static void FootLock(AnimRig rig, PoseSeq seq, Result res)
        {
            Calibrate(rig);
            res.travelBefore = Travel(seq);
            Vector3[] rt0 = (Vector3[])seq.RT.Clone();
            for (int pass = 0; pass < 3; pass++)
            {
                var clip = seq.ToClip(seq.Name);
                List<AnimRig.Frame> fr;
                try { fr = rig.Sample(clip, 0f, (seq.N - 1) / seq.Rate, seq.Rate); } finally { UnityEngine.Object.DestroyImmediate(clip); }
                int N = Mathf.Min(fr.Count, seq.N); float dt = 1f / seq.Rate;
                var sole = new float[2][]; sole[0] = new float[N]; sole[1] = new float[N];
                for (int i = 0; i < N; i++)
                {
                    sole[0][i] = ClipQa.Sole(fr[i], true); sole[1][i] = ClipQa.Sole(fr[i], false);
                }
                var cv = new Vector2[N];
                int[] foot = { AnimRig.LF, AnimRig.RF };
                for (int i = 1; i < N - 1; i++)
                {
                    float wsum = 0f; Vector2 acc = Vector2.zero;
                    for (int s = 0; s < 2; s++)
                    {
                        float wt = 1f - Soft(sole[s][i], 0.02f, 0.075f);
                        float vy = (sole[s][i + 1] - sole[s][i - 1]) / (2f * dt);
                        wt *= 1f - Soft(Mathf.Abs(vy), 0.35f, 0.9f);
                        if (wt <= 0.02f) continue;
                        Vector3 v = (fr[i + 1].P[foot[s]] - fr[i - 1].P[foot[s]]) / (2f * dt);
                        acc += wt * new Vector2(v.x, v.z); wsum += wt;
                    }
                    if (wsum > 0.3f) { Vector2 c = -acc / wsum; float mag = c.magnitude; if (mag > 1.6f) c *= 1.6f / mag; cv[i] = c * Mathf.Min(1f, wsum); }
                }
                // smooth the correction velocity, integrate into a world offset, and blend the mean out so the clip's net travel is not rewritten wholesale
                var cx = new float[N]; var cz = new float[N];
                for (int i = 0; i < N; i++) { cx[i] = cv[i].x; cz[i] = cv[i].y; }
                Smooth(cx, 1.5f); Smooth(cz, 1.5f);
                float ox = 0f, oz = 0f;
                for (int i = 1; i < N; i++)
                {
                    ox += cx[i] * dt * 0.95f; oz += cz[i] * dt * 0.95f;
                    Vector2 r = WorldToRoot(new Vector2(ox, oz));
                    seq.RT[i].x += r.x; seq.RT[i].z += r.y;
                }
            }
            // sink fix: lift where the sole is below the turf
            {
                var clip = seq.ToClip(seq.Name);
                List<AnimRig.Frame> fr;
                try { fr = rig.Sample(clip, 0f, (seq.N - 1) / seq.Rate, seq.Rate); } finally { UnityEngine.Object.DestroyImmediate(clip); }
                int N = Mathf.Min(fr.Count, seq.N);
                var lift = new float[N]; bool any = false;
                for (int i = 0; i < N; i++)
                {
                    float low = Mathf.Min(ClipQa.Sole(fr[i], true), ClipQa.Sole(fr[i], false));
                    if (low < -0.012f) { lift[i] = -low - 0.012f; any = true; }
                }
                if (any)
                {
                    Smooth(lift, 1.5f);
                    float g = ClipCleaner.BodyGain(rig);
                    for (int i = 0; i < N; i++) seq.RT[i].y += lift[i] / g;
                }
            }
            res.travelAfter = Travel(seq);
            // refuse to rewrite the travel of the clip by more than 35 percent: restore
            if (res.travelBefore > 0.3f && Mathf.Abs(res.travelAfter - res.travelBefore) > 0.35f * res.travelBefore)
            {
                Array.Copy(rt0, seq.RT, rt0.Length); res.notes += " feet:skipped(travel " + res.travelBefore.ToString("0.0") + "->" + res.travelAfter.ToString("0.0") + ")"; return;
            }
            res.feet = true; res.notes += " feet:locked(" + res.travelBefore.ToString("0.00") + "->" + res.travelAfter.ToString("0.00") + "m)";
        }

        static float Travel(PoseSeq s)
        {
            Vector3 d = s.RT[s.N - 1] - s.RT[0]; d.y = 0f; return d.magnitude;
        }
    }
}
