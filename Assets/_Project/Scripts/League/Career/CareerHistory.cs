using System;
using System.Collections.Generic;
using System.Text;
using PixFootball.Core;

namespace PixFootball.League
{
    public sealed class LegacyBreakdown
    {
        public int TeamId;
        public int Score;
        public string Tier = "";
        public List<KeyValuePair<string, int>> Lines = new List<KeyValuePair<string, int>>();
    }

    /// <summary>
    /// Everything that builds the franchise's story: season wrap-up (team season records, trophy case, awards, All-Pro teams, owner goals),
    /// the record book (single game / season / career, plus team records), the Hall of Fame and the franchise legacy score.
    /// </summary>
    public static class HistoryEngine
    {
        // ---------------------------------------------------------------- season wrap-up
        /// <summary>Everything that happens the moment the season ends (called once when the PIX Bowl is over).</summary>
        public static void FinalizeSeason(CareerSave s, Rng rng)
        {
            Season season = s.Season;
            var rows = season.Standings();
            AddTeamSeasons(s, season, rows);
            AddTrophies(s, season, rows);
            AddAccolades(s, season);
            ComputeAllPro(s, season);
            UpdateTeamRecords(s);
            Milestones(s, season);
            OwnerEngine.Evaluate(s);
            PostSeasonNews(s, season);
        }

        private static PlayoffResult ResultOf(Season season, int teamId)
        {
            int maxRound = 0; bool wonLast = false;
            for (int i = 0; i < season.Games.Count; i++)
            {
                ScheduledGame g = season.Games[i];
                if (g.Kind == GameKind.Regular || !g.Played || !g.Involves(teamId)) continue;
                if (g.Round >= maxRound) { maxRound = g.Round; wonLast = g.WinnerId == teamId; }
            }
            if (maxRound == 0)
            {
                for (int i = 0; i < season.Seeds.Count; i++) if (season.Seeds[i].TeamId == teamId) return PlayoffResult.WildCard;   // a bye that never played cannot happen, but stay safe
                return PlayoffResult.Missed;
            }
            if (maxRound == 4) return wonLast ? PlayoffResult.Champion : PlayoffResult.RunnerUp;
            if (maxRound == 3) return PlayoffResult.ConferenceFinal;
            if (maxRound == 2) return PlayoffResult.Divisional;
            return PlayoffResult.WildCard;
        }

        private static void AddTeamSeasons(CareerSave s, Season season, List<StandingsRow> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                StandingsRow r = rows[i];
                Team t = s.Pro.GetTeam(r.TeamId);
                var rec = new TeamSeasonRecord
                {
                    Year = season.Year, TeamId = r.TeamId, Wins = r.W, Losses = r.L, Ties = r.T, PointsFor = r.PF, PointsAgainst = r.PA,
                    DivisionTitle = r.GroupRank == 1, Result = ResultOf(season, r.TeamId), TeamOvr = t.TeamOverall(),
                    CoachId = t.HeadCoach != null ? t.HeadCoach.Id : 0
                };
                for (int k = 0; k < season.Seeds.Count; k++) if (season.Seeds[k].TeamId == r.TeamId) rec.ConferenceSeed = season.Seeds[k].Seed;
                rec.ConferenceTitle = rec.Result == PlayoffResult.RunnerUp || rec.Result == PlayoffResult.Champion;
                s.TeamSeasons.Add(rec);
            }
        }

        private static void AddTrophies(CareerSave s, Season season, List<StandingsRow> rows)
        {
            ScheduledGame fin = null;
            for (int i = season.Games.Count - 1; i >= 0; i--) if (season.Games[i].Kind == GameKind.Final) { fin = season.Games[i]; break; }
            for (int i = 0; i < rows.Count; i++)
            {
                StandingsRow r = rows[i];
                Team t = s.Pro.GetTeam(r.TeamId);
                string coach = t.HeadCoach != null ? t.HeadCoach.Name : "";
                if (r.GroupRank == 1) s.Trophies.Add(new TrophyEntry { Year = season.Year, TeamId = r.TeamId, Kind = TrophyKind.DivisionTitle, Coach = coach });
                if (r.L == 0 && r.T == 0 && r.W >= 17) s.Trophies.Add(new TrophyEntry { Year = season.Year, TeamId = r.TeamId, Kind = TrophyKind.PerfectRegularSeason, Coach = coach });
            }
            for (int i = 0; i < season.Games.Count; i++)
            {
                ScheduledGame g = season.Games[i];
                if (g.Kind != GameKind.PlayoffRound || g.Round != 3 || !g.Played) continue;
                s.Trophies.Add(new TrophyEntry
                {
                    Year = season.Year, TeamId = g.WinnerId, Kind = TrophyKind.ConferenceTitle, Opponent = CareerText.TeamNick(s, g.LoserId),
                    Score = Math.Max(g.HomeScore, g.AwayScore) + "-" + Math.Min(g.HomeScore, g.AwayScore), Coach = CoachName(s, g.WinnerId)
                });
            }
            if (fin != null)
            {
                s.Trophies.Add(new TrophyEntry
                {
                    Year = season.Year, TeamId = fin.WinnerId, Kind = TrophyKind.PixBowl, Opponent = CareerText.TeamNick(s, fin.LoserId),
                    Score = Math.Max(fin.HomeScore, fin.AwayScore) + "-" + Math.Min(fin.HomeScore, fin.AwayScore), BowlMvp = s.BowlMvpName, BowlMvpId = s.BowlMvpId, Coach = CoachName(s, fin.WinnerId)
                });
            }
        }

