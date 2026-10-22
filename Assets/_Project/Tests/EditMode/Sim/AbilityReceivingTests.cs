using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;
using static PixFootball.Tests.AbilityLab;

namespace PixFootball.Tests
{
    /// <summary>v0.8.0 ability tests, part 2: receivers, returners, kickers, clutch (see <see cref="SimAbilityTests"/>).</summary>
    public class SimAbilityReceivingTests
    {
        private static Play Man() { return SimTestKit.Defense("Cover 1 Man Free", "4-3 Base"); }
        private static Play Zone(int i) { return SimTestKit.Defense(new[] { "Cover 2 Zone", "Cover 3 Sky", "Cover 4 Quarters" }[i % 3], "4-3 Base"); }
        private static void Report(Result r) { TestContext.WriteLine("ABILITY " + r); }

        private static int FirstRouteSlot(Play p, RouteKind? only = null)
        {
            for (int i = 1; i < 11; i++)
            {
                var ra = p.Assignments[i] as RouteAssignment;
                if (ra != null && (!only.HasValue || ra.Route == only.Value)) return i;
            }
            return -1;
        }

        private static int ReadSlot(Play p, int order)
        {
            for (int i = 1; i < 11; i++)
            {
                var ra = p.Assignments[i] as RouteAssignment;
                if (ra != null && ra.ReadOrder == order) return i;
            }
            return FirstRouteSlot(p);
        }

        /// <summary>Runs the route with the quarterback holding the ball and returns the receiver's separation from his man (and his depth) at the given time.</summary>
        private static Tally Separation(PlaySimulation sim, int slot, float seconds, bool depth = false, bool lead = false)
        {
            int guard = 0;
            while (sim.Phase != PlayPhase.Dead && (sim.Phase == PlayPhase.PreSnap || sim.Time < seconds) && guard++ < 4000) sim.Step(default(ControlInput));
            SimAgent r = sim.Agents[slot];
            if (depth) return new Tally(r.Pos.Y - sim.LosY, 1);
            // separation: how far the nearest defender is from him, and for a vertical route how far his man trails him downfield
            float nearest = 99f; SimAgent man = null;
            for (int i = 11; i < 22; i++)
            {
                SimAgent a = sim.Agents[i];
                float dd = (a.Pos - r.Pos).Length;
                if (dd < nearest) nearest = dd;
                if (a.CoverTarget == r.Id) man = a;
            }
            if (lead) return man == null ? new Tally(0, 0) : new Tally(r.Pos.Y - man.Pos.Y, 1);
            return new Tally(nearest, 1);
        }

        private static PlaySimOptions Hold() { return new PlaySimOptions { Penalties = false, QbHoldsBall = true }; }

