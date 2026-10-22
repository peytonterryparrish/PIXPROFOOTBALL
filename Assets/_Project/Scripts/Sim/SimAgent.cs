using System;
using PixFootball.Core;
using PixFootball.League;

namespace PixFootball.Sim
{
    public enum AgentState
    {
        Normal,
        /// <summary>Locked in a block engagement (either side).</summary>
        Engaged,
        /// <summary>Being tackled / on the ground after the whistle.</summary>
        Down,
        /// <summary>Making or recovering from a tackle attempt (tackler grounded briefly).</summary>
        Tackling
    }

    public enum CarrierMove { None, JukeLeft, JukeRight, Spin, StiffArm, Truck, Hurdle, Dive, Slide }

    public enum QbPhase { PreSnap, WaitSnap, Dropback, Mesh, Fake, Set, Windup, Scramble, Done, Kneel, Spike }

    /// <summary>One of the 22 players on the field (offense 0-10, defense 11-21) plus all per-play AI state.</summary>
    public sealed class SimAgent
    {
        // ---- identity ----
        public int Id;
        public SimSide Side;
        public int Slot;
        public Player Player;
        public Position PlayPos;
        public string Label = "";
        public Assignment Assign;
        public Ratings R;
        public AgentBody Body;
        /// <summary>v0.8.0 abilities of this player (null when he has none). See <see cref="AbilityFx"/>.</summary>
        internal AbilityFx Fx;
        /// <summary>v0.8.0 play / agent context bits for <see cref="Fx"/> (third down, two-minute, short yardage, close game late, return, man coverage, blitz).</summary>
        internal int CtxMask;
        /// <summary>v0.8.0: points added to (or taken from) every rating of this agent by morale and team leadership (0 for a neutral player).</summary>
        public int PlayerModPoints;

        // ---- exposed state for the presentation layer ----
        public AgentAction Action = AgentAction.Stance;
        public float ActionTime;
        public bool HasBall;
        public AgentState State;
        public float StateTime;
        /// <summary>Seconds this agent has spent locked in a block engagement this play (feeds game fatigue).</summary>
        public float EngagedSeconds;
        /// <summary>Velocity at the end of the previous tick (impulses from hits are spread over a few ticks, never a one-frame pop).</summary>
        public Vec2 PrevTickVel;

        // ---- per-tick intent (set by AI, consumed by the body step) ----
        public Vec2 Want;
        public bool HasFace;
        public Vec2 FaceDir;
        public float Exertion;
        /// <summary>Action hint when no special state overrides it.</summary>
        public AgentAction Intent = AgentAction.Idle;

        // ---- generic timers ----
        /// <summary>v0.7 commit window: until this simulation time the agent's movement is capped at <see cref="CommitSpeedFrac"/> of his top speed (a catch is secured before he turns upfield). Animation can rely on the action lasting at least this long.</summary>
        public float CommitUntil, CommitSpeedFrac = 1f;
        public float TackleCooldown;
        public float MoveCooldown;
        public float ReactionClock;
        public bool ControlledByUser;

        // ---- carrier moves ----
        public CarrierMove Move;
        public float MoveTime;
        public float MoveTotal;
        public Vec2 MoveDir;
        public bool MoveResolved;
        /// <summary>The press jam of this corner has been thrown (separate from the carrier-move contest flag).</summary>
        public bool PressResolved;
        /// <summary>Receiver this zone defender is currently sticking with (hysteresis against flip-flopping).</summary>
        public int ZoneTargetId = -1;
        /// <summary>Reaction cache of the ball carrier AI (separate from the receiver and defender clocks).</summary>
        public float CarrierClock;
        public bool Protecting;
        /// <summary>v0.9.0 (E-198): the off-man cushion a corner is currently playing (yards); it follows the receiver vertical speed.</summary>
        public float Cushion;
        /// <summary>v0.9.0 (E-199): side (+1 / -1) the press jam steers this receiver toward while his JamTime runs.</summary>
        public float JamPush;
        /// <summary>v0.9.0 (E-160): which arm carries the ball relative to his heading (-1 = left side, +1 = right side, 0 = front-centre); the AI carrier switches it to the arm away from the nearest defender. Animation can read it.</summary>
        public int BallSide;
        /// <summary>v0.9.0 (E-361): until this simulation time the agent is still reacting to the snap (he holds his stance, then moves): linemen fire off the ball in 0.15-0.25 s, receivers 0.10-0.18 s, defensive backs 0.18-0.30 s.</summary>
        public float ReactUntil;
        /// <summary>v0.9.0 (E-151): the defender a carrier last decided a move against (one decision per threat).</summary>
        public int MoveThreatId = -1;
        /// <summary>v0.9.0 (E-208): a contact impulse in progress (tackle, hit): the velocity the body is being brought to and how long the transfer lasts (seconds).</summary>
        public Vec2 ImpactVel;
        public float ImpactTime;
        /// <summary>v0.9.0 (E-275): seconds of get-up after a fall (the body is on its hands and knees, then rises) during which he cannot sprint or tackle.</summary>
        public float GetUpTime;
        /// <summary>v0.9.0 (E-320): how long the human quarterback has held the stick away from the set point.</summary>
        public float StickHeld;
        /// <summary>v0.9.0 (E-152 / E-239): the speed of the move in progress (the sideways steering speed of a juke or spin, the speed a tackler dived at).</summary>
        public float MoveSpeed;
        public int IgnoreAgent = -1;
        public float IgnoreUntil;
        public float DragMass;
        public float DragTime;
        public int DragTackler = -1;
        public int TacklesMade;