        private static string CoachName(CareerSave s, int teamId)
        {
            Team t = s.Pro.GetTeam(teamId);
            return t != null && t.HeadCoach != null ? t.HeadCoach.Name : "";
        }

        private static void AddAccolades(CareerSave s, Season season)
        {
            int y = season.Year;
            for (int i = 0; i < season.Awards.Count; i++)
            {
                AwardWinner a = season.Awards[i];
                AccoladeKind k;
                switch (a.Award)
                {
                    case "MVP": k = AccoladeKind.Mvp; break;
                    case "Offensive Player of the Year": k = AccoladeKind.Opoy; break;
                    case "Defensive Player of the Year": k = AccoladeKind.Dpoy; break;
                    default: k = AccoladeKind.Roy; break;
                }
                s.Accolades.Add(new Accolade { PlayerId = a.PlayerId, Year = y, TeamId = a.TeamId, Kind = k });
            }
            if (s.BowlMvpId != 0) s.Accolades.Add(new Accolade { PlayerId = s.BowlMvpId, Year = y, TeamId = s.BowlMvpTeamId, Kind = AccoladeKind.PixBowlMvp });
            // every player on the champion's roster who appeared in a game gets the ring
            Team champ = s.Pro.GetTeam(season.ChampionTeamId);
            if (champ != null)
                foreach (Player p in CapManager.AllUnderContract(champ))
                {
                    SeasonStats st = p.FindStats(y);
                    if (st != null && st.Games > 0) s.Accolades.Add(new Accolade { PlayerId = p.Id, Year = y, TeamId = champ.Id, Kind = AccoladeKind.Champion });
                }
            if (s.Accolades.Count > 60000) s.Accolades.RemoveRange(0, s.Accolades.Count - 50000);
        }

        // ---------------------------------------------------------------- All-Pro
        private static readonly Position[][] AllProSlots =
        {
            new[] { Position.QB }, new[] { Position.RB }, new[] { Position.FB }, new[] { Position.WR, Position.WR, Position.WR }, new[] { Position.TE },
            new[] { Position.LT }, new[] { Position.LG }, new[] { Position.C }, new[] { Position.RG }, new[] { Position.RT },
            new[] { Position.LE }, new[] { Position.DT, Position.DT }, new[] { Position.RE }, new[] { Position.LOLB }, new[] { Position.MLB }, new[] { Position.ROLB },
            new[] { Position.CB, Position.CB, Position.CB }, new[] { Position.FS }, new[] { Position.SS }, new[] { Position.K }, new[] { Position.P }
        };

        private sealed class Cand { public Player P; public int TeamId; public float Score; }

