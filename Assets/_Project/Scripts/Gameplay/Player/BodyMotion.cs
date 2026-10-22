using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>Pure lean math (unit tested): how far a runner banks into a turn and pitches into an acceleration.</summary>
    public static class LeanMath
    {
        /// <summary>Bank angle (degrees, + = lean to the right) from lateral acceleration (m/s^2, + = turning right) and ground speed (m/s). A runner leans about atan(a / g), softened.</summary>
        public static float BankDegrees(float lateralAccel, float speed, float maxDegrees = 17f)
        {
            if (speed < 1.2f) return 0f;
            float k = Mathf.Clamp01((speed - 1.2f) / 3f);
            float a = Mathf.Atan(lateralAccel / 9.81f) * Mathf.Rad2Deg * 0.85f * k;
            return Mathf.Clamp(a, -maxDegrees, maxDegrees);
        }

        /// <summary>Forward pitch (degrees, + = chest down) from forward acceleration.</summary>
        public static float PitchDegrees(float forwardAccel, float speed, float maxDegrees = 9f)
        {
            if (speed < 0.5f) return 0f;
            return Mathf.Clamp(forwardAccel * 0.9f, -maxDegrees * 1.3f, maxDegrees);      // v0.9.0: braking leans back as far as 12 degrees (it was 4.5)
        }

        /// <summary>Exponential smoothing with time constant tau.</summary>
        public static float Smooth(float current, float target, float dt, float tau)
        {
            return Mathf.Lerp(current, target, 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, tau)));
        }

        /// <summary>Envelope 0..1 of a timed effect: ramps in over rampIn, holds, ramps out over rampOut before the end.</summary>
        public static float Envelope(float elapsed, float total, float rampIn, float rampOut)
        {
            if (elapsed < 0f || elapsed > total) return 0f;
            float a = rampIn <= 0f ? 1f : Mathf.Clamp01(elapsed / rampIn);
            float b = rampOut <= 0f ? 1f : Mathf.Clamp01((total - elapsed) / rampOut);
            return Mathf.Min(a, b);
        }
    }

    /// <summary>
    /// Procedural body motion layered over the animation in LateUpdate (v0.4): the spine banks into turns and pitches into accelerations (heavy, grounded runners), the stance foot is locked
    /// to the ground with two-bone IK while it is planted (no sliding), and the ball carrier's stiff arm reaches for the defender's chest. Everything fades out while an authored take, a ragdoll
    /// or a one-shot clip owns the body.
    /// </summary>
    public sealed class BodyMotion
    {
        sealed class Foot
        {
            public Transform Upper, Lower, End;
            public bool Locked; public Vector3 LockPos; public float Weight; public Vector3 Prev; public bool HavePrev;
        }

        readonly Transform[] _bones = new Transform[56];
        float _gy;     // ground height under the player (the avatar root), so the foot lock does not assume a pitch at y = 0
        Transform Bone(HumanBodyBones b) { int i = (int)b; if (i < 0 || i >= _bones.Length) return _animator != null ? _animator.GetBoneTransform(b) : null; var t = _bones[i]; if (t == null && _animator != null) t = _bones[i] = _animator.GetBoneTransform(b); return t; }     // QA-725: bones cached once
        readonly PlayerAvatar _av;
        readonly Animator _animator;
        readonly Foot _left = new Foot(), _right = new Foot();
        Vector3 _prevVel, _fwd = Vector3.forward, _velDir = Vector3.forward;
        float _lat, _fwdAcc, _bank, _pitch, _speed;
        bool _haveVel;
        public bool Enabled = true;
        /// <summary>1 = the locomotion layer owns the body (lean and foot lock apply); 0 = an authored take / clip does.</summary>
        public float LocomotionWeight = 1f;
        public static bool FootLockEnabled = true, LeanEnabled = true;

        // stiff arm
        Vector3 _armTarget; float _armTime, _armTotal;
        public bool StiffArmActive { get { return _armTime >= 0f && _armTime <= _armTotal; } }

        /// <summary>v0.9.0 LOCOMOTION: stride warp, foot planting and pelvis compensation (the inputs are set by the view each frame).</summary>
        public readonly FootPlant Plant;
        public float Bank { get { return _bank; } }
        public float Pitch { get { return _pitch; } }

        public BodyMotion(PlayerAvatar av)
        {
            _av = av; _animator = av != null ? av.Animator : null;
            if (_animator != null && _animator.isHuman)
            {
                _left.Upper = Bone(HumanBodyBones.LeftUpperLeg); _left.Lower = Bone(HumanBodyBones.LeftLowerLeg); _left.End = Bone(HumanBodyBones.LeftFoot);
                _right.Upper = Bone(HumanBodyBones.RightUpperLeg); _right.Lower = Bone(HumanBodyBones.RightLowerLeg); _right.End = Bone(HumanBodyBones.RightFoot);
            }
            _armTime = -1f;
            Plant = new FootPlant(_animator);
        }

        /// <summary>Called every frame with the simulated velocity (m/s) and the body forward.</summary>
        public void Sample(Vector3 velocity, Vector3 forward, float dt)
        {
            _fwd = forward;
            if (dt <= 1e-5f) return;
            if (!_haveVel) { _prevVel = velocity; _haveVel = true; }
            Vector3 acc = (velocity - _prevVel) / dt; acc.y = 0f;
            _prevVel = velocity;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            _lat = LeanMath.Smooth(_lat, Mathf.Clamp(Vector3.Dot(acc, right), -40f, 40f), dt, 0.10f);
            _fwdAcc = LeanMath.Smooth(_fwdAcc, Mathf.Clamp(Vector3.Dot(acc, forward), -30f, 30f), dt, 0.14f);
            _speed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
            _velDir = _speed > 0.3f ? new Vector3(velocity.x, 0f, velocity.z) / _speed : _fwd;
        }

        /// <summary>Starts a stiff arm toward a world point (the defender's chest) for the given time.</summary>
        public void StiffArmAt(Vector3 point, float seconds) { _armTarget = point; _armTime = 0f; _armTotal = seconds; }

        public void UpdateStiffArmTarget(Vector3 point) { _armTarget = point; }

        public void Cancel() { if (Plant != null) Plant.Cancel(); _trim = 1f; _slideErr = 0f; _sHave = false; _carry = Carriage.None; _carryW = 0f; _toeL = _toeR = 0f; _protect = false; _protectW = 0f; _armTime = -1f; _left.Locked = _right.Locked = false; _left.Weight = _right.Weight = 0f; _bank = _pitch = 0f; _haveVel = false; }

        /// <summary>LateUpdate, after the animation: lean, foot lock, stiff arm.</summary>
        public void Late(float dt)
        {
            if (!Enabled || _animator == null || !_animator.isHuman || dt <= 1e-5f) return;
            float w = Mathf.Clamp01(LocomotionWeight);
            _gy = _animator.transform.position.y;
            // ---- lean
            float bankT = LeanEnabled ? LeanMath.BankDegrees(_lat, _speed) * w : 0f;
            float pitchT = LeanEnabled ? LeanMath.PitchDegrees(_fwdAcc, _speed) * w : 0f;
            _bank = LeanMath.Smooth(_bank, bankT, dt, 0.09f); _pitch = LeanMath.Smooth(_pitch, pitchT, dt, 0.12f);
            if (Mathf.Abs(_bank) > 0.15f || Mathf.Abs(_pitch) > 0.15f)
            {
                Vector3 right = Vector3.Cross(Vector3.up, _fwd);
                var spine = Bone(HumanBodyBones.Spine); var chest = Bone(HumanBodyBones.Chest); var head = Bone(HumanBodyBones.Head);
                // +bank leans to the right: rotate about the forward axis by -bank; +pitch tips the chest forward: rotate about the right axis by +pitch
                Quaternion q = Quaternion.AngleAxis(-_bank, _fwd) * Quaternion.AngleAxis(_pitch, right);
                if (spine != null) spine.rotation = Quaternion.Slerp(Quaternion.identity, q, 0.45f) * spine.rotation;
                if (chest != null) chest.rotation = Quaternion.Slerp(Quaternion.identity, q, 0.55f) * chest.rotation;
                // the head stays up looking where the runner is going
                if (head != null) head.rotation = Quaternion.Inverse(q) * head.rotation;     // QA-721: spine 0.45 + chest 0.55 = the full lean reaches the head, so the head takes all of it back
            }
            // ---- stiff arm
            if (_armTime >= 0f)
            {
                _armTime += dt;
                if (_armTime > _armTotal) _armTime = -1f;
                else ApplyArm(LeanMath.Envelope(_armTime, _armTotal, 0.09f, 0.14f));
            }
            // ---- protect ball
            ApplyArmSwing(dt, w, Plant != null ? Plant.GaitLayer : 0f);
            ApplyProtect(dt, w);
            // ---- quarterback carriage (v0.7.0): ball high at the chest in the pocket, cocked by the ear on the run
            ApplyCarriage(dt, w);
            // ---- stride matching (v0.7.0): measure the raw animation before the foot lock moves anything
            TrackStride(dt, w);
            // ---- foot lock
            if (FootLockEnabled)
            {
                if (FootPlant.Enabled && Plant != null && Plant.Ok) { if (!Plant.ActionClaim) { Plant.Late(dt, w > 0.5f); if (!Plant.Engaged) { LockFoot(_left, dt, w); LockFoot(_right, dt, w); } } }
                else { LockFoot(_left, dt, w); LockFoot(_right, dt, w); }
            }
            // ---- toe-out clamp (v0.7.0, A-001)
            if (ToeClampEnabled && !(Plant != null && Plant.PolishWeight > 0.5f)) { ClampToes(_left, true, dt, w); ClampToes(_right, false, dt, w); }     // round 3: FootPlant.FootFix owns the foot heading while the leg polish runs (the old clamp measured the toe against the facing and fought it)
        }

        // ------------------------------------------------------------------------------------------ v0.9.0 arm swing amplitude (LOCO-013)
        /// <summary>The free arms swing wider the faster the runner goes (0.9 at a jog, 1.25 at a sprint): the swing of each upper arm about its own slow mean is scaled. Set by the view (false for ball carriers, whose arms are the carry pose).</summary>
        public bool ArmSwingEnabled;
        public static bool ArmSwingScaleOn = true;
        float _armMeanL, _armMeanR; bool _armHave;
        float _armAngL, _armAngR;
        /// <summary>QA: swing angle (degrees, + = forward) of the left / right upper arm before the scaling this frame.</summary>
        public float ArmAngleLeft { get { return _armAngL; } }
        public float ArmAngleRight { get { return _armAngR; } }
        /// <summary>The amplitude factor of the arm swing at a ground speed (pure, unit tested).</summary>
        public static float ArmSwingFactor(float speed) { return 0.9f + 0.35f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.5f, 8.5f, speed)); }

        void ApplyArmSwing(float dt, float w, float gait)
        {
            if (!ArmSwingScaleOn || !ArmSwingEnabled || w < 0.5f || gait < 0.5f || _speed < 1.5f) { _armHave = false; return; }
            float f = ArmSwingFactor(_speed);
            Vector3 fwd = new Vector3(_fwd.x, 0f, _fwd.z).normalized, right = Vector3.Cross(Vector3.up, fwd);
            for (int side = 0; side < 2; side++)
            {
                var up = Bone(side == 0 ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm); var lo = Bone(side == 0 ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                if (up == null || lo == null) continue;
                Vector3 v = lo.position - up.position;
                float a = Mathf.Atan2(Vector3.Dot(v, fwd), -Vector3.Dot(v, Vector3.up)) * Mathf.Rad2Deg;
                ref float mean = ref (side == 0 ? ref _armMeanL : ref _armMeanR);
                if (!_armHave) mean = a;
                mean += (a - mean) * (1f - Mathf.Exp(-dt / 0.35f));
                if (side == 0) _armAngL = a; else _armAngR = a;
                float delta = Mathf.Clamp((f - 1f) * (a - mean), -25f, 25f);
                if (Mathf.Abs(delta) > 0.05f) up.rotation = Quaternion.AngleAxis(-delta, right) * up.rotation;      // + = more forward: about the body's right axis a positive angle swings the arm backwards
            }
            _armHave = true;
        }

        // ------------------------------------------------------------------------------------------ v0.7.0 stride matching (A-008)
        public static bool StrideMatchEnabled = false;     // experiment (A-008): the closed loop did not beat the plain blend tree on the measured numbers, kept for QA only
        readonly Vector3[] _sPrev = new Vector3[2]; bool _sHave; readonly float[] _sMin = { 0.12f, 0.12f };
        float _slideErr = 0f, _trim = 1f;
        /// <summary>Playback-rate trim (0.6 .. 2.2) of the locomotion blend: grows while the planted foot is dragged along with the body (the cycle is too slow for the ground speed), shrinks while it is pushed backwards (too fast). Closed loop on the raw animation: the planted foot of every gait, speed and body size ends up standing still on the turf instead of gliding.</summary>
        public float LocoTrim { get { return StrideMatchEnabled ? _trim : 1f; } }
        /// <summary>Signed slide of the planted foot over the ground speed (+ = the foot travels with the body).</summary>
        public float SlideError { get { return _slideErr; } }

        void TrackStride(float dt, float w)
        {
            if (_left.End == null || _right.End == null) return;
            var cur0 = _left.End.position; var cur1 = _right.End.position;
            if (!_sHave) { _sPrev[0] = cur0; _sPrev[1] = cur1; _sHave = true; return; }
            bool moving = _speed > 1.6f && w > 0.6f;
            float sum = 0f; int n = 0;
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = i == 0 ? cur0 : cur1;
                float h = p.y - _gy;
                _sMin[i] = Mathf.Min(h, _sMin[i] + 0.03f * dt);
                if (moving && h < _sMin[i] + 0.02f)
                {
                    Vector3 v = (p - _sPrev[i]) / dt; v.y = 0f;
                    sum += Vector3.Dot(v, _velDir) / _speed; n++;
                }
            }
            _sPrev[0] = cur0; _sPrev[1] = cur1;
            if (n > 0)
            {
                float s = Mathf.Clamp(sum / n, -1.2f, 1.2f);
                _slideErr = Mathf.Lerp(_slideErr, s, 1f - Mathf.Exp(-dt / 0.28f));
                _trim = Mathf.Clamp(_trim * (1f + Mathf.Clamp(_slideErr, -0.6f, 0.6f) * dt * 1.5f), 0.6f, 2.2f);
            }
            else if (!moving) _trim = Mathf.MoveTowards(_trim, 1f, dt * 0.9f);
        }

        // ------------------------------------------------------------------------------------------ v0.7.0 foot splay (A-001)
        /// <summary>Largest toe-out / toe-in (degrees, measured on the ground plane against the body heading) a foot keeps while the locomotion layer runs. Several mocap cycles (the carrier run / sprint cycles: right foot +47..+65, the plain run / sprint cycles: right foot -25..-55) plant the foot way off the line of travel.</summary>
        public const float MaxToeOut = 13f, MaxToeIn = 7f;
        public static bool ToeClampEnabled = true;
        float _toeL, _toeR;
        /// <summary>QA: the toe correction (degrees) applied to the left / right foot this frame.</summary>
        public float ToeCorrectionLeft { get { return _toeL; } }
        public float ToeCorrectionRight { get { return _toeR; } }

        /// <summary>Pure: the yaw correction (degrees, same sign convention as the measured angle: + = toes to the body's right) that brings a foot's toe angle inside [-maxIn, maxOut]. <paramref name="left"/> flips the sign (toe-out of the left foot is a negative angle).</summary>
        public static float ToeCorrection(float angle, bool left, float maxOut = MaxToeOut, float maxIn = MaxToeIn)
        {
            float outward = left ? -angle : angle;
            float target = Mathf.Clamp(outward, -maxIn, maxOut);
            float delta = target - outward;
            return left ? -delta : delta;
        }

        void ClampToes(Foot f, bool left, float dt, float w)
        {
            var toe = Bone(left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
            float want = 0f;
            if (toe != null && f.End != null && f.Lower != null && w > 0.5f && _speed > 1.2f)
            {
                float h = f.End.position.y - _gy;
                float low = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.17f, 0.30f, h));     // only the foot on (or near) the turf: a kicked-up swing foot has no usable heading
                Vector3 t = toe.position - f.End.position; t.y = 0f;
                if (t.magnitude > 0.06f && low > 0.01f)
                {
                    float a = Vector3.SignedAngle(new Vector3(_fwd.x, 0f, _fwd.z), t, Vector3.up);
                    if (Mathf.Abs(a) < 120f) want = Mathf.Clamp(ToeCorrection(a, left), -50f, 50f) * low * Mathf.Clamp01((w - 0.5f) * 2f);
                }
            }
            ref float cur = ref (left ? ref _toeL : ref _toeR);
            cur = LeanMath.Smooth(cur, want, dt, 0.04f);
            if (Mathf.Abs(cur) >= 0.4f)
            {
                // half of the turn comes from the shin (tibial rotation about its long axis), half from the ankle about the vertical: no 50 degree ankle twist
                Vector3 shin = f.End.position - f.Lower.position;
                if (shin.sqrMagnitude > 1e-6f) f.Lower.rotation = Quaternion.AngleAxis(cur * 0.5f, shin.normalized) * f.Lower.rotation;
                f.End.rotation = Quaternion.AngleAxis(cur * 0.5f, Vector3.up) * f.End.rotation;
            }
            if (toe != null && f.End != null)
            {
                Vector3 t2 = toe.position - f.End.position; t2.y = 0f;
                float a2 = t2.magnitude > 0.06f ? Vector3.SignedAngle(new Vector3(_fwd.x, 0f, _fwd.z), t2, Vector3.up) * (left ? -1f : 1f) : float.NaN;
                if (left) _toeAfterL = a2; else _toeAfterR = a2;
            }
        }
        float _toeAfterL, _toeAfterR;
        /// <summary>QA: toe-out (degrees, + = outward) of each foot right after the clamp.</summary>
        public float ToeOutAfterLeft { get { return _toeAfterL; } }
        public float ToeOutAfterRight { get { return _toeAfterR; } }

        // ------------------------------------------------------------------------------------------ v0.7.0 quarterback carriage
        public enum Carriage { None, Chest, Ear, Jam }
        Carriage _carry; float _carryW;
        /// <summary>Quarterback ball carriage: both hands high at the chest with the ball point up and the elbows in (pocket), or the ball cocked by the right ear with the left arm forward for balance (scramble / throw on the run).</summary>
        public void SetCarriage(Carriage c) { _carry = c; }
        /// <summary>Minimum weight of the carriage over a playing clip (the under-centre drop-back take carries the ball low at the belly; the pocket loop is already high): set by the quarterback flow.</summary>
        public float CarriageFloor;
        public Carriage CurrentCarriage { get { return _carry; } }
        Carriage _carryShown;

        void ApplyCarriage(float dt, float w)
        {
            if (_carry != Carriage.None) _carryShown = _carry;
            _carryW = Mathf.MoveTowards(_carryW, _carry != Carriage.None ? 1f : 0f, dt / 0.16f);
            // over a playing clip (drop-back, throw, handoff) the take's own arms win; the carriage owns the arms while the locomotion layer is up
            float k = _carryW * Mathf.Max(Mathf.Clamp01(w * 1.25f), CarriageFloor * (1f - 0f));
            if (k <= 0.01f) return;
            var chest = Bone(HumanBodyBones.Chest); var head = Bone(HumanBodyBones.Head);
            if (chest == null) return;
            Vector3 right = Vector3.Cross(Vector3.up, _fwd);
            var ru = Bone(HumanBodyBones.RightUpperArm); var rl = Bone(HumanBodyBones.RightLowerArm); var rh = Bone(HumanBodyBones.RightHand);
            var lu = Bone(HumanBodyBones.LeftUpperArm); var ll = Bone(HumanBodyBones.LeftLowerArm); var lh = Bone(HumanBodyBones.LeftHand);
            if (ru == null || rl == null || rh == null || lu == null || ll == null || lh == null) return;
            if (_carryShown == Carriage.Chest)
            {
                Vector3 ball = chest.position + _fwd * 0.23f + Vector3.up * 0.04f;
                LimbIK.Apply(ru, rl, rh, ball + right * 0.10f, right * 0.35f + Vector3.down * 0.9f - _fwd * 0.25f, k);
                LimbIK.Apply(lu, ll, lh, ball - right * 0.10f, -right * 0.35f + Vector3.down * 0.9f - _fwd * 0.25f, k);
            }
            else if (_carryShown == Carriage.Jam)
            {
                // press corner: both hands up in front of the chest, palms to the receiver, ready to jam (reference 88 / 89)
                Vector3 front = chest.position + _fwd * 0.30f + Vector3.up * 0.02f;
                LimbIK.Apply(ru, rl, rh, front + right * 0.14f, right * 0.55f + Vector3.down * 0.8f - _fwd * 0.1f, k);
                LimbIK.Apply(lu, ll, lh, front - right * 0.14f, -right * 0.55f + Vector3.down * 0.8f - _fwd * 0.1f, k);
            }
            else if (head != null)
            {
                Vector3 ear = head.position + right * 0.21f + Vector3.up * 0.04f + _fwd * 0.10f;
                LimbIK.Apply(ru, rl, rh, ear, right * 0.7f + Vector3.up * 0.25f - _fwd * 0.5f, k);
                LimbIK.Apply(lu, ll, lh, chest.position + _fwd * 0.38f - right * 0.14f + Vector3.up * 0.06f, -right * 0.5f + Vector3.down * 0.6f, k * 0.8f);
            }
        }

        /// <summary>World position the ball sits at for the current carriage (pose-matched hands), for the ball follower.</summary>
        public Carriage ShownCarriage { get { return _carryW > 0.2f ? _carryShown : Carriage.None; } }

        // protect ball: the carry arm folds the ball high and tight against the chest
        bool _protect; int _protectSide = 1; float _protectW;

        /// <summary>The user holds the ball high and tight (LT): the carry arm tucks the ball against the chest. side: +1 = right arm.</summary>
        public void SetProtect(bool on, int side) { _protect = on; _protectSide = side >= 0 ? 1 : -1; }

        void ApplyProtect(float dt, float w)
        {
            _protectW = Mathf.MoveTowards(_protectW, _protect ? 1f : 0f, dt / 0.12f);
            float k = _protectW * w;
            if (k <= 0.01f) return;
            var chest = Bone(HumanBodyBones.Chest);
            var upper = Bone(_protectSide > 0 ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            var lower = Bone(_protectSide > 0 ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            var hand = Bone(_protectSide > 0 ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (chest == null || upper == null || lower == null || hand == null) return;
            Vector3 right = Vector3.Cross(Vector3.up, _fwd);
            Vector3 target = chest.position + Vector3.down * 0.12f + _fwd * 0.17f + right * (_protectSide * 0.10f);
            Vector3 pole = right * _protectSide * 0.7f + Vector3.down * 0.35f - _fwd * 0.2f;
            LimbIK.Apply(upper, lower, hand, target, pole, k);
        }

        void ApplyArm(float weight)
        {
            if (weight <= 0.01f) return;
            var chest = Bone(HumanBodyBones.Chest);
            Vector3 right = Vector3.Cross(Vector3.up, _fwd);
            Vector3 toTarget = _armTarget - (chest != null ? chest.position : _armTarget);
            bool rightArm = Vector3.Dot(right, toTarget) >= 0f;
            var upper = Bone(rightArm ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            var lower = Bone(rightArm ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            var hand = Bone(rightArm ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (upper == null || lower == null || hand == null) return;
            // reach for the target but never past the arm's length (the solver clamps); the elbow stays where the animation bends it
            Vector3 pole = lower.position - (upper.position + hand.position) * 0.5f;
            if (pole.sqrMagnitude < 1e-6f) pole = -_fwd + Vector3.down * 0.3f;
            LimbIK.Apply(upper, lower, hand, _armTarget, pole, weight);
        }

        void LockFoot(Foot f, float dt, float w)
        {
            if (f.Upper == null || f.Lower == null || f.End == null) return;
            Vector3 p = f.End.position;
            float speed = f.HavePrev ? new Vector2(p.x - f.Prev.x, p.z - f.Prev.z).magnitude / dt : 99f;
            f.Prev = p; f.HavePrev = true;
            if (w < 0.5f) { f.Locked = false; f.Weight = Mathf.MoveTowards(f.Weight, 0f, dt / 0.05f); return; }
            if (!f.Locked)
            {
                if (p.y - _gy < 0.115f && speed < 0.65f && f.Weight < 0.05f) { f.Locked = true; f.LockPos = p; }
            }
            else
            {
                float away = new Vector2(p.x - f.LockPos.x, p.z - f.LockPos.z).magnitude;
                if (p.y - _gy > 0.16f || away > 0.30f) f.Locked = false;     // QA-724: relative to the ground under the player
            }
            f.Weight = Mathf.MoveTowards(f.Weight, f.Locked ? 1f : 0f, dt / (f.Locked ? 0.06f : 0.05f));
            if (f.Weight <= 0.01f) return;
            Vector3 pole = f.Lower.position - (f.Upper.position + f.End.position) * 0.5f;
            Vector3 target = new Vector3(f.LockPos.x, p.y, f.LockPos.z);
            LimbIK.Apply(f.Upper, f.Lower, f.End, target, pole, f.Weight * w);
        }
    }
}
