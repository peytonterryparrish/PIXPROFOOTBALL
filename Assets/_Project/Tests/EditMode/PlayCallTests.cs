using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;

namespace PixFootball.Tests
{
    /// <summary>The play-call navigation model (pure C# view-model): Formation -> Set -> Play, tabs, filters, paging, flip, timeout, expiry.</summary>
    public class PlayCallModelTests
    {
        static readonly Playbook Book = DefaultPlaybooks.Create();

        static PlayCallModel Offense(bool withSpecial = true)
        {
            var m = new PlayCallModel(Book, PlaySide.Offense, true, withSpecial);
            m.Situation.Timeouts = 2;
            var caller = new PlayCaller(Book, new Rng(3));
            var gs = new GameState(RuleSet.Pro()) { Phase = GamePhase.Scrimmage, BallSpot = 40, Down = 2, Distance = 7, Possession = Team.Home };
            m.CoachPicks = caller.SuggestOffense(gs, CoachProfile.Neutral, new List<Play>(), 6);
            m.SpecialPlays = Book.PlaysFor(DefaultPlaybooks.PuntF);
            m.RecentPlays = new List<Play> { Book.FindPlay("Slants"), Book.FindPlay("Mesh") };
            m.Open();
            return m;
        }

        [Test]
        public void OpensOnTheFormationListWithEveryOffensiveFamily()
        {
            var m = Offense();
            Assert.AreEqual(CallStage.Formation, m.Stage);
            Assert.AreEqual(CallTab.Playbook, m.Tab);
            Assert.GreaterOrEqual(m.Formations.Count, 7);
            Assert.IsTrue(m.Formations.Any(f => f.Label == "SHOTGUN"));
            Assert.IsTrue(m.Tabs.SequenceEqual(new[] { CallTab.Playbook, CallTab.AskCoach, CallTab.Recent, CallTab.Special }));
        }

        [Test]
        public void FormationThenSetThenPlayWithBackSteps()
        {
            var m = Offense();
            int shotgun = m.Formations.FindIndex(f => f.Label == "SHOTGUN");
            while (m.FormationIndex != shotgun) m.Navigate(0, -1);   // D-pad down
            m.Confirm();
            Assert.AreEqual(CallStage.Set, m.Stage, "Shotgun has several alignment sets");
            Assert.GreaterOrEqual(m.CurrentFormation.Sets.Count, 3);
            m.Navigate(0, -1);
            Assert.AreEqual(1, m.SetIndex);
            m.Confirm();
            Assert.AreEqual(CallStage.Play, m.Stage);
            Assert.Greater(m.Plays.Count, 20);
            Assert.IsTrue(m.Plays.All(p => p.FormationName == m.CurrentSet.Formation.Name));
            Assert.IsTrue(m.Back());
            Assert.AreEqual(CallStage.Set, m.Stage);
            Assert.IsTrue(m.Back());
            Assert.AreEqual(CallStage.Formation, m.Stage);
            Assert.IsFalse(m.Back(), "the top of the screen cannot be cancelled: a play must be called");
        }

        [Test]
        public void SingleSetFormationsSkipTheSetScreen()
        {
            var m = Offense();
            int gl = m.Formations.FindIndex(f => f.Label == "GOAL LINE");
            while (m.FormationIndex != gl) m.Navigate(0, -1);
            m.Confirm();
            Assert.AreEqual(CallStage.Play, m.Stage);
        }

        [Test]
        public void ThreeCardsPerPageAndPagingWithBumpers()
        {
            var m = Offense();
            m.Confirm(); if (m.Stage == CallStage.Set) m.Confirm();
            Assert.AreEqual(PlayCallModel.PageSize, 3);
            Assert.AreEqual(3, m.PagePlays().Count);
            Assert.AreEqual(0, m.Page);
            m.Navigate(1, 0); m.Navigate(1, 0);
            Assert.AreEqual(2, m.SelectedOnPage);
            m.Navigate(1, 0);                 // past the third card turns the page
            Assert.AreEqual(1, m.Page);
            Assert.AreEqual(0, m.SelectedOnPage);
            m.PageStep(1);
            Assert.AreEqual(2, m.Page);
            m.PageStep(-1); m.PageStep(-1); m.PageStep(-1);
            Assert.AreEqual(0, m.Page, "paging clamps at the first page");
            Assert.AreEqual((m.Plays.Count + 2) / 3, m.PageCount);
        }