        public static void ComputeAllPro(CareerSave s, Season season)
        {
            int y = season.Year;
            var first = new AllProTeam { Year = y, Team = 1 }; var second = new AllProTeam { Year = y, Team = 2 };
            var byPos = new Dictionary<Position, List<Cand>>();
            for (int ti = 0; ti < s.Pro.Teams.Count; ti++)
            {
                Team t = s.Pro.Teams[ti];
                float winScore = season.WinsOf(t.Id) * 100f / 17f;
                foreach (Player p in CapManager.AllUnderContract(t))
                {
                    SeasonStats st = p.FindStats(y);
                    if (st == null || st.Games < 6) continue;
                    List<Cand> l;
                    if (!byPos.TryGetValue(p.Position, out l)) { l = new List<Cand>(); byPos[p.Position] = l; }
                    float prod = PositionProduction(p.Position, st);
                    l.Add(new Cand { P = p, TeamId = t.Id, Score = prod });   // production for now, blended below
                    l[l.Count - 1].Score = prod * 100f + winScore * 0.001f;
                }
            }
            foreach (var kv in byPos)
            {
                var list = kv.Value;
                // percentile of production inside the position, then blend with overall and team success
                var sorted = new List<Cand>(list);
                sorted.Sort((a, b) => b.Score.CompareTo(a.Score));
                for (int i = 0; i < sorted.Count; i++)
                {
                    float pct = sorted.Count <= 1 ? 100f : 100f * (sorted.Count - 1 - i) / (sorted.Count - 1);
                    Cand c = sorted[i];
                    float win = season.WinsOf(c.TeamId) * 100f / 17f;
                    bool hasStats = HasStatProduction(kv.Key);
                    c.Score = hasStats ? 0.5f * c.P.Overall + 0.38f * pct + 0.12f * win : 0.82f * c.P.Overall + 0.06f * pct + 0.12f * win;
                }
                list.Sort((a, b) => { int c = b.Score.CompareTo(a.Score); return c != 0 ? c : a.P.Id.CompareTo(b.P.Id); });
            }
            for (int g = 0; g < AllProSlots.Length; g++)
            {
                Position[] slots = AllProSlots[g];
                Position pos = slots[0];
                List<Cand> cands;
                if (!byPos.TryGetValue(pos, out cands)) continue;
                for (int k = 0; k < slots.Length; k++)
                {
                    if (k < cands.Count) first.Players.Add(Sel(cands[k]));
                    if (slots.Length + k < cands.Count) second.Players.Add(Sel(cands[slots.Length + k]));
                }
            }
            s.AllPro.Add(first); s.AllPro.Add(second);
            for (int i = 0; i < first.Players.Count; i++) s.Accolades.Add(new Accolade { PlayerId = first.Players[i].PlayerId, Year = y, TeamId = first.Players[i].TeamId, Kind = AccoladeKind.AllPro1 });
            for (int i = 0; i < second.Players.Count; i++) s.Accolades.Add(new Accolade { PlayerId = second.Players[i].PlayerId, Year = y, TeamId = second.Players[i].TeamId, Kind = AccoladeKind.AllPro2 });
        }

        private static AllProSelection Sel(Cand c)
        {
            return new AllProSelection { Pos = c.P.Position, PlayerId = c.P.Id, Name = c.P.Name, TeamId = c.TeamId, Score = c.Score };
        }

        private static bool HasStatProduction(Position p)
        {
            return !(PositionGroup.IsOffensiveLine(p) || p == Position.K || p == Position.P || p == Position.FB);
        }

        private static float PositionProduction(Position pos, StatLine st)
        {
            if (pos == Position.K) return st.FgMade + st.XpMade * 0.3f;
            if (pos == Position.P) return st.PuntYds / Math.Max(1f, st.Punts);
            if (PositionGroup.IsOffense(pos)) return AwardsCalculator.OffenseValue(st);
            return AwardsCalculator.DefenseValue(st);
        }

        public static AllProTeam GetAllPro(CareerSave s, int year, int team)
        {
            for (int i = 0; i < s.AllPro.Count; i++) if (s.AllPro[i].Year == year && s.AllPro[i].Team == team) return s.AllPro[i];
            return null;
        }

        public static int AccoladeCount(CareerSave s, int playerId, AccoladeKind kind)
        {
            int n = 0;
            for (int i = 0; i < s.Accolades.Count; i++) if (s.Accolades[i].PlayerId == playerId && s.Accolades[i].Kind == kind) n++;
            return n;
        }

        // ---------------------------------------------------------------- records
        private static string RecordKey(RecordEntry e) { return e.Category + "|" + (int)e.Scope; }
        private static string RecordValue(RecordEntry e) { return e.Value + "|" + e.PlayerId; }

        public static void SnapshotRecords(CareerSave s)
        {
            s.RecordSnapshot.Clear();
            RecordBook rb = s.Pro.Records;
            for (int i = 0; i < rb.Entries.Count; i++) s.RecordSnapshot.Add(RecordKey(rb.Entries[i]) + "=" + RecordValue(rb.Entries[i]));
        }

