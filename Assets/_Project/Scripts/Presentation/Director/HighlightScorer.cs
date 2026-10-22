using PixFootball.Core;
using PixFootball.Gameplay;

namespace PixFootball.Presentation
{
    /// <summary>Everything the excitement score and the replay title need to know about one play.</summary>
    public struct PlayFacts
    {
        public Headline Headline;
        public PlayType Type;
        /// <summary>Net yards of the play (the gain, a sack's loss, kick distance for kicks).</summary>
        public int Yards;
        public int ReturnYards;
        public bool Touchdown, Turnover, Safety;
        public bool OffenseIsHome;
        public int Quarter;
        public float ClockSeconds;
        /// <summary>Offense score minus defense score after the play (a big swing late is clutch).</summary>
        public int ScoreMargin;
        /// <summary>Strongest collision of the play, 0..1, and whether the contact director called it a big hit.</summary>
        public float Impact;
        public bool BigHit;
        public bool ThirdOrFourthDownConversion;
        public bool Overtime;
    }

    /// <summary>
    /// Excitement of a play (0..100) for the automatic replays and the halftime / postgame highlight reels: touchdowns and turnovers lead, then safeties, sacks, big hits and long gains,
    /// clutch time and conversions add, routine plays score close to nothing. Pure and tested.
    /// </summary>
    public static class HighlightScorer
    {
        /// <summary>Score at which an automatic replay runs: Full cinematics replays everything notable, Minimal only touchdowns and turnovers.</summary>
        public const float FullThreshold = 45f, MinimalThreshold = 60f;

        public static float Score(in PlayFacts f)
        {
            float s = 0f;
            switch (f.Headline)
            {
                case Headline.Touchdown:
                    s = 80f;
                    if (f.Yards >= 40) s += 12f; else if (f.Yards >= 20) s += 6f;
                    if (f.Turnover || f.Type == PlayType.Interception || f.Type == PlayType.Fumble || f.ReturnYards > 20) s += 8f;
                    break;
                case Headline.Safety: s = 70f; break;
                case Headline.Interception: s = 62f + f.ReturnYards * 0.35f; break;
                case Headline.FumbleLost: s = 60f + f.ReturnYards * 0.3f; break;
                case Headline.TwoPointGood: s = 55f; break;
                case Headline.FieldGoal: s = 28f + (f.Yards >= 50 ? 18f : f.Yards >= 42 ? 7f : 0f); break;   // a 50+ yard kick clears FullThreshold (45) and gets its replay (QA-873)
                case Headline.FieldGoalMissed: s = 26f; break;
                case Headline.TurnoverOnDowns: s = 46f; break;
                case Headline.Sack: s = 48f + f.Impact * 8f; break;
                case Headline.Fumble: s = 34f; break;
                case Headline.Penalty: s = 5f; break;
                case Headline.Incomplete: s = f.ThirdOrFourthDownConversion ? 12f : 2f; break;
                case Headline.Punt: s = f.ReturnYards >= 25 ? 40f : 6f; break;
                case Headline.Kickoff: s = f.ReturnYards >= 40 ? 44f : 3f; break;
                case Headline.FirstDown: s = 14f; break;
                default: s = 5f; break;
            }
            // long gains (runs and completions)
            if ((f.Type == PlayType.Run || f.Type == PlayType.Pass) && f.Headline != Headline.Touchdown && f.Yards >= 20)
                s = System.Math.Max(s, 45f + (f.Yards - 20) * 0.9f);
            if (f.Headline == Headline.Touchdown && f.Type != PlayType.Kickoff && f.Type != PlayType.Punt) s += System.Math.Min(6f, f.Yards * 0.05f);
            // big hits make any play a candidate
            if (f.BigHit) s = System.Math.Max(s, 46f + f.Impact * 14f);
            else if (f.Impact > 0.75f) s += 6f;
            if (f.ThirdOrFourthDownConversion && f.Headline != Headline.Incomplete) s += 10f;
            // clutch time
            bool late = (f.Quarter == 4 || f.Overtime) && f.ClockSeconds <= 240f;
            int margin = System.Math.Abs(f.ScoreMargin);
            if (late && margin <= 8) s += 8f;
            if (f.Overtime) s += 6f;
            if (s > 100f) s = 100f;
            return s;
        }

        /// <summary>Replay tag, e.g. "42-YARD TOUCHDOWN", "INTERCEPTION", "BIG HIT".</summary>
        public static string Title(in PlayFacts f)
        {
            switch (f.Headline)
            {
                case Headline.Touchdown:
                    if (f.Type == PlayType.Interception) return "PICK SIX";
                    if (f.Type == PlayType.Kickoff || f.Type == PlayType.Punt) return "RETURN TOUCHDOWN";
                    if (f.Turnover && f.ReturnYards >= 2) return f.ReturnYards + "-YARD RETURN TOUCHDOWN";   // a fumble return counts the return, not the lost carry
                    return f.Yards >= 2 ? f.Yards + "-YARD TOUCHDOWN" : "TOUCHDOWN";
                case Headline.Interception: return "INTERCEPTION";
                case Headline.FumbleLost: return "FUMBLE - TURNOVER";
                case Headline.Safety: return "SAFETY";
                case Headline.Sack: return "SACK";
                case Headline.FieldGoal: return "FIELD GOAL";
                case Headline.FieldGoalMissed: return "FIELD GOAL NO GOOD";
                case Headline.TwoPointGood: return "TWO-POINT CONVERSION";
                case Headline.TurnoverOnDowns: return "TURNOVER ON DOWNS";
                case Headline.Fumble: return "FUMBLE";
            }
            if (f.BigHit) return "BIG HIT";
            if ((f.Type == PlayType.Run || f.Type == PlayType.Pass) && f.Yards >= 20) return f.Yards + "-YARD " + (f.Type == PlayType.Run ? "RUN" : "COMPLETION");
            if (f.Headline == Headline.Kickoff && f.ReturnYards >= 40) return "BIG KICK RETURN";
            if (f.Headline == Headline.Punt && f.ReturnYards >= 25) return "BIG PUNT RETURN";
            return "INSTANT REPLAY";
        }

        /// <summary>Whether the cinematics setting (0 Full, 1 Minimal, 2 Off) wants an automatic replay for a play with this score.</summary>
        public static bool WantsAutoReplay(float score, int cinematics)
        {
            if (cinematics >= 2) return false;
            return score >= (cinematics == 0 ? FullThreshold : MinimalThreshold);
        }
    }
}
