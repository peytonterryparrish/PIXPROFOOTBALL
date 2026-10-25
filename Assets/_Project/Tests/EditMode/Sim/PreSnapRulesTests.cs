using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>
    /// v0.12.0 (RULES, master prompt M22 rule tests): pre-snap legality is computed from the alignment and the movement, and the pre-snap fouls are caused by bodies that really move early, a real lineup or a real
    /// clock, never by a roll at the snap. The pure checks (PreSnapRules) and the simulation that feeds them.
    /// </summary>
    public class PreSnapRulesTests
    {
        static readonly RuleSet Pro = RuleSet.Pro();
        // every test names the one cause it wants: the others start at zero, and the tuning is put back after
        float[] _saved;
        [SetUp]
        public void ZeroTheCauses()
        {
            _saved = new[] { PreSnapTuning.JumpOffense, PreSnapTuning.JumpDefense, PreSnapTuning.LineupMistake, PreSnapTuning.SlowSnap, PreSnapTuning.MotionDrift, PreSnapTuning.MotionBoth, PreSnapTuning.ShiftPass, PreSnapTuning.ShiftRun, PreSnapTuning.ShiftGapMean, PreSnapTuning.ShiftGapSpread };
            PreSnapTuning.JumpOffense = PreSnapTuning.JumpDefense = PreSnapTuning.LineupMistake = PreSnapTuning.SlowSnap = PreSnapTuning.MotionDrift = PreSnapTuning.MotionBoth = PreSnapTuning.ShiftPass = PreSnapTuning.ShiftRun = 0f;
        }

        [TearDown]
        public void PutTheTuningBack() { RestoreDefaults(); }

        void RestoreDefaults()
        {
            if (_saved == null) return;
            PreSnapTuning.JumpOffense = _saved[0]; PreSnapTuning.JumpDefense = _saved[1]; PreSnapTuning.LineupMistake = _saved[2]; PreSnapTuning.SlowSnap = _saved[3]; PreSnapTuning.MotionDrift = _saved[4];
            PreSnapTuning.MotionBoth = _saved[5]; PreSnapTuning.ShiftPass = _saved[6]; PreSnapTuning.ShiftRun = _saved[7]; PreSnapTuning.ShiftGapMean = _saved[8]; PreSnapTuning.ShiftGapSpread = _saved[9];
        }


        /// <summary>A line of five linemen at the ball plus the given extra players (position, x, y).</summary>
        static List<Aligned> Set(params object[] extras)
        {
            var l = new List<Aligned>
            {
                new Aligned(2, Position.LT, -2.5f, -0.15f), new Aligned(3, Position.LG, -1.25f, -0.15f), new Aligned(4, Position.C, 0f, -0.15f),
                new Aligned(5, Position.RG, 1.25f, -0.15f), new Aligned(6, Position.RT, 2.5f, -0.15f)
            };
            int id = 7;
            for (int i = 0; i + 2 < extras.Length; i += 3)
                l.Add(new Aligned(id++, (Position)extras[i], (float)extras[i + 1], (float)extras[i + 2]));
            return l;
        }

        static int IndexOf(List<Aligned> l, int id) { return l.FindIndex(a => a.Id == id); }

        // ------------------------------------------------------------------------------------------ alignment
        [Test]
        public void TheLegalStandardSetsOfAll22OffensiveFormationsPass()
        {
            var forms = DefaultPlaybooks.OffensiveFormations();
            Assert.AreEqual(22, forms.Count);
            foreach (RuleSet rules in new[] { RuleSet.Pro(), RuleSet.College() })
                foreach (Formation f in forms)
                {
                    var al = new List<Aligned>();
                    for (int i = 0; i < f.Slots.Length; i++) al.Add(new Aligned(i, f.Slots[i].Position, f.Slots[i].X, f.Slots[i].Y));
                    AlignReport r = PreSnapRules.Analyze(al, rules);
                    Assert.IsTrue(r.Legal, f.Name + " " + rules.Name + ": " + r);
                    Assert.GreaterOrEqual(r.OnLine, 7, f.Name);
                    Assert.LessOrEqual(r.Backfield, 4, f.Name);
                    Assert.IsTrue(PositionGroup.IsEligibleReceiver(al[r.LeftEnd].Pos) && PositionGroup.IsEligibleReceiver(al[r.RightEnd].Pos), f.Name + ": both ends of the line are eligible");
                    // the quarterback and the linemen are never eligible, every back is
                    for (int i = 0; i < al.Count; i++)
                    {
                        if (al[i].Pos == Position.QB || PositionGroup.IsOffensiveLine(al[i].Pos)) Assert.IsFalse(r.Eligible[i], f.Name + " " + al[i].Pos);
                        if (!r.IsOnLine[i] && PositionGroup.IsEligibleReceiver(al[i].Pos)) Assert.IsTrue(r.Eligible[i], f.Name + " back " + al[i].Pos);
                    }
                }
        }

        [Test]
        public void TheSimulationLinesUpEveryStandardFormationLegally()
        {
            float lm = PreSnapTuning.LineupMistake;
            PreSnapTuning.LineupMistake = 0f;
            try
            {
                var plays = new Dictionary<string, Play>();
                foreach (Play p in SimTestKit.Playbook.AllPlays)
                    if (p.Side == PlaySide.Offense && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike && p.Category != PlayCategory.Punt && p.Category != PlayCategory.FieldGoal
                        && p.Category != PlayCategory.PointAfter && p.Category != PlayCategory.Kickoff && p.Category != PlayCategory.Onside && !plays.ContainsKey(p.FormationName)) plays[p.FormationName] = p;
                int checkedForms = 0;
                foreach (Formation f in DefaultPlaybooks.OffensiveFormations())
                {
                    Play off;
                    if (!plays.TryGetValue(f.Name, out off)) continue;
                    var sim = SimTestKit.Run(off, SimTestKit.Defense("Cover 3 Sky", "Nickel"), 75, 75, 11, null, null, new PlaySimOptions { AutoSnap = false });
                    AlignReport r = sim.OffenseAlignment();
                    Assert.IsTrue(r.Legal, f.Name + ": " + r);
                    Assert.AreEqual(MotionIssue.None, sim.OffenseMovement().Issue, f.Name);
                    checkedForms++;
                }
                Assert.GreaterOrEqual(checkedForms, 20, "most formations have a play in the default playbook");
            }
            finally { PreSnapTuning.LineupMistake = lm; }
        }

        [Test]
        public void AnEligibleReceiverCoveredOnTheLineIsIneligible()
        {
            // tight end with a wide receiver on the line outside him: the wide receiver is the end and eligible, the tight end is covered
            var l = Set(Position.TE, 3.75f, 0f, Position.WR, 12f, 0f, Position.WR, -17.5f, 0f);
            AlignReport r = PreSnapRules.Analyze(l, Pro);
            Assert.IsTrue(r.Legal, r.ToString());
            int te = IndexOf(l, 7), wr = IndexOf(l, 8), wl = IndexOf(l, 9);
            Assert.IsTrue(r.Covered[te]); Assert.IsFalse(r.Eligible[te], "covered = ineligible");
            Assert.IsTrue(r.Eligible[wr]); Assert.IsTrue(r.Eligible[wl]);
            // the same tight end with the receiver a yard off the line is the end of the line and eligible
            l = Set(Position.TE, 3.75f, 0f, Position.WR, 12f, -1f, Position.WR, -17.5f, 0f);
            r = PreSnapRules.Analyze(l, Pro);
            Assert.IsTrue(r.Eligible[IndexOf(l, 7)]); Assert.IsFalse(r.Covered[IndexOf(l, 7)]);
            Assert.IsTrue(r.Eligible[IndexOf(l, 8)], "off the line = a back");
        }

        [Test]
        public void SixOnTheLineIsAnIllegalFormation()
        {
            // five linemen and one end: six. The receiver standing a yard off the line is the one who should have been on it
            var l = Set(Position.WR, -17.5f, 0f, Position.WR, 17.5f, -1f, Position.WR, 8f, -1f, Position.QB, 0f, -5f, Position.RB, 2f, -5f);
            AlignReport r = PreSnapRules.Analyze(l, Pro);
            Assert.AreEqual(6, r.OnLine);
            Assert.AreEqual(AlignIssue.TooFewOnLine, r.Issue);
            Assert.IsFalse(r.Legal);
            Assert.AreEqual(8, l[r.Culprit].Id, "the receiver nearest the line (a yard back) lined up wrong");
            // seven is legal
            l = Set(Position.WR, -17.5f, 0f, Position.TE, 3.75f, 0f, Position.WR, 17.5f, -1f, Position.WR, 8f, -1f, Position.QB, 0f, -5f, Position.RB, 2f, -5f);
            Assert.IsTrue(PreSnapRules.Analyze(l, Pro).Legal);
        }

        [Test]
        public void FiveInTheBackfieldIsIllegal()
        {
            // the eleven: five off the line (and so only six on it)
            var l = Set(Position.WR, -17.5f, 0f, Position.QB, 0f, -5f, Position.RB, 2f, -5f, Position.WR, 8f, -1f, Position.WR, 17.5f, -1f, Position.WR, 12f, -1f);
            AlignReport r = PreSnapRules.Analyze(l, Pro);
            Assert.AreEqual(5, r.Backfield);
            Assert.IsFalse(r.Legal);
            // a twelfth man does not hide it: seven on the line and five behind is still too many behind
            l.Add(new Aligned(20, Position.TE, 3.75f, 0f));
            r = PreSnapRules.Analyze(l, Pro);
            Assert.AreEqual(7, r.OnLine); Assert.AreEqual(5, r.Backfield);
            Assert.AreEqual(AlignIssue.TooManyInBackfield, r.Issue);
        }

        [Test]
        public void AnIneligibleLinemanOnTheEndOfTheLineIsIllegal()
        {
            // tackle on the end: the tight end is a yard off the line, so the right tackle is the outermost man
            var l = Set(Position.WR, -17.5f, 0f, Position.WR, -8f, 0f, Position.TE, 3.75f, -1f, Position.QB, 0f, -5f, Position.RB, 2f, -5f);
            AlignReport r = PreSnapRules.Analyze(l, Pro);
            Assert.AreEqual(7, r.OnLine);
            Assert.AreEqual(AlignIssue.IneligibleOnEndOfLine, r.Issue);
            Assert.AreEqual(Position.RT, l[r.Culprit].Pos);
        }

        // ------------------------------------------------------------------------------------------ movement
        static MoveState Still(int id, float settled = 5f, bool shift = false) { return new MoveState { Id = id, Speed = 0f, SettledFor = settled, JoinedShift = shift }; }
        static MoveState Moving(int id, float forward = 0f, bool shift = false) { return new MoveState { Id = id, Speed = 4.5f, ForwardSpeed = forward, JoinedShift = shift }; }

        [Test]
        public void ASimultaneousShiftWithoutTheOneSecondSetIsIllegal()
        {
            // two receivers changed places together and were still for 0.4 s when the ball was snapped
            var p = new List<MoveState> { Still(1), Still(8, 0.4f, true), Still(9, 0.4f, true), Still(10) };
            MotionVerdict v = PreSnapRules.CheckMovement(p, Pro);
            Assert.AreEqual(MotionIssue.IllegalShift, v.Issue);
            Assert.AreEqual(1, v.Culprit);
            // the same shift after a full second is legal, for both rule sets
            p = new List<MoveState> { Still(1), Still(8, 1.2f, true), Still(9, 1.2f, true), Still(10) };
            Assert.AreEqual(MotionIssue.None, PreSnapRules.CheckMovement(p, Pro).Issue);
            Assert.AreEqual(MotionIssue.None, PreSnapRules.CheckMovement(p, RuleSet.College()).Issue);
            // two men still shifting at the snap
            p = new List<MoveState> { Moving(8, 0f, true), Moving(9, 0f, true) };
            Assert.AreEqual(MotionIssue.IllegalShift, PreSnapRules.CheckMovement(p, Pro).Issue);
        }

        [Test]
        public void MotionTowardTheLineAtTheSnapIsIllegalButLateralAndBackwardMotionIsNot()
        {
            Assert.AreEqual(MotionIssue.None, PreSnapRules.CheckMovement(new List<MoveState> { Still(1), Moving(8, 0f), Still(9) }, Pro).Issue, "across the formation");
            Assert.AreEqual(MotionIssue.None, PreSnapRules.CheckMovement(new List<MoveState> { Moving(8, -1.5f) }, Pro).Issue, "away from the line");
            MotionVerdict v = PreSnapRules.CheckMovement(new List<MoveState> { Still(1), Moving(8, 2.0f), Still(9) }, Pro);
            Assert.AreEqual(MotionIssue.IllegalMotion, v.Issue);
            Assert.AreEqual(1, v.Culprit);
            // a second man in motion
            v = PreSnapRules.CheckMovement(new List<MoveState> { Moving(8, 0f), Moving(9, 0f) }, Pro);
            Assert.AreEqual(MotionIssue.IllegalMotion, v.Issue);
        }

        [Test]
        public void ADefenderWhoseFrontIsInTheNeutralZoneIsOffside()
        {
            Assert.IsTrue(PreSnapRules.InNeutralZone(0.5f, 0.34f, Pro), "his front is 0.16 yd from the line");
            Assert.IsFalse(PreSnapRules.InNeutralZone(1.45f, 0.34f, Pro), "a lineman at the usual 1.45 yd");
        }

        // ------------------------------------------------------------------------------------------ the simulation
        static PlaySimulation Counted(int seed, float snapDelay, bool cadence = false)
        {
            return SimTestKit.RunNamed("Inside Zone Right", "Singleback", "Cover 3 Sky", "4-3 Base", 40, 40, seed, null, null,
                new PlaySimOptions { AutoSnap = true, AutoSnapDelay = snapDelay, Cadence = cadence, Penalties = true });
        }

        static void StepPre(PlaySimulation sim, int max = 900) { var none = new ControlInput(); for (int k = 0; k < max && sim.Phase == PlayPhase.PreSnap; k++) sim.Step(none); }

        [Test]
        public void AFalseStartIsABodyThatMovedEarlyBeforeTheFlag()
        {
            float jo = PreSnapTuning.JumpOffense, jd = PreSnapTuning.JumpDefense, lm = PreSnapTuning.LineupMistake;
            PreSnapTuning.JumpOffense = 5f; PreSnapTuning.JumpDefense = 0f; PreSnapTuning.LineupMistake = 0f;
            try
            {
                int seen = 0;
                for (int seed = 1; seed <= 40; seed++)
                {
                    var sim = Counted(seed, 1.0f);
                    Vec2[] start = new Vec2[11];
                    for (int i = 0; i < 11; i++) start[i] = sim.Agents[i].Pos;
                    StepPre(sim);
                    Assert.AreEqual(PlayPhase.Dead, sim.Phase, "somebody always jumps with this hazard");
                    Assert.AreEqual(PenaltyType.FalseStart, sim.Result.Penalty.Type);
                    Assert.AreEqual(Team.Home, sim.Result.Penalty.CommittedBy);
                    int culprit = sim.EarlyMoverId;
                    Assert.GreaterOrEqual(culprit, 0); Assert.Less(culprit, 11);
                    Assert.GreaterOrEqual(sim.EarlyMoveAt, 0f);
                    Assert.Less(sim.EarlyMoveAt, sim.PreSnapTime - 0.15f, "he moved well before the flag");
                    Assert.Less(sim.PreSnapTime, sim.PlannedSnapAt, "the flag came before the planned snap");
                    Assert.Greater(sim.Agents[culprit].Pos.Y - start[culprit].Y, 0.15f, "the culprit's body actually went toward the line");
                    Assert.AreEqual(sim.Agents[culprit].PlayerId, sim.Result.Penalty.PlayerId);
                    seen++;
                }
                Assert.AreEqual(40, seen);
            }
            finally { PreSnapTuning.JumpOffense = jo; PreSnapTuning.JumpDefense = jd; PreSnapTuning.LineupMistake = lm; }
        }

        [Test]
        public void AnOffsideIsADefenderWhoCrossedTheNeutralZoneBeforeTheSnap()
        {
            float jo = PreSnapTuning.JumpOffense, jd = PreSnapTuning.JumpDefense, lm = PreSnapTuning.LineupMistake;
            PreSnapTuning.JumpOffense = 0f; PreSnapTuning.JumpDefense = 6f; PreSnapTuning.LineupMistake = 0f;
            try
            {
                var types = new HashSet<PenaltyType>(); int fouls = 0;
                for (int seed = 1; seed <= 60; seed++)
                {
                    var sim = Counted(seed, 1.0f);
                    StepPre(sim);
                    if (sim.Phase != PlayPhase.Dead) continue;      // a jump that started too late to cross is just a quick get-off
                    fouls++;
                    PenaltyType t = sim.Result.Penalty.Type;
                    Assert.IsTrue(t == PenaltyType.Offside || t == PenaltyType.NeutralZoneInfraction || t == PenaltyType.Encroachment, t.ToString());
                    types.Add(t);
                    Assert.AreEqual(Team.Away, sim.Result.Penalty.CommittedBy);
                    Assert.GreaterOrEqual(sim.EarlyMoverId, 11, "a defender moved first");
                    Assert.Less(sim.Agents[sim.EarlyMoverId].Pos.Y - sim.LosY, 1.45f - 0.5f, "he came across the line of scrimmage");
                }
                Assert.GreaterOrEqual(fouls, 40);
                Assert.IsTrue(types.Contains(PenaltyType.Offside));
            }
            finally { PreSnapTuning.JumpOffense = jo; PreSnapTuning.JumpDefense = jd; PreSnapTuning.LineupMistake = lm; }
        }

        [Test]
        public void ALateJumpThatDoesNotCrossBeforeTheSnapIsNoFoul()
        {
            // a snap 0.12 s after the jump starts: nobody is flagged, the play goes
            float jo = PreSnapTuning.JumpOffense, jd = PreSnapTuning.JumpDefense, lm = PreSnapTuning.LineupMistake;
            PreSnapTuning.JumpOffense = 0f; PreSnapTuning.JumpDefense = 6f; PreSnapTuning.LineupMistake = 0f;
            try
            {
                int live = 0;
                for (int seed = 1; seed <= 30; seed++)
                {
                    var sim = Counted(seed, 0.1f);     // the count is only 0.1 s long: every lead is cut to 0.1 s
                    StepPre(sim);
                    if (sim.Phase == PlayPhase.Live) live++;
                }
                Assert.AreEqual(30, live);
            }
            finally { PreSnapTuning.JumpOffense = jo; PreSnapTuning.JumpDefense = jd; PreSnapTuning.LineupMistake = lm; }
        }

        [Test]
        public void ALineupMistakeIsCaughtByTheComputedLegalityAtTheSnap()
        {
            float lm = PreSnapTuning.LineupMistake, jo = PreSnapTuning.JumpOffense, jd = PreSnapTuning.JumpDefense;
            PreSnapTuning.LineupMistake = 50f; PreSnapTuning.JumpOffense = 0f; PreSnapTuning.JumpDefense = 0f;
            try
            {
                var sim = Counted(5, 0.8f);
                AlignReport r = sim.OffenseAlignment();
                Assert.IsFalse(r.Legal, "everybody on the line stepped off it: " + r);
                StepPre(sim);
                Assert.AreEqual(PlayPhase.Dead, sim.Phase);
                Assert.AreEqual(PenaltyType.IllegalFormation, sim.Result.Penalty.Type);
                Assert.AreEqual(Team.Home, sim.Result.Penalty.CommittedBy);
                Assert.IsTrue(sim.Result.Penalty.Def.PreSnap);
            }
            finally { PreSnapTuning.LineupMistake = lm; PreSnapTuning.JumpOffense = jo; PreSnapTuning.JumpDefense = jd; }
        }

        [Test]
        public void ASlowQuarterbackRunsIntoDelayOfGameOnThePlayClock()
        {
            float ss = PreSnapTuning.SlowSnap, jo = PreSnapTuning.JumpOffense, jd = PreSnapTuning.JumpDefense, lm = PreSnapTuning.LineupMistake;
            PreSnapTuning.SlowSnap = 50f; PreSnapTuning.JumpOffense = 0f; PreSnapTuning.JumpDefense = 0f; PreSnapTuning.LineupMistake = 0f;
            try
            {
                var sim = Counted(9, 0.8f);
                StepPre(sim, 1200);
                Assert.AreEqual(PlayPhase.Dead, sim.Phase);
                Assert.AreEqual(PenaltyType.DelayOfGame, sim.Result.Penalty.Type);
                Assert.AreEqual(PreSnapTuning.PlayClockSlack, sim.PreSnapTime, 0.1f, "the flag falls when the clock reaches zero");
                // the director's own clock: 3 s left means the flag at 3 s
                var sim2 = SimTestKit.RunNamed("Inside Zone Right", "Singleback", "Cover 3 Sky", "4-3 Base", 40, 40, 4, null, null, new PlaySimOptions { AutoSnap = false, Penalties = true });
                float t0 = sim2.PreSnapTime;
                float len = sim2.BeginCount(0.8f, 3.0f, true, false);
                Assert.Greater(len, 3.0f, "a slow quarterback plans past the clock");
                for (int k = 0; k < 400 && sim2.Phase == PlayPhase.PreSnap; k++) sim2.Step(new ControlInput());
                Assert.AreEqual(PenaltyType.DelayOfGame, sim2.Result.Penalty.Type);
                Assert.AreEqual(3.0f, sim2.PreSnapTime - t0, 0.1f);
            }
            finally { PreSnapTuning.SlowSnap = ss; PreSnapTuning.JumpOffense = jo; PreSnapTuning.JumpDefense = jd; PreSnapTuning.LineupMistake = lm; }
        }

        [Test]
        public void AShiftNeedsTheSetAndAHastyQuarterbackBreaksIt()
        {
            float sp = PreSnapTuning.ShiftPass, sr = PreSnapTuning.ShiftRun, sg = PreSnapTuning.ShiftGapMean, sgs = PreSnapTuning.ShiftGapSpread;
            float jo = PreSnapTuning.JumpOffense, jd = PreSnapTuning.JumpDefense, lm = PreSnapTuning.LineupMistake, ss = PreSnapTuning.SlowSnap;
            PreSnapTuning.ShiftPass = 1f; PreSnapTuning.ShiftRun = 1f; PreSnapTuning.JumpOffense = 0f; PreSnapTuning.JumpDefense = 0f; PreSnapTuning.LineupMistake = 0f; PreSnapTuning.SlowSnap = 0f;
            try
            {
                // patient: the quarterback waits 0.55 s after the one second set
                PreSnapTuning.ShiftGapMean = 0.55f; PreSnapTuning.ShiftGapSpread = 0f;
                int shifts = 0;
                for (int seed = 1; seed <= 12; seed++)
                {
                    var sim = SimTestKit.RunNamed("Slants", "Shotgun Spread", "Cover 3 Sky", "Nickel", 75, 75, seed, null, null, new PlaySimOptions { AutoSnap = true, Cadence = true, Penalties = true });
                    StepPre(sim, 1500);
                    Assert.AreEqual(PlayPhase.Live, sim.Phase, "a patient shift is legal (seed " + seed + ")");
                    if (sim.MotionSlot < 0 && sim.PlannedSnapAt > 2.35f) shifts++;      // longer than any plain cadence (1.4-2.2 s): a shift plus the one second set
                }
                Assert.Greater(shifts, 0, "shifts were planned");
                // hasty: he snaps 0.3 s after the shifters stop
                PreSnapTuning.ShiftGapMean = -0.7f;
                int illegal = 0;
                for (int seed = 1; seed <= 12; seed++)
                {
                    var sim = SimTestKit.RunNamed("Slants", "Shotgun Spread", "Cover 3 Sky", "Nickel", 75, 75, seed, null, null, new PlaySimOptions { AutoSnap = true, Cadence = true, Penalties = true });
                    StepPre(sim, 1500);
                    if (sim.Phase == PlayPhase.Dead && sim.Result.Penalty.Type == PenaltyType.IllegalShift) illegal++;
                }
                Assert.Greater(illegal, 0, "a quarterback who snaps before the one-second set draws the illegal shift");
            }
            finally
            {
                PreSnapTuning.ShiftPass = sp; PreSnapTuning.ShiftRun = sr; PreSnapTuning.ShiftGapMean = sg; PreSnapTuning.ShiftGapSpread = sgs;
                PreSnapTuning.JumpOffense = jo; PreSnapTuning.JumpDefense = jd; PreSnapTuning.LineupMistake = lm; PreSnapTuning.SlowSnap = ss;
            }
        }

        [Test]
        public void ALegalMotionManCrossingTheFormationAtTheSnapIsFine()
        {
            float jo = PreSnapTuning.JumpOffense, jd = PreSnapTuning.JumpDefense, lm = PreSnapTuning.LineupMistake, ss = PreSnapTuning.SlowSnap, md = PreSnapTuning.MotionDrift, mb = PreSnapTuning.MotionBoth;
            PreSnapTuning.JumpOffense = 0f; PreSnapTuning.JumpDefense = 0f; PreSnapTuning.LineupMistake = 0f; PreSnapTuning.SlowSnap = 0f; PreSnapTuning.MotionDrift = 0f; PreSnapTuning.MotionBoth = 0f;
            try
            {
                var sim = SimTestKit.RunNamed("Slants", "Shotgun", "Cover 3 Sky", "Nickel", 75, 75, 3, null, null, new PlaySimOptions { AutoSnap = false, Penalties = true });
                int slot = -1;
                for (int s = 1; s < 11; s++) if (sim.CanMotion(s)) { slot = s; break; }
                Assert.GreaterOrEqual(slot, 0);
                Assert.IsTrue(sim.SetMotion(slot, 1));
                for (int k = 0; k < 90; k++) sim.Step(new ControlInput());      // 1.5 s: he is running across the formation
                Assert.Greater(sim.Agents[slot].Speed, 2f);
                Assert.AreEqual(MotionIssue.None, sim.OffenseMovement().Issue, "moving laterally at the snap is legal");
                sim.Step(new ControlInput { Snap = true });
                Assert.AreEqual(PlayPhase.Live, sim.Phase);
            }
            finally
            {
                PreSnapTuning.JumpOffense = jo; PreSnapTuning.JumpDefense = jd; PreSnapTuning.LineupMistake = lm; PreSnapTuning.SlowSnap = ss; PreSnapTuning.MotionDrift = md; PreSnapTuning.MotionBoth = mb;
            }
        }

        [Test]
        public void AMotionManWhoTurnsUpfieldEarlyDrawsIllegalMotion()
        {
            float jo = PreSnapTuning.JumpOffense, jd = PreSnapTuning.JumpDefense, lm = PreSnapTuning.LineupMistake, ss = PreSnapTuning.SlowSnap, md = PreSnapTuning.MotionDrift, mb = PreSnapTuning.MotionBoth;
            float sp = PreSnapTuning.ShiftPass, sr = PreSnapTuning.ShiftRun;
            PreSnapTuning.JumpOffense = 0f; PreSnapTuning.JumpDefense = 0f; PreSnapTuning.LineupMistake = 0f; PreSnapTuning.SlowSnap = 0f; PreSnapTuning.MotionDrift = 500f; PreSnapTuning.MotionBoth = 0f;
            PreSnapTuning.ShiftPass = 0f; PreSnapTuning.ShiftRun = 0f;
            try
            {
                int motions = 0, illegal = 0;
                for (int seed = 1; seed <= 60; seed++)
                {
                    var sim = SimTestKit.RunNamed("Slants", "Shotgun Spread", "Cover 3 Sky", "Nickel", 75, 75, seed, null, null, new PlaySimOptions { AutoSnap = true, Cadence = true, Penalties = true });
                    bool hadMotion = false;
                    var none = new ControlInput();
                    for (int k = 0; k < 900 && sim.Phase == PlayPhase.PreSnap; k++) { sim.Step(none); if (sim.MotionSlot >= 0) hadMotion = true; }
                    if (!hadMotion) continue;
                    motions++;
                    if (sim.Phase == PlayPhase.Dead && sim.Result.Penalty != null && sim.Result.Penalty.Type == PenaltyType.IllegalMotion) illegal++;
                }
                Assert.Greater(motions, 5);
                Assert.AreEqual(motions, illegal, "every early turn upfield is flagged");
            }
            finally
            {
                PreSnapTuning.JumpOffense = jo; PreSnapTuning.JumpDefense = jd; PreSnapTuning.LineupMistake = lm; PreSnapTuning.SlowSnap = ss; PreSnapTuning.MotionDrift = md; PreSnapTuning.MotionBoth = mb;
                PreSnapTuning.ShiftPass = sp; PreSnapTuning.ShiftRun = sr;
            }
        }

        [Test]
        public void PreSnapFoulsComeAtTheNflRatePerSnap()
        {
            RestoreDefaults();
            // pre-snap only (no live play): about 2.3 fouls per team-game over about 63 snaps = 0.037 a snap; the 4,000-play lab run reports each type
            var book = SimTestKit.Playbook;
            var offs = new List<Play>(); var defs = new List<Play>();
            foreach (Play p in book.AllPlays)
            {
                if (p.Side == PlaySide.Offense && (p.Category == PlayCategory.InsideRun || p.Category == PlayCategory.OutsideRun || p.Category == PlayCategory.ShortPass || p.Category == PlayCategory.MediumPass || p.Category == PlayCategory.DeepPass)) offs.Add(p);
                if (p.Side == PlaySide.Defense && (p.Category == PlayCategory.ZoneCoverage || p.Category == PlayCategory.ManCoverage)) defs.Add(p);
            }
            int n = 2400, fouls = 0;
            var counts = new Dictionary<PenaltyType, int>();
            for (int i = 0; i < n; i++)
            {
                Play off = offs[(i * 7) % offs.Count], def = defs[(i * 5) % defs.Count];
                var sim = SimTestKit.Run(off, def, 70, 70, 100 + i, null, null, new PlaySimOptions { AutoSnap = true, Cadence = true, Penalties = true });
                StepPre(sim, 1500);
                if (sim.Phase == PlayPhase.Dead && sim.Result != null && sim.Result.Penalty != null)
                {
                    fouls++;
                    int c; counts.TryGetValue(sim.Result.Penalty.Type, out c); counts[sim.Result.Penalty.Type] = c + 1;
                }
            }
            double per = fouls / (double)n;
            Assert.That(per, Is.InRange(0.022, 0.056), "pre-snap fouls per snap: " + string.Join(", ", counts.Select(kv => kv.Key + " " + kv.Value)));
            int fs; counts.TryGetValue(PenaltyType.FalseStart, out fs);
            foreach (var kv in counts) Assert.LessOrEqual(kv.Value, fs, kv.Key + " is never more common than the false start");
        }
    }
}
