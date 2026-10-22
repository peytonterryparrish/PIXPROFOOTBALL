using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    // View models for the College Network screens (hub, rankings, standings, schedules, box scores, leaders, Golden Helm race, bracket, bowls, team pages).
    // Plain data the UI binds to; nothing here changes the save. The scouting screens are in CollegeScoutViews.cs.

    public sealed class CollegeGameRow
    {
        public int GameId, Week;
        public string Label = "", DateLabel = "";
        public GameKind Kind;
        public int HomeId, AwayId;
        public string Home = "", Away = "", HomeAbbr = "", AwayAbbr = "", HomeCrest = "", AwayCrest = "";
        public int HomeRank, AwayRank;
        public string HomeRecord = "", AwayRecord = "";
        public bool Played, Overtime, Neutral, HasBox;
        public int HomeScore, AwayScore;
        /// <summary>Not played and on this week: can be watched live (spectator), as condensed highlights, attended, or simmed.</summary>
        public bool Watchable;
        /// <summary>Public top-100 draft-eligible players on the field (guides the user's game visits).</summary>
        public int ProspectsOfInterest;
        public int WinnerId = -1;
    }

    public sealed class CollegePollRow
    {
        public int Rank, TeamId, PrevRank, Movement, Points, FirstPlaceVotes;
        public string Name = "", ShortName = "", Abbr = "", Record = "", Conference = "", Crest = "";
        public bool Unranked;
    }

    public sealed class CollegeHubView
    {
        public int Year, Week;
        public string WeekTitle = "", DateLabel = "", StageText = "", PollLabel = "";
        public bool SeasonComplete;
        public List<CollegePollRow> Top25 = new List<CollegePollRow>();
        public List<HelmEntry> Helm = new List<HelmEntry>();
        public List<CollegeGameRow> Games = new List<CollegeGameRow>();
        public List<CollegeGameRow> GamesOfTheWeek = new List<CollegeGameRow>();
        public string Champion = "";
        public int ChampionId = -1;
        public string HelmWinner = "";
        public int ScoutPoints, ScoutPointsPerWeek, AutoPointsPerWeek, BoardCount, StarCount;
        public bool CanAttendGame;
    }

    public sealed class CollegeStandingRow
    {
        public int Rank, TeamId, PollRank;
        public string Name = "", ShortName = "", Abbr = "", Crest = "";
        public string ConfRecord = "", Record = "";
        public int PointsFor, PointsAgainst, Streak;
        public bool ConferenceLeader;
    }

    public sealed class CollegeConferenceView
    {
        public int ConferenceId;
        public string Name = "", Abbr = "", Logo = "";
        public List<CollegeStandingRow> Rows = new List<CollegeStandingRow>();
    }

    public sealed class CollegeScheduleRow
    {
        public int Week, GameId = -1;
        public string Label = "", DateLabel = "";
        public bool Home, Neutral, Bye, Played, Won, Tie, HasBox;
        public int OpponentId = -1, OpponentRank;
        public string Opponent = "";
        public int TeamScore, OpponentScore;
        public GameKind Kind;
    }

    public sealed class CollegeBoxView
    {
        public CollegeBox Box;
        public string Home = "", Away = "", HomeAbbr = "", AwayAbbr = "";
        public string Title = "";
        public string DateLabel = "";
    }

    public sealed class CollegeLeaderRow
    {
        public int Rank, PlayerId, SchoolId, Games;
        public string Name = "", School = "";
        public Position Pos;
        public float Value;
        public ClassYear Class;
    }

    public sealed class BracketSeed
    {
        public int Seed, TeamId;
        public string Name = "", Record = "";
        public bool ConferenceChampion, Eliminated, Champion;
    }

    public sealed class BracketRound
    {
        public int Round;
        public string Name = "";
        public List<CollegeGameRow> Games = new List<CollegeGameRow>();
    }

    public sealed class CollegeBracketView
    {
        public bool Built;
        public string PlayoffName = "", ChampionshipName = "";
        public List<BracketSeed> Seeds = new List<BracketSeed>();
        public List<BracketRound> Rounds = new List<BracketRound>();
        public List<CollegeGameRow> Bowls = new List<CollegeGameRow>();
        public int ChampionId = -1;
        public string Champion = "";
    }

    public sealed class CollegeRosterRow
    {
        public int PlayerId, Jersey, Stars, PublicRank;
        public string Name = "", Height = "";
        public int WeightLb;
        public Position Pos;
        public ClassYear Class;
        public bool Redshirting, Eligible;
        public string SeasonLine = "";
        public ScoutTier Tier;
        public string OverallText = "?";
    }

    public sealed class CollegeTeamView
    {
        public int SchoolId;
        public string Name = "", ShortName = "", Mascot = "", Abbr = "", Crest = "", Conference = "", Stadium = "", City = "", State = "", PrestigeLabel = "";
        public string PrimaryColor = "", SecondaryColor = "";
        public int Prestige, StadiumCapacity, Rank, Wins, Losses, ConfWins, ConfLosses;
        public string HeadCoach = "", OffensiveCoordinator = "", DefensiveCoordinator = "";
        public int RosterCount;
        public List<CollegeScheduleRow> Schedule = new List<CollegeScheduleRow>();
        public List<CollegeRosterRow> Roster = new List<CollegeRosterRow>();
        public List<CollegeSeasonRecord> History = new List<CollegeSeasonRecord>();
        public List<CollegeHonor> Honors = new List<CollegeHonor>();
    }

    public sealed class CollegeRecruitRow
    {
        public int Rank, SchoolId, Commits, FiveStars, FourStars;
        public string Name = "", Crest = "";
        public float AvgStars, Points;
    }

    public sealed class CollegeChampionRow
    {
        public int Year, ChampionId, RunnerUpId, HelmPlayerId;
        public string Champion = "", RunnerUp = "", HelmWinner = "";
    }

    /// <summary>The data each College Network screen needs. See Docs/Career.md ("College Network") for the screen to call map.</summary>
    public static class CollegeViews
    {
        private static CollegeWorld W(CareerSave s) { return s.College; }

        private static string Name(CareerSave s, int id) { School sc = s.College != null ? s.College.GetSchool(id) : null; return sc != null ? sc.Name : "School " + id; }
        private static string Short(CareerSave s, int id) { School sc = s.College != null ? s.College.GetSchool(id) : null; return sc != null ? sc.ShortName : "School " + id; }

        private static int Rank(Season cs, int teamId)
        {
            PollSnapshot p = cs.LatestPoll;
            PollEntry e = p != null ? p.Find(teamId) : null;
            return e != null ? e.Rank : 0;
        }

        // ================================================================ hub
        public static CollegeHubView Hub(CareerSave s)
        {
            var v = new CollegeHubView();
            if (s.College == null) return v;
            CollegeNet.Ensure(s);
            Season cs = s.College.Season;
            v.Year = cs.Year; v.Week = cs.IsComplete ? cs.LastScheduledWeek : cs.Week; v.SeasonComplete = cs.IsComplete;
            v.WeekTitle = CollegeCalendar.WeekTitle(cs, v.Week); v.DateLabel = CollegeCalendar.DateLabel(cs.Year, v.Week);
            v.StageText = cs.IsComplete ? cs.ChampionshipName + " champions: " + Name(s, cs.ChampionTeamId) : v.WeekTitle;
            PollSnapshot poll = cs.LatestPoll;
            v.PollLabel = poll != null ? poll.Label : "";
            v.Top25 = Top25(s);
            v.Helm = HelmRace(s);
            v.Games = WeekGames(s, cs.IsComplete ? cs.LastScheduledWeek : cs.Week);
            var picks = new List<CollegeGameRow>(v.Games);
            picks.Sort((a, b) =>
            {
                int sa = (a.HomeRank > 0 ? 26 - a.HomeRank : 0) + (a.AwayRank > 0 ? 26 - a.AwayRank : 0) + a.ProspectsOfInterest;
                int sb = (b.HomeRank > 0 ? 26 - b.HomeRank : 0) + (b.AwayRank > 0 ? 26 - b.AwayRank : 0) + b.ProspectsOfInterest;
                return sb.CompareTo(sa);
            });
            for (int i = 0; i < picks.Count && i < 5; i++) v.GamesOfTheWeek.Add(picks[i]);
            if (cs.IsComplete) { v.ChampionId = cs.ChampionTeamId; v.Champion = Name(s, cs.ChampionTeamId); }
            for (int i = 0; i < cs.Awards.Count; i++) if (cs.Awards[i].Award == AwardsCalculator.GoldenHelmName) v.HelmWinner = cs.Awards[i].PlayerName + " (" + Short(s, cs.Awards[i].TeamId) + ")";
            v.ScoutPoints = CollegeScouting.PointsRemaining(s); v.ScoutPointsPerWeek = CollegeScouting.PointsPerWeek(s); v.AutoPointsPerWeek = CollegeScouting.AutoPointsPerWeek(s);
            for (int i = 0; i < s.Net.Board.Count; i++) { if (s.Net.Board[i].OnBoard) v.BoardCount++; if (s.Net.Board[i].Starred) v.StarCount++; }
            v.CanAttendGame = !cs.IsComplete && !(s.Net.AttendYear == cs.Year && s.Net.AttendWeek == cs.Week) && v.ScoutPoints >= CollegeScouting.GameCost
                              && (s.Phase == CareerPhase.RegularSeason || s.Phase == CareerPhase.Playoffs || s.Phase == CareerPhase.PixBowl || s.Phase == CareerPhase.Preseason);
            return v;
        }

        // ================================================================ rankings
        public static List<CollegePollRow> Top25(CareerSave s, int pollIndex = -1)
        {
            var rows = new List<CollegePollRow>();
            if (s.College == null) return rows;
            Season cs = s.College.Season;
            PollSnapshot p = pollIndex >= 0 && pollIndex < cs.Polls.Count ? cs.Polls[pollIndex] : cs.LatestPoll;
            if (p == null) return rows;
            for (int i = 0; i < p.Top25.Count; i++)
            {
                PollEntry e = p.Top25[i];
                School sc = s.College.GetSchool(e.TeamId);
                rows.Add(new CollegePollRow
                {
                    Rank = e.Rank, TeamId = e.TeamId, PrevRank = e.PrevRank, Movement = e.PrevRank > 0 ? e.PrevRank - e.Rank : 0, Points = e.Points, FirstPlaceVotes = e.FirstPlaceVotes,
                    Name = e.Name, ShortName = sc != null ? sc.ShortName : e.Name, Abbr = sc != null ? sc.Abbreviation : "", Record = e.Record,
                    Conference = sc != null ? s.College.ConferenceNameOf(sc) : "", Crest = sc != null ? sc.Logo : "", Unranked = false
                });
            }
            return rows;
        }

        /// <summary>The labels of every poll of the season so far ("Preseason", "Week 3" ...).</summary>
        public static List<string> PollLabels(CareerSave s)
        {
            var l = new List<string>();
            if (s.College == null) return l;
            for (int i = 0; i < s.College.Season.Polls.Count; i++) l.Add(s.College.Season.Polls[i].Label);
            return l;
        }

        // ================================================================ games
        public static CollegeGameRow GameRow(CareerSave s, ScheduledGame g)
        {
            Season cs = s.College.Season;
            School h = s.College.GetSchool(g.HomeId), a = s.College.GetSchool(g.AwayId);
            var r = new CollegeGameRow
            {
                GameId = g.Id, Week = g.Week, Label = string.IsNullOrEmpty(g.Label) ? CollegeCalendar.WeekTitle(cs, g.Week) : g.Label, DateLabel = CollegeCalendar.DateLabel(cs.Year, g.Week), Kind = g.Kind,
                HomeId = g.HomeId, AwayId = g.AwayId, Home = h != null ? h.ShortName : "", Away = a != null ? a.ShortName : "", HomeAbbr = h != null ? h.Abbreviation : "", AwayAbbr = a != null ? a.Abbreviation : "",
                HomeCrest = h != null ? h.Logo : "", AwayCrest = a != null ? a.Logo : "", Played = g.Played, Overtime = g.Overtime, Neutral = g.Neutral, HomeScore = g.HomeScore, AwayScore = g.AwayScore,
                WinnerId = g.WinnerId
            };
            CollegeBox box = CollegeBoxes.Find(s, cs.Year, g.Id);
            r.HasBox = box != null;
            // ranks as they stood before the game
            int hr = 0, ar = 0;
            if (box != null) { hr = box.HomeRank; ar = box.AwayRank; }
            else
            {
                PollSnapshot best = null;
                for (int i = 0; i < cs.Polls.Count; i++) if (cs.Polls[i].Week <= g.Week - 1 && (best == null || cs.Polls[i].Week >= best.Week)) best = cs.Polls[i];
                PollEntry he = best != null ? best.Find(g.HomeId) : null, ae = best != null ? best.Find(g.AwayId) : null;
                hr = he != null ? he.Rank : 0; ar = ae != null ? ae.Rank : 0;
            }
            r.HomeRank = hr; r.AwayRank = ar;
            int hw = 0, hl = 0, aw = 0, al = 0;
            for (int i = 0; i < cs.Games.Count; i++)
            {
                ScheduledGame o = cs.Games[i];
                if (!o.Played || o.Week >= g.Week) continue;
                if (o.Involves(g.HomeId)) { if (o.WinnerId == g.HomeId) hw++; else if (o.LoserId == g.HomeId) hl++; }
                if (o.Involves(g.AwayId)) { if (o.WinnerId == g.AwayId) aw++; else if (o.LoserId == g.AwayId) al++; }
            }
            r.HomeRecord = hw + "-" + hl; r.AwayRecord = aw + "-" + al;
            r.Watchable = !g.Played && !cs.IsComplete && g.Week == cs.Week;
            if (!g.Played && s.Pro.DraftClass.Count == 0)
            {
                PoolCache pool = CollegeScouting.Pool(s);
                int n = 0;
                for (int i = 0; i < pool.List.Count && i < 100; i++) if (pool.List[i].S.Id == g.HomeId || pool.List[i].S.Id == g.AwayId) n++;
                r.ProspectsOfInterest = n;
            }
            return r;
        }

        public static List<CollegeGameRow> WeekGames(CareerSave s, int week)
        {
            var l = new List<CollegeGameRow>();
            if (s.College == null) return l;
            Season cs = s.College.Season;
            var games = cs.GamesOfWeek(week);
            for (int i = 0; i < games.Count; i++) l.Add(GameRow(s, games[i]));
            l.Sort((a, b) =>
            {
                int sa = (a.HomeRank > 0 ? 26 - a.HomeRank : 0) + (a.AwayRank > 0 ? 26 - a.AwayRank : 0);
                int sb = (b.HomeRank > 0 ? 26 - b.HomeRank : 0) + (b.AwayRank > 0 ? 26 - b.AwayRank : 0);
                return sa != sb ? sb.CompareTo(sa) : a.GameId.CompareTo(b.GameId);
            });
            return l;
        }

        /// <summary>Highest scheduled week of the season (regular weeks, title games, playoff, bowls).</summary>
        public static int LastWeek(CareerSave s) { return s.College == null ? 0 : s.College.Season.LastScheduledWeek; }

        public static List<CollegeScheduleRow> Schedule(CareerSave s, int schoolId)
        {
            var rows = new List<CollegeScheduleRow>();
            if (s.College == null) return rows;
            Season cs = s.College.Season;
            var games = cs.GamesOfTeam(schoolId);
            int gi = 0;
            for (int w = 1; w <= cs.RegularWeeks; w++)
            {
                if (gi < games.Count && games[gi].Week == w) rows.Add(ScheduleRowOf(s, games[gi++], schoolId));
                else rows.Add(new CollegeScheduleRow { Week = w, Bye = true, Label = "Week " + w, DateLabel = CollegeCalendar.DateLabel(cs.Year, w) });
            }
            for (; gi < games.Count; gi++) rows.Add(ScheduleRowOf(s, games[gi], schoolId));
            return rows;
        }

        private static CollegeScheduleRow ScheduleRowOf(CareerSave s, ScheduledGame g, int teamId)
        {
            Season cs = s.College.Season;
            bool home = g.HomeId == teamId;
            int opp = g.OpponentOf(teamId);
            var r = new CollegeScheduleRow
            {
                Week = g.Week, GameId = g.Id, Kind = g.Kind, Label = string.IsNullOrEmpty(g.Label) ? "Week " + g.Week : g.Label, DateLabel = CollegeCalendar.DateLabel(cs.Year, g.Week), Home = home, Neutral = g.Neutral,
                Played = g.Played, OpponentId = opp, Opponent = Short(s, opp), OpponentRank = Rank(cs, opp), HasBox = CollegeBoxes.Find(s, cs.Year, g.Id) != null
            };
            if (g.Played) { r.TeamScore = home ? g.HomeScore : g.AwayScore; r.OpponentScore = home ? g.AwayScore : g.HomeScore; r.Won = g.WinnerId == teamId; r.Tie = g.Tie; }
            return r;
        }

        public static CollegeBoxView BoxScore(CareerSave s, int gameId, int year = -1)
        {
            if (s.College == null) return null;
            if (year < 0) year = s.College.Season.Year;
            CollegeBox b = CollegeBoxes.Find(s, year, gameId);
            if (b == null) return null;
            School h = s.College.GetSchool(b.HomeId), a = s.College.GetSchool(b.AwayId);
            return new CollegeBoxView
            {
                Box = b, Home = h != null ? h.ShortName : "", Away = a != null ? a.ShortName : "", HomeAbbr = h != null ? h.Abbreviation : "", AwayAbbr = a != null ? a.Abbreviation : "",
                Title = (string.IsNullOrEmpty(b.Label) ? "Week " + b.Week : b.Label) + ": " + (a != null ? a.ShortName : "") + " " + b.AwayScore + ", " + (h != null ? h.ShortName : "") + " " + b.HomeScore + (b.Overtime ? " (OT)" : ""),
                DateLabel = CollegeCalendar.DateLabel(year, b.Week)
            };
        }

        // ================================================================ standings
        public static List<CollegeConferenceView> Conferences(CareerSave s)
        {
            var l = new List<CollegeConferenceView>();
            if (s.College == null) return l;
            for (int i = 0; i < s.College.Conferences.Count; i++) l.Add(ConferenceStandings(s, s.College.Conferences[i].Id));
            bool indep = false;
            for (int i = 0; i < s.College.Schools.Count; i++) if (s.College.Schools[i].ConferenceId < 0) indep = true;
            if (indep) l.Add(ConferenceStandings(s, -1));
            return l;
        }

        public static CollegeConferenceView ConferenceStandings(CareerSave s, int conferenceId)
        {
            var v = new CollegeConferenceView { ConferenceId = conferenceId, Name = CollegeWorld.IndependentName, Abbr = "IND" };
            if (s.College == null) return v;
            Conference c = s.College.GetConference(conferenceId);
            if (c != null) { v.Name = c.Name; v.Abbr = c.Abbreviation; v.Logo = c.Logo; }
            Season cs = s.College.Season;
            var rows = cs.ConferenceStandings(conferenceId);
            rows.Sort((a, b) =>
            {
                int x = a.ConferenceRank.CompareTo(b.ConferenceRank);
                return x != 0 ? x : a.TeamId.CompareTo(b.TeamId);
            });
            for (int i = 0; i < rows.Count; i++)
            {
                StandingsRow r = rows[i];
                School sc = s.College.GetSchool(r.TeamId);
                v.Rows.Add(new CollegeStandingRow
                {
                    Rank = i + 1, TeamId = r.TeamId, PollRank = Rank(cs, r.TeamId), Name = sc != null ? sc.Name : r.Name, ShortName = sc != null ? sc.ShortName : r.Name, Abbr = sc != null ? sc.Abbreviation : "", Crest = sc != null ? sc.Logo : "",
                    ConfRecord = r.ConfW + "-" + r.ConfL + (r.ConfT > 0 ? "-" + r.ConfT : ""), Record = r.Record, PointsFor = r.PF, PointsAgainst = r.PA, Streak = r.Streak, ConferenceLeader = i == 0
                });
            }
            return v;
        }

        // ================================================================ leaders and the Golden Helm race
        public static List<CollegeLeaderRow> Leaders(CareerSave s, StatCategory category, int n = 25)
        {
            var l = new List<CollegeLeaderRow>();
            if (s.College == null) return l;
            var top = LeagueLeaders.Top(s.College.Season, category, n);
            for (int i = 0; i < top.Count; i++)
            {
                LeaderEntry e = top[i];
                School sc = s.College.GetSchool(e.TeamId);
                Player p = sc != null ? sc.Team.GetPlayer(e.PlayerId) : null;
                l.Add(new CollegeLeaderRow { Rank = e.Rank, PlayerId = e.PlayerId, Name = e.Name, SchoolId = e.TeamId, School = sc != null ? sc.ShortName : "", Pos = e.Pos, Value = e.Value, Games = e.Games, Class = p != null ? p.Class : ClassYear.Senior });
            }
            return l;
        }

        /// <summary>The latest Golden Helm Trophy standings (top 10).</summary>
        public static List<HelmEntry> HelmRace(CareerSave s)
        {
            var l = new List<HelmEntry>();
            if (s.College == null) return l;
            var race = s.College.Season.HelmRace;
            if (race.Count == 0) return l;
            l.AddRange(race[race.Count - 1].Candidates);
            return l;
        }

        /// <summary>The race at a given week (for the weekly movement chart); the week numbers of the snapshots are in <see cref="HelmWeeks"/>.</summary>
        public static List<HelmEntry> HelmRaceAt(CareerSave s, int snapshotIndex)
        {
            var l = new List<HelmEntry>();
            if (s.College == null) return l;
            var race = s.College.Season.HelmRace;
            if (snapshotIndex < 0 || snapshotIndex >= race.Count) return l;
            l.AddRange(race[snapshotIndex].Candidates);
            return l;
        }

        public static List<int> HelmWeeks(CareerSave s)
        {
            var l = new List<int>();
            if (s.College == null) return l;
            for (int i = 0; i < s.College.Season.HelmRace.Count; i++) l.Add(s.College.Season.HelmRace[i].Week);
            return l;
        }

        // ================================================================ playoff and bowls
        public static CollegeBracketView Bracket(CareerSave s)
        {
            var v = new CollegeBracketView();
            if (s.College == null) return v;
            Season cs = s.College.Season;
            v.PlayoffName = cs.PlayoffName; v.ChampionshipName = cs.ChampionshipName;
            v.Built = cs.Seeds.Count > 0;
            var eliminated = new HashSet<int>();
            for (int i = 0; i < cs.Games.Count; i++)
                if ((cs.Games[i].Kind == GameKind.PlayoffRound || cs.Games[i].Kind == GameKind.Final) && cs.Games[i].Played) eliminated.Add(cs.Games[i].LoserId);
            for (int i = 0; i < cs.Seeds.Count; i++)
            {
                PlayoffSeed ps = cs.Seeds[i];
                v.Seeds.Add(new BracketSeed
                {
                    Seed = ps.Seed, TeamId = ps.TeamId, Name = Short(s, ps.TeamId), ConferenceChampion = ps.ConferenceChampion, Eliminated = eliminated.Contains(ps.TeamId), Champion = cs.ChampionTeamId == ps.TeamId,
                    Record = cs.WinsOf(ps.TeamId) + "-" + cs.LossesOf(ps.TeamId)
                });
            }
            string[] names = { "", "First round", "Quarterfinals", "Semifinals", "Championship" };
            for (int r = 1; r <= 4; r++)
            {
                var br = new BracketRound { Round = r, Name = names[r] };
                for (int i = 0; i < cs.Games.Count; i++)
                {
                    ScheduledGame g = cs.Games[i];
                    if (g.Round == r && (g.Kind == GameKind.PlayoffRound || g.Kind == GameKind.Final)) br.Games.Add(GameRow(s, g));
                }
                if (br.Games.Count > 0) v.Rounds.Add(br);
            }
            for (int i = 0; i < cs.Games.Count; i++) if (cs.Games[i].Kind == GameKind.Bowl) v.Bowls.Add(GameRow(s, cs.Games[i]));
            if (cs.IsComplete) { v.ChampionId = cs.ChampionTeamId; v.Champion = Name(s, cs.ChampionTeamId); }
            return v;
        }

        /// <summary>The conference championship games of the season.</summary>
        public static List<CollegeGameRow> ConferenceChampionships(CareerSave s)
        {
            var l = new List<CollegeGameRow>();
            if (s.College == null) return l;
            Season cs = s.College.Season;
            for (int i = 0; i < cs.Games.Count; i++) if (cs.Games[i].Kind == GameKind.ConferenceChampionship) l.Add(GameRow(s, cs.Games[i]));
            return l;
        }

        // ================================================================ team pages
        public static CollegeTeamView Team(CareerSave s, int schoolId)
        {
            School sc = s.College != null ? s.College.GetSchool(schoolId) : null;
            if (sc == null) return null;
            CollegeNet.Ensure(s);
            Season cs = s.College.Season;
            var v = new CollegeTeamView
            {
                SchoolId = sc.Id, Name = sc.Name, ShortName = sc.ShortName, Mascot = sc.Mascot, Abbr = sc.Abbreviation, Crest = sc.Logo, Conference = s.College.ConferenceNameOf(sc), Stadium = sc.StadiumName, City = sc.City, State = sc.State,
                PrestigeLabel = sc.PrestigeLabel, Prestige = sc.Prestige, StadiumCapacity = sc.StadiumCapacity, PrimaryColor = sc.PrimaryColor, SecondaryColor = sc.SecondaryColor, Rank = Rank(cs, sc.Id),
                Wins = cs.WinsOf(sc.Id), Losses = cs.LossesOf(sc.Id), RosterCount = sc.Team.Roster.Count
            };
            Coach hc = sc.Team.HeadCoach, oc = sc.Team.OffensiveCoordinator, dc = sc.Team.DefensiveCoordinator;
            v.HeadCoach = hc != null ? hc.Name : ""; v.OffensiveCoordinator = oc != null ? oc.Name : ""; v.DefensiveCoordinator = dc != null ? dc.Name : "";
            var rows = cs.Standings();
            for (int i = 0; i < rows.Count; i++) if (rows[i].TeamId == sc.Id) { v.ConfWins = rows[i].ConfW; v.ConfLosses = rows[i].ConfL; }
            v.Schedule = Schedule(s, sc.Id);
            ScoutingReport rep = CollegeScouting.UserReport(s);
            PoolCache pool = s.Pro.DraftClass.Count == 0 ? CollegeScouting.Pool(s) : null;
            for (int i = 0; i < sc.Team.Roster.Count; i++)
            {
                Player p = sc.Team.Roster[i];
                SeasonStats st = p.FindStats(cs.Year);
                var row = new CollegeRosterRow
                {
                    PlayerId = p.Id, Name = p.Name, Pos = p.Position, Class = p.Class, Stars = p.Stars, Jersey = p.Bio.JerseyNumber, Height = p.Bio.HeightDisplay, WeightLb = p.Bio.WeightLb, Redshirting = p.IsRedshirting,
                    Eligible = !p.IsRedshirting && (p.Class == ClassYear.Junior || p.Class == ClassYear.Senior), SeasonLine = st != null ? SeasonLine(p.Position, st) : ""
                };
                PoolEntry pe;
                if (pool != null && pool.ById.TryGetValue(p.Id, out pe)) row.PublicRank = pe.Rank;
                ScoutedProspect known = CollegeScouting.KnownEntry(rep, p.Id);
                row.Tier = CollegeScouting.TierFor(known);
                if (known != null) row.OverallText = CollegeScoutViews.RatingText(known, row.Tier, known.Estimated.Overall(p.Position), 1f, p.Overall);
                v.Roster.Add(row);
            }
            v.Roster.Sort((a, b) =>
            {
                int c = Array.IndexOf(LeagueRules.RosterOrder, a.Pos).CompareTo(Array.IndexOf(LeagueRules.RosterOrder, b.Pos));
                if (c != 0) return c;
                c = b.Class.CompareTo(a.Class);
                return c != 0 ? c : a.PlayerId.CompareTo(b.PlayerId);
            });
            for (int i = 0; i < s.Net.History.Count; i++) if (s.Net.History[i].SchoolId == sc.Id) v.History.Add(s.Net.History[i]);
            v.History.Sort((a, b) => b.Year.CompareTo(a.Year));
            return v;
        }

        public static string SeasonLine(Position pos, StatLine st)
        {
            if (st.PassAtt >= 20) return st.PassCmp + "/" + st.PassAtt + ", " + st.PassYds + " yds, " + st.PassTd + " TD, " + st.PassInt + " INT" + (st.RushYds >= 100 ? "; " + st.RushYds + " rush yds" : "");
            if (st.RushAtt >= 15) return st.RushAtt + " car, " + st.RushYds + " yds, " + st.RushTd + " TD";
            if (st.Rec >= 5) return st.Rec + " rec, " + st.RecYds + " yds, " + st.RecTd + " TD";
            if (st.FgAtt > 0) return st.FgMade + "/" + st.FgAtt + " FG, " + st.XpMade + "/" + st.XpAtt + " XP";
            if (st.Punts > 0) return st.Punts + " punts, " + (st.PuntYds / st.Punts) + " avg";
            if (st.Tackles + st.Sacks > 0) return st.Tackles + " tkl, " + st.Sacks + " sacks, " + st.Interceptions + " INT";
            return st.Games > 0 ? st.Games + " games" : "";
        }

        // ================================================================ recruiting and history
        /// <summary>Recruiting class rankings: this year's freshmen by stars (the class that keeps every roster at 85).</summary>
        public static List<CollegeRecruitRow> Recruiting(CareerSave s, int n = 25)
        {
            var rows = new List<CollegeRecruitRow>();
            if (s.College == null) return rows;
            for (int i = 0; i < s.College.Schools.Count; i++)
            {
                School sc = s.College.Schools[i];
                var r = new CollegeRecruitRow { SchoolId = sc.Id, Name = sc.ShortName, Crest = sc.Logo };
                float sum = 0f;
                for (int k = 0; k < sc.Team.Roster.Count; k++)
                {
                    Player p = sc.Team.Roster[k];
                    if (p.Class != ClassYear.Freshman || p.RedshirtUsed) continue;
                    r.Commits++; sum += p.Stars;
                    if (p.Stars >= 5) r.FiveStars++; else if (p.Stars == 4) r.FourStars++;
                    r.Points += p.Stars * p.Stars * 1.5f;
                }
                r.AvgStars = r.Commits == 0 ? 0f : sum / r.Commits;
                rows.Add(r);
            }
            rows.Sort((a, b) => { int c = b.Points.CompareTo(a.Points); return c != 0 ? c : a.SchoolId.CompareTo(b.SchoolId); });
            if (rows.Count > n) rows.RemoveRange(n, rows.Count - n);
            for (int i = 0; i < rows.Count; i++) rows[i].Rank = i + 1;
            return rows;
        }

        /// <summary>National champions and Golden Helm winners of the past seasons, newest first.</summary>
        public static List<CollegeChampionRow> Champions(CareerSave s)
        {
            var l = new List<CollegeChampionRow>();
            if (s.College == null) return l;
            for (int i = 0; i < s.College.History.Count; i++)
            {
                SeasonSummary h = s.College.History[i];
                l.Add(new CollegeChampionRow { Year = h.Year, ChampionId = h.ChampionTeamId, Champion = h.ChampionName ?? "", RunnerUpId = h.RunnerUpTeamId, RunnerUp = h.RunnerUpTeamId >= 0 ? Name(s, h.RunnerUpTeamId) : "", HelmPlayerId = h.MvpPlayerId, HelmWinner = h.MvpName ?? "" });
            }
            l.Sort((a, b) => b.Year.CompareTo(a.Year));
            return l;
        }

        /// <summary>Every school's finish in a season (wins, losses, final rank, postseason), best first.</summary>
        public static List<CollegeSeasonRecord> SeasonResults(CareerSave s, int year)
        {
            var l = new List<CollegeSeasonRecord>();
            for (int i = 0; i < s.Net.History.Count; i++) if (s.Net.History[i].Year == year) l.Add(s.Net.History[i]);
            l.Sort((a, b) =>
            {
                int ra = a.FinalRank == 0 ? 99 : a.FinalRank, rb = b.FinalRank == 0 ? 99 : b.FinalRank;
                int c = ra.CompareTo(rb);
                return c != 0 ? c : (b.Wins - b.Losses).CompareTo(a.Wins - a.Losses);
            });
            return l;
        }

        /// <summary>Honors of the last seasons for one honor kind (e.g. the All-America first team of a year).</summary>
        public static List<CollegeHonor> Honors(CareerSave s, int year, CollegeHonorKind kind)
        {
            var l = new List<CollegeHonor>();
            for (int i = 0; i < s.Net.Honors.Count; i++) if (s.Net.Honors[i].Year == year && s.Net.Honors[i].Kind == kind) l.Add(s.Net.Honors[i]);
            return l;
        }
    }
}
