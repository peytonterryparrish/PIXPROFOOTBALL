using System;

namespace PixFootball.Core
{
    public enum Team { Home = 0, Away = 1 }

    public static class TeamExtensions
    {
        public static Team Opponent(this Team t) { return t == Team.Home ? Team.Away : Team.Home; }
    }

    public enum GamePhase
    {
        /// <summary>Kickoff or free kick pending; <see cref="GameState.Possession"/> is the KICKING team.</summary>
        Kickoff,
        /// <summary>Normal scrimmage down.</summary>
        Scrimmage,
        /// <summary>A touchdown was just scored; PAT or two-point try pending (see <see cref="GameState.RequiresTwoPoint"/>).</summary>
        PointAfter,
        /// <summary>Regulation ended tied and overtime has not been started yet (only when AutoStartOvertime is false).</summary>
        Overtime,
        Final
    }

    /// <summary>
    /// Deterministic football game state machine. <see cref="BallSpot"/> is 0..100 yards from the possessing
    /// team's own goal line. Feed it <see cref="PlayResult"/>s via <see cref="Apply"/>.
    /// </summary>
    public sealed class GameState
    {
        public RuleSet Rules;
        public int Quarter = 1;
        public float ClockSeconds;
        public float PlayClock;
        public int Down = 1;
        public int Distance = 10;
        public int BallSpot;
        public Team Possession;
        public int HomeScore, AwayScore;
        public int HomeTimeouts, AwayTimeouts;
        public GamePhase Phase;

        /// <summary>True when the game clock is running at the end of the last play.</summary>
        public bool ClockRunning;
        /// <summary>Game-clock seconds that will burn before the next snap (cancelled by a timeout).</summary>
        public float PendingRunoff;
        public bool RequiresTwoPoint;
        public int KickSpot;
        public bool FreeKick;
        public Team OpeningKicker;
        public bool TwoMinuteWarningFirstHalf, TwoMinuteWarningSecondHalf;
        public bool PeriodExpiredPending;
        public bool UntimedDown;
        public int PlayCount;

        // Overtime
        public bool AutoStartOvertime = true;
        public Team OvertimeReceiverDefault = Team.Home;
        public Team OvertimeFirstTeam;
        /// <summary>Pro OT: 0 = first team's possession, 1 = second team's possession, 2 = sudden death.</summary>
        public int OvertimeStage;
        /// <summary>College OT: 1 or 2 within the current period.</summary>
        public int OvertimePossession;
        public Team OvertimePossessionTeam;

        private bool _warn;

        public GameState(RuleSet rules, Team openingKicker = Team.Away)
        {
            Rules = rules ?? RuleSet.Pro();
            OpeningKicker = openingKicker;
            ClockSeconds = Rules.QuarterSeconds;
            HomeTimeouts = AwayTimeouts = Rules.TimeoutsPerHalf;
            ToKickoff(openingKicker, Rules.KickoffSpot, false);
            PlayClock = Rules.PlayClockDead;
        }

        public GameState Clone() { return (GameState)MemberwiseClone(); }

        // ---------- queries ----------
        public Team Defense { get { return Possession.Opponent(); } }
        public int LineToGain { get { return BallSpot + Distance; } }
        public bool IsGoalToGo { get { return BallSpot + Distance >= 100; } }
        public int YardsToGoal { get { return 100 - BallSpot; } }
        public bool IsOvertime { get { return Quarter > 4; } }
        public int OvertimePeriod { get { return Quarter - 4; } }
        public int Half { get { return Quarter <= 2 ? 1 : (Quarter <= 4 ? 2 : 3); } }
        public bool IsFinal { get { return Phase == GamePhase.Final; } }
        public bool IsCollegeOvertime { get { return IsOvertime && Rules.Overtime == OvertimeFormat.CollegeAlternating; } }
        public int ScoreOf(Team t) { return t == Team.Home ? HomeScore : AwayScore; }
        public int TimeoutsOf(Team t) { return t == Team.Home ? HomeTimeouts : AwayTimeouts; }
        public int PossessionScoreDiff { get { return ScoreOf(Possession) - ScoreOf(Possession.Opponent()); } }

        /// <summary>Winner once final; null for a tie or an unfinished game.</summary>
        public Team? Winner
        {
            get
            {
                if (Phase != GamePhase.Final || HomeScore == AwayScore) return null;
                return HomeScore > AwayScore ? Team.Home : Team.Away;
            }
        }

