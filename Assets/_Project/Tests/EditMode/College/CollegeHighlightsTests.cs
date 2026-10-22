using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>Watching college games: condensed highlights from the physical game simulation, replayable plays, and posting the watched game to the season.</summary>
    public class CollegeHighlightsTests
    {
        private static CareerSave _s;
        private static CareerSave S { get { return _s ?? (_s = CollegeTestUtil.InSeason(91, 3)); } }

        private static CollegeGameSetup Setup(CareerSave s, int index = 0) { return CollegeNet.SetupGame(s, CollegeNet.GamesThisWeek(s)[index].Id); }

        [Test]
        public void TheTopPlaysOfACollegeGameAreExtractedWithTheDataTheViewNeeds()
        {
            CollegeGameSetup setup = Setup(S);
            CollegeHighlightReel reel = CollegeHighlights.Generate(setup, 10);
            Assert.AreSame(setup, reel.Setup);
            Assert.Greater(reel.TotalPlays, 100, "a whole game on the physical engine");
            Assert.That(reel.HomeScore + reel.AwayScore, Is.InRange(3, 160));
            Assert.AreEqual(10, reel.Highlights.Count);
            var idx = reel.Highlights.Select(h => h.PlayIndex).ToList();
            Assert.AreEqual(idx.OrderBy(x => x).ToList(), idx, "highlights are in game order");
            Assert.AreEqual(Enumerable.Range(1, 10).ToList(), reel.Highlights.Select(h => h.Rank).OrderBy(x => x).ToList(), "ranks 1..10 by importance");
            Assert.IsTrue(reel.Highlights.All(h => h.PlayId == h.GameSeed + ":" + h.PlayIndex));
            Assert.IsTrue(reel.Highlights.All(h => !string.IsNullOrEmpty(h.Headline) && h.Description.StartsWith("Q")));
            Assert.IsTrue(reel.Highlights.Any(h => h.Kind == HighlightKind.Touchdown || h.Kind == HighlightKind.DefensiveTouchdown), "touchdowns make the reel");
            Assert.IsTrue(reel.Highlights.All(h => h.Score > 0));
            var best = reel.Highlights.OrderBy(h => h.Rank).First();
            Assert.GreaterOrEqual(best.Score, reel.Highlights.Max(h => h.Score));
            Assert.IsTrue(reel.Highlights.Where(h => h.Type == PlayType.Pass).All(h => h.PasserName.Length > 0 && h.ReceiverName.Length > 0));
            Assert.IsTrue(reel.Highlights.Where(h => h.Type == PlayType.Run && !h.Turnover).All(h => h.RusherName.Length > 0));
            Assert.IsTrue(reel.Highlights.All(h => h.Down >= 0 && h.Quarter >= 1 && h.Quarter <= 8));
            Assert.IsTrue(reel.Highlights.Zip(reel.Highlights.Skip(1), (a, b) => a.HomeScoreAfter <= b.HomeScoreAfter && a.AwayScoreAfter <= b.AwayScoreAfter).All(x => x), "the score only goes up");
            Assert.IsTrue(reel.Highlights.All(h => h.OffenseSchoolId == setup.Game.HomeId || h.OffenseSchoolId == setup.Game.AwayId));
            Assert.IsTrue(reel.Highlights.Any(h => h.OffensePlay.Length > 0 && h.OffenseFormation.Length > 0), "formation names for staging");
            StringAssert.Contains(setup.HomeName, reel.Summary);
            Assert.IsTrue(reel.MatchesRecorded);
        }

        [Test]
        public void HighlightsAreDeterministicForASeed()
        {
            CollegeGameSetup setup = Setup(S, 1);
            CollegeHighlightReel a = CollegeHighlights.Generate(setup, 8), b = CollegeHighlights.Generate(setup, 8), c = CollegeHighlights.Generate(setup, 8, 4242);
            Assert.AreEqual(a.HomeScore, b.HomeScore); Assert.AreEqual(a.AwayScore, b.AwayScore); Assert.AreEqual(a.TotalPlays, b.TotalPlays);
            Assert.AreEqual(a.Highlights.Select(h => h.PlayId).ToList(), b.Highlights.Select(h => h.PlayId).ToList());
            Assert.AreEqual(a.Highlights.Select(h => h.Description).ToList(), b.Highlights.Select(h => h.Description).ToList());
            Assert.AreNotEqual(a.Highlights.Select(h => h.PlayId).ToList(), c.Highlights.Select(h => h.PlayId).ToList());
            Assert.AreEqual(8, c.Highlights.Count);
            Assert.AreEqual(4242, c.GameSeed);
        }

        [Test]
        public void AHighlightPlayCanBeReplayedFromItsIdWithItsEventLog()
        {
            CollegeGameSetup setup = Setup(S, 2);
            CollegeHighlightReel reel = CollegeHighlights.Generate(setup, 5);
            foreach (HighlightPlay h in reel.Highlights.Take(3))
            {
                PlayRecord rec = CollegeHighlights.ReplayPlay(setup, h);
                Assert.IsNotNull(rec, h.PlayId);
                Assert.AreEqual(h.PlayIndex, rec.Index);
                Assert.AreEqual(h.Type, rec.Result.Type);
                Assert.AreEqual(h.Yards, rec.Result.Yards);
                Assert.AreEqual(h.OffensePlay, rec.OffensePlayName);
                Assert.AreEqual(h.HomeScoreAfter, rec.HomeScore);
                Assert.IsNotNull(rec.Log, "the replay keeps the event log the view animates");
                Assert.Greater(rec.Log.Count, 3);
            }
            Assert.IsNull(CollegeHighlights.ReplayPlay(setup, reel.GameSeed, 99999));
        }

        [Test]
        public void ThePlaysBuildAConsistentBoxScore()
        {
            CollegeGameSetup setup = Setup(S, 3);
            CollegeHighlightReel reel = CollegeHighlights.Generate(setup, 5);
            GameResult r = reel.Result;
            Assert.AreEqual(setup.Game.HomeId, r.HomeTeamId); Assert.AreEqual(setup.Game.AwayId, r.AwayTeamId);
            Assert.AreEqual(reel.HomeScore, r.HomeScore); Assert.AreEqual(reel.AwayScore, r.AwayScore);
            Assert.AreEqual(r.HomeScore, r.Home.ByQuarter.Sum() + r.Home.OvertimeScore, "points by quarter add up");
            Assert.AreEqual(r.AwayScore, r.Away.ByQuarter.Sum() + r.Away.OvertimeScore);
            Assert.IsTrue(r.PlayerLines.All(l => l.TeamId == r.HomeTeamId || l.TeamId == r.AwayTeamId));
            Assert.IsTrue(r.PlayerLines.All(l => !string.IsNullOrEmpty(l.Name)));
            Assert.AreEqual(r.Home.PassYds, r.PlayerLines.Where(l => l.TeamId == r.HomeTeamId).Sum(l => l.Stats.PassYds), "team passing yards = passer lines");
            Assert.AreEqual(r.Away.RushYds, r.PlayerLines.Where(l => l.TeamId == r.AwayTeamId).Sum(l => l.Stats.RushYds));
            Assert.AreEqual(r.Home.PassTd, r.PlayerLines.Where(l => l.TeamId == r.HomeTeamId).Sum(l => l.Stats.PassTd), "touchdown passes");
            Assert.IsTrue(r.PlayerLines.Any(l => l.Stats.Tackles > 0));
            Assert.IsTrue(r.Home.Plays > 20 && r.Away.Plays > 20);
            Assert.IsTrue(r.PlayerLines.All(l => l.Stats.Games == 1));
        }

        [Test]
        public void WatchingTheHighlightsPostsTheGameToTheSeasonAndTheBoxScoreFollows()
        {
            CareerSave s = CollegeTestUtil.InSeason(92, 2);
            Season cs = s.College.Season;
            ScheduledGame g = CollegeNet.GamesThisWeek(s)[3];
            Assert.IsFalse(g.Played);
            CollegeHighlightReel reel = CollegeHighlights.PlayAndPost(s, g.Id, 6);
            Assert.IsNotNull(reel);
            Assert.IsTrue(g.Played);
            Assert.AreEqual(reel.HomeScore, g.HomeScore); Assert.AreEqual(reel.AwayScore, g.AwayScore);
            Assert.AreNotEqual(g.HomeScore, g.AwayScore, "a college game has a winner");
            Assert.IsNull(CollegeHighlights.PlayAndPost(s, g.Id), "it cannot be played twice");
            ScheduledGame later = cs.Games.First(x => x.Week == cs.Week + 1);
            Assert.IsNull(CollegeHighlights.PlayAndPost(s, later.Id), "only this week's games");
            // stats were posted to the players of the game
            School home = s.College.GetSchool(g.HomeId);
            Assert.IsTrue(home.Team.Roster.Any(p => p.FindStats(cs.Year) != null && p.FindStats(cs.Year).Games > 0), "season stats were posted");
            CollegeTestUtil.Step(s);
            CollegeBox box = CollegeBoxes.Find(s, cs.Year, g.Id);
            Assert.IsNotNull(box);
            Assert.AreEqual(g.HomeScore, box.HomeScore);
            Assert.IsTrue(box.Leaders.Count > 0);
            // the highlight view of the played game agrees with the books
            CollegeHighlightReel again = CollegeHighlights.ForGame(s, g.Id, 6);
            Assert.IsTrue(again.MatchesRecorded);
            Assert.AreEqual(g.HomeScore, again.RecordedHomeScore);
        }

        [Test]
        public void HighlightsOfAPlayedGameUseASeedWhoseWinnerMatchesTheRecord()
        {
            CareerSave s = CollegeTestUtil.AtPhase(93, CareerPhase.Awards);
            int matched = 0, n = 0;
            foreach (ScheduledGame g in s.College.Season.Games.Where(x => x.Kind == GameKind.Regular && x.Played).Take(3))
            {
                CollegeHighlightReel reel = CollegeHighlights.ForGame(s, g.Id, 6);
                n++;
                Assert.IsNotNull(reel);
                Assert.AreEqual(g.HomeScore, reel.RecordedHomeScore);
                if (reel.MatchesRecorded)
                {
                    matched++;
                    Assert.AreEqual(g.WinnerId == g.HomeId, reel.HomeScore > reel.AwayScore);
                }
            }
            Assert.GreaterOrEqual(matched, 2, "the highlight game has the recorded winner (" + matched + " of " + n + ")");
        }

        [Test]
        public void BothSchoolsRostersTakeTheFieldAndTheRulesAreTheCollegeRules()
        {
            CollegeGameSetup setup = Setup(S, 4);
            Assert.AreEqual(RuleSet.College().QuarterSeconds, setup.Rules.QuarterSeconds);
            CollegeHighlightReel reel = CollegeHighlights.Generate(setup, 5);
            Assert.IsTrue(reel.Result.PlayerLines.Any(l => l.TeamId == setup.Game.HomeId));
            Assert.IsTrue(reel.Result.PlayerLines.Any(l => l.TeamId == setup.Game.AwayId));
            var homeIds = new HashSet<int>(setup.Home.Roster.Select(p => p.Id));
            Assert.IsTrue(reel.Result.PlayerLines.Where(l => l.TeamId == setup.Game.HomeId).All(l => homeIds.Contains(l.PlayerId)));
        }
    }
}
