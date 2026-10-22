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
            PreSnapStances.Refresh(this);
            for (int i = 0; i < 22; i++) Agents[i].ClearHistory(Agents[i].Pos);
            _setup = true;
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
            int wt = player.Bio != null && player.Bio.WeightLb > 100 ? player.Bio.WeightLb : 215;
            int ht = player.Bio != null && player.Bio.HeightIn > 55 ? player.Bio.HeightIn : 72;
            a.Body = new AgentBody(a.R, wt, ht, GamePace.For(spec.Position));
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
                steps++;
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
            Vec2 prevWind = BallPhysics.Wind; float prevScale = BallPhysics.WindScale;
            BallPhysics.Wind = Options != null ? Options.Wind : Vec2.Zero;
            BallPhysics.WindScale = 1f;
            try { StepCore(input); }
            finally { BallPhysics.Wind = prevWind; BallPhysics.WindScale = prevScale; }
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
                    a.Body.Step(Dt, preWant, i == MotionSlot, new Vec2(0f, 1f), 0f);   // v0.6: the man in motion shuffles across the formation square to the line (QA-308 / QA-672)
                    bool inMotion = i == MotionSlot && a.Speed > 0.6f;
                    a.Intent = inMotion ? AgentAction.Run : AgentAction.Stance;
                    a.SetAction(inMotion ? AgentAction.Run : AgentAction.Stance);
                    a.ActionTime += Dt;
                    a.RecordHistory();
                }
                // a man in motion gets a few strides first and is never snapped while coming forward
                bool motionSettled = MotionSlot < 0 || (PreSnapTime >= Options.AutoSnapDelay + 0.8f && Agents[MotionSlot].Vel.Y < 0.4f);   // moving laterally at the snap is legal; coming toward the line is not (and he must have had a few steps)
                if (input.Snap || (Options.AutoSnap && PreSnapTime >= Options.AutoSnapDelay && motionSettled)) DoSnap();
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
            if (Ball.State != BallState.Held) Ball.Step(Dt);
            else UpdateHeldBall();
        }

        // ===================================================================================================
        // Snap and live loop
        // ===================================================================================================

        private void DoSnap()
        {
            // pre-snap penalties
            if (Options.Penalties && TryPreSnapPenalty()) return;
            Phase = PlayPhase.Live;
            Time = 0f;
            Log.Add(0f, SimEventType.Snap, (Kind == PlayKind.Scrimmage || Kind == PlayKind.Kneel || Kind == PlayKind.Spike) ? Snapper.Id : Specials.SnapAgentId, -1, Ball.Pos);
            OffAI.OnSnap();
            DefAI.OnSnap();
            Specials.OnSnap();
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
                if (ag.Move == CarrierMove.Dive && ag.MoveTime > 0f) { ag.PrevTickVel = ag.Body.Vel; continue; }   // the dive is a deliberate lunge: spreading it over ticks left sneaks and goal-line dives short
                Vec2 dv = ag.Body.Vel - ag.PrevTickVel; float m = dv.Length;
                if (m > MaxVelocityStep && Time > Dt * 2f) ag.Body.Vel = ag.PrevTickVel + dv * (MaxVelocityStep / m);
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

            OffAI.Update();
            DefAI.Update();
            Specials.Update();
            ApplyUserControl(input);
            Blocks.Update();
            Moves.Update();
            Tackles.ApplyDragHold();

            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Agents[i];
                Vec2 want = a.Want;
                if (a.State == AgentState.Down) want = Vec2.Zero;
                if (a.CommitUntil > Time)
                {
                    // commit window (v0.7): after a catch the receiver secures the ball before he turns upfield at full speed
                    float cap = a.Body.EffectiveVMax * a.CommitSpeedFrac;
                    float wl = want.Length;
                    if (wl > cap && wl > 0.001f) want = want * (cap / wl);
                }
                a.Body.Step(Dt, want, a.HasFace, a.FaceDir, a.Exertion);
            }
            SeparateBodies();
            ConfineToSurroundings();
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
                        BodyCollision.ResolveAroundFallen(st.Body, ly.Body, 0.75f);
                        continue;
                    }
                    if (a.Engagement != null && a.Engagement == b.Engagement) continue;
                    // the back runs past the quarterback who just handed him the ball
                    if (a.Side == b.Side && Kind == PlayKind.Scrimmage && Time < 1.8f && ((a.HasBall && b.Slot == 0) || (b.HasBall && a.Slot == 0))) continue;   // only while the exchange is happening
                    if (Tackles.InTackleLock(a, b)) continue;
                    if (a.IgnoreAgent == b.Id && Time < a.IgnoreUntil) continue;
                    if (b.IgnoreAgent == a.Id && Time < b.IgnoreUntil) continue;
                    float dx = a.Body.Pos.X - b.Body.Pos.X;
                    float cull = a.Body.Radius + b.Body.Radius + 0.15f;
                    if (dx > cull || dx < -cull) continue;
                    float dy = a.Body.Pos.Y - b.Body.Pos.Y;
                    if (dy > cull || dy < -cull) continue;
                    // teammates ghost through each other only while one is handing off
                    BodyCollision.Resolve(a.Body, b.Body, 0.05f);
                }
            }
        }

        // ===================================================================================================
        // Action resolution for presentation
        // ===================================================================================================

        private void ResolveAction(SimAgent a)
        {
            AgentAction act;
            if (a.State == AgentState.Down) act = AgentAction.Tackled;
            else if (a.Body.FallenTime > 0f) act = AgentAction.Fallen;
            else if (a.Move != CarrierMove.None && a.MoveTime > 0f) act = MoveAction(a.Move);
            else if (a.OverrideLeft > 0f) act = a.Override;
            else if (a.State == AgentState.Engaged) act = a.Engagement != null && a.Engagement.IsShedding ? AgentAction.Shed : AgentAction.Engaged;
            else if (a.Body.StumbleTime > 0f) act = AgentAction.Stumble;
            else if (a.Intent == AgentAction.Block && a.Speed < 2.5f) act = AgentAction.Block;
            else if (Phase == PlayPhase.Dead && a.Speed < 0.5f) act = AgentAction.Idle;
            else
            {
                float sp = a.Speed;
                if (sp < 0.5f) act = a.Intent == AgentAction.Stance ? AgentAction.Stance : AgentAction.Idle;
                else
                {
                    float ang = SimMath.AngleBetween(a.Vel, a.Facing);
                    if (ang > 2.0f) act = AgentAction.Backpedal;
                    else if (ang > 0.85f) act = AgentAction.Shuffle;
                    else if (a.HasBall) act = AgentAction.Carry;
                    else if (a.Intent == AgentAction.Route) act = AgentAction.Route;
                    else if (a.Intent == AgentAction.Block) act = AgentAction.Block;
                    else act = sp > a.Body.VMax * 0.82f ? AgentAction.Sprint : AgentAction.Run;
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

        /// <summary>Picks one agent of a group, more likely the lower his awareness (the careless player draws the flag, not always the same one).</summary>
        private int PickCareless(System.Collections.Generic.List<int> ids)
        {
            if (ids.Count == 0) return -1;
            float total = 0f;
            for (int i = 0; i < ids.Count; i++) total += 1.6f - Agents[ids[i]].R.Awareness / 99f;
            float r = Rng.NextFloat() * total;
            for (int i = 0; i < ids.Count; i++) { r -= 1.6f - Agents[ids[i]].R.Awareness / 99f; if (r <= 0f) return ids[i]; }
            return ids[ids.Count - 1];
        }

        /// <summary>Number of offensive players standing on the line of scrimmage (within 1.1 yd of the ball), the legality test behind IllegalFormation.</summary>
        public int OffensePlayersOnLine()
        {
            int n = 0;
            for (int i = 0; i < 11; i++) if (Agents[i].Pos.Y >= LosY - 1.1f) n++;
            return n;
        }

        private bool TryPreSnapPenalty()
        {
            if (Kind == PlayKind.Kickoff || Kind == PlayKind.Kneel || Kind == PlayKind.Spike || IsExtraPoint) return false;
            bool scrimmage = Kind == PlayKind.Scrimmage;
            // who is actually on the line: the culprits come from the real alignment, not from fixed slot numbers
            var offLine = new System.Collections.Generic.List<int>(); var offLinemen = new System.Collections.Generic.List<int>();
            for (int i = 0; i < 11; i++)
            {
                if (Agents[i].Pos.Y < LosY - 1.1f) continue;
                offLine.Add(i);
                if (PositionGroup.IsOffensiveLine(Agents[i].PlayPos)) offLinemen.Add(i);
            }
            var defLine = new System.Collections.Generic.List<int>();
            for (int i = 11; i < 22; i++) if (Agents[i].Pos.Y - LosY < 2.6f && !(Agents[i].Assign is ReturnerAssignment)) defLine.Add(i);
            if (offLinemen.Count == 0) offLinemen = offLine;
            float awOl = 0.7f, dlAw = 0.7f;
            if (offLinemen.Count > 0) { awOl = 0f; foreach (int i in offLinemen) awOl += Agents[i].R.Awareness; awOl = awOl / offLinemen.Count / 99f; }
            if (defLine.Count > 0) { dlAw = 0f; foreach (int i in defLine) dlAw += Agents[i].R.Awareness; dlAw = dlAw / defLine.Count / 99f; }
            float qbAw = Agents[0].R.Awareness / 99f;
            float u = Rng.NextFloat();
            float acc = 0f;
            // a formation with fewer than seven men on the line is illegal whatever the dice say
            if (scrimmage && OffensePlayersOnLine() < 7)
            {
                var culprits = new System.Collections.Generic.List<int>();
                for (int i = 1; i < 11; i++) if (!offLine.Contains(i) && Agents[i].IsReceiverEligible) culprits.Add(i);
                int cg = PickCareless(culprits);
                var fcall = new PenaltyCall(PenaltyType.IllegalFormation, State.Possession, 0f, Agents[cg >= 0 ? cg : 10].PlayerId);
                LogEvent(SimEventType.Penalty, cg >= 0 ? cg : 10, -1, TackleType.None, (float)PenaltyType.IllegalFormation, "IllegalFormation");
                FinishPreSnap(PlayResult.PenaltyOnly(fcall));
                return true;
            }
            if (offLinemen.Count > 0 && u < (acc += 0.0185f * (1.55f - awOl)))
            {
                int worst = PickCareless(offLinemen);
                var call = new PenaltyCall(PenaltyType.FalseStart, State.Possession, 0f, Agents[worst].PlayerId);
                LogEvent(SimEventType.Penalty, worst, -1, TackleType.None, (float)PenaltyType.FalseStart, "FalseStart");
                FinishPreSnap(PlayResult.PenaltyOnly(call));
                return true;
            }
            if (defLine.Count > 0 && u < (acc += 0.0075f * (1.55f - dlAw)))
            {
                int g = PickCareless(defLine);
                var call = new PenaltyCall(PenaltyType.Offside, State.Possession.Opponent(), 0f, Agents[g].PlayerId);
                LogEvent(SimEventType.Penalty, g, -1, TackleType.None, (float)PenaltyType.Offside, "Offside");
                FinishPreSnap(PlayResult.PenaltyOnly(call));
                return true;
            }
            if (defLine.Count > 0 && u < (acc += 0.0026f * (1.55f - dlAw)))
            {
                int g = PickCareless(defLine);
                var call = new PenaltyCall(PenaltyType.Encroachment, State.Possession.Opponent(), 0f, Agents[g].PlayerId);
                LogEvent(SimEventType.Penalty, g, -1, TackleType.None, (float)PenaltyType.Encroachment, "Encroachment");
                FinishPreSnap(PlayResult.PenaltyOnly(call));
                return true;
            }
            if (scrimmage && u < (acc += 0.0040f * (1.55f - qbAw)))
            {
                var call = new PenaltyCall(PenaltyType.DelayOfGame, State.Possession, 0f, Agents[0].PlayerId);
                LogEvent(SimEventType.Penalty, 0, -1, TackleType.None, (float)PenaltyType.DelayOfGame, "DelayOfGame");
                FinishPreSnap(PlayResult.PenaltyOnly(call));
                return true;
            }
            if (scrimmage && u < (acc += 0.0016f * (1.55f - qbAw)))
            {
                // a late shift: a back or receiver is still moving or lined up off the numbers
                var off = new System.Collections.Generic.List<int>();
                for (int i = 1; i < 11; i++) if (Agents[i].IsReceiverEligible) off.Add(i);
                int g = PickCareless(off); if (g < 0) g = 10;
                var call = new PenaltyCall(PenaltyType.IllegalFormation, State.Possession, 0f, Agents[g].PlayerId);
                LogEvent(SimEventType.Penalty, g, -1, TackleType.None, (float)PenaltyType.IllegalFormation, "IllegalFormation");
                FinishPreSnap(PlayResult.PenaltyOnly(call));
                return true;
            }
            return false;
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