        [Test]
        public void TypeFiltersNarrowThePlays()
        {
            var m = Offense();
            m.Confirm(); if (m.Stage == CallStage.Set) m.Confirm();
            int all = m.Plays.Count;
            m.CycleFilter(1);
            Assert.AreEqual(CallFilter.Run, m.Filter);
            Assert.IsTrue(m.Plays.All(p => p.IsRun)); Assert.Less(m.Plays.Count, all);
            m.CycleFilter(1);
            Assert.AreEqual(CallFilter.Pass, m.Filter);
            Assert.IsTrue(m.Plays.All(p => p.IsPass && p.Category != PlayCategory.PlayAction && p.Category != PlayCategory.Screen));
            m.CycleFilter(1);
            Assert.IsTrue(m.Plays.All(p => p.Category == PlayCategory.PlayAction));
            m.CycleFilter(1);
            Assert.IsTrue(m.Plays.All(p => p.Category == PlayCategory.Screen));
            m.CycleFilter(1);
            Assert.AreEqual(CallFilter.All, m.Filter);
            Assert.AreEqual(all, m.Plays.Count);
        }

        [Test]
        public void AskCoachRecentAndSpecialTabs()
        {
            var m = Offense();
            m.SetTab(CallTab.AskCoach);
            Assert.AreEqual(CallStage.Play, m.Stage);
            Assert.AreEqual(m.CoachPicks.Count, m.Plays.Count);
            Assert.IsFalse(string.IsNullOrEmpty(m.ReasonFor(m.Plays[0])), "the coach explains his suggestion");
            m.SetTab(CallTab.Recent);
            Assert.AreEqual(2, m.Plays.Count);
            m.SetTab(CallTab.Special);
            Assert.IsTrue(m.Plays.All(p => p.Category == PlayCategory.Punt));
            // B from a side tab returns to the playbook
            Assert.IsTrue(m.Back());
            Assert.AreEqual(CallTab.Playbook, m.Tab);
            Assert.AreEqual(CallStage.Formation, m.Stage);
            // RT cycles tabs
            m.CycleTab(1); Assert.AreEqual(CallTab.AskCoach, m.Tab);
            m.CycleTab(-1); m.CycleTab(-1); Assert.AreEqual(CallTab.Special, m.Tab);
        }

        [Test]
        public void ChoosingAPlayCallsItAndFlipMirrorsIt()
        {
            var m = Offense();
            m.SetTab(CallTab.AskCoach);
            Play first = m.Highlighted;
            Assert.IsTrue(m.CanFlip);
            m.ToggleFlip();
            Assert.IsTrue(m.Flip);
            m.Confirm();
            Assert.IsNotNull(m.Chosen);
            Assert.AreEqual(first.Name, m.Chosen.Name);
            Assert.IsTrue(m.Chosen.Mirrored, "the called play is the flipped one");
            // navigation is locked once a play is chosen
            int v = m.Version; m.Navigate(1, 0); m.CycleTab(1);
            Assert.AreEqual(v, m.Version);
        }

        [Test]
        public void SpecialTeamsPlaysAreNeverFlipped()
        {
            var m = Offense();
            m.SetTab(CallTab.Special);
            Assert.IsFalse(m.CanFlip);
            m.ToggleFlip();
            m.Confirm();
            Assert.IsFalse(m.Chosen.Mirrored);
        }

        [Test]
        public void TimeoutRequestHonoursTheTimeoutsLeft()
        {
            var m = Offense();
            m.RequestTimeout();
            Assert.IsTrue(m.TimeoutRequested);
            m.ClearTimeoutRequest();
            m.Situation.Timeouts = 0;
            m.RequestTimeout();
            Assert.IsFalse(m.TimeoutRequested);
            Assert.IsTrue(m.TimeoutBlocked);
        }

