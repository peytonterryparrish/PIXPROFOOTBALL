using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    public partial class Season
    {
        public const int ProRegularWeeks = 18;

        // ------------------------------------------------------------------ creation
        public static Season CreatePro(ProLeague league, int year, Rng rng)
        {
            var s = new Season { Kind = SeasonKind.Pro, Year = year, ChampionshipName = "PIX Bowl", RegularWeeks = ProRegularWeeks };
            for (int i = 0; i < league.Teams.Count; i++)
            {
                Team t = league.Teams[i];
                s.Teams.Add(new SeasonTeam { TeamId = t.Id, ConferenceId = t.ConferenceId, DivisionId = t.DivisionId, Name = t.FullName, Abbr = t.Abbreviation, Prestige = t.Prestige });
            }

            // division place from the previous season (or team strength for the first year)
            int[] place = new int[league.Teams.Count];
            bool havePrev = league.Season != null && league.Season.IsComplete && league.Season.Kind == SeasonKind.Pro && !league.Season.IsEmpty;
            if (havePrev)
            {
                var rows = league.Season.Standings();
                for (int i = 0; i < rows.Count; i++) place[rows[i].TeamId] = Math.Max(0, Math.Min(3, rows[i].GroupRank - 1));
            }
            else
            {
                for (int c = 0; c < league.Conferences.Count; c++)
                    for (int d = 0; d < league.Conferences[c].Divisions.Count; d++)
                    {
                        var ids = new List<int>(league.Conferences[c].Divisions[d].TeamIds);
                        ids.Sort((a, b) => league.Teams[b].TeamOverall().CompareTo(league.Teams[a].TeamOverall()));
                        for (int k = 0; k < ids.Count; k++) place[ids[k]] = k;
                    }
            }

            List<Matchup> matchups = ScheduleGenerator.ProMatchups(league, year, place);
            int[] weekOf, bye;
            var divisionOf = new int[league.Teams.Count];
            for (int i = 0; i < divisionOf.Length; i++) divisionOf[i] = league.Teams[i].DivisionId;
            if (!ScheduleGenerator.AssignProWeeks(matchups, league.Teams.Count, rng, out weekOf, out bye, divisionOf))
                throw new InvalidOperationException("Could not build a 17-game schedule.");
            for (int i = 0; i < matchups.Count; i++)
                s.Games.Add(new ScheduledGame { Id = i, Week = weekOf[i], HomeId = matchups[i].Home, AwayId = matchups[i].Away, Kind = GameKind.Regular });
            s.Games.Sort((a, b) => a.Week != b.Week ? a.Week.CompareTo(b.Week) : a.Id.CompareTo(b.Id));
            for (int i = 0; i < s.Games.Count; i++) s.Games[i].Id = i;
            s.SaveRng(rng);
            s.Attach(league);
            return s;
        }

        public int ByeWeekOf(int teamId)
        {
            for (int w = 1; w <= RegularWeeks; w++)
            {
                bool plays = false;
                for (int i = 0; i < Games.Count; i++)
                    if (Games[i].Week == w && Games[i].Kind == GameKind.Regular && Games[i].Involves(teamId)) { plays = true; break; }
                if (!plays) return w;
            }
            return 0;
        }

        // ------------------------------------------------------------------ postseason flow
        private void ProAfterWeek(int played)
        {
            if (Phase == SeasonPhase.Regular && played >= RegularWeeks)
            {
                Phase = SeasonPhase.Postseason;
                BuildProSeeds();
                PostseasonStage = 1;
                BuildWildCard(RegularWeeks + 1);
                return;
            }
            if (Phase != SeasonPhase.Postseason) return;
            if (!AllPlayed(played)) return;
            switch (PostseasonStage)
            {
                case 1: PostseasonStage = 2; BuildDivisional(played + 1); break;
                case 2: PostseasonStage = 3; BuildConferenceFinals(played + 1); break;
                case 3: PostseasonStage = 4; BuildPixBowl(played + 1); break;
                case 4: CompleteProSeason(); break;
            }
        }

        private void BuildProSeeds()
        {
            Seeds.Clear();
            var rows = StandingsCalculator.Compute(this);
            var confIds = new List<int>();
            for (int i = 0; i < rows.Count; i++) if (!confIds.Contains(rows[i].ConferenceId)) confIds.Add(rows[i].ConferenceId);
            confIds.Sort();
            for (int ci = 0; ci < confIds.Count; ci++)
            {
                int conf = confIds[ci];
                var inConf = rows.FindAll(r => r.ConferenceId == conf);
                inConf.Sort((a, b) => a.ConferenceRank.CompareTo(b.ConferenceRank));
                var winners = inConf.FindAll(r => r.GroupRank == 1);
                var others = inConf.FindAll(r => r.GroupRank != 1);
                int seed = 1;
                for (int i = 0; i < winners.Count; i++) Seeds.Add(new PlayoffSeed { ConferenceId = conf, Seed = seed++, TeamId = winners[i].TeamId, ConferenceChampion = false });
                for (int i = 0; i < others.Count && seed <= 7; i++) Seeds.Add(new PlayoffSeed { ConferenceId = conf, Seed = seed++, TeamId = others[i].TeamId });
            }
        }

        private List<PlayoffSeed> SeedsOf(int conf)
        {
            var l = Seeds.FindAll(x => x.ConferenceId == conf);
            l.Sort((a, b) => a.Seed.CompareTo(b.Seed));
            return l;
        }

        private List<int> ProConferenceIds()
        {
            var ids = new List<int>();
            for (int i = 0; i < Seeds.Count; i++) if (!ids.Contains(Seeds[i].ConferenceId)) ids.Add(Seeds[i].ConferenceId);
            ids.Sort();
            return ids;
        }

        private int SeedOf(int teamId)
        {
            for (int i = 0; i < Seeds.Count; i++) if (Seeds[i].TeamId == teamId) return Seeds[i].Seed;
            return 99;
        }

        private void BuildWildCard(int week)
        {
            var confs = ProConferenceIds();
            for (int c = 0; c < confs.Count; c++)
            {
                var sd = SeedsOf(confs[c]);
                AddGame(week, sd[1].TeamId, sd[6].TeamId, GameKind.PlayoffRound, "Wild Card", 1, false);
                AddGame(week, sd[2].TeamId, sd[5].TeamId, GameKind.PlayoffRound, "Wild Card", 1, false);
                AddGame(week, sd[3].TeamId, sd[4].TeamId, GameKind.PlayoffRound, "Wild Card", 1, false);
            }
        }

        private void BuildDivisional(int week)
        {
            var confs = ProConferenceIds();
            for (int c = 0; c < confs.Count; c++)
            {
                var sd = SeedsOf(confs[c]);
                var alive = new List<int> { sd[0].TeamId };
                for (int i = 0; i < Games.Count; i++)
                    if (Games[i].Round == 1 && Games[i].Kind == GameKind.PlayoffRound && Games[i].Played && SeedConf(Games[i].WinnerId) == confs[c]) alive.Add(Games[i].WinnerId);
                alive.Sort((a, b) => SeedOf(a).CompareTo(SeedOf(b)));
                AddGame(week, alive[0], alive[alive.Count - 1], GameKind.PlayoffRound, "Divisional Round", 2, false);
                if (alive.Count >= 4) AddGame(week, alive[1], alive[2], GameKind.PlayoffRound, "Divisional Round", 2, false);
            }
        }

        private int SeedConf(int teamId)
        {
            for (int i = 0; i < Seeds.Count; i++) if (Seeds[i].TeamId == teamId) return Seeds[i].ConferenceId;
            return -1;
        }

        private void BuildConferenceFinals(int week)
        {
            var confs = ProConferenceIds();
            for (int c = 0; c < confs.Count; c++)
            {
                var alive = new List<int>();
                for (int i = 0; i < Games.Count; i++)
                    if (Games[i].Round == 2 && Games[i].Kind == GameKind.PlayoffRound && Games[i].Played && SeedConf(Games[i].WinnerId) == confs[c]) alive.Add(Games[i].WinnerId);
                alive.Sort((a, b) => SeedOf(a).CompareTo(SeedOf(b)));
                string name = Info(alive[0]) != null ? ConferenceName(confs[c]) + " Championship" : "Conference Championship";
                AddGame(week, alive[0], alive[1], GameKind.PlayoffRound, name, 3, false);
            }
        }

        private string ConferenceName(int confId)
        {
            if (_pro != null) { Conference c = _pro.GetConference(confId); if (c != null) return c.Name.Replace(" Conference", ""); }
            return "Conference " + confId;
        }

        private void BuildPixBowl(int week)
        {
            var champs = new List<int>();
            for (int i = 0; i < Games.Count; i++) if (Games[i].Round == 3 && Games[i].Played) champs.Add(Games[i].WinnerId);
            // better regular-season record hosts nominally; the game is played at a neutral site
            var rows = StandingsCalculator.Compute(this);
            champs.Sort((a, b) =>
            {
                StandingsRow ra = rows.Find(r => r.TeamId == a), rb = rows.Find(r => r.TeamId == b);
                return rb.Pct.CompareTo(ra.Pct);
            });
            AddGame(week, champs[0], champs[1], GameKind.Final, ChampionshipName, 4, true);
        }

        private void CompleteProSeason()
        {
            ScheduledGame fin = null;
            for (int i = Games.Count - 1; i >= 0; i--) if (Games[i].Kind == GameKind.Final) { fin = Games[i]; break; }
            ChampionTeamId = fin.WinnerId;
            RunnerUpTeamId = fin.LoserId;
            Phase = SeasonPhase.Complete;
            FinalizeSeason();
        }

        // ------------------------------------------------------------------ wrap-up
        private void FinalizeSeason()
        {
            if (Finalized) return;
            Finalized = true;
            var teams = AllTeams();
            if (Records != null) Records.UpdateSeason(teams, Year);
            if (IsCollege) { CompleteCollegeAwards(teams); }
            else
            {
                AwardsCalculator.ComputePro(this, teams);
                if (_pro != null)
                {
                    var sum = new SeasonSummary { Year = Year, ChampionTeamId = ChampionTeamId, ChampionName = NameOf(ChampionTeamId), RunnerUpTeamId = RunnerUpTeamId, MvpPlayerId = MvpId, OpoyPlayerId = OpoyId, DpoyPlayerId = DpoyId, RoyPlayerId = RoyId };
                    for (int i = 0; i < Awards.Count; i++) if (Awards[i].Award == "MVP") sum.MvpName = Awards[i].PlayerName;
                    _pro.History.Add(sum);
                }
            }
        }
    }
}
