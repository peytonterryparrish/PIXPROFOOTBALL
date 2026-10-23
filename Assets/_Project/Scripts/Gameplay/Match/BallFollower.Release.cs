using PixFootball.Art;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 BODIES (playtest note 9): the ball is not glued to the forearm. Besides the tucked carry (five points of pressure, in play) the ball now has
    /// <b>Relaxed</b> (hanging loose in one hand while the carrier gets up and walks off after the whistle), <b>Hand</b> (resting on the palm, raised, for the start of a celebration),
    /// <b>Spike</b> (thrown down by the scorer at a set moment: falls, bounces, tumbles and lies still) and <b>Official</b> (travels to a referee's hand and stays in it).
    /// </summary>
    public sealed partial class BallFollower
    {
        /// <summary>Height of a lying football's centre above the turf (m).</summary>
        public const float RestHeight = 0.089f;
        Vector3 _prevBallPos, _ballVel; bool _prevHave;
        Vector3 _spikeVel, _spikeSpinAxis; float _spikeSpin; bool _spikeRest; float _spikeY;
        Transform _officialHand; Vector3 _officialFwd;

        void TrackBall(float dt)
        {
            if (_ball == null || dt < 1e-5f) return;
            if (_prevHave) _ballVel = Vector3.Lerp(_ballVel, (_ball.position - _prevBallPos) / dt, 0.5f);
            _prevBallPos = _ball.position; _prevHave = true;
        }

        /// <summary>The ball's own velocity (m/s), smoothed.</summary>
        public Vector3 BallVelocity { get { return _ballVel; } }
        /// <summary>QA: the ball hangs loose in a hand (not the tuck).</summary>
        public bool IsRelaxed { get { return _mode == Mode.Relaxed; } }
        /// <summary>The ball was thrown down and lies on the turf or is still bouncing.</summary>
        public bool IsSpiked { get { return _mode == Mode.Spike; } }
        /// <summary>The ball is in a referee's hand.</summary>
        public bool IsWithOfficial { get { return _mode == Mode.Official; } }
        /// <summary>The spiked ball has come to rest.</summary>
        public bool SpikeAtRest { get { return _mode == Mode.Spike && _spikeRest; } }

        /// <summary>The carrier lets the tuck go: the ball hangs in the hand at his side (cross-fades from wherever it is over 0.35 s).</summary>
        public void SetRelaxed(SimAgentView holder, int side)
        {
            if (_mode == Mode.Relaxed && _holder == holder) { _side = side; return; }
            MarkAttach(0.35f, 0f);
            _mode = Mode.Relaxed; _holder = holder; _side = side;
        }

        /// <summary>Throws the ball down from the hand: it leaves with the hand's own velocity plus a downward push and a little spin.</summary>
        public void SetSpike(SimAgentView holder, int side, float turfY)
        {
            if (_mode == Mode.Spike) return;
            Vector3 fwd = holder != null ? holder.Forward : Vector3.forward;
            _spikeVel = _ballVel * 0.6f + Vector3.down * 4.8f + fwd * 1.1f;
            _spikeVel.y = Mathf.Min(_spikeVel.y, -3.2f);
            _spikeSpinAxis = Vector3.Cross(Vector3.up, fwd); if (_spikeSpinAxis.sqrMagnitude < 0.01f) _spikeSpinAxis = Vector3.right;
            _spikeSpin = 520f; _spikeRest = false; _spikeY = turfY;
            _mode = Mode.Spike; _holder = null; _relT = 1f; _attachT = 1f; _attachPending = false;
        }

        /// <summary>The ball goes to a referee's hand (over <paramref name="seconds"/>, with a small arc) and stays there.</summary>
        public void SetOfficial(Transform hand, Vector3 officialForward, float seconds = 0.34f)
        {
            if (hand == null) return;
            if (_mode == Mode.Official && _officialHand == hand) return;
            MarkAttach(seconds, 0.10f);
            _mode = Mode.Official; _holder = null; _officialHand = hand; _officialFwd = officialForward;
        }

        // ------------------------------------------------------------------------------------------ poses
        /// <summary>The hand direction towards the fingers (the middle finger's first bone), or the body's forward when the rig has none.</summary>
        static Vector3 FingerDir(SimAgentView holder, int side, Transform hand)
        {
            var an = holder.Avatar != null ? holder.Avatar.Animator : null;
            var f = an != null && an.isHuman ? an.GetBoneTransform(side >= 0 ? HumanBodyBones.RightMiddleProximal : HumanBodyBones.LeftMiddleProximal) : null;
            Vector3 d = f != null ? f.position - hand.position : Vector3.zero;
            return d.sqrMagnitude > 1e-6f ? d.normalized : holder.Forward;
        }

        /// <summary>The ball resting on the open palm, raised (the start of a celebration, a pre-spike): above the hand, long axis forward.</summary>
        static bool PoseRaised(SimAgentView holder, int side, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            var av = holder.Avatar; Vector3 fwd = holder.Forward;
            var hh = side >= 0 ? av.RightHand : av.LeftHand; if (hh == null) return false;
            Vector3 along = FingerDir(holder, side, hh);
            pos = hh.position + along * 0.075f + Vector3.up * 0.085f;
            Vector3 flat = new Vector3(along.x, 0f, along.z); flat = flat.sqrMagnitude > 0.05f ? flat.normalized : fwd;
            rot = Quaternion.FromToRotation(Vector3.right, (flat * 0.85f + fwd * 0.35f + Vector3.up * 0.18f).normalized);
            return true;
        }

        /// <summary>The ball hanging loose in one hand beside the thigh: gripped from above, fingers round the middle, nose forward and a little down.</summary>
        static bool PoseRelaxed(SimAgentView holder, int side, out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            var av = holder.Avatar; Vector3 fwd = holder.Forward, right = Vector3.Cross(Vector3.up, fwd);
            var hh = side >= 0 ? av.RightHand : av.LeftHand; if (hh == null) return false;
            Vector3 along = FingerDir(holder, side, hh);
            pos = hh.position + along * 0.05f + Vector3.down * 0.075f + right * (side >= 0 ? 0.01f : -0.01f);
            Vector3 axis = (fwd * 0.85f + Vector3.down * 0.35f + along * 0.25f).normalized;
            rot = Quaternion.FromToRotation(Vector3.right, axis);
            return true;
        }

        bool PoseOfficial(out Vector3 pos, out Quaternion rot)
        {
            pos = Vector3.zero; rot = Quaternion.identity;
            if (_officialHand == null) return false;
            Vector3 fwd = _officialFwd.sqrMagnitude > 0.01f ? _officialFwd.normalized : Vector3.forward;
            pos = _officialHand.position + Vector3.down * 0.07f + fwd * 0.05f;
            rot = Quaternion.FromToRotation(Vector3.right, (fwd * 0.9f + Vector3.down * 0.3f).normalized);
            return true;
        }

        // ------------------------------------------------------------------------------------------ the spike
        void SpikeStep(float dt)
        {
            Vector3 p = _ball.position; Quaternion r = _ball.rotation;
            if (!_spikeRest)
            {
                _spikeVel += Vector3.down * 9.81f * dt;
                p += _spikeVel * dt;
                float floor = _spikeY + RestHeight;
                if (p.y <= floor && _spikeVel.y < 0f)
                {
                    p.y = floor;
                    if (_spikeVel.y < -1.4f) { _spikeVel.y *= -0.42f; _spikeVel.x *= 0.72f; _spikeVel.z *= 0.72f; _spikeSpin *= 0.65f; }
                    else
                    {
                        _spikeVel.y = 0f;
                        float k = Mathf.Exp(-5f * dt); _spikeVel.x *= k; _spikeVel.z *= k; _spikeSpin *= k;
                        if (new Vector2(_spikeVel.x, _spikeVel.z).magnitude < 0.12f) { _spikeRest = true; _spikeVel = Vector3.zero; }
                    }
                }
                r = Quaternion.AngleAxis(_spikeSpin * dt, _spikeSpinAxis) * r;
            }
            else
            {
                // lies on its side along the way it ended up pointing
                Vector3 along = r * Vector3.right; along.y = 0f;
                if (along.sqrMagnitude < 0.01f) along = Vector3.forward;
                r = Quaternion.Slerp(r, Quaternion.FromToRotation(Vector3.right, along.normalized), 1f - Mathf.Exp(-dt * 9f));
                p.y = _spikeY + RestHeight;
            }
            _ball.SetPositionAndRotation(p, r);
        }
    }
}
