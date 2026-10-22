using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.Sim;
using UnityEngine;
using Team = PixFootball.Core.Team;

namespace PixFootball.Tests
{
    /// <summary>v0.7 track C: the call sheet model (tabs, groups, search, one-press suggestions), the route view data, the display-fit maths and the pass-input constants.</summary>
    public class V070ControlsTests
    {
        static readonly Playbook Book = DefaultPlaybooks.Create();

        static PlayCallModel Sheet(PlaySide side = PlaySide.Offense, bool special = false)
        {
            var m = new PlayCallModel(Book, side, true, special, true);
            var plays = Book.AllPlays.Where(p => p.Side == side && p.Category != PlayCategory.Kneel && p.Category != PlayCategory.Spike).ToList();
            m.CoachPicks = plays.Take(3).Select(p => new CoachPick { Play = p, Reason = "TEST", Score = 1f }).ToList();
            m.RecentPlays = plays.Skip(3).Take(4).ToList();
            m.FavoritePlays = plays.Skip(8).Take(2).ToList();
            if (special) m.SpecialPlays = Book.PlaysFor(DefaultPlaybooks.PuntF).ToList();
            m.Open(CallTab.AskCoach);
            return m;
        }

        // ------------------------------------------------------------------------------------------------ the call sheet
        [Test]
        public void SheetTabsAreTheCallSheetTabs()
        {
            var m = Sheet(PlaySide.Offense, true);
            CollectionAssert.AreEqual(new[] { CallTab.AskCoach, CallTab.Formation, CallTab.Concept, CallTab.PlayType, CallTab.Personnel, CallTab.Recent, CallTab.Favorites, CallTab.Special }, m.Tabs);
            Assert.IsTrue(m.SheetMode);
            Assert.AreEqual(CallStage.Play, m.Stage, "the sheet is always on the play stage");
        }

        [Test]
        public void ClassicModelKeepsItsOldTabsAndStages()
        {
            var m = new PlayCallModel(Book, PlaySide.Offense, true, true);
            CollectionAssert.AreEqual(new[] { CallTab.Playbook, CallTab.AskCoach, CallTab.Recent, CallTab.Special }, m.Tabs);
            m.Open();
            Assert.AreEqual(CallStage.Formation, m.Stage);
            Assert.IsFalse(m.SheetMode);
        }

        [Test]
        public void OnePressFromOpeningCallsTheFirstSuggestion()
        {
            var m = Sheet();
            Assert.AreEqual(CallTab.AskCoach, m.Tab);
            Assert.AreEqual(3, m.Plays.Count);
            m.Confirm();
            Assert.IsNotNull(m.Chosen, "A from the opening screen calls the first pick");
            Assert.AreEqual(m.CoachPicks[0].Play.Name, m.Chosen.Name);
        }

        [Test]
        public void AnySuggestionIsCalledInAtMostThreePresses()
        {
            for (int pick = 0; pick < 3; pick++)
            {
                var m = Sheet();
                int presses = 0;
                for (int i = 0; i < pick; i++) { m.Navigate(1, 0); presses++; }   // D-pad right to the pick
                m.Confirm(); presses++;                                            // A
                Assert.IsNotNull(m.Chosen); Assert.AreEqual(m.CoachPicks[pick].Play.Name, m.Chosen.Name);
                Assert.LessOrEqual(presses, 3, "opening to snap-ready is at most three presses for a suggested play");
            }
            // a user who wandered to another tab is one B press from the suggestions
            var w = Sheet(); w.SetTab(CallTab.Formation); w.Back();
            Assert.AreEqual(CallTab.AskCoach, w.Tab);
        }

        [Test]
        public void GroupTabsListGroupsWithPlays()
        {
            var m = Sheet();
            foreach (var tab in new[] { CallTab.Formation, CallTab.Concept, CallTab.PlayType, CallTab.Personnel })
            {
                m.SetTab(tab);
                Assert.Greater(m.Groups.Count, 1, tab + " groups");
                Assert.IsTrue(m.TabHasGroups);
                foreach (var g in m.Groups) Assert.Greater(g.Plays.Count, 0, tab + " " + g.Label);
                Assert.AreEqual(m.CurrentGroup.Plays.Count, m.Plays.Count, tab + ": the cards show the group's plays");
            }
        }

