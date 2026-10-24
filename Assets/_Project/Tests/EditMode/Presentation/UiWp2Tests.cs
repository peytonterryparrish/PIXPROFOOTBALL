using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Gameplay;
using PixFootball.UI.Glass;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.11 UI-WP2: the hub plan (direction 16 layout, no college mode), the loading tips and the small pure helpers of the front end.</summary>
    public class UiWp2Tests
    {
        static PlanInput Input(bool resume = true, bool rematch = true) { return new PlanInput { CareerLine = "Continue: Test, Regular season", ResumeBadge = resume ? "RESUME" : null, HasRematch = rematch, RematchLine = "BUF at MIA" }; }

        [Test]
        public void EveryPageFitsTheBlockWithoutOverlaps()
        {
            var b = MenuHubPlan.BlockSize;
            for (int p = 0; p < MenuHubPlan.TabNames.Length; p++)
            {
                var tiles = MenuHubPlan.Page(p, Input());
                Assert.GreaterOrEqual(tiles.Count, 6, "page " + p);
                foreach (var t in tiles)
                {
                    Assert.GreaterOrEqual(t.X, 0f, t.Id); Assert.GreaterOrEqual(t.Y, 0f, t.Id);
                    Assert.LessOrEqual(t.X + t.W, b.x + 0.5f, "page " + p + " " + t.Id); Assert.LessOrEqual(t.Y + t.H, b.y + 0.5f, "page " + p + " " + t.Id);
                }
                for (int i = 0; i < tiles.Count; i++)
                    for (int j = i + 1; j < tiles.Count; j++)
                        Assert.IsFalse(MenuHubPlan.Overlap(tiles[i], tiles[j]), "page " + p + ": " + tiles[i].Id + " overlaps " + tiles[j].Id);
            }
        }

        [Test]
        public void HomePageFollowsTheMock()
        {
            var t = MenuHubPlan.Page(MenuHubPlan.Home, Input());
            var hero = t.First(x => x.Id == "play");
            Assert.AreEqual(780f, hero.W); Assert.AreEqual(470f, hero.H); Assert.IsTrue(hero.Hero);
            Assert.AreEqual(380f, t.First(x => x.Id == "franchise").W); Assert.AreEqual(292f, t.First(x => x.Id == "franchise").H);
            Assert.AreEqual(500f, t.First(x => x.Id == "freeroam").W);
            Assert.AreEqual(240f, t.First(x => x.Id == "settings").W); Assert.AreEqual(238f, t.First(x => x.Id == "settings").H);
            Assert.AreEqual("RESUME", t.First(x => x.Id == "franchise").Badge);
        }

        [Test]
        public void NoCollegeOrHeismanModeAnywhereInTheHub()
        {
            for (int p = 0; p < MenuHubPlan.TabNames.Length; p++)
                foreach (var t in MenuHubPlan.Page(p, Input()))
                {
                    string all = (t.Id + " " + t.Title + " " + t.Sub + " " + t.Badge).ToLowerInvariant();
                    StringAssert.DoesNotContain("college", all); StringAssert.DoesNotContain("heisman", all); StringAssert.DoesNotContain("draft fever", all);
                    foreach (var banned in new[] { "nfl", "madden", "espn" }) StringAssert.DoesNotContain(banned, all);
                }
            CollectionAssert.DoesNotContain(MenuHubPlan.TabNames.Select(x => x.ToLowerInvariant()).ToList(), "college");
        }

        [Test]
        public void OnlyTheFourFutureFeaturesAreLockedAndNothingElseIs()
        {
            var locked = new HashSet<string>();
            for (int p = 0; p < MenuHubPlan.TabNames.Length; p++) foreach (var t in MenuHubPlan.Page(p, Input())) if (t.Locked) locked.Add(t.Id);
            CollectionAssert.AreEquivalent(new[] { "online", "superstar", "trophy", "minigames" }, locked);
            foreach (var id in new[] { "play", "franchise", "freeroam", "settings", "quit" })
                foreach (var t in MenuHubPlan.Page(MenuHubPlan.Home, Input()).Where(x => x.Id == id)) Assert.IsFalse(t.Locked, id);
        }

        [Test]
        public void EveryPageIsReachableByTheDPadFromItsFirstTile()
        {
            for (int p = 0; p < MenuHubPlan.TabNames.Length; p++)
            {
                var tiles = MenuHubPlan.Page(p, Input());
                var cells = tiles.Select(t => new HubNav.Cell(t.Center(MenuHubPlan.BlockSize.y), new Vector2(t.W, t.H), t.Selectable)).ToList();
                int first = tiles.FindIndex(t => t.Selectable); Assert.GreaterOrEqual(first, 0);
                var links = HubNav.Links(cells);
                var seen = new HashSet<int> { first }; var q = new Queue<int>(); q.Enqueue(first);
                while (q.Count > 0) { int i = q.Dequeue(); foreach (int n in links[i]) if (n >= 0 && seen.Add(n)) q.Enqueue(n); }
                for (int i = 0; i < tiles.Count; i++) if (tiles[i].Selectable) Assert.IsTrue(seen.Contains(i), "page " + p + ": " + tiles[i].Id + " is not reachable");
            }
        }

        [Test]
        public void SettingsTilesOpenTheirPages()
        {
            Assert.AreEqual(0, MenuHubPlan.SettingsPageOf("video")); Assert.AreEqual(1, MenuHubPlan.SettingsPageOf("gameplay")); Assert.AreEqual(2, MenuHubPlan.SettingsPageOf("camera"));
            Assert.AreEqual(3, MenuHubPlan.SettingsPageOf("broadcast")); Assert.AreEqual(4, MenuHubPlan.SettingsPageOf("audio")); Assert.AreEqual(-1, MenuHubPlan.SettingsPageOf("play"));
            var t = MenuHubPlan.Page(MenuHubPlan.Settings, new PlanInput { WatchLine = "Controller input: Off (watch)" });
            StringAssert.Contains("Off (watch)", t.First(x => x.Id == "allsettings").Sub);
        }

        [Test]
        public void RematchIsLockedUntilAGameWasSetUp()
        {
            Assert.IsFalse(MenuHubPlan.Page(MenuHubPlan.Home, Input(true, true)).First(x => x.Id == "rematch").Locked);
            Assert.IsTrue(MenuHubPlan.Page(MenuHubPlan.Home, Input(true, false)).First(x => x.Id == "rematch").Locked);
        }

        [Test]
        public void WatchTipsDescribeTheWatchControlsAndFillTheShell()
        {
            Assert.AreEqual(LoadingContent.Count, LoadingContent.WatchTips.Length); Assert.AreEqual(LoadingContent.Count, LoadingContent.PlayTips.Length);
            Assert.AreEqual(GlassLoading.Tips.Length, LoadingContent.Count);
            foreach (var s in LoadingContent.WatchTips) { Assert.IsFalse(string.IsNullOrWhiteSpace(s)); StringAssert.DoesNotContain("receiver", s.ToLowerInvariant()); StringAssert.DoesNotContain("quarterback", s.ToLowerInvariant()); }
            StringAssert.Contains("Start", LoadingContent.WatchTips[0]);
            Assert.AreEqual(LoadingContent.Pick(true, 3), LoadingContent.Pick(true, 3 + LoadingContent.Count));
            Assert.AreEqual(LoadingContent.WatchTips[2], LoadingContent.Pick(true, -4));
            Assert.AreEqual("WATCH GAME", LoadingContent.Subtitle(GameSession.MatchScene, true));
            Assert.AreEqual("KICKOFF", LoadingContent.Subtitle(GameSession.MatchScene, false));
        }

        [Test]
        public void TopTeamsAreTheBestByOverall()
        {
            var top = MenuHubModel.TopTeams(MatchData.League, 4);
            Assert.AreEqual(4, top.Count);
            for (int i = 1; i < top.Count; i++) Assert.GreaterOrEqual(top[i - 1].TeamOverall(), top[i].TeamOverall());
            foreach (var t in MatchData.League.Teams) if (!top.Contains(t)) Assert.LessOrEqual(t.TeamOverall(), top[3].TeamOverall());
            Assert.AreEqual(0, MenuHubModel.TopTeams(null, 4).Count);
            StringAssert.Contains(" at ", MenuHubModel.RematchLine("MIA", "BUF", "5 MIN QUARTERS"));
        }


        [Test]
        public void TileTextMeetsTheContrastBar()
        {
            var t = GlassTokens.Tile();
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.textPrimary, t.tileFill), 7f, "white on a tile");
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.textMuted, t.rowFill), 4.5f, "muted text on a row");
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.onGold, t.gold), 7f, "text on the gold badge");
            Assert.GreaterOrEqual(GlassTokens.Contrast(t.textSecondary, t.tileFill), 4.5f, "sub line on a tile");
        }
        [Test]
        public void TextOnTheHubIsNeverBelowTwentyUnits()
        {
            // the plan only carries text; the builder clamps with Fit(minSize 20+). Captions must stay short enough for 20-unit type in the narrowest tile (230 wide)
            foreach (var t in MenuHubPlan.Page(MenuHubPlan.Home, Input())) if (t.W < 260f) Assert.LessOrEqual(t.Title.Length, 12, t.Id);
        }
    }
}
