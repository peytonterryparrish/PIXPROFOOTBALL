using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>Builds and maintains the fictional FBS-style college world (driven by the committed CSV data).</summary>
    public static class CollegeFactory
    {
        public const int DefaultStartYear = 2026;
        

        // relative strength by conference id (key); unlisted conferences get 0
        private static float ConferenceStrength(string key)
        {
            switch (key)
            {
                case "great_lakes_union": return 1.0f;
                case "southern_heritage_league": return 0.9f;
                case "tidewater_athletic_union": return 0.5f;
                case "prairie_twelve": return 0.45f;
                case "pacific_crest_union": return 0.2f;
                case "heartland_athletic_union": return 0f;
                case "solar_coast_conference": return -0.05f;
                case "summit_range_alliance": return -0.1f;
                case "midland_athletic_league": return -0.15f;
                case "continental_sports_league": return -0.2f;
                default: return 0f;
            }
        }

        // star tiers by rank: shares of 6,5,4,3,2,1 star programs (8,18,30,38,28,16 of 138)
        private static readonly float[] TierShares = { 8f, 18f, 30f, 38f, 28f, 16f };

        public static int[] TierCountsFor(int schools)
        {
            var counts = new int[TierShares.Length];
            int used = 0;
            for (int i = 0; i < counts.Length - 1; i++) { counts[i] = (int)Math.Round(TierShares[i] * schools / 138f); used += counts[i]; }
            counts[counts.Length - 1] = Math.Max(0, schools - used);
            return counts;
        }

        private static string Initials(string name)
        {
            var parts = name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++) sb.Append(char.ToUpperInvariant(parts[i][0]));
            return sb.ToString();
        }

        private static string ColorFromWord(string word)
        {
            switch (word.ToLowerInvariant())
            {
                case "amber": return "#F2A900";
                case "maroon": return "#6D1A2A";
                case "crimson": return "#B0132B";
                case "scarlet": return "#D81E1E";
                case "golden": case "gold": return "#E0B000";
                case "blue": case "azure": return "#1C54B2";
                case "emerald": case "green": return "#0F7B4A";
                case "violet": case "purple": return "#5B2C8C";
                case "silver": return "#A8B0B8";
                case "orange": return "#E8731A";
                case "teal": return "#0E8C8C";
                case "ivory": return "#F3EBD3";
                case "black": case "onyx": return "#161616";
                case "cobalt": return "#1F3FA8";
                case "copper": return "#B4622D";
            }
            return null;
        }

        /// <summary>
        /// Builds the college world from the committed data files (see <see cref="CollegeData"/>). Identities come from the data;
        /// only prestige variance, colours without a colour word, rosters and staff are seeded.
        /// </summary>
        public static CollegeWorld CreateWorld(int seed, IReadOnlyList<SchoolDef> schools, IReadOnlyList<ConfBowlDef> confBowls, int startYear = DefaultStartYear)
        {
            if (schools == null || schools.Count == 0) throw new ArgumentException("No schools supplied.");
            var rng = new Rng(seed);
            var world = new CollegeWorld { Seed = seed, Year = startYear };
            var usedNames = new HashSet<string>();
            var abbrs = new HashSet<string>();

            // conferences come from the data (in file order)
            var confIndex = new Dictionary<string, int>();
            if (confBowls != null)
            {
                for (int i = 0; i < confBowls.Count; i++)
                {
                    ConfBowlDef d = confBowls[i];
                    if (d.IsConference)
                    {
                        int id = world.Conferences.Count;
                        confIndex[d.Id] = id;
                        world.Conferences.Add(new Conference { Id = id, Key = d.Id, Name = d.Name, Abbreviation = Initials(d.Name), IsCollege = true, Logo = d.Logo });
                    }
                    else if (d.IsBowl) world.Bowls.Add(d);
                    else if (d.Category == "playoff") world.PlayoffName = d.Name;
                    else if (d.Category == "national_championship") world.ChampionshipName = d.Name;
                }
            }
            // conferences that appear only in schools.csv
            for (int i = 0; i < schools.Count; i++)
            {
                if (schools[i].IsIndependent || confIndex.ContainsKey(schools[i].ConferenceId)) continue;
                int id = world.Conferences.Count;
                confIndex[schools[i].ConferenceId] = id;
                world.Conferences.Add(new Conference { Id = id, Key = schools[i].ConferenceId, Name = schools[i].Conference, Abbreviation = Initials(schools[i].Conference), IsCollege = true });
            }

            int n = schools.Count;
            var strengths = new float[n];
            var confOf = new int[n];
            for (int i = 0; i < n; i++)
            {
                int ci = -1;
                confOf[i] = !schools[i].IsIndependent && confIndex.TryGetValue(schools[i].ConferenceId, out ci) ? ci : -1;
                strengths[i] = ConferenceStrength(schools[i].ConferenceId) + rng.Gaussian(0f, 0.95f);
            }
            var order = new List<int>();
            for (int i = 0; i < n; i++) order.Add(i);
            order.Sort((a, b) => { int c = strengths[b].CompareTo(strengths[a]); return c != 0 ? c : a.CompareTo(b); });
            var prestige = new int[n];
            int[] tierCounts = TierCountsFor(n);
            int pos = 0;
            for (int t = 0; t < tierCounts.Length; t++)
                for (int k = 0; k < tierCounts[t] && pos < n; k++) prestige[order[pos++]] = 6 - t;

            for (int i = 0; i < n; i++)
            {
                Rng sr = rng.Fork();
                SchoolDef def = schools[i];
                string place = def.Place;
                var school = new School
                {
                    Id = i, Key = def.Id, Logo = def.Logo, Name = def.Name, ShortName = place, Mascot = def.Nickname,
                    Abbreviation = NameGenerator.MakeAbbreviation(place, abbrs, sr),
                    ConferenceId = confOf[i], Prestige = prestige[i], PrestigePoints = prestige[i] * 16f - 8f + sr.Range(0f, 4f),
                    City = place, State = "",
                    Region = confOf[i] >= 0 ? (Region)(confOf[i] % 8) : (Region)sr.Range(0, 8)
                };
                string firstWord = def.Name.Split(' ')[0];
                school.PrimaryColor = ColorFromWord(firstWord) ?? NameGenerator.RandomColor(sr, 0.3f);
                school.SecondaryColor = NameGenerator.HsvToHex(sr.NextFloat(), 0.15f + 0.3f * sr.NextFloat(), 0.88f + 0.12f * sr.NextFloat());
                school.StadiumName = StadiumFor(school, sr);
                school.StadiumCapacity = 28000 + school.Prestige * 9500 + sr.Range(0, 9000);
                school.Team = new Team
                {
                    Id = i, City = school.ShortName, Nickname = school.Mascot, Abbreviation = school.Abbreviation,
                    PrimaryColor = school.PrimaryColor, SecondaryColor = school.SecondaryColor,
                    ConferenceId = school.ConferenceId, DivisionId = -1, IsCollege = true, StadiumName = school.StadiumName,
                    Prestige = school.Prestige, SalaryCap = 0
                };
                world.Schools.Add(school);
                if (school.ConferenceId >= 0) world.Conferences[school.ConferenceId].TeamIds.Add(i);
            }

            for (int i = 0; i < world.Schools.Count; i++)
            {
                Rng tr = rng.Fork();
                School s = world.Schools[i];
                BuildStaff(s, tr, usedNames);
                FillRoster(s, tr, world, usedNames, world.Year, true);
                DepthChartBuilder.Build(s.Team);
            }

            world.Season = Season.CreateCollege(world, startYear, rng.Fork());
            world.SaveRng(rng);
            world.Bind();
            return world;
        }

        private static readonly string[] StadiumKinds = { "Stadium", "Field", "Memorial Stadium", "Bowl", "Coliseum", "Grounds" };

        private static string StadiumFor(School s, Rng rng)
        {
            return rng.Chance(0.4f) ? s.ShortName + " " + StadiumKinds[rng.Range(0, StadiumKinds.Length)]
                                    : NameData.GeoWords[rng.Range(0, NameData.GeoWords.Length)] + " " + StadiumKinds[rng.Range(0, StadiumKinds.Length)];
        }

        internal static void BuildStaff(School s, Rng rng, HashSet<string> usedNames)
        {
            s.Team.Staff.Clear();
            float quality = (s.Prestige - 3.5f) * 3f;
            CoachRole[] roles = { CoachRole.HeadCoach, CoachRole.OffensiveCoordinator, CoachRole.DefensiveCoordinator };
            for (int i = 0; i < roles.Length; i++)
                s.Team.Staff.Add(LeagueFactory.CreateCoach(rng, usedNames, roles[i], s.Id * 10 + i, quality));
            s.Team.ScoutingPoints = 0;
        }

        /// <summary>Potential-rating mean for the recruits of a given prestige level.</summary>
        public static float RecruitMean(School s)
        {
            return 48.2f + s.Prestige * 3.3f + (s.PrestigePoints - s.Prestige * 16f) * 0.05f;
        }

        public static int StarsFromPotential(int potential, Rng rng)
        {
            float v = potential + rng.Gaussian(0f, 3f);
            if (v >= 82f) return 5;
            if (v >= 75f) return 4;
            if (v >= 68f) return 3;
            if (v >= 61f) return 2;
            return 1;
        }

        private static readonly ClassYear[] Classes = { ClassYear.Freshman, ClassYear.Sophomore, ClassYear.Junior, ClassYear.Senior };

        private static int AgeFor(ClassYear c, Rng rng)
        {
            return 18 + (int)c + (rng.Chance(0.5f) ? 1 : 0);
        }

        /// <summary>Creates one recruit for the school at the given class year.</summary>
        public static Player CreatePlayer(School s, Rng rng, IPlayerIdSource ids, HashSet<string> usedNames, Position pos, ClassYear cls, int year)
        {
            float mean = RecruitMean(s);
            int pot = (int)Math.Round(rng.Gaussian(mean, 7.8f));
            pot = Math.Max(42, Math.Min(97, pot));
            int age = AgeFor(cls, rng);
            Player p = PlayerFactory.Create(rng, ids, usedNames, pos, pot, age);
            p.Class = cls;
            p.Experience = (int)cls;
            p.Stars = StarsFromPotential(pot, rng);
            p.Bio.College = s.ShortName;
            p.Bio.JerseyNumber = 0;
            p.TeamId = s.Team.Id;
            p.DraftYear = 0;
            p.Contract = new Contract();
            return p;
        }

        internal static void FillRoster(School s, Rng rng, CollegeWorld world, HashSet<string> usedNames, int year, bool fullClasses)
        {
            var jerseys = new HashSet<int>();
            for (int pi = 0; pi < LeagueRules.RosterOrder.Length; pi++)
            {
                Position pos = LeagueRules.RosterOrder[pi];
                int slots = LeagueRules.CollegeSlots(pos);
                for (int i = 0; i < slots; i++)
                {
                    ClassYear cls = Classes[i % 4];
                    // keep each position's classes balanced; shift the starting class so roster totals are even
                    cls = Classes[(i + pi) % 4];
                    Player p = CreatePlayer(s, rng, world, usedNames, pos, cls, year);
                    p.Bio.JerseyNumber = LeagueFactory.PickJersey(pos, rng, jerseys);
                    MaybeRedshirt(p, rng);
                    s.Team.AddPlayer(p);
                }
            }
        }

        private static void MaybeRedshirt(Player p, Rng rng)
        {
            if (p.Class == ClassYear.Freshman && rng.Chance(0.35f))
            {
                p.IsRedshirting = true;
            }
        }

        /// <summary>
        /// Rolls the world forward one year: progression, graduation, early-entry removals and a new recruiting class.
        /// <paramref name="removedIds"/> are players who left (drafted / signed); may be null.
        /// </summary>
        public static void AdvanceYear(CollegeWorld world, Rng rng, HashSet<int> removedIds)
        {
            var usedNames = new HashSet<string>();
            for (int i = 0; i < world.Schools.Count; i++)
                for (int j = 0; j < world.Schools[i].Team.Roster.Count; j++)
                {
                    var pl = world.Schools[i].Team.Roster[j];
                    usedNames.Add(pl.Name);
                }

            world.Year++;
            for (int i = 0; i < world.Schools.Count; i++)
            {
                School s = world.Schools[i];
                Team t = s.Team;
                Coach hc = t.HeadCoach;
                int dev = hc != null ? hc.Development : 65;
                var keep = new List<Player>();
                for (int j = 0; j < t.Roster.Count; j++)
                {
                    Player p = t.Roster[j];
                    if (removedIds != null && removedIds.Contains(p.Id)) continue;
                    bool graduating = p.Class == ClassYear.Senior && !p.IsRedshirting;
                    if (graduating) { p.Retired = true; continue; }
                    if (p.IsRedshirting)
                    {
                        p.IsRedshirting = false;
                        p.RedshirtUsed = true;
                    }
                    else if (p.Class != ClassYear.Senior) p.Class = (ClassYear)((int)p.Class + 1);
                    Progression.AdvanceCollege(p, rng, dev);
                    p.Injury.WeeksOut = 0; p.Injury.Type = "";
                    keep.Add(p);
                }
                t.Roster = keep;
                t.InvalidateIndex();

                // recruiting: refill each position to its target
                var jerseys = new HashSet<int>();
                for (int j = 0; j < t.Roster.Count; j++) jerseys.Add(t.Roster[j].Bio.JerseyNumber);
                for (int pi = 0; pi < LeagueRules.RosterOrder.Length; pi++)
                {
                    Position pos = LeagueRules.RosterOrder[pi];
                    int have = t.CountAt(pos);
                    int target = LeagueRules.CollegeSlots(pos);
                    for (int k = have; k < target; k++)
                    {
                        Player p = CreatePlayer(s, rng, world, usedNames, pos, ClassYear.Freshman, world.Year);
                        p.Bio.JerseyNumber = LeagueFactory.PickJersey(pos, rng, jerseys);
                        MaybeRedshirt(p, rng);
                        t.AddPlayer(p);
                    }
                }
                DepthChartBuilder.Build(t);
            }
            world.Season = Season.CreateCollege(world, world.Year, rng.Fork());
            world.Bind();
        }

        /// <summary>Drifts prestige from the season's results (call after the season completes).</summary>
        public static void UpdatePrestige(CollegeWorld world, Season season)
        {
            if (season == null) return;
            var tiers = new List<School>(world.Schools);
            for (int i = 0; i < world.Schools.Count; i++)
            {
                School s = world.Schools[i];
                int wins = season.WinsOf(s.Id), losses = season.LossesOf(s.Id);
                float expected = 4f + s.Prestige * 1.1f; // wins
                float delta = (wins - expected) * 1.6f - (losses > 6 ? 2f : 0f);
                if (season.ChampionTeamId == s.Id) delta += 8f;
                s.PrestigePoints = Math.Max(0f, Math.Min(100f, s.PrestigePoints + delta * 0.5f - (s.PrestigePoints - (s.Prestige * 16f - 8f)) * 0.05f));
            }
            // re-rank into the same tier counts so the world stays balanced
            tiers.Sort((a, b) => b.PrestigePoints.CompareTo(a.PrestigePoints));
            int pos = 0;
            int[] tc = TierCountsFor(tiers.Count);
            for (int t = 0; t < tc.Length; t++)
                for (int k = 0; k < tc[t]; k++) { tiers[pos].Prestige = 6 - t; tiers[pos].Team.Prestige = 6 - t; pos++; }
        }
    }
}
