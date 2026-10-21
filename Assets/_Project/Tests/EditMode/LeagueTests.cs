using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    public class NameGeneratorTests
    {
        [Test]
        public void PoolsAreLargeAndDistinct()
        {
            Assert.GreaterOrEqual(NameGenerator.FirstNames.Length, 400);
            Assert.GreaterOrEqual(NameGenerator.LastNames.Length, 600);
            Assert.AreEqual(NameGenerator.FirstNames.Length, NameGenerator.FirstNames.Distinct().Count());
            Assert.AreEqual(NameGenerator.LastNames.Length, NameGenerator.LastNames.Distinct().Count());
            Assert.GreaterOrEqual(NameGenerator.Cities.Count, 50);
            Assert.GreaterOrEqual(NameGenerator.Nicknames.Count, 60);
        }

        [Test]
        public void GenerationIsDeterministic()
        {
            var a = new Rng(5); var b = new Rng(5);
            for (int i = 0; i < 200; i++)
            {
                Assert.AreEqual(NameGenerator.FirstName(a), NameGenerator.FirstName(b));
                Assert.AreEqual(NameGenerator.PlaceName(a), NameGenerator.PlaceName(b));
            }
        }

        [Test]
        public void PersonNamesAreUniqueWithinASet()
        {
            var rng = new Rng(9);
            var used = new HashSet<string>();
            for (int i = 0; i < 3000; i++)
            {
                string f, l;
                NameGenerator.PersonName(rng, used, out f, out l);
            }
            Assert.AreEqual(3000, used.Count);
        }

        [Test]
        public void ProTeamNamesAvoidRepeats()
        {
            var rng = new Rng(2);
            var used = new HashSet<string>();
            for (int i = 0; i < 32; i++)
            {
                string c, n;
                NameGenerator.ProTeamName(rng, used, out c, out n);
                Assert.IsNotEmpty(c); Assert.IsNotEmpty(n);
            }
            Assert.AreEqual(32, used.Count);
        }

        [Test]
        public void SchoolNamesAreOriginalAndNeverAggieStyle()
        {
            var rng = new Rng(77);
            var names = new HashSet<string>(); var abbr = new HashSet<string>(); var mascots = new HashSet<string>();
            for (int i = 0; i < 400; i++)
            {
                SchoolIdentity id = NameGenerator.SchoolName(rng, names, abbr, mascots);
                string hit;
                Assert.IsFalse(LeagueTestUtil.ContainsBlacklisted(id.Name, LeagueTestUtil.RealSchoolBlacklist, out hit), id.Name + " matches real school " + hit);
                Assert.IsFalse(id.Name.Contains("A&M"), id.Name);
                Assert.IsFalse(string.IsNullOrEmpty(id.Mascot));
                Assert.IsTrue(id.Abbreviation.Length >= 2 && id.Abbreviation.Length <= 4, id.Abbreviation);
            }
            Assert.AreEqual(400, names.Count);
        }
    }

    public class ProLeagueTests
    {
        private static ProLeague _lg;
        private static ProLeague Lg { get { return _lg ?? (_lg = LeagueFactory.CreatePro(2024)); } }

        [Test]
        public void HasThirtyTwoTeamsInTwoConferencesAndEightDivisions()
        {
            Assert.AreEqual(32, Lg.Teams.Count);
            Assert.AreEqual(2, Lg.Conferences.Count);
            int divs = 0;
            foreach (Conference c in Lg.Conferences)
            {
                Assert.AreEqual(4, c.Divisions.Count);
                Assert.AreEqual(16, c.TeamIds.Count);
                foreach (Division d in c.Divisions)
                {
                    divs++;
                    Assert.AreEqual(4, d.TeamIds.Count);
                    foreach (int id in d.TeamIds)
                    {
                        Assert.AreEqual(c.Id, Lg.Teams[id].ConferenceId);
                        Assert.AreEqual(d.Id, Lg.Teams[id].DivisionId);
                    }
                }
            }
            Assert.AreEqual(8, divs);
        }

        [Test]
        public void TeamIdentitiesAreUniqueFictionalAndHaveColors()
        {
            var full = new HashSet<string>(); var abbr = new HashSet<string>(); var nick = new HashSet<string>();
            foreach (Team t in Lg.Teams)
            {
                Assert.IsTrue(full.Add(t.FullName), t.FullName);
                Assert.IsTrue(abbr.Add(t.Abbreviation), t.Abbreviation);
                Assert.IsTrue(nick.Add(t.Nickname), t.Nickname);
                Assert.AreEqual(3, t.Abbreviation.Length);
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(t.PrimaryColor, "^#[0-9A-Fa-f]{6}$"), t.PrimaryColor);
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(t.SecondaryColor, "^#[0-9A-Fa-f]{6}$"), t.SecondaryColor);
                Assert.AreNotEqual(t.PrimaryColor, t.SecondaryColor);
                string hit;
                Assert.IsFalse(LeagueTestUtil.ContainsBlacklisted(t.Nickname, LeagueTestUtil.RealProNicknames, out hit), t.FullName + " uses real nickname " + hit);
                Assert.IsNotNull(t.HeadCoach); Assert.IsNotNull(t.OffensiveCoordinator); Assert.IsNotNull(t.DefensiveCoordinator);
            }
        }

        [Test]
        public void EveryRosterHasFiftyThreeWithRealisticPositionCounts()
        {
            foreach (Team t in Lg.Teams)
            {
                Assert.AreEqual(53, t.Roster.Count, t.FullName);
                foreach (PositionBucket b in PositionBuckets.All)
                {
                    int n = t.Roster.Count(p => PositionBuckets.Of(p.Position) == b);
                    Assert.AreEqual(PositionBuckets.ProCount(b), n, t.FullName + " " + b);
                }
                // every one of the 21 formation positions is covered with its configured depth
                foreach (Position pos in LeagueRules.RosterOrder) Assert.AreEqual(LeagueRules.ProSlots(pos), t.CountAt(pos), t.FullName + " " + pos);
            }
        }

        [Test]
        public void PlayersHaveSaneRatingsBioAndAges()
        {
            foreach (Player p in Lg.AllRosterPlayers())
            {
                Assert.GreaterOrEqual(p.Age, 21, p.ToString());
                Assert.LessOrEqual(p.Age, 37, p.ToString());
                Assert.GreaterOrEqual(p.Potential, 40);
                Assert.LessOrEqual(p.Potential, 99);
                Assert.LessOrEqual(p.Overall, p.Potential + 1, p.ToString());
                for (int k = 0; k < Ratings.KindCount; k++)
                {
                    int v = p.Ratings.Get((RatingKind)k);
                    Assert.IsTrue(v >= 0 && v <= 99);
                }
                Assert.IsNotEmpty(p.Bio.FirstName); Assert.IsNotEmpty(p.Bio.LastName); Assert.IsNotEmpty(p.Bio.Hometown);
                Assert.Greater(p.Bio.HeightIn, 60); Assert.Greater(p.Bio.WeightLb, 150);
                Assert.GreaterOrEqual(p.Bio.JerseyNumber, 1);
                Assert.IsNotNull(p.Contract);
                Assert.IsTrue(Enum.IsDefined(typeof(DevTrait), p.Dev));
            }
            Assert.GreaterOrEqual(Lg.AllRosterPlayers().Min(p => p.Age), 21);
            Assert.GreaterOrEqual(Lg.AllRosterPlayers().Max(p => p.Age), 33, "league should include veterans");
        }

        [Test]
        public void PositionArchetypesShapeRatings()
        {
            var power = Lg.AllRosterPlayers().Where(p => p.Archetype == Archetype.RbPower).ToList();
            var speed = Lg.AllRosterPlayers().Where(p => p.Archetype == Archetype.RbSpeed).ToList();
            Assert.Greater(power.Count, 10); Assert.Greater(speed.Count, 10);
            Assert.Greater(power.Average(p => p.Ratings.Strength), speed.Average(p => p.Ratings.Strength));
            Assert.Greater(speed.Average(p => p.Ratings.Elusiveness), power.Average(p => p.Ratings.Elusiveness));
            var pocket = Lg.AllRosterPlayers().Where(p => p.Archetype == Archetype.QbPocket).ToList();
            var scram = Lg.AllRosterPlayers().Where(p => p.Archetype == Archetype.QbScrambler).ToList();
            Assert.Greater(scram.Average(p => p.Ratings.Speed), pocket.Average(p => p.Ratings.Speed) + 8);
            var deep = Lg.AllRosterPlayers().Where(p => p.Archetype == Archetype.WrDeepThreat).ToList();
            var poss = Lg.AllRosterPlayers().Where(p => p.Archetype == Archetype.WrPossession).ToList();
            Assert.Greater(deep.Average(p => p.Ratings.Speed), poss.Average(p => p.Ratings.Speed));
            Assert.Greater(poss.Average(p => p.Ratings.Catching), deep.Average(p => p.Ratings.Catching) - 3);
        }

        [Test]
        public void RatingsFollowAgeAndPotential()
        {
            var young = Lg.AllRosterPlayers().Where(p => p.Age <= 23).ToList();
            var prime = Lg.AllRosterPlayers().Where(p => p.Age >= 26 && p.Age <= 29).ToList();
            Assert.Greater(young.Average(p => p.Potential - p.Overall), prime.Average(p => p.Potential - p.Overall) + 2.0);
            var star = Lg.AllRosterPlayers().Where(p => p.Potential >= 85).ToList();
            var rest = Lg.AllRosterPlayers().Where(p => p.Potential < 70).ToList();
            Assert.Greater(star.Average(p => p.Overall), rest.Average(p => p.Overall) + 10);
        }

        [Test]
        public void ContractsRespectTheCapAndMinimumSalary()
        {
            foreach (Team t in Lg.Teams)
            {
                Assert.LessOrEqual(t.CapUsed, LeagueRules.SalaryCap, t.FullName + " is over the cap");
                Assert.GreaterOrEqual(t.CapSpace, 0);
                foreach (Player p in t.Roster)
                {
                    Assert.GreaterOrEqual(p.Contract.Aav, LeagueRules.MinimumSalary, p.ToString());
                    Assert.GreaterOrEqual(p.Contract.CapHit, LeagueRules.MinimumSalary, p.ToString());
                    Assert.GreaterOrEqual(p.Contract.YearsLeft, 1);
                    Assert.LessOrEqual(p.Contract.YearsLeft, p.Contract.Years);
                    Assert.AreEqual(p.Contract.Years, p.Contract.CapHits.Count);
                    Assert.LessOrEqual(p.Contract.Guaranteed, p.Contract.Aav * p.Contract.Years);
                }
            }
            Assert.AreEqual(255000000L, LeagueRules.SalaryCap);
            Assert.IsTrue(Lg.AllRosterPlayers().Any(p => p.Contract.Rookie), "rookie scale deals expected");
            Assert.Greater(Lg.AllRosterPlayers().Max(p => p.Contract.Aav), Lg.AllRosterPlayers().Average(p => p.Contract.Aav) * 4);
        }

        [Test]
        public void RookieScaleDecreasesWithPickNumber()
        {
            long prev = long.MaxValue;
            for (int pick = 1; pick <= 260; pick += 3)
            {
                long v = ContractModel.RookieAavForPick(pick);
                Assert.LessOrEqual(v, prev);
                Assert.GreaterOrEqual(v, LeagueRules.MinimumSalary);
                prev = v;
            }
            Assert.Greater(ContractModel.RookieAavForPick(1), 5 * ContractModel.RookieAavForPick(100));
        }

        [Test]
        public void NamesAreUniqueAcrossTheLeagueAndWithinEachTeam()
        {
            var all = new HashSet<string>();
            foreach (Team t in Lg.Teams)
            {
                var local = new HashSet<string>();
                var jerseys = new HashSet<int>();
                foreach (Player p in t.Roster)
                {
                    Assert.IsTrue(local.Add(p.Name), p.Name + " duplicated on " + t.FullName);
                    Assert.IsTrue(all.Add(p.Name), p.Name + " duplicated in the league");
                    Assert.IsTrue(jerseys.Add(p.Bio.JerseyNumber), "duplicate jersey " + p.Bio.JerseyNumber + " on " + t.FullName);
                }
            }
            var ids = new HashSet<int>();
            foreach (Player p in Lg.AllRosterPlayers()) Assert.IsTrue(ids.Add(p.Id));
            foreach (Player p in Lg.FreeAgents) Assert.IsTrue(ids.Add(p.Id));
        }

        [Test]
        public void DepthChartsListStartersByOverall()
        {
            foreach (Team t in Lg.Teams)
            {
                foreach (Position pos in LeagueRules.RosterOrder)
                {
                    List<int> ids;
                    Assert.IsTrue(t.Depth.TryGet(pos, out ids), pos.ToString());
                    Assert.GreaterOrEqual(ids.Count, LeagueRules.Starters(pos));
                    int prev = 999;
                    for (int i = 0; i < ids.Count; i++)
                    {
                        Player p = t.GetPlayer(ids[i]);
                        Assert.IsNotNull(p);
                        int o = p.Ratings.Overall(pos);
                        if (p.Position == pos) { Assert.LessOrEqual(o, prev, t.FullName + " " + pos + " depth order"); prev = o; }
                    }
                }
                Assert.GreaterOrEqual(t.TeamOverall(), 55);
                Assert.LessOrEqual(t.TeamOverall(), 95);
            }
            int spread = Lg.Teams.Max(t => t.TeamOverall()) - Lg.Teams.Min(t => t.TeamOverall());
            Assert.GreaterOrEqual(spread, 6, "teams should differ in strength");
        }

        [Test]
        public void SameSeedGivesIdenticalLeagueAndDifferentSeedDoesNot()
        {
            ProLeague a = LeagueFactory.CreatePro(123);
            ProLeague b = LeagueFactory.CreatePro(123);
            ProLeague c = LeagueFactory.CreatePro(124);
            Assert.AreEqual(LeagueTestUtil.Fingerprint(a), LeagueTestUtil.Fingerprint(b));
            Assert.AreNotEqual(LeagueTestUtil.Fingerprint(a), LeagueTestUtil.Fingerprint(c));
            Assert.AreEqual(LeagueTestUtil.Fingerprint(a.Season), LeagueTestUtil.Fingerprint(b.Season));
            for (int i = 0; i < 32; i++) Assert.AreEqual(LeagueTestUtil.Fingerprint(a.Teams[i]), LeagueTestUtil.Fingerprint(b.Teams[i]));
        }

        [Test]
        public void FreeAgentPoolExistsAndIsUnsigned()
        {
            Assert.GreaterOrEqual(Lg.FreeAgents.Count, 50);
            foreach (Player p in Lg.FreeAgents) Assert.AreEqual(-1, p.TeamId);
        }
    }

    public class CollegeDataTests
    {
        [Test]
        public void CsvParserHandlesQuotesBomAndBlankLines()
        {
            var rows = CollegeData.ParseCsv("﻿\"a\",\"b,c\",\"d\"\"e\"\r\n1,2,3\r\n\r\n");
            Assert.AreEqual(2, rows.Count);
            CollectionAssert.AreEqual(new[] { "a", "b,c", "d\"e" }, rows[0]);
            CollectionAssert.AreEqual(new[] { "1", "2", "3" }, rows[1]);
        }

        [Test]
        public void SchoolsFileParsesToTheOfficialRoster()
        {
            var schools = CollegeData.ParseSchools(LeagueTestUtil.SchoolsCsv());
            Assert.AreEqual(138, schools.Count);
            Assert.AreEqual(138, schools.Select(s => s.Name).Distinct().Count());
            var counts = schools.GroupBy(s => s.Conference).ToDictionary(g => g.Key, g => g.Count());
            Assert.AreEqual(18, counts["Great Lakes Union"]);
            Assert.AreEqual(17, counts["Tidewater Athletic Union"]);
            Assert.AreEqual(16, counts["Southern Heritage League"]);
            Assert.AreEqual(16, counts["Prairie Twelve"]);
            Assert.AreEqual(14, counts["Heartland Athletic Union"]);
            Assert.AreEqual(14, counts["Solar Coast Conference"]);
            Assert.AreEqual(13, counts["Midland Athletic League"]);
            Assert.AreEqual(10, counts["Summit Range Alliance"]);
            Assert.AreEqual(10, counts["Continental Sports League"]);
            Assert.AreEqual(8, counts["Pacific Crest Union"]);
            Assert.AreEqual(2, counts["Independent"]);
            var amber = schools.First(s => s.Name == "Amber Falls Skyhawks");
            Assert.AreEqual("Skyhawks", amber.Nickname);
            Assert.AreEqual("Amber Falls", amber.Place);
            Assert.AreEqual("summit_range_alliance", amber.ConferenceId);
        }

        [Test]
        public void ConferenceBowlFileHasTheOfficialFortyOneBowls()
        {
            var defs = CollegeData.ParseConfBowls(LeagueTestUtil.ConfBowlsCsv());
            Assert.AreEqual(53, defs.Count);
            Assert.AreEqual(10, defs.Count(d => d.IsConference));
            Assert.AreEqual(41, defs.Count(d => d.IsBowl));
            Assert.AreEqual(6, defs.Count(d => d.IsPlayoffBowl));
            var cfp = defs.Where(d => d.IsPlayoffBowl).Select(d => d.Name).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(new[] { "Citrus Comet Bowl", "Cottoncrest Classic", "Crystal Cane Bowl", "Festival Flame Bowl", "Orchard Gold Bowl", "Thorncrest Bowl" }, cfp);
            Assert.IsTrue(defs.Any(d => d.Name == "Gridiron Crown Playoff" && d.Category == "playoff"));
            Assert.IsTrue(defs.Any(d => d.Name == "Gridiron Crown Championship" && d.Category == "national_championship"));
        }
    }

    public class CollegeWorldTests
    {
        private static CollegeWorld _w;
        private static CollegeWorld W { get { return _w ?? (_w = LeagueTestUtil.LoadCollege(99)); } }

        [Test]
        public void Has138SchoolsInTenConferencesPlusTwoIndependents()
        {
            Assert.AreEqual(138, W.Schools.Count);
            Assert.AreEqual(10, W.Conferences.Count);
            var expected = new Dictionary<string, int>
            {
                { "Great Lakes Union", 18 }, { "Tidewater Athletic Union", 17 }, { "Southern Heritage League", 16 }, { "Prairie Twelve", 16 },
                { "Heartland Athletic Union", 14 }, { "Solar Coast Conference", 14 }, { "Midland Athletic League", 13 },
                { "Summit Range Alliance", 10 }, { "Continental Sports League", 10 }, { "Pacific Crest Union", 8 }
            };
            int members = 0;
            foreach (Conference c in W.Conferences)
            {
                Assert.AreEqual(expected[c.Name], c.TeamIds.Count, c.Name);
                members += c.TeamIds.Count;
                foreach (int id in c.TeamIds) Assert.AreEqual(c.Id, W.Schools[id].ConferenceId);
                Assert.IsTrue(c.IsCollege);
            }
            Assert.AreEqual(2, W.Schools.Count(s => s.Independent));
            Assert.AreEqual(138, members + 2);
            Assert.AreEqual(138, W.Schools.Select(s => s.Id).Distinct().Count());
            Assert.AreEqual(41, W.Bowls.Count);
            Assert.AreEqual("Gridiron Crown Playoff", W.PlayoffName);
            Assert.AreEqual("Gridiron Crown Championship", W.ChampionshipName);
        }

        [Test]
        public void SchoolIdentitiesComeFromTheDataFileAndAreNeverRealSchools()
        {
            var defs = CollegeData.ParseSchools(LeagueTestUtil.SchoolsCsv());
            var abbr = new HashSet<string>();
            for (int i = 0; i < W.Schools.Count; i++)
            {
                School s = W.Schools[i];
                Assert.AreEqual(defs[i].Name, s.Name);
                Assert.AreEqual(defs[i].Nickname, s.Mascot);
                Assert.AreEqual(defs[i].Name, s.Team.FullName);
                string hit;
                Assert.IsTrue(abbr.Add(s.Abbreviation), "duplicate abbreviation " + s.Abbreviation);
                Assert.IsFalse(LeagueTestUtil.ContainsBlacklisted(s.Name, LeagueTestUtil.RealSchoolBlacklist, out hit), s.Name + " ~ " + hit);
                Assert.IsFalse(LeagueTestUtil.ContainsBlacklisted(s.ShortName, LeagueTestUtil.RealSchoolBlacklist, out hit), s.ShortName + " ~ " + hit);
                Assert.IsFalse(s.Name.Contains("A&M"));
                Assert.IsNotEmpty(s.StadiumName);
                Assert.GreaterOrEqual(s.Prestige, 1); Assert.LessOrEqual(s.Prestige, 6);
                Assert.IsTrue(System.Text.RegularExpressions.Regex.IsMatch(s.PrimaryColor, "^#[0-9A-Fa-f]{6}$"));
            }
            foreach (Conference c in W.Conferences)
            {
                string hit;
                Assert.IsFalse(LeagueTestUtil.ContainsBlacklisted(c.Name, new[] { "Big Ten", "Big 12", "SEC", "ACC", "Pac-12", "Sun Belt", "Mountain West", "Big East" }, out hit), c.Name);
            }
            foreach (ConfBowlDef b in W.Bowls)
            {
                string hit;
                Assert.IsFalse(LeagueTestUtil.ContainsBlacklisted(b.Name, new[] { "Rose Bowl", "Sugar Bowl", "Orange Bowl", "Cotton Bowl", "Fiesta Bowl", "Peach Bowl", "Citrus Bowl" }, out hit), b.Name);
            }
            Assert.GreaterOrEqual(LeagueTestUtil.RealSchoolBlacklist.Length, 60);
        }

        [Test]
        public void PrestigeFollowsConferenceStrengthWithVariance()
        {
            double Avg(string key) { return W.Schools.Where(s => W.Conferences.Any(c => c.Id == s.ConferenceId && c.Key == key)).Average(s => s.Prestige); }
            double top = (Avg("great_lakes_union") + Avg("southern_heritage_league")) / 2;
            double mid = (Avg("tidewater_athletic_union") + Avg("prairie_twelve")) / 2;
            double low = (Avg("heartland_athletic_union") + Avg("solar_coast_conference") + Avg("summit_range_alliance") + Avg("midland_athletic_league") + Avg("continental_sports_league")) / 5;
            Assert.Greater(top, mid);
            Assert.Greater(mid, low);
            Assert.Greater(top, low + 0.6);
            // variance: strong conferences still contain weaker programs and vice versa
            Assert.IsTrue(W.Schools.Any(s => s.Prestige <= 3 && W.Conferences.Any(c => c.Id == s.ConferenceId && c.Key == "great_lakes_union")));
            Assert.IsTrue(W.Schools.Any(s => s.Prestige >= 5 && W.Conferences.Any(c => c.Id == s.ConferenceId && c.Key == "continental_sports_league")) ||
                          W.Schools.Any(s => s.Prestige >= 4 && W.Conferences.Any(c => c.Id == s.ConferenceId && c.Key == "midland_athletic_league")) ||
                          W.Schools.Any(s => s.Prestige >= 4 && W.Conferences.Any(c => c.Id == s.ConferenceId && c.Key == "heartland_athletic_union")));
        }

        [Test]
        public void PrestigeTiersAreBalancedAndTopProgramsAreStronger()
        {
            int[] counts = new int[7];
            foreach (School s in W.Schools) counts[s.Prestige]++;
            for (int star = 1; star <= 6; star++) Assert.Greater(counts[star], 0, star + " star schools");
            Assert.Greater(counts[3] + counts[4], counts[6] + counts[1]);
            double top = W.Schools.Where(s => s.Prestige >= 5).Average(s => s.Team.TeamOverall());
            double bottom = W.Schools.Where(s => s.Prestige <= 2).Average(s => s.Team.TeamOverall());
            Assert.Greater(top, bottom + 5, "5-6 star programs must be clearly stronger");
            double p6 = W.Schools.Where(s => s.Prestige == 6).Average(s => s.Team.TeamOverall());
            double p3 = W.Schools.Where(s => s.Prestige == 3).Average(s => s.Team.TeamOverall());
            Assert.Greater(p6, p3 + 3);
        }

        [Test]
        public void RostersHaveEightyFiveWithClassYearsAndStars()
        {
            foreach (School s in W.Schools)
            {
                Assert.AreEqual(85, s.Team.Roster.Count, s.Name);
                foreach (ClassYear c in new[] { ClassYear.Freshman, ClassYear.Sophomore, ClassYear.Junior, ClassYear.Senior })
                {
                    int n = s.Team.Roster.Count(p => p.Class == c);
                    Assert.GreaterOrEqual(n, 12, s.Name + " " + c);
                }
                foreach (Player p in s.Team.Roster)
                {
                    Assert.GreaterOrEqual(p.Stars, 1); Assert.LessOrEqual(p.Stars, 5);
                    Assert.GreaterOrEqual(p.Age, 18); Assert.LessOrEqual(p.Age, 24);
                    Assert.AreEqual(s.ShortName, p.Bio.College);
                    if (p.IsRedshirting) Assert.AreEqual(ClassYear.Freshman, p.Class);
                }
                foreach (Position pos in LeagueRules.RosterOrder) Assert.AreEqual(LeagueRules.CollegeSlots(pos), s.Team.CountAt(pos), s.Name + " " + pos);
            }
            int rs = W.Schools.Sum(s => s.Team.Roster.Count(p => p.IsRedshirting));
            Assert.Greater(rs, 100);
            double topStars = W.Schools.Where(s => s.Prestige == 6).SelectMany(s => s.Team.Roster).Average(p => p.Stars);
            double lowStars = W.Schools.Where(s => s.Prestige == 1).SelectMany(s => s.Team.Roster).Average(p => p.Stars);
            Assert.Greater(topStars, lowStars + 0.8);
            var all = W.Schools.SelectMany(s => s.Team.Roster).ToList();
            Assert.Greater(all.Where(p => p.Class == ClassYear.Senior).Average(p => p.Overall), all.Where(p => p.Class == ClassYear.Freshman).Average(p => p.Overall) + 2);
        }

        [Test]
        public void WorldIsDeterministic()
        {
            CollegeWorld a = LeagueTestUtil.LoadCollege(5), b = LeagueTestUtil.LoadCollege(5), c = LeagueTestUtil.LoadCollege(6);
            for (int i = 0; i < a.Schools.Count; i++)
            {
                Assert.AreEqual(a.Schools[i].Name, b.Schools[i].Name);
                Assert.AreEqual(a.Schools[i].Prestige, b.Schools[i].Prestige);
                Assert.AreEqual(LeagueTestUtil.Fingerprint(a.Schools[i].Team), LeagueTestUtil.Fingerprint(b.Schools[i].Team));
            }
            Assert.AreEqual(LeagueTestUtil.Fingerprint(a.Season), LeagueTestUtil.Fingerprint(b.Season));
            bool anyDifferent = false;
            for (int i = 0; i < a.Schools.Count; i++) if (a.Schools[i].Prestige != c.Schools[i].Prestige) anyDifferent = true;
            Assert.IsTrue(anyDifferent, "a different seed should vary prestige");
        }
    }

    public class ScoutingAndLifecycleTests
    {
        private static List<Prospect> MakeClass(int seed, out ProLeague lg)
        {
            lg = LeagueFactory.CreatePro(seed);
            return ProspectGenerator.Standalone(lg, 2026, new Rng(seed), null);
        }

        [Test]
        public void StandaloneDraftClassHasEnoughQualityPlayers()
        {
            ProLeague lg;
            var cls = MakeClass(3, out lg);
            Assert.GreaterOrEqual(cls.Count, 300);
            Assert.AreEqual(cls.Count, cls.Select(p => p.ConsensusRank).Distinct().Count());
            Assert.AreEqual(1, cls.Min(p => p.ConsensusRank));
            Assert.Greater(cls.Take(32).Average(p => p.TrueGrade), cls.Skip(200).Average(p => p.TrueGrade) + 8);
            Assert.IsTrue(cls.Any(p => p.Player.Position == Position.K));
            Assert.IsTrue(cls.Any(p => p.Player.Position == Position.QB));
            Assert.AreEqual(cls.Count, cls.Select(p => p.Id).Distinct().Count());
        }

        [Test]
        public void CollegeDraftClassComesFromSeniorsAndDeclaredJuniors()
        {
            CollegeWorld w = LeagueTestUtil.LoadCollege(8);
            var cls = ProspectGenerator.FromCollege(w, 2026, new Rng(1));
            Assert.GreaterOrEqual(cls.Count, 300);
            Assert.IsTrue(cls.All(p => p.ClassYear == ClassYear.Senior || p.ClassYear == ClassYear.Junior));
            Assert.IsTrue(cls.Any(p => p.DeclaredEarly), "some juniors declare");
            Assert.IsTrue(cls.Where(p => p.DeclaredEarly).All(p => p.ClassYear == ClassYear.Junior));
            Assert.IsTrue(cls.All(p => p.SchoolId >= 0 && !string.IsNullOrEmpty(p.SchoolName)));
            // best prospects come from the strong programs
            double topPrestige = cls.Take(40).Average(p => w.Schools[p.SchoolId].Prestige);
            double allPrestige = cls.Average(p => w.Schools[p.SchoolId].Prestige);
            Assert.GreaterOrEqual(topPrestige, allPrestige);
        }

        [Test]
        public void ScoutingNoiseShrinksWithPointsSpent()
        {
            ProLeague lg;
            var cls = MakeClass(4, out lg);
            var pool = cls.Take(120).ToList();
            double err0 = 0, err10 = 0, err60 = 0, err250 = 0;
            var r0 = new ScoutingReport(1, 2026, 11, 100000);
            var r10 = new ScoutingReport(1, 2026, 11, 100000);
            var r60 = new ScoutingReport(1, 2026, 11, 100000);
            var r250 = new ScoutingReport(1, 2026, 11, 100000);
            foreach (Prospect p in pool)
            {
                err0 += Math.Abs(r0.EstimatedGrade(p) - p.TrueGrade);
                r10.Reveal(p, 10); err10 += Math.Abs(r10.EstimatedGrade(p) - p.TrueGrade);
                r60.Reveal(p, 60); err60 += Math.Abs(r60.EstimatedGrade(p) - p.TrueGrade);
                r250.Reveal(p, 250); err250 += Math.Abs(r250.EstimatedGrade(p) - p.TrueGrade);
            }
            Assert.Less(err10, err0);
            Assert.Less(err60, err10);
            Assert.Less(err250, err60);
            Assert.Less(err250 / pool.Count, 1.5);
            Assert.Greater(err0 / pool.Count, 3.0);
            Assert.Less(r250.Get(pool[0]).Sd, r10.Get(pool[0]).Sd);
            Assert.AreEqual(ScoutingReport.SdForPoints(0), ScoutingReport.BaseSd, 1e-4);
            Assert.Less(ScoutingReport.SdForPoints(100), ScoutingReport.SdForPoints(10));
        }

        [Test]
        public void ScoutedDraftBoardRankCorrelationImprovesWithPoints()
        {
            ProLeague lg;
            var cls = MakeClass(6, out lg);
            var pool = cls.Take(150).ToList();
            double c0 = RankCorrelation(pool, 0), c20 = RankCorrelation(pool, 20), c120 = RankCorrelation(pool, 120);
            Assert.Greater(c20, c0);
            Assert.Greater(c120, c20);
            Assert.Greater(c120, 0.97);
        }

        private static double RankCorrelation(List<Prospect> pool, int points)
        {
            var rep = new ScoutingReport(2, 2026, 99, 1000000);
            foreach (Prospect p in pool) if (points > 0) rep.Reveal(p, points);
            var est = pool.Select(p => rep.EstimatedGrade(p)).ToArray();
            var tru = pool.Select(p => (double)p.TrueGrade).ToArray();
            double me = est.Average(), mt = tru.Average(), sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < est.Length; i++) { sxy += (est[i] - me) * (tru[i] - mt); sxx += (est[i] - me) * (est[i] - me); syy += (tru[i] - mt) * (tru[i] - mt); }
            return sxy / Math.Sqrt(sxx * syy);
        }

        [Test]
        public void ScoutingBudgetIsEnforcedAndDeterministic()
        {
            ProLeague lg;
            var cls = MakeClass(7, out lg);
            var rep = new ScoutingReport(3, 2026, 5, 30);
            rep.Reveal(cls[0], 25);
            Assert.AreEqual(25, rep.PointsSpent);
            rep.Reveal(cls[1], 25);
            Assert.AreEqual(30, rep.PointsSpent);
            Assert.AreEqual(0, rep.PointsRemaining);
            rep.Reveal(cls[2], 10);
            Assert.AreEqual(30, rep.PointsSpent);
            var rep2 = new ScoutingReport(3, 2026, 5, 30);
            rep2.Reveal(cls[0], 25);
            Assert.AreEqual(rep.Get(cls[0]).Estimated.Speed, rep2.Get(cls[0]).Estimated.Speed);
            Assert.AreEqual(rep.EstimatedGrade(cls[0]), rep2.EstimatedGrade(cls[0]), 1e-4);
            var rep3 = new ScoutingReport(4, 2026, 6, 30);
            Assert.AreNotEqual(rep3.EstimatedGrade(cls[0]), rep.EstimatedGrade(cls[0]));
        }

        [Test]
        public void BigBoardSortsByEstimatedGrade()
        {
            ProLeague lg;
            var cls = MakeClass(9, out lg);
            var rep = new ScoutingReport(5, 2026, 77, 100000);
            foreach (Prospect p in cls.Take(60)) rep.Reveal(p, 40);
            var board = rep.BigBoard(cls.Take(60).ToList());
            for (int i = 1; i < board.Count; i++) Assert.GreaterOrEqual(rep.EstimatedGrade(board[i - 1]), rep.EstimatedGrade(board[i]));
        }

        [Test]
        public void ProgressionGrowsYouthAndDeclinesVeterans()
        {
            var rng = new Rng(3);
            var lg = LeagueFactory.CreatePro(31);
            double youngDelta = 0, oldDelta = 0; int ny = 0, no = 0;
            foreach (Player src in lg.AllRosterPlayers().ToList())
            {
                var clone = new Player
                {
                    Bio = new PlayerBio { Id = src.Id, FirstName = "A", LastName = "B", Age = src.Age },
                    Position = src.Position, Archetype = src.Archetype, Ratings = src.Ratings, Potential = src.Potential, Dev = src.Dev
                };
                int before = clone.Overall;
                if (src.Age <= 23 && src.Potential - before >= 5) { Progression.Advance(clone, rng); youngDelta += clone.Overall - before; ny++; }
                else if (src.Age >= 32) { Progression.Advance(clone, rng); oldDelta += clone.Overall - before; no++; }
            }
            Assert.Greater(ny, 20); Assert.Greater(no, 20);
            Assert.Greater(youngDelta / ny, 0.8);
            Assert.Less(oldDelta / no, -0.8);
        }

        [Test]
        public void DevelopmentTraitsSpeedGrowthAndPotentialCapsIt()
        {
            var rng = new Rng(8);
            double normal = 0, superstar = 0;
            for (int i = 0; i < 200; i++)
            {
                normal += Grow(DevTrait.Normal, 70, 99, rng);
                superstar += Grow(DevTrait.Superstar, 70, 99, rng);
            }
            Assert.Greater(superstar / 200.0, normal / 200.0 + 0.8);
            // a player already at his ceiling cannot grow far past it
            double atCap = 0;
            for (int i = 0; i < 200; i++) atCap += Grow(DevTrait.Superstar, 80, 80, rng);
            Assert.LessOrEqual(atCap / 200.0, 1.5);
            Assert.Greater(Progression.ExpectedChange(Position.RB, 22, DevTrait.Normal), 0);
            Assert.Less(Progression.ExpectedChange(Position.RB, 33, DevTrait.Normal), -1);
            Assert.Less(Progression.ExpectedChange(Position.QB, 33, DevTrait.Normal), 0.01);
            Assert.Less(Progression.ExpectedChange(Position.RB, 31, DevTrait.Normal), Progression.ExpectedChange(Position.QB, 31, DevTrait.Normal));
        }

        private static double Grow(DevTrait dev, int startOvr, int potential, Rng rng)
        {
            var p = new Player
            {
                Bio = new PlayerBio { Id = 1, FirstName = "T", LastName = "T", Age = 21 }, Position = Position.WR, Dev = dev, Potential = potential,
                Ratings = PlayerFactory.BuildRatings(Position.WR, Archetype.WrSlot, startOvr, rng)
            };
            int before = p.Overall;
            Progression.Advance(p, rng);
            return p.Overall - before;
        }

        [Test]
        public void RetirementOddsRiseWithAgeAndFallWithTalent()
        {
            var rng = new Rng(1);
            float Prob(Position pos, int age, int ovr, bool unsigned)
            {
                var p = new Player { Bio = new PlayerBio { Age = age }, Position = pos, Ratings = PlayerFactory.BuildRatings(pos, PlayerFactory.RandomArchetype(pos, rng), ovr, rng) };
                return Retirement.Probability(p, unsigned);
            }
            Assert.Less(Prob(Position.WR, 26, 75, false), 0.02f);
            Assert.Less(Prob(Position.WR, 31, 75, false), Prob(Position.WR, 35, 75, false));
            Assert.Greater(Prob(Position.WR, 35, 75, false), 0.15f);
            Assert.Greater(Prob(Position.WR, 35, 52, false), Prob(Position.WR, 35, 85, false));
            Assert.Less(Prob(Position.QB, 35, 80, false), Prob(Position.RB, 35, 80, false), "QBs last longer than backs");
            Assert.Greater(Prob(Position.WR, 33, 70, true), Prob(Position.WR, 33, 70, false));
            Assert.AreEqual(1f, Prob(Position.P, 42, 80, false), 1e-4);
        }
    }
}
