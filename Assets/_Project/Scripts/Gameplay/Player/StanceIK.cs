using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>Per-frame inputs of the stance pose layer that change while the player stands (quarterback cadence, the snap, the head look).</summary>
    public struct StanceDyn
    {
        /// <summary>Hips drop (m, the hips go down on "go" so the hands stay under the centre) and an extra forward lean (degrees).</summary>
        public float HipsDrop, LeanAdd;
        /// <summary>Head yaw / pitch added on top of the stance's own look (degrees; a head on a swivel).</summary>
        public float HeadYaw, HeadPitch;
        /// <summary>World point the Anchor hands go to (the centre's rear for a quarterback under centre, the ball for a snapper) and whether there is one.</summary>
        public Vector3 Anchor; public bool HasAnchor;
        /// <summary>World point both hands reach to (the incoming snap) with a 0..1 weight; overrides the stance's own hands.</summary>
        public Vector3 Reach; public float ReachW;
        /// <summary>World point the head looks at (ball, quarterback, line of scrimmage) with a 0..1 weight (0 = the stance's own look).</summary>
        public Vector3 Look; public float LookW;
        /// <summary>The feet follow a footfall planner (the drop, the pocket slide) instead of the stance: world ankle targets, their toe yaw and how far each heel / foot is lifted (m).</summary>
        public bool GaitOn; public Vector3 RFoot, LFoot; public float RFootYaw, LFootYaw, RLift, LLift;
        /// <summary>The shoulders turn this many degrees from the facing (+ = to the player's right) while the hips stay: the quarterback looking back over his shoulder on the drop.</summary>
        public float UpperYaw;
        /// <summary>Shifts the hands that are at the chest (local x right, y up, z forward, metres): the ball carried out toward the runner on a fake hand-off.</summary>
        public Vector3 ChestShift;
    }

    /// <summary>
    /// v0.9.0 QBSTANCE (E-045..E-068, E-055, E-060): the stance pose layer. The mocap stance clips are the wrong shape (linemen standing with their hands on their knees, the quarterback upright, every receiver 14 cm staggered) and were not authored for the photoreal bodies:
    /// this layer solves the authored <see cref="StancePose"/> over whatever the animation does in LateUpdate: hips placed and turned, the spine laid out hips-to-head, both legs by two-bone IK onto the turf (the heel up where the stance says so), the down hand IK'd onto the turf
    /// (the trunk deepens until the shoulder can reach), the other hand on the thigh / knee / at the chest, the head looking where the stance looks. The mocap underneath still supplies the breathing and weight shifts of the first few percent;
    /// the layer fades in and out (0.22 s, the same as a clip cross-fade) so a get-off does not pop.
    /// </summary>
    public sealed class StanceIK
    {
        readonly Animator _an;
        readonly Transform _hips, _spine, _chest, _upperChest, _neck, _head, _lul, _lll, _lf, _lt, _rul, _rll, _rf, _rt, _lua, _lla, _lh, _rua, _rla, _rh, _ls, _rs;
        readonly bool _ok;
        float _w, _t;
        float _uy;      // shoulder yaw of this frame (pose + input)
        readonly float _seed;
        /// <summary>Hips height of the standing bind pose (m) and the ankle height above the turf (m): the body scale of this avatar.</summary>
        public readonly float StandHips, RestAnkle;
        readonly Vector3 _headLocalFwd;
        /// <summary>Pitch (radians, + = toes down) of the ankle-to-toes vector of a foot flat on the turf in the bind pose: the stance foot keeps it (the ball of the foot is lower than the ankle).</summary>
        readonly float _footPitch;
        // arm / leg lengths (bone distances never change)
        readonly float _armLen;

        public StanceIK(Animator an, int seed)
        {
            _an = an; _seed = seed * 0.713f;
            if (an == null || !an.isHuman) return;
            _hips = an.GetBoneTransform(HumanBodyBones.Hips); _spine = an.GetBoneTransform(HumanBodyBones.Spine); _chest = an.GetBoneTransform(HumanBodyBones.Chest);
            _upperChest = an.GetBoneTransform(HumanBodyBones.UpperChest); _neck = an.GetBoneTransform(HumanBodyBones.Neck); _head = an.GetBoneTransform(HumanBodyBones.Head);
            _lul = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg); _lll = an.GetBoneTransform(HumanBodyBones.LeftLowerLeg); _lf = an.GetBoneTransform(HumanBodyBones.LeftFoot); _lt = an.GetBoneTransform(HumanBodyBones.LeftToes);
            _rul = an.GetBoneTransform(HumanBodyBones.RightUpperLeg); _rll = an.GetBoneTransform(HumanBodyBones.RightLowerLeg); _rf = an.GetBoneTransform(HumanBodyBones.RightFoot); _rt = an.GetBoneTransform(HumanBodyBones.RightToes);
            _lua = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); _lla = an.GetBoneTransform(HumanBodyBones.LeftLowerArm); _lh = an.GetBoneTransform(HumanBodyBones.LeftHand);
            _rua = an.GetBoneTransform(HumanBodyBones.RightUpperArm); _rla = an.GetBoneTransform(HumanBodyBones.RightLowerArm); _rh = an.GetBoneTransform(HumanBodyBones.RightHand);
            _ls = an.GetBoneTransform(HumanBodyBones.LeftShoulder); _rs = an.GetBoneTransform(HumanBodyBones.RightShoulder);
            if (_hips == null || _spine == null || _chest == null || _head == null || _lul == null || _lll == null || _lf == null || _rul == null || _rll == null || _rf == null
                || _lua == null || _lla == null || _lh == null || _rua == null || _rla == null || _rh == null) return;
            // the avatar was just created in its bind pose: standing straight, facing forward
            float gy = an.transform.position.y;
            StandHips = _hips.position.y - gy;
            RestAnkle = Mathf.Max(0.03f, Mathf.Min(_lf.position.y, _rf.position.y) - gy);
            Vector3 across = _rul.position - _lul.position; across.y = 0f;
            Vector3 bindFwd = across.sqrMagnitude > 1e-6f ? Vector3.Cross(across.normalized, Vector3.up) : Vector3.forward;
            _headLocalFwd = Quaternion.Inverse(_head.rotation) * bindFwd;
            if (_lt != null && _rt != null) { Vector3 tl = _lt.position - _lf.position, tr = _rt.position - _rf.position; _footPitch = 0.5f * (Mathf.Asin(Mathf.Clamp(-tl.y / Mathf.Max(0.02f, tl.magnitude), -1f, 1f)) + Mathf.Asin(Mathf.Clamp(-tr.y / Mathf.Max(0.02f, tr.magnitude), -1f, 1f))); }
            _armLen = Vector3.Distance(_rua.position, _rla.position) + Vector3.Distance(_rla.position, _rh.position);
            _ok = StandHips > 0.4f;
        }

        public bool Ok { get { return _ok; } }
        /// <summary>True while the layer has any weight.</summary>
        public bool Active { get { return _w > 0.001f; } }
        /// <summary>Fade weight of the layer (0 = the animation only).</summary>
        public float Weight { get { return _w; } }

        /// <summary>QA: the pose scale of this body (1 = the 1.06 m hips reference).</summary>
        public float Scale { get { return StandHips / 1.06f; } }

        public void Cancel() { _w = 0f; }

        /// <summary>Segment alignment: rotates <paramref name="b"/> (and everything below it) so the vector to <paramref name="c"/> points along the lean direction, by <paramref name="w"/>.</summary>
        static void Align(Transform b, Transform c, Vector3 fwd, float angleDeg, float w)
        {
            if (b == null || c == null || w <= 0.001f) return;
            Vector3 cur = c.position - b.position;
            float len = cur.magnitude; if (len < 1e-4f) return;
            float a = angleDeg * Mathf.Deg2Rad;
            Vector3 want = (fwd * Mathf.Sin(a) + Vector3.up * Mathf.Cos(a)) * len;
            Quaternion d = Quaternion.FromToRotation(cur, want);
            b.rotation = Quaternion.Slerp(Quaternion.identity, d, w) * b.rotation;
        }

        static float Eased(float w) { w = Mathf.Clamp01(w); return w * w * (3f - 2f * w); }

        /// <summary>QA: shoulder height above the turf the last solve ended with, the lean it settled on, and the lowest hand height (m).</summary>
        public float LastLean, LastShoulderY, LastLowHand;

        /// <summary>LateUpdate, after the animation and the other body layers.</summary>
        public void Late(float dt, bool active, in StancePose p, Vector3 rootPos, float yawDeg, in StanceDyn dyn)
        {
            if (!_ok) return;
            float target = active && p.Valid ? 1f : 0f;
            _w = Mathf.MoveTowards(_w, target, dt / (active ? 0.20f : 0.22f));
            if (_w <= 0.001f) { if (target <= 0f) _w = 0f; return; }
            _t += dt;
            float w = Eased(_w);
            float S = StandHips / 1.06f;
            float gy = rootPos.y;
            float yaw = yawDeg + p.Tilt;
            Vector3 fwd = new Vector3(Mathf.Sin(yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(yaw * Mathf.Deg2Rad));
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float breath = Mathf.Sin(_t * 1.7f + _seed);
            float sway = Mathf.Sin(_t * 0.55f + _seed * 1.9f);

            // ---- hips: turned to the stance yaw, then placed
            Vector3 across = _rul.position - _lul.position; across.y = 0f;
            if (across.sqrMagnitude > 1e-6f)
            {
                float cur = BodyHeading.YawOfAcross(across);
                float dy = Mathf.DeltaAngle(cur, yaw) * w;
                _hips.rotation = Quaternion.AngleAxis(dy, Vector3.up) * _hips.rotation;
            }
            float hipsY = (p.Hips * S - dyn.HipsDrop) + breath * 0.004f;
            Vector3 hp = rootPos + right * (p.HipsX * S) + fwd * (p.HipsZ * S + sway * 0.006f);
            hp.y = gy + hipsY;
            _hips.position = Vector3.Lerp(_hips.position, hp, w);

            // ---- feet first (they do not depend on the trunk): onto the turf, heel up where the stance says so
            Vector3 rAnkle = rootPos + right * (p.RX * S) + fwd * (p.RZ * S); rAnkle.y = gy + RestAnkle + p.RHeel;
            Vector3 lAnkle = rootPos + right * (p.LX * S) + fwd * (p.LZ * S); lAnkle.y = gy + RestAnkle + p.LHeel;
            if (dyn.GaitOn) { rAnkle = dyn.RFoot; lAnkle = dyn.LFoot; }

            // ---- trunk: lay the spine out hips to head; a hand on the turf deepens the lean until the shoulder can reach it
            bool rGround = p.RHand == HandMode.Ground, lGround = p.LHand == HandMode.Ground;
            float lean = p.Lean + dyn.LeanAdd + breath * 0.5f;
            float rY = gy + p.RY, lY = gy + p.LY;
            Lay(fwd, lean, p, w);
            if ((rGround || lGround) && w > 0.95f)
            {
                // the down hand has to reach the turf: deepen the lean until the shoulder is low enough (a short body, a tall body, a deep hips setting all settle by themselves)
                float reach = _armLen * 0.97f;
                float hipToShoulder = (_chest.position - _hips.position).magnitude * 1.15f;
                for (int i = 0; i < 4; i++)
                {
                    float need = 0f;
                    if (rGround) need = Mathf.Max(need, ReachDeficit(_rua, rootPos + right * (p.RPos.x * S) + fwd * (p.RPos.z * S), rY, reach));
                    if (lGround) need = Mathf.Max(need, ReachDeficit(_lua, rootPos + right * (p.LPos.x * S) + fwd * (p.LPos.z * S), lY, reach));
                    if (need <= 0.006f || lean >= p.MaxLean) break;
                    float dLean = Mathf.Clamp(need / (hipToShoulder * Mathf.Max(0.35f, Mathf.Sin(lean * Mathf.Deg2Rad))) * Mathf.Rad2Deg, 0.5f, 14f);
                    lean = Mathf.Min(p.MaxLean, lean + dLean);
                    Lay(fwd, lean, p, w);
                }
                // still too high (a tall body in a high-hips stance): sink the hips (at most 14 cm) so the hand reaches
                float sunk = 0f;
                for (int i = 0; i < 3; i++)
                {
                    float need = 0f;
                    if (rGround) need = Mathf.Max(need, ReachDeficit(_rua, rootPos + right * (p.RPos.x * S) + fwd * (p.RPos.z * S), rY, reach));
                    if (lGround) need = Mathf.Max(need, ReachDeficit(_lua, rootPos + right * (p.LPos.x * S) + fwd * (p.LPos.z * S), lY, reach));
                    float drop = Mathf.Min(need, 0.16f - sunk);
                    if (need <= 0.006f || drop <= 0.002f) break;
                    _hips.position += Vector3.down * drop; sunk += drop;
                }
            }
            LastLean = lean;
            _uy = dyn.UpperYaw + p.UpperYaw;
            if (Mathf.Abs(_uy) > 0.1f && w > 0.01f)
            {
                // the shoulders open toward the field while the hips stay: half of the turn in the lower spine, half in the chest
                float uy = _uy * w;
                Vector3 pivot = _spine.position;
                _spine.rotation = Quaternion.AngleAxis(uy * 0.45f, Vector3.up) * _spine.rotation;
                _chest.rotation = Quaternion.AngleAxis(uy * 0.55f, Vector3.up) * _chest.rotation;
            }

            // ---- legs
            Vector3 kneePoleR = fwd + right * 0.35f, kneePoleL = fwd - right * 0.35f;
            LimbIK.Apply(_rul, _rll, _rf, rAnkle, kneePoleR, w);
            LimbIK.Apply(_lul, _lll, _lf, lAnkle, kneePoleL, w);
            if (dyn.GaitOn)
            {
                ShapeFoot(_rf, _rt, FwdOf(dyn.RFootYaw), RightOf(dyn.RFootYaw), 0f, dyn.RLift * 0.8f, w);
                ShapeFoot(_lf, _lt, FwdOf(dyn.LFootYaw), RightOf(dyn.LFootYaw), 0f, dyn.LLift * 0.8f, w);
            }
            else
            {
                ShapeFoot(_rf, _rt, fwd, right, p.ToeOut, p.RHeel, w);
                ShapeFoot(_lf, _lt, fwd, right, -p.ToeOut, p.LHeel, w);
            }

            // ---- arms
            float lowHand = 9f;
            ApplyHand(_rua, _rla, _rh, true, p.RHand, p.RPos, rY, rootPos, fwd, right, gy, S, dyn, w, ref lowHand);
            ApplyHand(_lua, _lla, _lh, false, p.LHand, p.LPos, lY, rootPos, fwd, right, gy, S, dyn, w, ref lowHand);
            LastLowHand = lowHand;
            LastShoulderY = (_rua.position.y + _lua.position.y) * 0.5f - gy;

            // ---- head: the look of the stance (the ball, the defence, the receiver), plus the cadence swivel
            ApplyHead(p, rootPos, fwd, right, yaw, dyn, w);
        }

        /// <summary>Lays the spine out hips to head for a trunk lean (degrees from vertical).</summary>
        void Lay(Vector3 fwd, float lean, in StancePose p, float w)
        {
            float bend = p.Bend;
            Align(_hips, _spine, fwd, lean - bend, w);
            Transform up1 = _upperChest != null ? _upperChest : (_neck != null ? _neck : _head);
            Align(_spine, _chest, fwd, lean + bend, w);
            float neck = Mathf.Min(p.Neck + Mathf.Max(0f, lean - p.Lean) * 0.2f, 70f);
            Align(_chest, up1, fwd, neck, w);
            if (_upperChest != null) Align(_upperChest, _neck != null ? _neck : _head, fwd, neck, w);
            if (_neck != null) Align(_neck, _head, fwd, neck * 0.8f, w);
        }

        /// <summary>How far (m) the shoulder is too high for the arm to reach the target wrist height.</summary>
        static float ReachDeficit(Transform shoulder, Vector3 target, float wristY, float reach)
        {
            Vector3 s = shoulder.position;
            float dh = new Vector2(s.x - target.x, s.z - target.z).magnitude;
            float maxDy = Mathf.Sqrt(Mathf.Max(0f, reach * reach - dh * dh));
            return (s.y - wristY) - maxDy;
        }

        static Vector3 FwdOf(float yawDeg) { float a = yawDeg * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)); }
        static Vector3 RightOf(float yawDeg) { return Vector3.Cross(Vector3.up, FwdOf(yawDeg)); }

        void ShapeFoot(Transform foot, Transform toes, Vector3 fwd, Vector3 right, float toeOut, float heel, float w)
        {
            if (foot == null || toes == null) return;
            Vector3 cur = toes.position - foot.position;
            if (cur.sqrMagnitude < 1e-6f) return;
            // toes point along the stance heading (turned out by toeOut), pitched down by the heel raise (the foot hinges on the ball of the foot)
            float a = toeOut * Mathf.Deg2Rad;
            Vector3 flat = fwd * Mathf.Cos(a) + right * Mathf.Sin(a);
            float pitch = _footPitch + Mathf.Asin(Mathf.Clamp(heel / Mathf.Max(0.12f, cur.magnitude + 0.08f), 0f, 0.6f));
            Vector3 want = (flat * Mathf.Cos(pitch) + Vector3.down * Mathf.Sin(pitch)) * cur.magnitude;
            Quaternion d = Quaternion.FromToRotation(cur, want);
            foot.rotation = Quaternion.Slerp(Quaternion.identity, d, w) * foot.rotation;
        }

        void ApplyHand(Transform upper, Transform lower, Transform hand, bool rightSide, HandMode mode, Vector3 pos, float wristY, Vector3 root, Vector3 fwd, Vector3 right, float gy, float S, in StanceDyn dyn, float w, ref float lowHand)
        {
            Vector3 tgt; Vector3 pole = (rightSide ? right : -right) * 0.6f - fwd * 0.3f + Vector3.down * 0.4f;
            var knee = rightSide ? _rll : _lll; var thigh = rightSide ? _rul : _lul;
            float k = w;
            switch (mode)
            {
                case HandMode.Free: lowHand = Mathf.Min(lowHand, hand.position.y - gy); return;
                case HandMode.Ground:
                    tgt = root + right * (pos.x * S) + fwd * (pos.z * S);
                    // the hand goes down in front of the shoulder, on that side of the body
                    Vector3 shoulder = upper.position;
                    tgt = new Vector3(tgt.x, wristY, tgt.z);
                    Vector3 toS = tgt - new Vector3(shoulder.x, tgt.y, shoulder.z);
                    pole = (rightSide ? right : -right) * 0.5f - fwd * 0.5f;
                    break;
                case HandMode.Thigh:
                    tgt = Vector3.Lerp(thigh.position, knee.position, 0.62f) + Vector3.up * (pos.y * S) + fwd * (pos.z * S) + (rightSide ? -right : right) * 0.02f; break;
                case HandMode.Knee:
                    tgt = knee.position + Vector3.up * (pos.y * S) + fwd * (pos.z * S) + right * (pos.x * S); break;
                case HandMode.Hip:
                    tgt = _hips.position + right * ((rightSide ? 0.24f : -0.24f) * S + pos.x * S) + Vector3.up * (-0.04f * S + pos.y * S) + fwd * (pos.z * S); break;
                case HandMode.Chest: case HandMode.Cock:
                {
                    // offsets follow the shoulders (the closed front shoulder of a throw turns them with the chest)
                    Quaternion turn = Quaternion.AngleAxis(_uy * w, Vector3.up);
                    Vector3 c = _chest.position;
                    Vector3 off = right * (pos.x * S) + Vector3.up * (pos.y * S) + fwd * (pos.z * S) + (right * dyn.ChestShift.x + Vector3.up * dyn.ChestShift.y + fwd * dyn.ChestShift.z) * S;
                    tgt = c + turn * off;
                    pole = mode == HandMode.Cock ? turn * ((rightSide ? right : -right) * 0.8f + Vector3.up * 0.45f - fwd * 0.1f) : turn * ((rightSide ? right : -right) * 0.7f + Vector3.down * 0.5f - fwd * 0.2f);
                    break;
                }
                case HandMode.Anchor:
                    if (!dyn.HasAnchor) { lowHand = Mathf.Min(lowHand, hand.position.y - gy); return; }
                    tgt = dyn.Anchor + right * (pos.x * S);
                    tgt.y = wristY > 0f ? Mathf.Max(wristY, gy + 0.05f) : tgt.y;
                    pole = (rightSide ? right : -right) * 0.5f + Vector3.down * 0.7f;
                    break;
                default: return;
            }
            if (dyn.ReachW > 0.01f && mode != HandMode.Ground)
            {
                Vector3 reach = dyn.Reach + right * (rightSide ? 0.05f : -0.05f);
                tgt = Vector3.Lerp(tgt, reach, Mathf.Clamp01(dyn.ReachW));
            }
            LimbIK.Apply(upper, lower, hand, tgt, pole, k);
            lowHand = Mathf.Min(lowHand, hand.position.y - gy);
        }

        void ApplyHead(in StancePose p, Vector3 root, Vector3 fwd, Vector3 right, float yaw, in StanceDyn dyn, float w)
        {
            if (_head == null) return;
            float hy = p.HeadYaw + dyn.HeadYaw, hpit = p.HeadPitch + dyn.HeadPitch;
            Vector3 dir;
            if (dyn.LookW > 0.01f)
            {
                Vector3 to = dyn.Look - _head.position;
                if (to.sqrMagnitude > 0.01f)
                {
                    Vector3 look = to.normalized;
                    Vector3 baseDir = Quaternion.AngleAxis(hy, Vector3.up) * (fwd * Mathf.Cos(hpit * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(hpit * Mathf.Deg2Rad));
                    dir = Vector3.Slerp(baseDir, look, Mathf.Clamp01(dyn.LookW)).normalized;
                }
                else dir = fwd;
            }
            else dir = Quaternion.AngleAxis(hy, Vector3.up) * (fwd * Mathf.Cos(hpit * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(hpit * Mathf.Deg2Rad));
            // a head can only turn so far from the chest: 75 degrees of yaw
            Vector3 chestF = Quaternion.AngleAxis(_uy, Vector3.up) * fwd;
            Vector3 dh = new Vector3(dir.x, 0f, dir.z);
            if (dh.sqrMagnitude > 1e-4f)
            {
                float ang = Vector3.SignedAngle(chestF, dh, Vector3.up);
                if (Mathf.Abs(ang) > 75f) dir = Quaternion.AngleAxis(Mathf.Sign(ang) * 75f - ang, Vector3.up) * dir;
            }
            Vector3 headFwd = _head.rotation * _headLocalFwd;
            Quaternion d = Quaternion.FromToRotation(headFwd, dir);
            _head.rotation = Quaternion.Slerp(Quaternion.identity, d, w) * _head.rotation;
        }
    }
}
