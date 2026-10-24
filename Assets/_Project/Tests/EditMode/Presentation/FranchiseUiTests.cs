using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.UI;
using PixFootball.UI.Franchise;
using PixFootball.UI.Glass;
using UnityEngine;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>v0.8 Franchise UI: view models, navigation and the career flow driven through the UI controller (no scene needed).</summary>
    public class FranchiseUiTests
    {
        static CareerSave Fresh(int seed, int team = 5)
        {
            return CareerFlow.NewCareer(new CareerOptions { Seed = seed, UserTeamId = team });
        }

        static FranchiseController Ctl(CareerSave s) { return new FranchiseController(s, new CareerPrefs(), 0); }

        // ------------------------------------------------------------------ text and sort helpers
        [Test]
        public void MoneyAndTextHelpers()
        {
            Assert.AreEqual("$12.5M", Fmt.MoneyShort(12500000));
            Assert.AreEqual("$850K", Fmt.MoneyShort(850000));
            Assert.AreEqual("-$3.0M", Fmt.MoneyShort(-3000000));
            Assert.AreEqual("1 yr", Fmt.Years(1));
            Assert.AreEqual("3 yrs", Fmt.Years(3));
            Assert.AreEqual("6'3\"", Fmt.Height(75));
            Assert.AreEqual("2 offers", Fmt.Plural(2, "offer", "offers"));
            Assert.AreEqual("Pocket", CardText.Archetype("QbPocket"));
            Assert.AreEqual("Deep Threat", CardText.Archetype("WrDeepThreat"));
            Assert.AreEqual("12:07", DraftText.Clock(727f).Replace("12:07", "12:07"));
            Assert.AreEqual("0:00", DraftText.Clock(-4f));
        }

        [Test]
        public void RosterSortIsStableAndFollowsTheColumn()
        {
            var s = Fresh(301);
            var rows = CareerViews.RosterTable(s, s.UserTeam, true);
            RosterSort.Sort(rows, 3, true);
            for (int i = 1; i < rows.Count; i++) Assert.GreaterOrEqual(rows[i - 1].Ovr, rows[i].Ovr);
            RosterSort.Sort(rows, 4, false);
            for (int i = 1; i < rows.Count; i++) Assert.LessOrEqual(rows[i - 1].Age, rows[i].Age);
            RosterSort.Sort(rows, 1, false);
            for (int i = 1; i < rows.Count; i++) Assert.LessOrEqual(string.CompareOrdinal(rows[i - 1].Name, rows[i].Name), 0);
            var again = new List<RosterRow>(rows); RosterSort.Sort(again, 2, false); var third = new List<RosterRow>(rows); RosterSort.Sort(third, 2, false);
            for (int i = 0; i < again.Count; i++) Assert.AreEqual(again[i].PlayerId, third[i].PlayerId, "same input, same order");
        }

        [Test]
        public void PositionFilterGroupsThePositions()
        {
            Assert.IsTrue(PosFilter.Match(0, Position.K));
            Assert.IsTrue(PosFilter.Match(5, Position.LT) && PosFilter.Match(5, Position.C) && !PosFilter.Match(5, Position.DT));
            Assert.IsTrue(PosFilter.Match(8, Position.CB) && PosFilter.Match(8, Position.FS) && !PosFilter.Match(8, Position.MLB));
            Assert.IsTrue(PosFilter.Match(2, Position.FB) && PosFilter.Match(9, Position.P));
        }

        [Test]
        public void CapSortOrdersTheLedger()
        {
            var s = Fresh(302);
            var rows = CapManager.LedgerRows(s.UserTeam);
            CapSort.Sort(rows, 6, true);
            for (int i = 1; i < rows.Count; i++) Assert.GreaterOrEqual(rows[i - 1].CapHitThisYear, rows[i].CapHitThisYear);
            CapSort.Sort(rows, 9, false);
            for (int i = 1; i < rows.Count; i++) Assert.LessOrEqual(rows[i - 1].DeadIfReleased, rows[i].DeadIfReleased);
        }

        [Test]
        public void OfferEditorOptionsSpanTheAskAndStayAboveTheMinimum()
        {
            long ask = 12000000;
            var o = OfferEditor.AavOptions(ask, 850000);
            Assert.Greater(o.Length, 20);
            Assert.Less(o[0], ask); Assert.Greater(o[o.Length - 1], ask);
            for (int i = 1; i < o.Length; i++) Assert.Greater(o[i], o[i - 1]);
            Assert.AreEqual(ask, o[OfferEditor.IndexOfClosest(o, ask)], 130000);
            var cheap = OfferEditor.AavOptions(900000, 850000);
            Assert.GreaterOrEqual(cheap[0], 850000);
        }

        // ------------------------------------------------------------------ the hero tile
        [Test]
        public void TheHeroFollowsTheCalendar()
        {
            var s = Fresh(303);
            var c = Ctl(s);
            Assert.AreEqual(HeroAction.Advance, c.Hero().PrimaryAction, "preseason: start the season");
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            for (int guard = 0; guard < 6 && !c.UserHasGame; guard++) c.Advance();   // a bye week has no game
            var h = c.Hero();
            Assert.IsTrue(h.IsGame, "a game week shows the matchup card and the Play button");
            Assert.AreEqual(HeroAction.PlayGame, h.PrimaryAction);
            Assert.IsNotNull(h.Next); Assert.IsNotNull(h.Next.You); Assert.IsNotNull(h.Next.Them);
            var seen = new HashSet<HeroAction>();
            foreach (var phase in new[] { CareerPhase.Awards, CareerPhase.ReSign, CareerPhase.Scouting, CareerPhase.Draft, CareerPhase.RookieSigning, CareerPhase.FreeAgency, CareerPhase.TrainingCamp })
            {
                CareerFlow.AdvanceUntil(s, phase);
                var hh = c.Hero();
                Assert.IsFalse(string.IsNullOrEmpty(hh.Title), phase + " has a title");
                Assert.IsFalse(string.IsNullOrEmpty(hh.Primary), phase + " has a primary button");
                Assert.Greater(hh.Step, 0, "offseason step number");
                seen.Add(hh.PrimaryAction);
            }
            Assert.IsTrue(seen.Contains(HeroAction.OpenResign) && seen.Contains(HeroAction.OpenDraft) && seen.Contains(HeroAction.OpenFreeAgency));
        }

        // ------------------------------------------------------------------ the controller
        [Test]
        public void SimOptionsAndWeekSimsMoveTheCalendar()
        {
            var s = Fresh(304);
            var c = Ctl(s);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            int w0 = c.Week;
            Assert.AreEqual(1, w0);
            var opts = c.SimOptions();
            Assert.GreaterOrEqual(opts.Count, 4);
            c.SimToWeek(5);
            Assert.AreEqual(5, c.Week);
            Assert.AreEqual(CareerPhase.RegularSeason, s.Phase);
            c.SimToPlayoffs();
            Assert.AreEqual(CareerPhase.Playoffs, s.Phase);
            c.SimToOffseason();
            Assert.AreEqual(CareerPhase.Awards, s.Phase);
            Assert.AreEqual(0, CareerValidator.Check(s, false).Count);
        }

        [Test]
        public void ABlockedAdvanceExplainsItself()
        {
            var s = Fresh(305);
            var c = Ctl(s);
            CareerFlow.AdvanceUntil(s, CareerPhase.ReSign);
            // make the user over the cap: the step must be refused with the reason
            s.UserTeam.DeadCap += (s.UserTeam.SalaryCap - s.UserTeam.CapUsed) + 6000000L;
            var b = c.Blockers();
            Assert.Greater(b.Count, 0);
            var r = c.Advance();
            Assert.IsFalse(r.Ok);
            StringAssert.Contains("cap", c.Message.ToLowerInvariant());
            Assert.AreEqual(CareerPhase.ReSign, s.Phase);
            var left = c.AutoResolve();
            Assert.AreEqual(0, left.Count, "the auto resolve gets under the cap");
        }

        [Test]
        public void BoxScoresOfSimmedGamesAreRemembered()
        {
            var s = Fresh(306);
            var c = Ctl(s);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            for (int guard = 0; guard < 6 && !c.UserHasGame; guard++) c.Advance();
            var g = CareerFlow.UserGame(s);
            var r = c.SimUserGameNow();
            Assert.IsNotNull(r);
            Assert.IsNotNull(c.BoxOf(g));
            var rows = BoxScoreModel.TeamRows(r);
            Assert.GreaterOrEqual(rows.Count, 8);
            Assert.AreEqual((r.Home.TotalYards).ToString(), rows[0].Home);
            Assert.AreEqual(6, BoxScoreModel.LineScore(r.Home).Length);
            c.Advance();
            Assert.IsNotNull(c.BoxOf(g), "still there after the week advanced");
        }

        // ------------------------------------------------------------------ session, match config, saves
        [Test]
        public void TheMatchConfigUsesTheCareerTeamsAndTheUsersSide()
        {
            var s = Fresh(307);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            for (int guard = 0; guard < 6 && CareerFlow.SetupUserGame(s) == null; guard++) CareerFlow.Advance(s);
            var setup = CareerFlow.SetupUserGame(s);
            Assert.IsNotNull(setup);
            var cfg = CareerSession.BuildMatchConfig(setup, new CareerPrefs { Difficulty = 2, QuarterMinutes = 8 }, s);
            Assert.AreEqual(setup.Home.Key, cfg.HomeId); Assert.AreEqual(setup.Away.Key, cfg.AwayId);
            Assert.AreEqual(setup.UserIsHome ? PixFootball.Core.Team.Home : PixFootball.Core.Team.Away, cfg.UserTeam);
            Assert.AreEqual(8, cfg.QuarterMinutes); Assert.AreEqual(Difficulty.AllPro, cfg.Difficulty); Assert.IsFalse(cfg.CollegeRules);
            Assert.AreNotEqual(0, cfg.Seed);
            // conditions are deterministic and a dome is always clear
            WeatherChoice w1, w2; TimeChoice t1, t2;
            CareerSession.PickConditions(1234, setup.Home, 6, out w1, out t1); CareerSession.PickConditions(1234, setup.Home, 6, out w2, out t2);
            Assert.AreEqual(w1, w2); Assert.AreEqual(t1, t2);
            var dome = new LeagueTeam { StadiumName = "Skyline Dome" };
            for (int seed = 1; seed < 60; seed++) { WeatherChoice w; TimeChoice t; CareerSession.PickConditions(seed, dome, 14, out w, out t); Assert.AreEqual(WeatherChoice.Clear, w); }
            bool snow = false;
            for (int seed = 1; seed < 400; seed++) { WeatherChoice w; TimeChoice t; CareerSession.PickConditions(seed, setup.Home.IsDome ? new LeagueTeam() : setup.Home, 3, out w, out t); if (w == WeatherChoice.Snow) snow = true; }
            Assert.IsFalse(snow, "no snow early in the season");
        }

        [Test]
        public void SaveSlotsRoundTripTheCareerAndTheMeta()
        {
            string dir = Path.Combine(Path.GetTempPath(), "pix_career_test_" + Guid.NewGuid().ToString("N"));
            CareerSession.DirOverride = dir;
            try
            {
                Assert.IsNull(CareerSession.Latest());
                var prefs = new CareerPrefs { Difficulty = 0, QuarterMinutes = 3 };
                var s = CareerSession.StartNew(7, 308, prefs, 2, "Test franchise", false);
                Assert.IsTrue(CareerSession.Active);
                var metas = CareerSession.Slots();
                Assert.IsTrue(metas[1].Valid); Assert.IsFalse(metas[0].Valid);
                Assert.AreEqual(3, metas[1].Prefs.QuarterMinutes);
                Assert.AreEqual(s.UserTeam.FullName, metas[1].TeamName);
                Assert.AreEqual(1, CareerStartModel.DefaultSlot(metas));
                StringAssert.Contains("replace", CareerStartModel.SlotLabel(metas[1]));
                long fp = CareerSession.Save.Pro.Teams[3].CapUsed;
                var c = FranchiseController.FromSession();
                CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
                c.Advance();   // autosaves
                Assert.AreEqual(1, c.Autosaves);
                int week = s.Season.Week; var phase = s.Phase;
                CareerSession.Close();
                string err;
                Assert.IsTrue(CareerSession.Load(2, out err), err);
                Assert.AreEqual(phase, CareerSession.Save.Phase); Assert.AreEqual(week, CareerSession.Save.Season.Week);
                Assert.AreEqual(3, CareerSession.Prefs.QuarterMinutes);
                Assert.IsFalse(CareerSession.Load(4, out err));
                Assert.AreEqual(2, CareerSession.Latest().Slot);
                CareerSession.DeleteSlot(2);
                Assert.IsNull(CareerSession.Latest());
            }
            finally { CareerSession.Close(); CareerSession.DirOverride = null; if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void APlayedGameResultIsPostedToTheCareer()
        {
            var s = Fresh(311);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            for (int guard = 0; guard < 6 && CareerFlow.SetupUserGame(s) == null; guard++) CareerFlow.Advance(s);
            var setup = CareerFlow.SetupUserGame(s);
            CareerSession.Open(s, new CareerPrefs(), 0);
            try
            {
                CareerSession.BeginPending(setup.Game.Id);
                Assert.IsTrue(CareerSession.Pending);
                Assert.AreSame(s.Pro, MatchData.League, "the match scene plays with the career league");
                var r = QuickSim.SimGame(setup.Home, setup.Away, setup.Rules, new Rng(5), setup.Options);
                string msg = CareerSession.CompleteMatch(r);
                StringAssert.Contains("in the books", msg);
                Assert.IsTrue(CareerFlow.FindGame(s, setup.Game.Id).Played);
                Assert.AreEqual(r.HomeScore, CareerFlow.FindGame(s, setup.Game.Id).HomeScore);
                Assert.IsFalse(CareerSession.Pending);
                CareerSession.OnMenuLoaded();
                Assert.AreNotSame(s.Pro, MatchData.League, "the exhibition uses the standalone league again");
                var c = FranchiseController.FromSession();
                c.AbsorbPending();
                Assert.IsNotNull(c.BoxOf(CareerFlow.FindGame(s, setup.Game.Id)));
            }
            finally { CareerSession.Close(); }
        }

        [Test]
        public void BoxScoresOfTheUsersGamesSurviveASaveAndLoad()
        {
            var s = Fresh(313);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            for (int guard = 0; guard < 6 && CareerFlow.SetupUserGame(s) == null; guard++) CareerFlow.Advance(s);
            var c = Ctl(s);
            var r = c.SimUserGameNow();
            Assert.IsNotNull(r);
            Assert.AreEqual(1, s.UserBoxes.Count, "the user's game box score is stored in the save");
            var back = JsonUtility.FromJson<CareerSave>(JsonUtility.ToJson(s));
            back.Bind();
            var c2 = Ctl(back);
            Assert.AreEqual(1, c2.BoxCount);
            Assert.IsNotNull(c2.BoxOf(CareerFlow.UserGame(back)), "the reloaded career still shows the box score");
            Assert.Greater(back.UserBoxes[0].Result.PlayerLines.Count, 5);
        }

        [Test]
        public void ThePlayerCardShowsTheInMatchEffectSizes()
        {
            var d = AbilityCatalog.Get("ankle_shaker");
            Assert.IsNotNull(d);
            var fx = d.Effects[0];
            float shown = fx.At(3) * AbilityCatalog.Gain(fx.Hook, fx.Context);
            var text = AbilityCatalog.Describe(new PlayerAbility { Id = "ankle_shaker", Tier = 3 });
            StringAssert.Contains("+" + shown.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), text);
            Assert.AreEqual(0.5f, AbilityCatalog.Gain(SimHook.JukeSuccess), 0.0001f);
        }

        [Test]
        public void CareerStartRulesPickSeedsAndPrefs()
        {
            Assert.AreEqual(MatchData.LeagueSeed, CareerStartModel.SeedFor(true, 99));
            Assert.AreNotEqual(MatchData.LeagueSeed, CareerStartModel.SeedFor(false, 12345));
            var p = CareerStartModel.Prefs(2, 4);
            Assert.AreEqual(2, p.Difficulty); Assert.AreEqual(15, p.QuarterMinutes);
            StringAssert.Contains("All-Pro", CareerStartModel.PrefsText(p));
        }

        // ------------------------------------------------------------------ the list control and the shell
        [Test]
        public void TheTableScrollsItsWindowWithTheCursor()
        {
            var root = new GameObject("TestCanvas", typeof(RectTransform)).GetComponent<RectTransform>();
            try
            {
                var cols = new List<TableColumn> { new TableColumn("N", 1f, i => "row " + i) };
                var t = GlassTable.Create(root, "T", 600f, GlassTable.HeaderHeight + 5 * 50f, cols);
                Assert.AreEqual(5, t.VisibleRows);
                t.SetCount(40);
                Assert.AreEqual(0, t.Top);
                t.SetCursor(7);
                Assert.AreEqual(7, t.Cursor); Assert.AreEqual(3, t.Top, "the cursor row is the last visible one");
                t.SetCursor(39);
                Assert.AreEqual(35, t.Top);
                t.SetCursor(0);
                Assert.AreEqual(0, t.Top);
                t.SetCount(3);
                Assert.AreEqual(0, t.Top); Assert.AreEqual(0, t.Cursor);
                t.SetCount(0);
                Assert.AreEqual(0, t.Count);
            }
            finally { UnityEngine.Object.DestroyImmediate(root.gameObject); }
        }

        [Test]
        public void EveryFranchisePageBuildsAndRefreshesOnARealCareer()
        {
            var s = Fresh(309);
            CareerFlow.AdvanceUntil(s, CareerPhase.RegularSeason);
            for (int i = 0; i < 3; i++) CareerFlow.Advance(s);
            var ctl = Ctl(s);
            var canvas = new GameObject("TestCanvas", typeof(RectTransform)).GetComponent<RectTransform>();
            try
            {
                var ui = FranchiseUi.Build(canvas, ctl, new FranchiseHost());
                ui.Open();
                for (int tab = 0; tab < FranchiseNav.Tabs.Length; tab++)
                {
                    int subs = Math.Max(1, FranchiseNav.SubTabs[tab].Length);
                    for (int sub = 0; sub < subs; sub++)
                    {
                        ui.ShowTab(tab, sub);
                        var page = ui.PageOf(tab, sub);
                        Assert.IsTrue(page.Built, FranchiseNav.Tabs[tab] + " " + sub);
                        page.Refresh();
                    }
                }
                ui.ShowOverlay(new DraftPage());
                Assert.IsTrue(ui.HasOverlay);
                ui.CloseOverlay(false);
            }
            finally { UnityEngine.Object.DestroyImmediate(canvas.gameObject); }
        }

        // ------------------------------------------------------------------ the whole year through the controller
        [Test]
        public void SmokeCreateSimToTheDraftRunItAndStartTheNextSeason()
        {
            var s = Fresh(310, 9);
            var c = Ctl(s);
            int year = s.Year;
            c.SimToOffseason();
            Assert.AreEqual(CareerPhase.Awards, s.Phase);
            c.AdvanceUntil(CareerPhase.Scouting);
            Assert.AreEqual(CareerPhase.Scouting, s.Phase);
            var board = CareerDraft.Board(s);
            Assert.Greater(board.Count, 150);
            var hero = c.Hero();
            Assert.AreEqual(HeroAction.OpenDraft, hero.PrimaryAction);
            int picks = c.AutoDraft();
            Assert.Greater(picks, 0);
            Assert.AreEqual(CareerPhase.RookieSigning, s.Phase, "the auto draft ends with the rookies waiting");
            Assert.IsTrue(s.Draft.Finished);
            Assert.Greater(CareerDraft.SignAllRookies(s, s.UserTeamId), 0);
            Assert.AreEqual(HeroAction.OpenDraft, c.Hero().PrimaryAction, "the rookie step opens the draft screen");
            c.SimSeason();   // free agency, camp and into the next preseason with the auto pilot
            Assert.AreEqual(CareerPhase.Preseason, s.Phase);
            Assert.AreEqual(year + 1, s.Year);
            Assert.AreEqual(0, CareerValidator.Check(s, false).Count, "consistent at the preseason (the final cuts happen when the season starts)");
            Assert.AreEqual(HeroAction.Advance, c.Hero().PrimaryAction);
        }
    }
}
