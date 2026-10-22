using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;
using static PixFootball.Tests.AbilityLab;

namespace PixFootball.Tests
{
    /// <summary>
    /// v0.8.0: every one of the 41 abilities has a measurable effect in the play simulation. Each test is a paired experiment on the same seeds and
    /// rosters (see <see cref="AbilityLab"/>): one player gets the ability at tier 3 (or, for team-wide effects, the team does), the numbers are
    /// compared with the same plays without it, and the measured size is printed. The sizes are logged in Docs/QA/v080/sim_abilities.md.
    /// </summary>
    public class SimAbilityTests
    {
        private static readonly string[] Coverages = { "Cover 1 Man Free", "Cover 2 Zone", "Cover 3 Sky", "Cover 4 Quarters", "Cover 0 Blitz" };

        private static Play Cover(int i) { return SimTestKit.Defense(Coverages[i % Coverages.Length], "4-3 Base"); }
        private static readonly string[] ZoneNames = { "Cover 2 Zone", "Cover 3 Sky", "Cover 4 Quarters" };
        private static Play ZoneCover(int i) { return SimTestKit.Defense(ZoneNames[i % ZoneNames.Length], "4-3 Base"); }
        private static Play Cover(string name) { return SimTestKit.Defense(name, "4-3 Base"); }

        private static int CarrierSlot(Play p)
        {
            for (int i = 1; i < 11; i++)
            {
                var ca = p.Assignments[i] as CarryAssignment;
                if (ca != null && !ca.IsFake) return i;
            }
            return 1;
        }

        private static IEnumerable<Play> Passes(params string[] concepts)
        {
            foreach (string c in concepts) foreach (Play p in Offense(c)) yield return p;
        }

        private static void Report(Result r) { TestContext.WriteLine("ABILITY " + r); }

        private static GameState ThirdAndEight()
        {
            GameState gs = SimTestKit.Scrimmage(RuleSet.Pro(), 40, 3, 8);
            gs.Quarter = 2; gs.ClockSeconds = 600f;
            return gs;
        }

        // ================================================================== plumbing

        [Test]
        public void TheAbilityLookupFlattensEffectsAndRespectsContexts()
        {
            Player qb = SimTestKit.MakePlayer(new Rng(1), Position.QB, 80);
            Assert.IsNull(AbilityFx.For(qb), "no ability, no lookup");
            Give(qb, "money_down", 2);
            AbilityFx fx = AbilityFx.For(qb);
            Assert.IsNotNull(fx);
            int third = AbilityFx.Bit(AbilityContext.ThirdFourthDown);
            Assert.AreEqual(5f, fx.Sum(SimHook.ThrowAccuracy, third), 0.001f);
            Assert.AreEqual(0f, fx.Sum(SimHook.ThrowAccuracy, 0), 0.001f, "outside the context nothing counts");
            Assert.AreEqual(5f, fx.SumOnly(SimHook.ThrowAccuracy, third), 0.001f);
            Assert.AreEqual(2f, fx.Sum(SimHook.Clutch, third), 0.001f);
        }

        [Test]
        public void ThePlayContextBitsFollowTheGameSituation()
        {
            GameState gs = ThirdAndEight();
            gs.HomeScore = 17; gs.AwayScore = 14; gs.Quarter = 4; gs.ClockSeconds = 90f; gs.BallSpot = 85; gs.Distance = 1;
            PlaySimulation sim = SimTestKit.Run(SimTestKit.Offense("Inside Zone Right"), Cover("Cover 3 Sky"), 75, 75, 5, null, gs, new PlaySimOptions { Penalties = false });
            int m = sim.PlayContext;
            Assert.IsTrue((m & AbilityFx.Bit(AbilityContext.ThirdFourthDown)) != 0, "third down");
            Assert.IsTrue((m & AbilityFx.Bit(AbilityContext.TwoMinute)) != 0, "two-minute drill");
            Assert.IsTrue((m & AbilityFx.Bit(AbilityContext.RedZone)) != 0, "red zone");
            Assert.IsTrue((m & AbilityFx.Bit(AbilityContext.ShortYardage)) != 0, "short yardage");
            Assert.IsTrue((m & AbilityFx.Bit(AbilityContext.CloseGameLate)) != 0, "close game late");
            gs.HomeScore = 38; gs.Quarter = 2; gs.ClockSeconds = 800f; gs.BallSpot = 30; gs.Down = 1; gs.Distance = 10;
            sim = SimTestKit.Run(SimTestKit.Offense("Inside Zone Right"), Cover("Cover 3 Sky"), 75, 75, 5, null, gs, new PlaySimOptions { Penalties = false });
            Assert.AreEqual(0, sim.PlayContext, "a first down in the second quarter of a blowout has no special context");
        }