        [Test]
        public void StepGroupChangesThePlaysAndWraps()
        {
            var m = Sheet();
            m.SetTab(CallTab.PlayType);
            string first = m.CurrentGroup.Label; int n = m.Groups.Count;
            m.StepGroup(1);
            Assert.AreNotEqual(first, m.CurrentGroup.Label);
            Assert.AreEqual(0, m.PlayIndex);
            for (int i = 0; i < n - 1; i++) m.StepGroup(1);
            Assert.AreEqual(first, m.CurrentGroup.Label, "wraps around");
            m.StepGroup(-1);
            Assert.AreEqual(m.Groups[n - 1].Label, m.CurrentGroup.Label);
        }

        [Test]
        public void DPadUpDownMovesGroupsAndLeftRightMovesPlaysOnTheSheet()
        {
            var m = Sheet();
            m.SetTab(CallTab.Formation);
            string g0 = m.CurrentGroup.Label;
            m.Navigate(0, -1);   // D-pad down = next group
            Assert.AreNotEqual(g0, m.CurrentGroup.Label);
            m.Navigate(1, 0);
            Assert.AreEqual(1, m.PlayIndex);
            m.Navigate(-1, 0); m.Navigate(-1, 0);
            Assert.AreEqual(0, m.PlayIndex, "clamped at the first play");
        }

        [Test]
        public void BackClearsASearchFirstThenGoesToTheSuggestions()
        {
            var m = Sheet();
            m.SetTab(CallTab.Concept); m.SetSearch("slant");
            Assert.Greater(m.Plays.Count, 0);
            Assert.IsTrue(m.Back());
            Assert.AreEqual("", m.SearchText);
            Assert.AreEqual(CallTab.Concept, m.Tab);
            Assert.IsTrue(m.Back());
            Assert.AreEqual(CallTab.AskCoach, m.Tab);
            Assert.IsFalse(m.Back(), "nothing above the suggestions: a play has to be called");
        }

        [Test]
        public void SearchFindsPlaysByNameAndByInitialLetter()
        {
            var m = Sheet();
            m.SetTab(CallTab.Formation);
            m.SetSearch("slant");
            Assert.Greater(m.Plays.Count, 0);
            foreach (var p in m.Plays) Assert.IsTrue(p.Name.ToLowerInvariant().Contains("slant") || p.FormationName.ToLowerInvariant().Contains("slant"), p.Name);
            m.ClearSearch();
            m.CycleLetter(1);
            Assert.AreEqual(1, m.SearchText.Length); Assert.IsTrue(m.SearchIsLetter);
            foreach (var p in m.Plays) Assert.AreEqual(char.ToUpperInvariant(m.SearchText[0]), char.ToUpperInvariant(p.Name[0]));
            m.AppendSearch('x'); Assert.IsFalse(m.SearchIsLetter, "typing turns the letter into a text search");
            m.ClearSearch(); Assert.AreEqual("", m.SearchText);
        }

        [Test]
        public void FlipAppliesToTheCalledPlay()
        {
            var m = Sheet();
            m.SetTab(CallTab.Formation);
            Assert.IsTrue(m.CanFlip);
            m.ToggleFlip();
            m.Confirm();
            Assert.IsNotNull(m.Chosen); Assert.IsTrue(m.Chosen.Mirrored, "the called play is the mirrored one");
        }

        [Test]
        public void DefenseSheetHasItsOwnGroups()
        {
            var m = Sheet(PlaySide.Defense);
            Assert.IsFalse(m.CanFlip);
            m.SetTab(CallTab.Formation);
            Assert.Greater(m.Groups.Count, 1);
            Assert.IsTrue(m.Plays.All(p => p.Side == PlaySide.Defense));
        }

