using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.11.0 MOTION: running-form probe on the FINAL pose (after animation, matcher, FootPlant, action ground pass), every frame of a running body. Observation only.
    /// Per stride events: knee drive (peak thigh flexion in swing), heel recovery (peak knee flexion and peak ankle height in swing), foot strike (ankle ahead of the pelvis along the travel direction, in cm and in
    /// leg lengths = overstride; foot pitch at touch-down, + = heel up / forefoot first; knee flexion at touch-down), toe-off (ankle behind the pelvis, knee flexion, heel-up pitch).
    /// Per frame: trunk lean (pelvis to neck, forward +) split by longitudinal acceleration, arm-leg phase (the hand opposite to the forward foot is forward), pelvis height.
    /// </summary>
    public sealed class FormProbe
    {
        public sealed class Acc
        {
            public readonly Dictionary<string, List<float>> V = new Dictionary<string, List<float>>();
            public int Frames, Strikes;
            public void Add(string k, float v) { List<float> l; if (!V.TryGetValue(k, out l)) V[k] = l = new List<float>(); l.Add(v); }
        }

        sealed class Leg
        {
            public Transform U, L, F, T, Hand; public bool Left;
            public bool Stance, HaveState; public int SwingFrames, StanceFrames;
            public float PeakThigh, PeakKnee, PeakAnkleH; public bool PeakOk;
            public float PrevStrikeT;
            public Vector3 PrevA;
            public float LastPitch, LastKnee, LastTrail;
        }

        readonly Leg _l = new Leg { Left = true }, _r = new Leg();
        Transform _hips, _neck, _head; bool _ok; float _legLen = 0.85f; PixFootball.Gameplay.FootPlant _plant;
        readonly float _restAnkle = 0.097f, _restToe = 0.024f;
        readonly Queue<float> _speeds = new Queue<float>();
        float _prevPelvisY; bool _havePelvis;

        public FormProbe(Animator an, PixFootball.Gameplay.FootPlant plant = null)
        {
            if (an == null || !an.isHuman) return;
            _l.U = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg); _l.L = an.GetBoneTransform(HumanBodyBones.LeftLowerLeg); _l.F = an.GetBoneTransform(HumanBodyBones.LeftFoot); _l.T = an.GetBoneTransform(HumanBodyBones.LeftToes); _l.Hand = an.GetBoneTransform(HumanBodyBones.LeftHand);
            _r.U = an.GetBoneTransform(HumanBodyBones.RightUpperLeg); _r.L = an.GetBoneTransform(HumanBodyBones.RightLowerLeg); _r.F = an.GetBoneTransform(HumanBodyBones.RightFoot); _r.T = an.GetBoneTransform(HumanBodyBones.RightToes); _r.Hand = an.GetBoneTransform(HumanBodyBones.RightHand);
            _hips = an.GetBoneTransform(HumanBodyBones.Hips); _neck = an.GetBoneTransform(HumanBodyBones.Neck); _head = an.GetBoneTransform(HumanBodyBones.Head);
            _ok = _l.U != null && _l.L != null && _l.F != null && _r.U != null && _r.L != null && _r.F != null && _hips != null && (_neck != null || _head != null);
            if (_ok) _legLen = Vector3.Distance(_l.U.position, _l.L.position) + Vector3.Distance(_l.L.position, _l.F.position);
            if (plant != null && plant.Ok) { _restAnkle = plant.RestAnkleL; _restToe = plant.RestToeL; _plant = plant; }
        }

        static float Flex(Vector3 a, Vector3 b, Vector3 c)
        {
            // 180 - interior angle at b (0 = straight)
            return 180f - Vector3.Angle(a - b, c - b);
        }

        /// <param name="facing">body forward (horizontal)</param><param name="travel">velocity (horizontal)</param>
        float _frameSole; int _hintRun, _hn; readonly bool[] _hh = new bool[5]; readonly float[] _hs = new float[5];
        Acc _b;
        void Put(Acc a, string k, float v) { a.Add(k, v); if (_b != null) _b.Add(k, v); }

        public void Sample(Acc acc, Vector3 facing, Vector3 travel, float speed, float gy, float dt, Acc second = null)
        {
            if (!_ok || acc == null) return;
            facing.y = 0f; travel.y = 0f; if (facing.sqrMagnitude < 1e-4f) return; facing.Normalize();
            _b = second;
            Vector3 td = travel.sqrMagnitude > 1e-4f && speed > 1f ? travel.normalized : facing;
            acc.Frames++; if (_b != null) _b.Frames++;
            // longitudinal acceleration over ~0.2 s
            _speeds.Enqueue(speed); while (_speeds.Count > 13) _speeds.Dequeue();
            float accel = _speeds.Count >= 13 ? (speed - _speeds.Peek()) / (12f * Mathf.Max(1e-3f, dt)) : 0f; bool haveAcc = _speeds.Count >= 13;
            Vector3 P = _hips.position;
            _frameSole = 9f;
            Sample(acc, _l, td, P, gy);
            Sample(acc, _r, td, P, gy);
            if (speed > 1.5f)
            {
                bool hint = _plant != null && (_plant.HintLeft == 1 || _plant.HintRight == 1);
                _hintRun = hint ? _hintRun + 1 : 0;      // the matcher's contact tag runs one source frame ahead of the foot: the first two frames of a stance are the foot arriving
                Put(acc, "lowest sole cm (all)", _frameSole * 100f);
                Put(acc, "floating: sole > 4 cm, share of all moving frames", _frameSole > 0.04f ? 1f : 0f);
                if (hint) Put(acc, "floating: sole > 4 cm, share of contact-hinted frames", _frameSole > 0.04f ? 1f : 0f);
                // classification of the frame two back (so both neighbours on each side are known): flight (no contact tag), contact edge (a foot arriving or leaving: within two frames of the end of the tag), mid-contact
                for (int i = 0; i < 4; i++) { _hh[i] = _hh[i + 1]; _hs[i] = _hs[i + 1]; }
                _hh[4] = hint; _hs[4] = _frameSole; _hn++;
                if (_hn >= 5)
                {
                    bool mid = _hh[0] && _hh[1] && _hh[2] && _hh[3] && _hh[4];
                    Put(acc, "frame class: flight (no contact tag)", _hh[2] ? 0f : 1f);
                    Put(acc, "frame class: contact edge (arriving / leaving, within 2 frames of the tag end)", _hh[2] && !mid ? 1f : 0f);
                    Put(acc, "frame class: mid-contact", mid ? 1f : 0f);
                    if (mid) Put(acc, "floating: sole > 4 cm, share of mid-contact frames", _hs[2] > 0.04f ? 1f : 0f);
                    if (_hh[2] && !mid) Put(acc, "floating: sole > 4 cm, share of contact-edge frames", _hs[2] > 0.04f ? 1f : 0f);
                    if (!_hh[2]) Put(acc, "floating: sole > 4 cm, share of flight frames", _hs[2] > 0.04f ? 1f : 0f);
                }
            }
            // trunk lean: pelvis -> neck, forward (along the facing) positive
            Vector3 top = (_neck != null ? _neck : _head).position, tr = top - P; float lean = Mathf.Atan2(Vector3.Dot(tr, facing), tr.y) * Mathf.Rad2Deg;
            string ab = !haveAcc ? null : accel > 1.5f ? "accel > 1.5" : accel < -1.5f ? "decel < -1.5" : "steady";
            if (ab != null && speed > 1.5f) Put(acc, "trunk lean deg (" + ab + ")", lean);
            if (speed > 1.5f) Put(acc, "trunk lean deg (all)", lean);
            if (haveAcc && speed > 1.5f) { Put(acc, "accel m/s2 (all)", accel); Put(acc, "lean-accel pair", lean); Put(acc, "lean-accel pair", accel); }
            // arm-leg phase: the hand opposite to the forward foot should be forward
            if (_l.Hand != null && _r.Hand != null && speed > 2.5f)
            {
                float dFoot = Vector3.Dot(_l.F.position - _r.F.position, td), dHand = Vector3.Dot(_r.Hand.position - _l.Hand.position, facing);
                if (Mathf.Abs(dFoot) > 0.10f && Mathf.Abs(dHand) > 0.06f) Put(acc, "arm-leg phase agree", dFoot * dHand > 0f ? 1f : 0f);
            }
            if (_havePelvis && speed > 2.5f) Put(acc, "pelvis vertical speed m/s (abs)", Mathf.Abs(P.y - _prevPelvisY) / Mathf.Max(1e-3f, dt));
            _prevPelvisY = P.y; _havePelvis = true;
            if (speed > 2.5f) Put(acc, "pelvis height / leg length", (P.y - gy) / _legLen);
        }

        void Sample(Acc acc, Leg g, Vector3 td, Vector3 P, float gy)
        {
            Vector3 H = g.U.position, K = g.L.position, A = g.F.position, T = g.T != null ? g.T.position : A + td * 0.15f;
            float sole = Mathf.Min(T.y - gy, A.y - gy - 0.09f);
            bool stance = _plant != null ? (g.Left ? _plant.PlantedLeft : _plant.PlantedRight) : (g.HaveState ? (g.Stance ? sole < 0.045f : sole < 0.025f) : sole < 0.03f);      // with the planted-foot solver: the foot it holds on the turf (its touch-down is the first held frame)
            float thigh = Mathf.Atan2(Vector3.Dot(K - H, td), -(K - H).y) * Mathf.Rad2Deg;       // + = knee ahead of the hip
            float knee = Flex(H, K, A);
            float ankH = (A.y - gy - _restAnkle) * 100f;
            { float sh = Mathf.Min(T.y - gy - _restToe, A.y - gy - _restAnkle); if (sh < _frameSole) _frameSole = sh; }
            // pitch of the foot: heel height above its rest minus ball height above its rest over the foot length; + = heel up (toes down)
            float footLen = Mathf.Max(0.08f, new Vector2(T.x - A.x, T.z - A.z).magnitude);
            float pitch = Mathf.Atan2((A.y - gy - _restAnkle) - (T.y - gy - _restToe), footLen) * Mathf.Rad2Deg;
            float ahead = Vector3.Dot(A - P, td) * 100f;      // ankle ahead of the pelvis along the travel direction, cm
            if (g.HaveState)
            {
                if (!g.Stance && stance)
                {
                    // touch-down: close the swing
                    if (g.SwingFrames >= 4 && g.PeakOk)
                    {
                        Put(acc, "knee drive: peak thigh flexion in swing deg", g.PeakThigh);
                        Put(acc, "heel recovery: peak knee flexion in swing deg", g.PeakKnee);
                        Put(acc, "heel recovery: peak ankle height in swing cm", g.PeakAnkleH);
                    }
                    acc.Strikes++; if (_b != null) _b.Strikes++;
                    Put(acc, "strike: ankle ahead of pelvis cm", ahead); Put(acc, "strike: ankle ahead of pelvis, leg lengths", ahead / 100f / _legLen);
                    Put(acc, "strike: foot pitch deg (+ = heel up)", pitch); Put(acc, "strike: knee flexion deg", knee); Put(acc, "strike: thigh flexion deg", thigh);
                    g.StanceFrames = 0; g.PeakOk = false;
                }
                else if (g.Stance && !stance)
                {
                    // toe-off
                    if (g.StanceFrames >= 3) { Put(acc, "toe-off: ankle behind pelvis cm", -ahead); Put(acc, "toe-off: knee flexion deg", knee); Put(acc, "toe-off: foot pitch deg (+ = heel up)", pitch); }
                    g.SwingFrames = 0; g.PeakThigh = -90f; g.PeakKnee = 0f; g.PeakAnkleH = 0f; g.PeakOk = true;
                }
            }
            if (stance) g.StanceFrames++;
            else if (g.PeakOk || !g.HaveState)
            {
                g.SwingFrames++;
                if (!g.PeakOk) { g.PeakOk = true; g.PeakThigh = -90f; g.PeakKnee = 0f; g.PeakAnkleH = 0f; }
                g.PeakThigh = Mathf.Max(g.PeakThigh, thigh); g.PeakKnee = Mathf.Max(g.PeakKnee, knee); g.PeakAnkleH = Mathf.Max(g.PeakAnkleH, ankH);
            }
            g.Stance = stance; g.HaveState = true;
        }

        // ------------------------------------------------------------------------------------------ report
        static float Mean(List<float> l) { return l == null || l.Count == 0 ? float.NaN : l.Average(); }
        static float P(List<float> l, float p) { if (l == null || l.Count == 0) return float.NaN; var s = l.OrderBy(x => x).ToList(); return s[Mathf.Clamp((int)(p * (s.Count - 1) + 0.5f), 0, s.Count - 1)]; }
        static string F(float v) { return float.IsNaN(v) ? "-" : v.ToString("0.0", CultureInfo.InvariantCulture); }
        static string F2(float v) { return float.IsNaN(v) ? "-" : v.ToString("0.00", CultureInfo.InvariantCulture); }
        static string MP(Acc a, string k) { List<float> l; a.V.TryGetValue(k, out l); return F(Mean(l)) + " / " + F(P(l, 0.95f)); }
        static string M1(Acc a, string k) { List<float> l; a.V.TryGetValue(k, out l); return F(Mean(l)); }
        static string Frac(Acc a, string k) { List<float> l; a.V.TryGetValue(k, out l); return F2(Mean(l)); }
        static string Over(Acc a, string k, float thr) { List<float> l; a.V.TryGetValue(k, out l); return l == null || l.Count == 0 ? "-" : (l.Count(x => x > thr) / (float)l.Count).ToString("0.00", CultureInfo.InvariantCulture); }

        static string LeanAccelSlope(Acc a)
        {
            List<float> l; if (!a.V.TryGetValue("lean-accel pair", out l) || l.Count < 40) return "-";
            double sx = 0, sy = 0, sxx = 0, sxy = 0; int n = l.Count / 2;
            for (int i = 0; i < n; i++) { double y = l[2 * i], x = l[2 * i + 1]; sx += x; sy += y; sxx += x * x; sxy += x * y; }
            double den = n * sxx - sx * sx; return den < 1e-6 ? "-" : ((n * sxy - sx * sy) / den).ToString("0.00", CultureInfo.InvariantCulture);
        }

        public static string Table(IDictionary<string, Acc> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine("| case | frames | strikes | knee drive: peak thigh flexion deg (mean / p95) | heel recovery: peak knee flexion deg | peak ankle height cm | strike ahead of pelvis cm (mean / p95) | overstride: share of strikes > 0.30 leg | strike foot pitch deg (+ heel up) | strike knee flexion deg | toe-off behind pelvis cm | toe-off knee flexion deg | toe-off pitch deg | trunk lean deg steady / accel / decel | lean per m/s2 of accel (deg) | arm-leg phase agree | pelvis bob m/s (abs mean) | pelvis height / leg | lowest sole cm (mean / p95) | floating (sole > 4 cm): mid-contact frames | contact edges | flight frames | all moving frames | share flight / edge / mid |");
            sb.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var kv in rows.OrderBy(x => x.Key))
            {
                var a = kv.Value;
                sb.AppendLine("| " + kv.Key + " | " + a.Frames + " | " + a.Strikes + " | " + MP(a, "knee drive: peak thigh flexion in swing deg") + " | " + MP(a, "heel recovery: peak knee flexion in swing deg") + " | " + MP(a, "heel recovery: peak ankle height in swing cm") + " | " + MP(a, "strike: ankle ahead of pelvis cm") + " | " + Over(a, "strike: ankle ahead of pelvis, leg lengths", 0.30f) + " | " + M1(a, "strike: foot pitch deg (+ = heel up)") + " | " + M1(a, "strike: knee flexion deg") + " | " + M1(a, "toe-off: ankle behind pelvis cm") + " | " + M1(a, "toe-off: knee flexion deg") + " | " + M1(a, "toe-off: foot pitch deg (+ = heel up)") + " | " + M1(a, "trunk lean deg (steady)") + " / " + M1(a, "trunk lean deg (accel > 1.5)") + " / " + M1(a, "trunk lean deg (decel < -1.5)") + " | " + LeanAccelSlope(a) + " | " + Frac(a, "arm-leg phase agree") + " | " + F2(Mean(a.V.ContainsKey("pelvis vertical speed m/s (abs)") ? a.V["pelvis vertical speed m/s (abs)"] : null)) + " | " + F2(Mean(a.V.ContainsKey("pelvis height / leg length") ? a.V["pelvis height / leg length"] : null)) + " | " + MP(a, "lowest sole cm (all)") + " | " + Frac(a, "floating: sole > 4 cm, share of mid-contact frames") + " | " + Frac(a, "floating: sole > 4 cm, share of contact-edge frames") + " | " + Frac(a, "floating: sole > 4 cm, share of flight frames") + " | " + Frac(a, "floating: sole > 4 cm, share of all moving frames") + " | " + Frac(a, "frame class: flight (no contact tag)") + " / " + Frac(a, "frame class: contact edge (arriving / leaving, within 2 frames of the tag end)") + " / " + Frac(a, "frame class: mid-contact") + " |");
            }
            return sb.ToString();
        }
    }
}

