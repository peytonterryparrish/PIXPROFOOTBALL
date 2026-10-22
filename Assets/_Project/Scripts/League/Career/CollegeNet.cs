using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    /// <summary>Everything the Unity layer needs to put a college game on the field (live spectator match or the highlight generator): two schools, their rosters and depth charts, crest and colour keys, stadium and weather inputs.</summary>
    public sealed class CollegeGameSetup
    {
        public ScheduledGame Game;
        public int Year, Week;
        public School HomeSchool, AwaySchool;
        /// <summary>Rosters with depth charts (the school's <see cref="School.Team"/>); their <c>Key</c> is the school data id and <c>Logo</c> the crest asset key.</summary>
        public Team Home, Away;
        public RuleSet Rules;
        public SimOptions Options;
        public string Title = "";
        public string Label = "";
        public string DateLabel = "";
        public string HomeKey = "", AwayKey = "", HomeCrest = "", AwayCrest = "";
        public string HomeName = "", AwayName = "", HomeAbbr = "", AwayAbbr = "";
        public string HomeColor = "#000000", HomeColor2 = "#FFFFFF", AwayColor = "#000000", AwayColor2 = "#FFFFFF";
        public int HomeRank, AwayRank;
        public string HomeRecord = "", AwayRecord = "";
        public string StadiumName = "";
        public int StadiumCapacity;
        public string City = "", State = "";
        public bool Dome, Neutral;
        /// <summary>Feed this to the weather picker of the match (same convention as the pro <c>GameSetup.WeatherSeed</c>).</summary>
        public int WeatherSeed;
        /// <summary>Seed for a headless <c>PhysicalGameSim</c> of this game (condensed highlights).</summary>
        public int SimSeed;
        public bool AlreadyPlayed;
    }

    /// <summary>The college calendar: college week 1 is the last Saturday of August and each week is seven days, so the 14 regular weeks run to the end of November, the conference title games are the first Saturday of December and the playoff and bowls end in the second week of January.</summary>
    public static class CollegeCalendar
    {
        private static readonly string[] Months = { "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

        public static DateTime DateOf(int year, int week)
        {
            return new DateTime(year, 8, 30).AddDays(7 * (Math.Max(1, week) - 1));
        }

        public static string DateLabel(int year, int week)
        {
            DateTime d = DateOf(year, week);
            return Months[d.Month - 1] + " " + d.Day;
        }

        public static string MonthLabel(int year, int week) { return Months[DateOf(year, week).Month - 1]; }

        /// <summary>Header text for a college week: "Week 7", "Conference championships", the playoff round, "Bowl season".</summary>
        public static string WeekTitle(Season cs, int week)
        {
            if (cs == null || cs.IsEmpty) return "Week " + week;
            if (week <= cs.RegularWeeks) return "Week " + week;
            GameKind kind = GameKind.Bowl; int round = 0; bool any = false;
            for (int i = 0; i < cs.Games.Count; i++)
            {
                ScheduledGame g = cs.Games[i];
                if (g.Week != week) continue;
                any = true;
                if (g.Kind == GameKind.Final) { kind = GameKind.Final; break; }
                if (g.Kind == GameKind.PlayoffRound) { kind = GameKind.PlayoffRound; round = g.Round; }
                else if (g.Kind == GameKind.ConferenceChampionship && kind != GameKind.PlayoffRound) kind = GameKind.ConferenceChampionship;
            }
            if (!any) return week <= cs.RegularWeeks + 1 ? "Conference championships" : "Postseason";
            switch (kind)
            {
                case GameKind.ConferenceChampionship: return "Conference championships";
                case GameKind.Final: return cs.ChampionshipName;
                case GameKind.PlayoffRound:
                    if (round == 1) return cs.PlayoffName + " first round";
                    if (round == 2) return cs.PlayoffName + " quarterfinals and bowl season";
                    if (round == 3) return cs.PlayoffName + " semifinals";
                    return cs.PlayoffName;
            }
            return "Bowl season";
        }
    }

    /// <summary>
    /// The College Network (v0.9.0): the 138-school college world inside every career. It simulates week by week next to the pro calendar
    /// (<see cref="AdvanceWeek"/> is called by <see cref="CareerFlow"/> once per week), stores box scores, honors and school history,
    /// feeds the scouting system (<see cref="CollegeScouting"/>) and the draft class (<see cref="CollegeDraftClass"/>), and sets up college games for the live spectator match.
    /// It never draws from the career random stream, so the pro league plays out identically with or without a college world.
    /// </summary>
    public static class CollegeNet
    {
        // ================================================================ determinism helper
        public static ulong Mix(ulong h, ulong v)
        {
            unchecked
            {
                h ^= v + 0x9E3779B97F4A7C15UL + (h << 6) + (h >> 2);
                h *= 0xBF58476D1CE4E5B9UL;
                h ^= h >> 31;
                return h;
            }
        }

        /// <summary>A random stream from the career seed and up to three salts (year, week, id ...): same save, same salts, same numbers.</summary>
        public static Rng NetRng(CareerSave s, int a, int b = 0, int c = 0, int d = 0)
        {
            ulong h = Mix(0x1234567UL, (ulong)(uint)s.Seed);
            h = Mix(h, (ulong)(uint)a); h = Mix(h, (ulong)(uint)b); h = Mix(h, (ulong)(uint)c); h = Mix(h, (ulong)(uint)d);
            return new Rng(h);
        }

        public static bool HasCollege(CareerSave s) { return s != null && s.College != null; }

        // ================================================================ setup
        /// <summary>Sets the network up for a career that has a college world (called by <see cref="CareerFlow.NewCareer"/> and lazily by every API call). Safe to call again.</summary>
        public static void Ensure(CareerSave s)
        {
            if (s == null || s.College == null) return;
            CollegeNetState n = s.Net;
            if (n == null) { s.Net = n = new CollegeNetState(); }
            if (n.Ready) return;
            n.Ready = true;
            n.Year = s.College.Year;
            CollegeScouting.GenerateScouts(s);
            CollegeScouting.NewReport(s);
        }

        /// <summary>
        /// Gives a career without a college world one (an older save, or a runtime that had no school files): builds the 138 schools from the CSV texts
        /// (or the generated fallback list when the texts are empty and <paramref name="allowFallback"/> is set) and brings the college season to the same
        /// week as the pro calendar. Returns Ok with a message when the world exists afterwards.
        /// </summary>
        public static OpResult EnsureWorld(CareerSave s, string schoolsCsv, string bowlsCsv, bool allowFallback = true)
        {
            if (s.College != null) { Ensure(s); return OpResult.Success("The college world already exists."); }
            IReadOnlyList<SchoolDef> schools = null; IReadOnlyList<ConfBowlDef> bowls = null;
            if (!string.IsNullOrEmpty(schoolsCsv))
            {
                schools = CollegeData.ParseSchools(schoolsCsv);
                bowls = CollegeData.ParseConfBowls(bowlsCsv);
            }
            if ((schools == null || schools.Count < 2) && allowFallback)
            {
                schools = CollegeFallback.Schools(s.Seed);
                bowls = CollegeFallback.Bowls();
            }
            if (schools == null || schools.Count < 2) return OpResult.Fail("No school data.");
            CollegeWorld world = CollegeFactory.CreateWorld(s.Seed ^ 0x5A5A5A, schools, bowls, s.Year);
            s.League.College = world;
            s.Net = new CollegeNetState();
            bool inSeason = s.Phase == CareerPhase.RegularSeason || s.Phase == CareerPhase.Playoffs || s.Phase == CareerPhase.PixBowl;
            if (inSeason && s.Season != null)
            {
                int target = Math.Max(0, s.Season.Week - 1);
                int guard = 0;
                while (world.Season.Week <= target && !world.Season.IsComplete && guard++ < 40) StepWeek(s);
            }
            else if (s.Phase != CareerPhase.Preseason)
            {
                int guard = 0;
                while (!world.Season.IsComplete && guard++ < 40) StepWeek(s);
            }
            Ensure(s);
            return OpResult.Success("College world created: " + world.Schools.Count + " schools.");
        }

        // ================================================================ the week
        /// <summary>One pro week has passed: the scouts do their work and the college week is played (the games the match already posted count).</summary>
        public static void AdvanceWeek(CareerSave s)
        {
            if (s.College == null || !s.Settings.SimCollege) return;
            Ensure(s);
            CollegeScouting.WeeklyTick(s);
            Season cs = s.College.Season;
            if (cs != null && !cs.IsComplete) StepWeek(s);
        }

        /// <summary>Plays out whatever is left of the college season (the pro season is over).</summary>
        public static void FinishSeason(CareerSave s)
        {
            if (s.College == null || !s.Settings.SimCollege) return;
            Ensure(s);
            Season cs = s.College.Season;
            int guard = 0;
            while (cs != null && !cs.IsComplete && guard++ < 40) StepWeek(s);
        }

        /// <summary>Plays the current college week and stores its box scores; when the season ends it also settles honors and history.</summary>
        internal static void StepWeek(CareerSave s)
        {
            CollegeWorld w = s.College;
            Season cs = w.Season;
            if (cs == null || cs.IsComplete) return;
            CollegeNetState n = s.Net;
            int week = cs.Week;
            PollSnapshot pre = cs.LatestPoll;
            var pending = new List<GameResult>(n.Pending);
            n.Pending.Clear();
            cs.SimWeek();
            var all = new List<GameResult>(pending);
            all.AddRange(cs.LastWeekResults);
            for (int i = 0; i < all.Count; i++) CollegeBoxes.Store(s, cs, week, all[i], pre);
            CollegeBoxes.Prune(s);
            if (cs.IsComplete) OnSeasonComplete(s);
        }

        private static void OnSeasonComplete(CareerSave s)
        {
            CollegeNetState n = s.Net;
            Season cs = s.College.Season;
            if (n.HonorsYear == cs.Year) return;
            n.HonorsYear = cs.Year;
            CollegeHonors.Compute(s);
            CollegeHonors.RecordHistory(s);
            School champ = s.College.GetSchool(cs.ChampionTeamId);
            if (champ != null)
            {
                string helm = "";
                for (int i = 0; i < cs.Awards.Count; i++) if (cs.Awards[i].Award == AwardsCalculator.GoldenHelmName) helm = " " + cs.Awards[i].PlayerName + " (" + cs.NameOf(cs.Awards[i].TeamId) + ") wins the " + AwardsCalculator.GoldenHelmName + ".";
                NewsWriter.Post(s, NewsKind.College, champ.Name + " win the " + cs.ChampionshipName, cs.Year + " college season: " + champ.Name + " finish " + cs.WinsOf(champ.Id) + "-" + cs.LossesOf(champ.Id) + " and take the title." + helm, 3);
            }
        }

        /// <summary>Called when the draft is over and the college world rolled into its next year.</summary>
        internal static void OnNewCollegeYear(CareerSave s)
        {
            if (s.College == null) return;
            CollegeNetState n = s.Net;
            Ensure(s);
            n.Year = s.College.Year;
            n.CombineAttended = false;
            n.ProDaySchools.Clear();
            n.PhysicalsKnown.Clear();
            n.AttendYear = 0; n.AttendWeek = 0;
            n.PointsEarned = 0;
            CollegeScouting.NewYear(s);
            CollegeScouting.NewReport(s);
            CollegeScouting.PruneBoard(s);
            CollegeBoxes.Prune(s);
        }

        // ================================================================ games of the current week
        /// <summary>All college games of the current college week (the user can watch, attend, or let them sim).</summary>
        public static List<ScheduledGame> GamesThisWeek(CareerSave s)
        {
            if (s.College == null || s.College.Season == null || s.College.Season.IsComplete) return new List<ScheduledGame>();
            return s.College.Season.GamesOfWeek(s.College.Season.Week);
        }

        public static ScheduledGame FindGame(CareerSave s, int gameId)
        {
            if (s.College == null || s.College.Season == null) return null;
            var games = s.College.Season.Games;
            return gameId >= 0 && gameId < games.Count && games[gameId].Id == gameId ? games[gameId] : null;
        }

        /// <summary>Sets up a college game for the live spectator match or the highlight generator. Works for any game of the season (played games come back with <see cref="CollegeGameSetup.AlreadyPlayed"/> set).</summary>
        public static CollegeGameSetup SetupGame(CareerSave s, int gameId)
        {
            Ensure(s);
            ScheduledGame g = FindGame(s, gameId);
            if (g == null) return null;
            CollegeWorld w = s.College;
            School hs = w.GetSchool(g.HomeId), a = w.GetSchool(g.AwayId);
            if (hs == null || a == null) return null;
            Season cs = w.Season;
            var gs = new CollegeGameSetup
            {
                Game = g, Year = cs.Year, Week = g.Week, HomeSchool = hs, AwaySchool = a, Home = hs.Team, Away = a.Team, Rules = RuleSet.College(),
                Options = new SimOptions { Neutral = g.Neutral, ForceWinner = g.Kind != GameKind.Regular, HomeAdvantage = true },
                Label = string.IsNullOrEmpty(g.Label) ? CollegeCalendar.WeekTitle(cs, g.Week) : g.Label, DateLabel = CollegeCalendar.DateLabel(cs.Year, g.Week),
                HomeKey = hs.Key, AwayKey = a.Key, HomeCrest = hs.Logo, AwayCrest = a.Logo, HomeName = hs.Name, AwayName = a.Name, HomeAbbr = hs.Abbreviation, AwayAbbr = a.Abbreviation,
                HomeColor = hs.PrimaryColor, HomeColor2 = hs.SecondaryColor, AwayColor = a.PrimaryColor, AwayColor2 = a.SecondaryColor,
                HomeRank = RankOf(cs, g.HomeId, g.Week), AwayRank = RankOf(cs, g.AwayId, g.Week),
                HomeRecord = RecordBefore(cs, g.HomeId, g.Week), AwayRecord = RecordBefore(cs, g.AwayId, g.Week),
                StadiumName = hs.StadiumName, StadiumCapacity = hs.StadiumCapacity, City = hs.City, State = hs.State, Dome = hs.Team.IsDome, Neutral = g.Neutral, AlreadyPlayed = g.Played
            };
            if (g.Neutral) { gs.StadiumName = g.Label + " Stadium"; gs.StadiumCapacity = 70000; gs.City = ""; gs.State = ""; gs.Dome = false; }
            gs.Title = (gs.AwayRank > 0 ? "#" + gs.AwayRank + " " : "") + a.ShortName + (g.Neutral ? " vs " : " at ") + (gs.HomeRank > 0 ? "#" + gs.HomeRank + " " : "") + hs.ShortName + " - " + gs.Label;
            ulong h = Mix(Mix((ulong)(uint)s.Seed, (ulong)(uint)cs.Year), (ulong)(uint)(g.Id * 31 + g.Week));
            gs.WeatherSeed = (int)(h & 0x7FFFFFFF) | 1;
            gs.SimSeed = (int)((h >> 33) & 0x7FFFFFFF) | 1;
            return gs;
        }

        private static int RankOf(Season cs, int teamId, int week)
        {
            PollSnapshot best = null;
            for (int i = 0; i < cs.Polls.Count; i++) if (cs.Polls[i].Week <= week - 1 && (best == null || cs.Polls[i].Week >= best.Week)) best = cs.Polls[i];
            PollEntry e = best != null ? best.Find(teamId) : null;
            return e != null ? e.Rank : 0;
        }

        private static string RecordBefore(Season cs, int teamId, int week)
        {
            int w = 0, l = 0;
            for (int i = 0; i < cs.Games.Count; i++)
            {
                ScheduledGame g = cs.Games[i];
                if (!g.Played || g.Week >= week || !g.Involves(teamId)) continue;
                if (g.WinnerId == teamId) w++; else if (g.LoserId == teamId) l++;
            }
            return w + "-" + l;
        }

        /// <summary>Records the result of a college game played in the live spectator match. The rest of the week is simulated when the pro week advances.</summary>
        public static OpResult SubmitGame(CareerSave s, int gameId, GameResult r)
        {
            Ensure(s);
            ScheduledGame g = FindGame(s, gameId);
            if (g == null || s.College.Season.Week != g.Week) return OpResult.Fail("That college game is not scheduled this week.");
            if (g.Played) return OpResult.Fail("The game was already played.");
            if (r == null) return OpResult.Fail("No result.");
            if (g.Kind != GameKind.Regular && r.HomeScore == r.AwayScore) { r.HomeScore += 1; r.Home.Score = r.HomeScore; r.Overtime = true; }
            if (!s.College.Season.ApplyExternalResult(g.Id, r, NetRng(s, s.College.Year, g.Id, 77))) return OpResult.Fail("The result does not match the scheduled game.");
            s.Net.Pending.Add(r);
            return OpResult.Success();
        }

        /// <summary>Quick-simulates one game of the current college week right now (to read the box score before the week advances).</summary>
        public static GameResult SimGame(CareerSave s, int gameId)
        {
            Ensure(s);
            ScheduledGame g = FindGame(s, gameId);
            if (g == null || g.Played || s.College.Season.Week != g.Week) return null;
            GameResult r = s.College.Season.SimOneGame(g.Id, NetRng(s, s.College.Year, g.Id, 78));
            if (r != null) s.Net.Pending.Add(r);
            return r;
        }
    }

    /// <summary>Box score storage for the college games.</summary>
    public static class CollegeBoxes
    {
        public static void Store(CareerSave s, Season cs, int week, GameResult r, PollSnapshot pre)
        {
            ScheduledGame g = null;
            for (int i = 0; i < cs.Games.Count; i++)
                if (cs.Games[i].Week == week && cs.Games[i].HomeId == r.HomeTeamId && cs.Games[i].AwayId == r.AwayTeamId) { g = cs.Games[i]; break; }
            if (g == null) return;
            var list = s.Net.Boxes;
            for (int i = 0; i < list.Count; i++) if (list[i].Year == cs.Year && list[i].GameId == g.Id) return;
            var b = new CollegeBox
            {
                Year = cs.Year, Week = week, GameId = g.Id, HomeId = g.HomeId, AwayId = g.AwayId, HomeScore = r.HomeScore, AwayScore = r.AwayScore, Overtime = r.Overtime,
                Kind = g.Kind, Label = g.Label ?? ""
            };
            if (pre != null)
            {
                PollEntry he = pre.Find(g.HomeId), ae = pre.Find(g.AwayId);
                b.HomeRank = he != null ? he.Rank : 0; b.AwayRank = ae != null ? ae.Rank : 0;
            }
            b.HomeByQuarter.AddRange(r.Home.ByQuarter); b.AwayByQuarter.AddRange(r.Away.ByQuarter);
            if (r.Home.OvertimeScore > 0 || r.Away.OvertimeScore > 0) { b.HomeByQuarter.Add(r.Home.OvertimeScore); b.AwayByQuarter.Add(r.Away.OvertimeScore); }
            Fill(b.Home, r.Home); Fill(b.Away, r.Away);
            Leaders(b, r, cs);
            for (int i = 0; i < r.Scoring.Count && b.Scoring.Count < 10; i++)
            {
                ScoringEvent e = r.Scoring[i];
                int min = e.ClockSeconds / 60, sec = e.ClockSeconds % 60;
                School sch = s.College.GetSchool(e.TeamId);
                b.Scoring.Add("Q" + e.Quarter + " " + min + ":" + (sec < 10 ? "0" : "") + sec + " " + (sch != null ? sch.Abbreviation : "") + " " + e.Description + " (" + e.HomeScore + "-" + e.AwayScore + ")");
            }
            list.Add(b);
        }

        private static void Fill(CollegeBoxTeam t, TeamBoxScore b)
        {
            t.Plays = b.Plays; t.FirstDowns = b.FirstDowns; t.TotalYards = b.TotalYards; t.PassYds = b.PassYds; t.RushYds = b.RushYds; t.PassAtt = b.PassAtt; t.PassCmp = b.PassCmp;
            t.RushAtt = b.RushAtt; t.Turnovers = b.Giveaways; t.Penalties = b.Penalties; t.PenaltyYds = b.PenaltyYds; t.Sacks = b.SacksMade; t.ThirdDownAtt = b.ThirdDownAtt;
            t.ThirdDownConv = b.ThirdDownConv; t.TimeOfPossessionSeconds = b.TimeOfPossessionSeconds;
        }

        private static void Leaders(CollegeBox box, GameResult r, Season cs)
        {
            AddLeader(box, r, cs, "Passing", l => l.Stats.PassYds, l => l.Stats.PassAtt >= 5, l => l.Stats.PassCmp + "/" + l.Stats.PassAtt + ", " + l.Stats.PassYds + " yds, " + l.Stats.PassTd + " TD, " + l.Stats.PassInt + " INT");
            AddLeader(box, r, cs, "Rushing", l => l.Stats.RushYds, l => l.Stats.RushAtt >= 3, l => l.Stats.RushAtt + " car, " + l.Stats.RushYds + " yds, " + l.Stats.RushTd + " TD");
            AddLeader(box, r, cs, "Receiving", l => l.Stats.RecYds, l => l.Stats.Rec >= 2, l => l.Stats.Rec + " rec, " + l.Stats.RecYds + " yds, " + l.Stats.RecTd + " TD");
            AddLeader(box, r, cs, "Tackles", l => l.Stats.Tackles, l => l.Stats.Tackles >= 3, l => l.Stats.Tackles + " tackles, " + l.Stats.Sacks + " sacks");
        }

        private static void AddLeader(CollegeBox box, GameResult r, Season cs, string cat, Func<PlayerGameLine, float> value, Func<PlayerGameLine, bool> qualifies, Func<PlayerGameLine, string> text)
        {
            for (int side = 0; side < 2; side++)
            {
                int teamId = side == 0 ? r.HomeTeamId : r.AwayTeamId;
                PlayerGameLine best = null; float bv = float.MinValue;
                for (int i = 0; i < r.PlayerLines.Count; i++)
                {
                    PlayerGameLine l = r.PlayerLines[i];
                    if (l.TeamId != teamId || !qualifies(l)) continue;
                    float v = value(l);
                    if (v > bv) { bv = v; best = l; }
                }
                if (best != null) box.Leaders.Add(new CollegeLeader { PlayerId = best.PlayerId, Name = best.Name, Pos = best.Pos, TeamId = teamId, Category = cat, Text = text(best), Value = bv });
            }
        }

        public static CollegeBox Find(CareerSave s, int year, int gameId)
        {
            var list = s.Net.Boxes;
            for (int i = list.Count - 1; i >= 0; i--) if (list[i].Year == year && list[i].GameId == gameId) return list[i];
            return null;
        }

        /// <summary>The current season keeps every box score; last season keeps its postseason games (title games, playoff, bowls); older ones go (keeps the save small).</summary>
        public static void Prune(CareerSave s)
        {
            if (s.College == null) return;
            int current = s.College.Year;
            s.Net.Boxes.RemoveAll(b => b.Year < current - 1 || b.Year < current && b.Kind == GameKind.Regular);
        }
    }

    /// <summary>College honors (Golden Helm, All-America, All-Conference) and the school history, settled when the college season ends.</summary>
    public static class CollegeHonors
    {
        private sealed class Slot { public string Name; public int Count; public Func<Position, bool> Match; }

        private static readonly Slot[] Slots =
        {
            new Slot { Name = "QB", Count = 1, Match = p => p == Position.QB },
            new Slot { Name = "RB", Count = 2, Match = p => p == Position.RB || p == Position.FB },
            new Slot { Name = "WR", Count = 3, Match = p => p == Position.WR },
            new Slot { Name = "TE", Count = 1, Match = p => p == Position.TE },
            new Slot { Name = "OL", Count = 5, Match = p => PositionBuckets.Of(p) == PositionBucket.OL },
            new Slot { Name = "DL", Count = 4, Match = p => PositionBuckets.Of(p) == PositionBucket.DL },
            new Slot { Name = "LB", Count = 3, Match = p => PositionBuckets.Of(p) == PositionBucket.LB },
            new Slot { Name = "CB", Count = 2, Match = p => p == Position.CB },
            new Slot { Name = "S", Count = 2, Match = p => PositionBuckets.Of(p) == PositionBucket.S },
            new Slot { Name = "K", Count = 1, Match = p => p == Position.K },
            new Slot { Name = "P", Count = 1, Match = p => p == Position.P }
        };

        private static float RefScale(PositionBucket b)
        {
            switch (b)
            {
                case PositionBucket.QB: return 260f;
                case PositionBucket.RB: case PositionBucket.FB: return 150f;
                case PositionBucket.WR: return 150f;
                case PositionBucket.TE: return 100f;
                case PositionBucket.DL: return 65f;
                case PositionBucket.LB: return 75f;
                case PositionBucket.CB: return 45f;
                case PositionBucket.S: return 50f;
            }
            return 100f;
        }

        /// <summary>How good a season a player had: production (scaled per position) plus his rating and his team's wins. Linemen have no stats, so rating and team success carry them.</summary>
        public static float Score(Player p, SeasonStats st, int wins)
        {
            PositionBucket b = PositionBuckets.Of(p.Position);
            int ovr = p.Overall;
            if (st == null || st.Games == 0) return -1f;
            if (b == PositionBucket.K) return st.FgMade * 3f + st.XpMade * 0.3f - (st.FgAtt - st.FgMade) * 2f + ovr * 0.4f + wins * 0.5f;
            if (b == PositionBucket.P) return ovr * 0.6f + st.PuntIn20 * 1.2f + (st.Punts > 0 ? (float)st.PuntYds / st.Punts : 0f) * 0.5f + wins * 0.5f;
            if (b == PositionBucket.OL) return ovr * 0.9f + wins * 1.0f;
            float value = AwardsCalculator.CollegeValue(st);
            return value / RefScale(b) * 50f + ovr * 0.4f + wins * 0.8f;
        }

        private sealed class Cand { public Player P; public School S; public float Score; }

        public static void Compute(CareerSave s)
        {
            CollegeWorld w = s.College;
            Season cs = w.Season;
            int year = cs.Year;
            CollegeNetState n = s.Net;
            n.Honors.RemoveAll(h => h.Year == year || h.Year < year - 3);
            // Golden Helm
            for (int i = 0; i < cs.Awards.Count; i++)
                if (cs.Awards[i].Award == AwardsCalculator.GoldenHelmName)
                {
                    AwardWinner a = cs.Awards[i];
                    School sc = w.GetSchool(a.TeamId);
                    n.Honors.Add(new CollegeHonor { Year = year, PlayerId = a.PlayerId, Name = a.PlayerName, Pos = a.Pos, TeamId = a.TeamId, ConferenceId = sc != null ? sc.ConferenceId : -1, Kind = CollegeHonorKind.GoldenHelm, Detail = AwardsCalculator.GoldenHelmName });
                }
            // candidates
            var all = new List<Cand>();
            for (int si = 0; si < w.Schools.Count; si++)
            {
                School sc = w.Schools[si];
                int wins = cs.WinsOf(sc.Id);
                var ro = sc.Team.Roster;
                for (int i = 0; i < ro.Count; i++)
                {
                    Player p = ro[i];
                    SeasonStats st = p.FindStats(year);
                    float score = Score(p, st, wins);
                    if (score < 0f) continue;
                    all.Add(new Cand { P = p, S = sc, Score = score });
                }
            }
            all.Sort((a, b) => b.Score.CompareTo(a.Score));
            // All-America first and second team
            for (int si = 0; si < Slots.Length; si++)
            {
                Slot slot = Slots[si];
                int taken = 0;
                for (int i = 0; i < all.Count && taken < slot.Count * 2; i++)
                {
                    if (!slot.Match(all[i].P.Position)) continue;
                    bool first = taken < slot.Count;
                    taken++;
                    n.Honors.Add(new CollegeHonor
                    {
                        Year = year, PlayerId = all[i].P.Id, Name = all[i].P.Name, Pos = all[i].P.Position, TeamId = all[i].S.Id, ConferenceId = all[i].S.ConferenceId,
                        Kind = first ? CollegeHonorKind.AllAmericaFirst : CollegeHonorKind.AllAmericaSecond, Detail = "All-America " + (first ? "first" : "second") + " team (" + slot.Name + ")"
                    });
                }
            }
            // All-Conference first team and the conference player of the year
            for (int ci = 0; ci < w.Conferences.Count; ci++)
            {
                Conference conf = w.Conferences[ci];
                bool poyDone = false;
                for (int si = 0; si < Slots.Length; si++)
                {
                    Slot slot = Slots[si];
                    int taken = 0;
                    for (int i = 0; i < all.Count && taken < slot.Count; i++)
                    {
                        if (all[i].S.ConferenceId != conf.Id || !slot.Match(all[i].P.Position)) continue;
                        taken++;
                        n.Honors.Add(new CollegeHonor
                        {
                            Year = year, PlayerId = all[i].P.Id, Name = all[i].P.Name, Pos = all[i].P.Position, TeamId = all[i].S.Id, ConferenceId = conf.Id,
                            Kind = CollegeHonorKind.AllConference, Detail = "All-" + conf.Abbreviation + " first team (" + slot.Name + ")"
                        });
                    }
                }
                for (int i = 0; i < all.Count && !poyDone; i++)
                {
                    if (all[i].S.ConferenceId != conf.Id) continue;
                    poyDone = true;
                    n.Honors.Add(new CollegeHonor
                    {
                        Year = year, PlayerId = all[i].P.Id, Name = all[i].P.Name, Pos = all[i].P.Position, TeamId = all[i].S.Id, ConferenceId = conf.Id,
                        Kind = CollegeHonorKind.ConferencePlayer, Detail = conf.Name + " Player of the Year"
                    });
                }
            }
        }

        public static List<CollegeHonor> HonorsOf(CareerSave s, int playerId)
        {
            var l = new List<CollegeHonor>();
            for (int i = 0; i < s.Net.Honors.Count; i++) if (s.Net.Honors[i].PlayerId == playerId) l.Add(s.Net.Honors[i]);
            l.Sort((a, b) => a.Year != b.Year ? b.Year.CompareTo(a.Year) : a.Kind.CompareTo(b.Kind));
            return l;
        }

        public static string HonorText(CareerSave s, CollegeHonor h)
        {
            switch (h.Kind)
            {
                case CollegeHonorKind.GoldenHelm: return h.Year + " " + AwardsCalculator.GoldenHelmName + " winner";
                case CollegeHonorKind.AllAmericaFirst: return h.Year + " All-America first team";
                case CollegeHonorKind.AllAmericaSecond: return h.Year + " All-America second team";
                case CollegeHonorKind.ConferencePlayer: return h.Year + " " + h.Detail;
            }
            return h.Year + " " + h.Detail;
        }

        public static void RecordHistory(CareerSave s)
        {
            CollegeWorld w = s.College;
            Season cs = w.Season;
            int year = cs.Year;
            CollegeNetState n = s.Net;
            n.History.RemoveAll(r => r.Year == year || r.Year < year - 9);
            PollSnapshot fin = cs.LatestPoll;
            var playoff = new HashSet<int>();
            for (int i = 0; i < cs.Seeds.Count; i++) playoff.Add(cs.Seeds[i].TeamId);
            var confChamps = new HashSet<int>();
            for (int i = 0; i < cs.Games.Count; i++) if (cs.Games[i].Kind == GameKind.ConferenceChampionship && cs.Games[i].Played) confChamps.Add(cs.Games[i].WinnerId);
            var rows = cs.Standings();
            var byTeam = new Dictionary<int, StandingsRow>();
            for (int i = 0; i < rows.Count; i++) byTeam[rows[i].TeamId] = rows[i];
            for (int si = 0; si < w.Schools.Count; si++)
            {
                School sc = w.Schools[si];
                StandingsRow r;
                byTeam.TryGetValue(sc.Id, out r);
                PollEntry pe = fin != null ? fin.Find(sc.Id) : null;
                string post = "";
                ScheduledGame last = null;
                for (int i = 0; i < cs.Games.Count; i++) { ScheduledGame g = cs.Games[i]; if (g.Kind != GameKind.Regular && g.Played && g.Involves(sc.Id) && g.Kind != GameKind.ConferenceChampionship && (last == null || g.Week > last.Week)) last = g; }
                if (last != null) post = (last.WinnerId == sc.Id ? "Won " : "Lost ") + (string.IsNullOrEmpty(last.Label) ? "postseason game" : last.Label);
                n.History.Add(new CollegeSeasonRecord
                {
                    Year = year, SchoolId = sc.Id, Wins = cs.WinsOf(sc.Id), Losses = cs.LossesOf(sc.Id), ConfWins = r != null ? r.ConfW : 0, ConfLosses = r != null ? r.ConfL : 0,
                    PointsFor = r != null ? r.PF : 0, PointsAgainst = r != null ? r.PA : 0, FinalRank = pe != null ? pe.Rank : 0, ConferenceChampion = confChamps.Contains(sc.Id),
                    NationalChampion = cs.ChampionTeamId == sc.Id, InPlayoff = playoff.Contains(sc.Id), Postseason = post
                });
            }
        }
    }
}