        [Test]
        public void ExpiringThePlayClockCallsTheHighlightedPlay()
        {
            var m = Offense();
            Play p = m.Expire();
            Assert.IsNotNull(p);
            Assert.AreSame(p, m.Chosen);
            // from the first formation screen there is no highlighted play yet: the model still returns a legal call
            Assert.IsNotNull(Book.FindFormation(p.FormationName));
        }

        [Test]
        public void LastFormationIsRememberedWhenTheScreenReopens()
        {
            var m = Offense();
            int idx = m.Formations.FindIndex(f => f.Label == "I-FORM");
            while (m.FormationIndex != idx) m.Navigate(0, -1);
            m.Confirm();
            var m2 = new PlayCallModel(Book, PlaySide.Offense, true, false) { LastFormation = m.LastFormation };
            m2.Open();
            Assert.AreEqual("I-FORM", m2.CurrentFormation.Label);
        }

        [Test]
        public void DefenseGroupsFormationsIntoCoverageSets()
        {
            var m = new PlayCallModel(Book, PlaySide.Defense, true, false);
            m.Open();
            Assert.IsTrue(m.Formations.Any(f => f.Label == "NICKEL"));
            while (m.CurrentFormation.Label != "4-3 BASE") m.Navigate(0, -1);
            m.Confirm();
            Assert.AreEqual(CallStage.Set, m.Stage);
            var labels = m.CurrentFormation.Sets.Select(s => s.Label).ToList();
            CollectionAssert.IsSubsetOf(new[] { "ZONE COVERAGE", "MAN COVERAGE", "BLITZ" }, labels);
            m.Confirm();
            Assert.IsTrue(m.Plays.All(p => p.Category == PlayCategory.ZoneCoverage));
            CollectionAssert.AreEqual(new[] { CallFilter.All, CallFilter.Zone, CallFilter.Man, CallFilter.Blitz }, m.FiltersFor());
        }

        [Test]
        public void SpecialOnlyModelStartsOnTheSpecialTab()
        {
            var m = new PlayCallModel(Book, PlaySide.Defense, false, true) { SpecialPlays = Book.PlaysFor(DefaultPlaybooks.PuntReturnF) };
            m.Open();
            Assert.AreEqual(CallTab.Special, m.Tab);
            Assert.AreEqual(CallStage.Play, m.Stage);
            Assert.AreEqual(1, m.Tabs.Count);
            Assert.GreaterOrEqual(m.Plays.Count, 3);
        }
    }

    /// <summary>Playbook data: flipping, formation variants, diagrams of every play.</summary>
    public class PlayEditingAndDiagramTests
    {
        static readonly Playbook Book = DefaultPlaybooks.Create();

        [Test]
        public void MirroringTwiceRestoresTheOriginalPlay()
        {
            foreach (Play p in Book.PlaysBySide(PlaySide.Offense))
            {
                Play back = PlayEditing.Mirror(PlayEditing.Mirror(p));
                Assert.IsFalse(back.Mirrored, p.ToString());
                for (int i = 0; i < 11; i++)
                {
                    var a = p.Assignments[i]; var b = back.Assignments[i];
                    Assert.AreEqual(a.GetType(), b.GetType(), p + " slot " + i);
                    var ra = a as RouteAssignment; var rb = b as RouteAssignment;
                    if (ra != null) for (int w = 0; w < ra.Waypoints.Count; w++) Assert.AreEqual(ra.Waypoints[w].Point.X, rb.Waypoints[w].Point.X, 1e-4f, p + " route " + i);
                    var ca = a as CarryAssignment; var cb = b as CarryAssignment;
                    if (ca != null) for (int w = 0; w < ca.Path.Count; w++) Assert.AreEqual(ca.Path[w].X, cb.Path[w].X, 1e-4f, p.ToString());
                }
            }
        }