        [Test]
        public void APlayerWithoutAbilitiesAndNeutralMoraleIsExactlyTheOldPlayer()
        {
            for (int seed = 0; seed < 30; seed++)
            {
                PlaySimulation a = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 900 + seed, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false, PlayerModifiers = true });
                PlaySimulation b = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 900 + seed, RuleSet.Pro(), null, new PlaySimOptions { Penalties = false, PlayerModifiers = false });
                a.RunToCompletion(); b.RunToCompletion();
                Assert.AreEqual(b.Log.Digest(), a.Log.Digest(), "seed " + seed + ": the career layer changes nothing for a player who has none of it");
            }
        }

        [Test]
        public void MoraleMovesEveryRatingByUpToTwoPoints()
        {
            Play off = SimTestKit.Offense("Inside Zone Right"), def = Cover("Cover 3 Sky");
            PlaySimulation happy = Build(off, def, 75, 75, 3, (ro, rd, of, df) => { foreach (Player p in ro) p.Morale = 100; });
            PlaySimulation sad = Build(off, def, 75, 75, 3, (ro, rd, of, df) => { foreach (Player p in ro) p.Morale = 20; });
            PlaySimulation base_ = Build(off, def, 75, 75, 3, (ro, rd, of, df) => { foreach (Player p in ro) p.Morale = 70; });
            for (int i = 0; i < 11; i++)
            {
                Assert.AreEqual(2, happy.Agents[i].PlayerModPoints);
                Assert.AreEqual(-2, sad.Agents[i].PlayerModPoints);
                Assert.AreEqual(base_.Agents[i].R.Speed + 2, happy.Agents[i].R.Speed);
                Assert.AreEqual(base_.Agents[i].R.Awareness - 2, sad.Agents[i].R.Awareness);
                Assert.AreEqual(base_.Agents[i].R.Injury, happy.Agents[i].R.Injury, "injury resistance is a durability, not a skill");
            }
            Assert.AreEqual(0, happy.Agents[11].PlayerModPoints, "the other team is untouched");
        }

        [Test]
        public void MoraleMakesAMeasurableDifferenceOverAGame()
        {
            Play[] runs = Offense("Inside Zone");
            Result r = Pair("morale +2 vs -2 team, yards per carry", 900, 4000, (seed, high) =>
            {
                Play off = runs[seed % runs.Length];
                PlaySimulation sim = Build(off, Cover(seed), 75, 75, seed, (ro, rd, of, df) => { foreach (Player p in ro) p.Morale = high ? 100 : 20; });
                PlayResult res = sim.RunToCompletion();
                return res.Penalty != null ? new Tally(0, 0) : new Tally(res.Yards, 1);
            });
            Report(r);
            Assert.Greater(r.Delta, 0.05, "a happy team runs for more than an unhappy one");
        }

        [Test]
        public void LockerRoomLeaderLiftsEveryTeammate()
        {
            Play off = SimTestKit.Offense("Inside Zone Right"), def = Cover("Cover 3 Sky");
            PlaySimulation none = Build(off, def, 75, 75, 3);
            PlaySimulation lead = Build(off, def, 75, 75, 3, (ro, rd, of, df) => { Give(ro[3], "locker_room_leader", 3); });
            for (int i = 0; i < 11; i++)
            {
                Assert.AreEqual(1, lead.Agents[i].PlayerModPoints, "a tier-3 leader (effect 3 x 0.34 = 1.02) lifts the whole unit by a point");
                Assert.AreEqual(none.Agents[i].R.Awareness + 1, lead.Agents[i].R.Awareness);
            }
            Assert.AreEqual(0, lead.Agents[15].PlayerModPoints, "the opponents do not share it");
            PlaySimulation t1 = Build(off, def, 75, 75, 3, (ro, rd, of, df) => { Give(ro[3], "locker_room_leader", 1); });
            Assert.AreEqual(0, t1.Agents[0].PlayerModPoints, "a lone tier-1 leader is a morale story: 0.34 of a point rounds away");
            PlaySimulation two = Build(off, def, 75, 75, 3, (ro, rd, of, df) => { Give(ro[3], "locker_room_leader", 3); Give(ro[4], "locker_room_leader", 3); });
            Assert.AreEqual(2, two.Agents[0].PlayerModPoints, "two tier-3 leaders: 6 x 0.34 = 2.04, +2 for everybody");
            Report(new Result { Name = "locker_room_leader t3: +1 rating point to all 11 teammates (t1 0, two t3 leaders +2)", Base = new Arm { X = 0, N = 1 }, With = new Arm { X = 1, N = 1 } });
        }

        // ================================================================== quarterbacks

        private static Tally ThrowError(PlaySimulation sim, Func<PlaySimulation, bool> filter)
        {
            sim.RunToCompletion();
            if (sim.LastThrowDistance <= 0f || !filter(sim)) return new Tally(0, 0);
            return new Tally(sim.LastThrowAimError / sim.LastThrowDistance, 1);
        }

        [Test]
        public void RocketArmThrowsHarder()
        {
            Play[] plays = Passes("Four Verticals", "Mesh", "Slants").ToArray();
            Result r = Pair("rocket_arm t3 ball speed at release (yd/s)", 500, 11000, (seed, with) =>
            {
                PlaySimulation sim = Build(plays[seed % plays.Length], Cover(seed), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[0], "rocket_arm", 3); });
                sim.RunToCompletion();
                return sim.LastThrowSpeed > 0f ? new Tally(sim.LastThrowSpeed, 1) : new Tally(0, 0);
            });
            Report(r);
            Assert.Greater(r.Delta, 0.4, "six rating points of arm strength are a visibly faster ball");
        }

        [Test]
        public void CoolUnderFireKeepsHisAccuracyWhenThePocketCollapses()
        {
            Play[] plays = Passes("Four Verticals", "Corner-Post", "Levels", "Snag").ToArray();
            Result r = Pair("cool_under_fire t3 aim error per yard under pressure", 1500, 12000, (seed, with) =>
            {
                PlaySimulation sim = Build(plays[seed % plays.Length], ZoneCover(seed), 66, 84, seed, (ro, rd, of, df) => { if (with) Give(ro[0], "cool_under_fire", 3); });
                return ThrowError(sim, s => s.LastThrowPressure >= PlaySimulation.UnderPressureAt);
            });
            Report(r);
            Assert.Less(r.Delta, -0.001, "the passer is more accurate under pressure");
            Assert.Less(r.With.Rate, r.Base.Rate * 0.97);
        }

        [Test]
        public void CoolUnderFireDoesNothingInAKeptCleanPocket()
        {
            Play[] plays = Passes("Slants", "Mesh").ToArray();
            Result r = Pair("cool_under_fire t3 aim error per yard in a clean pocket", 600, 12500, (seed, with) =>
            {
                PlaySimulation sim = Build(plays[seed % plays.Length], Cover("Cover 3 Sky"), 75, 60, seed, (ro, rd, of, df) => { if (with) Give(ro[0], "cool_under_fire", 3); });
                return ThrowError(sim, s => s.LastThrowPressure < 0.1f);
            });
            Report(r);
            Assert.AreEqual(r.Base.Rate, r.With.Rate, 0.0004, "a clean pocket gets nothing extra");
        }

        [Test]
        public void MoneyDownMakesTheThrowsOnThirdDownMoreAccurate()
        {
            Play[] plays = Passes("Mesh", "Slants", "Texas Route").ToArray();
            Result r = Pair("money_down t3 aim error per yard on 3rd down", 900, 13000, (seed, with) =>
            {
                PlaySimulation sim = Build(plays[seed % plays.Length], Cover(seed), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[0], "money_down", 3); }, ThirdAndEight());
                return ThrowError(sim, s => true);
            });
            Report(r);
            Assert.Less(r.With.Rate, r.Base.Rate * 0.92, "+7 accuracy and +3 across the board on third down");
            Result first = Pair("money_down t3 aim error per yard on 1st down (control)", 600, 13500, (seed, with) =>
            {
                PlaySimulation sim = Build(plays[seed % plays.Length], Cover(seed), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[0], "money_down", 3); });
                return ThrowError(sim, s => true);
            });
            Report(first);
            Assert.AreEqual(first.Base.Rate, first.With.Rate, 0.0005, "on first down he is the same passer");
        }

        [Test]
        public void TwoMinuteGeneralIsSharperInTheTwoMinuteDrill()
        {
            Play[] plays = Passes("Mesh", "Slants", "Texas Route").ToArray();
            GameState Drill() { GameState gs = SimTestKit.Scrimmage(RuleSet.Pro(), 45, 2, 7); gs.Quarter = 4; gs.ClockSeconds = 80f; gs.HomeScore = 14; gs.AwayScore = 17; return gs; }
            Result r = Pair("two_minute_general t3 aim error per yard, last two minutes", 900, 14000, (seed, with) =>
            {
                PlaySimulation sim = Build(plays[seed % plays.Length], Cover(seed), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[0], "two_minute_general", 3); }, Drill());
                return ThrowError(sim, s => true);
            });
            Report(r);
            Assert.Less(r.With.Rate, r.Base.Rate * 0.96);
        }

        [Test]
        public void DeepTouchPutsTheLongBallWhereItBelongs()
        {
            Play[] plays = Passes("Four Verticals").ToArray();
            Result r = Pair("deep_touch t3 aim error per yard on throws of 18+ yards", 700, 15000, (seed, with) =>
            {
                PlaySimulation sim = Build(plays[seed % plays.Length], Cover(seed), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[0], "deep_touch", 3); });
                return ThrowError(sim, s => s.LastThrowDistance >= 20f);
            });
            Report(r);
            Assert.Less(r.With.Rate, r.Base.Rate * 0.94);
            Result shortThrow = Pair("deep_touch t3 aim error per yard on short throws (control)", 500, 15500, (seed, with) =>
            {
                PlaySimulation sim = Build(Offense("Slants")[seed % 4], Cover(seed), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[0], "deep_touch", 3); });
                return ThrowError(sim, s => s.LastThrowDistance < 14f);
            });
            Report(shortThrow);
            Assert.AreEqual(shortThrow.Base.Rate, shortThrow.With.Rate, 0.0005);
        }

        [Test]
        public void EscapeArtistSlipsSacksAndRunsFaster()
        {
            Play[] plays = Passes("Four Verticals", "Corner-Post", "Levels", "Snag").ToArray();
            Result r = Pair("escape_artist t3 sack rate on long-developing dropbacks", 1800, 16000, (seed, with) =>
            {
                PlaySimulation sim = Build(plays[seed % plays.Length], ZoneCover(seed), 70, 80, seed, (ro, rd, of, df) => { if (with) Give(ro[0], "escape_artist", 3); });
                PlayResult res = sim.RunToCompletion();
                if (res.Penalty != null) return new Tally(0, 0);
                return new Tally(res.Type == PlayType.Sack ? 1 : 0, 1);
            });
            Report(r);
            Assert.Less(r.With.Rate, r.Base.Rate * 0.92, "20 percent of the would-be sacks are slipped");
            // ScrambleSpeed: the body of a scrambling quarterback is quicker
            PlaySimulation a = Build(plays[0], Cover(2), 75, 75, 1, (ro, rd, of, df) => { Give(ro[0], "escape_artist", 3); });
            PlaySimulation b = Build(plays[0], Cover(2), 75, 75, 1);
            SimTestKit.StepToLive(a); SimTestKit.StepToLive(b);
            a.Qb.QbState = QbPhase.Scramble; b.Qb.QbState = QbPhase.Scramble;
            a.Step(default(ControlInput)); b.Step(default(ControlInput));
            TestContext.WriteLine("ABILITY escape_artist t3 scrambling top speed: " + b.Qb.Body.EffectiveVMax.ToString("0.00") + " -> " + a.Qb.Body.EffectiveVMax.ToString("0.00") + " yd/s");
            Assert.Greater(a.Qb.Body.EffectiveVMax, b.Qb.Body.EffectiveVMax * 1.004f, "+3 speed while he scrambles");
        }

        // ================================================================== ball carriers

        private static Tally RushYards(PlaySimulation sim)
        {
            PlayResult res = sim.RunToCompletion();
            return res.Penalty != null ? new Tally(0, 0) : new Tally(res.Yards, 1);
        }

        [Test]
        public void WreckingBallBreaksTacklesAndHoldsTheBall()
        {
            Play[] runs = Offense("Power").Concat(Offense("Dive")).Concat(Offense("Iso")).ToArray();
            Result r = Pair("wrecking_ball t3 broken tackles per carry", 1200, 20000, (seed, with) =>
            {
                Play off = runs[seed % runs.Length];
                int cs = CarrierSlot(off);
                PlaySimulation sim = Build(off, Cover(seed), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[cs], "wrecking_ball", 3); });
                sim.RunToCompletion();
                double broken = 0;
                foreach (PlayEvent e in sim.Log.Events) if (e.Type == SimEventType.BrokenTackle && e.AgentB == cs) broken++;
                return new Tally(broken, 1);
            });
            Report(r);
            Assert.Greater(r.With.Rate, r.Base.Rate * 1.10, "+10 break-tackle points");
            Result yards = Pair("wrecking_ball t3 yards per carry", 4000, 20000, (seed, with) =>
            {
                Play off = runs[seed % runs.Length];
                int cs = CarrierSlot(off);
                return RushYards(Build(off, Cover(seed), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[cs], "wrecking_ball", 3); }));
            });
            Report(yards);
            Assert.Greater(yards.Delta, 0.0);
        }

        [Test]
        public void WreckingBallFumblesLessOnBigHits()
        {
            Play run = Offense("Dive")[0];
            int cs = CarrierSlot(run);
            PlaySimulation plain = Build(run, Cover(2), 75, 75, 5);
            PlaySimulation wb = Build(run, Cover(2), 75, 75, 5, (ro, rd, of, df) => { Give(ro[cs], "wrecking_ball", 3); });
            foreach (TackleType t in new[] { TackleType.WrapUp, TackleType.BigHit, TackleType.GangTackle })
            {
                float p0 = plain.Tackles.FumbleChance(plain.Agents[15], plain.Agents[cs], t, 6f, false), p1 = wb.Tackles.FumbleChance(wb.Agents[15], wb.Agents[cs], t, 6f, false);
                TestContext.WriteLine("ABILITY wrecking_ball t3 fumble chance on a " + t + ": " + (100 * p0).ToString("0.00") + "% -> " + (100 * p1).ToString("0.00") + "% (x" + (p1 / p0).ToString("0.00") + ")");
                Assert.AreEqual(p0 * 0.85f, p1, p0 * 0.02f, "15 percent fewer fumbles at tier 3 (the Carrying bonus is a separate rating)");
            }
        }

        private static Tally MoveRate(PlaySimulation sim, int carrier, string prefix)
        {
            sim.RunToCompletion();
            double won = 0, n = 0;
            foreach (PlayEvent e in sim.Log.Events)
                if (e.Type == SimEventType.CarrierMove && e.AgentA == carrier && e.Note != null && e.Note.StartsWith(prefix)) { n++; if (e.Value > 0f) won++; }
            return new Tally(won, n);
        }

        [Test]
        public void AnkleShakerWinsMoreJukes()
        {
            Play[] runs = Offense("Outside Zone").Concat(Offense("Toss Sweep")).Concat(Offense("Jet Sweep")).ToArray();
            Result r = Pair("ankle_shaker t3 juke success rate", 1500, 22000, (seed, with) =>
            {
                Play off = runs[seed % runs.Length];
                int cs = CarrierSlot(off);
                return MoveRate(Build(off, Cover(seed), 80, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[cs], "ankle_shaker", 3); }), cs, "Juke");
            });
            Report(r);
            Assert.Greater(r.Delta, 0.03, "16 catalogue points, scaled by the Sim gain, on every juke");
        }

        [Test]
        public void AbilityEffectsScaleWithTheTier()
        {
            Play[] runs = Offense("Outside Zone").Concat(Offense("Toss Sweep")).Concat(Offense("Jet Sweep")).ToArray();
            double[] rate = new double[3];
            for (int tier = 1; tier <= 3; tier++)
            {
                int t = tier;
                Result r = Pair("ankle_shaker t" + tier + " juke success rate", 1200, 22500, (seed, with) =>
                {
                    Play off = runs[seed % runs.Length];
                    int cs = CarrierSlot(off);
                    return MoveRate(Build(off, Cover(seed), 80, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[cs], "ankle_shaker", t); }), cs, "Juke");
                });
                Report(r);
                rate[tier - 1] = r.With.Rate;
            }
            Assert.Less(rate[0], rate[1] + 0.005);
            Assert.Less(rate[1], rate[2] + 0.005);
            Assert.Less(rate[0], rate[2], "a three-star juke beats a one-star juke");
        }

        [Test]
        public void WhirlwindWinsMoreSpins()
        {
            Play[] runs = Offense("Outside Zone").Concat(Offense("Toss Sweep")).Concat(Offense("Jet Sweep")).Concat(Offense("Inside Zone")).ToArray();
            Result r = Pair("whirlwind t3 spin success rate", 2400, 23000, (seed, with) =>
            {
                Play off = runs[seed % runs.Length];
                int cs = CarrierSlot(off);
                return MoveRate(Build(off, Cover(seed), 80, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[cs], "whirlwind", 3); }), cs, "Spin");
            });
            Report(r);
            Assert.Greater(r.Delta, 0.08, "20 points on every spin");
        }

        // ---- speed abilities need a clean lane: park the defense a long way off ----

        private static PlaySimulation OpenFieldRun(int seed, Mutate mutate, string run = "Inside Zone")
        {
            Play[] runs = Offense(run);
            Play off = runs[seed % runs.Length];
            PlaySimulation sim = Build(off, Cover("Cover 3 Sky"), 75, 75, seed, mutate);
            for (int i = 11; i < 22; i++) SimTestKit.Place(sim.Agents[i], 5f + i * 3f, 120f);
            return sim;
        }

        [Test]
        public void HomeRunBurstGetsAwayInTheOpenField()
        {
            Result r = Pair("home_run_burst t3 yards covered in 5.5 s with a clean lane", 40, 24000, (seed, with) =>
            {
                Play[] runs = Offense("Inside Zone");
                Play off = runs[seed % runs.Length];
                int cs = CarrierSlot(off);
                PlaySimulation sim = OpenFieldRun(seed, (ro, rd, of, df) => { if (with) Give(ro[cs], "home_run_burst", 3); });
                for (int i = 0; i < 330 && sim.Phase != PlayPhase.Dead; i++) { sim.Step(default(ControlInput)); }
                return new Tally(sim.Agents[cs].Pos.Y - sim.LosY, 1);
            });
            Report(r);
            Assert.Greater(r.Delta, 0.05, "more ground in the same time once he is past the line");
        }

        [Test]
        public void StopOnADimeKeepsHisSpeedThroughACut()
        {
            Play off = SimTestKit.Offense("Inside Zone Right"), def = Cover("Cover 3 Sky");
            PlaySimulation plainSim = Build(off, def, 75, 75, 3, (ro, rd, of, df) => { ro[1] = SimTestKit.MakePlayer(new Rng(4), Position.RB, 85); });
            PlaySimulation dimeSim = Build(off, def, 75, 75, 3, (ro, rd, of, df) => { ro[1] = SimTestKit.MakePlayer(new Rng(4), Position.RB, 85); Give(ro[1], "stop_on_a_dime", 3); });
            Assert.AreEqual(1f - 0.30f * AbilityFx.Scale(SimHook.CutSpeedLoss), dimeSim.Agents[1].Body.CutBleed, 0.001f, "-30 percent speed lost in a cut at tier 3, times the Sim gain");
            Assert.AreEqual(1f, dimeSim.Agents[2].Body.CutBleed, 0.001f, "only the player who has it");
            Assert.AreEqual(1f, plainSim.Agents[1].Body.CutBleed, 0.001f);
            float[] speed = new float[2], across = new float[2];
            AgentBody[] bodies = { plainSim.Agents[1].Body, dimeSim.Agents[1].Body };
            for (int k = 0; k < 2; k++)
            {
                AgentBody b = bodies[k];
                b.Vel = new Vec2(0f, 8.5f); b.Facing = new Vec2(0f, 1f);
                for (int i = 0; i < 24; i++) b.Step(1f / 60f, new Vec2(7f, -1f), true, new Vec2(1f, 0f), 0f);   // a sharp cut back across the field
                speed[k] = b.Vel.Length; across[k] = b.Vel.Dot(new Vec2(7f, -1f).Norm());
            }
            TestContext.WriteLine("ABILITY stop_on_a_dime t3 0.4 s after a hard cut: speed " + speed[0].ToString("0.00") + " -> " + speed[1].ToString("0.00") + " yd/s, along the new line " + across[0].ToString("0.00") + " -> " + across[1].ToString("0.00"));
            Assert.Greater(speed[1], speed[0] + 0.01f, "he comes out of the cut faster");
            Assert.GreaterOrEqual(across[1], across[0], "and has more of it going the new way");
        }

        [Test]
        public void WorkhorseTiresMoreSlowly()
        {
            Result r = Pair("workhorse t3 energy burned by a 6-second run", 30, 25000, (seed, with) =>
            {
                Play[] runs = Offense("Outside Zone");
                int cs = CarrierSlot(runs[seed % runs.Length]);
                PlaySimulation sim = OpenFieldRun(seed, (ro, rd, of, df) => { if (with) Give(ro[cs], "workhorse", 3); }, "Outside Zone");
                for (int i = 0; i < 360 && sim.Phase != PlayPhase.Dead; i++) sim.Step(default(ControlInput));
                return new Tally(1f - sim.Agents[cs].Body.Energy, 1);
            });
            Report(r);
            Assert.Less(r.With.Rate, r.Base.Rate * 0.85, "25 percent less energy burned");
        }

        [Test]
        public void ShortYardageBruiserConvertsThirdAndOne()
        {
            Play[] runs = Offense("Dive").Concat(Offense("Iso")).Concat(Offense("Power")).ToArray();
            GameState ThirdAndOne() { GameState gs = SimTestKit.Scrimmage(RuleSet.Pro(), 45, 3, 1); gs.Quarter = 2; gs.ClockSeconds = 500f; return gs; }
            Result r = Pair("short_yardage_bruiser t3 third-and-1 conversion", 2400, 26000, (seed, with) =>
            {
                Play off = runs[seed % runs.Length];
                int cs = CarrierSlot(off);
                PlaySimulation sim = Build(off, Cover("Cover 1 Man Free"), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[cs], "short_yardage_bruiser", 3); }, ThirdAndOne());
                PlayResult res = sim.RunToCompletion();
                return res.Penalty != null ? new Tally(0, 0) : new Tally(res.Yards >= 1 ? 1 : 0, 1);
            });
            Report(r);
            Assert.Greater(r.Delta, 0.03, "he moves the pile");
            Result wide = Pair("short_yardage_bruiser t3 yards per carry on first-and-10 (control)", 800, 26500, (seed, with) =>
            {
                Play off = runs[seed % runs.Length];
                int cs = CarrierSlot(off);
                return RushYards(Build(off, Cover("Cover 1 Man Free"), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[cs], "short_yardage_bruiser", 3); }));
            });
            Report(wide);
            Assert.AreEqual(wide.Base.Rate, wide.With.Rate, 0.001, "the effect is the situation's, not the player's");
        }
    }
}