        public bool InLastTwoMinutesOfHalf
        {
            get { return (Quarter == 2 || Quarter == 4) && ClockSeconds <= Rules.LastMinutesWindowSeconds; }
        }

        public string ClockDisplay
        {
            get
            {
                int s = (int)Math.Ceiling(ClockSeconds);
                return (s / 60) + ":" + (s % 60).ToString("00");
            }
        }

        // ---------- manual actions ----------
        /// <summary>Charge a timeout. Cancels pending runoff and stops the clock.</summary>
        public bool CallTimeout(Team team)
        {
            if (Phase == GamePhase.Final) return false;
            if (TimeoutsOf(team) <= 0) return false;
            if (team == Team.Home) HomeTimeouts--; else AwayTimeouts--;
            StopClock();
            return true;
        }

        /// <summary>
        /// Burns the pending between-play runoff (the director calls this before the snap). Returns true if the
        /// period ended during the runoff, in which case no snap should occur.
        /// </summary>
        public bool RunClockToSnap() { return BurnRunoff(); }

        /// <summary>Starts overtime when <see cref="Phase"/> is Overtime (AutoStartOvertime == false).</summary>
        public void StartOvertime(Team receiver)
        {
            if (Phase != GamePhase.Overtime) throw new InvalidOperationException("Overtime is not pending.");
            Quarter = 5;
            BeginOvertimePeriod(receiver);
        }

        // ---------- the state machine ----------
        /// <summary>Advances the game by one play. Returns false if the play was not applied (game over, or the period ended before the snap).</summary>
        public bool Apply(PlayResult r)
        {
            if (r == null) throw new ArgumentNullException("r");
            if (Phase == GamePhase.Final) return false;
            if (Phase == GamePhase.Overtime) StartOvertime(OvertimeReceiverDefault);
            if (BurnRunoff()) return false;
            if (Phase == GamePhase.Final) return false;

            PlayCount++;
            _warn = false;
            UntimedDown = false;

            bool penaltyEnforced = false;
            if (r.Penalty != null)
            {
                bool accept;
                if (r.Type == PlayType.Penalty) accept = r.PenaltyDecision != PenaltyDecision.Decline;
                else if (r.PenaltyDecision == PenaltyDecision.Accept) accept = true;
                else if (r.PenaltyDecision == PenaltyDecision.Decline) accept = false;
                else accept = PenaltyEnforcer.ShouldAccept(this, r);

                if (accept) { ApplyPenaltyCore(r); penaltyEnforced = true; }
                else if (r.Type == PlayType.Penalty) return true; // declined dead-ball foul: nothing happens
            }
            else if (r.Type == PlayType.Penalty)
            {
                throw new ArgumentException("A Penalty play requires a PenaltyCall.");
            }

            if (!penaltyEnforced) ApplyPlayCore(r);
            if (Phase == GamePhase.Final) StopClock();
            EndPeriodIfNeeded();
            return true;
        }

        internal void ApplyPenaltyCore(PlayResult r)
        {
            SpendClock(r.TimeElapsed);
            bool defenseFoul = r.Penalty.CommittedBy != Possession;
            PenaltyEnforcer.Enforce(this, r.Penalty, r);
            StopClock();
            if (defenseFoul && r.Type != PlayType.Penalty && ClockSeconds <= 0f && !IsCollegeOvertime
                && Phase != GamePhase.Final)
                UntimedDown = true;
        }

        internal void ApplyPlayCore(PlayResult r)
        {
            switch (r.Type)
            {
                case PlayType.Run:
                case PlayType.Pass:
                case PlayType.Sack:
                case PlayType.Kneel:
                    RequirePhase(GamePhase.Scrimmage, r);
                    ApplyScrimmage(r);
                    break;
                case PlayType.Fumble:
                    RequirePhase(GamePhase.Scrimmage, r);
                    if (r.Turnover) ApplyTurnover(r); else ApplyScrimmage(r);
                    break;
                case PlayType.Interception:
                    RequirePhase(GamePhase.Scrimmage, r);
                    ApplyTurnover(r);
                    break;
                case PlayType.Incomplete:
                case PlayType.Spike:
                    RequirePhase(GamePhase.Scrimmage, r);
                    ApplyNoGain(r);
                    break;
                case PlayType.Punt:
                    RequirePhase(GamePhase.Scrimmage, r);
                    ApplyPunt(r);
                    break;
                case PlayType.FieldGoal:
                    RequirePhase(GamePhase.Scrimmage, r);
                    ApplyFieldGoal(r);
                    break;
                case PlayType.PAT:
                    RequirePhase(GamePhase.PointAfter, r);
                    if (RequiresTwoPoint) throw new InvalidOperationException("A two-point try is required.");
                    ApplyTry(r, 1);
                    break;
                case PlayType.TwoPoint:
                    RequirePhase(GamePhase.PointAfter, r);
                    ApplyTry(r, 2);
                    break;
                case PlayType.Kickoff:
                    RequirePhase(GamePhase.Kickoff, r);
                    ApplyKickoff(r);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported play type " + r.Type);
            }
        }

