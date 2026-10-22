using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>The view models the franchise screens bind to.</summary>
    public class CareerViewsTests
    {
        [Test]
        public void TheHubHomeShowsTheNextGameStandingsTickerAndGoals()
        {
            CareerSave s = CareerTestUtil.Fresh(161);
            HubHome h = CareerViews.Home(s);
            Assert.AreEqual(CareerPhase.Preseason, h.Phase);
            Assert.AreEqual("Start the season", h.NextStepLabel);
            Assert.IsNotNull(h.Next);
            Assert.IsNotNull(h.Next.Game, "the first game of the schedule is previewed in the preseason");
            Assert.AreEqual(1, h.Next.Week);
            Assert.AreEqual(4, h.Division.Count);
            Assert.Greater(h.Ticker.Count, 0);
            Assert.Greater(h.Goals.Count, 0);
            Assert.AreEqual(0, h.Blockers.Count);
            Assert.Greater(h.Next.You.Ovr, 50);
            Assert.Greater(h.Next.Them.Ovr, 50);
            Assert.AreEqual(s.UserTeam.TeamOverall(), h.Next.You.Ovr);
            Assert.AreEqual("0-0", h.Record);
            CareerFlow.Advance(s);
            for (int i = 0; i < 3; i++) CareerFlow.Advance(s);
            h = CareerViews.Home(s);
            Assert.AreEqual(s.UserTeam.TeamOverall(), h.Next == null || h.Next.You == null ? s.UserTeam.TeamOverall() : h.Next.You.Ovr);
            StringAssert.Contains("week", h.NextStepLabel.ToLowerInvariant());
            Assert.AreNotEqual("0-0", h.Record);
            Assert.GreaterOrEqual(h.CapSpace, 0);
            Assert.IsNotEmpty(h.LegacyTier);
            // the offseason labels
            CareerSave off = CareerTestUtil.AtPhase(162, CareerPhase.FreeAgency);
            Assert.AreEqual("Advance one day", CareerViews.NextStepLabel(off));
            Assert.IsNull(CareerViews.Home(off).Next);
        }

        [Test]
        public void ScheduleAndPowerRankingsCoverTheLeague()
        {
            CareerSave s = CareerTestUtil.Fresh(163);
            CareerFlow.Advance(s);
            for (int i = 0; i < 5; i++) CareerFlow.Advance(s);
            List<ScheduleRow> sched = CareerViews.Schedule(s, s.UserTeamId);
            Assert.AreEqual(18, sched.Count);
            Assert.AreEqual(1, sched.Count(r => r.Bye));
            Assert.AreEqual(17, sched.Count(r => !r.Bye));
            Assert.AreEqual(5 - sched.Take(5).Count(r => r.Bye), sched.Count(r => r.Played), "five weeks were played");
            foreach (ScheduleRow r in sched.Where(r => r.Played)) Assert.IsTrue(r.Tie || r.Won == (r.TeamScore > r.OpponentScore));
            var pr = CareerViews.PowerRankings(s);
            Assert.AreEqual(32, pr.Count);
            Assert.AreEqual(Enumerable.Range(1, 32).ToList(), pr.Select(r => r.Rank).ToList());
            Assert.AreEqual(32, pr.Select(r => r.TeamId).Distinct().Count());
        }

        [Test]
        public void ThePlayerCardHasEverythingForTheDetailScreen()
        {
            CareerSave s = CareerTestUtil.Fresh(164);
            CareerFlow.Advance(s);
            for (int i = 0; i < 3; i++) CareerFlow.Advance(s);
            Team u = s.UserTeam;
            Player p = u.Roster.Where(x => x.Abilities.Count > 0 && x.FindStats(s.Year) != null).OrderByDescending(x => x.Overall).First();
            PlayerCard c = CareerViews.Card(s, p.Id);
            Assert.AreEqual(p.Name, c.Name);
            Assert.AreEqual(p.Overall, c.Overall);
            Assert.AreEqual(RosterStatus.Active, c.Status);
            Assert.AreEqual(u.FullName, c.TeamName);
            Assert.Greater(c.Ratings.Count, 5);
            Assert.GreaterOrEqual(c.Ratings[0].Value, c.Ratings[c.Ratings.Count - 1].Value - 100);
            Assert.AreEqual(p.Abilities.Count, c.Abilities.Count);
            Assert.IsNotEmpty(c.Abilities[0].Name);
            Assert.IsTrue(c.Abilities[0].Stars.Length >= 1);
            StringAssert.Contains(c.Abilities[0].Name, c.Abilities[0].Effects);
            Assert.AreEqual(p.Contract.Aav, c.Aav);
            Assert.AreEqual(Offseason.DeadMoneyOf(p.Contract), c.DeadIfReleased);
            Assert.Greater(c.MarketValue, 0L);
            Assert.IsNotNull(c.Morale);
            Assert.Greater(c.Morale.Factors.Count, 2);
            Assert.GreaterOrEqual(c.Seasons.Count, 1);
            Assert.Greater(c.TradeValue, 0f);
            Assert.IsNotEmpty(c.Height);
            // practice squad and free agents work too, unknown players give null
            Player ps = u.PracticeSquad[0];
            Assert.AreEqual(RosterStatus.PracticeSquad, CareerViews.Card(s, ps.Id).Status);
            Player fa = s.Pro.FreeAgents[0];
            Assert.AreEqual(RosterStatus.FreeAgent, CareerViews.Card(s, fa.Id).Status);
            Assert.AreEqual("Free agent", CareerViews.Card(s, fa.Id).TeamName);
            Assert.IsNull(CareerViews.Card(s, -77));
        }

        [Test]
        public void TheRosterTableNeedsAndInjuryReportsReadTheTeam()
        {
            CareerSave s = CareerTestUtil.Fresh(165);
            Team u = s.UserTeam;
            var rows = CareerViews.RosterTable(s, u);
            Assert.AreEqual(53 + 16, rows.Count);
            Assert.AreEqual(53, CareerViews.RosterTable(s, u, false).Count);
            Assert.AreEqual(53, rows.Count(r => r.Status == RosterStatus.Active));
            Assert.IsTrue(rows.Where(r => r.Starter).All(r => r.Status == RosterStatus.Active));
            var needs = CareerViews.Needs(s, u);
            Assert.AreEqual(LeagueRules.RosterOrder.Length, needs.Count);
            for (int i = 1; i < needs.Count; i++) Assert.GreaterOrEqual(needs[i - 1].Need, needs[i].Need);
            Player hurt = u.Roster.First(p => Offseason.IsStarter(u, p));
            hurt.Injury.WeeksOut = 4; hurt.Injury.Type = "Calf strain";
            var inj = CareerViews.InjuryReport(s, u.Id);
            Assert.AreEqual(1, inj.Count);
            Assert.IsTrue(inj[0].Starter);
            Assert.AreEqual(4, inj[0].Weeks);
            var r1 = CareerViews.Ratings(u);
            Assert.AreEqual(u.TeamOverall(), r1.Ovr);
            Assert.That(r1.Off, Is.InRange(55, 99)); Assert.That(r1.Def, Is.InRange(55, 99));
        }
    }
}
