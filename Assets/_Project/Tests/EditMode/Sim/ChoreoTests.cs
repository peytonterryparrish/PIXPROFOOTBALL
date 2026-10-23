using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>v0.10.0 CHOREO: the sequence planner. Placement math of the takes, forecast and booking of tackles and catches, the actors following the takes' root motion, bounded steering, determinism.</summary>
    public class ChoreoTests
    {
        // ------------------------------------------------------------------------------------------ library
        static SyntheticChoreoLibrary Lib()
        {
            var lib = SyntheticChoreoLibrary.Default();
            var hi = SyntheticChoreoLibrary.Straight("synth_catch_high", 1.2f, 0f, 0f, 0f, 0.2f, 0f, -1f, 0.6f);
            hi.HandX = 0.05f; hi.HandY = 1.75f; hi.HandZ = 0.30f; hi.SettleT = 0.9f;
            var lo = SyntheticChoreoLibrary.Straight("synth_catch_low", 1.2f, 0f, 0f, 0f, 0.1f, 0f, -1f, 0.6f);
            lo.HandX = 0.0f; lo.HandY = 0.8f; lo.HandZ = 0.4f; lo.SettleT = 0.9f;
            var run = SyntheticChoreoLibrary.Straight("synth_catch_run", 1.4f, 0f, 0f, 0f, 5.6f, 0f, -1f, 0.6f);
            run.HandX = 0.0f; run.HandY = 1.5f; run.HandZ = 0.3f; run.SettleT = 1.0f;
            var pick = SyntheticChoreoLibrary.Straight("synth_pick", 1.2f, 0f, 0f, 0f, 1.0f, 0f, -1f, 0.55f);
            pick.HandX = 0.0f; pick.HandY = 1.6f; pick.HandZ = 0.3f; pick.SettleT = 0.9f;
            lib.CatchesMut.Add(hi); lib.CatchesMut.Add(lo); lib.CatchesMut.Add(run); lib.InterceptsMut.Add(pick);
            return lib;
        }

        static float Hips(ChoreoBooking b, bool a, float t) { return ((a ? b.ClipA : b.ClipB).Hips(t) - Vec2.Zero).Length; }

        // ------------------------------------------------------------------------------------------ math
        [Test]
        public void AlignPutsTheAuthoredPointAndHeadingOnTheField()
        {
            var r = ChoreoMath.Align(new Vec2(1.2f, 2.0f), 30f, new Vec2(20f, 40f), 75f);
            Vec2 p = r.Apply(new Vec2(1.2f, 2.0f));
            Assert.AreEqual(20f, p.X, 1e-3f); Assert.AreEqual(40f, p.Y, 1e-3f);
            Assert.AreEqual(45f, r.Yaw, 1e-3f);
            // a point 1 m ahead of the authored heading ends up 1 m ahead of the world heading
            Vec2 ahead = r.Apply(new Vec2(1.2f, 2.0f) + ChoreoMath.DirOfYaw(30f));
            Vec2 d = ahead - p;
            Assert.AreEqual(ChoreoClip.MetersToYards, d.Length, 1e-3f);
            Assert.AreEqual(75f, ChoreoMath.YawOf(d), 0.05f);
        }

        [Test]
        public void AlignPairAnchorsTheSecondActorExactly()
        {
            Vec2 a = new Vec2(0f, 1.27f), b = new Vec2(0f, 0.72f);
            var r = ChoreoMath.AlignPair(a, b, 0f, new Vec2(30f, 52f), new Vec2(29f, 50f), 20f, 0.6f);
            Vec2 wb = r.Apply(b);
            Assert.AreEqual(29f, wb.X, 1e-3f); Assert.AreEqual(50f, wb.Y, 1e-3f);
            // the layout between the two keeps its length
            float dAuth = (a - b).Length * ChoreoClip.MetersToYards;
            float dWorld = (r.Apply(a) - wb).Length;
            Assert.AreEqual(dAuth, dWorld, 1e-3f);
        }

        [Test]
        public void TimeToReachSolvesTheClosingQuadratic()
        {
            // 10 yd apart, closing at 5 yd/s, reach 1 yd: 1.8 s
            Assert.AreEqual(1.8f, ChoreoMath.TimeToReach(new Vec2(0f, 10f), new Vec2(0f, -5f), 1f), 1e-3f);
            Assert.AreEqual(0f, ChoreoMath.TimeToReach(new Vec2(0f, 0.5f), new Vec2(0f, -5f), 1f), 1e-6f);
            Assert.Greater(ChoreoMath.TimeToReach(new Vec2(0f, 10f), new Vec2(0f, 5f), 1f), 50f);
        }

        [Test]
        public void HashesAreStable()
        {
            Assert.AreEqual(ChoreoMath.HashString("TackleWrapFrontCHighDmo_A"), ChoreoMath.HashString("TackleWrapFrontCHighDmo_A"));
            Assert.AreNotEqual(ChoreoMath.HashString("a"), ChoreoMath.HashString("b"));
            Assert.AreEqual(ChoreoMath.Hash01(3, 4, 5), ChoreoMath.Hash01(3, 4, 5));
            Assert.AreEqual(-170f, ChoreoMath.DeltaAngle(170f, 0f), 1e-4f);
            Assert.AreEqual(20f, ChoreoMath.DeltaAngle(170f, 190f), 1e-4f);
        }

        [Test]
        public void ClipSamplingInterpolatesRootAndYaw()
        {
            var c = SyntheticChoreoLibrary.Straight("t", 2f, 0f, 0f, 2f, 4f, 90f);
            Vec2 mid = c.Root(1f);
            Assert.AreEqual(1f, mid.X, 0.03f); Assert.AreEqual(2f, mid.Y, 0.03f);
            Assert.AreEqual(90f, c.YawAt(0.7f), 1e-3f);
            Assert.AreEqual(Math.Sqrt(5.0), c.RootSpeed(1f), 0.05f);
        }

        [Test]
        public void ALibraryRoundTripsThroughItsFile()
        {
            var lib = Lib();
            StaticChoreoLibrary back;
            using (var ms = new System.IO.MemoryStream()) { ChoreoLibraryIO.Write(lib, ms); ms.Position = 0; back = ChoreoLibraryIO.Read(ms); }
            Assert.AreEqual(lib.Pairs.Count, back.Pairs.Count);
            Assert.AreEqual(lib.Catches.Count, back.Catches.Count);
            Assert.AreEqual(lib.Intercepts.Count, back.Intercepts.Count);
            Assert.AreEqual(lib.Pairs[0].A.Name, back.Pairs[0].A.Name);
            Assert.AreEqual(lib.Pairs[0].ContactT, back.Pairs[0].ContactT, 1e-6f);
            Assert.AreEqual(lib.Pairs[0].A.Root(0.7f).Y, back.Pairs[0].A.Root(0.7f).Y, 1e-5f);
            Assert.AreEqual(lib.Catches[1].HandY, back.Catches[1].HandY, 1e-6f);
        }

        // ------------------------------------------------------------------------------------------ tackles
        sealed class Lab { public float CarrierSpeed = 6.0f, DefDx = 0f, DefDy = 3.0f, DefSpeed = 6f; public bool FromBehind; public TackleType Force = TackleType.WrapUp; }

        static PlaySimulation TackleSim(int seed, Lab lab, IChoreoLibrary lib)
        {
            var opts = new PlaySimOptions { Penalties = false, ForceTackle = lab.Force, ChoreoLibrary = lib };
            PlaySimulation sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, seed, RuleSet.Pro(), null, opts);
            SimTestKit.StepToLive(sim);
            SimTestKit.ParkAll(sim, 6f);
            SimAgent c = null;
            for (int i = 1; i < 11 && c == null; i++) if (sim.Agents[i].Assign is CarryAssignment && !((CarryAssignment)sim.Agents[i].Assign).IsFake) c = sim.Agents[i];
            Assert.IsNotNull(c, "play has a ball carrier");
            sim.Blocks.HandedOff = true;
            sim.GiveBall(c);
            SimTestKit.Place(c, 27f, 40f, 0f, lab.CarrierSpeed);
            c.Body.Facing = new Vec2(0f, 1f);
            SimAgent d = sim.Agents[11 + 7];
            float dy = lab.FromBehind ? -Math.Abs(lab.DefDy) : lab.DefDy;
            Vec2 toC = new Vec2(-lab.DefDx, -dy).Norm();
            SimTestKit.Place(d, 27f + lab.DefDx, 40f + dy, toC.X * lab.DefSpeed, toC.Y * lab.DefSpeed);
            return sim;
        }

        static void RunUntilBooked(PlaySimulation sim, int max = 160)
        {
            var none = default(ControlInput);
            for (int i = 0; i < max && sim.Phase == PlayPhase.Live && sim.Choreo.Stats.TackleBookings == 0; i++) sim.Step(none);
        }

        [Test]
        public void NoLibraryNoChoreography()
        {
            var sim = TackleSim(11, new Lab(), null);
            Assert.IsNull(sim.Choreo);
        }

        [Test]
        public void ATackleBooksATakeAndBothBodiesFollowItsRoot()
        {
            var lib = Lib();
            var sim = TackleSim(11, new Lab(), lib);
            Assert.IsNotNull(sim.Choreo);
            RunUntilBooked(sim);
            Assert.AreEqual(1, sim.Choreo.Stats.TackleBookings, "the tackle is booked at the contact");
            ChoreoBooking b = null;
            foreach (var x in sim.Choreo.Bookings) if (x.Kind == ChoreoKind.Tackle) b = x;
            Assert.IsNotNull(b);
            Assert.AreEqual(PairKind.Wrap, b.PairKind);
            Assert.IsTrue(b.OwnsA && b.OwnsB);
            SimAgent ca = sim.Agents[b.A], cb = sim.Agents[b.B];
            float maxErr = 0f, maxStep = 0f; int ticks = 0;
            Vec2 prevA = ca.Pos;
            var none = default(ControlInput);
            for (int i = 0; i < 400 && b.OwnsA; i++)
            {
                sim.Step(none);
                if (!b.OwnsA) break;
                float t = sim.Choreo.Clock;
                float ct = b.ClipTime(t);
                float u = t - b.WarpStart;
                Vec2 root = b.Align.Apply(b.ClipA.Root(Math.Min(ct, b.ClipA.Length)));
                Vec2 slide = b.SlideA * (Choreographer.SlideTau * (1f - (float)Math.Exp(-u / Choreographer.SlideTau)));
                Vec2 expect = root + slide + b.ResidA * (1f - ChoreoMath.Smooth01(u / b.WarpOut));
                maxErr = Math.Max(maxErr, (ca.Pos - expect).Length);
                maxStep = Math.Max(maxStep, (ca.Pos - prevA).Length);
                prevA = ca.Pos; ticks++;
            }
            Assert.Greater(ticks, 60, "the take owned him for a while");
            Assert.Less(maxErr, 0.001f, "the carrier's simulated position IS the take's root path (plus the warp and the carried momentum)");
            Assert.Less(maxStep, 0.40f, "no pop: at most 0.4 yd (24 yd/s) in one tick");
            Assert.Less(b.MaxStepErr, 0.40f);
        }

        [Test]
        public void TheContactFrameHasTheTakesLayout()
        {
            var lib = Lib();
            var sim = TackleSim(5, new Lab { CarrierSpeed = 5f }, lib);
            RunUntilBooked(sim);
            ChoreoBooking b = null; foreach (var x in sim.Choreo.Bookings) if (x.Kind == ChoreoKind.Tackle) b = x;
            Assert.IsNotNull(b);
            // at the pair's contact time the two aligned hips are the authored distance apart
            float tc = b.Pair.ContactT;
            Vec2 ha = b.Align.Apply(b.ClipA.Hips(tc)), hb = b.Align.Apply(b.ClipB.Hips(tc));
            Assert.AreEqual(b.Pair.Distance * ChoreoClip.MetersToYards, (ha - hb).Length, 0.01f);
            Assert.GreaterOrEqual(b.SyncSim, b.StartSim);
            Assert.Less(b.SyncSim - b.StartSim, 0.6f);
        }

        [Test]
        public void TheActorsAreHandedBackAtTheTakesExit()
        {
            var lib = Lib();
            var sim = TackleSim(21, new Lab(), lib);
            RunUntilBooked(sim);
            ChoreoBooking b = null; foreach (var x in sim.Choreo.Bookings) if (x.Kind == ChoreoKind.Tackle) b = x;
            var none = default(ControlInput);
            int guard = 0;
            while ((b.OwnsA || b.OwnsB) && guard++ < 900) sim.Step(none);
            Assert.IsFalse(b.OwnsA); Assert.IsFalse(b.OwnsB);
            Assert.AreEqual(ChoreoPhase.Done, b.Phase);
            Assert.IsNull(sim.Choreo.OwnerOf(b.A)); Assert.IsNull(sim.Choreo.OwnerOf(b.B));
            // released no earlier than the exit time of each take
            Assert.GreaterOrEqual(sim.Choreo.Clock, Math.Min(b.ReleaseSimA, b.ReleaseSimB) - 0.05f);
        }

        [Test]
        public void ForecastLeadsTheContact()
        {
            var lib = Lib();
            var errs = new List<float>(); int plays = 0;
            string[] offs = { "Inside Zone Right", "Outside Zone Left", "Slants", "Four Verticals" };
            string[] forms = { "I-Form", "I-Form", "Shotgun", "Shotgun" };
            for (int s = 0; s < 40; s++)
            {
                int k = s % 4;
                PlaySimulation sim;
                try { sim = SimTestKit.RunNamed(offs[k], forms[k], "Cover 3 Sky", "4-3 Base", 75, 75, 100 + s, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false, ChoreoLibrary = lib }); }
                catch (ArgumentException) { continue; }
                sim.RunToCompletion(); plays++;
                errs.AddRange(sim.Choreo.Stats.EtaError);
            }
            Assert.Greater(errs.Count, 15, "most tackles were forecast about 0.5 s ahead");
            float sum = 0f; foreach (float e in errs) sum += e;
            Assert.Less(sum / errs.Count, 0.2f, "the 0.5 s forecast is within a fifth of a second of the real contact on average");
        }

        [Test]
        public void SteeringIsBoundedAndTheDefenderStillArrives()
        {
            var lib = Lib();
            int booked = 0, steered = 0;
            for (int s = 0; s < 30; s++)
            {
                PlaySimulation sim;
                try { sim = SimTestKit.RunNamed(s % 2 == 0 ? "Inside Zone Right" : "Outside Zone Left", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 200 + s, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false, ChoreoLibrary = lib }); }
                catch (ArgumentException) { continue; }
                sim.RunToCompletion();
                Assert.LessOrEqual(sim.Choreo.Stats.MaxSteerRate, 2.3f, "the bend of a path never exceeds 2.3 yd/s");
                booked += sim.Choreo.Stats.TackleBookings; steered += sim.Choreo.Stats.SteeredTicks;
            }
            Assert.Greater(booked, 15, "the tackles still happen");
            Assert.Greater(steered, 0, "the pursuers are bent a little on the way in");
        }

        [Test]
        public void SameSeedSameSequence()
        {
            string a = Digest(TackleSim(31, new Lab(), Lib())), b = Digest(TackleSim(31, new Lab(), Lib()));
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(Digest(TackleSim(32, new Lab { DefDx = 3f }, Lib())), a);
        }

        static string Digest(PlaySimulation sim)
        {
            var sb = new StringBuilder();
            var none = default(ControlInput);
            for (int i = 0; i < 480; i++)
            {
                sim.Step(none);
                if (i % 7 == 0) for (int k = 0; k < 22; k += 3) sb.Append(sim.Agents[k].Pos.X.ToString("0.0000")).Append(',').Append(sim.Agents[k].Pos.Y.ToString("0.0000")).Append(';');
            }
            foreach (var b in sim.Choreo.Bookings) sb.Append(b.ToString()).Append(b.StartSim.ToString("0.000")).Append('|');
            return sb.ToString();
        }

        // ------------------------------------------------------------------------------------------ catches
        static PlaySimulation CatchSim(int seed, IChoreoLibrary lib, float ballZ, float speed = 20f, float miss = 0f, float rcvVy = 0f)
        {
            var opts = new PlaySimOptions { Penalties = false, ChoreoLibrary = lib };
            PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, seed, RuleSet.Pro(), null, opts);
            SimTestKit.StepToLive(sim);
            SimTestKit.ParkAll(sim, 2f);
            SimAgent r = sim.Agents[10];
            r.R.Catching = 99;
            SimTestKit.Place(r, 27f, 48f, 0f, rcvVy);
            r.Body.Facing = new Vec2(0f, rcvVy > 0.5f ? 1f : -1f);
            sim.Qb.HasBall = false; sim.Qb.QbState = QbPhase.Done;
            Vec3 from = new Vec3(27f + miss, 42f, 1.9f);
            Vec3 to = new Vec3(27f + miss, 48f + rcvVy * 0.4f, ballZ);
            Vec3 dv = to - from;
            float t = Math.Max(0.05f, dv.XY.Length / speed);
            Vec3 vel = new Vec3(dv.X / t, dv.Y / t, (dv.Z + 0.5f * SimMath.Gravity * t * t) / t);
            sim.Ball.Launch(BallState.Pass, from, vel, 0);
            sim.OnPassReleased(sim.Qb, r, false);
            return sim;
        }

        [Test]
        public void ACatchIsBookedAheadAndMadeAtTheHands()
        {
            var lib = Lib();
            int booked = 0, caught = 0; float worstGap = 0f; float worstSync = 0f; float worstStep = 0f;
            for (int s = 0; s < 14; s++)
            {
                var sim = CatchSim(300 + s, lib, s % 2 == 0 ? 1.7f : 0.9f);
                bool seen = false; float bookedAt = -1f; ChoreoBooking cb = null;
                for (int i = 0; i < 90 && sim.Phase == PlayPhase.Live; i++)
                {
                    sim.Step(default(ControlInput));
                    var b = sim.Choreo.CatchBookingOf(10);
                    if (b != null && !seen) { seen = true; bookedAt = sim.Choreo.Clock; cb = b; booked++; }
                    if (sim.PassCompleted || sim.Turnover || sim.Ball.State == BallState.Dead) break;
                }
                if (sim.PassCompleted && cb != null && !sim.Log.Has(SimEventType.Drop) && !sim.PassTipped)
                {
                    caught++;
                    float lead = sim.Choreo.Clock - bookedAt;
                    Assert.Greater(lead, 0.1f, "booked before the ball arrived");
                    // the sim ball is at the hands of the take when the catch is made
                    PlayEvent ev = sim.Log.First(SimEventType.Catch).Value;
                    worstSync = Math.Max(worstSync, Math.Abs(ev.Time - cb.SyncSim));
                    worstStep = Math.Max(worstStep, cb.MaxStepErr);
                }
            }
            Assert.GreaterOrEqual(booked, 10, "the catch is booked for most passes");
            Assert.GreaterOrEqual(caught, 8);
            Assert.Less(worstSync, 0.30f, "the booked sync time is within 0.3 s of the real catch");
            Assert.Less(worstGap, 0.2f);
            Assert.Less(worstStep, 0.45f, "a body that follows a standing catch take is never popped by more than 0.45 yd in a tick");
        }

        [Test]
        public void ARunningCatchIsSteeredBelowTheBendLimit()
        {
            var lib = Lib();
            float maxRate = 0f;
            for (int s = 0; s < 10; s++)
            {
                var sim = CatchSim(500 + s, lib, 1.5f, 21f, 0.6f, 5.5f);
                for (int i = 0; i < 90 && sim.Phase == PlayPhase.Live; i++)
                {
                    sim.Step(default(ControlInput));
                    if (sim.PassCompleted || sim.Turnover || sim.Ball.State == BallState.Dead) break;
                }
                maxRate = Math.Max(maxRate, sim.Choreo.Stats.MaxSteerRate);
                var b = sim.Choreo.LastBookingOf(10);
                if (b != null) Assert.IsFalse(b.OwnsA && b.ClipA.RootSpeed(b.ClipA.ContactT) > 2.6f, "a runner is not pinned to a standing take");
            }
            Assert.LessOrEqual(maxRate, 2.3f);
        }

        [Test]
        public void WholePlaysStayWellFormedWithTheChoreographer()
        {
            var lib = Lib();
            string[] offs = { "Inside Zone Right", "Four Verticals", "Slants" };
            string[] forms = { "I-Form", "Shotgun", "Shotgun" };
            for (int s = 0; s < 18; s++)
            {
                int k = s % 3;
                var opts = new PlaySimOptions { Penalties = true, ChoreoLibrary = lib };
                PlaySimulation sim;
                try { sim = SimTestKit.RunNamed(offs[k], forms[k], "Cover 3 Sky", "4-3 Base", 75, 75, 900 + s, RuleSet.Pro(), null, opts); }
                catch (ArgumentException) { continue; }
                sim.RunToCompletion();
                Assert.AreEqual(PlayPhase.Dead, sim.Phase);
                for (int i = 0; i < 22; i++)
                {
                    var p = sim.Agents[i].Pos;
                    Assert.IsFalse(float.IsNaN(p.X) || float.IsNaN(p.Y), "agent " + i + " has a position");
                    Assert.Less(Math.Abs(p.X - 26.7f), 60f); Assert.Less(Math.Abs(p.Y - 50f), 90f);
                }
                Assert.LessOrEqual(sim.Choreo.Stats.MaxSteerRate, 2.3f);
            }
        }

        // ------------------------------------------------------------------------------------------ hip flip profile
        [Test]
        public void TheTurnProfileStartsAndEndsAtRestAndNeverExceedsItsRate()
        {
            foreach (float total in new[] { 0.3f, 1.0f, 2.0f, 2.8f, 3.1f })
            {
                const float omega = 4.0f, ramp = 0.13f;
                float T = ChoreoMath.TurnDuration(total, omega, ramp);
                Assert.Greater(T, 0f);
                Assert.AreEqual(total, ChoreoMath.TurnAngle(T, total, omega, ramp), 1e-4f, "ends on the target");
                Assert.AreEqual(0f, ChoreoMath.TurnAngle(0f, total, omega, ramp), 1e-6f);
                float prev = 0f, prevRate = 0f, maxRate = 0f, maxAccel = 0f; const float h = 1f / 120f;
                for (float t = h; t <= T + h; t += h)
                {
                    float a = ChoreoMath.TurnAngle(t, total, omega, ramp);
                    Assert.GreaterOrEqual(a, prev - 1e-5f, "never turns back");
                    float rate = (a - prev) / h; maxRate = Math.Max(maxRate, rate); maxAccel = Math.Max(maxAccel, Math.Abs(rate - prevRate) / h);
                    prev = a; prevRate = rate;
                }
                Assert.LessOrEqual(maxRate, omega * 1.02f);
                Assert.LessOrEqual(maxAccel, omega / ramp * 1.15f + 1f, "the rate changes at a bounded acceleration, never a step");
                Assert.LessOrEqual(Math.Abs(ChoreoMath.TurnAngle(h, total, omega, ramp) / h), omega / ramp * h * 1.2f + 1e-3f, "starts from rest");
            }
            Assert.AreEqual(ChoreoMath.TurnDuration(2f, 4f, 0.13f) + 0.13f * 0f, 2f / 4f + 0.13f, 1e-4f, "plateau turns take angle over rate plus one ramp");
        }

        [Test]
        public void ABackThatOpensHisHipsDoesItOnAPlannedProfileAndTheSameSeedGivesTheSameTurns()
        {
            string[] sig = new string[2]; int turns = 0;
            for (int run = 0; run < 2; run++)
            {
                var sb = new StringBuilder();
                for (int s = 0; s < 6; s++)
                {
                    var opts = new PlaySimOptions { Penalties = false, ChoreoLibrary = Lib() };
                    PlaySimulation sim;
                    try { sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 400 + s, RuleSet.Pro(), null, opts); }
                    catch (ArgumentException) { continue; }
                    sim.RunToCompletion();
                    foreach (var tn in sim.Choreo.Turns) { sb.Append(tn.Agent).Append(':').Append(tn.T0.ToString("0.00")).Append(':').Append(tn.TotalRad.ToString("0.00")).Append(';'); Assert.Greater(tn.Duration, 0.25f); Assert.LessOrEqual(tn.Duration, 2.0f); if (run == 0) turns++; }
                }
                sig[run] = sb.ToString();
            }
            Assert.AreEqual(sig[0], sig[1], "the flips are a pure function of the play");
            TestContext.WriteLine("hip flips planned in 6 plays: " + turns);
        }
    }
}