namespace PixFootball.EditorTools
{
    /// <summary>Collects <see cref="FormProbe"/> rows over a whole audit (per gait and speed band, and per position) and writes form_quality.md.</summary>
    public static class FormAuditHook
    {
        static readonly Dictionary<PixFootball.Gameplay.SimAgentView, FormProbe> Probes = new Dictionary<PixFootball.Gameplay.SimAgentView, FormProbe>();
        static readonly SortedDictionary<string, FormProbe.Acc> Gait = new SortedDictionary<string, FormProbe.Acc>(), Pos = new SortedDictionary<string, FormProbe.Acc>();

        public static void Reset() { Probes.Clear(); Gait.Clear(); Pos.Clear(); }

        public static void Feed(PixFootball.Gameplay.SimAgentView v, string action, bool carry, string position, float speedMs, Vector3 vel, float gy, float dt)
        {
            if (v == null || v.Avatar == null || v.Avatar.Animator == null || speedMs < 1.5f) return;
            FormProbe p; if (!Probes.TryGetValue(v, out p)) Probes[v] = p = new FormProbe(v.Avatar.Animator, v.Motion != null ? v.Motion.Plant : null);
            string g = (carry ? "Carry" : action) + (speedMs < 3f ? " <3 m/s" : speedMs < 5.5f ? " 3-5.5 m/s" : " >=5.5 m/s");
            FormProbe.Acc a; if (!Gait.TryGetValue(g, out a)) Gait[g] = a = new FormProbe.Acc();
            string pk = position + " " + (carry ? "Carry" : action) + (speedMs < 3f ? " <3" : speedMs < 5.5f ? " 3-5.5" : " >=5.5");
            FormProbe.Acc b; if (!Pos.TryGetValue(pk, out b)) Pos[pk] = b = new FormProbe.Acc();
            p.Sample(a, v.Forward, vel, speedMs, gy, dt, b);
        }

        public static void Write(string dir)
        {
            System.IO.File.WriteAllText(dir + "/form_quality.md", "# Running form in real plays (final pose, every running player each frame)\n\n## by gait\n\n" + FormProbe.Table(Gait) + "\n## by position\n\n" + FormProbe.Table(Pos));
        }
    }
}
