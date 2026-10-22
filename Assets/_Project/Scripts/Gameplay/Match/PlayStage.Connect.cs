using System;
using System.Collections.Generic;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 CONNECT: the stage side of the connections between animations.
    ///  - E-006 render interpolation: the stage keeps the last two fixed-step states of every agent and the ball and draws between them (the simulation runs at 60 Hz whatever the display rate).
    ///  - E-249 / E-252 pre-snap: shifts, audibles, flips and the break from the huddle are WALKED (the view glides to the simulated spot at a jog) instead of teleporting.
    ///  - Ball: who caught it and when (catch hold, tuck), the hand-off exchange, the holder's spot, the punt drop, the kickoff tee until the kick.
    ///  - Hints: pair seeds, block style, catch situation; reach IK to the real ball; carry tuck.
    /// </summary>
    public sealed partial class PlayStage
    {
        /// <summary>QA: switch the render interpolation off (the audit's "before" state).</summary>
        public static bool RenderInterpolation = true;
        /// <summary>QA: switch the pre-snap glide off (shifts and audibles teleport again).</summary>
        public static bool GlideEnabled = true;
        /// <summary>The director asks for the huddle break on the next Begin (scrimmage plays only).</summary>
        public bool NextBeginHuddle;

        // ------------------------------------------------------------------------------------------ interpolation
        readonly Vector3[] _pP = new Vector3[22], _cP = new Vector3[22], _pV = new Vector3[22], _cV = new Vector3[22], _pF = new Vector3[22], _cF = new Vector3[22];
        Vector3 _bpP, _bcP; bool _snapValid; PlaySimulation _snapSim;

        void OnSimStep(PlaySimulation s)
        {
            if (!_snapValid) return;
            for (int i = 0; i < 22; i++)
            {
                _pP[i] = _cP[i]; _pV[i] = _cV[i]; _pF[i] = _cF[i];
                var a = s.Agents[i];
                _cP[i] = Frame.ToUnity(a.Pos); _cV[i] = Frame.VelToUnity(a.Vel); _cF[i] = Frame.DirToUnity(a.Facing);
            }
            _bpP = _bcP; _bcP = Frame.ToUnity(s.Ball.Pos);
        }

        float InterpAlpha() { return RenderInterpolation && _snapValid && Sim != null ? Sim.StepAlpha : 1f; }

        void SampleAgent(int i, out Vector3 pos, out Vector3 vel, out Vector3 face)
        {
            if (!_snapValid) { var a = Sim.Agents[i]; pos = Frame.ToUnity(a.Pos); vel = Frame.VelToUnity(a.Vel); face = Frame.DirToUnity(a.Facing); return; }
            float al = InterpAlpha();
            Vector3 d = _cP[i] - _pP[i];
            if (d.sqrMagnitude > 1.5f) al = 1f;           // a teleport (a re-layout, a relocation): never smeared across the field
            pos = Vector3.Lerp(_pP[i], _cP[i], al); vel = Vector3.Lerp(_pV[i], _cV[i], al);
            face = _pF[i].sqrMagnitude < 1e-4f || _cF[i].sqrMagnitude < 1e-4f ? _cF[i] : Vector3.Slerp(_pF[i], _cF[i], al);
        }

        Vector3 SampleBallPos()
        {
            if (!_snapValid) return Frame.ToUnity(Sim.Ball.Pos);
            float al = InterpAlpha();
            if ((_bcP - _bpP).sqrMagnitude > 6f) al = 1f;
            return Vector3.Lerp(_bpP, _bcP, al);
        }

        // ------------------------------------------------------------------------------------------ begin / end
        struct Glide { public bool Init, Active, Huddle; public Vector3 Shown, LastTarget, HuddleFace; public float Speed, Wait; }
        readonly Glide[] _g = new Glide[22];
        readonly float[] _catchAt = new float[22], _dropAt = new float[22];
        int _lastHolder = -1; BallState _lastBallState; float _holderSince = -99f;
        int _exFrom = -1, _exTo = -1; float _exAt = -99f;

        void ConnectBegin(PlaySimulation sim, PlayFrame frame)
        {
            _snapSim = sim;
            System.Array.Clear(_pullW, 0, 22); System.Array.Clear(_pullOff, 0, 22);
            for (int i = 0; i < 22; i++)
            {
                var a = sim.Agents[i];
                _cP[i] = _pP[i] = frame.ToUnity(a.Pos); _cV[i] = _pV[i] = frame.VelToUnity(a.Vel); _cF[i] = _pF[i] = frame.DirToUnity(a.Facing);
                _g[i] = new Glide { Init = true, Shown = _cP[i], LastTarget = _cP[i] };
                _catchAt[i] = -99f; _dropAt[i] = -99f;
            }
            _bcP = _bpP = frame.ToUnity(sim.Ball.Pos);
            _lastHolder = -1; _lastBallState = sim.Ball.State; _holderSince = -99f; _exFrom = _exTo = -1; _exAt = -99f; _lastPhase = sim.Phase;
            _snapValid = true;
            sim.AfterStep += OnSimStep;
            bool huddle = NextBeginHuddle && sim.Kind == PlayKind.Scrimmage && sim.Phase == PlayPhase.PreSnap && GlideEnabled;
            NextBeginHuddle = false;
            if (huddle) PlaceHuddle(sim, frame);
        }

        void ConnectEnd()
        {
            if (_snapSim != null) { _snapSim.AfterStep -= OnSimStep; _snapSim = null; }
            _snapValid = false;
        }

        /// <summary>The offense stands in a ring 8 yards behind the ball and breaks to the line, the defense jogs up from deeper (E-252).</summary>
        void PlaceHuddle(PlaySimulation sim, PlayFrame frame)
        {
            Vector3 center = frame.ToUnity(new Vec2(sim.BallX0, sim.LosY - 8.5f));
            for (int i = 0; i < 22; i++)
            {
                var v = _views[i]; if (v == null) continue;
                Vector3 target = _cP[i];
                Vector3 start; Vector3 face;
                if (i < 11)
                {
                    float ang = (i / 11f) * Mathf.PI * 2f;
                    start = center + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 1.55f;
                    face = center - start; face.y = 0f; face = face.sqrMagnitude > 1e-4f ? face.normalized : Vector3.forward;
                    _g[i].Wait = 0.45f + 0.025f * i; _g[i].Huddle = true;
                }
                else
                {
                    var a = sim.Agents[i];
                    start = frame.ToUnity(new Vec2(a.Pos.X + ((i * 7) % 5 - 2) * 0.8f, a.Pos.Y + 3.8f + (i % 3) * 1.4f));
                    face = frame.DirToUnity(new Vec2(0f, -1f));
                    _g[i].Wait = 0f; _g[i].Huddle = false;
                }
                _g[i].Shown = start; _g[i].LastTarget = target; _g[i].Active = true; _g[i].Speed = 0f; _g[i].HuddleFace = face;
                v.ResetForPlay(start, Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg);
            }
        }

        /// <summary>True while a player is still walking to his spot (the huddle break, an audible, a shift): the snap waits.</summary>
        public bool BreakInProgress
        {
            get
            {
                if (Sim == null || Sim.Phase != PlayPhase.PreSnap) return false;
                for (int i = 0; i < 22; i++) if (_g[i].Active || _g[i].Wait > 0f) return true;
                return false;
            }
        }

        // ------------------------------------------------------------------------------------------ per agent
        void ConnectAgent(int i, SimAgent a, SimAgentView v, ref AnimHints h, ref Vector3 pos, ref Vector3 vel, ref Vector3 face, ref AgentAction viewAction, float dt)
        {
            h.Live = Sim.Phase == PlayPhase.Live;
            // ---- pre-snap: walk the change instead of teleporting
            bool gliding = GlideStep(i, a, v, ref pos, ref vel, ref face, dt);
            if (gliding) viewAction = vel.sqrMagnitude > 0.25f ? AgentAction.Run : AgentAction.Idle;
            // ---- the quarterback reads ahead of the exchange: the ball starts travelling to the back half a window before the simulated exchange
            if (i == 0 && viewAction == AgentAction.Handoff && h.EventEta >= 0f && h.EventEta <= 0.13f && Sim.QbHandoffTargetId > 0) { _preExTgt = Sim.QbHandoffTargetId; _preExEta = h.EventEta; }
            // ---- hands: the ball in the air is reached for, released otherwise; the carry arm tucks
            bool reach = false; Vector3 rp = Vector3.zero; bool one = false;
            var b = Sim.Ball;
            bool catchLike = viewAction == AgentAction.Catch || (viewAction == AgentAction.Jump && h.Defender);
            if (catchLike && (b.State == BallState.Pass || b.State == BallState.Kick || b.State == BallState.Pitch) && h.EventEta <= 0.32f)
            {
                reach = true; rp = SampleBallPos();
                one = h.Diving && ((h.Seed & 1) == 1);
            }
            else if (Time.time - _catchAt[i] < 0.22f && b.State == BallState.Held && b.Holder == i) { reach = true; rp = Ball != null ? Ball.Position : SampleBallPos(); }
            v.ReachBall(rp, reach ? 1f : 0f, one);
            v.SetTuck(0f, _carrySide[i]);
        }

        bool GlideStep(int i, SimAgent a, SimAgentView v, ref Vector3 pos, ref Vector3 vel, ref Vector3 face, float dt)
        {
            var g = _g[i];
            bool pre = Sim.Phase == PlayPhase.PreSnap && Sim.Kind == PlayKind.Scrimmage && GlideEnabled && !(_anyWalk && _walks[i].Active);
            Vector3 target = pos;
            if (!pre)
            {
                g.Shown = target; g.LastTarget = target; g.Active = false; g.Wait = 0f;
                if (v.Huddle) v.SetHuddle(false, 0);
                _g[i] = g; return false;
            }
            if ((target - g.LastTarget).sqrMagnitude > 0.36f) { g.Active = true; if (g.Speed < 0.2f) g.Speed = 0f; }   // the simulation moved him a long way in one go: walk it
            g.LastTarget = target;
            if (g.Wait > 0f)
            {
                g.Wait -= dt;
                v.SetHuddle(true, i * 7 + _playIndex);
                pos = g.Shown; vel = Vector3.zero; face = g.HuddleFace;
                _g[i] = g; return true;
            }
            if (v.Huddle) v.SetHuddle(false, 0);
            Vector3 to = target - g.Shown; to.y = 0f;
            float d = to.magnitude;
            if (!g.Active && d < 0.06f) { g.Shown = target; _g[i] = g; return false; }
            if (!g.Active) g.Active = true;
            float vmax = d > 6f ? 5.2f : (d > 2.5f ? 3.8f : 2.4f);
            float want = Mathf.Min(vmax, Mathf.Max(0.55f, d * 2.4f));
            g.Speed = Mathf.MoveTowards(g.Speed, want, 7f * Mathf.Max(dt, 0.0001f));
            float step = Mathf.Min(d, g.Speed * Mathf.Max(dt, 0.0001f));
            Vector3 dir = d > 1e-4f ? to / d : Vector3.forward;
            g.Shown += dir * step;
            bool arrived = d - step < 0.06f;
            Vector3 shownFace = d > 0.45f ? dir : face;
            if (arrived) { g.Shown = target; g.Active = false; g.Speed = 0f; }
            pos = new Vector3(g.Shown.x, pos.y, g.Shown.z);
            vel = arrived ? Vector3.zero : dir * g.Speed;
            face = shownFace;
            _g[i] = g;
            return !arrived;
        }

        // ------------------------------------------------------------------------------------------ hints
        void FillCatchHints(ref AnimHints h, SimAgent a, SimAgentView v)
        {
            Vector3 fwd = v.Forward, right = Vector3.Cross(Vector3.up, fwd);
            var b = Sim.Ball;
            Vector3 bv = Frame.VelToUnity(b.Vel.XY);
            if (bv.sqrMagnitude < 0.25f) bv = fwd;
            bv.y = 0f; bv.Normalize();
            h.BallLocalDir = new Vector3(Vector3.Dot(bv, right), 0f, Vector3.Dot(bv, fwd));
            float eta = Mathf.Max(0f, _flowEta);
            Vector3 at = Frame.ToUnity(b.Pos) + (Frame.VelToUnity(b.Vel.XY) + Vector3.up * (b.Vel.Z * PlayFrame.YardsToMeters)) * eta;
            at.y = Mathf.Max(0.1f, at.y - 0.5f * 9.81f * eta * eta);
            h.BallHeight = at.y;
            Vector3 rel = at - v.transform.position; rel.y = 0f;
            float lateral = Mathf.Abs(Vector3.Dot(rel, right));
            h.Airborne = a.JumpTime > 0f;
            h.Defender = a.Side == SimSide.Defense;
            h.Diving = !h.Airborne && (lateral > 1.25f || (at.y > 2.5f && lateral > 0.95f));
            float edge = Mathf.Min(a.Pos.X, 2f * PlayFrame.HalfWidthYd - a.Pos.X);
            h.NearSideline = edge < 1.6f;
            h.Dropped = Time.time - _dropAt[a.Id] < 0.9f;
            h.ReturnCatch = Sim.Kind != PlayKind.Scrimmage && a.ReturnerRole;
            h.FairCatch = h.ReturnCatch && Sim.KickWasFairCatch;
            float cd = 99f;
            for (int j = 0; j < 22; j++) { var o = Sim.Agents[j]; if (o.Side == a.Side || o.IsDown) continue; float dd = (o.Pos - a.Pos).Length; if (dd < cd) cd = dd; }
            h.Contested = cd < 1.1f;
        }

        void ConnectHints(ref AnimHints h, SimAgent a, SimAgentView v, AgentAction act)
        {
            h.Live = Sim.Phase == PlayPhase.Live;
            h.PairSeed = 0;
            if (act == AgentAction.Catch) FillCatchHints(ref h, a, v);
            else if (act == AgentAction.Jump && a.Side != Sim.BallSide) FillCatchHints(ref h, a, v);
            // ---- blocks
            BlockStyle bs = BlockStyle.None;
            var ba = a.Assign as BlockAssignment;
            bool passPlay = Sim.OffPlay != null && Sim.OffPlay.IsPass;
            if (ba != null)
            {
                switch (ba.Block)
                {
                    case BlockKind.PassPro: bs = BlockStyle.PassSet; break;
                    case BlockKind.Pull: bs = BlockStyle.Pull; break;
                    case BlockKind.Chip: bs = BlockStyle.Chip; break;
                    case BlockKind.Stalk: bs = BlockStyle.Stalk; break;
                    case BlockKind.Kickout: bs = BlockStyle.Kickout; break;
                    case BlockKind.Double: bs = BlockStyle.Double; break;
                    default: bs = passPlay ? BlockStyle.PassSet : BlockStyle.RunBlock; break;
                }
            }
            else if (act == AgentAction.Block) bs = BlockStyle.Stalk;
            var e = a.Engagement;
            if (e != null && e.Primary != null && e.Defender != null)
            {
                bs = e.RunMode ? (e.Blockers.Count > 1 ? BlockStyle.Double : BlockStyle.RunBlock) : BlockStyle.PassSet;
                int lo = Mathf.Min(e.Primary.Id, e.Defender.Id), hi = Mathf.Max(e.Primary.Id, e.Defender.Id);
                h.PairSeed = (lo * 131 + hi) * 7 + _playIndex * 3 + 1;      // E-162: the same for both actors (and for the double-team partner)
                h.Lev = e.Lev; h.Winning = a == e.Defender ? e.Lev < 0f : e.Lev > 0f;
                h.Rush = e.Move; h.Defender = e.Defender == a;
            }
            h.Block = bs;
            // ---- tackles: one seed for the tackler and the carrier (E-210)
            if (h.Tackle != TackleType.None || h.Sack)
            {
                int carrier = -1, tackler = -1; TackleInfo ti;
                if (a.Action == AgentAction.Tackled && _tackled.TryGetValue(a.Id, out ti)) { carrier = a.Id; tackler = ti.Tackler; }
                else if (IsTackleAction(a.Action) && _tackler.TryGetValue(a.Id, out carrier)) tackler = a.Id;
                if (carrier >= 0 && tackler >= 0) h.PairSeed = (carrier * 31 + tackler) * 7 + _playIndex * 3 + 1;
            }
            // ---- hand-off: both actors take the same pair (E-139)
            if (act == AgentAction.Handoff && Sim.QbHandoffTargetId >= 0) h.PairSeed = (Sim.QbHandoffTargetId * 31 + 7) * 7 + _playIndex * 3 + 1;
        }

        // ------------------------------------------------------------------------------------------ events
        void ConnectEvent(PlayEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.Drop: if (e.AgentA >= 0 && e.AgentA < 22) _dropAt[e.AgentA] = Time.time; break;
                case SimEventType.Touchback:
                    // E-299: the returner kneels in the end zone before the play is ended
                    if (e.AgentA >= 0 && e.AgentA < 22 && Contacts != null && !SimAgentView.DebugNoConnect)
                    {
                        var kp = AnimKit.QbSpecial(PlayKind.Kneel, e.AgentA + _playIndex);
                        if (kp.Valid) { kp.Hold = false; kp.End = Mathf.Min(kp.Clip.length, 1.8f); Contacts.PlayOnce(e.AgentA, kp, 1.7f); }
                    }
                    break;
            }
        }

        /// <summary>A sack follows this tackle event: within the next three log entries for the same carrier, or the carrier is the quarterback in his pocket (E-221).</summary>
        bool SackFollows(List<PlayEvent> ev, int idx, PlayEvent e)
        {
            if (e.Type != SimEventType.Tackle) return false;
            for (int k = 0; k < 3; k++)
            {
                int j = idx + k;
                if (j < ev.Count && ev[j].Type == SimEventType.Sack && (ev[j].AgentB == e.AgentB || ev[j].AgentA == e.AgentA)) return true;
            }
            if (e.AgentB == 0 && Sim.Kind == PlayKind.Scrimmage && !Sim.PassIsThrown)
            {
                var q = Sim.Agents[0];
                return q.QbState != QbPhase.Scramble && q.Pos.Y < Sim.LosY;
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------ the ball
        float _presentDt;
        readonly Vector3[] _pullOff = new Vector3[22]; readonly float[] _pullW = new float[22], _pullYaw = new float[22]; readonly bool[] _pullTouched = new bool[22];

        static float BodyRadius(SimAgentView v) { return v == null ? 0.29f : (v.Size == PlayerSize.Large ? 0.33f : (v.Size == PlayerSize.Small ? 0.26f : 0.29f)); }

        /// <summary>
        /// Engaged pairs are pulled to the authored gap of their take and turned face to face (E-173): the gap follows the take (run drive closer than a pass set) and the two bodies' size, every blocker of a double team
        /// is handled (not only the primary), and the pull weight decays over 0.2 s after the engagement ends instead of dropping to zero in one frame (E-174: the pair no longer pops apart at every shed and release).
        /// </summary>
        void ApplyEngagementPulls(float dt)
        {
            dt = Mathf.Max(dt, 0.0001f);
            for (int i = 0; i < 22; i++) _pullTouched[i] = false;
            for (int i = 0; i < 22; i++)
            {
                SimAgent d = Sim.Agents[i];
                if ((d.Action != AgentAction.Engaged && d.Action != AgentAction.Shed) || d.Engagement == null || d.Engagement.Defender != d) continue;
                var e = d.Engagement; var vd = _views[i]; if (vd == null) continue;
                int nb = e.Blockers.Count; if (nb == 0) continue;
                float ramp = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(d.ActionTime / 0.15f));
                Vector3 offD = Vector3.zero; float yawD = vd.Yaw; int counted = 0;
                for (int k = 0; k < nb; k++)
                {
                    var bk = e.Blockers[k]; var vb = _views[bk.Id]; if (vb == null) continue;
                    float distance = (e.RunMode ? 0.72f : 0.78f) * Mathf.Clamp((BodyRadius(vd) + BodyRadius(vb)) / 0.58f, 0.85f, 1.25f);
                    if (nb > 1) distance *= 1.1f;
                    Vector3 to = vd.transform.position - vb.transform.position; to.y = 0f;
                    float s = to.magnitude; if (s < 0.05f) continue;
                    Vector3 n = to / s;
                    float delta = Mathf.Clamp((s - distance) * 0.5f, -0.45f, 0.7f);
                    float yawBD = Mathf.Atan2(n.x, n.z) * Mathf.Rad2Deg;
                    // the blocker steps toward his man, the defender gives half of the gap back (shared among the blockers)
                    _pullOff[bk.Id] = n * delta; _pullYaw[bk.Id] = yawBD; _pullTouched[bk.Id] = true; _pullW[bk.Id] = Mathf.MoveTowards(_pullW[bk.Id], ramp, dt / 0.15f);
                    offD += -n * delta; yawD = yawBD + 180f; counted++;
                }
                if (counted == 0) continue;
                _pullOff[i] = offD / counted; _pullYaw[i] = yawD; _pullTouched[i] = true; _pullW[i] = Mathf.MoveTowards(_pullW[i], ramp, dt / 0.15f);
            }
            for (int i = 0; i < 22; i++)
            {
                var v = _views[i]; if (v == null) { _pullW[i] = 0f; continue; }
                if (!_pullTouched[i]) _pullW[i] = Mathf.MoveTowards(_pullW[i], 0f, dt / 0.2f);
                if (_pullW[i] > 0.001f && !v.ContactDriven) v.ApplyContact(_pullOff[i], _pullYaw[i], _pullW[i], true);
            }
        }

        int _preExTgt = -1; float _preExEta;
        int _magnetAgent = -1; float _magnetEta = 9f;
        /// <summary>The player about to catch the ball: for the last 0.2 s before the simulated catch the DISPLAYED ball is drawn into his hands (the simulation completes a catch about a metre from the body; the hands reach for it with IK and meet it).</summary>
        readonly float[] _magEta = new float[22];
        void Magnet(int agent, float eta) { if (agent < 0 || agent >= 22) return; _magEta[agent] = eta; if (eta < _magnetEta) { _magnetEta = eta; _magnetAgent = agent; } }

        Vector3 MagnetBall(Vector3 wp, BallState state)
        {
            if (_magnetAgent < 0 || _magnetEta > 0.3f || SimAgentView.DebugNoConnect || (state != BallState.Pass && state != BallState.Kick && state != BallState.Pitch)) return wp;
            // of everybody reaching for it, the player whose hands are nearest the ball gets it
            int pick = -1; float bestD = 99f; Vector3 hm = Vector3.zero; float pickEta = 9f;
            for (int j = 0; j < 22; j++)
            {
                if (_magEta[j] > 0.3f) continue;
                var vj = _views[j]; if (vj == null || vj.Avatar == null || vj.Avatar.RightHand == null || vj.Avatar.LeftHand == null) continue;
                Vector3 m = (vj.Avatar.RightHand.position + vj.Avatar.LeftHand.position) * 0.5f;
                float dj = (m - wp).magnitude; if (dj < bestD) { bestD = dj; pick = j; hm = m; pickEta = _magEta[j]; }
            }
            if (pick < 0) return wp;
            _magnetEta = pickEta;
            float dist = (hm - wp).magnitude;
            if (dist > 2.6f) return wp;                  // not a catch he can reach: leave the ball alone
            // the pull grows as the ball closes on the hands (continuous: the old time-based weight popped when the ball came inside reach)
            float w = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((2.2f - dist) / 1.9f)) * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((0.30f - _magnetEta) / 0.12f));
            return Vector3.Lerp(wp, hm, w);
        }

        PlayPhase _lastPhase = PlayPhase.PreSnap;

        /// <summary>True while a tackled player is still going down or getting up (ragdoll, take or get-up): the post-play window waits for him (E-216).</summary>
        public bool PostPlayBusy { get { return Contacts != null && Contacts.Busy; } }

        /// <summary>E-271: after the whistle the players do not freeze in the whistle pose: the ones near the pile gather round it at a walk, the rest drift toward it; the touchdown and kick plays keep their own choreography.</summary>
        void PostWhistleStart()
        {
            if (!GlideEnabled || Sim.Kind != PlayKind.Scrimmage || _anyWalk) return;
            var er = Sim.EndReason;
            if (er != EndReason.Tackle && er != EndReason.OutOfBounds && er != EndReason.Incomplete && er != EndReason.Sack) return;
            Vec2 spotSim = Sim.Carrier != null ? Sim.Carrier.Pos : Sim.Ball.Pos.XY;
            Vector3 spot = Frame.ToUnity(spotSim);
            int n = 0;
            for (int i = 0; i < 22; i++)
            {
                var a = Sim.Agents[i]; var v = _views[i];
                if (v == null || a.IsDown || a.State == AgentState.Engaged || (Sim.Carrier != null && a.Id == Sim.Carrier.Id)) continue;
                if (a.Action == AgentAction.Tackled || a.Action == AgentAction.Fallen || a.Action == AgentAction.Celebrate) continue;
                float dist = (a.Pos - spotSim).Length;
                if (dist > 22f) continue;
                float ang = (n++ * 2.399963f) + (er == EndReason.Incomplete ? 0.5f : 0f);       // golden angle: an even scatter
                float radius = er == EndReason.Incomplete ? 5f + (i % 4) * 1.2f : 2.6f + (i % 5) * 0.55f;
                Vector3 target = spot + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius;
                if (dist < 3f && er != EndReason.Incomplete) continue;
                WalkTo(i, target, dist < 10f ? 2.7f : 1.7f, spot);
            }
        }

        void ConnectBallTrack(BallSim b)
        {
            if (Sim.Phase != _lastPhase) { if (Sim.Phase == PlayPhase.Dead) PostWhistleStart(); _lastPhase = Sim.Phase; }
            int holder = b.State == BallState.Held ? b.Holder : -1;
            if (holder != _lastHolder)
            {
                if (holder >= 0)
                {
                    if (_lastHolder >= 0 && _lastBallState == BallState.Held && Sim.Agents[_lastHolder].Side == Sim.Agents[holder].Side) { _exFrom = _lastHolder; _exTo = holder; _exAt = Time.time; }
                    else { _catchAt[holder] = Time.time; _exFrom = _exTo = -1; }
                    _holderSince = Time.time;
                }
                _lastHolder = holder;
            }
            _lastBallState = b.State;
        }

        bool ConnectTeeNow() { return Sim.Kind == PlayKind.Kickoff && !Sim.KickStruck && Sim.Ball.State != BallState.Kick; }

        /// <summary>Returns true when the ball was placed here (quarterback, holder, punter), false to let the carry logic place it (primed with the catch hold or the exchange first).</summary>
        bool ConnectHeld(SimAgent h, SimAgentView v, bool qbPass)
        {
            float since = Time.time - _holderSince;
            bool kickPlay = Sim.Kind == PlayKind.FieldGoal || Sim.Kind == PlayKind.Punt;
            bool kickerRole = kickPlay && !Sim.KickStruck && ((h.Side == SimSide.Offense && h.Slot == 0) || Sim.KickerSlot == h.Id);      // E-124: the returners carry the ball like carriers
            Vector3 down = Frame.DirToUnity(new Vec2(0f, 1f));
            var carriage = v.Motion != null ? v.Motion.ShownCarriage : BodyMotion.Carriage.None;
            if (kickerRole && Sim.Kind == PlayKind.FieldGoal && h.Slot == 0 && since > 0.32f && !SimAgentView.DebugNoConnect)
            {
                Ball.SetSpot(Frame.ToUnity(new Vec3(h.Pos.X, h.Pos.Y + 0.4f, KickGeometry.HolderSpotHeightYd)), down); return true;       // E-253
            }
            if (kickerRole && Sim.Kind == PlayKind.Punt && Sim.KickerSlot == h.Id && Sim.KickContactTime > 0f && !SimAgentView.DebugNoConnect)
            {
                float left = Sim.KickContactTime - Sim.Time;
                if (left <= KickGeometry.PuntDropSeconds)
                {
                    Ball.SetDrop(Frame.ToUnity(new Vec3(h.Pos.X, h.Pos.Y + KickGeometry.PuntContactAheadYd, KickGeometry.PuntContactHeightYd)), Mathf.Max(0.02f, left), down); return true;   // E-256
                }
            }
            if (!SimAgentView.DebugNoConnect && h.Id == 0 && _preExTgt > 0 && _preExTgt < 22 && _views[_preExTgt] != null) { Ball.SetExchange(v, _views[_preExTgt], _carrySide[_preExTgt], -_preExEta, 0.24f); return true; }
            if (qbPass || kickerRole) { Ball.SetQbHold(v, h.Action == AgentAction.Throw, carriage); return true; }
            if (SimAgentView.DebugNoConnect) return false;
            // ---- the hand-off: the ball travels from the quarterback's hands to the runner's carry pose
            if (_exTo == h.Id && _exFrom >= 0 && _exFrom < 22 && Time.time - _exAt < 0.5f && _views[_exFrom] != null) Ball.SetExchange(_views[_exFrom], v, _carrySide[h.Id], Time.time - _exAt, 0.24f);
            // ---- the catch: both hands on the ball for a beat, then the tuck
            else if (Time.time - _catchAt[h.Id] < BallFollower.CatchHoldSeconds + BallFollower.CatchTuckSeconds + 0.1f && h.Action != AgentAction.Celebrate) Ball.SetCatch(v, _carrySide[h.Id], Time.time - _catchAt[h.Id]);
            if (h.HasBall) v.SetTuck(1f, _carrySide[h.Id]);
            return false;
        }

        /// <summary>Warms one view per roster body (size x base) with its ragdoll built, so nothing is created or built in the middle of a play (E-227 / E-269).</summary>
        public void WarmAllBodies(int perBody)
        {
            Init();
            var made = new List<SimAgentView>();
            foreach (PixFootball.Art.BodyBase bb in Enum.GetValues(typeof(PixFootball.Art.BodyBase)))
                foreach (PlayerSize sz in Enum.GetValues(typeof(PlayerSize)))
                    for (int k = 0; k < perBody; k++) made.Add(Acquire(sz, bb, int.MinValue));
            foreach (var v in made) { if (v.Ragdoll != null) v.Ragdoll.Prebuild(); Return(v); }
        }
    }
}
