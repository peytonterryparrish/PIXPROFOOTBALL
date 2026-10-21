using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.League.Team;

namespace PixFootball.Tests
{
    public class ProSeasonTests
    {
        private static ProLeague _fresh;
        private static ProLeague Fresh { get { return _fresh ?? (_fresh = LeagueFactory.CreatePro(321)); } }

        private static ProLeague _played;
        private static ProLeague Played
        {
            get
            {
                if (_played == null)
                {
                    _played = LeagueFactory.CreatePro(321);
                    _played.Season.SimToEnd();
                }
                return _played;
            }
        }

        [Test]
        public void ScheduleHasSeventeenGamesPerTeamOverEighteenWeeksWithOneBye()
        {
            Season s = Fresh.Season;
            Assert.AreEqual(272, s.Games.Count);
            Assert.AreEqual(18, s.RegularWeeks);
            Assert.AreEqual(18, s.Games.Max(g => g.Week));
            for (int t = 0; t < 32; t++)
            {
                Assert.AreEqual(17, s.RegularGameCount(t), "team " + t);
                var weeks = s.GamesOfTeam(t).Select(g => g.Week).ToList();
                Assert.AreEqual(17, weeks.Distinct().Count(), "team " + t + " plays once per week");
                int bye = s.ByeWeekOf(t);
                Assert.That(bye, Is.InRange(5, 14), "bye week of team " + t);
                int home = s.Games.Count(g => g.HomeId == t);
                Assert.That(home, Is.InRange(8, 9), "home games of team " + t);
            }
            Assert.AreEqual(32, s.Games.Select(g => g.HomeId).Distinct().Count());
        }

        [Test]
        public void ScheduleFollowsTheDivisionRotationFormula()
        {
            Season s = Fresh.Season;
            for (int t = 0; t < 32; t++)
            {
                Team team = Fresh.Teams[t];
                var games = s.GamesOfTeam(t);
                int divGames = games.Count(g => Fresh.Teams[g.OpponentOf(t)].DivisionId == team.DivisionId);
                int sameConf = games.Count(g => Fresh.Teams[g.OpponentOf(t)].ConferenceId == team.ConferenceId && Fresh.Teams[g.OpponentOf(t)].DivisionId != team.DivisionId);
                int otherConf = games.Count(g => Fresh.Teams[g.OpponentOf(t)].ConferenceId != team.ConferenceId);
                Assert.AreEqual(6, divGames, "division games");
                Assert.AreEqual(6, sameConf, "same-conference non-division games (4 rotation + 2 same place)");
                Assert.AreEqual(5, otherConf, "other-conference games (4 rotation + 1)");
                // each division rival twice, home and away
                foreach (int rival in Fresh.GetDivision(team.DivisionId).TeamIds.Where(i => i != t))
                {
                    Assert.AreEqual(1, games.Count(g => g.HomeId == t && g.AwayId == rival));
                    Assert.AreEqual(1, games.Count(g => g.AwayId == t && g.HomeId == rival));
                }
            }
        }

        [Test]
        public void ScheduleIsDeterministicPerSeed()
        {
            ProLeague a = LeagueFactory.CreatePro(55), b = LeagueFactory.CreatePro(55);
            Assert.AreEqual(LeagueTestUtil.Fingerprint(a.Season), LeagueTestUtil.Fingerprint(b.Season));
        }

