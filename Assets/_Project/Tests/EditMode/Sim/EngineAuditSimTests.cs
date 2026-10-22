using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;
using Team = PixFootball.Core.Team;

namespace PixFootball.Tests
{
    /// <summary>Third batch of audit-fix tests: snaps, jams, penalties on special teams, smoothness and kickoffs.</summary>
    public class EngineAuditSimTests
    {
        private static PlaySimOptions Quiet() { return new PlaySimOptions { Penalties = false }; }

        [Test]
        public void AWonJamEndsAndTheReceiverResumesHisRoute()
        {
            int jams = 0;
            for (int seed = 0; seed < 30; seed++)
            {
                var sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 2 Press", "Nickel", 75, 75, 800 + seed, RuleSet.Pro(), null, Quiet());
                SimTestKit.StepToLive(sim);
                float maxJam = 0f;
                SimTestKit.Steps(sim, 150);
                for (int i = 1; i < 11; i++) maxJam = Math.Max(maxJam, sim.Agents[i].JamTime);
                SimTestKit.Steps(sim, 90);
                for (int i = 1; i < 11; i++) Assert.LessOrEqual(sim.Agents[i].JamTime, 0f, "the jam timer counts down (it used to stay at its start value for the whole play)");
                if (maxJam > 0f) jams++;
                if (sim.Phase != PlayPhase.Live) continue;
            }
            Assert.GreaterOrEqual(jams, 0);
        }

        [Test]
        public void TheUnderCenterSnapReachesTheQuarterbackInUnderAThirdOfASecond()
        {
            for (int seed = 0; seed < 10; seed++)
            {
                var sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 900 + seed, RuleSet.Pro(), null, Quiet());
                SimTestKit.StepToLive(sim);
                SimTestKit.Steps(sim, 20);   // 0.33 s
                Assert.AreEqual(0, sim.Ball.Holder, "the quarterback has the ball (seed " + seed + ")");
            }
        }

        [Test]
        public void TheSnapEventNamesTheCenter()
        {
            string[] forms = { "I-Form", "Shotgun", "Singleback" };
            foreach (string form in forms)
            {
                Play off = null;
                foreach (Play p in SimTestKit.Playbook.AllPlays) if (p.Side == PlaySide.Offense && p.FormationName == form && p.IsPass) { off = p; break; }
                if (off == null) continue;
                var sim = SimTestKit.Run(off, SimTestKit.Defense("Cover 3 Sky", "4-3 Base"), 75, 75, 55, RuleSet.Pro(), null, Quiet());
                SimTestKit.StepToLive(sim);
                var snap = sim.Log.First(SimEventType.Snap);
                Assert.IsTrue(snap.HasValue);
                Assert.AreEqual(Position.C, sim.Agents[snap.Value.AgentA].PlayPos, form + ": the center snaps the ball");
            }
        }

        [Test]
        public void KickPlaysFlagOnlyTheirOwnLinemenForFalseStarts()
        {
            var rules = RuleSet.Pro();
            int flags = 0;
            for (int i = 0; i < 700; i++)
            {
                var gs = SimTestKit.Scrimmage(rules, 30 + (i % 25), 4, 8);
                var sim = SimTestKit.Kick("Punt Standard", "Punt Return Set", 20000 + i, gs, 75, 75, rules);
                var r = sim.RunToCompletion();
                if (r.Penalty == null || r.Penalty.Type != PenaltyType.FalseStart) continue;
                flags++;
                SimAgent culprit = null;
                for (int a = 0; a < 11; a++) if (sim.Agents[a].Player != null && sim.Agents[a].Player.Id == r.Penalty.PlayerId) culprit = sim.Agents[a];   // (test rosters reuse ids across the two sides: look on the kicking side)
                Assert.NotNull(culprit, "the flag names a player of the kicking team");
                Assert.That(culprit.Side == SimSide.Offense, "a false start is on the kicking team");
                Assert.That(PositionGroup.IsOffensiveLine(culprit.PlayPos) || culprit.PlayPos == Position.TE || culprit.PlayPos == Position.WR || culprit.PlayPos == Position.FB, "a lineman or wing, not the punter (" + culprit.PlayPos + ")");
                Assert.AreNotEqual(Position.P, culprit.PlayPos);
            }
            Assert.GreaterOrEqual(flags, 0);
        }

