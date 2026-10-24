using System;
using System.Collections.Generic;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0 ANIMLIB: the foot's own axes (captured from the bind pose of the rig, like FootPlant does at runtime), plant detection on sampled frames and the per-plant leg metrics
    /// (ankle displacement, foot yaw drift, ankle roll, foot rotation pops) that the foot IK bake and the QA use.
    /// </summary>
    public static class FootAxes
    {
        public static bool Ready;
        public static readonly Vector3[] Lat = new Vector3[2], Fwd = new Vector3[2];      // per side (0 left, 1 right): the foot bone's lateral / long axis in its own frame

        /// <summary>Creates the Medium rig and captures the foot axes before anything is sampled.</summary>
        public static AnimRig NewRig()
        {
            var rig = AnimRig.CreateMedium(); if (rig == null) return null;
            if (!Ready) Capture(rig);
            return rig;
        }

        public static void Capture(AnimRig rig)
        {
            try { rig.Anim.Rebind(); } catch { }
            Vector3 bf = Vector3.Cross(rig.T[AnimRig.RUL].position - rig.T[AnimRig.LUL].position, Vector3.up); bf.y = 0f;
            if (bf.sqrMagnitude < 1e-4f) { bf = rig.Go.transform.forward; bf.y = 0f; }
            bf.Normalize();
            for (int s = 0; s < 2; s++)
            {
                var f = rig.T[s == 0 ? AnimRig.LF : AnimRig.RF]; var inv = Quaternion.Inverse(f.rotation);
                Lat[s] = inv * Vector3.Cross(Vector3.up, bf); Fwd[s] = inv * bf;
            }
            Ready = true;
            Debug.Log("[FootAxes] captured; ankle y " + rig.T[AnimRig.LF].position.y.ToString("0.000") + " / " + rig.T[AnimRig.RF].position.y.ToString("0.000") + " toe y " + rig.T[AnimRig.LT].position.y.ToString("0.000"));
        }

        /// <summary>World rotation of a foot lying flat on the turf with its toes along the given yaw (degrees about +Y from +Z).</summary>
        public static Quaternion LevelRot(int side, float yawDeg)
        {
            Vector3 wf = Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward;
            Vector3 lf = Fwd[side], ll = Lat[side]; Vector3 lu = Vector3.Cross(lf, ll).normalized;
            Vector3 lf2 = Vector3.Cross(ll, lu).normalized;      // re-orthogonalised long axis
            return Quaternion.LookRotation(wf, Vector3.up) * Quaternion.Inverse(Quaternion.LookRotation(lf2, lu));
        }

        /// <summary>Angle (deg) between the foot's sole normal and the world up: a foot lying on its side or upside down (a sack, a fall) is not levelled.</summary>
        public static float Tilt(int side, Quaternion rot)
        {
            Vector3 lu = Vector3.Cross(Fwd[side], Lat[side]).normalized;
            return Vector3.Angle(rot * lu, Vector3.up);
        }

        public static float YawOfFoot(int side, Quaternion rot)
        {
            float y; return FootPlant.YawOf(rot, Lat[side], Vector3.forward, out y) ? y : float.NaN;
        }

        public static float RollOfFoot(int side, Quaternion rot)
        {
            float r; Vector3 fw; return FootPlant.RollOf(rot, Lat[side], Fwd[side], out r, out fw) ? r : float.NaN;
        }
    }

    public struct Plant { public int side, i0, i1; }

    public static class Plants
    {
        /// <summary>Sole height of both feet per frame (same proxy as ClipQa.Sole).</summary>
        public static float[][] Soles(List<AnimRig.Frame> fr)
        {
            var s = new float[2][]; s[0] = new float[fr.Count]; s[1] = new float[fr.Count];
            for (int i = 0; i < fr.Count; i++) { s[0][i] = ClipQa.Sole(fr[i], true); s[1][i] = ClipQa.Sole(fr[i], false); }
            return s;
        }

        public static float Ground(float[][] sole)
        {
            int N = sole[0].Length; var lows = new List<float>(N);
            for (int i = 0; i < N; i++) lows.Add(Mathf.Min(sole[0][i], sole[1][i]));
            lows.Sort(); return lows[(int)(lows.Count * 0.10f)];
        }

        /// <summary>Contact intervals: the sole within 4 cm of the ground without a vertical speed (same rule as the QA), at least 3 frames.</summary>
        public static List<Plant> Find(float[][] sole, float ground, float dt, int minLen = 3)
        {
            var list = new List<Plant>(); int N = sole[0].Length;
            for (int side = 0; side < 2; side++)
            {
                var low = new bool[N];
                for (int i = 1; i < N - 1; i++) low[i] = sole[side][i] < ground + 0.04f && Mathf.Abs((sole[side][i + 1] - sole[side][i - 1]) / (2f * dt)) < 0.6f;
                int i0 = 1;
                while (i0 < N - 1)
                {
                    if (!low[i0]) { i0++; continue; }
                    int i1 = i0; while (i1 + 1 < N - 1 && low[i1 + 1]) i1++;
                    if (i1 - i0 + 1 >= minLen) list.Add(new Plant { side = side, i0 = i0, i1 = i1 });
                    i0 = i1 + 1;
                }
            }
            return list;
        }
    }

    public struct CoreStat { public float disp, yaw, roll, toe; }

    public static class LegMetrics
    {
        /// <summary>Metrics over given core frames (side, first, last): mean ankle displacement from the plant's mean (cm), mean foot yaw drift (deg), mean ankle roll while the foot is flat (deg), mean toe-out of the foot against the pelvis heading (deg, abs).
        /// The frames need the feet, toes and upper-leg positions (the hip line gives the pelvis heading).</summary>
        public static CoreStat CoreMetrics(List<int[]> cores, List<AnimRig.Frame> fr, float ground)
        {
            double dispSum = 0, yawSum = 0, rollSum = 0, toeSum = 0, wsum = 0, rollW = 0;
            foreach (var cr in cores)
            {
                int side = cr[0], a = cr[1], b = Mathf.Min(cr[2], fr.Count - 1); int fi = side == 0 ? AnimRig.LF : AnimRig.RF, ti = side == 0 ? AnimRig.LT : AnimRig.RT;
                Vector2 mean = Vector2.zero; int c = 0;
                for (int i = a; i <= b; i++) { mean += new Vector2(fr[i].P[fi].x, fr[i].P[fi].z); c++; }
                if (c == 0) continue; mean /= c;
                float d = 0f, ymin = 1e9f, ymax = -1e9f, prevY = float.NaN, accY = 0f, toe = 0f; int toeN = 0;
                for (int i = a; i <= b; i++)
                {
                    d = Mathf.Max(d, Vector2.Distance(mean, new Vector2(fr[i].P[fi].x, fr[i].P[fi].z)));
                    float y = FootAxes.YawOfFoot(side, fr[i].R[fi]);
                    if (!float.IsNaN(y))
                    {
                        if (float.IsNaN(prevY)) accY = y; else accY += Mathf.DeltaAngle(prevY, y); prevY = y; ymin = Mathf.Min(ymin, accY); ymax = Mathf.Max(ymax, accY);
                        Vector3 hl = fr[i].P[AnimRig.RUL] - fr[i].P[AnimRig.LUL]; hl.y = 0f;
                        if (hl.sqrMagnitude > 1e-6f) { float py = AnimRig.YawOf(Vector3.Cross(hl.normalized, Vector3.up)); toe += Mathf.Abs(Mathf.DeltaAngle(py, y)); toeN++; }
                    }
                    float r = FootAxes.RollOfFoot(side, fr[i].R[fi]);
                    bool flat = fr[i].P[ti].y - 0.023f < ground + 0.03f && fr[i].P[fi].y - 0.097f < ground + 0.03f;
                    if (!float.IsNaN(r) && flat) { rollSum += Mathf.Abs(r); rollW += 1; }
                }
                float w = b - a + 1; dispSum += d * 100f * w; yawSum += (ymax > ymin ? ymax - ymin : 0f) * w; wsum += w; if (toeN > 0) toeSum += toe / toeN * w;
            }
            return new CoreStat { disp = wsum > 0 ? (float)(dispSum / wsum) : 0f, yaw = wsum > 0 ? (float)(yawSum / wsum) : 0f, roll = rollW > 0 ? (float)(rollSum / rollW) : 0f, toe = wsum > 0 ? (float)(toeSum / wsum) : 0f };
        }

        public static List<int[]> Cores(List<Plant> plants)
        {
            var cores = new List<int[]>();
            foreach (var p in plants) { bool longp = p.i1 - p.i0 + 1 >= 4; cores.Add(new[] { p.side, longp ? p.i0 + 1 : p.i0, longp ? p.i1 - 1 : p.i1 }); }
            return cores;
        }

        /// <summary>Fills the per-plant leg metrics of a QA row (plants as the QA finds them): ankle displacement over the planted core (cm), foot yaw drift (deg), ankle roll while flat (deg), toe-out against the pelvis (deg), foot rotation pops (>50 deg in one frame).</summary>
        public static void Measure(QaRow r, List<AnimRig.Frame> fr, float[][] sole, float ground, float dt)
        {
            int N = fr.Count; if (N < 5 || !FootAxes.Ready) return;
            var plants = Plants.Find(sole, ground, dt);
            var c = CoreMetrics(Cores(plants), fr, ground);
            r.plantN = plants.Count; r.plantDisp = c.disp; r.plantYaw = c.yaw; r.plantRoll = c.roll; r.plantToe = c.toe;
            int pops = 0;
            for (int s = 0; s < 2; s++) { int fi = s == 0 ? AnimRig.LF : AnimRig.RF; for (int i = 1; i < N; i++) if (Quaternion.Angle(fr[i - 1].R[fi], fr[i].R[fi]) > 50f) pops++; }
            r.footPops = pops;
        }
    }
}
