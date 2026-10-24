using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 RB track: the ball-carrier move takes. The simulation decides the move before it happens (<see cref="CarryFeed.Plan"/> with its lead <see cref="CarryFeed.PlanEta"/>); the view starts the
    /// matching ANIMLIB take (<see cref="ClipSelect"/>: ball-carrier jukes, dodges and tempo changes) as an UPPER-BODY plan over the motion matcher's legs, with the clip's plant moment synced to the
    /// moment the sim starts the lateral move. The legs stay the matcher's (foot-locked: it already follows the sim's planned path through the cut), the arms stay the carry layer's.
    /// The stiff arm is the carry layer's free-arm extension (SimAgentView.Carry.cs), so its animator trigger is not fired.
    /// </summary>
    public partial class SimAgentView
    {
        int _prevPlanK, _prevMoveK, _takeSeq; float _takeUntilK = -9f; AnimationClip _takeClipK;
        /// <summary>QA / audit: name of the carry take that is playing and when it started (clock seconds).</summary>
        public string CarryTakeName { get { return _takeClipK != null && _clock < _takeUntilK ? _takeClipK.name : ""; } }

        public static bool DebugNoCarryTake = true, DebugNoCarrySink;      // the clip take is OFF by default: an upper-body juke take brings its own pelvis and dragged the planted feet (slide p50 0.2 -> 1.0-1.5 m/s, rb.md); -pixCarryTake brings it back
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void CarryTakeSwitch() { var a = System.Environment.GetCommandLineArgs(); if (System.Array.IndexOf(a, "-pixCarryTake") >= 0) DebugNoCarryTake = false; if (System.Array.IndexOf(a, "-pixCarryNoTake") >= 0) DebugNoCarryTake = true; if (System.Array.IndexOf(a, "-pixCarryNoSink") >= 0) DebugNoCarrySink = true; }

        static bool CarryMoveAction(AgentAction a) { return a == AgentAction.Juke || a == AgentAction.StiffArm; }

        /// <summary>The carry layer owns this move action: the animator trigger / one-shot clip is not played (the take is already running or the layer does the move itself).</summary>
        bool CarryOwnsMove(AgentAction action)
        {
            if (DebugNoCarry || !CarryStyleConfig.Enabled || !_cfHave || !_cf.Has || ContactDriven) return false;
            return action == AgentAction.StiffArm || action == AgentAction.Juke || (action == AgentAction.Truck && _clock < _takeUntilK);      // both are drawn by the carry layer (free-arm extension; shoulder / head fake with the sink)
        }

        void CarryTakeService()
        {
            if (DebugNoCarry || !CarryStyleConfig.Enabled || !_cfHave || Rig == null || !Rig.Valid || ContactDriven || !_cf.Has) { _prevPlanK = _prevMoveK = 0; return; }
            var f = _cf;
            bool planStart = f.Plan != 0 && _prevPlanK == 0;
            bool moveStart = f.Move != 0 && _prevMoveK == 0;
            _prevPlanK = f.Plan; _prevMoveK = f.Move;
            if (!planStart && !(moveStart && _clock >= _takeUntilK)) return;
            var m = (CarrierMove)(planStart ? f.Plan : f.Move);
            if (m == CarrierMove.Truck)
            {
                // ANIMLIB truck_alt (QA-scored, tagged): a full-body take, so the carry layer steps out for it; the contact (lowered shoulder) is at the sim's lead
                var tp = AnimKit.TruckAlt(AgentId * 7 + _takeSeq++, planStart ? f.PlanEta + 0.04f : 0.04f);
                if (tp.Valid && Rig != null) { Rig.Play(tp); _ownsClip = true; _clipYaw = ClipFacingTable.Offset(tp.Clip); _takeClipK = tp.Clip; _takeUntilK = _clock + (planStart ? f.PlanEta : 0f) + Mathf.Min(1.2f, tp.Clip.length); }
                return;
            }
            if (DebugNoCarryTake) return;
            if (m != CarrierMove.JukeLeft && m != CarrierMove.JukeRight) return;
            float eta = planStart ? f.PlanEta + 0.04f : 0.04f;
            var clip = PickJukeClip(m == CarrierMove.JukeLeft ? -1 : 1, _takeSeq++);
            if (clip == null) return;
            var plan = ClipSelect.Synced(clip, eta, 0.3f, 0.45f, 0.12f, 0.22f);
            if (!plan.Valid) return;
            plan.UpperBody = true; plan.MaskSpeed = 1.2f;
            Rig.Play(plan); _ownsClip = true; _clipYaw = ClipFacingTable.Offset(plan.Clip);
            _takeClipK = clip; _takeUntilK = _clock + eta + 0.9f;
        }

        AnimationClip PickJukeClip(int dir, int seq)
        {
            var pool = new List<AnimationClip>();
            foreach (string key in new[] { "juke_left", "juke_right", "juke_tempo" })
            {
                foreach (var c in ClipSelect.Query(key, PlayerRole.RB))
                {
                    var e = ClipSelect.Tag(c); if (e == null) continue;
                    if (e.len > 2.4f || e.len < 0.5f) continue;
                    // the direction the take goes (sign of its root travel direction): the juke goes where the sim sends him
                    if (Mathf.Abs(e.rootDir) >= 5f && Mathf.Sign(e.rootDir) != dir) continue;
                    if (Mathf.Abs(e.rootDir) < 5f && key != "juke_tempo") continue;
                    pool.Add(c);
                }
            }
            if (pool.Count == 0) return ClipSelect.Pick(dir < 0 ? "juke_left" : "juke_right", PlayerRole.RB, AgentId + seq);
            return pool[((AgentId * 7 + seq * 3) % pool.Count + pool.Count) % pool.Count];
        }
    }
}
