namespace PixFootball.Core
{
    public enum OvertimeFormat
    {
        /// <summary>Pro: timed period, both teams touch the ball unless the first drive ends in a TD, then sudden death.</summary>
        ProModifiedSuddenDeath,
        /// <summary>College: alternating possessions from the opponent's 25, 2-pt tries required from the 3rd OT.</summary>
        CollegeAlternating
    }

    /// <summary>Data-only description of a rule variant. Use <see cref="Pro"/> or <see cref="College"/> then tweak.</summary>
    public sealed class RuleSet
    {
        public string Name = "Pro";
        /// <summary>Selects college yardage in the penalty catalog.</summary>
        public bool CollegeStyle = false;

        // Field / geometry
        public int FeetInBounds = 1;
        public float HashWidthFeet = 18.5f;

        // Clock
        public int QuarterMinutes = 15;
        public int TimeoutsPerHalf = 3;
        public bool HasTwoMinuteWarning = true;
        /// <summary>First downs stop the clock for the whole game (college).</summary>
        public bool ClockStopsOnFirstDown = false;
        /// <summary>If &gt; 0, first downs stop the clock in Q2/Q4 once the clock is at or below this many seconds.</summary>
        public int FirstDownStopWindowSeconds = 0;
        public int LastMinutesWindowSeconds = 120;
        public int PlayClockLive = 40;
        public int PlayClockDead = 25;
        /// <summary>Game-clock seconds that burn between snaps when the clock keeps running.</summary>
        public int NormalRunoff = 30;
        /// <summary>Seconds burned when the clock stopped and restarts on "ready for play".</summary>
        public int RestartOnReadyRunoff = 20;

        // Challenges
        public int Challenges = 2;
        public bool ThirdChallengeIfBothWon = true;
        public bool BoothReview = false;

        // Overtime
        public bool OvertimeEnabled = true;
        public OvertimeFormat Overtime = OvertimeFormat.ProModifiedSuddenDeath;
        public int OvertimeMinutes = 10;
        public int OvertimeTimeouts = 2;
        /// <summary>0 = unlimited periods. Pro regular season: 1.</summary>
        public int MaxOvertimePeriods = 1;
        public bool AllowTies = true;
        /// <summary>College OT: yard line (from the opponent's goal) where each possession starts.</summary>
        public int CollegeOvertimeStartYard = 25;
        /// <summary>College OT: first period in which a 2-pt try is required after a TD.</summary>
        public int TwoPointOnlyFromOvertimePeriod = 3;

        // Kicking
        public int KickoffSpot = 35;
        public int KickoffTouchbackSpot = 30;
        public int KickoffOutOfBoundsSpot = 40;
        public int PuntTouchbackSpot = 20;
        public int InterceptionTouchbackSpot = 20;
        public int SafetyKickSpot = 20;
        /// <summary>Yard line (from goal) the PAT is snapped from.</summary>
        public int PatSnapYardLine = 15;
        /// <summary>Yard line (from goal) a two-point try is snapped from.</summary>
        public int TwoPointYardLine = 2;
        public bool MissedFgFromSpotOfKick = true;
        public int MissedFgMinSpot = 20;

        // Penalty variants
        public bool DpiSpotFoul = true;
        /// <summary>Max DPI yardage (0 = unlimited).</summary>
        public int DpiMaxYards = 0;
        public int DefensiveHoldingYards = 5;
        public int OffensivePassInterferenceYards = 10;

        public int QuarterSeconds { get { return QuarterMinutes * 60; } }

        /// <summary>Ball spot (0-100) of the PAT snap.</summary>
        public int PatSpot { get { return 100 - PatSnapYardLine; } }
        /// <summary>Ball spot (0-100) of a two-point try.</summary>
        public int TwoPointSpot { get { return 100 - TwoPointYardLine; } }

        public RuleSet Clone() { return (RuleSet)MemberwiseClone(); }

        public static RuleSet Pro()
        {
            return new RuleSet();
        }

        public static RuleSet College()
        {
            return new RuleSet
            {
                Name = "College",
                CollegeStyle = true,
                FeetInBounds = 2,
                HashWidthFeet = 40f,
                HasTwoMinuteWarning = false,
                ClockStopsOnFirstDown = false,        // current NCAA rule: first downs stop the clock only in the last two minutes of each half
                FirstDownStopWindowSeconds = 120,
                Challenges = 0,
                ThirdChallengeIfBothWon = false,
                BoothReview = true,
                Overtime = OvertimeFormat.CollegeAlternating,
                OvertimeMinutes = 0,
                OvertimeTimeouts = 1,
                MaxOvertimePeriods = 0,
                AllowTies = false,
                KickoffSpot = 35,
                KickoffTouchbackSpot = 25,
                KickoffOutOfBoundsSpot = 35,
                PatSnapYardLine = 3,
                TwoPointYardLine = 3,
                MissedFgFromSpotOfKick = false,
                DpiSpotFoul = false,
                DpiMaxYards = 15,
                DefensiveHoldingYards = 10,
                OffensivePassInterferenceYards = 15
            };
        }
    }
}
