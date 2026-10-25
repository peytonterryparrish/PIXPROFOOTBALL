using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.League;
using Team = PixFootball.Core.Team;

namespace PixFootball.Sim
{
    public enum PlayKind { Scrimmage, Kickoff, Punt, FieldGoal, Kneel, Spike }

    /// <summary>Optional setup knobs.</summary>
    public sealed class PlaySimOptions
    {
        /// <summary>Hash the ball is spotted on (default Middle).</summary>
        public Hash Hash = Hash.Middle;
        /// <summary>Explicit ball X (overrides Hash).</summary>
        public float? BallX;
        /// <summary>Playbook used to look up formations by name (default: DefaultPlaybooks.Create()).</summary>
        public Playbook Playbook;
        /// <summary>Snap automatically after <see cref="AutoSnapDelay"/> seconds (CPU vs CPU / headless).</summary>
        public bool AutoSnap = true;
        public float AutoSnapDelay = 0.6f;
        /// <summary>
        /// v0.9.0 (E-323 / E-366 / E-374): when true an automatic snap follows a cadence instead of the bare <see cref="AutoSnapDelay"/>: the computer offense may send a man in motion, the defense may rotate
        /// or show a blitz, and the ball is snapped 1.4-2.2 s (random per play) after the formation settles (the match flow sets this for CPU and autopilot snaps). Off by default: headless callers and tests keep the old fixed delay.
        /// </summary>
        public bool Cadence;
        /// <summary>Enable pre-snap and in-play penalty rolls (holding, DPI, false start...).</summary>
        public bool Penalties = true;
        /// <summary>Test hook: the quarterback never throws or scrambles (used to measure pass protection in isolation).</summary>
        public bool QbHoldsBall;
        /// <summary>Test hook (contact gallery): every tackle attempt resolves to this outcome.</summary>
        public TackleType? ForceTackle;
        /// <summary>Test hook: defenders only attempt tackles once the carrier is this many yards past the line of scrimmage (clean open-field contact).</summary>
        public float ForceTackleAfter;
        /// <summary>v0.6 weather: 0 = dry ball (the calibrated simulation), up to 1 = soaked. Lowers catch probability a little and raises fumble chances; zero leaves the simulation unchanged.</summary>
        public float BallSlickness;
        /// <summary>Wind acting on the ball in flight, in the simulation frame (X lateral, Y downfield for the offense), yards per second. Default (0,0) = still air; build it with <see cref="SetWind"/>.</summary>
        public Vec2 Wind;
        /// <summary>Game-long fatigue (null = every player starts every play fresh). Pass the same tracker to every play of a game and call <see cref="FatigueTracker.AfterPlay"/> after each one.</summary>
        public FatigueTracker Fatigue;
        /// <summary>v0.7.0 (animation sync): seconds from the start of a quarterback throw (ball at the chest) to the release. 0 = the calibrated default (0.20 to 0.26 s by awareness). The match sets about 0.42 s so the throwing take (about 0.5 s of wind-up) plays at its natural tempo and the release lands on the release frame; a throw off a scramble uses 80 percent of it.</summary>
        public float QbWindupSeconds;
        /// <summary>v0.10.0 CHOREO: the clip library of the sequence planner (see <see cref="Choreographer"/>). Null (the default for headless runs and tests) = no choreography, the simulation is exactly the v0.9.0 one.</summary>
        public IChoreoLibrary ChoreoLibrary = DefaultChoreoLibrary;
        /// <summary>QA: the library every new options object starts with (the audit tools turn the choreography on for runs that build their own options; null in the game, where the match flow sets it).</summary>
        public static IChoreoLibrary DefaultChoreoLibrary;

        /// <summary>v0.8.0: apply the career layer to the players on the field: abilities (<see cref="PixFootball.League.AbilityCatalog"/>), morale rating points and team leadership. A player with no ability and a neutral morale is unchanged, so turning this off only matters for a league that has them. Tests that compare "with" and "without" use it as the control.</summary>
        public bool PlayerModifiers = true;

        /// <summary>Sets the wind from the game's conditions for a play in which the offense (or kicking team) drives toward the given end zone.</summary>
        public PlaySimOptions SetWind(MatchConditions conditions, bool offenseAttacksPositiveZ)
        {
            Wind = conditions != null ? conditions.ToSimFrame(offenseAttacksPositiveZ) : Vec2.Zero;
            return this;
        }
    }

    /// <summary>
    /// One live play: 22 agents and a ball on a fixed 60 Hz deterministic timestep. Units are yards and seconds.
    /// Field frame: X = lateral (0..53.33, offense's right = +X), Y = downfield in the offense's frame (own goal line 0, opponent goal line 100).
    /// </summary>
    public sealed partial class PlaySimulation
    {
        public const float Dt = 1f / 60f;
        public const float MaxPlaySeconds = 22f;
        /// <summary>Longest span of time one Tick call will simulate (a frame hitch is not caught up).</summary>
        public const float MaxTickSeconds = 0.1f;
        /// <summary>Kicks and returns may run longer than a scrimmage play before the whistle.</summary>
        public const float MaxKickPlaySeconds = 40f;
        /// <summary>Largest change of an agent's velocity in one tick (yd/s): 1.25 = 75 yd/s squared.</summary>
        public const float MaxVelocityStep = 1.25f;

        private static Playbook _defaultPlaybook;

        // ---- inputs ----
        public GameState State { get; private set; }
        public RuleSet Rules { get; private set; }
        public Rng Rng { get; private set; }
        public Play OffPlay { get; private set; }
        public Play DefPlay { get; private set; }
        public Formation OffForm { get; private set; }
        public Formation DefForm { get; private set; }
        public PlaySimOptions Options { get; private set; }

        // ---- world ----
        public readonly SimAgent[] Agents = new SimAgent[22];
        public BallSim Ball { get; private set; }
        public PlayEventLog Log { get; private set; }
        public PlayPhase Phase { get; private set; }
        public float Time { get; private set; }
        public float PreSnapTime { get; private set; }
        public PlayKind Kind { get; private set; }
        public bool IsTwoPoint { get; private set; }
        public bool IsExtraPoint { get; private set; }
        public float LosY { get; private set; }
        public float LineToGainY { get; private set; }
        public float BallX0 { get; private set; }
        public int UserAgent { get; private set; }
        public bool FirstDownCrossed { get; private set; }
        /// <summary>True once a forward pass has been released (presentation and user control read it).</summary>
        public bool PassIsThrown { get { return PassThrown; } }

        // ---- outcome ----
        public EndReason EndReason { get; private set; }
        public PlayResult Result { get; private set; }
        public bool IsOver { get { return Phase == PlayPhase.Dead; } }

        /// <summary>Which side currently has the ball (changes on turnovers).</summary>
        public SimSide BallSide { get; internal set; }

        // ---- subsystems ----
        internal OffenseAI OffAI;
        internal DefenseAI DefAI;
        internal BlockingSystem Blocks;
        internal TackleResolver Tackles;
        internal CatchResolver Catches;
        internal CarrierMoves Moves;
        internal SpecialTeamsAI Specials;

        private float _accum;
        private bool _setup;
        private ControlInput _pendingInput;