        private void RequirePhase(GamePhase expected, PlayResult r)
        {
            if (Phase != expected)
                throw new InvalidOperationException(r.Type + " cannot be applied during phase " + Phase + " (expected " + expected + ").");
        }

        // ---------- play handlers ----------
        private void ApplyScrimmage(PlayResult r)
        {
            SpendClock(r.TimeElapsed);
            int start = BallSpot;
            int yards = r.Yards;
            if (r.Type == PlayType.Kneel && yards == 0) yards = -1;
            int end = start + yards;

            if (r.Type != PlayType.Kneel && (r.Touchdown || end >= 100)) { ScoreTouchdown(Possession); return; }
            if (end <= 0 || r.Safety) { ScoreSafety(); return; }

            int lineToGain = start + Distance;
            bool first = end >= lineToGain;
            BallSpot = end;
            if (first)
            {
                SetFirstDown();
            }
            else
            {
                Down++;
                Distance = lineToGain - end;
                if (Down > 4) TurnoverOnDowns();
            }
            ClockAfterLivePlay(r.OutOfBounds, first, r.RunoffSeconds, false);
        }

        private void ApplyNoGain(PlayResult r)
        {
            SpendClock(r.TimeElapsed);
            Down++;
            if (Down > 4) TurnoverOnDowns();
            ClockAfterLivePlay(false, false, 0f, true);
        }

        private void ApplyTurnover(PlayResult r)
        {
            SpendClock(r.TimeElapsed);
            Team def = Possession.Opponent();
            if (r.Touchdown) { ScoreTouchdown(def); return; }

            int catchPoint = 100 - (BallSpot + r.Yards);
            if (catchPoint > 99) catchPoint = 99;
            int spot;
            if (r.Touchback || (catchPoint <= 0 && r.ReturnYards <= 0))
            {
                spot = Rules.InterceptionTouchbackSpot;
            }
            else
            {
                spot = Math.Max(catchPoint, 0) + r.ReturnYards;
                if (spot >= 100) { ScoreTouchdown(def); return; }
            }
            ChangePossession(spot);
            NotifyPossessionEnd();
            ClockAfterLivePlay(r.OutOfBounds, false, r.RunoffSeconds, false);
        }

        private void ApplyPunt(PlayResult r)
        {
            SpendClock(r.TimeElapsed);
            Team kicker = Possession;
            Team receiver = kicker.Opponent();
            int land = BallSpot + r.Yards;
            if (r.RecoveredByKickingTeam)
            {
                BallSpot = Clamp(land + r.ReturnYards, 1, 99);
                SetFirstDown();
                ClockAfterLivePlay(false, false, r.RunoffSeconds, false);
                return;
            }
            if (r.Touchdown) { ScoreTouchdown(receiver); return; }

            bool touchback = r.Touchback || land >= 100;
            bool dead = touchback || r.OutOfBounds || r.FairCatch;
            int spot = touchback ? Rules.PuntTouchbackSpot : 100 - land + (dead ? 0 : r.ReturnYards);
            if (spot >= 100) { ScoreTouchdown(receiver); return; }
            ChangePossession(spot);
            NotifyPossessionEnd();
            if (touchback || r.FairCatch) StopThenRestart(Rules.RestartOnReadyRunoff);
            else ClockAfterLivePlay(r.OutOfBounds, false, r.RunoffSeconds, false);
        }

        private void ApplyFieldGoal(PlayResult r)
        {
            SpendClock(r.TimeElapsed);
            Team kicker = Possession;
            if (r.Good)
            {
                AddScore(kicker, 3);
                if (IsOvertime)
                {
                    if (Rules.Overtime == OvertimeFormat.ProModifiedSuddenDeath)
                    {
                        if (ProOvertimeScore(kicker, 3)) return;
                    }
                    else
                    {
                        StopClock();
                        CollegeOvertimeEnd();
                        return;
                    }
                }
                ToKickoff(kicker, Rules.KickoffSpot, false);
                StopClock();
                return;
            }

            Team receiver = kicker.Opponent();
            if (r.Touchdown) { ScoreTouchdown(receiver); return; }
            int kickSpot = Rules.MissedFgFromSpotOfKick ? BallSpot - 7 : BallSpot;
            int spot = Math.Max(Rules.MissedFgMinSpot, 100 - kickSpot) + Math.Max(0, r.ReturnYards);
            ChangePossession(spot);
            NotifyPossessionEnd();
            StopThenRestart(Rules.RestartOnReadyRunoff);
        }