        // ---- route / carry / blocking ----
        public int PathIdx;
        public float RouteClock;
        public bool RouteDone;
        public bool IsReceiverEligible;
        /// <summary>v0.8.0: until this simulation time a receiver who just planted at a sharp break (Crisp Routes) comes out of it with extra burst.</summary>
        public float BreakBurstUntil;
        public int BlockTargetId = -1;
        public BlockEngagement Engagement;
        public float BeatenTime;
        public int BeatenBy = -1;
        public float ShedBurst;
        public int NoEngageId = -1;
        public float NoEngageUntil;
        public float JamTime;
        public AgentAction Override;
        public float OverrideLeft;
        public float PassProRelease = -1f;
        public int PullStage;
        public float PathIdxOpen = 8f;
        public Vec2 StartPos;
        public bool ReturnerRole;
        /// <summary>Pre-snap body position (see <see cref="PreSnapStance"/>); refreshed whenever the alignment changes. <see cref="Action"/> is still <see cref="AgentAction.Stance"/> until the snap.</summary>
        public PreSnapStance StanceKind;
        public QbPhase QbPendingState, PrevQbState;
        public float QbSetAt, QbLastAdvance, MeshPrevDist = -1f, PendingOpen = -1f;

        // ---- QB ----
        public QbPhase QbState;
        public float QbClock;
        public int ReadIdx;
        public float ReadTimer;
        public float WindupLeft;
        public int ThrowTarget = -1;
        public ThrowStyle PendingStyle;
        public Vec2 PendingNudge;

        // ---- defense ----
        public int CoverTarget = -1;
        public Vec2 Landmark;
        public ZoneRegion Zone;
        public float DefReadAt;
        public float CoverReaction;
        public bool BreakOnBall;
        public float BallBreakAt;
        public float JumpTime;
        public bool PursuitMode;
        public float HipDelay;
        public Vec2 LastTargetDir;
        public Vec2 HeldWant;
        public float FrozenUntil;

        // ---- reaction history (ring buffer of recent positions) ----
        private readonly Vec2[] _hist = new Vec2[40];
        private int _histHead;
        private int _histCount;

        public SimAgent(int id, SimSide side, int slot, Player player, Position playPos, string label, Assignment assign)
        {
            Id = id; Side = side; Slot = slot; Player = player; PlayPos = playPos; Label = label ?? ""; Assign = assign;
        }

        /// <summary>Ability effect for a hook in this agent's play contexts plus the dynamic ones passed in (0 for a player without abilities).</summary>
        internal float Fxv(SimHook hook, int dynMask = 0) { return Fx == null ? 0f : Fx.Sum(hook, CtxMask | dynMask); }
        /// <summary>Only the contextual part of an ability effect that applies right now (what a dynamic context adds on top of the baked ratings).</summary>
        internal float FxOnly(SimHook hook, int dynMask) { return Fx == null ? 0f : Fx.SumOnly(hook, dynMask); }

        public Vec2 Pos { get { return Body.Pos; } }
        public Vec2 Vel { get { return Body.Vel; } }
        public Vec2 Facing { get { return Body.Facing; } }
        public float Speed { get { return Body.Vel.Length; } }
        public int PlayerId { get { return Player != null ? Player.Id : 0; } }
        public bool IsDown { get { return State == AgentState.Down || Body.FallenTime > 0f; } }
        public bool IsOffenseSlot { get { return Side == SimSide.Offense; } }

        public void SetAction(AgentAction a)
        {
            if (Action != a) { Action = a; ActionTime = 0f; }
        }

        public void RecordHistory()
        {
            _hist[_histHead] = Body.Pos;
            _histHead = (_histHead + 1) % _hist.Length;
            if (_histCount < _hist.Length) _histCount++;
        }

        public void ClearHistory(Vec2 p)
        {
            for (int i = 0; i < _hist.Length; i++) _hist[i] = p;
            _histCount = _hist.Length;
        }

        /// <summary>Position about <paramref name="secondsAgo"/> ago (60 Hz samples), clamped to the oldest sample.</summary>
        public Vec2 PosAgo(float secondsAgo)
        {
            int ticks = (int)(secondsAgo * 60f + 0.5f);
            if (ticks <= 0 || _histCount < 2) return Body.Pos;
            if (ticks > _histCount - 1) ticks = _histCount - 1;
            // the newest sample is the current tick (history is recorded after the body step), so N ticks ago is head - 1 - N
            int idx = ((_histHead - 1 - ticks) % _hist.Length + _hist.Length) % _hist.Length;
            return _hist[idx];
        }

        public bool IsDefensiveLineman { get { return PositionGroup.IsDefensiveLine(PlayPos); } }
        public bool IsOffensiveLineman { get { return PositionGroup.IsOffensiveLine(PlayPos); } }

        public override string ToString()
        {
            return (Side == SimSide.Offense ? "O" : "D") + Slot + ":" + PlayPos + (Label.Length > 0 ? "/" + Label : "");
        }
    }
}
