using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// The football in the stadium: follows the simulated ball in flight (spiral / tumble / bounce), sits on the spot before the snap
    /// and is attached to the carrier's arm (tucked) or the quarterback's hands while held. Poses are applied in LateUpdate so they
    /// follow the animated bones.
    /// </summary>
    [DefaultExecutionOrder(150)]
    public sealed class BallFollower : MonoBehaviour
    {
        enum Mode { Free, Carry, QbHold, Tee }

        Transform _ball;
        Mode _mode = Mode.Free;
        Vector3 _pos, _vel, _downfield = Vector3.forward;
        BallState _state;
        bool _atRest;
        Quaternion _rot = Quaternion.identity;
        float _spin;
        SimAgentView _holder;
        int _side = 1;
        bool _throwing;

        /// <summary>World position of the ball after the last LateUpdate (cameras and markers read it).</summary>
        public Vector3 Position { get { return _ball != null ? _ball.position : _pos; } }

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
            _mode = Mode.Free; _holder = null; _pos = pos; _vel = vel; _state = state; _atRest = atRest; _downfield = downfield;
            Vector3 flat = new Vector3(vel.x, 0f, vel.z);
            switch (state)
            {
                case BallState.Pass: case BallState.Pitch: case BallState.Snap:
                {
                    // spiral: long axis along the velocity, rolling about it
                    if (vel.sqrMagnitude > 0.5f)
                    {
                        _spin += (state == BallState.Pass ? 3000f : 700f) * dt;
                        _rot = Quaternion.AngleAxis(_spin, vel.normalized) * Quaternion.FromToRotation(Vector3.right, vel.normalized);
                    }
                    break;
                }
                case BallState.Kick:
                {
                    // end-over-end about the lateral axis
                    _spin += 520f * dt;
                    Vector3 lat = flat.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
                    Vector3 axis0 = flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward;
                    _rot = Quaternion.AngleAxis(_spin, lat) * Quaternion.FromToRotation(Vector3.right, axis0);
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
                    else _rot = Quaternion.Slerp(_rot, Quaternion.FromToRotation(Vector3.right, new Vector3(_downfield.x, 0f, _downfield.z)), 1f - Mathf.Exp(-dt * 6f));
                    break;
                }
                default:
                    _rot = Quaternion.Slerp(_rot, Quaternion.FromToRotation(Vector3.right, new Vector3(downfield.x, 0f, downfield.z).normalized), 1f - Mathf.Exp(-dt * 12f));
                    break;
            }
        }

        public void SetTee(Vector3 pos, Vector3 downfield)
        {
            _mode = Mode.Tee; _pos = pos; _downfield = downfield; _holder = null;
        }

        public void SetCarry(SimAgentView holder, int side) { _mode = Mode.Carry; _holder = holder; _side = side; }
        public void SetQbHold(SimAgentView holder, bool throwing) { _mode = Mode.QbHold; _holder = holder; _throwing = throwing; }

        void LateUpdate()
        {
            if (_ball == null) return;
            switch (_mode)
            {
                case Mode.Free:
                    _ball.SetPositionAndRotation(_pos, _rot);
                    break;
                case Mode.Tee:
                {
                    Vector3 axis = (Vector3.up * 0.9f + _downfield * 0.45f).normalized;
                    _ball.SetPositionAndRotation(_pos + Vector3.up * 0.1f, Quaternion.FromToRotation(Vector3.right, axis));
                    break;
                }
                case Mode.Carry:
                case Mode.QbHold:
                    if (_holder == null || _holder.Avatar == null) break;
                    PoseHeld();
                    break;
            }
        }

        void PoseHeld()
        {
            var av = _holder.Avatar;
            Vector3 fwd = _holder.Forward, right = Vector3.Cross(Vector3.up, fwd);
            if (_mode == Mode.QbHold)
            {
                if (av.RightHand == null) return;
                Vector3 p;
                if (_throwing) p = av.RightHand.position + fwd * 0.04f + Vector3.up * 0.03f;
                else p = (av.RightHand.position + (av.LeftHand != null ? av.LeftHand.position : av.RightHand.position)) * 0.5f + fwd * 0.04f;
                _ball.SetPositionAndRotation(p, Quaternion.FromToRotation(Vector3.right, (fwd + Vector3.up * 0.25f).normalized));
                return;
            }
            Transform fore = _side >= 0 ? av.RightForearm : av.LeftForearm;
            Transform hand = _side >= 0 ? av.RightHand : av.LeftHand;
            if (fore == null || hand == null) return;
            Vector3 mid = Vector3.Lerp(fore.position, hand.position, 0.75f);
            Vector3 inward = -_side * right * 0.06f + fwd * 0.05f;
            Vector3 axis = (hand.position - fore.position);
            axis = axis.sqrMagnitude < 1e-6f ? fwd : axis.normalized;
            // tuck: the nose of the ball points a little forward and across the body
            Vector3 along = (axis * 0.7f + fwd * 0.5f - _side * right * 0.15f).normalized;
            _ball.SetPositionAndRotation(mid + inward, Quaternion.FromToRotation(Vector3.right, along));
        }
    }
}
