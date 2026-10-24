using System;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>v0.11.0 QB (playtest notes 8 / 10): the quarterback stays in the throwing stance until he decides to run, and the keyed throw opens the hips before the shoulders.</summary>
    public class QbStanceV11Tests
    {



        [Test]
        public void ComputerQuarterbackOnlyRunsAfterTheEscapeWorkIsOver()
        {
            // play the pass plays against blitzes: whenever he scrambles, every frame before the takeoff decision has QbTakeoffIntent false, and a takeoff never happens in the pocket
            var book = SimTestKit.Playbook; int scrambles = 0, takeoffs = 0;
            var plays = book.AllPlays.Where(p => p.Side == PlaySide.Offense && p.Assignments.Length > 0 && p.Assignments[0] is QbAssignment && ((QbAssignment)p.Assignments[0]).Action == QbAction.DropbackPass).Take(12).ToList();
            var blitz = book.AllPlays.Where(p => p.Side == PlaySide.Defense && p.Category == PlayCategory.Blitz && p.FormationName == "4-3 Base").ToList();
            for (int i = 0; i < 160; i++)
            {
                var sim = SimTestKit.Run(plays[i % plays.Count], blitz[i % blitz.Count], 75, 75, 90000 + i);
                SimTestKit.StepToLive(sim);
                bool sawScramble = false, took = false; int guard = 0;
                while (sim.Phase != PlayPhase.Dead && guard++ < 1800)
                {
                    sim.Step(default(ControlInput));
                    var q = sim.Agents[0];
                    if (q.QbState == QbPhase.Scramble) sawScramble = true;
                    if (sim.QbTakeoffIntent && q.QbState == QbPhase.Set) Assert.Fail("takeoff while he is set in the pocket");
                    if (sim.QbTakeoffIntent && q.QbState == QbPhase.Windup) Assert.Fail("takeoff during the wind-up");
                    if (sim.QbTakeoffIntent) took = true;
                }
                if (sawScramble) scrambles++; if (took) takeoffs++;
            }
            TestContext.WriteLine("QBSTANCE scrambles " + scrambles + ", takeoffs " + takeoffs);
            Assert.Greater(scrambles, 0);
        }
    }
}
