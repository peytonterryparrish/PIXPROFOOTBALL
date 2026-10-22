using System;
using System.Collections.Generic;
using PixFootball.Core;

namespace PixFootball.League
{
    // ------------------------------------------------------------------------------------------
    // v0.9.0 "College Network" data model. Same serialization rules as CareerModel.cs: [Serializable], public fields,
    // List<T> and integer ids only (no dictionaries, no nullable value types, no references between objects) so JsonUtility
    // can save and load it. Runtime caches are [NonSerialized].
    // ------------------------------------------------------------------------------------------

    /// <summary>How much the user knows about a player. The tiers follow the scouting points spent on him (see <see cref="CollegeScouting.BandPoints"/> and friends).</summary>
    public enum ScoutTier { Unknown, Glance, Band, Range, Exact }

    public enum CollegeHonorKind { GoldenHelm, AllAmericaFirst, AllAmericaSecond, AllConference, ConferencePlayer }

    /// <summary>A member of a pro team's scouting department. Region and position skill decide how well he reads players.</summary>
    [Serializable]
    public class Scout
    {
        public int Id;
        public string Name = "";
        public int TeamId;
        public int Age;
        /// <summary>Eye for talent, 38-95. Output per week is <see cref="WeeklyPoints"/>.</summary>
        public int Skill;
        /// <summary>The <see cref="Region"/> he knows best (+20 percent output there).</summary>
        public int HomeRegion;
        /// <summary>PositionBucket index he specialises in (+20 percent output on those players), -1 for a generalist.</summary>
        public int FocusBucket = -1;
        public int Experience;
        /// <summary>The director runs the department: he adds to the points the general manager can spend himself and does not scout on his own.</summary>
        public bool Director;
        /// <summary>School he is sent to watch (-1 none).</summary>
        public int AssignSchoolId = -1;
        /// <summary>Region he covers when no school is set (-1 = his home region).</summary>
        public int AssignRegion = -1;
        public long Salary;
        public int PointsThisSeason;

        public float WeeklyPoints { get { return Director ? 0f : 4f + (Skill - 40) / 8f; } }
        public string FocusLabel { get { return FocusBucket < 0 ? "Generalist" : ((PositionBucket)FocusBucket).ToString(); } }
    }

    /// <summary>Combine or pro day numbers of a prospect (measured from his physical ratings with a little noise).</summary>
    [Serializable]
    public class CombineResult
    {
        public bool Done;
        /// <summary>Invited but did not work out (top prospects sometimes skip it).</summary>
        public bool Skipped;
        /// <summary>The numbers come from the school's pro day instead of the combine.</summary>
        public bool ProDay;
        public float Forty, TenYard, ThreeCone, Shuttle;
        public int BenchReps, VerticalIn, BroadJumpIn;

        public string Summary
        {
            get
            {
                if (!Done) return Skipped ? "Skipped" : "-";
                return Forty.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " 40, " + BenchReps + " reps, " + VerticalIn + "\" vert";
            }
        }
    }

    /// <summary>A prospect (or college player) the user starred and/or put on his personal big board.</summary>
    [Serializable]
    public class BoardEntry
    {
        public int PlayerId;
        public bool Starred;
        /// <summary>On the ordered big board (the order is the order of the entries that have this flag).</summary>
        public bool OnBoard;
        public string Note = "";
        /// <summary>Free tag such as "Sleeper", "Must have" or "Avoid".</summary>
        public string Tag = "";
        public int AddedYear;
    }

    [Serializable]
    public class CollegeBoxTeam
    {
        public int Plays, FirstDowns, TotalYards, PassYds, RushYds, PassAtt, PassCmp, RushAtt, Turnovers, Penalties, PenaltyYds, Sacks, ThirdDownAtt, ThirdDownConv;
        public int TimeOfPossessionSeconds;
    }