        // ---- outcome facts ----
        internal bool PassThrown, PassCompleted, PassTipped;
        internal int PasserId = -1, TargetId = -1, CatcherId = -1;
        internal float PassCatchY, PassThrowY, PassReleaseTime = -10f;
        internal bool Turnover, TurnoverInterception, TurnoverFumble;
        internal float TurnoverSpotY;
        internal bool Fumbled;
        internal int FumbleCount;
        internal float FumbleSpotY;
        internal bool OutOfBounds;
        internal float EndY;
        internal int TacklerId = -1, RusherId = -1, SackerId = -1;
        internal bool QbSack;
        internal bool IsSackCandidate;
        internal PenaltyCall PendingPenalty;
        internal PenaltyCall OffsettingPenalty;

        /// <summary>True when a foul by this side can still be flagged: nothing is pending, or the pending foul is the other team's and the offset slot is free.</summary>
        internal bool FoulOpen(SimSide offender)
        {
            if (PendingPenalty == null) return true;
            Team by = offender == SimSide.Offense ? State.Possession : State.Possession.Opponent();
            return OffsettingPenalty == null && by != PendingPenalty.CommittedBy;
        }
        internal float PenaltyYardsPast;
        internal bool Touchdown, SafetyScored;
        internal bool DefensiveTd;
        internal TackleType LastTackleType;
        internal bool QbHasCrossedLos;
        internal float KickDistance, KickLandY, KickReturnStartY;
        internal bool KickGood, KickBlocked, KickTouchback, KickFairCatch, KickOob, KickRecoveredByKicker;
        internal int KickerId = -1, ReturnerId = -1;

        // ===================================================================================================
        // Setup
        // ===================================================================================================

        private static Player DefaultPlayer(Position p)
        {
            var pl = new Player { Position = p, Ratings = Ratings.Uniform(70) };
            pl.Bio.WeightLb = 220; pl.Bio.HeightIn = 73;
            return pl;
        }

        /// <summary>
        /// Places 22 agents from the formations (relative to the ball spot and hash) and prepares the play. Rosters are 11 players each,
        /// indexed by formation slot (offense: 0 QB, 1 RB, 2-6 OL, 7-10 eligibles). The offense is the team in possession (the kicking team on kicks).
        /// </summary>
        public void Setup(GameState state, Play offense, Play defense, Player[] offenseRoster, Player[] defenseRoster,
            RuleSet rules, Rng rng, PlaySimOptions options = null)
        {
            if (state == null) throw new ArgumentNullException("state");
            if (offense == null) throw new ArgumentNullException("offense");
            if (defense == null) throw new ArgumentNullException("defense");
            if (rng == null) throw new ArgumentNullException("rng");
            State = state;
            Rules = rules ?? state.Rules ?? RuleSet.Pro();
            Rng = rng;
            OffPlay = offense;
            DefPlay = defense;
            Options = options ?? new PlaySimOptions();
            if (Options.Playbook == null)
            {
                if (_defaultPlaybook == null) _defaultPlaybook = DefaultPlaybooks.Create();
                Options.Playbook = _defaultPlaybook;
            }
            OffForm = Options.Playbook.FindFormation(offense.FormationName);
            DefForm = Options.Playbook.FindFormation(defense.FormationName);
            if (OffForm == null) throw new ArgumentException("Unknown offensive formation " + offense.FormationName);
            if (DefForm == null) throw new ArgumentException("Unknown defensive formation " + defense.FormationName);

            Log = new PlayEventLog();
            Ball = new BallSim(Rng.Fork());
            Time = 0f; PreSnapTime = 0f; Phase = PlayPhase.PreSnap;
            EndReason = EndReason.None; Result = null;
            BallSide = SimSide.Offense;
            UserAgent = -1;

            DetermineKind();
            ComputePlayContexts(offenseRoster, defenseRoster);
            LosY = state.Phase == GamePhase.Kickoff ? state.KickSpot : state.BallSpot;
            if (IsExtraPoint) LosY = Rules.PatSpot;
            if (IsTwoPoint) LosY = Rules.TwoPointSpot;
            LineToGainY = (state.Phase == GamePhase.Scrimmage) ? Math.Min(100f, state.BallSpot + state.Distance) : 100f;
            BallX0 = Options.BallX.HasValue ? Options.BallX.Value : FieldGeometry.HashX(Options.Hash, Rules);
            FirstDownCrossed = false;

            for (int i = 0; i < 11; i++)
            {
                Player po = offenseRoster != null && i < offenseRoster.Length && offenseRoster[i] != null ? offenseRoster[i] : DefaultPlayer(OffForm.Slots[i].Position);
                Player pd = defenseRoster != null && i < defenseRoster.Length && defenseRoster[i] != null ? defenseRoster[i] : DefaultPlayer(DefForm.Slots[i].Position);
                Agents[i] = MakeAgent(i, SimSide.Offense, i, po, OffForm.Slots[i], offense.Assignments[i], new Vec2(0f, 1f));
                Agents[11 + i] = MakeAgent(11 + i, SimSide.Defense, i, pd, DefForm.Slots[i], defense.Assignments[i], new Vec2(0f, -1f));
            }

            Blocks = new BlockingSystem(this);
            Tackles = new TackleResolver(this);
            Catches = new CatchResolver(this);
            Moves = new CarrierMoves(this);
            OffAI = new OffenseAI(this);
            DefAI = new DefenseAI(this);
            Specials = new SpecialTeamsAI(this);

            ResetOutcomeFacts();
            PlaceBallPreSnap();
            if (Kind == PlayKind.Scrimmage) DefenseAlignment.AlignLineIfEnabled(this);   // v0.7: the line lines up by technique before the blockers pick their men
            OffAI.Prepare();
            DefAI.Prepare();
            DefenseAlignment.Apply(this);
            Specials.Prepare();
            SettlePreSnapBodies();
            PreSnapStances.Refresh(this);
            for (int i = 0; i < 22; i++) Agents[i].ClearHistory(Agents[i].Pos);
            _setup = true;
            for (int i = 0; i < 22; i++) _lastMoving[i] = -99f;
            ApplyLineupMistakes();      // v0.12.0 (RULES): a receiver or lineman lines up wrong (causal, awareness); the legality check at the snap decides
            InitChoreo();      // v0.10.0 CHOREO
            AutoJetMotion();
        }

        private void ResetOutcomeFacts()
        {
            PassThrown = PassCompleted = PassTipped = false;
            PasserId = TargetId = CatcherId = -1;
            Turnover = TurnoverInterception = TurnoverFumble = false;
            Fumbled = false; FumbleCount = 0;
            OutOfBounds = false;
            TacklerId = RusherId = SackerId = -1;
            QbSack = false; IsSackCandidate = false;
            PendingPenalty = null; OffsettingPenalty = null;
            Touchdown = SafetyScored = DefensiveTd = false;
            LastTackleType = TackleType.None;
            QbHasCrossedLos = false;
            KickGood = KickBlocked = KickTouchback = KickFairCatch = KickOob = KickRecoveredByKicker = false;
        }

