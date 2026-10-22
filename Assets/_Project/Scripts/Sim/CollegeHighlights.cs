using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using CoreTeam = PixFootball.Core.Team;
using LeagueTeam = PixFootball.League.Team;

namespace PixFootball.Sim
{
    public enum HighlightKind { Touchdown, DefensiveTouchdown, FieldGoal, BigPlay, Turnover, Sack, FourthDownStop, Safety, TwoPointTry, GoAheadScore }

    /// <summary>
    /// One highlight of a condensed college game. <see cref="GameSeed"/> + <see cref="PlayIndex"/> identify the play for good: the headless game is deterministic, so
    /// <see cref="CollegeHighlights.ReplayPlay"/> re-simulates it and hands the view the play's event log to animate. The fields below are the situation and the outcome
    /// the view needs to stage the play (down and distance, formation names, who threw, caught, ran, tackled).
    /// </summary>
    public sealed class HighlightPlay
    {
        /// <summary>Stable id, "gameSeed:playIndex".</summary>
        public string PlayId = "";
        public int GameSeed;
        /// <summary>Index into <see cref="PhysicalGameSim.Plays"/>.</summary>
        public int PlayIndex;
        public HighlightKind Kind;
        /// <summary>Importance (higher = better highlight).</summary>
        public float Score;
        /// <summary>1 = the best highlight of the game.</summary>
        public int Rank;
        public int Quarter;
        public float ClockBefore;
        public int Down, Distance, Spot;
        /// <summary>The team with the ball.</summary>
        public CoreTeam Offense;
        public int OffenseSchoolId;
        public string OffensePlay = "", DefensePlay = "", OffenseFormation = "", DefenseFormation = "";
        public PlayType Type;
        public int Yards, ReturnYards;
        public bool Touchdown, Turnover, Good;
        public int PasserId, ReceiverId, RusherId, TacklerId, DefenderId, KickerId, ReturnerId;
        public string PasserName = "", ReceiverName = "", RusherName = "", TacklerName = "", DefenderName = "", KickerName = "";
        public int HomeScoreAfter, AwayScoreAfter;
        public float SimSeconds;
        public string Headline = "", Description = "";
    }

    public sealed class CollegeHighlightReel
    {
        public CollegeGameSetup Setup;
        public int GameSeed;
        /// <summary>Which seed attempt produced this game (0 = the setup's own seed).</summary>
        public int Attempt;
        public int HomeScore, AwayScore;
        public bool Overtime;
        public int TotalPlays;
        /// <summary>The top plays in game order.</summary>
        public List<HighlightPlay> Highlights = new List<HighlightPlay>();
        /// <summary>For a game that was already played: whether the highlight game has the same winner as the recorded result.</summary>
        public bool MatchesRecorded = true;
        public int RecordedHomeScore, RecordedAwayScore;
        /// <summary>Box score and player lines built from the plays (so a game that was not played yet can be posted to the season).</summary>
        public GameResult Result;
        public string Summary = "";
    }

    /// <summary>
    /// College highlights: runs the physical game simulation between two schools headlessly and extracts the plays worth watching (touchdowns, big gains, turnovers,
    /// field goals, sacks, fourth-down stops, go-ahead scores) with everything the view needs to replay them. Also builds a posted-able <see cref="GameResult"/>.
    /// </summary>
    public static class CollegeHighlights
    {
        public const int DefaultTopPlays = 10;

        private static PhysicalGameSim NewSim(CollegeGameSetup setup, int seed, bool keepLogs)
        {
            var sim = new PhysicalGameSim(setup.Home, setup.Away, setup.Rules ?? RuleSet.College(), new Rng(seed)) { KeepLogs = keepLogs };
            return sim;
        }

        /// <summary>Runs the game with the given seed and extracts the top plays. Does not change any league data.</summary>
        public static CollegeHighlightReel Generate(CollegeGameSetup setup, int topN = DefaultTopPlays, int seed = 0, int attempt = 0)
        {
            if (seed == 0) seed = setup.SimSeed;
            PhysicalGameSim sim = NewSim(setup, seed, false);
            sim.Run(600);
            return Build(setup, sim, seed, attempt, topN);
        }