        private void ApplyTry(PlayResult r, int points)
        {
            Team scorer = Possession;
            if (r.Good) AddScore(scorer, points);
            else if (r.DefensiveScore) AddScore(scorer.Opponent(), 2);
            RequiresTwoPoint = false;
            StopClock();
            if (IsCollegeOvertime) { CollegeOvertimeEnd(); return; }
            ToKickoff(scorer, Rules.KickoffSpot, false);
            if (PeriodExpiredPending)
            {
                PeriodExpiredPending = false;
                EndPeriod();
            }
        }

        private void ApplyKickoff(PlayResult r)
        {
            SpendClock(r.TimeElapsed);
            Team kicker = Possession;
            Team receiver = kicker.Opponent();
            bool free = FreeKick;
            int land = KickSpot + r.Yards;
            FreeKick = false;

            if (r.RecoveredByKickingTeam)
            {
                Phase = GamePhase.Scrimmage;
                BallSpot = Clamp(land + r.ReturnYards, 1, 99);
                SetFirstDown();
                ClockAfterLivePlay(false, false, r.RunoffSeconds, false);
                return;
            }
            if (r.Touchdown) { ScoreTouchdown(receiver); return; }

            bool touchback = r.Touchback || land >= 100;
            int spot;
            if (touchback) spot = free ? Rules.PuntTouchbackSpot : Rules.KickoffTouchbackSpot;
            else if (r.OutOfBounds) spot = Rules.KickoffOutOfBoundsSpot;
            else spot = 100 - land + r.ReturnYards;
            if (spot >= 100) { ScoreTouchdown(receiver); return; }

            Possession = receiver;
            Phase = GamePhase.Scrimmage;
            BallSpot = Clamp(spot, 1, 99);
            SetFirstDown();
            if (touchback || r.OutOfBounds) StopThenRestart(Rules.RestartOnReadyRunoff);
            else ClockAfterLivePlay(false, false, r.RunoffSeconds, false);
        }

        // ---------- scoring ----------
        internal void ScoreTouchdown(Team scorer)
        {
            AddScore(scorer, 6);
            Possession = scorer;
            StopClock();
            if (IsOvertime)
            {
                if (Rules.Overtime == OvertimeFormat.ProModifiedSuddenDeath)
                {
                    ProOvertimeScore(scorer, 6);
                    return;
                }
                if (scorer != OvertimePossessionTeam)
                {
                    CollegeOvertimeEnd();
                    return;
                }
                RequiresTwoPoint = OvertimePeriod >= Rules.TwoPointOnlyFromOvertimePeriod;
                Phase = GamePhase.PointAfter;
                BallSpot = RequiresTwoPoint ? Rules.TwoPointSpot : Rules.PatSpot;
                return;
            }
            RequiresTwoPoint = false;
            Phase = GamePhase.PointAfter;
            BallSpot = Rules.PatSpot;
            Down = 1;
            Distance = 100 - BallSpot;
        }

        private void ScoreSafety()
        {
            Team conceding = Possession;
            Team scorer = conceding.Opponent();
            AddScore(scorer, 2);
            StopClock();
            if (IsOvertime)
            {
                if (Rules.Overtime == OvertimeFormat.ProModifiedSuddenDeath)
                {
                    if (ProOvertimeScore(scorer, 2)) return;
                }
                else
                {
                    CollegeOvertimeEnd();
                    return;
                }
            }
            ToKickoff(conceding, Rules.SafetyKickSpot, true);
        }

        private void AddScore(Team t, int pts)
        {
            if (t == Team.Home) HomeScore += pts; else AwayScore += pts;
        }

        // ---------- possession helpers ----------
        internal void SetFirstDown()
        {
            Down = 1;
            Distance = Math.Max(1, Math.Min(10, 100 - BallSpot));
        }

        internal void ChangePossession(int newSpot)
        {
            Possession = Possession.Opponent();
            BallSpot = Clamp(newSpot, 1, 99);
            Phase = GamePhase.Scrimmage;
            SetFirstDown();
        }