        [Test]
        public void FlippedPlayIsAValidPlayOnTheMirroredFormation()
        {
            foreach (Play p in Book.PlaysBySide(PlaySide.Offense).Take(80))
            {
                Play m = PlayEditing.Mirror(p);
                Assert.IsTrue(m.Mirrored);
                Formation f = PlayEditing.FormationFor(Book, m);
                Formation orig = Book.FindFormation(p.FormationName);
                for (int i = 0; i < 11; i++)
                {
                    Assert.AreEqual(i, m.Assignments[i].Slot, m + " slot " + i);
                    if (i >= 2 && i <= 6) Assert.AreEqual(orig.Slots[i].X, f.Slots[i].X, 1e-4f, "the line is symmetric");
                    else Assert.AreEqual(-orig.Slots[i].X, f.Slots[i].X, 1e-4f, m + " slot " + i);
                }
                var qb = (QbAssignment)m.Assignments[0];
                foreach (int r in qb.ReadProgression) Assert.IsInstanceOf<RouteAssignment>(m.Assignments[r], m.ToString());
            }
        }

        [Test]
        public void RunToTheRightBecomesRunToTheLeft()
        {
            Play right = Book.FindPlay("Inside Zone Right");
            Play flipped = PlayEditing.Mirror(right);
            var c = (CarryAssignment)flipped.Assignments[1];
            Assert.Less(c.Path[c.Path.Count - 1].X, 0f);
            Assert.AreEqual(RunGap.BLeft, c.Gap);
        }

        [Test]
        public void VariantFormationsSharePlaysWithTheirFamily()
        {
            Formation doubles = Book.FindFormation("Shotgun Doubles");
            Assert.IsNotNull(doubles);
            Assert.AreEqual("Shotgun", doubles.Family);
            Assert.AreEqual(5, doubles.Slots.Count(s => PositionGroup.IsEligibleReceiver(s.Position)));
            Assert.GreaterOrEqual(Book.PlaysFor("Shotgun Doubles").Count, 25);
            var errors = new List<string>();
            Assert.IsTrue(Book.Validate(errors), string.Join("\n", errors.Take(10).ToArray()));
        }

        [Test]
        public void EveryPlayHasADiagramThatStaysInsideItsViewport()
        {
            foreach (Play p in Book.AllPlays)
            {
                PlayDiagram d = PlayDiagram.For(Book, p);
                Assert.AreEqual(11, d.Players.Count(x => !x.Ghost), p.ToString());
                Assert.Greater(d.Strokes.Count + d.Zones.Count, 0, p + " draws nothing");
                foreach (DiagramStroke s in d.Strokes)
                    foreach (Vec2 v in s.Points)
                    {
                        Assert.LessOrEqual(v.X, d.MaxX + 0.01f, p + " stroke leaves the card"); Assert.GreaterOrEqual(v.X, d.MinX - 0.01f, p + " stroke leaves the card");
                        Assert.LessOrEqual(v.Y, d.MaxY + 0.01f, p.ToString());
                    }
            }
        }

        [Test]
        public void PassPlaysDrawARouteForEveryReadAndOneThroughPrimary()
        {
            foreach (Play p in Book.PlaysBySide(PlaySide.Offense).Where(x => x.IsPass))
            {
                PlayDiagram d = PlayDiagram.For(Book, p);
                var qb = (QbAssignment)p.Assignments[0];
                foreach (int r in qb.ReadProgression) Assert.IsTrue(d.Strokes.Any(s => s.Slot == r && s.Points.Count >= 2), p + " read " + r);
                Assert.IsTrue(d.Strokes.Any(s => s.Ink == InkKind.PrimaryRoute), p.ToString());
            }
        }

        [Test]
        public void DefenseDiagramsShowZonesBlitzArrowsAndManLines()
        {
            Assert.IsTrue(PlayDiagram.For(Book, Book.FindPlay("Cover 3 Sky")).Zones.Count >= 3);
            var blitz = PlayDiagram.For(Book, Book.PlaysFor("4-3 Base").First(p => p.Name == "Cover 0 Blitz"));
            Assert.IsTrue(blitz.Strokes.Any(s => s.Ink == InkKind.Blitz));
            Assert.IsTrue(blitz.Strokes.Any(s => s.Ink == InkKind.Man));
            Assert.IsTrue(blitz.Players.Any(p => p.Ghost), "man coverage draws the receivers it covers");
        }
    }

    /// <summary>Situational CPU play calling (down, distance, field zone, score, time, coach tendencies, hurry-up) and the defensive personnel match.</summary>
    public class PlayCallerTests
    {
        static readonly Playbook Book = DefaultPlaybooks.Create();