        /// <summary>
        /// Highlights for a scheduled game of the user's career. A game that is not played yet is simulated with its own seed (post it with
        /// <see cref="PlayAndPost"/>). A game that was already played tries a few seeds and keeps the one that has the recorded winner (and the closest score), so the highlights agree with the box score.
        /// </summary>
        public static CollegeHighlightReel ForGame(CareerSave s, int gameId, int topN = DefaultTopPlays, int maxAttempts = 8)
        {
            CollegeGameSetup setup = CollegeNet.SetupGame(s, gameId);
            if (setup == null) return null;
            ScheduledGame g = setup.Game;
            if (!g.Played) return Generate(setup, topN, 0, 0);
            CollegeHighlightReel best = null;
            int bestDiff = int.MaxValue;
            for (int a = 0; a < maxAttempts; a++)
            {
                int seed = unchecked(setup.SimSeed + a * 7919);
                if (seed == 0) seed = 1;
                CollegeHighlightReel r = Generate(setup, topN, seed, a);
                int winner = r.HomeScore > r.AwayScore ? g.HomeId : (r.AwayScore > r.HomeScore ? g.AwayId : -1);
                bool match = winner == g.WinnerId;
                int diff = Math.Abs(r.HomeScore - g.HomeScore) + Math.Abs(r.AwayScore - g.AwayScore);
                r.RecordedHomeScore = g.HomeScore; r.RecordedAwayScore = g.AwayScore; r.MatchesRecorded = match;
                if (best == null || match && !best.MatchesRecorded || match == best.MatchesRecorded && diff < bestDiff) { best = r; bestDiff = diff; }
                if (match && diff <= 14) break;
            }
            return best;
        }

        /// <summary>
        /// Watching a game as condensed highlights: simulates the game and, when it was not played yet (current college week), posts it to the season so the highlights ARE the result
        /// (stats, standings, Golden Helm and the box score follow). Returns null when the game cannot be played now.
        /// </summary>
        public static CollegeHighlightReel PlayAndPost(CareerSave s, int gameId, int topN = DefaultTopPlays)
        {
            CollegeGameSetup setup = CollegeNet.SetupGame(s, gameId);
            if (setup == null || setup.AlreadyPlayed || s.College.Season.IsComplete || setup.Week != s.College.Season.Week) return null;
            CollegeHighlightReel reel = Generate(setup, topN, 0, 0);
            if (reel.Result.HomeScore == reel.Result.AwayScore) { reel.Result.HomeScore++; reel.Result.Home.Score++; reel.HomeScore++; }   // a drawn college game gets a winner (the match decides it in overtime)
            OpResult post = CollegeNet.SubmitGame(s, gameId, reel.Result);
            return post.Ok ? reel : null;
        }

        /// <summary>Re-simulates the game of a highlight up to the play and returns its record, including the event log (<see cref="PlayRecord.Log"/>) the view can animate.</summary>
        public static PlayRecord ReplayPlay(CollegeGameSetup setup, int gameSeed, int playIndex)
        {
            PhysicalGameSim sim = NewSim(setup, gameSeed, true);
            int guard = 0;
            while (sim.Plays.Count <= playIndex && !sim.State.IsFinal && guard++ < 2000)
            {
                PlayRecord r = sim.PlayNext();
                if (r == null) break;
            }
            return playIndex < sim.Plays.Count ? sim.Plays[playIndex] : null;
        }

        public static PlayRecord ReplayPlay(CollegeGameSetup setup, HighlightPlay h) { return ReplayPlay(setup, h.GameSeed, h.PlayIndex); }

        // ================================================================ extraction
        private static string NameOf(PhysicalGameSim sim, int id)
        {
            if (id <= 0) return "";
            Player p = sim.Home.GetPlayer(id) ?? sim.Away.GetPlayer(id);
            return p != null ? p.Name : "";
        }

