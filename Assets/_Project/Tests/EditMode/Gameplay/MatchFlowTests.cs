using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.Sim;
using UnityEngine;
using Team = PixFootball.Core.Team;

namespace PixFootball.Tests
{
    public class MatchFlowTests
    {
        static MatchFlow NewFlow(int quarterMinutes = 5, float runoff = 0.5f, int seed = 11, bool naturalPace = false)
        {
            var cfg = new MatchConfig { QuarterMinutes = quarterMinutes, RunoffScale = runoff, CpuVsCpu = true, Seed = seed, NaturalPace = naturalPace };      // v0.11.0 SIM-FLOW: the old accelerated clock unless a test asks for the natural pace
            var home = MatchData.TeamByKey("buffalo_ironhorns");
            var away = MatchData.TeamByKey("miami_reefrunners");
            return new MatchFlow(cfg, home, away, Team.Away, new Rng(seed));
        }

        static PlayOutcome PlayOne(MatchFlow f, out PeriodChange pc, out bool played)
        {
            pc = f.PrepareSnap(); played = false;
            if (pc.Changed) return null;
            Play off, def; f.CpuCalls(out off, out def);
            var sim = f.CreateSim(off, def, true);
            sim.RunToCompletion();
            played = true;
            return f.Finish(sim);
        }

        // ------------------------------------------------------------------------------------------- coordinate mirroring
        [Test]
        public void PlayFrameMirrorsBothAxesAndKeepsHandedness()
        {
            var plus = new PlayFrame(true); var minus = new PlayFrame(false);
            var p = new Vec2(30f, 25f);
            Vector3 a = plus.ToUnity(p), b = minus.ToUnity(p);
            Assert.AreEqual(a.x, -b.x, 1e-4f); Assert.AreEqual(a.z, -b.z, 1e-4f);
            // offense drives toward its own +Y: that is +Z when attacking plus, -Z when attacking minus
            Assert.Greater(plus.ToUnity(new Vec2(26.665f, 60f)).z, plus.ToUnity(new Vec2(26.665f, 50f)).z);
            Assert.Less(minus.ToUnity(new Vec2(26.665f, 60f)).z, minus.ToUnity(new Vec2(26.665f, 50f)).z);
            // offense's right: +X in the sim is world +X facing +Z, and world -X facing -Z (a 180 degree rotation)
            Assert.Greater(plus.ToUnity(new Vec2(30f, 50f)).x, 0f);
            Assert.Less(minus.ToUnity(new Vec2(30f, 50f)).x, 0f);
            // own goal line is 45.72 m behind the offense
            Assert.AreEqual(-45.72f, plus.SpotToZ(0f), 1e-3f); Assert.AreEqual(45.72f, minus.SpotToZ(0f), 1e-3f);
            Assert.AreEqual(45.72f, plus.SpotToZ(100f), 1e-3f); Assert.AreEqual(-45.72f, minus.SpotToZ(100f), 1e-3f);
            // round trip
            Assert.AreEqual(p.X, minus.ToSim(minus.ToUnity(p)).X, 1e-3f); Assert.AreEqual(p.Y, minus.ToSim(minus.ToUnity(p)).Y, 1e-3f);
            Assert.AreEqual(p.X, plus.ToSim(plus.ToUnity(p)).X, 1e-3f);
            // directions
            Assert.AreEqual(1f, plus.DirToUnity(new Vec2(0, 1)).z, 1e-4f); Assert.AreEqual(-1f, minus.DirToUnity(new Vec2(0, 1)).z, 1e-4f);
            var back = minus.DirToSim(new Vector2(0.6f, 0.8f)); Assert.AreEqual(-0.6f, back.X, 1e-4f); Assert.AreEqual(-0.8f, back.Y, 1e-4f);
            // the same physical point seen from the other frame (a turnover flips the frame)
            Vector3 world = plus.ToUnity(new Vec2(10f, 70f));
            Vec2 inMinus = minus.ToSim(world);
            Assert.AreEqual(53.33f - 10f, inMinus.X, 0.01f); Assert.AreEqual(30f, inMinus.Y, 1e-3f);
        }

