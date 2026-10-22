using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9 GFX (audit 119 / 310 / 425 / 429 / 437): the look of the ball in the air and at rest, kept in its own file so the carry / catch / exchange logic of BallFollower stays untouched.
    /// Spin derived from the kind of play (the simulation keeps no angular state): passes spiral at 3000 deg/s with a wobble on a tip or a deflection, punts spiral at 2700, kickoffs tumble at 900,
    /// field goals at 600; the long axis follows the whole velocity so the nose pitches with the arc; a lying ball sits 3 cm lower and stays where it came to rest; a thin additive trail on fast
    /// flights above 1.5 m and a landing ring under a punt / kickoff in the air (the returner cue of All-Pro 2K8).
    /// </summary>
    public sealed partial class BallFollower
    {
        /// <summary>What kind of play this is (set by the stage every frame before SetFree).</summary>
        public PlayKind Kind = PlayKind.Scrimmage;
        /// <summary>Spin about the long axis (degrees per second) the view uses for the ball in the air.</summary>
        public float SpinDegPerSecond { get; private set; }
        public const float RestSink = 0.03f;
        float _wobble, _wobbleT; Vector3 _lastVel; BallState _lastState;
        TrailRenderer _trail; Transform _ring; Vector3 _landing; bool _landingOn; float _ringPulse;

        /// <summary>Rotation of a passed / pitched / snapped ball (GFX-119).</summary>
        Quaternion GfxPassRot(Vector3 vel, BallState state, float dt)
        {
            float rate = state == BallState.Pass ? BallSpinHint.PassSpin : 700f;      // v0.9.0 QBSTANCE (E-116): by throw style
            _spin += rate * dt; SpinDegPerSecond = rate;
            Vector3 vn = vel.normalized;
            // a tip or a deflection (the flight direction jumps) starts a wobble that dies out in about 1.5 s; a long throw wobbles a little at the end of its flight
            if (_lastState == state && _lastVel.sqrMagnitude > 1f && Vector3.Angle(_lastVel, vel) > 5f) { _wobble = 1f; _wobbleT = 0f; }
            _wobbleT += dt; _wobble = Mathf.MoveTowards(_wobble, 0f, dt / 1.5f);
            float wob = (_wobble * 16f + (state == BallState.Pass ? 1.5f : 0f)) * Mathf.Sin(_wobbleT * 17f);
            Vector3 perp = Vector3.Cross(vn, Vector3.up); if (perp.sqrMagnitude < 1e-4f) perp = Vector3.right;
            _lastVel = vel;
            return Quaternion.AngleAxis(wob, perp.normalized) * Quaternion.AngleAxis(_spin, vn) * Quaternion.FromToRotation(Vector3.right, vn);
        }

        /// <summary>Rotation of a kicked ball (GFX-310 / 429): a punt spirals about its long axis, a kickoff and a field goal tumble end over end; the long axis follows the whole velocity.</summary>
        Quaternion GfxKickRot(Vector3 vel, Vector3 flat, float dt)
        {
            Vector3 vn = vel.sqrMagnitude > 0.25f ? vel.normalized : (flat.sqrMagnitude > 0.01f ? flat.normalized : Vector3.forward);
            if (Kind == PlayKind.Punt)
            {
                _spin += 2700f * dt; SpinDegPerSecond = 2700f;
                return Quaternion.AngleAxis(_spin, vn) * Quaternion.FromToRotation(Vector3.right, vn);
            }
            float rate = Kind == PlayKind.Kickoff ? 900f : 600f;
            _spin += rate * dt; SpinDegPerSecond = 0f;
            Vector3 lat = Vector3.Cross(Vector3.up, vn); if (lat.sqrMagnitude < 1e-4f) lat = Vector3.right;
            return Quaternion.AngleAxis(_spin, lat.normalized) * Quaternion.FromToRotation(Vector3.right, vn);
        }

        /// <summary>Rotation of a ball that came to rest (GFX-425): it lies flat along the way it was last pointing / rolling instead of swinging toward downfield.</summary>
        Quaternion GfxRestRot(float dt)
        {
            Vector3 along = _rot * Vector3.right; along.y = 0f;
            if (along.sqrMagnitude < 0.01f) along = new Vector3(_downfield.x, 0f, _downfield.z);
            return Quaternion.Slerp(_rot, Quaternion.FromToRotation(Vector3.right, along.normalized), 1f - Mathf.Exp(-dt * 9f));
        }

        /// <summary>The sim rests the ball centre on its 11.9 cm sphere radius; a lying football sits about 3 cm lower (GFX-425).</summary>
        Vector3 GfxRestDrop() { return _atRest && _state == BallState.Loose ? Vector3.down * RestSink : Vector3.zero; }

        void EnsureTrail()
        {
            if (_trail != null || _ball == null) return;
            var a = PixLook.Assets; var sh = a != null ? a.fxShader : null;
            if (sh == null) return;
            var go = new GameObject("FlightTrail"); go.transform.SetParent(_ball, false);
            _trail = go.AddComponent<TrailRenderer>();
            var m = new Material(sh) { name = "BallTrail", hideFlags = HideFlags.HideAndDontSave };
            m.SetFloat("_SrcBlend", 5f); m.SetFloat("_DstBlend", 1f); m.SetFloat("_ZTest", 4f); m.SetColor("_Color", new Color(1f, 0.96f, 0.88f, 0.25f)); m.SetFloat("_Intensity", 0.9f);
            _trail.sharedMaterial = m; _trail.time = 0.35f; _trail.startWidth = 0.07f; _trail.endWidth = 0f; _trail.minVertexDistance = 0.15f; _trail.emitting = false;
            _trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; _trail.receiveShadows = false; _trail.alignment = LineAlignment.View;
            _trail.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, new[] { new GradientAlphaKey(0.28f, 0f), new GradientAlphaKey(0f, 1f) });
            _trail.colorGradient = g;
        }

        void EnsureRing()
        {
            if (_ring != null) return;
            var rt = PixRuntimeAssets.Load();
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "LandingRing"; Destroy(q.GetComponent<Collider>());
            _ring = q.transform; _ring.SetParent(transform.parent, false);
            _ring.rotation = Quaternion.Euler(90f, 0f, 0f); _ring.localScale = new Vector3(2.4f, 2.4f, 1f);
            var r = q.GetComponent<Renderer>();
            if (rt != null && rt.ringMarker != null) r.sharedMaterial = rt.ringMarker;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
            q.SetActive(false);
        }

        /// <summary>Where the ball in the air will come down (world space) and whether to show the ring there (punts and kickoffs only).</summary>
        public void SetLanding(Vector3 world, bool show)
        {
            _landing = world; _landingOn = show;
            if (show) EnsureRing();
            if (_ring != null && _ring.gameObject.activeSelf != show) _ring.gameObject.SetActive(show);
        }

        /// <summary>Per frame (LateUpdate): the flight trail and the landing ring (GFX-437).</summary>
        void GfxEffects()
        {
            bool air = _mode == Mode.Free && (_state == BallState.Pass || _state == BallState.Kick) && _vel.sqrMagnitude > 14f * 14f && _pos.y > 1.5f;
            if (air) EnsureTrail();
            if (_trail != null)
            {
                if (_mode != Mode.Free || _lastState != _state) _trail.Clear();
                _trail.emitting = air;
            }
            if (_ring != null && _landingOn)
            {
                _ringPulse += Time.deltaTime;
                float k = 1f + 0.12f * Mathf.Sin(_ringPulse * 6f);
                _ring.position = new Vector3(_landing.x, 0.04f, _landing.z); _ring.localScale = new Vector3(2.4f * k, 2.4f * k, 1f);
            }
            _lastState = _state;
        }
    }
}