        private static CollegeHighlightReel Build(CollegeGameSetup setup, PhysicalGameSim sim, int seed, int attempt, int topN)
        {
            var reel = new CollegeHighlightReel
            {
                Setup = setup, GameSeed = seed, Attempt = attempt, HomeScore = sim.State.HomeScore, AwayScore = sim.State.AwayScore, Overtime = sim.State.IsOvertime || sim.State.IsCollegeOvertime,
                TotalPlays = sim.Plays.Count
            };
            var cands = new List<HighlightPlay>();
            int prevHome = 0, prevAway = 0;
            for (int i = 0; i < sim.Plays.Count; i++)
            {
                PlayRecord rec = sim.Plays[i];
                if (rec.Note || rec.Result == null) continue;
                HighlightPlay h = Evaluate(sim, rec, i, prevHome, prevAway, seed, setup);
                prevHome = rec.HomeScore; prevAway = rec.AwayScore;
                if (h != null) cands.Add(h);
            }
            var byScore = new List<HighlightPlay>(cands);
            byScore.Sort((a, b) => { int c = b.Score.CompareTo(a.Score); return c != 0 ? c : a.PlayIndex.CompareTo(b.PlayIndex); });
            if (byScore.Count > topN) byScore.RemoveRange(topN, byScore.Count - topN);
            for (int i = 0; i < byScore.Count; i++) byScore[i].Rank = i + 1;
            byScore.Sort((a, b) => a.PlayIndex.CompareTo(b.PlayIndex));
            reel.Highlights = byScore;
            reel.Result = ToGameResult(sim, setup);
            reel.Summary = setup.AwayName + " " + reel.AwayScore + ", " + setup.HomeName + " " + reel.HomeScore + (reel.Overtime ? " (OT)" : "") + ": " + reel.Highlights.Count + " highlights from " + reel.TotalPlays + " plays";
            return reel;
        }

        private static HighlightPlay Evaluate(PhysicalGameSim sim, PlayRecord rec, int index, int prevHome, int prevAway, int seed, CollegeGameSetup setup)
        {
            PlayResult r = rec.Result;
            float score = 0f;
            HighlightKind kind = HighlightKind.BigPlay;
            bool scrimmage = r.Type == PlayType.Run || r.Type == PlayType.Pass || r.Type == PlayType.Sack || r.Type == PlayType.Incomplete || r.Type == PlayType.Interception || r.Type == PlayType.Fumble;
            bool turnoverTd = r.Touchdown && r.Turnover;
            if (rec.PenaltyAccepted) return null;
            if (r.Type == PlayType.PAT) return null;
            if (r.Type == PlayType.TwoPoint)
            {
                if (!r.Good && !r.DefensiveScore) return null;
                score = 38f; kind = HighlightKind.TwoPointTry;
            }
            else if (r.Touchdown && (scrimmage || r.Type == PlayType.Punt || r.Type == PlayType.Kickoff))
            {
                kind = turnoverTd ? HighlightKind.DefensiveTouchdown : HighlightKind.Touchdown;
                score = 100f + Math.Min(Math.Abs(r.Yards) + r.ReturnYards, 90) * 0.55f + (turnoverTd ? 55f : 0f) + (r.Type == PlayType.Punt || r.Type == PlayType.Kickoff ? 45f : 0f);
            }
            else if (r.Turnover && (r.Type == PlayType.Interception || r.Type == PlayType.Fumble))
            {
                kind = HighlightKind.Turnover; score = 62f + r.ReturnYards * 0.5f;
            }
            else if (r.Safety) { kind = HighlightKind.Safety; score = 70f; }
            else if (r.Type == PlayType.FieldGoal && r.Good) { kind = HighlightKind.FieldGoal; score = 32f + (r.Yards >= 52 ? 28f : (r.Yards >= 45 ? 12f : 0f)); }
            else if ((r.Type == PlayType.Run || r.Type == PlayType.Pass) && r.Yards >= 20) { kind = HighlightKind.BigPlay; score = 22f + r.Yards * 1.4f; }
            else if (r.Type == PlayType.Sack) { kind = HighlightKind.Sack; score = 16f + (rec.DownBefore >= 3 ? 14f : 0f) + Math.Min(8, -r.Yards) * 0.8f; }
            else if (rec.DownBefore == 4 && scrimmage && r.Type != PlayType.Punt && r.Type != PlayType.FieldGoal && !r.Touchdown && r.Yards < rec.DistanceBefore)
            {
                kind = HighlightKind.FourthDownStop; score = 44f + (rec.SpotBefore >= 90 ? 14f : 0f);
            }
            else return null;
            // go-ahead / game-tying scores and late drama
            int lead0 = prevHome - prevAway, lead1 = rec.HomeScore - rec.AwayScore;
            bool scored = rec.HomeScore != prevHome || rec.AwayScore != prevAway;
            if (scored && (lead0 <= 0 && lead1 > 0 || lead0 >= 0 && lead1 < 0 || lead0 != 0 && lead1 == 0))
            {
                score += 28f;
                if (kind != HighlightKind.Touchdown && kind != HighlightKind.DefensiveTouchdown) kind = HighlightKind.GoAheadScore;
            }
            if (rec.Quarter >= 4 && rec.ClockBefore < 120f && Math.Abs(lead1) <= 8) score += 18f;

            var h = new HighlightPlay
            {
                PlayId = seed + ":" + index, GameSeed = seed, PlayIndex = index, Kind = kind, Score = score, Quarter = rec.Quarter, ClockBefore = rec.ClockBefore, Down = rec.DownBefore, Distance = rec.DistanceBefore, Spot = rec.SpotBefore,
                Offense = rec.Offense, OffenseSchoolId = rec.Offense == CoreTeam.Home ? setup.Game.HomeId : setup.Game.AwayId, OffensePlay = rec.OffensePlayName, DefensePlay = rec.DefensePlayName,
                Type = r.Type, Yards = r.Yards, ReturnYards = r.ReturnYards, Touchdown = r.Touchdown, Turnover = r.Turnover, Good = r.Good,
                PasserId = r.PasserId, ReceiverId = r.ReceiverId, RusherId = r.RusherId, TacklerId = r.TacklerId, DefenderId = r.DefenderId, KickerId = r.KickerId, ReturnerId = r.ReturnerId,
                HomeScoreAfter = rec.HomeScore, AwayScoreAfter = rec.AwayScore, SimSeconds = rec.SimSeconds
            };
            h.PasserName = NameOf(sim, h.PasserId); h.ReceiverName = NameOf(sim, h.ReceiverId); h.RusherName = NameOf(sim, h.RusherId); h.TacklerName = NameOf(sim, h.TacklerId);
            h.DefenderName = NameOf(sim, h.DefenderId); h.KickerName = NameOf(sim, h.KickerId);
            foreach (Play p in sim.Book.AllPlays)
            {
                if (p.Name == rec.OffensePlayName && p.Side == PlaySide.Offense && h.OffenseFormation.Length == 0) h.OffenseFormation = p.FormationName;
                if (p.Name == rec.DefensePlayName && p.Side == PlaySide.Defense && h.DefenseFormation.Length == 0) h.DefenseFormation = p.FormationName;
            }
            Describe(h, sim, setup);
            return h;
        }

