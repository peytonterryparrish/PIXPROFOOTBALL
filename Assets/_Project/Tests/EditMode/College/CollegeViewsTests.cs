using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Tests
{
    /// <summary>The view models the College Network screens bind to: hub, top 25, schedules, team pages, leaders, scouting table rows, prospect card.</summary>
    public class CollegeViewsTests
    {
        private static CareerSave _mid;
        private static CareerSave Mid { get { return _mid ?? (_mid = CollegeTestUtil.InSeason(81, 6)); } }

        [Test]
        public void TheHubShowsTheWeekTheTop25TheHelmRaceAndTheGames()
        {
            CollegeHubView h = CollegeViews.Hub(Mid);
            Assert.AreEqual(Mid.College.Year, h.Year);
            Assert.AreEqual(7, h.Week);
            Assert.AreEqual("Week 7", h.WeekTitle);
            Assert.AreEqual("Oct 11", h.DateLabel);
            Assert.IsFalse(h.SeasonComplete);
            Assert.AreEqual(25, h.Top25.Count);
            Assert.AreEqual("Week 6", h.PollLabel);
            Assert.AreEqual(10, h.Helm.Count);
            Assert.Greater(h.Games.Count, 40);
            Assert.IsTrue(h.Games.All(g => g.Watchable && !g.Played), "this week's games can all be watched");
            Assert.LessOrEqual(h.GamesOfTheWeek.Count, 5);
            Assert.IsTrue(h.GamesOfTheWeek.Take(2).Any(g => g.HomeRank > 0 || g.AwayRank > 0), "the featured games involve ranked teams");
            Assert.Greater(h.Games.Sum(g => g.ProspectsOfInterest), 5, "the games show how many top prospects play");
            Assert.AreEqual(CollegeScouting.PointsRemaining(Mid), h.ScoutPoints);
            Assert.AreEqual(CollegeScouting.PointsPerWeek(Mid), h.ScoutPointsPerWeek);
            Assert.IsTrue(h.CanAttendGame);
            CollegeGameRow g0 = h.Games[0];
            Assert.IsNotEmpty(g0.Home); Assert.IsNotEmpty(g0.Away); Assert.IsNotEmpty(g0.HomeRecord);
        }

        [Test]
        public void Top25RowsCarryRecordsMovementAndConference()
        {
            var top = CollegeViews.Top25(Mid);
            Assert.AreEqual(25, top.Count);
            Assert.IsTrue(top.All(r => !string.IsNullOrEmpty(r.Record) && !string.IsNullOrEmpty(r.Conference) && !string.IsNullOrEmpty(r.ShortName)));
            Assert.IsTrue(top.Take(5).All(r => r.Points > 0));
            Assert.IsTrue(top.Zip(top.Skip(1), (a, b) => a.Points >= b.Points).All(x => x));
            var pre = CollegeViews.Top25(Mid, 0);
            Assert.AreEqual(25, pre.Count);
            Assert.IsTrue(pre.All(r => r.PrevRank == 0), "the preseason poll has no previous rank");
            var labels = CollegeViews.PollLabels(Mid);
            Assert.AreEqual("Preseason", labels[0]);
            Assert.AreEqual(7, labels.Count);
        }

        [Test]
        public void ASchoolScheduleHasTwelveGamesAndTheByeWeeksWithResultsAndOpponentRanks()
        {
            int team = CollegeViews.Top25(Mid)[0].TeamId;
            var sched = CollegeViews.Schedule(Mid, team);
            Assert.AreEqual(14, sched.Count(r => r.Week <= 14), "14 regular weeks");
            Assert.AreEqual(12, sched.Count(r => !r.Bye && r.Kind == GameKind.Regular));
            Assert.AreEqual(2, sched.Count(r => r.Bye));
            Assert.AreEqual(6, sched.Count(r => r.Played), "six weeks played");
            Assert.AreEqual(sched.Count(r => r.Played), sched.Count(r => r.Played && r.HasBox));
            foreach (CollegeScheduleRow r in sched.Where(x => x.Played)) Assert.AreEqual(r.Won, r.TeamScore > r.OpponentScore);
            Assert.IsTrue(sched.Where(r => !r.Bye).All(r => !string.IsNullOrEmpty(r.Opponent) && !string.IsNullOrEmpty(r.DateLabel)));
            Assert.AreEqual(sched.Count(r => r.Played && r.Won), Mid.College.Season.WinsOf(team));
        }

        [Test]
        public void TheTeamPageShowsTheProgramTheRosterAndTheHiddenRatings()
        {
            int id = CollegeViews.Top25(Mid)[0].TeamId;
            CollegeTeamView v = CollegeViews.Team(Mid, id);
            School sc = Mid.College.GetSchool(id);
            Assert.AreEqual(sc.Name, v.Name);
            Assert.AreEqual(1, v.Rank);
            Assert.AreEqual(85, v.RosterCount);
            Assert.AreEqual(85, v.Roster.Count);
            Assert.IsNotEmpty(v.HeadCoach); Assert.IsNotEmpty(v.Stadium);
            Assert.AreEqual(Mid.College.Season.WinsOf(id), v.Wins);
            Assert.AreEqual(14, v.Schedule.Count(r => r.Week <= 14));
            Assert.IsTrue(v.Roster.Where(r => r.Tier <= ScoutTier.Glance).All(r => r.OverallText == "?"), "ratings are hidden until scouted");
            Assert.IsTrue(v.Roster.Any(r => r.SeasonLine.Length > 0), "season stat lines");
            Assert.IsTrue(v.Roster.Where(r => r.Eligible).All(r => r.PublicRank > 0), "eligible players have a public rank");
            Assert.IsTrue(v.Roster.Where(r => !r.Eligible).All(r => r.PublicRank == 0));
            Assert.IsNull(CollegeViews.Team(Mid, 9999));
        }

        [Test]
        public void LeadersAreSortedAndCarryTheSchool()
        {
            var pass = CollegeViews.Leaders(Mid, StatCategory.PassYds, 15);
            Assert.AreEqual(15, pass.Count);
            Assert.IsTrue(pass.Zip(pass.Skip(1), (a, b) => a.Value >= b.Value).All(x => x));
            Assert.IsTrue(pass.All(r => r.Pos == Position.QB || r.Value > 0));
            Assert.IsTrue(pass.All(r => !string.IsNullOrEmpty(r.School)));
            Assert.Greater(pass[0].Value, 600, "after six weeks the leader has real yards");
            var sacks = CollegeViews.Leaders(Mid, StatCategory.Sacks, 10);
            Assert.AreEqual(10, sacks.Count);
        }

        [Test]
        public void TheScoutingTableRowsFollowTheReferenceLayout()
        {
            var t = CollegeScoutViews.ScoutingTable(Mid, new ScoutingFilter { Sort = "public", Take = 10 });
            Assert.AreEqual(10, t.Rows.Count);
            Assert.Greater(t.Total, 5000, "the whole eligible pool is browsable");
            ScoutingRow r = t.Rows[0];
            Assert.AreEqual(1, r.Rank);
            Assert.IsNotEmpty(r.Name); Assert.IsNotEmpty(r.School); Assert.IsNotEmpty(r.Height);
            Assert.Greater(r.WeightLb, 150);
            Assert.IsNotEmpty(r.PublicRound);
            Assert.IsNotEmpty(r.UserRound);
            Assert.AreEqual(6, r.Attributes.Count(a => a.Code.Length == 3), "three-letter attribute codes");
            Assert.AreEqual(CollegeScouting.PointsRemaining(Mid), t.PointsRemaining);
            Assert.AreEqual(CollegeScouting.PointsPerWeek(Mid), t.PointsPerWeek);
            Assert.AreEqual(8, t.Needs.Count);
            Assert.GreaterOrEqual(r.NextTierCost, 0);
        }

        [Test]
        public void TheProspectCardShowsBioCollegeStatsHonorsAndScoutedGrades()
        {
            CareerSave s = CollegeTestUtil.AtPhase(82, CareerPhase.Scouting);
            Prospect p = s.Pro.DraftClass.Where(x => x.Honors.Count > 0 && x.Player.CollegeCareer.Count > 0).OrderBy(x => x.ConsensusRank).First();
            CollegeScouting.UserReport(s).PointsBudget += 200;
            CollegeScouting.ScoutPlayer(s, p.Id, 25);
            ProspectCardView c = CollegeScoutViews.Card(s, p.Id);
            Assert.AreEqual(p.Player.Name, c.Name);
            Assert.AreEqual(p.Player.Position, c.Pos);
            Assert.AreEqual(p.SchoolName, s.College.GetSchool(c.SchoolId).ShortName);
            Assert.IsNotEmpty(c.School); Assert.IsNotEmpty(c.Height); Assert.IsNotEmpty(c.Hometown);
            Assert.AreEqual(p.ConsensusRank, c.PublicRank);
            Assert.IsTrue(c.InClass);
            Assert.That(c.Attributes.Count, Is.InRange(6, 10), "the card lists the position's rating weights");
            Assert.AreEqual(ScoutTier.Range, c.Tier);
            Assert.IsTrue(c.Attributes.All(a => !a.Locked));
            CollegeScouting.ScoutPlayer(s, p.Id, 40);
            c = CollegeScoutViews.Card(s, p.Id);
            Assert.AreEqual(ScoutTier.Exact, c.Tier);
            Assert.AreEqual(p.Player.Overall.ToString(), c.OverallText);
            Assert.IsTrue(c.Honors.Count > 0);
            Assert.IsTrue(c.CollegeSeasons.Count > 0);
            Assert.IsTrue(c.CollegeSeasons.All(x => x.Line.Length > 0 || x.Games == 0));
            Assert.IsTrue(c.ScoutNotes.Any(n => n.StartsWith("Strength")), "scout notes read the strengths");
            // abilities show up once enough points were spent
            if (p.Player.Abilities.Count > 0) Assert.Greater(c.Abilities.Count, 0);
            Assert.IsNull(CollegeScoutViews.Card(s, 987654));
        }

        [Test]
        public void TheCardOfAnUnscoutedPlayerOffersTheNextStepAndShowsNothingSecret()
        {
            var row = CollegeScoutViews.ScoutingTable(Mid, new ScoutingFilter { Sort = "public", Take = 100 }).Rows.First(r => r.Tier == ScoutTier.Unknown);
            ProspectCardView c = CollegeScoutViews.Card(Mid, row.PlayerId);
            Assert.AreEqual(ScoutTier.Unknown, c.Tier);
            Assert.AreEqual("?", c.OverallText);
            Assert.AreEqual("?", c.PotentialText);
            Assert.AreEqual("?", c.GradeText);
            Assert.AreEqual(CollegeScouting.BandPoints, c.NextTierCost);
            Assert.IsTrue(c.Attributes.All(a => a.Locked && a.Value == -1 && a.Text == "?"));
            Assert.IsTrue(c.ScoutNotes.Count == 1 && c.ScoutNotes[0].Contains("Not scouted"));
            Assert.AreEqual(0, c.Abilities.Count);
            Assert.IsFalse(c.InClass);
        }

        [Test]
        [Category("Slow")]   // multi-season: excluded from the release run (-testCategory "!Slow")
        public void SchoolHistoryAndChampionsGrowSeasonBySeason()
        {
            CareerSave s = CollegeTestUtil.Career(83);
            CareerFlow.SimToNextSeason(s);
            CareerFlow.SimToNextSeason(s);
            var champs = CollegeViews.Champions(s);
            Assert.AreEqual(2, champs.Count);
            Assert.Greater(champs[0].Year, champs[1].Year);
            Assert.IsTrue(champs.All(c => !string.IsNullOrEmpty(c.Champion) && !string.IsNullOrEmpty(c.HelmWinner)));
            Assert.AreEqual(2 * 138, s.Net.History.Count);
            int team = champs[0].ChampionId;
            var v = CollegeViews.Team(s, team);
            Assert.AreEqual(2, v.History.Count);
            Assert.IsTrue(v.History.First(h => h.Year == champs[0].Year).NationalChampion);
            var allAmerica = CollegeViews.Honors(s, champs[1].Year, CollegeHonorKind.AllAmericaFirst);
            Assert.AreEqual(25, allAmerica.Count);
            Assert.IsTrue(CollegeViews.Honors(s, champs[1].Year, CollegeHonorKind.GoldenHelm).Count == 1);
        }

        [Test]
        public void ThePlayerCardOfAProShowsHisCollegeSeasons()
        {
            CareerSave s = CollegeTestUtil.AtPhase(84, CareerPhase.RookieSigning);
            var pick = s.Pro.LastDraft[0];
            PlayerCard card = CareerViews.Card(s, pick.PlayerId);
            Assert.Greater(card.CollegeSeasons.Count, 0);
            Assert.AreEqual(0, card.Seasons.Count);
            Assert.AreEqual(pick.College, card.College);
        }
    }
}
