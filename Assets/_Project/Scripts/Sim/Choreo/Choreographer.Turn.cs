using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>One planned hip flip of a defensive back (back-pedal to run), kept for the audit.</summary>
    public sealed class ChoreoTurn
    {
        public int Agent;
        public float T0, Duration, TotalRad, Omega, Ramp;
        public int Sign;                 // +1 = counter-clockwise on the field (Vec2.Rotate positive)
        public Vec2 FaceStart, FaceTarget;
        public bool Done;
    }

    public sealed partial class Choreographer
    {
        /// <summary>Seconds the angular rate takes to build up and to come down (a hip flip does not start or stop at a constant rate).</summary>
        public const float TurnRamp = 0.10f;
        /// <summary>QA switch: leave the defensive backs' facing to the AI.</summary>
        public static bool DebugNoTurn = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoTurn") >= 0;

        readonly ChoreoTurn[] _turn = new ChoreoTurn[22];
        readonly List<ChoreoTurn> _turns = new List<ChoreoTurn>();
        /// <summary>Hip flips planned in this play.</summary>
        public IReadOnlyList<ChoreoTurn> Turns { get { return _turns; } }

        static bool IsBack(Position p) { return p == Position.CB || p == Position.FS || p == Position.SS; }

        /// <summary>
        /// The defensive back's turn-and-run as a planned transition. A back that back-pedals (facing the quarterback, velocity away from him) and whose job has become "run with him" has to open his hips:
        /// the AI swaps the facing target 150 degrees in one tick and the body then turns at a constant rate that starts at once, which is what made the hips flip. Here the swap is planned: the facing
        /// follows a rate profile (ramp up, hold at what the body can turn, ramp down) toward the target the AI asked for, so the turn takes the same time it always took, plus the ramps,
        /// begins and ends with zero angular velocity, and the view's gait change (back-pedal to run) happens inside a turn whose rate it can follow.
        /// Runs after the AI and before the bodies step; changes nothing else (no speed, no path).
        /// </summary>
        void PlanTurns()
        {
            if (DebugNoTurn) return;
            for (int i = 11; i < 22; i++)
            {
                var a = S.Agents[i];
                var tr = _turn[i];
                bool eligible = IsBack(a.PlayPos) && a.State == AgentState.Normal && !a.IsDown && _byAgent[i] == null && !a.ControlledByUser && !a.HasBall;
                if (!eligible) { if (tr != null) { tr.Done = true; _turn[i] = null; } continue; }

                Vec2 face = a.Body.Facing;
                if (tr == null)
                {
                    float spd = a.Speed;
                    if (spd < 2.4f) continue;
                    Vec2 want = a.Want; float ws = want.Length;
                    if (ws < 3.0f) continue;
                    Vec2 wd = want * (1f / ws);
                    Vec2 tgt = a.HasFace && a.FaceDir.LenSq() > 0.25f ? a.FaceDir.Norm() : wd;
                    Vec2 vd = a.Vel * (1f / spd);
                    if (SimMath.AngleBetween(vd, face) < 1.9f) continue;           // not back-pedaling
                    if (SimMath.AngleBetween(vd, wd) > 1.2f) continue;              // not continuing in the direction he is going (a reversal is not a flip)
                    if (SimMath.AngleBetween(tgt, wd) > 1.0f) continue;             // the facing target is not along the new running line
                    float total = SimMath.AngleBetween(face, tgt);
                    if (total < 1.9f) continue;
                    float cross = face.Cross(tgt);
                    int sign = cross >= 0f ? 1 : -1;
                    if (total > 2.8f)
                    {
                        // nearly a half turn: open to the side he is drifting toward (the side of his lateral velocity), a back opens his hips toward the receiver
                        float lat = face.Cross(wd);
                        if (Math.Abs(lat) > 0.05f) sign = lat >= 0f ? 1 : -1;
                        total = total + 0f;
                    }
                    float cap = a.Body.TurnRate * (1f - 0.55f * SimMath.Clamp01(spd / Math.Max(1f, a.Body.VMax)));
                    float omega = Math.Max(2.0f, cap);
                    tr = new ChoreoTurn { Agent = i, T0 = _t, TotalRad = total, Omega = omega, Ramp = TurnRamp, Sign = sign, FaceStart = face, FaceTarget = tgt };
                    tr.Duration = ChoreoMath.TurnDuration(total, omega, TurnRamp);
                    _turn[i] = tr; _turns.Add(tr); Stats.TurnBookings++;
                }
                float u = _t - tr.T0;
                if (u >= tr.Duration + 0.02f) { tr.Done = true; _turn[i] = null; continue; }
                float ang = ChoreoMath.TurnAngle(u + PlaySimulation.Dt, tr.TotalRad, tr.Omega, tr.Ramp);
                Vec2 f = tr.FaceStart.Rotate(tr.Sign * ang).Norm();
                a.HasFace = true; a.FaceDir = f;
                Stats.TurnTicks++;
            }
        }
    }
}
