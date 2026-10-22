using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PixFootball.Core;

namespace PixFootball.Tests
{
    /// <summary>
    /// UI/FLOW audit fixes in the play-call display model (PlayCallModel, PlayDiagram, PlayCaller.CoachProfile, PenaltyEnforcer descriptions, playbook set names / run gaps).
    /// Pure C#: QA-500, 505, 506, 507, 508, 616, 66, 64, 158, 159, 167, 492, 547, 548, 549.
    /// </summary>
    public class UiFlowPlayCallModelTests
    {
        static readonly Playbook Book = DefaultPlaybooks.Create();

        // ------------------------------------------------------------------------------------------- QA-500
        [Test]
        public void CoachLabelBandsAreSymmetricAroundFifty()
        {
            Func<int, string> label = t => new CoachProfile { PassTendency = t }.Label;
            Assert.AreEqual("PASS HEAVY", label(57));
            Assert.AreEqual("BALANCED", label(56));
            Assert.AreEqual("RUN HEAVY", label(43));
            Assert.AreEqual("BALANCED", label(44));
            Assert.AreEqual("BALANCED", label(50));
            for (int d = 0; d <= 50; d++)
            {
                bool pass = label(50 + d) == "PASS HEAVY", run = label(50 - d) == "RUN HEAVY";
                Assert.AreEqual(pass, run, "tendency " + (50 + d) + " vs " + (50 - d));
            }
        }

        // ------------------------------------------------------------------------------------------- QA-505 / 506
        [Test]
        public void ExpiringTheClockNeverHandsBackNull()
        {
            var special = new PlayCallModel(Book, PlaySide.Special, false, true);
            special.Open();
            Assert.IsNotNull(special.Expire(), "special model without SpecialPlays");

            var offense = new PlayCallModel(Book, PlaySide.Offense, true, false);
            offense.Open();
            offense.Formations.Clear(); offense.Open();            // an empty view with no formations and no coach picks
            Play p = offense.Expire();
            Assert.IsNotNull(p);
            Assert.AreEqual(PlaySide.Offense, p.Side);
            Assert.AreSame(p, offense.Chosen);

            var defense = new PlayCallModel(Book, PlaySide.Defense, true, false);
            defense.Open();
            defense.Formations.Clear(); defense.Open();
            Assert.AreEqual(PlaySide.Defense, defense.Expire().Side);
        }

        [Test]
        public void PlayClockDefaultComesFromTheRules()
        {
            Assert.AreEqual(new RuleSet().PlayClockLive, new CallSituation().PlayClockSeconds);
        }

        // ------------------------------------------------------------------------------------------- QA-507 / 508
        [Test]
        public void DefenseSetsKeepEveryPlayAndAreLabelledFromTheirCategory()
        {
            var m = new PlayCallModel(Book, PlaySide.Defense, true, false);
            Assert.Greater(m.Formations.Count, 0);
            foreach (FormationEntry e in m.Formations)
            {
                int expected = Book.PlaysFor(e.Formation.Family == e.Formation.Name ? e.Formation.Name : e.Formation.Family).Count(p => p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike);
                Assert.AreEqual(expected, e.PlayCount, e.Label + ": a category was dropped or counted twice");
                foreach (SetEntry s in e.Sets)
                {
                    Assert.AreEqual(1, s.Plays.Select(p => p.Category).Distinct().Count(), e.Label + "/" + s.Label + " mixes categories");
                    Assert.AreEqual(PlayCallModel.DefenseSetLabel(s.Plays[0].Category), s.Label);
                }
            }
        }

