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
            float dirY = DirY(c);
            Vec2 g = new Vec2(0f, dirY);
            float vc = Math.Max(c.Speed, c.Body.EffectiveVMax * 0.78f);
            Vec2 cur = c.Speed > 1.5f ? c.Vel.Norm() : g;
            float bestVal = float.MinValue;
            Vec2 bestDir = g;
            float bestOpen = look;
            int n = 0;
            // candidates: fan around goal direction + the guide + current heading
            for (int k = -8; k <= 9; k++)
            {
                Vec2 d;
                if (k == 9) d = guide.LenSq() > 0.25f ? guide.Norm() : cur;
                else if (k == 8) d = cur;
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
                // sidelines
                Vec2 end = c.Pos + d * Math.Min(usable, 6f);
                float toLine = FieldGeometry.DistanceToSideline(end.X);
                if (end.X < 0.8f || end.X > FieldGeometry.Width - 0.8f) val -= 4f;
                else if (toLine < 2.5f) val -= 1.0f;
                if (guideWeight > 0f && guide.LenSq() > 0.25f) val += guideWeight * (d.Dot(guide.Norm()) - 0.5f);
                if (prevDir.LenSq() > 0.25f) val += 0.55f * d.Dot(prevDir);
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
                guideW = carry != null && (carry.Style == CarryStyle.Power || carry.Style == CarryStyle.Counter || carry.Style == CarryStyle.Trap || carry.Style == CarryStyle.Sweep || carry.Style == CarryStyle.Toss) ? 2.8f : 1.4f; // designed holes behind pullers are followed; zone and draw runs read
            }
            else if (path != null && path.Count > 0 && c.PathIdx < path.Count)
            {
                Vec2 wp = S.Rel(path[path.Count - 1]);
                guide = (wp - c.Pos).Norm();
                guideW = 0.5f;
            }

            float look = 8f;
            float dist2Goal = dirY > 0 ? 100f - c.Pos.Y : c.Pos.Y;
            if (dist2Goal < look) look = Math.Max(2.5f, dist2Goal + 0.5f);

            c.ReactionClock -= PlaySimulation.Dt;
            if (c.ReactionClock <= 0f || c.LastTargetDir.LenSq() < 0.25f)
            {
                float aw = SimMath.Rating01(c.R.Awareness);
                float open, val;
                Vec2 d = LaneFinder.Best(S, c, guide, guideW, look, c.LastTargetDir, out open, out val);
                // low awareness: occasionally misreads and sticks with the designed track
                if (S.Rng.NextFloat() < (1f - aw) * 0.18f && guide.LenSq() > 0.25f) d = guide.Norm();
                c.LastTargetDir = d;
                c.PathIdxOpen = open;
                c.ReactionClock = 0.07f + (1f - aw) * 0.16f;
            }
            Vec2 dir = c.LastTargetDir;
            float cap = c.Body.EffectiveVMax * 0.955f;
            if (c.PathIdxOpen < 1.8f) cap *= 0.80f + 0.05f * c.PathIdxOpen;
            if (c.Protecting) cap *= 0.94f;
            c.Want = dir * cap;
            c.Intent = AgentAction.Carry;
            c.Exertion = 0f;
            S.Moves.ConsiderAIMoves(c);
        }
    }
}
