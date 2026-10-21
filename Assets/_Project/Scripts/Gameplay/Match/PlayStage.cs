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

        /// <summary>Home teams wear their primary colour; visitors wear white with the primary colour as trim.</summary>
        public static UniformSet For(TeamInfo t, bool home)
        {
            var c = t.Colors;
            Color white = new Color(0.78f, 0.79f, 0.82f), pants = new Color(0.74f, 0.75f, 0.78f);
            UniformSet u;
            if (home) u.Uniform = c;
            else u.Uniform = new TeamColors(white, pants, c.Primary);
            u.Helmet = new TeamColors(c.Primary, c.Secondary, c.Tertiary);
            return u;
        }
    }

    /// <summary>
    /// Spawns, pools and drives the 22 SimAgentViews (S/M/L bodies by position) plus the ball for one live play: converts simulation
    /// state to the stadium with a <see cref="PlayFrame"/> and derives the animation hints (throw distance, catch direction, tackle approach...).
    /// </summary>
    public sealed class PlayStage : MonoBehaviour
    {
        readonly Dictionary<PlayerSize, Stack<SimAgentView>> _pool = new Dictionary<PlayerSize, Stack<SimAgentView>>();
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

        SimAgentView Acquire(PlayerSize size)
        {
            Stack<SimAgentView> st;
            if (_pool.TryGetValue(size, out st) && st.Count > 0) { var v = st.Pop(); v.gameObject.SetActive(true); return v; }
            return SimAgentView.CreateForMatch(transform, size, "Player_" + size);
        }

        void Return(SimAgentView v)
        {
            if (v == null) return;
            v.gameObject.SetActive(false);
            Stack<SimAgentView> st;
            if (!_pool.TryGetValue(v.Size, out st)) _pool[v.Size] = st = new Stack<SimAgentView>();
            st.Push(v);
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
            End();
            Sim = sim; Frame = frame; _logIdx = 0; _playIndex = playIndex; _fresh.Clear(); _tackled.Clear(); _tackler.Clear();
            Contacts.Reset();
            var qbForm = sim.OffForm != null && sim.OffForm.Slots.Length > 0 ? sim.OffForm.Slots[0] : null;
            _shotgun = qbForm != null && qbForm.Y < -2.5f;
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = sim.Agents[i];
                var v = Acquire(RoleUtil.SizeOf(a.PlayPos));
                v.AgentId = i; v.Role = RoleUtil.Of(a.PlayPos);
                var set = a.Side == SimSide.Offense ? offense : defense;
                v.Dress(set.Uniform, set.Helmet);
                Vector3 p = frame.ToUnity(a.Pos);
                Vector3 f = frame.DirToUnity(a.Facing);
                v.ResetForPlay(p, Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg);
                v.name = "P" + i + "_" + a.PlayPos;
                _views[i] = v;
                _carrySide[i] = 1; _carrySideAt[i] = -9f;
            }
            Ball.SetVisible(true);
            Present(0.0166f);
        }

        public void End()
        {
            for (int i = 0; i < 22; i++) { if (_views[i] != null) Return(_views[i]); _views[i] = null; }
            Sim = null;
        }

        public Vector3 WorldPos(int agent) { return Frame.ToUnity(Sim.Agents[agent].Pos); }

        void ScanLog()
        {
            var ev = Sim.Log.Events;
            while (_logIdx < ev.Count)
            {
                var e = ev[_logIdx++];
                _fresh.Add(e);
                if (Contacts != null) Contacts.OnEvent(e, _logIdx < ev.Count && ev[_logIdx].Type == SimEventType.Sack);
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
            if (Sim == null) return;
            _fresh.Clear();
            ScanLog();
            bool specials = Sim.Kind != PlayKind.Scrimmage && Sim.Kind != PlayKind.Kneel && Sim.Kind != PlayKind.Spike;
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Sim.Agents[i]; var v = _views[i];
                if (v == null) continue;
                Vector3 pos = Frame.ToUnity(a.Pos);
                Vector3 vel = Frame.VelToUnity(a.Vel);
                Vector3 face = Frame.DirToUnity(a.Facing);
                AnimHints h = BuildHints(a, v);
                float lat = 0f;
                if (a.Move != CarrierMove.None)
                {
                    Vector3 md = Frame.DirToUnity(a.MoveDir);
                    lat = Vector3.Dot(Vector3.Cross(Vector3.up, v.Forward), md);
                    if (a.Move == CarrierMove.JukeLeft) lat = -1f; else if (a.Move == CarrierMove.JukeRight) lat = 1f;
                }
                v.ApplyMatch(pos, vel, face, a.Action, h, lat, specials, _shotgun, dt);
            }
            if (Contacts != null) Contacts.Tick(dt);
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
            for (int i = 0; i < 22; i++)
            {
                SimAgent a = Sim.Agents[i];
                if ((a.Action != AgentAction.Engaged && a.Action != AgentAction.Shed) || a.Engagement == null || a.Engagement.Defender != a) continue;
                SimAgent b = a.Engagement.Primary;
                if (b == null || _views[b.Id] == null || _views[i] == null) continue;
                Pull(_views[b.Id], _views[i], a.ActionTime, 0.78f, 0.15f, true, true);
            }
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

        AnimHints BuildHints(SimAgent a, SimAgentView v)
        {
            var h = new AnimHints { Seed = a.Id * 7 + _playIndex * 3, Side = 'C' };
            Vector3 fwd = v.Forward, right = Vector3.Cross(Vector3.up, fwd);
            switch (a.Action)
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
            if (b.State == BallState.Held && b.Holder >= 0)
            {
                SimAgent h = Sim.Agents[b.Holder]; var v = _views[b.Holder];
                if (v != null)
                {
                    bool qbPass = h.Side == SimSide.Offense && h.Slot == 0 && !Sim.PassIsThrown && Sim.Kind == PlayKind.Scrimmage
                                  && (h.QbState == QbPhase.Set || h.QbState == QbPhase.Windup || h.QbState == QbPhase.Dropback || h.QbState == QbPhase.Scramble || h.QbState == QbPhase.Mesh || h.QbState == QbPhase.Fake);
                    if (qbPass || Sim.Kind == PlayKind.FieldGoal || Sim.Kind == PlayKind.Punt) { Ball.SetQbHold(v, h.Action == AgentAction.Throw); return; }
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
            Vector3 wp = Frame.ToUnity(b.Pos);
            if (Sim.Phase == PlayPhase.PreSnap && Sim.Kind == PlayKind.Kickoff) { Ball.SetTee(Frame.ToUnity(new Vec2(b.Pos.X, b.Pos.Y)), down); return; }
            Ball.SetFree(wp, Frame.VelToUnity(b.Vel.XY) + Vector3.up * (b.Vel.Z * PlayFrame.YardsToMeters), b.State, b.AtRest, down, dt);
        }
    }
}