        [Test]
        public void CategoryLabelsAreNeverRawIdentifiers()
        {
            Assert.AreEqual("GOAL LINE", PlayCallModel.DefenseSetLabel(PlayCategory.GoalLine));
            Assert.AreEqual("PREVENT", PlayCallModel.DefenseSetLabel(PlayCategory.Prevent));
            Assert.AreEqual("ZONE COVERAGE", PlayCallModel.DefenseSetLabel(PlayCategory.ZoneCoverage));
            foreach (PlayCategory c in Enum.GetValues(typeof(PlayCategory)))
            {
                string n = PlayCallModel.CategoryName(c);
                Assert.IsFalse(string.IsNullOrEmpty(n), c.ToString());
                Assert.AreEqual(n.ToUpperInvariant(), n, c + " is not caps");
                Assert.IsFalse(Regex.IsMatch(n, "[a-z]"), c.ToString());
            }
            Assert.AreEqual("Offense - Inside Run", PlayCallModel.SetNameFor(PlaySide.Offense, PlayCategory.InsideRun));
            Assert.AreEqual("INSIDE ZONE", PlayCallModel.SpacedUpper("InsideZone"));
            Assert.AreEqual("QB KEEP", PlayCallModel.SpacedUpper("QbKeep"));
        }

        [Test]
        public void PlaybookSetNamesAreReadable()
        {
            Assert.Greater(Book.Sets.Count, 10);
            foreach (PlaySet s in Book.Sets)
            {
                Assert.IsFalse(Regex.IsMatch(s.Name, "[a-z][A-Z]"), "raw identifier in set name '" + s.Name + "'");
                Assert.IsTrue(s.Name.Contains(" - "), s.Name);
            }
        }

        // ------------------------------------------------------------------------------------------- QA-616
        [Test]
        public void FiltersOnlyOfferWhatThePlaysOfTheViewCanShow()
        {
            var m = new PlayCallModel(Book, PlaySide.Defense, true, false); m.Open();
            for (int fi = 0; fi < m.Formations.Count; fi++)
            {
                while (m.Stage != CallStage.Formation) Assert.IsTrue(m.Back());
                while (m.FormationIndex != fi) m.Navigate(0, -1);
                m.Confirm();
                int sets = m.CurrentFormation.Sets.Count;
                for (int si = 0; si < sets; si++)
                {
                    if (m.Stage == CallStage.Set) { while (m.SetIndex != si) m.Navigate(0, -1); m.Confirm(); }
                    CallFilter[] fs = m.FiltersFor();
                    CollectionAssert.Contains(fs, CallFilter.All);
                    foreach (CallFilter f in fs)
                    {
                        while (m.Filter != f) m.CycleFilter(1);
                        Assert.Greater(m.Plays.Count, 0, m.CurrentFormation.Label + " / " + m.CurrentSet.Label + " filter " + f + " opens an empty page");
                    }
                    while (m.Filter != CallFilter.All) m.CycleFilter(1);
                    if (m.CurrentSet.Label == "GOAL LINE") CollectionAssert.DoesNotContain(fs, CallFilter.Blitz);
                    if (sets > 1) Assert.IsTrue(m.Back());
                }
            }
        }

        [Test]
        public void OffenseFiltersListEachTypeThatExists()
        {
            var m = new PlayCallModel(Book, PlaySide.Offense, true, false); m.Open();
            m.Confirm(); if (m.Stage == CallStage.Set) m.Confirm();
            CollectionAssert.AreEqual(new[] { CallFilter.All, CallFilter.Run, CallFilter.Pass, CallFilter.PlayAction, CallFilter.Screen }, m.FiltersFor(), "the first Singleback set has all five");
        }

        // ------------------------------------------------------------------------------------------- QA-66
        [Test]
        public void AskCoachReasonsFitTheirCardAndDifferPerPlay()
        {
            var seen = new HashSet<CarryStyle>(); var picks = new List<CoachPick>();
            foreach (Play p in Book.PlaysBySide(PlaySide.Offense).Where(x => x.IsRun && x.Category != PlayCategory.QbRun))
            {
                var c = p.Assignments.OfType<CarryAssignment>().FirstOrDefault(a => !a.IsFake);
                if (c == null || !seen.Add(c.Style)) continue;
                picks.Add(new CoachPick { Play = p, Reason = "1ST DOWN: SET UP THE PASS", Score = 1f });
                if (picks.Count == 3) break;
            }
            Assert.AreEqual(3, picks.Count);
            var m = new PlayCallModel(Book, PlaySide.Offense, true, false) { CoachPicks = picks };
            m.Open(); m.SetTab(CallTab.AskCoach);
            var reasons = m.Plays.Select(p => m.ReasonFor(p)).ToList();
            foreach (string r in reasons) { StringAssert.DoesNotContain("SET UP THE PASS", r); Assert.IsFalse(string.IsNullOrEmpty(r)); }
            Assert.AreEqual(3, reasons.Distinct().Count(), string.Join(" | ", reasons.ToArray()));
        }