        static GameState State(int spot, int down, int dist, int q = 2, float clock = 600f, int diff = 0)
        {
            var gs = new GameState(RuleSet.Pro()) { Phase = GamePhase.Scrimmage, BallSpot = spot, Down = down, Distance = dist, Possession = Team.Home, Quarter = q, ClockSeconds = clock };
            gs.HomeScore = 20 + diff; gs.AwayScore = 20;
            return gs;
        }

        static float PassShare(GameState s, CoachProfile c, int n = 400)
        {
            var caller = new PlayCaller(Book, new Rng(11));
            var recent = new List<Play>(); int pass = 0;
            for (int i = 0; i < n; i++)
            {
                Play p = caller.CallOffense(s, c, recent);
                if (p.IsPass) pass++;
                recent.Add(p); if (recent.Count > 4) recent.RemoveAt(0);
            }
            return pass / (float)n;
        }

        [Test]
        public void DownAndDistanceDriveTheRunPassMix()
        {
            float shortYardage = PassShare(State(50, 3, 1), CoachProfile.Neutral);
            float thirdLong = PassShare(State(50, 3, 12), CoachProfile.Neutral);
            Assert.Less(shortYardage, 0.5f); Assert.Greater(thirdLong, 0.75f);
        }

        [Test]
        public void CoachTendencyShiftsTheMix()
        {
            var run = new CoachProfile { PassTendency = 35, Aggression = 50, Style = PlayStyle.PowerRun };
            var air = new CoachProfile { PassTendency = 68, Aggression = 50, Style = PlayStyle.AirRaid };
            float r = PassShare(State(45, 1, 10), run), a = PassShare(State(45, 1, 10), air);
            Assert.Greater(a, r + 0.2f);
            Assert.AreEqual("RUN HEAVY", run.Label); Assert.AreEqual("PASS HEAVY", air.Label); Assert.AreEqual("BALANCED", CoachProfile.Neutral.Label);
        }

        [Test]
        public void GoalLineCallsUseThePowerPackage()
        {
            var caller = new PlayCaller(Book, new Rng(5)); int goalLine = 0;
            for (int i = 0; i < 100; i++) { Play p = caller.CallOffense(State(99, 1, 1), CoachProfile.Neutral, new List<Play>()); var f = Book.FindFormation(p.FormationName); if (f.Family == DefaultPlaybooks.GoalLine || p.Name == "Fade") goalLine++; }
            Assert.Greater(goalLine, 40);
        }

        [Test]
        public void TwoMinuteDrillCallsSidelineRoutesAndSpikesWhenOutOfTimeouts()
        {
            var s = State(60, 2, 8, 4, 60f, -3);
            Assert.IsTrue(PlayCaller.IsHurryUp(s));
            var caller = new PlayCaller(Book, new Rng(2)); int sideline = 0;
            for (int i = 0; i < 60; i++)
            {
                Play p = caller.CallOffense(s, CoachProfile.Neutral, new List<Play>());
                Assert.IsTrue(p.IsPass || p.IsRun);
                if (p.IsPass && p.Assignments.OfType<RouteAssignment>().Any(r => r.Route == RouteKind.Out || r.Route == RouteKind.Corner || r.Route == RouteKind.Fade || r.Route == RouteKind.Comeback)) sideline++;
            }
            Assert.Greater(sideline, 35);
            var spike = State(60, 2, 8, 4, 12f, -3); spike.HomeTimeouts = 0;
            Assert.IsTrue(PlayCaller.ShouldSpike(spike));
            Assert.IsFalse(PlayCaller.ShouldSpike(State(60, 2, 8, 2, 500f)));
        }

        [Test]
        public void LeadingTeamKneelsInTheFinalMinutes()
        {
            var s = State(40, 1, 10, 4, 100f, 7); s.AwayTimeouts = 0;
            Assert.IsTrue(PlayCaller.ShouldKneel(s));
            s.AwayTimeouts = 2; Assert.IsFalse(PlayCaller.ShouldKneel(s));
        }

