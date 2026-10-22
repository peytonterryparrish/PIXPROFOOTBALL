using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>Tests for the fixes made while working through Docs/QA/audit.md (SIM rows in Docs/QA/engine.md).</summary>
    public class EngineAuditTests
    {
        private static PlaySimOptions Quiet() { return new PlaySimOptions { Penalties = false }; }

        // ------------------------------------------------------------------ stances (QA-306)

        [Test]
        public void EveryPositionGetsItsOwnPreSnapStance()
        {
            var sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 5, RuleSet.Pro(), null, Quiet());
            Assert.AreEqual(PlayPhase.PreSnap, sim.Phase);
            var seen = new HashSet<PreSnapStance>();
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = sim.Agents[i];
                Assert.AreEqual(AgentAction.Stance, a.Action, "the action stays Stance while the ball is dead");
                Assert.AreNotEqual(PreSnapStance.None, a.StanceKind, a.Label + " has a stance");
                seen.Add(a.StanceKind);
                if (a.PlayPos == Position.C) Assert.AreEqual(PreSnapStance.Snapper, a.StanceKind);
                else if (PositionGroup.IsOffensiveLine(a.PlayPos)) Assert.AreEqual(PreSnapStance.ThreePoint, a.StanceKind);
                else if (PositionGroup.IsLinebacker(a.PlayPos)) Assert.AreEqual(PreSnapStance.Linebacker, a.StanceKind);
                else if (a.PlayPos == Position.QB) Assert.AreEqual(PreSnapStance.QbUnderCenter, a.StanceKind);
                else if (PositionGroup.IsSafety(a.PlayPos)) Assert.AreEqual(PreSnapStance.Safety, a.StanceKind);
            }
            Assert.GreaterOrEqual(seen.Count, 6, "at least six distinct stances in one formation");
            var sg = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 5, RuleSet.Pro(), null, Quiet());
            Assert.AreEqual(PreSnapStance.QbShotgun, sg.Agents[0].StanceKind, "the shotgun quarterback");
        }

        [Test]
        public void KickFormationsHaveKickerHolderAndSnapperInPlace()
        {
            var rules = RuleSet.Pro();
            foreach (string kick in new[] { "Field Goal", "Extra Point" })
            {
                GameState gs = kick == "Extra Point" ? SimTestKit.PointAfter(rules) : SimTestKit.Scrimmage(rules, 70, 4, 8);
                PlaySimulation sim = SimTestKit.Run(SimTestKit.Special(kick), SimTestKit.Special("Field Goal Block Middle"), 75, 75, 3, rules, gs, Quiet());
                SimAgent k = sim.Specials.KickerAgent, h = sim.Specials.HolderAgent, sn = sim.Specials.SnapperAgent;
                Assert.NotNull(k); Assert.NotNull(h); Assert.NotNull(sn);
                Assert.That(Math.Abs(sn.Pos.Y - sim.LosY), Is.LessThan(0.9f), kick + ": the snapper is on the line");
                Assert.That(sim.LosY - h.Pos.Y, Is.InRange(5.5f, 8f), kick + ": the holder kneels about seven yards back");
                Assert.That(sim.LosY - k.Pos.Y, Is.InRange(6.5f, 10f), kick + ": the kicker stands behind the holder");
                Assert.That(Math.Abs(k.Pos.X - h.Pos.X), Is.LessThan(2.6f), kick + ": kicker and holder are nearly in a line");
                Assert.AreEqual(PreSnapStance.HolderKneel, h.StanceKind);
                Assert.AreEqual(PreSnapStance.Snapper, sn.StanceKind);
                Assert.AreEqual(PreSnapStance.Kicker, k.StanceKind);
                int onLine = 0;
                for (int i = 0; i < 11; i++) if (Math.Abs(sim.Agents[i].Pos.Y - sim.LosY) < 1.2f) onLine++;
                Assert.GreaterOrEqual(onLine, 7, kick + ": seven on the line");
                foreach (var a in sim.Agents.Take(11)) Assert.That(a.Facing.Y, Is.GreaterThan(0.5f), kick + ": " + a.Label + " faces the ball");
            }
        }

        [Test]
        public void KickoffTeamLinesUpAcrossTheFieldWithTheKickerBehind()
        {
            var rules = RuleSet.Pro();
            PlaySimulation sim = SimTestKit.Run(SimTestKit.Special("Kickoff Deep Middle"), SimTestKit.Special("Kick Return Middle Wedge"), 75, 75, 4, rules, new GameState(rules, PixFootball.Core.Team.Away), Quiet());
            SimAgent k = sim.Specials.KickerAgent;
            Assert.NotNull(k);
            float minX = 99f, maxX = -99f;
            for (int i = 1; i < 11; i++) { SimAgent a = sim.Agents[i]; minX = Math.Min(minX, a.Pos.X); maxX = Math.Max(maxX, a.Pos.X); Assert.That(Math.Abs(a.Pos.Y - (sim.LosY - KickTiming.CoverageStartBack)), Is.LessThan(1.6f), "a coverage man sits on the kicking line (v0.7: a few steps back, so the line can run level with the ball at contact)"); }
            Assert.That(maxX - minX, Is.GreaterThan(35f), "the coverage unit spreads across the field, not a single file");
            Assert.That(sim.LosY - k.Pos.Y, Is.InRange(4.5f, 8f), "the kicker runs up from behind the line (5-7 steps)");
        }
    }
}
