using System;

namespace PixFootball.Core
{
    /// <summary>What happened when a penalty was enforced.</summary>
    public sealed class EnforcementResult
    {
        public PenaltyCall Call;
        /// <summary>Signed yards the ball moved from the previous spot (negative = toward the offense's own goal).</summary>
        public int Yards;
        public int NewSpot;
        public bool AutomaticFirstDown;
        public bool FirstDown;
        public bool LossOfDown;
        public bool HalfDistance;
        public bool TurnoverOnDowns;
        public bool Offsetting;
        public string Description;
    }

    public static class PenaltyEnforcer
    {
        /// <summary>
        /// Enforces <paramref name="call"/> on <paramref name="s"/> (mutating the down, distance and ball spot).
        /// The state must still be the pre-play state. Does not touch the clock.
        /// </summary>
        public static EnforcementResult Enforce(GameState s, PenaltyCall call, PlayResult play = null)
        {
            if (s == null) throw new ArgumentNullException("s");
            if (call == null) throw new ArgumentNullException("call");
            Penalty def = call.Def;
            RuleSet rules = s.Rules;
            var res = new EnforcementResult { Call = call };

            if (s.Phase == GamePhase.Kickoff) return EnforceOnKick(s, call, def, res);
            if (s.Phase == GamePhase.PointAfter) return EnforceOnTry(s, call, def, res);

            bool onOffense = call.CommittedBy == s.Possession;
            int los = s.BallSpot;
            int lineToGain = los + s.Distance;
            int toGoal = 100 - los;
            int yards = def.YardsFor(rules, call.FoulSpotYards);
            int newSpot;
            bool lossOfDown = def.LossOfDown;

            if (def.Type == PenaltyType.DefensivePassInterference && rules.DpiSpotFoul)
            {
                // Pro DPI: ball goes to the spot of the foul, never past the 1.
                yards = Math.Min(yards, Math.Max(0, toGoal - 1));
                newSpot = los + yards;
            }
            else if (def.Type == PenaltyType.IllegalBlockInBack)
            {
                int foul = los + (int)Math.Round(call.FoulSpotYards);
                newSpot = Math.Max(1, foul - yards);
                yards = newSpot - los;
            }
            else if (onOffense)
            {
                int limit = los / 2;
                if (yards > limit) { yards = limit; res.HalfDistance = true; }
                newSpot = los - yards;
                yards = -yards;
            }
            else
            {
                int limit = toGoal / 2;
                if (yards > limit) { yards = limit; res.HalfDistance = true; }
                newSpot = los + yards;
            }

            res.Yards = yards;
            res.NewSpot = newSpot;
            res.LossOfDown = lossOfDown;

            s.BallSpot = newSpot;
            if (!onOffense)
            {
                bool auto = def.AutomaticFirstDown;
                bool reached = newSpot >= lineToGain;
                res.AutomaticFirstDown = auto;
                if (auto || reached)
                {
                    s.SetFirstDown();
                    res.FirstDown = true;
                }
                else
                {
                    s.Distance = lineToGain - newSpot;
                }
            }
            else
            {
                s.Distance = lineToGain - newSpot;
                if (lossOfDown)
                {
                    s.Down++;
                    if (s.Down > 4)
                    {
                        res.TurnoverOnDowns = true;
                        s.TurnoverOnDowns();
                    }
                }
            }
            res.Description = def.Name + " on " + call.CommittedBy + ": " + Math.Abs(res.Yards) + " yards"
                              + (res.HalfDistance ? " (half the distance)" : "")
                              + (res.FirstDown ? ", first down" : "")
                              + (res.LossOfDown ? ", loss of down" : "");
            return res;
        }

        /// <summary>Offsetting fouls: the down is replayed from the same spot.</summary>
        public static EnforcementResult Offset(GameState s, PenaltyCall a, PenaltyCall b)
        {
            return new EnforcementResult
            {
                Call = a, Offsetting = true, NewSpot = s.BallSpot,
                Description = "Offsetting penalties (" + a.Def.Name + " / " + b.Def.Name + "), replay the down"
            };
        }

        private static EnforcementResult EnforceOnKick(GameState s, PenaltyCall call, Penalty def, EnforcementResult res)
        {
            int yards = def.YardsFor(s.Rules, 0f);
            if (call.CommittedBy == s.Possession)
            {
                s.KickSpot = Math.Max(1, s.KickSpot - yards);
                res.Yards = -yards;
            }
            else
            {
                s.KickSpot = Math.Min(60, s.KickSpot + yards);
                res.Yards = yards;
            }
            s.BallSpot = s.KickSpot;
            res.NewSpot = s.KickSpot;
            res.Description = def.Name + " on " + call.CommittedBy + " (kickoff re-kick)";
            return res;
        }

        private static EnforcementResult EnforceOnTry(GameState s, PenaltyCall call, Penalty def, EnforcementResult res)
        {
            int yards = def.YardsFor(s.Rules, 0f);
            bool onOffense = call.CommittedBy == s.Possession;
            int limit = onOffense ? s.BallSpot / 2 : (100 - s.BallSpot) / 2;
            yards = Math.Min(yards, limit);
            s.BallSpot = onOffense ? s.BallSpot - yards : s.BallSpot + yards;
            res.Yards = onOffense ? -yards : yards;
            res.NewSpot = s.BallSpot;
            res.HalfDistance = true;
            res.Description = def.Name + " on " + call.CommittedBy + " (try)";
            return res;
        }

        /// <summary>
        /// Decides whether the offended team should accept the penalty rather than take the play result.
        /// Compares an expected-points style valuation of both resulting states.
        /// </summary>
        public static bool ShouldAccept(GameState before, PlayResult play)
        {
            if (play == null || play.Penalty == null) return false;
            if (play.Type == PlayType.Penalty) return true;
            Team offended = play.Penalty.CommittedBy.Opponent();

            GameState declined = before.Clone();
            declined.ApplyPlayCore(play);
            GameState accepted = before.Clone();
            accepted.ApplyPenaltyCore(play);
            return Value(accepted, offended) > Value(declined, offended) + 1e-6;
        }

        /// <summary>Applies the better option for the offended team and returns whether the penalty was accepted.</summary>
        public static bool ApplyBestOption(GameState s, PlayResult play)
        {
            bool accept = ShouldAccept(s, play);
            play.PenaltyDecision = accept ? PenaltyDecision.Accept : PenaltyDecision.Decline;
            s.Apply(play);
            return accept;
        }

        /// <summary>Heuristic value (in points) of a state for the given team: score margin plus expected points.</summary>
        public static double Value(GameState s, Team team)
        {
            double margin = s.ScoreOf(team) - s.ScoreOf(team.Opponent());
            if (s.Phase == GamePhase.Final) return margin * 50.0;
            double ep = ExpectedPoints(s);
            return margin + (s.Possession == team ? ep : -ep);
        }

        /// <summary>Expected points for the team in possession.</summary>
        public static double ExpectedPoints(GameState s)
        {
            switch (s.Phase)
            {
                case GamePhase.Kickoff: return -0.55;
                case GamePhase.PointAfter: return 0.94;
            }
            double f = Math.Max(0, Math.Min(100, s.BallSpot)) / 100.0;
            double ep = 6.0 * Math.Pow(f, 1.5) - 0.5;
            ep -= 0.12 * (s.Down - 1);
            ep -= 0.03 * Math.Min(s.Distance, 20);
            if (s.Down >= 4) ep -= 0.4;
            return ep;
        }
    }
}
