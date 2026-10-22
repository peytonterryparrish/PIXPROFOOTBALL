using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.UI.Glass;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>Pure logic of the tile-hub main menu and the matchup card: headlines from the league data, unit ratings, venue text, tile navigation and stage framing.</summary>
    public class MenuHubTests
    {
        [Test]
        public void HeadlinesAreDeterministicAndFromTheLeague()
        {
            var a = MenuHubModel.Headlines(MatchData.League);
            var b = MenuHubModel.Headlines(MatchData.League);
            Assert.GreaterOrEqual(a.Count, 8); Assert.LessOrEqual(a.Count, 12);
            CollectionAssert.AreEqual(a, b);
            var best = MatchData.League.Teams[0]; foreach (var t in MatchData.League.Teams) if (t.TeamOverall() > best.TeamOverall()) best = t;
            StringAssert.Contains(best.Nickname, a[0]);
            foreach (var h in a)
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(h));
                foreach (var banned in new[] { "NFL", "Madden", "EA ", "ESPN" }) StringAssert.DoesNotContain(banned, h);
            }
            Assert.IsTrue(a.Exists(h => h.StartsWith("Game of the week")), "a game of the week headline");
        }

        [Test]
        public void HeadlinesSurviveATinyLeague()
        {
            var l = MenuHubModel.Headlines(null);
            Assert.AreEqual(1, l.Count);
        }

        [Test]
        public void UnitRatingsAreSensible()
        {
            foreach (var t in MatchData.League.Teams)
            {
                var r = MenuHubModel.Ratings(t);
                Assert.That(r.Ovr, Is.InRange(40, 99)); Assert.That(r.Off, Is.InRange(40, 99)); Assert.That(r.Def, Is.InRange(40, 99));
            }
        }

        [Test]
        public void VenueAndKickoffText()
        {
            var home = MatchData.League.Teams[0];
            Assert.AreEqual("8:15 PM", MenuHubModel.KickoffTime(TimeChoice.Night));
            Assert.AreEqual("1:05 PM", MenuHubModel.KickoffTime(TimeChoice.Day));
            Assert.AreEqual("TBD", MenuHubModel.KickoffTime(TimeChoice.Random));
            StringAssert.Contains(home.City, MenuHubModel.VenueLine(home, TimeChoice.Dusk));
            StringAssert.StartsWith("5:40 PM", MenuHubModel.VenueLine(home, TimeChoice.Dusk));
            Assert.AreEqual("0-0", MenuHubModel.Record(home));
        }

        [Test]
        public void TileNavigationReachesEveryTileAndSkipsLockedOnes()
        {
            // the HOME page: hero 2x2 (0,0), wide (2,0), small (2,1) (3,1), locked wide (0,2), small (2,2), locked (3,2)
            float W = 288f, H = 212f, G = 20f;
            Vector2 C(int col, int row, int cs, int rs) { float w = cs * W + (cs - 1) * G, h = rs * H + (rs - 1) * G; return new Vector2(col * (W + G) + w * 0.5f, -(row * (H + G) + h * 0.5f)); }
            Vector2 S(int cs, int rs) { return new Vector2(cs * W + (cs - 1) * G, rs * H + (rs - 1) * G); }
            var cells = new List<HubNav.Cell>
            {
                new HubNav.Cell(C(0, 0, 2, 2), S(2, 2), true), new HubNav.Cell(C(2, 0, 2, 1), S(2, 1), true), new HubNav.Cell(C(2, 1, 1, 1), S(1, 1), true), new HubNav.Cell(C(3, 1, 1, 1), S(1, 1), true),
                new HubNav.Cell(C(0, 2, 2, 1), S(2, 1), false), new HubNav.Cell(C(2, 2, 1, 1), S(1, 1), false), new HubNav.Cell(C(3, 2, 1, 1), S(1, 1), false)
            };
            int r = HubNav.Neighbor(cells, 0, Vector2.right);
            Assert.That(r, Is.EqualTo(1).Or.EqualTo(2), "right of the hero is one of the tiles beside it");
            Assert.That(HubNav.Neighbor(cells, 1, Vector2.down), Is.EqualTo(2).Or.EqualTo(3), "down from the wide tile lands on a small tile");
            Assert.AreEqual(3, HubNav.Neighbor(cells, 2, Vector2.right));
            Assert.AreEqual(2, HubNav.Neighbor(cells, 3, Vector2.left));
            Assert.AreEqual(0, HubNav.Neighbor(cells, 2, Vector2.left));
            Assert.AreEqual(-1, HubNav.Neighbor(cells, 0, Vector2.down), "locked tiles are never targets");
            Assert.AreEqual(-1, HubNav.Neighbor(cells, 3, Vector2.right));
            // every selectable tile can be reached from the first
            var seen = new HashSet<int> { 0 }; var queue = new Queue<int>(); queue.Enqueue(0);
            var links = HubNav.Links(cells);
            while (queue.Count > 0) { int i = queue.Dequeue(); foreach (int n in links[i]) if (n >= 0 && seen.Add(n)) queue.Enqueue(n); }
            CollectionAssert.AreEquivalent(new[] { 0, 1, 2, 3 }, seen);
        }

        [Test]
        public void TickerJoinsHeadlinesWithAnAccentSeparator()
        {
            string s = GlassTicker.Join(new[] { "One", "Two" });
            StringAssert.StartsWith("One", s); StringAssert.EndsWith("Two", s); StringAssert.Contains("//", s);
        }

        [Test]
        public void StageKeepsThePlayerInTheFreeSpaceForEveryAspect()
        {
            float hub = 96f + 1212f;
            float f169 = GlassStage.PlayerScreenFraction(1920f, hub), f219 = GlassStage.PlayerScreenFraction(2580f, hub), f43 = GlassStage.PlayerScreenFraction(1920f, 96f + 1728f);
            Assert.That(f169, Is.InRange(0.75f, 0.92f));
            Assert.Less(f219, f169, "on a wider canvas the free space is larger, so the player sits further from the right edge");
            Assert.That(f219 * 2580f, Is.GreaterThan(hub + 300f));
            Assert.That(f43, Is.InRange(0.55f, 0.92f));
        }
    }
}