        private void DetermineKind()
        {
            IsTwoPoint = false; IsExtraPoint = false;
            switch (OffPlay.Category)
            {
                case PlayCategory.Kickoff: case PlayCategory.Onside: Kind = PlayKind.Kickoff; break;
                case PlayCategory.Punt: Kind = PlayKind.Punt; break;
                case PlayCategory.FieldGoal: Kind = PlayKind.FieldGoal; break;
                case PlayCategory.PointAfter: Kind = PlayKind.FieldGoal; IsExtraPoint = true; break;
                case PlayCategory.Kneel: Kind = PlayKind.Kneel; break;
                case PlayCategory.Spike: Kind = PlayKind.Spike; break;
                default: Kind = PlayKind.Scrimmage; break;
            }
            if (State.Phase == GamePhase.PointAfter && Kind == PlayKind.Scrimmage) IsTwoPoint = true;
            if (State.Phase == GamePhase.PointAfter && Kind == PlayKind.FieldGoal) IsExtraPoint = true;
        }

        private SimAgent MakeAgent(int id, SimSide side, int slot, Player player, SlotSpec spec, Assignment assign, Vec2 facing)
        {
            var a = new SimAgent(id, side, slot, player, spec.Position, spec.Label, assign);
            a.R = player.Ratings;
            BakeAbilities(a, player, side, assign);
            int wt = player.Bio != null && player.Bio.WeightLb > 100 ? player.Bio.WeightLb : 215;
            int ht = player.Bio != null && player.Bio.HeightIn > 55 ? player.Bio.HeightIn : 72;
            a.Body = new AgentBody(a.R, wt, ht, GamePace.For(spec.Position));
            if (spec.Position == Position.QB) a.Body.BackpedalFactor += 0.07f;   // v0.9.0: a quarterback drops back at 4.5-5.5 yd/s (a 7-step drop is 9 yd in 2.3 s), faster than a defensive back backpedals
            ApplyStaticBodyAbilities(a);
            if (Options != null && Options.Fatigue != null) a.Body.Energy = Options.Fatigue.EnergyOf(player.Id);
            a.Body.Facing = facing;
            a.IsReceiverEligible = side == SimSide.Offense && PositionGroup.IsEligibleReceiver(spec.Position);
            a.Intent = AgentAction.Stance;
            a.Action = AgentAction.Stance;
            return a;
        }

        /// <summary>Resolves the final alignment (called once the ball spot is known).</summary>
        private void PlaceBallPreSnap()
        {
            for (int i = 0; i < 11; i++)
            {
                SlotSpec so = OffForm.Slots[i];
                SlotSpec sd = DefForm.Slots[i];
                Agents[i].Body.Pos = new Vec2(BallX0 + so.X, LosY + so.Y);
                Agents[11 + i].Body.Pos = new Vec2(BallX0 + sd.X, LosY + sd.Y);
                // keep everyone in bounds
                Agents[i].Body.Pos = ClampInField(Agents[i].Body.Pos);
                Agents[11 + i].Body.Pos = ClampInField(Agents[11 + i].Body.Pos);
                Agents[i].StartPos = Agents[i].Body.Pos;
                Agents[11 + i].StartPos = Agents[11 + i].Body.Pos;
            }
            // kickoff formations: kicking team faces downfield, receiving team faces the kicker
            Ball.Hold(-1, new Vec3(BallX0, LosY, BallPhysics.BallRadius));
            Ball.State = BallState.Dead;
            Ball.Holder = -1;
        }

        private static Vec2 ClampInField(Vec2 p)
        {
            // QA-241: a receiver split wide of a hash mark lines up inside the boundary (2 yd), not on the sideline stripe
            return new Vec2(SimMath.Clamp(p.X, 2.0f, FieldGeometry.Width - 2.0f), SimMath.Clamp(p.Y, -9f, 109f));
        }

        // ===================================================================================================
        // Convenience accessors
        // ===================================================================================================

        public SimAgent Off(int slot) { return Agents[slot]; }
        public SimAgent Def(int slot) { return Agents[11 + slot]; }
        public SimAgent Qb { get { return Agents[0]; } }
        public SimAgent AgentById(int id) { return id >= 0 && id < 22 ? Agents[id] : null; }
        public SimAgent Carrier { get { return Ball.Holder >= 0 ? Agents[Ball.Holder] : null; } }

        public SimAgent FindOffenseByLabel(string label)
        {
            for (int i = 0; i < 11; i++) if (Agents[i].Label == label) return Agents[i];
            return null;
        }

        /// <summary>Absolute field position of a point given relative to the ball at the snap.</summary>
        public Vec2 Rel(Vec2 rel) { return new Vec2(BallX0 + rel.X, LosY + rel.Y); }

        /// <summary>A route point relative to the ball, kept on the field and inside the end zone: a corner route no longer runs 6 yards out of bounds and a go route from the 10 stops at the back line.</summary>
        public Vec2 RouteRel(Vec2 rel)
        {
            float x = SimMath.Clamp(BallX0 + rel.X, 1.4f, FieldGeometry.Width - 1.4f);
            float y = Math.Min(LosY + rel.Y, 107f);
            return new Vec2(x, y);
        }

        public bool OnOffenseSide(SimAgent a) { return a.Side == SimSide.Offense; }

        /// <summary>True when agent belongs to the team currently in possession.</summary>
        public bool HasPossession(SimAgent a) { return a.Side == BallSide; }


        // ===================================================================================================
        // Tick
        // ===================================================================================================

        /// <summary>
        /// Advances the simulation by dt seconds (internally in fixed 1/60 s steps). userAgent is the agent id (0-21) the human controls, or -1.
        /// Pulse inputs (juke, spin, throw...) are consumed on the first fixed step of this call.
        /// </summary>
        public void Tick(float dt, ControlInput userInput, int userControlledAgent)
        {
            if (!_setup) throw new InvalidOperationException("Call Setup first.");
            SetUser(userControlledAgent);
            _accum += Math.Min(dt, MaxTickSeconds);   // a long frame hitch must not fast-forward the play
            ControlInput inp = userInput;
            // v0.7: a button pressed on a frame that is shorter than one fixed step (any display faster than 60 Hz) used to be dropped; pulses now wait for the next step
            if (_hasCarried) { MergePulses(ref inp, _carried); _hasCarried = false; _carried = default(ControlInput); }
            int steps = 0;
            while (_accum >= Dt - 1e-6f)
            {
                _accum -= Dt;
                Step(inp);
                steps++; StepCounter++; if (AfterStep != null) AfterStep(this);      // v0.9.0 CONNECT E-006: render interpolation snapshots every fixed step
                inp.JukeLeft = inp.JukeRight = inp.Spin = inp.StiffArm = inp.Truck = inp.Hurdle = inp.Dive = inp.Jump = false;
                inp.Tackle = inp.SwitchPlayer = inp.Snap = inp.Throwaway = false;
                inp.ThrowToSlot = 0;
                inp.KickPress = false;
            }
            if (steps == 0 && HasPulse(inp)) { _carried = inp; _hasCarried = true; }
        }

        private bool _hasCarried;
        private ControlInput _carried;

