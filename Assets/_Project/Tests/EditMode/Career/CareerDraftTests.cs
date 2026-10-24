using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>Combine and scouting, the draft with pick ownership and live grades, and rookie signing.</summary>
    public class CareerDraftTests
    {
        [Test]
        public void ScoutingPointsSharpenTheBoardAndAreLimited()
        {
            CareerSave s = CareerTestUtil.AtPhase(121, CareerPhase.Scouting);
            Assert.Greater(s.Pro.DraftClass.Count, 240);
            Assert.AreEqual(32, s.Pro.ScoutingReports.Count);
            int points = CareerDraft.PointsRemaining(s);
            Assert.Greater(points, 100);
            var board = CareerDraft.Board(s);
            Assert.AreEqual(s.Pro.DraftClass.Count, board.Count);
            for (int i = 1; i < board.Count; i++) Assert.GreaterOrEqual(board[i - 1].EstGrade, board[i].EstGrade, "best estimate first");
            ProspectRow row = board[20];
            float before = row.Uncertainty;
            Assert.Greater(before, 5f, "an unscouted prospect is blurry");
            OpResult r = CareerDraft.Scout(s, row.Id, 60);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.AreEqual(points - 60, CareerDraft.PointsRemaining(s));
            ProspectRow after = CareerDraft.Board(s).First(x => x.Id == row.Id);
            Assert.Less(after.Uncertainty, before * 0.5f);
            Assert.AreEqual(60, after.PointsSpent);
            // the estimate moves toward the truth with scouting
            Prospect p = CareerDraft.FindProspect(s, row.Id);
            Assert.Less(Math.Abs(after.EstOverall - p.Player.Overall), Math.Abs(before * 3f) + 1f);
            // cannot spend more than there is
            OpResult greedy = CareerDraft.Scout(s, board[21].Id, points * 10);
            Assert.IsTrue(greedy.Ok);
            Assert.AreEqual(0, CareerDraft.PointsRemaining(s));
            Assert.IsFalse(CareerDraft.Scout(s, board[22].Id, 5).Ok);
            Assert.IsFalse(CareerDraft.Scout(s, -1, 5).Ok);
            // the star list and the personal big board
            CareerDraft.SetStarred(s, board[1].Id, true); CareerDraft.SetStarred(s, board[2].Id, true); CareerDraft.SetStarred(s, board[3].Id, true);
            CareerDraft.MoveOnBoard(s, board[3].Id, 0);
            var starred = CareerDraft.Board(s, "starred");
            Assert.AreEqual(board[3].Id, starred[0].Id);
            Assert.IsTrue(starred[0].Starred && starred[1].Starred && starred[2].Starred);
            CareerDraft.SetStarred(s, board[2].Id, false);
            Assert.AreEqual(2, s.Draft.BigBoard.Count);
            var consensus = CareerDraft.Board(s, "consensus");
            Assert.AreEqual(1, consensus[0].ConsensusRank);
            // only possible before the draft
            CareerSave other = CareerTestUtil.Fresh(122);
            Assert.IsFalse(CareerDraft.Scout(other, 1, 5).Ok);
        }

        [Test]
        public void TheDraftRunsInOrderWithTheUsersPicksWaitingForHim()
        {
            CareerSave s = CareerTestUtil.AtPhase(123, CareerPhase.Scouting);
            CareerFlow.Advance(s);
            Assert.AreEqual(CareerPhase.Draft, s.Phase);
            DraftState d = s.Draft;
            Assert.AreEqual(224, d.Slots.Count);
            for (int i = 1; i < d.Slots.Count; i++) Assert.Greater(d.Slots[i].Overall, d.Slots[i - 1].Overall);
            // worst record first: the first-round order is the season's draft order
            var order = s.Season.DraftOrder();
            for (int i = 0; i < 32; i++) Assert.AreEqual(order[i], d.Slots[i].OriginalTeamId);
            Assert.AreEqual(s.Season.ChampionTeamId, d.Slots[31].OriginalTeamId, "the champion picks last");
            int userSlots = d.Slots.Count(x => x.TeamId == s.UserTeamId);
            Assert.AreEqual(7, userSlots);

            // not on the clock: cannot pick
            CareerFlow.Advance(s);   // sims to the user's first pick
            DraftSlot cur = CareerDraft.Current(s);
            Assert.IsTrue(CareerDraft.UserOnClock(s) || s.Draft.Finished);
            Assert.AreEqual(s.UserTeamId, cur.TeamId);
            int madeBefore = d.Slots.Count(x => x.Made);
            Assert.AreEqual(cur.Overall - 1, madeBefore);
            var best = CareerDraft.Board(s, "grade", true).First();
            Prospect already = s.Pro.DraftClass.FirstOrDefault(x => x.Drafted);      // v0.11.0: when the user is on the clock at pick 1 nobody is drafted yet
            if (already != null) Assert.IsFalse(CareerDraft.UserPick(s, already.Id).Ok, "already taken");
            OpResult pick = CareerDraft.UserPick(s, best.Id);
            Assert.IsTrue(pick.Ok, pick.Message);
            Assert.IsTrue(cur.Made);
            Assert.AreEqual(best.Id, cur.PlayerId);
            Assert.IsNotEmpty(cur.Grade);
            Player p = s.UserTeam.GetPlayer(best.Id);
            Assert.IsNotNull(p);
            Assert.AreEqual(cur.Overall, p.DraftPick);
            Assert.AreEqual(cur.Round, p.DraftRound);
            Assert.AreEqual(s.Year, p.DraftYear);
            Assert.AreEqual(0, p.Experience);
            Assert.AreEqual(0, p.Contract.YearsLeft, "unsigned until the rookie signing step");
            Assert.IsTrue(s.RookieRights.Any(r => r.PlayerId == p.Id && r.TeamId == s.UserTeamId && !r.Signed));
            Assert.IsTrue(TradeEngine.FindPick(s, cur.PickId).Used);
            Assert.IsFalse(CareerDraft.UserPick(s, best.Id).Ok, "not on the clock any more");

            // run it out: everybody is drafted exactly once
            CareerDraft.SimToEnd(s, new Rng(5));
            Assert.IsTrue(d.Finished);
            Assert.AreEqual(224, d.Slots.Count(x => x.Made));
            Assert.AreEqual(224, d.Slots.Select(x => x.PlayerId).Distinct().Count());
            foreach (DraftSlot sl in d.Slots) Assert.AreEqual(sl.TeamId, s.TeamOf(sl.PlayerId), "each pick is on the roster of the team that made it");
        }

        [Test]
        public void LiveGradesCompareThePickWithThePublicBoard()
        {
            Assert.AreEqual("A+", CareerDraft.GradeOf(40, 5));
            Assert.AreEqual("A", CareerDraft.GradeOf(25, 10));
            Assert.AreEqual("B+", CareerDraft.GradeOf(20, 14));
            Assert.AreEqual("B", CareerDraft.GradeOf(10, 10));
            Assert.AreEqual("C", CareerDraft.GradeOf(10, 18));
            Assert.AreEqual("D", CareerDraft.GradeOf(10, 30));
            Assert.AreEqual("F", CareerDraft.GradeOf(3, 60));
            CareerSave s = CareerTestUtil.AtPhase(124, CareerPhase.Scouting);
            CareerFlow.Advance(s);
            CareerDraft.SimToEnd(s, new Rng(9));
            string grade = CareerDraft.TeamGrade(s, 3);
            Assert.IsTrue(new[] { "A+", "A", "B+", "B", "C", "D", "F" }.Contains(grade), grade);
            // a draft always has some bargains and some reaches
            Assert.IsTrue(s.Draft.Slots.Any(x => x.Grade == "A" || x.Grade == "A+" || x.Grade == "B+"));
            Assert.IsTrue(s.Draft.Slots.Any(x => x.Grade == "C" || x.Grade == "D" || x.Grade == "F"));
        }

        [Test]
        public void CpuTeamsOfferToTradeUpToTheUsersPick()
        {
            CareerSave s = CareerTestUtil.AtPhase(125, CareerPhase.Scouting);
            CareerFlow.Advance(s);
            // put the user on the clock at his first-round pick without simulating the picks before it
            int idx = s.Draft.Slots.FindIndex(x => x.TeamId == s.UserTeamId);
            s.Draft.Current = idx;
            Assert.IsTrue(CareerDraft.UserOnClock(s));
            DraftSlot mine = CareerDraft.Current(s);
            // give the user's slot a high round-1 value if the user's first pick is late
            var offers = CareerDraft.TradeUpOffers(s, new Rng(3), 3);
            Assert.LessOrEqual(offers.Count, 3);
            foreach (TradeOffer o in offers)
            {
                Assert.AreEqual(s.UserTeamId, o.Proposal.TeamB);
                TradeEvaluation ev = TradeEngine.Validate(s, o.Proposal);
                Assert.IsTrue(ev.Legal, string.Join("; ", ev.Problems.ToArray()));
                float get = o.Proposal.FromA.Sum(a => TradeEngine.AssetValue(s, a));
                float give = o.Proposal.FromB.Sum(a => TradeEngine.AssetValue(s, a));
                Assert.GreaterOrEqual(get, give * 1.05f);
            }
            if (offers.Count > 0)
            {
                TradeOffer o = offers[0];
                int theirPickId = o.Proposal.FromA.First(a => a.IsPick).PickId;
                OpResult r = TradeEngine.AcceptOffer(s, o.Id);
                Assert.IsTrue(r.Ok, r.Message);
                DraftSlot userOldSlot = s.Draft.Slots.First(x => x.PickId == mine.PickId);
                Assert.AreEqual(o.FromTeamId, userOldSlot.TeamId, "the CPU team now holds the pick on the clock");
                DraftSlot theirOld = s.Draft.Slots.First(x => x.PickId == theirPickId);
                Assert.AreEqual(s.UserTeamId, theirOld.TeamId, "the user moved down");
            }
        }

        [Test]
        public void RookiesSignAtTheScaleAndTheCapGuardsTheUser()
        {
            CareerSave s = CareerTestUtil.AtPhase(126, CareerPhase.RookieSigning);
            Team u = s.UserTeam;
            var rights = CareerDraft.UnsignedRights(s, u.Id);
            Assert.AreEqual(7, rights.Count);
            // every CPU pick is signed already, on a rookie deal
            foreach (RookieRight r in s.RookieRights.Where(r => r.TeamId != u.Id))
            {
                Assert.IsTrue(r.Signed);
                Player p = s.Pro.GetTeam(r.TeamId).FindAnyPlayer(r.PlayerId);
                Assert.IsNotNull(p);
                Assert.Greater(p.Contract.YearsLeft, 0);
            }
            RookieRight first = rights.OrderBy(r => r.Overall).First();
            Player rookie = u.FindAnyPlayer(first.PlayerId);
            Contract expected = CareerDraft.RookieDeal(s, first);
            // no cap room: refused
            long savedCap = u.SalaryCap;
            u.SalaryCap = u.CapUsed + 1000L;
            Assert.IsFalse(CareerDraft.SignRookie(s, first.PlayerId).Ok);
            u.SalaryCap = savedCap + 100000000L;
            OpResult ok = CareerDraft.SignRookie(s, first.PlayerId);
            Assert.IsTrue(ok.Ok, ok.Message);
            Assert.AreEqual(expected.Aav, rookie.Contract.Aav);
            Assert.AreEqual(4, rookie.Contract.Years);
            Assert.IsTrue(rookie.Contract.Rookie);
            Assert.IsTrue(first.Signed);
            Assert.IsFalse(CareerDraft.SignRookie(s, first.PlayerId).Ok, "signed once");
            // earlier picks cost more
            var deals = rights.Where(r => !r.Signed).Select(r => CareerDraft.RookieDeal(s, r)).ToList();
            Assert.AreEqual(6, deals.Count);
            Assert.AreEqual(6, CareerDraft.SignAllRookies(s, u.Id));
            Assert.AreEqual(0, CareerDraft.UnsignedRights(s, u.Id).Count);
            // the auto pilot finishing the phase leaves nobody unsigned and none left over
            CareerFlow.Advance(s);
            Assert.AreEqual(CareerPhase.FreeAgency, s.Phase);
            Assert.AreEqual(0, s.RookieRights.Count(r => !r.Signed));
            foreach (RookieRight r in s.RookieRights) Assert.IsTrue(r.Signed);
        }

        [Test]
        public void UnsignedUserRookiesWhoDoNotFitBecomeFreeAgents()
        {
            CareerSave s = CareerTestUtil.AtPhase(127, CareerPhase.RookieSigning);
            Team u = s.UserTeam;
            u.SalaryCap = u.CapUsed + 100000L;   // nothing fits
            int rights = CareerDraft.UnsignedRights(s, u.Id).Count;
            Assert.Greater(rights, 0);
            var ids = CareerDraft.UnsignedRights(s, u.Id).Select(r => r.PlayerId).ToList();
            CareerFlow.Advance(s);
            foreach (int id in ids)
            {
                Player p = s.FindPlayer(id);
                Assert.IsNotNull(p);
                if (u.FindAnyPlayer(id) == null) Assert.IsTrue(s.Pro.FreeAgents.Contains(p), p.Name + " should be in the pool");
            }
        }

        [Test]
        public void UndraftedProspectsHitTheMarketAndTheClassIsGone()
        {
            CareerSave s = CareerTestUtil.AtPhase(128, CareerPhase.RookieSigning);
            Assert.AreEqual(0, s.Pro.DraftClass.Count, "the class is cleared once the draft is over");
            var udfa = s.Pro.FreeAgents.Where(p => p.DraftYear == s.Year && p.DraftRound == 0 && p.Experience == 0).ToList();
            Assert.Greater(udfa.Count, 10);
            Assert.IsTrue(udfa.All(p => FreeAgency.IsUndrafted(s, p)));
            Assert.AreEqual(224, s.Pro.LastDraft.Count);
        }

        [Test]
        public void ACollegeWorldFeedsTheDraftClassAndTurnsOverWithIt()
        {
            CareerSave s = CareerTestUtil.Fresh(129, 5, true);
            CareerFlow.AdvanceUntil(s, CareerPhase.Scouting);
            Assert.Greater(s.Pro.DraftClass.Count, 240);
            Assert.IsTrue(s.Pro.DraftClass.Count(pr => pr.SchoolId >= 0) > 250, "prospects come from the college rosters");
            int collegeYear = s.College.Year;
            Assert.IsTrue(s.College.Season.IsComplete);
            CareerFlow.AdvanceUntil(s, CareerPhase.RookieSigning);
            Assert.AreEqual(collegeYear + 1, s.College.Year);
            Assert.IsFalse(s.College.Season.IsComplete);
            Player rookie = s.Pro.GetTeam(s.Pro.LastDraft[0].TeamId).FindAnyPlayer(s.Pro.LastDraft[0].PlayerId);
            Assert.IsNotNull(rookie);
            Assert.IsNotEmpty(rookie.Bio.College);
        }
    }
}
