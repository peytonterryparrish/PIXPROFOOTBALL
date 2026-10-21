using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    public enum RecordScope { SingleGame, SingleSeason, Career }

    [Serializable]
    public class RecordEntry
    {
        public string Category;
        public RecordScope Scope;
        public int Value;
        public int PlayerId;
        public string PlayerName;
        public string TeamName;
        public int Year;
    }

    /// <summary>All-time bests: single game, single season and career, for the most meaningful stat categories.</summary>
    [Serializable]
    public class RecordBook
    {
        public List<RecordEntry> Entries = new List<RecordEntry>();

        private static readonly string[] Categories =
        {
            "PassYds", "PassTd", "RushYds", "RushTd", "Rec", "RecYds", "RecTd", "Sacks", "Interceptions", "Tackles", "FgMade", "Points"
        };

        public static IReadOnlyList<string> CategoryNames { get { return Categories; } }

        public static int ValueOf(StatLine s, string category)
        {
            switch (category)
            {
                case "PassYds": return s.PassYds;
                case "PassTd": return s.PassTd;
                case "RushYds": return s.RushYds;
                case "RushTd": return s.RushTd;
                case "Rec": return s.Rec;
                case "RecYds": return s.RecYds;
                case "RecTd": return s.RecTd;
                case "Sacks": return s.Sacks;
                case "Interceptions": return s.Interceptions;
                case "Tackles": return s.Tackles;
                case "FgMade": return s.FgMade;
                case "Points": return s.Points;
            }
            return 0;
        }

        public RecordEntry Get(string category, RecordScope scope)
        {
            for (int i = 0; i < Entries.Count; i++)
                if (Entries[i].Scope == scope && Entries[i].Category == category) return Entries[i];
            return null;
        }

        /// <summary>Returns true when the record was broken (or set for the first time).</summary>
        public bool Offer(string category, RecordScope scope, int value, int playerId, string playerName, string teamName, int year)
        {
            if (value <= 0) return false;
            RecordEntry e = Get(category, scope);
            if (e == null)
            {
                Entries.Add(new RecordEntry { Category = category, Scope = scope, Value = value, PlayerId = playerId, PlayerName = playerName, TeamName = teamName, Year = year });
                return true;
            }
            if (value > e.Value)
            {
                e.Value = value; e.PlayerId = playerId; e.PlayerName = playerName; e.TeamName = teamName; e.Year = year;
                return true;
            }
            return false;
        }

        public void UpdateGame(GameResult g, int year, Team home, Team away)
        {
            for (int i = 0; i < g.PlayerLines.Count; i++)
            {
                PlayerGameLine l = g.PlayerLines[i];
                Team t = l.TeamId == home.Id ? home : away;
                for (int c = 0; c < Categories.Length; c++)
                    Offer(Categories[c], RecordScope.SingleGame, ValueOf(l.Stats, Categories[c]), l.PlayerId, l.Name, t.FullName, year);
            }
        }

        public void UpdateSeason(IEnumerable<Team> teams, int year)
        {
            foreach (Team t in teams)
            {
                for (int i = 0; i < t.Roster.Count; i++)
                {
                    Player p = t.Roster[i];
                    SeasonStats s = p.FindStats(year);
                    if (s == null) continue;
                    for (int c = 0; c < Categories.Length; c++)
                        Offer(Categories[c], RecordScope.SingleSeason, ValueOf(s, Categories[c]), p.Id, p.Name, t.FullName, year);
                    StatLine career = p.CareerTotals();
                    for (int c = 0; c < Categories.Length; c++)
                        Offer(Categories[c], RecordScope.Career, ValueOf(career, Categories[c]), p.Id, p.Name, t.FullName, year);
                }
            }
        }
    }
}
