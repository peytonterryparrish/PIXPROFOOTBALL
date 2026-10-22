using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>Free agency: the pool, multi-team bidding over days, the player's decision and CPU shopping.</summary>
    public class CareerFreeAgencyTests
    {
        private static CareerSave AtFreeAgency(int seed)
        {
            CareerSave s = CareerTestUtil.AtPhase(seed, CareerPhase.FreeAgency);
            Assert.AreEqual(0, s.FaDay);
            return s;
        }

        [Test]
        public void FreeAgencyOpensWithADeepPoolAndRunsForConfiguredDays()
        {
            CareerSave s = AtFreeAgency(61);
            Assert.Greater(s.Pro.FreeAgents.Count, 150);
            Assert.AreEqual(14, s.Settings.FreeAgencyDays);
            Assert.AreEqual("Free agency day 0 of 14", CareerText.PhaseLabel(s));
            int signed = 0;
            for (int d = 0; d < 14; d++)
            {
                StepResult r = CareerFlow.Advance(s);
                Assert.IsTrue(r.Ok);
                Assert.IsNotNull(r.FreeAgencyDay);
                Assert.AreEqual(d + 1, r.FreeAgencyDay.Day);
                signed += r.FreeAgencyDay.Signings.Count;
                foreach (Team t in s.Pro.Teams)
                {
                    Assert.LessOrEqual(t.Roster.Count, RosterRules.OffseasonLimit, t.Nickname);
                }
            }
            Assert.Greater(signed, 40);
            Assert.IsTrue(FreeAgency.IsDone(s));
            Assert.AreEqual(CareerPhase.FreeAgency, s.Phase);
            // the next advance leaves free agency for camp
            Assert.IsTrue(CareerFlow.Advance(s).Ok);
            Assert.AreEqual(CareerPhase.TrainingCamp, s.Phase);
            foreach (Team t in s.Pro.Teams) Assert.LessOrEqual(t.CapUsed, t.SalaryCap, t.Nickname + " is over the cap at the end of free agency");
            Assert.AreEqual(signed, s.FaLog.Count(f => f.Year == s.Year));
        }

        [Test]
        public void PlayersWeighMoneyContenderRoleAndMood()
        {
            CareerSave s = AtFreeAgency(62);
            Player p = FreeAgency.Pool(s).First(x => x.Overall >= 72);
            Team best = s.Pro.Teams.OrderByDescending(t => t.TeamOverall()).First();
            Team worst = s.Pro.Teams.OrderBy(t => t.TeamOverall()).First();
            ContractAsk ask = FreeAgency.Ask(s, p, 0);

            OfferEvaluation low = FreeAgency.Evaluate(s, p, best.Id, (long)(ask.Aav * 0.8), ask.Years, ask.BonusPct, 0);
            OfferEvaluation fair = FreeAgency.Evaluate(s, p, best.Id, ask.Aav, ask.Years, ask.BonusPct, 0);
            OfferEvaluation rich = FreeAgency.Evaluate(s, p, best.Id, (long)(ask.Aav * 1.25), ask.Years, ask.BonusPct, 0);
            Assert.AreEqual(0f, low.Money, 0.01f);
            Assert.AreEqual(0.5f, fair.Money, 0.06f);
            Assert.AreEqual(1f, rich.Money, 0.01f);
            Assert.Less(low.Score, fair.Score);
            Assert.Less(fair.Score, rich.Score);
            // better teams look better to contender-minded players
            Assert.Greater(FreeAgency.ContenderScore(s, best), FreeAgency.ContenderScore(s, worst));
            // a team that already starts better players at the position offers less role
            Team weakAtPos = s.Pro.Teams.OrderBy(t => Offseason.BestOvr(t, p.Position)).First();
            Team strongAtPos = s.Pro.Teams.OrderByDescending(t => Offseason.BestOvr(t, p.Position)).First();
            Assert.GreaterOrEqual(FreeAgency.RoleScore(weakAtPos, p), FreeAgency.RoleScore(strongAtPos, p));
            // the old team gets a loyalty edge
            RosterManager.RememberPrior(s, p, worst.Id);
            Assert.Greater(FreeAgency.Evaluate(s, p, worst.Id, ask.Aav, ask.Years, ask.BonusPct, 0).Mood, FreeAgency.Evaluate(s, p, best.Id, ask.Aav, ask.Years, ask.BonusPct, 0).Mood - 0.2f);
            // the threshold falls day by day so everybody signs eventually
            Assert.Greater(FreeAgency.Threshold(0), FreeAgency.Threshold(10));
            Assert.GreaterOrEqual(FreeAgency.Threshold(100), 0.3f);
            // asks also fall
            Assert.Greater(FreeAgency.Ask(s, p, 0).Aav, FreeAgency.Ask(s, p, 14).Aav);
            // each player has a stable main priority
            Assert.AreEqual(FreeAgency.PriorityOf(p), FreeAgency.PriorityOf(p));
        }

        [Test]
        public void TheUserCanBidAndATopOfferBeatsTheField()
        {
            CareerSave s = AtFreeAgency(63);
            Team u = s.UserTeam;
            u.SalaryCap += 200000000L;   // plenty of room for the test
            Player target = FreeAgency.Pool(s).Where(p => p.Overall >= 78 && !FreeAgency.IsUndrafted(s, p)).OrderByDescending(p => p.Overall).First();
            ContractAsk ask = FreeAgency.Ask(s, target, 0);
            Assert.IsFalse(FreeAgency.PlaceOffer(s, 999999999, ask.Aav, 3, 0.1f).Ok);
            OpResult placed = FreeAgency.PlaceOffer(s, target.Id, (long)(ask.Aav * 1.4), 5, 0.3f);
            Assert.IsTrue(placed.Ok, placed.Message);
            Assert.IsNotNull(FreeAgency.OfferOf(s, u.Id, target.Id));
            // raising replaces the bid instead of adding another
            Assert.IsTrue(FreeAgency.PlaceOffer(s, target.Id, (long)(ask.Aav * 1.5), 5, 0.3f).Ok);
            Assert.AreEqual(1, s.FaOffers.Count(o => o.PlayerId == target.Id && o.TeamId == u.Id));
            for (int d = 0; d < 14 && FreeAgency.FindInPool(s, target.Id) != null; d++) CareerFlow.Advance(s);
            Assert.IsNull(FreeAgency.FindInPool(s, target.Id), "somebody signed him");
            int owner = s.TeamOf(target.Id);
            Assert.GreaterOrEqual(owner, 0);
            if (owner == u.Id)
            {
                Assert.AreEqual(5, target.Contract.Years);
                Assert.GreaterOrEqual(target.Contract.Aav, (long)(ask.Aav * 1.4));
            }
            Assert.IsNull(FreeAgency.OfferOf(s, u.Id, target.Id), "the bid is closed once he signs");
            Assert.IsTrue(s.FaLog.Any(f => f.PlayerId == target.Id));
        }

        [Test]
        public void OffersWithoutCapRoomAreRejectedAndWithdrawable()
        {
            CareerSave s = AtFreeAgency(64);
            Team u = s.UserTeam;
            Player star = FreeAgency.Pool(s).First(p => p.Overall >= 76 && !FreeAgency.IsUndrafted(s, p));
            u.SalaryCap = u.CapUsed + 1000000L;
            ContractAsk ask = FreeAgency.Ask(s, star, 0);
            OpResult r = FreeAgency.PlaceOffer(s, star.Id, ask.Aav, ask.Years, ask.BonusPct);
            Assert.IsFalse(r.Ok);
            Assert.IsFalse(FreeAgency.WithdrawOffer(s, star.Id).Ok);
            u.SalaryCap = u.CapUsed + 400000000L;
            Assert.IsTrue(FreeAgency.PlaceOffer(s, star.Id, ask.Aav, ask.Years, ask.BonusPct).Ok);
            Assert.IsTrue(FreeAgency.WithdrawOffer(s, star.Id).Ok);
            Assert.AreEqual(0, s.FaOffers.Count);
        }

        [Test]
        public void CpuTeamsShopByNeedAndStayUnderTheCap()
        {
            CareerSave s = AtFreeAgency(65);
            var needByTeam = s.Pro.Teams.ToDictionary(t => t.Id, t => LeagueRules.RosterOrder.Sum(pos => Math.Max(0, Offseason.Deficit(t, pos))));
            var before = s.Pro.Teams.ToDictionary(t => t.Id, t => t.Roster.Count);
            CareerFlow.SkipFreeAgency(s);
            Assert.IsTrue(FreeAgency.IsDone(s));
            var signings = s.FaLog.Where(f => f.Year == s.Year).ToList();
            Assert.Greater(signings.Count, 40);
            // teams with the biggest holes signed more players than teams without
            var neediest = s.Pro.Teams.OrderByDescending(t => needByTeam[t.Id]).Take(6).Select(t => t.Id).ToList();
            var fullest = s.Pro.Teams.OrderBy(t => needByTeam[t.Id]).Take(6).Select(t => t.Id).ToList();
            Assert.GreaterOrEqual(signings.Count(f => neediest.Contains(f.TeamId)), signings.Count(f => fullest.Contains(f.TeamId)) - 2);
            foreach (FaSigning f in signings)
            {
                Team t = s.Pro.GetTeam(f.TeamId);
                Assert.IsTrue(t.FindAnyPlayer(f.PlayerId) != null || s.FindPlayer(f.PlayerId) != null);
                Assert.Greater(f.Aav, 0);
            }
            foreach (Team t in s.Pro.Teams) Assert.LessOrEqual(t.Roster.Count, RosterRules.OffseasonLimit);
            // the best player available was not left to rot: a top free agent signed
            Assert.IsTrue(signings.Any(f => f.Ovr >= 78));
        }

        [Test]
        public void UndraftedRookiesAskTheMinimumAndStreetSigningsRespectTheAsk()
        {
            CareerSave s = CareerTestUtil.AtPhase(66, CareerPhase.Preseason);
            Team u = s.UserTeam;
            Player udfa = s.Pro.FreeAgents.FirstOrDefault(p => FreeAgency.IsUndrafted(s, p));
            if (udfa != null)
            {
                ContractAsk a = FreeAgency.Ask(s, udfa);
                Assert.AreEqual(CapMath.MinSalary(s, s.Year), a.Aav);
                Assert.AreEqual(3, a.Years);
            }
            Player vet = s.Pro.FreeAgents.Where(p => p.Overall >= 70 && !FreeAgency.IsUndrafted(s, p)).OrderByDescending(p => p.Overall).FirstOrDefault();
            if (vet != null)
            {
                long ask = FreeAgency.Ask(s, vet).Aav;
                u.SalaryCap += 100000000L;
                OpResult cheap = FreeAgency.SignNow(s, u, vet.Id, ask / 2, 2);
                Assert.IsFalse(cheap.Ok, "half the ask is refused");
                Assert.IsTrue(FreeAgency.FindInPool(s, vet.Id) != null);
                // the active roster is limited to 90 in the preseason: cut first when full
                while (u.Roster.Count >= RosterRules.OffseasonLimit) RosterManager.Release(s, u, u.Roster.OrderBy(p => p.Overall).First().Id);
                OpResult ok = FreeAgency.SignNow(s, u, vet.Id, ask, 2);
                Assert.IsTrue(ok.Ok, ok.Message);
                Assert.IsTrue(u.Roster.Contains(vet));
                Assert.AreEqual(2, vet.Contract.Years);
            }
            // practice squad street signings use the squad deal
            Player kid = s.Pro.FreeAgents.FirstOrDefault(p => p.Experience <= 2 && p.Overall >= 45);
            if (kid != null)
            {
                u.PracticeSquad.RemoveAt(0);
                OpResult ps = FreeAgency.SignNow(s, u, kid.Id, 0, 1, true);
                Assert.IsTrue(ps.Ok, ps.Message);
                Assert.IsTrue(u.PracticeSquad.Contains(kid));
                Assert.Less(kid.Contract.CapHit, Contract.MinSalary);
            }
            // bidding is only for the free agency phase
            CareerSave fa = AtFreeAgency(67);
            Assert.IsFalse(FreeAgency.SignNow(fa, fa.UserTeam, fa.Pro.FreeAgents[0].Id, 1000000L, 1).Ok);
            Assert.IsFalse(FreeAgency.PlaceOffer(s, vet != null ? vet.Id : 1, 1000000L, 1, 0.1f).Ok);
        }
    }
}
