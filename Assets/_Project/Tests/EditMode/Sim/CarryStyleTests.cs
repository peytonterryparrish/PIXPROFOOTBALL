using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>
    /// v0.11.0 RB track (owner note 7): the ball carrier's decided style. The simulation chooses the carry arm (away from the defenders' leverage), announces every cut of 36+ degrees
    /// (Approach, then Plant off the OUTSIDE foot, then Drive), plans its moves ahead and keeps them inside a per-carry budget; yards per carry must stay where the calibration has them.
    /// </summary>
    public class CarryStyleTests
    {
        private static List<Play> Runs()
        {
            return SimTestKit.Playbook.AllPlays.Where(p => p.Side == PlaySide.Offense && (p.Category == PlayCategory.InsideRun || p.Category == PlayCategory.OutsideRun || p.Category == PlayCategory.Draw)).ToList();
        }

        private static List<Play> Defenses()
        {
            return SimTestKit.Playbook.AllPlays.Where(p => p.Side == PlaySide.Defense && (p.Category == PlayCategory.ZoneCoverage || p.Category == PlayCategory.ManCoverage || p.Category == PlayCategory.Blitz)).ToList();
        }

        private sealed class Stats
        {
            public int Carries, Threat, Wrong, Threat3, Wrong3, Switches, Cuts, CutsPlantOutside, CutsBad, Moves, PlansExecuted;
            public double Yards;
            public int MaxMoves;
            public int ApproachBeforePlant, PlantWithoutApproach;
        }

        private static Stats Sample(int plays, bool styleOn, int seed0)
        {
            bool prev = CarryStyleConfig.Enabled;
            CarryStyleConfig.Enabled = styleOn;
            try
            {
                var runs = Runs(); var defs = Defenses();
                var st = new Stats();
                for (int pi = 0; pi < plays; pi++)
                {
                    Play off = runs[pi % runs.Count], def = defs[(pi * 5 + pi / runs.Count) % defs.Count];
                    PlaySimulation sim = SimTestKit.Run(off, def, 88, 88, seed0 + pi, null, null, new PlaySimOptions { Penalties = false });
                    var none = default(ControlInput);
                    int guard = 0, carrier = -1, lastArm = 0, moves = 0; CarryCutPhase lastPhase = CarryCutPhase.None; CarrierMove lastMove = CarrierMove.None;
                    while (sim.Phase != PlayPhase.Dead && guard++ < 3000)
                    {
                        sim.Step(none);
                        int h = sim.Ball.Holder;
                        if (h < 0 || sim.Ball.State != BallState.Held) continue;
                        SimAgent a = sim.Agents[h];
                        if (a.Side != SimSide.Offense || a.Slot == 0) continue;
                        if (carrier != h) { carrier = h; st.Carries++; lastArm = 0; lastPhase = CarryCutPhase.None; }
                        // arm compliance
                        Vec2 heading = a.Speed > 1.5f ? a.Vel.Norm() : a.Facing;
                        Vec2 right = new Vec2(heading.Y, -heading.X);
                        SimAgent near = null; float nd = 99f;
                        for (int j = 11; j < 22; j++) { SimAgent o = sim.Agents[j]; if (o.IsDown || o.Body.FallenTime > 0.3f) continue; float d = (o.Pos - a.Pos).Length; if (d < nd) { nd = d; near = o; } }
                        if (styleOn && a.CarryArm != 0 && near != null && nd < 6f && a.Speed > 1.6f)
                        {
                            float lat = (near.Pos - a.Pos).Dot(right);
                            if (Math.Abs(lat) > 0.6f)
                            {
                                int expect = lat > 0f ? -1 : 1;
                                st.Threat++; if (a.CarryArm != expect) st.Wrong++;
                                if (nd < 3f) { st.Threat3++; if (a.CarryArm != expect) st.Wrong3++; }
                            }
                        }
                        if (lastArm != 0 && a.CarryArm != lastArm && a.CarryArm != 0) st.Switches++;
                        if (a.CarryArm != 0) lastArm = a.CarryArm;
                        // cut phases
                        if (a.CutPhase != lastPhase)
                        {
                            if (a.CutPhase == CarryCutPhase.Approach) { st.Cuts++; if (a.CutPlantFoot != (a.CutAngle >= 0f ? -1 : 1)) st.CutsBad++; else st.CutsPlantOutside++; }
                            if (a.CutPhase == CarryCutPhase.Plant) { if (lastPhase == CarryCutPhase.Approach) st.ApproachBeforePlant++; else st.PlantWithoutApproach++; }
                            lastPhase = a.CutPhase;
                        }
                        if (a.Move != CarrierMove.None && lastMove == CarrierMove.None) { moves++; st.Moves++; }
                        lastMove = a.Move;
                    }
                    st.MaxMoves = Math.Max(st.MaxMoves, moves);
                    if (carrier >= 0 && sim.Result != null) st.Yards += sim.Result.Yards;
                }
                return st;
            }
            finally { CarryStyleConfig.Enabled = prev; }
        }

        [Test]
        public void CarryArmIsAwayFromTheDefendersAndSwitchesAreRare()
        {
            var s = Sample(160, true, 31000);
            double comp = s.Threat > 0 ? 1.0 - (double)s.Wrong / s.Threat : 0, comp3 = s.Threat3 > 0 ? 1.0 - (double)s.Wrong3 / s.Threat3 : 0;
            TestContext.WriteLine("carries " + s.Carries + " arm compliance " + comp.ToString("0.000") + " (" + s.Threat + " frames), defender < 3 yd " + comp3.ToString("0.000") + " (" + s.Threat3 + "), switches per carry " + ((double)s.Switches / Math.Max(1, s.Carries)).ToString("0.00"));
            Assert.Greater(s.Carries, 100);
            Assert.GreaterOrEqual(comp, 0.80, "the ball is in the arm away from the nearest defender");
            Assert.LessOrEqual((double)s.Switches / s.Carries, 2.5, "a back does not change hands every step");
        }

        [Test]
        public void EveryCutIsAnnouncedAndPlantedOffTheOutsideFoot()
        {
            var s = Sample(160, true, 32000);
            TestContext.WriteLine("cuts " + s.Cuts + " per carry " + ((double)s.Cuts / Math.Max(1, s.Carries)).ToString("0.00") + " bad plant foot " + s.CutsBad + " plant without approach " + s.PlantWithoutApproach);
            Assert.Greater(s.Cuts, 30, "runs contain cuts");
            Assert.AreEqual(0, s.CutsBad, "the plant foot is the foot opposite to the direction of the cut");
            Assert.AreEqual(0, s.PlantWithoutApproach, "a plant is always preceded by the announced approach");
        }

        [Test]
        public void MovesStayInsideTheBudget()
        {
            var s = Sample(160, true, 33000);
            double perCarry = (double)s.Moves / Math.Max(1, s.Carries);
            TestContext.WriteLine("moves per carry " + perCarry.ToString("0.00") + " max in one carry " + s.MaxMoves);
            Assert.LessOrEqual(perCarry, 0.85, "an elusive move on every second carry is already a lot");
            Assert.LessOrEqual(s.MaxMoves, 4);
        }

        private static float PullerEngageRate(bool styleOn, int n)
        {
            bool prev = CarryStyleConfig.Enabled; CarryStyleConfig.Enabled = styleOn;
            try
            {
                var rules = RuleSet.Pro(); int engaged = 0, pullers = 0;
                Play off = SimTestKit.Offense("Power Right", "I-Form"), def = SimTestKit.Defense("Cover 3 Sky", "4-3 Base");
                for (int s = 0; s < n; s++)
                {
                    var rng = new Rng(900 + s);
                    Player[] ro = SimTestKit.Roster(SimTestKit.Playbook.FindFormation(off.FormationName), 75, rng), rd = SimTestKit.Roster(SimTestKit.Playbook.FindFormation(def.FormationName), 75, rng);
                    var sim = new PlaySimulation(); sim.Setup(SimTestKit.Scrimmage(rules), off, def, ro, rd, rules, rng.Fork(), new PlaySimOptions { Penalties = false });
                    var ids = new List<int>(); for (int i = 0; i < 11; i++) { var ba = sim.Agents[i].Assign as BlockAssignment; if (ba != null && ba.Block == BlockKind.Pull) ids.Add(i); }
                    if (ids.Count == 0) continue; pullers++;
                    bool eng = false; SimTestKit.StepToLive(sim);
                    for (int i = 0; i < 200 && sim.Phase == PlayPhase.Live; i++) { sim.Step(default(ControlInput)); foreach (int id in ids) if (sim.Agents[id].State == AgentState.Engaged) eng = true; }
                    if (eng) engaged++;
                }
                return pullers > 0 ? (float)engaged / pullers : 0f;
            }
            finally { CarryStyleConfig.Enabled = prev; }
        }

        [Test]
        public void PullersEngageAsOftenAsWithoutTheStyle()
        {
            float on = PullerEngageRate(true, 200), off = PullerEngageRate(false, 200);
            TestContext.WriteLine("pullers engaged style on " + on.ToString("0.000") + " off " + off.ToString("0.000"));
            Assert.GreaterOrEqual(on, off - 0.07f, "the press and the planned cut must not make the pullers miss their men");
        }

        [Test]
        public void YardsPerCarryStayRealistic()
        {
            var on = Sample(700, true, 34000); var off = Sample(700, false, 34000);
            double ypcOn = on.Yards / Math.Max(1, on.Carries), ypcOff = off.Yards / Math.Max(1, off.Carries);
            TestContext.WriteLine("yards per carry style on " + ypcOn.ToString("0.00") + " off " + ypcOff.ToString("0.00") + " (carries " + on.Carries + " / " + off.Carries + ")");
            Assert.That(ypcOn, Is.InRange(3.2, 5.6));
            Assert.That(Math.Abs(ypcOn - ypcOff), Is.LessThan(0.60), "the style must not move the calibrated rushing average (about 0.3 yd of noise at 700 carries a side)");
        }
    }
}