        [Test]
        public void EndsSwapEveryQuarterAndFollowPossession()
        {
            Assert.IsTrue(PlayFrame.For(Team.Home, 1).TowardsPlusZ); Assert.IsFalse(PlayFrame.For(Team.Away, 1).TowardsPlusZ);
            Assert.IsFalse(PlayFrame.For(Team.Home, 2).TowardsPlusZ); Assert.IsTrue(PlayFrame.For(Team.Away, 2).TowardsPlusZ);
            Assert.IsTrue(PlayFrame.For(Team.Home, 3).TowardsPlusZ); Assert.IsFalse(PlayFrame.For(Team.Home, 4).TowardsPlusZ);
        }

        // ------------------------------------------------------------------------------------------- team select data
        [Test]
        public void TeamSelectDataHasThirtyTwoTeamsWithColorsAndOveralls()
        {
            MatchData.Reset();
            Assert.AreEqual(32, MatchData.League.Teams.Count);
            foreach (var t in MatchData.League.Teams)
            {
                Assert.IsFalse(string.IsNullOrEmpty(t.Key), "key");
                Assert.IsFalse(string.IsNullOrEmpty(t.Abbreviation), "abbr " + t.FullName);
                Assert.IsTrue(t.PrimaryColor.StartsWith("#") && t.PrimaryColor.Length == 7);
                Assert.That(MatchData.Overall(t), Is.InRange(40, 99), t.FullName);
            }
            Assert.AreEqual(32, MatchData.League.Teams.Select(t => t.Abbreviation).Distinct().Count());
            var d = new MatchConfig();
            Assert.AreEqual(5, d.QuarterMinutes); Assert.AreEqual(Difficulty.Pro, d.Difficulty); Assert.IsFalse(d.CollegeRules);
            CollectionAssert.AreEqual(new[] { 2, 3, 5, 8, 15 }, MatchConfig.QuarterChoices);
            Assert.AreEqual(-8, new MatchConfig { Difficulty = Difficulty.Rookie }.CpuRatingDelta);
            Assert.AreEqual(6, new MatchConfig { Difficulty = Difficulty.AllPro }.CpuRatingDelta);
        }

        // ------------------------------------------------------------------------------------------- clock
        [Test]
        public void AcceleratedClockScalesTheRunoff()
        {
            foreach (float scale in new[] { 1f, 0.5f })
            {
                var f = NewFlow(5, scale, 3);
                PeriodChange pc; bool played; bool seen = false;
                for (int i = 0; i < 20; i++)
                {
                    PlayOne(f, out pc, out played);
                    if (f.State.Phase == GamePhase.Scrimmage && f.State.ClockRunning && f.State.PendingRunoff > 0f)
                    {
                        float expected = scale >= 1f ? 30f : 15f;
                        Assert.LessOrEqual(f.State.PendingRunoff, expected + 0.01f, "runoff at scale " + scale);
                        if (scale >= 1f) Assert.GreaterOrEqual(f.State.PendingRunoff, 19.9f);
                        seen = true;
                        break;
                    }
                }
                Assert.IsTrue(seen, "never saw a running-clock play at scale " + scale);
            }
        }

        [Test]
        public void FiveMinuteQuartersPlayRoughlySixtyToSeventySnaps()
        {
            var counts = new List<int>();
            for (int seed = 1; seed <= 4; seed++)
            {
                var f = NewFlow(5, 1.0f, seed);
                RunGame(f);
                Assert.IsTrue(f.State.IsFinal, "game did not finish, seed " + seed);
                counts.Add(f.Snaps);
            }
            float avg = (float)counts.Average();
            Debug.Log("[MatchFlowTests] snaps per 5-min game: " + string.Join(",", counts));
            Assert.That(avg, Is.InRange(55f, 80f), "snaps " + string.Join(",", counts));
        }

        [Test]
        public void NaturalPaceGivesAShortQuarterGameTheSnapsOfARealOne()
        {
            // v0.11.0 SIM-FLOW: a 3-minute-quarter simulated game with the compressed clock plays about as many snaps as a real game (125 to 180 with the kicks), not 35
            var counts = new List<int>();
            for (int seed = 1; seed <= 3; seed++) { var f = NewFlow(3, 1.0f, seed, true); RunGame(f); Assert.IsTrue(f.State.IsFinal, "seed " + seed); counts.Add(f.Snaps); Assert.AreEqual(0.2f, f.State.ClockScale, 0.001f); }
            Assert.That(counts.Average(), Is.InRange(110f, 215f), "snaps " + string.Join(",", counts));
        }