        [Test]
        public void BurnerGetsBehindCornersOnGoRoutes()
        {
            Play[] plays = Offense("Four Verticals");
            Result sep = Pair("burner t3 yards his man trails him downfield at 2.6 s on a go route", 500, 30000, (seed, with) =>
            {
                Play off = plays[seed % plays.Length];
                int slot = FirstRouteSlot(off, RouteKind.Go);
                return Separation(Build(off, Man(), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[slot], "burner", 3); }, null, Hold()), slot, 2.6f, false, true);
            });
            Report(sep);
            Assert.Greater(sep.Delta, 0.01);
            Result depth = Pair("burner t3 depth reached at 2.6 s on a go route (yd)", 200, 30500, (seed, with) =>
            {
                Play off = plays[seed % plays.Length];
                int slot = FirstRouteSlot(off, RouteKind.Go);
                return Separation(Build(off, Man(), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[slot], "burner", 3); }, null, Hold()), slot, 2.6f, true);
            });
            Report(depth);
            Assert.Greater(depth.Delta, 0.0);
            Result shortRoute = Pair("burner t3 distance to the nearest defender on a short route (control, yd)", 400, 31000, (seed, with) =>
            {
                Play off = Offense("Slants")[seed % Offense("Slants").Length];
                int slot = ReadSlot(off, 1);
                return Separation(Build(off, Man(), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[slot], "burner", 3); }, null, Hold()), slot, 1.6f);
            });
            Report(shortRoute);
            Assert.AreEqual(shortRoute.Base.Rate, shortRoute.With.Rate, 0.001, "the Deep effects only apply on vertical routes");
        }

        [Test]
        public void CrispRoutesCreateSeparationOnEveryRoute()
        {
            Play[] plays = Offense("Quick Hitches").Concat(Offense("Curl-Flat")).Concat(Offense("Double Outs")).ToArray();
            Result sep = Pair("crisp_routes t3 distance to the nearest defender at 1.9 s on hitch, curl and out routes (yd)", 700, 32000, (seed, with) =>
            {
                Play off = plays[seed % plays.Length];
                int slot = ReadSlot(off, 1);
                return Separation(Build(off, Man(), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[slot], "crisp_routes", 3); }, null, Hold()), slot, 1.9f);
            });
            Report(sep);
            Assert.Greater(sep.Delta, 0.005);
        }

        // ---- catching: staged contests under a ball in the air ----

        private static float CatchChance(string id, int tier, float x, float y, float ballZ, bool defender)
        {
            PlaySimulation sim = Build(SimTestKit.Offense("Four Verticals", "Shotgun"), SimTestKit.Defense("Cover 3 Sky", "4-3 Base"), 75, 75, 41, (ro, rd, of, df) =>
            {
                ro[10].Ratings = Ratings.Uniform(75);
                if (id != null) Give(ro[10], id, tier);
            });
            SimTestKit.StepToLive(sim);
            SimTestKit.ParkAll(sim, 2f);
            SimAgent r = sim.Agents[10];
            SimTestKit.Place(r, x, y);
            r.Body.Facing = new Vec2(0f, -1f);
            SimAgent d = sim.Agents[11];
            SimTestKit.Place(d, x + 0.55f, y + 0.4f);
            if (!defender) SimTestKit.Place(d, 10f, 2f);
            d.Body.Facing = new Vec2(0f, -1f);
            sim.Ball.Launch(BallState.Pass, new Vec3(x, y + 0.3f, ballZ), new Vec3(0f, -10f, 0f), 0);
            return sim.Catches.CatchProbability(r, defender ? d : null);
        }

        [Test]
        public void StickyHandsHoldOnWithADefenderDrapedOnHim()
        {
            float basic = CatchChance(null, 0, 40f, 48f, 1.6f, true), sticky = CatchChance("sticky_hands", 3, 40f, 48f, 1.6f, true);
            float openBasic = CatchChance(null, 0, 40f, 48f, 1.6f, false), openSticky = CatchChance("sticky_hands", 3, 40f, 48f, 1.6f, false);
            TestContext.WriteLine("ABILITY sticky_hands t3 catch chance with a defender on him: " + basic.ToString("0.000") + " -> " + sticky.ToString("0.000") + "; the defender far away: " + openBasic.ToString("0.000") + " -> " + openSticky.ToString("0.000"));
            Assert.Greater(sticky, basic + 0.015f);
        }

        [Test]
        public void FearlessMiddleOnlyHelpsOverTheMiddle()
        {
            float midBasic = CatchChance(null, 0, 27f, 48f, 1.6f, true), mid = CatchChance("fearless_middle", 3, 27f, 48f, 1.6f, true);
            float wideBasic = CatchChance(null, 0, 8f, 48f, 1.6f, true), wide = CatchChance("fearless_middle", 3, 8f, 48f, 1.6f, true);
            TestContext.WriteLine("ABILITY fearless_middle t3 catch chance with a defender on him: over the middle " + midBasic.ToString("0.000") + " -> " + mid.ToString("0.000") + "; near the sideline " + wideBasic.ToString("0.000") + " -> " + wide.ToString("0.000"));
            Assert.Greater(mid, midBasic + 0.01f);
            Assert.AreEqual(wideBasic, wide, 0.0001f, "he gets nothing outside the numbers");
        }

        [Test]
        public void SkyWalkerWinsTheJumpBall()
        {
            float basic = CatchChance(null, 0, 40f, 48f, 2.6f, true), sky = CatchChance("sky_walker", 3, 40f, 48f, 2.6f, true);
            TestContext.WriteLine("ABILITY sky_walker t3 contested catch chance: " + basic.ToString("0.000") + " -> " + sky.ToString("0.000"));
            Assert.Greater(sky, basic + 0.015f);
            // and the duel itself: the whole staged contest, receiver against a corner under the same ball
            Play cover3 = SimTestKit.Defense("Cover 3 Sky", "4-3 Base");
            int cb = SlotOf(SimTestKit.Playbook.FindFormation(cover3.FormationName), Position.CB);
            Result r = Pair("sky_walker t3 jump balls caught (staged duel)", 1500, 33000, (seed, with) =>
                new Tally(JumpBall(seed, cover3, cb, rec => { if (with) Give(rec, "sky_walker", 3); }, null, 2.5f) ? 1 : 0, 1));
            Report(r);
            Assert.Greater(r.Delta, 0.0);
        }

        [Test]
        public void YardsMachineTurnsShortCatchesIntoLongGains()
        {
            Play[] plays = Offense("Slants").Concat(Offense("Quick Hitches")).Concat(Offense("Mesh")).ToArray();
            Result r = Pair("yards_machine t3 yards after the catch", 1500, 34000, (seed, with) =>
            {
                PlaySimulation sim = Build(plays[seed % plays.Length], Zone(seed), 75, 75, seed, (ro, rd, of, df) => { if (with) for (int i = 1; i < 11; i++) if (of.Slots[i].Position == Position.WR || of.Slots[i].Position == Position.TE || of.Slots[i].Position == Position.RB) Give(ro[i], "yards_machine", 3); });
                PlayResult res = sim.RunToCompletion();
                if (res.Type != PlayType.Pass || res.Penalty != null) return new Tally(0, 0);
                return new Tally(res.Yards - (sim.PassCatchY - sim.LosY), 1);
            });
            Report(r);
            Assert.Greater(r.Delta, 0.1);
        }

        // ---- the return game ----

        [Test]
        public void ReturnAceBringsKicksBackFurther()
        {
            Play ko = SimTestKit.Special("Kickoff Deep Middle"), kr = SimTestKit.Special("Kick Return Middle Wedge");
            int ret = Enumerable.Range(0, 11).First(i => kr.Assignments[i] is ReturnerAssignment);
            Result r = Pair("return_ace t3 kickoff return yards", 4000, 35000, (seed, with) =>
            {
                var rules = RuleSet.Pro();
                var gs = new GameState(rules, PixFootball.Core.Team.Away);
                PlaySimulation sim = Build(ko, kr, 75, 75, seed, (ro, rd, of, df) => { if (with) Give(rd[ret], "return_ace", 3); }, gs, null, rules);
                PlayResult res = sim.RunToCompletion();
                if (res.Type != PlayType.Kickoff || res.Touchback || res.Penalty != null) return new Tally(0, 0);
                return new Tally(res.ReturnYards, 1);
            });
            Report(r);
            Assert.Greater(r.Delta, 0.2);
            Play pu = SimTestKit.Special("Punt Standard"), pr = SimTestKit.Special("Punt Return Set");
            int pret = Enumerable.Range(0, 11).First(i => pr.Assignments[i] is ReturnerAssignment);
            Result p = Pair("return_ace t3 punt return yards", 1500, 36000, (seed, with) =>
            {
                var rules = RuleSet.Pro();
                var gs = SimTestKit.Scrimmage(rules, 30, 4, 8);
                PlaySimulation sim = Build(pu, pr, 75, 75, seed, (ro, rd, of, df) => { if (with) Give(rd[pret], "return_ace", 3); }, gs, null, rules);
                PlayResult res = sim.RunToCompletion();
                if (res.Type != PlayType.Punt || res.Touchback || res.Penalty != null || sim.KickBlocked) return new Tally(0, 0);
                return new Tally(res.ReturnYards, 1);
            });
            Report(p);
            Assert.Greater(p.Delta, 0.0);
        }

        // ---- kickers ----

        [Test]
        public void BoomerKicksFarther()
        {
            Play pu = SimTestKit.Special("Punt Standard"), pr = SimTestKit.Special("Punt Return Set");
            int punter = Enumerable.Range(0, 11).First(i => pu.Assignments[i] is KickAssignment && ((KickAssignment)pu.Assignments[i]).Role == KickRole.Kicker);
            Result r = Pair("boomer t3 gross punt yards", 700, 37000, (seed, with) =>
            {
                var rules = RuleSet.Pro();
                PlaySimulation sim = Build(pu, pr, 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[punter], "boomer", 3); }, SimTestKit.Scrimmage(rules, 30, 4, 8), null, rules);
                PlayResult res = sim.RunToCompletion();
                if (res.Type != PlayType.Punt || sim.KickBlocked || res.Penalty != null) return new Tally(0, 0);
                return new Tally(res.Yards, 1);
            });
            Report(r);
            Assert.Greater(r.Delta, 0.8, "six points of leg are more than a yard on every punt");
            Play fg = SimTestKit.Special("Field Goal"), fgb = SimTestKit.Special("Field Goal Block Middle");
            int kicker = Enumerable.Range(0, 11).First(i => fg.Assignments[i] is KickAssignment && ((KickAssignment)fg.Assignments[i]).Role == KickRole.Kicker);
            Result f = Pair("boomer t3 made field goals from 56 yards", 2500, 37500, (seed, with) =>
            {
                var rules = RuleSet.Pro();
                PlaySimulation sim = Build(fg, fgb, 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[kicker], "boomer", 3); }, SimTestKit.Scrimmage(rules, 61, 4, 5), null, rules);
                PlayResult res = sim.RunToCompletion();
                if (res.Type != PlayType.FieldGoal || res.Penalty != null) return new Tally(0, 0);
                return new Tally(res.Good ? 1 : 0, 1);
            });
            Report(f);
            Assert.Greater(f.Delta, 0.008);
        }

        [Test]
        public void IceInTheVeinsKicksTrueWithTheGameOnTheLine()
        {
            Play fg = SimTestKit.Special("Field Goal"), fgb = SimTestKit.Special("Field Goal Block Middle");
            int kicker = Enumerable.Range(0, 11).First(i => fg.Assignments[i] is KickAssignment && ((KickAssignment)fg.Assignments[i]).Role == KickRole.Kicker);
            GameState Late() { var gs = SimTestKit.Scrimmage(RuleSet.Pro(), 66, 4, 5); gs.Quarter = 4; gs.ClockSeconds = 20f; gs.HomeScore = 20; gs.AwayScore = 21; return gs; }
            Result late = Pair("ice_in_veins t3 made field goals from 51 yards, tied game late", 3000, 38000, (seed, with) =>
            {
                PlaySimulation sim = Build(fg, fgb, 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[kicker], "ice_in_veins", 3); }, Late());
                PlayResult res = sim.RunToCompletion();
                if (res.Type != PlayType.FieldGoal || res.Penalty != null) return new Tally(0, 0);
                return new Tally(res.Good ? 1 : 0, 1);
            });
            Report(late);
            Assert.Greater(late.Delta, 0.015);
            Result early = Pair("ice_in_veins t3 made field goals from 51 yards, first quarter (control)", 1500, 38500, (seed, with) =>
            {
                PlaySimulation sim = Build(fg, fgb, 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[kicker], "ice_in_veins", 3); }, SimTestKit.Scrimmage(RuleSet.Pro(), 66, 4, 5));
                PlayResult res = sim.RunToCompletion();
                if (res.Type != PlayType.FieldGoal || res.Penalty != null) return new Tally(0, 0);
                return new Tally(res.Good ? 1 : 0, 1);
            });
            Report(early);
            Assert.AreEqual(early.Base.Rate, early.With.Rate, 0.0001, "no pressure, no edge");
        }

        // ---- everybody ----

        [Test]
        public void ClutchGeneRaisesEveryRatingInACloseFourthQuarter()
        {
            Play off = SimTestKit.Offense("Inside Zone Right"), def = SimTestKit.Defense("Cover 3 Sky", "4-3 Base");
            GameState Close() { var gs = SimTestKit.Scrimmage(RuleSet.Pro(), 40, 1, 10); gs.Quarter = 4; gs.ClockSeconds = 400f; gs.HomeScore = 17; gs.AwayScore = 20; return gs; }
            GameState Blowout() { var gs = Close(); gs.HomeScore = 3; return gs; }
            PlaySimulation plain = Build(off, def, 75, 75, 9, null, Close());
            PlaySimulation clutch = Build(off, def, 75, 75, 9, (ro, rd, of, df) => { Give(ro[0], "clutch_gene", 3); }, Close());
            PlaySimulation blowout = Build(off, def, 75, 75, 9, (ro, rd, of, df) => { Give(ro[0], "clutch_gene", 3); }, Blowout());
            TestContext.WriteLine("ABILITY clutch_gene t3 QB ratings in a close 4th quarter: accuracy " + plain.Qb.R.ThrowAccuracy + " -> " + clutch.Qb.R.ThrowAccuracy + ", awareness " + plain.Qb.R.Awareness + " -> " + clutch.Qb.R.Awareness + "; in a blowout " + blowout.Qb.R.ThrowAccuracy);
            Assert.AreEqual(plain.Qb.R.ThrowAccuracy + 6, clutch.Qb.R.ThrowAccuracy);
            Assert.AreEqual(plain.Qb.R.Awareness + 6, clutch.Qb.R.Awareness);
            Assert.AreEqual(plain.Qb.R.Injury, clutch.Qb.R.Injury);
            Assert.AreEqual(plain.Qb.R.ThrowAccuracy, blowout.Qb.R.ThrowAccuracy, "not when the game is out of reach");
            Play[] plays = Offense("Mesh").Concat(Offense("Slants")).ToArray();
            Result r = Pair("clutch_gene t3 aim error per yard, close fourth quarter", 900, 39000, (seed, with) =>
            {
                PlaySimulation sim = Build(plays[seed % plays.Length], Zone(seed), 75, 75, seed, (ro, rd, of, df) => { if (with) Give(ro[0], "clutch_gene", 3); }, Close());
                sim.RunToCompletion();
                return sim.LastThrowDistance > 0 ? new Tally(sim.LastThrowAimError / sim.LastThrowDistance, 1) : new Tally(0, 0);
            });
            Report(r);
            Assert.Less(r.With.Rate, r.Base.Rate * 0.93);
        }

        [Test]
        public void IronManScalesTheInjuryRoll()
        {
            var p = SimTestKit.MakePlayer(new Rng(2), Position.RB, 80);
            Assert.AreEqual(1f, PhysicalGameSim.InjuryScale(p), 0.0001f);
            Give(p, "iron_man", 1); Assert.AreEqual(0.85f, PhysicalGameSim.InjuryScale(p), 0.0001f);
            Give(p, "iron_man", 3); Assert.AreEqual(0.65f, PhysicalGameSim.InjuryScale(p), 0.0001f);
            TestContext.WriteLine("ABILITY iron_man t3 injury chance x" + PhysicalGameSim.InjuryScale(p).ToString("0.00") + " (League Season.RollInjuries applies the same scale to every game line; the match simulation has no in-play injuries)");
        }
    }
}
