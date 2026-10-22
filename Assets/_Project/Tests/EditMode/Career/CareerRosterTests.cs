using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>Roster management: 53-man roster, practice squad, injured reserve, cuts and waivers, depth chart editing.</summary>
    public class CareerRosterTests
    {
        private static Player WithBigContract(Team t)
        {
            return t.Roster.Where(p => p.Contract.YearsLeft >= 2 && Offseason.DeadMoneyOf(p.Contract) > 1000000L && !Offseason.IsStarter(t, p))
                .OrderByDescending(p => Offseason.DeadMoneyOf(p.Contract)).FirstOrDefault()
                ?? t.Roster.Where(p => p.Contract.YearsLeft >= 2).OrderByDescending(p => p.Contract.Aav).First();
        }

        [Test]
        public void ReleasingInSeasonWaivesThePlayerAndTheDeadMoneyIsCharged()
        {
            CareerSave s = CareerTestUtil.Fresh(31);
            CareerFlow.Advance(s);
            Team u = s.UserTeam;
            Player p = WithBigContract(u);
            long now, next; CapManager.DeadMoneyPreview(p.Contract, out now, out next);
            long deadBefore = u.DeadCap, deadNextBefore = u.DeadCapNext;
            long hit = p.Contract.CapHit;
            OpResult r = RosterManager.Release(s, u, p.Id);
            Assert.IsTrue(r.Ok, r.Message);
            Assert.IsFalse(u.Roster.Contains(p));
            Assert.IsNotNull(RosterManager.FindWaiver(s, p.Id));
            Assert.AreEqual(deadBefore + now, u.DeadCap);
            Assert.AreEqual(deadNextBefore + next, u.DeadCapNext);
            Assert.AreEqual(52, u.Roster.Count);
            Assert.AreSame(p, s.FindPlayer(p.Id), "a waived player can still be found");
            // the waivers clear when the week is played: claimed (dead money disappears) or free agent (dead money stays)
            CareerFlow.Advance(s);
            Assert.IsNull(RosterManager.FindWaiver(s, p.Id));
            int owner = s.TeamOf(p.Id);
            if (owner >= 0)
            {
                Assert.AreNotEqual(u.Id, owner);
                Assert.AreEqual(deadBefore, u.DeadCap, "a claimed player takes his dead money with him");
                Assert.AreEqual(hit, p.Contract.CapHit, "the claimant takes over the same contract");
            }
            else
            {
                Assert.IsTrue(s.Pro.FreeAgents.Contains(p));
                Assert.AreEqual(0, p.Contract.YearsLeft);
                Assert.AreEqual(deadBefore + now, u.DeadCap);
            }
        }

        [Test]
        public void WaiverClaimsGoToTheWorstRecordFirstAndTheUserCanClaim()
        {
            CareerSave s = CareerTestUtil.Fresh(32);
            CareerFlow.Advance(s);
            for (int i = 0; i < 4; i++) CareerFlow.Advance(s);
            // another team waives a good player; the user, with a free roster spot, puts in a claim
            Team other = s.Pro.Teams.First(t => t.Id != s.UserTeamId);
            Player p = other.Roster.Where(x => !Offseason.IsStarter(other, x) && x.Contract.YearsLeft > 0).OrderByDescending(x => x.Overall).First();
            Assert.IsTrue(RosterManager.Release(s, other, p.Id).Ok);
            Team u = s.UserTeam;
            RosterManager.Release(s, u, u.Roster.Where(x => !Offseason.IsStarter(u, x)).OrderBy(x => x.Overall).First().Id);   // open a spot
            Assert.IsTrue(RosterManager.ClaimWaiver(s, p.Id).Ok);
            Assert.IsFalse(RosterManager.ClaimWaiver(s, 123456789).Ok);
            List<int> order = RosterManager.WaiverOrder(s);
            Assert.AreEqual(32, order.Count);
            var rows = s.Season.Standings().ToDictionary(r => r.TeamId);
            Assert.LessOrEqual(rows[order[0]].Pct, rows[order[31]].Pct, "worst record first");
            Assert.AreEqual(order.IndexOf(u.Id) + 1, RosterManager.WaiverPriority(s, u.Id));
            CareerFlow.Advance(s);
            Assert.IsNull(RosterManager.FindWaiver(s, p.Id));
            CareerTestUtil.AssertClean(s, false);
        }

        [Test]
        public void ReleasingInTheOffseasonSendsThePlayerStraightToFreeAgency()
        {
            CareerSave s = CareerTestUtil.AtPhase(33, CareerPhase.Scouting);
            Team u = s.UserTeam;
            Player p = u.Roster.Where(x => x.Contract.YearsLeft >= 1).OrderBy(x => x.Overall).First();
            OpResult r = RosterManager.Release(s, u, p.Id);
            Assert.IsTrue(r.Ok);
            Assert.IsTrue(s.Pro.FreeAgents.Contains(p));
            Assert.AreEqual(0, p.Contract.YearsLeft);
            Assert.AreEqual(u.Id, RosterManager.PriorTeamOf(s, p.Id));
            Assert.IsNull(RosterManager.FindWaiver(s, p.Id));
        }

        [Test]
        public void PracticeSquadRulesAreEnforced()
        {
            CareerSave s = CareerTestUtil.Fresh(34);
            Team u = s.UserTeam;
            Assert.AreEqual(16, u.PracticeSquad.Count);
            Player youngster = u.Roster.Where(p => p.Experience <= 1 && p.Contract.Aav <= Contract.MinSalary * 2).OrderBy(p => p.Overall).FirstOrDefault();
            // full squad: moving anybody fails
            if (youngster != null) Assert.IsFalse(RosterManager.ToPracticeSquad(s, u, youngster.Id).Ok);
            // make room, then veterans and big contracts are refused
            Player cut = u.PracticeSquad[0];
            Assert.IsTrue(RosterManager.Release(s, u, cut.Id).Ok);
            Assert.AreEqual(15, u.PracticeSquad.Count);
            Player vet = u.Roster.First(p => p.Experience >= 6 && !Offseason.IsStarter(u, p));
            OpResult veto = RosterManager.ToPracticeSquad(s, u, vet.Id);
            Assert.IsFalse(veto.Ok);
            Assert.AreEqual(53, u.Roster.Count);
            if (youngster != null)
            {
                OpResult ok = RosterManager.ToPracticeSquad(s, u, youngster.Id);
                Assert.IsTrue(ok.Ok, ok.Message);
                Assert.AreEqual(52, u.Roster.Count);
                Assert.AreEqual(16, u.PracticeSquad.Count);
                Assert.Less(youngster.Contract.CapHit, Contract.MinSalary, "practice squad deals are below the minimum");
                // promote him back
                Assert.IsTrue(RosterManager.Promote(s, u, youngster.Id).Ok);
                Assert.AreEqual(53, u.Roster.Count);
                Assert.IsFalse(RosterManager.Promote(s, u, youngster.Id).Ok, "no longer on the squad");
            }
            Assert.LessOrEqual(u.CapUsed, u.SalaryCap);
        }

        [Test]
        public void InjuredReserveNeedsALongInjuryAndAMinimumStay()
        {
            CareerSave s = CareerTestUtil.Fresh(35);
            CareerFlow.Advance(s);
            Team u = s.UserTeam;
            Player p = u.Roster.First(x => x.Position == Position.RB);
            p.Injury.WeeksOut = 2; p.Injury.Type = "Sprained ankle";
            Assert.IsFalse(RosterManager.PlaceOnIR(s, u, p.Id).Ok, "two weeks is not an IR injury");
            p.Injury.WeeksOut = 5;
            long capWith = u.CapUsed;
            Assert.IsTrue(RosterManager.PlaceOnIR(s, u, p.Id).Ok);
            Assert.AreEqual(52, u.Roster.Count);
            Assert.IsTrue(u.InjuredReserve.Contains(p));
            Assert.AreEqual(capWith, u.CapUsed, "injured reserve players still count on the cap");
            Assert.AreEqual(0, p.Injury.IrWeeks);
            Assert.AreSame(p, s.UserTeam.FindAnyPlayer(p.Id));
            Assert.IsFalse(RosterManager.ActivateFromIR(s, u, p.Id).Ok, "still hurt");
            for (int w = 0; w < 5; w++) CareerFlow.Advance(s);
            Assert.AreEqual(0, p.Injury.WeeksOut);
            Assert.GreaterOrEqual(p.Injury.IrWeeks, 5);
            u.Roster.RemoveAt(u.Roster.Count - 1);   // make room: the roster may be full from auto signings
            u.InvalidateIndex();
            OpResult back = RosterManager.ActivateFromIR(s, u, p.Id);
            Assert.IsTrue(back.Ok, back.Message);
            Assert.IsTrue(u.Roster.Contains(p));
            Assert.AreEqual(-1, p.Injury.IrWeeks);
        }

        [Test]
        public void CpuTeamsPutLongTermInjuriesOnReserveAndRefillTheRoster()
        {
            CareerSave s = CareerTestUtil.Fresh(36);
            CareerFlow.Advance(s);
            Team cpu = s.Pro.Teams.First(t => t.Id != s.UserTeamId);
            var hurt = cpu.Roster.Where(p => Offseason.IsStarter(cpu, p)).Take(3).ToList();
            foreach (Player p in hurt) { p.Injury.WeeksOut = 12; p.Injury.Type = "Torn ACL"; }
            CareerFlow.Advance(s);
            CareerFlow.Advance(s);
            foreach (Player p in hurt)
            {
                Assert.IsTrue(cpu.InjuredReserve.Contains(p) || !cpu.Roster.Contains(p) || p.Injury.WeeksOut <= 0, p.Name + " should have been moved");
            }
            Assert.AreEqual(53, cpu.Roster.Count, "replacements were signed");
            Assert.GreaterOrEqual(cpu.InjuredReserve.Count, 1);
            Assert.LessOrEqual(cpu.CapUsed, cpu.SalaryCap + 3000000L);
            CareerTestUtil.AssertClean(s, false);
        }

        [Test]
        public void DepthChartEditsAreValidatedAndStayManual()
        {
            CareerSave s = CareerTestUtil.Fresh(37);
            Team u = s.UserTeam;
            var qbs = u.Depth.Get(Position.QB).ToList();
            Assert.GreaterOrEqual(qbs.Count, 2);
            int first = qbs[0], second = qbs[1];
            Assert.IsTrue(DepthManager.Swap(s, u, Position.QB, 0, 1).Ok);
            Assert.AreEqual(second, u.Depth.StarterId(Position.QB));
            Assert.IsTrue(DepthManager.IsManual(s, u.Id, Position.QB));
            // a kicker cannot play quarterback, an unknown player cannot be listed
            Player kicker = u.Roster.First(p => p.Position == Position.K);
            Assert.IsFalse(DepthManager.Insert(s, u, Position.QB, kicker.Id, 0).Ok);
            Assert.IsFalse(DepthManager.Insert(s, u, Position.QB, 999999, 0).Ok);
            Assert.IsFalse(DepthManager.SetOrder(s, u, Position.QB, new[] { first, first }).Ok);
            // a guard may line up at tackle
            Player guard = u.Roster.First(p => p.Position == Position.LG);
            Assert.IsTrue(DepthManager.IsEligible(guard, Position.LT));
            Assert.IsTrue(DepthManager.Insert(s, u, Position.LT, guard.Id, 1).Ok);
            Assert.IsTrue(u.Depth.Get(Position.LT).Contains(guard.Id));
            // roster moves rebuild the chart but the hand-made QB order survives
            Player extra = s.Pro.FreeAgents.OrderByDescending(p => p.Overall).First(p => p.Position == Position.WR);
            s.Pro.FreeAgents.Remove(extra); extra.Contract = CapMath.Minimum(s, 1, s.Year); Offseason.AssignJersey(u, extra, new Rng(2)); u.AddPlayer(extra);
            DepthManager.Refresh(s, u);
            Assert.AreEqual(second, u.Depth.StarterId(Position.QB));
            Assert.IsTrue(u.Depth.Get(Position.WR).Contains(extra.Id) || u.Depth.Get(Position.WR).Count > 0);
            // back to automatic
            DepthManager.AutoSort(s, u, Position.QB);
            Assert.IsFalse(DepthManager.IsManual(s, u.Id, Position.QB));
            Player best = u.Roster.Where(p => p.Position == Position.QB).OrderByDescending(p => p.Overall).First();
            Assert.AreEqual(best.Id, u.Depth.StarterId(Position.QB));
            Assert.AreEqual(0, DepthManager.Issues(u).Count(x => x.Contains("starters")));
            // candidates and entries for the screen
            Assert.Greater(DepthManager.Entries(s, u, Position.WR).Count, 4);
            Assert.IsTrue(DepthManager.Candidates(u, Position.LT).All(p => DepthManager.IsEligible(p, Position.LT)));
        }

        [Test]
        public void TheSimulationSkipsInjuredStartersListedOnTheChart()
        {
            CareerSave s = CareerTestUtil.Fresh(38);
            CareerFlow.Advance(s);
            Team u = s.UserTeam;
            Player qb = u.GetPlayer(u.Depth.StarterId(Position.QB));
            qb.Injury.WeeksOut = 3; qb.Injury.Type = "Shoulder";
            StepResult r = CareerFlow.Advance(s);
            Assert.IsTrue(r.Ok);
            if (r.Week.UserResult != null)
            {
                PlayerGameLine line = r.Week.UserResult.FindLine(qb.Id);
                Assert.IsTrue(line == null || line.Stats.PassAtt == 0, "an injured quarterback does not throw");
            }
            var issues = DepthManager.Issues(u);
            Assert.IsNotNull(issues);
        }

        [Test]
        public void FinalCutsTrimOversizedRostersToFiftyThreeWithAFullSquad()
        {
            CareerSave s = CareerTestUtil.AtPhase(39, CareerPhase.Preseason);
            Assert.AreEqual(2027, s.Year);
            // before the cuts CPU rosters are bigger than 53 (draft picks, free agents)
            Assert.Greater(s.Pro.Teams.Count(t => t.Roster.Count > 53), 5);
            CareerFlow.AutoResolveUser(s);
            CareerFlow.Advance(s);
            CareerTestUtil.AssertClean(s, true);
            foreach (Team t in s.Pro.Teams)
            {
                Assert.AreEqual(53, t.Roster.Count);
                Assert.GreaterOrEqual(t.PracticeSquad.Count, 12);
                Assert.IsTrue(t.Roster.Any(p => p.Position == Position.K) && t.Roster.Any(p => p.Position == Position.P) && t.Roster.Count(p => p.Position == Position.QB) >= 2);
            }
        }
    }
}
