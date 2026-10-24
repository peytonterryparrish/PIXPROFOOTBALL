using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.11.0 RB track: the carrier decides his move BEFORE it happens (the move is planned while the defender is still 0.5-0.75 s from contact and executed about 0.25 s later, so the
    /// view can play the entry of the matching take across the lead), from the defender's leverage, the carrier's ratings, the situation and a per-carry move budget.
    /// Real-life basis: backs set up one move per defender (a jump-cut or juke when the tackler is head-on and the space is on the side, a stiff-arm when he is coming at an angle along the
    /// shoulder, a lowered shoulder against a smaller man, a spin only when there is room and the ball is secure); a back protecting a lead in the fourth quarter takes the safe yard.
    /// </summary>
    internal sealed partial class CarrierMoves
    {
        static float StyleTtcMax { get { return CarryStyleConfig.Enabled ? 0.74f : 0.50f; } }
        static float StylePass { get { return CarryStyleConfig.Enabled ? 0.52f : 0.75f; } }

        void StyleClear(SimAgent c) { c.PlanMove = CarrierMove.None; c.PlanEta = 0f; c.PlanThreat = -1; }

        /// <summary>Executes or calls off a move that was planned earlier. True while a plan is pending (the old decision code stands down).</summary>
        bool StylePlanTick(SimAgent c)
        {
            if (c.PlanMove == CarrierMove.None) return false;
            if (!c.HasBall || c.State != AgentState.Normal || c.Body.FallenTime > 0f || c.Move != CarrierMove.None) { StyleClear(c); return false; }
            SimAgent th = c.PlanThreat >= 0 ? S.Agents[c.PlanThreat] : null;
            if (th == null || th.IsDown || (th.Pos - c.Pos).Length > 4.6f) { StyleClear(c); c.MoveThreatId = -1; return false; }
            c.PlanEta -= PlaySimulation.Dt;
            if (c.PlanEta <= 0f)
            {
                CarrierMove m = c.PlanMove; StyleClear(c);
                if (TryMove(c, m)) c.CarryMoveCount++;
                return false;
            }
            return true;
        }

        void StyleQueue(SimAgent c, CarrierMove m, float ttc, int threatId)
        {
            if (!CarryStyleConfig.Enabled || c.ControlledByUser) { TryMove(c, m); return; }
            float eta = SimMath.Clamp(ttc - 0.50f, 0f, 0.26f);
            if (eta <= 0.01f) { if (TryMove(c, m)) c.CarryMoveCount++; return; }
            c.PlanMove = m; c.PlanThreat = threatId; c.PlanEta = eta; c.PlanTtc = ttc;
        }

        /// <summary>Which way the juke goes: away from the defender's lean, toward space (a second defender or the sideline on that side vetoes it).</summary>
        CarrierMove StyleJukeSide(SimAgent c, SimAgent threat, Vec2 heading, float lat)
        {
            if (!CarryStyleConfig.Enabled) return lat > 0f ? CarrierMove.JukeRight : CarrierMove.JukeLeft;
            Vec2 perp = heading.Perp();      // to the LEFT
            float lean = threat.Vel.Dot(perp) * 0.3f;
            float score = lat + lean;      // > 0: he is (or leans) left: juke right
            // space on each side within 3.2 yd (other defenders, the sideline)
            float spaceL = 3.2f, spaceR = 3.2f;
            for (int i = 0; i < 22; i++)
            {
                SimAgent o = S.Agents[i]; if (o.Side == c.Side || o == threat || o.IsDown) continue;
                Vec2 rel = o.Pos - c.Pos; float al = rel.Dot(heading); if (al < -0.5f || al > 3.5f) continue;
                float l = rel.Dot(perp); float d = Math.Abs(l);
                if (l > 0f) spaceL = Math.Min(spaceL, d); else spaceR = Math.Min(spaceR, d);
            }
            float toLeftLine = c.Pos.X, toRightLine = FieldGeometry.Width - c.Pos.X;      // heading is mostly +Y here, left (Perp) is toward -X
            if (Math.Abs(heading.Y) > 0.5f) { float sgn = heading.Y > 0f ? 1f : -1f; if (sgn > 0f) { spaceL = Math.Min(spaceL, toLeftLine); spaceR = Math.Min(spaceR, toRightLine); } else { spaceL = Math.Min(spaceL, toRightLine); spaceR = Math.Min(spaceR, toLeftLine); } }
            float v = score * 1.0f + (spaceR - spaceL) * 0.35f;      // v > 0 -> juke right
            return v > 0f ? CarrierMove.JukeRight : CarrierMove.JukeLeft;
        }

        /// <summary>Adjusts the weights of the old decision with leverage, situation and budget.</summary>
        void StyleWeights(SimAgent c, SimAgent threat, ref float wJuke, ref float wSpin, ref float wStiff, ref float wTruck, ref float wHurdle)
        {
            if (!CarryStyleConfig.Enabled || c.ControlledByUser) return;
            // budget: the more he has done this carry, the less he does (one move per defender, rarely three per carry)
            float budget = 1f / (1f + 0.9f * c.CarryMoveCount);
            Vec2 heading = c.Speed > 1.2f ? c.Vel.Norm() : c.Facing;
            Vec2 rel = threat.Pos - c.Pos; Vec2 relN = rel.Norm();
            float closing = (c.Vel - threat.Vel).Dot(relN);
            float atAngle = Math.Abs(relN.Dot(heading.Perp()));      // 0 = head-on, 1 = square off his shoulder
            // leverage: head-on defender -> juke / truck / spin; shoulder angle -> stiff-arm; a defender coming from behind-side is a spin or a straight run
            wJuke *= (0.7f + 0.7f * (1f - atAngle)) * 0.6f;
            wStiff *= (0.5f + 1.0f * atAngle) * 1.7f;
            wTruck *= (0.6f + 0.7f * (1f - atAngle)) * 1.4f;
            // a spin needs room and a secure ball
            float room = 9f;
            for (int i = 0; i < 22; i++) { SimAgent o = S.Agents[i]; if (o.Side == c.Side || o == threat || o.IsDown) continue; float d = (o.Pos - c.Pos).Length; if (d < room) room = d; }
            if (room < 2.6f) { wSpin *= 0.3f; wJuke *= 0.6f; }
            if (FieldGeometry.DistanceToSideline(c.Pos.X) < 2.0f) wSpin *= 0.4f;
            wSpin *= 1f - 0.4f * SimMath.Clamp01(S.Options.BallSlickness);
            // situation: protecting a lead late
            var st = S.State;
            if (st != null && st.Quarter >= 4 && st.ClockSeconds < 300f && st.PossessionScoreDiff > 0 && st.PossessionScoreDiff <= 16 && c.Side == SimSide.Offense)
            {
                wSpin *= 0.35f; wJuke *= 0.6f; wHurdle *= 0.8f;
            }
            // a faster, fresher back does less (he is already past the man): a move at speed against a slow-closing tackler is a waste
            if (closing < 4.0f) { wSpin *= 0.7f; wJuke *= 0.8f; }
            wSpin *= 1.35f;      // the budget and the pass rate cut spins as much as jukes; spins are rare already (about 1 per 60 carries)
            wJuke *= budget; wSpin *= budget; wStiff *= budget; wTruck *= budget; wHurdle *= budget;
        }
    }
}
