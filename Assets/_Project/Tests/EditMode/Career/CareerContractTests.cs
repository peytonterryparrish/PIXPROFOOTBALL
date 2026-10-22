using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>Contracts and the cap: structure, dead money, rookie scale, re-sign negotiations, extensions, franchise tag, compliance.</summary>
    public class CareerContractTests
    {
        [Test]
        public void ContractStructureProratesTheBonusAndDeadMoneyFollowsTheUnamortisedBonus()
        {
            Contract c = Contract.Create(4, 10000000L, 0.30f, 0.6f, 2026);
            Assert.AreEqual(4, c.CapHits.Count);
            Assert.AreEqual(12000000L, c.SigningBonus);
            Assert.AreEqual(24000000L, c.Guaranteed);
            Assert.GreaterOrEqual(c.CapHits.Sum(), 38000000L);
            Assert.LessOrEqual(c.CapHits.Sum(), 42000000L);
            Assert.AreEqual(4, c.YearsLeft);
            // released with 3 years left: the larger of 3 years of bonus and 3/4 of the guarantee
            c.YearsLeft = 3;
            Assert.AreEqual(Math.Max(9000000L, 18000000L), Offseason.DeadMoneyOf(c));
            long now, next; CapManager.DeadMoneyPreview(c, out now, out next);
            Assert.AreEqual(18000000L, now + next);
            Assert.AreEqual(9000000L, now);
            // an expiring year leaves everything in the current year
            c.YearsLeft = 1;
            CapManager.DeadMoneyPreview(c, out now, out next);
            Assert.AreEqual(0L, next);
            Assert.Greater(now, 0L);
        }

        [Test]
        public void TheCapGrowsAndMarketMoneyFollowsIt()
        {
            CareerSave s = CareerTestUtil.Fresh(41);
            Player qb = s.Pro.Teams[0].Roster.First(p => p.Position == Position.QB);
            long m26 = CapMath.Market(s, qb, 2026), m30 = CapMath.Market(s, qb, 2030);
            Assert.AreEqual(Math.Pow(1.05, 4), (double)m30 / m26, 0.02);
            Assert.Greater(CapMath.MinSalary(s, 2030), CapMath.MinSalary(s, 2026));
            Assert.Less(CapMath.PracticeSquadSalary(s, 2026), Contract.MinSalary);
            // position, ovr and age move the price
            long star = CapMath.Market(s, Position.QB, 92, 27, 2026), starK = CapMath.Market(s, Position.K, 92, 27, 2026), avg = CapMath.Market(s, Position.QB, 70, 27, 2026), old = CapMath.Market(s, Position.QB, 92, 36, 2026);
            Assert.Greater(star, starK * 3);
            Assert.Greater(star, avg * 2);
            Assert.Less(old, star);
        }

        [Test]
        public void RookieScaleFollowsTheDraftSlot()
        {
            CareerSave s = CareerTestUtil.Fresh(42);
            Contract p1 = CapMath.Rookie(s, 1, 2026), p32 = CapMath.Rookie(s, 32, 2026), p100 = CapMath.Rookie(s, 100, 2026), udfa = CapMath.Rookie(s, 400, 2026);
            Assert.Greater(p1.Aav, p32.Aav);
            Assert.Greater(p32.Aav, p100.Aav);
            Assert.Greater(p100.Aav, udfa.Aav);
            Assert.AreEqual(4, p1.Years);
            Assert.AreEqual(3, udfa.Years);
            Assert.IsTrue(p1.Rookie);
            Assert.Greater(p1.Guaranteed, p100.Guaranteed);
            Assert.Greater(CapMath.Rookie(s, 1, 2030).Aav, p1.Aav, "the scale grows with the cap");
        }

        [Test]
        public void AskingPricesFollowOverallAgeAndMood()
        {
            CareerSave s = CareerTestUtil.AtPhase(43, CareerPhase.ReSign);
            Team u = s.UserTeam;
            var exp = Negotiator.ExpiringPlayers(s, u);
            Assert.Greater(exp.Count, 3, "a normal team has several expiring contracts");
            Player p = exp.OrderByDescending(x => x.Overall).First();
            ContractAsk calm = Negotiator.Ask(s, u, p);
            Assert.GreaterOrEqual(calm.Aav, CapMath.MinSalary(s, s.Year + 1));
            int morale = p.Morale;
            p.Morale = 95;
            ContractAsk happy = Negotiator.Ask(s, u, p);
            p.Morale = 40;
            ContractAsk sour = Negotiator.Ask(s, u, p);
            p.Morale = morale;
            Assert.Less(happy.Aav, calm.Aav + 1);
            Assert.Greater(sour.Aav, happy.Aav);
            Assert.AreEqual(Negotiator.YearsFor(p.Age), calm.Years);
            Assert.Greater(Negotiator.YearsFor(24), Negotiator.YearsFor(34));
            // stable between calls
            Assert.AreEqual(Negotiator.Ask(s, u, p).Aav, Negotiator.Ask(s, u, p).Aav);
        }

        [Test]
        public void ReSigningAcceptsCountersDeclinesAndWalksAfterThreeRounds()
        {
            CareerSave s = CareerTestUtil.AtPhase(44, CareerPhase.ReSign);
            Team u = s.UserTeam;
            var exp = Negotiator.ExpiringPlayers(s, u).Where(x => x.Overall >= 60).OrderByDescending(x => x.Overall).ToList();
            Assert.GreaterOrEqual(exp.Count, 3);
            Player a = exp[0], b = exp[1], c = exp[2];
            foreach (Player p in new[] { a, b, c }) p.Morale = 70;

            // a full-price offer is accepted when the cap allows it (cut space first for the test)
            u.SalaryCap += 80000000L;
            ContractAsk ask = Negotiator.Ask(s, u, a);
            OfferResult ok = Negotiator.Offer(s, u, a.Id, (long)(ask.Aav * 1.02), ask.Years, ask.BonusPct);
            Assert.AreEqual(OfferVerdict.Accepted, ok.Verdict, ok.Message);
            Assert.AreEqual(ask.Years, a.Contract.YearsLeft);
            Assert.GreaterOrEqual(a.Contract.Aav, (long)(ask.Aav * 1.0));
            Assert.IsFalse(Negotiator.ExpiringPlayers(s, u).Contains(a));
            Assert.AreEqual(OfferVerdict.Rejected, Negotiator.Offer(s, u, a.Id, ask.Aav, 3, 0.1f).Verdict, "already signed");

            // 92% of the ask: a counter at the ask
            ContractAsk askB = Negotiator.Ask(s, u, b);
            OfferResult counter = Negotiator.Offer(s, u, b.Id, (long)(askB.Aav * 0.92), askB.Years, askB.BonusPct);
            Assert.AreEqual(OfferVerdict.Counter, counter.Verdict, counter.Message);
            Assert.AreEqual(askB.Aav, counter.CounterAav);
            // far too low: declined, and the third round ends the talks
            OfferResult low1 = Negotiator.Offer(s, u, b.Id, askB.Aav / 2, 3, 0.1f);
            Assert.AreEqual(OfferVerdict.Declined, low1.Verdict);
            OfferResult low2 = Negotiator.Offer(s, u, b.Id, askB.Aav / 2, 3, 0.1f);
            Assert.AreEqual(OfferVerdict.WalkedAway, low2.Verdict);
            Assert.IsTrue(b.Contract.YearsLeft <= 0);

            // an unhappy player on a losing team will not even talk
            c.Morale = 20;
            for (int i = 0; i < s.TeamSeasons.Count; i++) { }
            TeamSeasonRecord rec = s.TeamSeasons.LastOrDefault(r => r.TeamId == u.Id);
            if (rec != null) { rec.Wins = 3; rec.Losses = 14; }
            else s.TeamSeasons.Add(new TeamSeasonRecord { TeamId = u.Id, Year = s.Year, Wins = 3, Losses = 14 });
            if (c.Overall >= 68)
            {
                Assert.IsFalse(Negotiator.Ask(s, u, c).WillNegotiate);
                Assert.AreEqual(OfferVerdict.Refuses, Negotiator.Offer(s, u, c.Id, 99000000L, 3, 0.1f).Verdict);
            }
        }

        [Test]
        public void ARichOfferBeyondTheCapIsRejected()
        {
            CareerSave s = CareerTestUtil.AtPhase(45, CareerPhase.ReSign);
            Team u = s.UserTeam;
            Player a = Negotiator.ExpiringPlayers(s, u).OrderByDescending(x => x.Overall).First();
            u.SalaryCap = u.CapUsed + 100000L;   // no room at all
            ContractAsk ask = Negotiator.Ask(s, u, a);
            OfferResult r = Negotiator.Offer(s, u, a.Id, ask.Aav * 2, ask.Years, ask.BonusPct);
            Assert.AreEqual(OfferVerdict.Rejected, r.Verdict);
            StringAssert.Contains("cap", r.Message.ToLowerInvariant());
            Assert.IsTrue(a.Contract.YearsLeft <= 0);
        }

        [Test]
        public void UnderContractPlayersCanOnlyBeExtendedInTheirFinalYear()
        {
            CareerSave s = CareerTestUtil.Fresh(46);
            CareerFlow.Advance(s);
            Team u = s.UserTeam;
            Player longDeal = u.Roster.First(p => p.Contract.YearsLeft >= 3);
            Assert.AreEqual(OfferVerdict.Rejected, Negotiator.Offer(s, u, longDeal.Id, 5000000L, 3, 0.1f).Verdict);
            Player last = u.Roster.Where(p => p.Contract.YearsLeft == 1 && p.Overall >= 65).OrderByDescending(p => p.Overall).First();
            last.Morale = 85;
            u.SalaryCap += 60000000L;
            long hitNow = last.Contract.CapHit;
            ContractAsk ask = Negotiator.Ask(s, u, last);
            OfferResult r = Negotiator.Offer(s, u, last.Id, (long)(ask.Aav * 1.05), 4, ask.BonusPct);
            Assert.AreEqual(OfferVerdict.Accepted, r.Verdict, r.Message);
            Assert.AreEqual(5, last.Contract.YearsLeft, "this year plus four");
            Assert.AreEqual(last.Contract.Years, last.Contract.CapHits.Count);
            Assert.AreEqual(hitNow, last.Contract.CapHit, "this year's cap hit does not change");
            Assert.IsFalse(last.Contract.Rookie);
        }

        [Test]
        public void TheFranchiseTagIsOncePerOffseasonAndOnlyForExpiringPlayers()
        {
            CareerSave s = CareerTestUtil.AtPhase(47, CareerPhase.ReSign);
            Team u = s.UserTeam;
            var exp = Negotiator.ExpiringPlayers(s, u).OrderByDescending(x => x.Overall).ToList();
            Player a = exp[0], b = exp[1];
            Player signed = u.Roster.First(p => p.Contract.YearsLeft > 1);
            Assert.IsFalse(CapManager.FranchiseTag(s, u, signed.Id).Ok);
            u.SalaryCap += 100000000L;
            long cost = CapManager.TagCost(s, a);
            Assert.GreaterOrEqual(cost, (long)(a.Contract.Aav * 1.2) - 10000L);
            OpResult tag = CapManager.FranchiseTag(s, u, a.Id);
            Assert.IsTrue(tag.Ok, tag.Message);
            Assert.AreEqual(1, a.Contract.Years);
            Assert.AreEqual(1, a.Contract.YearsLeft);
            Assert.AreEqual(cost, a.Contract.Aav);
            Assert.AreEqual(a.Contract.Aav, a.Contract.Guaranteed, "the tag is fully guaranteed");
            Assert.IsFalse(CapManager.TagAvailable(s, u.Id));
            Assert.IsFalse(CapManager.FranchiseTag(s, u, b.Id).Ok, "one tag per team");
            // the window must be open
            CareerSave other = CareerTestUtil.Fresh(48);
            Assert.IsFalse(CapManager.FranchiseTag(other, other.UserTeam, other.UserTeam.Roster[0].Id).Ok);
        }

        [Test]
        public void RestructuringLowersThisYearAndRaisesTheFutureAndThenNoMoreWhenOneYearIsLeft()
        {
            CareerSave s = CareerTestUtil.Fresh(49);
            Team u = s.UserTeam;
            Player p = u.Roster.Where(x => Offseason.RestructureRoom(x.Contract) > 0).OrderByDescending(x => x.Contract.CapHit).First();
            long hit = p.Contract.CapHit;
            long nextHit = CapManager.CapHitNext(p.Contract);
            long used = u.CapUsed;
            OpResult r = CapManager.Restructure(s, u, p.Id);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.Less(p.Contract.CapHit, hit);
            Assert.Greater(CapManager.CapHitNext(p.Contract), nextHit);
            Assert.Less(u.CapUsed, used);
            Player last = u.Roster.First(x => x.Contract.YearsLeft == 1);
            Assert.IsFalse(CapManager.Restructure(s, u, last.Id).Ok);
        }

        [Test]
        public void CapComplianceIsFixedByTheAutoFixAndBlocksTheUserAtTheLeagueYear()
        {
            CareerSave s = CareerTestUtil.AtPhase(50, CareerPhase.ReSign);
            Team u = s.UserTeam;
            u.SalaryCap = u.CapUsed - 9000000L;
            Assert.Contains(u.Id, CapManager.NonCompliantTeams(s));
            Assert.AreEqual(1, CareerFlow.Blockers(s).Count);
            Assert.IsFalse(CareerFlow.Advance(s).Ok);
            int moves = CapManager.AutoFix(s, u);
            Assert.Greater(moves, 0);
            Assert.LessOrEqual(u.CapUsed, u.SalaryCap);
            Assert.AreEqual(0, CareerFlow.Blockers(s).Count);
            // the report shows the books
            CapReport rep = CapManager.Report(s, u);
            Assert.IsTrue(rep.Compliant);
            Assert.AreEqual(u.SalaryCap - u.CapUsed, rep.Space);
            Assert.Greater(CapManager.LedgerRows(u).Count, 53);
            Assert.AreEqual(rep.Used - u.DeadCap, CapManager.LedgerRows(u).Sum(x => x.CapHitThisYear));
        }

        [Test]
        public void PracticeSquadAndReserveContractsCountAgainstTheCap()
        {
            CareerSave s = CareerTestUtil.Fresh(51);
            Team u = s.UserTeam;
            long active = u.Roster.Sum(p => p.Contract.CapHit);
            long squad = u.PracticeSquad.Sum(p => p.Contract.CapHit);
            Assert.Greater(squad, 0L);
            Assert.AreEqual(active + squad + u.DeadCap, u.CapUsed);
            Assert.Less(u.PracticeSquad[0].Contract.CapHit, Contract.MinSalary);
        }

        [Test]
        public void TheLeagueYearTicksContractsAndCarriesDeadMoneyForward()
        {
            CareerSave s = CareerTestUtil.AtPhase(52, CareerPhase.Retirements);
            Team u = s.UserTeam;
            u.DeadCapNext = 4000000L;
            var years = u.Roster.ToDictionary(p => p.Id, p => p.Contract.YearsLeft);
            var survivors = u.Roster.Select(p => p.Id).ToList();
            CareerFlow.Advance(s);
            Assert.AreEqual(CareerPhase.ReSign, s.Phase);
            Assert.AreEqual(4000000L, u.DeadCap);
            Assert.AreEqual(0L, u.DeadCapNext);
            foreach (Player p in u.Roster) if (years.ContainsKey(p.Id)) Assert.AreEqual(Math.Max(0, years[p.Id] - 1), p.Contract.YearsLeft);
            Assert.AreEqual(CapMath.CapFor(s, s.Year + 1), u.SalaryCap);
        }
    }
}
