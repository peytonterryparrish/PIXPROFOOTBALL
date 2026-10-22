using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>Coaching staff (hiring, firing, development, scheme fit, play-calling data) and player/team morale.</summary>
    public class CareerStaffMoraleTests
    {
        [Test]
        public void EveryTeamHasThreeCoachesWithSalariesInsideTheBudget()
        {
            CareerSave s = CareerTestUtil.Fresh(101);
            foreach (Team t in s.Pro.Teams)
            {
                Assert.IsNotNull(t.HeadCoach); Assert.IsNotNull(t.OffensiveCoordinator); Assert.IsNotNull(t.DefensiveCoordinator);
                Assert.Greater(StaffManager.Payroll(s, t), 0L);
                Assert.LessOrEqual(StaffManager.Payroll(s, t), StaffManager.Budget(s, t) + 1L, t.Nickname);
            }
            Assert.GreaterOrEqual(s.CoachPool.Count, 24);
            Assert.IsTrue(s.CoachPool.All(c => c.AskSalary > 0 && c.Years >= 2));
        }

        [Test]
        public void FiringLeavesAVacancyThatBlocksCampUntilTheUserHires()
        {
            CareerSave s = CareerTestUtil.AtPhase(102, CareerPhase.TrainingCamp);
            Team u = s.UserTeam;
            Coach oc = u.OffensiveCoordinator;
            int poolBefore = s.CoachPool.Count;
            OpResult fire = StaffManager.Fire(s, u, CoachRole.OffensiveCoordinator);
            Assert.IsTrue(fire.Ok);
            Assert.IsNull(u.OffensiveCoordinator);
            Assert.IsNull(StaffManager.ContractOf(s, u.Id, oc.Id));
            Assert.AreEqual(poolBefore + (oc.Age < 66 ? 1 : 0), s.CoachPool.Count);
            Assert.IsTrue(StaffManager.HasVacancy(u));
            Assert.IsFalse(StaffManager.Fire(s, u, CoachRole.OffensiveCoordinator).Ok);
            Assert.Greater(CareerFlow.Blockers(s).Count, 0);
            Assert.IsFalse(CareerFlow.Advance(s).Ok);
            // an unaffordable star is refused, an affordable candidate is hired
            CoachCandidate star = s.CoachPool.Where(c => c.Coach.Role == CoachRole.OffensiveCoordinator).OrderByDescending(c => c.AskSalary).First();
            long payrollWithout = StaffManager.Payroll(s, u);
            long saved = star.AskSalary;
            star.AskSalary = StaffManager.Budget(s, u) - payrollWithout + 1000000L;
            Assert.IsFalse(StaffManager.Hire(s, u, star.Coach.Id).Ok);
            star.AskSalary = saved;
            CoachCandidate cheap = s.CoachPool.Where(c => c.Coach.Role == CoachRole.OffensiveCoordinator && payrollWithout + c.AskSalary <= StaffManager.Budget(s, u)).OrderByDescending(c => StaffManager.CandidateValue(c.Coach)).First();
            OpResult hire = StaffManager.Hire(s, u, cheap.Coach.Id);
            Assert.IsTrue(hire.Ok, hire.Message);
            Assert.AreSame(cheap.Coach, u.OffensiveCoordinator);
            Assert.AreEqual(cheap.AskSalary, StaffManager.ContractOf(s, u.Id, cheap.Coach.Id).Salary);
            Assert.IsNull(StaffManager.FindCandidate(s, cheap.Coach.Id));
            Assert.AreEqual(0, CareerFlow.Blockers(s).Count);
            Assert.IsTrue(s.News.Any(n => n.Kind == NewsKind.Coach));
            // replacing a sitting coordinator fires him first
            CoachCandidate dc = s.CoachPool.First(c => c.Coach.Role == CoachRole.DefensiveCoordinator && StaffManager.Payroll(s, u) - StaffManager.ContractOf(s, u.Id, u.DefensiveCoordinator.Id).Salary + c.AskSalary <= StaffManager.Budget(s, u));
            Coach oldDc = u.DefensiveCoordinator;
            Assert.IsTrue(StaffManager.Hire(s, u, dc.Coach.Id).Ok);
            Assert.AreSame(dc.Coach, u.DefensiveCoordinator);
            Assert.IsNotNull(StaffManager.FindCandidate(s, oldDc.Id), "the fired coach is on the market");
            Assert.IsFalse(StaffManager.Hire(s, u, dc.Coach.Id, false).Ok);
        }

        [Test]
        public void CpuTeamsKeepFullStaffsAndChangeCoachesOverTheYears()
        {
            CareerSave s = CareerTestUtil.Fresh(103);
            var names0 = s.Pro.Teams.Select(t => t.HeadCoach.Name).ToList();
            CareerFlow.SimToNextSeason(s);
            CareerFlow.SimToNextSeason(s);
            foreach (Team t in s.Pro.Teams.Where(t => t.Id != s.UserTeamId))
            {
                Assert.IsFalse(StaffManager.HasVacancy(t), t.Nickname);
                Assert.LessOrEqual(StaffManager.Payroll(s, t), StaffManager.Budget(s, t) * 1.3, t.Nickname);
            }
            int changed = 0;
            for (int i = 0; i < 32; i++) if (s.Pro.Teams[i].HeadCoach.Name != names0[i]) changed++;
            Assert.Greater(changed, 0, "somebody was fired or retired in two years");
            Assert.GreaterOrEqual(s.CoachPool.Count, 12);
        }

        [Test]
        public void SchemeFitFollowsTheCoordinatorsScheme()
        {
            CareerSave s = CareerTestUtil.Fresh(104);
            Team t = s.UserTeam;
            t.OffensiveCoordinator.OffScheme = OffenseScheme.AirRaid;
            Player gunslinger = new Player { Position = Position.QB, Archetype = Archetype.QbGunslinger, Ratings = Ratings.Uniform(80) };
            Player power = new Player { Position = Position.RB, Archetype = Archetype.RbPower, Ratings = Ratings.Uniform(80) };
            Player pocket = new Player { Position = Position.QB, Archetype = Archetype.QbPocket, Ratings = Ratings.Uniform(80) };
            Assert.AreEqual(1f, StaffManager.SchemeFit(t, gunslinger));
            Assert.AreEqual(-0.5f, StaffManager.SchemeFit(t, power));
            Assert.AreEqual(0f, StaffManager.SchemeFit(t, pocket));
            t.OffensiveCoordinator.OffScheme = OffenseScheme.PowerRun;
            Assert.AreEqual(1f, StaffManager.SchemeFit(t, power));
            Assert.AreEqual(0f, StaffManager.SchemeFit(t, new Player { Position = Position.K, Archetype = Archetype.KPlacekicker, Ratings = Ratings.Uniform(80) }));
            t.DefensiveCoordinator.DefScheme = DefenseScheme.Aggressive;
            Assert.AreEqual(1f, StaffManager.SchemeFit(t, new Player { Position = Position.RE, Archetype = Archetype.DlPassRusher, Ratings = Ratings.Uniform(80) }));
            // the fit changes the development figure and the team-level fit score
            int dev1 = StaffManager.DevelopmentRating(t, power);
            t.OffensiveCoordinator.OffScheme = OffenseScheme.AirRaid;
            int dev2 = StaffManager.DevelopmentRating(t, power);
            Assert.Greater(dev1, dev2);
            Assert.That(StaffManager.TeamSchemeFit(t, true), Is.InRange(0, 100));
        }

        [Test]
        public void BetterCoachingProducesBetterProgression()
        {
            var gen = new Rng(3);
            double high = 0, low = 0;
            int n = 600;
            for (int i = 0; i < n; i++)
            {
                Player a = PlayerFactory.Create(gen, new TestIds(), new HashSet<string>(), Position.WR, 76, 23);
                Player b = PlayerFactory.Create(new Rng(3 + i), new TestIds(), new HashSet<string>(), Position.WR, 76, 23);
                Player a2 = PlayerFactory.Create(new Rng(3 + i), new TestIds(), new HashSet<string>(), Position.WR, 76, 23);
                int o1 = b.Overall, o2 = a2.Overall;
                Progression.Advance(b, new Rng(100 + i), 95);
                Progression.Advance(a2, new Rng(100 + i), 35);
                high += b.Overall - o1; low += a2.Overall - o2;
            }
            Assert.Greater(high / n, low / n + 1.0, "a 95 coach adds about 2.7 overall a year over a 35 coach");
        }

        [Test]
        public void PlaycallTendenciesComeFromTheCoachesPreferences()
        {
            CareerSave s = CareerTestUtil.Fresh(105);
            Team t = s.UserTeam;
            t.OffensiveCoordinator.PassTendency = 80; t.OffensiveCoordinator.OffScheme = OffenseScheme.AirRaid;
            t.HeadCoach.Aggression = 90;
            t.DefensiveCoordinator.DefScheme = DefenseScheme.Aggressive; t.DefensiveCoordinator.Aggression = 80;
            PlaycallTendencies air = StaffManager.Tendencies(t);
            t.OffensiveCoordinator.PassTendency = 35; t.OffensiveCoordinator.OffScheme = OffenseScheme.PowerRun;
            t.HeadCoach.Aggression = 15;
            t.DefensiveCoordinator.DefScheme = DefenseScheme.Zone; t.DefensiveCoordinator.Aggression = 20;
            PlaycallTendencies run = StaffManager.Tendencies(t);
            Assert.Greater(air.PassRate, run.PassRate + 0.15f);
            Assert.Greater(air.DeepShotRate, run.DeepShotRate);
            Assert.Greater(air.FourthDownAggression, run.FourthDownAggression);
            Assert.Greater(air.BlitzRate, run.BlitzRate + 0.1f);
            Assert.Greater(air.ManCoverageRate, run.ManCoverageRate);
            Assert.That(air.PassRate, Is.InRange(0.35f, 0.70f));
            Assert.AreEqual(OffenseScheme.AirRaid, air.OffScheme);
        }

        [Test]
        public void MoraleExplainsItselfAndFollowsPlayingTimeContractAndWinning()
        {
            CareerSave s = CareerTestUtil.Fresh(106);
            Team t = s.UserTeam;
            Player starter = t.Roster.First(p => Offseason.IsStarter(t, p) && p.Position == Position.QB);
            PlayerMoraleInfo info = MoraleEngine.Breakdown(s, t, starter);
            Assert.IsTrue(info.Factors.Any(f => f.Label == "Starting role" && f.Value > 0));
            Assert.IsTrue(info.Factors.Any(f => f.Label == "Team success"));
            Assert.AreEqual(Math.Max(15, Math.Min(100, 62 + info.Factors.Sum(f => f.Value))), info.Target);
            // a benched player who is as good as the starter wants more playing time
            Player backup = t.Roster.First(p => p.Position == Position.WR && !Offseason.IsStarter(t, p));
            backup.Ratings = Ratings.Uniform(95);   // better than everybody
            var angry = MoraleEngine.Breakdown(s, t, backup);
            Assert.IsTrue(angry.Factors.Any(f => f.Value <= -8), "wants more playing time");
            // an underpaid star
            Player star = t.Roster.OrderByDescending(p => p.Overall).First(p => p.Contract.YearsLeft > 0);
            long pay = star.Contract.Aav;
            star.Contract = Contract.Create(star.Contract.Years, Contract.MinSalary * 2, 0f, 0f, 2026);
            Assert.IsTrue(MoraleEngine.Breakdown(s, t, star).Factors.Any(f => f.Label.StartsWith("Underpaid")));
            // the weekly update moves a quarter of the way to the target
            starter.Morale = 40;
            int target = MoraleEngine.Breakdown(s, t, starter).Target;
            MoraleEngine.UpdateWeek(s, new Rng(4));
            Assert.Greater(starter.Morale, 40);
            Assert.LessOrEqual(starter.Morale, target + 2);
            // data hooks for the sim
            starter.Morale = 100; Assert.AreEqual(2f, MoraleEngine.RatingModifier(starter));
            starter.Morale = 0; Assert.AreEqual(-2f, MoraleEngine.RatingModifier(starter));
            starter.Morale = 70; Assert.AreEqual(0f, MoraleEngine.RatingModifier(starter));
            Assert.That(MoraleEngine.TeamMorale(s, t.Id), Is.InRange(0, 100));
        }

        [Test]
        public void WinningTeamsAreHappierThanLosingOnesAfterASeason()
        {
            CareerSave s = CareerTestUtil.Fresh(107);
            CareerFlow.AdvanceUntil(s, CareerPhase.Playoffs);
            var rows = s.Season.Standings();
            int best = rows.OrderByDescending(r => r.Pct).First().TeamId, worst = rows.OrderBy(r => r.Pct).First().TeamId;
            Assert.Greater(MoraleEngine.TeamMorale(s, best), MoraleEngine.TeamMorale(s, worst));
            double avgBest = s.Pro.GetTeam(best).Roster.Average(p => p.Morale), avgWorst = s.Pro.GetTeam(worst).Roster.Average(p => p.Morale);
            Assert.Greater(avgBest, avgWorst + 3);
            Assert.AreEqual(s.Pro.Teams.Count, s.Morale.Count);
        }

        [Test]
        public void MoraleChangesWhatPlayersAskForAndWhetherTheyReSign()
        {
            CareerSave s = CareerTestUtil.AtPhase(108, CareerPhase.ReSign);
            Team t = s.UserTeam;
            var exp = Negotiator.ExpiringPlayers(s, t);
            double happy = 0, sour = 0;
            foreach (Player p in exp)
            {
                int m = p.Morale;
                p.Morale = 95; happy += Negotiator.Ask(s, t, p).Aav;
                p.Morale = 35; sour += Negotiator.Ask(s, t, p).Aav;
                p.Morale = m;
            }
            Assert.Greater(sour, happy * 1.05);
            Player one = exp[0];
            one.Morale = 95; float hi = Negotiator.CpuKeepProbability(s, t, one);
            one.Morale = 20; float lo = Negotiator.CpuKeepProbability(s, t, one);
            Assert.Greater(hi, lo);
        }

        private sealed class TestIds : IPlayerIdSource
        {
            private static int _next = 9000000;
            public int NextPlayerId() { return _next++; }
        }
    }
}
