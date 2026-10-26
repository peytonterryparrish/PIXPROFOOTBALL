using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 CHOREO: the legs of a body that the simulation pushes slowly while its upper body is in a hand fight (pass rush, run block, jam, shed). Below about 1.7 m/s the locomotion has no gait of its own and the
    /// animated feet slid over the turf (0.8 m/s on average, engaged linemen skating). Here every foot is pinned to a world position (a stance) and, when the body has moved the animated foot more than
    /// <see cref="Drift"/> away from it, that foot steps: it swings to the animated foot's position a little ahead along the push over <see cref="Dur"/> s with a small lift, while the other foot stays planted.
    /// The result is short choppy steps driven by the push and feet that do not move while they are on the turf. It runs on the final pose through two-bone IK (ankle only, the foot keeps its world rotation),
    /// weighted in and out over 0.1 / 0.15 s. No random numbers.
    /// </summary>
    public sealed class EngageStepper
    {
        /// <summary>
        /// v0.12.0 CONTACT: a foot steps when the pelvis leaves the support area (the ground projection of the pelvis further than <see cref="SupportR"/> from the line between the two anchored feet), when the leg is stretched
        /// (<see cref="TriggerShare"/> of its reach) or, as a safety, when the animated foot is <see cref="Drift"/> away from its anchor. A foot that has just landed stays down for <see cref="Hold"/> s (no flutter). The swing
        /// lifts the foot <see cref="LiftH"/> over the animated ankle height before it travels (toe-off) and sets it down at the end (touch-down), so the foot is OFF the turf for the whole travel: planted means planted, a step is a step.
        /// </summary>
        public const float Drift = 0.34f, Dur = 0.20f, Lead = 0.08f, LiftH = 0.12f, ReachFrac = 0.97f, SupportR = 0.22f, Hold = 0.12f, TravelFrom = 0.24f, TravelTo = 0.76f;
        /// <summary>QA: the v0.11 contact behaviour for the A/B (<c>-pixV011Contact</c>): the old stepper (20 cm drift trigger, 6 cm lift), the old caps, no wrap steps, no pad hands (with <c>-pixOldWalkIn</c> for the walk-in).</summary>
        public static readonly bool V011 = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixV011Contact") >= 0;
        /// <summary>v0.12.0: the v0.11 stepper (small lift, 20 cm drift, no press, no pelvis correction) for the A/B and for the get-off and stumble windows, which are not contacts; the new design is for engaged / blocking / wrapping bodies.</summary>
        bool Old { get { return V011 || !_contact; } }
        bool _contact = true;

        sealed class F { public Transform U, L, E, Toe; public bool Have, Swing; public Vector3 Anchor, From; public float T, Down, Yaw; }
        readonly F _l = new F(), _r = new F();
        readonly Animator _a; readonly bool _ok; readonly Transform _hips;
        float _w;
        float _groundY = float.NaN;
        /// <summary>v0.11.0: the ankle never goes lower than this over the turf (sole height of the foot model): blocks and hand fights showed the soles 5 to 10 cm into the grass (CRITICAL log).</summary>
        public const float AnkleFloor = 0.085f;
        public static bool FloorClamp = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoAnkleFloor") < 0;
        /// <summary>v0.13.0: the toe bone stays this high (m) over the turf; a foot never points further than MaxToeDown degrees below the horizon (the clip pitches a toe-off foot to 75 and more); <c>-pixNoPitchFloor</c> (and <c>-pixV012Stance</c>) restore the v0.12 flat floor.</summary>
        public const float ToeFloor = 0.03f, MaxToeDown = 62f, MaxLift = 0.24f;
        public static readonly bool NoPitchFloor = StanceRules.V012 || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoPitchFloor") >= 0;
        Vector3 _outL, _outR, _pole; Quaternion _rotL, _rotR; bool _haveOut; float _speed;
        /// <summary>The feet are never held further apart (m, on the turf) than the animation has them plus this slack, nor more than <see cref="MinSpread"/> where the animation is narrower; and a foot never reaches past this share of the leg.</summary>
        public const float AirHeight = 0.22f;
        // ---- v0.12.0 (follow-up): the DRAWN sole. The flat-foot rest heights of FootPlant do not describe a pitched foot (the toe tip or the heel edge of the shoe is the lowest point, up to 10 cm off the bone model).
        // Once per body the skinned mesh is baked and the vertices of the foot (within 28 cm of the ankle, not above it) are thinned on a 3 cm grid in the foot bone's own space; the height of the sole is the lowest of
        // those points in the world (about 100 dot products per foot): exact for any foot pitch, any of the 10 body avatars.
        Vector3[] _soleL, _soleR, _soleLT, _soleRT;      // foot-bone points and toe-bone points (the part of the shoe in front of the toe bone flexes with it) of each foot
        public bool HasDrawnSole { get { return _soleL != null && _soleR != null && _soleL.Length > 0 && _soleR.Length > 0; } }
        static readonly System.Collections.Generic.List<Vector3> _bakeVerts = new System.Collections.Generic.List<Vector3>();
        void BuildSole(Animator a)
        {
            try
            {
                var smrs = a.GetComponentsInChildren<SkinnedMeshRenderer>(false);
                var gl = new System.Collections.Generic.Dictionary<long, Vector4>(); var gr = new System.Collections.Generic.Dictionary<long, Vector4>();
                var glt = new System.Collections.Generic.Dictionary<long, Vector4>(); var grt = new System.Collections.Generic.Dictionary<long, Vector4>();
                var mesh = new Mesh(); Vector3 cl = _l.E.position, cr = _r.E.position; const float R2 = 0.28f * 0.28f, Cell = 0.03f;
                for (int s = 0; s < smrs.Length; s++)
                {
                    var smr = smrs[s]; if (smr == null || !smr.enabled || !smr.gameObject.activeInHierarchy) continue;
                    smr.BakeMesh(mesh, true); mesh.GetVertices(_bakeVerts);
                    var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
                    for (int k = 0; k < _bakeVerts.Count; k++)
                    {
                        Vector3 w = m.MultiplyPoint3x4(_bakeVerts[k]);
                        Thin(gl, glt, _l, w, cl, R2, Cell); Thin(gr, grt, _r, w, cr, R2, Cell);
                    }
                }
                Object.Destroy(mesh);
                _soleL = Flatten(gl); _soleR = Flatten(gr); _soleLT = Flatten(glt); _soleRT = Flatten(grt);
            }
            catch (System.Exception) { _soleL = _soleR = _soleLT = _soleRT = null; }
        }
        static void Thin(System.Collections.Generic.Dictionary<long, Vector4> d, System.Collections.Generic.Dictionary<long, Vector4> dt, F f, Vector3 w, Vector3 ankle, float r2, float cell)
        {
            float dx = w.x - ankle.x, dz = w.z - ankle.z; if (dx * dx + dz * dz > r2 || w.y > ankle.y + 0.02f) return;
            Transform bone = f.E; var dict = d;
            if (f.Toe != null) { Vector3 tv = f.Toe.position - f.E.position; float tl = tv.magnitude; if (tl > 0.02f && Vector3.Dot(w - f.E.position, tv / tl) > tl - 0.01f) { bone = f.Toe; dict = dt; } }
            Vector3 p = bone.InverseTransformPoint(w);
            long key = ((long)Mathf.RoundToInt(p.x / cell) & 0xFFFFF) | (((long)Mathf.RoundToInt(p.y / cell) & 0xFFFFF) << 20) | (((long)Mathf.RoundToInt(p.z / cell) & 0xFFFFF) << 40);
            Vector4 old; if (!dict.TryGetValue(key, out old) || w.y < old.w) dict[key] = new Vector4(p.x, p.y, p.z, w.y);
        }
        static Vector3[] Flatten(System.Collections.Generic.Dictionary<long, Vector4> d)
        {
            var a = new Vector3[d.Count]; int i = 0; foreach (var v in d.Values) a[i++] = new Vector3(v.x, v.y, v.z); return a;
        }
        /// <summary>World height of the lowest drawn sole point of this foot (foot-bone and toe-bone points) in its current pose.</summary>
        float DrawnSole(F f, bool left)
        {
            float lo = Lowest(left ? _soleL : _soleR, f.E);
            var tp = left ? _soleLT : _soleRT; if (tp != null && tp.Length > 0 && f.Toe != null) lo = Mathf.Min(lo, Lowest(tp, f.Toe));
            return lo;
        }
        static float Lowest(Vector3[] pts, Transform bone)
        {
            Matrix4x4 m = bone.localToWorldMatrix; float lo = float.PositiveInfinity;
            for (int i = 0; i < pts.Length; i++) { float y = m.m10 * pts[i].x + m.m11 * pts[i].y + m.m12 * pts[i].z + m.m13; if (y < lo) lo = y; }
            return lo;
        }
        /// <summary>v0.12.0: the lowest point of the sole (ankle or toe) this high over the turf makes a foot "in the air" (follows the animation); before, the ankle height alone: a heel-up foot with the toe on the turf counted as in the air and dragged its toe along the grass.</summary>
        public const float AirSole = 0.10f;
        bool InAir(F f, bool left, Vector3 anim, float groundY)
        {
            if (Old || !HasDrawnSole) return anim.y - groundY > AirHeight;
            return DrawnSole(f, left) - groundY > AirSole;
        }
        public const float MinSpread = 0.62f, SpreadSlack = 0.08f, ReachShare = 0.92f, TriggerShare = 0.90f;
        /// <summary>QA: steps taken (swings completed).</summary>
        public int Steps;
        public float Weight { get { return _w; } }
        /// <summary>QA: distance (m) of each ankle from the target of the last <see cref="Late"/>, measured right after <see cref="Reapply"/> (leg reach), and the target itself.</summary>
        public float ErrL, ErrR; public Vector3 OutL { get { return _outL; } } public Vector3 OutR { get { return _outR; } } public bool SwingL { get { return _l.Swing; } } public bool SwingR { get { return _r.Swing; } }
        public Transform AnkleL { get { return _l.E; } } public Transform AnkleR { get { return _r.E; } }

        public EngageStepper(Animator a)
        {
            _a = a;
            if (a == null || !a.isHuman) return;
            _l.U = a.GetBoneTransform(HumanBodyBones.LeftUpperLeg); _l.L = a.GetBoneTransform(HumanBodyBones.LeftLowerLeg); _l.E = a.GetBoneTransform(HumanBodyBones.LeftFoot);
            _r.U = a.GetBoneTransform(HumanBodyBones.RightUpperLeg); _r.L = a.GetBoneTransform(HumanBodyBones.RightLowerLeg); _r.E = a.GetBoneTransform(HumanBodyBones.RightFoot);
            _ok = _l.U != null && _l.L != null && _l.E != null && _r.U != null && _r.L != null && _r.E != null;
            _l.Toe = a.GetBoneTransform(HumanBodyBones.LeftToes); _r.Toe = a.GetBoneTransform(HumanBodyBones.RightToes);
            _hips = a.GetBoneTransform(HumanBodyBones.Hips);
            if (_ok && !V011) BuildSole(a);      // v0.12.0: the drawn sole of this body (once)
        }

        public void Reset() { _haveOut = false; _w = 0f; _l.Have = _r.Have = false; _l.Swing = _r.Swing = false; }

        /// <summary>Ankle and toe-bone heights over the turf of a flat foot (FootPlant measures them); the planted foot is pressed onto the turf with them.</summary>
        public void SetRest(float ankleL, float ankleR, float toeL, float toeR) { _restAL = ankleL; _restAR = ankleR; _restTL = toeL; _restTR = toeR; _haveRest = true; }
        float _restAL, _restAR, _restTL, _restTR; bool _haveRest;
        /// <summary>A planted foot whose sole the clip holds higher than this (m) over the turf is on a step of the clip and is not pressed down.</summary>
        public const float PressReach = 0.12f, PressLowest = 0.35f;
        bool _lowLeft;
        public static readonly bool NoPress = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoPress") >= 0, NoReapply = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoReapply") >= 0;

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        /// <summary>Heading (deg) of the foot: ankle to toe bone on the ground plane (0 when there is no toe bone).</summary>
        static float YawOf(F f)
        {
            if (f.Toe == null) return 0f;
            Vector3 d = f.Toe.position - f.E.position; d.y = 0f;
            return d.sqrMagnitude < 1e-6f ? f.E.eulerAngles.y : Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg;
        }

        /// <summary>Distance (m) from the point to the segment a-b (all on the ground plane).</summary>
        public static float SegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a; float l2 = ab.sqrMagnitude;
            if (l2 < 1e-8f) return (p - a).magnitude;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / l2);
            return (p - (a + ab * t)).magnitude;
        }

        /// <summary>Late, on the final pose. <paramref name="vel"/> = the body's horizontal velocity (m/s), <paramref name="fwd"/> = its facing.</summary>
        public void Late(float dt, bool active, Vector3 vel, Vector3 fwd, float groundY = float.NaN, bool contact = true)
        {
            if (!_ok || dt <= 1e-5f) return;
            _groundY = groundY; _speed = Flat(vel).magnitude; _contact = contact;
            if (active) _w = Mathf.MoveTowards(_w, 1f, dt / (Old ? 0.10f : 0.04f)); else _w = Mathf.MoveTowards(_w, 0f, dt / 0.15f);
            if (_w <= 0.001f) { _l.Have = _r.Have = false; _l.Swing = _r.Swing = false; _pelCur = Vector3.zero; return; }
            Vector3 al = _l.E.position, ar = _r.E.position;      // the animated ankles of this frame
            // a foot that is up in the air (a jump, a stumble's kick) is not pinned: it follows the animation, its anchor re-set where it comes down
            if (!float.IsNaN(groundY)) { if (InAir(_l, true, al, groundY)) { _l.Anchor = al; _l.Yaw = YawOf(_l); _l.Swing = false; _l.Have = true; } if (InAir(_r, false, ar, groundY)) { _r.Anchor = ar; _r.Yaw = YawOf(_r); _r.Swing = false; _r.Have = true; } }
            if (!_l.Have) { _l.Anchor = al; _l.Yaw = YawOf(_l); _l.Have = true; _l.Swing = false; }
            if (!_r.Have) { _r.Anchor = ar; _r.Yaw = YawOf(_r); _r.Have = true; _r.Swing = false; }
            if (active)
            {
                float dl = Flat(al - _l.Anchor).magnitude, dr = Flat(ar - _r.Anchor).magnitude;
                float rl = Vector3.Distance(_l.U.position, new Vector3(_l.Anchor.x, al.y, _l.Anchor.z)), rr = Vector3.Distance(_r.U.position, new Vector3(_r.Anchor.x, ar.y, _r.Anchor.z));
                float reachL = LimbIK.Reach(_l.U.position, _l.L.position, _l.E.position) * ReachFrac, reachR = LimbIK.Reach(_r.U.position, _r.L.position, _r.E.position) * ReachFrac;
                _l.Down += dt; _r.Down += dt;
                bool stretchL = rl > reachL * TriggerShare / ReachFrac, stretchR = rr > reachR * TriggerShare / ReachFrac;
                bool outside = !Old && _hips != null && SegmentDistance(Flat(_hips.position), Flat(_l.Anchor), Flat(_r.Anchor)) > SupportR;      // the pelvis has left the area the two feet hold up
                bool needL = !_l.Swing && (stretchL || ((outside || dl > (Old ? 0.20f : Drift)) && _l.Down >= (Old ? 0f : (_speed > 2f ? 0.03f : Hold)))), needR = !_r.Swing && (stretchR || ((outside || dr > (Old ? 0.20f : Drift)) && _r.Down >= (Old ? 0f : (_speed > 2f ? 0.03f : Hold))));
                // (when only the pelvis is outside, the foot that is further from where the animation wants it steps; a stretched leg steps whatever the other does)
                // one foot at a time: the one that is further from where the body wants it
                if (needL && needR) { if (dl >= dr) needR = false; else needL = false; }
                if (needL && !_r.Swing) { _l.Swing = true; _l.From = _l.Anchor; _l.T = 0f; }
                if (needR && !_l.Swing) { _r.Swing = true; _r.From = _r.Anchor; _r.T = 0f; }
            }
            Vector3 dir = Flat(vel).sqrMagnitude > 0.04f ? Flat(vel).normalized : Flat(fwd).normalized;
            Vector3 pole = Flat(fwd).sqrMagnitude > 1e-4f ? Flat(fwd).normalized : Vector3.forward;
            float limit = Mathf.Max(MinSpread, Flat(al - ar).magnitude + SpreadSlack);
            if (HasDrawnSole && !float.IsNaN(groundY)) _lowLeft = DrawnSole(_l, true) <= DrawnSole(_r, false);      // v0.12.0: the lower foot is always put on the turf
            Step(_l, al, dir, pole, dt, _haveOut ? _outR : ar, limit, out _outL); Step(_r, ar, dir, pole, dt, _outL, limit, out _outR); _haveOut = true;
            if (!Old && !NoPelvisDrop && _hips != null) PelvisToFeet(pole);
            if (!V011 && !NoPitchFloor && !float.IsNaN(groundY)) { FootGuard(_l, ref _outL, pole); FootGuard(_r, ref _outR, pole); }      // v0.13.0 STANCE-HEAD
            _pole = pole; _rotL = _l.E.rotation; _rotR = _r.E.rotation;
        }


        /// <summary>
        /// v0.13.0 STANCE-HEAD: the root cause of the bone-based FEET_BELOW_TURF count (v0.11 252, v0.12 1,229 a game). Of 332 sampled sunk-foot frames of the v0.12 baseline 331 had the DRAWN sole 7 to 16 cm under the turf (the count is real sinking, not a bone-versus-sole artefact),
        /// 317 with this stepper at full weight, 255 with the ankle at the flat 8.5 cm floor, and the foot pitched 45 to 84 degrees toes-down: the clip's toe-off foot (the ankle high, the heel up) keeps its world rotation (LimbIK) while the stepper puts the ankle on a pinned, lower point,
        /// so the ball of the foot goes into the grass. Here, on the solved pose: a foot never points further than <see cref="MaxToeDown"/> below the horizon (a ballet-point foot is not a stance), and the ankle is raised until the toe bone is <see cref="ToeFloor"/> over the turf (the foot keeps its world rotation).
        /// </summary>
        void FootGuard(F f, ref Vector3 placed, Vector3 pole)
        {
            if (f.Toe == null || _w < 0.01f) return;
            Vector3 v = f.Toe.position - f.E.position; float len = v.magnitude;
            if (len > 0.03f)
            {
                float pitch = Mathf.Asin(Mathf.Clamp(v.y / len, -1f, 1f)) * Mathf.Rad2Deg;      // negative: toes down
                if (pitch < -MaxToeDown)
                {
                    Vector3 flat = Flat(v); flat = flat.sqrMagnitude > 1e-6f ? flat.normalized : Flat(f.E.forward).normalized;
                    float pr = -MaxToeDown * Mathf.Deg2Rad;
                    f.E.rotation = Quaternion.FromToRotation(v, (flat * Mathf.Cos(pr) + Vector3.up * Mathf.Sin(pr)) * len) * f.E.rotation;
                }
            }
            float def = _groundY + ToeFloor - f.Toe.position.y;
            if (def > 0.002f)
            {
                placed.y += Mathf.Min(def, MaxLift);
                LimbIK.Apply(f.U, f.L, f.E, placed, pole, 1f);      // the foot keeps its world rotation
                Quiet(f);
            }
        }
        void Quiet(F f)
        {
            if (Old || f.Swing || f.Toe == null) return;
            float d = Mathf.DeltaAngle(YawOf(f), f.Yaw) * _w;
            if (Mathf.Abs(d) > 0.01f) f.E.rotation = Quaternion.AngleAxis(d, Vector3.up) * f.E.rotation;
        }

        /// <summary>
        /// v0.12.0: a leg that cannot reach its pinned foot leaves the sole in the air (both soles 4 cm and more up in 17-21 % of the standing hand-fight frames once the feet were pinned): the pelvis moves toward the pinned ankles
        /// by what the legs miss (at most <see cref="PelvisMax"/>), the legs are solved again. The way FootPlant lowers the pelvis for the reach it misses.
        /// </summary>
        void PelvisToFeet(Vector3 pole)
        {
            Vector3 d = Vector3.zero;
            Vector3 vl = _outL - _l.U.position, vr = _outR - _r.U.position;
            float el = vl.magnitude - LimbIK.Reach(_l.U.position, _l.L.position, _l.E.position) * 0.999f, er = vr.magnitude - LimbIK.Reach(_r.U.position, _r.L.position, _r.E.position) * 0.999f;
            if (el > 0.004f) d += vl.normalized * el;
            if (er > 0.004f) d += vr.normalized * er;
            if (d.sqrMagnitude < 1e-6f && _pelCur.sqrMagnitude < 1e-6f) return;
            if (d.magnitude > PelvisMax) d = d.normalized * PelvisMax;
            _pelCur = Vector3.MoveTowards(_pelCur, d, PelvisRate);      // eased: a correction that appears in one frame is a hips jump
            _hips.position += _pelCur * _w;
            LimbIK.Apply(_l.U, _l.L, _l.E, _outL, pole, 1f); LimbIK.Apply(_r.U, _r.L, _r.E, _outR, pole, 1f);
        }
        public static readonly float PelvisMax = Arg("-pixPelMax", 0.12f), PelvisRate = 0.02f; Vector3 _pelCur;
        static float Arg(string n, float d) { var a = System.Environment.GetCommandLineArgs(); for (int i = 0; i + 1 < a.Length; i++) if (a[i] == n) { float v; if (float.TryParse(a[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return v; } return d; }
        public static readonly bool PressUpOnly = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixPressUp") >= 0;
        /// <summary>QA: how far (m) the pelvis is being pulled toward the pinned feet right now.</summary>
        public float PelvisPull { get { return _pelCur.magnitude; } }
        public static readonly bool NoPelvisDrop = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoPelvisDrop") >= 0;
        /// <summary>
        /// v0.12.0 CONTACT: called after everything that may move the pelvis or the legs after this solver (the hips leash, Final IK's hand solve through <c>PixIk.Late</c>): puts the ankles back on the targets of
        /// <see cref="Late"/> with the feet in the world rotation they had. One solver per chain: Final IK reaches with the hands and moves the spine and pelvis for it, the legs stay ours. No-op while the stepper has no weight.
        /// </summary>
        public void Reapply()
        {
            if (NoReapply || !_ok || !_haveOut || _w <= 0.001f) return;
            ReapplyLeg(_l.U, _l.L, _l.E, _outL, _pole, _rotL);
            ReapplyLeg(_r.U, _r.L, _r.E, _outR, _pole, _rotR);
            ErrL = (_l.E.position - _outL).magnitude; ErrR = (_r.E.position - _outR).magnitude;
        }

        /// <summary>The two-bone leg solve of <see cref="Reapply"/>: the ankle on <paramref name="ankle"/> (clamped to the leg's reach), the foot bone in <paramref name="footWorld"/>.</summary>
        public static void ReapplyLeg(Transform upper, Transform lower, Transform foot, Vector3 ankle, Vector3 pole, Quaternion footWorld)
        {
            LimbIK.Apply(upper, lower, foot, ankle, pole, 1f);
            foot.rotation = footWorld;
        }

        /// <summary>A landing foot is brought inside the spread of the other foot and inside the leg's reach (a wide lunge read as legs reaching for the next yard). A pinned foot is never moved by this: it would slide.</summary>
        static Vector3 ClampLanding(F f, Vector3 land, Vector3 other, float spread)
        {
            Vector3 sd = Flat(land - other); if (sd.magnitude > spread) { Vector3 c = other + sd.normalized * spread; land = new Vector3(c.x, land.y, c.z); }
            float legReach = LimbIK.Reach(f.U.position, f.L.position, f.E.position) * ReachShare, dy = f.U.position.y - land.y;
            float hmax = Mathf.Sqrt(Mathf.Max(0.01f, legReach * legReach - dy * dy)); Vector3 hd = Flat(land - f.U.position);
            if (hd.magnitude > hmax) { Vector3 c = f.U.position + hd.normalized * hmax; land = new Vector3(c.x, land.y, c.z); }
            return land;
        }

        void Step(F f, Vector3 anim, Vector3 dir, Vector3 pole, float dt, Vector3 other, float spread, out Vector3 placed)
        {
            Vector3 tgt;
            if (f.Swing)
            {
                f.T += dt; float dur = Old ? 0.17f : Mathf.Lerp(Dur, 0.12f, Mathf.Clamp01((_speed - 1.5f) / 2f)); float u = Mathf.Clamp01(f.T / dur);      // (v0.12: the faster the body is pushed, the quicker the steps)
                Vector3 landing = ClampLanding(f, anim + dir * (Old ? Lead : Lead + 0.10f * Mathf.Max(0f, _speed - 1f)), other, spread);      // (v0.12: a longer stride ahead of a faster body)
                float v = Old ? u : Mathf.Clamp01((u - TravelFrom) / (TravelTo - TravelFrom)), s = v * v * (3f - 2f * v);      // v0.12.0: up first, then across, then down
                Vector3 p = Vector3.Lerp(f.From, landing, s);
                tgt = new Vector3(p.x, anim.y + (Old ? 0.06f * Mathf.Sin(Mathf.PI * u) : LiftH * Mathf.Pow(Mathf.Sin(Mathf.PI * u), 0.7f)), p.z);
                if (u >= 1f) { f.Anchor = new Vector3(landing.x, f.Anchor.y, landing.z); f.Swing = false; f.Down = 0f; f.Yaw = YawOf(f); Steps++; }
            }
            else
            {
                float y = anim.y;
                if (!Old && !NoPress && HasDrawnSole && !float.IsNaN(_groundY))
                {
                    // v0.12.0: a planted foot is ON the turf: the ankle is moved so the lowest DRAWN sole point (any foot pitch) is at the sole goal; a foot the clip holds more than PressReach above it is the clip's own step
                    bool left = f == _l;
                    float gap = DrawnSole(f, left) - _groundY - FootPlant.SoleGoal;
                    if (!PressUpOnly && gap > 0.002f && gap < ((left == _lowLeft || (left ? _r.Swing : _l.Swing)) ? PressLowest : PressReach)) y = anim.y - gap; else if (gap < -0.002f && gap > -0.2f) y = anim.y - gap;
                }
                tgt = new Vector3(f.Anchor.x, y, f.Anchor.z);
            }
            tgt = Vector3.Lerp(anim, tgt, _w);
            if (FloorClamp && _w > 0.01f && !float.IsNaN(_groundY)) tgt.y = Mathf.Max(tgt.y, _groundY + AnkleFloor);
            placed = tgt;
            LimbIK.Apply(f.U, f.L, f.E, tgt, pole, 1f);
            if (!Old && !f.Swing && f.Toe != null && _w > 0.01f)
            {
                // v0.12.0: a planted foot also keeps the direction it was set down in (the toe is a hand span from the ankle: a foot that turns on its ankle moves the toe 5 to 10 cm while the hand fight turns the body)
                float d = Mathf.DeltaAngle(YawOf(f), f.Yaw) * _w;
                if (Mathf.Abs(d) > 0.01f) f.E.rotation = Quaternion.AngleAxis(d, Vector3.up) * f.E.rotation;
            }
        }
    }
}
