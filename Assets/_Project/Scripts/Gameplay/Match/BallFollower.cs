using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// The football in the stadium: follows the simulated ball in flight (spiral / tumble / bounce), sits on the tee before a kickoff and is attached to the carrier's arm (tucked), the quarterback's hands,
    /// both hands of a catcher, the exchange between two players, the holder's fingertip or a falling punt while held. Poses are applied in LateUpdate so they follow the animated bones.
    /// v0.9.0 CONNECT: catch-hold mode (hands at the ball for the catch window, then the tuck), hand-off exchange (the ball travels from one pair of hands to the other over the exchange window),
    /// position AND orientation blends at every attach / release / arm switch, forearm-relative carry pose, tee at the launch height, holder spot, punt drop.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed partial class BallFollower : MonoBehaviour
    {
        enum Mode { Free, Carry, QbHold, Tee, Hand, Catch, Exchange, Spot, Drop, Relaxed, Spike, Official }

        Transform _ball;
        Mode _mode = Mode.Free;
        Vector3 _pos, _vel, _downfield = Vector3.forward;
        BallState _state;
        bool _atRest;
        Quaternion _rot = Quaternion.identity;
        float _spin;
        SimAgentView _holder, _from;
        int _side = 1;
        bool _throwing;
        BodyMotion.Carriage _qbCarriage;
        // v0.7.0 (note 30): no pops when the ball changes hands with the air - the offset between where it was and where the simulation puts it decays over a few frames
        Vector3 _relOffset, _attachOffset; Quaternion _relRot = Quaternion.identity, _attachRot = Quaternion.identity;
        float _relT = 1f, _attachT = 1f, _attachDur = BlendSeconds; bool _attachPending; float _attachArc;
        public const float BlendSeconds = 0.12f;
        /// <summary>Blend of the ball leaving a hand into the simulated flight (a fumble or a throw): longer than an attach, the hand and the simulated ball can be a metre apart.</summary>
        public const float FreeBlendSeconds = 0.2f;
        /// <summary>Seconds both hands stay on the ball after a catch before the tuck starts, and the tuck itself.</summary>
        public const float CatchHoldSeconds = 0.14f, CatchTuckSeconds = 0.3f;
        float _modeT;           // seconds in the current mode (catch / exchange / drop)
        float _exchangeDur = 0.26f;
        Vector3 _dropFrom, _dropTo; float _dropDur = 0.34f;
        Vector3 _spotPos;

        /// <summary>World position of the ball after the last LateUpdate (cameras and markers read it).</summary>
        public Vector3 Position { get { return _ball != null ? _ball.position : _pos; } }
        /// <summary>QA: the mode the ball is in right now.</summary>
        public string ModeName { get { return _mode.ToString(); } }
        /// <summary>The player the ball is attached to (null while free).</summary>
        public SimAgentView Holder { get { return _mode == Mode.Free || _mode == Mode.Tee || _mode == Mode.Drop || _mode == Mode.Spike || _mode == Mode.Official ? null : _holder; } }

        public static BallFollower Create(Transform parent)
        {
            var go = new GameObject("Ball");
            go.transform.SetParent(parent, false);
            var b = go.AddComponent<BallFollower>();
            var lib = ArtLibrary.Load();
            var prefab = lib != null ? lib.Prefab("prop_footballball") : null;
            if (prefab != null)
            {
                var inst = Instantiate(prefab, go.transform);
                inst.name = "BallMesh";
                inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
                foreach (var r in inst.GetComponentsInChildren<Renderer>(true)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                b._ball = go.transform;
            }
            else b._ball = go.transform;
            return b;
        }

        public void SetVisible(bool v) { gameObject.SetActive(v); }

        public void SetFree(Vector3 pos, Vector3 vel, BallState state, bool atRest, Vector3 downfield, float dt)
        {
            if (IsHeld(_mode) && _ball != null)
            {
                _relOffset = _ball.position - pos; _relT = 0f; _relRot = _ball.rotation;
                if (_relOffset.sqrMagnitude > 9f) _relOffset = Vector3.zero;      // a real teleport (a kick through a pile) is not smoothed
            }
            else if ((_mode == Mode.Tee || _mode == Mode.Drop || _mode == Mode.Spot) && _ball != null)
            {
                _relOffset = _ball.position - pos; _relT = 0f; _relRot = _ball.rotation;
                if (_relOffset.sqrMagnitude > 4f) _relOffset = Vector3.zero;
            }
            _mode = Mode.Free; _holder = null; _from = null; _pos = pos; _vel = vel; _state = state; _atRest = atRest; _downfield = downfield;
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            switch (state)
            {
                case BallState.Pass: case BallState.Pitch: case BallState.Snap:
                {
                    // spiral: long axis along the velocity, rolling about it
                    if (vel.sqrMagnitude > 0.5f)
                    {
                        _rot = GfxPassRot(vel, state, dt);   // v0.9 GFX-119 (BallFollower.Gfx.cs)
                    }
                    break;
                }
                case BallState.Kick:
                {
                    _rot = GfxKickRot(vel, flat, dt);   // v0.9 GFX-310 / 429: punt spiral, kickoff and field goal tumble, nose along the whole velocity (BallFollower.Gfx.cs)
                    break;
                }
                case BallState.Loose:
                {
                    if (!atRest)
                    {
                        _spin += Mathf.Clamp(vel.magnitude * 120f, 60f, 700f) * dt;
                        Vector3 lat = flat.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
                        _rot = Quaternion.AngleAxis(_spin, lat) * _rot;
                    }
                    else _rot = GfxRestRot(dt);   // v0.9 GFX-425: lies where it stopped (BallFollower.Gfx.cs)
                    break;
                }
                default:
                    _rot = Quaternion.Slerp(_rot, Quaternion.FromToRotation(Vector3.right, new Vector3(downfield.x, 0f, downfield.z).normalized), 1f - Mathf.Exp(-dt * 12f));
                    break;
            }
        }

        /// <summary>Kickoff tee: the ball stands on its tee at the launch height until the kick is struck (E-255 / E-387).</summary>
        public void SetTee(Vector3 pos, Vector3 downfield)
        {
            if (_mode != Mode.Tee) { _relT = 1f; }
            _mode = Mode.Tee; _pos = pos; _downfield = downfield; _holder = null;
        }

        static bool IsHeld(Mode m) { return m == Mode.Carry || m == Mode.QbHold || m == Mode.Hand || m == Mode.Catch || m == Mode.Exchange || m == Mode.Relaxed || m == Mode.Official; }

        public void SetCarry(SimAgentView holder, int side)
        {
            if (_mode == Mode.Carry && _holder == holder && _side != side) { if (PixFootball.Sim.CarryStyleConfig.Enabled) MarkAttach(0.07f, 0.015f); else MarkAttach(0.2f, 0.07f); _side = side; return; }      // v0.11.0 RB (B-065): the carry layer has both hands on the ball when it changes arms, so the blend is 0.07 s (it was 0.2 s with neither hand at the ball: 0.5 m off the hands)   // E-132: the arm switch carries the ball across the chest
            if (_mode == Mode.Carry && _holder == holder) { _side = side; return; }
            if (_mode == Mode.Catch && _holder == holder) { _side = side; return; }       // the catch hold hands over to the tuck by itself
            if (_mode == Mode.Exchange && _holder == holder) { _side = side; return; }
            MarkAttach(BlendSeconds, 0f);
            _mode = Mode.Carry; _holder = holder; _side = side;
        }

        /// <summary>The ball in the open hand of the player (touchdown celebration, pre-spike): side +1 = right hand.</summary>
        public void SetHand(SimAgentView holder, int side)
        {
            if (_mode == Mode.Hand && _holder == holder) { _side = side; return; }
            MarkAttach(BlendSeconds, 0f); _mode = Mode.Hand; _holder = holder; _side = side;
        }

        public void SetQbHold(SimAgentView holder, bool throwing, BodyMotion.Carriage carriage = BodyMotion.Carriage.None)
        {
            if (_mode != Mode.QbHold || _holder != holder || _throwing != throwing || _qbCarriage != carriage) MarkAttach(BlendSeconds, 0f);      // v0.9.0 QBSTANCE (E-109): the ball glides between the pocket, the ear and the throwing hand instead of jumping 20 to 40 cm
            _mode = Mode.QbHold; _holder = holder; _throwing = throwing; _qbCarriage = carriage;
        }

        /// <summary>v0.9.0 E-126: a catch. Both hands stay on the ball for <see cref="CatchHoldSeconds"/>, then the ball tucks to the carry arm over <see cref="CatchTuckSeconds"/>. secondsSinceCatch drives the blend (the view keeps counting while the sim holds the ball).</summary>
        public void SetCatch(SimAgentView holder, int side, float secondsSinceCatch)
        {
            if (_mode != Mode.Catch || _holder != holder) { MarkAttach(0.1f, 0f); _modeT = Mathf.Max(0f, secondsSinceCatch); }
            _mode = Mode.Catch; _holder = holder; _side = side;
            _modeT = Mathf.Max(_modeT, secondsSinceCatch);
        }

        /// <summary>v0.9.0 E-247 / E-159: the hand-off. The ball travels from the quarterback's hands to the back's carry pose over <paramref name="duration"/> seconds.</summary>
        public void SetExchange(SimAgentView from, SimAgentView to, int side, float secondsSinceExchange, float duration = 0.26f)
        {
            // the transfer is centred on the simulated exchange: it starts duration / 2 before it (negative secondsSinceExchange = the view reads ahead of the simulation) and ends duration / 2 after it
            if (_mode != Mode.Exchange || _holder != to) { _mode = Mode.Exchange; _modeT = secondsSinceExchange; if (_ball != null) { MarkAttach(0.06f, 0f); } }
            _from = from; _holder = to; _side = side; _exchangeDur = Mathf.Max(0.08f, duration);
            _modeT = secondsSinceExchange < 0f ? secondsSinceExchange : Mathf.Max(_modeT, secondsSinceExchange);
        }

        /// <summary>v0.9.0 E-253: the holder steadies the ball upright on the turf at <paramref name="pos"/>.</summary>
        public void SetSpot(Vector3 pos, Vector3 downfield)
        {
            if (_mode != Mode.Spot) { MarkAttach(0.15f, 0f); }
            _mode = Mode.Spot; _spotPos = pos; _downfield = downfield; _holder = null;
        }

        /// <summary>v0.9.0 E-256: the punter lets the ball fall from his hands to the contact point; it leaves the hands nose down and arrives with the foot.</summary>
        public void SetDrop(Vector3 to, float secondsLeft, Vector3 downfield)
        {
            if (_mode != Mode.Drop)
            {
                _dropFrom = _ball != null ? _ball.position : to; _modeT = 0f; _holder = null;
            }
            _mode = Mode.Drop; _dropTo = to; _downfield = downfield;
            _dropDur = Mathf.Max(0.05f, _modeT + secondsLeft);
        }

        void MarkAttach(float seconds, float arc)
        {
            if (_mode == Mode.Tee || _mode == Mode.Free || _mode == Mode.Spot || _mode == Mode.Drop || IsHeld(_mode)) { _attachPending = true; _attachDur = Mathf.Max(0.03f, seconds); _attachArc = arc; }
        }

        void LateUpdate()
        {
            if (_ball == null) return;
            float dt = Time.deltaTime;
            GfxEffects();   // v0.9 GFX-437: flight trail and landing ring
            switch (_mode)
            {
                case Mode.Free:
                {
                    _relT += dt;
                    float u = Mathf.SmoothStep(0f, 1f, _relT / FreeBlendSeconds);
                    _ball.SetPositionAndRotation(_pos + GfxRestDrop() + _relOffset * (1f - u), _relT >= FreeBlendSeconds ? _rot : Quaternion.Slerp(_relRot, _rot, u));
                    break;
                }
                case Mode.Tee:
                {
                    Vector3 axis = (Vector3.up * 0.9f + _downfield * 0.45f).normalized;
                    Vector3 p = new Vector3(_pos.x, KickGeometry.TeeHeightYd * PlayFrame.YardsToMeters, _pos.z);
                    ApplyAttach(p, Quaternion.FromToRotation(Vector3.right, axis), dt);
                    break;
                }
                case Mode.Spot:
                {
                    Vector3 axis = (Vector3.up * 0.96f + _downfield * 0.26f).normalized;      // tilted about 75 degrees, the point toward the kicker's target
                    ApplyAttach(_spotPos, Quaternion.FromToRotation(Vector3.right, axis), dt);
                    break;
                }
                case Mode.Drop:
                {
                    _modeT += dt;
                    float u = Mathf.Clamp01(_modeT / _dropDur);
                    Vector3 p = Vector3.Lerp(_dropFrom, _dropTo, u);
                    p.y = Mathf.Lerp(_dropFrom.y, _dropTo.y, u * u);          // gravity: the fall accelerates
                    Vector3 axis = (_downfield * 0.94f - Vector3.up * (0.34f + 0.3f * u)).normalized;   // nose down about 20 degrees, rotating on
                    _ball.SetPositionAndRotation(p, Quaternion.FromToRotation(Vector3.right, axis));
                    break;
                }
                case Mode.Spike: SpikeStep(dt); break;       // v0.10.0 BODIES: thrown down by the scorer, bounces and comes to rest
                case Mode.Official:
                {
                    Vector3 po; Quaternion ro;
                    if (PoseOfficial(out po, out ro)) ApplyAttach(po, ro, dt);
                    break;
                }
                default:
                {
                    if (_holder == null || _holder.Avatar == null) break;
                    Vector3 p; Quaternion r;
                    if (!HeldPoseNow(dt, out p, out r)) break;
                    ApplyAttach(p, r, dt);
                    break;
                }
            }
            GfxTee(); GfxGroundClamp();      // v0.11.0 QB: the tee under a kickoff ball, never below the turf
            TrackBall(dt);      // v0.10.0 BODIES: the ball's own velocity (a spike leaves the hand with it)
        }

        // blends position AND orientation from where the ball was when it attached
        void ApplyAttach(Vector3 p, Quaternion r, float dt)
        {
            if (_attachPending)
            {
                _attachOffset = _ball.position - p; _attachRot = _ball.rotation; _attachT = 0f; _attachPending = false;
                if (_attachOffset.sqrMagnitude > 9f) _attachOffset = Vector3.zero;
            }
            if (_attachT < _attachDur)
            {
                _attachT += dt;
                float u = Mathf.SmoothStep(0f, 1f, _attachT / _attachDur);
                p += _attachOffset * (1f - u);
                if (_attachArc > 0f) p += Vector3.up * (_attachArc * Mathf.Sin(Mathf.PI * u));
                r = Quaternion.Slerp(_attachRot, r, u);
            }
            _ball.SetPositionAndRotation(p, r);
        }

        bool HeldPoseNow(float dt, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            switch (_mode)
            {
                case Mode.Catch:
                {
                    _modeT += dt;
                    Vector3 pc; Quaternion rc;
                    if (!PoseCatch(_holder, _side, out pc, out rc)) return false;
                    Vector3 pk; Quaternion rk;
                    if (!PoseCarry(_holder, _side, out pk, out rk)) { pos = pc; rot = rc; return true; }
                    float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_modeT - CatchHoldSeconds) / CatchTuckSeconds));
                    pos = Vector3.Lerp(pc, pk, u); rot = Quaternion.Slerp(rc, rk, u);
                    if (_modeT > CatchHoldSeconds + CatchTuckSeconds) { _mode = Mode.Carry; }
                    return true;
                }
                case Mode.Exchange:
                {
                    _modeT += dt;
                    Vector3 pa = Vector3.zero, pb; Quaternion ra = Quaternion.identity, rb;
                    bool okA = _from != null && PoseQbHold(_from, false, BodyMotion.Carriage.Chest, out pa, out ra);
                    bool okB = PoseCarry(_holder, _side, out pb, out rb);
                    if (!okB) return false;
                    if (!okA) { pos = pb; rot = rb; return true; }
                    float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_modeT + _exchangeDur * 0.5f) / _exchangeDur));
                    pos = Vector3.Lerp(pa, pb, u); rot = Quaternion.Slerp(ra, rb, u);
                    if (_modeT >= _exchangeDur * 0.5f) _mode = Mode.Carry;
                    return true;
                }
                case Mode.Hand: return PoseRaised(_holder, _side, out pos, out rot);      // v0.10.0 BODIES: the ball rests on the palm, no longer inside the wrist
                case Mode.Relaxed: return PoseRelaxed(_holder, _side, out pos, out rot);
                case Mode.Official: return PoseOfficial(out pos, out rot);
                case Mode.QbHold: return PoseQbHold(_holder, _throwing, _qbCarriage, out pos, out rot);
                default: return PoseCarry(_holder, _side, out pos, out rot);
            }
        }

        // ------------------------------------------------------------------------------------------ poses
        static bool PoseHand(SimAgentView holder, int side, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            var av = holder.Avatar; Vector3 fwd = holder.Forward;
            var hh = side >= 0 ? av.RightHand : av.LeftHand; if (hh == null) return false;
            pos = hh.position + Vector3.up * 0.06f + fwd * 0.03f; rot = Quaternion.FromToRotation(Vector3.right, (Vector3.up * 0.8f + fwd * 0.5f).normalized);
            return true;
        }

        static bool PoseQbHold(SimAgentView holder, bool throwing, BodyMotion.Carriage carriage, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            var av = holder.Avatar; Vector3 fwd = holder.Forward;
            var th = holder.ThrowHandBone;      // v0.9.0 QBSTANCE (E-107): the throwing hand of a left-handed passer is his left
            if (th == null) return false;
            Vector3 p; Vector3 qaxis = fwd + Vector3.up * 0.25f;
            var oh = holder.OffHandBone;
            var lhand = oh != null ? oh.position : th.position;
            if (throwing) p = th.position + fwd * 0.04f + Vector3.up * 0.03f;
            else if (carriage == BodyMotion.Carriage.Ear) { p = th.position + Vector3.up * 0.06f + fwd * 0.03f; qaxis = fwd + Vector3.up * 0.45f; }
            else if (carriage == BodyMotion.Carriage.Chest) { p = (th.position + lhand) * 0.5f + fwd * 0.02f; qaxis = Vector3.up * 0.95f + fwd * 0.3f; }     // both hands, the point up
            else p = (th.position + lhand) * 0.5f + fwd * 0.04f;
            pos = p; rot = Quaternion.FromToRotation(Vector3.right, qaxis.normalized);
            return true;
        }

        /// <summary>Both hands on the ball (the catch): the ball sits between the palms, the point up and forward.</summary>
        public static float CatchBias = 1.0f;      // v0.10.0 CHOREO: how far the ball sits toward the carry-side hand instead of dead between the palms (0 = the v0.9 pose)
        static bool PoseCatch(SimAgentView holder, int side, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            var av = holder.Avatar; Vector3 fwd = holder.Forward;
            if (av.RightHand == null || av.LeftHand == null) return false;
            Vector3 a = av.RightHand.position, b = av.LeftHand.position;
            Vector3 mid = (a + b) * 0.5f; Vector3 near = side >= 0 ? a : b;
            pos = Vector3.Lerp(mid, near, CatchBias) + fwd * 0.01f;      // the hands of a catch are 0.2 to 0.25 m apart: the ball dead between them read 0.12 m from either wrist; toward one hand it sits in that palm
            Vector3 along = (Vector3.up * 0.7f + fwd * 0.6f).normalized;
            rot = Quaternion.FromToRotation(Vector3.right, along);
            return true;
        }

        /// <summary>v0.9.0 E-148: the tucked carry sits on the forearm toward the body, measured from the arm bones and the chest instead of the root axes, so it follows the arm through spins, hurdles and falls.</summary>
        static bool PoseCarry(SimAgentView holder, int side, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            var av = holder.Avatar; Vector3 fwd = holder.Forward, right = Vector3.Cross(Vector3.up, fwd);
            Transform fore = side >= 0 ? av.RightForearm : av.LeftForearm;
            Transform hand = side >= 0 ? av.RightHand : av.LeftHand;
            if (fore == null || hand == null) return false;
            Vector3 mid = Vector3.Lerp(fore.position, hand.position, 0.72f);
            Vector3 axis = hand.position - fore.position;
            axis = axis.sqrMagnitude < 1e-6f ? fwd : axis.normalized;
            Vector3 inward;
            if (av.Chest != null)
            {
                inward = av.Chest.position - mid; inward.y = 0f;
                inward = inward.sqrMagnitude > 1e-4f ? inward.normalized * 0.10f : -side * right * 0.09f;
            }
            else inward = -side * right * 0.09f;
            // tuck: the nose of the ball points a little forward and across the body
            Vector3 along = (axis * 0.7f + fwd * 0.5f - side * right * 0.15f).normalized;
            pos = mid + inward + fwd * 0.03f + Vector3.up * 0.03f;      // v0.10.0 BODIES: 10 cm in from the forearm axis (it was 6.5: the ball sat inside the forearm), a little higher: high and tight rot = Quaternion.FromToRotation(Vector3.right, along);
            return true;
        }
    }
}
