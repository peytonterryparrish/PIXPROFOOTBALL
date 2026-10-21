using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>Shared helpers for the league test files.</summary>
    internal static class LeagueTestUtil
    {
        // A generated league is expensive-ish; share one per fixture seed.
        private static readonly Dictionary<int, ProLeague> ProCache = new Dictionary<int, ProLeague>();

        public static ProLeague FreshPro(int seed) { return LeagueFactory.CreatePro(seed); }

        private const string DataRel = "Assets/_Project/Data/College";

        /// <summary>Finds the committed college CSV folder (Unity tests run with the project root as cwd).</summary>
        public static string CollegeDataDir()
        {
            var roots = new List<string>();
            roots.Add(System.IO.Directory.GetCurrentDirectory());
            string env = Environment.GetEnvironmentVariable("PIX_PROJECT_ROOT");
            if (!string.IsNullOrEmpty(env)) roots.Add(env);
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 10 && !string.IsNullOrEmpty(dir); i++)
            {
                roots.Add(dir);
                var parent = System.IO.Directory.GetParent(dir);
                dir = parent != null ? parent.FullName : null;
            }
            for (int i = 0; i < roots.Count; i++)
            {
                string p = System.IO.Path.Combine(roots[i], DataRel);
                if (System.IO.File.Exists(System.IO.Path.Combine(p, "schools.csv"))) return p;
            }
            throw new System.IO.FileNotFoundException("college data folder not found; cwd=" + roots[0]);
        }

        public static string SchoolsCsv() { return System.IO.File.ReadAllText(System.IO.Path.Combine(CollegeDataDir(), "schools.csv")); }
        public static string ProTeamsCsv()
        {
            string dir = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(CollegeDataDir()), "Pro");
            return System.IO.File.ReadAllText(System.IO.Path.Combine(dir, "teams.csv"));
        }

        public static List<ProTeamDef> LoadProTeams() { return ProData.ParseTeams(ProTeamsCsv()); }
        public static List<SchoolDef> LoadSchools() { return CollegeData.ParseSchools(SchoolsCsv()); }

        public static string ConfBowlsCsv() { return System.IO.File.ReadAllText(System.IO.Path.Combine(CollegeDataDir(), "conferences_bowls.csv")); }

        public static CollegeWorld LoadCollege(int seed)
        {
            return CollegeFactory.CreateWorld(seed, CollegeData.ParseSchools(SchoolsCsv()), CollegeData.ParseConfBowls(ConfBowlsCsv()));
        }

        /// <summary>Real FBS schools / well-known college names that must never appear in generated content.</summary>
        public static readonly string[] RealSchoolBlacklist =
        {
            "Alabama Crimson Tide", "Auburn", "Clemson", "Boise State", "Ohio State", "Michigan State", "Penn State", "Florida State",
            "Oklahoma State", "Iowa State", "Kansas State", "Mississippi State", "Arizona State", "Oregon State", "Washington State",
            "Texas A&M", "A&M", "Notre Dame", "USC", "UCLA", "LSU", "Stanford", "Baylor", "TCU", "Vanderbilt", "Purdue", "Rutgers",
            "Syracuse", "Tulane", "Northwestern", "Wake Forest", "NC State", "Virginia Tech", "Georgia Tech", "Texas Tech", "Boston College",
            "Ole Miss", "BYU", "SMU", "UNLV", "UTSA", "UCF", "Air Force", "Appalachian State", "Coastal Carolina", "Georgia Southern",
            "Old Dominion", "James Madison", "Western Kentucky", "Middle Tennessee", "South Alabama", "Arkansas State", "Texas State",
            "Kent State", "Ball State", "Bowling Green", "East Carolina", "North Texas", "San Diego State", "Fresno State", "Colorado State",
            "Utah State", "Louisiana Tech", "Michigan Wolverines", "Texas Longhorns", "Oklahoma Sooners", "Georgia Bulldogs", "Florida Gators",
            "Ohio Bobcats", "Miami Hurricanes", "Oregon Ducks", "Washington Huskies", "Wisconsin Badgers", "Iowa Hawkeyes", "Nebraska Cornhuskers",
            "Tennessee Volunteers", "Arkansas Razorbacks", "South Carolina Gamecocks", "Kansas Jayhawks", "Cal Golden Bears", "Fighting Irish",
            "Crimson Tide", "Buckeyes", "Cornhuskers", "Nittany Lions", "Seminoles", "Red Raiders", "Horned Frogs", "Commodores", "Yellow Jackets"
        };


        /// <summary>Real pro team nicknames that the fictional league must avoid.</summary>
        public static readonly string[] RealProNicknames =
        {
            "Cardinals", "Falcons", "Ravens", "Bills", "Panthers", "Bears", "Bengals", "Browns", "Cowboys", "Broncos", "Lions", "Packers",
            "Texans", "Colts", "Jaguars", "Chiefs", "Raiders", "Chargers", "Rams", "Dolphins", "Vikings", "Patriots", "Saints", "Giants",
            "Jets", "Eagles", "Steelers", "49ers", "Seahawks", "Buccaneers", "Titans", "Commanders", "Redskins", "Oilers", "Browns"
        };

        public static bool ContainsBlacklisted(string text, string[] blacklist, out string hit)
        {
            hit = null;
            if (string.IsNullOrEmpty(text)) return false;
            string t = " " + text.ToLowerInvariant() + " ";
            for (int i = 0; i < blacklist.Length; i++)
            {
                string b = blacklist[i].ToLowerInvariant();
                // whole-word / whole-phrase match so invented names that merely start with the same letters are fine
                if (t.Contains(" " + b + " ") || t.Contains(" " + b + "s ")) { hit = blacklist[i]; return true; }
            }
            return false;
        }

        /// <summary>Order-sensitive hash of a team's identity, roster ratings and contracts.</summary>
        public static long Fingerprint(Team t)
        {
            unchecked
            {
                long h = 1469598103934665603L;
                h = Mix(h, t.Id); h = Mix(h, t.FullName.GetHashCode());
                for (int i = 0; i < t.Roster.Count; i++)
                {
                    Player p = t.Roster[i];
                    h = Mix(h, p.Id);
                    h = Mix(h, StableHash(p.Name));
                    h = Mix(h, (int)p.Position);
                    h = Mix(h, p.Overall);
                    h = Mix(h, p.Potential);
                    h = Mix(h, p.Bio.Age);
                    h = Mix(h, (int)(p.Contract.Aav / 1000));
                    for (int k = 0; k < Ratings.KindCount; k++) h = Mix(h, p.Ratings.Get((RatingKind)k));
                }
                return h;
            }
        }

        public static long Fingerprint(ProLeague lg)
        {
            long h = 7;
            for (int i = 0; i < lg.Teams.Count; i++) h = Mix(h, Fingerprint(lg.Teams[i]));
            for (int i = 0; i < lg.FreeAgents.Count; i++) h = Mix(h, lg.FreeAgents[i].Id);
            return h;
        }

        public static long Fingerprint(Season s)
        {
            long h = 11;
            h = Mix(h, s.ChampionTeamId);
            for (int i = 0; i < s.Games.Count; i++)
            {
                h = Mix(h, s.Games[i].HomeId); h = Mix(h, s.Games[i].AwayId);
                h = Mix(h, s.Games[i].HomeScore * 1000 + s.Games[i].AwayScore);
                h = Mix(h, s.Games[i].Week);
            }
            return h;
        }

        public static long Fingerprint(GameResult g)
        {
            long h = 13;
            h = Mix(h, g.HomeScore); h = Mix(h, g.AwayScore); h = Mix(h, g.TotalPlays);
            h = Mix(h, g.Home.PassYds); h = Mix(h, g.Away.RushYds);
            for (int i = 0; i < g.Drives.Count; i++) { h = Mix(h, g.Drives[i].Yards); h = Mix(h, g.Drives[i].Plays); h = Mix(h, StableHash(g.Drives[i].Result)); }
            for (int i = 0; i < g.PlayerLines.Count; i++) { h = Mix(h, g.PlayerLines[i].PlayerId); h = Mix(h, g.PlayerLines[i].Stats.RushYds + g.PlayerLines[i].Stats.PassYds); }
            return h;
        }

        public static int StableHash(string s)
        {
            unchecked
            {
                int h = 17;
                for (int i = 0; i < s.Length; i++) h = h * 31 + s[i];
                return h;
            }
        }

        private static long Mix(long h, long v)
        {
            unchecked { h ^= v; h *= 1099511628211L; h ^= h >> 29; return h; }
        }

        /// <summary>A copy of the team whose ratings are all shifted by <paramref name="boost"/> (Injury/Stamina untouched).</summary>
        public static Team CloneWithBoost(Team t, int boost, int newId)
        {
            var c = new Team { Id = newId, City = t.City, Nickname = t.Nickname + " Elite", Abbreviation = t.Abbreviation, Staff = t.Staff };
            for (int i = 0; i < t.Roster.Count; i++)
            {
                Player p = t.Roster[i];
                var np = new Player { Bio = p.Bio, Position = p.Position, Archetype = p.Archetype, Potential = p.Potential, Dev = p.Dev, Contract = p.Contract };
                Ratings r = p.Ratings;
                for (int k = 0; k < Ratings.KindCount; k++)
                {
                    var kind = (RatingKind)k;
                    if (kind == RatingKind.Injury || kind == RatingKind.Stamina) continue;
                    r.Set(kind, r.Get(kind) + boost);
                }
                np.Ratings = r;
                c.AddPlayer(np);
            }
            c.Depth = t.Depth;
            c.InvalidateIndex();
            return c;
        }

        public static string DescribeBands(string label, double v, double lo, double hi)
        {
            return label + " = " + v.ToString("F2") + " expected in [" + lo + ", " + hi + "]";
        }
    }
}
