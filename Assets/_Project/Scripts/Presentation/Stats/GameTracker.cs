using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Gameplay;
using PixFootball.League;
using PixFootball.Sim;
using LeagueTeam = PixFootball.League.Team;
using CoreTeam = PixFootball.Core.Team;

namespace PixFootball.Presentation
{
    public enum FeatureRole { None, Passer, Receiver, Rusher, Sacker, Interceptor, Kicker, Returner }

    /// <summary>What one finished play meant for the broadcast stats: the featured player (stat lower-third) and a finished drive (drive summary).</summary>
    public sealed class PlayDigest
    {
        public int FeaturedPlayerId;
        public FeatureRole Role;
        public DriveRecord EndedDrive;
        public int EndedDriveTeamIdx;   // 0 home, 1 away
        public bool Scored;
        public ScoringEvent Score;
    }

    /// <summary>
    /// Live box score for the broadcast layer: per player lines, team box scores, drives (plays, yards, time of possession, result) and scoring events, updated from every finished play of the
    /// match (<see cref="PlayOutcome"/> plus the players on the field). Engine-free and tested; the lower-thirds, drive summaries, halftime and postgame shows read from it.
    /// </summary>
    public sealed class GameTracker
    {
        public readonly LeagueTeam Home, Away;
        public readonly string HomeAbbr, AwayAbbr;
        public readonly int QuarterSeconds;
        public readonly TeamBoxScore HomeBox = new TeamBoxScore(), AwayBox = new TeamBoxScore();
        public readonly List<DriveRecord> Drives = new List<DriveRecord>();
        public readonly List<ScoringEvent> Scoring = new List<ScoringEvent>();
        public readonly Dictionary<int, PlayerGameLine> Lines = new Dictionary<int, PlayerGameLine>();
        public readonly Dictionary<int, Player> Players = new Dictionary<int, Player>();
        public DriveRecord Current { get; private set; }
        public int Plays { get; private set; }

        // snapshot before the snap
        int _q0, _down0, _dist0, _spot0, _poss0; float _clock0; bool _have0;
        int _driveStartElapsed;

        public GameTracker(LeagueTeam home, LeagueTeam away, string homeAbbr, string awayAbbr, int quarterMinutes)
        {
            Home = home; Away = away; HomeAbbr = homeAbbr; AwayAbbr = awayAbbr; QuarterSeconds = Math.Max(60, quarterMinutes * 60);
            HomeBox.TeamId = home.Id; AwayBox.TeamId = away.Id;
        }

        public TeamBoxScore BoxOf(int teamIdx) { return teamIdx == 0 ? HomeBox : AwayBox; }
        public LeagueTeam TeamOf(int teamIdx) { return teamIdx == 0 ? Home : Away; }
        public string AbbrOf(int teamIdx) { return teamIdx == 0 ? HomeAbbr : AwayAbbr; }
        public int TeamIdxOfPlayer(Player p) { return p != null && p.TeamId == Away.Id && Away.Id != Home.Id ? 1 : 0; }

        /// <summary>Seconds of game time elapsed at the given quarter / clock (regulation quarters; overtime counts after the fourth).</summary>
        public int Elapsed(int quarter, float clockSeconds)
        {
            int q = Math.Max(1, quarter);
            return (int)Math.Round((q - 1) * QuarterSeconds + (QuarterSeconds - Math.Max(0f, Math.Min(QuarterSeconds, clockSeconds))));
        }

        /// <summary>Call when a play is built (before the snap; may be called more than once).</summary>
        public void BeginPlay(GameState s)
        {
            _q0 = s.Quarter; _down0 = s.Down; _dist0 = s.Distance; _spot0 = s.BallSpot; _poss0 = s.Possession == CoreTeam.Home ? 0 : 1; _clock0 = s.ClockSeconds; _have0 = true;
        }

        public PlayerGameLine LineOf(Player p)
        {
            PlayerGameLine l;
            if (!Lines.TryGetValue(p.Id, out l))
            {
                l = new PlayerGameLine { PlayerId = p.Id, Name = p.Name, Pos = p.Position, TeamId = p.TeamId, Started = true };
                Lines[p.Id] = l; Players[p.Id] = p;
            }
            return l;
        }