        internal void TurnoverOnDowns()
        {
            ChangePossession(100 - BallSpot);
            NotifyPossessionEnd();
        }

        private void ToKickoff(Team kicker, int kickSpot, bool free)
        {
            Phase = GamePhase.Kickoff;
            Possession = kicker;
            KickSpot = kickSpot;
            FreeKick = free;
            BallSpot = kickSpot;
            Down = 1;
            Distance = 10;
            RequiresTwoPoint = false;
        }

        private static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }

        // ---------- clock ----------
        internal void StopClock()
        {
            ClockRunning = false;
            PendingRunoff = 0f;
            PlayClock = Rules.PlayClockDead;
        }

        private void StopThenRestart(float restartRunoff)
        {
            if (_warn) { StopClock(); return; }
            ClockRunning = false;
            PendingRunoff = restartRunoff;
            PlayClock = Rules.PlayClockDead;
        }

        private void RunClock(float runoff)
        {
            if (_warn) { StopClock(); return; }
            ClockRunning = true;
            PendingRunoff = runoff >= 0f ? runoff : Rules.NormalRunoff;
            PlayClock = Rules.PlayClockLive;
        }

        private bool FirstDownStopsClock()
        {
            if (Rules.ClockStopsOnFirstDown) return true;
            return Rules.FirstDownStopWindowSeconds > 0 && (Quarter == 2 || Quarter == 4)
                   && ClockSeconds <= Rules.FirstDownStopWindowSeconds;
        }

        private void ClockAfterLivePlay(bool outOfBounds, bool firstDown, float explicitRunoff, bool incomplete)
        {
            if (Phase == GamePhase.Final) { StopClock(); return; }
            if (_warn || incomplete) { StopClock(); return; }
            if (outOfBounds)
            {
                if (InLastTwoMinutesOfHalf) StopClock(); else StopThenRestart(Rules.RestartOnReadyRunoff);
                return;
            }
            if (firstDown && FirstDownStopsClock()) { StopThenRestart(Rules.RestartOnReadyRunoff); return; }
            RunClock(explicitRunoff);
        }

        private bool WarningGiven { get { return Quarter == 2 ? TwoMinuteWarningFirstHalf : TwoMinuteWarningSecondHalf; } }

        private void GiveWarning()
        {
            if (Quarter == 2) TwoMinuteWarningFirstHalf = true; else TwoMinuteWarningSecondHalf = true;
        }

        private bool WarningApplies { get { return Rules.HasTwoMinuteWarning && (Quarter == 2 || Quarter == 4) && !WarningGiven; } }

        private void SpendClock(float seconds)
        {
            if (seconds <= 0f || IsCollegeOvertime) return;
            float before = ClockSeconds;
            ClockSeconds = Math.Max(0f, before - seconds);
            if (WarningApplies && before > 120f && ClockSeconds <= 120f && ClockSeconds > 0f)
            {
                GiveWarning();
                _warn = true;
            }
        }

        private bool BurnRunoff()
        {
            if (PendingRunoff <= 0f) return false;
            float run = PendingRunoff;
            PendingRunoff = 0f;
            if (IsCollegeOvertime || Phase == GamePhase.Final) return false;
            float before = ClockSeconds;
            if (WarningApplies && before > 120f && before - run <= 120f)
            {
                GiveWarning();
                ClockSeconds = 120f;
                ClockRunning = false;
                PlayClock = Rules.PlayClockDead;
                return false;
            }
            ClockSeconds = Math.Max(0f, before - run);
            if (ClockSeconds <= 0f && !UntimedDown)
            {
                EndPeriod();
                return true;
            }
            return false;
        }

        // ---------- periods ----------
        private void EndPeriodIfNeeded()
        {
            if (Phase == GamePhase.Final || Phase == GamePhase.Overtime) return;
            if (IsCollegeOvertime) return;
            if (ClockSeconds > 0f) return;
            if (UntimedDown) return;
            if (Phase == GamePhase.PointAfter)
            {
                if (Quarter == 4 && !TryCanMatter()) { EndPeriod(); return; }
                PeriodExpiredPending = true;
                return;
            }
            EndPeriod();
        }

        /// <summary>After a TD in the final seconds of Q4: could the try change the result?</summary>
        private bool TryCanMatter()
        {
            int diff = ScoreOf(Possession) - ScoreOf(Possession.Opponent());
            return diff >= -2 && diff <= 0;
        }

