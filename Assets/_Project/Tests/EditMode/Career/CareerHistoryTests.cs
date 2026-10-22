using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    /// <summary>History: season wrap-up, trophy case, awards and All-Pro, record book, Hall of Fame, legacy score, news and the owner.</summary>
    public class CareerHistoryTests
    {
        private static CareerSave _afterSeason;
        private static CareerSave AfterSeason
        {
            get
            {
                if (_afterSeason == null) _afterSeason = CareerTestUtil.AtPhase(111, CareerPhase.Awards);
                return _afterSeason;
            }
        }

        [Test]
        public void EverySeasonWritesOneRecordPerTeamAndTheBracketAddsUp()
        {
            CareerSave s = AfterSeason;
            var recs = s.TeamSeasons.Where(r => r.Year == s.Year).ToList();
            Assert.AreEqual(32, recs.Count);
            Assert.AreEqual(recs.Sum(r => r.Wins), recs.Sum(r => r.Losses));
            Assert.AreEqual(1, recs.Count(r => r.Result == PlayoffResult.Champion));
            Assert.AreEqual(1, recs.Count(r => r.Result == PlayoffResult.RunnerUp));
            Assert.AreEqual(14, recs.Count(r => r.Result != PlayoffResult.Missed), "seven teams per conference");
            Assert.AreEqual(8, recs.Count(r => r.DivisionTitle));
            Assert.AreEqual(2, recs.Count(r => r.ConferenceTitle));
            foreach (TeamSeasonRecord r in recs) Assert.AreEqual(17, r.Wins + r.Losses + r.Ties, "17 games");
            TeamSeasonRecord champ = recs.First(r => r.Result == PlayoffResult.Champion);
            Assert.AreEqual(s.Season.ChampionTeamId, champ.TeamId);
            Assert.AreSame(champ, HistoryEngine.SeasonRecord(s, champ.TeamId, s.Year));
        }

        [Test]
        public void TheTrophyCaseHoldsTheBowlTheConferenceAndTheDivisionTitles()
        {
            CareerSave s = AfterSeason;
            var year = s.Trophies.Where(t => t.Year == s.Year).ToList();
            Assert.AreEqual(1, year.Count(t => t.Kind == TrophyKind.PixBowl));
            Assert.AreEqual(2, year.Count(t => t.Kind == TrophyKind.ConferenceTitle));
            Assert.AreEqual(8, year.Count(t => t.Kind == TrophyKind.DivisionTitle));
            TrophyEntry bowl = year.First(t => t.Kind == TrophyKind.PixBowl);
            Assert.AreEqual(s.Season.ChampionTeamId, bowl.TeamId);
            Assert.IsNotEmpty(bowl.Opponent);
            StringAssert.Contains("-", bowl.Score);
            Assert.IsNotEmpty(bowl.BowlMvp);
            Assert.IsNotEmpty(bowl.Coach);
            Team champ = s.Pro.GetTeam(bowl.TeamId);
            Assert.IsNotNull(champ.FindAnyPlayer(bowl.BowlMvpId), "the PIX Bowl MVP plays for the champion");
            var case1 = HistoryEngine.TrophyCase(s, champ.Id);
            Assert.AreEqual(TrophyKind.PixBowl, case1[0].Kind, "titles first");
            Assert.IsTrue(s.Accolades.Any(a => a.Kind == AccoladeKind.PixBowlMvp && a.PlayerId == bowl.BowlMvpId));
            Assert.Greater(s.Accolades.Count(a => a.Kind == AccoladeKind.Champion && a.TeamId == champ.Id), 30, "a ring for every player who played");
        }

        [Test]
        public void SeasonAwardsAndAllProTeamsAreNamedAndStayFair()
        {
            CareerSave s = AfterSeason;
            foreach (AccoladeKind k in new[] { AccoladeKind.Mvp, AccoladeKind.Opoy, AccoladeKind.Dpoy, AccoladeKind.Roy })
                Assert.AreEqual(1, s.Accolades.Count(a => a.Year == s.Year && a.Kind == k), k.ToString());
            AllProTeam first = HistoryEngine.GetAllPro(s, s.Year, 1), second = HistoryEngine.GetAllPro(s, s.Year, 2);
            Assert.IsNotNull(first); Assert.IsNotNull(second);
            // 24 slots: QB RB FB 3 WR TE 5 OL 4 DL 3 LB 3 CB 2 S K P
            Assert.AreEqual(1, first.Players.Count(p => p.Pos == Position.QB));
            Assert.AreEqual(3, first.Players.Count(p => p.Pos == Position.WR));
            Assert.AreEqual(2, first.Players.Count(p => p.Pos == Position.DT));
            Assert.AreEqual(3, first.Players.Count(p => p.Pos == Position.CB));
            foreach (Position oline in new[] { Position.LT, Position.LG, Position.C, Position.RG, Position.RT }) Assert.AreEqual(1, first.Players.Count(p => p.Pos == oline), oline.ToString());
            Assert.GreaterOrEqual(first.Players.Count, 24);
            Assert.AreEqual(0, first.Players.Select(p => p.PlayerId).Intersect(second.Players.Select(p => p.PlayerId)).Count(), "second team is a different group of players");
            Assert.AreEqual(first.Players.Count, first.Players.Select(p => p.PlayerId).Distinct().Count());
            // All-Pro players are good players
            double avg = first.Players.Average(p => s.FindPlayer(p.PlayerId).Overall);
            double leagueAvg = s.Pro.AllRosterPlayers().Average(p => p.Overall);
            Assert.Greater(avg, leagueAvg + 5);
            Assert.AreEqual(first.Players.Count, s.Accolades.Count(a => a.Year == s.Year && a.Kind == AccoladeKind.AllPro1));
            // the count helper
            int qbId = first.Players.First(p => p.Pos == Position.QB).PlayerId;
            Assert.GreaterOrEqual(HistoryEngine.AccoladeCount(s, qbId, AccoladeKind.AllPro1), 1);
        }

        [Test]
        public void BrokenRecordsAreDetectedOnceAndMakeHeadlines()
        {
            CareerSave s = AfterSeason;
            int originalStart = s.StartYear;
            s.StartYear = s.Year - 1;   // the league has some history: records in the very first season make no headlines
            RecordEntry any = s.Pro.Records.Entries.First(e => e.Scope == RecordScope.SingleSeason && e.Category == "PassYds");
            HistoryEngine.SnapshotRecords(s);
            int news = s.News.Count;
            Assert.AreEqual(0, HistoryEngine.DetectBrokenRecords(s).Count);
            Player qb = s.UserTeam.Roster.First(p => p.Position == Position.QB);
            Assert.IsTrue(s.Pro.Records.Offer("PassYds", RecordScope.SingleSeason, any.Value + 250, qb.Id, qb.Name, s.UserTeam.FullName, s.Year));
            var broken = HistoryEngine.DetectBrokenRecords(s);
            Assert.AreEqual(1, broken.Count);
            Assert.AreEqual(qb.Id, broken[0].PlayerId);
            Assert.AreEqual(news + 1, s.News.Count);
            NewsItem item = s.News.Last();
            Assert.AreEqual(NewsKind.Record, item.Kind);
            StringAssert.Contains(qb.Name, item.Headline);
            StringAssert.Contains("passing yards", item.Headline);
            Assert.AreEqual(0, HistoryEngine.DetectBrokenRecords(s).Count, "reported only once");
            // team records exist after a season
            Assert.GreaterOrEqual(s.TeamRecords.Count, 4);
            Assert.IsTrue(s.TeamRecords.Any(r => r.Category == "Most wins in a season" && r.Value >= 12));
            Assert.AreEqual(s.TeamRecords.Select(r => r.Category).Distinct().Count(), s.TeamRecords.Count);
            s.StartYear = originalStart;
        }

        [Test]
        public void TheHallOfFameInductsGreatRetiredPlayersAfterTheWait()
        {
            CareerSave s = CareerTestUtil.Fresh(112);
            // a synthetic legend and a journeyman who retired three years ago
            Team t = s.UserTeam;
            Player legend = new Player { Position = Position.QB, Archetype = Archetype.QbPocket, Ratings = Ratings.Uniform(92), Retired = true, TeamId = -1 };
            legend.Bio.Id = 8800001; legend.Bio.FirstName = "Hall"; legend.Bio.LastName = "Legend"; legend.Bio.Age = 38;
            Player journeyman = new Player { Position = Position.WR, Archetype = Archetype.WrSlot, Ratings = Ratings.Uniform(66), Retired = true, TeamId = -1 };
            journeyman.Bio.Id = 8800002; journeyman.Bio.FirstName = "Just"; journeyman.Bio.LastName = "Fine"; journeyman.Bio.Age = 33;
            for (int y = 2018; y <= 2025; y++)
            {
                SeasonStats st = legend.StatsFor(y, t.Id);
                st.Games = 17; st.PassAtt = 560; st.PassCmp = 380; st.PassYds = 4600; st.PassTd = 36; st.PassInt = 9;
                s.Accolades.Add(new Accolade { PlayerId = legend.Id, Year = y, TeamId = t.Id, Kind = AccoladeKind.AllPro1 });
                if (y % 3 == 0) s.Accolades.Add(new Accolade { PlayerId = legend.Id, Year = y, TeamId = t.Id, Kind = AccoladeKind.Mvp });
                if (y % 2 == 0) s.Accolades.Add(new Accolade { PlayerId = legend.Id, Year = y, TeamId = t.Id, Kind = AccoladeKind.Champion });
                SeasonStats js = journeyman.StatsFor(y, t.Id);
                js.Games = 12; js.Rec = 40; js.RecYds = 480; js.RecTd = 3; js.Targets = 70;
            }
            s.Pro.RetiredPlayers.Add(legend); s.Pro.RetiredPlayers.Add(journeyman);
            s.Retired.Add(new RetiredRecord { PlayerId = legend.Id, Year = s.Year - 3, TeamId = t.Id });
            s.Retired.Add(new RetiredRecord { PlayerId = journeyman.Id, Year = s.Year - 3, TeamId = t.Id });
            Assert.Greater(HistoryEngine.HofScore(s, legend), HistoryEngine.HofThreshold + 20);
            Assert.Less(HistoryEngine.HofScore(s, journeyman), HistoryEngine.HofThreshold);
            // too recent: not eligible yet
            Player recent = new Player { Position = Position.QB, Ratings = Ratings.Uniform(92), Retired = true };
            recent.Bio.Id = 8800003; recent.Bio.FirstName = "Too"; recent.Bio.LastName = "Soon";
            s.Pro.RetiredPlayers.Add(recent);
            s.Retired.Add(new RetiredRecord { PlayerId = recent.Id, Year = s.Year, TeamId = t.Id });
            s.Accolades.Add(new Accolade { PlayerId = recent.Id, Year = s.Year - 1, TeamId = t.Id, Kind = AccoladeKind.Mvp });
            var cls = HistoryEngine.InductClass(s);
            Assert.AreEqual(1, cls.Count);
            Assert.AreEqual(legend.Id, cls[0].PlayerId);
            Assert.AreEqual(t.Id, cls[0].TeamId);
            Assert.AreEqual(8, cls[0].Seasons);
            StringAssert.Contains("passing yards", cls[0].Summary);
            StringAssert.Contains("MVP", cls[0].Accolades);
            Assert.AreEqual(1, s.HallOfFame.Count);
            Assert.IsTrue(s.News.Any(n => n.Kind == NewsKind.HallOfFame));
            Assert.AreEqual(0, HistoryEngine.InductClass(s).Count, "nobody is inducted twice");
        }

        [Test]
        public void TheLegacyScoreAddsUpTitlesStreaksAndHonours()
        {
            CareerSave s = CareerTestUtil.Fresh(113);
            int id = s.UserTeamId;
            Assert.AreEqual(0, HistoryEngine.Legacy(s, id).Score);
            Assert.AreEqual("Rebuilding", HistoryEngine.Legacy(s, id).Tier);
            for (int y = 2026; y < 2032; y++)
                s.TeamSeasons.Add(new TeamSeasonRecord
                {
                    Year = y, TeamId = id, Wins = 12, Losses = 5, DivisionTitle = true, ConferenceTitle = y % 2 == 0, Result = y == 2028 ? PlayoffResult.Champion : (y % 2 == 0 ? PlayoffResult.RunnerUp : PlayoffResult.Divisional)
                });
            LegacyBreakdown lb = HistoryEngine.Legacy(s, id);
            // 1 title (50), 2 conference titles (30), 6 division titles (36), 6 playoffs (48), 24 wins above eight x1.5 (36), streak 6 x2 (12)
            Assert.AreEqual(50 + 30 + 36 + 48 + 36 + 12, lb.Score);
            Assert.AreEqual("Powerhouse", lb.Tier);
            s.HallOfFame.Add(new HofEntry { PlayerId = 1, TeamId = id });
            Assert.AreEqual(lb.Score + 15, HistoryEngine.Legacy(s, id).Score);
            Assert.GreaterOrEqual(lb.Lines.Count, 8);
            Assert.AreEqual(0, HistoryEngine.Legacy(s, (id + 1) % 32).Score, "legacy is per franchise");
        }

        [Test]
        public void TheTickerAndNewsFeedServeTheHeadlines()
        {
            CareerSave s = AfterSeason;
            Assert.Greater(s.News.Count, 20);
            Assert.IsTrue(s.News.Any(n => n.Kind == NewsKind.Title));
            Assert.IsTrue(s.News.Any(n => n.Kind == NewsKind.Award));
            Assert.IsTrue(s.News.Any(n => n.Kind == NewsKind.Injury));
            var ticker = NewsWriter.Ticker(s, 12);
            Assert.AreEqual(12, ticker.Count);
            Assert.IsTrue(ticker.Count(n => n.Priority >= 3 || n.Mine) >= 8, "the ticker favours important and own-team items");
            Assert.IsTrue(ticker.All(n => !string.IsNullOrEmpty(n.Headline)));
            var feed = NewsWriter.Feed(s, 30);
            Assert.AreEqual(30, feed.Count);
            for (int i = 1; i < feed.Count; i++) Assert.GreaterOrEqual(feed[i - 1].Id, feed[i].Id, "newest first");
            var mine = NewsWriter.Feed(s, 30, true);
            Assert.IsTrue(mine.All(n => n.Mine && n.TeamIds.Contains(s.UserTeamId)));
            var titles = NewsWriter.Feed(s, 5, false, true, NewsKind.Title);
            Assert.IsTrue(titles.All(n => n.Kind == NewsKind.Title));
            NewsItem title = titles[0];
            StringAssert.Contains("PIX Bowl", title.Headline);
            Assert.AreEqual(5, title.Priority);
        }

        [Test]
        public void SigningsInjuriesAndTradesBecomeHeadlinesForTheUserTeam()
        {
            CareerSave s = CareerTestUtil.AtPhase(114, CareerPhase.FreeAgency);
            int before = s.News.Count;
            Team u = s.UserTeam;
            u.SalaryCap += 300000000L;
            Player star = FreeAgency.Pool(s).First(p => p.Overall >= 76 && !FreeAgency.IsUndrafted(s, p));
            ContractAsk ask = FreeAgency.Ask(s, star, 0);
            FreeAgency.PlaceOffer(s, star.Id, (long)(ask.Aav * 1.6), 5, 0.3f);
            CareerFlow.SkipFreeAgency(s);
            Assert.Greater(s.News.Count, before);
            Assert.IsTrue(s.News.Skip(before).Any(n => n.Kind == NewsKind.Signing));
            // user team items are marked
            Assert.IsTrue(s.News.Skip(before).Where(n => n.TeamIds.Contains(u.Id)).All(n => n.Mine));
            // the news is trimmed to a size the save can carry
            for (int i = 0; i < NewsWriter.MaxItems + 200; i++) NewsWriter.Post(s, NewsKind.Rumor, "filler " + i, "", 1);
            Assert.LessOrEqual(s.News.Count, NewsWriter.MaxItems);
        }

        [Test]
        public void TheOwnerSetsGoalsAndRatesTheSeason()
        {
            CareerSave s = AfterSeason;
            var goals = s.Goals.Where(g => g.Year == s.Year).ToList();
            Assert.GreaterOrEqual(goals.Count, 1);
            Assert.IsTrue(goals.All(g => g.Resolved), "evaluated when the season ended");
            Assert.IsTrue(goals.Any(g => g.Id == "wins"));
            OwnerGoal wins = goals.First(g => g.Id == "wins");
            TeamSeasonRecord rec = HistoryEngine.SeasonRecord(s, s.UserTeamId, s.Year);
            Assert.AreEqual(rec.Wins, wins.Actual);
            Assert.AreEqual(rec.Wins >= wins.Target, wins.Met);
            Assert.That(s.OwnerSatisfaction, Is.InRange(0, 100));
            Assert.IsNotEmpty(OwnerEngine.JobSecurity(s));
            // next year's goals appear with the new season
            CareerSave next = CareerTestUtil.AtPhase(114, CareerPhase.Preseason);
            Assert.IsTrue(next.Goals.Any(g => g.Year == next.Year && !g.Resolved));
        }
    }
}
