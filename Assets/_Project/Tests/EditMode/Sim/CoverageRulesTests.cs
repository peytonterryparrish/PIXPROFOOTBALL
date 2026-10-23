using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>
    /// v0.10.0 defense: the coverage rules. Every defender, every tick of a recorded pass play, is compared with the rule his assignment gives him (see <see cref="CoverageAudit"/>):
    /// zone defenders reach and hold their landmarks and leave them only by rule, man defenders stay on their men, a man whose receiver blocks is a free defender, rushers keep their lanes.
    /// </summary>
    public class CoverageRulesTests
    {
        private static PlaySimOptions Quiet() { return new PlaySimOptions { Penalties = false }; }

        /// <summary>Snaps a simulation that was set up with AutoSnap off.</summary>
        private static void Snap(PlaySimulation sim)
        {
            for (int i = 0; i < 5 && sim.Phase == PlayPhase.PreSnap; i++) sim.Step(new ControlInput { Snap = true });
            Assert.AreEqual(PlayPhase.Live, sim.Phase);
        }

        private static List<Play> Passes(bool includePa = true)
        {
            return SimTestKit.Playbook.AllPlays.Where(p => p.Side == PlaySide.Offense && p.IsPass && p.Category != PlayCategory.Screen && p.Name != "Hail Mary" && (includePa || p.Category != PlayCategory.PlayAction)).ToList();
        }

        private static List<Play> Defenses()
        {
            return SimTestKit.Playbook.AllPlays.Where(p => p.Side == PlaySide.Defense && p.Category != PlayCategory.Prevent && p.Category != PlayCategory.GoalLine).ToList();
        }

        // ------------------------------------------------------------------ the audit

        [Test]
        public void EveryDefenderFrameOfAPassPlayFollowsItsAssignmentRule()
        {
            CoverageAuditStats t = CoverageAudit.Batch(Passes(), Defenses(), 700, 9000);
            TestContext.WriteLine(CoverageAudit.Report(t));
            Assert.Greater(t.Frames, 800000, "the audit saw enough defender frames");
            Assert.That(t.Rate, Is.LessThanOrEqualTo(0.02), "violation frames as a share of defender frames (target 2 percent)");
            Assert.AreEqual(0, t.Swaps, "a man defender never swaps his man without the free-defender rule");
        }

        [Test]
        public void NoDefenderIsLeftStandingAwayFromEveryPostItCouldHold()
        {
            CoverageAuditStats t = CoverageAudit.Batch(Passes(), Defenses(), 700, 19000);
            TestContext.WriteLine("lost defenders " + t.Lost + " in " + t.Plays + " plays");
            Assert.That(t.Lost, Is.LessThanOrEqualTo(t.Plays / 100), "at most one lost defender per hundred plays (the target is zero)");
        }

        [Test]
        public void ZoneCoverageHoldsItsLandmarksAgainstEveryConcept()
        {
            var zones = Defenses().Where(p => p.Category == PlayCategory.ZoneCoverage).ToList();
            CoverageAuditStats t = CoverageAudit.Batch(Passes(), zones, 500, 29000);
            long zoneFrames; t.Kinds.TryGetValue("Zone", out zoneFrames);
            long zoneViol; t.KindViol.TryGetValue("Zone", out zoneViol);
            TestContext.WriteLine("zone frames " + zoneFrames + " violations " + zoneViol);
            Assert.That(zoneViol / (double)Math.Max(1, zoneFrames), Is.LessThan(0.005), "zone defenders leave their zone on a rule, not on their own");
        }

        [Test]
        public void ManCoverageStaysOnItsManThroughMotionCrossersAndPicks()
        {
            var men = Defenses().Where(p => p.Category == PlayCategory.ManCoverage || p.Scheme == CoverageScheme.Cover1 || p.Scheme == CoverageScheme.Cover0).ToList();
            var crossers = Passes().Where(p => p.Name == "Mesh" || p.Name == "Shallow Cross" || p.Name == "Y-Cross" || p.Name == "Drive" || p.Name == "Levels").ToList();
            CoverageAuditStats t = CoverageAudit.Batch(crossers, men, 400, 39000);
            long manFrames; t.Kinds.TryGetValue("Man", out manFrames);
            long manViol; t.KindViol.TryGetValue("Man", out manViol);
            TestContext.WriteLine("man frames " + manFrames + " violations " + manViol + " swaps " + t.Swaps);
            Assert.AreEqual(0, t.Swaps);
            Assert.That(manViol / (double)Math.Max(1, manFrames), Is.LessThan(0.02));
        }

        // ------------------------------------------------------------------ rules one by one

        [Test]
        public void ManLinebackersWalkOutOntoTheirReceiversBeforeTheSnap()
        {
            // a 4-3 in Cover 1 man against a bunch: the linebackers who have a man stand within 2.5 yd of that man's side, not at their base spot 10 yd away
            Play off = SimTestKit.Offense("Slants", "Shotgun Doubles");
            Play def = SimTestKit.Defense("Cover 1 Man Free", "4-3 Base");
            var sim = SimTestKit.Run(off, def, 75, 75, 5, null, null, new PlaySimOptions { Penalties = false, AutoSnap = false });
            int checkedMen = 0;
            for (int i = 11; i < 22; i++)
            {
                SimAgent d = sim.Agents[i];
                if (!PositionGroup.IsLinebacker(d.PlayPos) || d.CoverTarget < 0) continue;
                SimAgent r = sim.Agents[d.CoverTarget];
                Assert.That(Math.Abs(d.Pos.X - r.Pos.X), Is.LessThan(2.5f), d.PlayPos + " on " + r.Label + " is " + Math.Abs(d.Pos.X - r.Pos.X).ToString("0.0") + " yd across from his man");
                Assert.That(d.Pos.Y - sim.LosY, Is.InRange(2.9f, 6.0f), "a man linebacker stands off the line");
                checkedMen++;
            }
            Assert.Greater(checkedMen, 0, "the 4-3 has linebackers in man coverage");
        }

        [Test]
        public void TheTampa2MikeStandsAtHisLinebackerSpotBeforeTheSnapAndRunsTheMiddleAfter()
        {
            Play off = SimTestKit.Offense("Four Verticals", "Shotgun");
            Play def = SimTestKit.Defense("Tampa 2", "4-3 Base");
            var sim = SimTestKit.Run(off, def, 75, 75, 3, null, null, new PlaySimOptions { Penalties = false, AutoSnap = false });
            SimAgent mike = Enumerable.Range(11, 11).Select(i => sim.Agents[i]).First(a => a.PlayPos == Position.MLB);
            Assert.That(mike.Pos.Y - sim.LosY, Is.InRange(3.5f, 5.5f), "the Mike lines up at a linebacker's depth (he used to show his drop by standing 12 yd deep)");
            Snap(sim);
            SimTestKit.Steps(sim, 150);   // 2.5 s
            Assert.That(mike.Pos.Y - sim.LosY, Is.GreaterThan(11f), "after the snap he is running the deep middle");
        }

        [Test]
        public void ADefenderWhoseManBlocksBecomesAFreeDefenderByRuleAndRatsTheHook()
        {
            // Corner-Post keeps the back in to block; the linebacker on him is released after 0.9 s and sits in the hook (or takes the first uncovered runner) instead of following the back or standing in the backfield
            Play off = SimTestKit.Offense("Corner-Post", "Shotgun");
            Play def = SimTestKit.Defense("Cover 1 Man Free", "4-3 Base");
            int freed = 0, plays = 0;
            for (int s = 0; s < 40; s++)
            {
                var sim = SimTestKit.Run(off, def, 75, 75, 60 + s, null, null, Quiet());
                SimTestKit.StepToLive(sim);
                var idle = new float[22]; bool anyFree = false;
                int guard = 0;
                while (sim.Phase != PlayPhase.Dead && !sim.PassIsThrown && guard++ < 400)
                {
                    sim.Step(default(ControlInput));
                    for (int i = 11; i < 22; i++)
                    {
                        SimAgent d = sim.Agents[i];
                        if (!d.IsFree) { idle[i] = 0f; continue; }
                        anyFree = true;
                        Assert.That(d.CoverTarget < 0 || sim.Agents[d.CoverTarget].IsReceiverEligible, "a free defender who picks a man picks a receiver");
                        if (d.Speed < 0.3f && sim.Time > 1.5f && (d.Pos - d.Landmark).Length > 3.5f) idle[i] += PlaySimulation.Dt; else idle[i] = 0f;
                        Assert.Less(idle[i], 0.8f, "a free defender is never a statue away from his hook");
                    }
                }
                plays++; if (anyFree) freed++;
            }
            TestContext.WriteLine("plays with a free defender " + freed + " of " + plays);
            Assert.Greater(freed, 0, "the release rule fires");
        }

        [Test]
        public void ManDefenderIsNotReleasedOnTheFirstTickOfABlockAssignment()
        {
            Play off = SimTestKit.Offense("Corner-Post", "Shotgun");
            Play def = SimTestKit.Defense("Cover 1 Man Free", "4-3 Base");
            var sim = SimTestKit.Run(off, def, 75, 75, 7, null, null, Quiet());
            SimTestKit.StepToLive(sim);
            SimTestKit.Steps(sim, 30);   // 0.5 s
            for (int i = 11; i < 22; i++) Assert.IsFalse(sim.Agents[i].IsFree && sim.Agents[i].RuleReleases > 0, sim.Agents[i].PlayPos + " was released before his man had shown he is blocking");
        }

        [Test]
        public void QuartersCornersAndSafetiesMatchTheReceiversWhoGoVertical()
        {
            Play off = SimTestKit.Offense("Four Verticals", "Shotgun");
            Play def = SimTestKit.Defense("Cover 4 Quarters", "4-3 Base");
            int matches = 0;
            for (int s = 0; s < 20; s++)
            {
                var sim = SimTestKit.Run(off, def, 75, 75, 300 + s, null, null, Quiet());
                sim.RunToCompletion();
                for (int i = 11; i < 22; i++) matches += sim.Agents[i].RuleMatches;
            }
            Assert.Greater(matches, 20, "the quarters pattern-match fires against four vertical routes");
        }

        [Test]
        public void Cover3MatchCornersCarryTheVerticalOutsideReceiversLikeQuarters()
        {
            Play off = SimTestKit.Offense("Four Verticals", "Shotgun");
            Play def = SimTestKit.Defense("Cover 3 Match", "4-3 Base");
            Assert.IsTrue(def.Assignments.OfType<CoverageAssignment>().Any(c => c.Match), "the corners are match zones");
            Assert.IsFalse(SimTestKit.Defense("Cover 3 Sky", "4-3 Base").Assignments.OfType<CoverageAssignment>().Any(c => c.Match), "plain cover 3 is a zone, not a match");
            int matches = 0;
            for (int s = 0; s < 20; s++)
            {
                var sim = SimTestKit.Run(off, def, 75, 75, 400 + s, null, null, Quiet());
                sim.RunToCompletion();
                for (int i = 11; i < 22; i++) matches += sim.Agents[i].RuleMatches;
            }
            Assert.Greater(matches, 10);
        }

        [Test]
        public void ARollingQuarterbackBehindTheLineDoesNotPullTheCoverageOffItsReceivers()
        {
            // the boot rolls the passer to the strong side; the underneath zones shade with him a yard or two but nobody abandons a zone until he crosses the line
            Play off = SimTestKit.Offense("PA Boot", "I-Form");
            Play def = SimTestKit.Defense("Cover 3 Sky", "4-3 Base");
            CoverageAuditStats t = new CoverageAuditStats();
            for (int s = 0; s < 60; s++) t.Merge(CoverageAudit.Run(off, def, 500 + s));
            long scrambleViol = t.Viol.Where(kv => kv.Key.StartsWith("scr:")).Sum(kv => kv.Value);
            TestContext.WriteLine(CoverageAudit.Report(t));
            Assert.That(t.Rate, Is.LessThan(0.01));
            Assert.That(scrambleViol, Is.LessThan(30), "violation frames inside the scramble window");
        }

        // ------------------------------------------------------------------ alignment, fronts, shells

        [Test]
        public void EveryFrontLinesTheLineUpByTechniqueOnTheRealOffensiveLine()
        {
            var expect = new Dictionary<string, int[]>   // strong side right (a tight end on the right): left to right
            {
                { "4-3 Base", new[] { 5, 3, 1, 7 } }, { "4-3 Over", new[] { 5, 1, 3, 7 } }, { "Nickel", new[] { 5, 3, 1, 7 } }, { "Dime", new[] { 5, 3, 1, 7 } },
                { "46 Bear", new[] { 5, 2, 2, 7 } }, { "3-4 Base", new[] { 5, 0, 4 } }, { "3-3-5", new[] { 5, 0, 7 } }
            };
            Play off = SimTestKit.Offense("Inside Zone Right", "I-Form");   // tight end on the right
            foreach (var kv in expect)
            {
                Play def = SimTestKit.Playbook.AllPlays.First(p => p.Side == PlaySide.Defense && p.FormationName == kv.Key);
                var sim = SimTestKit.Run(off, def, 75, 75, 3, null, null, new PlaySimOptions { Penalties = false, AutoSnap = false });
                int[] tech = DefenseAlignment.AlignLine(sim);
                Assert.NotNull(tech, kv.Key);
                CollectionAssert.AreEqual(kv.Value, tech, kv.Key + " strong right: " + string.Join(",", tech));
                var ol = Enumerable.Range(0, 11).Select(i => sim.Agents[i]).Where(a => PositionGroup.IsOffensiveLine(a.PlayPos) || a.PlayPos == Position.TE).ToList();
                foreach (SimAgent d in Enumerable.Range(11, 11).Select(i => sim.Agents[i]).Where(a => PositionGroup.IsDefensiveLine(a.PlayPos)))
                    Assert.LessOrEqual(ol.Min(a => Math.Abs(a.StartPos.X - d.StartPos.X)), 0.75f, kv.Key + ": " + d.PlayPos + " is lined up on nobody");
            }
        }

        [Test]
        public void TheFrontMirrorsWhenTheStrengthIsToTheLeft()
        {
            Play off = SimTestKit.Offense("Inside Zone Right", "I-Form");
            Play def = SimTestKit.Defense("Cover 3 Sky", "4-3 Over");
            var sim = SimTestKit.Run(off, def, 75, 75, 3, null, null, new PlaySimOptions { Penalties = false, AutoSnap = false });
            Assert.IsTrue(sim.FlipOffense());   // the tight end is on the left now
            int[] tech = DefenseAlignment.AlignLine(sim);
            CollectionAssert.AreEqual(new[] { 7, 3, 1, 5 }, tech, "the over front to the left: strong end, strong three-tech, weak one-tech, weak end");
        }

        [Test]
        public void EachTechniqueOwnsItsGap()
        {
            Assert.AreEqual(RunGap.ARight, DefenseAlignment.GapOfTechnique(1, 1));
            Assert.AreEqual(RunGap.ALeft, DefenseAlignment.GapOfTechnique(0, -1));
            Assert.AreEqual(RunGap.BRight, DefenseAlignment.GapOfTechnique(3, 1));
            Assert.AreEqual(RunGap.BLeft, DefenseAlignment.GapOfTechnique(4, -1));
            Assert.AreEqual(RunGap.CRight, DefenseAlignment.GapOfTechnique(5, 1));
            Assert.AreEqual(RunGap.DLeft, DefenseAlignment.GapOfTechnique(7, -1));
            // and the agents carry it: a 3-technique tackle has the B gap, the one-technique the A gap
            Play off = SimTestKit.Offense("Inside Zone Right", "I-Form");
            var sim = SimTestKit.Run(off, SimTestKit.Defense("Cover 3 Sky", "4-3 Base"), 75, 75, 3, null, null, new PlaySimOptions { Penalties = false, AutoSnap = false });
            foreach (SimAgent d in Enumerable.Range(11, 11).Select(i => sim.Agents[i]).Where(a => a.Technique >= 0))
            {
                Assert.IsTrue(d.HasTechGap);
                RunGap expect = DefenseAlignment.GapOfTechnique(d.Technique, d.StartPos.X >= sim.BallX0 ? 1 : -1);
                Assert.AreEqual(expect, d.TechGap, d.PlayPos + " tech " + d.Technique);
            }
        }

        [Test]
        public void ADisguisedCoverageShowsTwoHighAndRotatesIntoItsRealLandmarksAtTheSnap()
        {
            Play off = SimTestKit.Offense("Four Verticals", "Shotgun");
            Play def = SimTestKit.Defense("Cover 3 Buzz", "4-3 Base");   // shows two-high, plays three deep with the strong safety buzzing down
            Assert.AreEqual(ShellLook.TwoHigh, DefaultPlaybooks.EffectiveShell(def));
            var sim = SimTestKit.Run(off, def, 75, 75, 3, null, null, new PlaySimOptions { Penalties = false, AutoSnap = false });
            var safeties = Enumerable.Range(11, 11).Select(i => sim.Agents[i]).Where(a => PositionGroup.IsSafety(a.PlayPos)).OrderBy(a => a.Pos.X).ToList();
            Assert.AreEqual(2, safeties.Count);
            Assert.That(safeties[0].Pos.X - sim.BallX0, Is.EqualTo(-ShellLayout.TwoHighX).Within(0.2f), "the left safety on his hash");
            Assert.That(safeties[1].Pos.X - sim.BallX0, Is.EqualTo(ShellLayout.TwoHighX).Within(0.2f), "the right safety on his hash");
            Assert.That(safeties[0].Pos.Y - sim.LosY, Is.EqualTo(ShellLayout.TwoHighDepth).Within(0.2f));
            Snap(sim);
            SimTestKit.Steps(sim, 140);   // 2.3 s
            var fs = Enumerable.Range(11, 11).Select(i => sim.Agents[i]).First(a => a.Zone != null && a.Zone.Kind == ZoneKind.DeepThird && Math.Abs(a.Zone.CenterX) < 1f);
            Assert.That(Math.Abs(fs.Pos.X - FieldGeometry.CenterX), Is.LessThan(5f), "the middle-third safety has rotated to the middle of the field");
            var buzz = Enumerable.Range(11, 11).Select(i => sim.Agents[i]).First(a => a.Zone != null && a.Zone.Kind == ZoneKind.Buzz);
            Assert.That(buzz.Pos.Y - sim.LosY, Is.LessThan(10f), "the strong safety has rotated down from the shell to the buzz zone");
        }

        [Test]
        public void ZeroAndOneHighShellsHaveTheRightNumberOfDeepSafeties()
        {
            Play off = SimTestKit.Offense("Slants", "Shotgun");
            foreach (var kv in new Dictionary<string, int> { { "Cover 0 Blitz", 0 }, { "Cover 1 Man Free", 1 }, { "Cover 2 Zone", 2 }, { "Cover 4 Match", 1 }, { "Cover 3 Sky", 1 }, { "Cover 3 Buzz", 2 } })
            {
                Play def = SimTestKit.Playbook.AllPlays.First(p => p.Side == PlaySide.Defense && p.Name == kv.Key && p.FormationName == "Nickel");
                var sim = SimTestKit.Run(off, def, 75, 75, 3, null, null, new PlaySimOptions { Penalties = false, AutoSnap = false });
                int deep = Enumerable.Range(11, 11).Select(i => sim.Agents[i]).Count(a => PositionGroup.IsSafety(a.PlayPos) && a.Pos.Y - sim.LosY >= 9.5f);
                Assert.AreEqual(kv.Value, deep, kv.Key + " shows " + deep + " deep safeties");
            }
        }

        [Test]
        public void AScramblingQuarterbackIsRalliedOnOnlyAfterHeCommitsToRun()
        {
            // the defenders react to a committed runner, not to a passer drifting behind the line: no deep zone abandons its depth while the passer is behind the line in a scramble
            int scrambles = 0, abandoned = 0;
            Play off = SimTestKit.Offense("Four Verticals", "Shotgun");
            Play def = SimTestKit.Defense("Cover 3 Sky", "4-3 Base");
            for (int s = 0; s < 120; s++)
            {
                var sim = SimTestKit.Run(off, def, 75, 99, 800 + s, null, null, Quiet());
                SimTestKit.StepToLive(sim);
                bool sawScramble = false; int guard = 0;
                while (sim.Phase != PlayPhase.Dead && !sim.PassIsThrown && !sim.QbHasCrossedLos && guard++ < 600)
                {
                    sim.Step(default(ControlInput));
                    if (sim.Qb.QbState == QbPhase.Scramble && sim.Qb.Pos.Y < sim.LosY - 1.5f)
                    {
                        sawScramble = true;
                        foreach (SimAgent d in Enumerable.Range(11, 11).Select(i => sim.Agents[i]).Where(a => a.Zone != null && a.Zone.Kind == ZoneKind.DeepThird))
                            if (sim.Time > 2.4f && d.Pos.Y - sim.LosY < 8f) abandoned++;
                    }
                }
                if (sawScramble) scrambles++;
            }
            TestContext.WriteLine("scrambles " + scrambles + " deep-defender frames up at the line " + abandoned);
            Assert.AreEqual(0, abandoned, "a deep third never comes up to the line while the passer is still behind it");
        }
    }
}
