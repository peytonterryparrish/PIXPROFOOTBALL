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
            return Mathf.Clamp(forwardAccel * 0.9f, -maxDegrees * 0.5f, maxDegrees);
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

        readonly PlayerAvatar _av;
        readonly Animator _animator;
        readonly Foot _left = new Foot(), _right = new Foot();
        Vector3 _prevVel, _fwd = Vector3.forward;
        float _lat, _fwdAcc, _bank, _pitch, _speed;
        bool _haveVel;
        public bool Enabled = true;
        /// <summary>1 = the locomotion layer owns the body (lean and foot lock apply); 0 = an authored take / clip does.</summary>
        public float LocomotionWeight = 1f;
        public static bool FootLockEnabled = true, LeanEnabled = true;

        // stiff arm
        Vector3 _armTarget; float _armTime, _armTotal;
        public bool StiffArmActive { get { return _armTime >= 0f && _armTime <= _armTotal; } }

        public float Bank { get { return _bank; } }
        public float Pitch { get { return _pitch; } }

        public BodyMotion(PlayerAvatar av)
        {
            _av = av; _animator = av != null ? av.Animator : null;
            if (_animator != null && _animator.isHuman)
            {
                _left.Upper = _animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg); _left.Lower = _animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg); _left.End = _animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                _right.Upper = _animator.GetBoneTransform(HumanBodyBones.RightUpperLeg); _right.Lower = _animator.GetBoneTransform(HumanBodyBones.RightLowerLeg); _right.End = _animator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
            _armTime = -1f;
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
        }

        /// <summary>Starts a stiff arm toward a world point (the defender's chest) for the given time.</summary>
        public void StiffArmAt(Vector3 point, float seconds) { _armTarget = point; _armTime = 0f; _armTotal = seconds; }

        public void UpdateStiffArmTarget(Vector3 point) { _armTarget = point; }

        public void Cancel() { _protect = false; _protectW = 0f; _armTime = -1f; _left.Locked = _right.Locked = false; _left.Weight = _right.Weight = 0f; _bank = _pitch = 0f; _haveVel = false; }

        /// <summary>LateUpdate, after the animation: lean, foot lock, stiff arm.</summary>
        public void Late(float dt)
        {
            if (!Enabled || _animator == null || !_animator.isHuman || dt <= 1e-5f) return;
            float w = Mathf.Clamp01(LocomotionWeight);
            // ---- lean
            float bankT = LeanEnabled ? LeanMath.BankDegrees(_lat, _speed) * w : 0f;
            float pitchT = LeanEnabled ? LeanMath.PitchDegrees(_fwdAcc, _speed) * w : 0f;
            _bank = LeanMath.Smooth(_bank, bankT, dt, 0.09f); _pitch = LeanMath.Smooth(_pitch, pitchT, dt, 0.12f);
            if (Mathf.Abs(_bank) > 0.15f || Mathf.Abs(_pitch) > 0.15f)
            {
                Vector3 right = Vector3.Cross(Vector3.up, _fwd);
                var spine = _animator.GetBoneTransform(HumanBodyBones.Spine); var chest = _animator.GetBoneTransform(HumanBodyBones.Chest); var head = _animator.GetBoneTransform(HumanBodyBones.Head);
                // +bank leans to the right: rotate about the forward axis by -bank; +pitch tips the chest forward: rotate about the right axis by +pitch
                Quaternion q = Quaternion.AngleAxis(-_bank, _fwd) * Quaternion.AngleAxis(_pitch, right);
                if (spine != null) spine.rotation = Quaternion.Slerp(Quaternion.identity, q, 0.45f) * spine.rotation;
                if (chest != null) chest.rotation = Quaternion.Slerp(Quaternion.identity, q, 0.55f) * chest.rotation;
                // the head stays up looking where the runner is going
                if (head != null) head.rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Inverse(q), 0.55f) * head.rotation;
            }
            // ---- stiff arm
            if (_armTime >= 0f)
            {
                _armTime += dt;
                if (_armTime > _armTotal) _armTime = -1f;
                else ApplyArm(LeanMath.Envelope(_armTime, _armTotal, 0.09f, 0.14f));
            }
            // ---- protect ball
            ApplyProtect(dt, w);
            // ---- foot lock
            if (FootLockEnabled) { LockFoot(_left, dt, w); LockFoot(_right, dt, w); }
        }

        // protect ball: the carry arm folds the ball high and tight against the chest
        bool _protect; int _protectSide = 1; float _protectW;

        /// <summary>The user holds the ball high and tight (LT): the carry arm tucks the ball against the chest. side: +1 = right arm.</summary>
        public void SetProtect(bool on, int side) { _protect = on; _protectSide = side >= 0 ? 1 : -1; }

        void ApplyProtect(float dt, float w)
        {
            _protectW = Mathf.MoveTowards(_protectW, _protect ? 1f : 0f, dt / 0.12f);
            float k = _protectW * w;
            if (k <= 0.01f) return;
            var chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
            var upper = _animator.GetBoneTransform(_protectSide > 0 ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            var lower = _animator.GetBoneTransform(_protectSide > 0 ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            var hand = _animator.GetBoneTransform(_protectSide > 0 ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
            if (chest == null || upper == null || lower == null || hand == null) return;
            Vector3 right = Vector3.Cross(Vector3.up, _fwd);
            Vector3 target = chest.position + Vector3.down * 0.12f + _fwd * 0.17f + right * (_protectSide * 0.10f);
            Vector3 pole = right * _protectSide * 0.7f + Vector3.down * 0.35f - _fwd * 0.2f;
            LimbIK.Apply(upper, lower, hand, target, pole, k);
        }

        void ApplyArm(float weight)
        {
            if (weight <= 0.01f) return;
            var chest = _animator.GetBoneTransform(HumanBodyBones.Chest);
            Vector3 right = Vector3.Cross(Vector3.up, _fwd);
            Vector3 toTarget = _armTarget - (chest != null ? chest.position : _armTarget);
            bool rightArm = Vector3.Dot(right, toTarget) >= 0f;
            var upper = _animator.GetBoneTransform(rightArm ? HumanBodyBones.RightUpperArm : HumanBodyBones.LeftUpperArm);
            var lower = _animator.GetBoneTransform(rightArm ? HumanBodyBones.RightLowerArm : HumanBodyBones.LeftLowerArm);
            var hand = _animator.GetBoneTransform(rightArm ? HumanBodyBones.RightHand : HumanBodyBones.LeftHand);
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
                if (p.y < 0.115f && speed < 0.65f && f.Weight < 0.05f) { f.Locked = true; f.LockPos = p; }
            }
            else
            {
                float away = new Vector2(p.x - f.LockPos.x, p.z - f.LockPos.z).magnitude;
                if (p.y > 0.16f || away > 0.30f) f.Locked = false;
            }
            f.Weight = Mathf.MoveTowards(f.Weight, f.Locked ? 1f : 0f, dt / (f.Locked ? 0.06f : 0.05f));
            if (f.Weight <= 0.01f) return;
            Vector3 pole = f.Lower.position - (f.Upper.position + f.End.position) * 0.5f;
            Vector3 target = new Vector3(f.LockPos.x, p.y, f.LockPos.z);
            LimbIK.Apply(f.Upper, f.Lower, f.End, target, pole, f.Weight * w);
        }
    }
}