        [Test]
        public void FilterWorksInsideAGroup()
        {
            var m = Sheet();
            m.SetTab(CallTab.Formation);
            int all = m.Plays.Count;
            m.CycleFilter(1);
            Assert.AreNotEqual(CallFilter.All, m.Filter);
            Assert.LessOrEqual(m.Plays.Count, all);
            foreach (var p in m.Plays) Assert.IsTrue(m.Filter == CallFilter.Run ? p.IsRun : (m.Filter == CallFilter.Pass || m.Filter == CallFilter.PlayAction || m.Filter == CallFilter.Screen) ? p.IsPass : true);
        }

        // ------------------------------------------------------------------------------------------------ route view
        static MatchFlow NewFlow(int seed = 5)
        {
            var cfg = new MatchConfig { QuarterMinutes = 5, RunoffScale = 0.5f, CpuVsCpu = true, Seed = seed, UserTeam = Team.Home };
            return new MatchFlow(cfg, MatchData.TeamByKey("buffalo_ironhorns"), MatchData.TeamByKey("miami_reefrunners"), Team.Away, new Rng(seed));
        }

        static PlaySimulation PassSim(MatchFlow f)
        {
            f.State.Phase = GamePhase.Scrimmage; f.State.BallSpot = 40; f.State.Down = 2; f.State.Distance = 8; f.State.Possession = Team.Home;
            Play o = f.Book.AllPlays.First(p => p.Name == "Slants" && p.FormationName == "Shotgun");
            Play d = f.Book.AllPlays.First(p => p.Name == "Cover 3 Sky" && p.FormationName == "Nickel");
            return f.CreateSim(o, d, false);
        }

