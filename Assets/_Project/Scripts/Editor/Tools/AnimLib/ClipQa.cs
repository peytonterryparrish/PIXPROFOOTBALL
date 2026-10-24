using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>One clip's measured quality (v0.10.0 ANIMLIB). All angles in degrees, distances in metres, speeds in m/s.</summary>
    public sealed class QaRow
    {
        public string name, keys = "", source = "";
        public float length; public bool loop;
        // root
        public float travel, rootSpeed, rootDir, hipMed, gaitSpeed;
        // feet
        public float slideMean, slidePct, plantedPct, penPct, groundCm, floatCm; public bool treadmill;
        // skeleton
        public float waistTwist, torsoTwist, torsoBend, headTwist, kneeHyper, kneeHyperPct, elbowOverPct, popMax, popFrames, jitter;
        // hands
        public float wristOverPct, wristMaxDeg, forearmTwistMax, forearmFlip, fingerStd, fingerOverPct, fingerCurl, fingerJitter, fingerAbs; public bool fingerStatic;
        // clip shape
        public int tposeFrames; public float deadStart, deadEnd, seamPose;
        // verdict
        public float score; public bool reject; public string faults = "";
        public bool fSlide, fGround, fHand, fTwist, fSkel, fYaw, fFloor, fRest;
        public float yaw0, footAsymCm, armsLead, armsTail, armsMax; public bool fArms;
        // v0.11 leg metrics (FootAxes.cs): ankle displacement over planted frames (cm), foot yaw drift (deg), ankle roll while flat (deg), foot rotation pops
        public float plantDisp, plantYaw, plantRoll, plantToe, footPops; public int plantN;

        public static string Header()
        {
            return "name,keys,length,loop,travel,rootSpeed,rootDir,hipMed,gaitSpeed,slideMean,slidePct,plantedPct,penPct,groundCm,floatCm,treadmill,waistTwist,torsoTwist,torsoBend,headTwist,kneeHyper,kneeHyperPct,elbowOverPct,popMax,popFrames,jitter,"
                + "wristOverPct,wristMaxDeg,forearmTwistMax,forearmFlip,fingerStd,fingerOverPct,fingerCurl,fingerJitter,fingerStatic,tposeFrames,deadStart,deadEnd,seamPose,score,reject,faults,fSlide,fGround,fHand,fTwist,fSkel,yaw0,footAsymCm,fYaw,fFloor,fRest,armsLead,armsTail,armsMax,fArms,source,plantN,plantDisp,plantYaw,plantRoll,footPops,plantToe";
        }

        public string Csv()
        {
            Func<float, string> F = v => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            Func<string, string> Q = s => "\"" + (s ?? "").Replace("\"", "'") + "\"";
            Func<bool, string> B = b => b ? "1" : "0";
            return string.Join(",", new[]
            {
                Q(name), Q(keys), F(length), B(loop), F(travel), F(rootSpeed), F(rootDir), F(hipMed), F(gaitSpeed), F(slideMean), F(slidePct), F(plantedPct), F(penPct), F(groundCm), F(floatCm), B(treadmill), F(waistTwist), F(torsoTwist), F(torsoBend), F(headTwist),
                F(kneeHyper), F(kneeHyperPct), F(elbowOverPct), F(popMax), F(popFrames), F(jitter), F(wristOverPct), F(wristMaxDeg), F(forearmTwistMax), F(forearmFlip), F(fingerStd), F(fingerOverPct), F(fingerCurl), F(fingerJitter), B(fingerStatic),
                tposeFrames.ToString(), F(deadStart), F(deadEnd), F(seamPose), F(score), B(reject), Q(faults), B(fSlide), B(fGround), B(fHand), B(fTwist), B(fSkel), F(yaw0), F(footAsymCm), B(fYaw), B(fFloor), B(fRest), F(armsLead), F(armsTail), F(armsMax), B(fArms), Q(source), plantN.ToString(), F(plantDisp), F(plantYaw), F(plantRoll), F(footPops), F(plantToe)
            });
        }
    }

    /// <summary>
    /// Per-clip quality measurement for the animation library: foot plants (slide, ground penetration, float), skeleton (waist / torso twist, spine bend, knee and elbow
    /// hyperextension, bone pops, jitter), hands (wrist and forearm limits, finger data and curl) and shape (T-pose frames, dead time, loop seam), combined into a 0-100 score.
    /// The cleaner (ClipPolish) fixes what it can and re-measures; clips that stay below <see cref="RejectScore"/> are rejected.
    /// </summary>
    public static class ClipQa
    {
        public const float Rate = 30f;
        public const float RejectScore = 50f;
        // fault thresholds (a clip counts as having that fault above them)
        public const float SlidePct = 25f, SlideMean = 0.28f, PenPct = 5f, FloatCm = 4f, WaistTwist = 85f, TorsoTwist = 80f, KneeHyper = 8f, PopFrames = 2f, WristOver = 3f, ForearmTwist = 105f, FingerOver = 2f;

        static string[] _names; static float[] _min, _max;
        static int[] _twist, _bendFb, _bendLr, _neckHead, _wrist, _forearmTw, _forearmSt, _kneeSt;
        static int[][] _fingers = new int[2][], _fingerStretch = new int[2][];

        static void Init()
        {
            if (_names != null) return;
            _names = HumanTrait.MuscleName; int n = _names.Length;
            _min = new float[n]; _max = new float[n];
            for (int i = 0; i < n; i++) { _min[i] = HumanTrait.GetMuscleDefaultMin(i); _max[i] = HumanTrait.GetMuscleDefaultMax(i); }
            _twist = Idx("Spine Twist Left-Right", "Chest Twist Left-Right", "UpperChest Twist Left-Right");
            _bendFb = Idx("Spine Front-Back", "Chest Front-Back", "UpperChest Front-Back");
            _bendLr = Idx("Spine Left-Right", "Chest Left-Right", "UpperChest Left-Right");
            _neckHead = Idx("Neck Turn Left-Right", "Head Turn Left-Right");
            _wrist = Idx("Left Hand Down-Up", "Left Hand In-Out", "Right Hand Down-Up", "Right Hand In-Out");
            _forearmTw = Idx("Left Forearm Twist In-Out", "Right Forearm Twist In-Out");
            _forearmSt = Idx("Left Forearm Stretch", "Right Forearm Stretch");
            _kneeSt = Idx("Left Lower Leg Stretch", "Right Lower Leg Stretch");
            string[] sides = { "Left ", "Right " };
            for (int s = 0; s < 2; s++)
            {
                var all = new List<int>(); var st = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    string nm = _names[i];
                    if (!nm.StartsWith(sides[s])) continue;
                    if (!(nm.Contains("Thumb") || nm.Contains("Index") || nm.Contains("Middle") || nm.Contains("Ring") || nm.Contains("Little"))) continue;
                    all.Add(i); if (nm.Contains("Stretched")) st.Add(i);
                }
                _fingers[s] = all.ToArray(); _fingerStretch[s] = st.ToArray();
            }
        }

        /// <summary>Height of the sole: the toe bone sits 2.3 cm and the ankle bone 9.7 cm above the sole of a flat foot (measured on the Medium body in the standing idle).</summary>
        public static float Sole(AnimRig.Frame f, bool left)
        {
            return Mathf.Min(f.P[left ? AnimRig.LT : AnimRig.RT].y - 0.023f, f.P[left ? AnimRig.LF : AnimRig.RF].y - 0.097f);
        }

        static int[] Idx(params string[] names) { return names.Select(m => Array.IndexOf(_names, m)).Where(i => i >= 0).ToArray(); }

        /// <summary>Muscle value to joint degrees (the avatar's default muscle limits).</summary>
        public static float Deg(int m, float v) { return v >= 0f ? v * _max[m] : -v * _min[m]; }
        public static float Deg(string muscle, float v) { Init(); int m = Array.IndexOf(_names, muscle); return m < 0 ? 0f : Deg(m, v); }
        public static int MuscleIndex(string name) { Init(); return Array.IndexOf(_names, name); }
        public static float MuscleMax(int m) { Init(); return _max[m]; }
        public static float MuscleMin(int m) { Init(); return _min[m]; }
        public static int[] FingerMuscles(int side) { Init(); return _fingers[side]; }
        public static int[] FingerStretch(int side) { Init(); return _fingerStretch[side]; }

        public static QaRow Analyse(AnimRig rig, AnimationClip clip, string keys = "")
        {
            Init();
            var r = new QaRow { name = clip.name, keys = keys, length = clip.length, loop = clip.isLooping };
            float dur = Mathf.Min(clip.length, 24f);
            if (dur < 0.1f) { r.score = 0f; r.reject = true; r.faults = "empty"; return r; }
            var fr = rig.Sample(clip, 0f, dur, Rate);
            var seq = PoseSeq.FromClip(clip, 0f, dur, Rate);
            Measure(r, rig, fr, seq);
            Verdict(r);
            return r;
        }

        /// <summary>Measures a not-yet-written sequence (ClipPolish uses it to accept or reject a fix).</summary>
        public static QaRow Analyse(AnimRig rig, PoseSeq seq, string keys = "")
        {
            var clip = seq.ToClip(seq.Name);
            try { return Analyse(rig, clip, keys); } finally { UnityEngine.Object.DestroyImmediate(clip); }
        }

        static void Measure(QaRow r, AnimRig rig, List<AnimRig.Frame> fr, PoseSeq seq)
        {
            int N = fr.Count; float dt = 1f / Rate; float dur = (N - 1) * dt;
            if (N < 4) return;
            // ---- root
            Vector3 d = fr[N - 1].P[AnimRig.Hips] - fr[0].P[AnimRig.Hips]; d.y = 0f;
            float path = 0f; for (int i = 1; i < N; i++) { Vector3 s = fr[i].P[AnimRig.Hips] - fr[i - 1].P[AnimRig.Hips]; s.y = 0f; path += s.magnitude; }
            r.travel = d.magnitude; r.rootSpeed = path / Mathf.Max(0.1f, dur);
            if (r.travel > 0.25f) r.rootDir = Mathf.DeltaAngle(AnimRig.YawOf(AnimRig.Forward(fr[0])), AnimRig.YawOf(d));
            var hips = new List<float>(); var lowest = new List<float>();
            // ---- feet: per foot a "sole" height proxy (the lower of the toe and the ankle minus its rest height above the sole)
            var soleL = new float[N]; var soleR = new float[N];
            int[] feet = { AnimRig.LF, AnimRig.RF }, toes = { AnimRig.LT, AnimRig.RT };
            for (int i = 0; i < N; i++)
            {
                var f = fr[i];
                soleL[i] = Sole(f, true); soleR[i] = Sole(f, false);
                hips.Add(f.P[AnimRig.Hips].y); lowest.Add(Mathf.Min(soleL[i], soleR[i]));
            }
            hips.Sort(); r.hipMed = hips[hips.Count / 2];
            var lows = lowest.ToList(); lows.Sort();
            float p10 = lows[(int)(lows.Count * 0.10f)];
            r.groundCm = p10 * 100f;
            bool upright = r.hipMed > 0.65f;
            int pen = 0; foreach (float l in lowest) if (l < -0.04f) pen++;
            r.penPct = 100f * pen / N;
            r.floatCm = upright ? Mathf.Max(0f, p10 * 100f) : 0f;
            // arms thrown out to the sides at shoulder height (a T-pose look): at the start / end of the take (a calibration pose that was not trimmed) or for a long stretch inside it
            {
                var ao = ArmCheck.ArmsOut(fr); int lead = 0, tail = 0, run = 0, mx = 0;
                while (lead < N && ao[lead]) lead++;
                while (tail < N && ao[N - 1 - tail]) tail++;
                for (int i = 0; i < N; i++) { if (ao[i]) { run++; mx = Mathf.Max(mx, run); } else run = 0; }
                r.armsLead = lead * dt; r.armsTail = tail * dt; r.armsMax = mx * dt;
            }
            // heading of the body at the start (0 = +Z of the game) and the left / right difference of the ground level of the soles (a rest pose taken from a take's first frame shows as one foot above the turf)
            {
                float sx = 0f, sz = 0f; for (int i = 0; i < Mathf.Min(4, N); i++) { float y = AnimRig.YawOf(AnimRig.Forward(fr[i])) * Mathf.Deg2Rad; sx += Mathf.Sin(y); sz += Mathf.Cos(y); }
                r.yaw0 = Mathf.Atan2(sx, sz) * Mathf.Rad2Deg;
                var l10 = new List<float>(soleL); var r10 = new List<float>(soleR); l10.Sort(); r10.Sort();
                float pl = l10[(int)(N * 0.10f)], pr = r10[(int)(N * 0.10f)]; int nl = 0, nr = 0;
                for (int i = 0; i < N; i++) { if (soleL[i] < p10 + 0.04f) nl++; if (soleR[i] < p10 + 0.04f) nr++; }
                r.footAsymCm = (upright && nl > 0.2f * N && nr > 0.2f * N) ? Mathf.Abs(pl - pr) * 100f : 0f;
            }
            // contact = the sole within 4 cm of the ground level for at least 2 frames in a row without a vertical speed (a swing foot only grazes the turf for a frame);
            // slide = horizontal speed of the foot inside the contact, ignoring its first and last frame when the contact is 4+ frames long (heel strike / toe off roll)
            float ground = p10;
            int planted = 0, slid = 0; float slideSum = 0f; var plantSpeeds = new List<float>();
            for (int side = 0; side < 2; side++)
            {
                float[] sole = side == 0 ? soleL : soleR; int fi = feet[side], ti = toes[side];
                var low = new bool[N];
                for (int i = 1; i < N - 1; i++) low[i] = sole[i] < ground + 0.04f && Mathf.Abs((sole[i + 1] - sole[i - 1]) / (2f * dt)) < 0.6f;
                int i0 = 1;
                while (i0 < N - 1)
                {
                    if (!low[i0]) { i0++; continue; }
                    int i1 = i0; while (i1 + 1 < N - 1 && low[i1 + 1]) i1++;
                    int len = i1 - i0 + 1;
                    if (len >= 2)
                        for (int i = len >= 4 ? i0 + 1 : i0; i <= (len >= 4 ? i1 - 1 : i1); i++)
                        {
                            Vector3 v = (fr[i + 1].P[fi] - fr[i - 1].P[fi]) / (2f * dt); Vector3 vt = (fr[i + 1].P[ti] - fr[i - 1].P[ti]) / (2f * dt);
                            v.y = vt.y = 0f; float sp = Mathf.Min(v.magnitude, vt.magnitude);
                            planted++; slideSum += sp; plantSpeeds.Add(sp);
                            if (sp > 0.25f) slid++;
                        }
                    i0 = i1 + 1;
                }
            }
            LegMetrics.Measure(r, fr, new[] { soleL, soleR }, ground, dt);
            r.plantedPct = 100f * planted / Mathf.Max(1, 2 * (N - 2));
            float rawMean = planted > 0 ? slideSum / planted : 0f;
            r.gaitSpeed = planted > 3 ? rawMean : 0f;
            // an in-place cycle (no root travel) is a treadmill by design: the simulation supplies the ground speed (CycleSpeeds); its foot speed is not a slide
            r.treadmill = r.travel < 0.09f * Mathf.Max(1f, dur) && rawMean > 0.75f && planted > 6;
            if (r.treadmill) { plantSpeeds.Sort(); float med = plantSpeeds[plantSpeeds.Count / 2]; int off = 0; foreach (float sp in plantSpeeds) if (Mathf.Abs(sp - med) > 0.45f) off++; r.slideMean = 0f; r.slidePct = Mathf.Max(0f, 100f * off / plantSpeeds.Count - 10f); }
            else { r.slideMean = rawMean; r.slidePct = planted > 0 ? 100f * slid / planted : 0f; }
            // ---- skeleton from the rig
            float waist = 0f; int pops = 0; float popMax = 0f; double jerkSum = 0; int jerkN = 0;
            int[] ids = { AnimRig.Spine, AnimRig.Chest, AnimRig.Head, AnimRig.LUA, AnimRig.LLA, AnimRig.LH, AnimRig.RUA, AnimRig.RLA, AnimRig.RH, AnimRig.LUL, AnimRig.LLL, AnimRig.LF, AnimRig.RUL, AnimRig.RLL, AnimRig.RF };
            int[] popIds = { AnimRig.Spine, AnimRig.Chest, AnimRig.Head, AnimRig.LUA, AnimRig.LLA, AnimRig.RUA, AnimRig.RLA, AnimRig.LUL, AnimRig.LLL, AnimRig.RUL, AnimRig.RLL };
            var energy = new float[N];
            for (int i = 0; i < N; i++)
            {
                var f = fr[i];
                if (Vector3.Dot((f.P[AnimRig.Neck] - f.P[AnimRig.Hips]).normalized, Vector3.up) > 0.75f)
                    waist = Mathf.Max(waist, Mathf.Abs(Mathf.DeltaAngle(AnimRig.YawOf(AnimRig.Forward(f)), AnimRig.YawOf(AnimRig.ShoulderForward(f)))));
                if (AnimAudit.IsTPose(f)) r.tposeFrames++;
                if (i > 0)
                {
                    float e = 0f;
                    for (int b = 0; b < ids.Length; b++) e += Quaternion.Angle(fr[i - 1].R[ids[b]], f.R[ids[b]]);
                    for (int b = 0; b < popIds.Length; b++) { float a = Quaternion.Angle(fr[i - 1].R[popIds[b]], f.R[popIds[b]]); if (a > 50f) pops++; popMax = Mathf.Max(popMax, a); }
                    energy[i] = e;
                }
                if (i > 1)
                    for (int b = 0; b < ids.Length; b++)
                    {
                        Quaternion d1 = Quaternion.Inverse(fr[i - 1].R[ids[b]]) * f.R[ids[b]], d0 = Quaternion.Inverse(fr[i - 2].R[ids[b]]) * fr[i - 1].R[ids[b]];
                        jerkSum += Quaternion.Angle(d0, d1); jerkN++;
                    }
            }
            r.waistTwist = waist; r.popFrames = pops; r.popMax = popMax; r.jitter = jerkN > 0 ? (float)(jerkSum / jerkN) : 0f;
            float peak = 0f; for (int i = 1; i < N; i++) peak = Mathf.Max(peak, energy[i]);
            float thr = Mathf.Max(0.25f, peak * 0.10f);
            int s0 = 1; while (s0 < N && energy[s0] < thr) s0++; int e0 = N - 1; while (e0 > 0 && energy[e0] < thr) e0--;
            r.deadStart = s0 >= N - 1 ? 0f : s0 * dt; r.deadEnd = e0 <= 0 ? 0f : (N - 1 - e0) * dt;
            r.seamPose = SeamPose(fr[0], fr[N - 1]);
            // ---- muscles: torso, head, wrist, forearm, fingers
            int n = seq.N;
            float tw = 0f, bend = 0f, head = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = 0f; foreach (int m in _twist) t += Deg(m, seq.M[m][i]); tw = Mathf.Max(tw, Mathf.Abs(t));
                float b = 0f; foreach (int m in _bendFb) b += Deg(m, seq.M[m][i]); bend = Mathf.Max(bend, Mathf.Abs(b));
                float hd = 0f; foreach (int m in _neckHead) hd += Deg(m, seq.M[m][i]); head = Mathf.Max(head, Mathf.Abs(hd));
            }
            r.torsoTwist = tw; r.torsoBend = bend; r.headTwist = head;
            int wrOver = 0, wrN = 0; float wrMax = 0f;
            foreach (int m in _wrist) for (int i = 0; i < n; i++) { float a = Mathf.Abs(seq.M[m][i]); wrN++; if (a > 1.0f) wrOver++; wrMax = Mathf.Max(wrMax, Mathf.Abs(Deg(m, seq.M[m][i]))); }
            r.wristOverPct = wrN > 0 ? 100f * wrOver / wrN : 0f; r.wristMaxDeg = wrMax;
            float ftw = 0f, flip = 0f;
            foreach (int m in _forearmTw) for (int i = 0; i < n; i++) { float dg = Mathf.Abs(Deg(m, seq.M[m][i])); ftw = Mathf.Max(ftw, dg); if (i > 0) flip = Mathf.Max(flip, Mathf.Abs(Deg(m, seq.M[m][i]) - Deg(m, seq.M[m][i - 1]))); }
            r.forearmTwistMax = ftw; r.forearmFlip = flip;
            // hyperextension: a straight knee / elbow is muscle +1, anything above it bends the joint the wrong way (80 degrees per unit)
            int elOver = 0, elN = 0; foreach (int m in _forearmSt) for (int i = 0; i < n; i++) { elN++; if (seq.M[m][i] > 1.06f) elOver++; }
            r.elbowOverPct = elN > 0 ? 100f * elOver / elN : 0f;
            int knOver = 0, knN = 0; float knMax = 0f;
            foreach (int m in _kneeSt) for (int i = 0; i < n; i++) { knN++; if (seq.M[m][i] > 1.06f) knOver++; knMax = Mathf.Max(knMax, (seq.M[m][i] - 1f) * 80f); }
            r.kneeHyper = Mathf.Max(0f, knMax); r.kneeHyperPct = knN > 0 ? 100f * knOver / knN : 0f;
            // fingers (both hands)
            double sdSum = 0, jitSum = 0, curlSum = 0; int cntM = 0, cntJ = 0, overN = 0, allN = 0;
            for (int s = 0; s < 2; s++)
            {
                foreach (int m in _fingers[s])
                {
                    var v = seq.M[m]; double mean = 0; for (int i = 0; i < n; i++) mean += v[i]; mean /= n;
                    double var = 0; for (int i = 0; i < n; i++) { double q = v[i] - mean; var += q * q; }
                    sdSum += Math.Sqrt(var / n); cntM++;
                    for (int i = 0; i < n; i++) { allN++; if (Mathf.Abs(v[i]) > 1.02f) overN++; }
                }
                foreach (int m in _fingerStretch[s])
                {
                    var v = seq.M[m];
                    for (int i = 1; i < n - 1; i++) { jitSum += Mathf.Abs(v[i - 1] - 2f * v[i] + v[i + 1]); cntJ++; }
                    double mean = 0; for (int i = 0; i < n; i++) mean += v[i]; curlSum += mean / n;
                }
            }
            r.fingerStd = cntM > 0 ? (float)(sdSum / cntM) : 0f; r.fingerJitter = cntJ > 0 ? (float)(jitSum / cntJ) : 0f;
            r.fingerOverPct = allN > 0 ? 100f * overN / allN : 0f;
            int stN = _fingerStretch[0].Length + _fingerStretch[1].Length; r.fingerCurl = stN > 0 ? (float)(curlSum / stN) : 0f;     // mean "Stretched" muscle value of all finger joints
            // no finger data = every finger muscle sits at 0 (the importer's default for takes without finger bones); an authored constant pose is data
            double absSum = 0; int absN = 0; for (int s = 0; s < 2; s++) foreach (int m in _fingerStretch[s]) { for (int i = 0; i < n; i++) absSum += Mathf.Abs(seq.M[m][i]); absN += n; }
            r.fingerAbs = absN > 0 ? (float)(absSum / absN) : 0f;
            r.fingerStatic = r.fingerStd < 0.012f && r.fingerAbs < 0.03f;
        }

        static float SeamPose(AnimRig.Frame a, AnimRig.Frame b)
        {
            int[] ids = { AnimRig.Spine, AnimRig.Chest, AnimRig.Head, AnimRig.LUA, AnimRig.LLA, AnimRig.RUA, AnimRig.RLA, AnimRig.LUL, AnimRig.LLL, AnimRig.RUL, AnimRig.RLL };
            Quaternion ha = Quaternion.Inverse(a.R[AnimRig.Hips]), hb = Quaternion.Inverse(b.R[AnimRig.Hips]);
            float s = 0f; foreach (int i in ids) s += Quaternion.Angle(ha * a.R[i], hb * b.R[i]);
            return s / ids.Length;
        }

        static readonly string[] ArmsOkWords = { "celebrat", "cheer", "dance", "freestyle", "touchdown", "heisman", "pointing", "stretch", "taunt", "signal", "gesture", "ref_", "hail", "draft_night", "endzone", "helmet", "conversation", "spike", "kneel", "salute", "bench", "mc_", "frustrat", "armsshaking", "idle_flair", "stand_happy", "stand_sad", "sit_", "shoulder", "chest_stretch" };
        public static bool ArmsOutIsFine(string name) { string n = name.ToLowerInvariant(); foreach (string w in ArmsOkWords) if (n.Contains(w)) return true; return false; }

        public static bool IsPairedName(string n) { return n.EndsWith("_A") || n.EndsWith("_B") || n.EndsWith("_CO") || n.StartsWith("Tackle") || n.StartsWith("Sack") || n.StartsWith("BigHit") || n.StartsWith("Collision") || n.Contains("_s0") && (n.EndsWith("_A") || n.EndsWith("_B")); }
        static readonly string[] TurnWords = { "turn", "rotate", "_90", "_135", "_180", "_225", "_270", "_315", "_360", "90deg", "135deg", "180deg", "avoid", "cut", "juke", "spin", "dodge", "stance_to", "to_sprint", "to_shuffle", "shuffle_to", "break", "release", "motion", "_45", "45deg" };

        /// <summary>Score, fault flags and the reject verdict from the measurements.</summary>
        public static void Verdict(QaRow r)
        {
            float s = 100f; var f = new List<string>();
            // feet
            r.fSlide = r.slidePct > SlidePct || r.slideMean > SlideMean;
            if (r.fSlide) { s -= Mathf.Min(26f, 0.22f * r.slidePct + 30f * r.slideMean); f.Add("slide"); }
            r.fGround = r.penPct > PenPct || r.floatCm > FloatCm;
            if (r.penPct > PenPct) { s -= Mathf.Min(14f, 0.5f * r.penPct); f.Add("penetrates"); }
            if (r.floatCm > FloatCm) { s -= Mathf.Min(12f, (r.floatCm - FloatCm) * 1.6f); f.Add("floats"); }
            // skeleton
            r.fTwist = r.waistTwist > WaistTwist || r.torsoTwist > TorsoTwist || r.torsoBend > 105f || r.headTwist > 115f;
            if (r.waistTwist > WaistTwist) { s -= Mathf.Min(14f, (r.waistTwist - WaistTwist) * 0.5f + 4f); f.Add("waist-twist"); }
            if (r.torsoTwist > TorsoTwist) { s -= Mathf.Min(10f, (r.torsoTwist - TorsoTwist) * 0.4f + 3f); f.Add("torso-twist"); }
            if (r.torsoBend > 105f) { s -= 6f; f.Add("spine-bend"); }
            if (r.headTwist > 115f) { s -= 5f; f.Add("head-twist"); }
            r.fSkel = r.kneeHyper > KneeHyper || r.elbowOverPct > 3f || r.popFrames >= PopFrames;
            if (r.kneeHyper > KneeHyper) { s -= Mathf.Min(14f, r.kneeHyperPct * 0.5f + (r.kneeHyper - KneeHyper) * 0.5f + 3f); f.Add("knee-hyper"); }
            if (r.elbowOverPct > 3f) { s -= Mathf.Min(8f, r.elbowOverPct * 0.4f); f.Add("elbow-hyper"); }
            if (r.popFrames >= PopFrames) { s -= Mathf.Min(20f, r.popFrames * 3.5f); f.Add("pops"); }
            else if (r.popFrames >= 1f) s -= 2f;
            if (r.jitter > 9f) { s -= Mathf.Min(8f, (r.jitter - 9f) * 0.8f); f.Add("jitter"); }
            // hands
            r.fHand = r.wristOverPct > WristOver || r.forearmTwistMax > ForearmTwist || r.forearmFlip > 45f || r.fingerOverPct > FingerOver || r.fingerStatic;
            if (r.wristOverPct > WristOver) { s -= Mathf.Min(8f, r.wristOverPct * 0.3f + 2f); f.Add("wrist-limit"); }
            if (r.forearmTwistMax > ForearmTwist) { s -= Mathf.Min(8f, (r.forearmTwistMax - ForearmTwist) * 0.3f + 2f); f.Add("forearm-twist"); }
            if (r.forearmFlip > 45f) { s -= 4f; f.Add("forearm-flip"); }
            if (r.fingerOverPct > FingerOver) { s -= Mathf.Min(5f, r.fingerOverPct * 0.3f); f.Add("finger-limit"); }
            if (r.fingerStatic) { s -= 3f; f.Add("no-finger-data"); }
            r.fArms = !ArmsOutIsFine(r.name) && (r.armsLead >= 0.1f || r.armsTail >= 0.2f || r.armsMax >= 0.5f);
            if (r.fArms) { s -= 6f; f.Add("arms-out"); }
            // the three import faults MOTION found on the Mixamo set: wrong heading, wrong floor level, a rest pose from the first frame (one foot above the turf)
            bool turny = false; { string ln = r.name.ToLowerInvariant(); foreach (string w in TurnWords) if (ln.Contains(w)) { turny = true; break; } }
            r.fYaw = !IsPairedName(r.name) && !turny && Mathf.Abs(r.yaw0) > 15f;
            if (r.fYaw) { s -= 3f; f.Add("yaw-offset"); }
            r.fFloor = r.hipMed > 0.65f && r.plantedPct > 12f && Mathf.Abs(r.groundCm) > 3f;
            if (r.fFloor) { s -= 3f; f.Add("floor-offset"); }
            r.fRest = r.footAsymCm > 3f;
            if (r.fRest) { s -= 4f; f.Add("rest-pose-foot"); }
            // clip shape
            if (r.tposeFrames > 0) { s -= Mathf.Min(25f, r.tposeFrames * 0.8f); f.Add("t-pose"); }
            if (r.deadStart > 0.7f || r.deadEnd > 0.9f) { s -= 4f; f.Add("dead-time"); }
            if (r.loop && r.seamPose > 14f) { s -= Mathf.Min(8f, (r.seamPose - 14f) * 0.5f + 2f); f.Add("loop-seam"); }
            r.score = Mathf.Clamp(s, 0f, 100f);
            r.faults = string.Join(" ", f);
            r.reject = r.score < RejectScore || (r.fArms && r.length >= 1f && r.armsMax >= 0.9f * r.length) || r.tposeFrames > 0.3f * (r.length * Rate) || r.popFrames >= 8f;
        }
    }
}
