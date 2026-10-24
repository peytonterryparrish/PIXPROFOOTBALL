using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0 ANIMLIB: foot lock baked into a Humanoid clip. Planted feet (the sole on the turf, little travel) are pinned to the world position where they landed by a two-bone leg IK
    /// (the knee keeps bending the way the take bends it), the flat foot is laid level on the turf with the toes on the plant's mean heading (ankle roll / yaw drift out), and the result is read back
    /// into the Humanoid leg muscles through HumanPoseHandler, so the clip stays a plain muscle clip. Plants where the foot sweeps under the body (a treadmill cycle, a cut) are not pinned, only levelled.
    /// The result is evaluated again and compared with the IK pose; frames the muscles cannot reproduce (limits) get a smaller weight and the bake runs again (up to 3 rounds).
    /// The root is not touched, so paired takes stay in sync.
    /// </summary>
    public static class FootIk
    {
        public sealed class Stat
        {
            public int plants, locked, sweeping, orientFrames, lockFrames, reachLimited, verBad, rounds, culled; public float meanErr, verPos, verRot;
            public float dispB, dispA, yawB, yawA, rollB, rollA, toeB, toeA;     // on the plants of the original take (core frames): ankle displacement cm, foot yaw drift deg, ankle roll while flat deg
            public string Note() { return "ik:" + locked + "/" + plants + " plants (" + sweeping + " sweep) orient " + orientFrames + "f reach " + reachLimited + " ver " + verPos.ToString("0.0") + "cm/" + verRot.ToString("0") + "deg bad " + verBad + " r" + rounds + " cull " + culled + " | disp " + dispB.ToString("0.0") + "->" + dispA.ToString("0.0") + " yaw " + yawB.ToString("0") + "->" + yawA.ToString("0") + " roll " + rollB.ToString("0.0") + "->" + rollA.ToString("0.0") + " toe " + toeB.ToString("0") + "->" + toeA.ToString("0"); }
        }

        public static Stat Last;
        static readonly string[] LegMuscles = { "Upper Leg Front-Back", "Upper Leg In-Out", "Upper Leg Twist In-Out", "Lower Leg Stretch", "Lower Leg Twist In-Out", "Foot Up-Down", "Foot Twist In-Out" };
        static int[][] _mi;
        static HumanPoseHandler _h; static AnimRig _hRig;

        static void Init(AnimRig rig)
        {
            if (_mi == null)
            {
                _mi = new int[2][];
                for (int s = 0; s < 2; s++)
                {
                    _mi[s] = new int[LegMuscles.Length];
                    for (int k = 0; k < LegMuscles.Length; k++) _mi[s][k] = ClipQa.MuscleIndex((s == 0 ? "Left " : "Right ") + LegMuscles[k]);
                }
            }
            if (_h == null || _hRig != rig)
            {
                if (_h != null) { try { _h.Dispose(); } catch { } }
                _h = new HumanPoseHandler(rig.Anim.avatar, rig.Anim.transform); _hRig = rig;
            }
        }

        static void SmoothW(float[] v, float sigma)
        {
            int n = v.Length, rad = Mathf.CeilToInt(sigma * 3f); var o = new float[n]; var w = new float[2 * rad + 1]; float sum = 0f;
            for (int k = -rad; k <= rad; k++) { w[k + rad] = Mathf.Exp(-0.5f * k * k / (sigma * sigma)); sum += w[k + rad]; }
            for (int i = 0; i < n; i++) { float a = 0f; for (int k = -rad; k <= rad; k++) { int j = i + k; a += (j < 0 || j >= n ? 0f : v[j]) * w[k + rad]; } o[i] = a / sum; }
            Array.Copy(o, v, n);
        }

        static int Nearest<T>(T[] a, int i, Func<T, bool> ok)
        {
            for (int d = 0; d < a.Length; d++) { int j = i - d; if (j >= 0 && ok(a[j])) return j; j = i + d; if (j < a.Length && ok(a[j])) return j; }
            return -1;
        }

        static float Soft(float x, float a, float b) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

        sealed class Plan { public float[][] wp = new float[2][], wo = new float[2][], yawT = new float[2][]; public Vector2[][] anchor = new Vector2[2][]; }

        static PlayableGraph Begin(AnimRig rig, AnimationClip clip, out AnimationClipPlayable cp)
        {
            var graph = PlayableGraph.Create("FootIk");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var output = AnimationPlayableOutput.Create(graph, "o", rig.Anim);
            cp = AnimationClipPlayable.Create(graph, clip); cp.SetApplyFootIK(false); cp.SetSpeed(1.0);
            output.SetSourcePlayable(cp);
            rig.Go.transform.position = Vector3.zero; rig.Go.transform.rotation = AnimRig.StartRot;
            cp.SetTime(0); graph.Evaluate(0f);
            return graph;
        }

        static void End(AnimRig rig, PlayableGraph graph) { graph.Destroy(); rig.Go.transform.position = Vector3.zero; rig.Go.transform.rotation = AnimRig.StartRot; }

        /// <summary>Pins the planted feet of seq (modified in place). lockPos = false: no position lock (treadmill clips), only the flat foot / heading.</summary>
        public static Stat Bake(AnimRig rig, PoseSeq seq, bool lockPos, bool orient = true, float maxSweep = 0.12f, float toeLimit = 14f)
        {
            var st = new Stat();
            if (!FootAxes.Ready) FootAxes.Capture(rig);
            Init(rig);
            int N = seq.N; float rate = seq.Rate, dt = 1f / rate;
            var clip = seq.ToClip(seq.Name);
            try
            {
                List<AnimRig.Frame> fr = rig.Sample(clip, 0f, (N - 1) / rate, rate);
                N = Mathf.Min(N, fr.Count);
                if (N < 6) return st;
                var sole = Plants.Soles(fr); float ground = Plants.Ground(sole);
                var plants = Plants.Find(sole, ground, dt);
                var plan = new Plan();
                for (int s = 0; s < 2; s++) { plan.wp[s] = new float[N]; plan.wo[s] = new float[N]; plan.anchor[s] = new Vector2[N]; plan.yawT[s] = new float[N]; for (int i = 0; i < N; i++) plan.yawT[s][i] = float.NaN; }
                var cores = new List<int[]>();     // side, first, last core frame
                foreach (var p in plants)
                {
                    int s = p.side, fi = s == 0 ? AnimRig.LF : AnimRig.RF, ti = s == 0 ? AnimRig.LT : AnimRig.RT;
                    int ca = -1, cb = -1;
                    for (int i = p.i0; i <= p.i1; i++) if (sole[s][i] < ground + 0.03f) { if (ca < 0) ca = i; cb = i; }
                    if (ca < 0 || cb - ca < 1) continue;
                    st.plants++; cores.Add(new[] { s, ca, cb });
                    Vector2 mean = Vector2.zero; int c = 0; float disp = 0f;
                    for (int i = ca; i <= cb; i++) { mean += new Vector2(fr[i].P[fi].x, fr[i].P[fi].z); c++; }
                    mean /= c;
                    for (int i = ca; i <= cb; i++) disp = Mathf.Max(disp, Vector2.Distance(mean, new Vector2(fr[i].P[fi].x, fr[i].P[fi].z)));
                    bool pin = lockPos && disp <= maxSweep;
                    if (lockPos) { if (pin) st.locked++; else st.sweeping++; }
                    // heading of the plant: held when the foot is not pivoting (yaw range under 25 degrees)
                    float y0 = float.NaN, acc = 0f, ymin = 1e9f, ymax = -1e9f, sum = 0f; int yc = 0;
                    for (int i = ca; i <= cb; i++)
                    {
                        float y = FootAxes.YawOfFoot(s, fr[i].R[fi]); if (float.IsNaN(y)) continue;
                        if (float.IsNaN(y0)) { y0 = y; acc = 0f; } else acc = Mathf.DeltaAngle(y0, y);
                        ymin = Mathf.Min(ymin, acc); ymax = Mathf.Max(ymax, acc); sum += acc; yc++;
                    }
                    bool holdYaw = yc > 1 && ymax - ymin <= 25f;
                    float meanYaw = yc > 0 ? y0 + sum / yc : 0f;
                    // toe-out against the pelvis heading: a foot turned more than 14 degrees off the hips (a retarget with the toes out / in) is brought back (30 percent of the excess stays)
                    if (holdYaw && toeLimit > 0f)
                    {
                        float sx = 0f, sz = 0f, pmin = 1e9f, pmax = -1e9f, p0 = float.NaN, upr = 0f; int upn = 0;
                        for (int i = ca; i <= cb; i++)
                        {
                            Vector3 hl = fr[i].P[AnimRig.RUL] - fr[i].P[AnimRig.LUL]; hl.y = 0f; if (hl.sqrMagnitude < 1e-6f) continue;
                            float py = AnimRig.YawOf(Vector3.Cross(hl.normalized, Vector3.up)); if (float.IsNaN(p0)) p0 = py;
                            float dd = Mathf.DeltaAngle(p0, py); pmin = Mathf.Min(pmin, dd); pmax = Mathf.Max(pmax, dd);
                            sx += Mathf.Sin(py * Mathf.Deg2Rad); sz += Mathf.Cos(py * Mathf.Deg2Rad); upr += Vector3.Dot((fr[i].P[AnimRig.Neck] - fr[i].P[AnimRig.Hips]).normalized, Vector3.up); upn++;
                        }
                        if (pmax - pmin <= 20f && (sx != 0f || sz != 0f) && upn > 0 && upr / upn > 0.8f)
                        {
                            float pel = Mathf.Atan2(sx, sz) * Mathf.Rad2Deg, rel = Mathf.DeltaAngle(pel, meanYaw), ar = Mathf.Abs(rel);
                            if (ar > toeLimit && ar <= 50f) meanYaw = pel + Mathf.Sign(rel) * (toeLimit + 0.3f * (ar - toeLimit));
                        }
                    }
                    for (int i = Mathf.Max(0, ca - 5); i <= Mathf.Min(N - 1, cb + 5); i++)
                    {
                        float d = Mathf.Max(0, Mathf.Max(ca - i, i - cb));
                        float ramp = 1f - Soft(d, 0f, 5f);
                        float hw = 1f - Soft(sole[s][i] - ground, 0.03f, 0.075f);
                        float w = ramp * hw;
                        if (w <= 0.003f) continue;
                        float flat = 1f - Soft(Mathf.Max(fr[i].P[ti].y - 0.023f, fr[i].P[fi].y - 0.097f) - ground, 0.02f, 0.05f);
                        if (pin && w > plan.wp[s][i]) { plan.wp[s][i] = w; plan.anchor[s][i] = mean; }
                        float wf = orient ? w * flat : 0f;
                        if (wf > plan.wo[s][i]) { plan.wo[s][i] = wf; plan.yawT[s][i] = holdYaw ? meanYaw : float.NaN; }
                    }
                }
                if (st.plants == 0) return st;
                Smooth(plan, N);
                bool any0 = false; for (int s = 0; s < 2; s++) for (int i = 0; i < N; i++) if (plan.wp[s][i] > 0.003f || plan.wo[s][i] > 0.003f) { any0 = true; break; }
                if (!any0) return st;
                { var cs0 = LegMetrics.CoreMetrics(cores, fr, ground); st.dispB = cs0.disp; st.yawB = cs0.yaw; st.rollB = cs0.roll; st.toeB = cs0.toe; }
                // rounds: bake, evaluate the result, lower the weight where the muscles could not reproduce the IK pose
                PoseSeq work = null; List<AnimRig.Frame> fr2 = null;
                for (int cullRound = 0; cullRound < 3; cullRound++)
                {
                for (int round = 0; round < 4; round++)
                {
                    st.rounds = round + 1; st.lockFrames = st.orientFrames = st.reachLimited = 0;
                    work = seq.Clone(); work.Name = seq.Name; work.InPlace = seq.InPlace; work.Loop = seq.Loop;
                    bool[][] touched; Vector3[][] intP; Quaternion[][] intR;
                    IkPass(rig, clip, plan, sole, N, dt, work, st, out touched, out intP, out intR);
                    var bad = Verify(rig, work, N, dt, touched, intP, intR, st, out fr2);
                    if (bad == null) break;
                    for (int s = 0; s < 2; s++) { float kf = round < 2 ? 0.3f : 0f; int nb = round < 2 ? 0 : 3; for (int i = 0; i < N; i++) if (bad[s][i]) for (int j = Mathf.Max(0, i - nb); j <= Mathf.Min(N - 1, i + nb); j++) { plan.wp[s][j] *= kf; plan.wo[s][j] *= kf; } if (round < 2) { SmoothW(plan.wp[s], 1.0f); SmoothW(plan.wo[s], 1.0f); } }
                }
                // per plant: a plant that came out worse than it went in (slide, heading, roll, toe-out) goes back to the take's own motion; the others keep the lock
                bool culled = false;
                if (fr2 != null && cullRound < 2)
                    foreach (var cr in cores)
                    {
                        var one = new List<int[]> { cr };
                        var b0 = LegMetrics.CoreMetrics(one, fr, ground); var a0 = LegMetrics.CoreMetrics(one, fr2, ground);
                        if (a0.disp > b0.disp + 0.4f || a0.yaw > b0.yaw + 5f || a0.toe > b0.toe + 2.5f || a0.roll > b0.roll + 1f)
                        {
                            int s = cr[0]; bool any = false;
                            for (int i = Mathf.Max(0, cr[1] - 6); i <= Mathf.Min(N - 1, cr[2] + 6); i++) { if (plan.wp[s][i] > 0f || plan.wo[s][i] > 0f) any = true; plan.wp[s][i] = 0f; plan.wo[s][i] = 0f; }
                            if (any) { culled = true; st.culled++; }
                        }
                    }
                if (!culled) break;
                }
                for (int m = 0; m < work.M.Length; m++) Array.Copy(work.M[m], seq.M[m], seq.N);
                if (fr2 != null) { var cs1 = LegMetrics.CoreMetrics(cores, fr2, ground); st.dispA = cs1.disp; st.yawA = cs1.yaw; st.rollA = cs1.roll; st.toeA = cs1.toe; }
                return st;
            }
            finally { UnityEngine.Object.DestroyImmediate(clip); rig.Go.transform.position = Vector3.zero; rig.Go.transform.rotation = AnimRig.StartRot; }
        }

        static void Smooth(Plan plan, int N)
        {
            for (int s = 0; s < 2; s++)
            {
                SmoothW(plan.wp[s], 1.3f); SmoothW(plan.wo[s], 1.3f);
                for (int i = 0; i < N; i++)
                {
                    if (plan.wp[s][i] > 0.003f && plan.anchor[s][i] == Vector2.zero) { int j = Nearest(plan.anchor[s], i, v => v != Vector2.zero); if (j >= 0) plan.anchor[s][i] = plan.anchor[s][j]; else plan.wp[s][i] = 0f; }
                    if (plan.wo[s][i] > 0.003f && float.IsNaN(plan.yawT[s][i])) { int j = Nearest(plan.yawT[s], i, v => !float.IsNaN(v)); if (j >= 0 && Mathf.Abs(j - i) <= 3) plan.yawT[s][i] = plan.yawT[s][j]; }
                }
            }
        }

        /// <summary>One IK round: evaluates the original clip frame by frame, solves the legs of the planned frames and reads the leg muscles back into work.</summary>
        static void IkPass(AnimRig rig, AnimationClip clip, Plan plan, float[][] sole, int N, float dt, PoseSeq work, Stat st, out bool[][] touched, out Vector3[][] intP, out Quaternion[][] intR)
        {
            touched = new bool[2][]; intP = new Vector3[2][]; intR = new Quaternion[2][]; _dM = new[] { new float[N], new float[N] };
            for (int s = 0; s < 2; s++) { touched[s] = new bool[N]; intP[s] = new Vector3[N]; intR[s] = new Quaternion[N]; }
            AnimationClipPlayable cp; var graph = Begin(rig, clip, out cp);
            double errSum = 0; int errN = 0;
            try
            {
                var pose = new HumanPose();
                var newM = new float[2][][];
                for (int s = 0; s < 2; s++) { newM[s] = new float[LegMuscles.Length][]; for (int k = 0; k < LegMuscles.Length; k++) newM[s][k] = new float[N]; }
                for (int i = 0; i < N; i++)
                {
                    if (i > 0) graph.Evaluate(dt);
                    bool did = false;
                    for (int s = 0; s < 2; s++)
                    {
                        float wpi = plan.wp[s][i], woi = plan.wo[s][i];
                        if (woi > 0.003f) woi *= 1f - Soft(FootAxes.Tilt(s, rig.T[s == 0 ? AnimRig.LF : AnimRig.RF].rotation), 25f, 45f);
                        if (wpi < 0.02f) wpi = 0f; if (woi < 0.02f) woi = 0f; if (wpi <= 0.003f && woi <= 0.003f) continue;
                        Transform U = rig.T[s == 0 ? AnimRig.LUL : AnimRig.RUL], L = rig.T[s == 0 ? AnimRig.LLL : AnimRig.RLL], F = rig.T[s == 0 ? AnimRig.LF : AnimRig.RF];
                        Quaternion origR = F.rotation; Vector3 A = F.position;
                        if (wpi > 0.003f)
                        {
                            // the pin lets go of a foot that is far from its anchor (a swing foot passing the plant)
                            float dist = Vector2.Distance(new Vector2(A.x, A.z), plan.anchor[s][i]);
                            float w = wpi * (1f - Soft(dist, 0.09f, 0.16f));
                            if (w > 0.003f)
                            {
                                float dy = Mathf.Clamp(sole[s][i], -0.05f, 0.05f);
                                Vector3 goal = new Vector3(plan.anchor[s][i].x, A.y - dy, plan.anchor[s][i].y);
                                Vector3 T = Vector3.Lerp(A, goal, w);
                                bool lim; Solve(U, L, F, T, rig, out lim);
                                if (lim) st.reachLimited++;
                                errSum += (F.position - T).magnitude; errN++; st.lockFrames++;
                                F.rotation = origR;
                            }
                        }
                        if (woi > 0.003f)
                        {
                            float yaw = plan.yawT[s][i]; if (float.IsNaN(yaw)) yaw = FootAxes.YawOfFoot(s, origR);
                            if (!float.IsNaN(yaw)) { F.rotation = Quaternion.Slerp(F.rotation, FootAxes.LevelRot(s, yaw), woi); st.orientFrames++; }
                        }
                        touched[s][i] = true; intP[s][i] = F.position; intR[s][i] = F.rotation; did = true;
                    }
                    if (did)
                    {
                        _h.GetHumanPose(ref pose);
                        for (int s = 0; s < 2; s++)
                        {
                            if (!touched[s][i]) continue;
                            for (int k = 0; k < LegMuscles.Length; k++) { int mi = _mi[s][k]; if (mi >= 0) { _dM[s][i] = Mathf.Max(_dM[s][i], Mathf.Abs(pose.muscles[mi] - Mathf.Clamp(work.M[mi][i], -1.05f, 1.05f))); work.M[mi][i] = pose.muscles[mi]; } }
                        }
                    }
                }
            }
            finally { End(rig, graph); }
            st.meanErr = errN > 0 ? (float)(errSum / errN) * 100f : 0f;
        }

        /// <summary>Evaluates work and compares the feet at the touched frames with the IK pose; returns the frames that are off (null when none) and the feet frames for the metrics.</summary>
        static float[][] _dM;
        static bool[][] Verify(AnimRig rig, PoseSeq work, int N, float dt, bool[][] touched, Vector3[][] intP, Quaternion[][] intR, Stat st, out List<AnimRig.Frame> frames)
        {
            frames = new List<AnimRig.Frame>(N);
            var clip2 = work.ToClip(work.Name); AnimationClipPlayable cp; var graph = Begin(rig, clip2, out cp);
            bool[][] bad = null; st.verBad = 0; st.verPos = 0f; st.verRot = 0f;
            try
            {
                for (int i = 0; i < N; i++)
                {
                    if (i > 0) graph.Evaluate(dt);
                    var f = new AnimRig.Frame { P = new Vector3[AnimRig.NB], R = new Quaternion[AnimRig.NB], t = i * dt };
                    foreach (int b in new[] { AnimRig.LF, AnimRig.RF, AnimRig.LT, AnimRig.RT, AnimRig.LUL, AnimRig.RUL }) { f.P[b] = rig.T[b].position; f.R[b] = rig.T[b].rotation; }
                    frames.Add(f);
                    for (int s = 0; s < 2; s++)
                    {
                        if (!touched[s][i]) continue;
                        var F = rig.T[s == 0 ? AnimRig.LF : AnimRig.RF];
                        float dp = (F.position - intP[s][i]).magnitude * 100f, dr = Quaternion.Angle(F.rotation, intR[s][i]);
                        st.verPos = Mathf.Max(st.verPos, dp); st.verRot = Mathf.Max(st.verRot, dr);
                        if (dp > 2f || dr > 10f || (_dM != null && _dM[s][i] > 0.5f)) { if (bad == null) bad = new[] { new bool[N], new bool[N] }; bad[s][i] = true; st.verBad++; }
                    }
                }
            }
            finally { End(rig, graph); UnityEngine.Object.DestroyImmediate(clip2); }
            return bad;
        }

        /// <summary>Bake into a clone and keep the result (copied into seq) only when the leg metrics improve and nothing else gets worse. note describes what happened.</summary>
        public static bool Try(AnimRig rig, PoseSeq seq, QaRow q0, out QaRow q1, out string note)
        {
            q1 = null; note = "";
            if (!FootAxes.Ready || seq.N < 8) return false;
            var s = seq.Clone(); s.Name = seq.Name; s.InPlace = seq.InPlace; s.Loop = seq.Loop;
            var st = Bake(rig, s, !q0.treadmill); Last = st;
            note = st.Note();
            if (st.lockFrames == 0 && st.orientFrames == 0) { note = "ik:none"; return false; }
            q1 = ClipQa.Analyse(rig, s);
            bool better = q1.slidePct < q0.slidePct - 3f || st.dispA < st.dispB - 0.8f || st.rollA < st.rollB - 1f || st.toeA < st.toeB - 2f;
            var why = new List<string>();
            if (!better) why.Add("not-better");
            if (q1.score < q0.score - 0.5f) why.Add("score" + q0.score.ToString("0") + ">" + q1.score.ToString("0"));
            if (q1.popFrames > q0.popFrames) why.Add("pops");
            if (q1.footPops > q0.footPops) why.Add("footpops");
            if (q1.kneeHyper > Mathf.Max(q0.kneeHyper, 8f)) why.Add("knee");
            if (st.dispA > st.dispB + 1f) why.Add("disp");
            if (st.yawA > st.yawB + 5f) why.Add("yaw");
            if (st.toeA > st.toeB + 2f) why.Add("toe");
            if (st.rollA > st.rollB + 0.5f) why.Add("roll");
            if (q1.groundCm < q0.groundCm - 3f) why.Add("ground");
            if (st.verBad > 2) why.Add("verify");
            bool ok = why.Count == 0;
            if (!ok) { note += " REJECTED[" + string.Join(",", why) + "]"; return false; }
            for (int m = 0; m < s.M.Length; m++) Array.Copy(s.M[m], seq.M[m], s.N);
            return true;
        }

        /// <summary>QA of the muscle read-back: max and mean |difference| of the leg muscles (muscle units) between the clip and HumanPoseHandler.GetHumanPose of the evaluated skeleton.</summary>
        public static void RoundTrip(AnimRig rig, PoseSeq seq, out float maxDiff, out float meanDiff)
        {
            Init(rig);
            int N = seq.N; float dt = 1f / seq.Rate;
            var clip = seq.ToClip(seq.Name); double sum = 0; int cnt = 0; maxDiff = 0f;
            AnimationClipPlayable cp; var graph = Begin(rig, clip, out cp);
            try
            {
                var pose = new HumanPose();
                for (int i = 0; i < N; i++)
                {
                    if (i > 0) graph.Evaluate(dt);
                    _h.GetHumanPose(ref pose);
                    for (int s = 0; s < 2; s++) for (int k = 0; k < LegMuscles.Length; k++) { int mi = _mi[s][k]; if (mi < 0) continue; float d = Mathf.Abs(pose.muscles[mi] - seq.M[mi][i]); maxDiff = Mathf.Max(maxDiff, d); sum += d; cnt++; }
                }
            }
            finally { End(rig, graph); UnityEngine.Object.DestroyImmediate(clip); }
            meanDiff = cnt > 0 ? (float)(sum / cnt) : 0f;
        }

        /// <summary>Two-bone IK: moves the ankle F of the chain U (hip) - L (knee) - F to the world point T keeping the knee bending in its current plane. Rotates U and L only.</summary>
        static void Solve(Transform U, Transform L, Transform F, Vector3 T, AnimRig rig, out bool limited)
        {
            limited = false;
            Vector3 H = U.position, K = L.position, A = F.position;
            float l1 = (K - H).magnitude, l2 = (A - K).magnitude;
            Vector3 d = T - H; float dist = d.magnitude;
            float maxR = (l1 + l2) * 0.992f, minR = Mathf.Abs(l1 - l2) + 0.03f;
            if (dist > maxR) { d *= maxR / dist; dist = maxR; limited = true; } else if (dist < minR) { d *= minR / Mathf.Max(dist, 1e-4f); dist = minR; }
            Vector3 axis = d / dist;
            // pole: the knee's current offset from the hip-ankle line
            Vector3 ha = (A - H); Vector3 haN = ha.sqrMagnitude > 1e-6f ? ha.normalized : axis;
            Vector3 pole = (K - H) - Vector3.Dot(K - H, haN) * haN;
            if (pole.sqrMagnitude < 1e-6f) pole = rig.Go.transform.rotation * Vector3.forward;
            pole = pole - Vector3.Dot(pole, axis) * axis;
            if (pole.sqrMagnitude < 1e-8f) pole = Vector3.Cross(axis, Vector3.right);
            pole.Normalize();
            float a = (l1 * l1 - l2 * l2 + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));
            Vector3 K2 = H + axis * a + pole * h;
            Vector3 T2 = H + d;
            U.rotation = Quaternion.FromToRotation(K - H, K2 - H) * U.rotation;
            Vector3 A1 = F.position, K1 = L.position;
            L.rotation = Quaternion.FromToRotation(A1 - K1, T2 - K1) * L.rotation;
        }
    }
}
