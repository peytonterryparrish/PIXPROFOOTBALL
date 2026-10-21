using System.Collections.Generic;
using PixFootball.Art;
using PixFootball.League;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.Gameplay
{
    /// <summary>Shared match data: the 32-team league (built once from teams.csv with a fixed seed) and the config of the game being set up.</summary>
    public static class MatchData
    {
        public const int LeagueSeed = 2026;
        static ProLeague _league;
        static List<ProTeamDef> _defs;

        /// <summary>The configuration chosen on the team-select screen (read by the match scene).</summary>
        public static MatchConfig Current = new MatchConfig();

        public static string CsvText()
        {
            var rt = PixRuntimeAssets.Load();
            string csv = rt != null && rt.teamsCsv != null ? rt.teamsCsv.text : null;
#if UNITY_EDITOR
            if (string.IsNullOrEmpty(csv) && System.IO.File.Exists("Assets/_Project/Data/Pro/teams.csv")) csv = System.IO.File.ReadAllText("Assets/_Project/Data/Pro/teams.csv");
#endif
            return csv;
        }

        public static IReadOnlyList<ProTeamDef> Defs
        {
            get { if (_defs == null) _defs = ProData.ParseTeams(CsvText() ?? ""); return _defs; }
        }

        public static bool HasLeague { get { return _league != null; } }

        public static ProLeague League
        {
            get
            {
                if (_league == null)
                {
                    string csv = CsvText();
                    _league = !string.IsNullOrEmpty(csv) && Defs.Count == 32 ? LeagueFactory.CreatePro(LeagueSeed, Defs) : LeagueFactory.CreatePro(LeagueSeed);
                }
                return _league;
            }
        }

        public static LeagueTeam TeamByKey(string key)
        {
            foreach (var t in League.Teams) if (t.Key == key) return t;
            return League.Teams[0];
        }

        public static void Reset() { _league = null; _defs = null; }

        /// <summary>Starters' average rating shown on the team select cards.</summary>
        public static int Overall(LeagueTeam t) { return t.TeamOverall(); }
    }
}