        /// <summary>Applies a finished play. <paramref name="lookup"/> maps a player id from the play result to the player on the field.</summary>
        public PlayDigest RecordPlay(PlayOutcome o, PlayKind kind, Func<int, Player> lookup, GameState after)
        {
            var d = new PlayDigest();
            if (o == null || o.Result == null) return d;
            PlayResult r = o.Result;
            int off = o.Offense == CoreTeam.Home ? 0 : 1, def = 1 - off;
            var offBox = BoxOf(off); var defBox = BoxOf(def);
            Plays++;
            bool scrim = kind == PlayKind.Scrimmage || kind == PlayKind.Kneel || kind == PlayKind.Spike;
            bool penaltyOnly = o.PenaltyAccepted && o.Headline == Headline.Penalty;
            Func<int, Player> get = id => { if (id <= 0) return null; var p = lookup != null ? lookup(id) : null; if (p != null) Players[p.Id] = p; return p; };
            Func<int, PlayerGameLine> line = id => { var p = get(id); return p == null ? null : LineOf(p); };

            if (penaltyOnly)
            {
                BoxOf(r.Penalty.CommittedBy == CoreTeam.Home ? 0 : 1).Penalties++;
                BoxOf(r.Penalty.CommittedBy == CoreTeam.Home ? 0 : 1).PenaltyYds += Math.Abs(r.Penalty.Def.ProYards);
                return d;
            }
            if (o.PenaltyAccepted && r.Penalty != null)
            {
                var pb = BoxOf(r.Penalty.CommittedBy == CoreTeam.Home ? 0 : 1);
                pb.Penalties++; pb.PenaltyYds += Math.Abs(r.Penalty.Def.ProYards);
                return d;   // the down is replayed: no stats for the nullified play
            }

            // ---------------------------------------------------------------- drive bookkeeping
            if (scrim && _have0)
            {
                if (Current != null && Current.TeamId != TeamOf(off).Id) EndDrive(d, "Turnover", after);
                if (Current == null)
                {
                    Current = new DriveRecord { TeamId = TeamOf(off).Id, Quarter = _q0, StartClockSeconds = (int)_clock0, StartSpot = _spot0 };
                    _driveStartElapsed = Elapsed(_q0, _clock0);
                    Drives.Add(Current); offBox.Drives++;
                }
                Current.Plays++;
            }

            // ---------------------------------------------------------------- stats by play type
            offBox.Plays += scrim ? 1 : 0;
            switch (r.Type)
            {
                case PlayType.Run:
                case PlayType.Kneel:
                {
                    var l = line(r.RusherId); var t = line(r.TacklerId);
                    offBox.RushAtt++; offBox.RushYds += r.Yards;
                    if (l != null) { l.Stats.RushAtt++; l.Stats.RushYds += r.Yards; l.Stats.RushLong = Math.Max(l.Stats.RushLong, r.Yards); if (r.Touchdown && !o.PossessionChanged) { l.Stats.RushTd++; offBox.RushTd++; } }
                    if (t != null) { t.Stats.Tackles++; if (r.Yards < 0) t.Stats.TacklesForLoss++; }
                    if (Current != null) Current.Yards += r.Yards;
                    if (l != null && r.Type == PlayType.Run) { d.FeaturedPlayerId = l.PlayerId; d.Role = FeatureRole.Rusher; }
                    break;
                }
                case PlayType.Pass:
                {
                    var p = line(r.PasserId); var rc = line(r.ReceiverId); var t = line(r.TacklerId);
                    offBox.PassAtt++; offBox.PassCmp++; offBox.PassYds += r.Yards;
                    if (p != null) { p.Stats.PassAtt++; p.Stats.PassCmp++; p.Stats.PassYds += r.Yards; p.Stats.PassLong = Math.Max(p.Stats.PassLong, r.Yards); if (r.Touchdown) { p.Stats.PassTd++; offBox.PassTd++; } }
                    if (rc != null) { rc.Stats.Targets++; rc.Stats.Rec++; rc.Stats.RecYds += r.Yards; rc.Stats.RecLong = Math.Max(rc.Stats.RecLong, r.Yards); if (r.Touchdown) rc.Stats.RecTd++; }
                    if (t != null) t.Stats.Tackles++;
                    if (Current != null) Current.Yards += r.Yards;
                    if (rc != null) { d.FeaturedPlayerId = rc.PlayerId; d.Role = FeatureRole.Receiver; }
                    else if (p != null) { d.FeaturedPlayerId = p.PlayerId; d.Role = FeatureRole.Passer; }
                    break;
                }
                case PlayType.Incomplete:
                {
                    var p = line(r.PasserId); var rc = line(r.ReceiverId);
                    offBox.PassAtt++;
                    if (p != null) p.Stats.PassAtt++;
                    if (rc != null) rc.Stats.Targets++;
                    break;
                }
                case PlayType.Sack:
                {
                    var p = line(r.PasserId); var s = line(r.TacklerId);
                    offBox.Sacked++; offBox.SackYdsLost += Math.Abs(r.Yards); defBox.SacksMade++;
                    if (p != null) { p.Stats.Sacked++; p.Stats.SackYds += Math.Abs(r.Yards); }
                    if (s != null) { s.Stats.Sacks++; s.Stats.Tackles++; d.FeaturedPlayerId = s.PlayerId; d.Role = FeatureRole.Sacker; }
                    if (Current != null) Current.Yards += r.Yards;
                    break;
                }
                case PlayType.Interception:
                {
                    var p = line(r.PasserId); var dfn = line(r.DefenderId);
                    offBox.PassAtt++; offBox.PassInt++; offBox.Giveaways++; defBox.InterceptionsMade++; defBox.Takeaways++;
                    if (p != null) { p.Stats.PassAtt++; p.Stats.PassInt++; }
                    if (dfn != null) { dfn.Stats.Interceptions++; dfn.Stats.IntYds += r.ReturnYards; if (r.Touchdown) dfn.Stats.IntTd++; d.FeaturedPlayerId = dfn.PlayerId; d.Role = FeatureRole.Interceptor; }
                    break;
                }
                case PlayType.Fumble:
                {
                    var l = line(r.RusherId);
                    if (l != null) { l.Stats.Fumbles++; if (r.Turnover) l.Stats.FumblesLost++; }
                    if (r.Turnover) { offBox.FumblesLost++; offBox.Giveaways++; defBox.Takeaways++; }
                    if (Current != null) Current.Yards += r.Yards;
                    break;
                }
                case PlayType.FieldGoal:
                {
                    var k = line(r.KickerId);
                    offBox.FgAtt++; if (r.Good) offBox.FgMade++;
                    if (k != null) { k.Stats.FgAtt++; if (r.Good) { k.Stats.FgMade++; k.Stats.FgLong = Math.Max(k.Stats.FgLong, r.Yards); } d.FeaturedPlayerId = k.PlayerId; d.Role = FeatureRole.Kicker; }
                    break;
                }
                case PlayType.PAT:
                {
                    offBox.XpAtt++; if (r.Good) offBox.XpMade++;
                    var k = line(r.KickerId);
                    if (k != null) { k.Stats.XpAtt++; if (r.Good) k.Stats.XpMade++; }
                    if (r.Good && Drives.Count > 0) Drives[Drives.Count - 1].Points += 1;
                    break;
                }
                case PlayType.TwoPoint:
                {
                    offBox.TwoPointAtt++; if (r.Good) { offBox.TwoPointMade++; if (Drives.Count > 0) Drives[Drives.Count - 1].Points += 2; }
                    break;
                }
                case PlayType.Punt:
                {
                    offBox.Punts++; offBox.PuntYds += r.Yards;
                    var k = line(r.KickerId); if (k != null) { k.Stats.Punts++; k.Stats.PuntYds += r.Yards; }
                    var rt = line(r.ReturnerId); if (rt != null) { rt.Stats.PrAtt++; rt.Stats.PrYds += r.ReturnYards; }
                    defBox.PuntReturnYds += r.ReturnYards;
                    break;
                }
                case PlayType.Kickoff:
                {
                    var rt = line(r.ReturnerId); if (rt != null) { rt.Stats.KrAtt++; rt.Stats.KrYds += r.ReturnYards; if (r.Touchdown) rt.Stats.RetTd++; d.FeaturedPlayerId = rt.PlayerId; d.Role = FeatureRole.Returner; }
                    defBox.KickoffReturnYds += r.ReturnYards;
                    break;
                }
            }
            if (o.Headline == Headline.FirstDown) offBox.FirstDowns++;
            if (scrim && (r.Type == PlayType.Run || r.Type == PlayType.Pass || r.Type == PlayType.Sack || r.Type == PlayType.Incomplete || r.Type == PlayType.Interception || r.Type == PlayType.Fumble))
            {
                bool conv = o.Headline == Headline.FirstDown || (o.Headline == Headline.Touchdown && !o.PossessionChanged);
                if (_down0 == 3) { offBox.ThirdDownAtt++; if (conv) offBox.ThirdDownConv++; }
                else if (_down0 == 4) { offBox.FourthDownAtt++; if (conv) offBox.FourthDownConv++; }
            }

            // ---------------------------------------------------------------- scoring
            if (o.ScoringTeam.HasValue)
            {
                int sIdx = o.ScoringTeam.Value == CoreTeam.Home ? 0 : 1;
                int pts = sIdx == 0 ? o.HomePoints : o.AwayPoints;
                var se = new ScoringEvent { Quarter = o.QuarterBefore, ClockSeconds = (int)after.ClockSeconds, TeamId = TeamOf(sIdx).Id, Points = pts, Description = o.Text, HomeScore = after.HomeScore, AwayScore = after.AwayScore };
                Scoring.Add(se); d.Scored = true; d.Score = se;
                BoxOf(sIdx).Score += pts;
                if (Current != null && (scrim || r.Type == PlayType.FieldGoal) && sIdx == off)
                {
                    if (r.Touchdown && !o.PossessionChanged) Current.Points += 6;
                    else if (r.Type == PlayType.FieldGoal) Current.Points += 3;
                }
            }

            // ---------------------------------------------------------------- drive end
            if (Current != null && scrim)
            {
                string res = null;
                switch (o.Headline)
                {
                    case Headline.Touchdown: res = "Touchdown"; break;
                    case Headline.Interception: res = "Interception"; break;
                    case Headline.FumbleLost: res = "Fumble"; break;
                    case Headline.TurnoverOnDowns: res = "Downs"; break;
                    case Headline.Safety: res = "Safety"; break;
                }
                if (o.Headline == Headline.Touchdown && r.Turnover) res = r.Type == PlayType.Interception ? "Interception" : "Fumble";
                if (res != null) EndDrive(d, res, after);
            }
            else if (Current != null && (r.Type == PlayType.Punt)) EndDrive(d, "Punt", after);
            else if (Current != null && r.Type == PlayType.FieldGoal) EndDrive(d, r.Good ? "Field Goal" : "Missed FG", after);
            if (Current != null && (o.PeriodChanged || o.GameOver) && Current != null) EndDrive(d, o.GameOver ? "End of Game" : (o.QuarterBefore == 2 ? "End of Half" : "End of Quarter"), after);
            // time of possession from the drives
            return d;
        }

