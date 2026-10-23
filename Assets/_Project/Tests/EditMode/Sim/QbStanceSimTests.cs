using System;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>v0.9.0 QBSTANCE (sim side): the right-footed kicker stands LEFT of the ball, the quarterback's drop turns him through a reverse pivot, the release waits for the body, the pocket stays alive.</summary>
    public class QbStanceSimTests
    {
        private static PlaySimOptions Quiet() { return new PlaySimOptions { Penalties = false }; }

        [Test]
        public void KickoffKickerStandsLeftOfTheTeeAndPlantsLeftOfIt()
        {
            var rules = RuleSet.Pro();
            PlaySimulation sim = SimTestKit.Run(SimTestKit.Special("Kickoff Deep Middle"), SimTestKit.Special("Kick Return Middle Wedge"), 75, 75, 4, rules, new GameState(rules, PixFootball.Core.Team.Away), Quiet());
            SimAgent k = sim.Specials.KickerAgent;
            Assert.Less(k.Pos.X, sim.BallX0 - 2.0f, "pre-snap: the kicker is left of the ball seen from behind");
            // run the kick: at contact the plant foot is beside the ball on the left
            sim.Tick(PlaySimulation.Dt, new ControlInput { Snap = true }, -1);
            float xAtContact = float.NaN; int guard = 0;
            while (sim.Phase != PlayPhase.Dead && guard++ < 600)
            {
                sim.Tick(PlaySimulation.Dt, default(ControlInput), -1);
                if (sim.Log.Has(SimEventType.Kick) && float.IsNaN(xAtContact)) { xAtContact = k.Pos.X; break; }
            }
            Assert.IsFalse(float.IsNaN(xAtContact), "the ball was kicked");
            Assert.Less(xAtContact, sim.BallX0 + 0.1f, "the kicker is at or left of the ball at contact");
        }

        [Test]
        public void FieldGoalKickerStartsLeftRearOfTheHolder()
        {
            var rules = RuleSet.Pro();
            PlaySimulation sim = SimTestKit.Run(SimTestKit.Special("Field Goal"), SimTestKit.Special("Field Goal Block Middle"), 75, 75, 3, rules, SimTestKit.Scrimmage(rules, 70, 4, 8), Quiet());
            SimAgent k = sim.Specials.KickerAgent, h = sim.Specials.HolderAgent;
            Assert.Less(k.Pos.X, h.Pos.X - 0.7f, "the (right-footed) kicker stands to the left of the holder");
            Assert.Less(k.Pos.Y, h.Pos.Y - 1.2f, "and behind him");
        }

        [Test]
        public void PunterStandsALittleLeftOfTheBallLine()
        {
            var rules = RuleSet.Pro();
            var punt = SimTestKit.Playbook.AllPlays.First(p => p.Side == PlaySide.Special && p.Category == PlayCategory.Punt);
            var ret = SimTestKit.Playbook.AllPlays.First(p => p.Side == PlaySide.Special && p.Category == PlayCategory.PuntReturn);
            PlaySimulation sim = SimTestKit.Run(punt, ret, 75, 75, 5, rules, SimTestKit.Scrimmage(rules, 30, 4, 8), Quiet());
            SimAgent p0 = sim.Specials.KickerAgent;
            Assert.NotNull(p0);
            Assert.That(sim.BallX0 - p0.Pos.X, Is.InRange(0.2f, 1.2f), "the punter is about 0.4 m left of the ball line");
        }

        static SimAgent QbOf(PlaySimulation sim) { return sim.Agents[0]; }

        [Test]
        public void UnderCenterDropTurnsTheBodyToFaceTheDropAndShotgunKeepsFacingTheLine()
        {
            // under centre: the reverse pivot
            var uc = SimTestKit.RunNamed("Curl-Flat", "Singleback", "Cover 4 Quarters", "4-3 Base", 75, 75, 5, RuleSet.Pro(), null, Quiet());
            float minFaceY = 1f; int guard = 0;
            uc.Tick(PlaySimulation.Dt, new ControlInput { Snap = true }, -1);
            while (uc.Phase != PlayPhase.Dead && guard++ < 240 && QbOf(uc).QbState != QbPhase.Set && QbOf(uc).QbState != QbPhase.Windup)
            {
                uc.Tick(PlaySimulation.Dt, default(ControlInput), -1);
                if (QbOf(uc).QbState == QbPhase.Dropback) minFaceY = Math.Min(minFaceY, QbOf(uc).Facing.Y);
            }
            Assert.Less(minFaceY, 0.5f, "under centre the quarterback opens at least 60 degrees from the line during the drop (reverse pivot, v0.10.0: 72 degrees, never his back to the field)");
            Assert.Greater(minFaceY, -0.1f, "but he never turns his back on the field");
            // shotgun: eyes on the line
            var sg = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 5, RuleSet.Pro(), null, Quiet());
            float minY = 1f; guard = 0;
            sg.Tick(PlaySimulation.Dt, new ControlInput { Snap = true }, -1);
            while (sg.Phase != PlayPhase.Dead && guard++ < 240 && QbOf(sg).QbState != QbPhase.Windup)
            {
                sg.Tick(PlaySimulation.Dt, default(ControlInput), -1);
                if (QbOf(sg).QbState == QbPhase.Dropback || QbOf(sg).QbState == QbPhase.Set) minY = Math.Min(minY, QbOf(sg).Facing.Y);
            }
            Assert.Greater(minY, 0.6f, "the shotgun quarterback keeps his shoulders to the line");
        }

        [Test]
        public void ReleaseWaitsForTheBodyToFaceTheTarget()
        {
            foreach (int seed in new[] { 3, 8, 12 })
            {
                var sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, seed, RuleSet.Pro(), null, Quiet());
                sim.Tick(PlaySimulation.Dt, new ControlInput { Snap = true }, -1);
                int guard = 0; bool thrown = false;
                while (sim.Phase != PlayPhase.Dead && guard++ < 900 && !thrown)
                {
                    sim.Tick(PlaySimulation.Dt, default(ControlInput), -1);
                    if (sim.Ball.State == BallState.Pass && sim.Ball.Intended >= 0)
                    {
                        thrown = true;
                        Vec2 to = sim.Agents[sim.Ball.Intended].Pos - QbOf(sim).Pos;
                        float ang = SimMath.AngleBetween(QbOf(sim).Facing, to.Norm());
                        Assert.Less(ang, 0.7f, "seed " + seed + ": at release the passer faces within 40 degrees of his target (was " + ang + " rad)");
                    }
                }
            }
        }

        [Test]
        public void RollOutKeepsMovingAndAThrowOffItIsNotPlanted()
        {
            var boot = SimTestKit.Playbook.AllPlays.First(p => p.Side == PlaySide.Offense && p.Assignments[0] is QbAssignment && (((QbAssignment)p.Assignments[0]).Action == QbAction.Rollout || ((QbAssignment)p.Assignments[0]).Action == QbAction.Boot));
            var sim = SimTestKit.Run(boot, SimTestKit.Defense("Cover 3 Sky", "4-3 Base"), 75, 75, 6, RuleSet.Pro(), SimTestKit.Scrimmage(RuleSet.Pro()), Quiet());
            float x0 = QbOf(sim).Pos.X, maxExt = 0f; int guard = 0;
            sim.Tick(PlaySimulation.Dt, new ControlInput { Snap = true }, -1);
            while (sim.Phase != PlayPhase.Dead && guard++ < 300 && sim.Ball.State != BallState.Pass)
            {
                sim.Tick(PlaySimulation.Dt, default(ControlInput), -1);
                maxExt = Math.Max(maxExt, Math.Abs(QbOf(sim).Pos.X - x0));
            }
            Assert.Greater(maxExt, 6.0f, "the boot runs out past six yards before he throws or stops");
        }

        [Test]
        public void CleanPocketHasLiveFeet()
        {
            var sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 90, 40, 5, RuleSet.Pro(), null, Quiet());
            sim.Tick(PlaySimulation.Dt, new ControlInput { Snap = true }, -1);
            float minX = 99f, maxX = -99f; int guard = 0, setFrames = 0;
            while (sim.Phase != PlayPhase.Dead && guard++ < 400 && QbOf(sim).QbState != QbPhase.Windup && sim.Ball.State != BallState.Pass)
            {
                sim.Tick(PlaySimulation.Dt, default(ControlInput), -1);
                if (QbOf(sim).QbState == QbPhase.Set && QbOf(sim).Speed < 6f) { setFrames++; minX = Math.Min(minX, QbOf(sim).Pos.X); maxX = Math.Max(maxX, QbOf(sim).Pos.X); }
            }
            if (setFrames > 40) Assert.Greater(maxX - minX, 0.05f, "in the set position the passer still slides a little (live feet)");
        }
    }
}
