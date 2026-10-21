namespace PixFootball.Core
{
    public enum PlayType
    {
        Run, Pass, Sack, Incomplete, Interception, Fumble, Punt, FieldGoal, PAT, TwoPoint,
        Kickoff, Kneel, Spike, Penalty
    }

    /// <summary>
    /// Outcome of one play, produced by the simulation and consumed by <see cref="GameState.Apply"/>.
    /// Conventions (yards are from the offense's perspective, positive = toward the opponent goal):
    ///  Run/Pass/Sack/Fumble: Yards = net gain from the line of scrimmage (fumble: to the point of the fumble/recovery).
    ///  Interception: Yards = how far past the LOS the pass was caught; ReturnYards = return by the defense.
    ///  Fumble with Turnover: ReturnYards = return by the recovering defense.
    ///  Punt/Kickoff: Yards = gross kick distance from the kick spot; ReturnYards = return by the receiving team.
    ///  FieldGoal/PAT/TwoPoint: Good = success. Blocked/missed FG may carry ReturnYards/Touchdown for the defense.
    ///  Touchdown: scored by the team that ends the play with the ball (offense, or the returner on turnovers/kicks).
    /// </summary>
    public sealed class PlayResult
    {
        public PlayType Type;
        public int Yards;
        public float TimeElapsed = 5f;
        /// <summary>Game-clock seconds that burn before the next snap if the clock keeps running (-1 = rule default).</summary>
        public float RunoffSeconds = -1f;
        public bool OutOfBounds;
        public bool Touchdown;
        public bool Turnover;
        public bool Safety;
        public bool Good;
        public bool Touchback;
        public bool FairCatch;
        /// <summary>Punt/kickoff recovered by the kicking team (onside kick, muffed punt).</summary>
        public bool RecoveredByKickingTeam;
        /// <summary>PAT/two-point returned by the defense for two points.</summary>
        public bool DefensiveScore;
        public int ReturnYards;

        public PenaltyCall Penalty;
        public PenaltyDecision PenaltyDecision = PenaltyDecision.Auto;

        // Player ids (for stats)
        public int PasserId, ReceiverId, RusherId, TacklerId, DefenderId, KickerId, ReturnerId;

        // --- factories ---
        public static PlayResult Run(int yards, float time = 5f, bool outOfBounds = false, int rusherId = 0, int tacklerId = 0)
        {
            return new PlayResult { Type = PlayType.Run, Yards = yards, TimeElapsed = time, OutOfBounds = outOfBounds, RusherId = rusherId, TacklerId = tacklerId };
        }

        public static PlayResult Pass(int yards, float time = 6f, bool outOfBounds = false, int passerId = 0, int receiverId = 0)
        {
            return new PlayResult { Type = PlayType.Pass, Yards = yards, TimeElapsed = time, OutOfBounds = outOfBounds, PasserId = passerId, ReceiverId = receiverId };
        }

        public static PlayResult Incomplete(float time = 4f, int passerId = 0, int targetId = 0)
        {
            return new PlayResult { Type = PlayType.Incomplete, TimeElapsed = time, PasserId = passerId, ReceiverId = targetId };
        }

        public static PlayResult Sack(int yardsLost, float time = 5f, int passerId = 0, int sackerId = 0)
        {
            return new PlayResult { Type = PlayType.Sack, Yards = -System.Math.Abs(yardsLost), TimeElapsed = time, PasserId = passerId, TacklerId = sackerId };
        }

        public static PlayResult Intercept(int yardsPastLos, int returnYards = 0, bool touchdown = false, float time = 7f)
        {
            return new PlayResult { Type = PlayType.Interception, Yards = yardsPastLos, ReturnYards = returnYards, Touchdown = touchdown, Turnover = true, TimeElapsed = time };
        }

        public static PlayResult FumbleLost(int yards, int returnYards = 0, bool touchdown = false, float time = 5f)
        {
            return new PlayResult { Type = PlayType.Fumble, Yards = yards, ReturnYards = returnYards, Touchdown = touchdown, Turnover = true, TimeElapsed = time };
        }

        public static PlayResult FumbleRecovered(int yards, float time = 5f)
        {
            return new PlayResult { Type = PlayType.Fumble, Yards = yards, TimeElapsed = time };
        }

        public static PlayResult Punt(int distance, int returnYards = 0, bool touchdown = false, bool touchback = false,
            bool outOfBounds = false, bool fairCatch = false, float time = 8f)
        {
            return new PlayResult { Type = PlayType.Punt, Yards = distance, ReturnYards = returnYards, Touchdown = touchdown, Touchback = touchback, OutOfBounds = outOfBounds, FairCatch = fairCatch, TimeElapsed = time };
        }

        public static PlayResult FieldGoal(bool good, int distance = 0, float time = 4f)
        {
            return new PlayResult { Type = PlayType.FieldGoal, Good = good, Yards = distance, TimeElapsed = time };
        }

        public static PlayResult Extra(bool good)
        {
            return new PlayResult { Type = PlayType.PAT, Good = good, TimeElapsed = 0f };
        }

        public static PlayResult TwoPointTry(bool good)
        {
            return new PlayResult { Type = PlayType.TwoPoint, Good = good, TimeElapsed = 0f };
        }

        public static PlayResult Kickoff(int distance, int returnYards = 0, bool touchdown = false, bool touchback = false,
            bool outOfBounds = false, float time = 6f)
        {
            return new PlayResult { Type = PlayType.Kickoff, Yards = distance, ReturnYards = returnYards, Touchdown = touchdown, Touchback = touchback, OutOfBounds = outOfBounds, TimeElapsed = time };
        }

        public static PlayResult Onside(int distance, bool recoveredByKicker)
        {
            return new PlayResult { Type = PlayType.Kickoff, Yards = distance, RecoveredByKickingTeam = recoveredByKicker, TimeElapsed = 4f };
        }

        public static PlayResult KneelDown(float time = 2f)
        {
            return new PlayResult { Type = PlayType.Kneel, Yards = -1, TimeElapsed = time };
        }

        public static PlayResult SpikeBall(float time = 1f)
        {
            return new PlayResult { Type = PlayType.Spike, TimeElapsed = time };
        }

        /// <summary>Dead-ball (pre-snap) penalty with no play.</summary>
        public static PlayResult PenaltyOnly(PenaltyCall call, float time = 0f)
        {
            return new PlayResult { Type = PlayType.Penalty, Penalty = call, TimeElapsed = time };
        }

        public PlayResult WithPenalty(PenaltyCall call, PenaltyDecision decision = PenaltyDecision.Auto)
        {
            Penalty = call; PenaltyDecision = decision; return this;
        }
    }
}
