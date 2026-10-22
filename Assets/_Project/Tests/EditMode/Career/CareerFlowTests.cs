using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>The franchise calendar: creation, the phase machine, weekly play/sim, blockers and determinism.</summary>
    public class CareerFlowTests
    {
        [Test]
        public void NewCareerStartsInPreseasonWithLegalRosters()
        {
            CareerSave s = CareerTestUtil.Fresh(11);
            Assert.AreEqual(CareerPhase.Preseason, s.Phase);
            Assert.AreEqual(32, s.Pro.Teams.Count);
            Assert.AreEqual(5, s.UserTeamId);
            Assert.AreEqual(LeagueRules.SalaryCap, s.Pro.Teams[0].SalaryCap);
            CareerTestUtil.AssertClean(s, true);
            for (int t = 0; t < 32; t++)
            {
                Team team = s.Pro.Teams[t];
                Assert.AreEqual(53, team.Roster.Count, team.Nickname);
                Assert.AreEqual(16, team.PracticeSquad.Count, team.Nickname);
                Assert.LessOrEqual(team.CapUsed, team.SalaryCap, team.Nickname);
            }
            // three draft years of picks exist, one per team and round
            Assert.AreEqual(3 * 7 * 32, s.Picks.Count);
            // abilities were handed out
            int withAbility = s.Pro.AllRosterPlayers().Count(p => p.Abilities.Count > 0);
            Assert.Greater(withAbility, 200);
            // goals and owner
            Assert.Greater(s.Goals.Count, 0);
            Assert.Greater(s.News.Count, 0);
        }

        [Test]
        public void SameSeedBuildsTheSameCareerAndOtherSeedsDiffer()
        {
            Assert.AreEqual(CareerTestUtil.Fingerprint(CareerTestUtil.Fresh(21)), CareerTestUtil.Fingerprint(CareerTestUtil.Fresh(21)));
            Assert.AreNotEqual(CareerTestUtil.Fingerprint(CareerTestUtil.Fresh(21)), CareerTestUtil.Fingerprint(CareerTestUtil.Fresh(22)));
        }

        [Test]
        public void ThePhasesRunInOrderThroughAFullYear()
        {
            CareerSave s = CareerTestUtil.Fresh(12);
            var seen = new List<CareerPhase> { s.Phase };
            int weeks = 0, steps = 0;
            int startYear = s.Year;
            while (!(s.Year == startYear + 1 && s.Phase == CareerPhase.Preseason) && steps++ < 200)
            {
                List<string> b = CareerFlow.AutoResolveUser(s);
                Assert.AreEqual(0, b.Count, "unresolved: " + string.Join("; ", b.ToArray()));
                CareerPhase before = s.Phase;
                StepResult r = CareerFlow.Advance(s);
                Assert.IsTrue(r.Ok, string.Join("; ", r.Blockers.ToArray()));
                if (r.Week != null) weeks++;
                if (seen[seen.Count - 1] != s.Phase) seen.Add(s.Phase);
            }
            CareerPhase[] expected =
            {
                CareerPhase.Preseason, CareerPhase.RegularSeason, CareerPhase.Playoffs, CareerPhase.PixBowl, CareerPhase.Awards, CareerPhase.Retirements,
                CareerPhase.ReSign, CareerPhase.Scouting, CareerPhase.Draft, CareerPhase.RookieSigning, CareerPhase.FreeAgency, CareerPhase.TrainingCamp, CareerPhase.Preseason
            };
            Assert.AreEqual(string.Join(",", expected.Select(x => x.ToString()).ToArray()), string.Join(",", seen.Select(x => x.ToString()).ToArray()));
            Assert.AreEqual(22, weeks, "18 regular weeks + wild card + divisional + conference + PIX Bowl");
            Assert.AreEqual(startYear + 1, s.Year);
            Assert.AreEqual(startYear + 1, s.Season.Year);
            Assert.AreEqual(1, s.Pro.History.Count);
            CareerTestUtil.AssertClean(s, false);
        }

        [Test]
        public void AWeekReportCarriesTheResultsInjuriesAndLabel()
        {
            CareerSave s = CareerTestUtil.Fresh(13);
            CareerFlow.Advance(s);   // preseason -> regular season
            Assert.AreEqual(CareerPhase.RegularSeason, s.Phase);
            Assert.AreEqual("Week 1 of 18", CareerText.PhaseLabel(s));
            StepResult r = CareerFlow.Advance(s);
            Assert.IsNotNull(r.Week);
            Assert.AreEqual(1, r.Week.Week);
            Assert.GreaterOrEqual(r.Week.Results.Count, 15);
            Assert.AreEqual(2, s.Season.Week);
            if (r.Week.UserGame != null)
            {
                Assert.IsNotNull(r.Week.UserResult);
                Assert.IsTrue(r.Week.UserGame.Played);
            }
            foreach (InjuryEvent e in r.Week.Injuries) Assert.Greater(e.Weeks, 0);
        }

        [Test]
        public void PlayingTheUserGameThroughTheHookPostsTheResultAndItsInjuries()
        {
            CareerSave s = CareerTestUtil.Fresh(14);
            CareerFlow.Advance(s);
            GameSetup gs = null;
            for (int guard = 0; guard < 20 && gs == null; guard++)
            {
                gs = CareerFlow.SetupUserGame(s);
                if (gs == null) CareerFlow.Advance(s);   // a bye week
            }
            Assert.IsNotNull(gs, "the user plays in the first 18 weeks");
            Assert.IsTrue(gs.Home.Id == s.UserTeamId || gs.Away.Id == s.UserTeamId);
            Assert.IsNotNull(gs.Rules);
            GameResult played = CareerTestUtil.PlayLikeUnity(s, gs, 99);
            // the match supplies its own injury: a starter out three weeks
            Team user = s.UserTeam;
            Player starter = user.GetPlayer(user.Depth.StarterId(Position.WR));
            played.InjuriesProvided = true;
            played.Injuries.Add(new GameInjury { PlayerId = starter.Id, Name = starter.Name, TeamId = user.Id, Pos = starter.Position, Type = "Hamstring strain", Weeks = 3 });
            int week = s.Season.Week;
            OpResult sub = CareerFlow.SubmitUserGame(s, played);
            Assert.IsTrue(sub.Ok, sub.Message);
            Assert.IsTrue(gs.Game.Played);
            Assert.AreEqual(played.HomeScore, gs.Game.HomeScore);
            Assert.IsFalse(CareerFlow.SubmitUserGame(s, played).Ok, "a game can only be submitted once");
            StepResult r = CareerFlow.Advance(s);
            Assert.AreEqual(week + 1, s.Season.Week);
            Assert.AreSame(played, r.Week.UserResult);
            // injured at least the game after it: the week's heal only touches players who were hurt at kickoff
            Assert.IsTrue(starter.Injury.IsInjured || user.InjuredReserve.Contains(starter));
            Assert.AreEqual(3, starter.Injury.WeeksOut);
            Assert.IsTrue(r.Week.Injuries.Any(e => e.PlayerId == starter.Id && e.Weeks == 3));
            // stats were posted
            Assert.Greater(user.Roster.Sum(p => p.FindStats(s.Year) != null ? p.FindStats(s.Year).Games : 0) + user.InjuredReserve.Count, 20);
        }

        [Test]
        public void AnyGameOfTheWeekCanBePlayedOrSimulatedOneByOne()
        {
            CareerSave s = CareerTestUtil.Fresh(151);
            CareerFlow.Advance(s);
            var games = CareerFlow.GamesThisWeek(s);
            Assert.GreaterOrEqual(games.Count, 15);
            ScheduledGame other = games.First(g => !g.Involves(s.UserTeamId));
            ScheduledGame another = games.Last(g => !g.Involves(s.UserTeamId) && g.Id != other.Id);
            // spectate one in the match, simulate another now and read its box score
            GameSetup gs = CareerFlow.SetupGame(s, other.Id);
            Assert.IsNotNull(gs);
            Assert.IsFalse(gs.UserIsHome);
            GameResult played = CareerTestUtil.PlayLikeUnity(s, gs, 5);
            Assert.IsTrue(CareerFlow.SubmitGame(s, other.Id, played).Ok);
            GameResult simmed = CareerFlow.SimGame(s, another.Id);
            Assert.IsNotNull(simmed);
            Assert.IsTrue(another.Played);
            Assert.AreEqual(simmed.HomeScore, another.HomeScore);
            Assert.IsNull(CareerFlow.SimGame(s, another.Id), "already played");
            Assert.IsNull(CareerFlow.SetupGame(s, other.Id), "already played");
            Assert.IsFalse(CareerFlow.SubmitGame(s, 99999, played).Ok);
            StepResult r = CareerFlow.Advance(s);
            Assert.AreEqual(games.Count, r.Week.Results.Count, "every game of the week is in the report exactly once");
            Assert.IsTrue(r.Week.Results.Contains(played) && r.Week.Results.Contains(simmed));
            Assert.AreEqual(2, s.Season.Week);
            Assert.IsTrue(games.All(g => g.Played));
        }

        [Test]
        public void PreseasonBlocksOnAnIllegalRosterAndTheAutoPilotFixesIt()
        {
            CareerSave s = CareerTestUtil.Fresh(15);
            Team u = s.UserTeam;
            // sign six street players: 59 > 53
            var pool = s.Pro.FreeAgents.OrderByDescending(p => p.Overall).Take(6).ToList();
            foreach (Player p in pool) { s.Pro.FreeAgents.Remove(p); p.Contract = CapMath.Minimum(s, 1, s.Year); Offseason.AssignJersey(u, p, new Rng(3)); u.AddPlayer(p); }
            Assert.AreEqual(59, u.Roster.Count);
            List<string> blockers = CareerFlow.Blockers(s);
            Assert.Greater(blockers.Count, 0);
            Assert.IsFalse(CareerFlow.Advance(s).Ok);
            Assert.AreEqual(CareerPhase.Preseason, s.Phase);
            Assert.AreEqual(0, CareerFlow.AutoResolveUser(s).Count);
            Assert.AreEqual(53, u.Roster.Count);
            Assert.IsTrue(CareerFlow.Advance(s).Ok);
            Assert.AreEqual(CareerPhase.RegularSeason, s.Phase);
            CareerTestUtil.AssertClean(s, true);
        }

        [Test]
        public void TheDraftBlocksWhileTheUserIsOnTheClock()
        {
            CareerSave s = CareerTestUtil.AtPhase(16, CareerPhase.Scouting);
            Assert.IsTrue(CareerFlow.Advance(s).Ok);
            Assert.AreEqual(CareerPhase.Draft, s.Phase);
            StepResult r = CareerFlow.Advance(s);   // runs to the user's first pick
            Assert.IsTrue(r.Ok);
            if (CareerDraft.UserOnClock(s))
            {
                Assert.Greater(CareerFlow.Blockers(s).Count, 0);
                Assert.IsFalse(CareerFlow.Advance(s).Ok);
                CareerDraft.AutoPick(s, new Rng(1));
            }
            int guard = 0;
            while (s.Phase == CareerPhase.Draft && guard++ < 20)
            {
                if (CareerDraft.UserOnClock(s)) CareerDraft.AutoPick(s, new Rng(guard));
                CareerFlow.Advance(s);
            }
            Assert.AreEqual(CareerPhase.RookieSigning, s.Phase);
            Assert.AreEqual(224, s.Pro.LastDraft.Count);
        }

        [Test]
        public void APassiveUserCanGetThroughTwoYearsWithOnlyTheMinimumFixes()
        {
            // the user never negotiates, bids or scouts: he presses "advance" and only does what the blockers demand
            CareerSave s = CareerTestUtil.Fresh(152);
            Team u = s.UserTeam;
            var used = Offseason.LeagueUsedNames(s.Pro);
            int steps = 0;
            while (!(s.Year == 2028 && s.Phase == CareerPhase.Preseason) && steps++ < 500)
            {
                StepResult r = CareerFlow.Advance(s);
                if (r.Ok) continue;
                string why = string.Join("; ", r.Blockers.ToArray());
                switch (s.Phase)
                {
                    case CareerPhase.Draft: CareerDraft.AutoPick(s, new Rng(steps)); break;
                    case CareerPhase.Preseason:
                        while (u.Roster.Count > 53) RosterManager.Release(s, u, u.Roster.Where(p => !Offseason.IsStarter(u, p)).OrderBy(p => p.Overall).First().Id);
                        while (u.Roster.Count < 46 && RosterManager.FillOne(s, u, new Rng(steps), used, false)) { }
                        CapManager.AutoFix(s, u);
                        break;
                    case CareerPhase.ReSign: case CareerPhase.FreeAgency: CapManager.AutoFix(s, u); break;
                    case CareerPhase.TrainingCamp: StaffManager.HireBest(s, u); CapManager.AutoFix(s, u); break;
                    default: Assert.Fail("unexpected blocker in " + s.Phase + ": " + why); break;
                }
                Assert.IsTrue(CareerFlow.Advance(s).Ok || CareerFlow.Blockers(s).Count > 0, why);
            }
            Assert.AreEqual(2028, s.Year);
            Assert.AreEqual(CareerPhase.Preseason, s.Phase);
            CareerTestUtil.AssertClean(s, false);
        }

        [Test]
        public void TheSameCareerReplaysIdenticallyAndSimToNextSeasonRollsTheYear()
        {
            CareerSave a = CareerTestUtil.Fresh(17), b = CareerTestUtil.Fresh(17);
            CareerFlow.SimToNextSeason(a);
            CareerFlow.SimToNextSeason(b);
            Assert.AreEqual(2027, a.Year);
            Assert.AreEqual(CareerPhase.Preseason, a.Phase);
            Assert.AreEqual(CareerTestUtil.Fingerprint(a), CareerTestUtil.Fingerprint(b));
            Assert.AreEqual(a.Season.Games.Count, b.Season.Games.Count);
        }

        [Test]
        public void TheCapGrowsFivePercentAYearAndEveryTeamIsUnderIt()
        {
            CareerSave s = CareerTestUtil.Fresh(18);
            Assert.AreEqual(255000000L, CapMath.CapFor(s, 2026));
            Assert.AreEqual(267750000L, CapMath.CapFor(s, 2027));
            Assert.AreEqual(281130000L, CapMath.CapFor(s, 2028));
            CareerFlow.SimToNextSeason(s);
            Assert.AreEqual(CapMath.CapFor(s, 2027), s.UserTeam.SalaryCap);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            for (int t = 0; t < 32; t++) Assert.LessOrEqual(s.Pro.Teams[t].CapUsed, s.Pro.Teams[t].SalaryCap);
        }

        [Test]
        public void CollegeWorldRunsAlongsideTheProSeason()
        {
            CareerSave s = CareerTestUtil.Fresh(19, 5, true);
            Assert.IsNotNull(s.College);
            Assert.AreEqual(s.Pro.Teams.Count, 32);
            int collegeYear = s.College.Year;
            CareerFlow.SimToNextSeason(s);
            Assert.AreEqual(collegeYear + 1, s.College.Year, "the college world turns over with the draft");
            Assert.AreEqual(s.Year, s.College.Year);
            CareerTestUtil.AssertClean(s, false);
        }
    }
}
