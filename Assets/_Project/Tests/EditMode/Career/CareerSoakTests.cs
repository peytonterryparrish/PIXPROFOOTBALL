using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>Multi-season soak: ten simulated years with full offseasons, checking rosters, the cap, league strength and determinism.</summary>
    public class CareerSoakTests
    {
        private sealed class YearLine
        {
            public int Year;
            public float Starters, Roster, Age;
            public int FreeAgents, Retired, Signings, Trades, Picks, News;
            public long Fingerprint;
            public int PsMin, ActiveMin, ActiveMax;
        }

        private static List<YearLine> _soak;
        private static CareerSave _soakSave;

        private static void RunSoak()
        {
            if (_soak != null) return;
            var lines = new List<YearLine>();
            CareerSave s = CareerTestUtil.Fresh(2024, 9);
            lines.Add(Line(s, 0));
            for (int y = 0; y < 10; y++)
            {
                // the strict checks at the start of week 1
                CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
                List<string> strict = CareerValidator.Check(s, true);
                Assert.AreEqual(0, strict.Count, "year " + s.Year + ": " + string.Join("; ", strict.Take(8).ToArray()));
                CareerFlow.AdvanceUntil(s, CareerPhase.Awards);
                CareerFlow.AdvanceUntil(s, CareerPhase.Preseason);
                // after camp and before the cuts every team is under the cap, the user too
                foreach (Team t in s.Pro.Teams) Assert.LessOrEqual(t.CapUsed, t.SalaryCap, s.Year + " " + t.Nickname + " over the cap in the preseason");
                List<string> general = CareerValidator.Check(s, false);
                Assert.AreEqual(0, general.Count, "year " + s.Year + ": " + string.Join("; ", general.Take(8).ToArray()));
                lines.Add(Line(s, y + 1));
            }
            _soak = lines;
            _soakSave = s;
        }

        private static YearLine Line(CareerSave s, int n)
        {
            return new YearLine
            {
                Year = s.Year, Starters = CareerValidator.LeagueStarterOverall(s), Roster = CareerValidator.LeagueRosterOverall(s), Age = CareerValidator.AverageAge(s),
                FreeAgents = s.Pro.FreeAgents.Count, Retired = s.Retired.Count, Signings = s.FaLog.Count, Trades = s.Transactions.Count(t => t.Kind == TransactionKind.Trade),
                Picks = s.Picks.Count(k => k.Year >= s.Year), News = s.News.Count, Fingerprint = CareerTestUtil.Fingerprint(s),
                ActiveMin = s.Pro.Teams.Min(t => t.Roster.Count), ActiveMax = s.Pro.Teams.Max(t => t.Roster.Count), PsMin = s.Pro.Teams.Min(t => t.PracticeSquad.Count)
            };
        }

        [Test]
        public void TenSeasonsRunWithValidRostersTheCapAndAStableLeague()
        {
            RunSoak();
            Assert.AreEqual(11, _soak.Count);
            float s0 = _soak[0].Starters, r0 = _soak[0].Roster;
            foreach (YearLine l in _soak)
            {
                TestContext.WriteLine("{0}: starters {1:F2} roster {2:F2} age {3:F2} fa {4} signings(total) {5} trades(total) {6}", l.Year, l.Starters, l.Roster, l.Age, l.FreeAgents, l.Signings, l.Trades);
                Assert.AreEqual(s0, l.Starters, 3.0, l.Year + " starter overall drifted");
                Assert.AreEqual(r0, l.Roster, 3.0, l.Year + " roster overall drifted");
                Assert.That(l.Age, Is.InRange(25.5f, 29.5f), l.Year + " average age");
                Assert.That(l.FreeAgents, Is.InRange(50, 900), l.Year + " free agent pool");
                Assert.AreEqual(3 * 7 * 32, l.Picks, "three drafts of picks, always");
            }
            CareerSave s = _soakSave;
            Assert.AreEqual(2036, s.Year);
            Assert.AreEqual(10, s.Pro.History.Count);
            Assert.AreEqual(10 * 32, s.TeamSeasons.Count);
            Assert.AreEqual(10, s.Trophies.Count(t => t.Kind == TrophyKind.PixBowl));
            Assert.AreEqual(20, s.AllPro.Count);
            Assert.GreaterOrEqual(s.HallOfFame.Count, 1, "ten years produce a Hall of Fame class or two");
            Assert.Greater(s.Transactions.Count, 3000);
            Assert.Greater(s.Transactions.Count(t => t.Kind == TransactionKind.Trade), 0, "CPU teams trade");
            Assert.LessOrEqual(s.News.Count, NewsWriter.MaxItems);
            Assert.GreaterOrEqual(s.Pro.RetiredPlayers.Count, 400);
            foreach (Team t in s.Pro.Teams) Assert.IsFalse(StaffManager.HasVacancy(t) && t.Id != s.UserTeamId);
            // the champion is not always the same club
            Assert.GreaterOrEqual(s.Pro.History.Select(h => h.ChampionTeamId).Distinct().Count(), 4);
        }

        [Test]
        public void TheSoakIsDeterministicBySeed()
        {
            RunSoak();
            // rerun the first three years from scratch and compare the fingerprints with the soak
            CareerSave s = CareerTestUtil.Fresh(2024, 9);
            Assert.AreEqual(_soak[0].Fingerprint, CareerTestUtil.Fingerprint(s));
            for (int y = 0; y < 3; y++)
            {
                CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
                CareerFlow.AdvanceUntil(s, CareerPhase.Preseason);
                Assert.AreEqual(_soak[y + 1].Fingerprint, CareerTestUtil.Fingerprint(s), "year " + s.Year);
            }
            // and a different seed gives a different history
            CareerSave other = CareerTestUtil.Fresh(2025, 9);
            CareerFlow.SimToNextSeason(other);
            Assert.AreNotEqual(_soak[1].Fingerprint, CareerTestUtil.Fingerprint(other));
        }

        [Test]
        public void ThePlayersAndLedgersOfTheSoakAreConsistent()
        {
            RunSoak();
            CareerSave s = _soakSave;
            // every retired player is gone from every team and the pool
            var retired = new HashSet<int>(s.Pro.RetiredPlayers.Select(p => p.Id));
            foreach (Team t in s.Pro.Teams) foreach (Player p in CapManager.AllUnderContract(t)) Assert.IsFalse(retired.Contains(p.Id), p.Name);
            foreach (Player p in s.Pro.FreeAgents) Assert.IsFalse(retired.Contains(p.Id), p.Name);
            // ids never repeat across the league
            var all = s.Pro.AllRosterPlayers().Select(p => p.Id).Concat(s.Pro.FreeAgents.Select(p => p.Id)).ToList();
            Assert.AreEqual(all.Count, all.Distinct().Count());
            // contracts: every active player is under contract and the cap hit matches the structure
            foreach (Team t in s.Pro.Teams)
                foreach (Player p in t.Roster)
                {
                    Assert.Greater(p.Contract.YearsLeft, 0, p.Name + " on the roster without a contract");
                    Assert.Greater(p.Contract.CapHit, 0L);
                }
            // abilities stay valid after years of progression
            foreach (Player p in s.Pro.AllRosterPlayers())
                foreach (PlayerAbility a in p.Abilities) { Assert.IsNotNull(AbilityCatalog.Get(a.Id)); Assert.IsTrue(AbilityCatalog.Get(a.Id).AppliesTo(p.Position)); }
            // ten champions, ten MVPs
            Assert.AreEqual(10, s.Accolades.Count(a => a.Kind == AccoladeKind.Mvp));
            Assert.AreEqual(10, s.Accolades.Count(a => a.Kind == AccoladeKind.Roy));
            // the cap grew
            Assert.AreEqual(CapMath.CapFor(s, 2036), s.UserTeam.SalaryCap);
            Assert.Greater(s.UserTeam.SalaryCap, 400000000L);
        }
    }
}
