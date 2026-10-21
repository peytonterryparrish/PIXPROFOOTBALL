using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    public partial class Season
    {
        public const int CfpTeams = 12;

        // ------------------------------------------------------------------ creation
        public static Season CreateCollege(CollegeWorld world, int year, Rng rng)
        {
            var s = new Season { Kind = SeasonKind.College, Year = year, ChampionshipName = world.ChampionshipName, PlayoffName = world.PlayoffName };
            for (int i = 0; i < world.Schools.Count; i++)
            {
                School sc = world.Schools[i];
                s.Teams.Add(new SeasonTeam
                {
                    TeamId = sc.Id, ConferenceId = sc.ConferenceId, DivisionId = -1, Name = sc.Name, Abbr = sc.Abbreviation,
                    Prestige = sc.Prestige, Region = (int)sc.Region
                });
            }
            List<Matchup> matchups = ScheduleGenerator.CollegeMatchups(world, rng);
            int[] weekOf;
            s.RegularWeeks = ScheduleGenerator.AssignCollegeWeeks(matchups, world.Schools.Count, rng, out weekOf);
            for (int i = 0; i < matchups.Count; i++)
                s.Games.Add(new ScheduledGame { Id = i, Week = weekOf[i], HomeId = matchups[i].Home, AwayId = matchups[i].Away, Kind = GameKind.Regular });
            s.Games.Sort((a, b) => a.Week != b.Week ? a.Week.CompareTo(b.Week) : a.Id.CompareTo(b.Id));
            for (int i = 0; i < s.Games.Count; i++) s.Games[i].Id = i;
            s.SaveRng(rng);
            s.Attach(world);
            // preseason poll
            s.Polls.Add(Rankings.Poll(s, 0, "Preseason", s.GetRng(), null));
            return s;
        }

        /// <summary>Latest poll (or null before the first one).</summary>
        public PollSnapshot LatestPoll { get { return Polls.Count == 0 ? null : Polls[Polls.Count - 1]; } }

        public int ConferenceChampionshipWeek { get { return RegularWeeks + 1; } }

        // ------------------------------------------------------------------ flow
        private void CollegeAfterWeek(int played)
        {
            Rng rng = GetRng();
            if (Phase == SeasonPhase.Regular)
            {
                PollSnapshot prev = LatestPoll;
                Polls.Add(Rankings.Poll(this, played, "Week " + played, rng.Fork(), prev));
                HelmRace.Add(AwardsCalculator.HelmCandidates(this, played));
                SaveRng(rng);
                if (played >= RegularWeeks)
                {
                    Phase = SeasonPhase.Postseason;
                    PostseasonStage = 1;
                    BuildConferenceChampionships(played + 1);
                }
                return;
            }
            if (Phase != SeasonPhase.Postseason) { SaveRng(rng); return; }
            if (!AllPlayed(played)) return;

            switch (PostseasonStage)
            {
                case 1:
                {
                    // conference championships are done: final regular poll, Golden Helm, playoff field
                    Polls.Add(Rankings.Poll(this, played, "Final Regular Season", rng.Fork(), LatestPoll));
                    HelmSnapshot hs = AwardsCalculator.HelmCandidates(this, played);
                    HelmRace.Add(hs);
                    if (hs.Candidates.Count > 0) GoldenHelmId = hs.Candidates[0].PlayerId;
                    BuildCfpField();
                    BuildCfpRoundOne(played + 1);
                    PostseasonStage = 2;
                    break;
                }
                case 2:
                    BuildQuarterfinalsAndBowls(played + 1);
                    PostseasonStage = 3;
                    break;
                case 3:
                    BuildSemifinals(played + 1);
                    PostseasonStage = 4;
                    break;
                case 4:
                    BuildNationalChampionship(played + 1);
                    PostseasonStage = 5;
                    break;
                case 5:
                {
                    ScheduledGame fin = null;
                    for (int i = Games.Count - 1; i >= 0; i--) if (Games[i].Kind == GameKind.Final) { fin = Games[i]; break; }
                    ChampionTeamId = fin.WinnerId;
                    RunnerUpTeamId = fin.LoserId;
                    Polls.Add(Rankings.Poll(this, played, "Final", rng.Fork(), LatestPoll));
                    Phase = SeasonPhase.Complete;
                    FinalizeSeason();
                    break;
                }
            }
            SaveRng(rng);
        }

        private void BuildConferenceChampionships(int week)
        {
            var rows = StandingsCalculator.Compute(this);
            var confIds = new List<int>();
            for (int i = 0; i < Teams.Count; i++) if (Teams[i].ConferenceId >= 0 && !confIds.Contains(Teams[i].ConferenceId)) confIds.Add(Teams[i].ConferenceId);
            confIds.Sort();
            for (int c = 0; c < confIds.Count; c++)
            {
                var inConf = rows.FindAll(r => r.ConferenceId == confIds[c]);
                inConf.Sort((a, b) => a.ConferenceRank.CompareTo(b.ConferenceRank));
                if (inConf.Count < 2) continue;
                string confName = _college != null && _college.GetConference(confIds[c]) != null ? _college.GetConference(confIds[c]).Abbreviation : ("Conf " + confIds[c]);
                AddGame(week, inConf[0].TeamId, inConf[1].TeamId, GameKind.ConferenceChampionship, confName + " Championship", 0, false);
            }
        }

        private string[] CfpBowls
        {
            get
            {
                var l = new List<string>();
                if (_college != null) foreach (ConfBowlDef b in _college.Bowls) if (b.IsPlayoffBowl) l.Add(b.Name);
                string[] fallback = { "Thorncrest Bowl", "Crystal Cane Bowl", "Citrus Comet Bowl", "Cottoncrest Classic", "Festival Flame Bowl", "Orchard Gold Bowl" };
                for (int i = l.Count; i < 6; i++) l.Add(fallback[i]);
                return l.ToArray();
            }
        }

        private void BuildCfpField()
        {
            Seeds.Clear();
            List<int> ranking = Rankings.CommitteeRanking(this);
            // conference champions: winners of the title games (or best-ranked team of a conference without a game)
            var champs = new List<int>();
            for (int i = 0; i < Games.Count; i++)
                if (Games[i].Kind == GameKind.ConferenceChampionship && Games[i].Played) champs.Add(Games[i].WinnerId);
            champs.Sort((a, b) => ranking.IndexOf(a).CompareTo(ranking.IndexOf(b)));
            var auto = new List<int>();
            for (int i = 0; i < champs.Count && auto.Count < 5; i++) auto.Add(champs[i]);
            var field = new List<int>(auto);
            for (int i = 0; i < ranking.Count && field.Count < CfpTeams; i++) if (!field.Contains(ranking[i])) field.Add(ranking[i]);
            // seeds 1-4: top four champions by ranking; 5-12 the rest by ranking
            var byes = new List<int>();
            for (int i = 0; i < auto.Count && byes.Count < 4; i++) byes.Add(auto[i]);
            var rest = new List<int>();
            for (int i = 0; i < field.Count; i++) if (!byes.Contains(field[i])) rest.Add(field[i]);
            rest.Sort((a, b) => ranking.IndexOf(a).CompareTo(ranking.IndexOf(b)));
            int seed = 1;
            for (int i = 0; i < byes.Count; i++) Seeds.Add(new PlayoffSeed { ConferenceId = -1, Seed = seed++, TeamId = byes[i], ConferenceChampion = true });
            for (int i = 0; i < rest.Count; i++) Seeds.Add(new PlayoffSeed { ConferenceId = -1, Seed = seed++, TeamId = rest[i], ConferenceChampion = auto.Contains(rest[i]) });
            Seeds.Sort((a, b) => a.Seed.CompareTo(b.Seed));
        }

        private int TeamOfSeed(int seed)
        {
            for (int i = 0; i < Seeds.Count; i++) if (Seeds[i].Seed == seed) return Seeds[i].TeamId;
            return -1;
        }

        private void BuildCfpRoundOne(int week)
        {
            int[][] pairs = { new[] { 5, 12 }, new[] { 6, 11 }, new[] { 7, 10 }, new[] { 8, 9 } };
            for (int i = 0; i < pairs.Length; i++)
                AddGame(week, TeamOfSeed(pairs[i][0]), TeamOfSeed(pairs[i][1]), GameKind.PlayoffRound, PlayoffName + " - First Round", 1, false);
        }

        private int RoundOneWinnerForSeeds(int a, int b)
        {
            int ta = TeamOfSeed(a), tb = TeamOfSeed(b);
            for (int i = 0; i < Games.Count; i++)
                if (Games[i].Kind == GameKind.PlayoffRound && Games[i].Round == 1 && Games[i].Involves(ta) && Games[i].Involves(tb)) return Games[i].WinnerId;
            return -1;
        }

        private void BuildQuarterfinalsAndBowls(int week)
        {
            string[] bowls = CfpBowls;
            int[][] qf = { new[] { 1, 8, 9 }, new[] { 4, 5, 12 }, new[] { 2, 7, 10 }, new[] { 3, 6, 11 } };
            for (int i = 0; i < qf.Length; i++)
            {
                int top = TeamOfSeed(qf[i][0]);
                int opp = RoundOneWinnerForSeeds(qf[i][1], qf[i][2]);
                AddGame(week, top, opp, GameKind.PlayoffRound, PlayoffName + " Quarterfinal - " + bowls[i], 2, true);
            }
            BuildOtherBowls(week);
        }

        private void BuildSemifinals(int week)
        {
            string[] bowls = CfpBowls;
            // QF order: [1 vs 8/9], [4 vs 5/12], [2 vs 7/10], [3 vs 6/11]
            var qfWinners = new List<int>();
            for (int i = 0; i < Games.Count; i++) if (Games[i].Kind == GameKind.PlayoffRound && Games[i].Round == 2) qfWinners.Add(Games[i].WinnerId);
            // order by creation (games are appended in order)
            AddGame(week, qfWinners[0], qfWinners[1], GameKind.PlayoffRound, PlayoffName + " Semifinal - " + bowls[4], 3, true);
            AddGame(week, qfWinners[2], qfWinners[3], GameKind.PlayoffRound, PlayoffName + " Semifinal - " + bowls[5], 3, true);
        }

        private void BuildNationalChampionship(int week)
        {
            var w = new List<int>();
            for (int i = 0; i < Games.Count; i++) if (Games[i].Kind == GameKind.PlayoffRound && Games[i].Round == 3) w.Add(Games[i].WinnerId);
            AddGame(week, w[0], w[1], GameKind.Final, ChampionshipName, 4, true);
        }

        private void BuildOtherBowls(int week)
        {
            List<int> ranking = Rankings.CommitteeRanking(this);
            var inCfp = new HashSet<int>();
            for (int i = 0; i < Seeds.Count; i++) inCfp.Add(Seeds[i].TeamId);

            // the non-playoff bowls from the data file (35 of the 41); fall back to a small generated list if none
            var bowlNames = new List<string>();
            if (_college != null)
                foreach (ConfBowlDef b in _college.Bowls) if (b.IsBowl && !b.IsPlayoffBowl) bowlNames.Add(b.Name);
            if (bowlNames.Count == 0) foreach (string n in NameGenerator.BowlNames) bowlNames.Add(n);

            var wins = new Dictionary<int, int>();
            for (int g = 0; g < Games.Count; g++)
            {
                if (!Games[g].Played || Games[g].WinnerId < 0) continue;
                wins[Games[g].WinnerId] = (wins.ContainsKey(Games[g].WinnerId) ? wins[Games[g].WinnerId] : 0) + 1;
            }
            // bowl-eligible teams (6+ wins) first, ranked; if the bowl slate is not full, the best 5-win (then 4-win) teams fill in
            int needed = bowlNames.Count * 2;
            var field = new List<int>();
            for (int minWins = 6; minWins >= 0 && field.Count < needed; minWins--)
            {
                for (int i = 0; i < ranking.Count && field.Count < needed; i++)
                {
                    int id = ranking[i];
                    if (inCfp.Contains(id) || field.Contains(id)) continue;
                    int w = wins.ContainsKey(id) ? wins[id] : 0;
                    if (w >= minWins) field.Add(id);
                }
            }
            int bowls = Math.Min(field.Count / 2, bowlNames.Count);
            // best pairings go to the first (most prominent) bowls in the data order
            for (int i = 0; i < bowls; i++)
            {
                int a = field[i * 2], b = field[i * 2 + 1];
                bool aHome = (i % 2 == 0);
                AddGame(week, aHome ? a : b, aHome ? b : a, GameKind.Bowl, bowlNames[i], 0, true);
            }
        }

        private void CompleteCollegeAwards(List<Team> teams)
        {
            // Golden Helm: the winner was chosen before the playoff; make sure the award list is complete
            Awards.Clear();
            if (GoldenHelmId != 0)
            {
                HelmSnapshot last = HelmRace.Count > 0 ? HelmRace[HelmRace.Count - 1] : null;
                if (last != null)
                {
                    for (int i = 0; i < last.Candidates.Count; i++)
                        if (last.Candidates[i].PlayerId == GoldenHelmId)
                        {
                            HelmEntry e = last.Candidates[i];
                            Awards.Add(new AwardWinner { Award = AwardsCalculator.GoldenHelmName, PlayerId = e.PlayerId, PlayerName = e.Name, TeamId = e.TeamId, Pos = e.Pos, Score = e.Score });
                        }
                }
            }
            if (_college != null)
            {
                var sum = new SeasonSummary { Year = Year, ChampionTeamId = ChampionTeamId, ChampionName = NameOf(ChampionTeamId), RunnerUpTeamId = RunnerUpTeamId, MvpPlayerId = GoldenHelmId };
                for (int i = 0; i < Awards.Count; i++) sum.MvpName = Awards[i].PlayerName;
                _college.History.Add(sum);
            }
        }
    }
}
