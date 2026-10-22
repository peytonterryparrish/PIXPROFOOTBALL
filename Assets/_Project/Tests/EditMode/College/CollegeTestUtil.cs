using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Tests
{
    /// <summary>Helpers for the College Network (v0.9.0) tests: careers built from the committed CSV texts, shortcuts to calendar points, college fingerprints.</summary>
    internal static class CollegeTestUtil
    {
        /// <summary>A career with the real 138-school data, built the way the Unity layer does it (the file texts go into the options).</summary>
        public static CareerSave Career(int seed, int userTeam = 5)
        {
            var o = new CareerOptions
            {
                Seed = seed, UserTeamId = userTeam, Teams = LeagueTestUtil.LoadProTeams(),
                SchoolsCsv = LeagueTestUtil.SchoolsCsv(), BowlsCsv = LeagueTestUtil.ConfBowlsCsv()
            };
            return CareerFlow.NewCareer(o);
        }

        /// <summary>Starts the season and plays the given number of pro weeks (the college plays the same number of weeks).</summary>
        public static CareerSave InSeason(int seed, int weeks, int userTeam = 5)
        {
            CareerSave s = Career(seed, userTeam);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            for (int i = 0; i < weeks; i++) Step(s);
            return s;
        }

        public static void Step(CareerSave s)
        {
            CareerFlow.AutoResolveUser(s);
            StepResult r = CareerFlow.Advance(s);
            Assert.IsTrue(r.Ok, string.Join("; ", r.Blockers.ToArray()));
        }

        public static CareerSave AtPhase(int seed, CareerPhase phase)
        {
            CareerSave s = Career(seed);
            CareerFlow.AdvanceUntil(s, phase);
            return s;
        }

        /// <summary>Order-sensitive digest of the college world and the network state.</summary>
        public static long Fingerprint(CareerSave s)
        {
            unchecked
            {
                long h = 17;
                CollegeWorld w = s.College;
                if (w == null) return h;
                h = h * 31 + w.Year;
                for (int i = 0; i < w.Schools.Count; i++)
                {
                    School sc = w.Schools[i];
                    h = h * 31 + sc.Prestige;
                    h = h * 31 + sc.Team.Roster.Count;
                    for (int k = 0; k < sc.Team.Roster.Count; k++) { Player p = sc.Team.Roster[k]; h = h * 31 + p.Id; h = h * 31 + p.Overall; }
                }
                Season cs = w.Season;
                for (int i = 0; i < cs.Games.Count; i++) { h = h * 31 + cs.Games[i].HomeScore; h = h * 31 + cs.Games[i].AwayScore; }
                h = h * 31 + cs.ChampionTeamId;
                h = h * 31 + s.Net.Boxes.Count;
                h = h * 31 + s.Net.Honors.Count;
                h = h * 31 + s.Net.Scouts.Count;
                for (int i = 0; i < s.Net.Scouts.Count; i++) { h = h * 31 + s.Net.Scouts[i].Skill; h = h * 31 + s.Net.Scouts[i].PointsThisSeason; }
                ScoutingReport rep = CollegeScouting.UserReport(s);
                if (rep != null)
                {
                    h = h * 31 + rep.PointsSpent; h = h * 31 + rep.PointsBudget; h = h * 31 + rep.Entries.Count;
                    for (int i = 0; i < rep.Entries.Count; i++) { h = h * 31 + rep.Entries[i].ProspectId; h = h * 31 + rep.Entries[i].PointsSpent; h = h * 31 + rep.Entries[i].Estimated.Speed; }
                }
                for (int i = 0; i < s.Net.Board.Count; i++) { h = h * 31 + s.Net.Board[i].PlayerId; h = h * 31 + (s.Net.Board[i].OnBoard ? 1 : 0); }
                return h;
            }
        }

        public static Prospect Best(CareerSave s, Func<Prospect, bool> pred = null)
        {
            return s.Pro.DraftClass.Where(p => pred == null || pred(p)).OrderBy(p => p.ConsensusRank).First();
        }

        public static void AssertRostersFull(CareerSave s)
        {
            foreach (School sc in s.College.Schools)
            {
                Assert.AreEqual(LeagueRules.CollegeRosterSize, sc.Team.Roster.Count, sc.Name + " roster size");
                foreach (Position pos in LeagueRules.RosterOrder)
                    Assert.AreEqual(LeagueRules.CollegeSlots(pos), sc.Team.CountAt(pos), sc.Name + " " + pos);
            }
        }
    }
}
