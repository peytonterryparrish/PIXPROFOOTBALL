using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    [Serializable]
    public class PlayerGameLine
    {
        public int PlayerId;
        public string Name;
        public Position Pos;
        public int TeamId;
        public bool Started;
        public StatLine Stats = new StatLine();
    }

    [Serializable]
    public class DriveRecord
    {
        public int TeamId;
        public int Quarter;
        public int StartClockSeconds;
        /// <summary>Yards from the offense's own goal line.</summary>
        public int StartSpot;
        public int Plays;
        public int Yards;
        public int Seconds;
        /// <summary>"Touchdown", "Field Goal", "Missed FG", "Punt", "Interception", "Fumble", "Downs", "Safety", "End of Half", "End of Regulation", "End of Game".</summary>
        public string Result = "";
        public int Points;
    }

    [Serializable]
    public class ScoringEvent
    {
        public int Quarter;
        public int ClockSeconds;
        public int TeamId;
        public int Points;
        public string Description = "";
        public int HomeScore, AwayScore;
    }

    [Serializable]
    public class TeamBoxScore
    {
        public int TeamId;
        public int Score;
        public List<int> ByQuarter = new List<int> { 0, 0, 0, 0 };
        public int OvertimeScore;
        public int Plays;
        public int FirstDowns;
        public int PassAtt, PassCmp, PassYds, PassTd, PassInt;
        public int Sacked, SackYdsLost;
        public int RushAtt, RushYds, RushTd;
        public int SacksMade, InterceptionsMade, FumblesLost, FumblesForced, Takeaways, Giveaways;
        public int Penalties, PenaltyYds;
        public int ThirdDownAtt, ThirdDownConv, FourthDownAtt, FourthDownConv;
        public int RedZoneTrips, RedZoneTds;
        public int Punts, PuntYds;
        public int FgAtt, FgMade, XpAtt, XpMade, TwoPointAtt, TwoPointMade;
        public int KickoffReturnYds, PuntReturnYds, Kneels;
        public int TimeOfPossessionSeconds;
        public int Drives;

        public int TotalYards { get { return PassYds - SackYdsLost + RushYds; } }
        public int NetPassYards { get { return PassYds - SackYdsLost; } }
    }

    /// <summary>An injury suffered in a game (v0.8.0): who, what and how many weeks. Season rolls them after each simulated game; a played match may supply its own.</summary>
    [Serializable]
    public class GameInjury
    {
        public int PlayerId;
        public string Name;
        public int TeamId;
        public Position Pos;
        public string Type = "";
        public int Weeks;
    }

    [Serializable]
    public class GameResult
    {
        public int HomeTeamId, AwayTeamId;
        public int HomeScore, AwayScore;
        public bool Overtime;
        public int OvertimePeriods;
        public bool Neutral;
        public int TotalPlays;
        public TeamBoxScore Home = new TeamBoxScore();
        public TeamBoxScore Away = new TeamBoxScore();
        public List<PlayerGameLine> PlayerLines = new List<PlayerGameLine>();
        public List<DriveRecord> Drives = new List<DriveRecord>();
        public List<ScoringEvent> Scoring = new List<ScoringEvent>();
        public List<string> PlayLog = new List<string>();
        /// <summary>Injuries from this game with their durations (filled by the season simulation or by the played match).</summary>
        public List<GameInjury> Injuries = new List<GameInjury>();
        /// <summary>True when <see cref="Injuries"/> already holds the injuries of a played match (the season then rolls no extra ones).</summary>
        public bool InjuriesProvided;

        public bool Tie { get { return HomeScore == AwayScore; } }
        /// <summary>Winning team id or -1 for a tie.</summary>
        public int WinnerTeamId { get { return HomeScore > AwayScore ? HomeTeamId : (AwayScore > HomeScore ? AwayTeamId : -1); } }
        public int LoserTeamId { get { return HomeScore > AwayScore ? AwayTeamId : (AwayScore > HomeScore ? HomeTeamId : -1); } }

        public TeamBoxScore BoxFor(int teamId) { return teamId == HomeTeamId ? Home : Away; }

        public PlayerGameLine FindLine(int playerId)
        {
            for (int i = 0; i < PlayerLines.Count; i++) if (PlayerLines[i].PlayerId == playerId) return PlayerLines[i];
            return null;
        }
    }

    /// <summary>Tunables for a single simulated game.</summary>
    public sealed class SimOptions
    {
        /// <summary>Home team gets a small edge; ignored when <see cref="Neutral"/>.</summary>
        public bool HomeAdvantage = true;
        public bool Neutral = false;
        public bool KeepPlayLog = false;
        /// <summary>Playoff / must-have-a-winner games: no ties, unlimited overtime.</summary>
        public bool ForceWinner = false;
    }
}
