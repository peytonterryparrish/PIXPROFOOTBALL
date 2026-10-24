using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.11.0 RB: hands what the simulation decided about the ball carrier's style (carry arm, press, crouch, cut phase, planned move) to his view, and lets the sim's carry arm drive the ball.
    /// </summary>
    public sealed partial class PlayStage
    {
        void CarryHook(SimAgent a, SimAgentView v)
        {
            if (v == null || a == null || Sim == null) return;
            var f = new SimAgentView.CarryFeed();
            f.Has = CarryStyleConfig.Enabled && a.HasBall && a.CarryArm != 0;
            if (f.Has)
            {
                f.Arm = a.CarryArm; f.Press = a.CarryPress; f.Crouch = a.CarryCrouch; f.Two = a.CarryTwoHands; f.Tight = a.CarryTight; f.Protecting = a.Protecting;
                f.CutPhase = (int)a.CutPhase; f.CutKind = (int)a.CutKind; f.CutPlant = a.CutPlantFoot; f.CutAngle = a.CutAngle; f.CutEta = a.CutEta; f.CutT = a.CutT;
                f.Plan = (int)a.PlanMove; f.PlanEta = a.PlanEta; f.Move = (int)a.Move; f.MoveT = a.MoveTotal - a.MoveTime; f.MoveTotal = a.MoveTotal;
                f.SimSpeed = a.Speed * PlayFrame.YardsToMeters;
                int tid = (a.Move != CarrierMove.None || a.PlanMove != CarrierMove.None) ? (a.PlanThreat >= 0 ? a.PlanThreat : a.MoveThreatId) : a.CarryThreat;
                if (tid < 0) tid = a.CarryThreat;
                if (tid >= 0 && tid < 22) { f.HasThreat = true; f.ThreatPos = WorldPos(tid); f.ThreatSide = a.CarryThreatSide; }
            }
            v.SetCarryFeed(f);
        }

        /// <summary>
        /// v0.11.0 RB (B-023): after the whistle of a tackle or a sack the carrier is on the turf inside the tackle take while the sim ball is dead at the spot where it was ruled: drawn there the ball lay 1.5-4 m from the pile.
        /// A ball that is not held after such a play is kept in the last carrier's hand (the relaxed hold, then the official) until it is spiked or taken.
        /// </summary>
        bool PostWhistleBallGuard(BallSim b)
        {
            if (!CarryStyleConfig.Enabled || SimAgentView.DebugNoCarry || Sim == null || Sim.Phase != PlayPhase.Dead || b.State == BallState.Held || b.Holder >= 0) return false;
            if (Sim.EndReason != PixFootball.Sim.EndReason.Tackle && Sim.EndReason != PixFootball.Sim.EndReason.Sack) return false;
            int last = b.LastHolder; if (last < 0 || last >= 22 || _views[last] == null) return false;
            SimAgent h = Sim.Agents[last]; var v = _views[last];
            if ((Frame.ToUnity(b.Pos.XY) - Frame.ToUnity(h.Pos)).magnitude > 6f) return false;      // a fumble that rolled away is not this case
            if (BallFlowHeld(h, v)) return true;
            Ball.SetCarry(v, _carrySide[last] >= 0 ? 1 : -1); return true;
        }

        /// <summary>The carry arm is the sim's decision; the view animates the switch and tells the ball when it changes arms. True when handled.</summary>
        bool CarrySideHook(SimAgent h, SimAgentView v)
        {
            if (!CarryStyleConfig.Enabled || SimAgentView.DebugNoCarry || h.CarryArm == 0 || v == null) return false;
            int side = v.CarryArmShown != 0 ? v.CarryArmShown : h.CarryArm;
            _carrySide[h.Id] = side;
            // B-065: while a full-body take owns the arms (the hand-off take after the exchange, spin, truck) the ball sits in the carry-side palm where those arms are, not on a forearm the clip does not bend
            if (v.Rig != null && v.Rig.Valid && v.Rig.Weight > 0.25f && !v.Rig.UpperBodyActive && v.CarryWeight < 0.5f) { Ball.SetPalmHold(v, side); return true; }
            Ball.SetCarry(v, side);
            if (v.Motion != null) v.Motion.SetProtect(false, side);      // the carry layer holds the arm (the old tuck IK of BodyMotion would fight it)
            return true;
        }
    }
}
