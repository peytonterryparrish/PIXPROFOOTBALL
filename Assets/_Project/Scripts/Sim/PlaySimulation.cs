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
    }

    /// <summary>
    /// One live play: 22 agents and a ball on a fixed 60 Hz deterministic timestep. Units are yards and seconds.
    /// Field frame: X = lateral (0..53.33, offense's right = +X), Y = downfield in the offense's frame (own goal line 0, opponent goal line 100).
    /// </summary>
    public sealed partial class PlaySimulation
    {
        public const float Dt = 1f / 60f;
        public const float MaxPlaySeconds = 22f;

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
        private bool _pendingValid;

        // ---- outcome facts ----
        internal bool PassThrown, PassCompleted, PassTipped;
        internal int PasserId = -1, TargetId = -1, CatcherId = -1;
        internal float PassCatchY, PassThrowY;
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
            OffAI.Prepare();
            DefAI.Prepare();
            Specials.Prepare();
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
            PendingPenalty = null;
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
            a.Body = new AgentBody(a.R, wt, ht);
            a.Body.Facing = facing;
            a.IsReceiverEligible = side == SimSide.Offense && PositionGroup.IsEligibleReceiver(spec.Position);
            Vec2 pos = new Vec2(0f, 0f);
            a.Body.Pos = new Vec2(0f, 0f);
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
            return new Vec2(SimMath.Clamp(p.X, 0.6f, FieldGeometry.Width - 0.6f), SimMath.Clamp(p.Y, -9f, 109f));
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

        public bool OnOffenseSide(SimAgent a) { return a.Side == SimSide.Offense; }

        /// <summary>True when agent belongs to the team currently in possession.</summary>
        public bool HasPossession(SimAgent a) { return a.Side == BallSide; }

        public float AttackDir(SimSide possessionSide) { return 1f; }

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
            _accum += dt;
            ControlInput inp = userInput;
            while (_accum >= Dt - 1e-6f)
            {
                _accum -= Dt;
                Step(inp);
                inp.JukeLeft = inp.JukeRight = inp.Spin = inp.StiffArm = inp.Truck = inp.Hurdle = inp.Dive = inp.Jump = false;
                inp.Tackle = inp.SwitchPlayer = inp.Snap = inp.Throwaway = false;
                inp.ThrowToSlot = 0;
            }
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
            _pendingInput = input;
            if (Phase == PlayPhase.PreSnap)
            {
                PreSnapTime += Dt;
                for (int i = 0; i < 22; i++)
                {
                    SimAgent a = Agents[i];
                    a.Want = Vec2.Zero; a.HasFace = false; a.Exertion = 0f;
                    Vec2 preWant = i == MotionSlot ? PreSnapWant(i) : Vec2.Zero;
                    a.Body.Step(Dt, preWant, false, Vec2.Zero, 0f);
                    bool inMotion = i == MotionSlot && a.Speed > 0.6f;
                    a.Intent = inMotion ? AgentAction.Run : AgentAction.Stance;
                    a.SetAction(inMotion ? AgentAction.Run : AgentAction.Stance);
                    a.ActionTime += Dt;
                    a.RecordHistory();
                }
                if (input.Snap || (Options.AutoSnap && PreSnapTime >= Options.AutoSnapDelay)) DoSnap();
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
            Log.Add(0f, SimEventType.Snap, Kind == PlayKind.Kickoff ? 0 : (Kind == PlayKind.Punt ? 1 : 4), -1, Ball.Pos);
            OffAI.OnSnap();
            DefAI.OnSnap();
            Specials.OnSnap();
        }

        private void LiveStep(ControlInput input)
        {
            Time += Dt;
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Agents[i];
                a.Want = Vec2.Zero; a.HasFace = false; a.Exertion = 0f;
                a.ActionTime += Dt; a.StateTime += Dt;
                if (a.TackleCooldown > 0f) a.TackleCooldown -= Dt;
                if (a.MoveCooldown > 0f) a.MoveCooldown -= Dt;
                if (a.BeatenTime > 0f) a.BeatenTime -= Dt;
                if (a.JumpTime > 0f) a.JumpTime -= Dt;
                if (a.OverrideLeft > 0f) a.OverrideLeft -= Dt;
            }

            OffAI.Update();
            DefAI.Update();
            Specials.Update();
            ApplyUserControl(input);
            Blocks.Update();
            Moves.Update();

            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Agents[i];
                Vec2 want = a.Want;
                if (a.State == AgentState.Down) want = Vec2.Zero;
                a.Body.Step(Dt, want, a.HasFace, a.FaceDir, a.Exertion);
            }
            SeparateBodies();
            for (int i = 0; i < 22; i++) Agents[i].RecordHistory();

            UpdateBall();
            if (Phase != PlayPhase.Live) return;
            Tackles.Update();
            if (Phase != PlayPhase.Live) return;
            CheckBoundsAndScoring();
            if (Phase != PlayPhase.Live) return;
            CheckFirstDown();

            for (int i = 0; i < 22; i++) ResolveAction(Agents[i]);

            if (Time >= MaxPlaySeconds) ForceEnd();
        }

        private void ForceEnd()
        {
            // pathological play: whistle it dead where the ball is
            Ball.Kill();
            EndPlay(EndReason.TimeLimit);
        }

        private void SeparateBodies()
        {
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Agents[i];
                if (a.Body.FallenTime > 0f && a.State == AgentState.Down) continue;
                for (int j = i + 1; j < 22; j++)
                {
                    SimAgent b = Agents[j];
                    if (b.Body.FallenTime > 0f && b.State == AgentState.Down) continue;
                    if (a.Body.FallenTime > 0f || b.Body.FallenTime > 0f) continue;
                    if (a.Engagement != null && a.Engagement == b.Engagement) continue;
                    // the back runs past the quarterback who just handed him the ball
                    if (a.Side == b.Side && Kind == PlayKind.Scrimmage && ((a.HasBall && b.Slot == 0) || (b.HasBall && a.Slot == 0))) continue;
                    if (Tackles.InTackleLock(a, b)) continue;
                    if (a.IgnoreAgent == b.Id && Time < a.IgnoreUntil) continue;
                    if (b.IgnoreAgent == a.Id && Time < b.IgnoreUntil) continue;
                    float dx = a.Body.Pos.X - b.Body.Pos.X;
                    if (dx > 1.1f || dx < -1.1f) continue;
                    float dy = a.Body.Pos.Y - b.Body.Pos.Y;
                    if (dy > 1.1f || dy < -1.1f) continue;
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
            if (PendingPenalty != null) return;
            Team by = culprit.Side == SimSide.Offense ? State.Possession : State.Possession.Opponent();
            PendingPenalty = new PenaltyCall(type, by, yardsPastLos, culprit.PlayerId);
            PenaltyYardsPast = yardsPastLos;
            LogEvent(SimEventType.Penalty, culprit.Id, -1, TackleType.None, (float)type, type.ToString());
        }

        internal void EndPlay(EndReason reason)
        {
            if (Phase == PlayPhase.Dead) return;
            EndReason = reason;
            Phase = PlayPhase.Dead;
            Result = BuildResult(reason);
            if (PendingPenalty != null && Result.Penalty == null) Result.WithPenalty(PendingPenalty);
            LogEvent(SimEventType.PlayEnd, -1, -1, TackleType.None, Result.Yards, Result.Type.ToString());
            for (int i = 0; i < 22; i++)
            {
                Agents[i].Want = Vec2.Zero;
                if (Agents[i].State == AgentState.Engaged) Blocks.Release(Agents[i]);
            }
        }

        private bool TryPreSnapPenalty()
        {
            if (Kind == PlayKind.Kickoff || Kind == PlayKind.Kneel || Kind == PlayKind.Spike || IsExtraPoint) return false;
            // false start: offensive linemen with low awareness
            float awSum = 0f;
            for (int i = 2; i <= 6; i++) awSum += Agents[i].R.Awareness;
            float awOl = awSum / 5f / 99f;
            float dlAw = 0f;
            for (int i = 0; i < 4; i++) dlAw += Agents[11 + i].R.Awareness;
            dlAw = dlAw / 4f / 99f;
            float qbAw = Agents[0].R.Awareness / 99f;
            float u = Rng.NextFloat();
            float acc = 0f;
            bool scrimmage = Kind == PlayKind.Scrimmage;
            if (u < (acc += 0.0185f * (1.55f - awOl)))
            {
                // the guilty lineman is the one with the worst awareness
                int worst = 2;
                for (int i = 2; i <= 6; i++) if (Agents[i].R.Awareness < Agents[worst].R.Awareness) worst = i;
                var call = new PenaltyCall(PenaltyType.FalseStart, State.Possession, 0f, Agents[worst].PlayerId);
                LogEvent(SimEventType.Penalty, worst, -1, TackleType.None, (float)PenaltyType.FalseStart, "FalseStart");
                FinishPreSnap(PlayResult.PenaltyOnly(call));
                return true;
            }
            if (u < (acc += 0.0075f * (1.55f - dlAw)))
            {
                var call = new PenaltyCall(PenaltyType.Offside, State.Possession.Opponent(), 0f, Agents[11].PlayerId);
                LogEvent(SimEventType.Penalty, 11, -1, TackleType.None, (float)PenaltyType.Offside, "Offside");
                FinishPreSnap(PlayResult.PenaltyOnly(call));
                return true;
            }
            if (u < (acc += 0.0026f * (1.55f - dlAw)))
            {
                var call = new PenaltyCall(PenaltyType.Encroachment, State.Possession.Opponent(), 0f, Agents[12].PlayerId);
                LogEvent(SimEventType.Penalty, 12, -1, TackleType.None, (float)PenaltyType.Encroachment, "Encroachment");
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
                var call = new PenaltyCall(PenaltyType.IllegalFormation, State.Possession, 0f, Agents[10].PlayerId);
                LogEvent(SimEventType.Penalty, 10, -1, TackleType.None, (float)PenaltyType.IllegalFormation, "IllegalFormation");
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
            LogEvent(SimEventType.PlayEnd, -1, -1, TackleType.None, 0f, "PreSnapPenalty");
        }
    }
}
