using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Tests
{
    /// <summary>Save and load of the college world and the College Network state (scouts, the user's scouting knowledge, big board, box scores, honors, history).</summary>
    public class CollegeSaveTests
    {
        private static CareerSave RoundTrip(CareerSave s) { int len; return CareerSaveTests.RoundTrip(s, out len); }

        [Test]
        public void ASeasonInProgressKeepsTheCollegeWorldAndTheNetworkThroughTheRoundTrip()
        {
            CareerSave s = CollegeTestUtil.InSeason(101, 6);
            var rows = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Sort = "public", Take = 12 }).Rows;
            CollegeScouting.UserReport(s).PointsBudget += 100;
            CollegeScouting.ScoutPlayer(s, rows[2].PlayerId, 25);
            CollegeScouting.AddToBoard(s, rows[2].PlayerId);
            CollegeScouting.SetNote(s, rows[2].PlayerId, "Watch his hands", "Sleeper");
            CollegeScouting.Star(s, rows[5].PlayerId, true);
            CollegeScouting.AttendGame(s, CollegeNet.GamesThisWeek(s)[0].Id);
            CollegeScouting.AssignScout(s, CollegeScouting.UserScouts(s).First(x => !x.Director).Id, rows[0].SchoolId, -1, (int)PositionBucket.OL);
            CareerSave back = RoundTrip(s);

            Assert.AreEqual(138, back.College.Schools.Count);
            Assert.AreEqual(s.College.Season.Week, back.College.Season.Week);
            Assert.AreEqual(s.College.Season.Polls.Count, back.College.Season.Polls.Count);
            Assert.AreEqual(s.College.Season.HelmRace.Count, back.College.Season.HelmRace.Count);
            Assert.AreEqual(CollegeTestUtil.Fingerprint(s), CollegeTestUtil.Fingerprint(back));
            Assert.AreEqual(s.Net.Boxes.Count, back.Net.Boxes.Count);
            Assert.AreEqual(s.Net.Boxes[10].Leaders.Count, back.Net.Boxes[10].Leaders.Count);
            Assert.AreEqual(s.Net.Boxes[10].Scoring, back.Net.Boxes[10].Scoring);
            Assert.AreEqual(s.Net.Scouts.Count, back.Net.Scouts.Count);
            var a = CollegeScouting.UserScouts(s).First(x => !x.Director); var b = back.Net.Scouts.First(x => x.Id == a.Id);
            Assert.AreEqual(a.AssignSchoolId, b.AssignSchoolId); Assert.AreEqual(a.FocusBucket, b.FocusBucket); Assert.AreEqual(a.Skill, b.Skill); Assert.AreEqual(a.PointsThisSeason, b.PointsThisSeason);
            Assert.IsTrue(back.Net.HasUserReport);
            ScoutingReport r1 = CollegeScouting.UserReport(s), r2 = CollegeScouting.UserReport(back);
            Assert.AreEqual(r1.Entries.Count, r2.Entries.Count);
            Assert.AreEqual(r1.PointsRemaining, r2.PointsRemaining);
            ScoutedProspect e1 = CollegeScouting.KnownEntry(r1, rows[2].PlayerId), e2 = CollegeScouting.KnownEntry(r2, rows[2].PlayerId);
            Assert.AreEqual(e1.PointsSpent, e2.PointsSpent); Assert.AreEqual(e1.Estimated.Speed, e2.Estimated.Speed); Assert.AreEqual(e1.EstimatedPotential, e2.EstimatedPotential, 0.001f); Assert.AreEqual(e1.Sd, e2.Sd, 0.001f);
            Assert.AreEqual(CollegeScouting.BoardIds(s), CollegeScouting.BoardIds(back));
            Assert.AreEqual("Watch his hands", CollegeScouting.FindEntry(back, rows[2].PlayerId).Note);
            Assert.AreEqual("Sleeper", CollegeScouting.FindEntry(back, rows[2].PlayerId).Tag);
            Assert.IsTrue(CollegeScouting.FindEntry(back, rows[5].PlayerId).Starred);
            Assert.AreEqual(s.Net.AttendWeek, back.Net.AttendWeek);
            Assert.IsFalse(CollegeScouting.AttendGame(back, CollegeNet.GamesThisWeek(back)[1].Id).Ok, "the visit of this week is remembered");
            // the views of the loaded career equal the original's
            Assert.AreEqual(CollegeViews.Top25(s).Select(r => r.TeamId).ToList(), CollegeViews.Top25(back).Select(r => r.TeamId).ToList());
            Assert.AreEqual(CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Take = 20 }).Rows.Select(r => r.PlayerId).ToList(), CollegeScoutViews.ScoutingTable(back, new ScoutingFilter { Take = 20 }).Rows.Select(r => r.PlayerId).ToList());
            CareerTestUtil.AssertClean(back, false);
        }

        [Test]
        public void ALoadedCareerKeepsSimulatingTheCollegeExactlyLikeTheOriginal()
        {
            CareerSave s = CollegeTestUtil.InSeason(102, 4);
            CareerSave back = RoundTrip(s);
            CareerFlow.AdvanceUntil(s, CareerPhase.Scouting);
            CareerFlow.AdvanceUntil(back, CareerPhase.Scouting);
            Assert.AreEqual(CollegeTestUtil.Fingerprint(s), CollegeTestUtil.Fingerprint(back));
            Assert.AreEqual(s.College.Season.ChampionTeamId, back.College.Season.ChampionTeamId);
            Assert.AreEqual(s.Pro.DraftClass.Select(p => p.Id).ToList(), back.Pro.DraftClass.Select(p => p.Id).ToList(), "the same class");
            Assert.AreEqual(s.Pro.DraftClass.Select(p => p.Combine.Forty).ToList(), back.Pro.DraftClass.Select(p => p.Combine.Forty).ToList());
            Assert.AreEqual(s.Net.Honors.Count, back.Net.Honors.Count);
        }

        [Test]
        public void TheScoutingPhaseAndTheDraftSurviveTheRoundTripWithCombineBoardAndKnowledge()
        {
            CareerSave s = CollegeTestUtil.AtPhase(103, CareerPhase.Scouting);
            Assert.IsTrue(CollegeScouting.AttendCombine(s).Ok);
            var ids = s.Pro.DraftClass.Take(5).Select(p => p.Id).ToList();
            CollegeScouting.UserReport(s).PointsBudget += 200;
            CollegeScouting.ScoutPlayer(s, ids[0], 65);
            foreach (int id in ids) CollegeScouting.AddToBoard(s, id);
            Prospect skipper = s.Pro.DraftClass.First(p => p.Combine.Skipped || !p.Combine.Done);
            CollegeScouting.AttendProDay(s, skipper.SchoolId);
            CareerSave back = RoundTrip(s);
            Assert.IsFalse(back.Net.HasUserReport);
            Assert.AreEqual(s.Net.PhysicalsKnown.Count, back.Net.PhysicalsKnown.Count);
            Assert.IsTrue(back.Net.CombineAttended);
            Assert.AreEqual(s.Net.ProDaySchools, back.Net.ProDaySchools);
            Assert.AreEqual(ScoutTier.Exact, CollegeScouting.TierOf(back, ids[0]));
            Assert.AreEqual(ids, back.Draft.BigBoard);
            Assert.AreEqual(ids, CollegeScouting.BoardIds(back));
            var c1 = CollegeScoutViews.Card(s, ids[0]); var c2 = CollegeScoutViews.Card(back, ids[0]);
            Assert.AreEqual(c1.OverallText, c2.OverallText);
            Assert.AreEqual(c1.Honors, c2.Honors);
            Assert.AreEqual(c1.CollegeSeasons.Count, c2.CollegeSeasons.Count);
            var p1 = s.Pro.DraftClass.First(p => p.Id == ids[1]); var p2 = back.Pro.DraftClass.First(p => p.Id == ids[1]);
            Assert.AreEqual(p1.Combine.Forty, p2.Combine.Forty, 0.0001f);
            Assert.AreEqual(p1.Combine.BenchReps, p2.Combine.BenchReps);
            Assert.AreEqual(p1.Player.CollegeCareer.Count, p2.Player.CollegeCareer.Count);
            // the draft goes on identically
            CareerFlow.AdvanceUntil(s, CareerPhase.RookieSigning);
            CareerFlow.AdvanceUntil(back, CareerPhase.RookieSigning);
            Assert.AreEqual(CareerTestUtil.Fingerprint(s), CareerTestUtil.Fingerprint(back));
            Assert.AreEqual(CollegeTestUtil.Fingerprint(s), CollegeTestUtil.Fingerprint(back));
        }

        [Test]
        public void AnOldSaveWithoutTheNetworkStateIsSetUpOnFirstUse()
        {
            CareerSave s = CollegeTestUtil.InSeason(104, 3);
            s.Net = new CollegeNetState();   // what JsonUtility loads for a save made before v0.9.0
            Assert.IsFalse(s.Net.Ready);
            Assert.AreEqual(0, s.Net.Scouts.Count);
            CollegeHubView hub = CollegeViews.Hub(s);   // any view or action sets it up
            Assert.IsTrue(s.Net.Ready);
            Assert.GreaterOrEqual(s.Net.Scouts.Count, 32 * 3);
            Assert.AreEqual(4, CollegeScouting.UserScouts(s).Count);
            Assert.AreEqual(CollegeScouting.StartingPoints, CollegeScouting.PointsRemaining(s));
            Assert.AreEqual(4, hub.Week);
            Assert.AreEqual(25, hub.Top25.Count);
            CollegeTestUtil.Step(s);
            Assert.Greater(CollegeScouting.PointsRemaining(s), CollegeScouting.StartingPoints);
            Assert.AreEqual(1, s.Net.Boxes.Select(b => b.Week).Distinct().Count(), "box scores start with the week the network was set up");
            CareerFlow.AdvanceUntil(s, CareerPhase.Scouting);
            Assert.That(s.Pro.DraftClass.Count, Is.InRange(250, 290));
            Assert.IsTrue(CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Take = 5 }).Rows.Count == 5);
        }

        [Test]
        public void AnOldSaveFromTheOffseasonStillTurnsTheCollegeOver()
        {
            CareerSave s = CollegeTestUtil.AtPhase(105, CareerPhase.Awards);
            s.Net = new CollegeNetState();
            CareerFlow.AdvanceUntil(s, CareerPhase.RookieSigning);
            Assert.AreEqual(s.Year + 1, s.College.Year);
            Assert.IsTrue(s.Net.Ready);
            CollegeTestUtil.AssertRostersFull(s);
        }

        [Test]
        public void EveryPhaseOfTheYearCanBeSavedAndLoadedWithTheCollegeWorldOn()
        {
            CareerSave s = CollegeTestUtil.Career(106);
            var seen = new HashSet<CareerPhase>();
            int guard = 0;
            while (guard++ < 120 && !(s.Year == s.StartYear + 1 && s.Phase == CareerPhase.Preseason))
            {
                if (seen.Add(s.Phase) || s.Phase == CareerPhase.RegularSeason && s.Season.Week == 9)
                {
                    CareerSave back = RoundTrip(s);
                    Assert.AreEqual(s.Phase, back.Phase);
                    Assert.AreEqual(CareerTestUtil.Fingerprint(s), CareerTestUtil.Fingerprint(back), s.Phase.ToString());
                    Assert.AreEqual(CollegeTestUtil.Fingerprint(s), CollegeTestUtil.Fingerprint(back), "college state in " + s.Phase);
                    CareerFlow.AutoResolveUser(back);
                    Assert.IsTrue(CareerFlow.Advance(back).Ok, "advance after loading in " + s.Phase);
                }
                CareerFlow.AutoResolveUser(s);
                Assert.IsTrue(CareerFlow.Advance(s).Ok);
            }
            Assert.AreEqual(12, seen.Count);
        }
    }
}
