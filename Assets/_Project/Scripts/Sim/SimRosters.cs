using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.Sim
{
    /// <summary>Builds the 11-man rosters (one Player per formation slot) a play needs from a team's depth chart.</summary>
    public static class SimRosters
    {
        /// <summary>
        /// Chooses a starter for every slot of <paramref name="formation"/>: the best available depth-chart player at the slot's position,
        /// never using the same player twice. Falls back to the best-overall roster player at that position, then to similar positions.
        /// </summary>
        public static Player[] ForFormation(LeagueTeam team, Formation formation)
        {
            var result = new Player[formation.Slots.Length];
            var used = new HashSet<int>();
            for (int i = 0; i < formation.Slots.Length; i++)
            {
                Position pos = formation.Slots[i].Position;
                result[i] = Pick(team, pos, used);
                if (result[i] != null) used.Add(result[i].Id);
            }
            return result;
        }

        private static Player Pick(LeagueTeam team, Position pos, HashSet<int> used)
        {
            List<int> ids;
            if (team.Depth != null && team.Depth.TryGet(pos, out ids))
            {
                for (int i = 0; i < ids.Count; i++)
                {
                    Player p = team.GetPlayer(ids[i]);
                    if (p != null && !used.Contains(p.Id) && p.IsAvailable) return p;
                }
            }
            Player best = null; int bo = -1;
            for (int i = 0; i < team.Roster.Count; i++)
            {
                Player p = team.Roster[i];
                if (used.Contains(p.Id) || !p.IsAvailable) continue;
                if (p.Position != pos) continue;
                int o = p.Overall;
                if (o > bo) { bo = o; best = p; }
            }
            if (best != null) return best;
            foreach (Position alt in Alternatives(pos))
            {
                for (int i = 0; i < team.Roster.Count; i++)
                {
                    Player p = team.Roster[i];
                    if (used.Contains(p.Id) || !p.IsAvailable || p.Position != alt) continue;
                    int o = p.Overall;
                    if (o > bo) { bo = o; best = p; }
                }
                if (best != null) return best;
            }
            return null;
        }

        private static IEnumerable<Position> Alternatives(Position pos)
        {
            switch (pos)
            {
                case Position.FB: yield return Position.RB; yield return Position.TE; break;
                case Position.RB: yield return Position.WR; yield return Position.FB; break;
                case Position.WR: yield return Position.RB; yield return Position.TE; yield return Position.CB; break;
                case Position.TE: yield return Position.FB; yield return Position.WR; break;
                case Position.LT: case Position.RT: case Position.LG: case Position.RG: case Position.C:
                    yield return Position.LG; yield return Position.RG; yield return Position.LT; yield return Position.RT; yield return Position.C; break;
                case Position.LE: case Position.RE: yield return Position.DT; yield return Position.LOLB; yield return Position.ROLB; break;
                case Position.DT: yield return Position.LE; yield return Position.RE; break;
                case Position.LOLB: case Position.ROLB: case Position.MLB: yield return Position.MLB; yield return Position.LOLB; yield return Position.ROLB; yield return Position.SS; break;
                case Position.CB: yield return Position.FS; yield return Position.SS; break;
                case Position.FS: yield return Position.SS; yield return Position.CB; break;
                case Position.SS: yield return Position.FS; yield return Position.CB; break;
                case Position.K: yield return Position.P; break;
                case Position.P: yield return Position.K; break;
            }
        }
    }
}
