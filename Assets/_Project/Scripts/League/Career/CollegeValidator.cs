using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>Consistency checks for the college world and the College Network state (called by <see cref="CareerValidator.Check"/>).</summary>
    public static class CollegeValidator
    {
        public static List<string> Check(CareerSave s)
        {
            var problems = new List<string>();
            CollegeWorld w = s.College;
            if (w == null) return problems;
            var classIds = new HashSet<int>();
            for (int i = 0; i < s.Pro.DraftClass.Count; i++) classIds.Add(s.Pro.DraftClass[i].Id);   // prospects are on a school roster and, once picked, on a pro roster until the draft turns the college over
            var seen = new HashSet<int>();
            var proIds = new HashSet<int>();
            foreach (Player p in s.Pro.AllRosterPlayers()) proIds.Add(p.Id);
            for (int i = 0; i < s.Pro.FreeAgents.Count; i++) proIds.Add(s.Pro.FreeAgents[i].Id);
            for (int si = 0; si < w.Schools.Count; si++)
            {
                School sc = w.Schools[si];
                Team t = sc.Team;
                if (t.Roster.Count != LeagueRules.CollegeRosterSize) problems.Add(sc.Name + ": roster of " + t.Roster.Count + " (need " + LeagueRules.CollegeRosterSize + ")");
                for (int pi = 0; pi < LeagueRules.RosterOrder.Length; pi++)
                {
                    Position pos = LeagueRules.RosterOrder[pi];
                    if (t.CountAt(pos) != LeagueRules.CollegeSlots(pos)) problems.Add(sc.Name + ": " + t.CountAt(pos) + " " + pos + " (need " + LeagueRules.CollegeSlots(pos) + ")");
                }
                var jerseys = new HashSet<int>();
                for (int i = 0; i < t.Roster.Count; i++)
                {
                    Player p = t.Roster[i];
                    if (!seen.Add(p.Id)) problems.Add(sc.Name + ": " + p.Name + " is on two school rosters");
                    if (p.Retired) problems.Add(sc.Name + ": retired player " + p.Name + " on the roster");
                    if (!classIds.Contains(p.Id) && proIds.Contains(p.Id)) problems.Add(sc.Name + ": " + p.Name + " is also a pro");
                    if (p.Overall < 0 || p.Overall > 99) problems.Add(sc.Name + ": " + p.Name + " overall out of range");
                    if (!classIds.Contains(p.Id) && !jerseys.Add(p.Bio.JerseyNumber)) problems.Add(sc.Name + ": duplicate jersey " + p.Bio.JerseyNumber);
                    if (!classIds.Contains(p.Id) && p.TeamId != sc.Id) problems.Add(sc.Name + ": " + p.Name + " carries team id " + p.TeamId);
                }
            }
            if (w.Season != null && !w.Season.IsEmpty && w.Season.Teams.Count != w.Schools.Count) problems.Add("college season has " + w.Season.Teams.Count + " teams for " + w.Schools.Count + " schools");
            CollegeNetState n = s.Net;
            if (n != null && n.Ready)
            {
                for (int t = 0; t < s.Pro.Teams.Count; t++)
                {
                    int count = 0;
                    for (int i = 0; i < n.Scouts.Count; i++) if (n.Scouts[i].TeamId == t) count++;
                    if (count < CollegeScouting.MinScouts || count > CollegeScouting.MaxScouts) problems.Add("team " + t + " has " + count + " scouts");
                }
                bool proReport = false;
                for (int i = 0; i < s.Pro.ScoutingReports.Count; i++) if (s.Pro.ScoutingReports[i].TeamId == s.UserTeamId) proReport = true;
                if (proReport && n.HasUserReport) problems.Add("the user's scouting knowledge exists twice");
                if (!proReport && !n.HasUserReport) problems.Add("the user has no scouting report");
                var board = new HashSet<int>();
                for (int i = 0; i < n.Board.Count; i++) if (!board.Add(n.Board[i].PlayerId)) problems.Add("player " + n.Board[i].PlayerId + " twice in the board list");
                var boxes = new HashSet<long>();
                for (int i = 0; i < n.Boxes.Count; i++) if (!boxes.Add(((long)n.Boxes[i].Year << 32) | (uint)n.Boxes[i].GameId)) problems.Add("duplicate box score");
            }
            return problems;
        }
    }
}
