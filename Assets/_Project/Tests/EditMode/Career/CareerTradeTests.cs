using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>Trades: the value model, the pick book, CPU acceptance, counters, legality, the deadline and CPU-initiated offers.</summary>
    public class CareerTradeTests
    {
        private static TradeProposal Proposal(CareerSave s, Team cpu, IEnumerable<TradeAsset> fromUser, IEnumerable<TradeAsset> fromCpu)
        {
            var tp = new TradeProposal { TeamA = s.UserTeamId, TeamB = cpu.Id };
            tp.FromA.AddRange(fromUser); tp.FromB.AddRange(fromCpu);
            return tp;
        }

        [Test]
        public void ThePickChartFallsSteeplyAndFuturePicksAreDiscounted()
        {
            CareerSave s = CareerTestUtil.Fresh(71);
            Assert.AreEqual(3000f, TradeEngine.PickChartValue(1), 0.1f);
            Assert.Greater(TradeEngine.PickChartValue(1), TradeEngine.PickChartValue(10) * 1.2f);
            Assert.Greater(TradeEngine.PickChartValue(32), TradeEngine.PickChartValue(64));
            Assert.Greater(TradeEngine.PickChartValue(64), TradeEngine.PickChartValue(224) * 20f);
            Team t = s.Pro.Teams[3];
            DraftPickAsset now = TradeEngine.PicksOf(s, t.Id).First(k => k.Year == s.Year && k.Round == 1);
            DraftPickAsset next = TradeEngine.PicksOf(s, t.Id).First(k => k.Year == s.Year + 1 && k.Round == 1);
            DraftPickAsset later = TradeEngine.PicksOf(s, t.Id).First(k => k.Year == s.Year + 2 && k.Round == 1);
            Assert.AreEqual(21, TradeEngine.PicksOf(s, t.Id).Count);
            Assert.Greater(TradeEngine.PickValue(s, now), 0f);
            // same slot estimate, but a year away is worth less
            Assert.Less(TradeEngine.PickValue(s, later), TradeEngine.PickValue(s, now) * 1.05f);
            Assert.AreEqual(TradeEngine.PickChartValue(TradeEngine.EstimatedOverall(s, next)) * 0.88f, TradeEngine.PickValue(s, next), 0.5f);
            Assert.AreEqual(t.Id, now.OwnerTeamId);
        }

        [Test]
        public void PlayerValueRewardsOverallYouthPositionAndContractSurplus()
        {
            CareerSave s = CareerTestUtil.Fresh(72);
            Team t = s.Pro.Teams[8];
            Player rb = t.Roster.First(p => p.Position == Position.RB), qb = t.Roster.First(p => p.Position == Position.QB);
            // clone-and-adjust: same player at different ovr / age
            Player probe = qb;
            int age = probe.Bio.Age;
            float v = TradeEngine.PlayerValue(s, probe);
            probe.Bio.Age = 24; float young = TradeEngine.PlayerValue(s, probe);
            probe.Bio.Age = 35; float old = TradeEngine.PlayerValue(s, probe);
            probe.Bio.Age = age;
            Assert.Greater(young, old * 3f);
            Assert.AreEqual(v, TradeEngine.PlayerValue(s, probe), 0.01f);
            // a 90 is worth multiples of an 80, an 80 multiples of a 70
            var top = s.Pro.AllRosterPlayers().OrderByDescending(p => p.Overall).First();
            Assert.Greater(TradeEngine.PlayerValue(s, top), 1500f);
            var ordinary = s.Pro.AllRosterPlayers().Where(p => p.Overall >= 66 && p.Overall <= 70 && p.Position == Position.WR).First();
            Assert.Less(TradeEngine.PlayerValue(s, ordinary), TradeEngine.PlayerValue(s, top));
            // contract surplus: the same player on a cheaper deal is worth more
            Player wr = s.Pro.AllRosterPlayers().Where(p => p.Position == Position.WR && p.Overall >= 78 && p.Contract.YearsLeft >= 2).First();
            Contract original = wr.Contract;
            float baseline = TradeEngine.PlayerValue(s, wr);
            wr.Contract = Contract.Create(original.Years, Math.Max(Contract.MinSalary, original.Aav / 3), 0.1f, 0.3f, 2026);
            wr.Contract.YearsLeft = original.YearsLeft;
            float cheap = TradeEngine.PlayerValue(s, wr);
            wr.Contract = original;
            Assert.Greater(cheap, baseline);
            // quarterbacks are worth more than backs
            Assert.Greater(TradeEngine.PlayerValue(s, qb) / Math.Max(1, qb.Overall - 50), TradeEngine.PlayerValue(s, rb) / Math.Max(1, rb.Overall - 50) * 0.8f);
        }

        [Test]
        public void ACpuTeamAcceptsAnOverpayAndTheTradeExecutes()
        {
            CareerSave s = CareerTestUtil.Fresh(73);
            Team u = s.UserTeam, cpu = s.Pro.Teams.First(t => t.Id != u.Id && t.Id != 0);
            Player want = cpu.Roster.Where(p => !Offseason.IsStarter(cpu, p) && p.Contract.YearsLeft > 0 && p.Overall >= 66 && p.Position != Position.K && p.Position != Position.P && p.Position != Position.QB)
                .OrderByDescending(p => TradeEngine.PlayerValue(s, p)).First();
            Player give = u.Roster.Where(p => p.Contract.SigningBonus > 0 && p.Contract.YearsLeft >= 2 && !Offseason.IsStarter(u, p) && p.Contract.Aav < 6000000L).OrderByDescending(p => p.Contract.SigningBonus).FirstOrDefault()
                ?? u.Roster.Where(p => p.Contract.YearsLeft >= 2 && !Offseason.IsStarter(u, p)).First();
            DraftPickAsset pick = TradeEngine.PicksOf(s, u.Id).First(k => k.Year == s.Year && k.Round == 2);
            var tp = Proposal(s, cpu, new[] { TradeAsset.OfPlayer(give.Id), TradeAsset.OfPick(pick.Id) }, new[] { TradeAsset.OfPlayer(want.Id) });
            TradeEvaluation ev = TradeEngine.Evaluate(s, tp);
            Assert.IsTrue(ev.Legal, string.Join("; ", ev.Problems.ToArray()));
            Assert.Greater(ev.BReceives, ev.BGives);
            Assert.IsTrue(ev.CpuAccepts, ev.Message);

            long deadBefore = u.DeadCap + u.DeadCapNext;
            long dn, dx; TradeEngine.TradeDead(give.Contract, out dn, out dx);
            long giveBonus = give.Contract.SigningBonus;
            int userCount = u.Roster.Count, cpuCount = cpu.Roster.Count;
            int txBefore = s.Transactions.Count;
            OpResult r = TradeEngine.Propose(s, tp);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.IsTrue(cpu.Roster.Contains(give));
            Assert.IsTrue(u.Roster.Contains(want));
            Assert.AreEqual(cpu.Id, pick.OwnerTeamId);
            Assert.AreEqual(u.Id, pick.OriginalTeamId);
            Assert.AreEqual(userCount, u.Roster.Count);   // one in, one out
            Assert.AreEqual(cpuCount, cpu.Roster.Count);
            Assert.AreEqual(deadBefore + dn + dx, u.DeadCap + u.DeadCapNext, "the sender keeps the unamortised bonus");
            Assert.AreEqual(0L, give.Contract.SigningBonus, "the new team does not carry the bonus");
            Assert.Greater(s.Transactions.Count, txBefore);
            Assert.IsTrue(s.News.Any(n => n.Kind == NewsKind.Trade));
            Assert.AreEqual(u.Id, RosterManager.PriorTeamOf(s, give.Id));
            CareerTestUtil.AssertClean(s, false);
            // the pick is still tradeable but the moved assets are not the user's any more
            Assert.IsFalse(TradeEngine.Validate(s, tp).Legal);
        }

        [Test]
        public void ALowballIsRefusedAndAskWhatItTakesFindsAWorkableCounter()
        {
            CareerSave s = CareerTestUtil.Fresh(74);
            Team u = s.UserTeam, cpu = s.Pro.Teams.First(t => t.Id != u.Id && t.Id != 1);
            u.SalaryCap += 100000000L;
            Player want = cpu.Roster.Where(p => p.Contract.YearsLeft > 0 && p.Position != Position.K && p.Position != Position.P).OrderByDescending(p => TradeEngine.PlayerValue(s, p)).Skip(2).First();
            DraftPickAsset sweetener = TradeEngine.PicksOf(s, u.Id).First(k => k.Round == 7);
            var tp = Proposal(s, cpu, new[] { TradeAsset.OfPick(sweetener.Id) }, new[] { TradeAsset.OfPlayer(want.Id) });
            TradeEvaluation ev = TradeEngine.Evaluate(s, tp);
            Assert.IsTrue(ev.Legal);
            Assert.IsFalse(ev.CpuAccepts);
            Assert.IsFalse(TradeEngine.Propose(s, tp).Ok);
            Assert.AreSame(want, cpu.GetPlayer(want.Id), "nothing moved");
            TradeProposal counter = TradeEngine.AskWhatItTakes(s, tp);
            Assert.IsNotNull(counter, "the user owns enough to make the deal work");
            Assert.Greater(counter.FromA.Count, tp.FromA.Count);
            Assert.IsTrue(TradeEngine.Evaluate(s, counter).CpuAccepts);
            Assert.IsTrue(TradeEngine.Propose(s, counter).Ok);
            Assert.IsTrue(u.Roster.Contains(want));
        }

        [Test]
        public void IllegalTradesAreRejectedWithReasons()
        {
            CareerSave s = CareerTestUtil.Fresh(75);
            CareerFlow.Advance(s);   // regular season: 53-man limit applies
            Team u = s.UserTeam, cpu = s.Pro.Teams.First(t => t.Id != u.Id);
            // not owned
            DraftPickAsset theirPick = TradeEngine.PicksOf(s, cpu.Id).First();
            var notMine = Proposal(s, cpu, new[] { TradeAsset.OfPick(theirPick.Id) }, new TradeAsset[0]);
            Assert.IsFalse(TradeEngine.Validate(s, notMine).Legal);
            // nothing on the table, same team
            Assert.IsFalse(TradeEngine.Validate(s, new TradeProposal { TeamA = u.Id, TeamB = cpu.Id }).Legal);
            Assert.IsFalse(TradeEngine.Validate(s, new TradeProposal { TeamA = u.Id, TeamB = u.Id, FromA = { TradeAsset.OfPlayer(u.Roster[0].Id) } }).Legal);
            // roster limit for the user: a two-for-one in the user's favour does not fit at 53
            var two = cpu.Roster.Where(p => p.Contract.YearsLeft > 0).Take(2).Select(p => TradeAsset.OfPlayer(p.Id)).ToList();
            var tp = Proposal(s, cpu, new[] { TradeAsset.OfPick(TradeEngine.PicksOf(s, u.Id).First().Id) }, two);
            TradeEvaluation ev = TradeEngine.Evaluate(s, tp);
            Assert.IsFalse(ev.Legal);
            StringAssert.Contains("roster", string.Join(" ", ev.Problems.ToArray()).ToLowerInvariant());
            // practice squad players cannot be traded
            var ps = Proposal(s, cpu, new[] { TradeAsset.OfPlayer(u.PracticeSquad[0].Id) }, new TradeAsset[0]);
            Assert.IsFalse(TradeEngine.Validate(s, ps).Legal);
            // the cap binds the receiving CPU team
            Team capped = s.Pro.Teams.First(t => t.Id != u.Id && t.Id != cpu.Id);
            capped.SalaryCap = capped.CapUsed + 500000L;
            Player star = u.Roster.OrderByDescending(p => p.Contract.CapHit).First();
            var give = Proposal(s, capped, new[] { TradeAsset.OfPlayer(star.Id) }, new TradeAsset[0]);
            if (star.Contract.CapHit > 1000000L) Assert.IsFalse(TradeEngine.Validate(s, give).Legal);
        }

        [Test]
        public void TheDeadlineAndThePlayoffsCloseTheMarket()
        {
            CareerSave s = CareerTestUtil.Fresh(76);
            Assert.AreEqual(TradeWindow.Open, TradeEngine.Window(s));
            CareerFlow.Advance(s);
            for (int i = 0; i < 8; i++) CareerFlow.Advance(s);
            Assert.AreEqual(9, s.Season.Week);
            Assert.AreEqual(TradeWindow.Open, TradeEngine.Window(s), "week 9 is the last week with trades");
            CareerFlow.Advance(s);
            Assert.AreEqual(10, s.Season.Week);
            Assert.AreEqual(TradeWindow.ClosedDeadline, TradeEngine.Window(s));
            Team u = s.UserTeam, cpu = s.Pro.Teams.First(t => t.Id != u.Id);
            var tp = Proposal(s, cpu, new[] { TradeAsset.OfPick(TradeEngine.PicksOf(s, u.Id).First().Id) }, new TradeAsset[0]);
            TradeEvaluation ev = TradeEngine.Evaluate(s, tp);
            Assert.IsFalse(ev.Legal);
            StringAssert.Contains("deadline", ev.Message.ToLowerInvariant());
            Assert.IsFalse(TradeEngine.Propose(s, tp).Ok);
            CareerFlow.AdvanceUntil(s, CareerPhase.Playoffs);
            Assert.AreEqual(TradeWindow.ClosedPostseason, TradeEngine.Window(s));
            CareerFlow.AdvanceUntil(s, CareerPhase.Awards);
            Assert.AreEqual(TradeWindow.Open, TradeEngine.Window(s));
        }

        [Test]
        public void ACpuTeamWillNotGiveUpItsFranchiseQuarterbackForEvenValue()
        {
            CareerSave s = CareerTestUtil.Fresh(77);
            Team cpu = s.Pro.Teams.Where(t => t.Id != s.UserTeamId).OrderByDescending(t => t.Roster.Where(p => p.Position == Position.QB).Max(p => p.Overall)).First();
            Player qb = cpu.Roster.Where(p => p.Position == Position.QB).OrderByDescending(p => p.Overall).First();
            Assert.GreaterOrEqual(qb.Overall, 70);
            Team u = s.UserTeam;
            var picks = TradeEngine.PicksOf(s, u.Id).Where(k => k.Round <= 3).Select(k => TradeAsset.OfPick(k.Id)).ToList();
            var tp = Proposal(s, cpu, picks, new[] { TradeAsset.OfPlayer(qb.Id) });
            TradeEvaluation ev = TradeEngine.Evaluate(s, tp);
            if (qb.Overall >= 80 && Offseason.IsStarter(cpu, qb) && ev.BReceives < ev.BGives * 1.5f) Assert.IsFalse(ev.CpuAccepts, "untouchable");
            else Assert.IsNotNull(ev);
        }

        [Test]
        public void CpuTeamsMakeOffersForTheUsersPlayersAndTheUserCanAcceptOrDecline()
        {
            CareerSave s = CareerTestUtil.Fresh(78);
            CareerFlow.Advance(s);
            int made = 0;
            for (int attempt = 0; attempt < 120 && made == 0; attempt++) made += TradeEngine.GenerateOffers(s, new Rng(1000 + attempt));
            Assert.Greater(made, 0, "offers appear over time");
            var pending = TradeEngine.PendingOffers(s);
            Assert.LessOrEqual(pending.Count, TradeEngine.MaxPendingOffers);
            TradeOffer offer = pending[0];
            Assert.AreEqual(s.UserTeamId, offer.ToTeamId);
            Assert.AreEqual(offer.FromTeamId, offer.Proposal.TeamA);
            Assert.AreEqual(s.UserTeamId, offer.Proposal.TeamB);
            TradeEvaluation ev = TradeEngine.Validate(s, offer.Proposal);
            Assert.IsTrue(ev.Legal, string.Join("; ", ev.Problems.ToArray()));
            int wantedId = offer.Proposal.FromB.First(a => !a.IsPick).PlayerId;
            // the offer is fair or generous to the user
            TradeEvaluation full = TradeEngine.Evaluate(s, offer.Proposal);
            float rawGet = offer.Proposal.FromA.Sum(a => TradeEngine.AssetValue(s, a)), rawGive = offer.Proposal.FromB.Sum(a => TradeEngine.AssetValue(s, a));
            Assert.GreaterOrEqual(rawGet, rawGive * 1.1f, "the CPU pays at least what the player is worth");
            OpResult acc = TradeEngine.AcceptOffer(s, offer.Id);
            Assert.IsTrue(acc.Ok, acc.Message);
            Assert.AreEqual(offer.FromTeamId, s.TeamOf(wantedId));
            Assert.AreEqual(TradeOfferStatus.Accepted, offer.Status);
            Assert.IsFalse(TradeEngine.AcceptOffer(s, offer.Id).Ok);
            // declining and expiry
            foreach (var o in TradeEngine.PendingOffers(s)) TradeEngine.RejectOffer(s, o.Id);
            Assert.AreEqual(0, TradeEngine.PendingOffers(s).Count);
            CareerTestUtil.AssertClean(s, false);
        }

        [Test]
        public void CpuTeamsTradeWithEachOtherAndEveryDealStaysLegal()
        {
            CareerSave s = CareerTestUtil.AtPhase(79, CareerPhase.Scouting);
            int tradeNews = s.News.Count(n => n.Kind == NewsKind.Trade);
            int txBefore = s.Transactions.Count(t => t.Kind == TransactionKind.Trade);
            int done = 0;
            for (int round = 0; round < 6; round++) done += TradeEngine.SimulateCpuTrades(s, new Rng(round + 5), 3, 80);
            Assert.GreaterOrEqual(done, 1, "the offseason market is alive");
            Assert.AreEqual(done, s.Transactions.Count(t => t.Kind == TransactionKind.Trade) - txBefore);
            // picks traded between CPU teams changed hands
            Assert.Greater(s.Picks.Count(k => k.OwnerTeamId != k.OriginalTeamId), 0);
            CareerTestUtil.AssertClean(s, false);
            foreach (Team t in s.Pro.Teams) Assert.LessOrEqual(t.CapUsed, t.SalaryCap + 1L, t.Nickname);
        }

        [Test]
        public void TradedPicksTravelIntoTheDraftAndFuturePicksSurviveTheYearRoll()
        {
            CareerSave s = CareerTestUtil.AtPhase(80, CareerPhase.Scouting);
            Team u = s.UserTeam;
            Team cpu = s.Pro.Teams.First(t => t.Id != u.Id);
            // the CPU team gives its first-round pick and a pick from next year to the user (user pays with a good player)
            DraftPickAsset first = TradeEngine.PicksOf(s, cpu.Id).First(k => k.Year == s.Year && k.Round == 1);
            DraftPickAsset future = TradeEngine.PicksOf(s, cpu.Id).First(k => k.Year == s.Year + 1 && k.Round == 4);
            first.OwnerTeamId = u.Id; future.OwnerTeamId = u.Id;   // as if traded (execution is covered by the other tests)
            CareerFlow.Advance(s);
            Assert.AreEqual(CareerPhase.Draft, s.Phase);
            DraftSlot slot = s.Draft.Slots.First(x => x.PickId == first.Id);
            Assert.AreEqual(u.Id, slot.TeamId);
            Assert.AreEqual(cpu.Id, slot.OriginalTeamId);
            Assert.AreEqual(224, s.Draft.Slots.Count);
            CareerFlow.AdvanceUntil(s, CareerPhase.Preseason);
            Assert.AreEqual(u.Id, TradeEngine.FindPick(s, future.Id).OwnerTeamId);
            Assert.AreEqual(3 * 7 * 32, s.Picks.Count(k => k.Year >= s.Year && k.Year <= s.Year + 2), "always the current and next two drafts");
            Assert.IsFalse(s.Picks.Any(k => k.Year == s.Year - 1 && !k.Used));
        }
    }
}