        [Test]
        public void DefenseReasonsSharedBySeveralPicksGetTheirOwnShell()
        {
            var plays = new[] { Book.FindPlay("Cover 3 Sky"), Book.FindPlay("Cover 2 Zone"), Book.FindPlay("Cover 4 Quarters") };
            var picks = plays.Select(p => new CoachPick { Play = p, Reason = "BALANCED LOOK", Score = 1f }).ToList();
            var m = new PlayCallModel(Book, PlaySide.Defense, true, false) { CoachPicks = picks };
            m.Open(); m.SetTab(CallTab.AskCoach);
            var reasons = m.Plays.Select(p => m.ReasonFor(p)).ToList();
            Assert.AreEqual(3, reasons.Distinct().Count(), string.Join(" | ", reasons.ToArray()));
            StringAssert.Contains("COVER 3", reasons[0]);
        }

        // ------------------------------------------------------------------------------------------- QA-64
        [Test]
        public void SpecialTeamsTabShowsEveryCategoryBeforeRepeatingOne()
        {
            Func<PlayCategory, string, Play> mk = (c, n) => new Play { Id = n, Name = n, Category = c, Side = PlaySide.Special };
            var list = new List<Play> { mk(PlayCategory.Punt, "a"), mk(PlayCategory.Punt, "b"), mk(PlayCategory.Punt, "c"), mk(PlayCategory.FieldGoal, "d"), mk(PlayCategory.Kickoff, "e") };
            CollectionAssert.AreEqual(new[] { "a", "d", "e", "b", "c" }, PlayCallModel.InterleaveByCategory(list).Select(p => p.Name).ToArray());

            var m = new PlayCallModel(Book, PlaySide.Offense, true, true) { SpecialPlays = Book.PlaysFor(DefaultPlaybooks.PuntF).Concat(Book.PlaysFor(DefaultPlaybooks.FieldGoalF)).ToList() };
            m.Open(CallTab.Special);
            Assert.GreaterOrEqual(m.PagePlays().Select(p => p.Category).Distinct().Count(), 2, "page one is not just punts");
            Assert.AreEqual(m.SpecialPlays.Count, m.Plays.Count, "nothing is lost by the reordering");
        }

        // ------------------------------------------------------------------------------------------- QA-158
        [Test]
        public void CallScreenScoreIsPrintedAwayTeamFirstLikeTheScorebug()
        {
            var s = new CallSituation { ScoreText = "BUF 3  -  MIA 7" };   // legacy contract: HOME first
            Assert.AreEqual("MIA 7 - BUF 3", s.DisplayScore);
            s.SetScore("MIA", 7, "BUF", 3);
            Assert.AreEqual("MIA 7 - BUF 3", s.DisplayScore);
            Assert.AreEqual("", new CallSituation().DisplayScore);
        }

        // ------------------------------------------------------------------------------------------- QA-492
        [Test]
        public void PenaltyDescriptionUsesTheTeamNameWhenGiven()
        {
            Func<GameState> start = () => new GameState(RuleSet.Pro()) { Phase = GamePhase.Scrimmage, BallSpot = 30, Down = 1, Distance = 10, Possession = Team.Home };
            var call = new PenaltyCall(PenaltyType.Offside, Team.Away);
            EnforcementResult named = PenaltyEnforcer.Enforce(start(), call, null, "Ironhorns", "Reefrunners");
            StringAssert.Contains("on Reefrunners", named.Description);
            StringAssert.DoesNotContain("Away", named.Description);
            EnforcementResult plain = PenaltyEnforcer.Enforce(start(), call);
            StringAssert.Contains("on Away", plain.Description, "without names the enum name is kept (old behaviour)");
            Assert.AreEqual("Ironhorns", PenaltyEnforcer.TeamLabel(Team.Home, "Ironhorns", "Reefrunners"));
        }