        /// <summary>Compares the record book with the last snapshot, posts news for broken records and returns them.</summary>
        public static List<RecordEntry> DetectBrokenRecords(CareerSave s, int maxNews = 3)
        {
            var broken = new List<RecordEntry>();
            RecordBook rb = s.Pro.Records;
            var old = new Dictionary<string, string>();
            for (int i = 0; i < s.RecordSnapshot.Count; i++)
            {
                int eq = s.RecordSnapshot[i].IndexOf('=');
                if (eq > 0) old[s.RecordSnapshot[i].Substring(0, eq)] = s.RecordSnapshot[i].Substring(eq + 1);
            }
            bool firstYear = s.Year == s.StartYear;   // the first season has no history: everything is a "record" while the book fills up, so no headlines
            int news = 0;
            for (int i = 0; i < rb.Entries.Count; i++)
            {
                RecordEntry e = rb.Entries[i];
                string key = RecordKey(e), val;
                bool had = old.TryGetValue(key, out val);
                if (had && val == RecordValue(e)) continue;
                if (!had) continue;   // first time a category fills: nothing was broken
                broken.Add(e);
                bool userTeam = e.TeamName == CareerText.TeamName(s, s.UserTeamId);
                if (!firstYear && news < maxNews && (e.Scope != RecordScope.SingleGame || userTeam || e.Value >= 6))
                {
                    news++;
                    string scope = e.Scope == RecordScope.SingleGame ? "single-game" : (e.Scope == RecordScope.SingleSeason ? "single-season" : "career");
                    NewsWriter.Post(s, NewsKind.Record, e.PlayerName + " breaks the " + scope + " " + Pretty(e.Category) + " record", e.PlayerName + " (" + e.TeamName + ") now holds the " + scope + " record for " + Pretty(e.Category) + " with " + e.Value + ".",
                        e.Scope == RecordScope.SingleGame ? 3 : 4, -1, -1, e.PlayerId);
                }
            }
            SnapshotRecords(s);
            return broken;
        }

        public static string Pretty(string category)
        {
            switch (category)
            {
                case "PassYds": return "passing yards";
                case "PassTd": return "passing touchdowns";
                case "RushYds": return "rushing yards";
                case "RushTd": return "rushing touchdowns";
                case "Rec": return "receptions";
                case "RecYds": return "receiving yards";
                case "RecTd": return "receiving touchdowns";
                case "Sacks": return "sacks";
                case "Interceptions": return "interceptions";
                case "Tackles": return "tackles";
                case "FgMade": return "field goals";
                case "Points": return "points";
            }
            return category;
        }

        private static void OfferTeamRecord(CareerSave s, string category, int teamId, int year, int value, bool lowerIsBetter)
        {
            TeamRecordEntry e = null;
            for (int i = 0; i < s.TeamRecords.Count; i++) if (s.TeamRecords[i].Category == category) e = s.TeamRecords[i];
            string name = CareerText.TeamName(s, teamId);
            if (e == null) { s.TeamRecords.Add(new TeamRecordEntry { Category = category, TeamId = teamId, TeamName = name, Year = year, Value = value }); return; }
            bool better = lowerIsBetter ? value < e.Value : value > e.Value;
            if (!better) return;
            e.TeamId = teamId; e.TeamName = name; e.Year = year; e.Value = value;
            NewsWriter.Post(s, NewsKind.Record, name + " set a new record: " + category.ToLowerInvariant(), name + " set the all-time mark for " + category.ToLowerInvariant() + " with " + value + " in " + year + ".", 3, teamId);
        }

        private static void UpdateTeamRecords(CareerSave s)
        {
            int year = s.Season.Year;
            // the best team of the year in each category is offered once (so a record is announced once, not once per club that beat the last)
            TeamSeasonRecord w = null, pf = null, pa = null, diff = null;
            for (int i = 0; i < s.TeamSeasons.Count; i++)
            {
                TeamSeasonRecord r = s.TeamSeasons[i];
                if (r.Year != year) continue;
                if (w == null || r.Wins > w.Wins) w = r;
                if (pf == null || r.PointsFor > pf.PointsFor) pf = r;
                if (pa == null || r.PointsAgainst < pa.PointsAgainst) pa = r;
                if (diff == null || r.PointsFor - r.PointsAgainst > diff.PointsFor - diff.PointsAgainst) diff = r;
            }
            if (w == null) return;
            OfferTeamRecord(s, "Most wins in a season", w.TeamId, year, w.Wins, false);
            OfferTeamRecord(s, "Most points scored in a season", pf.TeamId, year, pf.PointsFor, false);
            OfferTeamRecord(s, "Fewest points allowed in a season", pa.TeamId, year, pa.PointsAgainst, true);
            OfferTeamRecord(s, "Best point differential in a season", diff.TeamId, year, diff.PointsFor - diff.PointsAgainst, false);
        }

