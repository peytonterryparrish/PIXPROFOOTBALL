using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.10.0 MOTION: read-only look-ahead for the locomotion matcher. Where the simulation will have an agent at +t seconds: a receiver follows his route (stem speeds, plants at the breaks, the same schedule
    /// RunRoute uses), everybody else follows his desired velocity (<see cref="SimAgent.Want"/>) through the body's own response (time constant and acceleration cap). Nothing here changes the simulation.
    /// </summary>
    public sealed partial class PlaySimulation
    {
        /// <summary>Planned positions (field frame, yards) at the three look-ahead times.</summary>
        public void PlanPoints(SimAgent a, float t1, float t2, float t3, out Vec2 p1, out Vec2 p2, out Vec2 p3)
        {
            var ra = a.Assign as RouteAssignment;
            if (OffAI != null && ra != null && a.Side == SimSide.Offense && !a.HasBall && !a.RouteDone && BallSide == SimSide.Offense)
            {
                p1 = OffAI.PredictPos(a, t1); p2 = OffAI.PredictPos(a, t2); p3 = OffAI.PredictPos(a, t3);
                return;
            }
            // desired velocity through the body: v' = (Want - v) / Tau limited to the acceleration cap
            const float h = 0.05f;
            float tau = Math.Max(0.15f, a.Body.Tau), cap = Math.Max(3f, a.Body.AccelCap);
            float vx = a.Vel.X, vy = a.Vel.Y, px = 0f, py = 0f, t = 0f;
            float wx = a.Want.X, wy = a.Want.Y;
            p1 = p2 = p3 = a.Pos;
            bool d1 = false, d2 = false;
            while (t < t3 - 1e-4f)
            {
                float ax = (wx - vx) / tau, ay = (wy - vy) / tau;
                float am = (float)Math.Sqrt(ax * ax + ay * ay);
                if (am > cap) { ax *= cap / am; ay *= cap / am; }
                vx += ax * h; vy += ay * h; px += vx * h; py += vy * h; t += h;
                if (!d1 && t >= t1 - 1e-4f) { p1 = new Vec2(a.Pos.X + px, a.Pos.Y + py); d1 = true; }
                if (!d2 && t >= t2 - 1e-4f) { p2 = new Vec2(a.Pos.X + px, a.Pos.Y + py); d2 = true; }
            }
            p3 = new Vec2(a.Pos.X + px, a.Pos.Y + py);
        }
    }
}
