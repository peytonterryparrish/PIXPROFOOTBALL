using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    public sealed partial class PlaySimulation
    {
        /// <summary>v0.11.0 RB: a player takes the ball: his carry style starts from nothing (arm chosen by the first read, no cut, no move plan).</summary>
        internal void ResetCarryStyle(SimAgent a)
        {
            a.CarryArm = 0; a.CarryArmPrev = 0; a.CarryArmWant = 0; a.CarryArmSince = Time - 5f;
            a.CarryPress = 0f; a.CarryCrouch = 0f; a.CarryTwoHands = false; a.CarryTight = false; a.CarryThreat = -1;
            a.CutPhase = CarryCutPhase.None; a.CutKind = CarryCutKind.None; a.CutCooldownUntil = Time + 0.3f;
            a.PlanMove = CarrierMove.None; a.PlanEta = 0f; a.PlanThreat = -1;
        }

        /// <summary>v0.11.0 RB: for the motion matcher's planned path: a carrier in the Approach of a cut will steer along the new line from the plant on (desired velocity = the cut direction at the speed he can carry through it).</summary>
        internal void CarryPlanAhead(SimAgent a, out float tSwitch, out float wx, out float wy)
        {
            tSwitch = -1f; wx = wy = 0f;
            if (!CarryStyleConfig.Enabled || !a.HasBall) return;
            if (a.PlanMove == CarrierMove.JukeLeft || a.PlanMove == CarrierMove.JukeRight)
            {
                // a planned juke: the lateral steering of the move is already in the path the matcher sees (it picks the cut / strafe frames and the plant foot for it)
                Vec2 h = a.Speed > 1.2f ? a.Vel.Norm() : a.Facing; Vec2 side = h.Perp() * (a.PlanMove == CarrierMove.JukeLeft ? 1f : -1f);
                tSwitch = Math.Max(0f, a.PlanEta); wx = a.Vel.X * 0.92f + side.X * 3.2f; wy = a.Vel.Y * 0.92f + side.Y * 3.2f; return;
            }
            if (a.CutPhase != CarryCutPhase.Approach) return;
            float sp = Math.Max(a.Speed * 0.82f, 3f);
            tSwitch = Math.Max(0f, a.CutEta);
            wx = a.CutDir.X * sp; wy = a.CutDir.Y * sp;
        }
    }
}