        private static string Yd(int yards) { return yards + (yards == 1 ? " yard" : " yards"); }

        private static void Describe(HighlightPlay h, PhysicalGameSim sim, CollegeGameSetup setup)
        {
            string team = h.Offense == CoreTeam.Home ? setup.HomeName : setup.AwayName;
            string when = "Q" + h.Quarter + " " + ((int)h.ClockBefore / 60) + ":" + (((int)h.ClockBefore % 60) < 10 ? "0" : "") + ((int)h.ClockBefore % 60);
            string score = " (" + setup.AwayAbbr + " " + h.AwayScoreAfter + ", " + setup.HomeAbbr + " " + h.HomeScoreAfter + ")";
            string text;
            switch (h.Type)
            {
                case PlayType.Pass:
                    text = h.PasserName + " finds " + h.ReceiverName + " for " + Yd(h.Yards) + (h.Touchdown ? " and a touchdown" : ""); break;
                case PlayType.Run:
                    text = h.RusherName + " runs for " + Yd(h.Yards) + (h.Touchdown ? " and a touchdown" : ""); break;
                case PlayType.Sack:
                    text = h.TacklerName + " sacks " + h.PasserName + " for a loss of " + Yd(Math.Abs(h.Yards)); break;
                case PlayType.Interception:
                    text = h.DefenderName + " intercepts " + h.PasserName + (h.ReturnYards > 0 ? ", returns it " + Yd(h.ReturnYards) : "") + (h.Touchdown ? " for a touchdown" : ""); break;
                case PlayType.Fumble:
                    text = h.Turnover ? h.DefenderName + " recovers the fumble" + (h.Touchdown ? " and scores" : "") : "Fumble"; break;
                case PlayType.FieldGoal:
                    text = h.KickerName + " hits a " + h.Yards + "-yard field goal"; break;
                case PlayType.Punt: case PlayType.Kickoff:
                    text = "A " + h.ReturnYards + "-yard return" + (h.Touchdown ? " for a touchdown" : ""); break;
                case PlayType.TwoPoint:
                    text = "The two-point try is good"; break;
                default: text = h.OffensePlay; break;
            }
            switch (h.Kind)
            {
                case HighlightKind.FourthDownStop: text = "Fourth-down stop: " + text; break;
                case HighlightKind.Safety: text = "Safety: " + text; break;
            }
            h.Headline = text.Trim();
            h.Description = when + ": " + team + " - " + h.Headline + score;
        }