        void EndDrive(PlayDigest d, string result, GameState after)
        {
            if (Current == null) return;
            Current.Result = result;
            int end = Elapsed(after.Quarter, after.ClockSeconds);
            if (after.Quarter != _q0 && result.StartsWith("End of")) end = Elapsed(_q0, 0f);
            Current.Seconds = Math.Max(0, end - _driveStartElapsed);
            int idx = Current.TeamId == Home.Id ? 0 : 1;
            BoxOf(idx).TimeOfPossessionSeconds += Current.Seconds;
            d.EndedDrive = Current; d.EndedDriveTeamIdx = idx;
            Current = null;
        }

        // ============================================================================================ queries
        public PlayerGameLine Line(int playerId) { PlayerGameLine l; return Lines.TryGetValue(playerId, out l) ? l : null; }
        public Player PlayerById(int id) { Player p; return Players.TryGetValue(id, out p) ? p : null; }

        /// <summary>The best performers of a team in a category ("pass", "rush", "rec", "def"), best first.</summary>
        public List<PlayerGameLine> Leaders(int teamIdx, string category, int count)
        {
            int tid = TeamOf(teamIdx).Id;
            var l = new List<PlayerGameLine>();
            foreach (var kv in Lines)
            {
                var pl = kv.Value;
                if (pl.TeamId != tid) continue;
                var s = pl.Stats;
                bool ok = category == "pass" ? s.PassAtt > 0 : category == "rush" ? s.RushAtt > 0 : category == "rec" ? s.Rec > 0 : (s.Tackles + s.Sacks + s.Interceptions) > 0;
                if (ok) l.Add(pl);
            }
            Comparison<PlayerGameLine> cmp = (a, b) => Metric(b, category).CompareTo(Metric(a, category));
            l.Sort(cmp);
            if (l.Count > count) l.RemoveRange(count, l.Count - count);
            return l;
        }