        // ------------------------------------------------------------------------------------------- QA-547
        [Test]
        public void RunPlaysCarryTheirGapAndPlaysideBlockersMoveTowardTheRun()
        {
            int checkedBlocks = 0;
            foreach (Play p in Book.PlaysBySide(PlaySide.Offense).Where(x => x.IsRun))
            {
                var carry = p.Assignments.OfType<CarryAssignment>().FirstOrDefault(c => !c.IsFake);
                if (carry != null) Assert.AreEqual(carry.Gap, p.Gap, p + ": Play.Gap follows the carry");
            }
            foreach (Play basePlay in Book.PlaysBySide(PlaySide.Offense).Where(x => x.IsRun && !x.Mirrored).Take(60))
            {
                foreach (Play p in new[] { basePlay, PlayEditing.Mirror(basePlay) })
                {
                    var carry = p.Assignments.OfType<CarryAssignment>().FirstOrDefault(c => !c.IsFake && c.Path.Count > 0);
                    if (carry == null) continue;
                    int dir = carry.Path[carry.Path.Count - 1].X >= 0f ? 1 : -1;
                    PlayDiagram d = PlayDiagram.For(Book, p);
                    for (int i = 2; i <= 6; i++)
                    {
                        var b = p.Assignments[i] as BlockAssignment;
                        if (b == null || b.Target != BlockTarget.Playside || b.Path.Count > 0 || b.Block == BlockKind.Pull) continue;
                        DiagramStroke s = d.Strokes.First(x => x.Slot == i && x.Ink == InkKind.Block);
                        float dx = s.Points[1].X - s.Points[0].X;
                        Assert.AreEqual(dir, Math.Sign(dx), p + " slot " + i + ": the playside block must lean toward the run");
                        checkedBlocks++;
                    }
                }
            }
            Assert.Greater(checkedBlocks, 0, "the sample contains playside blocks to check");
        }

        // ------------------------------------------------------------------------------------------- QA-548
        [Test]
        public void ManCoverageGhostsAreTheOffensiveSlotsTheyCover()
        {
            Play blitz = Book.PlaysFor("4-3 Base").First(p => p.Name == "Cover 0 Blitz");
            var targets = blitz.Assignments.OfType<CoverageAssignment>().Where(c => c.IsMan).Select(c => c.TargetSlot).Distinct().ToList();
            Assert.Greater(targets.Count, 0);
            foreach (string name in new[] { DefaultPlaybooks.IForm, DefaultPlaybooks.Trips, DefaultPlaybooks.Empty, DefaultPlaybooks.GoalLine, DefaultPlaybooks.Singleback })
            {
                Formation off = Book.FindFormation(name);
                PlayDiagram d = PlayDiagram.For(Book, blitz, off);
                var ghosts = d.Players.Where(p => p.Ghost).ToList();
                CollectionAssert.AreEquivalent(targets, ghosts.Select(g => g.Slot).ToList(), name);
                foreach (DiagramPlayer g in ghosts)
                {
                    Assert.AreEqual(off.Slots[g.Slot].X, g.X, 1e-4f, name + " slot " + g.Slot);
                    Assert.AreEqual(off.Slots[g.Slot].Y, g.Y, 1e-4f, name + " slot " + g.Slot);
                }
                // every man line ends at its ghost
                foreach (DiagramStroke s in d.Strokes.Where(x => x.Ink == InkKind.Man))
                {
                    var cv = (CoverageAssignment)blitz.Assignments[s.Slot];
                    DiagramPlayer g = ghosts.First(x => x.Slot == cv.TargetSlot);
                    Assert.AreEqual(g.X, s.Points[s.Points.Count - 1].X, 1e-3f);
                }
            }
            var withoutFormation = PlayDiagram.For(blitz, Book.FindFormation("4-3 Base"));
            Assert.IsTrue(withoutFormation.Players.Any(p => p.Ghost), "the book-less overload falls back to a standard look");
        }

