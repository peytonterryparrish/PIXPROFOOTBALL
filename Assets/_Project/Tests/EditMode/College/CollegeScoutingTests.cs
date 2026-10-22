using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Tests
{
    /// <summary>College Network scouting: scouts, weekly points, fuzzy ratings that sharpen tier by tier, game visits, combine and pro days, private knowledge of the CPU teams.</summary>
    public class CollegeScoutingTests
    {
        private static ScoutingFilter Top(int n = 60) { return new ScoutingFilter { Sort = "public", Take = n }; }

        private static void Fund(CareerSave s, int points) { CollegeScouting.UserReport(s).PointsBudget += points; }

        // ------------------------------------------------------------ scouts
        [Test]
        public void EveryTeamHasThreeToFiveScoutsWithRegionAndPositionSkill()
        {
            CareerSave s = CollegeTestUtil.Career(41);
            for (int t = 0; t < 32; t++)
            {
                var scouts = CollegeScouting.TeamScouts(s, t);
                Assert.That(scouts.Count, Is.InRange(3, 5));
                Assert.AreEqual(1, scouts.Count(x => x.Director), "one director of scouting");
                foreach (Scout sc in scouts)
                {
                    Assert.That(sc.Skill, Is.InRange(38, 95));
                    Assert.That(sc.HomeRegion, Is.InRange(0, 7));
                    Assert.That(sc.FocusBucket, Is.InRange(-1, PositionBuckets.All.Length - 1));
                    Assert.IsNotEmpty(sc.Name);
                    Assert.AreEqual(t, sc.TeamId);
                }
            }
            Assert.AreEqual(s.Net.Scouts.Count, s.Net.Scouts.Select(x => x.Id).Distinct().Count(), "unique scout ids");
            Assert.IsTrue(s.Net.Scouts.Any(x => x.FocusBucket >= 0), "some scouts specialise in a position group");
            Assert.AreEqual(8, CollegeScouting.Market(s).Count);
            ScoutDepartmentView dept = CollegeScoutViews.Department(s);
            Assert.AreEqual(4, dept.Scouts.Count);
            Assert.Greater(dept.ManualPointsPerWeek, 3);
        }

        [Test]
        public void WeeklyPointsArriveAndAssignedScoutsSharpenTheirArea()
        {
            CareerSave s = CollegeTestUtil.InSeason(42, 0);
            int start = CollegeScouting.PointsRemaining(s);
            Assert.AreEqual(CollegeScouting.StartingPoints, start);
            int perWeek = CollegeScouting.PointsPerWeek(s);
            for (int i = 0; i < 4; i++) CollegeTestUtil.Step(s);
            Assert.AreEqual(start + 4 * perWeek, CollegeScouting.PointsRemaining(s), "the weekly points arrive (the scouts' own work is free)");
            ScoutingReport rep = CollegeScouting.UserReport(s);
            Assert.Greater(rep.Entries.Count, 20, "assigned scouts studied players");
            Assert.Greater(rep.Entries.Count(e => e.PointsSpent >= CollegeScouting.BandPoints), 3, "some players reached the grade-band tier by scouts alone");
            foreach (Scout sc in CollegeScouting.UserScouts(s).Where(x => !x.Director)) Assert.Greater(sc.PointsThisSeason, 10, sc.Name + " did work");
        }

        [Test]
        public void AssigningAScoutToASchoolFocusesHim()
        {
            CareerSave s = CollegeTestUtil.InSeason(43, 0);
            var top = CollegeScoutViews.ScoutingTable(s, Top(1)).Rows[0];
            int school = top.SchoolId;
            foreach (Scout sc in CollegeScouting.UserScouts(s).Where(x => !x.Director))
            {
                OpResult r = CollegeScouting.AssignScout(s, sc.Id, school, -1, -1);
                Assert.IsTrue(r.Ok, r.Message);
                Assert.AreEqual(school, sc.AssignSchoolId);
            }
            for (int i = 0; i < 3; i++) CollegeTestUtil.Step(s);
            ScoutingReport rep = CollegeScouting.UserReport(s);
            var ids = new HashSet<int>(s.College.GetSchool(school).Team.Roster.Select(p => p.Id));
            int atSchool = rep.Entries.Count(e => ids.Contains(e.ProspectId));
            Assert.GreaterOrEqual(atSchool, 5, "the whole department camped at one school");
            Assert.AreEqual(rep.Entries.Count, atSchool, "and watched nobody else");
            Assert.IsFalse(CollegeScouting.AssignScout(s, CollegeScouting.UserScouts(s).First(x => x.Director).Id, school, -1, -1).Ok, "the director does not scout");
            Assert.IsFalse(CollegeScouting.AssignScout(s, s.Net.Scouts.First(x => x.TeamId != s.UserTeamId).Id, school, -1, -1).Ok, "not another team's scout");
        }

        [Test]
        public void AFocusedScoutOnlyWatchesHisPositionGroup()
        {
            CareerSave s = CollegeTestUtil.InSeason(44, 0);
            Scout sc = CollegeScouting.UserScouts(s).First(x => !x.Director);
            foreach (Scout other in CollegeScouting.UserScouts(s).Where(x => !x.Director && x != sc)) CollegeScouting.AssignScout(s, other.Id, -1, 0, (int)PositionBucket.K);
            Assert.IsTrue(CollegeScouting.AssignScout(s, sc.Id, -1, sc.HomeRegion, (int)PositionBucket.QB).Ok);
            for (int i = 0; i < 4; i++) CollegeTestUtil.Step(s);
            ScoutingReport rep = CollegeScouting.UserReport(s);
            int qb = 0, other2 = 0;
            foreach (ScoutedProspect e in rep.Entries.Where(e => e.PointsSpent > 0))
            {
                Player p = s.College.Schools.SelectMany(x => x.Team.Roster).First(x => x.Id == e.ProspectId);
                PositionBucket b = PositionBuckets.Of(p.Position);
                if (b == PositionBucket.QB) qb++; else if (b != PositionBucket.K && b != PositionBucket.P) other2++;
            }
            Assert.Greater(qb, 0);
            Assert.AreEqual(0, other2, "only quarterbacks (and the kicker scouts' kickers) were studied");
        }

        // ------------------------------------------------------------ tiers
        [Test]
        public void RatingsUnlockTierByTierWithScoutingPoints()
        {
            CareerSave s = CollegeTestUtil.InSeason(45, 0);
            // a player none of the user's scouts looked at: the 40th public name
            var table = CollegeScoutViews.ScoutingTable(s, Top(80));
            ScoutingRow row = table.Rows.First(r => r.Tier == ScoutTier.Unknown && r.PublicRank > 20);
            int id = row.PlayerId;
            Player truth = s.College.Schools.SelectMany(x => x.Team.Roster).First(x => x.Id == id);
            Fund(s, 400);
            Assert.AreEqual(ScoutTier.Unknown, CollegeScouting.TierOf(s, id));
            Assert.IsTrue(row.Attributes.All(a => a.Locked && a.Text == "?"), "nothing is unlocked on an unscouted player");
            Assert.AreEqual("?", row.OverallText);

            Assert.IsTrue(CollegeScouting.ScoutPlayer(s, id, 3).Ok);
            Assert.AreEqual(ScoutTier.Glance, CollegeScouting.TierOf(s, id));
            ProspectCardView c = CollegeScoutViews.Card(s, id);
            Assert.IsTrue(c.Attributes.All(a => a.Locked), "three points are still only a glance");
            float sd0 = c.Uncertainty;

            Assert.IsTrue(CollegeScouting.ScoutPlayer(s, id, 3).Ok);
            Assert.AreEqual(ScoutTier.Band, CollegeScouting.TierOf(s, id), "6 points: grade bands");
            c = CollegeScoutViews.Card(s, id);
            Assert.IsTrue(c.Attributes.All(a => !a.Locked && Regex.IsMatch(a.Text, "^[A-F][+-]?$")), "bands are letter grades");
            Assert.Less(c.Uncertainty, sd0);
            Assert.That(c.OverallText, Does.Match("^[A-F][+-]?$"));

            Assert.IsTrue(CollegeScouting.ScoutNextTier(s, id).Ok);
            Assert.AreEqual(ScoutTier.Range, CollegeScouting.TierOf(s, id), "20 points: ranges");
            c = CollegeScoutViews.Card(s, id);
            Assert.IsTrue(c.Attributes.All(a => Regex.IsMatch(a.Text, "^\\d+-\\d+$") && a.Lo <= a.Hi), "ranges are lo-hi");
            Assert.That(c.OverallText, Does.Match("^\\d+-\\d+$"));
            float sdRange = c.Uncertainty;

            Assert.IsTrue(CollegeScouting.ScoutNextTier(s, id).Ok);
            Assert.AreEqual(ScoutTier.Exact, CollegeScouting.TierOf(s, id), "60 points: the exact numbers");
            c = CollegeScoutViews.Card(s, id);
            Assert.Less(c.Uncertainty, sdRange);
            foreach (AttrCell a in c.Attributes) { Assert.IsTrue(a.Exact); Assert.AreEqual(truth.Ratings.Get(a.Kind), a.Value, a.Name); Assert.AreEqual(a.Value.ToString(), a.Text); }
            Assert.AreEqual(truth.Overall.ToString(), c.OverallText);
            Assert.AreEqual(truth.Potential.ToString(), c.PotentialText);
            Assert.IsFalse(CollegeScouting.ScoutNextTier(s, id).Ok, "nothing left to learn");
            Assert.AreEqual(0, c.NextTierCost);
        }

        [Test]
        public void ScoutedRangesContainTheTruthAndSharpenWithPoints()
        {
            CareerSave s = CollegeTestUtil.InSeason(46, 0);
            Fund(s, 20000);
            var rows = CollegeScoutViews.ScoutingTable(s, Top(60)).Rows;
            int inside = 0, total = 0;
            float sdBand = 0, sdRange = 0, sdExact = 0;
            int n = 0;
            foreach (ScoutingRow r in rows.Take(40))
            {
                Player truth = s.College.Schools.SelectMany(x => x.Team.Roster).First(x => x.Id == r.PlayerId);
                CollegeScouting.ScoutPlayer(s, r.PlayerId, Math.Max(1, 20 - r.PointsSpent));
                ProspectCardView c = CollegeScoutViews.Card(s, r.PlayerId);
                Assert.AreEqual(ScoutTier.Range, c.Tier);
                foreach (AttrCell a in c.Attributes) { total++; if (truth.Ratings.Get(a.Kind) >= a.Lo && truth.Ratings.Get(a.Kind) <= a.Hi) inside++; }
                sdRange += c.Uncertainty; n++;
            }
            Assert.GreaterOrEqual((float)inside / total, 0.85f, "a range holds the true value in at least 85 percent of cases (" + inside + "/" + total + ")");
            Assert.Less(sdRange / n, 3.2f, "after 20 points the average uncertainty is below 3.2 rating points");
            Assert.AreEqual(ScoutingReport.SdForPoints(0), 9f, 0.01f);
            Assert.Less(ScoutingReport.SdForPoints(60), ScoutingReport.SdForPoints(20));
            sdBand = sdExact = 0;
        }

        [Test]
        public void SpendingIsLimitedByTheBudgetAndRefusesBadTargets()
        {
            CareerSave s = CollegeTestUtil.InSeason(47, 0);
            var rows = CollegeScoutViews.ScoutingTable(s, Top(30)).Rows;
            ScoutingReport rep = CollegeScouting.UserReport(s);
            int left = rep.PointsRemaining;
            OpResult r = CollegeScouting.ScoutPlayer(s, rows[0].PlayerId, left + 500);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(0, rep.PointsRemaining, "only what he had was spent");
            Assert.IsFalse(CollegeScouting.ScoutPlayer(s, rows[1].PlayerId, 5).Ok, "no points left");
            Fund(s, 50);
            Assert.IsFalse(CollegeScouting.ScoutPlayer(s, rows[1].PlayerId, 0).Ok, "at least one point");
            Assert.IsFalse(CollegeScouting.ScoutPlayer(s, -5, 5).Ok, "unknown player");
            Player freshman = s.College.Schools[0].Team.Roster.First(p => p.Class == ClassYear.Freshman);
            Assert.IsFalse(CollegeScouting.ScoutPlayer(s, freshman.Id, 5).Ok, "freshmen are not draft eligible yet");
        }

        // ------------------------------------------------------------ game visits
        [Test]
        public void AttendingAGameGivesBothTeamsPlayersAFreeLookOncePerWeek()
        {
            CareerSave s = CollegeTestUtil.InSeason(48, 2);
            ScoutingReport rep = CollegeScouting.UserReport(s);
            ScheduledGame g = CollegeNet.GamesThisWeek(s).First();
            int before = rep.PointsRemaining, entries = rep.Entries.Count;
            OpResult r = CollegeScouting.AttendGame(s, g.Id);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(before - CollegeScouting.GameCost, rep.PointsRemaining, "only the visit costs points; the looks are the bonus");
            var ids = new HashSet<int>(s.College.GetSchool(g.HomeId).Team.Roster.Concat(s.College.GetSchool(g.AwayId).Team.Roster).Select(p => p.Id));
            var looked = rep.Entries.Where(e => ids.Contains(e.ProspectId) && e.PointsSpent >= CollegeScouting.GameBonusPerPlayer).ToList();
            Assert.Greater(looked.Count, 4, "players of both teams got a look");
            var homeIds = new HashSet<int>(s.College.GetSchool(g.HomeId).Team.Roster.Select(p => p.Id));
            Assert.IsTrue(looked.Any(e => homeIds.Contains(e.ProspectId)) && looked.Any(e => !homeIds.Contains(e.ProspectId)), "both sides");
            Assert.IsFalse(CollegeScouting.AttendGame(s, CollegeNet.GamesThisWeek(s).Last().Id).Ok, "once a week");
            Assert.IsFalse(CollegeViews.Hub(s).CanAttendGame);
            CollegeTestUtil.Step(s);
            Assert.IsTrue(CollegeViews.Hub(s).CanAttendGame || CollegeScouting.PointsRemaining(s) < CollegeScouting.GameCost);
            Assert.IsFalse(CollegeScouting.AttendGame(s, g.Id).Ok, "that game is over");
        }

        // ------------------------------------------------------------ combine and pro days
        [Test]
        public void TheCombineRevealsThePhysicalsTheNumbersFollowTheRatings()
        {
            CareerSave s = CollegeTestUtil.AtPhase(49, CareerPhase.Scouting);
            var cls = s.Pro.DraftClass;
            Assert.AreEqual(CollegeScouting.PointsRemaining(s) > 400, true, "the season's points carried over plus the allowance");
            Prospect p = cls.First(x => x.Combine.Done && !x.Combine.ProDay && CollegeScouting.TierOf(s, x.Id) <= ScoutTier.Glance);
            ProspectCardView before = CollegeScoutViews.Card(s, p.Id);
            Assert.IsFalse(before.PhysicalsKnown);
            Assert.IsTrue(before.Attributes.All(a => a.Locked));
            int pts = CollegeScouting.PointsRemaining(s);
            OpResult r = CollegeScouting.AttendCombine(s);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(pts - CollegeScouting.CombineCost, CollegeScouting.PointsRemaining(s));
            Assert.IsFalse(CollegeScouting.AttendCombine(s).Ok, "once a year");
            ProspectCardView after = CollegeScoutViews.Card(s, p.Id);
            Assert.IsTrue(after.PhysicalsKnown);
            AttrCell speed = after.Attributes.FirstOrDefault(a => a.Kind == RatingKind.Speed);
            if (speed != null) { Assert.IsTrue(speed.Measured); Assert.AreEqual(p.Player.Ratings.Speed, speed.Value); }
            Assert.IsTrue(after.Attributes.Where(a => CollegeScouting.IsPhysical(a.Kind)).All(a => a.Measured && a.Exact));
            Assert.IsTrue(after.Attributes.Where(a => !CollegeScouting.IsPhysical(a.Kind)).All(a => a.Locked), "skills stay hidden");
            Assert.IsTrue(p.Combine.Done);
            StringAssert.Contains("40", p.Combine.Summary);
            // the stopwatch follows speed
            var done = cls.Where(x => x.Combine.Done).ToList();
            double mx = done.Average(x => x.Player.Ratings.Speed), my = done.Average(x => x.Combine.Forty);
            double cov = done.Sum(x => (x.Player.Ratings.Speed - mx) * (x.Combine.Forty - my));
            double corr = cov / Math.Sqrt(done.Sum(x => Math.Pow(x.Player.Ratings.Speed - mx, 2)) * done.Sum(x => Math.Pow(x.Combine.Forty - my, 2)));
            Assert.Less(corr, -0.9, "faster players run faster 40s");
            Assert.Greater(done.Average(x => x.Combine.BenchReps), 8);
            Assert.IsTrue(done.All(x => x.Combine.Forty > 4.0f && x.Combine.Forty < 5.9f), "plausible 40 times");
        }

        [Test]
        public void ProDaysMeasureSkippersAndPlayersNobodyInvited()
        {
            CareerSave s = CollegeTestUtil.AtPhase(50, CareerPhase.Scouting);
            var cls = s.Pro.DraftClass;
            Assert.IsTrue(cls.Any(x => x.Combine.Skipped), "some top prospects skip the combine");
            Assert.IsTrue(cls.Any(x => !x.Combine.Done && !x.Combine.Skipped), "the bottom of the class was not invited");
            Prospect missing = cls.Where(x => !x.Combine.Done).OrderBy(x => x.ConsensusRank).First();
            var sameSchool = cls.Where(x => x.SchoolId == missing.SchoolId && !x.Combine.Done).ToList();
            int pts = CollegeScouting.PointsRemaining(s);
            OpResult r = CollegeScouting.AttendProDay(s, missing.SchoolId);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(pts - CollegeScouting.ProDayCost, CollegeScouting.PointsRemaining(s));
            foreach (Prospect x in sameSchool) { Assert.IsTrue(x.Combine.Done && x.Combine.ProDay); Assert.IsTrue(CollegeScouting.PhysicalsKnown(s, x.Id)); }
            Assert.IsFalse(CollegeScouting.AttendProDay(s, missing.SchoolId).Ok, "once per school");
            Assert.IsFalse(CollegeScouting.AttendProDay(s, 9999).Ok);
        }

        [Test]
        public void OnceTheClassExistsOnlyItsMembersCanBeScouted()
        {
            CareerSave s = CollegeTestUtil.AtPhase(51, CareerPhase.Scouting);
            var inClass = new HashSet<int>(s.Pro.DraftClass.Select(p => p.Id));
            Player stayer = s.College.Schools.SelectMany(x => x.Team.Roster).First(p => p.Class == ClassYear.Junior && !p.IsRedshirting && !inClass.Contains(p.Id));
            OpResult r = CollegeScouting.ScoutPlayer(s, stayer.Id, 10);
            Assert.IsFalse(r.Ok, "a junior who stays in school is not in this draft");
            Assert.IsNull(CollegeScoutViews.Card(s, stayer.Id));
            Assert.IsFalse(CollegeScouting.Star(s, stayer.Id, true).Ok);
            Assert.IsTrue(CollegeScouting.ScoutPlayer(s, s.Pro.DraftClass[0].Id, 5).Ok);
        }

        [Test]
        public void CombineAndProDaysAreOnlyInTheScoutingPhase()
        {
            CareerSave s = CollegeTestUtil.InSeason(51, 1);
            Assert.IsFalse(CollegeScouting.AttendCombine(s).Ok);
            Assert.IsFalse(CollegeScouting.AttendProDay(s, 0).Ok);
        }

        // ------------------------------------------------------------ the CPU teams
        [Test]
        public void CpuTeamsScoutTooAndEachTeamKnowsDifferentThings()
        {
            CareerSave s = CollegeTestUtil.AtPhase(52, CareerPhase.Scouting);
            Assert.AreEqual(32, s.Pro.ScoutingReports.Count);
            for (int t = 0; t < 32; t++)
            {
                ScoutingReport r = s.Pro.ScoutingReports[t];
                Assert.AreEqual(t, r.TeamId);
                if (t != s.UserTeamId) Assert.GreaterOrEqual(r.Entries.Count(e => e.PointsSpent > 0), 100, "team " + t + " worked on the top of the board");
            }
            var budgets = s.Pro.ScoutingReports.Where(r => r.TeamId != s.UserTeamId).Select(r => r.PointsBudget).ToList();
            Assert.GreaterOrEqual(budgets.Distinct().Count(), 12, "budgets differ with the scouting departments");
            // private knowledge: two CPU teams hold different estimates for the same prospect
            Prospect top = CollegeTestUtil.Best(s);
            ScoutedProspect a = s.Pro.ScoutingReports[0].Get(top), b = s.Pro.ScoutingReports[1].Get(top);
            Assert.AreNotEqual(a.Estimated.Speed + a.Estimated.Awareness * 100 + a.Estimated.Strength * 10000, b.Estimated.Speed + b.Estimated.Awareness * 100 + b.Estimated.Strength * 10000, "private, noisy views");
            // the deeper they scouted a prospect, the closer they are
            var near = s.Pro.ScoutingReports.Where(r => r.TeamId != s.UserTeamId).Select(r => r.Get(top).Sd).Average();
            var far = s.Pro.ScoutingReports.Where(r => r.TeamId != s.UserTeamId).Select(r => r.Get(CollegeTestUtil.Best(s, x => x.ConsensusRank > 150)).Sd).Average();
            Assert.Less(near, far, "teams know the top of the board better than its middle");
        }

        [Test]
        public void ScoutQualityMovesTheCpuBudget()
        {
            CareerSave s = CollegeTestUtil.AtPhase(53, CareerPhase.Scouting);
            var rows = new List<KeyValuePair<float, int>>();
            for (int t = 0; t < 32; t++)
            {
                if (t == s.UserTeamId) continue;
                float q = CollegeScouting.TeamScouts(s, t).Sum(x => x.Director ? x.Skill - 60f : (x.WeeklyPoints - 7f) * 12f);
                Coach hc = s.Pro.Teams[t].HeadCoach;
                float basePts = s.Pro.Teams[t].ScoutingPoints + (hc.Scouting - 60) * 2;
                rows.Add(new KeyValuePair<float, int>(q + basePts, s.Pro.ScoutingReports[t].PointsBudget));
            }
            double mx = rows.Average(r => r.Key), my = rows.Average(r => r.Value);
            double corr = rows.Sum(r => (r.Key - mx) * (r.Value - my)) / Math.Sqrt(rows.Sum(r => Math.Pow(r.Key - mx, 2)) * rows.Sum(r => Math.Pow(r.Value - my, 2)));
            Assert.Greater(corr, 0.9, "better departments get bigger budgets");
        }

        [Test]
        public void WhatTheUserLearnedInTheSeasonCarriesIntoTheDraftClass()
        {
            CareerSave s = CollegeTestUtil.InSeason(54, 3);
            // a senior near the top of the public board
            var top = CollegeScoutViews.ScoutingTable(s, Top(40)).Rows.Where(r => r.ClassLabel == "Senior" && r.PublicRank <= 40).Take(2).ToList();
            Assert.AreEqual(2, top.Count);
            Fund(s, 300);
            CollegeScouting.ScoutPlayer(s, top[0].PlayerId, 25);
            CollegeScouting.ScoutPlayer(s, top[1].PlayerId, 70);
            CollegeScouting.Star(s, top[0].PlayerId, true);
            Assert.IsTrue(s.Net.HasUserReport);
            int points0 = CollegeScouting.UserReport(s).Entries.First(e => e.ProspectId == top[1].PlayerId).PointsSpent;
            CareerFlow.AdvanceUntil(s, CareerPhase.Scouting);
            Assert.IsFalse(s.Net.HasUserReport, "the knowledge moved into the pro league's scouting reports");
            ScoutingReport rep = s.Pro.ScoutingReports[s.UserTeamId];
            Assert.IsNotNull(CollegeScouting.KnownEntry(rep, top[1].PlayerId));
            Assert.GreaterOrEqual(CollegeScouting.KnownEntry(rep, top[1].PlayerId).PointsSpent, points0);
            Assert.AreEqual(ScoutTier.Exact, CollegeScouting.TierOf(s, top[1].PlayerId));
            Assert.GreaterOrEqual((int)CollegeScouting.TierOf(s, top[0].PlayerId), (int)ScoutTier.Range);
            var cardTop = CollegeScoutViews.Card(s, top[1].PlayerId);
            Assert.IsTrue(cardTop.InClass);
            Assert.IsTrue(cardTop.Attributes.All(a => a.Exact));
            Assert.IsTrue(CollegeScouting.FindEntry(s, top[0].PlayerId).Starred, "the star survived");
            Assert.IsTrue(rep.Entries.All(e => s.Pro.DraftClass.Any(p => p.Id == e.ProspectId)), "only class members keep a file");
        }

        // ------------------------------------------------------------ the department
        [Test]
        public void ScoutsCanBeHiredAndLetGoWithinTheLimits()
        {
            CareerSave s = CollegeTestUtil.Career(55);
            Assert.AreEqual(4, CollegeScouting.UserScouts(s).Count);
            Scout cand = CollegeScouting.Market(s)[0];
            OpResult r = CollegeScouting.HireScout(s, cand.Id);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(5, CollegeScouting.UserScouts(s).Count);
            Assert.AreEqual(7, CollegeScouting.Market(s).Count);
            Assert.IsFalse(CollegeScouting.HireScout(s, CollegeScouting.Market(s)[0].Id).Ok, "the department is full");
            Assert.IsFalse(CollegeScouting.HireScout(s, 123456).Ok);
            Scout fire = CollegeScouting.UserScouts(s).First(x => !x.Director);
            Assert.IsTrue(CollegeScouting.FireScout(s, fire.Id).Ok);
            Assert.IsTrue(CollegeScouting.FireScout(s, CollegeScouting.UserScouts(s).First(x => !x.Director).Id).Ok);
            Assert.AreEqual(3, CollegeScouting.UserScouts(s).Count);
            Assert.IsFalse(CollegeScouting.FireScout(s, CollegeScouting.UserScouts(s).First(x => !x.Director).Id).Ok, "at least three scouts");
            Assert.IsFalse(CollegeScouting.FireScout(s, CollegeScouting.UserScouts(s).First(x => x.Director).Id).Ok, "the director stays");
            Assert.AreEqual(3, CollegeScouting.TeamScouts(s, s.UserTeamId).Count);
        }

        [Test]
        [Category("Slow")]   // multi-season: excluded from the release run (-testCategory "!Slow")
        public void ScoutsAgeAndTheMarketRefreshesEveryYear()
        {
            CareerSave s = CollegeTestUtil.Career(56);
            var before = s.Net.Scouts.ToDictionary(x => x.Id, x => x.Age);
            var marketBefore = CollegeScouting.Market(s).Select(x => x.Id).ToList();
            CareerFlow.SimToNextSeason(s);
            int aged = 0;
            foreach (Scout sc in s.Net.Scouts) { int a; if (before.TryGetValue(sc.Id, out a)) { Assert.AreEqual(a + 1, sc.Age); aged++; } }
            Assert.Greater(aged, 100, "most scouts are still around");
            for (int t = 0; t < 32; t++) Assert.That(CollegeScouting.TeamScouts(s, t).Count, Is.InRange(3, 5));
            Assert.AreEqual(8, CollegeScouting.Market(s).Count);
            Assert.AreNotEqual(marketBefore, CollegeScouting.Market(s).Select(x => x.Id).ToList());
            Assert.AreEqual(CollegeScouting.StartingPoints, CollegeScouting.PointsRemaining(s), "a new scouting season starts with the base points");
        }

        [Test]
        public void ScoutingIsDeterministic()
        {
            long a = Play(57), b = Play(57), c = Play(58);
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(a, c);
        }

        private static long Play(int seed)
        {
            CareerSave s = CollegeTestUtil.InSeason(seed, 2);
            var rows = CollegeScoutViews.ScoutingTable(s, Top(10)).Rows;
            Fund(s, 100);
            CollegeScouting.ScoutPlayer(s, rows[3].PlayerId, 30);
            CollegeScouting.AttendGame(s, CollegeNet.GamesThisWeek(s).First().Id);
            CollegeTestUtil.Step(s); CollegeTestUtil.Step(s);
            return CollegeTestUtil.Fingerprint(s);
        }
    }
}