        // ================================================================ box score from the plays
        /// <summary>Builds the result of a headless game: scores by quarter, team totals and a stat line for every player who did something, in the same format the quick simulation and the match use.</summary>
        public static GameResult ToGameResult(PhysicalGameSim sim, CollegeGameSetup setup)
        {
            int homeId = setup.Game.HomeId, awayId = setup.Game.AwayId;
            var r = new GameResult { HomeTeamId = homeId, AwayTeamId = awayId, HomeScore = sim.State.HomeScore, AwayScore = sim.State.AwayScore, Neutral = setup.Neutral, TotalPlays = sim.Plays.Count };
            r.Overtime = sim.State.IsOvertime || sim.State.IsCollegeOvertime;
            r.Home.TeamId = homeId; r.Away.TeamId = awayId; r.Home.Score = r.HomeScore; r.Away.Score = r.AwayScore;
            var lines = new Dictionary<int, PlayerGameLine>();
            Func<int, PlayerGameLine> line = id =>
            {
                if (id <= 0) return null;
                PlayerGameLine l;
                if (lines.TryGetValue(id, out l)) return l;
                Player p = sim.Home.GetPlayer(id);
                int team = homeId;
                if (p == null) { p = sim.Away.GetPlayer(id); team = awayId; }
                if (p == null) return null;
                l = new PlayerGameLine { PlayerId = id, Name = p.Name, Pos = p.Position, TeamId = team, Started = true };
                l.Stats.Games = 1; l.Stats.GamesStarted = 1;
                lines[id] = l; r.PlayerLines.Add(l);
                return l;
            };
            int prevHome = 0, prevAway = 0, prevQ = 1;
            var qHome = new int[8]; var qAway = new int[8];
            for (int i = 0; i < sim.Plays.Count; i++)
            {
                PlayRecord rec = sim.Plays[i];
                if (rec.Note || rec.Result == null) continue;
                PlayResult p = rec.Result;
                TeamBoxScore off = rec.Offense == CoreTeam.Home ? r.Home : r.Away, def = rec.Offense == CoreTeam.Home ? r.Away : r.Home;
                int q = Math.Max(1, Math.Min(7, rec.Quarter));
                qHome[q] += rec.HomeScore - prevHome; qAway[q] += rec.AwayScore - prevAway;
                prevHome = rec.HomeScore; prevAway = rec.AwayScore; prevQ = q;
                if (rec.PenaltyAccepted) { off.Penalties++; continue; }
                PlayerGameLine passer = line(p.PasserId), recv = line(p.ReceiverId), rush = line(p.RusherId), tackler = line(p.TacklerId), defender = line(p.DefenderId), kicker = line(p.KickerId), ret = line(p.ReturnerId);
                switch (p.Type)
                {
                    case PlayType.Pass:
                        off.Plays++; off.PassAtt++; off.PassCmp++; off.PassYds += p.Yards; if (p.Touchdown) off.PassTd++;
                        if (p.Yards >= rec.DistanceBefore || p.Touchdown) off.FirstDowns++;
                        if (passer != null) { passer.Stats.PassAtt++; passer.Stats.PassCmp++; passer.Stats.PassYds += p.Yards; if (p.Touchdown) passer.Stats.PassTd++; passer.Stats.PassLong = Math.Max(passer.Stats.PassLong, p.Yards); }
                        if (recv != null) { recv.Stats.Targets++; recv.Stats.Rec++; recv.Stats.RecYds += p.Yards; if (p.Touchdown) recv.Stats.RecTd++; recv.Stats.RecLong = Math.Max(recv.Stats.RecLong, p.Yards); }
                        if (tackler != null) tackler.Stats.Tackles++;
                        break;
                    case PlayType.Incomplete:
                        off.Plays++; off.PassAtt++;
                        if (passer != null) passer.Stats.PassAtt++;
                        if (recv != null) recv.Stats.Targets++;
                        break;
                    case PlayType.Run:
                        off.Plays++; off.RushAtt++; off.RushYds += p.Yards; if (p.Touchdown) off.RushTd++;
                        if (p.Yards >= rec.DistanceBefore || p.Touchdown) off.FirstDowns++;
                        if (rush != null) { rush.Stats.RushAtt++; rush.Stats.RushYds += p.Yards; if (p.Touchdown) rush.Stats.RushTd++; rush.Stats.RushLong = Math.Max(rush.Stats.RushLong, p.Yards); }
                        if (tackler != null) { tackler.Stats.Tackles++; if (p.Yards < 0) tackler.Stats.TacklesForLoss++; }
                        break;
                    case PlayType.Sack:
                        off.Plays++; off.Sacked++; off.SackYdsLost += Math.Abs(p.Yards); def.SacksMade++;
                        if (passer != null) passer.Stats.Sacked++;
                        if (tackler != null) { tackler.Stats.Sacks++; tackler.Stats.Tackles++; tackler.Stats.TacklesForLoss++; }
                        break;
                    case PlayType.Interception:
                        off.Plays++; off.PassAtt++; off.PassInt++; off.Giveaways++; def.InterceptionsMade++; def.Takeaways++;
                        if (passer != null) { passer.Stats.PassAtt++; passer.Stats.PassInt++; }
                        if (defender != null) { defender.Stats.Interceptions++; defender.Stats.IntYds += p.ReturnYards; if (p.Touchdown) defender.Stats.IntTd++; }
                        break;
                    case PlayType.Fumble:
                        off.Plays++;
                        if (p.Turnover) { off.Giveaways++; off.FumblesLost++; def.Takeaways++; if (rush != null) { rush.Stats.Fumbles++; rush.Stats.FumblesLost++; } else if (passer != null) { passer.Stats.Fumbles++; passer.Stats.FumblesLost++; } if (defender != null) defender.Stats.FumbleRecoveries++; }
                        break;
                    case PlayType.FieldGoal:
                        off.FgAtt++; if (p.Good) off.FgMade++;
                        if (kicker != null) { kicker.Stats.FgAtt++; if (p.Good) { kicker.Stats.FgMade++; kicker.Stats.FgLong = Math.Max(kicker.Stats.FgLong, p.Yards); } }
                        break;
                    case PlayType.PAT:
                        off.XpAtt++; if (p.Good) off.XpMade++;
                        if (kicker != null) { kicker.Stats.XpAtt++; if (p.Good) kicker.Stats.XpMade++; }
                        break;
                    case PlayType.TwoPoint:
                        off.TwoPointAtt++; if (p.Good) { off.TwoPointMade++; PlayerGameLine who = recv ?? rush ?? passer; if (who != null) who.Stats.TwoPtMade++; }
                        break;
                    case PlayType.Punt:
                        off.Punts++; off.PuntYds += p.Yards;
                        if (kicker != null) { kicker.Stats.Punts++; kicker.Stats.PuntYds += p.Yards; }
                        if (ret != null) { ret.Stats.PrAtt++; ret.Stats.PrYds += p.ReturnYards; if (p.Touchdown) ret.Stats.RetTd++; }
                        break;
                    case PlayType.Kickoff:
                        if (ret != null) { ret.Stats.KrAtt++; ret.Stats.KrYds += p.ReturnYards; if (p.Touchdown) ret.Stats.RetTd++; }
                        break;
                    case PlayType.Kneel: case PlayType.Spike: off.Plays++; break;
                }
                if (rec.DownBefore == 3 && (p.Type == PlayType.Run || p.Type == PlayType.Pass || p.Type == PlayType.Incomplete || p.Type == PlayType.Sack)) { off.ThirdDownAtt++; if (p.Yards >= rec.DistanceBefore || p.Touchdown) off.ThirdDownConv++; }
            }
            for (int q = 1; q <= 4; q++) { r.Home.ByQuarter[q - 1] = qHome[q]; r.Away.ByQuarter[q - 1] = qAway[q]; }
            for (int q = 5; q <= 7; q++) { r.Home.OvertimeScore += qHome[q]; r.Away.OvertimeScore += qAway[q]; }
            return r;
        }
    }
}
