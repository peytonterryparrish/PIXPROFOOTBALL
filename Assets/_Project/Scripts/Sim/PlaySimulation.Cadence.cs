using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.9.0 pre-snap behaviour of the computer side: E-323 / E-366 / E-374: with <see cref="PlaySimOptions.Cadence"/> an automatic snap follows a cadence: the offense may send a man in motion, the defense may show a blitz,
    /// and the ball is snapped 1.4-2.2 s after the formation settles (a snap count).
    /// v0.12.0 (RULES): the count itself and everything the pre-snap fouls hang on (who jumps it, the shift, the slow snap) moved to <c>Sim/Rules/PreSnapFouls.cs</c>; this file keeps the headless cadence entry and the
    /// seconds-to-snap property. The old E-332 "visible jump" is now a real early mover (see <see cref="PlaySimulation.EarlyMoverId"/>).
    /// </summary>
    public sealed partial class PlaySimulation
    {
        /// <summary>Seconds from the first live pre-snap step until the cadence snaps the ball (the options' fixed delay when no count was planned).</summary>
        public float PlannedSnapSeconds { get { return _countPlanned ? _snapAt : (Options != null ? Options.AutoSnapDelay : 0.6f); } }

        /// <summary>The automatic snap's count: the cadence (1.4-2.2 s) with its motion / shift / disguise when <see cref="PlaySimOptions.Cadence"/> is on, the fixed delay otherwise.</summary>
        private void PlanCadence()
        {
            float len = Options.Cadence && Kind == PlayKind.Scrimmage ? 1.4f + 0.8f * PreRng.NextFloat() : Options.AutoSnapDelay;
            PlanCount(len, -1f, true, Options.Cadence && Kind == PlayKind.Scrimmage);
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