        public static TeamSeasonRecord SeasonRecord(CareerSave s, int teamId, int year)
        {
            for (int i = 0; i < s.TeamSeasons.Count; i++) if (s.TeamSeasons[i].TeamId == teamId && s.TeamSeasons[i].Year == year) return s.TeamSeasons[i];
            return null;
        }

        // ---------------------------------------------------------------- milestones and wrap-up news
        private static readonly string[] MilestoneNames = { "PassYds", "PassTd", "RushYds", "RecYds", "Sacks", "Interceptions" };
        private static readonly int[][] MilestoneSteps =
        {
            new[] { 10000, 20000, 30000, 40000, 50000 }, new[] { 100, 200, 300, 400 }, new[] { 5000, 10000, 15000 }, new[] { 5000, 10000, 15000 }, new[] { 50, 100, 150 }, new[] { 25, 50, 75 }
        };

        private static void Milestones(CareerSave s, Season season)
        {
            int posted = 0;
            for (int ti = 0; ti < s.Pro.Teams.Count && posted < 6; ti++)
            {
                Team t = s.Pro.Teams[ti];
                foreach (Player p in CapManager.AllUnderContract(t))
                {
                    SeasonStats st = p.FindStats(season.Year);
                    if (st == null || st.Games == 0) continue;
                    StatLine career = p.CareerTotals();
                    for (int m = 0; m < MilestoneNames.Length; m++)
                    {
                        int now = RecordBook.ValueOf(career, MilestoneNames[m]), before = now - RecordBook.ValueOf(st, MilestoneNames[m]);
                        for (int k = 0; k < MilestoneSteps[m].Length; k++)
                        {
                            int step = MilestoneSteps[m][k];
                            if (before < step && now >= step && posted < 6)
                            {
                                NewsWriter.Post(s, NewsKind.Milestone, p.Name + " reaches " + step.ToString("N0") + " career " + Pretty(MilestoneNames[m]), CareerText.PlayerLabel(p) + " of the " + t.Nickname + " passed " + step.ToString("N0") + " career " + Pretty(MilestoneNames[m]) + ".", step >= 20000 || m > 1 && step >= 10000 ? 3 : 2, t.Id, -1, p.Id);
                                posted++;
                            }
                        }
                    }
                }
            }
        }

        private static void PostSeasonNews(CareerSave s, Season season)
        {
            Team champ = s.Pro.GetTeam(season.ChampionTeamId);
            Team runner = s.Pro.GetTeam(season.RunnerUpTeamId);
            ScheduledGame fin = null;
            for (int i = season.Games.Count - 1; i >= 0; i--) if (season.Games[i].Kind == GameKind.Final) { fin = season.Games[i]; break; }
            if (champ != null)
                NewsWriter.Post(s, NewsKind.Title, champ.FullName + " win the " + season.Year + " PIX Bowl", champ.Nickname + " beat the " + (runner != null ? runner.Nickname : "field") + (fin != null ? " " + Math.Max(fin.HomeScore, fin.AwayScore) + "-" + Math.Min(fin.HomeScore, fin.AwayScore) : "") + (s.BowlMvpName.Length > 0 ? ". " + s.BowlMvpName + " is the game MVP." : "."), 5, champ.Id, runner != null ? runner.Id : -1, s.BowlMvpId);
            for (int i = 0; i < season.Awards.Count; i++)
            {
                AwardWinner a = season.Awards[i];
                NewsWriter.Post(s, NewsKind.Award, a.PlayerName + " wins " + (a.Award == "MVP" ? "league MVP" : a.Award), a.PlayerName + " (" + a.Pos + ", " + CareerText.TeamName(s, a.TeamId) + ") takes home the " + season.Year + " " + a.Award + " award.", a.Award == "MVP" ? 4 : 3, a.TeamId, -1, a.PlayerId);
            }
            AllProTeam ap = GetAllPro(s, season.Year, 1);
            if (ap != null)
                NewsWriter.Post(s, NewsKind.Award, "All-Pro first team announced", "The " + season.Year + " All-Pro first team: " + ap.Players.Count + " selections. QB " + FirstOf(ap, Position.QB) + ".", 3);
        }

        private static string FirstOf(AllProTeam t, Position pos)
        {
            for (int i = 0; i < t.Players.Count; i++) if (t.Players[i].Pos == pos) return t.Players[i].Name;
            return "n/a";
        }

