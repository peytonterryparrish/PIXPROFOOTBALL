using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Tests
{
    /// <summary>College Network (v0.9.0): the college world inside every career, the weekly lockstep with the pro calendar, rankings, Golden Helm, bowls, playoff, box scores, honors, games for the spectator match.</summary>
    public class CollegeNetTests
    {
        private static CareerSave _done;   // a career at the Awards phase (read-only in the tests that share it)

        private static CareerSave Done { get { return _done ?? (_done = CollegeTestUtil.AtPhase(31, CareerPhase.Awards)); } }

        // ------------------------------------------------------------ creation
        [Test]
        public void EveryCareerHasACollegeWorldBuiltFromTheCsvTexts()
        {
            CareerSave s = CollegeTestUtil.Career(21);
            Assert.IsNotNull(s.College);
            Assert.AreEqual(138, s.College.Schools.Count);
            Assert.AreEqual(41, s.College.Bowls.Count);
            Assert.AreEqual(10, s.College.Conferences.Count);
            Assert.AreEqual(s.Year, s.College.Year, "the college year starts with the pro year");
            Assert.IsTrue(s.Net.Ready);
            for (int t = 0; t < 32; t++)
            {
                int n = CollegeScouting.TeamScouts(s, t).Count;
                Assert.That(n, Is.InRange(3, 5), "scouts of team " + t);
            }
            Assert.AreEqual(4, CollegeScouting.UserScouts(s).Count);
            Assert.AreEqual("Amber Falls Skyhawks", s.College.Schools.First(x => x.Key == "amber_falls_skyhawks").Name, "identities come from the data file");
            CollegeTestUtil.AssertRostersFull(s);
        }

        [Test]
        public void ParsedSchoolListsStillWorkLikeBefore()
        {
            var o = new CareerOptions { Seed = 22, Teams = LeagueTestUtil.LoadProTeams(), Schools = LeagueTestUtil.LoadSchools(), Bowls = CollegeData.ParseConfBowls(LeagueTestUtil.ConfBowlsCsv()) };
            CareerSave s = CareerFlow.NewCareer(o);
            Assert.AreEqual(138, s.College.Schools.Count);
            Assert.IsTrue(s.Net.Ready);
        }

        [Test]
        public void WithoutSchoolFilesTheGeneratedFallbackStillGivesACollegeNetwork()
        {
            var o = new CareerOptions { Seed = 23 };   // no texts, no lists
            CareerSave s = CareerFlow.NewCareer(o);
            Assert.IsNotNull(s.College);
            Assert.AreEqual(138, s.College.Schools.Count);
            Assert.AreEqual(41, s.College.Bowls.Count);
            Assert.AreEqual(10, s.College.Conferences.Count);
            Assert.AreEqual(6, s.College.Bowls.Count(b => b.IsPlayoffBowl));
            var names = new HashSet<string>(s.College.Schools.Select(x => x.Name));
            Assert.AreEqual(138, names.Count, "school names are unique");
            Assert.AreEqual(138, new HashSet<string>(s.College.Schools.Select(x => x.Key)).Count, "school keys are unique");
            foreach (School sc in s.College.Schools)
            {
                string hit;
                Assert.IsFalse(LeagueTestUtil.ContainsBlacklisted(sc.Name, LeagueTestUtil.RealSchoolBlacklist, out hit), sc.Name + " ~ " + hit);
            }
            CollegeTestUtil.AssertRostersFull(s);
            CareerFlow.AdvanceUntil(s, CareerPhase.Scouting);
            Assert.That(s.Pro.DraftClass.Count, Is.InRange(240, 290));
            Assert.IsTrue(s.Pro.DraftClass.Count(p => p.SchoolId >= 0) > 250, "the class comes from the generated college world");
        }

        [Test]
        public void FallbackCanBeSwitchedOffForTheOldBehaviour()
        {
            var o = new CareerOptions { Seed = 24, GenerateCollegeIfMissing = false };
            CareerSave s = CareerFlow.NewCareer(o);
            Assert.IsNull(s.College);
            Assert.AreEqual(0, CollegeViews.Top25(s).Count);
            Assert.IsFalse(CollegeScouting.ScoutPlayer(s, 1, 5).Ok);
            Assert.IsNull(CollegeScoutViews.Card(s, 1));
            CareerFlow.AdvanceUntil(s, CareerPhase.Scouting);
            Assert.Greater(s.Pro.DraftClass.Count, 300, "a league without a college world keeps its synthetic class");
            Assert.IsTrue(s.Pro.DraftClass.All(p => p.SchoolId < 0));
        }

        [Test]
        public void AnOldSaveWithoutACollegeWorldGetsOneAndStaysInStep()
        {
            var o = new CareerOptions { Seed = 25, GenerateCollegeIfMissing = false, Teams = LeagueTestUtil.LoadProTeams() };
            CareerSave s = CareerFlow.NewCareer(o);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            for (int i = 0; i < 5; i++) CollegeTestUtil.Step(s);
            Assert.IsNull(s.College);
            OpResult r = CollegeNet.EnsureWorld(s, LeagueTestUtil.SchoolsCsv(), LeagueTestUtil.ConfBowlsCsv());
            Assert.IsTrue(r.Ok, r.Message);
            Assert.IsNotNull(s.College);
            Assert.AreEqual(s.Season.Week, s.College.Season.Week, "the college week caught up with the pro week");
            Assert.IsTrue(s.Net.Boxes.Any(b => b.Week == 1), "the weeks it caught up have box scores");
            Assert.IsTrue(CollegeNet.EnsureWorld(s, "", "").Ok, "a second call changes nothing");
            CollegeTestUtil.Step(s);
            Assert.AreEqual(s.Season.Week, s.College.Season.Week);
            CareerFlow.AdvanceUntil(s, CareerPhase.Scouting);
            Assert.IsTrue(s.Pro.DraftClass.Count(p => p.SchoolId >= 0) > 250);
            CareerTestUtil.AssertClean(s, false);
        }

        [Test]
        public void AnOldSaveFromTheOffseasonGetsAFinishedCollegeSeason()
        {
            var o = new CareerOptions { Seed = 26, GenerateCollegeIfMissing = false, Teams = LeagueTestUtil.LoadProTeams() };
            CareerSave s = CareerFlow.NewCareer(o);
            CareerFlow.AdvanceUntil(s, CareerPhase.Awards);
            Assert.IsTrue(CollegeNet.EnsureWorld(s, "", "", true).Ok, "no files: the fallback is used");
            Assert.IsTrue(s.College.Season.IsComplete);
            Assert.AreEqual(s.Year, s.College.Year);
            CareerFlow.AdvanceUntil(s, CareerPhase.Scouting);
            Assert.IsTrue(s.Pro.DraftClass.Count(p => p.SchoolId >= 0) > 250);
        }

        // ------------------------------------------------------------ the calendar
        [Test]
        public void TheCollegeWeekRunsInStepWithTheProWeek()
        {
            CareerSave s = CollegeTestUtil.Career(27);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            Assert.AreEqual(1, s.College.Season.Week);
            int weeks = 0;
            while (s.Phase != CareerPhase.Awards && weeks < 30)
            {
                int proWeek = s.Season.Week;
                Assert.AreEqual(Math.Min(proWeek, s.College.Season.IsComplete ? 99 : proWeek), s.College.Season.IsComplete ? proWeek : s.College.Season.Week, "week " + proWeek);
                CollegeTestUtil.Step(s);
                weeks++;
            }
            Assert.IsTrue(s.College.Season.IsComplete, "the college season is finished when the pro season is");
            Assert.LessOrEqual(s.College.Season.LastScheduledWeek, 20);
        }

        [Test]
        public void CollegeWeeksCarryCalendarDates()
        {
            Assert.AreEqual("Aug 30", CollegeCalendar.DateLabel(2026, 1));
            Assert.AreEqual("Sep 6", CollegeCalendar.DateLabel(2026, 2));
            Assert.AreEqual("Dec 6", CollegeCalendar.DateLabel(2026, 15));
            Assert.AreEqual("Jan 10", CollegeCalendar.DateLabel(2026, 20));
            Assert.AreEqual("Sep", CollegeCalendar.MonthLabel(2026, 4));
            CareerSave s = Done;
            Season cs = s.College.Season;
            Assert.AreEqual("Week 3", CollegeCalendar.WeekTitle(cs, 3));
            Assert.AreEqual("Conference championships", CollegeCalendar.WeekTitle(cs, cs.RegularWeeks + 1));
            Assert.AreEqual(cs.ChampionshipName, CollegeCalendar.WeekTitle(cs, cs.LastScheduledWeek));
        }

        [Test]
        public void RankingsAndTheGoldenHelmRaceUpdateEveryWeek()
        {
            CareerSave s = CollegeTestUtil.Career(28);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            int polls0 = s.College.Season.Polls.Count, helm0 = s.College.Season.HelmRace.Count;
            Assert.AreEqual(1, polls0, "the preseason poll exists");
            for (int w = 1; w <= 6; w++)
            {
                CollegeTestUtil.Step(s);
                Assert.AreEqual(polls0 + w, s.College.Season.Polls.Count, "a poll after week " + w);
                Assert.AreEqual(helm0 + w, s.College.Season.HelmRace.Count, "a Golden Helm snapshot after week " + w);
            }
            var top = CollegeViews.Top25(s);
            Assert.AreEqual(25, top.Count);
            for (int i = 0; i < top.Count; i++) Assert.AreEqual(i + 1, top[i].Rank);
            Assert.IsTrue(top.Any(r => r.Movement != 0), "teams move in the poll");
            var helm = CollegeViews.HelmRace(s);
            Assert.AreEqual(10, helm.Count);
            Assert.IsTrue(helm.Zip(helm.Skip(1), (a, b) => a.Score >= b.Score).All(x => x), "sorted by score");
            Assert.AreEqual(CollegeViews.HelmWeeks(s).Count, s.College.Season.HelmRace.Count);
        }

        [Test]
        public void ConferenceTitleGamesThePlayoffAndAll41BowlsArePlayed()
        {
            Season cs = Done.College.Season;
            Assert.IsTrue(cs.IsComplete);
            Assert.AreEqual(10, cs.Games.Count(g => g.Kind == GameKind.ConferenceChampionship && g.Played), "one title game per conference");
            Assert.AreEqual(12, cs.Seeds.Count, "12-team field");
            Assert.AreEqual(new[] { 4, 4, 2 }, new[] { 1, 2, 3 }.Select(r => cs.Games.Count(g => g.Kind == GameKind.PlayoffRound && g.Round == r && g.Played)).ToArray());
            Assert.AreEqual(1, cs.Games.Count(g => g.Kind == GameKind.Final && g.Played));
            Assert.AreEqual(35, cs.Games.Count(g => g.Kind == GameKind.Bowl && g.Played), "the 35 regular bowls");
            var bowlNames = new HashSet<string>(cs.Games.Where(g => g.Kind == GameKind.Bowl).Select(g => g.Label));
            foreach (ConfBowlDef b in Done.College.Bowls.Where(b => b.IsBowl && !b.IsPlayoffBowl)) Assert.IsTrue(bowlNames.Contains(b.Name), b.Name + " was played");
            foreach (ConfBowlDef b in Done.College.Bowls.Where(b => b.IsPlayoffBowl))
                Assert.IsTrue(cs.Games.Any(g => g.Played && g.Label != null && g.Label.Contains(b.Name)), b.Name + " hosted a playoff game");
            Assert.GreaterOrEqual(cs.ChampionTeamId, 0);
            Assert.IsTrue(cs.Games.All(g => g.Played));
        }

        [Test]
        public void TheBracketAndBowlViewsShowEveryGame()
        {
            CollegeBracketView v = CollegeViews.Bracket(Done);
            Assert.IsTrue(v.Built);
            Assert.AreEqual(12, v.Seeds.Count);
            Assert.AreEqual(4, v.Rounds.Count);
            Assert.AreEqual(new[] { 4, 4, 2, 1 }, v.Rounds.Select(r => r.Games.Count).ToArray());
            Assert.AreEqual(35, v.Bowls.Count);
            Assert.AreEqual(1, v.Seeds.Count(x => x.Champion));
            Assert.AreEqual(v.ChampionId, Done.College.Season.ChampionTeamId);
            Assert.AreEqual(10, CollegeViews.ConferenceChampionships(Done).Count);
        }

        // ------------------------------------------------------------ box scores, honors, history
        [Test]
        public void BoxScoresAreStoredForEveryPlayedGame()
        {
            CareerSave s = CollegeTestUtil.InSeason(32, 5);
            Season cs = s.College.Season;
            int played = cs.Games.Count(g => g.Played);
            Assert.AreEqual(played, s.Net.Boxes.Count(b => b.Year == cs.Year));
            foreach (CollegeBox b in s.Net.Boxes.Take(60))
            {
                ScheduledGame g = cs.Games[b.GameId];
                Assert.AreEqual(g.HomeScore, b.HomeScore); Assert.AreEqual(g.AwayScore, b.AwayScore);
                Assert.AreEqual(b.HomeScore, b.HomeByQuarter.Sum(), "quarters add up (home)");
                Assert.AreEqual(b.AwayScore, b.AwayByQuarter.Sum(), "quarters add up (away)");
                Assert.IsTrue(b.Leaders.Count >= 2, "game leaders");
                Assert.IsTrue(b.Home.TotalYards > 0 && b.Away.TotalYards > 0);
            }
            CollegeBoxView v = CollegeViews.BoxScore(s, cs.Games.First(g => g.Played).Id);
            Assert.IsNotNull(v);
            StringAssert.Contains("Week 1", v.Title);
        }

        [Test]
        [Category("Slow")]   // multi-season: excluded from the release run (-testCategory "!Slow")
        public void TheCurrentSeasonKeepsAllBoxScoresAndLastSeasonOnlyItsPostseason()
        {
            CareerSave s = CollegeTestUtil.Career(33);
            CareerFlow.SimToNextSeason(s);
            int y0 = s.College.Year - 1;
            Assert.IsTrue(s.Net.Boxes.Any(b => b.Year == y0 && b.Kind != GameKind.Regular), "last season keeps its title games, playoff and bowls");
            Assert.IsFalse(s.Net.Boxes.Any(b => b.Year == y0 && b.Kind == GameKind.Regular), "but not its regular season");
            CareerFlow.SimToNextSeason(s);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            for (int i = 0; i < 3; i++) CollegeTestUtil.Step(s);
            Assert.IsFalse(s.Net.Boxes.Any(b => b.Year == y0), "two seasons back is gone");
            Assert.IsTrue(s.Net.Boxes.Any(b => b.Year == s.College.Year - 1 && b.Kind != GameKind.Regular), "last season's postseason stays");
            Assert.IsTrue(s.Net.Boxes.Any(b => b.Year == s.College.Year && b.Kind == GameKind.Regular), "this season is complete so far");
        }

        [Test]
        public void HonorsAndSchoolHistoryAreSettledWhenTheSeasonEnds()
        {
            CareerSave s = Done;
            int year = s.College.Year;
            Assert.AreEqual(year, s.Net.HonorsYear);
            var helm = s.Net.Honors.Where(h => h.Year == year && h.Kind == CollegeHonorKind.GoldenHelm).ToList();
            Assert.AreEqual(1, helm.Count);
            Assert.AreEqual(s.College.Season.GoldenHelmId, helm[0].PlayerId);
            Assert.AreEqual(25, s.Net.Honors.Count(h => h.Year == year && h.Kind == CollegeHonorKind.AllAmericaFirst), "25-man All-America first team");
            Assert.AreEqual(25, s.Net.Honors.Count(h => h.Year == year && h.Kind == CollegeHonorKind.AllAmericaSecond));
            Assert.AreEqual(10, s.Net.Honors.Count(h => h.Year == year && h.Kind == CollegeHonorKind.ConferencePlayer));
            Assert.AreEqual(10 * 25, s.Net.Honors.Count(h => h.Year == year && h.Kind == CollegeHonorKind.AllConference));
            var rows = CollegeViews.SeasonResults(s, year);
            Assert.AreEqual(138, rows.Count);
            Assert.AreEqual(1, rows.Count(r => r.NationalChampion));
            Assert.AreEqual(10, rows.Count(r => r.ConferenceChampion));
            Assert.AreEqual(12, rows.Count(r => r.InPlayoff));
            Assert.AreEqual(25, rows.Count(r => r.FinalRank > 0));
            Assert.IsTrue(s.News.Any(n => n.Kind == NewsKind.College), "college headline in the news feed");
            Assert.IsTrue(s.News.Where(n => n.Kind == NewsKind.College).All(n => n.TeamIds.Count == 0), "college news never carries a pro team id");
            var champs = CollegeViews.Champions(s);
            Assert.AreEqual(1, champs.Count);
            Assert.AreEqual(s.College.Season.ChampionTeamId, champs[0].ChampionId);
        }

        [Test]
        public void StandingsConferenceViewsAreSortedAndComplete()
        {
            CareerSave s = Done;
            var all = CollegeViews.Conferences(s);
            Assert.GreaterOrEqual(all.Count, 10);
            Assert.AreEqual(138, all.Sum(c => c.Rows.Count));
            foreach (CollegeConferenceView c in all)
            {
                for (int i = 1; i < c.Rows.Count; i++)
                {
                    float a = c.Rows[i - 1].ConfRecord.Split('-').Select(int.Parse).First(), b = c.Rows[i].ConfRecord.Split('-').Select(int.Parse).First();
                    Assert.GreaterOrEqual(a + 3, b, c.Name + " roughly sorted by conference wins");
                }
                Assert.IsTrue(c.Rows[0].ConferenceLeader);
            }
        }

        // ------------------------------------------------------------ playing a college game in the match
        [Test]
        public void ACollegeGameSetUpForTheSpectatorMatchCarriesEverythingTheViewNeeds()
        {
            CareerSave s = CollegeTestUtil.InSeason(34, 3);
            ScheduledGame g = CollegeNet.GamesThisWeek(s).First(x => !x.Played);
            CollegeGameSetup gs = CollegeNet.SetupGame(s, g.Id);
            Assert.IsNotNull(gs);
            Assert.AreEqual(s.College.Season.Year, gs.Year);
            Assert.AreEqual(g.Week, gs.Week);
            Assert.AreSame(s.College.GetSchool(g.HomeId).Team, gs.Home);
            Assert.AreSame(s.College.GetSchool(g.AwayId).Team, gs.Away);
            Assert.AreEqual(85, gs.Home.Roster.Count);
            Assert.AreEqual(s.College.GetSchool(g.HomeId).Key, gs.HomeKey);
            Assert.IsNotEmpty(gs.HomeCrest); Assert.IsNotEmpty(gs.AwayCrest);
            Assert.IsTrue(gs.HomeColor.StartsWith("#") && gs.AwayColor.StartsWith("#"));
            Assert.IsNotEmpty(gs.StadiumName); Assert.Greater(gs.StadiumCapacity, 20000);
            Assert.AreNotEqual(0, gs.WeatherSeed); Assert.AreNotEqual(0, gs.SimSeed);
            Assert.IsNotNull(gs.Rules);
            Assert.IsNotEmpty(gs.Title); Assert.IsNotEmpty(gs.DateLabel);
            Assert.IsFalse(gs.AlreadyPlayed);
            CollegeGameSetup again = CollegeNet.SetupGame(s, g.Id);
            Assert.AreEqual(gs.WeatherSeed, again.WeatherSeed, "deterministic");
            Assert.IsNull(CollegeNet.SetupGame(s, 999999));
        }

        [Test]
        public void AGamePlayedInTheMatchCountsLikeASimmedGame()
        {
            CareerSave s = CollegeTestUtil.InSeason(35, 2);
            Season cs = s.College.Season;
            ScheduledGame g = CollegeNet.GamesThisWeek(s).First(x => !x.Played);
            CollegeGameSetup gs = CollegeNet.SetupGame(s, g.Id);
            GameResult r = QuickSim.SimGame(gs.Home, gs.Away, gs.Rules, new Rng(5), gs.Options);
            int wins = cs.WinsOf(r.WinnerTeamId);
            OpResult res = CollegeNet.SubmitGame(s, g.Id, r);
            Assert.IsTrue(res.Ok, res.Message);
            Assert.IsTrue(g.Played);
            Assert.AreEqual(wins + 1, cs.WinsOf(r.WinnerTeamId));
            Assert.IsFalse(CollegeNet.SubmitGame(s, g.Id, r).Ok, "no second result");
            Assert.IsFalse(CollegeNet.SimGame(s, g.Id) != null, "a played game cannot be simmed again");
            int boxes = s.Net.Boxes.Count;
            CollegeTestUtil.Step(s);
            Assert.AreEqual(boxes + cs.GamesOfWeek(cs.Week - 1).Count, s.Net.Boxes.Count, "the played game's box score is stored with the week's others");
            CollegeBox b = CollegeBoxes.Find(s, cs.Year, g.Id);
            Assert.IsNotNull(b);
            Assert.AreEqual(r.HomeScore, b.HomeScore);
            // a result for another pair of teams is refused
            ScheduledGame other = CollegeNet.GamesThisWeek(s).First(x => !x.Played);
            Assert.IsFalse(CollegeNet.SubmitGame(s, other.Id, r).Ok);
        }

        [Test]
        public void ASingleCollegeGameCanBeSimmedToReadItsBoxScoreEarly()
        {
            CareerSave s = CollegeTestUtil.InSeason(36, 1);
            ScheduledGame g = CollegeNet.GamesThisWeek(s).First();
            GameResult r = CollegeNet.SimGame(s, g.Id);
            Assert.IsNotNull(r);
            Assert.IsTrue(g.Played);
            CollegeTestUtil.Step(s);
            Assert.IsNotNull(CollegeBoxes.Find(s, s.College.Season.Year, g.Id));
            Assert.AreEqual(s.College.Season.Games.Count(x => x.Week == g.Week), s.Net.Boxes.Count(b => b.Week == g.Week && b.Year == s.College.Season.Year));
        }

        [Test]
        public void TheProLeagueIsTheSameWithOrWithoutTheCollegeWorldThroughTheSeason()
        {
            CareerSave withCollege = CareerTestUtil.Fresh(37);
            var o = new CareerOptions { Seed = 37, UserTeamId = 5, GenerateCollegeIfMissing = false };
            CareerSave without = CareerFlow.NewCareer(o);
            CareerFlow.AdvanceUntil(withCollege, CareerPhase.Awards);
            CareerFlow.AdvanceUntil(without, CareerPhase.Awards);
            Assert.AreEqual(LeagueTestUtil.Fingerprint(without.Pro), LeagueTestUtil.Fingerprint(withCollege.Pro), "the college sim draws nothing from the career random stream");
            Assert.AreEqual(without.Season.ChampionTeamId, withCollege.Season.ChampionTeamId);
            Assert.AreEqual(without.Transactions.Count, withCollege.Transactions.Count);
        }
    }
}
