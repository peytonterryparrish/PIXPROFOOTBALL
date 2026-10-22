using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Tests
{
    /// <summary>Stars, the ordered personal big board, notes, and the projected round from the public consensus against the user's own grades.</summary>
    public class CollegeBoardTests
    {
        private static List<int> Seniors(CareerSave s, int n)
        {
            return CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Sort = "public", Take = 80 }).Rows.Where(r => r.ClassLabel == "Senior").Take(n).Select(r => r.PlayerId).ToList();
        }

        [Test]
        public void StarsAndTheOrderedBoardWorkSeparately()
        {
            CareerSave s = CollegeTestUtil.InSeason(71, 1);
            var ids = Seniors(s, 5);
            Assert.IsTrue(CollegeScouting.Star(s, ids[0], true).Ok);
            Assert.IsTrue(CollegeScouting.Star(s, ids[1], true).Ok);
            Assert.AreEqual(0, CollegeScouting.BoardIds(s).Count, "a star alone does not rank him");
            Assert.AreEqual(2, CollegeScoutViews.Starred(s).Count);
            CollegeScouting.AddToBoard(s, ids[2]);
            CollegeScouting.AddToBoard(s, ids[3]);
            CollegeScouting.AddToBoard(s, ids[4]);
            Assert.AreEqual(new List<int> { ids[2], ids[3], ids[4] }, CollegeScouting.BoardIds(s));
            Assert.IsTrue(CollegeScouting.FindEntry(s, ids[2]).Starred, "board entries are starred");
            Assert.IsTrue(CollegeScouting.MoveOnBoard(s, ids[4], 0).Ok);
            Assert.AreEqual(new List<int> { ids[4], ids[2], ids[3] }, CollegeScouting.BoardIds(s));
            Assert.IsTrue(CollegeScouting.AddToBoard(s, ids[0], 1).Ok, "a starred player joins the board at a chosen place");
            Assert.AreEqual(new List<int> { ids[4], ids[0], ids[2], ids[3] }, CollegeScouting.BoardIds(s));
            Assert.AreEqual(1, CollegeScoutViews.Starred(s).Count, "the starred list only has the ones not on the board");
            Assert.IsTrue(CollegeScouting.RemoveFromBoard(s, ids[0]).Ok);
            Assert.AreEqual(new List<int> { ids[4], ids[2], ids[3] }, CollegeScouting.BoardIds(s));
            Assert.IsTrue(CollegeScouting.FindEntry(s, ids[0]).Starred, "still starred");
            Assert.IsTrue(CollegeScouting.Star(s, ids[0], false).Ok);
            Assert.IsNull(CollegeScouting.FindEntry(s, ids[0]), "an entry with nothing left is dropped");
            Assert.IsFalse(CollegeScouting.RemoveFromBoard(s, ids[0]).Ok);
            Assert.IsFalse(CollegeScouting.AddToBoard(s, -3).Ok);
            var rows = CollegeScoutViews.BigBoard(s);
            Assert.AreEqual(new List<int> { ids[4], ids[2], ids[3] }, rows.Select(r => r.PlayerId).ToList());
            Assert.AreEqual(new List<int> { 1, 2, 3 }, rows.Select(r => r.Order).ToList());
            Assert.IsTrue(rows.All(r => r.OnBoard && r.Starred));
            Assert.AreEqual(3, CollegeViews.Hub(s).BoardCount);
        }

        [Test]
        public void NotesAndTagsAreKeptAndShowOnTheCardAndTheBoard()
        {
            CareerSave s = CollegeTestUtil.InSeason(72, 1);
            int id = Seniors(s, 1)[0];
            Assert.IsTrue(CollegeScouting.SetNote(s, id, "Elite athlete, check the film vs. top ten teams", "Must have").Ok);
            ProspectCardView c = CollegeScoutViews.Card(s, id);
            Assert.AreEqual("Elite athlete, check the film vs. top ten teams", c.Note);
            Assert.AreEqual("Must have", c.Tag);
            Assert.IsFalse(c.OnBoard);
            CollegeScouting.AddToBoard(s, id);
            Assert.AreEqual("Must have", CollegeScoutViews.BigBoard(s)[0].Tag);
            CollegeScouting.SetNote(s, id, new string('x', 900));
            Assert.AreEqual(400, CollegeScouting.FindEntry(s, id).Note.Length, "notes are capped");
            Assert.AreEqual("Must have", CollegeScouting.FindEntry(s, id).Tag, "a null tag leaves the tag alone");
            CollegeScouting.SetNote(s, id, "", "");
            CollegeScouting.RemoveFromBoard(s, id); CollegeScouting.Star(s, id, false);
            Assert.IsNull(CollegeScouting.FindEntry(s, id));
        }

        [Test]
        public void TheBoardSurvivesFromTheSeasonIntoTheDraftAndDropsPlayersWhoStayInSchool()
        {
            CareerSave s = CollegeTestUtil.InSeason(73, 2);
            var seniors = Seniors(s, 3);
            // a junior who will not declare: the lowest public junior of the pool
            var pool = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Sort = "public", Take = 4000 }).Rows;
            int junior = pool.Last(r => r.ClassLabel == "Junior").PlayerId;
            foreach (int id in seniors) CollegeScouting.AddToBoard(s, id);
            CollegeScouting.AddToBoard(s, junior);
            CollegeScouting.Star(s, seniors[0], true);
            CareerFlow.AdvanceUntil(s, CareerPhase.Scouting);
            Assert.IsFalse(s.Pro.DraftClass.Any(p => p.Id == junior));
            Assert.IsNull(CollegeScouting.FindEntry(s, junior), "he stayed in school: off the board");
            var board = CollegeScouting.BoardIds(s);
            Assert.AreEqual(seniors.Where(id => s.Pro.DraftClass.Any(p => p.Id == id)).ToList(), board, "the others keep their order");
            Assert.AreEqual(board, s.Draft.BigBoard, "the draft state mirrors the board");
            Assert.IsTrue(CollegeScoutViews.BigBoard(s).All(r => r.Order > 0));
            // then through the draft: the board is part of the draft house list
            CareerFlow.Advance(s);
            Assert.AreEqual(CareerPhase.Draft, s.Phase);
            Assert.AreEqual(board, s.Draft.BigBoard);
        }

        [Test]
        public void TheOldDraftHouseApiStarsAndOrdersThroughTheSameBoard()
        {
            CareerSave s = CollegeTestUtil.AtPhase(74, CareerPhase.Scouting);
            var ids = s.Pro.DraftClass.Take(4).Select(p => p.Id).ToList();
            foreach (int id in ids) CareerDraft.SetStarred(s, id, true);
            Assert.AreEqual(ids, s.Draft.BigBoard);
            Assert.AreEqual(ids, CollegeScouting.BoardIds(s));
            CareerDraft.MoveOnBoard(s, ids[3], 0);
            Assert.AreEqual(ids[3], s.Draft.BigBoard[0]);
            CareerDraft.SetStarred(s, ids[1], false);
            Assert.IsFalse(s.Draft.BigBoard.Contains(ids[1]));
            var rows = CareerDraft.Board(s, "starred");
            Assert.IsTrue(rows.Take(3).All(r => r.Starred));
            Assert.AreEqual(ids[3], rows[0].Id);
        }

        [Test]
        public void ProjectedRoundComesFromThePublicBoardAndFromTheUsersOwnGrades()
        {
            CareerSave s = CollegeTestUtil.AtPhase(75, CareerPhase.Scouting);
            var cls = s.Pro.DraftClass;
            Assert.AreEqual("Round 1 (top 10)", CollegeScoutViews.RoundText(1));
            Assert.AreEqual("Round 1", CollegeScoutViews.RoundText(32));
            Assert.AreEqual("Round 2", CollegeScoutViews.RoundText(33));
            Assert.AreEqual("Round 7", CollegeScoutViews.RoundText(224));
            Assert.AreEqual("Undrafted", CollegeScoutViews.RoundText(225));
            var rows = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Sort = "public", Take = 300 }).Rows;
            Assert.AreEqual(cls.Count, rows.Count);
            for (int i = 0; i < rows.Count; i++) { Assert.AreEqual(i + 1, rows[i].PublicRank); Assert.AreEqual(CollegeScoutViews.RoundText(i + 1), rows[i].PublicRound); }
            // before any scouting the user's board is the public board plus his glance noise; after spending points his order moves
            ScoutingReport rep = CollegeScouting.UserReport(s);
            rep.PointsBudget += 5000;
            var mid = rows.Skip(40).Take(20).ToList();
            foreach (ScoutingRow r in mid) CollegeScouting.ScoutPlayer(s, r.PlayerId, 70);
            var after = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Sort = "user", Take = 300 }).Rows;
            int moved = mid.Count(r => after.First(x => x.PlayerId == r.PlayerId).UserRank != r.UserRank);
            Assert.Greater(moved, 10, "scouting changes where the user ranks players");
            Assert.IsTrue(after.Zip(after.Skip(1), (a, b) => a.UserRank < b.UserRank).All(x => x), "sorted by the user's own rank");
            var board = CollegeScoutViews.BigBoard(s);
            Assert.AreEqual(0, board.Count);
            CollegeScouting.AddToBoard(s, mid[0].PlayerId);
            var br = CollegeScoutViews.BigBoard(s)[0];
            Assert.AreEqual(br.PublicRank - br.UserRank, br.Delta);
            Assert.AreEqual(CollegeScoutViews.RoundText(br.UserRank), br.UserRound);
            Assert.AreEqual(CollegeScoutViews.RoundText(br.PublicRank), br.PublicRound);
        }

        [Test]
        public void TheScoutingTableFiltersByPositionSchoolStarsAndName()
        {
            CareerSave s = CollegeTestUtil.InSeason(76, 2);
            var qbs = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Bucket = (int)PositionBucket.QB, Sort = "public", Take = 30 });
            Assert.Greater(qbs.Total, 50);
            Assert.IsTrue(qbs.Rows.All(r => r.Pos == Position.QB));
            Assert.IsTrue(qbs.Rows.Zip(qbs.Rows.Skip(1), (a, b) => a.PublicRank < b.PublicRank).All(x => x));
            var one = qbs.Rows[0];
            var school = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { SchoolId = one.SchoolId, Take = 50 });
            Assert.IsTrue(school.Rows.All(r => r.SchoolId == one.SchoolId));
            Assert.IsTrue(school.Rows.Any(r => r.PlayerId == one.PlayerId));
            var search = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Search = one.Name.Split(' ')[1], Take = 20 });
            Assert.IsTrue(search.Rows.Any(r => r.PlayerId == one.PlayerId));
            CollegeScouting.Star(s, one.PlayerId, true);
            var stars = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { OnlyStarred = true });
            Assert.AreEqual(1, stars.Rows.Count);
            Assert.IsTrue(stars.Rows[0].Starred);
            var page2 = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Sort = "public", Skip = 10, Take = 10 });
            Assert.AreEqual(11, page2.Rows[0].Rank);
            Assert.AreEqual(11, page2.Rows[0].PublicRank);
            var region = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Region = 2, Take = 50 });
            Assert.IsTrue(region.Rows.All(r => (int)s.College.GetSchool(r.SchoolId).Region == 2));
            var scoutedOnly = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Sort = "points", Take = 5 });
            Assert.GreaterOrEqual(scoutedOnly.Rows[0].PointsSpent, scoutedOnly.Rows[4].PointsSpent);
        }

        [Test]
        public void NeedsAndPointsShowOnTheScoutingTable()
        {
            CareerSave s = CollegeTestUtil.AtPhase(77, CareerPhase.Scouting);
            var t = CollegeScoutViews.ScoutingTable(s, new ScoutingFilter { Sort = "public", Take = 260 });
            Assert.AreEqual(8, t.Needs.Count);
            Assert.AreEqual(CollegeScouting.PointsRemaining(s), t.PointsRemaining);
            Assert.IsTrue(t.ClassExists);
            Assert.IsTrue(t.CombineAvailable);
            Assert.IsFalse(t.CombineAttended);
            Assert.AreEqual(CollegeScouting.ExactPoints, t.ExactCost);
            Pos(t, Position.QB);
            Assert.IsTrue(t.Rows.Any(r => r.Need == "High" || r.Need == "Medium"), "positions of need are flagged");
            Assert.IsTrue(t.Rows.All(r => r.Attributes.Count >= 3 && r.Attributes.Count <= 6), "up to six attribute columns (kickers have fewer)");
            Assert.IsTrue(t.Rows.Where(r => r.Pos != Position.K && r.Pos != Position.P).All(r => r.Attributes.Count == 6));
            var top = t.Needs[0];
            Assert.IsTrue(t.Rows.Where(r => r.Pos == top.Pos).All(r => r.Need != ""), "the biggest need is flagged on every prospect of that position");
        }

        private static void Pos(ScoutingTableView t, Position p) { Assert.IsTrue(t.Rows.Any(r => r.Pos == p)); }
    }
}