        private void EndPeriod()
        {
            PendingRunoff = 0f;
            ClockRunning = false;
            PlayClock = Rules.PlayClockDead;
            UntimedDown = false;
            PeriodExpiredPending = false;

            if (Quarter == 1)
            {
                Quarter = 2;
                ClockSeconds = Rules.QuarterSeconds;
            }
            else if (Quarter == 2)
            {
                Quarter = 3;
                ClockSeconds = Rules.QuarterSeconds;
                HomeTimeouts = AwayTimeouts = Rules.TimeoutsPerHalf;
                ToKickoff(OpeningKicker.Opponent(), Rules.KickoffSpot, false);
            }
            else if (Quarter == 3)
            {
                Quarter = 4;
                ClockSeconds = Rules.QuarterSeconds;
            }
            else if (Quarter == 4)
            {
                ClockSeconds = 0f;
                if (HomeScore != AwayScore || !Rules.OvertimeEnabled) { Finish(); return; }
                Phase = GamePhase.Overtime;
                if (AutoStartOvertime) StartOvertime(OvertimeReceiverDefault);
            }
            else
            {
                // Pro overtime period expired (college OT is not clocked).
                ClockSeconds = 0f;
                if (HomeScore != AwayScore) Finish();
                else if (Rules.AllowTies && Rules.MaxOvertimePeriods > 0 && OvertimePeriod >= Rules.MaxOvertimePeriods) Finish();
                else NextOvertimePeriod();
            }
        }

        private void Finish()
        {
            Phase = GamePhase.Final;
            StopClock();
        }

        // ---------- overtime ----------
        private void BeginOvertimePeriod(Team first)
        {
            OvertimeFirstTeam = first;
            HomeTimeouts = AwayTimeouts = Rules.OvertimeTimeouts;
            PendingRunoff = 0f;
            ClockRunning = false;
            UntimedDown = false;
            PeriodExpiredPending = false;
            FreeKick = false;
            RequiresTwoPoint = false;
            if (Rules.Overtime == OvertimeFormat.ProModifiedSuddenDeath)
            {
                ClockSeconds = Rules.OvertimeMinutes * 60;
                OvertimeStage = 0;
                ToKickoff(first.Opponent(), Rules.KickoffSpot, false);
            }
            else
            {
                ClockSeconds = Rules.QuarterSeconds;
                OvertimePossession = 1;
                BeginCollegeOvertimePossession(first);
            }
            PlayClock = Rules.PlayClockDead;
        }

        private void NextOvertimePeriod()
        {
            Quarter++;
            BeginOvertimePeriod(OvertimeFirstTeam.Opponent());
        }

        private void BeginCollegeOvertimePossession(Team team)
        {
            OvertimePossessionTeam = team;
            Possession = team;
            Phase = GamePhase.Scrimmage;
            BallSpot = 100 - Rules.CollegeOvertimeStartYard;
            RequiresTwoPoint = false;
            SetFirstDown();
            StopClock();
        }

        private void CollegeOvertimeEnd()
        {
            if (OvertimePossession == 1)
            {
                OvertimePossession = 2;
                BeginCollegeOvertimePossession(OvertimeFirstTeam.Opponent());
            }
            else if (HomeScore != AwayScore) Finish();
            else NextOvertimePeriod();
        }

        /// <summary>Called for every non-scoring change of possession (drive ended without points).</summary>
        private void NotifyPossessionEnd()
        {
            if (!IsOvertime || Phase == GamePhase.Final) return;
            if (Rules.Overtime == OvertimeFormat.CollegeAlternating)
            {
                CollegeOvertimeEnd();
                return;
            }
            if (OvertimeStage == 0) OvertimeStage = 1;
            else if (OvertimeStage == 1)
            {
                if (HomeScore == AwayScore) OvertimeStage = 2; else Finish();
            }
        }

        /// <summary>Pro OT score handling. Returns true if the game ended.</summary>
        private bool ProOvertimeScore(Team scorer, int points)
        {
            if (OvertimeStage == 2) { Finish(); return true; }
            if (OvertimeStage == 0)
            {
                if (points >= 6 || scorer != OvertimeFirstTeam) { Finish(); return true; }
                OvertimeStage = 1; // first team kicked a field goal; the other team gets the ball
                return false;
            }
            bool leads = ScoreOf(scorer) > ScoreOf(scorer.Opponent());
            if (leads) { Finish(); return true; }
            if (HomeScore == AwayScore) OvertimeStage = 2;
            return false;
        }
    }
}
