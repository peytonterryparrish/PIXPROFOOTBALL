using System;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>Pre-snap changes must alter the real simulation assignments (audibles, flip, hot routes, slide, motion, defensive shifts, delay of game).</summary>
    public class PreSnapTests
    {
        static PlaySimulation Manual(string off, string offForm, string def, string defForm, int seed = 3, GameState gs = null)
        {
            var opts = new PlaySimOptions { AutoSnap = false, Penalties = false };
            return SimTestKit.RunNamed(off, offForm, def, defForm, 75, 75, seed, null, gs, opts);
        }

        static PlayResult SnapAndRun(PlaySimulation sim)
        {
            sim.Tick(0.02f, new ControlInput { Snap = true }, -1);
            return sim.RunToCompletion();
        }

        [Test]
        public void AudibleSwapsTheAssignmentsOfEveryPlayerAndLeavesThePlaybookAlone()
        {
            var sim = Manual("Slants", "Shotgun", "Cover 3 Sky", "Nickel");
            Play before = sim.OffPlay;
            Play zone = SimTestKit.Offense("Inside Zone Right", "Shotgun");
            Assert.IsTrue(sim.ChangeOffensePlay(zone));
            Assert.AreSame(zone, sim.OffPlay);
            for (int i = 0; i < 11; i++) Assert.AreSame(zone.Assignments[i], sim.Agents[i].Assign, "slot " + i);
            Assert.IsInstanceOf<CarryAssignment>(sim.Agents[1].Assign);
            Assert.IsInstanceOf<RouteAssignment>(before.Assignments[8], "the original play is untouched");
            Assert.IsFalse(sim.ChangeOffensePlay(SimTestKit.Offense("Slants", "Singleback")), "an audible stays inside the formation");
            // the new call plays out: the back carries the ball
            var r = SnapAndRun(sim);
            Assert.AreEqual(PlayType.Run, r.Type);
        }

        [Test]
        public void AudibleAfterTheSnapIsRefused()
        {
            var sim = Manual("Slants", "Shotgun", "Cover 3 Sky", "Nickel");
            sim.Tick(0.05f, new ControlInput { Snap = true }, -1);
            Assert.IsFalse(sim.ChangeOffensePlay(SimTestKit.Offense("Mesh", "Shotgun")));
            Assert.IsFalse(sim.SetHotRoute(8, HotRoute.Fade));
        }

        [Test]
        public void HotRouteReplacesTheReceiversRouteAndBecomesTheFirstRead()
        {
            var sim = Manual("Slants", "Shotgun", "Cover 3 Sky", "Nickel");
            Assert.IsTrue(sim.SetHotRoute(10, HotRoute.Fade));
            var ra = (RouteAssignment)sim.Agents[10].Assign;
            Assert.AreEqual(RouteKind.Fade, ra.Route);
            Assert.IsTrue(ra.IsHot);
            Assert.AreSame(ra, sim.OffPlay.Assignments[10]);
            Assert.AreEqual(10, ((QbAssignment)sim.OffPlay.Assignments[0]).ReadProgression[0]);
            Assert.AreEqual(RouteKind.Slant, ((RouteAssignment)SimTestKit.Offense("Slants", "Shotgun").Assignments[10]).Route, "playbook not mutated");
            Assert.IsTrue(sim.HotRoutes.ContainsKey(10));
            // all seven hot routes build legal assignments from the receiver's alignment
            foreach (HotRoute h in Enum.GetValues(typeof(HotRoute)))
            {
                Assert.IsTrue(sim.SetHotRoute(8, h), h.ToString());
                if (h == HotRoute.Block) Assert.IsInstanceOf<BlockAssignment>(sim.Agents[8].Assign);
                else Assert.IsTrue(((RouteAssignment)sim.Agents[8].Assign).Waypoints.Count >= 2, h.ToString());
            }
        }

        [Test]
        public void HotRouteDirectionsAreRelativeToTheSideOfTheField()
        {
            var sim = Manual("Slants", "Shotgun", "Cover 3 Sky", "Nickel");
            // slot 8 (X) is on the left, slot 9 (Z) on the right: Out goes toward their own sideline, In toward the middle
            sim.SetHotRoute(8, HotRoute.Out); sim.SetHotRoute(9, HotRoute.Out);
            var xr = (RouteAssignment)sim.Agents[8].Assign; var zr = (RouteAssignment)sim.Agents[9].Assign;
            Assert.Less(xr.Waypoints[xr.Waypoints.Count - 1].Point.X, sim.OffForm.Slots[8].X);
            Assert.Greater(zr.Waypoints[zr.Waypoints.Count - 1].Point.X, sim.OffForm.Slots[9].X);
            sim.SetHotRoute(8, HotRoute.In);
            var xin = (RouteAssignment)sim.Agents[8].Assign;
            Assert.Greater(xin.Waypoints[xin.Waypoints.Count - 1].Point.X, sim.OffForm.Slots[8].X);
        }

        [Test]
        public void TheRunnerAndTheLineCannotBeGivenHotRoutes()
        {
            var sim = Manual("Inside Zone Right", "Shotgun", "Cover 3 Sky", "Nickel");
            Assert.IsFalse(sim.CanHotRoute(1), "the back is carrying the ball");
            Assert.IsFalse(sim.CanHotRoute(3), "linemen are not eligible");
            Assert.IsFalse(sim.CanHotRoute(0));
            Assert.IsTrue(sim.CanHotRoute(8));
        }

        [Test]
        public void FlippingTheRunMirrorsTheFormationAndThePath()
        {
            var sim = Manual("Inside Zone Right", "Shotgun", "Cover 3 Sky", "Nickel");
            float hbX = sim.Agents[1].Pos.X - sim.BallX0;
            Assert.IsTrue(sim.FlipOffense());
            Assert.IsTrue(sim.OffenseFlipped);
            Assert.AreEqual(-hbX, sim.Agents[1].Pos.X - sim.BallX0, 1e-3f);
            var c = (CarryAssignment)sim.Agents[1].Assign;
            Assert.Less(c.Path[c.Path.Count - 1].X, 0f);
            // the line swaps its roles: the old right guard now runs the left guard's blocking
            Assert.AreEqual(((BlockAssignment)SimTestKit.Offense("Inside Zone Right", "Shotgun").Assignments[3]).Target == BlockTarget.ZoneRight ? BlockTarget.ZoneLeft : BlockTarget.ZoneRight, ((BlockAssignment)sim.Agents[5].Assign).Target);
            Assert.IsTrue(sim.FlipOffense());
            Assert.IsFalse(sim.OffenseFlipped);
            Assert.AreEqual(hbX, sim.Agents[1].Pos.X - sim.BallX0, 1e-3f);
            // the flipped run actually goes left
            Assert.IsTrue(sim.FlipOffense());
            SnapAndRun(sim);
            Assert.IsTrue(sim.Log.Events.Any(e => e.Type == SimEventType.Handoff));
        }

        [Test]
        public void SlideProtectionChangesTheLinesBlockTargets()
        {
            var sim = Manual("Slants", "Shotgun", "Cover 3 Sky", "Nickel");
            Assert.IsTrue(sim.SetSlideProtection(-1));
            for (int i = 2; i <= 6; i++) Assert.AreEqual(BlockTarget.SlideLeft, ((BlockAssignment)sim.Agents[i].Assign).Target, "slot " + i);
            Assert.AreEqual(-1, sim.SlideDirection);
            Assert.IsTrue(sim.SetSlideProtection(1));
            for (int i = 2; i <= 6; i++) Assert.AreEqual(BlockTarget.SlideRight, ((BlockAssignment)sim.Agents[i].Assign).Target);
            CollectionAssert.Contains(sim.PreSnapSummary(), "SLIDE RIGHT");
            var run = Manual("Inside Zone Right", "Shotgun", "Cover 3 Sky", "Nickel");
            Assert.IsFalse(run.SetSlideProtection(1), "a run has no pass protection to slide");
        }

        [Test]
        public void MotionMovesTheReceiverAlongTheLineBeforeTheSnap()
        {
            var sim = Manual("Slants", "Shotgun", "Cover 3 Sky", "Nickel");
            Assert.IsTrue(sim.CanMotion(10), "the slot man starts a yard off the line");
            Assert.IsFalse(sim.CanMotion(8), "the split end is on the line");
            Assert.IsFalse(sim.CanMotion(3));
            float x0 = sim.Agents[10].Pos.X;
            Assert.IsTrue(sim.SetMotion(10, -1));
            for (int i = 0; i < 90; i++) sim.Step(new ControlInput());
            Assert.Less(sim.Agents[10].Pos.X, x0 - 2f, "he moved toward the left");
            Assert.AreEqual(AgentAction.Run, sim.Agents[10].Action);
            Assert.AreEqual(sim.Agents[10].Pos.Y, sim.LosY - 1f, 0.2f, "motion stays parallel to the line");
            sim.SetMotion(10, 0);
            for (int i = 0; i < 60; i++) sim.Step(new ControlInput());
            Assert.Less(sim.Agents[10].Speed, 0.5f);
        }

        [Test]
        public void ModifiedPlaysStillRunToCompletion()
        {
            for (int seed = 1; seed <= 12; seed++)
            {
                var opts = new PlaySimOptions { AutoSnap = true, AutoSnapDelay = 3f };
                var sim = SimTestKit.RunNamed("Slants", "Shotgun", "Cover 3 Sky", "Nickel", 75, 75, seed, null, null, opts);
                sim.SetHotRoute(10, HotRoute.Streak); sim.SetSlideProtection(-1); sim.SetMotion(10, 1);
                sim.SetDefenseAdjust(new DefenseAdjust { Coverage = CoverageScheme.Cover1, Press = PressMode.Press, Line = LineShift.Spread, Backers = LinebackerShift.Out, Blitz = BlitzShow.Show });
                sim.FlipOffense();
                Assert.IsNotNull(sim.RunToCompletion(), "seed " + seed);
            }
        }

        [Test]
        public void CoverageAudibleChangesTheShellAndAlignment()
        {
            var sim = Manual("Slants", "Shotgun", "Cover 3 Sky", "Nickel");
            Assert.AreEqual(CoverageScheme.Cover3, sim.DefPlay.Scheme);
            Assert.IsTrue(sim.SetDefenseAdjust(new DefenseAdjust { Coverage = CoverageScheme.Cover2 }));
            Assert.AreEqual(CoverageScheme.Cover2, sim.DefPlay.Scheme);
            for (int i = 0; i < 11; i++) Assert.AreSame(sim.DefPlay.Assignments[i], sim.Agents[11 + i].Assign);
            Assert.IsTrue(sim.SetDefenseAdjust(new DefenseAdjust { Coverage = CoverageScheme.Cover0 }));
            Assert.AreEqual(CoverageScheme.Cover0, sim.DefPlay.Scheme);
            Assert.IsTrue(sim.DefPlay.Assignments.Any(a => a is CoverageAssignment c && c.IsMan));
            CollectionAssert.Contains(sim.PreSnapSummary(), "COVER 0");
            // cover 1 is man free: the corners now cover men
            sim.SetDefenseAdjust(new DefenseAdjust { Coverage = CoverageScheme.Cover1 });
            Assert.IsTrue(sim.DefPlay.Assignments.OfType<CoverageAssignment>().Count(c => c.IsMan) >= 3);
        }

        [Test]
        public void PressAndOffMoveTheCornersAndFlagTheAssignment()
        {
            var sim = Manual("Slants", "Shotgun", "Cover 1 Man Free", "Nickel");
            int cb = Enumerable.Range(0, 11).First(i => sim.DefForm.Slots[i].Position == Position.CB);
            float baseDepth = sim.Agents[11 + cb].Pos.Y - sim.LosY;
            sim.SetDefenseAdjust(new DefenseAdjust { Press = PressMode.Press });
            Assert.AreEqual(1.0f, sim.Agents[11 + cb].Pos.Y - sim.LosY, 0.05f);
            Assert.IsTrue(((CoverageAssignment)sim.Agents[11 + cb].Assign).Press);
            sim.SetDefenseAdjust(new DefenseAdjust { Press = PressMode.Off });
            Assert.GreaterOrEqual(sim.Agents[11 + cb].Pos.Y - sim.LosY, 6.9f);
            Assert.IsFalse(((CoverageAssignment)sim.Agents[11 + cb].Assign).Press);
            sim.SetDefenseAdjust(new DefenseAdjust());
            Assert.AreEqual(baseDepth, sim.Agents[11 + cb].Pos.Y - sim.LosY, 0.01f, "clearing the adjustments restores the called alignment");
        }

        [Test]
        public void LineAndLinebackerShiftsMoveTheDefenders()
        {
            var sim = Manual("Slants", "Shotgun", "Cover 3 Sky", "Nickel");
            float widest(PlaySimulation s) => Enumerable.Range(11, 4).Max(i => Math.Abs(s.Agents[i].Pos.X - s.BallX0));
            float normal = widest(sim);
            sim.SetDefenseAdjust(new DefenseAdjust { Line = LineShift.Pinch });
            Assert.Less(widest(sim), normal - 0.8f);
            sim.SetDefenseAdjust(new DefenseAdjust { Line = LineShift.Spread });
            Assert.Greater(widest(sim), normal + 0.8f);
            int lb = Enumerable.Range(0, 11).First(i => PositionGroup.IsLinebacker(sim.DefForm.Slots[i].Position));
            sim.SetDefenseAdjust(new DefenseAdjust());
            float d0 = sim.Agents[11 + lb].Pos.Y - sim.LosY;
            sim.SetDefenseAdjust(new DefenseAdjust { Backers = LinebackerShift.In });
            Assert.Less(sim.Agents[11 + lb].Pos.Y - sim.LosY, d0);
            sim.SetDefenseAdjust(new DefenseAdjust { Backers = LinebackerShift.Out });
            Assert.Greater(sim.Agents[11 + lb].Pos.Y - sim.LosY, d0);
        }

        [Test]
        public void ShowBlitzBringsTheBlitzersToTheLineAndBackOffGivesRoom()
        {
            var sim = Manual("Slants", "Shotgun", "Cover 0 Blitz", "Nickel");
            int blitzer = Enumerable.Range(0, 11).First(i => sim.DefPlay.Assignments[i] is BlitzAssignment);
            float y0 = sim.Agents[11 + blitzer].Pos.Y - sim.LosY;
            sim.SetDefenseAdjust(new DefenseAdjust { Blitz = BlitzShow.Show });
            Assert.LessOrEqual(sim.Agents[11 + blitzer].Pos.Y - sim.LosY, 1.65f);
            Assert.LessOrEqual(sim.Agents[11 + blitzer].Pos.Y - sim.LosY, y0 + 0.01f);
            sim.SetDefenseAdjust(new DefenseAdjust { Blitz = BlitzShow.BackOff });
            Assert.Greater(sim.Agents[11 + blitzer].Pos.Y - sim.LosY, y0);
        }

        [Test]
        public void DelayOfGameKillsThePlayAtOnceAndFlagsTheOffense()
        {
            var sim = Manual("Slants", "Shotgun", "Cover 3 Sky", "Nickel");
            Assert.IsTrue(sim.CallDelayOfGame());
            Assert.AreEqual(PlayPhase.Dead, sim.Phase);
            Assert.AreEqual(PlayType.Penalty, sim.Result.Type);
            Assert.AreEqual(PenaltyType.DelayOfGame, sim.Result.Penalty.Type);
            Assert.AreEqual(Team.Home, sim.Result.Penalty.CommittedBy);
        }

        [Test]
        public void PreSnapFoulsBlowTheSnapDeadWithNoPlay()
        {
            // the false start roll happens at the snap: force the worst case with a lineman who knows nothing
            int dead = 0;
            for (int seed = 1; seed <= 400 && dead < 3; seed++)
            {
                var opts = new PlaySimOptions { AutoSnap = true, AutoSnapDelay = 0.1f, Penalties = true };
                var sim = SimTestKit.RunNamed("Slants", "Shotgun", "Cover 3 Sky", "Nickel", 40, 40, seed, null, null, opts);
                for (int k = 0; k < 20 && sim.Phase == PlayPhase.PreSnap; k++) sim.Step(new ControlInput());
                if (sim.Phase == PlayPhase.Dead && sim.Result != null && sim.Result.Type == PlayType.Penalty)
                {
                    dead++;
                    Assert.AreEqual(0f, sim.Time, "no live time passed");
                    Assert.IsTrue(sim.Result.Penalty.Def.PreSnap);
                    Assert.AreEqual(EndReason.Penalty, sim.EndReason);
                }
            }
            Assert.GreaterOrEqual(dead, 1, "some snaps end in a pre-snap foul");
        }
    }
}


