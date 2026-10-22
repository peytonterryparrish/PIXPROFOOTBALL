using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// Classifies every player's pre-snap stance from his position, alignment and the formation so the presentation layer never has to guess
    /// an OL from a CB. Pure and deterministic (no random draws).
    /// </summary>
    internal static class PreSnapStances
    {
        public static void Refresh(PlaySimulation s)
        {
            if (s == null || s.Phase != PlayPhase.PreSnap) return;
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = s.Agents[i];
                if (a != null) a.StanceKind = Classify(s, a);
            }
        }

        public static PreSnapStance Classify(PlaySimulation s, SimAgent a)
        {
            Position pos = a.PlayPos;
            bool off = a.Side == SimSide.Offense;
            float depth = off ? a.Pos.Y - s.LosY : a.Pos.Y - s.LosY;   // offense: negative = backfield, defense: positive = off the ball
            float relX = a.Pos.X - s.BallX0;
            if (a.ReturnerRole) return PreSnapStance.Returner;
            if (s.Kind == PlayKind.Kickoff)
            {
                if (a == s.Specials.KickerAgent || pos == Position.K) return PreSnapStance.Kicker;
                return PreSnapStance.KickoffRunner;
            }
            if (s.Kind == PlayKind.Punt || s.Kind == PlayKind.FieldGoal)
            {
                if (off)
                {
                    if (pos == Position.P || pos == Position.K) return PreSnapStance.Kicker;
                    if (a == s.Specials.HolderAgent) return PreSnapStance.HolderKneel;
                    if (a == s.Specials.SnapperAgent) return PreSnapStance.Snapper;
                    if (s.Kind == PlayKind.Punt && pos == Position.WR && Math.Abs(relX) > 12f) return PreSnapStance.Gunner;
                    if (PositionGroup.IsOffensiveLine(pos) || (pos == Position.TE && depth > -0.6f)) return PreSnapStance.ThreePoint;
                    return depth > -0.6f ? PreSnapStance.TwoPoint : PreSnapStance.BackSet;
                }
            }
            if (PositionGroup.IsOffensiveLine(pos)) return pos == Position.C ? PreSnapStance.Snapper : PreSnapStance.ThreePoint;
            if (PositionGroup.IsDefensiveLine(pos))
            {
                if (Math.Abs(relX) > 6f) return PreSnapStance.TwoPoint;   // wide-9 edge rusher
                bool shortYardage = s.State != null && (s.State.Distance <= 2 || s.State.YardsToGoal <= 3);
                return pos == Position.DT && depth < 1.1f && shortYardage ? PreSnapStance.FourPoint : PreSnapStance.ThreePoint;
            }
            if (PositionGroup.IsLinebacker(pos)) return PreSnapStance.Linebacker;
            if (pos == Position.CB) return depth <= 3.2f ? PreSnapStance.PressCorner : PreSnapStance.OffCoverage;
            if (PositionGroup.IsSafety(pos)) return PreSnapStance.Safety;
            switch (pos)
            {
                case Position.QB: return depth < -3f ? PreSnapStance.QbShotgun : PreSnapStance.QbUnderCenter;
                case Position.RB: return PreSnapStance.BackSet;
                case Position.FB: return depth > -4.5f && depth < -2.5f ? PreSnapStance.ThreePoint : PreSnapStance.BackSet;
                case Position.TE: return depth > -0.6f && Math.Abs(relX) < 6.5f ? PreSnapStance.ThreePoint : PreSnapStance.TwoPoint;
                case Position.WR: return PreSnapStance.TwoPoint;
                case Position.K: case Position.P: return PreSnapStance.Kicker;
            }
            return PreSnapStance.Upright;
        }
    }
}