        [Test]
        public void FullSeasonAndPlayoffsProduceOnePixBowlChampion()
        {
            Season s = Played.Season;
            Assert.IsTrue(s.IsComplete);
            Assert.AreEqual(SeasonPhase.Complete, s.Phase);
            Assert.AreEqual("PIX Bowl", s.ChampionshipName);
            Assert.IsTrue(s.Games.All(g => g.Played));
            var finals = s.Games.Where(g => g.Kind == GameKind.Final).ToList();
            Assert.AreEqual(1, finals.Count);
            Assert.AreEqual("PIX Bowl", finals[0].Label);
            Assert.AreEqual(finals[0].WinnerId, s.ChampionTeamId);
            Assert.AreEqual(finals[0].LoserId, s.RunnerUpTeamId);
            Assert.AreNotEqual(s.ChampionTeamId, s.RunnerUpTeamId);
            Assert.AreNotEqual(Fresh.Teams[s.ChampionTeamId].ConferenceId == Fresh.Teams[s.RunnerUpTeamId].ConferenceId, true, "the title game pairs the two conference champions");
            Assert.IsFalse(finals[0].Tie);
            // 6 wild card + 4 divisional + 2 conference championship + 1 final
            var po = s.Games.Where(g => g.Kind == GameKind.PlayoffRound).ToList();
            Assert.AreEqual(6, po.Count(g => g.Round == 1));
            Assert.AreEqual(4, po.Count(g => g.Round == 2));
            Assert.AreEqual(2, po.Count(g => g.Round == 3));
            Assert.IsTrue(po.All(g => !g.Tie));
            Assert.AreEqual(14, s.Seeds.Count);
            foreach (int conf in new[] { 0, 1 })
            {
                var seeds = s.Seeds.Where(x => x.ConferenceId == conf).OrderBy(x => x.Seed).ToList();
                CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6, 7 }, seeds.Select(x => x.Seed).ToArray());
                // seeds 1-4 are division winners
                var rows = s.Standings();
                for (int i = 0; i < 4; i++) Assert.AreEqual(1, rows.First(r => r.TeamId == seeds[i].TeamId).GroupRank, "division winner seed");
                for (int i = 4; i < 7; i++) Assert.AreNotEqual(1, rows.First(r => r.TeamId == seeds[i].TeamId).GroupRank, "wild card");
            }
            Assert.AreEqual(1, Played.History.Count);
            Assert.AreEqual(s.ChampionTeamId, Played.History[0].ChampionTeamId);
        }

        [Test]
        public void StandingsAreCompleteAndRanked()
        {
            Season s = Played.Season;
            var rows = s.Standings();
            Assert.AreEqual(32, rows.Count);
            Assert.AreEqual(272, rows.Sum(r => r.W) + rows.Sum(r => r.T) / 2);
            foreach (var r in rows)
            {
                Assert.AreEqual(17, r.Games);
                Assert.That(r.GroupRank, Is.InRange(1, 4));
            }
            for (int d = 0; d < 8; d++)
            {
                var div = rows.Where(r => r.DivisionId == d).OrderBy(r => r.GroupRank).ToList();
                CollectionAssert.AreEqual(new[] { 1, 2, 3, 4 }, div.Select(r => r.GroupRank).ToArray());
                for (int i = 1; i < 4; i++) Assert.GreaterOrEqual(div[i - 1].Pct, div[i].Pct, "division order");
            }
            for (int c = 0; c < 2; c++)
            {
                var conf = rows.Where(r => r.ConferenceId == c).OrderBy(r => r.ConferenceRank).ToList();
                CollectionAssert.AreEqual(Enumerable.Range(1, 16).ToArray(), conf.Select(r => r.ConferenceRank).ToArray());
                for (int i = 1; i < 16; i++) Assert.GreaterOrEqual(conf[i - 1].Pct, conf[i].Pct);
            }
            int spread = rows.Max(r => r.W) - rows.Min(r => r.W);
            Assert.GreaterOrEqual(spread, 5, "records should spread out");
            Assert.AreEqual(rows.Sum(r => r.PF), rows.Sum(r => r.PA));
        }

        [Test]
        public void BetterTeamsWinMoreOverTheSeason()
        {
            Season s = Played.Season;
            var rows = s.Standings();
            double mx = rows.Average(r => Played.Teams[r.TeamId].TeamOverall()), my = rows.Average(r => r.W);
            double cov = 0, vx = 0, vy = 0;
            foreach (var r in rows)
            {
                double dx = Played.Teams[r.TeamId].TeamOverall() - mx, dy = r.W - my;
                cov += dx * dy; vx += dx * dx; vy += dy * dy;
            }
            Assert.Greater(cov / Math.Sqrt(vx * vy), 0.4);
        }

        [Test]
        public void SeasonSimulationIsDeterministic()
        {
            ProLeague a = LeagueFactory.CreatePro(777), b = LeagueFactory.CreatePro(777);
            a.Season.SimToEnd(); b.Season.SimToEnd();
            Assert.AreEqual(LeagueTestUtil.Fingerprint(a.Season), LeagueTestUtil.Fingerprint(b.Season));
            Assert.AreEqual(a.Season.MvpId, b.Season.MvpId);
            ProLeague c = LeagueFactory.CreatePro(778);
            c.Season.SimToEnd();
            Assert.AreNotEqual(LeagueTestUtil.Fingerprint(a.Season), LeagueTestUtil.Fingerprint(c.Season));
        }

        [Test]
        public void WeeklySimAdvancesTheCalendarAndKeepsBoxScores()
        {
            ProLeague lg = LeagueFactory.CreatePro(909);
            Season s = lg.Season;
            Assert.AreEqual(1, s.Week);
            Assert.IsTrue(s.SimWeek());
            Assert.AreEqual(2, s.Week);
            Assert.AreEqual(s.GamesOfWeek(1).Count, s.LastWeekResults.Count);
            Assert.IsTrue(s.GamesOfWeek(1).All(g => g.Played));
            Assert.IsTrue(s.GamesOfWeek(2).All(g => !g.Played));
            Assert.AreEqual(SeasonPhase.Regular, s.Phase);
            s.SimRegularSeason();
            Assert.AreEqual(SeasonPhase.Postseason, s.Phase);
            Assert.AreEqual(19, s.Week);
            Assert.AreEqual(6, s.GamesOfWeek(19).Count);
        }

        [Test]
        public void AwardsAreGivenToRealPlayersAndRookieIsARookie()
        {
            Season s = Played.Season;
            foreach (string name in new[] { "MVP", "Offensive Player of the Year", "Defensive Player of the Year", "Rookie of the Year" })
            {
                AwardWinner w = s.Awards.FirstOrDefault(a => a.Award == name);
                Assert.IsNotNull(w, name);
                Player p = Played.FindPlayer(w.PlayerId);
                Assert.IsNotNull(p, name);
                Assert.AreEqual(p.Name, w.PlayerName);
                Assert.IsNotNull(p.FindStats(s.Year));
            }
            Player roy = Played.FindPlayer(s.RoyId);
            Assert.AreEqual(0, roy.Experience);
            Assert.AreEqual(s.Year, roy.DraftYear);
            Player dpoy = Played.FindPlayer(s.DpoyId);
            Assert.IsTrue(PositionGroup.IsDefense(dpoy.Position));
        }

        [Test]
        public void LeagueLeadersAreSortedAndMatchPlayerStats()
        {
            Season s = Played.Season;
            foreach (StatCategory c in new[] { StatCategory.PassYds, StatCategory.RushYds, StatCategory.RecYds, StatCategory.Sacks, StatCategory.Interceptions, StatCategory.Tackles, StatCategory.PassTd, StatCategory.PasserRating })
            {
                var top = LeagueLeaders.Top(s, c, 10);
                Assert.Greater(top.Count, 3, c.ToString());
                for (int i = 1; i < top.Count; i++) Assert.GreaterOrEqual(top[i - 1].Value, top[i].Value);
                Assert.AreEqual(1, top[0].Rank);
            }
            var pass = LeagueLeaders.Top(s, StatCategory.PassYds, 1)[0];
            Player qb = Played.FindPlayer(pass.PlayerId);
            Assert.AreEqual(qb.FindStats(s.Year).PassYds, (int)pass.Value);
            Assert.Greater(pass.Value, 2500);
            Assert.Less(pass.Value, 6500);
        }

        [Test]
        public void RecordBookTracksTheBestSingleSeasonAndSingleGame()
        {
            Season s = Played.Season;
            RecordEntry seasonPass = Played.Records.Get("PassYds", RecordScope.SingleSeason);
            Assert.IsNotNull(seasonPass);
            Assert.AreEqual((int)LeagueLeaders.Top(s, StatCategory.PassYds, 1)[0].Value, seasonPass.Value);
            RecordEntry gamePass = Played.Records.Get("PassYds", RecordScope.SingleGame);
            Assert.IsNotNull(gamePass);
            Assert.Greater(gamePass.Value, 300);
            Assert.Less(gamePass.Value, seasonPass.Value);
            Assert.IsNotNull(Played.Records.Get("PassYds", RecordScope.Career));
            Assert.IsFalse(Played.Records.Offer("PassYds", RecordScope.SingleSeason, 10, 1, "x", "y", 2026), "a small value is not a record");
            Assert.IsTrue(Played.Records.Offer("PassYds", RecordScope.SingleSeason, seasonPass.Value + 1, 1, "x", "y", 2026));
        }

        [Test]
        public void PlayerSeasonStatsAreAccumulatedAndInjuriesHappen()
        {
            int withStats = Played.AllRosterPlayers().Count(p => p.FindStats(2026) != null && p.FindStats(2026).Games > 0);
            Assert.Greater(withStats, 1000);
            Player qb = Played.Teams[0].GetPlayer(Played.Teams[0].Depth.StarterId(Position.QB));
            var st = qb.FindStats(2026);
            Assert.IsNotNull(st);
            Assert.LessOrEqual(st.Games, 17);
            Assert.Greater(Played.AllRosterPlayers().Count(p => p.Injury.Wear > 0 || p.Injury.IsInjured), 5, "some injuries over a season");
        }

        [Test]
        public void DraftOrderPutsTheChampionLastAndTheWorstRecordFirst()
        {
            Season s = Played.Season;
            var order = s.DraftOrder();
            Assert.AreEqual(32, order.Count);
            Assert.AreEqual(32, order.Distinct().Count());
            Assert.AreEqual(s.ChampionTeamId, order[31]);
            var rows = s.Standings();
            int worst = rows.OrderBy(r => r.Pct).ThenBy(r => r.PF - r.PA).First().TeamId;
            Assert.That(order.IndexOf(worst), Is.LessThan(3));
        }
    }

    public class StandingsTiebreakerTests
    {
        private static Season Make(int teams, int divisionOf, int confOf = 0)
        {
            var s = new Season { Kind = SeasonKind.Pro };
            for (int i = 0; i < teams; i++) s.Teams.Add(new SeasonTeam { TeamId = i, DivisionId = divisionOf, ConferenceId = confOf, Name = "T" + i });
            return s;
        }

        private static void G(Season s, int home, int away, int hs, int aws, int week = 1)
        {
            s.Games.Add(new ScheduledGame { Id = s.Games.Count, Week = week, HomeId = home, AwayId = away, HomeScore = hs, AwayScore = aws, Played = true, Kind = GameKind.Regular });
        }

        [Test]
        public void HeadToHeadBeatsPointDifferential()
        {
            Season s = Make(4, 0);
            G(s, 0, 1, 20, 17);   // A beat B
            G(s, 0, 2, 10, 13);   // A lost to C
            G(s, 0, 3, 24, 21);   // A beat D
            G(s, 1, 2, 45, 3);    // B crushed C
            G(s, 1, 3, 40, 3);    // B crushed D
            var rows = s.Standings().OrderBy(r => r.GroupRank).ToList();
            var a = rows.First(r => r.TeamId == 0); var b = rows.First(r => r.TeamId == 1);
            Assert.AreEqual(a.Pct, b.Pct, 1e-6);
            Assert.Less(a.PF - a.PA, b.PF - b.PA);
            Assert.Less(a.GroupRank, b.GroupRank, "A won the head-to-head");
        }

        [Test]
        public void DivisionRecordBreaksTiesBetweenTeamsThatNeverMet()
        {
            var s = new Season { Kind = SeasonKind.Pro };
            // division 0 = {0, 1, 5}; division 1 = {2, 3}; one conference
            foreach (int id in new[] { 0, 1, 5 }) s.Teams.Add(new SeasonTeam { TeamId = id, DivisionId = 0, ConferenceId = 0 });
            foreach (int id in new[] { 2, 3 }) s.Teams.Add(new SeasonTeam { TeamId = id, DivisionId = 1, ConferenceId = 0 });
            G(s, 0, 5, 10, 3);   // 0 beats 5 (division)
            G(s, 2, 0, 10, 3);   // 0 loses to 2
            G(s, 5, 1, 10, 3);   // 1 loses to 5 (division)
            G(s, 1, 3, 10, 3);   // 1 beats 3
            var rows = s.Standings();
            Func<int, StandingsRow> r = id => rows.First(x => x.TeamId == id);
            Assert.AreEqual(0.5f, r(0).Pct, 1e-6); Assert.AreEqual(0.5f, r(1).Pct, 1e-6); Assert.AreEqual(0.5f, r(5).Pct, 1e-6);
            Assert.Less(r(0).GroupRank, r(5).GroupRank, "1-0 in the division beats 1-1");
            Assert.Less(r(5).GroupRank, r(1).GroupRank, "1-1 in the division beats 0-1");
        }

        [Test]
        public void ThreeWayTiesUseTheMiniLeagueAmongTheTiedTeams()
        {
            Season s = Make(4, 0);
            // A,B,C each 2-1 against each other and D; head-to-head among them: A beat B, B beat C, C beat A (all 1-1) -> falls to point differential
            G(s, 0, 1, 20, 10); G(s, 1, 2, 20, 10); G(s, 2, 0, 31, 10);
            G(s, 0, 3, 14, 10); G(s, 1, 3, 14, 10); G(s, 3, 2, 10, 14);
            var rows = s.Standings();
            // all three are 2-1... but C is 2-1 with +? ensure ordering is by point differential among the three
            var tied = rows.Where(r => r.TeamId != 3).OrderBy(r => r.GroupRank).ToList();
            Assert.AreEqual(2, tied[0].W); Assert.AreEqual(2, tied[1].W); Assert.AreEqual(2, tied[2].W);
            for (int i = 1; i < 3; i++) Assert.GreaterOrEqual(tied[i - 1].PF - tied[i - 1].PA, tied[i].PF - tied[i].PA);
            Assert.AreEqual(4, rows.First(r => r.TeamId == 3).GroupRank);
        }

        [Test]
        public void CollegeStandingsRankByConferenceRecord()
        {
            var s = new Season { Kind = SeasonKind.College };
            for (int i = 0; i < 4; i++) s.Teams.Add(new SeasonTeam { TeamId = i, ConferenceId = 0, DivisionId = -1 });
            s.Teams.Add(new SeasonTeam { TeamId = 4, ConferenceId = 1, DivisionId = -1 });
            // team 0 is 3-0 in the conference but lost a non-conference game; team 1 is 2-1 in the conference and 3-0 overall
            G(s, 0, 1, 10, 3); G(s, 0, 2, 10, 3); G(s, 0, 3, 10, 3);
            G(s, 4, 0, 14, 7);
            G(s, 1, 2, 10, 3); G(s, 3, 1, 10, 3);
            G(s, 1, 4, 10, 3);
            var rows = s.Standings();
            Assert.AreEqual(1, rows.First(r => r.TeamId == 0).GroupRank);
            Assert.AreEqual(1, rows.First(r => r.TeamId == 0).ConferenceRank);
        }
    }

    public class CollegeSeasonTests
    {
        private static CollegeWorld _fresh;
        private static CollegeWorld Fresh { get { return _fresh ?? (_fresh = LeagueTestUtil.LoadCollege(444)); } }
        private static CollegeWorld _played;
        private static CollegeWorld Played
        {
            get
            {
                if (_played == null) { _played = LeagueTestUtil.LoadCollege(444); _played.Season.SimToEnd(); }
                return _played;
            }
        }

        [Test]
        public void EveryTeamPlaysTwelveGamesWithNineOrMoreConferenceGames()
        {
            Season s = Fresh.Season;
            Assert.LessOrEqual(s.RegularWeeks, 16);
            foreach (School sc in Fresh.Schools)
            {
                var games = s.GamesOfTeam(sc.Id).Where(g => g.Kind == GameKind.Regular).ToList();
                Assert.AreEqual(12, games.Count, sc.Name);
                Assert.AreEqual(games.Count, games.Select(g => g.Week).Distinct().Count(), sc.Name + " one game per week");
                Assert.AreEqual(games.Count, games.Select(g => g.OpponentOf(sc.Id)).Distinct().Count(), sc.Name + " plays each opponent once");
                int conf = games.Count(g => sc.ConferenceId >= 0 && Fresh.Schools[g.OpponentOf(sc.Id)].ConferenceId == sc.ConferenceId);
                int members = sc.ConferenceId >= 0 ? Fresh.Conferences[sc.ConferenceId].TeamIds.Count : 0;
                if (sc.Independent) Assert.AreEqual(0, conf, sc.Name);
                else if (members <= 10) Assert.AreEqual(members - 1, conf, sc.Name + " plays a full round robin");
                else Assert.That(conf, Is.InRange(8, 9), sc.Name);
                int home = games.Count(g => g.HomeId == sc.Id);
                Assert.That(home, Is.InRange(3, 9), sc.Name);
            }
            Assert.IsTrue(Fresh.Season.Games.Any(g => !Fresh.Schools[g.HomeId].Independent && !Fresh.Schools[g.AwayId].Independent && Fresh.Schools[g.HomeId].ConferenceId != Fresh.Schools[g.AwayId].ConferenceId));
        }

        [Test]
        public void CollegeSeasonProducesPollsChampionPlayoffAndBowls()
        {
            Season s = Played.Season;
            Assert.IsTrue(s.IsComplete);
            Assert.AreEqual("Gridiron Crown Championship", s.ChampionshipName);
            // conference championships
            var cc = s.Games.Where(g => g.Kind == GameKind.ConferenceChampionship).ToList();
            Assert.AreEqual(10, cc.Count);
            Assert.IsTrue(cc.All(g => Played.Schools[g.HomeId].ConferenceId == Played.Schools[g.AwayId].ConferenceId && Played.Schools[g.HomeId].ConferenceId >= 0));
            Assert.IsTrue(cc.All(g => g.Played && !g.Tie));
            // 12-team playoff
            Assert.AreEqual(12, s.Seeds.Count);
            Assert.AreEqual(12, s.Seeds.Select(x => x.TeamId).Distinct().Count());
            CollectionAssert.AreEqual(Enumerable.Range(1, 12).ToArray(), s.Seeds.OrderBy(x => x.Seed).Select(x => x.Seed).ToArray());
            var po = s.Games.Where(g => g.Kind == GameKind.PlayoffRound).ToList();
            Assert.AreEqual(4, po.Count(g => g.Round == 1));
            Assert.AreEqual(4, po.Count(g => g.Round == 2));
            Assert.AreEqual(2, po.Count(g => g.Round == 3));
            Assert.IsTrue(po.All(g => g.Label.StartsWith("Gridiron Crown Playoff")));
            var fin = s.Games.Single(g => g.Kind == GameKind.Final);
            Assert.AreEqual("Gridiron Crown Championship", fin.Label);
            Assert.AreEqual(fin.WinnerId, s.ChampionTeamId);
            Assert.IsTrue(s.ChampionTeamId >= 0);
            Assert.AreNotEqual(s.ChampionTeamId, s.RunnerUpTeamId);
            // the six playoff-tier bowls host the quarterfinals and semifinals; the other 35 host regular bowl games
            var cfpNames = new[] { "Citrus Comet Bowl", "Cottoncrest Classic", "Crystal Cane Bowl", "Festival Flame Bowl", "Orchard Gold Bowl", "Thorncrest Bowl" };
            foreach (string n in cfpNames) Assert.AreEqual(1, po.Count(g => g.Round >= 2 && g.Label.EndsWith(n)), n);
            var bowls = s.Games.Where(g => g.Kind == GameKind.Bowl).ToList();
            Assert.AreEqual(35, bowls.Count);
            Assert.AreEqual(35, bowls.Select(b => b.Label).Distinct().Count());
            var expected = Played.Bowls.Where(b => b.IsBowl && !b.IsPlayoffBowl).Select(b => b.Name).OrderBy(x => x).ToArray();
            CollectionAssert.AreEqual(expected, bowls.Select(b => b.Label).OrderBy(x => x).ToArray());
            Assert.AreEqual(41, bowls.Count + cfpNames.Length, "all 41 official bowls are used");
            var inPlayoff = new HashSet<int>(s.Seeds.Select(x => x.TeamId));
            Assert.IsTrue(bowls.All(b => !inPlayoff.Contains(b.HomeId) && !inPlayoff.Contains(b.AwayId)), "playoff teams do not play other bowls");
            var bowlTeams = bowls.SelectMany(b => new[] { b.HomeId, b.AwayId }).ToList();
            Assert.AreEqual(bowlTeams.Count, bowlTeams.Distinct().Count());
        }

        [Test]
        public void WeeklyPollsHaveTwentyFiveRankedTeamsAndMoveWeekToWeek()
        {
            Season s = Played.Season;
            Assert.GreaterOrEqual(s.Polls.Count, s.RegularWeeks + 2);
            Assert.AreEqual("Preseason", s.Polls[0].Label);
            foreach (PollSnapshot p in s.Polls)
            {
                Assert.AreEqual(25, p.Top25.Count, p.Label);
                CollectionAssert.AreEqual(Enumerable.Range(1, 25).ToArray(), p.Top25.Select(e => e.Rank).ToArray());
                Assert.AreEqual(25, p.Top25.Select(e => e.TeamId).Distinct().Count());
                for (int i = 1; i < 25; i++) Assert.GreaterOrEqual(p.Top25[i - 1].Points, p.Top25[i].Points);
                Assert.LessOrEqual(p.Top25.Sum(e => e.FirstPlaceVotes), 62, "first place votes never exceed the ballots");
            }
            // résumé matters: the late-season poll leader has a strong record; polls change
            PollSnapshot last = s.Polls[s.Polls.Count - 2];
            var top = last.Top25[0];
            Assert.That(s.WinsOf(top.TeamId), Is.GreaterThanOrEqualTo(10));
            Assert.IsTrue(s.Polls[1].Top25.Select(e => e.TeamId).SequenceEqual(s.Polls[s.Polls.Count - 2].Top25.Select(e => e.TeamId)) == false);
            Assert.IsTrue(s.Polls.Skip(1).Any(p => p.Top25.Any(e => e.PrevRank > 0 && e.PrevRank != e.Rank)));
            // teams with no losses outrank comparable teams with several
            var preseason = s.Polls[0];
            Assert.AreEqual(0, preseason.Top25[0].PrevRank);
        }

        [Test]
        public void GoldenHelmRaceRunsWeeklyAndCrownsAWinner()
        {
            Season s = Played.Season;
            Assert.AreEqual("Golden Helm Trophy", AwardsCalculator.GoldenHelmName);
            Assert.GreaterOrEqual(s.HelmRace.Count, s.RegularWeeks);
            foreach (HelmSnapshot h in s.HelmRace)
            {
                Assert.LessOrEqual(h.Candidates.Count, 10);
                for (int i = 1; i < h.Candidates.Count; i++) Assert.GreaterOrEqual(h.Candidates[i - 1].Score, h.Candidates[i].Score);
            }
            Assert.IsTrue(s.HelmRace.Any(h => h.Candidates.Count > 0));
            var first = s.HelmRace.First(h => h.Candidates.Count > 0).Candidates.Select(c => c.PlayerId).ToList();
            var lastRace = s.HelmRace[s.HelmRace.Count - 1].Candidates.Select(c => c.PlayerId).ToList();
            Assert.IsFalse(first.SequenceEqual(lastRace), "the race should shift over the season");
            Assert.AreNotEqual(0, s.GoldenHelmId);
            AwardWinner w = s.Awards.FirstOrDefault(a => a.Award == "Golden Helm Trophy");
            Assert.IsNotNull(w);
            Assert.AreEqual(s.GoldenHelmId, w.PlayerId);
            School school = Played.Schools[w.TeamId];
            Player p = school.Team.GetPlayer(w.PlayerId);
            Assert.IsNotNull(p);
            Assert.AreEqual(p.Name, w.PlayerName);
            Assert.IsNotNull(p.FindStats(s.Year));
            Assert.AreEqual(1, Played.History.Count);
        }

        [Test]
        public void StatsAreRecordedForCollegePlayers()
        {
            Season s = Played.Season;
            int withStats = Played.Schools.SelectMany(x => x.Team.Roster).Count(p => p.FindStats(s.Year) != null && p.FindStats(s.Year).Games > 0);
            Assert.Greater(withStats, 4000);
            var top = LeagueLeaders.Top(s, StatCategory.RushYds, 5);
            Assert.AreEqual(5, top.Count);
            Assert.Greater(top[0].Value, 800);
        }

        [Test]
        public void CollegeSeasonIsDeterministic()
        {
            CollegeWorld a = LeagueTestUtil.LoadCollege(61), b = LeagueTestUtil.LoadCollege(61);
            a.Season.SimToEnd(); b.Season.SimToEnd();
            Assert.AreEqual(LeagueTestUtil.Fingerprint(a.Season), LeagueTestUtil.Fingerprint(b.Season));
            Assert.AreEqual(a.Season.GoldenHelmId, b.Season.GoldenHelmId);
            Assert.AreEqual(a.Season.Polls[a.Season.Polls.Count - 1].Top25[0].TeamId, b.Season.Polls[b.Season.Polls.Count - 1].Top25[0].TeamId);
        }

        [Test]
        public void StrongProgramsWinMoreOnAverage()
        {
            Season s = Played.Season;
            double top = Played.Schools.Where(x => x.Prestige >= 5).Average(x => s.WinsOf(x.Id));
            double bottom = Played.Schools.Where(x => x.Prestige <= 2).Average(x => s.WinsOf(x.Id));
            Assert.Greater(top, bottom + 1.5);
        }
    }
}
