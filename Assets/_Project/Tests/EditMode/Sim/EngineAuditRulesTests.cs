using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;
using Team = PixFootball.Core.Team;

namespace PixFootball.Tests
{
    /// <summary>Second batch of audit-fix tests: coverage, tackling and bounds in the simulation, plus the rule and playbook fixes in Core.</summary>
    public class EngineAuditRulesTests
    {
        private static PlaySimOptions Quiet() { return new PlaySimOptions { Penalties = false }; }

        // ------------------------------------------------------------------ coverage, tackling, bounds

        [Test]
        public void ManCornersHoldTheirCushionAtTheSnap()
        {
            for (int seed = 0; seed < 12; seed++)
            {
                var sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 1 Man Free", "Nickel", 75, 75, 700 + seed, RuleSet.Pro(), null, Quiet());
                SimTestKit.StepToLive(sim);
                SimTestKit.Steps(sim, 30);   // half a second
                for (int i = 11; i < 22; i++)
                {
                    SimAgent a = sim.Agents[i];
                    if (a.PlayPos != Position.CB) continue;
                    Assert.That(a.Pos.Y - sim.LosY, Is.GreaterThan(3.0f), a.Label + " must not charge the line: he plays in front of his man, not behind him");
                }
            }
        }

        [Test]
        public void DefendersDoNotDiveThroughBlockers()
        {
            int early = 0, n = 0;
            for (int seed = 0; seed < 120; seed++)
            {
                var sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 4000 + seed, RuleSet.Pro(), null, Quiet());
                sim.RunToCompletion();
                n++;
                foreach (var e in sim.Log.Events) if (e.Type == SimEventType.Tackle && e.Time < 0.9f) early++;
            }
            Assert.AreEqual(0, early, "no tackle happens before the back has the ball and a hole (" + n + " plays)");
        }

