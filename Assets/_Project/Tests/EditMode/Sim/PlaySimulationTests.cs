using System;
using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>The orchestrator: setup, determinism, phases, event log, user control, results into GameState, distributions and full games.</summary>
    public class PlaySimulationTests
    {
        private static bool Finite(float f) { return !float.IsNaN(f) && !float.IsInfinity(f); }

        private static void AssertSane(PlaySimulation sim, string where)
        {
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = sim.Agents[i];
                Assert.IsTrue(Finite(a.Pos.X) && Finite(a.Pos.Y) && Finite(a.Vel.X) && Finite(a.Vel.Y), where + ": agent " + a + " has a non-finite state");
                Assert.IsTrue(Finite(a.Facing.X) && Finite(a.Facing.Y), where + ": agent facing");
                Assert.Less(Math.Abs(a.Pos.X), 400f, where + ": agent position runs away");
                Assert.Less(Math.Abs(a.Pos.Y), 400f, where + ": agent position runs away");
            }
            Vec3 b = sim.Ball.Pos;
            Assert.IsTrue(Finite(b.X) && Finite(b.Y) && Finite(b.Z), where + ": ball position");
            Assert.Less(Math.Abs(b.Y), 500f, where + ": ball position");
        }

        // ---------------------------------------------------------------- determinism

        [Test]
        public void SameSeedGivesTheSamePlayEventForEvent()
        {
            string[] offs = { "Inside Zone Right", "Four Verticals", "Mesh", "PA Boot" };
            foreach (string o in offs)
            {
                string form = o == "Inside Zone Right" || o == "PA Boot" ? "I-Form" : "Shotgun";
                PlaySimulation a = SimTestKit.RunNamed(o, form, "Cover 3 Sky", "4-3 Base", 75, 75, 4242);
                PlaySimulation b = SimTestKit.RunNamed(o, form, "Cover 3 Sky", "4-3 Base", 75, 75, 4242);
                PlayResult ra = a.RunToCompletion(), rb = b.RunToCompletion();
                Assert.AreEqual(a.Log.Digest(), b.Log.Digest(), o + ": event digest");
                Assert.AreEqual(a.Log.Count, b.Log.Count, o);
                Assert.AreEqual(ra.Type, rb.Type, o);
                Assert.AreEqual(ra.Yards, rb.Yards, o);
                Assert.AreEqual(a.Time, b.Time, o);
                for (int i = 0; i < 22; i++)
                {
                    Assert.AreEqual(a.Agents[i].Pos.X, b.Agents[i].Pos.X, o + " agent " + i);
                    Assert.AreEqual(a.Agents[i].Pos.Y, b.Agents[i].Pos.Y, o + " agent " + i);
                }
            }
        }

        [Test]
        public void DifferentSeedsGiveDifferentPlays()
        {
            var digests = new HashSet<ulong>();
            for (int s = 0; s < 12; s++)
                digests.Add(SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 100 + s).RunToCompletionLog());
            Assert.GreaterOrEqual(digests.Count, 10, "seeds drive the outcomes");
        }

        [Test]
        public void StepCountDoesNotChangeTheOutcome()
        {
            // Tick(dt) accumulates into fixed steps: feeding 1/60, 1/30 or 1/20 slices of time is the same simulation
            ulong reference = 0;
            foreach (float slice in new[] { 1f / 60f, 1f / 30f, 1f / 20f })
            {
                PlaySimulation sim = SimTestKit.RunNamed("Mesh", "Shotgun", "Cover 1 Man Free", "4-3 Base", 75, 75, 99);
                int guard = 0;
                while (sim.Phase != PlayPhase.Dead && guard++ < 5000) sim.Tick(slice, default(ControlInput), -1);
                Assert.AreEqual(PlayPhase.Dead, sim.Phase);
                if (reference == 0) reference = sim.Log.Digest(); else Assert.AreEqual(reference, sim.Log.Digest(), "slice " + slice);
            }
        }

        [Test]
        public void UserInputRunsAreDeterministicToo()
        {
            ulong first = 0;
            for (int rep = 0; rep < 3; rep++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 2024, null, null, new PlaySimOptions { AutoSnap = false });
                SimAgent hb = null;
                for (int i = 1; i < 11 && hb == null; i++) if (sim.Agents[i].Assign is CarryAssignment) hb = sim.Agents[i];
                int guard = 0;
                sim.Tick(0.25f, default(ControlInput), hb.Id);
                sim.Tick(1f / 60f, new ControlInput { Snap = true }, hb.Id);
                while (sim.Phase != PlayPhase.Dead && guard++ < 3000)
                {
                    var inp = new ControlInput { Move = new Vec2(0.2f, 1f).Norm(), Sprint = true };
                    if (guard == 120) inp.JukeLeft = true;
                    if (guard == 200) inp.Spin = true;
                    sim.Tick(1f / 60f, inp, hb.Id);
                }
                if (rep == 0) first = sim.Log.Digest(); else Assert.AreEqual(first, sim.Log.Digest(), "rep " + rep);
            }
        }

        // ---------------------------------------------------------------- setup and placement

        [Test]
        public void AlignmentsFollowTheBallSpotAndHash([Values(Hash.Left, Hash.Middle, Hash.Right)] Hash hash, [Values(false, true)] bool college)
        {
            RuleSet rules = college ? RuleSet.College() : RuleSet.Pro();
            foreach (int spot in new[] { 3, 35, 62, 97 })
            {
                var gs = SimTestKit.Scrimmage(rules, spot, 2, 7);
                PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 5, rules, gs, new PlaySimOptions { Hash = hash });
                Assert.AreEqual((float)spot, sim.LosY, 0.001f);
                Assert.AreEqual(FieldGeometry.HashX(hash, rules), sim.BallX0, 0.001f);
                Assert.AreEqual((float)Math.Min(100, spot + 7), sim.LineToGainY, 0.001f);
                for (int i = 0; i < 11; i++) Assert.LessOrEqual(sim.Agents[i].Pos.Y, sim.LosY + 0.05f, "offense is behind the ball: " + sim.Agents[i]);
                for (int i = 11; i < 22; i++) Assert.GreaterOrEqual(sim.Agents[i].Pos.Y, sim.LosY - 0.05f, "defense is on its side: " + sim.Agents[i]);
                for (int i = 0; i < 22; i++)
                {
                    Assert.GreaterOrEqual(sim.Agents[i].Pos.X, 0f);
                    Assert.LessOrEqual(sim.Agents[i].Pos.X, FieldGeometry.Width);
                    for (int j = i + 1; j < 22; j++)
                        Assert.Greater((sim.Agents[i].Pos - sim.Agents[j].Pos).Length, 0.25f, "no overlapping players " + sim.Agents[i] + " " + sim.Agents[j]);
                }
                // the center sits over the ball
                SimAgent center = null;
                for (int i = 0; i < 11; i++) if (sim.Agents[i].PlayPos == Position.C) center = sim.Agents[i];
                Assert.IsNotNull(center);
                Assert.AreEqual(sim.BallX0, center.Pos.X, 0.7f);
            }
        }

        [Test]
        public void CollegeAndProHashMarksDiffer()
        {
            float pro = FieldGeometry.HashX(Hash.Left, RuleSet.Pro());
            float col = FieldGeometry.HashX(Hash.Left, RuleSet.College());
            Assert.Greater(Math.Abs(pro - col), 0.1f);
            var sp = SimTestKit.RunNamed("Mesh", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 1, RuleSet.Pro(), null, new PlaySimOptions { Hash = Hash.Left });
            var sc = SimTestKit.RunNamed("Mesh", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 1, RuleSet.College(), SimTestKit.Scrimmage(RuleSet.College()), new PlaySimOptions { Hash = Hash.Left });
            Assert.Greater(Math.Abs(sp.BallX0 - sc.BallX0), 0.1f);
        }

        [Test]
        public void OffenseAndDefenseAreTheRosterPlayersBySlot()
        {
            var rng = new Rng(7);
            Play off = SimTestKit.Offense("Four Verticals", "Shotgun"), def = SimTestKit.Defense("Cover 3 Sky", "4-3 Base");
            Player[] ro = SimTestKit.Roster(SimTestKit.Playbook.FindFormation(off.FormationName), 70, rng);
            Player[] rd = SimTestKit.Roster(SimTestKit.Playbook.FindFormation(def.FormationName), 70, rng);
            var sim = new PlaySimulation();
            sim.Setup(SimTestKit.Scrimmage(RuleSet.Pro()), off, def, ro, rd, RuleSet.Pro(), rng.Fork(), null);
            for (int i = 0; i < 11; i++)
            {
                Assert.AreSame(ro[i], sim.Agents[i].Player);
                Assert.AreSame(rd[i], sim.Agents[11 + i].Player);
                Assert.AreEqual(SimSide.Offense, sim.Agents[i].Side);
                Assert.AreEqual(SimSide.Defense, sim.Agents[11 + i].Side);
                Assert.AreEqual(i, sim.Agents[i].Id);
                Assert.AreEqual(11 + i, sim.Agents[11 + i].Id);
            }
            Assert.IsTrue(Finite(sim.Ball.Pos.X) && Finite(sim.Ball.Pos.Y), "ball is placed");
        }

        // ---------------------------------------------------------------- phases and the event log

        [Test]
        public void PhaseMachineGoesPreSnapLiveDeadAndStaysDead()
        {
            PlaySimulation sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 12, null, null, new PlaySimOptions { AutoSnap = false });
            Assert.AreEqual(PlayPhase.PreSnap, sim.Phase);
            Assert.IsNull(sim.Result);
            Assert.IsFalse(sim.IsOver);
            for (int i = 0; i < 120; i++) sim.Step(default(ControlInput));
            Assert.AreEqual(PlayPhase.PreSnap, sim.Phase, "no snap without the input");
            Assert.AreEqual(0f, sim.Time, 0.0001f, "the live clock has not started");
            sim.Step(new ControlInput { Snap = true });
            Assert.AreEqual(PlayPhase.Live, sim.Phase);
            Assert.IsTrue(sim.Log.Has(SimEventType.Snap));
            float last = 0f; int guard = 0;
            while (sim.Phase == PlayPhase.Live && guard++ < 5000)
            {
                sim.Step(default(ControlInput));
                Assert.GreaterOrEqual(sim.Time, last);
                last = sim.Time;
            }
            Assert.AreEqual(PlayPhase.Dead, sim.Phase);
            Assert.IsTrue(sim.IsOver);
            Assert.IsNotNull(sim.Result);
            PlayResult r = sim.Result;
            int events = sim.Log.Count;
            for (int i = 0; i < 90; i++) sim.Step(default(ControlInput));
            Assert.AreEqual(PlayPhase.Dead, sim.Phase);
            Assert.AreSame(r, sim.Result, "the result is final");
            Assert.AreEqual(events, sim.Log.Count, "nothing is logged after the whistle");
            AssertSane(sim, "after the whistle");
        }

        [Test]
        public void TheAutoSnapStartsThePlayWithoutInput()
        {
            PlaySimulation sim = SimTestKit.RunNamed("Mesh", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 3);
            int guard = 0;
            while (sim.Phase == PlayPhase.PreSnap && guard++ < 600) sim.Step(default(ControlInput));
            Assert.AreEqual(PlayPhase.Live, sim.Phase);
            Assert.GreaterOrEqual(guard * PlaySimulation.Dt, sim.Options.AutoSnapDelay - 0.05f);
        }

        [Test]
        public void EventLogIsOrderedAndBracketedBySnapAndPlayEnd()
        {
            for (int s = 0; s < 60; s++)
            {
                string[] offs = { "Inside Zone Right", "Four Verticals", "Mesh", "PA Boot" };
                string o = offs[s % 4];
                PlaySimulation sim = SimTestKit.RunNamed(o, o == "Inside Zone Right" || o == "PA Boot" ? "I-Form" : "Shotgun", "Cover 2 Zone", "4-3 Base", 75, 75, 700 + s, null, null, new PlaySimOptions { Penalties = false }); // a dead-ball flag before the snap is a legitimate two-event log, so switch the flags off here
                sim.RunToCompletion();
                var ev = sim.Log.Events;
                Assert.Greater(ev.Count, 2);
                int ends = 0;
                for (int i = 0; i < ev.Count; i++)
                {
                    if (i > 0) Assert.GreaterOrEqual(ev[i].Time, ev[i - 1].Time - 1e-5f, "monotonic times, play " + s + " event " + i + "\n" + sim.Log.ToText());
                    if (ev[i].Type == SimEventType.PlayEnd) ends++;
                    Assert.IsTrue(Finite(ev[i].Pos.X) && Finite(ev[i].Pos.Y) && Finite(ev[i].Value));
                    Assert.GreaterOrEqual(ev[i].AgentA, -1); Assert.Less(ev[i].AgentA, 22);
                    Assert.GreaterOrEqual(ev[i].AgentB, -1); Assert.Less(ev[i].AgentB, 22);
                }
                Assert.AreEqual(1, ends, "exactly one PlayEnd");
                Assert.AreEqual(SimEventType.PlayEnd, ev[ev.Count - 1].Type, "the whistle is the last thing in the log");
                Assert.AreEqual(SimEventType.Snap, sim.Log.First(SimEventType.Snap).Value.Type);
                // the snap is the first thing that happens
                Assert.AreEqual(SimEventType.Snap, ev[0].Type);
            }
        }

        [Test]
        public void ActionsCoverTheAnimationVocabulary()
        {
            var seen = new HashSet<AgentAction>();
            string[] offs = { "Inside Zone Right", "Four Verticals", "Mesh", "PA Boot", "Toss Sweep", "Draw", "Counter Right" };
            for (int s = 0; s < 140; s++)
            {
                string o = offs[s % offs.Length];
                Play off = null;
                foreach (Play p in SimTestKit.Playbook.AllPlays) if (p.Side == PlaySide.Offense && p.Name == o) { off = p; break; }
                if (off == null) continue;
                Play def = SimTestKit.Defense(s % 3 == 0 ? "Cover 1 Man Free" : (s % 3 == 1 ? "Cover 3 Sky" : "Cover 0 Blitz"), "4-3 Base");
                PlaySimulation sim = SimTestKit.Run(off, def, 75, 75, 5000 + s);
                int guard = 0, after = 0;
                // the post-whistle wind-down counts too (tackled players, celebrations)
                while ((sim.Phase != PlayPhase.Dead || after++ < 90) && guard++ < 3200)
                {
                    sim.Step(default(ControlInput));
                    for (int i = 0; i < 22; i++) seen.Add(sim.Agents[i].Action);
                }
            }
            TestContext.WriteLine("actions seen: " + string.Join(", ", seen));
            AgentAction[] required =
            {
                AgentAction.Stance, AgentAction.Run, AgentAction.Backpedal, AgentAction.Shuffle, AgentAction.Block, AgentAction.Engaged,
                AgentAction.Route, AgentAction.Catch, AgentAction.Throw, AgentAction.Carry, AgentAction.TackleWrapUp, AgentAction.Tackled
            };
            foreach (AgentAction a in required) Assert.IsTrue(seen.Contains(a), "missing action " + a);
            Assert.GreaterOrEqual(seen.Count, 14);
        }

        [Test]
        public void EveryAgentActionIsAValidEnumMember()
        {
            // the presentation layer maps each action to an animation state, so nothing may fall outside the enum
            for (int s = 0; s < 40; s++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 300 + s);
                int guard = 0;
                while (sim.Phase != PlayPhase.Dead && guard++ < 3000)
                {
                    sim.Step(default(ControlInput));
                    for (int i = 0; i < 22; i++) Assert.IsTrue(Enum.IsDefined(typeof(AgentAction), sim.Agents[i].Action));
                }
            }
        }

        // ---------------------------------------------------------------- user control

        [Test]
        public void TheSnapPulseStartsAManualPlay()
        {
            PlaySimulation sim = SimTestKit.RunNamed("Mesh", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 1, null, null, new PlaySimOptions { AutoSnap = false });
            sim.Tick(2f, default(ControlInput), 0);
            Assert.AreEqual(PlayPhase.PreSnap, sim.Phase);
            sim.Tick(1f / 60f, new ControlInput { Snap = true }, 0);
            Assert.AreEqual(PlayPhase.Live, sim.Phase);
            Assert.AreEqual(0, sim.UserAgent);
        }

        [Test]
        public void TheUserQuarterbackThrowsToTheChosenReceiver()
        {
            int hits = 0, n = 20;
            for (int s = 0; s < n; s++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 800 + s, null, null, new PlaySimOptions { QbHoldsBall = true, Penalties = false });
                SimTestKit.StepToLive(sim);
                while (sim.Time < 1.6f) sim.Tick(1f / 60f, default(ControlInput), 0);
                var elig = new List<int>(); for (int k = 1; k < 11; k++) if (sim.Agents[k].IsReceiverEligible) elig.Add(k); int slot = elig[s % elig.Count];
                sim.Tick(1f / 60f, new ControlInput { ThrowToSlot = slot }, 0);
                for (int i = 0; i < 120 && sim.Phase == PlayPhase.Live; i++) sim.Tick(1f / 60f, default(ControlInput), 0);
                var th = sim.Log.First(SimEventType.Throw);
                if (th.HasValue && th.Value.AgentA == 0 && th.Value.AgentB == slot) hits++;
            }
            Assert.GreaterOrEqual(hits, n - 2, "the pass goes where the user pointed");
        }

        [Test]
        public void TheUserQuarterbackCanThrowTheBallAway()
        {
            PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 66, null, null, new PlaySimOptions { QbHoldsBall = true, Penalties = false });
            SimTestKit.StepToLive(sim);
            while (sim.Time < 1.5f) sim.Tick(1f / 60f, default(ControlInput), 0);
            sim.Tick(1f / 60f, new ControlInput { Throwaway = true }, 0);
            for (int i = 0; i < 240 && sim.Phase == PlayPhase.Live; i++) sim.Tick(1f / 60f, default(ControlInput), 0);
            Assert.IsTrue(sim.Log.Has(SimEventType.Throwaway));
            Assert.AreEqual(PlayPhase.Dead, sim.Phase);
        }

        [Test]
        public void TheUserBallCarrierCanJukeSpinAndStiffArm()
        {
            var moves = new Dictionary<string, AgentAction>
            {
                { "juke", AgentAction.Juke }, { "spin", AgentAction.Spin }, { "stiff", AgentAction.StiffArm }
            };
            foreach (var kv in moves)
            {
                bool saw = false;
                for (int s = 0; s < 6 && !saw; s++)
                {
                    PlaySimulation sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 40 + s, null, null, new PlaySimOptions { Penalties = false });
                    SimTestKit.StepToLive(sim);
                    SimTestKit.ParkAll(sim, 6f);
                    SimAgent c = null;
                    for (int i = 1; i < 11 && c == null; i++) if (sim.Agents[i].Assign is CarryAssignment && !((CarryAssignment)sim.Agents[i].Assign).IsFake) c = sim.Agents[i];
                    sim.Blocks.HandedOff = true;
                    sim.GiveBall(c);
                    SimTestKit.Place(c, 27f, 40f, 0f, 6f);
                    for (int i = 0; i < 20; i++) sim.Tick(1f / 60f, new ControlInput { Move = new Vec2(0f, 1f), Sprint = true }, c.Id);
                    var inp = new ControlInput { Move = new Vec2(0f, 1f), Sprint = true };
                    if (kv.Key == "juke") inp.JukeLeft = true; else if (kv.Key == "spin") inp.Spin = true; else inp.StiffArm = true;
                    for (int i = 0; i < 40 && sim.Phase == PlayPhase.Live; i++)
                    {
                        sim.Tick(1f / 60f, i == 0 ? inp : new ControlInput { Move = new Vec2(0f, 1f), Sprint = true }, c.Id);
                        if (c.Action == kv.Value) saw = true;
                    }
                }
                Assert.IsTrue(saw, "user " + kv.Key + " shows as " + kv.Value);
            }
        }

        [Test]
        public void TheUserDefenderCanDiveAndSwitchPlayers()
        {
            PlaySimulation sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 14, null, null, new PlaySimOptions { Penalties = false });
            SimTestKit.StepToLive(sim);
            SimTestKit.ParkAll(sim, 6f);
            SimAgent c = null;
            for (int i = 1; i < 11 && c == null; i++) if (sim.Agents[i].Assign is CarryAssignment && !((CarryAssignment)sim.Agents[i].Assign).IsFake) c = sim.Agents[i];
            sim.Blocks.HandedOff = true;
            sim.GiveBall(c);
            SimTestKit.Place(c, 27f, 40f, 0f, 6f);
            SimAgent d = sim.Agents[11 + 6];
            SimTestKit.Place(d, 28.5f, 44f);
            bool dove = false;
            for (int i = 0; i < 60 && sim.Phase == PlayPhase.Live; i++)
            {
                var inp = new ControlInput { Move = new Vec2(-0.2f, -1f).Norm(), Sprint = true };
                if (i == 12) inp.Dive = true;
                sim.Tick(1f / 60f, inp, d.Id);
                if (d.Action == AgentAction.Dive || d.Action == AgentAction.TackleDive || sim.Log.Has(SimEventType.CarrierMove)) dove = true;
            }
            Assert.AreEqual(d.Id, sim.UserAgent);
            Assert.IsTrue(dove, "the dive input launches a dive");
            // switching moves control to another defender (never an offensive player)
            PlaySimulation sim2 = SimTestKit.RunNamed("Mesh", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 18);
            SimTestKit.StepToLive(sim2);
            sim2.Tick(1f / 60f, default(ControlInput), 11 + 4);
            Assert.AreEqual(11 + 4, sim2.UserAgent);
            sim2.Tick(1f / 60f, new ControlInput { SwitchPlayer = true }, 11 + 4);
            Assert.AreNotEqual(11 + 4, sim2.UserAgent);
            Assert.GreaterOrEqual(sim2.UserAgent, 11);
            Assert.IsFalse(sim2.Agents[11 + 4].ControlledByUser);
            Assert.IsTrue(sim2.Agents[sim2.UserAgent].ControlledByUser);
        }

        [Test]
        public void ADefenderCanBeSteeredWithTheStick()
        {
            PlaySimulation sim = SimTestKit.RunNamed("Mesh", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 23, null, null, new PlaySimOptions { QbHoldsBall = true, Penalties = false });
            SimTestKit.StepToLive(sim);
            SimAgent d = sim.Agents[11 + 8];
            float x0 = d.Pos.X;
            for (int i = 0; i < 90; i++) sim.Tick(1f / 60f, new ControlInput { Move = new Vec2(1f, 0f), Sprint = true }, d.Id);
            Assert.Greater(d.Pos.X, x0 + 6f, "the user's stick moves his player instead of the AI");
        }

        // ---------------------------------------------------------------- penalties

        [Test]
        public void PreSnapAndInPlayPenaltiesAreRareButPresent()
        {
            int pens = 0, preSnap = 0, n = 2000;
            for (int s = 0; s < n; s++)
            {
                string[] offs = { "Inside Zone Right", "Four Verticals", "Mesh", "PA Boot" };
                string o = offs[s % 4];
                PlaySimulation sim = SimTestKit.RunNamed(o, o == "Inside Zone Right" || o == "PA Boot" ? "I-Form" : "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 20000 + s);
                PlayResult r = sim.RunToCompletion();
                if (r.Penalty != null) pens++;
                if (r.Type == PlayType.Penalty) preSnap++;
            }
            TestContext.WriteLine("penalties " + pens + "/" + n + " (dead-ball " + preSnap + ")");
            Assert.Greater(pens, n / 200, "at least 0.5% of plays draw a flag");
            Assert.Less(pens, n * 11 / 100, "no more than 11% of plays draw a flag");
            Assert.Greater(preSnap, 0, "false starts and the like happen");
        }

        [Test]
        public void PenaltiesCanBeSwitchedOff()
        {
            for (int s = 0; s < 150; s++)
            {
                PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 1 Man Free", "4-3 Base", 75, 75, 30000 + s, null, null, new PlaySimOptions { Penalties = false });
                PlayResult r = sim.RunToCompletion();
                Assert.IsNull(r.Penalty);
                Assert.AreNotEqual(PlayType.Penalty, r.Type);
            }
        }

        // ---------------------------------------------------------------- results into GameState

        private static void AssertAppliesCleanly(PlaySimulation sim, PlayResult r, GameState before, string label)
        {
            Assert.IsNotNull(r, label);
            Assert.IsTrue(Finite(r.TimeElapsed) && r.TimeElapsed >= 0f && r.TimeElapsed <= 45f, label + ": time " + r.TimeElapsed);
            Assert.GreaterOrEqual(r.Yards, -40, label);
            Assert.LessOrEqual(r.Yards, 110, label);
            GameState g = before.Clone();
            bool applied = false;
            Assert.DoesNotThrow(() => applied = g.Apply(r), label + " (" + r.Type + ")");
            Assert.IsTrue(applied || g.IsFinal, label);
            Assert.IsTrue(g.BallSpot >= 0 && g.BallSpot <= 100, label + ": spot " + g.BallSpot);
            Assert.IsTrue(g.Down >= 1 && g.Down <= 4, label + ": down " + g.Down);
        }

        [Test]
        public void EveryScrimmagePlayYieldsAResultTheGameStateAccepts()
        {
            var rules = RuleSet.Pro();
            string[] offs = { "Inside Zone Right", "Four Verticals", "Mesh", "PA Boot", "Toss Sweep", "Counter Right", "Draw", "Curl-Flat" };
            string[] defs = { "Cover 1 Man Free", "Cover 2 Zone", "Cover 3 Sky", "Cover 4 Quarters", "Cover 0 Blitz" };
            int plays = 0;
            for (int s = 0; s < 400; s++)
            {
                Play off = null;
                string name = offs[s % offs.Length];
                var cands = new List<Play>();
                foreach (Play p in SimTestKit.Playbook.AllPlays) if (p.Side == PlaySide.Offense && p.Name == name) cands.Add(p);
                if (cands.Count == 0) continue;
                off = cands[(s / offs.Length) % cands.Count];
                Play def = SimTestKit.Defense(defs[(s / 8) % defs.Length], "4-3 Base");
                int spot = new[] { 1, 12, 35, 50, 74, 92, 99 }[s % 7];
                var gs = SimTestKit.Scrimmage(rules, spot, 1 + s % 4, 1 + (s * 7) % 12);
                gs.ClockSeconds = 600f; gs.Quarter = 2;
                PlaySimulation sim = SimTestKit.Run(off, def, 75, 75, 40000 + s, rules, gs);
                PlayResult r = sim.RunToCompletion();
                Assert.LessOrEqual(sim.Time, 30f, "no play runs past 30 s of sim time: " + off.Name);
                AssertSane(sim, off.Name);
                AssertAppliesCleanly(sim, r, gs, off.Name + " vs " + def.Name + " @" + spot);
                plays++;
            }
            Assert.GreaterOrEqual(plays, 250);
        }

        [Test]
        public void KneelsAndSpikesApplyAsTheirOwnPlayTypes()
        {
            var rules = RuleSet.Pro();
            var gs = SimTestKit.Scrimmage(rules, 40, 1, 10);
            gs.Quarter = 4; gs.ClockSeconds = 90f;
            PlaySimulation kneel = SimTestKit.Run(SimTestKit.Offense("Kneel"), SimTestKit.Defense("Cover 3 Sky", "4-3 Base"), 75, 75, 5, rules, gs);
            PlayResult kr = kneel.RunToCompletion();
            Assert.AreEqual(PlayType.Kneel, kr.Type);
            Assert.LessOrEqual(kr.Yards, 0);
            AssertAppliesCleanly(kneel, kr, gs, "kneel");
            PlaySimulation spike = SimTestKit.Run(SimTestKit.Offense("Spike"), SimTestKit.Defense("Cover 3 Sky", "4-3 Base"), 75, 75, 6, rules, gs);
            PlayResult sr = spike.RunToCompletion();
            Assert.AreEqual(PlayType.Spike, sr.Type);
            Assert.IsTrue(spike.Log.Has(SimEventType.Spike));
            AssertAppliesCleanly(spike, sr, gs, "spike");
        }

        [Test]
        public void TwoPointTriesPlayFromTheTwoYardLine()
        {
            var rules = RuleSet.Pro();
            int good = 0, n = 120;
            for (int s = 0; s < n; s++)
            {
                var gs = SimTestKit.PointAfter(rules);
                string[] offs = { "Inside Zone Right", "Four Verticals", "Mesh" };
                string o = offs[s % 3];
                PlaySimulation sim = SimTestKit.RunNamed(o, o == "Inside Zone Right" ? "I-Form" : "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 50000 + s, rules, gs);
                PlayResult r = sim.RunToCompletion();
                Assert.IsTrue(sim.IsTwoPoint, "a non-kick play after a touchdown is a two-point try");
                Assert.AreEqual(rules.TwoPointSpot, sim.LosY, 0.001f);
                AssertAppliesCleanly(sim, r, gs, "two-point " + o);
                if (r.Good || r.Touchdown) good++;
            }
            TestContext.WriteLine("two-point conversions: " + good + "/" + n);
            Assert.Greater(good, n / 8);
            Assert.Less(good, n * 3 / 4);
        }

        // ---------------------------------------------------------------- the whole playbook

        [Test]
        public void EveryPlayInThePlaybookRunsToAValidResult()
        {
            var rules = RuleSet.Pro();
            var book = SimTestKit.Playbook;
            Play zone = SimTestKit.Defense("Cover 3 Sky", "4-3 Base");
            int offensive = 0, defensive = 0, special = 0;
            int idx = 0;
            foreach (Play p in book.AllPlays)
            {
                idx++;
                if (p.Side == PlaySide.Offense)
                {
                    Play d = Defense(book, idx);
                    var gs = SimTestKit.Scrimmage(rules, 40, 2, 6);
                    var sim = SimTestKit.Run(p, d, 75, 75, 60000 + idx, rules, gs);
                    PlayResult r = sim.RunToCompletion();
                    Assert.LessOrEqual(sim.Time, 30f, p.Name + "/" + p.FormationName);
                    AssertSane(sim, p.Name + "/" + p.FormationName);
                    AssertAppliesCleanly(sim, r, gs, p.Name + "/" + p.FormationName);
                    offensive++;
                }
                else if (p.Side == PlaySide.Defense)
                {
                    Play o = Offense(book, idx);
                    var gs = SimTestKit.Scrimmage(rules, 45, 2, 7);
                    var sim = SimTestKit.Run(o, p, 75, 75, 61000 + idx, rules, gs);
                    PlayResult r = sim.RunToCompletion();
                    Assert.LessOrEqual(sim.Time, 30f, p.Name + "/" + p.FormationName);
                    AssertSane(sim, p.Name + "/" + p.FormationName);
                    AssertAppliesCleanly(sim, r, gs, "defense " + p.Name + "/" + p.FormationName);
                    defensive++;
                }
                else special++;
            }
            TestContext.WriteLine("ran " + offensive + " offensive and " + defensive + " defensive plays (" + special + " special-teams plays are covered by the kick tests)");
            Assert.Greater(offensive, 50);
            Assert.Greater(defensive, 20);
        }

        private static Play Defense(Playbook book, int i)
        {
            var list = new List<Play>();
            foreach (Play p in book.AllPlays) if (p.Side == PlaySide.Defense && p.FormationName == "4-3 Base") list.Add(p);
            return list[i % list.Count];
        }

        private static Play Offense(Playbook book, int i)
        {
            var list = new List<Play>();
            foreach (Play p in book.AllPlays)
                if (p.Side == PlaySide.Offense && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike) list.Add(p);
            return list[i % list.Count];
        }

        // ---------------------------------------------------------------- distributions

        [Test]
        public void ThousandsOfPlaysGiveSaneFootballNumbers()
        {
            // inside zone, four verticals, mesh and play-action boot against Cover 1 / 2 / 3 / 4 / 0 blitz
            var plays = new[]
            {
                new KeyValuePair<string, string[]>("Inside Zone Right", new[] { "Singleback", "I-Form", "Shotgun" }),
                new KeyValuePair<string, string[]>("Four Verticals", new[] { "Shotgun", "Empty", "Trips" }),
                new KeyValuePair<string, string[]>("Mesh", new[] { "Shotgun", "Empty", "Pistol" }),
                new KeyValuePair<string, string[]>("PA Boot", new[] { "Singleback", "I-Form" })
            };
            string[] defs = { "Cover 1 Man Free", "Cover 2 Zone", "Cover 3 Sky", "Cover 4 Quarters", "Cover 0 Blitz" };
            int runs = 0, runYds = 0, att = 0, comp = 0, sacks = 0, ints = 0, dropbacks = 0, total = 0, nans = 0;
            float maxT = 0f;
            const int n = 2000;
            for (int i = 0; i < n; i++)
            {
                var pl = plays[i % plays.Length];
                string form = pl.Value[(i / plays.Length) % pl.Value.Length];
                string d = defs[(i / (plays.Length * 3)) % defs.Length];
                PlaySimulation sim = SimTestKit.RunNamed(pl.Key, form, d, "4-3 Base", 75, 75, 100000 + i);
                PlayResult r = sim.RunToCompletion();
                total++;
                maxT = Math.Max(maxT, sim.Time);
                if (!Finite(r.TimeElapsed) || !Finite(sim.Agents[0].Pos.X) || !Finite(sim.Ball.Pos.Y)) nans++;
                if (r.Type == PlayType.Penalty) continue;
                if (pl.Key == "Inside Zone Right") { runs++; runYds += r.Yards; continue; }
                dropbacks++;
                switch (r.Type)
                {
                    case PlayType.Pass: att++; comp++; break;
                    case PlayType.Incomplete: att++; break;
                    case PlayType.Interception: att++; ints++; break;
                    case PlayType.Sack: sacks++; break;
                }
            }
            float runAvg = runYds / (float)runs, compPct = 100f * comp / att, sackPct = 100f * sacks / dropbacks, intPct = 100f * ints / att;
            TestContext.WriteLine("plays=" + total + " runAvg=" + runAvg.ToString("0.00") + " comp%=" + compPct.ToString("0.0") + " sack%=" + sackPct.ToString("0.0")
                + " int%=" + intPct.ToString("0.0") + " maxSimTime=" + maxT.ToString("0.0"));
            Assert.AreEqual(0, nans, "no NaNs");
            Assert.LessOrEqual(maxT, 30f, "no play over 30 s of sim time");
            Assert.GreaterOrEqual(runAvg, 2.5f); Assert.LessOrEqual(runAvg, 5.5f);   // inside zone against five coverages including man and a zero blitz
            Assert.GreaterOrEqual(compPct, 55f); Assert.LessOrEqual(compPct, 76f);   // slower, realistic ball speed lets the quarterback hold the ball longer; band covers the mesh / four-verticals / boot mix
            Assert.GreaterOrEqual(sackPct, 4f); Assert.LessOrEqual(sackPct, 9f);
            Assert.GreaterOrEqual(intPct, 1.0f); Assert.LessOrEqual(intPct, 3.5f); // quarterbacks now throw to the open man or away, so the floor is lower than when they forced the ball
        }

        [Test]
        public void SteppedPlaysNeverProduceNonFiniteStateAndAlwaysEnd()
        {
            string[] offs = { "Inside Zone Right", "Four Verticals", "Mesh", "PA Boot" };
            string[] defs = { "Cover 1 Man Free", "Cover 2 Zone", "Cover 3 Sky", "Cover 4 Quarters", "Cover 0 Blitz" };
            for (int s = 0; s < 300; s++)
            {
                string o = offs[s % 4];
                PlaySimulation sim = SimTestKit.RunNamed(o, o == "Inside Zone Right" || o == "PA Boot" ? "I-Form" : "Shotgun", defs[(s / 4) % 5], "4-3 Base", 75, 75, 70000 + s);
                int guard = 0;
                while (sim.Phase != PlayPhase.Dead && guard++ < 3000)
                {
                    sim.Step(default(ControlInput));
                    if (guard % 15 == 0) AssertSane(sim, o + " seed " + s + " t=" + sim.Time);
                }
                Assert.AreEqual(PlayPhase.Dead, sim.Phase, o + " seed " + s + " ended");
                Assert.LessOrEqual(sim.Time, 30f);
                AssertSane(sim, o + " seed " + s + " at the whistle");
            }
        }

        [Test]
        public void StrongerTeamsWinMoreSnaps()
        {
            float Mean(int offOvr, int defOvr)
            {
                float yds = 0f; int n = 0;
                for (int s = 0; s < 400; s++)
                {
                    string[] offs = { "Inside Zone Right", "Four Verticals", "Mesh" };
                    string o = offs[s % 3];
                    var sim = SimTestKit.RunNamed(o, o == "Inside Zone Right" ? "I-Form" : "Shotgun", "Cover 3 Sky", "4-3 Base", offOvr, defOvr, 80000 + s);
                    PlayResult r = sim.RunToCompletion();
                    if (r.Type == PlayType.Penalty) continue;
                    yds += r.Type == PlayType.Interception ? -20 : r.Yards; n++;
                }
                return yds / n;
            }
            float strong = Mean(90, 65), even = Mean(75, 75), weak = Mean(65, 90);
            TestContext.WriteLine("mean yards per play: 90v65=" + strong + " 75v75=" + even + " 65v90=" + weak);
            Assert.Greater(strong, even);
            Assert.Greater(even, weak);
        }

        // ---------------------------------------------------------------- full games

        [Test]
        public void ACompleteGameRunsEndToEnd()
        {
            ProLeague lg = LeagueFactory.CreatePro(7);
            var game = new PhysicalGameSim(lg.Teams[0], lg.Teams[1], RuleSet.Pro(), new Rng(2025));
            int plays = game.Run();
            Assert.IsTrue(game.State.IsFinal, "the game finishes");
            TestContext.WriteLine("plays=" + plays + " final " + game.State.HomeScore + "-" + game.State.AwayScore);
            Assert.Greater(plays, 90);
            Assert.Less(plays, 300);
            Assert.GreaterOrEqual(game.State.HomeScore, 0); Assert.GreaterOrEqual(game.State.AwayScore, 0);
            Assert.Less(game.State.HomeScore, 80); Assert.Less(game.State.AwayScore, 80);
            foreach (PlayRecord rec in game.Plays)
            {
                Assert.IsNotNull(rec.Result);
                Assert.LessOrEqual(rec.SimSeconds, 30f);
            }
        }

        [Test]
        public void FullGamesAreDeterministicAndScoreLikeFootball()
        {
            ProLeague lg = LeagueFactory.CreatePro(9);
            int total = 0, games = 4;
            string first = null;
            for (int g = 0; g < games; g++)
            {
                var game = new PhysicalGameSim(lg.Teams[2 * g], lg.Teams[2 * g + 1], RuleSet.Pro(), new Rng(900 + g));
                game.Run();
                Assert.IsTrue(game.State.IsFinal);
                total += game.State.HomeScore + game.State.AwayScore;
                if (g == 0) first = game.State.HomeScore + "-" + game.State.AwayScore + "/" + game.Plays.Count;
            }
            var again = new PhysicalGameSim(lg.Teams[0], lg.Teams[1], RuleSet.Pro(), new Rng(900));
            again.Run();
            Assert.AreEqual(first, again.State.HomeScore + "-" + again.State.AwayScore + "/" + again.Plays.Count, "same seed, same game");
            float perGame = total / (float)games;
            TestContext.WriteLine("points per game: " + perGame);
            Assert.Greater(perGame, 15f);
            Assert.Less(perGame, 90f);
        }
    }

    internal static class SimExtensions
    {
        public static ulong RunToCompletionLog(this PlaySimulation sim)
        {
            sim.RunToCompletion();
            return sim.Log.Digest();
        }
    }
}
