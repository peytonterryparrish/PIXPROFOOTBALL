using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>The data-driven pro league built from Data/Pro/teams.csv.</summary>
    public class ProDataTests
    {
        private static readonly string[] RealNflNicknames =
        {
            "Bills", "Dolphins", "Patriots", "Jets", "Ravens", "Bengals", "Browns", "Steelers", "Texans", "Colts", "Jaguars", "Titans",
            "Broncos", "Chiefs", "Raiders", "Chargers", "Cowboys", "Giants", "Eagles", "Commanders", "Bears", "Lions", "Packers", "Vikings",
            "Falcons", "Panthers", "Saints", "Buccaneers", "Cardinals", "Rams", "49ers", "Seahawks"
        };

        private static List<ProTeamDef> _defs;
        private static List<ProTeamDef> Defs { get { return _defs ?? (_defs = LeagueTestUtil.LoadProTeams()); } }

        private static ProLeague _lg;
        private static ProLeague Lg { get { return _lg ?? (_lg = LeagueFactory.CreatePro(2024, Defs, LeagueTestUtil.LoadSchools())); } }

        private static ProLeague _played;
        private static ProLeague Played
        {
            get
            {
                if (_played == null)
                {
                    _played = LeagueFactory.CreatePro(2025, Defs, LeagueTestUtil.LoadSchools());
                    _played.Season.SimToEnd();
                }
                return _played;
            }
        }

        [Test]
        public void CsvParsesThirtyTwoOfficialTeams()
        {
            Assert.AreEqual(32, Defs.Count);
            foreach (ProTeamDef d in Defs)
            {
                Assert.IsNotEmpty(d.Id); Assert.IsNotEmpty(d.City); Assert.IsNotEmpty(d.Nickname); Assert.IsNotEmpty(d.Logo);
                Assert.AreEqual(d.City + " " + d.Nickname, d.Name);
                Assert.IsTrue(Regex.IsMatch(d.Primary, "^#[0-9A-F]{6}$"), d.Name + " " + d.Primary);
                Assert.IsTrue(Regex.IsMatch(d.Secondary, "^#[0-9A-F]{6}$"), d.Name + " " + d.Secondary);
                Assert.IsTrue(Regex.IsMatch(d.Tertiary, "^#[0-9A-F]{6}$"), d.Name + " " + d.Tertiary);
            }
            Assert.AreEqual("Buffalo Ironhorns", Defs[0].Name);
            Assert.AreEqual("#0C3CB4", Defs[0].Primary);
            Assert.AreEqual("ProV6/buffalo_ironhorns", Defs[0].Logo);
            Assert.AreEqual(32, Defs.Select(d => d.Id).Distinct().Count());
        }

        [Test]
        public void ParserHandlesBomQuotesAndBadColors()
        {
            var t = ProData.ParseTeams("﻿\"id\",\"city\",\"nickname\",\"name\",\"conference\",\"division\",\"primary\",\"secondary\",\"tertiary\",\"logo\"\r\n" +
                                       "\"a_b\",\"A\",\"Bees\",\"A Bees\",\"X Conf\",\"East\",\"#abcdef\",\"zzz\",\"\",\"L/a\"\r\n");
            Assert.AreEqual(1, t.Count);
            Assert.AreEqual("#ABCDEF", t[0].Primary);
            Assert.AreEqual("#FFFFFF", t[0].Secondary);
            Assert.AreEqual("#FFFFFF", t[0].Tertiary);
            Assert.AreEqual(0, ProData.ParseTeams("").Count);
        }

        [Test]
        public void LeagueHasTwoConferencesFourDivisionsOfFour()
        {
            Assert.AreEqual(32, Lg.Teams.Count);
            Assert.AreEqual(2, Lg.Conferences.Count);
            Assert.AreEqual("Liberty Conference", Lg.Conferences[0].Name);
            Assert.AreEqual("Frontier Conference", Lg.Conferences[1].Name);
            int divs = 0;
            foreach (Conference c in Lg.Conferences)
            {
                Assert.AreEqual(4, c.Divisions.Count);
                Assert.AreEqual(16, c.TeamIds.Count);
                string[] names = { "East", "North", "South", "West" };
                for (int i = 0; i < 4; i++)
                {
                    Division d = c.Divisions[i];
                    divs++;
                    StringAssert.EndsWith(names[i], d.Name);
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
        public void TeamsMatchTheCsvIncludingConferenceDivisionColorsAndLogo()
        {
            for (int i = 0; i < 32; i++)
            {
                Team t = Lg.Teams[i];
                ProTeamDef d = Defs[i]; // the file is already ordered conference > division
                Assert.AreEqual(d.City, t.City);
                Assert.AreEqual(d.Nickname, t.Nickname);
                Assert.AreEqual(d.Name, t.FullName);
                Assert.AreEqual(d.Id, t.Key);
                Assert.AreEqual(d.Logo, t.Logo);
                Assert.AreEqual(d.Primary, t.PrimaryColor);
                Assert.AreEqual(d.Secondary, t.SecondaryColor);
                Assert.AreEqual(d.Tertiary, t.TertiaryColor);
                Assert.AreEqual(Lg.Conferences[t.ConferenceId].Name, d.Conference);
                StringAssert.EndsWith(d.Division, Lg.GetDivision(t.DivisionId).Name);
                Assert.AreEqual(53, t.Roster.Count, t.FullName);
            }
        }

        [Test]
        public void AbbreviationsAreUniqueShortAndMatchTheNamedExamples()
        {
            var seen = new HashSet<string>();
            foreach (Team t in Lg.Teams)
            {
                Assert.IsTrue(seen.Add(t.Abbreviation), "duplicate " + t.Abbreviation);
                Assert.That(t.Abbreviation.Length, Is.InRange(2, 3), t.FullName);
                Assert.IsTrue(Regex.IsMatch(t.Abbreviation, "^[A-Z]+$"), t.Abbreviation);
            }
            Assert.AreEqual("BUF", Lg.Teams.First(t => t.Key == "buffalo_ironhorns").Abbreviation);
            Assert.AreEqual("MIA", Lg.Teams.First(t => t.Key == "miami_reefrunners").Abbreviation);
            Assert.AreEqual("NE", Lg.Teams.First(t => t.Key == "new_england_sentinels").Abbreviation);
            Assert.AreEqual("NYS", Lg.Teams.First(t => t.Key == "new_york_skybolts").Abbreviation);
            Assert.AreEqual("NYC", Lg.Teams.First(t => t.Key == "new_york_colossi").Abbreviation);
            Assert.AreEqual("LAT", Lg.Teams.First(t => t.Key == "los_angeles_thunderhawks").Abbreviation);
            Assert.AreEqual("LAG", Lg.Teams.First(t => t.Key == "los_angeles_goldhorns").Abbreviation);
        }

        [Test]
        public void DerivedAbbreviationsStayUniqueForUnknownTeams()
        {
            var teams = new List<ProTeamDef>();
            for (int i = 0; i < 40; i++) teams.Add(new ProTeamDef { Id = "x" + i, City = i % 2 == 0 ? "Springfield" : "Spring Valley", Nickname = "Team" + (char)('A' + i % 26) + i });
            string[] a = ProData.DeriveAbbreviations(teams);
            Assert.AreEqual(40, a.Distinct().Count());
            Assert.IsTrue(a.All(x => x.Length >= 2 && x.Length <= 3));
        }

        [Test]
        public void NamesAvoidRealNflNicknamesAndConferenceTerms()
        {
            foreach (Team t in Lg.Teams)
            {
                // whole-word match: compounds such as "Frostgiants" / "Bluechargers" are new words, not the real nicknames
                var words = t.FullName.ToLowerInvariant().Split(' ');
                foreach (string real in RealNflNicknames)
                {
                    string r = real.ToLowerInvariant();
                    Assert.AreNotEqual(r, t.Nickname.ToLowerInvariant(), t.FullName);
                    Assert.IsFalse(words.Contains(r), t.FullName + " contains " + real);
                }
                var fields = new List<string> { t.City, t.Nickname, t.FullName, t.Abbreviation, t.Key, t.Logo };
                fields.Add(Lg.Conferences[t.ConferenceId].Name);
                fields.Add(Lg.Conferences[t.ConferenceId].Abbreviation);
                fields.Add(Lg.GetDivision(t.DivisionId).Name);
                foreach (string f in fields)
                    foreach (string banned in new[] { "AFC", "NFC", "NFL" })
                        Assert.IsFalse(f.IndexOf(banned, StringComparison.OrdinalIgnoreCase) >= 0, f + " contains " + banned);
            }
            foreach (Conference c in Lg.Conferences)
                foreach (string banned in new[] { "AFC", "NFC", "NFL" })
                    Assert.IsFalse(c.Name.IndexOf(banned, StringComparison.OrdinalIgnoreCase) >= 0, c.Name);
        }

        [Test]
        public void ProPlayersAttendedSchoolsFromTheSchoolList()
        {
            var places = new HashSet<string>(LeagueTestUtil.LoadSchools().Select(s => s.Place));
            Assert.GreaterOrEqual(places.Count, 100);
            foreach (Team t in Lg.Teams)
                foreach (Player p in t.Roster)
                    Assert.IsTrue(places.Contains(p.Bio.College), t.FullName + " " + p.Name + " college '" + p.Bio.College + "'");
            foreach (Player p in Lg.FreeAgents) Assert.IsTrue(places.Contains(p.Bio.College), "FA college '" + p.Bio.College + "'");
        }

        [Test]
        public void WithoutASchoolListCollegesAreGenerated()
        {
            ProLeague lg = LeagueFactory.CreatePro(9, Defs);
            Assert.AreEqual(32, lg.Teams.Count);
            Assert.IsTrue(lg.Teams.All(t => t.Roster.All(p => !string.IsNullOrEmpty(p.Bio.College))));
        }

        [Test]
        public void OldGeneratedLeagueStillWorks()
        {
            ProLeague lg = LeagueFactory.CreatePro(77);
            Assert.AreEqual(32, lg.Teams.Count);
            Assert.IsTrue(lg.Teams.All(t => t.Key == "" && t.Abbreviation.Length == 3));
        }

        [Test]
        public void ScheduleSeasonPlayoffsAndPixBowlWork()
        {
            Season s = Lg.Season;
            Assert.AreEqual(272, s.Games.Count);
            for (int t = 0; t < 32; t++) Assert.AreEqual(17, s.RegularGameCount(t), "team " + t);

            Season p = Played.Season;
            Assert.IsTrue(p.IsComplete);
            Assert.AreEqual("PIX Bowl", p.ChampionshipName);
            var finals = p.Games.Where(g => g.Kind == GameKind.Final).ToList();
            Assert.AreEqual(1, finals.Count);
            Assert.AreEqual(finals[0].WinnerId, p.ChampionTeamId);
            Assert.AreNotEqual(Played.Teams[p.ChampionTeamId].ConferenceId, Played.Teams[p.RunnerUpTeamId].ConferenceId);
            Assert.AreEqual(14, p.Seeds.Count);
        }

        [Test]
        public void SameSeedIsDeterministicAndDifferentSeedDiffers()
        {
            var schools = LeagueTestUtil.LoadSchools();
            ProLeague a = LeagueFactory.CreatePro(123, Defs, schools);
            ProLeague b = LeagueFactory.CreatePro(123, Defs, schools);
            ProLeague c = LeagueFactory.CreatePro(124, Defs, schools);
            Assert.AreEqual(LeagueTestUtil.Fingerprint(a), LeagueTestUtil.Fingerprint(b));
            Assert.AreEqual(LeagueTestUtil.Fingerprint(a.Season), LeagueTestUtil.Fingerprint(b.Season));
            Assert.AreNotEqual(LeagueTestUtil.Fingerprint(a), LeagueTestUtil.Fingerprint(c));
        }

        [Test]
        public void ShuffledFileOrderStillProducesTheSameLeagueLayout()
        {
            var shuffled = new List<ProTeamDef>(Defs);
            shuffled.Reverse();
            ProLeague lg = LeagueFactory.CreatePro(5, shuffled);
            Assert.AreEqual(32, lg.Teams.Select(t => t.FullName).Distinct().Count());
            foreach (Team t in lg.Teams)
            {
                ProTeamDef d = Defs.First(x => x.Name == t.FullName);
                Assert.AreEqual(d.Conference, lg.Conferences[t.ConferenceId].Name);
                StringAssert.EndsWith(d.Division, lg.GetDivision(t.DivisionId).Name);
            }
        }

        [Test]
        public void MalformedTeamListsAreRejected()
        {
            Assert.Throws<ArgumentException>(() => LeagueFactory.CreatePro(1, Defs.Take(31).ToList()));
            var bad = new List<ProTeamDef>(Defs);
            bad[0] = new ProTeamDef { Id = "x", City = "X", Nickname = "Xs", Name = "X Xs", Conference = "Frontier Conference", Division = "East" };
            Assert.Throws<ArgumentException>(() => LeagueFactory.CreatePro(1, bad));
        }

        [Test]
        public void LeagueSaveCanUseTheOfficialTeams()
        {
            LeagueSave s = LeagueSave.Create(3, LeagueTestUtil.LoadSchools(), null, Defs);
            Assert.AreEqual("Buffalo Ironhorns", s.Pro.Teams[0].FullName);
        }
    }
}