        [Test]
        public void ContactImpulsesAreSpreadOverSeveralTicks()
        {
            float worst = 0f;
            for (int seed = 0; seed < 25; seed++)
            {
                var sim = SimTestKit.RunNamed("Power Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 5000 + seed, RuleSet.Pro(), null, Quiet());
                SimTestKit.StepToLive(sim);
                var prev = new Vec2[22];
                for (int i = 0; i < 22; i++) prev[i] = sim.Agents[i].Vel;
                int guard = 0;
                while (sim.Phase == PlayPhase.Live && guard++ < 900)
                {
                    sim.Step(default(ControlInput));
                    for (int i = 0; i < 22; i++)
                    {
                        float dv = (sim.Agents[i].Vel - prev[i]).Length;
                        if (sim.Agents[i].Move == CarrierMove.Dive) { prev[i] = sim.Agents[i].Vel; continue; }   // a dive is a deliberate lunge
                        if (dv > 1.2f) TestContext.WriteLine("seed " + seed + " t=" + sim.Time.ToString("0.00") + " agent " + i + " " + sim.Agents[i].Label + " dv=" + dv.ToString("0.00") + " state=" + sim.Agents[i].State + " action=" + sim.Agents[i].Action + " fallen=" + sim.Agents[i].Body.FallenTime.ToString("0.00") + " v=" + sim.Agents[i].Vel + " prev=" + prev[i]);
                        if (dv > worst) worst = dv;
                        prev[i] = sim.Agents[i].Vel;
                    }
                }
            }
            Assert.That(worst, Is.LessThan(PlaySimulation.MaxVelocityStep + 0.6f), "no player changes velocity by more than 1.25 yd/s in one tick (plus his own acceleration)");
        }

        [Test]
        public void NobodyLeavesTheStadium()
        {
            string[] plays = { "Four Verticals", "Fade", "Inside Zone Right", "Toss Sweep Right" };
            foreach (string name in plays)
            {
                Play off = null;
                foreach (Play p in SimTestKit.Playbook.AllPlays) if (p.Side == PlaySide.Offense && p.Name == name) { off = p; break; }
                if (off == null) continue;
                for (int seed = 0; seed < 8; seed++)
                {
                    Play def = SimTestKit.Playbook.PlaysFor(DefaultPlaybooks.Nickel).First(p => p.Name == "Cover 1 Man Free");
                    var sim = SimTestKit.Run(off, def, 75, 75, 6000 + seed, RuleSet.Pro(), null, Quiet());
                    int guard = 0;
                    SimTestKit.StepToLive(sim);
                    while (sim.Phase == PlayPhase.Live && guard++ < 1500)
                    {
                        sim.Step(default(ControlInput));
                        for (int i = 0; i < 22; i++)
                        {
                            Assert.That(sim.Agents[i].Pos.X, Is.InRange(-4.5f, 58f), name + " seed " + seed + " agent " + i + " X");
                            Assert.That(sim.Agents[i].Pos.Y, Is.InRange(-15f, 115f), name + " seed " + seed + " agent " + i + " Y");
                        }
                    }
                }
            }
        }

        [Test]
        public void PlayerCannotWalkThroughAFallenBody()
        {
            var sim = SimTestKit.RunNamed("Inside Zone Right", "I-Form", "Cover 3 Sky", "4-3 Base", 75, 75, 9, RuleSet.Pro(), null, Quiet());
            SimTestKit.StepToLive(sim);
            SimTestKit.ParkAll(sim, 40f);
            SimAgent lying = sim.Agents[13], walker = sim.Agents[16];
            SimTestKit.Place(lying, 20f, 60f);
            lying.Body.Fall(3f);
            SimTestKit.Place(walker, 20f, 64f);
            float minDist = 99f;
            for (int i = 0; i < 90; i++)
            {
                walker.Want = new Vec2(0f, -walker.Body.EffectiveVMax);
                sim.Step(default(ControlInput));
                lying.Body.Fall(3f);
                minDist = Math.Min(minDist, (walker.Pos - lying.Pos).Length);
                if (sim.Phase != PlayPhase.Live) break;
            }
            Assert.That(minDist, Is.GreaterThan(0.45f), "a downed player is an obstacle, nobody runs straight through him");
        }

        // ------------------------------------------------------------------ rules (Core)

        private static GameState Start(RuleSet rules = null)
        {
            var g = new GameState(rules ?? RuleSet.Pro());
            Assert.IsTrue(g.Apply(PlayResult.Kickoff(70, touchback: true)));
            return g;
        }

        private static GameState CollegeOt()
        {
            var g = Start(RuleSet.College());
            g.Quarter = 4; g.ClockSeconds = 1f;
            g.Apply(PlayResult.Run(1, 5f));
            return g;
        }

        [Test]
        public void CollegeOvertimeDefensiveTouchdownOnTheFirstPossessionEndsTheGame()
        {
            var g = CollegeOt();
            Assert.AreEqual(5, g.Quarter);
            g.Apply(PlayResult.Intercept(5, 60, true));
            Assert.AreEqual(GamePhase.Final, g.Phase);
            Assert.AreEqual(Team.Away, g.Winner);
        }

        [Test]
        public void CollegeOvertimeWinningTouchdownOnTheSecondPossessionSkipsTheExtraPoint()
        {
            var g = CollegeOt();
            g.BallSpot = 80; g.Down = 4; g.Distance = 10;
            g.Apply(PlayResult.FieldGoal(true));
            Assert.AreEqual(Team.Away, g.Possession);
            g.Apply(PlayResult.Run(25));   // touchdown that already wins the game
            Assert.AreEqual(GamePhase.Final, g.Phase);
            Assert.AreEqual(Team.Away, g.Winner);
        }

        [Test]
        public void OffensiveFoulInTheLastMinuteRunsTenSecondsOffTheClock()
        {
            var g = Start();
            g.Quarter = 4; g.ClockSeconds = 50f; g.HomeScore = 14; g.AwayScore = 10;
            g.TwoMinuteWarningSecondHalf = true;
            g.PendingRunoff = 0f; g.ClockRunning = true;
            float before = g.ClockSeconds;
            g.Apply(PlayResult.PenaltyOnly(new PenaltyCall(PenaltyType.FalseStart, g.Possession)));
            Assert.That(before - g.ClockSeconds, Is.GreaterThanOrEqualTo(9.5f), "ten seconds come off for an offensive foul with the clock running");
        }

        [Test]
        public void OffsettingFoulsReplayTheDownWithoutYardage()
        {
            var g = Start();
            int spot = g.BallSpot, down = g.Down;
            PlayResult r = PlayResult.Run(8, 5f);
            r.Penalty = new PenaltyCall(PenaltyType.OffensiveHolding, g.Possession, 3f);
            r.OffsettingPenalty = new PenaltyCall(PenaltyType.DefensiveHolding, g.Possession.Opponent(), 3f);
            var choice = PenaltyEnforcer.Describe(g, r);
            Assert.IsTrue(choice.Offsetting);
            Assert.IsTrue(g.Apply(r));
            Assert.AreEqual(spot, g.BallSpot, "no yards either way");
            Assert.AreEqual(down, g.Down, "the down is replayed");
        }

        [Test]
        public void KickingTeamRecoveryInTheEndZoneScoresATouchdownOrASafety()
        {
            var td = Start();
            td.Down = 4; td.Distance = 8; td.BallSpot = 60;
            PlayResult muff = PlayResult.Punt(45, 0);
            muff.RecoveredByKickingTeam = true;
            td.Apply(muff);
            Assert.GreaterOrEqual(td.HomeScore, 6, "the kicking team recovers a muffed punt in the end zone: touchdown");

            var sf = Start();
            sf.Down = 4; sf.Distance = 8; sf.BallSpot = 3;
            PlayResult blocked = PlayResult.Punt(-10, 0);
            blocked.RecoveredByKickingTeam = true;
            sf.Apply(blocked);
            Assert.AreEqual(2, sf.AwayScore, "a blocked punt recovered behind the kicking team's own goal line is a safety");
        }

        [Test]
        public void FoulOnAVoluntaryTwoPointTryIsMeasuredFromTheTwoPointSpot()
        {
            var g = Start();
            g.Apply(PlayResult.Run(70));   // touchdown
            Assert.AreEqual(GamePhase.PointAfter, g.Phase);
            var call = new PenaltyCall(PenaltyType.Offside, g.Possession.Opponent());
            var er = PenaltyEnforcer.Enforce(g.Clone(), call, PlayResult.TwoPointTry(false));
            Assert.AreEqual(g.Rules.TwoPointSpot + 1, er.NewSpot, "half the distance to the goal from the two-yard line is one yard");
        }

        [Test]
        public void DeadBallFoulsHaveNothingToDecline()
        {
            var g = Start();
            var choice = PenaltyEnforcer.Describe(g, PlayResult.PenaltyOnly(new PenaltyCall(PenaltyType.Offside, g.Possession.Opponent())));
            Assert.IsTrue(choice.DeadBall);
            Assert.IsTrue(choice.RecommendAccept, "a pre-snap foul is always enforced");
        }

        [Test]
        public void OvertimeCoinTossGivesBothTeamsTheBallAcrossSeeds()
        {
            int home = 0, away = 0;
            for (int s = 1; s <= 40; s++)
            {
                var g = new GameState(RuleSet.Pro());
                Team t = g.TossForOvertime(new Rng((ulong)(s * 7919)));
                if (t == Team.Home) home++; else away++;
                Assert.AreEqual(t, g.OvertimeReceiverDefault);
            }
            Assert.That(home, Is.InRange(8, 32));
            Assert.That(away, Is.InRange(8, 32));
        }

        [Test]
        public void CpuTeamsUseTimeoutsInTheTwoMinuteDrill()
        {
            var g = Start();
            g.Quarter = 4; g.ClockSeconds = 100f; g.HomeScore = 14; g.AwayScore = 7;
            g.TwoMinuteWarningSecondHalf = true;
            g.Apply(PlayResult.Run(3, 5f));
            Assert.IsTrue(g.ClockRunning);
            Assert.IsTrue(PlayCaller.WantsTimeout(g, Team.Away, null), "the trailing defense stops the clock");
            Assert.IsFalse(PlayCaller.WantsTimeout(g, Team.Home, null), "the leading team lets it run");
            g.ClockSeconds = 400f;
            Assert.IsFalse(PlayCaller.WantsTimeout(g, Team.Away, null), "not before the last two minutes");
        }

        [Test]
        public void CpuHurryUpWindowMatchesTheQuickSimWindow()
        {
            var g = Start();
            g.Quarter = 4; g.HomeScore = 7; g.AwayScore = 10;
            g.ClockSeconds = 200f;
            Assert.IsTrue(PlayCaller.IsHurryUp(g), "trailing with 3:20 left hurries");
            g.ClockSeconds = 330f;
            Assert.IsFalse(PlayCaller.IsHurryUp(g));
        }

        [Test]
        public void KneelNeverTakesAnOffenseIntoItsOwnEndZone()
        {
            var g = Start();
            g.Quarter = 4; g.ClockSeconds = 90f; g.HomeScore = 14; g.AwayScore = 10; g.BallSpot = 1;
            g.AwayTimeouts = 0;
            Assert.IsFalse(PlayCaller.ShouldKneel(g));
        }

        [Test]
        public void SpikeIsNotCalledWhenTheClockIsAlreadyStopped()
        {
            var g = Start();
            g.Quarter = 4; g.ClockSeconds = 25f; g.HomeScore = 7; g.AwayScore = 10;
            g.Apply(PlayResult.Incomplete());
            Assert.IsFalse(g.ClockRunning);
            Assert.IsFalse(PlayCaller.ShouldSpike(g));
        }

        // ------------------------------------------------------------------ playbooks (Core)

        [Test]
        public void ShippedPlaybookHasNoIllegalFormationsOrOutOfBoundsRoutes()
        {
            var errors = new List<string>();
            Assert.IsTrue(SimTestKit.Playbook.Validate(errors), string.Join("\n", errors.Take(12)));
        }

        [Test]
        public void LegalityCheckCatchesSixOnTheLine()
        {
            var book = DefaultPlaybooks.Create();
            Formation f = book.FindFormation(DefaultPlaybooks.IForm);
            Assert.NotNull(f);
            var slots = (SlotSpec[])f.Slots.Clone();
            // take the tight end and every receiver off the line, and one lineman back: fewer than seven on the line
            for (int i = 0; i < slots.Length; i++) if (slots[i].Position == Position.LT) slots[i] = new SlotSpec(slots[i].Position, slots[i].X, -2.5f, slots[i].Label);
            for (int i = 0; i < slots.Length; i++) if (slots[i].Position == Position.TE) slots[i] = new SlotSpec(slots[i].Position, slots[i].X, -2.5f, slots[i].Label);
            var bad = new Formation("Broken", f.Side, f.Unit, slots);
            book.Formations.Add(bad);
            var errors = new List<string>();
            book.Validate(errors);
            Assert.That(errors.Any(e => e.Contains("Broken")), "a formation with fewer than seven on the line is reported: " + string.Join(" | ", errors.Take(5)));
        }

        [Test]
        public void DefaultAudiblesFollowTheDirectionOfTheCall()
        {
            var book = new AudibleBook(SimTestKit.Playbook);
            Play left = null;
            foreach (Play p in SimTestKit.Playbook.AllPlays) if (p.Side == PlaySide.Offense && p.Name == "Inside Zone Left") { left = p; break; }
            if (left == null) Assert.Inconclusive("no Inside Zone Left");
            var set = book.For(left);
            foreach (Play p in set) if (p != null && p.Name.EndsWith(" Right")) Assert.Fail("a Left call audibles to " + p.Name);
        }

        [Test]
        public void AudibleSlotsRejectBadInput()
        {
            var book = new AudibleBook(SimTestKit.Playbook);
            Play any = SimTestKit.Offense("Four Verticals", "Shotgun");
            Play other = SimTestKit.Offense("Inside Zone Right", "I-Form");
            book.Set(any.FormationName, 99, any.Id);   // bad slot: ignored, no exception
            book.Set(any.FormationName, 1, other.Id);  // other formation: ignored
            Assert.AreNotEqual(other.Id, book.Get(any, 1).Id);
        }

        [Test]
        public void MergeDoesNotDoublePlays()
        {
            var a = DefaultPlaybooks.Create();
            int before = a.AllPlays.Count();
            a.Merge(DefaultPlaybooks.Create());
            Assert.AreEqual(before, a.AllPlays.Count());
            var errors = new List<string>();
            Assert.IsTrue(a.Validate(errors), string.Join("\n", errors.Take(5)));
        }

        [Test]
        public void FindPlayByNameCanBeScopedToAFormation()
        {
            Play a = SimTestKit.Playbook.FindPlay("Cover 3 Sky", DefaultPlaybooks.Nickel);
            Assert.NotNull(a);
            Assert.AreEqual(DefaultPlaybooks.Nickel, a.FormationName);
            Play b = SimTestKit.Playbook.FindPlay("Cover 3 Sky", DefaultPlaybooks.Base34);
            Assert.AreEqual(DefaultPlaybooks.Base34, b.FormationName);
        }

        [Test]
        public void BlitzCallsBringTheRushersTheirNamesPromise()
        {
            Func<string, string, int> rushers = (name, form) =>
            {
                Play p = SimTestKit.Defense(name, form);
                int n = 0;
                foreach (Assignment a in p.Assignments) if (a is RushAssignment || a is BlitzAssignment) n++;
                return n;
            };
            Assert.AreEqual(5, rushers("Cover 3 Fire Zone", "4-3 Base"), "a fire zone brings five");
            Assert.AreEqual(5, rushers("Cover 3 Fire Zone", "3-4 Base"), "a fire zone brings five");
            Assert.AreEqual(6, rushers("Cover 0 Blitz", "4-3 Base"));
            Assert.AreEqual(6, rushers("Cover 0 Blitz", "Nickel"), "the nickel version sends both backers (it has no third)");
            // the cross blitz sends the two inside backers through the OPPOSITE gaps
            Play cross = SimTestKit.Defense("LB Cross Blitz Cover 1", "3-4 Base");
            var gaps = new List<RunGap>();
            foreach (Assignment a in cross.Assignments) { var b = a as BlitzAssignment; if (b != null) gaps.Add(b.Gap); }
            Assert.Contains(RunGap.ARight, gaps);
            Assert.Contains(RunGap.BLeft, gaps);
            // the zone dog has one blitzer per gap: no gap used twice
            Play dog = SimTestKit.Defense("Zone Blitz Dog", "3-4 Base");
            var dogGaps = new List<RunGap>();
            foreach (Assignment a in dog.Assignments) { var bb = a as BlitzAssignment; if (bb != null) dogGaps.Add(bb.Gap); }
            Assert.AreEqual(dogGaps.Count, dogGaps.Distinct().Count());
            Assert.GreaterOrEqual(SimTestKit.Playbook.PlaysFor("3-4 Base").Count(p => p.Side == PlaySide.Defense), 13, "the 3-4 has real variety now (it had 8)");
        }

        [Test]
        public void FourthDownRespectsTheKickersRange()
        {
            var g = Start();
            g.Quarter = 2; g.ClockSeconds = 600f; g.Down = 4; g.Distance = 8; g.BallSpot = 62;   // a 55-yard attempt
            int punts = 0, fgs = 0;
            for (int s = 0; s < 80; s++)
            {
                var weak = new PlayCaller(SimTestKit.Playbook, new Rng((ulong)(s + 1)));
                if (weak.FourthDown(g, CoachProfile.Neutral, 44) == PlayCaller.FourthCall.FieldGoal) fgs++; else punts++;
            }
            Assert.AreEqual(0, fgs, "a kicker good to 44 yards is not sent out from the 38");
            var strong = new PlayCaller(SimTestKit.Playbook, new Rng(5));
            Assert.AreEqual(PlayCaller.FourthCall.FieldGoal, strong.FourthDown(g, CoachProfile.Neutral, 60));
        }

        [Test]
        public void CollegePassInterferenceIsAFlatFifteenAndFoulSpotsRoundAwayFromZero()
        {
            var dpi = Penalty.Get(PenaltyType.DefensivePassInterference);
            Assert.AreEqual(6, dpi.YardsFor(RuleSet.Pro(), 6f), "pro DPI is a spot foul");
            Assert.AreEqual(15, dpi.YardsFor(RuleSet.College(), 6f), "college DPI is a flat 15");
            Assert.AreEqual(3, dpi.YardsFor(RuleSet.Pro(), 2.5f), "2.5 rounds to 3, not to the even 2");
            Assert.AreEqual(4, dpi.YardsFor(RuleSet.Pro(), 3.5f));
        }

        [Test]
        public void KickoffOffsideIsBlamedOnTheKickingTeam()
        {
            var g = new GameState(RuleSet.Pro());
            Assert.AreEqual(GamePhase.Kickoff, g.Phase);
            PenaltyCall c = PenaltyCall.For(PenaltyType.Offside, g);
            Assert.AreEqual(g.Possession, c.CommittedBy, "the kicking team is the one that is offside on a kickoff");
        }

        [Test]
        public void DefensiveCoordinatorSchemeSteersTheBaseFront()
        {
            var g = Start();
            int threeFour = 0, fourThree = 0;
            for (int i = 0; i < 200; i++)
            {
                var caller = new PlayCaller(SimTestKit.Playbook, new Rng((ulong)(100 + i)));
                var c34 = CoachProfile.Neutral; c34.FrontBias = 1f;
                var c43 = CoachProfile.Neutral; c43.FrontBias = -1f;
                if (caller.PickDefensiveFormation(g, c34, "21", DefaultPlaybooks.IForm) == DefaultPlaybooks.Base34) threeFour++;
                if (caller.PickDefensiveFormation(g, c43, "21", DefaultPlaybooks.IForm) == DefaultPlaybooks.Base34) fourThree++;
            }
            Assert.Greater(threeFour, fourThree + 60, "a 3-4 coordinator lines up in the 3-4 far more often than a 4-3 coordinator (" + threeFour + " vs " + fourThree + " of 200)");
        }

        [Test]
        public void CoachSchemeMapsToPlayStyleByName()
        {
            Assert.AreEqual(PlayStyle.AirRaid, PhysicalGameSim.StyleOf(OffenseScheme.AirRaid));
            Assert.AreEqual(PlayStyle.WestCoast, PhysicalGameSim.StyleOf(OffenseScheme.WestCoast));
            Assert.AreEqual(PlayStyle.PowerRun, PhysicalGameSim.StyleOf(OffenseScheme.PowerRun));
            Assert.AreEqual(PlayStyle.SpreadOption, PhysicalGameSim.StyleOf(OffenseScheme.SpreadOption));
            Assert.AreEqual(PlayStyle.Balanced, PhysicalGameSim.StyleOf(OffenseScheme.Balanced));
        }

        [Test]
        public void PunterLandsInsideTheUprightsOnlyWhenTheKickIsNotAbsurdlyHigh()
        {
            Assert.IsTrue(GoalPost.IsGood(PixFootball.Core.FieldGeometry.CenterX, 6f));
            Assert.IsFalse(GoalPost.IsGood(PixFootball.Core.FieldGeometry.CenterX, 20f), "above the 15-yard uprights is no good (the old 36-yard limit let a 60-foot-high kick count)");
        }

        // ------------------------------------------------------------------ rotation, hail mary

        [Test]
        public void TiredStartersSitForRestedBackups()
        {
            var league = LeagueTestUtil.FreshPro(11);
            PixFootball.League.Team team = league.Teams[0];
            Formation f = SimTestKit.Playbook.FindFormation(DefaultPlaybooks.Base43);
            Player[] baseline = SimRosters.ForFormation(team, f, new FatigueTracker());
            var tired = new FatigueTracker();
            foreach (Player p in baseline) if (p != null && p.Position == Position.DT) tired.Set(p.Id, 0.2f);
            Player[] rotated = SimRosters.ForFormation(team, f, tired);
            int changed = 0;
            for (int i = 0; i < baseline.Length; i++) if (baseline[i] != null && rotated[i] != null && baseline[i].Id != rotated[i].Id) changed++;
            Assert.GreaterOrEqual(changed, 1, "at least one gassed tackle was replaced");
            for (int i = 0; i < baseline.Length; i++)
                if (baseline[i] != null && baseline[i].Position == Position.QB) Assert.AreEqual(baseline[i].Id, rotated[i].Id, "quarterbacks never rotate");
        }

        [Test]
        public void HailMaryReceiversConvergeOnTheLandingZone()
        {
            Play hm = null;
            foreach (Play p in SimTestKit.Playbook.AllPlays) if (p.Side == PlaySide.Offense && p.Name == "Hail Mary") { hm = p; break; }
            Assert.NotNull(hm);
            var gs = SimTestKit.Scrimmage(RuleSet.Pro(), 55, 1, 10);
            var sim = SimTestKit.Run(hm, SimTestKit.Defense("Cover 3 Sky", "4-3 Base"), 75, 75, 31, RuleSet.Pro(), gs, Quiet());
            float spread0 = 0f, spread1 = 0f;
            for (int i = 7; i <= 10; i++) spread0 += Math.Abs(sim.Agents[i].Pos.X - sim.BallX0);
            SimTestKit.StepToLive(sim);
            SimTestKit.Steps(sim, 240);   // 4 s
            for (int i = 7; i <= 10; i++) spread1 += Math.Abs(sim.Agents[i].Pos.X - sim.BallX0);
            Assert.Less(spread1, spread0 * 0.92f, "the receivers gather toward the landing zone (they used to run four parallel go routes)");
        }
    }
}
