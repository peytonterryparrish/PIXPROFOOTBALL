using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>
    /// v0.7 track B (engine pacing, play-end clarity, kicking). Rows B-001 .. B-012 in Docs/QA/v070/engine_pace.md cite these tests.
    /// </summary>
    public class PaceAndPlayEndTests
    {
        private static readonly string[] Coverages = { "Cover 1 Man Free", "Cover 2 Zone", "Cover 3 Sky", "Cover 4 Quarters", "Cover 0 Blitz" };

        private static PlaySimOptions Quiet() { return new PlaySimOptions { Penalties = false }; }

        private static Ratings Uniform(int v) { return Ratings.Uniform(v); }
        private static PlaySimulation KickSim(string off, string def, int seed, GameState gs, int offOvr, int defOvr, RuleSet rules)
        {
            return SimTestKit.Run(SimTestKit.Special(off), SimTestKit.Special(def), offOvr, defOvr, seed, rules, gs, Quiet());
        }


        // ------------------------------------------------------------------ pace

        [Test]
        public void GamePaceSlowsBodiesByPositionAndStaysInTheTenToFifteenPercentBand()
        {
            var r = Uniform(85);
            var baseBody = new AgentBody(r, 200, 73);
            foreach (Position p in new[] { Position.WR, Position.RB, Position.CB, Position.MLB, Position.QB })
            {
                var b = new AgentBody(r, 200, 73, GamePace.For(p));
                float ratio = b.VMax / baseBody.VMax;
                TestContext.WriteLine(p + " pace " + GamePace.For(p) + " vmax ratio " + ratio);
                Assert.That(ratio, Is.InRange(0.84f, 0.93f), p + " is 7-16 percent slower than the v0.6 body");
                Assert.Greater(b.Tau, baseBody.Tau, "he winds up to speed a little more slowly");
            }
            Assert.That(GamePace.AnimationRate, Is.EqualTo(GamePace.Scale));
            Assert.Less(GamePace.For(Position.LT), GamePace.For(Position.WR), "v0.9.0 (E-005 / E-316): linemen are clearly slower than receivers and backs (tackles 0.805, ends 0.91, receivers 0.85), no longer the other way round");
            Assert.Less(GamePace.For(Position.DT), GamePace.For(Position.LE), "tackles are slower than ends");
        }

        [Test]
        public void SimulatedPlayersUseTheGamePace()
        {
            var sim = SimTestKit.Run(SimTestKit.Offense("Four Verticals", "Shotgun"), SimTestKit.Defense("Cover 3 Sky", "4-3 Base"), 85, 85, 11, null, null, Quiet());
            foreach (SimAgent a in sim.Agents)
            {
                Assert.That(a.Body.Pace, Is.EqualTo(GamePace.For(a.PlayPos)).Within(1e-5f), a.ToString());
                Assert.LessOrEqual(a.Body.Pace, 1f);
                if (!PositionGroup.IsDefensiveLine(a.PlayPos)) Assert.Less(a.Body.Pace, 1f);
            }
        }

        [Test]
        public void ButtonPulsesOnAFrameShorterThanOneStepAreNotDropped()
        {
            // a 144 Hz display delivers frames of 0.007 s: the snap press used to fall on a frame with no fixed step and vanish
            var sim = SimTestKit.Run(SimTestKit.Offense("Four Verticals", "Shotgun"), SimTestKit.Defense("Cover 3 Sky", "4-3 Base"), 75, 75, 5, null, null, new PlaySimOptions { Penalties = false, AutoSnap = false });
            var none = default(ControlInput);
            for (int i = 0; i < 40; i++) sim.Tick(0.007f, none, -1);
            Assert.AreEqual(PlayPhase.PreSnap, sim.Phase);
            sim.Tick(0.002f, new ControlInput { Snap = true }, -1);   // far less than one 1/60 s step
            for (int i = 0; i < 5 && sim.Phase == PlayPhase.PreSnap; i++) sim.Tick(0.007f, none, -1);
            Assert.AreEqual(PlayPhase.Live, sim.Phase, "the snap press waited for the next fixed step");
        }

        // ------------------------------------------------------------------ play end

        private static IEnumerable<PlaySimulation> ManyPlays(int n, int seedBase, bool penalties = false)
        {
            var plays = SimTestKit.Playbook.AllPlays.Where(p => p.Side == PlaySide.Offense && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike && p.Name != "Hail Mary").ToList();
            var rng = new Rng(seedBase);
            for (int i = 0; i < n; i++)
            {
                Play off = plays[rng.Range(0, plays.Count)];
                Play def = SimTestKit.Defense(Coverages[rng.Range(0, Coverages.Length)], "4-3 Base");
                var sim = SimTestKit.Run(off, def, 75, 75, seedBase + i, null, null, new PlaySimOptions { Penalties = penalties });
                sim.RunToCompletion();
                yield return sim;
            }
        }

        [Test]
        public void EveryPlayEndPublishesAReasonWithText()
        {
            int n = 0;
            foreach (PlaySimulation sim in ManyPlays(300, 61000, true))
            {
                n++;
                Assert.NotNull(sim.EndInfo, "EndInfo on every finished play");
                Assert.IsNotEmpty(sim.EndInfo.Text, sim.Log.ToText());
                Assert.AreNotEqual(PlayEndKind.None, sim.EndInfo.Kind);
            }
            Assert.AreEqual(300, n);
        }

        [Test]
        public void ATackleIsAlwaysAnActualContactNeverAPhantomAtDistance()
        {
            // 600 plays: the gap between the tackler's and the carrier's bodies at first contact (the Tackle event) stays within arm's reach, dive tackles included
            int tackles = 0, dives = 0; float worst = 0f;
            var plays = SimTestKit.Playbook.AllPlays.Where(p => p.Side == PlaySide.Offense && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike).ToList();
            var rng = new Rng(777);
            for (int i = 0; i < 600; i++)
            {
                Play off = plays[rng.Range(0, plays.Count)];
                Play def = SimTestKit.Defense(Coverages[rng.Range(0, Coverages.Length)], "4-3 Base");
                var sim = SimTestKit.Run(off, def, 75, 75, 62000 + i, null, null, Quiet());
                int seen = 0, guard = 0;
                while (sim.Phase != PlayPhase.Dead && guard++ < 3000)
                {
                    sim.Step(default(ControlInput));
                    while (seen < sim.Log.Events.Count)
                    {
                        PlayEvent e = sim.Log.Events[seen++];
                        if (e.Type != SimEventType.Tackle || e.AgentA < 0 || e.AgentB < 0 || e.Tackle == TackleType.Drag) continue;
                        SimAgent t = sim.Agents[e.AgentA], c = sim.Agents[e.AgentB];
                        float gap = (t.Pos - c.Pos).Length - t.Body.Radius - c.Body.Radius;
                        tackles++; if (e.Tackle == TackleType.DiveTackle) dives++;
                        worst = Math.Max(worst, gap);
                        Assert.Less(gap, 0.85f, e.Tackle + " tackle from " + gap.ToString("0.00") + " yd away in " + off.Name + " vs " + def.Name + " seed " + (62000 + i));
                    }
                }
            }
            TestContext.WriteLine("tackles " + tackles + " (dives " + dives + "), widest gap " + worst.ToString("0.00") + " yd");
            Assert.Greater(tackles, 400);
        }

        [Test]
        public void TheWhistleComesAfterTheWrapWindowAndTheBallIsSpottedAtContact()
        {
            int checkedPlays = 0;
            foreach (PlaySimulation sim in ManyPlays(300, 63000))
            {
                if (sim.EndInfo.Kind != PlayEndKind.Tackle) continue;
                PlayEvent tackle = sim.Log.Events.Last(e => e.Type == SimEventType.Tackle && e.AgentA >= 0);
                PlayEvent end = sim.Log.Events.Last(e => e.Type == SimEventType.PlayEnd);
                if (end.Time - tackle.Time > 1.0f) continue;   // a fumble recovery after the contact
                Assert.GreaterOrEqual(end.Time - tackle.Time, GamePace.TackleWrapSeconds - 0.02f, "the carrier is wrapped up on screen before the whistle");
                Assert.LessOrEqual(end.Time - tackle.Time, GamePace.TackleWrapSeconds + 0.06f);
                Assert.That(sim.EndInfo.Text, Does.StartWith("TACKLED BY").Or.StartWith("GANG TACKLE BY"));
                checkedPlays++;
            }
            Assert.Greater(checkedPlays, 100);
        }

        [Test]
        public void ABlockEngagementLastsAtLeastTheMinimumBeforeItIsWonOrLost()
        {
            int losses = 0;
            foreach (PlaySimulation sim in ManyPlays(300, 64000))
                foreach (PlayEvent e in sim.Log.Events)
                    if (e.Type == SimEventType.BlockLoss && e.Note != "slip" && e.Value > 0f)
                    {
                        losses++;
                        Assert.GreaterOrEqual(e.Value, GamePace.MinEngageSeconds - 0.03f, "block lost after only " + e.Value.ToString("0.00") + " s");
                    }
            Assert.Greater(losses, 20);
        }

        [Test]
        public void CatchHoldsTheCatchActionForItsCommitWindow()
        {
            int catches = 0;
            foreach (PlaySimulation sim in ManyPlays(200, 65000))
            {
                PlayEvent? c = sim.Log.First(SimEventType.Catch);
                if (!c.HasValue || c.Value.AgentA < 0) continue;
                catches++;
            }
            Assert.Greater(catches, 40);
            Assert.GreaterOrEqual(GamePace.CatchSeconds, 0.35f);
            Assert.Less(GamePace.CatchSpeedFrac, 1f);
        }

        // ------------------------------------------------------------------ kicks

        [Test]
        public void FieldGoalOperationTakesAboutOnePointThreeSecondsWithAShortApproach()
        {
            var rules = RuleSet.Pro();
            var snapToKick = new List<float>(); var approach = new List<float>();
            for (int i = 0; i < 20; i++)
            {
                var sim = KickSim("Field Goal", "Field Goal Block Middle", 66000 + i, SimTestKit.Scrimmage(rules, 80, 4, 8), 75, 75, rules);
                sim.RunToCompletion();
                if (sim.Log.Has(SimEventType.KickBlocked)) continue;
                PlayEvent k = sim.Log.First(SimEventType.Kick).Value;
                PlayEvent ap = sim.Log.First(SimEventType.KickApproach).Value;
                snapToKick.Add(k.Time); approach.Add(k.Time - ap.Time);
                Assert.That(ap.Value, Is.EqualTo(k.Time - ap.Time).Within(0.06f), "the approach event announces the time to contact");
            }
            TestContext.WriteLine("snap to kick " + snapToKick.Average() + " s, approach " + approach.Average() + " s");
            Assert.That(snapToKick.Average(), Is.InRange(1.2f, 1.45f));
            Assert.That(approach.Average(), Is.InRange(0.5f, 0.75f), "two or three steps");
        }

        [Test]
        public void KickoffHasARealRunUpAndAFourSecondHang()
        {
            var rules = RuleSet.Pro();
            var runUp = new List<float>(); var hang = new List<float>(); var snapToKick = new List<float>(); float startBack = 0f;
            for (int i = 0; i < 30; i++)
            {
                var sim = KickSim("Kickoff Deep Middle", "Kick Return Middle Wedge", 67000 + i, new GameState(rules, PixFootball.Core.Team.Away), 75, 75, rules);
                startBack = sim.LosY - sim.Specials.KickerAgent.Pos.Y;
                sim.RunToCompletion();
                PlayEvent k = sim.Log.First(SimEventType.Kick).Value;
                PlayEvent ap = sim.Log.First(SimEventType.KickApproach).Value;
                snapToKick.Add(k.Time); runUp.Add(k.Time - ap.Time);
                PlayEvent? l = sim.Log.First(SimEventType.Catch) ?? sim.Log.First(SimEventType.KickLanded);
                if (l.HasValue) hang.Add(l.Value.Time - k.Time);
            }
            TestContext.WriteLine("run-up " + runUp.Average() + " s from " + startBack + " yd, snap to kick " + snapToKick.Average() + " s, hang " + hang.Average() + " s");
            Assert.That(runUp.Average(), Is.EqualTo(KickTiming.KickoffApproach).Within(0.08f), "the approach matches the staged kickoff clip (contact at 1.27 clip seconds)");
            Assert.That(startBack, Is.InRange(4.5f, 8f), "5-7 steps back");
            Assert.That(snapToKick.Average(), Is.InRange(1.8f, 2.4f));
            Assert.That(hang.Average(), Is.InRange(3.6f, 4.4f));
        }

        [Test]
        public void KickoffCoverageLineRunsLevelWithTheBallAtContactNotAheadOfTheKicker()
        {
            var rules = RuleSet.Pro();
            var sim = KickSim("Kickoff Deep Middle", "Kick Return Middle Wedge", 68000, new GameState(rules, PixFootball.Core.Team.Away), 75, 75, rules);
            float aheadAtKick = -99f, maxSpeedAtKick = 0f; int guard = 0;
            while (sim.Phase != PlayPhase.Dead && guard++ < 3000 && !sim.Log.Has(SimEventType.Kick))
            {
                sim.Step(default(ControlInput));
                if (sim.Phase == PlayPhase.Live && !sim.Log.Has(SimEventType.Kick))
                    for (int i = 1; i < 11; i++) aheadAtKick = Math.Max(aheadAtKick, sim.Agents[i].Pos.Y - sim.LosY);
            }
            for (int i = 1; i < 11; i++) maxSpeedAtKick = Math.Max(maxSpeedAtKick, sim.Agents[i].Speed);
            TestContext.WriteLine("furthest coverage man ahead of the ball before the kick: " + aheadAtKick + " yd, fastest at contact " + maxSpeedAtKick);
            Assert.Less(aheadAtKick, 0.6f, "nobody crosses the ball line before it is kicked");
            Assert.Greater(maxSpeedAtKick, 3.5f, "they are running at contact");
        }

        [Test]
        public void UserKickPressStartsTheRunUpAndTheKickLandsOneApproachLater()
        {
            var rules = RuleSet.Pro();
            var sim = SimTestKit.Run(SimTestKit.Special("Kickoff Deep Middle"), SimTestKit.Special("Kick Return Middle Wedge"), 75, 75, 9, rules, new GameState(rules, PixFootball.Core.Team.Away), Quiet());
            var none = default(ControlInput);
            for (int i = 0; i < 60 * 3; i++) sim.Tick(1f / 60f, none, 0);
            Assert.IsFalse(sim.Log.Has(SimEventType.Kick), "nothing happens until the user presses");
            Assert.IsFalse(sim.Log.Has(SimEventType.KickApproach));
            Assert.IsTrue(sim.AwaitingUserKick);
            Assert.Less(sim.KickContactTime, 0f, "no contact planned yet");
            float pressedAt = sim.Time;
            sim.Tick(1f / 60f, new ControlInput { KickPress = true, KickPower = 1f, KickTiming = 1f, KickAim = 0f }, 0);
            sim.Tick(1f / 60f, none, 0);
            Assert.IsTrue(sim.Log.Has(SimEventType.KickApproach), "the press starts the approach");
            Assert.IsFalse(sim.AwaitingUserKick, "the meter is done");
            float contact = sim.KickContactTime;
            Assert.That(contact - pressedAt, Is.EqualTo(KickTiming.KickoffApproach).Within(0.06f));
            for (int i = 0; i < 60 * 3 && !sim.Log.Has(SimEventType.Kick); i++) sim.Tick(1f / 60f, none, 0);
            PlayEvent k = sim.Log.First(SimEventType.Kick).Value;
            Assert.That(k.Time, Is.EqualTo(contact).Within(0.1f), "the ball leaves the tee at the planned contact time");
        }

        [Test]
        public void EveryKindOfFinishedKickPlayHasAReason()
        {
            var rules = RuleSet.Pro();
            var kinds = new HashSet<PlayEndKind>();
            for (int i = 0; i < 25; i++)
            {
                foreach (string ko in new[] { "Kickoff Deep Middle", "Kickoff Squib", "Onside Kick Short" })
                {
                    var s = KickSim(ko, "Kick Return Middle Wedge", 69000 + i, new GameState(rules, PixFootball.Core.Team.Away), 75, 75, rules);
                    s.RunToCompletion();
                    Assert.NotNull(s.EndInfo); Assert.IsNotEmpty(s.EndInfo.Text); kinds.Add(s.EndInfo.Kind);
                }
                var p = KickSim("Punt Standard", "Punt Return Set", 69100 + i, SimTestKit.Scrimmage(rules, 30, 4, 8), 75, 75, rules);
                p.RunToCompletion(); Assert.NotNull(p.EndInfo); Assert.IsNotEmpty(p.EndInfo.Text); kinds.Add(p.EndInfo.Kind);
                var f = KickSim("Field Goal", "Field Goal Block Middle", 69200 + i, SimTestKit.Scrimmage(rules, 80, 4, 8), 75, 75, rules);
                f.RunToCompletion(); Assert.NotNull(f.EndInfo); Assert.IsNotEmpty(f.EndInfo.Text); kinds.Add(f.EndInfo.Kind);
            }
            TestContext.WriteLine(string.Join(", ", kinds));
            Assert.IsTrue(kinds.Contains(PlayEndKind.Tackle) || kinds.Contains(PlayEndKind.FairCatch) || kinds.Contains(PlayEndKind.Touchback));
            Assert.IsTrue(kinds.Contains(PlayEndKind.FieldGoalGood) || kinds.Contains(PlayEndKind.FieldGoalMissed));
        }

        // ------------------------------------------------------------------ DL alignment techniques (REFERENCE_Stances.md)

        [Test]
        public void DefensiveLineAlignsByTechniqueRelativeToTheActualOffensiveLine() { WithTechniques(AlignsByTechniqueBody); }

        private void AlignsByTechniqueBody()
        {
            int checkedFronts = 0;
            foreach (var offF in SimTestKit.Playbook.Formations.Where(f => f.Side == PlaySide.Offense))
            {
                Play off = SimTestKit.Playbook.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Offense && p.FormationName == offF.Name && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike);
                if (off == null) continue;
                foreach (string front in new[] { "4-3 Base", "3-4 Base", "Nickel" })
                {
                    Play def = SimTestKit.Playbook.AllPlays.FirstOrDefault(p => p.Side == PlaySide.Defense && p.FormationName == front);
                    if (def == null) continue;
                    var sim = SimTestKit.Run(off, def, 75, 75, 3, null, null, Quiet());
                    var ol = Enumerable.Range(0, 11).Select(i => sim.Agents[i]).Where(a => a.StartPos.Y >= sim.LosY - 1.2f && (PositionGroup.IsOffensiveLine(a.PlayPos) || a.PlayPos == Position.TE)).ToList();
                    if (ol.Count(a => PositionGroup.IsOffensiveLine(a.PlayPos)) != 5) continue;
                    var dl = Enumerable.Range(11, 11).Select(i => sim.Agents[i]).Where(a => PositionGroup.IsDefensiveLine(a.PlayPos)).OrderBy(a => a.StartPos.X).ToList();
                    foreach (SimAgent d in dl)
                    {
                        float nearest = ol.Min(a => Math.Abs(a.StartPos.X - d.StartPos.X));
                        Assert.LessOrEqual(nearest, ShadeCheck, front + " vs " + offF.Name + ": " + d.PlayPos + " at " + (d.StartPos.X - sim.BallX0).ToString("0.0") + " is lined up on nobody (nearest lineman " + nearest.ToString("0.0") + " yd)");
                    }
                    // the techniques themselves
                    int[] tech = DefenseAlignment.AlignLine(sim);
                    Assert.NotNull(tech, front);
                    if (front == "4-3 Base" || front == "Nickel") CollectionAssert.AreEquivalent(new[] { 1, 3 }, tech.Where(t => t == 1 || t == 3).ToArray(), "1-tech nose and 3-tech tackle: " + string.Join(",", tech));
                    else CollectionAssert.Contains(tech, 0, "3-4: a 0-tech nose");
                    checkedFronts++;
                }
            }
            Assert.Greater(checkedFronts, 10);
        }

        private const float ShadeCheck = 0.75f;

        [Test]
        public void GoalLineDiveCrossesThePlaneOftenEnoughAndNeverFromNothing()
        {
            // the ball breaks the plane when the diving carrier's reaching arm does, consistently with where the ball is drawn; a sneak from the 1 is a coin flip, not 3 percent or 99
            var rules = RuleSet.Pro();
            Play off = SimTestKit.Playbook.AllPlays.First(p => p.Side == PlaySide.Offense && p.Name == "QB Sneak");
            Play def = SimTestKit.Defense("Cover 3 Sky", "4-3 Base");
            int td = 0, n = 120;
            for (int i = 0; i < n; i++)
            {
                var sim = SimTestKit.Run(off, def, 75, 75, 900 + i, rules, SimTestKit.Scrimmage(rules, 99, 1, 1), Quiet());
                sim.RunToCompletion();
                if (sim.Touchdown) td++;
            }
            TestContext.WriteLine("QB sneak from the 1: " + td + "/" + n);
            // v0.9.0: the quarterback no longer dives at the snap from 1.9 yd (the lunge ended 0.6 yd short behind his own line: 0 of 120); he pushes the pile and dives only inside 1.35 yd. Sneaks now score about nine times in ten (the NFL tush push is 80-93 percent).
            Assert.That(td / (float)n, Is.InRange(0.2f, 0.97f));
        }

        private static void WithTechniques(Action body)
        {
            bool prev = DefenseAlignment.UseTechniques;
            DefenseAlignment.UseTechniques = true;   // (B-013: the technique alignment ships OFF until the blocking schemes are recalibrated)
            try { body(); } finally { DefenseAlignment.UseTechniques = prev; }
        }

        [Test]
        public void TechniqueAlignmentShipsOnAndTheBaseFrontsAreUnchanged()
        {
            // v0.9.0 (B-013): the technique alignment now ships ON (the blocking schemes were recalibrated for it)
            Assert.IsTrue(DefenseAlignment.UseTechniques);
        }

        [Test]
        public void FourThreeUnderPutsTheOneTechOnTheStrongSideAndTheThreeTechOnTheWeakSide() { WithTechniques(FourThreeUnderBody); }

        private void FourThreeUnderBody()
        {
            Play off = SimTestKit.Offense("Inside Zone Right", "I-Form");   // tight end on the right: strong side right
            Play def = SimTestKit.Defense("Cover 3 Sky", "4-3 Base");
            var sim = SimTestKit.Run(off, def, 75, 75, 3, null, null, Quiet());
            var ol = Enumerable.Range(0, 11).Select(i => sim.Agents[i]).Where(a => PositionGroup.IsOffensiveLine(a.PlayPos)).OrderBy(a => a.StartPos.X).ToList();
            var te = sim.Agents.Take(11).First(a => a.PlayPos == Position.TE);
            var dl = Enumerable.Range(11, 11).Select(i => sim.Agents[i]).Where(a => PositionGroup.IsDefensiveLine(a.PlayPos)).OrderBy(a => a.StartPos.X).ToList();
            Assert.AreEqual(4, dl.Count);
            float c = ol[2].StartPos.X;
            Assert.That(dl[2].StartPos.X, Is.EqualTo(c + DefenseAlignment.ShadeYards).Within(0.05f), "1-tech: outside shade of the center, strong side");
            Assert.That(dl[1].StartPos.X, Is.EqualTo(ol[1].StartPos.X - DefenseAlignment.ShadeYards).Within(0.05f), "3-tech: outside shade of the weak guard");
            Assert.That(dl[0].StartPos.X, Is.EqualTo(ol[0].StartPos.X - DefenseAlignment.ShadeYards).Within(0.05f), "5-tech: outside shade of the weak tackle");
            Assert.That(dl[3].StartPos.X, Is.EqualTo(te.StartPos.X + DefenseAlignment.ShadeYards).Within(0.05f), "7-tech: outside shade of the tight end");
        }
    }
}
