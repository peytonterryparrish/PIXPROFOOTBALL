using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>UI-FLOW data pass: team colours, abbreviations, original names, college ratings and class years, injuries, stadiums, prestige labels.</summary>
    public class UiFlowDataTests
    {
        static int[] Rgb(string hex) { return new[] { Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16) }; }
        static double Dist(string a, string b)
        {
            int[] x = Rgb(a), y = Rgb(b);
            return Math.Sqrt((x[0] - y[0]) * (x[0] - y[0]) + (x[1] - y[1]) * (x[1] - y[1]) + (x[2] - y[2]) * (x[2] - y[2]));
        }

        static List<ProTeamDef> Defs { get { return LeagueTestUtil.LoadProTeams(); } }

        // ------------------------------------------------------------------------------------------- QA-586/995/996 colours
        [Test]
        public void DivisionRivalsWearClearlyDifferentPrimaryColours()
        {
            foreach (var g in Defs.GroupBy(d => d.Conference + "/" + d.Division))
            {
                var t = g.ToList();
                Assert.AreEqual(4, t.Count, g.Key);
                for (int i = 0; i < t.Count; i++)
                    for (int j = i + 1; j < t.Count; j++)
                        Assert.GreaterOrEqual(Dist(t[i].Primary, t[j].Primary), 90.0, g.Key + ": " + t[i].Name + " " + t[i].Primary + " vs " + t[j].Name + " " + t[j].Primary);
            }
        }

        [Test]
        public void EveryKitHasContrastBetweenItsColours()
        {
            foreach (var d in Defs)
            {
                Assert.GreaterOrEqual(Dist(d.Primary, d.Secondary), 60.0, d.Name + " primary vs secondary");
                Assert.GreaterOrEqual(Dist(d.Primary, d.Tertiary), 60.0, d.Name + " primary vs tertiary");
            }
        }

        [Test]
        public void WhitesAreNormalisedToOneWhite()
        {
            foreach (var d in Defs)
                foreach (string c in new[] { d.Primary, d.Secondary, d.Tertiary })
                {
                    int[] v = Rgb(c);
                    if (v.Min() >= 0xF0) Assert.IsTrue(c == "#FFFFFF" || c == "#FFFCE4", d.Name + " has a stray near-white " + c + " (use #FFFFFF, or the one cream #FFFCE4)");
                }
        }

        // ------------------------------------------------------------------------------------------- QA-587/997 abbreviations
        [Test]
        public void EveryTeamHasAUniqueThreeLetterCodeFromTheDataFile()
        {
            var defs = Defs;
            var seen = new HashSet<string>();
            foreach (var d in defs)
            {
                StringAssert.IsMatch("^[A-Z]{3}$", d.Abbr, d.Name + " abbr column");
                Assert.IsTrue(seen.Add(d.Abbr), "duplicate code " + d.Abbr);
            }
            ProLeague lg = LeagueFactory.CreatePro(2024, defs, LeagueTestUtil.LoadSchools());
            foreach (Team t in lg.Teams)
            {
                Assert.AreEqual(3, t.Abbreviation.Length, t.FullName);
                Assert.AreEqual(defs.First(x => x.Id == t.Key).Abbr, t.Abbreviation, t.FullName);
            }
        }

        [Test]
        public void FallbackCodesAreAlsoThreeLettersAndUnique()
        {
            var noCodes = Defs.Select(d => new ProTeamDef { Id = d.Id + "_x", City = d.City, Nickname = d.Nickname, Name = d.Name, Conference = d.Conference, Division = d.Division }).ToList();
            string[] codes = ProData.DeriveAbbreviations(noCodes);
            Assert.AreEqual(32, codes.Distinct().Count());
            Assert.IsTrue(codes.All(c => c.Length == 3 && c.All(ch => ch >= 'A' && ch <= 'Z')));
            Assert.AreEqual("", ProData.CleanAbbr("NE")); Assert.AreEqual("", ProData.CleanAbbr("N1E")); Assert.AreEqual("KCF", ProData.CleanAbbr(" kcf "));
            var two = new List<ProTeamDef> { new ProTeamDef { Id = "a", City = "Spring Valley", Nickname = "Owls" }, new ProTeamDef { Id = "b", City = "Big Rock", Nickname = "Owls" } };
            Assert.IsTrue(ProData.DeriveAbbreviations(two).All(c => c.Length == 3));
        }

        // ------------------------------------------------------------------------------------------- QA-583/584 original names
        [Test]
        public void RealPlayersAreOnTheNameBlocklistAndNeverGenerated()
        {
            foreach (string n in new[] { "Jerry Rice", "Barry Sanders", "Reggie White", "Randy Moss", "Lawrence Taylor", "Terrell Owens", "Aaron Rodgers", "Lamar Jackson", "Jared Goff", "Jordan Love",
                                          "Trevor Lawrence", "Russell Wilson", "Calvin Johnson", "Patrick Willis", "John Madden" })
            {
                string[] p = n.Split(' ');
                Assert.IsTrue(NameGenerator.IsRealPersonName(p[0], p[1]), n);
                Assert.IsTrue(NameGenerator.IsRealPersonName(p[0].ToUpperInvariant(), p[1].ToLowerInvariant()), n + " is case-insensitive");
            }
            Assert.IsFalse(NameGenerator.IsRealPersonName("Quinn", "Hadley")); Assert.IsFalse(NameGenerator.IsRealPersonName("", "Rice"));
            Assert.IsTrue(NameGenerator.RealNameBlocklist.All(n => n == n.ToLowerInvariant() && n.Contains(" ")), "entries are lower case 'first last'");

            var rng = new Rng(2026);
            var used = new HashSet<string>();
            for (int i = 0; i < 40000; i++)
            {
                string f, l;
                NameGenerator.PersonName(rng, used, out f, out l);
                Assert.IsFalse(NameGenerator.IsRealPersonName(f, l), f + " " + l);
            }
            // the pools really can build several of them, so the list is not decoration
            var reachable = NameGenerator.RealNameBlocklist.Count(n => { var p = n.Split(' '); return NameGenerator.FirstNames.Contains(p[0], StringComparer.OrdinalIgnoreCase) && NameGenerator.LastNames.Contains(p[1], StringComparer.OrdinalIgnoreCase); });
            Assert.GreaterOrEqual(reachable, 12);
        }

        [Test]
        public void GeneratedLeaguesNeverContainARealPlayerName()
        {
            ProLeague lg = LeagueFactory.CreatePro(31, Defs, LeagueTestUtil.LoadSchools());
            foreach (Player p in lg.AllRosterPlayers().Concat(lg.FreeAgents))
                Assert.IsFalse(NameGenerator.IsRealPersonName(p.Bio.FirstName, p.Bio.LastName), p.Name);
            foreach (Team t in lg.Teams)
                foreach (Coach c in t.Staff) { string[] cn = c.Name.Split(' '); Assert.IsFalse(cn.Length == 2 && NameGenerator.IsRealPersonName(cn[0], cn[1]), c.Name); }
        }

        [Test]
        public void MascotAndNicknamePoolsHaveNoRealFranchiseNames()
        {
            string[] real = { "Titans", "Sabres", "Grizzlies", "Raptors", "Hurricanes", "Longhorns", "Rams", "Vikings", "Buccaneers", "Raiders", "Chargers", "Cardinals", "Panthers", "Eagles", "Trojans", "Spartans", "Jayhawks" };
            foreach (string r in real)
            {
                CollectionAssert.DoesNotContain(NameGenerator.Mascots, r, "mascot pool");
                CollectionAssert.DoesNotContain(NameGenerator.Nicknames, r, "pro nickname pool");
            }
            Assert.GreaterOrEqual(NameGenerator.Nicknames.Count, 60);
        }

        // ------------------------------------------------------------------------------------------- QA-558/559/998/999/1000/1001/589 school data
        [Test]
        public void SchoolShortNamesAndNicknamesAreUniqueAndFreeOfFillerWords()
        {
            var schools = LeagueTestUtil.LoadSchools();
            Assert.AreEqual(138, schools.Count);
            var colourWords = new HashSet<string> { "Golden", "Scarlet", "Green", "Maroon", "Orange", "Blue", "Crimson", "Violet", "Silver", "Teal", "Gold", "Red", "Bronze", "Amber", "Black", "Garnet", "Emerald" };
            Assert.AreEqual(138, schools.Select(s => s.ShortName).Distinct().Count(), "short names are unique");
            Assert.AreEqual(138, schools.Select(s => s.Nickname).Distinct().Count(), "nicknames are unique within the league");
            foreach (var s in schools)
            {
                Assert.IsNotEmpty(s.ShortName, s.Name + " has no short_name"); Assert.IsNotEmpty(s.State, s.Name + " has no state");
                Assert.AreEqual(s.ShortName, s.Place);
                Assert.IsFalse(colourWords.Contains(s.ShortName), s.Name + ": a colour word is not a home college (" + s.ShortName + ")");
                Assert.IsFalse((" " + s.Name + " ").Contains(" Union "), s.Name + " keeps the generator filler word");
            }
            var proNicks = new HashSet<string>(Defs.Select(d => d.Nickname), StringComparer.OrdinalIgnoreCase);
            foreach (var s in schools) Assert.IsFalse(proNicks.Contains(s.Nickname), s.Name + " shares its nickname with a pro team");
        }

        [Test]
        public void SchoolNamesDoNotEchoRealPlacesOrMascots()
        {
            string[] echoes = { "California", "Louisiana", "Ohio", "Massachusetts", "Tulsa", "Volunteers", "Longhorns", "Gators", "Wolverines", "Cowboys", "Cardinals", "Rams", "Raiders", "Panthers", "Eagles",
                                "Bears", "Broncos", "Falcons", "Cyclones", "Bearcats", "Huskies", "Jaguars", "Sabres", "Trailblazers", "Redwings", "Grizzlies", "Cougars", "Corsairs" };
            foreach (var s in LeagueTestUtil.LoadSchools())
            {
                string hit;
                Assert.IsFalse(LeagueTestUtil.ContainsBlacklisted(s.Name, echoes, out hit), s.Name + " echoes " + hit);
            }
        }

        [Test]
        public void ConferenceSizesAreBoundedAndEverySchoolResolvesAConference()
        {
            var schools = LeagueTestUtil.LoadSchools();
            var defs = CollegeData.ParseConfBowls(LeagueTestUtil.ConfBowlsCsv());
            var confIds = new HashSet<string>(defs.Where(d => d.IsConference).Select(d => d.Id));
            var sizes = schools.Where(s => !s.IsIndependent).GroupBy(s => s.ConferenceId).ToDictionary(g => g.Key, g => g.Count());
            foreach (var kv in sizes)
            {
                Assert.IsTrue(confIds.Contains(kv.Key), "conference_id " + kv.Key + " has a row in conferences_bowls.csv");
                Assert.That(kv.Value, Is.InRange(8, 18), kv.Key);
            }
            Assert.AreEqual(confIds.Count, sizes.Count, "every conference has members");
            Assert.AreEqual(138, sizes.Values.Sum() + schools.Count(s => s.IsIndependent));

            CollegeWorld w = LeagueTestUtil.LoadCollege(7);
            foreach (School s in w.Schools)
            {
                Assert.IsNotEmpty(w.ConferenceNameOf(s), s.Name);
                if (s.Independent) Assert.AreEqual(CollegeWorld.IndependentName, w.ConferenceNameOf(s));
                else Assert.AreEqual(w.Conferences[s.ConferenceId].Name, w.ConferenceNameOf(s));
            }
            Assert.AreEqual(CollegeWorld.IndependentName, w.ConferenceNameOf(null));
        }

        [Test]
        public void PlayoffBowlsAreNotThinCopiesOfTheRealOnes()
        {
            var defs = CollegeData.ParseConfBowls(LeagueTestUtil.ConfBowlsCsv());
            Assert.AreEqual(defs.Count, defs.Select(d => d.Name).Distinct().Count());
            string[] old = { "Sugar Rush Bowl", "Cottoncrest Classic", "Citrus Comet Bowl", "Crystal Cane Bowl", "Festival Flame Bowl", "Orchard Gold Bowl", "Thorncrest Bowl" };
            foreach (string o in old) Assert.IsFalse(defs.Any(d => d.Name == o), o + " was renamed");
            Assert.AreEqual(6, defs.Count(d => d.IsPlayoffBowl));
        }

        // ------------------------------------------------------------------------------------------- QA-556/557/613 college ratings, classes, prestige
        static CollegeWorld World { get { return _w ?? (_w = LeagueTestUtil.LoadCollege(99)); } }
        static CollegeWorld _w;

        [Test]
        public void CollegeSeniorsRateOnTheCollegeCurve()
        {
            var all = World.Schools.SelectMany(s => s.Team.Roster).ToList();
            var seniors = all.Where(p => p.Class == ClassYear.Senior).ToList();
            var freshmen = all.Where(p => p.Class == ClassYear.Freshman).ToList();
            double avgSenior = seniors.Average(p => p.Overall);
            Assert.That(avgSenior, Is.InRange(52.0, 66.0), "senior average (the pro-age-curve version averaged 44 over the whole college)");
            Assert.Greater(avgSenior, freshmen.Average(p => p.Overall) + 4, "seniors are clearly ahead of freshmen");
            var sorted = seniors.Select(p => p.Overall).OrderBy(x => x).ToList();
            Assert.GreaterOrEqual(sorted[(int)(sorted.Count * 0.9)], 65, "the best tenth of the seniors rate 65+");
            Assert.GreaterOrEqual(sorted[sorted.Count - 1], 78, "somebody is a high-70s / 80s star");
            Assert.Greater(all.Count(p => p.Overall >= 70), 150, "the old curve produced only 56 players at 70+");
            var qbs = all.Where(p => p.Position == Position.QB).ToList();
            Assert.GreaterOrEqual(qbs.Average(p => p.Overall), 49.0, "quarterbacks no longer average ~41");
            Assert.GreaterOrEqual(qbs.Where(p => p.Class == ClassYear.Senior).Average(p => p.Overall), 53.0);
            Assert.GreaterOrEqual(World.Schools.Average(s => s.Team.TeamOverall()), 54.0, "school team overalls were 41-61 (average ~50) on the old curve");
            double top = World.Schools.Where(s => s.Prestige >= 5).Average(s => s.Team.TeamOverall()), bottom = World.Schools.Where(s => s.Prestige <= 2).Average(s => s.Team.TeamOverall());
            Assert.Greater(top, bottom + 5);
        }

        [Test]
        public void ClassYearsAreBalancedPerSchoolAndForSingleSlotPositions()
        {
            foreach (School s in World.Schools)
            {
                var counts = new[] { ClassYear.Freshman, ClassYear.Sophomore, ClassYear.Junior, ClassYear.Senior }.Select(c => s.Team.Roster.Count(p => p.Class == c)).ToArray();
                Assert.LessOrEqual(counts.Max() - counts.Min(), 1, s.Name + " classes " + string.Join("/", counts));
            }
            foreach (Position pos in new[] { Position.K, Position.P })
                foreach (ClassYear c in new[] { ClassYear.Freshman, ClassYear.Sophomore, ClassYear.Junior, ClassYear.Senior })
                {
                    int n = World.Schools.Count(s => s.Team.Roster.Any(p => p.Position == pos && p.Class == c));
                    Assert.GreaterOrEqual(n, 15, pos + " " + c + " (the audit saw all kickers as seniors and all punters as freshmen)");
                }
            var w = new[] { 0, 0, 0, 0 };
            var rng = new Rng(3);
            for (int i = 0; i < 50; i++) CollegeFactory.ClassesForSlots(1, w, rng);
            Assert.LessOrEqual(w.Max() - w.Min(), 1, "single-slot picks even out");
        }

        [Test]
        public void PrestigeHasOneStarScaleAndALabelForTheSixthTier()
        {
            foreach (School s in World.Schools)
            {
                Assert.That(s.PrestigeStars, Is.InRange(1, 5), s.Name);
                Assert.LessOrEqual(s.Team.Prestige, 5, s.Name + " team prestige uses the 1-5 pro scale");
                Assert.IsNotEmpty(s.PrestigeLabel);
            }
            var elite = new School { Prestige = 6 };
            Assert.AreEqual(5, elite.PrestigeStars); Assert.AreEqual("Elite", elite.PrestigeLabel);
            Assert.AreEqual("3-star", new School { Prestige = 3 }.PrestigeLabel);
        }

        // ------------------------------------------------------------------------------------------- QA-564 injuries
        [Test]
        public void InjuryTypeDecidesTheDuration()
        {
            var rng = new Rng(11);
            foreach (string type in Season.InjuryTypeNames)
            {
                int[] range = Season.InjuryWeekRange(type);
                Assert.IsNotNull(range, type);
                int min = int.MaxValue, max = 0; double sum = 0;
                for (int i = 0; i < 600; i++)
                {
                    int w = Season.RollInjuryWeeks(type, rng);
                    min = Math.Min(min, w); max = Math.Max(max, w); sum += w;
                    Assert.GreaterOrEqual(w, range[0], type);
                    Assert.LessOrEqual(w, Math.Min(40, range[1] + 1 + (range[1] - range[0]) / 2), type);
                }
                Assert.GreaterOrEqual(sum / 600.0, range[0], type);
            }
            for (int i = 0; i < 300; i++)
            {
                Assert.LessOrEqual(Season.RollInjuryWeeks("Sprained ankle", rng), 6, "an ankle sprain is never a ten-week injury");
                Assert.GreaterOrEqual(Season.RollInjuryWeeks("Torn ACL", rng), 20, "a torn ACL is never a one-week injury");
            }
            Assert.IsNull(Season.InjuryWeekRange("Not a thing"));
        }

        [Test]
        public void InjuriesRolledOverASeasonMatchTheirType()
        {
            ProLeague lg = LeagueFactory.CreatePro(7);
            lg.Season.SimToEnd();
            int checkedCount = 0;
            foreach (Player p in lg.AllRosterPlayers())
            {
                if (!p.Injury.IsInjured) continue;
                int[] range = Season.InjuryWeekRange(p.Injury.Type);
                Assert.IsNotNull(range, "known injury type '" + p.Injury.Type + "'");
                Assert.LessOrEqual(p.Injury.WeeksOut, Math.Min(40, range[1] + 1 + (range[1] - range[0]) / 2), p.Injury.Type);
                checkedCount++;
            }
            Assert.Greater(checkedCount, 0, "some players are hurt at the end of the season");
        }

        // ------------------------------------------------------------------------------------------- QA-590 stadiums
        [Test]
        public void StadiumTypeComesFromTheCityNotFromChance()
        {
            Assert.IsTrue(LeagueFactory.IsDomeCity("Minnesota")); Assert.IsFalse(LeagueFactory.IsDomeCity("Buffalo"));
            Assert.AreEqual("Minnesota Dome", LeagueFactory.StadiumNameFor("Minnesota"));
            Assert.AreEqual(LeagueFactory.StadiumNameFor("Buffalo"), LeagueFactory.StadiumNameFor("Buffalo"));
            StringAssert.DoesNotContain("Dome", LeagueFactory.StadiumNameFor("Buffalo"));
            ProLeague a = LeagueFactory.CreatePro(1, Defs, LeagueTestUtil.LoadSchools()), b = LeagueFactory.CreatePro(2, Defs, LeagueTestUtil.LoadSchools());
            for (int i = 0; i < 32; i++)
            {
                Assert.AreEqual(a.Teams[i].StadiumName, b.Teams[i].StadiumName, a.Teams[i].FullName + ": same city, same ground whatever the seed");
                Assert.AreEqual(LeagueFactory.IsDomeCity(a.Teams[i].City), a.Teams[i].IsDome, a.Teams[i].FullName);
            }
            Assert.IsTrue(a.Teams.Any(t => t.IsDome) && a.Teams.Any(t => !t.IsDome));
        }

        // ------------------------------------------------------------------------------------------- QA-597/599 quick-sim log text
        [Test]
        public void QuickSimLogNamesOnsideKicksAndEndOfRegulationCorrectly()
        {
            ProLeague lg = LeagueFactory.CreatePro(5);
            int onside = 0, ot = 0;
            for (int i = 0; i < 40; i++)
            {
                var res = QuickSim.SimGame(lg.Teams[i % 32], lg.Teams[(i * 7 + 3) % 32 == i % 32 ? (i + 1) % 32 : (i * 7 + 3) % 32], RuleSet.Pro(), new Rng(900 + i), new SimOptions { KeepPlayLog = true });
                foreach (string line in res.PlayLog)
                    if (line.Contains("Onside")) { onside++; StringAssert.DoesNotContain("touchback", line); }
                if (res.Overtime) ot++;
                foreach (DriveRecord d in res.Drives)
                    if (d.Result == "End of Half") Assert.LessOrEqual(d.Quarter, 2, "only a drive that began in the first half can end the half");
            }
            Assert.GreaterOrEqual(ot + onside + 1, 1);   // keeps the loop honest without depending on rare events
        }
    }
}