        static float Metric(PlayerGameLine l, string category)
        {
            var s = l.Stats;
            switch (category)
            {
                case "pass": return s.PassYds + s.PassTd * 20f;
                case "rush": return s.RushYds + s.RushTd * 20f;
                case "rec": return s.RecYds + s.RecTd * 20f;
                default: return s.Tackles + s.Sacks * 4f + s.Interceptions * 6f + s.ForcedFumbles * 3f;
            }
        }

        /// <summary>Game impact score of a line (player of the game).</summary>
        public static float Impact(PlayerGameLine l)
        {
            var s = l.Stats;
            return s.PassYds * 0.045f + s.PassTd * 5f - s.PassInt * 4f + s.RushYds * 0.11f + s.RushTd * 6f + s.RecYds * 0.10f + s.RecTd * 6f + s.Rec * 0.4f
                   + s.Sacks * 4.5f + s.Interceptions * 6f + s.Tackles * 0.55f + s.TacklesForLoss * 1.2f + s.FgMade * 2.5f + s.IntTd * 6f + s.RetTd * 6f - s.FumblesLost * 4f;
        }

        public PlayerGameLine PlayerOfTheGame(int winnerTeamIdx)
        {
            PlayerGameLine best = null; float bs = float.MinValue;
            int wid = winnerTeamIdx >= 0 ? TeamOf(winnerTeamIdx).Id : -1;
            foreach (var kv in Lines)
            {
                float s = Impact(kv.Value) + (kv.Value.TeamId == wid ? 3f : 0f);
                if (s > bs) { bs = s; best = kv.Value; }
            }
            return best;
        }
    }
}