        private static bool HasPulse(ControlInput i)
        {
            return i.JukeLeft || i.JukeRight || i.Spin || i.StiffArm || i.Truck || i.Hurdle || i.Dive || i.Jump || i.Tackle || i.SwitchPlayer || i.Snap || i.Throwaway || i.ThrowToSlot != 0 || i.KickPress;
        }

        private static void MergePulses(ref ControlInput into, ControlInput from)
        {
            into.JukeLeft |= from.JukeLeft; into.JukeRight |= from.JukeRight; into.Spin |= from.Spin; into.StiffArm |= from.StiffArm;
            into.Truck |= from.Truck; into.Hurdle |= from.Hurdle; into.Dive |= from.Dive; into.Jump |= from.Jump; into.Tackle |= from.Tackle;
            into.SwitchPlayer |= from.SwitchPlayer; into.Snap |= from.Snap; into.Throwaway |= from.Throwaway;
            if (from.ThrowToSlot != 0 && into.ThrowToSlot == 0) { into.ThrowToSlot = from.ThrowToSlot; into.ThrowStyle = from.ThrowStyle; into.LeadNudge = from.LeadNudge; }
            if (from.KickPress && !into.KickPress) { into.KickPress = true; into.KickPower = from.KickPower; into.KickTiming = from.KickTiming; into.KickAim = from.KickAim; }
        }

        /// <summary>Runs the whole play with no user input until it ends (CPU vs CPU / headless). Returns the result.</summary>
        public PlayResult RunToCompletion()
        {
            if (!_setup) throw new InvalidOperationException("Call Setup first.");
            var none = new ControlInput();
            int guard = (int)((MaxPlaySeconds + 10f) / Dt) + 600;
            while (Phase != PlayPhase.Dead && guard-- > 0) Step(none);
            if (Phase != PlayPhase.Dead) EndPlay(EndReason.TimeLimit);
            return Result;
        }

        private void SetUser(int agent)
        {
            if (agent == UserAgent) return;
            if (UserAgent >= 0 && UserAgent < 22) Agents[UserAgent].ControlledByUser = false;
            UserAgent = (agent >= 0 && agent < 22) ? agent : -1;
            if (UserAgent >= 0) Agents[UserAgent].ControlledByUser = true;
        }

        /// <summary>One fixed 1/60 s step.</summary>
        public void Step(ControlInput input)
        {
            Vec2 prevWind = BallPhysics.Wind; float prevScale = BallPhysics.WindScale; bool prevKick = BallPhysics.KickDrag;
            BallPhysics.KickDrag = Kind == PlayKind.Kickoff || Kind == PlayKind.Punt || Kind == PlayKind.FieldGoal;
            BallPhysics.Wind = Options != null ? Options.Wind : Vec2.Zero;
            BallPhysics.WindScale = 1f;
            try { StepCore(input); }
            finally { BallPhysics.Wind = prevWind; BallPhysics.WindScale = prevScale; BallPhysics.KickDrag = prevKick; }
        }

        /// <summary>The wind acting on this play's ball (simulation frame, yards per second).</summary>
        public Vec2 Wind { get { return Options != null ? Options.Wind : Vec2.Zero; } }

        /// <summary>True while a user-controlled kicker is waiting for the kick button (the HUD runs the kick meter now); <see cref="ControlInput.KickPress"/> with the meter readings strikes the ball.</summary>
        public bool AwaitingUserKick { get { return Specials != null && Specials.AwaitingUserKick; } }

        /// <summary>Absolute simulation time of the planned ball contact of a kick (kickoff, punt, field goal), or -1 when none is planned yet (a user kicker has not pressed). Pairs with <see cref="SimEventType.KickApproach"/>: start the kick clip there so its foot-contact frame lands on this time.</summary>
        public float KickContactTime { get { return Specials != null ? Specials.ContactTime : -1f; } }

        /// <summary>Agent id of the kicker / punter of this kick play (-1 on scrimmage plays).</summary>
        public int KickerSlot { get { return Specials != null && Specials.KickerAgent != null && Kind != PlayKind.Scrimmage ? Specials.KickerAgent.Id : -1; } }

        /// <summary>True once the kicker has struck the ball (kickoff, punt, field goal).</summary>
        public bool KickStruck { get { return Specials != null && Specials.KickAway; } }

        private void StepCore(ControlInput input)
        {
            _pendingInput = input;
            if (Phase == PlayPhase.PreSnap)
            {
                PreSnapTime += Dt;
                for (int i = 0; i < 22; i++)
                {
                    SimAgent a = Agents[i];
                    a.Want = Vec2.Zero; a.HasFace = false; a.Exertion = 0f;
                    Vec2 preWant = i == MotionSlot ? PreSnapWant(i) : (i >= 11 ? DefenseMotionResponse(i) : Vec2.Zero);
                    preWant = RulesWant(i, preWant);   // v0.12.0 (RULES): shift walks, a second man in motion, the early turn upfield
                    bool mover = i == MotionSlot || i == _motion2 || _hasWalk[i];
                    a.Body.Step(Dt, preWant, mover, new Vec2(0f, 1f), 0f);   // v0.6: the man in motion shuffles across the formation square to the line (QA-308 / QA-672)
                    bool inMotion = mover && a.Speed > 0.6f;
                    a.Intent = inMotion ? AgentAction.Run : AgentAction.Stance;
                    a.SetAction(inMotion ? AgentAction.Run : AgentAction.Stance);
                    JumpStep(i);   // v0.12.0 (RULES): a player who jumps the count leaves his stance (the false start / offside is the body that moved)
                    a.ActionTime += Dt;
                    a.RecordHistory();
                }
                if (RulesAfterStep()) return;      // v0.12.0 (RULES): the foul the movement just caused (false start, offside, neutral zone, encroachment, delay of game)
                // a man in motion gets a few strides first and is never snapped while coming forward
                if (Options.AutoSnap && !_countPlanned) PlanCadence();
                float snapAfter = _countPlanned ? _snapAt : Options.AutoSnapDelay;
                bool motionSettled = MotionSlot < 0 || _driftFrom >= 0f || (PreSnapTime >= snapAfter + (Options.Cadence ? 0f : 0.8f) && Agents[MotionSlot].Vel.Y < 0.4f);   // moving laterally at the snap is legal; coming toward the line is not (and he must have had a few steps)
                if (input.Snap || (Options.AutoSnap && PreSnapTime >= snapAfter && motionSettled)) DoSnap();
                return;
            }
            if (Phase == PlayPhase.Dead)
            {
                WindDown();
                return;
            }
            LiveStep(input);
        }

        private void WindDown()
        {
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Agents[i];
                Vec2 want = Vec2.Zero;
                a.Body.Step(Dt, want, false, Vec2.Zero, 0f);
                a.ActionTime += Dt;
                a.StateTime += Dt;
                ResolveAction(a);
            }
            SeparateBodies();
            if (Choreo != null) Choreo.Follow();      // v0.10.0 CHOREO: the takes play out after the whistle too (falls, get-ups, piles)
            if (Ball.State != BallState.Held) Ball.Step(Dt);
            else UpdateHeldBall();
        }

        // ===================================================================================================
        // Snap and live loop
        // ===================================================================================================

