using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0 ANIMLIB cleanup passes that sit next to ClipPolish (each returns whether it changed the sequence; the caller keeps the result only when the QA does not get worse):
    ///   TPoseRepair - T-pose frames inside a take (a retarget glitch) are bridged between the neighbouring good frames
    ///   WristClamp  - wrist muscles hard-limited to the avatar range, forearm twist flips spread
    ///   BodyLow     - lowest point of the body over the take (soles, hands, knees, elbows, hips, spine, head, with limb radii): a take that sinks into the turf is lifted
    ///   DeadTrim    - dead time at the start / end of a single take is cut (paired takes: the same cut from both actors)
    /// </summary>
    public static class ClipPolish2
    {
        // ------------------------------------------------------------------------------------------------ T-pose
        public static int TPoseRepair(AnimRig rig, PoseSeq seq)
        {
            var clip = seq.ToClip(seq.Name); List<AnimRig.Frame> fr;
            try { fr = rig.Sample(clip, 0f, (seq.N - 1) / seq.Rate, seq.Rate); } finally { UnityEngine.Object.DestroyImmediate(clip); }
            int N = Mathf.Min(seq.N, fr.Count); var t = new bool[N]; int cnt = 0;
            for (int i = 0; i < N; i++) { t[i] = AnimAudit.IsTPose(fr[i]); if (t[i]) cnt++; }
            if (cnt == 0) return 0;
            // dilate by one frame (the neighbours of a T-pose frame are on their way into it)
            var d = (bool[])t.Clone(); for (int i = 0; i < N; i++) if (t[i]) { if (i > 0) d[i - 1] = true; if (i < N - 1) d[i + 1] = true; }
            int fixedN = 0, a = 0;
            while (a < N)
            {
                if (!d[a]) { a++; continue; }
                int b = a; while (b + 1 < N && d[b + 1]) b++;
                int l = a - 1, r = b + 1;
                if (l < 0 && r >= N) return fixedN;     // all T-pose: nothing to bridge with (the QA rejects the take)
                for (int i = a; i <= b; i++)
                {
                    float k = l < 0 ? 1f : (r >= N ? 0f : (i - l) / (float)(r - l)); int li = l < 0 ? r : l, ri = r >= N ? l : r;
                    for (int m = 0; m < seq.M.Length; m++) seq.M[m][i] = Mathf.Lerp(seq.M[m][li], seq.M[m][ri], k);
                    seq.RQ[i] = Quaternion.Slerp(seq.RQ[li], seq.RQ[ri], k);
                    Vector3 pl = seq.RT[li], pr = seq.RT[ri]; seq.RT[i] = Vector3.Lerp(pl, pr, k);
                    fixedN++;
                }
                a = b + 1;
            }
            return fixedN;
        }

        // ------------------------------------------------------------------------------------------------ wrist
        /// <summary>Wrist muscles hard-limited to +-0.99 (a value beyond 1 is a bone bent past the avatar's wrist range), the forearm twist limited to +-1.</summary>
        public static int WristClamp(PoseSeq seq)
        {
            int changed = 0;
            foreach (string side in new[] { "Left", "Right" })
            {
                foreach (string mn in new[] { side + " Hand Down-Up", side + " Hand In-Out", side + " Forearm Twist In-Out" })
                {
                    int m = ClipQa.MuscleIndex(mn); if (m < 0) continue;
                    var v = seq.M[m]; bool any = false;
                    for (int i = 0; i < seq.N; i++) if (Mathf.Abs(v[i]) > 0.99f) { any = true; break; }
                    if (!any) continue;
                    // soft shoulder first (smoothing the excess), then the hard limit
                    for (int i = 0; i < seq.N; i++) { float a = Mathf.Abs(v[i]); if (a > 0.85f) { float x = 0.85f + 0.14f * (1f - Mathf.Exp(-(a - 0.85f) / 0.14f)); v[i] = Mathf.Sign(v[i]) * Mathf.Min(x, 0.99f); changed++; } }
                }
            }
            return changed;
        }

        // ------------------------------------------------------------------------------------------------ ground
        static float Low(AnimRig.Frame f)
        {
            float m = Mathf.Min(FootLow(f, true), FootLow(f, false));
            m = Mathf.Min(m, f.P[AnimRig.LH].y - 0.045f, f.P[AnimRig.RH].y - 0.045f, f.P[AnimRig.LLL].y - 0.06f, f.P[AnimRig.RLL].y - 0.06f, f.P[AnimRig.LLA].y - 0.05f, f.P[AnimRig.RLA].y - 0.05f);
            m = Mathf.Min(m, f.P[AnimRig.Hips].y - 0.11f, f.P[AnimRig.Spine].y - 0.12f, f.P[AnimRig.Chest].y - 0.14f, f.P[AnimRig.Head].y - 0.11f, f.P[AnimRig.LUA].y - 0.06f, f.P[AnimRig.RUA].y - 0.06f);
            return m;
        }

        static float FootLow(AnimRig.Frame f, bool left)
        {
            int s = left ? 0 : 1, fi = left ? AnimRig.LF : AnimRig.RF;
            if (FootAxes.Ready && FootAxes.Tilt(s, f.R[fi]) > 45f) return Mathf.Min(f.P[left ? AnimRig.LT : AnimRig.RT].y - 0.02f, f.P[fi].y - 0.07f);      // a foot on its side / upside down: the soles are not the lowest part
            return ClipQa.Sole(f, left);
        }

        /// <summary>The 4th percentile of the lowest body point over the take (m, 0 = on the turf); null when the take is too short.</summary>
        public static float BodyLowP(List<AnimRig.Frame> fr)
        {
            if (fr.Count < 4) return 0f;
            var l = fr.Select(Low).ToList(); l.Sort();
            return l[Mathf.Min(l.Count - 1, (int)(l.Count * 0.04f))];
        }

        /// <summary>How far (m) a take has to be raised so its lowest body point is on the turf: 0 when it is fine or when the take is lying on the ground / airborne.</summary>
        public static float FloorLift(AnimRig rig, PoseSeq seq, out float hipMed)
        {
            var clip = seq.ToClip(seq.Name); List<AnimRig.Frame> fr;
            try { fr = rig.Sample(clip, 0f, (seq.N - 1) / seq.Rate, seq.Rate); } finally { UnityEngine.Object.DestroyImmediate(clip); }
            var hips = fr.Select(f => f.P[AnimRig.Hips].y).ToList(); hips.Sort(); hipMed = hips[hips.Count / 2];
            float low = BodyLowP(fr);
            if (hipMed < 0.34f) return 0f;                      // lying
            if (low > -0.035f) return 0f;
            return Mathf.Min(0.26f, -low - 0.008f);
        }

        public static void Lift(AnimRig rig, PoseSeq seq, float lift)
        {
            float g = ClipCleaner.BodyGain(rig);
            for (int i = 0; i < seq.N; i++) seq.RT[i].y += lift / g;
        }

        // ------------------------------------------------------------------------------------------------ dead time
        /// <summary>The seconds to cut from the start and from the end of a take: dead time over 0.7 s / 0.9 s, leaving 0.25 s / 0.4 s of it.</summary>
        public static void DeadCut(QaRow q, out float t0, out float t1)
        {
            t0 = q.deadStart > 0.7f ? q.deadStart - 0.25f : 0f;
            t1 = q.deadEnd > 0.9f ? q.deadEnd - 0.4f : 0f;
            if (q.length - t0 - t1 < 0.8f) { t0 = t1 = 0f; }
        }
    }
}
