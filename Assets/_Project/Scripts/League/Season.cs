using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    public enum SeasonKind { Pro, College }
    public enum SeasonPhase { Regular, Postseason, Complete }
    public enum GameKind { Regular, ConferenceChampionship, PlayoffRound, Bowl, Final }

    [Serializable]
    public class SeasonTeam
    {
        public int TeamId;
        public int ConferenceId = -1;
        public int DivisionId = -1;
        public string Name;
        public string Abbr;
        public int Prestige;
        public int Region;
    }

    [Serializable]
    public class ScheduledGame
    {
        public int Id;
        public int Week;
        public int HomeId, AwayId;
        public bool Played;
        public int HomeScore, AwayScore;
        public bool Overtime;
        public GameKind Kind;
        public int Round;
        public bool Neutral;
        /// <summary>Playoff / bowl game title, e.g. "Wild Card", "PIX Bowl", "Sunrise Bowl".</summary>
        public string Label = "";

        public bool Tie { get { return Played && HomeScore == AwayScore; } }
        public int WinnerId { get { return !Played ? -1 : (HomeScore > AwayScore ? HomeId : (AwayScore > HomeScore ? AwayId : -1)); } }
        public int LoserId { get { return !Played ? -1 : (HomeScore > AwayScore ? AwayId : (AwayScore > HomeScore ? HomeId : -1)); } }
        public bool Involves(int teamId) { return HomeId == teamId || AwayId == teamId; }
        public int OpponentOf(int teamId) { return HomeId == teamId ? AwayId : HomeId; }
    }

    [Serializable]
    public class PlayoffSeed
    {
        public int ConferenceId;
        public int Seed;
        public int TeamId;
        public bool ConferenceChampion;
    }

    [Serializable]
    public class AwardWinner
    {
        public string Award;
        public int PlayerId;
        public string PlayerName;
        public int TeamId;
        public Position Pos;
        public float Score;
    }

    /// <summary>
    /// A full season for the pro league (17 games, bye weeks, 7-team conference playoffs, the PIX Bowl) or the college world
    /// (12 games, weekly polls, conference championships, 12-team playoff and bowls).
    /// </summary>
    [Serializable]
    public partial class Season
    {
        public SeasonKind Kind;
        public int Year;
        /// <summary>The next week to simulate (1-based).</summary>
        public int Week = 1;
        public int RegularWeeks;
        public SeasonPhase Phase = SeasonPhase.Regular;
        public string ChampionshipName = "";
        public string PlayoffName = "";
        public List<SeasonTeam> Teams = new List<SeasonTeam>();
        public List<ScheduledGame> Games = new List<ScheduledGame>();
        public List<PlayoffSeed> Seeds = new List<PlayoffSeed>();
        public int ChampionTeamId = -1;
        public int RunnerUpTeamId = -1;
        public int PostseasonStage;
        public List<PollSnapshot> Polls = new List<PollSnapshot>();
        public List<HelmSnapshot> HelmRace = new List<HelmSnapshot>();
        public List<AwardWinner> Awards = new List<AwardWinner>();
        public int MvpId, OpoyId, DpoyId, RoyId, GoldenHelmId;
        public bool Finalized;
        public long RngS0, RngS1;
        public bool InjuriesEnabled = true;

        [NonSerialized] private Dictionary<int, Team> _teams;
        [NonSerialized] public List<GameResult> LastWeekResults = new List<GameResult>();
        [NonSerialized] public RecordBook Records;
        [NonSerialized] private ProLeague _pro;
        [NonSerialized] private CollegeWorld _college;
        [NonSerialized] private Dictionary<int, SeasonTeam> _info;

        public bool IsCollege { get { return Kind == SeasonKind.College; } }
        public bool IsComplete { get { return Phase == SeasonPhase.Complete; } }
        public RuleSet Rules { get { return IsCollege ? RuleSet.College() : RuleSet.Pro(); } }
        public bool IsEmpty { get { return Teams.Count == 0; } }

        // ------------------------------------------------------------------ wiring
        public void Bind(List<Team> teams)
        {
            _teams = new Dictionary<int, Team>(teams.Count);
            for (int i = 0; i < teams.Count; i++) _teams[teams[i].Id] = teams[i];
            _info = new Dictionary<int, SeasonTeam>(Teams.Count);
            for (int i = 0; i < Teams.Count; i++) _info[Teams[i].TeamId] = Teams[i];
        }

        internal void Attach(ProLeague league)
        {
            _pro = league; Records = league.Records; Bind(league.Teams);
        }

        internal void Attach(CollegeWorld world)
        {
            _college = world; Records = world.Records; Bind(world.TeamList());
        }

        public Team GetTeam(int id)
        {
            Team t;
            return _teams != null && _teams.TryGetValue(id, out t) ? t : null;
        }

        public SeasonTeam Info(int id)
        {
            SeasonTeam t;
            return _info != null && _info.TryGetValue(id, out t) ? t : null;
        }

        public string NameOf(int teamId)
        {
            SeasonTeam t = Info(teamId);
            return t != null ? t.Name : ("Team " + teamId);
        }

        public Rng GetRng()
        {
            var r = new Rng(0UL);
            r.SetState(unchecked((ulong)RngS0), unchecked((ulong)RngS1));
            return r;
        }

        public void SaveRng(Rng r)
        {
            ulong a, b;
            r.GetState(out a, out b);
            RngS0 = unchecked((long)a); RngS1 = unchecked((long)b);
        }

        // ------------------------------------------------------------------ queries
        public List<ScheduledGame> GamesOfWeek(int week)
        {
            var l = new List<ScheduledGame>();
            for (int i = 0; i < Games.Count; i++) if (Games[i].Week == week) l.Add(Games[i]);
            return l;
        }

        public List<ScheduledGame> GamesOfTeam(int teamId)
        {
            var l = new List<ScheduledGame>();
            for (int i = 0; i < Games.Count; i++) if (Games[i].Involves(teamId)) l.Add(Games[i]);
            l.Sort((a, b) => a.Week.CompareTo(b.Week));
            return l;
        }

        public int RegularGameCount(int teamId)
        {
            int n = 0;
            for (int i = 0; i < Games.Count; i++) if (Games[i].Kind == GameKind.Regular && Games[i].Involves(teamId)) n++;
            return n;
        }

        public int WinsOf(int teamId) { int n = 0; for (int i = 0; i < Games.Count; i++) if (Games[i].Played && Games[i].WinnerId == teamId) n++; return n; }
        public int LossesOf(int teamId) { int n = 0; for (int i = 0; i < Games.Count; i++) if (Games[i].Played && Games[i].LoserId == teamId) n++; return n; }

        public int LastScheduledWeek
        {
            get { int w = 0; for (int i = 0; i < Games.Count; i++) if (Games[i].Week > w) w = Games[i].Week; return w; }
        }

        // ------------------------------------------------------------------ simulation
        /// <summary>
        /// Plays every game of the current week and moves the calendar forward (building playoff rounds as needed).
        /// Returns false when the season was already complete.
        /// </summary>
        public bool SimWeek()
        {
            if (Phase == SeasonPhase.Complete) return false;
            if (_teams == null) throw new InvalidOperationException("Season is not bound to a league; call Bind().");
            LastWeekResults = new List<GameResult>();
            var games = GamesOfWeek(Week);
            Rng rng = GetRng();
            var hurt = InjuredNow();   // QA-563: only players already hurt at kickoff heal after this week's games, so a fresh injury always costs at least one game
            for (int i = 0; i < games.Count; i++)
            {
                ScheduledGame g = games[i];
                if (g.Played) continue;
                SimGameInternal(g, rng.Fork());
            }
            HealPlayers(hurt);
            SaveRng(rng);
            AfterWeek();
            return true;
        }

        public void SimToEnd()
        {
            int guard = 0;
            while (Phase != SeasonPhase.Complete && guard++ < 80) SimWeek();
        }

        /// <summary>Plays the regular season only (stops at the first postseason week).</summary>
        public void SimRegularSeason()
        {
            int guard = 0;
            while (Phase == SeasonPhase.Regular && guard++ < 40) SimWeek();
        }

        private List<Player> InjuredNow()
        {
            var hurt = new List<Player>();
            foreach (var kv in _teams)
            {
                var roster = kv.Value.Roster;
                for (int i = 0; i < roster.Count; i++) if (roster[i].Injury.WeeksOut > 0) hurt.Add(roster[i]);
            }
            return hurt;
        }

        private static void HealPlayers(List<Player> hurt)
        {
            for (int i = 0; i < hurt.Count; i++) hurt[i].Injury.Heal(1);
        }

        private void SimGameInternal(ScheduledGame g, Rng rng)
        {
            Team home = _teams[g.HomeId], away = _teams[g.AwayId];
            var opt = new SimOptions { Neutral = g.Neutral, ForceWinner = g.Kind != GameKind.Regular };
            RuleSet rules = Rules;
            GameResult r = QuickSim.SimGame(home, away, rules, rng, opt);
            g.Played = true; g.HomeScore = r.HomeScore; g.AwayScore = r.AwayScore; g.Overtime = r.Overtime;
            LastWeekResults.Add(r);
            bool post = g.Kind != GameKind.Regular;
            PostStats(r, home, away, post);
            if (Records != null && !post) Records.UpdateGame(r, Year, home, away);
            if (InjuriesEnabled) RollInjuries(r, home, away, rng);
        }

        private void PostStats(GameResult r, Team home, Team away, bool post)
        {
            for (int i = 0; i < r.PlayerLines.Count; i++)
            {
                PlayerGameLine l = r.PlayerLines[i];
                Team t = l.TeamId == home.Id ? home : away;
                Player p = t.GetPlayer(l.PlayerId);
                if (p == null) continue;
                p.StatsFor(Year, t.Id, post).Add(l.Stats);
            }
        }

        private static readonly string[] InjuryTypes =
            { "Sprained ankle", "Hamstring strain", "Concussion", "Knee sprain", "Shoulder", "Bruised ribs", "Groin", "Calf strain", "Torn ACL", "Broken hand" };

        // weeks out per injury type: {min, max} (QA-564). The type is rolled first and decides the duration, so a sprained ankle is never a ten-week injury and a torn ACL never a one-week one.
        private static readonly int[][] InjuryWeekRanges =
        {
            new[] { 1, 3 },   // Sprained ankle
            new[] { 2, 5 },   // Hamstring strain
            new[] { 1, 3 },   // Concussion
            new[] { 3, 6 },   // Knee sprain
            new[] { 2, 6 },   // Shoulder
            new[] { 1, 3 },   // Bruised ribs
            new[] { 2, 4 },   // Groin
            new[] { 1, 4 },   // Calf strain
            new[] { 28, 40 }, // Torn ACL (rest of the season and the offseason)
            new[] { 4, 8 }    // Broken hand
        };

        /// <summary>All injury types the season can roll.</summary>
        public static IReadOnlyList<string> InjuryTypeNames { get { return InjuryTypes; } }

        /// <summary>The {min, max} weeks out for an injury type (a copy), or null for an unknown type.</summary>
        public static int[] InjuryWeekRange(string type)
        {
            int i = Array.IndexOf(InjuryTypes, type);
            return i < 0 ? null : new[] { InjuryWeekRanges[i][0], InjuryWeekRanges[i][1] };
        }

        /// <summary>Rolls how many weeks the given injury keeps a player out: inside the type's range, with a 5 percent setback of up to half the range extra (never past 40 weeks).</summary>
        public static int RollInjuryWeeks(string type, Rng rng)
        {
            int[] range = InjuryWeekRange(type) ?? new[] { 1, 4 };
            int weeks = range[0] + (int)Math.Round(rng.NextFloat() * (range[1] - range[0]));
            if (rng.Chance(0.05f) && range[1] < 20) weeks += 1 + (range[1] - range[0]) / 2;
            return Math.Max(1, Math.Min(weeks, 40));
        }

        private void RollInjuries(GameResult r, Team home, Team away, Rng rng)
        {
            for (int i = 0; i < r.PlayerLines.Count; i++)
            {
                PlayerGameLine l = r.PlayerLines[i];
                if (!l.Started && !l.Stats.HasActivity) continue;
                Team t = l.TeamId == home.Id ? home : away;
                Player p = t.GetPlayer(l.PlayerId);
                if (p == null || p.Injury.IsInjured) continue;
                float prob = 0.0085f * (1f + (75f - p.Ratings.Injury) / 70f);
                if (rng.NextFloat() >= prob) continue;
                string type = InjuryTypes[rng.Range(0, InjuryTypes.Length)];
                int weeks = RollInjuryWeeks(type, rng);
                p.Injury.WeeksOut = weeks;
                p.Injury.Type = type;
                if (weeks >= 6) p.Injury.Wear++;
            }
        }

        // ------------------------------------------------------------------ standings
        public List<StandingsRow> Standings() { return StandingsCalculator.Compute(this); }

        public List<StandingsRow> DivisionStandings(int divisionId)
        {
            var rows = StandingsCalculator.Compute(this);
            var l = new List<StandingsRow>();
            for (int i = 0; i < rows.Count; i++) if (rows[i].DivisionId == divisionId) l.Add(rows[i]);
            return l;
        }

        public List<StandingsRow> ConferenceStandings(int conferenceId)
        {
            var rows = StandingsCalculator.Compute(this);
            var l = new List<StandingsRow>();
            for (int i = 0; i < rows.Count; i++) if (rows[i].ConferenceId == conferenceId) l.Add(rows[i]);
            return l;
        }

        /// <summary>Team ids for next year's draft: worst record first, playoff teams by how far they went, champion last.</summary>
        public List<int> DraftOrder()
        {
            var rows = StandingsCalculator.Compute(this);
            // QA-573: rank playoff teams by the last round they reached (the week of their final playoff game), so a bye team that lost its first game
            // is not level with a wild-card loser; teams that never reached the playoffs stay below all of them
            var made = new Dictionary<int, int>();
            for (int i = 0; i < Games.Count; i++)
            {
                ScheduledGame g = Games[i];
                if (!g.Played || g.Kind == GameKind.Regular) continue;
                if (!made.ContainsKey(g.HomeId) || made[g.HomeId] < g.Week) made[g.HomeId] = g.Week;
                if (!made.ContainsKey(g.AwayId) || made[g.AwayId] < g.Week) made[g.AwayId] = g.Week;
            }
            for (int i = 0; i < Seeds.Count; i++) if (!made.ContainsKey(Seeds[i].TeamId)) made[Seeds[i].TeamId] = 1;
            var list = new List<StandingsRow>(rows);
            list.Sort((a, b) =>
            {
                int ma = made.ContainsKey(a.TeamId) ? made[a.TeamId] : 0;
                int mb = made.ContainsKey(b.TeamId) ? made[b.TeamId] : 0;
                if (ChampionTeamId == a.TeamId) ma = 99;
                if (ChampionTeamId == b.TeamId) mb = 99;
                if (ma != mb) return ma.CompareTo(mb);
                int c = a.Pct.CompareTo(b.Pct);
                if (c != 0) return c;
                c = (a.PF - a.PA).CompareTo(b.PF - b.PA);
                if (c != 0) return c;
                return a.TeamId.CompareTo(b.TeamId);
            });
            var ids = new List<int>(list.Count);
            for (int i = 0; i < list.Count; i++) ids.Add(list[i].TeamId);
            return ids;
        }

        // ------------------------------------------------------------------ helpers shared by postseason builders
        internal int NextGameId() { return Games.Count; }

        internal ScheduledGame AddGame(int week, int home, int away, GameKind kind, string label, int round, bool neutral)
        {
            var g = new ScheduledGame { Id = Games.Count, Week = week, HomeId = home, AwayId = away, Kind = kind, Label = label, Round = round, Neutral = neutral };
            Games.Add(g);
            return g;
        }

        internal bool AllPlayed(int week)
        {
            bool any = false;
            for (int i = 0; i < Games.Count; i++)
            {
                if (Games[i].Week != week) continue;
                any = true;
                if (!Games[i].Played) return false;
            }
            return any;
        }

        private void AfterWeek()
        {
            Week++;
            if (IsCollege) CollegeAfterWeek(Week - 1); else ProAfterWeek(Week - 1);
        }

        internal List<Team> AllTeams()
        {
            var l = new List<Team>(_teams.Count);
            for (int i = 0; i < Teams.Count; i++) l.Add(_teams[Teams[i].TeamId]);
            return l;
        }
    }
}
