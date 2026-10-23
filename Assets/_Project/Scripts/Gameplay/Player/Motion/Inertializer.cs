using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 MOTION: inertialized blending. When the matcher jumps to another clip / frame the pose does not cross-fade (two gaits averaged are a gait nobody runs); the difference between the pose shown a frame ago
    /// and the new animation's pose becomes an offset per bone that dies out as a critically damped spring (half-life ~0.09 s). The new animation plays at full weight from the first frame, the discontinuity is carried by the offset.
    /// Runs in LateUpdate, first thing, on the pose the animator just wrote; the pose it outputs (animation + decaying offset) is remembered as the reference for the next switch.
    /// The pelvis is tracked in the frame of the view root (not the body model): when the body model is turned (the facing loop's yaw, the model yaw of another convention) the shown pelvis stays where it was in the world and the turn is carried
    /// by the same decaying offset instead of a visible jump.
    /// </summary>
    public sealed class Inertializer
    {
        static readonly HumanBodyBones[] Bones =
        {
            HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck, HumanBodyBones.Head,
            HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
            HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
            HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
        };

        readonly Transform[] _t; readonly Transform _frame;
        readonly Quaternion[] _out;        // the pose shown last frame (animation + offset): local rotations, the pelvis relative to the frame
        readonly Vector3[] _off;           // rotation-vector offset per bone (radians)
        Vector3 _hipsOut, _hipsOff;        // pelvis position relative to the frame
        bool _have;
        float _age = 99f;                  // seconds since the last switch
        public float HalfLife = 0.09f;
        /// <summary>Largest offset (degrees) the last switch had to hide (diagnostic).</summary>
        public float LastJump { get; private set; }
        /// <summary>Remaining size of the offset (degrees, largest bone).</summary>
        public float Residual { get; private set; }

        public Inertializer(Animator a, Transform frame = null)
        {
            _t = new Transform[Bones.Length];
            if (a != null && a.isHuman) for (int i = 0; i < Bones.Length; i++) _t[i] = a.GetBoneTransform(Bones[i]);
            _frame = frame != null ? frame : (a != null ? (a.transform.parent != null ? a.transform.parent : a.transform) : null);
            _out = new Quaternion[Bones.Length]; _off = new Vector3[Bones.Length];
        }

        public bool Ok { get { return _t[0] != null && _frame != null; } }
        public void Reset() { _have = false; _age = 99f; for (int i = 0; i < _off.Length; i++) _off[i] = Vector3.zero; _hipsOff = Vector3.zero; Residual = 0f; }

        static Vector3 Log(Quaternion q)
        {
            if (q.w < 0f) { q.x = -q.x; q.y = -q.y; q.z = -q.z; q.w = -q.w; }
            float s = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z);
            if (s < 1e-6f) return new Vector3(q.x, q.y, q.z) * 2f;
            float ang = 2f * Mathf.Atan2(s, q.w);
            return new Vector3(q.x, q.y, q.z) * (ang / s);
        }

        static Quaternion Exp(Vector3 v)
        {
            float a = v.magnitude;
            if (a < 1e-6f) return new Quaternion(v.x * 0.5f, v.y * 0.5f, v.z * 0.5f, 1f).normalized;
            float h = a * 0.5f, s = Mathf.Sin(h) / a;
            return new Quaternion(v.x * s, v.y * s, v.z * s, Mathf.Cos(h));
        }

        Quaternion Read(int i) { return i == 0 ? Quaternion.Inverse(_frame.rotation) * _t[0].rotation : _t[i].localRotation; }
        void Write(int i, Quaternion q) { if (i == 0) _t[0].rotation = _frame.rotation * q; else _t[i].localRotation = q; }

        /// <summary>One frame, called right after the animation. <paramref name="switched"/> = the pose source jumped this frame (the offset between what was shown and what the animation now says is taken up).</summary>
        public void Apply(float dt, bool switched, float strength = 1f)
        {
            if (!Ok) return;
            Transform hips = _t[0];
            Vector3 hipsRel = _frame.InverseTransformPoint(hips.position);
            if (switched && _have)
            {
                float worst = 0f;
                for (int i = 0; i < _t.Length; i++)
                {
                    if (_t[i] == null) continue;
                    // offset o such that Exp(o) * anim = shown  =>  o = Log(shown * inverse(anim))
                    Quaternion d = _out[i] * Quaternion.Inverse(Read(i));
                    _off[i] = Log(d) * strength;
                    worst = Mathf.Max(worst, _off[i].magnitude * Mathf.Rad2Deg);
                }
                _hipsOff = (_hipsOut - hipsRel) * strength;
                LastJump = worst; _age = 0f;
            }
            else if (_have)
            {
                _age += dt;
            }
            // critically damped decay with zero initial offset velocity: x(t) = x0 (1 + w t) exp(-w t)
            float w = 2f * 0.6931f / Mathf.Max(0.02f, HalfLife);
            float k = (1f + w * _age) * Mathf.Exp(-w * _age);
            float res = 0f;
            if (_age < 1.2f)
            {
                for (int i = 0; i < _t.Length; i++)
                {
                    if (_t[i] == null) continue;
                    Vector3 o = _off[i] * k;
                    if (o.sqrMagnitude > 1e-9f) Write(i, Exp(o) * Read(i));
                    res = Mathf.Max(res, o.magnitude * Mathf.Rad2Deg);
                }
                if (_hipsOff.sqrMagnitude > 1e-10f) hips.position = _frame.TransformPoint(hipsRel + _hipsOff * k);
            }
            Residual = res;
            for (int i = 0; i < _t.Length; i++) if (_t[i] != null) _out[i] = Read(i);
            _hipsOut = _frame.InverseTransformPoint(hips.position);
            _have = true;
        }
    }
}