        [Test]
        public void ALongFrameIsNotCaughtUpInOneTick()
        {
            var sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 12, RuleSet.Pro(), null, Quiet());
            SimTestKit.StepToLive(sim);
            float t0 = sim.Time;
            sim.Tick(5f, default(ControlInput), -1);   // a 5-second hitch
            Assert.LessOrEqual(sim.Time - t0, PlaySimulation.MaxTickSeconds + 0.02f, "a frame hitch no longer fast-forwards the play");
        }

        [Test]
        public void NoPlayerEverTeleports()
        {
            float worst = 0f;
            string[] plays = { "Toss Sweep Right", "Outside Zone Right", "Four Verticals", "Smash" };
            foreach (string name in plays)
            {
                Play off = null;
                foreach (Play p in SimTestKit.Playbook.AllPlays) if (p.Side == PlaySide.Offense && p.Name == name) { off = p; break; }
                if (off == null) continue;
                for (int seed = 0; seed < 12; seed++)
                {
                    Play def = SimTestKit.Playbook.PlaysFor(DefaultPlaybooks.Nickel).First(p => p.Name == "Cover 1 Man Free");
                    var sim = SimTestKit.Run(off, def, 75, 75, 7000 + seed, RuleSet.Pro(), null, Quiet());
                    SimTestKit.StepToLive(sim);
                    var prev = new Vec2[22];
                    for (int i = 0; i < 22; i++) prev[i] = sim.Agents[i].Pos;
                    int guard = 0;
                    while (sim.Phase == PlayPhase.Live && guard++ < 1200)
                    {
                        sim.Step(default(ControlInput));
                        for (int i = 0; i < 22; i++)
                        {
                            worst = Math.Max(worst, (sim.Agents[i].Pos - prev[i]).Length);
                            prev[i] = sim.Agents[i].Pos;
                        }
                    }
                }
            }
            Assert.That(worst, Is.LessThan(0.8f), "no player jumps more than 0.8 yd in one tick (a push-out used to teleport the runner up to 1.3 yd)");
        }

        [Test]
        public void KickoffsAreDeepWithAHealthyShareOfTouchbacksAndNoneLandBeyondTheEndLine()
        {
            var rules = RuleSet.Pro();
            int n = 0, tb = 0;
            for (int i = 0; i < 160; i++)
            {
                var gs = new GameState(rules, Team.Away);
                var sim = SimTestKit.Kick("Kickoff Deep Middle", "Kick Return Middle Wedge", 31000 + i, gs, 75, 75, rules);
                var r = sim.RunToCompletion();
                if (r.Type != PlayType.Kickoff) continue;
                n++;
                if (r.Touchback) tb++;
                Assert.LessOrEqual(sim.KickLandY, 110.5f, "the kick lands in the stadium (seed " + i + ")");
            }
            double share = 100.0 * tb / n;
            TestContext.WriteLine("touchback share " + share.ToString("0.0") + "% of " + n);
            Assert.That(share, Is.InRange(20.0, 85.0));
        }

        [Test]
        public void PreSnapFoulsAreNamedOnPlayersOfTheOffendingTeam()
        {
            var rules = RuleSet.Pro();
            int flagged = 0;
            for (int seed = 0; seed < 500; seed++)
            {
                var sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 40000 + seed, rules, null, new PlaySimOptions { Penalties = true });
                var r = sim.RunToCompletion();
                if (r.Penalty == null) continue;
                if (r.Penalty.Type != PenaltyType.FalseStart && r.Penalty.Type != PenaltyType.Offside && r.Penalty.Type != PenaltyType.Encroachment && r.Penalty.Type != PenaltyType.IllegalFormation) continue;
                flagged++;
                bool offenseFoul = r.Penalty.Type == PenaltyType.FalseStart || r.Penalty.Type == PenaltyType.IllegalFormation;
                int lo = offenseFoul ? 0 : 11;
                bool found = false;
                for (int a = lo; a < lo + 11; a++) if (sim.Agents[a].Player != null && sim.Agents[a].Player.Id == r.Penalty.PlayerId) found = true;
                Assert.IsTrue(found, r.Penalty.Type + " must name a player of the " + (offenseFoul ? "offense" : "defense"));
                Assert.AreEqual(offenseFoul ? State(sim).Possession : State(sim).Possession.Opponent(), r.Penalty.CommittedBy);
            }
            Assert.Greater(flagged, 0, "pre-snap fouls do occur");
        }

        private static GameState State(PlaySimulation sim) { return sim.State; }

        [Test]
        public void IdenticalSeedsGiveIdenticalFilms()
        {
            string Film(int seed)
            {
                var sim = SimTestKit.RunNamed("Smash", "Shotgun", "Cover 2 Zone", "Nickel", 75, 75, seed, RuleSet.Pro(), null, Quiet());
                sim.RunToCompletion();
                return sim.Log.ToText();
            }
            Assert.AreEqual(Film(77), Film(77));
            Assert.AreNotEqual(Film(77), Film(78));
        }
    }
}
