using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>Open-field lane evaluation shared by ball carriers, scrambling QBs and returners.</summary>
    internal static class LaneFinder
    {
        public static float DirY(SimAgent c) { return c.Side == SimSide.Offense ? 1f : -1f; }

        /// <summary>
        /// Picks the best direction for a carrier. guide/guideWeight bias toward a designed path. Returns the direction and how many
        /// yards of lane are open along it (capped at look).
        /// </summary>
        public static Vec2 Best(PlaySimulation S, SimAgent c, Vec2 guide, float guideWeight, float look, Vec2 prevDir, out float openOut, out float valueOut)
        {
            float heldVal;
            return Best(S, c, guide, guideWeight, look, prevDir, out openOut, out valueOut, out heldVal);
        }

        /// <summary>As <see cref="Best(PlaySimulation,SimAgent,Vec2,float,float,Vec2,out float,out float)"/>, also returning the value of the lane the carrier is already on (<paramref name="prevDir"/>), so he can keep it unless something clearly beats it.</summary>
        public static Vec2 Best(PlaySimulation S, SimAgent c, Vec2 guide, float guideWeight, float look, Vec2 prevDir, out float openOut, out float valueOut, out float heldValue)
        {
            float dirY = DirY(c);
            heldValue = float.MinValue;
            Vec2 g = new Vec2(0f, dirY);
            float vc = Math.Max(c.Speed, c.Body.EffectiveVMax * 0.78f);
            Vec2 cur = c.Speed > 1.5f ? c.Vel.Norm() : g;
            float bestVal = float.MinValue;
            Vec2 bestDir = g;
            float bestOpen = look;
            int n = 0;
            // candidates: fan around goal direction + the guide + current heading
            int kmax = c.Speed < 4.5f ? 13 : 8;   // a slow runner can reverse field / cut back up to 155 degrees
            for (int k = -kmax; k <= kmax + 2; k++)
            {
                Vec2 d;
                if (k == kmax + 2) { if (prevDir.LenSq() < 0.25f) continue; d = prevDir.Norm(); }
                else if (k == kmax + 1) d = guide.LenSq() > 0.25f ? guide.Norm() : cur;
                else if (k == kmax) d = cur;
                else d = g.Rotate(k * 0.20f); // +-92 degrees in 11 degree steps
                n++;
                float open = look;
                float threat = 0f;
                for (int i = 0; i < 22; i++)
                {
                    SimAgent o = S.Agents[i];
                    if (o.Side == c.Side || o.State == AgentState.Down || o.Body.FallenTime > 0.35f) continue;
                    Vec2 rel = o.Pos - c.Pos;
                    float s = rel.Dot(d);
                    if (s < -0.4f || s > look + 1.5f) continue;
                    float lateral = Math.Abs(rel.Cross(d));
                    if (lateral > 3.2f) continue;
                    float sPos = Math.Max(0f, s);
                    float tC = sPos / vc;
                    if (o.State == AgentState.Engaged)
                    {
                        float wall = o.Body.Radius + 0.2f + c.Body.Radius * 0.5f;
                        if (lateral < wall) open = Math.Min(open, Math.Max(0.2f, s - 0.4f));
                        else if (lateral < wall + 0.7f) threat += 0.1f;
                        continue;
                    }
                    Vec2 pt = c.Pos + d * sPos;
                    float dist = (o.Pos - pt).Length;
                    float tD = Math.Max(0f, dist - (0.85f + o.Body.Radius)) / (o.Body.VMax * 0.9f) + (o.Body.StumbleTime > 0f ? 0.4f : 0f);
                    float margin = tD - tC;
                    if (margin < 0.3f)
                    {
                        float avoid = Math.Max(0.4f, s);
                        if (margin < -0.1f) open = Math.Min(open, avoid);
                        else open = Math.Min(open, avoid + (margin + 0.1f) * vc * 1.6f);
                    }
                    else if (margin < 0.8f) threat += 0.1f;
                }
                float prog = d.Y * dirY;
                float usable = Math.Min(open, look);
                float val = usable * (0.45f + 0.55f * Math.Max(0f, prog)) + 1.6f * prog - 0.9f * threat;
                if (c.Speed > 3f) val -= 1.15f * SimMath.AngleBetween(cur, d);
                // v0.10.0: a runner on a scrimmage play does not retreat toward his own goal line from a pursuer (a receiver caught at the 46 ran 57 yd backwards into his own end zone with a linebacker two yards behind him): going backward costs 3.2 per unit past a slight cutback
                if (S.Kind == PlayKind.Scrimmage && c.Slot != 0 && prog < -0.15f) val += 3.2f * (prog + 0.15f);
                // sidelines
                Vec2 end = c.Pos + d * Math.Min(usable, 6f);
                float toLine = FieldGeometry.DistanceToSideline(end.X);
                if (end.X < 0.8f || end.X > FieldGeometry.Width - 0.8f) val -= 4f;
                else if (toLine < 2.5f) val -= 1.0f;
                if (guideWeight > 0f && guide.LenSq() > 0.25f) val += guideWeight * (d.Dot(guide.Norm()) - 0.5f);
                if (prevDir.LenSq() > 0.25f) val += 0.55f * d.Dot(prevDir);
                // v0.9.0 (E-149): a runner at the line does not bounce along it looking for open grass: lateral drift is a cost there and the first two yards of vertical are worth something (the hole)
                if (S.Kind == PlayKind.Scrimmage && c.Side == SimSide.Offense && Math.Abs(c.Pos.Y - S.LosY) < 1.0f)
                {
                    val -= 0.6f * Math.Abs(d.X);
                    val += 0.8f * Math.Min(2f, Math.Max(0f, d.Y * dirY) * 2f);
                }
                if (k == kmax + 2) heldValue = val;
                if (val > bestVal) { bestVal = val; bestDir = d; bestOpen = usable; }
            }
            openOut = bestOpen;
            valueOut = bestVal;
            return bestDir;
        }
    }

    /// <summary>Drives whichever agent holds the ball: reads blocks, follows designed paths, picks lanes, uses moves.</summary>
    internal sealed class CarrierAI
    {
        private readonly PlaySimulation S;

        public CarrierAI(PlaySimulation s) { S = s; }

        public void Update(SimAgent c)
        {
            float dirY = LaneFinder.DirY(c);
            Vec2 guide = Vec2.Zero;
            float guideW = 0f;
            var carry = c.Assign as CarryAssignment;
            List<Vec2> path = null;
            if (carry != null && !carry.IsFake) path = carry.Path;
            else if (c.Assign is QbAssignment && c.Side == SimSide.Offense) path = ((QbAssignment)c.Assign).RunPath;

            bool pastLos = dirY > 0 ? c.Pos.Y > S.LosY + 0.5f : c.Pos.Y < S.LosY - 0.5f;
            if (S.Kind != PlayKind.Scrimmage) pastLos = true;
            if (path != null && path.Count > 0 && c.PathIdx < path.Count && !pastLos)
            {
                // advance along the designed path
                while (c.PathIdx < path.Count - 1 && (S.Rel(path[c.PathIdx]) - c.Pos).Length < 1.2f) c.PathIdx++;
                Vec2 wp = S.Rel(path[c.PathIdx]);
                guide = (wp - c.Pos).Norm();
                guideW = carry != null && (carry.Style == CarryStyle.Power || carry.Style == CarryStyle.Counter || carry.Style == CarryStyle.Trap || carry.Style == CarryStyle.Sweep || carry.Style == CarryStyle.Toss) ? 4.5f : 3.0f; // v0.9.0 (E-150): designed holes behind pullers are followed (4.5), zone and draw runs read but start on the design (3.0) until he crosses the line
            }
            else if (path != null && path.Count > 0 && c.PathIdx < path.Count)
            {
                Vec2 wp = S.Rel(path[path.Count - 1]);
                guide = (wp - c.Pos).Norm();
                guideW = 0.5f;
            }

            var retA = c.Assign as ReturnerAssignment;
            if (retA != null && S.Kind != PlayKind.Scrimmage && path == null)
            {
                // the designed return lane: head for it until the blocking shows something better
                Vec2 lanePt = new Vec2(S.BallX0 + retA.ReturnLaneX, c.Pos.Y + 10f * dirY);
                guide = (lanePt - c.Pos).Norm();
                guideW = 0.9f;
            }
            float look = 8f;
            float dist2Goal = dirY > 0 ? 100f - c.Pos.Y : c.Pos.Y;
            if (dist2Goal < look) look = Math.Max(2.5f, dist2Goal + 0.5f);

            c.CarrierClock -= PlaySimulation.Dt;
            // nearest defender: a new threat inside 3 yd ends the commitment early (he must be able to react), and decides whether he covers the ball
            float nearDef = 99f;
            for (int i = 0; i < 22; i++)
            {
                SimAgent o = S.Agents[i];
                if (o.Side == c.Side || o.State == AgentState.Down || o.Body.FallenTime > 0.35f) continue;
                float dd = (o.Pos - c.Pos).Length;
                if (dd < nearDef) nearDef = dd;
            }
            if (c.CarrierClock <= 0f || c.LastTargetDir.LenSq() < 0.25f)
            {
                float aw = SimMath.Rating01(c.R.Awareness);
                float open, val, heldVal;
                Vec2 d = LaneFinder.Best(S, c, guide, guideW, look, c.LastTargetDir, out open, out val, out heldVal);
                // v0.9.0 (E-140): he commits to a lane for 0.35 s and only switches by more than 30 degrees when the new lane is clearly better than the one he is on
                if (c.LastTargetDir.LenSq() > 0.25f && heldVal > float.MinValue && SimMath.AngleBetween(d, c.LastTargetDir) > 0.52f && val - heldVal < 1.2f && nearDef > 1.6f) d = c.LastTargetDir;
                // low awareness: occasionally misreads and sticks with the designed track
                if (S.Rng.NextFloat() < (1f - aw) * 0.18f && guide.LenSq() > 0.25f) d = guide.Norm();
                c.LastTargetDir = d;
                c.PathIdxOpen = open;
                c.CarrierClock = (nearDef < 3.0f ? 0.16f : 0.35f) + (1f - aw) * 0.08f;
            }
            else if (nearDef < 1.4f && c.CarrierClock > 0.12f) c.CarrierClock = 0.12f;   // contact is coming: re-read now
            // v0.9.0 (E-160): a carrier with a defender closing covers the ball (the arm away from him); the ball is switched to the far side
            if (!c.ControlledByUser && c.Slot != 0)
            {
                c.Protecting = nearDef < 2.2f && c.Move == CarrierMove.None;
                if (nearDef < 3.2f)
                {
                    SimAgent nd = null; float nbd = 99f;
                    for (int i = 0; i < 22; i++) { SimAgent o = S.Agents[i]; if (o.Side == c.Side || o.State == AgentState.Down) continue; float dd = (o.Pos - c.Pos).Length; if (dd < nbd) { nbd = dd; nd = o; } }
                    if (nd != null) { Vec2 rel = nd.Pos - c.Pos; Vec2 fw = c.Speed > 1.5f ? c.Vel.Norm() : c.Facing; c.BallSide = rel.Dot(fw.Perp()) > 0f ? -1 : 1; }
                }
            }
            Vec2 dir = c.LastTargetDir;
            float cap = c.Body.EffectiveVMax * 0.955f;   // a carrier with the ball, reading blocks and protecting it, runs about 92% of his open-field top speed
            // v0.9.0 (E-157): a planned cut is made from a brake, not from full speed (the cut model always started at top speed)
            if (c.Speed > 3.5f)
            {
                float turn = SimMath.AngleBetween(c.Vel, dir);
                if (turn > 0.70f) cap *= SimMath.Lerp(1f, 0.62f, SimMath.Clamp01((turn - 0.70f) / 2.4f));
            }
            if (S.Kind == PlayKind.Punt && c.ReturnerRole) cap *= 0.90f;   // a punt returner fields the ball on the move and looks for the fair-catch, then the coverage: slower to the open field
            if (c.PathIdxOpen < 1.8f) cap *= 0.88f + 0.06f * c.PathIdxOpen;
            if (c.Protecting && c.ControlledByUser) cap *= 0.94f;
            c.Want = dir * cap;
            c.Intent = AgentAction.Carry;
            c.Exertion = 0f;
            S.Moves.ConsiderAIMoves(c);
        }
    }
}
