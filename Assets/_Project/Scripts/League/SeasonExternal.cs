using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    public partial class Season
    {
        /// <summary>
        /// Records a game that was played outside the quick simulation (the Unity match): posts the score, the player lines, the records and the
        /// injuries exactly like a simulated game. Call it before <see cref="SimWeek"/>; the week's other games are then simulated as usual.
        /// Returns false when the game is unknown or already played.
        /// </summary>
        public bool ApplyExternalResult(int gameId, GameResult r, Rng rng)
        {
            if (r == null || gameId < 0 || gameId >= Games.Count) return false;
            ScheduledGame g = Games[gameId];
            if (g.Played || _teams == null) return false;
            if (g.HomeId != r.HomeTeamId || g.AwayId != r.AwayTeamId) return false;
            if (_pendingHurt == null) _pendingHurt = InjuredNow();   // QA-563: players hurt in this game must not heal at the end of this very week
            Team home = _teams[g.HomeId], away = _teams[g.AwayId];
            g.Played = true; g.HomeScore = r.HomeScore; g.AwayScore = r.AwayScore; g.Overtime = r.Overtime;
            bool post = g.Kind != GameKind.Regular;
            PostStats(r, home, away, post);
            if (Records != null && !post) Records.UpdateGame(r, Year, home, away);
            if (r.InjuriesProvided)
            {
                for (int i = 0; i < r.Injuries.Count; i++)
                {
                    GameInjury gi = r.Injuries[i];
                    Team t = gi.TeamId == home.Id ? home : away;
                    Player p = t.GetPlayer(gi.PlayerId);
                    if (p == null || p.Injury.IsInjured) continue;
                    p.Injury.WeeksOut = Math.Max(1, Math.Min(40, gi.Weeks));
                    p.Injury.Type = string.IsNullOrEmpty(gi.Type) ? "Injury" : gi.Type;
                    p.Injury.IrWeeks = -1;
                    if (p.Injury.WeeksOut >= 6) p.Injury.Wear++;
                }
            }
            else if (InjuriesEnabled && rng != null) RollInjuries(r, home, away, rng);
            return true;
        }

        /// <summary>Simulates one scheduled game of the current week right now (so the user can read the box score before the week advances). Returns null when it cannot be played.</summary>
        public GameResult SimOneGame(int gameId, Rng rng)
        {
            if (gameId < 0 || gameId >= Games.Count || _teams == null) return null;
            ScheduledGame g = Games[gameId];
            if (g.Played) return null;
            if (_pendingHurt == null) _pendingHurt = InjuredNow();
            int before = LastWeekResults.Count;
            SimGameInternal(g, rng);
            return LastWeekResults[LastWeekResults.Count - 1];
        }
    }
}
