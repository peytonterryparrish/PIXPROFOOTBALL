using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.9.0 pre-snap behaviour of the computer side and the visible fouls:
    /// E-323 / E-366 / E-374: with <see cref="PlaySimOptions.Cadence"/> an automatic snap follows a cadence: the offense may send a man in motion, the defense may show a blitz, and the ball is snapped 1.4-2.2 s
    /// after the formation settles (a snap count). E-332: a false start, an offside or an encroachment is a man who actually jumps (0.2 s of movement toward the line) before the whistle.
    /// </summary>
    public sealed partial class PlaySimulation
    {
        private bool _cadencePlanned;
        private float _cadenceSnapAt;

        /// <summary>Seconds from the first live pre-snap step until the cadence snaps the ball (the options' fixed delay when the cadence is off).</summary>
        public float PlannedSnapSeconds { get { return Options != null && Options.Cadence && _cadencePlanned ? _cadenceSnapAt : (Options != null ? Options.AutoSnapDelay : 0.6f); } }

        private void PlanCadence()
        {
            _cadencePlanned = true;
            _cadenceSnapAt = Options.AutoSnapDelay;
            if (Kind != PlayKind.Scrimmage) return;
            float snapAt = 1.4f + 0.8f * Rng.NextFloat();
            // a man in motion: a third of the passes and a fifth of the runs with an eligible back or receiver who starts off the line
            if (MotionSlot < 0 && Rng.NextFloat() < (OffPlay.IsPass ? 0.30f : 0.20f))
            {
                var cands = new List<int>();
                for (int s = 1; s < 11; s++) if (CanMotion(s) && !_hotRoutes.ContainsKey(s)) cands.Add(s);
                if (cands.Count > 0)
                {
                    int slot = cands[Rng.Range(0, cands.Count)];
                    int dir = Agents[slot].Pos.X >= BallX0 ? -1 : 1;   // across the formation
                    SetMotion(slot, dir);
                    snapAt = Math.Max(snapAt, 1.9f + 0.4f * Rng.NextFloat());
                }
            }
            // the defense disguises: a blitz call shows the blitz about a third of the time
            bool blitz = false;
            for (int i = 0; i < 11; i++) if (DefPlay.Assignments[i] is BlitzAssignment) blitz = true;
            if (blitz && Rng.NextFloat() < 0.35f)
            {
                DefenseAdjust adj = DefAdjust != null ? DefAdjust.Clone() : new DefenseAdjust();
                adj.Blitz = BlitzShow.Show;
                SetDefenseAdjust(adj);
            }
            _cadenceSnapAt = snapAt;
        }

        // ---------------------------------------------------------------- the visible jump

        private sealed class FlinchState { public int Idx; public float Dir; public float T; public PlayResult Result; }
        private FlinchState _flinch;

        private void BeginFlinch(int agentIdx, float dirY, PlayResult result)
        {
            _flinch = new FlinchState { Idx = agentIdx, Dir = dirY, T = 0f, Result = result };
        }

        /// <summary>The culprit's desired velocity while he jumps (0.2 s toward the line), or zero.</summary>
        private Vec2 FlinchWant(int agentIdx)
        {
            if (_flinch == null || _flinch.Idx != agentIdx) return Vec2.Zero;
            return new Vec2(0f, _flinch.Dir * 3.4f);
        }

        private void StepFlinch()
        {
            _flinch.T += Dt;
            if (_flinch.T < 0.22f) return;
            PlayResult r = _flinch.Result;
            _flinch = null;
            FinishPreSnap(r);
        }
    }
}

namespace PixFootball.Sim
{
    public sealed partial class PlaySimulation
    {
        /// <summary>
        /// v0.9.0 (E-156): a jet sweep is a play WITH motion: the sweep runner goes across the formation before the snap (a jog, 4.5 yd/s, he stops short of the far side), so the quick pitch finds him at speed at the
        /// edge. The hand-off timeline is now realistic (the pitch no longer leaves at 0.28 s), and a man who starts 8-10 yd from the quarterback needs the motion to get there in time.
        /// </summary>
        private bool IsJetMotion(int slot)
        {
            if (OffAI == null || OffAI.QbPlanAction != QbAction.Toss || OffAI.HandoffTargetId != slot) return false;
            var ca = Agents[slot].Assign as CarryAssignment;
            return ca != null && ca.Style == CarryStyle.Sweep && Agents[slot].PlayPos == Position.WR;
        }

        private void AutoJetMotion()
        {
            if (Kind != PlayKind.Scrimmage || OffAI == null || OffAI.QbPlanAction != QbAction.Toss) return;
            int slot = OffAI.HandoffTargetId;
            if (slot < 1 || slot > 10 || MotionSlot >= 0) return;
            SimAgent runner = Agents[slot];
            var ca = runner.Assign as CarryAssignment;
            if (ca == null || ca.Style != CarryStyle.Sweep || ca.Path.Count == 0 || runner.PlayPos != Position.WR || !CanMotion(slot)) return;
            float pathSide = ca.Path[0].X + (Rel(ca.Path[ca.Path.Count - 1]).X - BallX0 - ca.Path[0].X) * 0f;
            float toward = (BallX0 + ca.Path[0].X) - runner.Pos.X;
            if (Math.Abs(toward) < 2f) return;
            SetMotion(slot, toward > 0f ? 1 : -1);
        }
    }
}