        [Test]
        public void RouteViewDrawsEveryReceiverRouteWithReadNumbers()
        {
            var sim = PassSim(NewFlow());
            var go = new GameObject("RoutesTest");
            try
            {
                var ov = RouteOverlay.Create(go.transform);
                ov.Rebuild(sim, PlayFrame.For(Team.Home, 1), Color.red);
                int routes = 0; for (int i = 1; i < 11; i++) { var ra = sim.Agents[i].Assign as RouteAssignment; if (ra != null && ra.Waypoints.Count > 0) routes++; }
                Assert.Greater(routes, 1);
                Assert.AreEqual(routes, ov.RouteCount);
                Assert.AreEqual(routes, ov.Badges.Count);
                Assert.IsTrue(ov.Badges.Any(b => b.Read == 1 && b.Primary), "the first read is the primary route");
                var mf = go.GetComponentsInChildren<MeshFilter>(true);
                Assert.IsTrue(mf.Any(m => m.sharedMesh != null && m.sharedMesh.vertexCount > 0), "ribbon meshes were built");
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void RouteViewFollowsFlipsAndHotRoutes()
        {
            var sim = PassSim(NewFlow());
            int s0 = RouteOverlay.SignatureOf(sim);
            Assert.AreEqual(s0, RouteOverlay.SignatureOf(sim), "stable while nothing changes");
            if (sim.FlipOffense()) Assert.AreNotEqual(s0, RouteOverlay.SignatureOf(sim), "a flip changes the picture");
            int s1 = RouteOverlay.SignatureOf(sim);
            int hot = -1; for (int i = 1; i < 11; i++) if (sim.CanHotRoute(i)) { hot = i; break; }
            Assert.GreaterOrEqual(hot, 1);
            bool changed = false;
            foreach (var hr in new[] { HotRoute.Streak, HotRoute.Out, HotRoute.Curl, HotRoute.Slant })
                if (sim.SetHotRoute(hot, hr) && RouteOverlay.SignatureOf(sim) != s1) { changed = true; break; }
            Assert.IsTrue(changed, "a hot route redraws the route view");
        }

        [Test]
        public void VisibleTeamColourIsBrightAndSaturated()
        {
            foreach (var c in new[] { new Color(0.05f, 0.2f, 0.1f), new Color(0.1f, 0.1f, 0.35f), Color.black, Color.white, new Color(0.5f, 0.5f, 0.5f) })
            {
                float h, s, v; Color.RGBToHSV(RouteOverlay.Visible(c), out h, out s, out v);
                Assert.GreaterOrEqual(v, 0.99f);
                Assert.GreaterOrEqual(s, 0.64f);
            }
        }

        // ------------------------------------------------------------------------------------------------ passing and display
        [Test]
        public void PassInputConstantsMakeAShortTapAndATinyHold()
        {
            Assert.Less(MatchDirector.BulletHoldSeconds, 0.3f, "a tiny hold threshold");
            Assert.Greater(MatchDirector.BulletHoldSeconds, 0.08f, "but a tap is still a tap");
            Assert.Greater(MatchDirector.TapLobDistance, 12f);
        }

        [Test]
        public void HoldIsABulletAndTapIsALobOrTouch()
        {
            // v0.7 note 16: the swap of the old mapping (tap = bullet, hold = lob)
            Assert.AreEqual(ThrowStyle.Bullet, MatchDirector.ThrowStyleFor(MatchDirector.BulletHoldSeconds, 8f), "hold = bullet, even to a close receiver");
            Assert.AreEqual(ThrowStyle.Bullet, MatchDirector.ThrowStyleFor(0.6f, 30f));
            Assert.AreEqual(ThrowStyle.Touch, MatchDirector.ThrowStyleFor(0.05f, 8f), "a tap to a close receiver is a touch pass");
            Assert.AreEqual(ThrowStyle.Lob, MatchDirector.ThrowStyleFor(0.05f, MatchDirector.TapLobDistance + 1f), "a tap to a deep receiver is a lob");
            Assert.AreEqual(ThrowStyle.Lob, MatchDirector.ThrowStyleFor(MatchDirector.BulletHoldSeconds - 0.01f, 40f));
        }

        [Test]
        public void DisplayFitNeverExceedsTheMonitorAndKeepsTheAspect()
        {
            int w, h;
            PixSettings.FitSize(2560, 1440, 1920, 1080, true, out w, out h);
            Assert.AreEqual(1920, w); Assert.AreEqual(1080, h);
            PixSettings.FitSize(2560, 1440, 1920, 1080, false, out w, out h);
            Assert.LessOrEqual(w, 1880); Assert.LessOrEqual(h, 970, "a window leaves room for the title bar and the taskbar");
            Assert.AreEqual(16f / 9f, (float)w / h, 0.02f);
            PixSettings.FitSize(1920, 1080, 3840, 2160, true, out w, out h);
            Assert.AreEqual(1920, w); Assert.AreEqual(1080, h);
            PixSettings.FitSize(3440, 1440, 2560, 1440, true, out w, out h);
            Assert.LessOrEqual(w, 2560); Assert.LessOrEqual(h, 1440); Assert.AreEqual(3440f / 1440f, (float)w / h, 0.02f);
            PixSettings.FitSize(100, 100, 1920, 1080, true, out w, out h);
            Assert.GreaterOrEqual(w, 640); Assert.GreaterOrEqual(h, 480);
        }

        [Test]
        public void EveryAspectKeepsTheWholeReferenceRectangleOnScreen()
        {
            foreach (var s in new[] { new[] { 1280f, 720f }, new[] { 1920f, 1200f }, new[] { 2560f, 1080f }, new[] { 3440f, 1440f }, new[] { 1600f, 1200f }, new[] { 3840f, 2160f } })
            {
                Vector2 c = PixFootball.UI.PlayCallLayout.CanvasSizeFor(s[0], s[1]);
                float scale = s[0] / c.x;
                Assert.AreEqual(s[1] / c.y, scale, 0.001f, "uniform scale");
                Assert.GreaterOrEqual(c.x, 1919.5f); Assert.GreaterOrEqual(c.y, 1079.5f);
            }
        }
    }
}
