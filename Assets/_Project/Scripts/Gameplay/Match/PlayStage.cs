using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>A team's playing kit: jersey / pants / trim colours and the helmet shell.</summary>
    public struct UniformSet
    {
        public TeamColors Uniform, Helmet;
        /// <summary>True for the home kit (team colours); the visitors wear white (v0.6 number colours depend on it).</summary>
        public bool Home;
        /// <summary>Team nickname for the chest wordmark and the key of the crest sprite for the helmet decal (v0.6).</summary>
        public string Wordmark, CrestKey;

        /// <summary>Home teams wear their primary colour; visitors wear white with the primary colour as trim.</summary>
        public static UniformSet For(TeamInfo t, bool home)
        {
            var c = t.Colors;
            Color white = new Color(0.78f, 0.79f, 0.82f), pants = new Color(0.74f, 0.75f, 0.78f);
            UniformSet u;
            if (home) u.Uniform = c;
            else u.Uniform = new TeamColors(white, pants, c.Primary);
            u.Helmet = new TeamColors(c.Primary, c.Secondary, c.Tertiary);
            u.Home = home; u.Wordmark = t.Nickname; u.CrestKey = "crest_" + t.Id;
            return u;
        }
    }

    /// <summary>
    /// Spawns, pools and drives the 22 SimAgentViews (S/M/L bodies by position) plus the ball for one live play: converts simulation
    /// state to the stadium with a <see cref="PlayFrame"/> and derives the animation hints (throw distance, catch direction, tackle approach...).
    /// </summary>
    public sealed partial class PlayStage : MonoBehaviour
    {
        readonly Dictionary<int, List<SimAgentView>> _pool = new Dictionary<int, List<SimAgentView>>();     // key = size + 3 * base (v0.7 roster v2: 15 bodies)
        readonly SimAgentView[] _views = new SimAgentView[22];
        readonly int[] _carrySide = new int[22];
        readonly float[] _carrySideAt = new float[22];
        readonly Dictionary<int, TackleInfo> _tackled = new Dictionary<int, TackleInfo>();
        readonly Dictionary<int, int> _tackler = new Dictionary<int, int>();
        readonly List<PlayEvent> _fresh = new List<PlayEvent>();
        struct TackleInfo { public int Tackler; public TackleType Type; public bool Sack; }

        public BallFollower Ball;
        /// <summary>v0.4 contact director: authored tackle takes, gang tackles, ragdoll hit reactions and get-ups.</summary>
        public ContactDirector Contacts;
        /// <summary>v0.6 look: kit dirt, field wear, jersey numbers (null in tests / free roam).</summary>
        public LookDirector Look;
        public PlaySimulation Sim { get; private set; }
        public PlayFrame Frame { get; private set; }
        public SimAgentView[] Views { get { return _views; } }
        /// <summary>Events logged since the previous Present call.</summary>
        public List<PlayEvent> Fresh { get { return _fresh; } }
        public bool LobThrow;
        int _logIdx, _playIndex;
        bool _shotgun;

        public void Init()
        {
            if (Ball == null) Ball = BallFollower.Create(transform);
            if (Contacts == null) Contacts = new ContactDirector(this);
        }

        /// <summary>Takes a pooled view of this body size. v0.6.0: a view that last wore <paramref name="wantId"/> is preferred, so the same player keeps his face / kit options from play to play (no re-dressing hitch with the photoreal bodies).</summary>
        static int PoolKey(PlayerSize size, PixFootball.Art.BodyBase b) { return (int)size + 3 * (int)b; }

        SimAgentView Acquire(PlayerSize size, int wantId = int.MinValue) { return Acquire(size, PixFootball.Art.BodyBase.Std, wantId); }

        SimAgentView Acquire(PlayerSize size, PixFootball.Art.BodyBase baseShape, int wantId)
        {
            List<SimAgentView> st;
            if (_pool.TryGetValue(PoolKey(size, baseShape), out st) && st.Count > 0)
            {
                int pick = st.Count - 1;
                if (wantId != int.MinValue) for (int i = st.Count - 1; i >= 0; i--) if (st[i].IdentityId == wantId) { pick = i; break; }
                var v = st[pick]; st.RemoveAt(pick); v.gameObject.SetActive(true); return v;
            }
            PlayerAvatar.PendingBase = baseShape;
            return SimAgentView.CreateForMatch(transform, size, "Player_" + size + (baseShape == PixFootball.Art.BodyBase.Std ? "" : "_" + baseShape));
        }

        void Return(SimAgentView v)
        {
            if (v == null) return;
            v.gameObject.SetActive(false);
            List<SimAgentView> st;
            int key = PoolKey(v.Size, v.Avatar != null ? v.Avatar.Base : PixFootball.Art.BodyBase.Std);
            if (!_pool.TryGetValue(key, out st)) _pool[key] = st = new List<SimAgentView>();
            st.Add(v);
        }

        /// <summary>Pre-creates a pool so the first snaps do not hitch.</summary>
        public void Warm(int small, int medium, int large)
        {
            Init();
            var made = new List<SimAgentView>();
            for (int i = 0; i < small; i++) made.Add(Acquire(PlayerSize.Small));
            for (int i = 0; i < medium; i++) made.Add(Acquire(PlayerSize.Medium));
            for (int i = 0; i < large; i++) made.Add(Acquire(PlayerSize.Large));
            foreach (var v in made) Return(v);
        }

        public void Begin(PlaySimulation sim, PlayFrame frame, UniformSet offense, UniformSet defense, int playIndex)
        {
            Init();
            bool carryMode = ContinuityEnabled && (_carry.Count > 0 || NextBeginFromBench);      // v0.11.0 SIM-FLOW: bodies of the last play stay on the field
            End();
            if (!carryMode) { ReleaseLeavers(); }
            NextBeginFromBench = false; LastCarried = LastNewcomers = LastLeavers = 0;
            Sim = sim; Frame = frame; _logIdx = 0; _playIndex = playIndex; _fresh.Clear(); _tackled.Clear(); _tackler.Clear();
            OffenseKit = offense; DefenseKit = defense; ClearWalks();
            Contacts.Reset();
            if (Look != null) Look.BeginPlay();
            var qbForm = sim.OffForm != null && sim.OffForm.Slots.Length > 0 ? sim.OffForm.Slots[0] : null;
            _shotgun = qbForm != null && qbForm.Y < -2.5f;
            StanceRules.ShortYardage = sim.Kind == PlayKind.Scrimmage && sim.OffForm != null && (sim.OffForm.Name == "Goal Line" || sim.LosY >= 97f);      // v0.11.0 CRITICAL: goal-line linemen are in a true three-point stance
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = sim.Agents[i];
                PlayerSize bodySize = RoleUtil.SizeOf(a.PlayPos); PixFootball.Art.BodyBase bodyBase = PixFootball.Art.BodyBase.Std;
                if (PixSettings.PhotoPlayers) BodySelect.For(a.PlayPos, a.Player != null && a.Player.Bio != null ? a.Player.Bio.HeightIn : 0, a.Player != null && a.Player.Bio != null ? a.Player.Bio.WeightLb : 0, out bodySize, out bodyBase);
                int ident = a.Player != null ? a.Player.Id : 1000 + i;
                CarryRec cr = default(CarryRec); bool carried = carryMode && TakeCarried(ident, bodySize, bodyBase, out cr);
                var v = carried ? cr.View : Acquire(bodySize, bodyBase, ident);
                v.AgentId = i; v.Role = RoleUtil.Of(a.PlayPos);
                v.Stance = StanceRules.For(a.PlayPos, a.Side, sim.Kind, a.Side == SimSide.Offense ? sim.OffForm.Unit : sim.DefForm.Unit, a.StartPos.X - sim.BallX0, a.StartPos.Y - sim.LosY, _shotgun, a.ReturnerRole, StanceCtx(a));
                var set = a.Side == SimSide.Offense ? offense : defense;
                v.Dress(set.Uniform, set.Helmet);
                v.SetIdentity(a.Player != null ? a.Player.Id : 1000 + i, a.PlayPos);
                if (Look != null) Look.Dress(v, a, set);
                Vector3 p = frame.ToUnity(a.Pos);
                Vector3 f = frame.DirToUnity(a.Facing);
                float startYaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
                if (carryMode)
                {
                    if (carried) { p = cr.Pos; startYaw = cr.Yaw; LastCarried++; TotalCarried++; }
                    else { p = BenchStart(set.Home, p, out startYaw); LastNewcomers++; TotalNewcomers++; }
                }
                v.ResetForPlay(p, startYaw);
                v.name = "P" + i + "_" + a.PlayPos;
                _views[i] = v;
                _carrySide[i] = 1; _carrySideAt[i] = -9f;
            }
            if (carryMode) FinishCarry();
            CarryActive = carryMode;
            ConnectBegin(sim, frame);      // v0.9.0 CONNECT: render interpolation snapshots, huddle break to the line
            Ball.SetVisible(true);
            Present(0.0166f);
        }

        // ------------------------------------------------------------------------------------------ v0.6 coin toss
        readonly List<SimAgentView> _toss = new List<SimAgentView>();

        /// <summary>The coin toss on the field: two captains per team face the referee (at <paramref name="center"/>), the home team on the -X side.</summary>
        public void BeginToss(UniformSet home, UniformSet away, Vector3 center)
        {
            Init(); EndToss();
            for (int k = 0; k < 4; k++)
            {
                bool isHome = k < 2;
                var v = Acquire(PlayerSize.Medium);
                v.AgentId = -1; v.Role = PlayerRole.WR; v.Stance = default(StanceSpec);
                var kit = isHome ? home : away;
                v.Dress(kit.Uniform, kit.Helmet);
                v.SetIdentity(7000 + k * 31 + (isHome ? 0 : 500), Position.QB);
                float side = isHome ? -1f : 1f;
                Vector3 p = center + new Vector3(side * (2.35f + 0.95f * (k & 1)), 0f, (k & 1) == 0 ? -0.3f : 0.4f);
                Vector3 d = center - p; d.y = 0f;
                v.ResetForPlay(p, Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                v.name = "Captain" + k;
                _toss.Add(v);
            }
        }

        public void EndToss() { foreach (var v in _toss) Return(v); _toss.Clear(); }

        public void End()
        {
            if (Contacts != null) Contacts.Reset();      // v0.9.0 CONNECT E-292: before the views go back to the pool (a held take must be released while the views still exist)
            ConnectEnd();
            if (Look != null && Sim != null) Look.EndPlay();
            for (int i = 0; i < 22; i++) { if (_views[i] != null) Return(_views[i]); _views[i] = null; }
            Sim = null;
        }

        public Vector3 WorldPos(int agent) { return Frame.ToUnity(Sim.Agents[agent].Pos); }

        // ------------------------------------------------------------------------------------------ v0.5 broadcast choreography
        /// <summary>The kits of the play on the field (the replay recorder stores them so a replay dresses its players the same way).</summary>
        public UniformSet OffenseKit, DefenseKit;

        struct Walk { public bool Active; public Vector3 Pos, Target, Look; public float Speed; }
        readonly Walk[] _walks = new Walk[22];
        readonly float[] _walkSpeed = new float[22];
        bool _anyWalk;

        /// <summary>After the whistle a player jogs to a spot (celebration huddle around the scorer) and then faces <paramref name="look"/>; the simulation keeps owning everything else.</summary>
        public void WalkTo(int agent, Vector3 target, float speed, Vector3 look)
        {
            if (agent < 0 || agent >= 22 || _views[agent] == null) return;
            var w = _walks[agent];
            if (!w.Active) { w.Pos = _views[agent].transform.position; _walkSpeed[agent] = Sim != null ? Sim.Agents[agent].Speed * PlayFrame.YardsToMeters : 0f; }      // v0.11.0 SIM-FLOW: a runner who is called off the play decelerates from his speed, he does not stop dead
            w.Active = true; w.Target = target; w.Speed = speed; w.Look = look;
            _walks[agent] = w; _anyWalk = true;
        }

        public void ClearWalks() { System.Array.Clear(_walks, 0, _walks.Length); System.Array.Clear(_walkSpeed, 0, _walkSpeed.Length); _anyWalk = false; }

        public bool WalkArrived(int agent)
        {
            if (agent < 0 || agent >= 22 || !_walks[agent].Active) return true;
            Vector3 d = _walks[agent].Target - _walks[agent].Pos; d.y = 0f;
            return d.sqrMagnitude < 0.04f;
        }

        void StepWalk(int i, float dt, ref Vector3 pos, ref Vector3 vel, ref Vector3 face)
        {
            var w = _walks[i];
            Vector3 to = w.Target - w.Pos; to.y = 0f;
            float d = to.magnitude;
            vel = Vector3.zero;
            if (d > 0.18f)
            {
                // v0.9.0 CONNECT E-290: a speed profile (accelerate, ease out over the last 2 m) instead of a straight line at full speed and a dead stop
                float want = Mathf.Min(w.Speed, Mathf.Max(0.7f, d * 1.6f));
                _walkSpeed[i] = Mathf.MoveTowards(_walkSpeed[i], want, 6.5f * Mathf.Max(dt, 0.0001f));
                float step = Mathf.Min(d, _walkSpeed[i] * Mathf.Max(dt, 0.0001f));
                w.Pos += to / d * step;
                vel = to / d * _walkSpeed[i];
                face = to / d;
            }
            else
            {
                Vector3 lk = w.Look - w.Pos; lk.y = 0f;
                if (lk.sqrMagnitude > 0.01f) face = lk.normalized;
            }
            pos = new Vector3(w.Pos.x, pos.y, w.Pos.z);
            _walks[i] = w;
        }

        /// <summary>Plays a one-shot or looping reaction clip over the player (cheer, frustration ...). The contact director flag keeps the match animation logic off him until the next play.</summary>
        public bool PlayReaction(int agent, string clipKey, int seed, bool loop)
        {
            if (agent < 0 || agent >= 22) return false;
            var v = _views[agent];
            if (v == null || v.Rig == null || !v.Rig.Valid) return false;
            var plan = AnimKit.Reaction(clipKey, seed, loop);
            if (!plan.Valid) return false;
            v.ContactDriven = true; v.NoteClip(plan.Clip);
            v.Rig.Play(plan);
            return true;
        }

        void ScanLog()
        {
            var ev = Sim.Log.Events;
            while (_logIdx < ev.Count)
            {
                var e = ev[_logIdx++];
                _fresh.Add(e);
                if (Look != null) Look.OnEvent(e);
                ConnectEvent(e);      // v0.9.0 CONNECT: drops, interceptions, deflections, strips, QB hits, fumbles, recoveries, jams
                if (Contacts != null) Contacts.OnEvent(e, SackFollows(ev, _logIdx, e));      // v0.9.0 CONNECT E-221: the sack is found within the next 3 events, or from the tackle itself
                if ((e.Type == SimEventType.Tackle || e.Type == SimEventType.Sack) && e.AgentA >= 0 && e.AgentB >= 0)
                {
                    TackleInfo ti;
                    bool had = _tackled.TryGetValue(e.AgentB, out ti);
                    if (!had || e.Type == SimEventType.Sack || e.Tackle != TackleType.Drag)
                        _tackled[e.AgentB] = new TackleInfo { Tackler = e.AgentA, Type = e.Tackle == TackleType.None && had ? ti.Type : e.Tackle, Sack = e.Type == SimEventType.Sack || (had && ti.Sack) };
                    _tackler[e.AgentA] = e.AgentB;
                }
            }
        }

        public void Present(float dt)
        {
            if (_side.Count > 0) TickSideline(dt);
            if (_leavers.Count > 0) TickLeavers(dt);      // v0.11.0 SIM-FLOW
            if (Sim == null) return;
            _betweenClock += dt;
            _presentDt = dt;
            _magnetAgent = -1; _magnetEta = 9f;
            for (int mi = 0; mi < 22; mi++) _magEta[mi] = 9f;
            _preExTgt = -1;
            _fresh.Clear();
            ScanLog();
            if (Sim.Phase == PlayPhase.PreSnap && Sim.OffForm != null && Sim.OffForm.Slots.Length > 0) _shotgun = Sim.OffForm.Slots[0].Y < -2.5f;
            bool specials = Sim.Kind != PlayKind.Scrimmage && Sim.Kind != PlayKind.Kneel && Sim.Kind != PlayKind.Spike;
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Sim.Agents[i]; var v = _views[i];
                if (v == null) continue;
                Vector3 pos, vel, face; SampleAgent(i, out pos, out vel, out face);      // v0.9.0 CONNECT E-006: rendered between the last two fixed steps
                if (Sim.Phase == PlayPhase.PreSnap && a.Action == AgentAction.Stance) v.ReplanStance(StanceRules.For(a.PlayPos, a.Side, Sim.Kind, a.Side == SimSide.Offense ? Sim.OffForm.Unit : Sim.DefForm.Unit, a.StartPos.X - Sim.BallX0, a.StartPos.Y - Sim.LosY, _shotgun, a.ReturnerRole, StanceCtx(a)));   // audibles, flips and shifts move players
                AgentAction viewAction = FlowAction(i, a, v);      // v0.7.0: the intended receiver reaches before the ball arrives, the quarterback and the runner meet for the hand-off
                AnimHints h = BuildHints(a, v, viewAction);
                h.EventEta = _flowEta;
                ConnectAgent(i, a, v, ref h, ref pos, ref vel, ref face, ref viewAction, dt);      // v0.9.0 CONNECT: pre-snap glide / huddle, reach and tuck, extra hints
                float lat = 0f;
                if (a.Move != CarrierMove.None)
                {
                    Vector3 md = Frame.DirToUnity(a.MoveDir);
                    lat = Vector3.Dot(Vector3.Cross(Vector3.up, v.Forward), md);
                    if (a.Move == CarrierMove.JukeLeft) lat = -1f; else if (a.Move == CarrierMove.JukeRight) lat = 1f;
                }
                if (_anyWalk && _walks[i].Active) StepWalk(i, dt, ref pos, ref vel, ref face);
                if (_anyWalk && _walks[i].Active && Sim.Phase == PlayPhase.PreSnap) viewAction = vel.sqrMagnitude > 0.25f ? AgentAction.Run : AgentAction.Idle;      // v0.11.0 SIM-FLOW: a walk in the pre-snap (timeout) is a walk, not a stance
                UpdateStanceInput(i, a, v, dt);      // v0.9.0 QBSTANCE
                MotionPlanHook(a, v);      // v0.10.0 MOTION: the planned path for the locomotion matcher
                CarryHook(a, v);      // v0.11.0 RB: the carrier's style from the sim
                v.ApplyMatch(pos, vel, face, viewAction, h, lat, specials, _shotgun, dt);
            }
            ServiceChoreo(dt);      // v0.10.0 CHOREO
            ServiceAftermath();      // v0.11.0 SIM-FLOW
            ServiceScenes();
            TimeoutHuddles();
            if (Contacts != null) Contacts.Tick(dt);
            if (Look != null) Look.Tick(dt);
            ApplyContacts();
            UpdateBall(dt);
        }

        /// <summary>
        /// Contact quality (v0.3): paired tackle and block clips are authored with the two actors a fixed distance apart, while the engine keeps its own spacing.
        /// Over the first 0.15-0.18 s of the clip the pair is pulled to the authored distance (the tackler steps into the carrier, blockers and rushers lock up)
        /// and the tackler / blockers turn to face their partner.
        /// </summary>
        void ApplyContacts()
        {
            foreach (var kv in _tackled)
            {
                int c = kv.Key, t = kv.Value.Tackler;
                if (c < 0 || t < 0 || c >= 22 || t >= 22) continue;
                var vc = _views[c]; var vt = _views[t];
                if (vc == null || vt == null) continue;
                SimAgent ac = Sim.Agents[c], at = Sim.Agents[t];
                if (vc.ContactDriven || vt.ContactDriven) continue;   // an authored pair take places both views itself
                if (ac.Action != AgentAction.Tackled || !IsTackleAction(at.Action)) continue;
                Pull(vc, vt, Mathf.Min(ac.ActionTime, at.ActionTime), 0.8f, 0.18f, false, true);
            }
            ApplyEngagementPulls(_presentDt);      // v0.9.0 CONNECT E-173 / E-174
            QbMeshPull();      // v0.10.0 QB: the hand-off reach
        }

        static void Pull(SimAgentView a, SimAgentView b, float t, float distance, float over, bool turnA, bool turnB)
        {
            Vector3 d = b.transform.position - a.transform.position; d.y = 0f;
            float s = d.magnitude;
            if (s < 0.05f) return;
            Vector3 n = d / s;
            float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / over));
            float delta = Mathf.Clamp((s - distance) * 0.5f, -0.45f, 0.7f);
            float yawAB = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
            a.ApplyContact(n * delta, yawAB, w, turnA);
            b.ApplyContact(-n * delta, yawAB + 180f, w, turnB);
        }

        AnimHints BuildHints(SimAgent a, SimAgentView v, AgentAction act)
        {
            var h = new AnimHints { Seed = a.Id * 7 + _playIndex * 3, Side = 'C', Carrying = a.HasBall, Kind = Sim.Kind };
            Vector3 fwd = v.Forward, right = Vector3.Cross(Vector3.up, fwd);
            AddFlowHints(ref h, a, v);
            AddQbHints(ref h, a, v);      // v0.9.0 QBSTANCE
            switch (act)
            {
                case AgentAction.Throw:
                {
                    h.Pitch = Sim.Ball.State == BallState.Pitch;
                    int tgt = a.ThrowTarget >= 0 ? a.ThrowTarget : Sim.Ball.Intended;
                    if (tgt >= 0 && tgt < 22)
                    {
                        Vector3 d = WorldPos(tgt) - WorldPos(a.Id); d.y = 0f;
                        h.ThrowDistance = d.magnitude / PlayFrame.YardsToMeters;
                        h.ThrowAngle = Vector3.SignedAngle(fwd, d.sqrMagnitude > 1e-4f ? d : fwd, Vector3.up);
                    }
                    h.Lob = LobThrow || a.PendingStyle == ThrowStyle.Lob;
                    break;
                }
                case AgentAction.Catch:
                {
                    Vector3 bv = Frame.VelToUnity(Sim.Ball.Vel.XY);
                    if (bv.sqrMagnitude < 0.25f) bv = fwd;
                    bv.y = 0f; bv.Normalize();
                    h.BallLocalDir = new Vector3(Vector3.Dot(bv, right), 0f, Vector3.Dot(bv, fwd));
                    h.BallHeight = Sim.Ball.Pos.Z * PlayFrame.YardsToMeters;
                    h.Airborne = a.JumpTime > 0f;
                    h.Defender = a.Side == SimSide.Defense;
                    break;
                }
                case AgentAction.Engaged: case AgentAction.Shed:
                    if (a.Engagement != null) { h.Rush = a.Engagement.Move; h.Defender = a.Engagement.Defender == a; }
                    break;
                case AgentAction.Handoff:
                    h.Partner = a.Slot != 0 || a.Side != SimSide.Offense;
                    break;
            }
            // tackles: both actors share the approach geometry
            int carrier = -1, tackler = -1; TackleInfo ti = default(TackleInfo);
            if (a.Action == AgentAction.Tackled && _tackled.TryGetValue(a.Id, out ti)) { carrier = a.Id; tackler = ti.Tackler; }
            else if (IsTackleAction(a.Action) && _tackler.TryGetValue(a.Id, out carrier) && _tackled.TryGetValue(carrier, out ti)) tackler = a.Id;
            if (carrier >= 0 && tackler >= 0)
            {
                Vector3 cp = WorldPos(carrier), tp = WorldPos(tackler);
                var cv = _views[carrier];
                Vector3 cf = cv != null ? cv.Forward : fwd;
                Vector3 to = tp - cp; to.y = 0f;
                float dist = Mathf.Max(0.01f, to.magnitude);
                h.Approach_Back = Vector3.Dot(cf, to / dist) < 0f;
                float lateral = Vector3.Dot(Vector3.Cross(Vector3.up, cf), to / dist);
                h.Side = Mathf.Abs(lateral) < 0.4f ? 'C' : (lateral > 0f ? 'R' : 'L');
                h.Tackle = ti.Type; h.Sack = ti.Sack;
                h.Partner = a.Id == carrier;
            }
            ConnectHints(ref h, a, v, act);      // v0.9.0 CONNECT: pair seeds, block style, catch situation
            ChoreoHints(ref h, a, act);      // v0.10.0 CHOREO: the booked catch
            return h;
        }

        static bool IsTackleAction(AgentAction a)
        {
            return a == AgentAction.TackleWrapUp || a == AgentAction.TackleBigHit || a == AgentAction.TackleShoestring || a == AgentAction.TackleDive || a == AgentAction.TackleGang;
        }

        void UpdateBall(float dt)
        {
            var b = Sim.Ball;
            Vector3 down = Frame.DirToUnity(new Vec2(0f, 1f));
            ConnectBallTrack(b);      // v0.9.0 CONNECT: who caught / received the ball and when
            if (PostWhistleBallGuard(b)) return;      // v0.11.0 RB (B-023): after a tackle or sack the ball stays with the carrier, it is not drawn at the sim's dead-ball position
            if (b.State == BallState.Held && b.Holder >= 0)
            {
                SimAgent h = Sim.Agents[b.Holder]; var v = _views[b.Holder];
                if (v != null)
                {
                    bool qbPass = h.Side == SimSide.Offense && h.Slot == 0 && !Sim.PassIsThrown && Sim.Kind == PlayKind.Scrimmage
                                  && (h.QbState == QbPhase.Set || h.QbState == QbPhase.Windup || h.QbState == QbPhase.Dropback || h.QbState == QbPhase.Scramble || h.QbState == QbPhase.Mesh || h.QbState == QbPhase.Fake);
                    if (ConnectHeld(h, v, qbPass)) return;      // v0.9.0 CONNECT: quarterback / holder / punter holds, catch hold, hand-off exchange (E-124 E-126 E-247 E-253 E-256)
                    if (BallFlowHeld(h, v)) return;      // v0.10.0 BODIES: the ball after the whistle (relaxed hold, spike, official), see PlayStage.BallFlow.cs
                    if (h.Action == AgentAction.Celebrate && !SimAgentView.DebugNoFlow) { Ball.SetHand(v, CelebrateHand(h, v)); return; }      // v0.7.0 (note 29): the ball stays in one hand through the whole celebration
                    if (CarrySideHook(h, v)) return;      // v0.11.0 RB: the sim decides the carry arm
                    // carry arm: away from the nearest defender, with hysteresis
                    if (Time.time - _carrySideAt[h.Id] > 1.0f)
                    {
                        SimAgent near = null; float bd = 64f;
                        for (int i = 0; i < 22; i++)
                        {
                            var o = Sim.Agents[i]; if (o.Side == h.Side || o.IsDown) continue;
                            float d = (o.Pos - h.Pos).Length; if (d < bd) { bd = d; near = o; }
                        }
                        if (near != null)
                        {
                            Vector3 to = WorldPos(near.Id) - WorldPos(h.Id);
                            float lat = Vector3.Dot(Vector3.Cross(Vector3.up, v.Forward), to);
                            _carrySide[h.Id] = lat > 0f ? -1 : 1;
                        }
                        _carrySideAt[h.Id] = Time.time;
                    }
                    Ball.SetCarry(v, _carrySide[h.Id]);
                    if (v.Motion != null) v.Motion.SetProtect(h.Protecting, _carrySide[h.Id]);
                    return;
                }
            }
            Vector3 wp = MagnetBall(QbSnapBall(SampleBallPos(), b), b.State);      // v0.10.0 QB: the snap is drawn into his hands at the catch frame
            //      // v0.9.0 CONNECT E-006 / E-125: interpolated, and drawn into the catcher's hands at the end of its flight
            if (ConnectTeeNow()) { Ball.SetTee(Frame.ToUnity(new Vec2(b.Pos.X, b.Pos.Y)), down); return; }
            Ball.Kind = Sim.Kind;   // GFX-310: spiral punts, tumbling kickoffs / field goals
            // GFX-437: the landing ring for the returner (punts and kickoffs in the air, the 2K8 cue)
            bool ring = b.State == BallState.Kick && (Sim.Kind == PlayKind.Punt || Sim.Kind == PlayKind.Kickoff) && b.Pos.Z > 1f;
            if (ring) { Vec3 at; float tt; if (BallPhysics.PredictAtHeight(b.Pos, b.Vel, BallPhysics.BallRadius, out at, out tt)) Ball.SetLanding(Frame.ToUnity(new Vec2(at.X, at.Y)), true); else ring = false; }
            if (!ring) Ball.SetLanding(Vector3.zero, false);
            Ball.SetFree(wp, Frame.VelToUnity(b.Vel.XY) + Vector3.up * (b.Vel.Z * PlayFrame.YardsToMeters), b.State, b.AtRest, down, dt);
        }
    }
}