        private void DoSnap()
        {
            // pre-snap penalties
            if (Options.Penalties && Kind != PlayKind.Kickoff && Kind != PlayKind.Kneel && Kind != PlayKind.Spike && !IsExtraPoint) Rng.NextFloat();      // v0.12.0 (RULES): the v0.11 snap roll is gone, its draw stays so the play draws the sequence it always drew (calibrations stay comparable)
            if (Options.Penalties && EvaluateSnap()) return;      // v0.12.0 (RULES): computed from the alignment and movement at the snap (no dice)
            Phase = PlayPhase.Live;
            Time = 0f;
            Log.Add(0f, SimEventType.Snap, (Kind == PlayKind.Scrimmage || Kind == PlayKind.Kneel || Kind == PlayKind.Spike) ? Snapper.Id : Specials.SnapAgentId, -1, Ball.Pos);
            OffAI.OnSnap();
            DefAI.OnSnap();
            Specials.OnSnap();
            AssignReactions();
        }

        /// <summary>
        /// v0.9.0 (E-361 / E-367 / E-368): nobody reacts to the snap with the same latency (22 players moved 0.10-0.15 s after the snap). Linemen fire out on the ball in 0.15-0.28 s with a spread,
        /// the pass rush gets its get-off (0.11-0.23 s), receivers and backs go on the ball in 0.10-0.20 s, linebackers read in 0.18-0.30 s, defensive backs react to the quarterback and the
        /// receiver in 0.18-0.35 s. The human-controlled player has no delay.
        /// </summary>
        private void AssignReactions()
        {
            if (Kind != PlayKind.Scrimmage) return;
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Agents[i];
                a.ReactUntil = 0f;
                if (i == 0 || a.ControlledByUser) continue;
                float aw = SimMath.Rating01(a.R.Awareness), acc = SimMath.Rating01(a.R.Acceleration);
                float rnd = Rng.NextFloat();
                float t;
                // (the audit measures the first move as the time the body passes 1 yd/s, which includes about 0.1 s of acceleration)
                if (PositionGroup.IsOffensiveLine(a.PlayPos)) t = 0.08f + (1f - acc) * 0.08f + rnd * 0.06f;
                else if (PositionGroup.IsDefensiveLine(a.PlayPos)) t = 0.06f + (1f - acc) * 0.08f + rnd * 0.05f;
                else if (PositionGroup.IsLinebacker(a.PlayPos)) t = 0.12f + (1f - aw) * 0.10f + rnd * 0.05f;
                else if (PositionGroup.IsDefensiveBack(a.PlayPos)) t = 0.12f + (1f - aw) * 0.12f + rnd * 0.05f;
                else t = 0.04f + (1f - aw) * 0.05f + rnd * 0.05f;
                a.ReactUntil = t;
            }
        }

        private void LiveStep(ControlInput input)
        {
            LiveStepCore(input);
            // contact impulses (hits, juke kicks, collisions, the tackle itself) are spread over a few ticks: at most 1.25 yd/s per tick (75 yd/s squared),
            // also on the tick that ends the play (the early returns of LiveStepCore used to skip the limiter, so a tackle popped both players' velocity)
            for (int i = 0; i < 22; i++)
            {
                SimAgent ag = Agents[i];
                ag.Body.LimitSpeed();
                if (ag.Move == CarrierMove.Dive && ag.MoveTime > 0f) { LimitHeadingChange(ag); ag.PrevTickVel = ag.Body.Vel; continue; }   // the dive is a deliberate lunge: spreading it over ticks left sneaks and goal-line dives short
                Vec2 dv = ag.Body.Vel - ag.PrevTickVel; float m = dv.Length;
                if (m > MaxVelocityStep && Time > Dt * 2f) ag.Body.Vel = ag.PrevTickVel + dv * (MaxVelocityStep / m);
                LimitHeadingChange(ag);
                ag.PrevTickVel = ag.Body.Vel;
            }
        }

        private void LiveStepCore(ControlInput input)
        {
            Time += Dt;
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Agents[i];
                a.Want = Vec2.Zero; a.HasFace = false; a.Exertion = 0f;
                a.ActionTime += Dt; a.StateTime += Dt;
                if (a.State == AgentState.Engaged) a.EngagedSeconds += Dt;
                if (a.TackleCooldown > 0f) a.TackleCooldown -= Dt;
                if (a.MoveCooldown > 0f) a.MoveCooldown -= Dt;
                if (a.BeatenTime > 0f) a.BeatenTime -= Dt;
                if (a.JamTime > 0f) a.JamTime -= Dt;   // (it was never counted down: a won jam froze the receiver at 35% speed for the whole play)
                if (a.JumpTime > 0f) a.JumpTime -= Dt;
                if (a.OverrideLeft > 0f) a.OverrideLeft -= Dt;
            }

            UpdateAbilityBodies();
            OffAI.Update();
            DefAI.Update();
            Specials.Update();
            ApplyUserControl(input);
            Blocks.Update();
            Moves.Update();
            Tackles.ApplyDragHold();
            if (Choreo != null) Choreo.Steer();      // v0.10.0 CHOREO: forecast the interactions of the next second and bend the paths that lead into them

            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Agents[i];
                Vec2 want = a.Want;
                if (a.State == AgentState.Down) want = Vec2.Zero;
                if (a.ReactUntil > Time && !a.ControlledByUser && a.State == AgentState.Normal)
                {
                    // still reacting to the snap: he holds his stance (the first action of the play is the move itself, never an idle stand-up)
                    want = Vec2.Zero; a.HasFace = false; a.Intent = AgentAction.Stance;
                }
                if (a.CommitUntil > Time)
                {
                    // commit window (v0.7): after a catch the receiver secures the ball before he turns upfield at full speed
                    float cap = a.Body.EffectiveVMax * a.CommitSpeedFrac;
                    float wl = want.Length;
                    if (wl > cap && wl > 0.001f) want = want * (cap / wl);
                }
                if (a.ImpactTime > 0f)
                {
                    // v0.9.0 (E-208): a contact impulse is a transfer over its duration (0.22 s), not a one-tick velocity cut
                    float k = Math.Min(1f, Dt / a.ImpactTime);
                    a.Body.Vel = a.Body.Vel + (a.ImpactVel - a.Body.Vel) * k;
                    a.ImpactTime -= Dt;
                }
                a.Body.Step(Dt, want, a.HasFace, a.FaceDir, a.Exertion);
            }
            SeparateBodies();
            ConfineToSurroundings();
            if (Choreo != null) Choreo.Follow();      // v0.10.0 CHOREO: the booked takes move their actors
            for (int i = 0; i < 22; i++) Agents[i].RecordHistory();

            UpdateBall();
            if (Phase != PlayPhase.Live) return;
            Tackles.Update();
            if (Phase != PlayPhase.Live) return;
            if (_endAt < 0f) CheckBoundsAndScoring();   // (a runner who has been wrapped up is down by contact: the sideline and goal line no longer matter)
            if (Phase != PlayPhase.Live) return;
            if (_endAt < 0f) CheckFirstDown();

            for (int i = 0; i < 22; i++) ResolveAction(Agents[i]);

            if (_endAt >= 0f && Time >= _endAt) { EndPlay(_endReasonPending); return; }

            if (Time >= (Kind == PlayKind.Scrimmage ? MaxPlaySeconds : MaxKickPlaySeconds)) ForceEnd();
        }

        /// <summary>v0.9.0 (E-019): a running body does not change the DIRECTION of his velocity faster than MaxHeadingRate (a knock, a collision or a tackle used to flip it by up to 3,476 deg/s in one tick). Magnitude is kept; the direction follows over the next ticks.</summary>
        public const float MaxHeadingRate = 540f * SimMath.Deg2Rad;   // radians per second at running speed (9 deg per tick; the audit target is under 600 deg/s everywhere)

        private void LimitHeadingChange(SimAgent ag)
        {
            if (Time <= Dt * 2f || ag.State == AgentState.Down || ag.Body.FallenTime > 0f) return;
            Vec2 pv = ag.PrevTickVel, nv = ag.Body.Vel;
            float ps = pv.Length, ns = nv.Length;
            if (ps < 2.5f || ns < 1.5f) return;
            float ang = SimMath.AngleBetween(pv, nv);
            float maxA = MaxHeadingRate * Dt;
            if (ang <= maxA) return;
            Vec2 dir = SimMath.RotateToward(pv.Norm(), nv.Norm(), maxA);
            ag.Body.Vel = dir * ns;
        }

        private void ForceEnd()
        {
            // a pathological play is whistled dead: the carrier is ruled down where he stands (no mid-field freeze without a tackle)
            SimAgent c = Carrier;
            if (c != null) { c.State = AgentState.Down; c.Body.Fall(0.9f); }
            Ball.Kill();
            EndPlay(EndReason.TimeLimit);
        }

        private void SeparateBodies()
        {
            // v0.9.0 (E-044 / E-347): up to three relaxation passes so a six-body pile settles instead of jittering; later passes only push (no second impulse)
            for (int pass = 0; pass < 3; pass++)
                if (!SeparateBodiesPass(pass)) break;
        }

        /// <summary>True when the pair is the locked pair of an engagement: the defender and the PRIMARY blocker (the spring owns them). v0.9.0 (E-167): the second blocker of a double team is a real body that collides with both.</summary>
        private static bool IsLockedPair(SimAgent a, SimAgent b)
        {
            BlockEngagement e = a.Engagement != null ? a.Engagement : b.Engagement;
            if (e == null) return false;
            if (a.Engagement != e && a != e.Defender) return false;
            if (b.Engagement != e && b != e.Defender) return false;
            SimAgent p = e.Primary;
            return (a == e.Defender && b == p) || (b == e.Defender && a == p);
        }

        /// <summary>Sim time of the last hand-off or pitch (the exchange window in which the back and the passer may share space).</summary>
        internal float ExchangeTime = -10f;

        private bool SeparateBodiesPass(int pass)
        {
            bool any = false;
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Agents[i];
                if (a.State == AgentState.Down && a.Body.FallenTime <= 0f) continue;
                for (int j = i + 1; j < 22; j++)
                {
                    SimAgent b = Agents[j];
                    if (b.State == AgentState.Down && b.Body.FallenTime <= 0f) continue;
                    if (a.Body.FallenTime > 0f || b.Body.FallenTime > 0f)
                    {
                        // QA-315: a downed player is an obstacle for the standing ones (a lying body is narrower than a standing one)
                        bool aLying = a.Body.FallenTime > 0f, bLying = b.Body.FallenTime > 0f;
                        if (aLying && bLying) continue;
                        SimAgent st = aLying ? b : a, ly = aLying ? a : b;
                        if (st.State == AgentState.Engaged || st.State == AgentState.Down || Tackles.InTackleLock(a, b)) continue;
                        if (BodyCollision.ResolveAroundFallen(st.Body, ly.Body, 0.75f)) any = true;
                        continue;
                    }
                    if (IsLockedPair(a, b))
                    {
                        // v0.9.0 (E-168): the engaged pair is held by the spring, but it may not interpenetrate and then pop apart when the lock ends (up to 0.5 yd in one tick): a deep overlap is eased out at most 0.05 yd a tick
                        Vec2 ld = b.Body.Pos - a.Body.Pos;
                        float lmin = (a.Body.Radius + b.Body.Radius) * 0.86f;
                        float ldist = ld.Length;
                        if (ldist < lmin && ldist > 1e-3f)
                        {
                            Vec2 ln = ld * (1f / ldist);
                            float push = Math.Min(0.05f, (lmin - ldist) * 0.5f);
                            float tot2 = a.Body.Mass + b.Body.Mass;
                            a.Body.Pos = a.Body.Pos - ln * (push * b.Body.Mass / tot2) * 2f;
                            b.Body.Pos = b.Body.Pos + ln * (push * a.Body.Mass / tot2) * 2f;
                            any = true;
                        }
                        continue;
                    }
                    // the back runs past the quarterback who just handed him the ball
                    if (a.Side == b.Side && Kind == PlayKind.Scrimmage && Time < ExchangeTime + 0.8f && ((a.HasBall && b.Slot == 0) || (b.HasBall && a.Slot == 0)))
                    {
                        // only while the exchange is happening, and even then the two bodies may overlap arms, not torsos: position-only separation to 0.82 of the contact distance (no impulse, no stopping the back)
                        Vec2 ed = b.Body.Pos - a.Body.Pos;
                        float emin = (a.Body.Radius + b.Body.Radius) * 0.82f, edist = ed.Length;
                        if (edist < emin && edist > 1e-3f)
                        {
                            Vec2 en = ed * (1f / edist);
                            float ep = Math.Min(0.06f, (emin - edist) * 0.5f);
                            a.Body.Pos = a.Body.Pos - en * ep; b.Body.Pos = b.Body.Pos + en * ep;
                            any = true;
                        }
                        continue;
                    }
                    if (Tackles.InTackleLock(a, b)) continue;
                    if (a.IgnoreAgent == b.Id && Time < a.IgnoreUntil) continue;
                    if (b.IgnoreAgent == a.Id && Time < b.IgnoreUntil) continue;
                    float dx = a.Body.Pos.X - b.Body.Pos.X;
                    float cull = a.Body.Radius + b.Body.Radius + 0.15f;
                    if (dx > cull || dx < -cull) continue;
                    float dy = a.Body.Pos.Y - b.Body.Pos.Y;
                    if (dy > cull || dy < -cull) continue;
                    // teammates ghost through each other only while one is handing off
                    if (BodyCollision.Resolve(a.Body, b.Body, pass == 0 ? 0.05f : 0f)) any = true;
                }
            }
            return any;
        }

        // ===================================================================================================
        // Action resolution for presentation
        // ===================================================================================================

        private void ResolveAction(SimAgent a)
        {
            AgentAction act;
            if (a.State == AgentState.Down) act = AgentAction.Tackled;
            else if (a.Body.FallenTime > 0f) act = AgentAction.Fallen;
            else if (a.Body.GetUpTime > 0f) act = AgentAction.GetUp;   // v0.9.0 (E-275): rising from the turf
            else if (a.Move != CarrierMove.None && a.MoveTime > 0f) act = MoveAction(a.Move);
            else if (a.OverrideLeft > 0f) act = a.Override;
            else if (a.State == AgentState.Engaged) act = a.Engagement != null && a.Engagement.IsShedding ? AgentAction.Shed : AgentAction.Engaged;
            else if (a.Body.StumbleTime > 0f) act = AgentAction.Stumble;
            else if (a.Intent == AgentAction.Block && (a.Speed < 2.5f || a.IsOffensiveLineman || a.IsDefensiveLineman || SimMath.AngleBetween(a.Vel, a.Facing) < 1.75f)) act = AgentAction.Block;   // v0.9.0 (E-171): a blocker stays in his block action while he works (a lineman never shows the corner's shuffle)
            else if (Phase == PlayPhase.Dead && a.Speed < 0.5f) act = AgentAction.Idle;
            else
            {
                // v0.9.0 (E-261 / E-285 / E-322 / E-346): the locomotion label has hysteresis bands (a body is 'moving' above 1.0 yd/s but keeps the label down to 0.6, Backpedal / Shuffle / Sprint switch at
                // two different thresholds depending on where he is coming from) and a 0.2 s minimum between changes of gait, so the view no longer flips its locomotion state 7 times a play
                float sp = a.Speed;
                AgentAction cur = a.Action;
                bool wasMoving = cur == AgentAction.Run || cur == AgentAction.Sprint || cur == AgentAction.Backpedal || cur == AgentAction.Shuffle || cur == AgentAction.Route || cur == AgentAction.Carry || cur == AgentAction.Block;
                float stopBand = wasMoving ? 0.6f : 1.0f;
                if (sp < stopBand) act = a.Intent == AgentAction.Stance ? AgentAction.Stance : AgentAction.Idle;
                else
                {
                    float ang = SimMath.AngleBetween(a.Vel, a.Facing);
                    float tBack = cur == AgentAction.Backpedal ? 1.85f : 2.15f;
                    float tShuf = cur == AgentAction.Shuffle ? 0.70f : 1.00f;
                    if (ang > tBack) act = AgentAction.Backpedal;
                    else if (ang > tShuf) act = AgentAction.Shuffle;
                    else if (a.HasBall) act = AgentAction.Carry;
                    else if (a.Intent == AgentAction.Route) act = AgentAction.Route;
                    else if (a.Intent == AgentAction.Block) act = AgentAction.Block;
                    else act = sp > a.Body.VMax * (cur == AgentAction.Sprint ? 0.78f : 0.86f) ? AgentAction.Sprint : AgentAction.Run;
                    bool curLoco = cur == AgentAction.Run || cur == AgentAction.Sprint || cur == AgentAction.Backpedal || cur == AgentAction.Shuffle || cur == AgentAction.Route || cur == AgentAction.Carry;
                    bool newLoco = act == AgentAction.Run || act == AgentAction.Sprint || act == AgentAction.Backpedal || act == AgentAction.Shuffle || act == AgentAction.Route || act == AgentAction.Carry;
                    if (act != cur && curLoco && newLoco && a.ActionTime < 0.2f && !(cur == AgentAction.Carry && !a.HasBall)) act = cur;
                }
            }
            a.SetAction(act);
        }

        private static AgentAction MoveAction(CarrierMove m)
        {
            switch (m)
            {
                case CarrierMove.JukeLeft: case CarrierMove.JukeRight: return AgentAction.Juke;
                case CarrierMove.Spin: return AgentAction.Spin;
                case CarrierMove.StiffArm: return AgentAction.StiffArm;
                case CarrierMove.Truck: return AgentAction.Truck;
                case CarrierMove.Hurdle: return AgentAction.Hurdle;
                case CarrierMove.Dive: return AgentAction.Dive;
                case CarrierMove.Slide: return AgentAction.Slide;
                default: return AgentAction.Run;
            }
        }

        /// <summary>Plays a one-shot animation action on an agent for a duration.</summary>
        internal void PlayAction(SimAgent a, AgentAction act, float seconds)
        {
            a.Override = act;
            a.OverrideLeft = seconds;
            a.SetAction(act);
        }

        // ===================================================================================================
        // User control
        // ===================================================================================================

        private void ApplyUserControl(ControlInput input)
        {
            if (UserAgent < 0) return;
            if (Kind != PlayKind.Scrimmage && Specials.IsKicker(Agents[UserAgent])) { Specials.UserKick(input); return; }
            SimAgent a = Agents[UserAgent];
            if (input.SwitchPlayer) SwitchUserPlayer();
            a = Agents[UserAgent];
            if (a.State == AgentState.Down || a.Body.FallenTime > 0f) return;
            if (a.State == AgentState.Engaged) { OffAI.UserEngagedInput(a, input); return; }

            bool isQb = a.Side == SimSide.Offense && a.Slot == 0 && Kind == PlayKind.Scrimmage;
            if (isQb && Ball.Holder == a.Id && !PassThrown) { OffAI.UserQbInput(a, input); return; }

            float maxSp = a.Body.EffectiveVMax * (input.Sprint ? 1f : 0.84f);
            if (a.HasBall && input.Protect) { a.Protecting = true; maxSp *= 0.94f; } else a.Protecting = false;
            float mag = Math.Min(1f, input.Move.Length);
            if (mag > 0.08f)
            {
                a.Want = input.Move.Norm() * (maxSp * mag);
                a.Exertion = input.Sprint ? 1f : 0f;
            }
            a.Intent = AgentAction.Run;
            if (a.HasBall) Moves.UserMoves(a, input);
            else if (a.Side != BallSide) DefAI.UserDefenderInput(a, input);
            else if (a.IsReceiverEligible && !a.HasBall) OffAI.UserReceiverInput(a, input);
        }

        private void SwitchUserPlayer()
        {
            // switch to the defender (or receiver) nearest the ball / ball carrier
            SimSide want = UserAgent >= 0 ? Agents[UserAgent].Side : SimSide.Defense;
            Vec2 focus = Ball.Pos.XY;
            if (Ball.Holder >= 0) focus = Agents[Ball.Holder].Pos;
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < 22; i++)
            {
                SimAgent c = Agents[i];
                if (c.Side != want || i == UserAgent || c.State == AgentState.Down || c.Body.FallenTime > 0f) continue;
                float d = SimMath.DistSq(c.Pos, focus);
                // QA-467: the offense switches to a back or receiver, not a lineman or the quarterback who just threw
                if (want == SimSide.Offense && Kind == PlayKind.Scrimmage && !c.IsReceiverEligible && !c.HasBall) d += 10000f;
                if (d < bd) { bd = d; best = i; }
            }
            if (best >= 0) SetUser(best);
        }

        // ===================================================================================================
        // Ending the play and building the PlayResult
        // ===================================================================================================

        internal void LogEvent(SimEventType type, int a, int b, Vec3 pos, TackleType tackle = TackleType.None, float value = 0f, string note = null)
        {
            Log.Add(Time, type, a, b, pos, tackle, value, note);
        }

        /// <summary>As the other overload, recording a velocity (the carrier's speed at a tackle, E-350).</summary>
        internal void LogEventVel(SimEventType type, int a, int b, Vec3 pos, TackleType tackle, float value, Vec2 vel)
        {
            Log.Add(Time, type, a, b, pos, tackle, value, null, vel);
        }

        internal void LogEvent(SimEventType type, int a, int b, TackleType tackle = TackleType.None, float value = 0f, string note = null)
        {
            Vec3 p = Ball.Pos;
            if (a >= 0 && a < 22) p = new Vec3(Agents[a].Pos, 0f);
            Log.Add(Time, type, a, b, p, tackle, value, note);
        }

        /// <summary>Raises a penalty flag; the first flag of the play wins. foulYards = yards past the LOS (negative = behind).</summary>
        internal void Flag(PenaltyType type, SimAgent culprit, float yardsPastLos)
        {
            Team by = culprit.Side == SimSide.Offense ? State.Possession : State.Possession.Opponent();
            if (PendingPenalty != null)
            {
                // a foul by the other team on the same play offsets the first (QA-489); a second foul by the same team is ignored
                if (OffsettingPenalty == null && by != PendingPenalty.CommittedBy)
                {
                    OffsettingPenalty = new PenaltyCall(type, by, yardsPastLos, culprit.PlayerId);
                    LogEvent(SimEventType.Penalty, culprit.Id, -1, TackleType.None, (float)type, type.ToString() + " (offsetting)");
                }
                return;
            }
            PendingPenalty = new PenaltyCall(type, by, yardsPastLos, culprit.PlayerId);
            PenaltyYardsPast = yardsPastLos;
            LogEvent(SimEventType.Penalty, culprit.Id, -1, TackleType.None, (float)type, type.ToString());
        }

        private float _endAt = -1f;
        private EndReason _endReasonPending;
        private float? _ruledSpotY;

        /// <summary>True between the first contact of a tackle and the whistle (the wrap window): the play is decided but not yet over.</summary>
        public bool EndPending { get { return _endAt >= 0f; } }

        /// <summary>
        /// Ends the play <paramref name="delay"/> seconds from now (the tackle's wrap-up). The ball is spotted where the carrier was at first contact;
        /// everyone keeps moving normally until the whistle. A second request while one is pending is ignored.
        /// </summary>
        internal void EndPlayAfter(EndReason reason, float delay)
        {
            if (Phase != PlayPhase.Live || _endAt >= 0f) return;
            SimAgent c = Carrier;
            if (c != null)
            {
                float dirY = c.Side == SimSide.Offense ? 1f : -1f;
                _ruledSpotY = c.Pos.Y + dirY * 0.2f;
            }
            _endReasonPending = reason;
            _endAt = Time + delay;
        }

        internal void EndPlay(EndReason reason)
        {
            if (Phase == PlayPhase.Dead) return;
            EndReason = reason;
            Phase = PlayPhase.Dead;
            Result = BuildResult(reason);
            EndInfo = BuildEndInfo(reason);
            if (PendingPenalty != null && Result.Penalty == null) Result.WithPenalty(PendingPenalty);
            if (PendingPenalty != null) EndInfo.Detail = "FLAG: " + (PendingPenalty.Def != null ? PendingPenalty.Def.Name.ToUpperInvariant() : PendingPenalty.Type.ToString());
            if (OffsettingPenalty != null && PendingPenalty != null) Result.OffsettingPenalty = OffsettingPenalty;
            LogEvent(SimEventType.PlayEnd, -1, -1, TackleType.None, Result.Yards, Result.Type.ToString());
            for (int i = 0; i < 22; i++)
            {
                Agents[i].Want = Vec2.Zero;
                if (Agents[i].State == AgentState.Engaged) Blocks.Release(Agents[i]);
            }
        }


        private void FinishPreSnap(PlayResult r)
        {
            Phase = PlayPhase.Dead;
            EndReason = EndReason.Penalty;
            Result = r;
            string pn = r.Penalty != null ? (r.Penalty.Def != null ? r.Penalty.Def.Name : r.Penalty.Type.ToString()) : "FLAG";
            int culprit = -1;
            for (int i = Log.Events.Count - 1; i >= 0; i--) if (Log.Events[i].Type == SimEventType.Penalty) { culprit = Log.Events[i].AgentA; break; }
            EndInfo = new PlayEndReason { Kind = PlayEndKind.Penalty, Text = "FLAG - " + pn.ToUpperInvariant(), Primary = culprit, PrimaryNumber = Jersey(culprit), Time = Time };
            LogEvent(SimEventType.PlayEnd, -1, -1, TackleType.None, 0f, "PreSnapPenalty");
        }
    }
}
namespace PixFootball.Sim
{
    public sealed partial class PlaySimulation
    {
        /// <summary>
        /// True when another standing player's body lies across the straight line between two agents (a blocker between a lunging tackler
        /// and the ball carrier): dive tackles do not go through people.
        /// </summary>
        internal bool BodyBetween(SimAgent from, SimAgent to)
        {
            Vec2 a = from.Pos, b = to.Pos;
            for (int i = 0; i < 22; i++)
            {
                SimAgent o = Agents[i];
                if (o == from || o == to || o.State == AgentState.Down || o.Body.FallenTime > 0.2f) continue;
                float t;
                Vec2 p = SimMath.ClosestOnSegment(a, b, o.Pos, out t);
                if (t < 0.15f || t > 0.85f) continue;
                if ((p - o.Pos).Length < o.Body.Radius + 0.2f) return true;
            }
            return false;
        }
    }
}
namespace PixFootball.Sim
{
    public sealed partial class PlaySimulation
    {
        /// <summary>Players may run off the field (past the sideline or end line) but stop at the bench area / wall: nobody leaves the stadium chasing a thrown-away ball.</summary>
        private void ConfineToSurroundings()
        {
            const float side = 3.2f, endBack = 4f;
            for (int i = 0; i < 22; i++)
            {
                AgentBody b = Agents[i].Body;
                Vec2 p = b.Pos, v = b.Vel; bool hit = false;
                if (p.X < -side) { p = new Vec2(-side, p.Y); v = new Vec2(Math.Max(0f, v.X), v.Y); hit = true; }
                else if (p.X > FieldGeometry.Width + side) { p = new Vec2(FieldGeometry.Width + side, p.Y); v = new Vec2(Math.Min(0f, v.X), v.Y); hit = true; }
                if (p.Y < -10f - endBack) { p = new Vec2(p.X, -10f - endBack); v = new Vec2(v.X, Math.Max(0f, v.Y)); hit = true; }
                else if (p.Y > 110f + endBack) { p = new Vec2(p.X, 110f + endBack); v = new Vec2(v.X, Math.Min(0f, v.Y)); hit = true; }
                if (hit) { b.Pos = p; b.Vel = v * 0.5f; }
            }
        }
    }
}
namespace PixFootball.Sim
{
    public sealed partial class PlaySimulation
    {
        /// <summary>The offensive player who snaps the ball: the lineman at center (Position.C), falling back to slot 4.</summary>
        public SimAgent Snapper
        {
            get
            {
                for (int i = 0; i < 11; i++) if (Agents[i] != null && Agents[i].PlayPos == Position.C) return Agents[i];
                return Agents[4];
            }
        }
    }
}