        // ---------------------------------------------------------------- Hall of Fame
        public static float HofScore(CareerSave s, Player p)
        {
            float score = 0f;
            int seasons = 0;
            for (int i = 0; i < p.Career.Count; i++) if (!p.Career[i].Postseason && p.Career[i].Games > 0) seasons++;
            score += Math.Min(15, seasons) * 1.2f;
            for (int i = 0; i < s.Accolades.Count; i++)
            {
                Accolade a = s.Accolades[i];
                if (a.PlayerId != p.Id) continue;
                switch (a.Kind)
                {
                    case AccoladeKind.Mvp: score += 12f; break;
                    case AccoladeKind.Opoy: case AccoladeKind.Dpoy: score += 7f; break;
                    case AccoladeKind.Roy: score += 2f; break;
                    case AccoladeKind.AllPro1: score += 6f; break;
                    case AccoladeKind.AllPro2: score += 3f; break;
                    case AccoladeKind.Champion: score += 2.5f; break;
                    case AccoladeKind.PixBowlMvp: score += 5f; break;
                }
            }
            StatLine c = p.CareerTotals();
            float value = AwardsCalculator.OffenseValue(c) + AwardsCalculator.DefenseValue(c) * 3.5f;
            if (p.Position == Position.K) value = c.FgMade * 1.6f;
            if (p.Position == Position.P) value = c.Punts * 0.25f;
            score += Math.Min(30f, value / 200f);
            return score;
        }

        public const float HofThreshold = 42f;
        public const int HofMaxPerClass = 4;

        /// <summary>Records a retirement for later Hall of Fame voting.</summary>
        public static void RecordRetirement(CareerSave s, Player p, int teamId)
        {
            s.Retired.Add(new RetiredRecord { PlayerId = p.Id, Year = s.Year, TeamId = teamId });
            if (s.Retired.Count > 6000) s.Retired.RemoveRange(0, 1500);
        }

        /// <summary>Votes this year's Hall of Fame class: retired players past the waiting period, best career score first, above the bar.</summary>
        public static List<HofEntry> InductClass(CareerSave s)
        {
            var cands = new List<KeyValuePair<float, RetiredRecord>>();
            for (int i = 0; i < s.Retired.Count; i++)
            {
                RetiredRecord r = s.Retired[i];
                if (r.Inducted) continue;
                int eligibleFrom = r.Year + s.Settings.HofWaitYears;
                if (s.Year < eligibleFrom || s.Year > eligibleFrom + 3) continue;
                Player p = s.Pro.FindPlayer(r.PlayerId);
                if (p == null) continue;
                cands.Add(new KeyValuePair<float, RetiredRecord>(HofScore(s, p), r));
            }
            cands.Sort((a, b) => b.Key.CompareTo(a.Key));
            var inducted = new List<HofEntry>();
            for (int i = 0; i < cands.Count && inducted.Count < HofMaxPerClass; i++)
            {
                if (cands[i].Key < HofThreshold) break;
                RetiredRecord r = cands[i].Value;
                Player p = s.Pro.FindPlayer(r.PlayerId);
                r.Inducted = true;
                HofEntry e = BuildHofEntry(s, p, r, cands[i].Key);
                s.HallOfFame.Add(e);
                inducted.Add(e);
                NewsWriter.Post(s, NewsKind.HallOfFame, p.Name + " elected to the Hall of Fame", e.Summary + " " + e.Accolades, 4, e.TeamId, -1, p.Id);
            }
            return inducted;
        }

        private static HofEntry BuildHofEntry(CareerSave s, Player p, RetiredRecord r, float score)
        {
            var e = new HofEntry { PlayerId = p.Id, Name = p.Name, Pos = p.Position, InductionYear = s.Year, RetiredYear = r.Year, DraftYear = p.DraftYear, Score = score };
            // team played the most seasons for
            var seasonsBy = new Dictionary<int, int>(); var order = new List<int>();
            int seasons = 0;
            for (int i = 0; i < p.Career.Count; i++)
            {
                if (p.Career[i].Postseason || p.Career[i].Games <= 0) continue;
                seasons++;
                int tid = p.Career[i].TeamId;
                if (!seasonsBy.ContainsKey(tid)) { seasonsBy[tid] = 0; order.Add(tid); }
                seasonsBy[tid]++;
            }
            e.Seasons = seasons;
            int bestTeam = r.TeamId, bestN = -1;
            var names = new StringBuilder();
            for (int i = 0; i < order.Count; i++)
            {
                if (seasonsBy[order[i]] > bestN) { bestN = seasonsBy[order[i]]; bestTeam = order[i]; }
                if (i > 0) names.Append(", ");
                names.Append(CareerText.TeamNick(s, order[i]));
            }
            e.TeamId = bestTeam; e.Teams = names.ToString();
            StatLine c = p.CareerTotals();
            e.Summary = CareerLine(p.Position, c, seasons);
            var acc = new StringBuilder();
            int[] counts = new int[8];
            for (int i = 0; i < s.Accolades.Count; i++) if (s.Accolades[i].PlayerId == p.Id) counts[(int)s.Accolades[i].Kind]++;
            Append(acc, counts[(int)AccoladeKind.Mvp], "MVP");
            Append(acc, counts[(int)AccoladeKind.AllPro1], "first-team All-Pro");
            Append(acc, counts[(int)AccoladeKind.AllPro2], "second-team All-Pro");
            Append(acc, counts[(int)AccoladeKind.Opoy] + counts[(int)AccoladeKind.Dpoy], "Player of the Year");
            Append(acc, counts[(int)AccoladeKind.Champion], "PIX Bowl ring");
            e.Accolades = acc.ToString();
            return e;
        }