        // ------------------------------------------------------------------------------------------- QA-549
        [Test]
        public void ClampedStrokesAreFlaggedAndReported()
        {
            var d = new PlayDiagram { Title = "synthetic" };
            var deep = new DiagramStroke { Ink = InkKind.Route, Slot = 3 };
            deep.Points.Add(new Vec2(0f, 0f)); deep.Points.Add(new Vec2(0f, 60f));
            var side = new DiagramStroke { Ink = InkKind.Route, Slot = 4 };
            side.Points.Add(new Vec2(0f, 0f)); side.Points.Add(new Vec2(40f, 5f));
            var ok = new DiagramStroke { Ink = InkKind.Route, Slot = 5 };
            ok.Points.Add(new Vec2(0f, 0f)); ok.Points.Add(new Vec2(3f, 10f));
            d.Strokes.Add(deep); d.Strokes.Add(side); d.Strokes.Add(ok);
            d.ClampToViewport();
            Assert.IsTrue(deep.Clipped); Assert.IsTrue(side.Clipped); Assert.IsFalse(ok.Clipped);
            Assert.AreEqual(2, d.ClippedStrokes);
            Assert.AreEqual(2, d.ClipReport().Count);
            Assert.Less(deep.Points[1].Y, 60f);

            foreach (Play p in Book.AllPlays)
            {
                PlayDiagram pd = PlayDiagram.For(Book, p);
                Assert.AreEqual(pd.Strokes.Count(s => s.Clipped), pd.ClippedStrokes, p.ToString());
            }
        }

        // ------------------------------------------------------------------------------------------- QA-159 / 167
        [Test]
        public void ChipsOfAFormationNeverSitOnTopOfEachOther()
        {
            foreach (Formation f in Book.Formations)
            {
                PlayDiagram d = PlayDiagram.ForFormation(f);
                var pl = d.Players.Where(p => !p.Ghost).ToList();
                for (int i = 0; i < pl.Count; i++)
                    for (int j = i + 1; j < pl.Count; j++)
                    {
                        DiagramPlayer a = pl[i], b = pl[j];
                        bool aLine = !a.Defense && PositionGroup.IsOffensiveLine(a.Position), bLine = !b.Defense && PositionGroup.IsOffensiveLine(b.Position);
                        if (aLine && bLine) continue;
                        if (Math.Abs(a.Y - b.Y) >= 1.6f) continue;
                        float min = (aLine || bLine) ? 1.6f : 2.1f;
                        Assert.GreaterOrEqual(Math.Abs(a.X - b.X), min - 0.01f, f.Name + ": " + a.Label + " and " + b.Label + " overlap");
                    }
            }
            // the shotgun back stands beside the quarterback, not on him
            PlayDiagram sg = PlayDiagram.ForFormation(Book.FindFormation(DefaultPlaybooks.ShotgunF));
            Assert.GreaterOrEqual(Math.Abs(sg.Players[1].X - sg.Players[0].X), 2.1f);
        }

        [Test]
        public void ExtraDefensiveBacksAreLabelledNickelAndDime()
        {
            Func<string, List<string>> labels = n => PlayDiagram.ForFormation(Book.FindFormation(n)).Players.Where(p => p.Position == Position.CB).Select(p => p.Label).ToList();
            CollectionAssert.AreEquivalent(new[] { "CB", "NB", "CB" }, labels(DefaultPlaybooks.Nickel));
            CollectionAssert.AreEquivalent(new[] { "CB", "NB", "DB", "CB" }, labels(DefaultPlaybooks.Dime));
            CollectionAssert.AreEquivalent(new[] { "CB", "CB" }, labels(DefaultPlaybooks.Base43));
        }
    }
}