    [Serializable]
    public class CollegeLeader
    {
        public int PlayerId;
        public string Name = "";
        public Position Pos;
        public int TeamId;
        /// <summary>"Passing", "Rushing", "Receiving", "Tackles", "Sacks", "Interceptions".</summary>
        public string Category = "";
        public string Text = "";
        public float Value;
    }

    /// <summary>A compact box score of a college game (team totals, points by quarter, game leaders and the scoring plays). Kept for the current and the previous season.</summary>
    [Serializable]
    public class CollegeBox
    {
        public int Year, Week, GameId;
        public int HomeId, AwayId, HomeScore, AwayScore;
        public bool Overtime;
        public GameKind Kind;
        public string Label = "";
        public List<int> HomeByQuarter = new List<int>();
        public List<int> AwayByQuarter = new List<int>();
        public CollegeBoxTeam Home = new CollegeBoxTeam();
        public CollegeBoxTeam Away = new CollegeBoxTeam();
        public List<CollegeLeader> Leaders = new List<CollegeLeader>();
        public List<string> Scoring = new List<string>();
        /// <summary>Poll ranks of both teams before the game (0 = unranked).</summary>
        public int HomeRank, AwayRank;
    }

    [Serializable]
    public class CollegeHonor
    {
        public int Year;
        public int PlayerId;
        public string Name = "";
        public Position Pos;
        public int TeamId;
        public int ConferenceId = -1;
        public CollegeHonorKind Kind;
        public string Detail = "";
    }

    /// <summary>One school's season, kept for the history screens.</summary>
    [Serializable]
    public class CollegeSeasonRecord
    {
        public int Year;
        public int SchoolId;
        public int Wins, Losses, ConfWins, ConfLosses;
        public int PointsFor, PointsAgainst;
        /// <summary>Final poll rank (0 = unranked).</summary>
        public int FinalRank;
        public bool ConferenceChampion;
        public bool NationalChampion;
        public bool InPlayoff;
        public string Postseason = "";
    }

    [Serializable]
    public class ScoutLogItem
    {
        public int Year, Week;
        public string Text = "";
    }

    /// <summary>
    /// Everything the College Network adds to a career save (see <see cref="CollegeNet"/>): the scouting departments of all 32 teams, the user's scouting knowledge
    /// while the draft class does not exist yet, his big board, stored college box scores, honors and school history. A save made before v0.9.0 loads with
    /// <see cref="Ready"/> false and is set up lazily on first use.
    /// </summary>
    [Serializable]
    public class CollegeNetState
    {
        public int Version = 1;
        public bool Ready;
        /// <summary>College year the scouting state belongs to.</summary>
        public int Year;
        public List<Scout> Scouts = new List<Scout>();
        public List<Scout> ScoutPool = new List<Scout>();
        public int NextScoutId = 1;
        /// <summary>True while the user's scouting knowledge lives here (during the college season); at the combine it moves into <c>ProLeague.ScoutingReports</c>.</summary>
        public bool HasUserReport;
        public ScoutingReport UserReport = new ScoutingReport();
        /// <summary>Players whose physical measurements the user has seen (combine or pro day).</summary>
        public List<int> PhysicalsKnown = new List<int>();
        public bool CombineAttended;
        public List<int> ProDaySchools = new List<int>();
        public int AttendYear, AttendWeek;
        public List<BoardEntry> Board = new List<BoardEntry>();
        public List<CollegeBox> Boxes = new List<CollegeBox>();
        public List<CollegeHonor> Honors = new List<CollegeHonor>();
        public List<CollegeSeasonRecord> History = new List<CollegeSeasonRecord>();
        public List<ScoutLogItem> Log = new List<ScoutLogItem>();
        /// <summary>Year whose honors and history were already computed.</summary>
        public int HonorsYear;
        public int PointsEarned;

        /// <summary>Results of games played by the match this week, before the week advances (not saved).</summary>
        [NonSerialized] public List<GameResult> Pending = new List<GameResult>();
        /// <summary>Runtime cache of the draft-eligible college players (rebuilt on demand, never saved).</summary>
        [NonSerialized] internal object PoolCacheObj;
    }
}