        private static void Append(StringBuilder sb, int n, string label)
        {
            if (n <= 0) return;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(n).Append("x ").Append(label);
        }

        public static string CareerLine(Position pos, StatLine c, int seasons)
        {
            if (c.PassAtt > 200) return seasons + " seasons, " + c.PassYds.ToString("N0") + " passing yards, " + c.PassTd + " TD, " + c.PassInt + " INT.";
            if (c.RushYds > c.RecYds && c.RushAtt > 100) return seasons + " seasons, " + c.RushYds.ToString("N0") + " rushing yards, " + c.RushTd + " TD.";
            if (c.Rec > 60) return seasons + " seasons, " + c.Rec + " catches for " + c.RecYds.ToString("N0") + " yards and " + c.RecTd + " TD.";
            if (c.Sacks + c.Tackles > 60) return seasons + " seasons, " + c.Tackles + " tackles, " + c.Sacks + " sacks, " + c.Interceptions + " interceptions.";
            if (c.FgAtt > 40) return seasons + " seasons, " + c.FgMade + " field goals.";
            return seasons + " seasons in the league.";
        }

        // ---------------------------------------------------------------- legacy
        public static LegacyBreakdown Legacy(CareerSave s, int teamId)
        {
            var lb = new LegacyBreakdown { TeamId = teamId };
            int titles = 0, confTitles = 0, divTitles = 0, playoffs = 0, winPts = 0, streak = 0, bestStreak = 0, mvps = 0;
            for (int i = 0; i < s.TeamSeasons.Count; i++)
            {
                TeamSeasonRecord r = s.TeamSeasons[i];
                if (r.TeamId != teamId) continue;
                if (r.Result == PlayoffResult.Champion) titles++;
                else if (r.ConferenceTitle) confTitles++;
                if (r.DivisionTitle) divTitles++;
                if (r.Result != PlayoffResult.Missed) { playoffs++; streak++; if (streak > bestStreak) bestStreak = streak; } else streak = 0;
                winPts += Math.Max(0, r.Wins - 8);
            }
            for (int i = 0; i < s.Accolades.Count; i++) if (s.Accolades[i].TeamId == teamId && s.Accolades[i].Kind == AccoladeKind.Mvp) mvps++;
            int hof = 0;
            for (int i = 0; i < s.HallOfFame.Count; i++) if (s.HallOfFame[i].TeamId == teamId) hof++;
            int records = 0;
            string teamName = CareerText.TeamName(s, teamId);
            for (int i = 0; i < s.Pro.Records.Entries.Count; i++) if (s.Pro.Records.Entries[i].TeamName == teamName) records++;
            for (int i = 0; i < s.TeamRecords.Count; i++) if (s.TeamRecords[i].TeamId == teamId) records++;
            Add(lb, "PIX Bowl titles (" + titles + ")", titles * 50);
            Add(lb, "Conference titles (" + confTitles + ")", confTitles * 15);
            Add(lb, "Division titles (" + divTitles + ")", divTitles * 6);
            Add(lb, "Playoff appearances (" + playoffs + ")", playoffs * 8);
            Add(lb, "Wins above eight", (int)(winPts * 1.5f));
            Add(lb, "Playoff streak (" + bestStreak + ")", bestStreak >= 3 ? bestStreak * 2 : 0);
            Add(lb, "League MVPs (" + mvps + ")", mvps * 5);
            Add(lb, "Hall of Famers (" + hof + ")", hof * 15);
            Add(lb, "Records held (" + records + ")", records * 3);
            int total = 0;
            for (int i = 0; i < lb.Lines.Count; i++) total += lb.Lines[i].Value;
            lb.Score = total;
            lb.Tier = total >= 450 ? "Legendary" : (total >= 250 ? "Dynasty" : (total >= 120 ? "Powerhouse" : (total >= 40 ? "Contender" : "Rebuilding")));
            return lb;
        }