        [Test]
        public void RecentCallsAreNotRepeated()
        {
            var caller = new PlayCaller(Book, new Rng(8)); var recent = new List<Play>(); int repeats = 0; Play last = null;
            for (int i = 0; i < 200; i++)
            {
                Play p = caller.CallOffense(State(40, 1, 10), CoachProfile.Neutral, recent);
                if (last != null && last.Name == p.Name) repeats++;
                last = p; recent.Add(p); if (recent.Count > 4) recent.RemoveAt(0);
            }
            Assert.Less(repeats, 12, "the same play back to back is rare");
        }

        [Test]
        public void DefensePersonnelMatchesTheOffense()
        {
            var caller = new PlayCaller(Book, new Rng(4));
            var s = State(50, 1, 10);
            var counts = new Dictionary<string, Dictionary<string, int>>();
            foreach (string form in new[] { "Shotgun", "Empty", "Goal Line", "I-Form" })
            {
                Formation off = Book.FindFormation(form);
                var d = new Dictionary<string, int>();
                for (int i = 0; i < 120; i++) { Play p = caller.CallDefense(s, CoachProfile.Neutral, off); d[p.FormationName] = d.ContainsKey(p.FormationName) ? d[p.FormationName] + 1 : 1; }
                counts[form] = d;
            }
            Assert.Greater(counts["Shotgun"].GetValueOrDefault("Nickel"), 70, "11 personnel draws nickel");
            Assert.Greater(counts["Empty"].GetValueOrDefault("Dime") + counts["Empty"].GetValueOrDefault("Nickel"), 100, "five receivers draw a nickel or dime look");
            Assert.Greater(counts["I-Form"].GetValueOrDefault("4-3 Base") + counts["I-Form"].GetValueOrDefault("3-4 Base"), 100, "two backs draw a base defense");
            Assert.AreEqual(0, counts["I-Form"].GetValueOrDefault("Dime"));
        }

        [Test]
        public void BlitzFrequencyFollowsTheCoachsAggression()
        {
            var caller = new PlayCaller(Book, new Rng(6)); var off = Book.FindFormation("Shotgun"); var s = State(50, 2, 6);
            Func<int, int> blitzes = aggr => { int n = 0; for (int i = 0; i < 500; i++) if (caller.CallDefense(s, new CoachProfile { Aggression = aggr, PassTendency = 50 }, off).Category == PlayCategory.Blitz) n++; return n; };
            int passive = blitzes(10), aggressive = blitzes(95);
            Assert.Greater(aggressive, passive * 2);
        }

        [Test]
        public void GoalLineDefenseAtTheOneAndPreventLate()
        {
            var caller = new PlayCaller(Book, new Rng(9));
            Play gl = caller.CallDefense(State(99, 1, 1), CoachProfile.Neutral, Book.FindFormation("Goal Line"));
            Assert.AreEqual(DefaultPlaybooks.GoalLineD, gl.FormationName);
            int prevent = 0;
            for (int i = 0; i < 60; i++) if (caller.CallDefense(State(40, 3, 10, 4, 60f, -3), CoachProfile.Neutral, Book.FindFormation("Shotgun")).Category == PlayCategory.Prevent) prevent++;
            Assert.Greater(prevent, 10);
        }

        [Test]
        public void AskCoachSuggestionsAreDistinctAndExplained()
        {
            var caller = new PlayCaller(Book, new Rng(1));
            var picks = caller.SuggestOffense(State(40, 3, 9), CoachProfile.Neutral, new List<Play>(), 6);
            Assert.AreEqual(6, picks.Count);
            Assert.AreEqual(6, picks.Select(p => PlayCaller.Concept(p.Play.Name)).Distinct().Count());
            foreach (var p in picks) Assert.IsFalse(string.IsNullOrEmpty(p.Reason));
            Assert.IsTrue(picks.Count(p => p.Play.IsPass) >= 4, "3rd and 9 is a passing down");
            var d = caller.SuggestDefense(State(99, 1, 1), CoachProfile.Neutral, 3);
            Assert.IsTrue(d.All(p => p.Play.FormationName == DefaultPlaybooks.GoalLineD));
        }
    }
}
