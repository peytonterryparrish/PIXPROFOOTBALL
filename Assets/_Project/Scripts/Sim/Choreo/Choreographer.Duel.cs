using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// v0.10.0 CHOREO: the contested catch. When the intended receiver is nearly standing, the ball is low and a defender is on him, the duel is rolled ahead of the arrival (<see cref="CatchResolver.PreRoll"/>:
    /// the same engage roll, margin and draws the arrival makes) and, when the receiver wins, the pair take of a contested catch is booked for both bodies, its hands-close frame on the arrival. When the
    /// defender wins (drop, deflection, interception) no success pair plays: the receiver and the defender get their plain takes. The simulation moves both bodies along the take (a tackle's machinery).
    /// </summary>
    public sealed partial class Choreographer
    {
        public static bool DebugNoDuel = Array.IndexOf(Environment.GetCommandLineArgs(), "-pixNoDuel") >= 0;

        readonly CatchResolver.DuelOutcome[] _duelOut = new CatchResolver.DuelOutcome[22];
        readonly int[] _duelDef = new int[22];
        readonly ChoreoBooking[] _duelBook = new ChoreoBooking[22];

        /// <summary>The duel of this receiver was rolled: 0 none, 1 he catches, 2 drop, 3 interception, 4 deflection.</summary>
        public int DuelOutcomeOf(int receiver) { return receiver >= 0 && receiver < 22 ? (int)_duelOut[receiver] : 0; }
        /// <summary>The booked contested-catch pair of this receiver (null when none).</summary>
        public ChoreoBooking DuelBookingOf(int receiver) { return receiver >= 0 && receiver < 22 ? _duelBook[receiver] : null; }
        /// <summary>True when the receiver's catch is a duel he wins: the plain solo take is not booked for him.</summary>
        bool DuelWon(int receiver) { return !DebugNoDuel && _duelOut[receiver] == CatchResolver.DuelOutcome.Catch; }

        void ResetDuels() { for (int i = 0; i < 22; i++) { _duelOut[i] = CatchResolver.DuelOutcome.None; _duelBook[i] = null; _duelDef[i] = -1; } }

        void ContestForecast(SimAgent r, CatchForecast f)
        {
            if (DebugNoDuel || r.Id < 1 || r.Id >= 11 || r.ControlledByUser) return;
            if (_byAgent[r.Id] != null && _duelBook[r.Id] == null) return;      // already in another take
            if (r.Vel.Length * 0.9144f > 2.0f || f.At.Z > 3.0f) return;
            if (_duelOut[r.Id] == CatchResolver.DuelOutcome.None)
            {
                // the defender on him (the same gate as the view's duel candidate: within a yard of where the receiver will be)
                SimAgent d = null; float best = 1.0f;
                for (int i = 11; i < 22; i++)
                {
                    SimAgent c = S.Agents[i];
                    if (c.State == AgentState.Down || c.State == AgentState.Engaged || c.Body.FallenTime > 0f) continue;
                    float dist = (c.Pos + c.Vel * f.Eta - f.AgentAt).Length;
                    if (dist < best) { best = dist; d = c; }
                }
                if (d == null) return;
                Vec2 dAt = d.Pos + (d.Want.Length > 0.5f ? (d.Vel + d.Want) * 0.5f : d.Vel) * f.Eta;
                float rBall = (f.AgentAt - f.At.XY).Length, dBall = (dAt - f.At.XY).Length;
                // the arrival makes it a duel only when both are within reach of the ball (the resolver's own near list and its half-yard slack)
                if (rBall > S.Catches.ReachRadius(r, f.At) + 0.55f || dBall > S.Catches.ReachRadius(d, f.At) + 0.55f) return;
                var o = S.Catches.PreRoll(r, d, rBall, dBall, f.At, S.Ball.Vel);
                _duelOut[r.Id] = o; _duelDef[r.Id] = d.Id; Stats.DuelRolls++;
                if (o == CatchResolver.DuelOutcome.Catch) Stats.DuelCatches++;
            }
            if (_duelOut[r.Id] != CatchResolver.DuelOutcome.Catch || _duelBook[r.Id] != null) return;
            if (_byAgent[r.Id] != null || _byAgent[_duelDef[r.Id]] != null) return;
            SimAgent def = S.Agents[_duelDef[r.Id]];
            BookDuel(r, def, f);
        }

        void BookDuel(SimAgent r, SimAgent d, CatchForecast f)
        {
            if (_lib == null) return;
            float rate = GamePace.AnimationRate;
            Vec2 cdir = r.Vel.Length > 1.75f ? r.Vel.Norm() : r.Facing;
            float cyaw = ChoreoMath.YawOf(cdir);
            float bearing = ChoreoMath.Bearing(d.Pos, r.Pos, cyaw);
            float spM = r.Vel.Length * 0.9144f;
            var pair = ChoosePairFit(PairKind.Contested, bearing, spM, 0f, r, d, r.Vel, cyaw, null);
            if (pair == null) return;
            // book when the take's lead-in fits in the time left (its contact is the arrival); sooner is too early (the bodies are not yet together), later has no lead-in
            float window = Math.Min(0.36f / rate, pair.ContactT / rate) + 0.04f;
            if (f.Eta > window || f.Eta < 0.05f) return;
            var b = BuildPairBooking(pair, PairKind.Contested, r, d, r.Vel, cyaw, f.Eta);
            if (b == null) return;
            _duelBook[r.Id] = b; Stats.DuelBookings++;
            Remember(pair.A.Name);
        }
    }
}