        private static void Add(LegacyBreakdown lb, string label, int pts) { lb.Lines.Add(new KeyValuePair<string, int>(label, pts)); }

        /// <summary>The trophy case of one team: titles first, newest first.</summary>
        public static List<TrophyEntry> TrophyCase(CareerSave s, int teamId)
        {
            var l = new List<TrophyEntry>();
            for (int i = 0; i < s.Trophies.Count; i++) if (s.Trophies[i].TeamId == teamId) l.Add(s.Trophies[i]);
            l.Sort((a, b) => { int c = a.Kind.CompareTo(b.Kind); return c != 0 ? c : b.Year.CompareTo(a.Year); });
            return l;
        }
    }

    /// <summary>The owner: seasonal goals and satisfaction (the "OWNER" tab of the franchise hub).</summary>
    public static class OwnerEngine
    {
        public static void SetGoals(CareerSave s)
        {
            int year = s.Year;
            for (int i = s.Goals.Count - 1; i >= 0; i--) if (s.Goals[i].Year == year) s.Goals.RemoveAt(i);
            var order = new List<Team>(s.Pro.Teams);
            order.Sort((a, b) => b.TeamOverall().CompareTo(a.TeamOverall()));
            int rank = order.FindIndex(t => t.Id == s.UserTeamId) + 1;
            int wins = Math.Max(5, Math.Min(13, (int)Math.Round(8.5 + (16.5 - rank) * 0.22)));
            s.Goals.Add(new OwnerGoal { Year = year, Id = "wins", Text = "Win at least " + wins + " games", Target = wins, Weight = 1 });
            if (rank <= 20) s.Goals.Add(new OwnerGoal { Year = year, Id = "playoffs", Text = "Make the playoffs", Target = 1, Weight = 1 });
            if (rank <= 8) s.Goals.Add(new OwnerGoal { Year = year, Id = "division", Text = "Win the division", Target = 1, Weight = 1 });
            if (rank <= 3) s.Goals.Add(new OwnerGoal { Year = year, Id = "title", Text = "Win the PIX Bowl", Target = 1, Weight = 2 });
            else if (rank <= 12) s.Goals.Add(new OwnerGoal { Year = year, Id = "playoffwin", Text = "Win a playoff game", Target = 1, Weight = 1 });
        }

        public static void Evaluate(CareerSave s)
        {
            TeamSeasonRecord r = HistoryEngine.SeasonRecord(s, s.UserTeamId, s.Season.Year);
            if (r == null) return;
            int delta = 0;
            for (int i = 0; i < s.Goals.Count; i++)
            {
                OwnerGoal g = s.Goals[i];
                if (g.Year != s.Season.Year || g.Resolved) continue;
                switch (g.Id)
                {
                    case "wins": g.Actual = r.Wins; g.Met = r.Wins >= g.Target; break;
                    case "playoffs": g.Actual = r.Result != PlayoffResult.Missed ? 1 : 0; g.Met = g.Actual == 1; break;
                    case "division": g.Actual = r.DivisionTitle ? 1 : 0; g.Met = r.DivisionTitle; break;
                    case "title": g.Actual = r.Result == PlayoffResult.Champion ? 1 : 0; g.Met = g.Actual == 1; break;
                    case "playoffwin": g.Actual = r.Result >= PlayoffResult.Divisional ? 1 : 0; g.Met = g.Actual == 1; break;
                }
                g.Resolved = true;
                delta += g.Met ? 7 * g.Weight : -6 * g.Weight;
            }
            if (r.Result == PlayoffResult.Champion) delta += 10;
            s.OwnerSatisfaction = Math.Max(0, Math.Min(100, s.OwnerSatisfaction + delta));
            Team t = s.UserTeam;
            NewsWriter.Post(s, NewsKind.Owner, "Owner " + (delta >= 0 ? "pleased" : "disappointed") + " with the " + s.Season.Year + " season", "Owner satisfaction is now " + s.OwnerSatisfaction + " (" + JobSecurity(s) + ").", 2, t.Id);
        }

        public static string JobSecurity(CareerSave s)
        {
            return s.OwnerSatisfaction >= 75 ? "Secure" : (s.OwnerSatisfaction >= 50 ? "Stable" : (s.OwnerSatisfaction >= 30 ? "Warm seat" : "Hot seat"));
        }
    }
}
