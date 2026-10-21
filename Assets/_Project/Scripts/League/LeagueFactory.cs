using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>Creates the fictional 32-team pro league.</summary>
    public static class LeagueFactory
    {
        public const int DefaultStartYear = 2026;

        private struct TeamDef
        {
            public string City, Nick, Abbr, Primary, Secondary;
            public TeamDef(string city, string nick, string abbr, string p, string s)
            { City = city; Nick = nick; Abbr = abbr; Primary = p; Secondary = s; }
        }

        // conference 0 = Liberty, 1 = Frontier; each four divisions of four, East/North/South/West.
        private static readonly TeamDef[] Defs =
        {
            // LIBERTY EAST
            new TeamDef("Hartford", "Cannons", "HFD", "#1C3F7A", "#C9A227"),
            new TeamDef("Richmond", "Sentinels", "RIC", "#7A1B2D", "#D8D2C4"),
            new TeamDef("Norfolk", "Breakers", "NOR", "#0E6E8C", "#F2F2F2"),
            new TeamDef("Albany", "Stormcrows", "ALB", "#2B2B3D", "#8E5CC9"),
            // LIBERTY NORTH
            new TeamDef("Milwaukee", "Foundrymen", "MIL", "#C4461A", "#1E1E1E"),
            new TeamDef("Columbus", "Pioneers", "CLB", "#285C3B", "#E6C25A"),
            new TeamDef("Louisville", "Riverkings", "LOU", "#143C8C", "#E5E5E5"),
            new TeamDef("Rochester", "Lakehawks", "ROC", "#4C7F9E", "#101820"),
            // LIBERTY SOUTH
            new TeamDef("Charleston", "Corsairs", "CHS", "#111111", "#D4AF37"),
            new TeamDef("Birmingham", "Furnace", "BHM", "#B3261E", "#2E2E2E"),
            new TeamDef("Memphis", "Riverboats", "MEM", "#5B3A8C", "#F4C430"),
            new TeamDef("Savannah", "Marauders", "SAV", "#0B5D4B", "#C0C0C0"),
            // LIBERTY WEST
            new TeamDef("Albuquerque", "Sunhawks", "ABQ", "#E3792B", "#2A1A5E"),
            new TeamDef("Tucson", "Copperheads", "TUC", "#9A4A1E", "#12343B"),
            new TeamDef("El Paso", "Scorpions", "ELP", "#6D1F1F", "#E3C770"),
            new TeamDef("Reno", "Silverbacks", "REN", "#8A9BA8", "#14213D"),
            // FRONTIER EAST
            new TeamDef("Providence", "Lanterns", "PVD", "#F2B134", "#1B2A41"),
            new TeamDef("Trenton", "Hammers", "TRE", "#3A3A3A", "#E4572E"),
            new TeamDef("Wilmington", "Bluecoats", "WIL", "#24468B", "#B8B8B8"),
            new TeamDef("Manchester", "Millers", "MAN", "#7B6B43", "#1F4E3D"),
            // FRONTIER NORTH
            new TeamDef("Omaha", "Stampeders", "OMA", "#A32020", "#F5E6C8"),
            new TeamDef("Des Moines", "Voyagers", "DSM", "#1D6F8A", "#F0A500"),
            new TeamDef("Madison", "Northmen", "MSN", "#2D3E50", "#C7D3DC"),
            new TeamDef("Wichita", "Aviators", "WCH", "#0F5E9C", "#FFB400"),
            // FRONTIER SOUTH
            new TeamDef("San Antonio", "Missionaries", "SAT", "#7C2D12", "#F1E3C6"),
            new TeamDef("Austin", "Outlaws", "AUS", "#171717", "#E5342F"),
            new TeamDef("Oklahoma City", "Wranglers", "OKC", "#B85C1E", "#1F3A5F"),
            new TeamDef("Baton Rouge", "Bayou Kings", "BTR", "#3C1E70", "#19A974"),
            // FRONTIER WEST
            new TeamDef("Portland", "Ironclads", "POR", "#4A5560", "#E8743B"),
            new TeamDef("Sacramento", "Prospectors", "SAC", "#C79A1B", "#222B3A"),
            new TeamDef("Spokane", "Lumberjacks", "SPK", "#1F5C34", "#B5281F"),
            new TeamDef("Honolulu", "Tradewinds", "HNL", "#0A8F9C", "#F25C54")
        };

        private static readonly string[] DivisionNames = { "East", "North", "South", "West" };
        private static readonly string[] ConferenceNames = { "Liberty Conference", "Frontier Conference" };
        private static readonly string[] ConferenceAbbr = { "LIB", "FRO" };
        private static readonly string[] StadiumSuffix = { "Stadium", "Field", "Park", "Coliseum", "Dome", "Grounds" };

        /// <summary>One resolved team slot (generated fallback or loaded from teams.csv).</summary>
        private struct Slot
        {
            public string Key, City, Nick, Abbr, Primary, Secondary, Tertiary, Logo;
        }

        /// <summary>Generated fallback league (invented teams, invented college names).</summary>
        public static ProLeague CreatePro(int seed, int startYear = DefaultStartYear)
        {
            var slots = new Slot[Defs.Length];
            for (int i = 0; i < Defs.Length; i++)
                slots[i] = new Slot
                {
                    Key = "", City = Defs[i].City, Nick = Defs[i].Nick, Abbr = Defs[i].Abbr,
                    Primary = Defs[i].Primary, Secondary = Defs[i].Secondary, Tertiary = "#FFFFFF", Logo = ""
                };
            return Build(seed, startYear, slots, ConferenceNames, ConferenceAbbr, DivisionNames, null);
        }

        /// <summary>
        /// Data-driven league using the official teams from Data/Pro/teams.csv. Teams are ordered by conference,
        /// division (East/North/South/West) then file order, so team ids are stable. Pro players' colleges are drawn
        /// from <paramref name="colleges"/> (the school list) when supplied.
        /// </summary>
        public static ProLeague CreatePro(int seed, IReadOnlyList<ProTeamDef> teams, IReadOnlyList<SchoolDef> colleges = null, int startYear = DefaultStartYear)
        {
            if (teams == null || teams.Count != 32) throw new ArgumentException("the pro league needs exactly 32 teams (got " + (teams == null ? 0 : teams.Count) + ")");

            // conferences in order of first appearance; divisions in East/North/South/West order
            var confNames = new List<string>();
            for (int i = 0; i < teams.Count; i++) if (!confNames.Contains(teams[i].Conference)) confNames.Add(teams[i].Conference);
            if (confNames.Count != 2) throw new ArgumentException("the pro league needs exactly 2 conferences (got " + confNames.Count + ")");

            var order = new List<int>();
            for (int i = 0; i < teams.Count; i++) order.Add(i);
            var sorted = order.ToArray();
            Array.Sort(sorted, (a, b) =>
            {
                int c = confNames.IndexOf(teams[a].Conference).CompareTo(confNames.IndexOf(teams[b].Conference));
                if (c != 0) return c;
                c = DivisionRank(teams[a].Division).CompareTo(DivisionRank(teams[b].Division));
                return c != 0 ? c : a.CompareTo(b);
            });

            string[] abbrs = ProData.DeriveAbbreviations(teams);
            var slots = new Slot[32];
            for (int k = 0; k < 32; k++)
            {
                ProTeamDef def = teams[sorted[k]];
                string conf = confNames.IndexOf(def.Conference) == 0 ? "A" : "B";
                string div = DivisionNames[Math.Max(0, Math.Min(3, DivisionRank(def.Division)))];
                if (DivisionRank(def.Division) > 3) throw new ArgumentException("unknown division '" + def.Division + "' for " + def.Name);
                if (conf != (k < 16 ? "A" : "B") || div != DivisionNames[(k / 4) % 4])
                    throw new ArgumentException("pro teams must be 4 divisions of 4 per conference (check " + def.Name + ")");
                slots[k] = new Slot
                {
                    Key = def.Id ?? "", City = def.City, Nick = def.Nickname, Abbr = abbrs[sorted[k]],
                    Primary = def.Primary, Secondary = def.Secondary, Tertiary = def.Tertiary, Logo = def.Logo ?? ""
                };
            }

            var confAbbr = new string[2];
            for (int c = 0; c < 2; c++)
            {
                string first = confNames[c].Split(' ')[0];
                confAbbr[c] = first.Substring(0, Math.Min(3, first.Length)).ToUpperInvariant();
            }

            List<string> collegeNames = null;
            if (colleges != null && colleges.Count > 0)
            {
                collegeNames = new List<string>();
                var seen = new HashSet<string>();
                for (int i = 0; i < colleges.Count; i++)
                {
                    string place = colleges[i].Place;
                    if (!string.IsNullOrEmpty(place) && seen.Add(place)) collegeNames.Add(place);
                }
            }
            return Build(seed, startYear, slots, confNames.ToArray(), confAbbr, DivisionNames, collegeNames);
        }

        private static int DivisionRank(string division)
        {
            for (int i = 0; i < DivisionNames.Length; i++)
                if (string.Equals(DivisionNames[i], division, StringComparison.OrdinalIgnoreCase)) return i;
            return 99;
        }

        private static ProLeague Build(int seed, int startYear, Slot[] slots, string[] confNames, string[] confAbbr, string[] divNames, List<string> collegeNames)
        {
            var rng = new Rng(seed);
            var league = new ProLeague { Seed = seed, Year = startYear };
            var usedNames = new HashSet<string>();

            for (int c = 0; c < 2; c++)
            {
                var conf = new Conference { Id = c, Name = confNames[c], Abbreviation = confAbbr[c], IsCollege = false };
                for (int d = 0; d < 4; d++)
                {
                    var div = new Division { Id = c * 4 + d, Name = confAbbr[c] + " " + divNames[d], ConferenceId = c };
                    for (int t = 0; t < 4; t++)
                    {
                        int id = (c * 4 + d) * 4 + t;
                        Slot def = slots[id];
                        var team = new Team
                        {
                            Id = id, City = def.City, Nickname = def.Nick, Abbreviation = def.Abbr,
                            PrimaryColor = def.Primary, SecondaryColor = def.Secondary, TertiaryColor = def.Tertiary,
                            Key = def.Key, Logo = def.Logo,
                            ConferenceId = c, DivisionId = div.Id, IsCollege = false,
                            StadiumName = def.City + " " + StadiumSuffix[rng.Range(0, StadiumSuffix.Length)]
                        };
                        div.TeamIds.Add(id);
                        conf.TeamIds.Add(id);
                        league.Teams.Add(team);
                    }
                    conf.Divisions.Add(div);
                }
                league.Conferences.Add(conf);
            }

            var colleges = collegeNames;
            {
                Rng cr = rng.Fork();
                if (colleges == null)
                {
                    colleges = new List<string>();
                    var un = new HashSet<string>(); var ua = new HashSet<string>(); var um = new HashSet<string>();
                    for (int i = 0; i < 90; i++) colleges.Add(NameGenerator.SchoolName(cr, un, ua, um).ShortName);
                }
            }

            for (int i = 0; i < league.Teams.Count; i++)
            {
                Rng tr = rng.Fork();
                BuildTeam(league.Teams[i], tr, league, usedNames, startYear, colleges);
            }

            // free agent pool
            Rng fr = rng.Fork();
            for (int i = 0; i < 110; i++)
            {
                Position pos = RandomRosterPosition(fr);
                int peak = (int)Math.Round(fr.Gaussian(62f, 6f));
                int age = Math.Max(22, Math.Min(35, (int)Math.Round(fr.Gaussian(28f, 3.5f))));
                Player p = PlayerFactory.Create(fr, league, usedNames, pos, peak, age);
                p.Experience = Math.Max(0, age - 22);
                p.DraftYear = startYear - p.Experience;
                p.TeamId = -1; p.Bio.JerseyNumber = PickJersey(pos, fr, null); p.Bio.College = colleges[fr.Range(0, colleges.Count)];
                p.Contract = new Contract();
                league.FreeAgents.Add(p);
            }

            Rng seasonRng = rng.Fork();
            league.Season = Season.CreatePro(league, startYear, seasonRng);
            league.SaveRng(rng);
            league.Bind();
            return league;
        }

        internal static Position RandomRosterPosition(Rng rng)
        {
            Position[] order = LeagueRules.RosterOrder;
            int total = 0;
            for (int i = 0; i < order.Length; i++) total += LeagueRules.ProSlots(order[i]);
            int r = rng.Range(0, total);
            for (int i = 0; i < order.Length; i++)
            {
                r -= LeagueRules.ProSlots(order[i]);
                if (r < 0) return order[i];
            }
            return Position.WR;
        }

        // ---------------------------------------------------------------- team building
        private static void BuildTeam(Team team, Rng rng, ProLeague league, HashSet<string> usedNames, int year, List<string> colleges)
        {
            float quality = rng.Gaussian(0f, 3.5f);
            team.Prestige = Math.Max(1, Math.Min(5, 3 + (int)Math.Round(quality / 3f)));
            team.ScoutingPoints = 300 + rng.Range(0, 200);
            BuildStaff(team, rng, usedNames, quality);
            var jerseys = new HashSet<int>();

            for (int pi = 0; pi < LeagueRules.RosterOrder.Length; pi++)
            {
                Position pos = LeagueRules.RosterOrder[pi];
                int starters = LeagueRules.Starters(pos);
                int slots = LeagueRules.ProSlots(pos);
                for (int i = 0; i < slots; i++)
                {
                    int tier = i < starters ? 0 : (i < starters + 1 ? 1 : 2);
                    float mean = tier == 0 ? 76.5f : (tier == 1 ? 71f : 68f);
                    if (pos == Position.K || pos == Position.P) mean += 2f;
                    if (pos == Position.FB) mean -= 6f;
                    int peak = (int)Math.Round(mean + quality + rng.Gaussian(0f, 6.5f));
                    float ageMean = tier == 0 ? 27.2f : (tier == 1 ? 25.8f : 24.8f);
                    switch (PositionBuckets.Of(pos))
                    {
                        case PositionBucket.QB: ageMean += 1.5f; break;
                        case PositionBucket.RB: ageMean -= 1f; break;
                        case PositionBucket.OL: ageMean += 0.8f; break;
                        case PositionBucket.K: case PositionBucket.P: ageMean += 3f; break;
                    }
                    int age = (int)Math.Round(rng.Gaussian(ageMean, 3.2f));
                    age = Math.Max(21, Math.Min(37, age));
                    Player p = PlayerFactory.Create(rng, league, usedNames, pos, peak, age);
                    FinishProPlayer(p, rng, year, jerseys, colleges);
                    team.AddPlayer(p);
                }
            }

            FitContractsToCap(team, rng);
            DepthChartBuilder.Build(team);
        }

        /// <summary>Contract, jersey number, college and draft metadata for a freshly generated pro.</summary>
        internal static void FinishProPlayer(Player p, Rng rng, int year, HashSet<int> teamJerseys, List<string> colleges)
        {
            p.Experience = Math.Max(0, p.Age - 22 + (rng.Chance(0.2f) ? 1 : 0));
            p.DraftYear = year - p.Experience;
            p.DraftRound = rng.Chance(0.12f) ? 0 : Math.Min(7, 1 + (int)(-Math.Log(1.0 - rng.NextDouble() * 0.97) * 2.6));
            p.DraftPick = p.DraftRound == 0 ? 0 : Math.Min(224, (p.DraftRound - 1) * 32 + rng.Range(1, 33));
            p.Bio.JerseyNumber = PickJersey(p.Position, rng, teamJerseys);
            p.Bio.College = colleges != null && colleges.Count > 0 ? colleges[rng.Range(0, colleges.Count)] : "";
            int ovr = p.Ratings.Overall(p.Position);

            bool onRookieDeal = p.Experience <= 3 && p.DraftRound > 0;
            if (onRookieDeal)
            {
                int pick = p.DraftPick > 0 ? p.DraftPick : 250;
                var c = ContractModel.RookieContract(pick, year - p.Experience);
                c.YearsLeft = Math.Max(1, c.Years - p.Experience);
                p.Contract = c;
            }
            else
            {
                int years = rng.Range(1, 6);
                var c = ContractModel.VeteranContract(p.Position, ovr, p.Age, years, year, rng);
                c.YearsLeft = rng.Range(1, years + 1);
                p.Contract = c;
            }
            p.Morale = rng.Range(55, 92);
        }

        private static readonly int[][] JerseyRanges =
        {
            new[] { 1, 19 },      // QB
            new[] { 20, 49 },     // RB
            new[] { 30, 49 },     // FB
            new[] { 10, 19, 80, 89 }, // WR
            new[] { 80, 89, 40, 49 }, // TE
            new[] { 60, 79 },     // OL
            new[] { 90, 99, 70, 79 }, // DL
            new[] { 50, 59, 90, 99 }, // LB
            new[] { 20, 39 },     // CB
            new[] { 20, 49 },     // S
            new[] { 1, 19 },      // K
            new[] { 1, 19 }       // P
        };

        internal static int PickJersey(Position pos, Rng rng, HashSet<int> used)
        {
            int bucketIdx;
            switch (PositionBuckets.Of(pos))
            {
                case PositionBucket.QB: bucketIdx = 0; break;
                case PositionBucket.RB: bucketIdx = 1; break;
                case PositionBucket.FB: bucketIdx = 2; break;
                case PositionBucket.WR: bucketIdx = 3; break;
                case PositionBucket.TE: bucketIdx = 4; break;
                case PositionBucket.OL: bucketIdx = 5; break;
                case PositionBucket.DL: bucketIdx = 6; break;
                case PositionBucket.LB: bucketIdx = 7; break;
                case PositionBucket.CB: bucketIdx = 8; break;
                case PositionBucket.S: bucketIdx = 9; break;
                case PositionBucket.K: bucketIdx = 10; break;
                default: bucketIdx = 11; break;
            }
            int[] ranges = JerseyRanges[bucketIdx];
            for (int attempt = 0; attempt < 60; attempt++)
            {
                int r = rng.Range(0, ranges.Length / 2) * 2;
                int n = rng.Range(ranges[r], ranges[r + 1] + 1);
                if (used == null || used.Add(n)) return n;
            }
            for (int n = 1; n < 100; n++) if (used == null || used.Add(n)) return n;
            return 0;
        }

        /// <summary>Scales contract money so the roster fits under the cap with some room to spare.</summary>
        internal static void FitContractsToCap(Team team, Rng rng)
        {
            double target = LeagueRules.SalaryCap * (0.90 + 0.07 * rng.NextDouble());
            long used = team.CapUsed;
            if (used <= target) return;
            double factor = target / used;
            for (int i = 0; i < team.Roster.Count; i++)
            {
                Player p = team.Roster[i];
                if (p.Contract.Aav <= Contract.MinSalary) continue;
                p.Contract = ContractModel.Scale(p.Contract, factor);
            }
            // rounding / minimum-salary floors can leave a small overshoot
            int guard = 0;
            while (team.CapUsed > LeagueRules.SalaryCap && guard++ < 50)
                for (int i = 0; i < team.Roster.Count; i++)
                    if (team.Roster[i].Contract.Aav > Contract.MinSalary * 2)
                        team.Roster[i].Contract = ContractModel.Scale(team.Roster[i].Contract, 0.97);
        }

        // ---------------------------------------------------------------- coaching
        internal static void BuildStaff(Team team, Rng rng, HashSet<string> usedNames, float quality)
        {
            team.Staff.Clear();
            CoachRole[] roles = { CoachRole.HeadCoach, CoachRole.OffensiveCoordinator, CoachRole.DefensiveCoordinator };
            for (int i = 0; i < roles.Length; i++)
                team.Staff.Add(CreateCoach(rng, usedNames, roles[i], team.Id * 10 + i, quality));
        }

        public static Coach CreateCoach(Rng rng, HashSet<string> usedNames, CoachRole role, int id, float quality)
        {
            string first, last;
            NameGenerator.PersonName(rng, usedNames, out first, out last);
            var c = new Coach
            {
                Id = id, Name = first + " " + last, Role = role, Age = rng.Range(38, 69),
                OffenseRating = Rate(rng, 66f + quality), DefenseRating = Rate(rng, 66f + quality),
                Development = Rate(rng, 65f + quality * 0.5f), Scouting = Rate(rng, 64f),
                Aggression = rng.Range(15, 90), OffScheme = (OffenseScheme)rng.Range(0, 5), DefScheme = (DefenseScheme)rng.Range(0, 5)
            };
            switch (c.OffScheme)
            {
                case OffenseScheme.AirRaid: c.PassTendency = 66 + rng.Range(-4, 5); break;
                case OffenseScheme.WestCoast: c.PassTendency = 56 + rng.Range(-4, 5); break;
                case OffenseScheme.PowerRun: c.PassTendency = 38 + rng.Range(-4, 5); break;
                case OffenseScheme.SpreadOption: c.PassTendency = 46 + rng.Range(-4, 5); break;
                default: c.PassTendency = 50 + rng.Range(-4, 5); break;
            }
            c.Prestige = (c.OffenseRating + c.DefenseRating) / 2;
            return c;
        }

        private static int Rate(Rng rng, float mean)
        {
            return Math.Max(40, Math.Min(99, (int)Math.Round(rng.Gaussian(mean, 9f))));
        }
    }
}
