using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.EditorTools
{
    /// <summary>
    /// v0.13.0 STANCE-HEAD: the evidence behind the head / stance / sunk-foot numbers of the round.
    ///  * head over chest (degrees about the vertical, from the bones, <see cref="NaturalBody.HeadOverChest"/>) for every drawn body and frame, split by exempt actions (throws, tackles, dives, get-ups, ground) and free ones;
    ///    histograms per action / group / stance kind with p50 / p95 / p99 / max and the turn rate of the head over the chest; written to <c>head_yaw.txt</c> and the summary.
    ///  * <see cref="FeetProbe"/>: a float_probe.csv row (bones next to the drawn sole) at every sampled FEET_BELOW_TURF frame (tag "below") and at a control sample of the contact bodies (tag "blk").
    ///  * the per-position stance compliance is computed from stance_snapshots.csv by <c>Docs/QA/v0130/tools/stance_compliance.pl</c>.
    /// Recording only.
    /// </summary>
    public sealed partial class MatchWatchRunner
    {
        sealed class AngHist
        {
            readonly int[] _bin; readonly float _w; public long N; public float Max; public double Sum;
            public AngHist(int bins, float width) { _bin = new int[bins]; _w = width; }
            public void Add(float x) { int b = Mathf.Clamp((int)(x / _w), 0, _bin.Length - 1); _bin[b]++; N++; Sum += x; if (x > Max) Max = x; }
            public float Pct(double p)
            {
                if (N == 0) return 0f; long want = (long)Math.Ceiling(p * N), acc = 0;
                for (int b = 0; b < _bin.Length; b++) { acc += _bin[b]; if (acc >= want) return Math.Min(Max, (b + 1) * _w); }
                return Max;
            }
            public long Over(float x) { long n = 0; for (int b = Mathf.Max(0, (int)(x / _w)); b < _bin.Length; b++) n += _bin[b]; return n; }
        }
        readonly SortedDictionary<string, AngHist> _headDeg = new SortedDictionary<string, AngHist>();
        readonly SortedDictionary<string, AngHist> _headRate = new SortedDictionary<string, AngHist>();
        static void HistAdd(SortedDictionary<string, AngHist> d, string k, float x, int bins, float w) { AngHist h; if (!d.TryGetValue(k, out h)) d[k] = h = new AngHist(bins, w); h.Add(x); }

        /// <summary>The actions in which the head may turn far from the chest: throws, tackles, dives, get-ups and the ground (ragdoll, fallen, slides).</summary>
        static bool HeadExempt(AgentAction a, SimAgentView v)
        {
            if (v.Ragdoll != null && v.Ragdoll.Active) return true;
            switch (a)
            {
                case AgentAction.Throw: case AgentAction.TackleWrapUp: case AgentAction.TackleBigHit: case AgentAction.TackleShoestring: case AgentAction.TackleDive: case AgentAction.TackleGang:
                case AgentAction.Tackled: case AgentAction.Dive: case AgentAction.GetUp: case AgentAction.Fallen: case AgentAction.Slide: return true;
                default: return false;
            }
        }

        void HeadSample(SimAgentView v, VT t, SimAgent a, bool contact)
        {
            t.PrevViewYaw = v.Yaw;
            var nb = v.Nat; if (nb == null || !nb.Ok) return;
            float hoc = nb.HeadTwist, ah = Mathf.Abs(hoc);      // the twist about the neck axis (the v0.12 yaw difference is kept as "v0.12 yaw measure" below)
            bool ex = HeadExempt(a.Action, v);
            string cls = ex ? "EXEMPT" : "FREE";
            HistAdd(_headDeg, "ALL " + cls, ah, 182, 1f);
            float rate = t.HocHave ? Mathf.Abs(Mathf.DeltaAngle(t.PrevHoc, hoc)) * 60f : -1f;
            if (rate >= 0f) HistAdd(_headRate, "ALL " + cls, rate, 200, 20f);
            if (!ex)
            {
                string g = Grp(a.PlayPos);
                HistAdd(_headDeg, "v0.12 yaw measure, free", Mathf.Abs(Mathf.DeltaAngle(nb.ChestYaw, nb.HeadYaw)), 182, 1f);
                HistAdd(_headDeg, "free act " + a.Action, ah, 182, 1f); HistAdd(_headDeg, "free grp " + g, ah, 182, 1f);
                if (a.Action == AgentAction.Stance) { HistAdd(_headDeg, "free stance " + v.Stance.Kind, ah, 182, 1f); }
                HistAdd(_headDeg, contact ? "free contact-driven" : "free not contact-driven", ah, 182, 1f);
                if (rate >= 0f) { HistAdd(_headRate, "free act " + a.Action, rate, 200, 20f); HistAdd(_headRate, "free grp " + g, rate, 200, 20f); }
            }
            else HistAdd(_headDeg, "exempt act " + a.Action, ah, 182, 1f);
            t.PrevHoc = hoc; t.HocHave = true;
        }

        void FeetProbe(int i, SimAgentView v, VT t, SimAgent a, PlaySimulation sim, float gy, float speedMs, float hipsY, float lowest, bool contact)
        {
            bool below = lowest < -0.07f;
            var act = a.Action;
            bool ctl = !below && (act == AgentAction.Block || act == AgentAction.Engaged || act == AgentAction.Shed || act == AgentAction.TackleWrapUp || act == AgentAction.TackleGang || act == AgentAction.Tackled);
            if (below ? (_frame % 20 != 0) : (!ctl || _frame % 45 != 0)) return;
            float lth = (t.LT != null ? t.LT.position.y : t.LF.position.y - 0.09f) - gy, rth = (t.RT != null ? t.RT.position.y : t.RF.position.y - 0.09f) - gy;
            float lah = t.LF.position.y - gy - 0.09f, rah = t.RF.position.y - gy - 0.09f;
            string bone = lowest == lth ? "Ltoe" : lowest == rth ? "Rtoe" : lowest == lah ? "Lankle" : "Rankle";
            var st = v.Stepper;
            var sb = new StringBuilder();
            sb.Append("lowest=").Append(P(lowest)).Append(";bone=").Append(bone).Append(";contact=").Append(contact ? 1 : 0).Append(";ragdoll=").Append(v.Ragdoll != null && v.Ragdoll.Active ? 1 : 0)
              .Append(";stepW=").Append(st != null ? P(st.Weight) : "").Append(";swing=").Append(st != null ? (st.SwingL ? "L" : "") + (st.SwingR ? "R" : "") : "").Append(";pull=").Append(st != null ? P(st.PelvisPull) : "")
              .Append(";stanceOn=").Append(v.StanceLayerOn ? 1 : 0).Append(";hipsLocalY=").Append(P(v.transform.InverseTransformPoint(t.Hips.position).y));
            ProbeRow(below ? "below" : "blk", i, v, t, a, sim, gy, speedMs, hipsY, sb.ToString());
        }

        // ---------------------------------------------------------------------------------------------------------- v0.13.0 facing probe
        readonly StringBuilder _faceCsv = new StringBuilder();
        bool _faceHeader;
        /// <summary>One row at the start of every counted FACE_BODY_70DEG standing run (frames = 20) and one at its end (frames = the length): the phase, the action and stance, the clip, and the four headings (view root, hips, shoulders, measured pose) against the simulated facing, to tell a clip's baked yaw from a stance / view yaw from a body on his way round.</summary>
        void FaceProbe(SimAgentView v, VT t, SimAgent a, PlaySimulation sim, BodyPose pose, float simYaw, float speedMs, float errF, int frames, bool end)
        {
            if (!_faceHeader) { _faceCsv.AppendLine("end,frames,play,frame,matchPhase,simPhase,simKind,agent,pos,side,act,stance,clip,errPose,errView,errHips,errShoulders,viewSpeed,simSpeed,huddle,contact,stanceOn,stanceW,viewTurnRate"); _faceHeader = true; }
            float eV = Mathf.Abs(StanceRules.YawDelta(v.Yaw, simYaw)), eH = Mathf.Abs(StanceRules.YawDelta(pose.HipsYaw, simYaw)), eS = Mathf.Abs(StanceRules.YawDelta(pose.ShouldersYaw, simYaw));
            float turn = Mathf.Abs(StanceRules.YawDelta(v.Yaw, t.PrevViewYaw)) * 60f;
            _faceCsv.Append(end ? 1 : 0).Append(',').Append(frames).Append(',').Append(_dir.PlayNumber).Append(',').Append(_frame).Append(',').Append(_dir.Phase).Append(',').Append(sim.Phase).Append(',').Append(sim.Kind).Append(',').Append(a.Id).Append(',').Append(a.PlayPos).Append(',').Append(a.Side).Append(',').Append(a.Action).Append(',').Append(v.Stance.Kind).Append(',')
                .Append((v.Rig != null ? v.Rig.CurrentClipName : "").Replace(',', '_')).Append(',').Append(F(errF)).Append(',').Append(F(eV)).Append(',').Append(F(eH)).Append(',').Append(F(eS)).Append(',').Append(F(t.ViewSpeed)).Append(',').Append(F(speedMs)).Append(',').Append(v.Huddle ? 1 : 0).Append(',').Append(v.ContactDriven ? 1 : 0).Append(',')
                .Append(v.StanceLayerOn ? 1 : 0).Append(',').Append(v.StanceLayer != null ? F(v.StanceLayer.Weight) : "").Append(',').Append(F(turn)).AppendLine();
        }
        void FaceFlush() { if (_faceCsv.Length == 0) return; File.AppendAllText(_out + "/face_probe.csv", _faceCsv.ToString()); _faceCsv.Length = 0; }

        string StanceHeadReport()
        {
            var sb = new StringBuilder("== v0.13 head over chest (deg, frames of every drawn body) ==\n");
            foreach (var kv in _headDeg)
            {
                var h = kv.Value; if (h.N == 0) continue;
                AngHist r; _headRate.TryGetValue(kv.Key, out r);
                sb.AppendLine(kv.Key + " : n=" + h.N + " mean " + (h.Sum / h.N).ToString("0.0", CI) + " p50 " + h.Pct(0.5).ToString("0.0", CI) + " p95 " + h.Pct(0.95).ToString("0.0", CI) + " p99 " + h.Pct(0.99).ToString("0.0", CI) + " max " + h.Max.ToString("0.0", CI)
                    + " over45 " + h.Over(45f) + " (" + (100.0 * h.Over(45f) / h.N).ToString("0.00", CI) + "%) over60 " + h.Over(60f) + " (" + (100.0 * h.Over(60f) / h.N).ToString("0.00", CI) + "%)"
                    + (r != null && r.N > 0 ? " | turn rate deg/s p50 " + r.Pct(0.5).ToString("0", CI) + " p95 " + r.Pct(0.95).ToString("0", CI) + " p99 " + r.Pct(0.99).ToString("0", CI) + " max " + r.Max.ToString("0", CI) : ""));
            }
            string text = sb.ToString();
            try { File.WriteAllText(_out + "/head_yaw.txt", text); } catch { }
            return text;
        }
    }
}
