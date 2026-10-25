using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.12.0 GAIT (candidate 2): start / stop / cut mechanics of free locomotion, the part that depends only on the simulation (the sim is authoritative, the view only poses).
    /// The simulated velocity goes in, three things come out: the trunk lean the acceleration asks for (a real first step leans 30-40 degrees and straightens over 3-5 steps, a stop sits back), the hip sink that goes with a
    /// start, a stop and a cut, and the cut severity (heading rate at speed). The pose layer is <c>BodyMotion.ApplyGaitLean</c>: it measures the lean the playing clip already has and adds only the missing difference,
    /// so it works on top of whatever clip the matcher plays. Pure math, unit tested (<c>GaitLeanTests</c>).
    /// </summary>
    public sealed class GaitLean
    {
        /// <summary>QA: -pixGaitOff switches the whole layer off (the v0.11 behaviour).</summary>
        public static bool Enabled = true;
        /// <summary>QA: -pixGaitNoCutDrop / -pixGaitNoSink leave the hip sink out.</summary>
        public static bool NoSink;
        public const float G = 9.81f;

        // ------------------------------------------------------------------------------------------ pure math
        /// <summary>Total trunk lean (deg, forward +) a forward acceleration asks for: about the angle of the force line (atan(a / g)) with a little more, never past 40.</summary>
        public static float AccelLean(float accel) { return accel <= 0f ? 0f : Mathf.Min(40f, 4f + 1.1f * Mathf.Atan(accel / G) * Mathf.Rad2Deg); }

        /// <summary>Total trunk lean (deg, forward +, so negative = back) a braking deceleration asks for: upright to slightly back, the further the harder the stop (never past -14).</summary>
        public static float BrakeLean(float accel) { return accel >= 0f ? 4f : Mathf.Max(-14f, 4f - 0.55f * Mathf.Atan(-accel / G) * Mathf.Rad2Deg); }

        /// <summary>0..1: how much of the start (positive) or stop (negative) lean applies at a smoothed longitudinal acceleration.</summary>
        public static float AccelWeight(float accel) { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.2f, 3.0f, accel)); }

        /// <summary>0..1: the body travels along its facing (start / stop lean makes no sense for a back-pedal or a side shuffle). Angle in degrees between the velocity and the body forward.</summary>
        public static float AlignWeight(float angleDeg) { return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(40f, 75f, angleDeg)); }

        /// <summary>0..1: there is a body moving (or just about to) at all.</summary>
        public static float SpeedWeight(float speed) { return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 1.0f, speed)); }

        /// <summary>The lean difference (deg) to add to a clip that leans <paramref name="clipLean"/> so the body leans as the acceleration asks (>= 0 for a start, <= 0 for a stop).</summary>
        public static float Delta(float accel, float clipLean)
        {
            if (accel > 0f) return Mathf.Max(0f, AccelLean(accel) - clipLean) * AccelWeight(accel);
            if (accel < 0f) return Mathf.Min(0f, BrakeLean(accel) - clipLean) * AccelWeight(-accel);
            return 0f;
        }

        /// <summary>Cut severity 0..1+: how hard the simulated path turns (heading rate, deg/s) at speed. 100 deg/s is a gentle curve, 300 deg/s a plant-and-cut.</summary>
        public static float CutSeverity(float headingRate, float speed)
        {
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(50f, 200f, Mathf.Abs(headingRate))) * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1.8f, 3.5f, speed));
        }

        /// <summary>Hip sink (m) of a stop, a start and a cut at their weights. The strongest of the three wins (they do not add).</summary>
        public static float Sink(float stopW, float startW, float cutSev)
        {
            return Mathf.Max(0.055f * stopW, Mathf.Max(0.025f * startW, 0.07f * cutSev));
        }

        // ------------------------------------------------------------------------------------------ per body state
        Vector3 _prevVel; bool _have; float _prevHdg, _hdgRate, _accel, _speed; float _align = 1f;
        float _clipLean, _clipHave, _delta, _sink, _sinkTarget;
        float _stopW, _startW, _cutSev, _wantTotal, _wantW;

        /// <summary>Smoothed longitudinal acceleration of the simulated velocity (m/s2, + = speeding up).</summary>
        public float Accel { get { return _accel; } }
        /// <summary>Heading rate of the simulated path (deg/s, + = turning right).</summary>
        public float HeadingRate { get { return _hdgRate; } }
        public float CutSev { get { return _cutSev; } }
        /// <summary>Lean (deg) this layer adds to the clip's own lean right now (the smoothed value, before the layer weight).</summary>
        public float Delta0 { get { return _delta; } }
        /// <summary>The hip sink (m) it asks FootPlant for.</summary>
        public float SinkM { get { return _sink; } }
        public float Align { get { return _align; } }

        public float WantTotal { get { return _wantTotal; } }
        /// <summary>For a layer that sets its own lean target (the ball carrier style): the target moved toward what the acceleration asks (more forward for a start, further back for a stop), by the layer weight.</summary>
        public float Merge(float want)
        {
            if (float.IsNaN(_wantTotal)) return want;
            return Mathf.Lerp(want, _accel > 0f ? Mathf.Max(want, _wantTotal) : Mathf.Min(want, _wantTotal), _wantW);
        }

        public void Reset() { _have = false; _accel = 0f; _hdgRate = 0f; _delta = 0f; _sink = 0f; _clipHave = 0f; _cutSev = 0f; _stopW = _startW = 0f; _wantTotal = float.NaN; }

        /// <summary>Every frame with the simulated velocity (m/s) and the body forward.</summary>
        public void Observe(Vector3 velocity, Vector3 forward, float dt)
        {
            if (dt <= 1e-5f) return;
            velocity.y = 0f; forward.y = 0f; forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.forward;
            float sp = velocity.magnitude;
            if (!_have) { _prevVel = velocity; _speed = sp; _prevHdg = sp > 0.5f ? Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg : Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg; _have = true; if (sp > 0.5f) _align = AlignWeight(Vector3.Angle(velocity, forward)); }
            float rawAcc = (sp - _speed) / dt; _speed = sp; _prevVel = velocity;
            _accel = LeanMath.Smooth(_accel, Mathf.Clamp(rawAcc, -14f, 14f), dt, 0.08f);
            if (sp > 1.0f)
            {
                float h = Mathf.Atan2(velocity.x, velocity.z) * Mathf.Rad2Deg;
                float rate = Mathf.DeltaAngle(_prevHdg, h) / dt; _prevHdg = h;
                _hdgRate = LeanMath.Smooth(_hdgRate, Mathf.Clamp(rate, -900f, 900f), dt, 0.045f);
                float ang = Vector3.Angle(velocity, forward);
                _align = LeanMath.Smooth(_align, AlignWeight(ang), dt, 0.06f);
            }
            else { _hdgRate = LeanMath.Smooth(_hdgRate, 0f, dt, 0.05f); _align = LeanMath.Smooth(_align, sp > 0.3f ? AlignWeight(Vector3.Angle(velocity, forward)) : 1f, dt, 0.10f); }      // slow: a back-pedal coming to a stop stays a back-pedal
        }

        /// <summary>Solves this frame: the lean difference to add to a clip that leans <paramref name="clipLeanNow"/> degrees, and the hip sink. <paramref name="own"/> = 0..1 layer weight (0 for a QB, a body whose legs are not the locomotion's).</summary>
        public float Solve(float clipLeanNow, float dt, float own)
        {
            if (!_have) return 0f;
            if (own <= 0.001f) { _clipHave = 0f; _delta = LeanMath.Smooth(_delta, 0f, dt, 0.1f); _sink = LeanMath.Smooth(_sink, 0f, dt, 0.1f); _startW = _stopW = _cutSev = 0f; _wantTotal = float.NaN; return 0f; }
            // the clip's own lean, slowly: its stride wobble stays, the mean is what the acceleration is measured against
            if (_clipHave < 0.5f) { _clipLean = clipLeanNow; _clipHave = 1f; } else _clipLean = LeanMath.Smooth(_clipLean, clipLeanNow, dt, 0.30f);
            float gate = SpeedWeight(_speed) * _align * own;
            float a = _accel;
            float dT = Delta(a, _clipLean) * gate;
            _wantTotal = gate > 0.01f && a != 0f ? (a > 0f ? AccelLean(a) : BrakeLean(a)) : float.NaN; _wantW = AccelWeight(Mathf.Abs(a)) * gate;
            float tau = Mathf.Abs(dT) > Mathf.Abs(_delta) ? 0.05f : 0.13f;
            float next = LeanMath.Smooth(_delta, dT, dt, tau);
            _delta = Mathf.Clamp(Mathf.MoveTowards(_delta, next, 420f * dt), -40f, 40f);
            _startW = a > 0f ? AccelWeight(a) * gate : 0f; _stopW = a < 0f ? AccelWeight(-a) * gate : 0f;
            _cutSev = CutSeverity(_hdgRate, _speed) * own;
            _sinkTarget = NoSink ? 0f : Sink(_stopW, _startW, _cutSev);
            _sink = LeanMath.Smooth(_sink, _sinkTarget, dt, _sinkTarget > _sink ? 0.05f : 0.16f);
            return _delta;
        }
    }
}
