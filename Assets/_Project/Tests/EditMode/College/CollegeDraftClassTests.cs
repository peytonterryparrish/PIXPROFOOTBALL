using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Tests
{
    /// <summary>The draft class comes from the college world: seniors plus declared juniors, with college stats, honors and combine numbers; recruiting keeps the schools full.</summary>
    public class CollegeDraftClassTests
    {
        private static CareerSave _s;
        private static CareerSave S { get { return _s ?? (_s = CollegeTestUtil.AtPhase(61, CareerPhase.Scouting)); } }

        [Test]
        public void TheClassIsAboutTwoHundredSixtyAndComesFromTheCollegeRosters()
        {
            var cls = S.Pro.DraftClass;
            Assert.That(cls.Count, Is.InRange(250, 285));
            Assert.AreEqual(cls.Count, cls.Select(p => p.Id).Distinct().Count());
            Assert.IsTrue(cls.All(p => p.SchoolId >= 0), "every prospect has a school");
            foreach (Prospect p in cls.Take(80))
            {
                School sc = S.College.GetSchool(p.SchoolId);
                Assert.IsNotNull(sc.Team.GetPlayer(p.Id), p.Player.Name + " is on the roster of " + sc.Name + " until the draft turns the college over");
                Assert.AreEqual(sc.ShortName, p.SchoolName);
                Assert.AreEqual(sc.ShortName, p.Player.Bio.College);
                Assert.IsTrue(p.Player.Class == ClassYear.Senior || p.Player.Class == ClassYear.Junior);
                Assert.AreEqual(p.Player.Class == ClassYear.Junior, p.DeclaredEarly);
            }
            Assert.AreEqual(Enumerable.Range(1, cls.Count).ToList(), cls.Select(p => p.ConsensusRank).ToList(), "the public board is ranked 1..n in order");
            Assert.AreEqual(32, S.Pro.ScoutingReports.Count);
        }

        [Test]
        public void JuniorsDeclareByProjectionTheBetterThePublicThinksOfThemTheLikelier()
        {
            Assert.Greater(CollegeDraftClass.DeclareChance(10), CollegeDraftClass.DeclareChance(100));
            Assert.Greater(CollegeDraftClass.DeclareChance(100), CollegeDraftClass.DeclareChance(250));
            Assert.Greater(CollegeDraftClass.DeclareChance(250), CollegeDraftClass.DeclareChance(2000));
            Assert.Less(CollegeDraftClass.DeclareChance(3000), 0.1f);
            var cls = S.Pro.DraftClass;
            int juniors = cls.Count(p => p.DeclaredEarly);
            Assert.That(juniors, Is.InRange(25, 150), "early entrants: " + juniors);
            // in the whole class, juniors are a minority but a real group at the top
            int topJuniors = cls.Where(p => p.ConsensusRank <= 60).Count(p => p.DeclaredEarly);
            Assert.Greater(topJuniors, 3, "star juniors come out");
            // juniors of the pool who did not declare are still in school
            var inClass = new HashSet<int>(cls.Select(p => p.Id));
            int stayed = S.College.Schools.SelectMany(x => x.Team.Roster).Count(p => p.Class == ClassYear.Junior && !p.IsRedshirting && !inClass.Contains(p.Id));
            Assert.Greater(stayed, 1000, "most juniors stay");
        }

        [Test]
        public void CollegeStatsAndHonorsMoveWithTheProspectOutOfTheProCareer()
        {
            var cls = S.Pro.DraftClass;
            int withStats = cls.Count(p => p.Player.CollegeCareer.Any(c => c.Games > 0));
            Assert.Greater(withStats, cls.Count * 0.7f, "college seasons are attached");
            Assert.IsTrue(cls.All(p => p.Player.Career.Count == 0), "a prospect has no pro seasons");
            foreach (Prospect p in cls.Take(30))
                foreach (SeasonStats st in p.Player.CollegeCareer) Assert.AreEqual(p.SchoolId, st.TeamId, "college stats carry the school id");
            Assert.Greater(cls.Count(p => p.Honors.Count > 0), 40, "honors attached");
            Assert.IsTrue(cls.Any(p => p.Honors.Any(h => h.Contains("All-America"))));
            Assert.IsTrue(cls.SelectMany(p => p.Honors).All(h => h.StartsWith(S.Year.ToString())));
        }

        [Test]
        public void CollegeSeasonsDoNotCountAsProSeasonsAfterTheDraft()
        {
            CareerSave s = CollegeTestUtil.AtPhase(62, CareerPhase.RookieSigning);
            var drafted = new HashSet<int>(s.Pro.LastDraft.Select(d => d.PlayerId));
            var rookies = s.Pro.Teams.SelectMany(t => t.Roster).Where(p => drafted.Contains(p.Id)).ToList();
            Assert.AreEqual(224, rookies.Count);
            Assert.IsTrue(rookies.All(p => p.Career.Count == 0), "no pro stats yet");
            Assert.Greater(rookies.Count(p => p.CollegeCareer.Count > 0), 150);
            PlayerCard card = CareerViews.Card(s, rookies[0].Id);
            Assert.AreEqual(0, card.Seasons.Count);
            Assert.Greater(card.CollegeSeasons.Count, 0);
        }

        [Test]
        public void TheOverallDistributionOfTheClassIsSensible()
        {
            var cls = S.Pro.DraftClass;
            var ovr = cls.Select(p => p.Player.Overall).OrderByDescending(x => x).ToList();
            Assert.That(ovr.Average(), Is.InRange(68.0, 80.0));
            Assert.GreaterOrEqual(ovr[0], 82, "a star at the top");
            Assert.GreaterOrEqual(ovr.Take(10).Average(), 80);
            Assert.GreaterOrEqual(ovr.Take(32).Min(), 74, "a first round of real talent");
            Assert.GreaterOrEqual(ovr.Last(), 55);
            Assert.Less(ovr.Last(), ovr.Average());
            Assert.Greater(ovr.Take(50).Average(), ovr.Skip(200).Average() + 5, "the top of the class is clearly better than the bottom");
            // positions: every group has a share
            foreach (PositionBucket b in new[] { PositionBucket.QB, PositionBucket.RB, PositionBucket.WR, PositionBucket.TE, PositionBucket.OL, PositionBucket.DL, PositionBucket.LB, PositionBucket.CB, PositionBucket.S })
                Assert.GreaterOrEqual(cls.Count(p => PositionBuckets.Of(p.Player.Position) == b), 6, b + " group");
            Assert.GreaterOrEqual(cls.Count(p => p.Player.Position == Position.K), 3);
            Assert.GreaterOrEqual(cls.Count(p => p.Player.Position == Position.P), 3);
            Assert.Less(cls.Count(p => p.Player.Position == Position.QB), 30);
            // the public board has top prospects at premium positions and grades correlate with the true grade
            double mx = cls.Average(p => p.TrueGrade), my = cls.Average(p => -p.ConsensusRank);
            double corr = cls.Sum(p => (p.TrueGrade - mx) * (-p.ConsensusRank - my)) / Math.Sqrt(cls.Sum(p => Math.Pow(p.TrueGrade - mx, 2)) * cls.Sum(p => Math.Pow(-p.ConsensusRank - my, 2)));
            Assert.Greater(corr, 0.7, "the public board follows real talent (r=" + Math.Round(corr, 2) + ")");
        }

        [Test]
        public void ProspectsDoNotCarryLongInjuriesIntoThePros()
        {
            Assert.IsTrue(S.Pro.DraftClass.All(p => p.Player.Injury.WeeksOut <= 12), "a college season-ending injury is rehabbed by the time of the draft");
        }

        [Test]
        public void PublicOpinionIsStableFromTheSeasonIntoTheClass()
        {
            CareerSave s = CollegeTestUtil.InSeason(63, 2);
            var before = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Sort = "public", Take = 30 }).Rows;
            var idsBefore = before.Select(r => r.PlayerId).ToList();
            CareerFlow.AdvanceUntil(s, CareerPhase.Scouting);
            var cls = s.Pro.DraftClass;
            // the players the public liked in week 3 are mostly the top of the class board (juniors may have stayed)
            int inTop60 = idsBefore.Count(id => cls.Any(p => p.Id == id && p.ConsensusRank <= 60));
            Assert.GreaterOrEqual(inTop60, 18, "the public top 30 of the season is in the top 60 of the class (" + inTop60 + ")");
        }

        [Test]
        public void TheClassIsDraftedThenTheCollegeTurnsOverWithFreshmenAtEightyFive()
        {
            CareerSave s = CollegeTestUtil.AtPhase(64, CareerPhase.Scouting);
            int collegeYear = s.College.Year;
            var classIds = new HashSet<int>(s.Pro.DraftClass.Select(p => p.Id));
            CareerFlow.AdvanceUntil(s, CareerPhase.RookieSigning);
            Assert.AreEqual(collegeYear + 1, s.College.Year);
            CollegeTestUtil.AssertRostersFull(s);
            foreach (School sc in s.College.Schools)
            {
                Assert.IsFalse(sc.Team.Roster.Any(p => classIds.Contains(p.Id)), sc.Name + " lost the players who turned pro");
                Assert.IsFalse(sc.Team.Roster.Any(p => p.Retired), sc.Name + " has no graduated players left");
                Assert.GreaterOrEqual(sc.Team.Roster.Count(p => p.Class == ClassYear.Freshman), 8, sc.Name + " recruited a class");
            }
            Assert.AreEqual(138, s.College.Schools.Select(x => x.Team.Roster.Count).Count(n => n == 85));
            // recruiting follows prestige: elite programs recruit more stars than the low ones
            double top = s.College.Schools.Where(x => x.Prestige >= 5).SelectMany(x => x.Team.Roster).Where(p => p.Class == ClassYear.Freshman).Average(p => p.Stars);
            double low = s.College.Schools.Where(x => x.Prestige <= 2).SelectMany(x => x.Team.Roster).Where(p => p.Class == ClassYear.Freshman).Average(p => p.Stars);
            Assert.Greater(top, low + 0.4, "better programs sign better recruits");
            var rec = CollegeViews.Recruiting(s, 25);
            Assert.AreEqual(25, rec.Count);
            Assert.GreaterOrEqual(rec[0].Points, rec[24].Points);
            Assert.GreaterOrEqual(rec[0].AvgStars, rec[24].AvgStars - 1f);
        }

        [Test]
        public void UndraftedProspectsBecomeFreeAgentsAndTheDraftIsStillTwoHundredTwentyFour()
        {
            CareerSave s = CollegeTestUtil.AtPhase(65, CareerPhase.RookieSigning);
            Assert.AreEqual(224, s.Pro.LastDraft.Count);
            Assert.IsTrue(s.Pro.LastDraft.All(d => !string.IsNullOrEmpty(d.College)));
            var udfa = s.Pro.FreeAgents.Where(p => p.DraftYear == s.Year && p.DraftRound == 0 && p.Experience == 0).ToList();
            Assert.Greater(udfa.Count, 10);
            CareerTestUtil.AssertClean(s, false);
        }
    }
}