        static int RunGame(MatchFlow f)
        {
            int guard = 0, n = 0;
            while (!f.State.IsFinal && guard++ < 700)
            {
                PeriodChange pc; bool played;
                PlayOne(f, out pc, out played);
                if (pc.Final) break;
                if (played) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------------------------------- scripted flow
        [Test]
        public void FullGameFlowKickoffScoringQuartersAndFinal()
        {
            // v0.9.0: a touchdown in one short seeded game is a lottery (plays are 20-40 percent longer and the seed's drives changed), so the scripted flow is played on up to ten seeds:
            // every game must satisfy the flow invariants and at least one must score a touchdown (and then the try and the kickoff must follow)
            bool anyTd = false;
            for (int seed = 7; seed < 17 && !anyTd; seed++) anyTd = ScriptedGame(seed);
            Assert.IsTrue(anyTd, "no touchdowns in ten whole games");
        }

        static bool ScriptedGame(int seed)
        {
            var f = NewFlow(2, 0.5f, seed);
            Assert.AreEqual(GamePhase.Kickoff, f.State.Phase);
            bool sawPat = false, sawKickoffAfterScore = false, sawTd = false; int maxQ = 1;
            var quarters = new HashSet<int>(); bool lastWasTd = false, lastWasTry = false;
            int guard = 0;
            while (!f.State.IsFinal && guard++ < 700)
            {
                PeriodChange pc; bool played;
                var phaseBefore = f.State.Phase;
                var o = PlayOne(f, out pc, out played);
                quarters.Add(f.State.Quarter); maxQ = Mathf.Max(maxQ, f.State.Quarter);
                if (!played) continue;
                if (lastWasTd && phaseBefore == GamePhase.PointAfter) sawPat = true;
                if (lastWasTry && phaseBefore == GamePhase.Kickoff) sawKickoffAfterScore = true;
                lastWasTd = o.Headline == Headline.Touchdown && f.State.Phase == GamePhase.PointAfter;
                lastWasTry = phaseBefore == GamePhase.PointAfter;
                if (o.Headline == Headline.Touchdown) sawTd = true;
                if (o.PossessionChanged) Assert.AreNotEqual(o.Offense, f.State.Possession);
            }
            Assert.IsTrue(f.State.IsFinal);
            Assert.GreaterOrEqual(maxQ, 4);
            Assert.IsTrue(quarters.Contains(1) && quarters.Contains(2) && quarters.Contains(3) && quarters.Contains(4));
            Assert.AreEqual(f.State.HomeScore, f.HomeByQuarter.Sum()); Assert.AreEqual(f.State.AwayScore, f.AwayByQuarter.Sum());
            Assert.AreEqual(f.State.Winner.HasValue, f.State.HomeScore != f.State.AwayScore);
            if (sawTd)
            {
                Assert.IsTrue(sawPat, "PAT/2pt did not follow a touchdown");
                Assert.IsTrue(sawKickoffAfterScore, "kickoff did not follow the try");
            }
            return sawTd;
        }

        [Test]
        public void PlayOptionsResolveForEverySituation()
        {
            var f = NewFlow();
            var ko = f.OffenseOptions(); Assert.That(ko.Count, Is.InRange(3, 6)); foreach (var o in ko) Assert.IsNotNull(o.Play, o.Label);
            PeriodChange pc; bool played;
            PlayOne(f, out pc, out played);
            int checkedSituations = 0;
            for (int i = 0; i < 80 && !f.State.IsFinal; i++)
            {
                if (f.State.Phase == GamePhase.Scrimmage || f.State.Phase == GamePhase.PointAfter)
                {
                    var offs = f.OffenseOptions();
                    Assert.That(offs.Count, Is.InRange(2, 6), f.State.Phase + " " + f.State.Down + "&" + f.State.Distance + "@" + f.State.BallSpot);
                    foreach (var o in offs) { Assert.IsNotNull(o.Play, o.Label); Assert.IsNotNull(f.Book.FindFormation(o.Play.FormationName)); }
                    if (f.State.Phase == GamePhase.Scrimmage) { var defs = f.DefenseOptions(); Assert.GreaterOrEqual(defs.Count, 4); foreach (var o in defs) Assert.IsNotNull(o.Play, o.Label); }
                    checkedSituations++;
                }
                PlayOne(f, out pc, out played);
            }
            Assert.Greater(checkedSituations, 10);
        }

        // ------------------------------------------------------------------------------------------- scripted sequence
        [Test]
        public void ScriptedGoalLineSequenceTouchdownTryKickoffAndPossessionFrames()
        {
            bool done = false;
            for (int seed = 1; seed <= 25 && !done; seed++)
            {
                var f = NewFlow(5, 1.0f, seed);
                // skip the opening kickoff, then put the ball on the 1-yard line and hand it to the offense
                PeriodChange pc; bool played;
                PlayOne(f, out pc, out played);
                Assert.AreEqual(GamePhase.Scrimmage, f.State.Phase);
                var offTeam = f.State.Possession;
                f.State.BallSpot = 99; f.State.Down = 1; f.State.Distance = 1;
                var opts = f.OffenseOptions();
                Assert.IsTrue(opts.Any(o => o.Label == "QB Sneak"), "goal-line call list has the sneak");
                var sim = f.CreateSim(opts.First(o => o.Label == "QB Sneak").Play, f.DefenseOptions()[0].Play, true);
                sim.RunToCompletion();
                var frameBefore = f.CurrentFrame;
                var o1 = f.Finish(sim);
                if (o1.Headline != Headline.Touchdown) continue;
                done = true;
                Assert.AreEqual(GamePhase.PointAfter, f.State.Phase);
                Assert.AreEqual(offTeam, o1.ScoringTeam.Value);
                Assert.AreEqual(6, offTeam == Team.Home ? o1.HomePoints : o1.AwayPoints);
                var tryOpts = f.OffenseOptions();
                Assert.AreEqual("Extra Point", tryOpts[0].Label);
                // kick the point
                var pat = f.CreateSim(tryOpts[0].Play, f.DefenseAnswer(tryOpts[0].Play), true);
                pat.RunToCompletion();
                f.Finish(pat);
                Assert.AreEqual(GamePhase.Kickoff, f.State.Phase);
                Assert.AreEqual(offTeam, f.State.Possession, "the scoring team kicks off");
                // after the kickoff the receiving team has the ball and drives the other way
                PlayOne(f, out pc, out played);
                Assert.AreEqual(GamePhase.Scrimmage, f.State.Phase);
                Assert.AreNotEqual(offTeam, f.State.Possession);
                Assert.AreNotEqual(frameBefore.TowardsPlusZ, f.CurrentFrame.TowardsPlusZ, "the new offense attacks the opposite end zone");
            }
            Assert.IsTrue(done, "no touchdown from the one-yard line in 25 tries");
        }

        [Test]
        public void BodySizesFollowPositionGroups()
        {
            Assert.AreEqual(PlayerSize.Large, RoleUtil.SizeOf(Position.LT)); Assert.AreEqual(PlayerSize.Large, RoleUtil.SizeOf(Position.DT));
            Assert.AreEqual(PlayerSize.Medium, RoleUtil.SizeOf(Position.MLB)); Assert.AreEqual(PlayerSize.Medium, RoleUtil.SizeOf(Position.TE));
            Assert.AreEqual(PlayerSize.Medium, RoleUtil.SizeOf(Position.RB)); Assert.AreEqual(PlayerSize.Medium, RoleUtil.SizeOf(Position.QB));
            Assert.AreEqual(PlayerSize.Small, RoleUtil.SizeOf(Position.WR)); Assert.AreEqual(PlayerSize.Small, RoleUtil.SizeOf(Position.CB));
            Assert.AreEqual(PlayerSize.Small, RoleUtil.SizeOf(Position.K));
        }

        [Test]
        public void DifficultyBoostDoesNotLeakIntoRosters()
        {
            var cfg = new MatchConfig { Difficulty = Difficulty.AllPro, UserTeam = Team.Home };
            var home = MatchData.TeamByKey("buffalo_ironhorns"); var away = MatchData.TeamByKey("miami_reefrunners");
            var f = new MatchFlow(cfg, home, away, Team.Away, new Rng(5));
            int before = away.Roster.Sum(p => p.Ratings.Speed + p.Ratings.Strength);
            Play off, def; f.CpuCalls(out off, out def);
            var sim = f.CreateSim(off, def, true);
            int after = away.Roster.Sum(p => p.Ratings.Speed + p.Ratings.Strength);
            Assert.AreEqual(before, after);
            sim.RunToCompletion();
            Assert.IsNotNull(f.Finish(sim));
        }
    }
}
