using System;
using System.Collections.Generic;
using PixFootball.League;

namespace PixFootball.Sim
{
    /// <summary>
    /// Game-long fatigue. A play simulation only knows the 3 to 7 seconds it runs, so every body used to start every snap fully fresh. The
    /// tracker keeps each player's energy (1 = fresh, 0 = gassed) across plays: it is handed to the next <see cref="PlaySimOptions.Fatigue"/>,
    /// updated from the bodies after the whistle, and recovers while the player is off the field or only jogging back to the huddle.
    /// Exertion is scaled up from the raw play drain (a play only shows sprints, not the pushing and chasing the rest of a drive adds).
    /// </summary>
    public sealed class FatigueTracker
    {
        /// <summary>Multiplier from the energy a body burned during the play to the energy the player loses.</summary>
        public const float DrainScale = 1.25f;
        /// <summary>Extra energy per second spent locked in a block or shedding one (linemen get tired without sprinting).</summary>
        public const float EngagedDrainPerSecond = 0.006f;
        /// <summary>Energy regained per second on the bench or while the other unit is on the field.</summary>
        public const float BenchRecoveryPerSecond = 1f / 75f;
        /// <summary>Energy regained per second between snaps for players who stay on the field.</summary>
        public const float HuddleRecoveryPerSecond = 1f / 900f;   // v0.9.0 (E-355): was 1/600 (a drive barely tired anyone)

        private readonly Dictionary<int, float> _energy = new Dictionary<int, float>();

        /// <summary>Energy of a player (1 when the tracker has never seen him).</summary>
        public float EnergyOf(int playerId)
        {
            float e;
            return _energy.TryGetValue(playerId, out e) ? e : 1f;
        }

        public void Set(int playerId, float energy) { _energy[playerId] = Math.Max(0f, Math.Min(1f, energy)); }

        public int Count { get { return _energy.Count; } }

        /// <summary>
        /// Records one finished play: players who were on the field lose the energy their body burned (scaled) and regain a little during the
        /// huddle; players of the given rosters who were NOT in the play recover as if on the bench.
        /// </summary>
        /// <param name="sim">The finished play.</param>
        /// <param name="betweenSnapsSeconds">Seconds until the next snap (play-clock runoff).</param>
        /// <param name="restingPlayers">Everyone who sat out this play (optional).</param>
        public void AfterPlay(PlaySimulation sim, float betweenSnapsSeconds = 25f, IEnumerable<Player> restingPlayers = null)
        {
            if (sim == null) return;
            float idle = Math.Max(0f, betweenSnapsSeconds) + Math.Max(0f, sim.Time);
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = sim.Agents[i];
                if (a == null || a.Player == null) continue;
                int id = a.Player.Id;
                float before = EnergyOf(id);
                float burned = Math.Max(0f, before - a.Body.Energy);
                float engaged = a.EngagedSeconds * EngagedDrainPerSecond;
                float e = before - burned * DrainScale - engaged + betweenSnapsSeconds * HuddleRecoveryPerSecond;
                Set(id, e);
            }
            if (restingPlayers != null)
                foreach (Player p in restingPlayers)
                    if (p != null) Set(p.Id, EnergyOf(p.Id) + idle * BenchRecoveryPerSecond);
        }

        /// <summary>Recovery across a break (timeout, quarter break, halftime, a change of possession): every tracked player rests this long.</summary>
        public void Rest(float seconds)
        {
            var keys = new List<int>(_energy.Keys);
            foreach (int k in keys) Set(k, _energy[k] + seconds * BenchRecoveryPerSecond);
        }

        public void Reset() { _energy.Clear(); }
    }
}
