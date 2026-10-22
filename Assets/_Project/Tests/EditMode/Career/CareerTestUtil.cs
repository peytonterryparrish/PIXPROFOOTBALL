using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>Helpers shared by the Career (v0.8.0 Franchise) tests.</summary>
    internal static class CareerTestUtil
    {
        public static CareerSave Fresh(int seed, int userTeam = 5, bool college = false)
        {
            var o = new CareerOptions { Seed = seed, UserTeamId = userTeam };
            if (college)
            {
                o.Teams = LeagueTestUtil.LoadProTeams();
                o.Schools = LeagueTestUtil.LoadSchools();
                o.Bowls = CollegeData.ParseConfBowls(LeagueTestUtil.ConfBowlsCsv());
            }
            return CareerFlow.NewCareer(o);
        }

        /// <summary>A career advanced (with the auto pilot for the user) to the given phase.</summary>
        public static CareerSave AtPhase(int seed, CareerPhase phase, int userTeam = 5)
        {
            CareerSave s = Fresh(seed, userTeam);
            if (phase == CareerPhase.Preseason) { CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason); CareerFlow.AdvanceUntil(s, CareerPhase.Preseason); return s; }   // the NEXT preseason (the first year starts there)
            if (s.Phase == phase) return s;
            CareerFlow.AdvanceUntil(s, phase);
            return s;
        }

        public static void AssertClean(CareerSave s, bool strict)
        {
            List<string> problems = CareerValidator.Check(s, strict);
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems.Take(10).ToArray()));
        }

        public static long Fingerprint(CareerSave s)
        {
            unchecked
            {
                long h = LeagueTestUtil.Fingerprint(s.Pro);
                h = h * 31 + s.Year;
                h = h * 31 + (int)s.Phase;
                h = h * 31 + s.Transactions.Count;
                h = h * 31 + s.News.Count;
                h = h * 31 + s.Picks.Count;
                for (int i = 0; i < s.Pro.Teams.Count; i++) h = h * 31 + s.Pro.Teams[i].CapUsed;
                for (int i = 0; i < s.Pro.History.Count; i++) h = h * 31 + s.Pro.History[i].ChampionTeamId;
                return h;
            }
        }

        public static Player FindStarter(Team t, Position pos)
        {
            return t.GetPlayer(t.Depth.StarterId(pos));
        }

        /// <summary>A simulated game result for the user's scheduled game (what the Unity match would hand back).</summary>
        public static GameResult PlayLikeUnity(CareerSave s, GameSetup gs, int seed)
        {
            return QuickSim.SimGame(gs.Home, gs.Away, gs.Rules, new Rng(seed), gs.Options);
        }

        /// <summary>Advance to the first regular-season week in which the user has a game.</summary>
        public static void ToRegularSeason(CareerSave s)
        {
            if (s.Phase != CareerPhase.RegularSeason) CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
        }
    }
}
