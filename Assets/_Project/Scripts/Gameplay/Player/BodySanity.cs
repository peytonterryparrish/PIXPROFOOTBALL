using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 BODIES (playtest note 9): the last word on a pose. Every procedural layer (stance IK, limb IK, arm IK, lean, ground fit) and every mocap take can leave a joint outside what a body does:
    /// the chest twisted a quarter turn off the hips, the head thrown back over the shoulders, the hand rolled a half turn on the forearm (candy-wrapper skin), an elbow or a knee bent backwards.
    /// This component measures the final pose of one humanoid against per-joint limits and eases any excess back inside them, in two phases:
    /// <see cref="Torso"/> (spine twist and bend, neck and head), called by the view before the arm IK so the hands that reach for a ball or a thigh follow the corrected chest,
    /// and <see cref="Limbs"/> (forearm and hand roll, upper-arm and thigh roll, elbow and knee hyperextension), run after every other body layer and before the ball is placed.
    /// Rolls are measured against the bind pose about the bone's own axis (swing-twist), so a limit means the same on every body and in every state. Cost: a few quaternion products per limb.
    /// </summary>
    [DefaultExecutionOrder(100)]
    public sealed class BodySanity : MonoBehaviour
    {
        // ---- limits (degrees)
        /// <summary>Total chest-over-pelvis roll about the spine (the three spine bones together). A bent lineman and a thrower both stay inside it; the wind-up of a throw is allowed more.</summary>
        public static float SpineTwistMax = 32f, SpineTwistMaxThrow = 46f;
        /// <summary>Head against the chest: yaw to either side, extension (looking up), flexion (looking down).</summary>
        public static float HeadYawMax = 78f, HeadExtendMax = 105f, HeadFlexMax = 62f, HeadTiltMax = 30f;
        /// <summary>Forearm roll about its own axis (pronation / supination, from the T-pose), hand roll on the forearm, upper-arm roll, thigh roll.</summary>
        public static float ForearmRollMax = 88f, HandRollMax = 35f, UpperArmRollMax = 100f, ThighRollMax = 80f;
        /// <summary>Joint hinge: how far a straight arm / leg may bend backwards.</summary>
        public static float ElbowHyper = 4f, KneeHyper = 4f;
        /// <summary>Knees are measured but not corrected by default: swinging the shin about the knee moves the ankle by 0.45 m x sin(angle), which lifts a planted foot (a 16 degree fix is 12 cm); the foot-plant IK owns the leg.</summary>
        public static bool CorrectKnees = false;
        public static bool Enabled = true;

        // ---- QA counters (the worst excess corrected in the last frame, degrees)
        public float FixSpine, FixHead, FixForearm, FixHand, FixElbow, FixKnee, FixThigh, FixUpperArm;
        public int FramesFixed;
        /// <summary>QA: the signed bend of each elbow / knee (left, right) before the limit was applied this frame.</summary>
        public readonly float[] KneeRaw = new float[2], ElbowRaw = new float[2];
        /// <summary>True while a throw / wind-up pose is on (the shoulders are allowed to be turned further against the hips).</summary>
        public bool ThrowPose;

        sealed class Bn
        {
            public Transform T; public Quaternion Bind0;       // bind local rotation
            public Vector3 Axis;                               // bone axis in its own local space (towards the child)
            public Vector3 Hinge;                              // flexion axis in local space (elbow, knee)
            public Quaternion BindWorld;
            public Vector3 Fwd;                                // the body's forward at bind in this bone's local space (which way is the front of the limb)
        }

        Animator _an;
        Bn _spine, _chest, _uchest, _neck, _head;
        Bn[] _ua = new Bn[2], _la = new Bn[2], _hand = new Bn[2], _ul = new Bn[2], _ll = new Bn[2], _foot = new Bn[2];
        Transform _top, _hips;
        // chest frame at bind, expressed in the top spine bone's local space; the head forward in head space
        Vector3 _cf, _cu, _cr, _headFwd, _headRight, _bindFwd, _hipsFwd;
        bool _ok;

        public bool Ok { get { return _ok; } }

        /// <summary>Called right after the avatar is spawned (it is still in its bind pose).</summary>
        public void Init(Animator an)
        {
            _an = an;
            if (an == null || !an.isHuman) return;
            _hips = an.GetBoneTransform(HumanBodyBones.Hips);
            var lul = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var rul = an.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (_hips == null || lul == null || rul == null) return;
            Vector3 across = rul.position - lul.position; across.y = 0f;
            Vector3 bindFwd = across.sqrMagnitude > 1e-6f ? Vector3.Cross(across.normalized, Vector3.up) : Vector3.forward;
            _bindFwd = bindFwd; _hipsFwd = Quaternion.Inverse(_hips.rotation) * bindFwd;

            _spine = Make(HumanBodyBones.Spine, HumanBodyBones.Chest);
            _chest = Make(HumanBodyBones.Chest, an.GetBoneTransform(HumanBodyBones.UpperChest) != null ? HumanBodyBones.UpperChest : HumanBodyBones.Neck);
            _uchest = an.GetBoneTransform(HumanBodyBones.UpperChest) != null ? Make(HumanBodyBones.UpperChest, HumanBodyBones.Neck) : null;
            _neck = an.GetBoneTransform(HumanBodyBones.Neck) != null ? Make(HumanBodyBones.Neck, HumanBodyBones.Head) : null;
            _head = Make(HumanBodyBones.Head, HumanBodyBones.Head);
            if (_spine == null || _chest == null || _head == null) return;
            _top = _uchest != null ? _uchest.T : _chest.T;
            _cf = Quaternion.Inverse(_top.rotation) * bindFwd; _cu = Quaternion.Inverse(_top.rotation) * Vector3.up; _cr = Vector3.Cross(_cu, _cf).normalized;
            _headFwd = Quaternion.Inverse(_head.T.rotation) * bindFwd; _headRight = Quaternion.Inverse(_head.T.rotation) * Vector3.Cross(Vector3.up, bindFwd);
            for (int s = 0; s < 2; s++)
            {
                bool left = s == 0;
                _ua[s] = Make(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm, left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
                _la[s] = Make(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm, left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                _hand[s] = Make(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand, left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
                _ul[s] = Make(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg, left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
                _ll[s] = Make(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg, left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                _foot[s] = Make(left ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot, left ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes);
                if (_la[s] != null && _ua[s] != null)
                {
                    // elbow: the forearm folds towards the front of the body; hinge axis = (upper arm direction) x (body forward), in the forearm's space
                    Vector3 d = (_la[s].T.position - _ua[s].T.position); if (d.sqrMagnitude > 1e-8f) _la[s].Hinge = Quaternion.Inverse(_la[s].BindWorld) * Vector3.Cross(d.normalized, bindFwd).normalized;
                    if (_la[s].Hinge.sqrMagnitude > 0.5f) LimbIK.RegisterHinge(_la[s].T, _la[s].Hinge);      // the IK rolls the upper arm so this hinge lies across the bending plane
                }
                if (_ll[s] != null && _ul[s] != null)
                {
                    // knee: the shin folds backwards; hinge axis = (thigh direction) x (body backward)
                    Vector3 d = (_ll[s].T.position - _ul[s].T.position); if (d.sqrMagnitude > 1e-8f) _ll[s].Hinge = Quaternion.Inverse(_ll[s].BindWorld) * Vector3.Cross(d.normalized, -bindFwd).normalized;
                }
            }
            _ok = true;
        }

        void OnDestroy() { for (int s = 0; s < 2; s++) if (_la[s] != null) LimbIK.UnregisterHinge(_la[s].T); }

        Bn Make(HumanBodyBones b, HumanBodyBones child)
        {
            var t = _an.GetBoneTransform(b); if (t == null) return null;
            var c = _an.GetBoneTransform(child);
            var bn = new Bn { T = t, Bind0 = t.localRotation, BindWorld = t.rotation, Axis = Vector3.right, Fwd = Quaternion.Inverse(t.rotation) * _bindFwd };
            if (c != null && c != t && c.localPosition.sqrMagnitude > 1e-10f) bn.Axis = c.localPosition.normalized;
            return bn;
        }

        // ---------------------------------------------------------------------------------------------- math
        /// <summary>Roll (degrees, -180..180) of a bone about its own axis, against its bind pose (swing-twist of the local rotation delta).</summary>
        public static float Roll(Quaternion bind0, Quaternion now, Vector3 axis)
        {
            Quaternion d = Quaternion.Inverse(bind0) * now;
            if (d.w < 0f) { d.x = -d.x; d.y = -d.y; d.z = -d.z; d.w = -d.w; }
            float p = d.x * axis.x + d.y * axis.y + d.z * axis.z;
            float a = 2f * Mathf.Atan2(p, d.w) * Mathf.Rad2Deg;
            if (a > 180f) a -= 360f; else if (a < -180f) a += 360f;
            return a;
        }

        float RollOf(Bn b) { return Roll(b.Bind0, b.T.localRotation, b.Axis); }

        static void AddRoll(Bn b, float deg) { if (Mathf.Abs(deg) > 1e-3f) b.T.localRotation = b.T.localRotation * Quaternion.AngleAxis(deg, b.Axis); }

        /// <summary>QA: current spine twist (the three spine bones' roll added) in degrees.</summary>
        public float SpineTwistNow() { return _ok ? RollOf(_spine) + RollOf(_chest) + (_uchest != null ? RollOf(_uchest) : 0f) : 0f; }
        /// <summary>QA: head angles against the chest (yaw, pitch up +, tilt) in degrees.</summary>
        public void HeadAnglesNow(out float yaw, out float pitch)
        {
            yaw = pitch = 0f; if (!_ok) return;
            HeadAngles(out yaw, out pitch);
        }

        void HeadAngles(out float yaw, out float pitch)
        {
            // the head's lateral axis does not move when the head nods, so it gives the yaw about the chest's up axis without the singularity a face looking straight along the chest's up axis (a bent-over lineman looking forward) has;
            // the nod is then the angle of the face about that lateral axis, measured from the chest's front turned by the yaw
            Quaternion inv = Quaternion.Inverse(_top.rotation);
            Vector3 r = inv * (_head.T.rotation * _headRight);
            float rx = Vector3.Dot(r, _cr), rz = Vector3.Dot(r, _cf);
            float yawRad = Mathf.Atan2(-rz, rx);
            yaw = yawRad * Mathf.Rad2Deg;
            Vector3 f = inv * (_head.T.rotation * _headFwd);
            float fx = Vector3.Dot(f, _cr), fy = Vector3.Dot(f, _cu), fz = Vector3.Dot(f, _cf);
            float c = Mathf.Cos(yawRad), s = Mathf.Sin(yawRad);
            float zn = fx * s + fz * c;
            pitch = Mathf.Atan2(fy, zn) * Mathf.Rad2Deg;
        }

        // ---------------------------------------------------------------------------------------------- torso
        /// <summary>Spine twist and head limits. Call after the stance / lean layers and before the arm IK.</summary>
        public void Torso()
        {
            if (!Enabled || !_ok) return;
            FixSpine = FixHead = 0f;
            // ---- spine: the three bones' roll adds up to the chest-over-pelvis twist
            float rs = RollOf(_spine), rc = RollOf(_chest), ru = _uchest != null ? RollOf(_uchest) : 0f;
            float sum = rs + rc + ru, lim = ThrowPose ? SpineTwistMaxThrow : SpineTwistMax;
            if (Mathf.Abs(sum) > lim)
            {
                float target = Mathf.Sign(sum) * lim, k = target / sum;      // every bone keeps its share of the twist, scaled to the limit
                AddRoll(_spine, rs * k - rs); AddRoll(_chest, rc * k - rc); if (_uchest != null) AddRoll(_uchest, ru * k - ru);
                FixSpine = Mathf.Abs(sum) - lim;
            }
            // ---- neck and head against the chest
            float yaw, pitch; HeadAngles(out yaw, out pitch);
            float dy = 0f, dp = 0f;
            if (yaw > HeadYawMax) dy = HeadYawMax - yaw; else if (yaw < -HeadYawMax) dy = -HeadYawMax - yaw;
            if (pitch > HeadExtendMax) dp = HeadExtendMax - pitch; else if (pitch < -HeadFlexMax) dp = -HeadFlexMax - pitch;
            if (dy != 0f || dp != 0f)
            {
                // yaw about the chest's up axis, pitch about its right axis (world), split: 40 percent neck, the rest head
                Vector3 up = _top.rotation * _cu, right = _head.T.rotation * _headRight;
                Quaternion q = Quaternion.AngleAxis(dy, up) * Quaternion.AngleAxis(-dp, right);
                if (_neck != null)
                {
                    Quaternion qn = Quaternion.Slerp(Quaternion.identity, q, 0.4f);
                    _neck.T.rotation = qn * _neck.T.rotation;
                    q = Quaternion.Slerp(Quaternion.identity, q, 0.6f) ;
                }
                _head.T.rotation = q * _head.T.rotation;
                FixHead = Mathf.Max(Mathf.Abs(dy), Mathf.Abs(dp));
            }
            if (FixSpine > 0f || FixHead > 0f) FramesFixed++;
        }

        // ---------------------------------------------------------------------------------------------- limbs
        /// <summary>Forearm / hand roll, upper-arm and thigh roll, elbow and knee hyperextension. Runs after every body layer.</summary>
        void LateUpdate()
        {
            if (Enabled && _ok) Limbs();
        }

        public void Limbs()
        {
            FixForearm = FixHand = FixElbow = FixKnee = FixThigh = FixUpperArm = 0f;
            for (int s = 0; s < 2; s++)
            {
                // ---- hand roll on the forearm: share the excess with the forearm (hand orientation in the world stays, the twist is split over two joints instead of one)
                var la = _la[s]; var hd = _hand[s];
                if (la != null && hd != null)
                {
                    float rh = RollOf(hd);
                    if (Mathf.Abs(rh) > HandRollMax)
                    {
                        float excess = rh - Mathf.Sign(rh) * HandRollMax;
                        // the excess roll moves from the wrist to the forearm bone and the hand keeps its orientation in the world: the same pose with the twist spread over the forearm instead of pinched at the wrist
                        Quaternion handWorld = hd.T.rotation;
                        AddRoll(la, excess);
                        hd.T.rotation = handWorld;
                        FixHand = Mathf.Max(FixHand, Mathf.Abs(excess));
                    }
                    float rf = RollOf(la);
                    if (Mathf.Abs(rf) > ForearmRollMax)
                    {
                        float excess = rf - Mathf.Sign(rf) * ForearmRollMax;
                        AddRoll(la, -excess);
                        FixForearm = Mathf.Max(FixForearm, Mathf.Abs(excess));
                    }
                }
                var ua = _ua[s];
                if (ua != null)
                {
                    float ru = RollOf(ua);
                    if (Mathf.Abs(ru) > UpperArmRollMax) { float excess = ru - Mathf.Sign(ru) * UpperArmRollMax; AddRoll(ua, -excess); FixUpperArm = Mathf.Max(FixUpperArm, Mathf.Abs(excess)); }
                }
                var ul = _ul[s]; var ll0 = _ll[s];
                if (ul != null)
                {
                    float rt = RollOf(ul);
                    if (Mathf.Abs(rt) > ThighRollMax) { float excess = rt - Mathf.Sign(rt) * ThighRollMax; AddRoll(ul, -excess); FixThigh = Mathf.Max(FixThigh, Mathf.Abs(excess)); }
                }
                // ---- elbow / knee: a straight limb does not bend backwards
                if (la != null && ua != null && hd != null) ElbowRaw[s] = BendSigned(ua, la, hd.T, true);
                if (ll0 != null && ul != null && _foot[s] != null) KneeRaw[s] = BendSigned(ul, ll0, _foot[s].T, false);
                if (la != null && ua != null && hd != null) FixElbow = Mathf.Max(FixElbow, Hyper(ua, la, hd.T, true, ElbowHyper));
                var ll = _ll[s];
                if (CorrectKnees && ll != null && ul != null && _foot[s] != null) FixKnee = Mathf.Max(FixKnee, Hyper(ul, ll, _foot[s].T, false, KneeHyper));
            }
        }

        /// <summary>
        /// Signed bend of a two-bone limb from positions alone (degrees; positive = folded the way the joint folds, negative = bent backwards). The limb's front is the body's forward carried by the upper bone;
        /// an elbow folds towards it, a knee folds away from it. (The first version read the angle off the bone's local rotation about a hinge axis and mistook a splayed, rolled knee for a hyperextended one.)
        /// </summary>
        float BendSigned(Bn upper, Bn lower, Transform end, bool elbow)
        {
            Vector3 vt = lower.T.position - upper.T.position, vs = end.position - lower.T.position;
            if (vt.sqrMagnitude < 1e-8f || vs.sqrMagnitude < 1e-8f) return 0f;
            vt.Normalize(); vs.Normalize();
            float phi = Vector3.Angle(vt, vs);
            if (phi < 0.5f) return 0f;
            Vector3 f = upper.T.rotation * upper.Fwd; f -= Vector3.Dot(f, vt) * vt;
            if (f.sqrMagnitude < 1e-6f) return phi;
            float side = Vector3.Dot(vs - vt, f);
            bool flexion = elbow ? side > 0f : side < 0f;
            if (!elbow && !flexion)
            {
                // a knee's front by the thigh bone is unreliable once the thigh is flexed far and rolled (a centre in a deep crouch read as a backwards knee of 107 degrees): the pelvis has to agree
                Vector3 chord = end.position - upper.T.position; Vector3 pf = _hips.rotation * _hipsFwd; pf -= Vector3.Dot(pf, chord.normalized) * chord.normalized;
                if (pf.sqrMagnitude > 1e-4f && Vector3.Dot(lower.T.position - (upper.T.position + chord * 0.5f), pf) > 0f) flexion = true;
            }
            return flexion ? phi : -phi;
        }

        float Hyper(Bn upper, Bn lower, Transform end, bool elbow, float allow)
        {
            float a = BendSigned(upper, lower, end, elbow);
            if (a >= -allow || a < -45f) return 0f;      // a bend of more than 45 degrees the wrong way is a mistaken front, not a pose
            float fix = -a - allow;
            Vector3 vt = (lower.T.position - upper.T.position).normalized, vs = (end.position - lower.T.position).normalized;
            Vector3 axis = Vector3.Cross(vs, vt); if (axis.sqrMagnitude < 1e-8f) return 0f;
            lower.T.rotation = Quaternion.AngleAxis(fix, axis.normalized) * lower.T.rotation;
            return fix;
        }

        /// <summary>QA: signed bend of the elbow / knee of one side (0 left, 1 right); negative = backwards.</summary>
        public float ElbowAngle(int side) { return _ok && _la[side] != null && _ua[side] != null && _hand[side] != null ? BendSigned(_ua[side], _la[side], _hand[side].T, true) : 0f; }
        public float KneeAngle(int side) { return _ok && _ll[side] != null && _ul[side] != null && _foot[side] != null ? BendSigned(_ul[side], _ll[side], _foot[side].T, false) : 0f; }
        public float ForearmRoll(int side) { return _ok && _la[side] != null ? RollOf(_la[side]) : 0f; }
        public float HandRoll(int side) { return _ok && _hand[side] != null ? RollOf(_hand[side]) : 0f; }
        public float UpperArmRoll(int side) { return _ok && _ua[side] != null ? RollOf(_ua[side]) : 0f; }
        public float ThighRoll(int side) { return _ok && _ul[side] != null ? RollOf(_ul[side]) : 0f; }
    }
}
