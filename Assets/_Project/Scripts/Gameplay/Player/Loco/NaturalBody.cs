using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.12.0 NATURAL (owner notes 1 and 3): the pose layer that takes the robot out of the body and keeps the head and hips inside what a spine can do.
    ///  * measurement: the yaw (about the world vertical) of the pelvis, the chest and the head from the bones, relative to the bind pose (so any skeleton, any clip), used by the layers below and by <c>NaturalProbe</c>;
    ///  * <see cref="Secondary"/> (before the foot planter): arm swing, shoulder counter-rotation and head stabilization of a running body, scaled by his own <see cref="NaturalStyle"/>;
    ///  * <see cref="Idle"/>: breathing, weight shifts and glances for a body that stands (not the stance sets: those belong to the stance layer);
    ///  * <see cref="LookLimit"/> (on the final pose): the chest over the pelvis and the head over the chest stay inside <see cref="NaturalLook"/>'s limits and rates; the look is shared, not over-twisted.
    /// Everything is a delta on the pose the animation, the matcher and the other layers produced: it works over every clip and fades out for ragdolls, authored contact takes and the quarterback's throw.
    /// </summary>
    public sealed class NaturalBody
    {
        /// <summary>QA: -pixNatOff switches the whole layer off (the v0.12.0 GAIT state); the others switch one part off.</summary>
        public static bool Enabled = true, NoSecondary, NoIdle, NoLook, NoTempo, NoFacing;
        public static bool NoHeadAll = StanceRules.V012;      // v0.13.0: NoHeadAll = the v0.12 coverage of the head limit (-pixNatNoHeadAll)
        /// <summary>v0.13.0 STANCE-HEAD: how far the head may turn over the chest (degrees, soft limit: linear to 80 % of it) and how fast it may turn (degrees per second). v0.12 had 70 / 650 (NaturalLook.NeckMax / NeckRate): head over chest p99 68.6 and the owner still saw heads cranked round.</summary>
        public static float HeadMax = StanceRules.V012 ? 70f : 48f, HeadRate = StanceRules.V012 ? 650f : 380f;

        readonly Animator _an; readonly Transform _root;
        Transform _hips, _spine, _chest, _neck, _head, _lua, _rua, _lla, _rla;
        Vector3 _fHips, _fChest, _fHead;
        Quaternion _hRelBind = Quaternion.identity; Vector3 _hAxis = Vector3.up;
        /// <summary>The bones were found and the bind pose captured.</summary>
        public bool Ok { get; private set; }
        public NaturalStyle Style = NaturalStyle.Neutral;
        float _t;

        public NaturalBody(Animator an)
        {
            _an = an;
            if (an == null || !an.isHuman) return;
            _root = an.transform;
            _hips = an.GetBoneTransform(HumanBodyBones.Hips); _spine = an.GetBoneTransform(HumanBodyBones.Spine);
            _chest = an.GetBoneTransform(HumanBodyBones.UpperChest) ?? an.GetBoneTransform(HumanBodyBones.Chest);
            _neck = an.GetBoneTransform(HumanBodyBones.Neck); _head = an.GetBoneTransform(HumanBodyBones.Head);
            _lua = an.GetBoneTransform(HumanBodyBones.LeftUpperArm); _rua = an.GetBoneTransform(HumanBodyBones.RightUpperArm);
            _lla = an.GetBoneTransform(HumanBodyBones.LeftLowerArm); _rla = an.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var lul = an.GetBoneTransform(HumanBodyBones.LeftUpperLeg); var rul = an.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            if (_hips == null || _chest == null || _head == null || lul == null || rul == null) return;
            Vector3 across = rul.position - lul.position; across.y = 0f;
            if (across.sqrMagnitude < 1e-6f) return;
            Vector3 fwd = Vector3.Cross(across.normalized, Vector3.up);      // the facing of the bind pose (BodyHeading.YawOfAcross uses the same convention)
            _fHips = Quaternion.Inverse(_hips.rotation) * fwd; _fChest = Quaternion.Inverse(_chest.rotation) * fwd; _fHead = Quaternion.Inverse(_head.rotation) * fwd;
            _hRelBind = Quaternion.Inverse(_chest.rotation) * _head.rotation;      // v0.13.0: the head's bind orientation in the chest's frame, and the neck axis (chest to head) in the same frame
            Vector3 up = _head.position - _chest.position; _hAxis = up.sqrMagnitude > 1e-6f ? Quaternion.Inverse(_chest.rotation) * up.normalized : Vector3.up;
            Ok = true;
        }

        public void SetStyle(NaturalStyle s) { Style = s; _t = s.Phase0 * 40f; }

        // ------------------------------------------------------------------------------------------ measurement
        static float YawOf(Vector3 v) { return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg; }
        /// <summary>World yaw (degrees about +Y, 0 = +Z) the pelvis, the chest and the head face right now.</summary>
        public float PelvisYaw { get { return Ok ? YawOf(_hips.rotation * _fHips) : 0f; } }
        public float ChestYaw { get { return Ok ? YawOf(_chest.rotation * _fChest) : 0f; } }
        public float HeadYaw { get { return Ok ? YawOf(_head.rotation * _fHead) : 0f; } }
        /// <summary>Chest over pelvis and head over chest (degrees, + = to the right of the body).</summary>
        public float ChestOverPelvis { get { return Ok ? Mathf.DeltaAngle(PelvisYaw, ChestYaw) : 0f; } }
        public float HeadOverChest { get { return !Ok ? 0f : (StanceRules.V012 ? Mathf.DeltaAngle(ChestYaw, HeadYaw) : HeadTwist); } }
        /// <summary>
        /// v0.13.0 STANCE-HEAD: the turn of the head about the neck axis against the chest (degrees, + = to the right of the body; swing-twist of the head's rotation over the chest, the neck's own nod ignored).
        /// The v0.12 measure was the difference of two world yaws of the bind-forward axes: for a bent-over body (a three-point lineman, the centre, a long snapper, a holder) those axes point at the turf and their yaw is noise, so the audit read 70 degrees
        /// for 30 % of the line frames (and the limiter then twisted the head to the limit). The twist about the neck axis has no such singularity.
        /// </summary>
        public float HeadTwist
        {
            get
            {
                if (!Ok) return 0f;
                Quaternion d = (Quaternion.Inverse(_chest.rotation) * _head.rotation) * Quaternion.Inverse(_hRelBind);
                if (d.w < 0f) d = new Quaternion(-d.x, -d.y, -d.z, -d.w);
                float s = d.x * _hAxis.x + d.y * _hAxis.y + d.z * _hAxis.z;
                return Mathf.DeltaAngle(0f, 2f * Mathf.Atan2(s, d.w) * Mathf.Rad2Deg);
            }
        }
        /// <summary>World axis of the neck (the chest's frame axis of the head) for rotating the head about it.</summary>
        Vector3 NeckAxisWorld { get { return _chest.rotation * _hAxis; } }
        /// <summary>Head pitch of the bind-pose forward axis (degrees, + = looking down) for QA.</summary>
        public float HeadPitch { get { if (!Ok) return 0f; Vector3 v = _head.rotation * _fHead; return -Mathf.Asin(Mathf.Clamp(v.y, -1f, 1f)) * Mathf.Rad2Deg; } }

        // ------------------------------------------------------------------------------------------ running: arms, counter-rotation, head, posture
        float _gate, _idleW;
        float _armMeanL, _armMeanR; bool _armHave;
        float _cMean, _hMean; bool _cHave, _hHave;
        /// <summary>QA: what the layers added this frame (degrees): arm delta (max of both), extra counter-rotation, head cancel.</summary>
        public float DbgArm, DbgCounter, DbgHead;

        static float Slow(float mean, float value, float dt, float tau) { return mean + (value - mean) * (1f - Mathf.Exp(-dt / tau)); }
        static Quaternion Yaw(float deg) { return Quaternion.AngleAxis(deg, Vector3.up); }

        /// <summary>Before the foot planter, after the animation: the running body's secondary motion. <paramref name="owned"/> = the matcher plays the legs and nothing else owns the pose.</summary>
        public void Secondary(float dt, float speed, Vector3 travel, bool owned, bool carrying, bool qb)
        {
            DbgArm = DbgCounter = DbgHead = 0f;
            if (!Ok || !Enabled || NoSecondary || dt <= 1e-5f) { _gate = 0f; return; }
            float gt = owned ? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.2f, 3.6f, speed)) : 0f;
            _gate = LeanMath.Smooth(_gate, gt, dt, 0.12f);
            if (_gate < 0.02f) { _armHave = _cHave = _hHave = false; return; }
            travel.y = 0f; if (travel.sqrMagnitude < 1e-4f) return; travel.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, travel);
            // ---- arms: the swing about its own slow mean is scaled by his style and by the speed (the taken arms are relaxed capture arms)
            if (!carrying && !qb && _lua != null && _rua != null && _lla != null && _rla != null)
            {
                float f = Mathf.Lerp(1f, Style.ArmSwing * BodyMotion.ArmSwingFactor(speed), _gate);
                for (int side = 0; side < 2; side++)
                {
                    var up = side == 0 ? _lua : _rua; var lo = side == 0 ? _lla : _rla;
                    Vector3 v = lo.position - up.position;
                    float a = Mathf.Atan2(Vector3.Dot(v, travel), -v.y) * Mathf.Rad2Deg;
                    if (!_armHave) { if (side == 0) _armMeanL = a; else _armMeanR = a; }
                    float mean = side == 0 ? _armMeanL : _armMeanR;
                    mean = Slow(mean, a, dt, 0.35f); if (side == 0) _armMeanL = mean; else _armMeanR = mean;
                    float delta = Mathf.Clamp((f - 1f) * (a - mean), -25f, 25f);
                    if (Mathf.Abs(delta) > 0.05f) { up.rotation = Quaternion.AngleAxis(-delta, right) * up.rotation; DbgArm = Mathf.Max(DbgArm, Mathf.Abs(delta)); }
                }
                _armHave = true;
            }
            // ---- shoulders against the pelvis: the swing of the chest about its slow mean is made wider (the same twist for the spine and the chest)
            if (_spine != null && _chest != null)
            {
                float c = ChestOverPelvis;
                if (!_cHave) { _cMean = c; _cHave = true; } else _cMean = Slow(_cMean, c, dt, 0.45f);
                float extra = Mathf.Clamp((Style.Counter - 1f) * (c - _cMean) * _gate, -9f, 9f);
                if (Mathf.Abs(extra) > 0.05f) { _spine.rotation = Yaw(extra * 0.5f) * _spine.rotation; _chest.rotation = Yaw(extra * 0.5f) * _chest.rotation; DbgCounter = extra; }
                // ---- posture: his own forward trunk (more over his feet or more upright), the head stays level
                float post = Style.Posture * _gate;
                if (Mathf.Abs(post) > 0.05f)
                {
                    _spine.rotation = Quaternion.AngleAxis(post * 0.5f, right) * _spine.rotation; _chest.rotation = Quaternion.AngleAxis(post * 0.5f, right) * _chest.rotation;
                    _head.rotation = Quaternion.AngleAxis(-post * 0.6f, right) * _head.rotation;
                }
            }
            // ---- head stabilization: the head cancels a share of the trunk's yaw wobble (it looks where he runs, it does not nod with every step)
            {
                float hy = HeadYaw;
                if (!_hHave) { _hMean = hy; _hHave = true; } else _hMean = Mathf.LerpAngle(_hMean, hy, 1f - Mathf.Exp(-dt / 0.45f));
                float cancel = Mathf.Clamp(-Style.HeadStab * _gate * Mathf.DeltaAngle(_hMean, hy), -14f, 14f);
                if (Mathf.Abs(cancel) > 0.05f)
                {
                    if (_neck != null && _neck != _head) _neck.rotation = Yaw(cancel * 0.4f) * _neck.rotation;
                    _head.rotation = Yaw(cancel * (_neck != null && _neck != _head ? 0.6f : 1f)) * _head.rotation;
                    DbgHead = cancel;
                }
            }
        }

        // ------------------------------------------------------------------------------------------ standing: breath, weight shift, glances
        float _gYaw, _gPitch, _gTYaw, _gTPitch, _gTime; int _gN;
        /// <summary>QA: idle layer output (degrees): breath pitch, sway roll, glance yaw.</summary>
        public float DbgBreath, DbgSway, DbgGlance;

        /// <summary>A body that stands (not a stance set: those belong to the stance layer): it breathes, shifts its weight and glances. All of it above the pelvis, so the feet stay where the stance planted them.</summary>
        public void Idle(float dt, bool standing, Vector3 facing, float glanceScale = 1f)
        {
            DbgBreath = DbgSway = DbgGlance = 0f;
            if (!Ok || !Enabled || NoIdle || dt <= 1e-5f) { _idleW = 0f; return; }
            _idleW = LeanMath.Smooth(_idleW, standing ? 1f : 0f, dt, 0.4f);
            _t += dt;
            if (_idleW < 0.01f) { _gYaw = _gPitch = 0f; _gTYaw = _gTPitch = 0f; _gTime = 0.5f + Style.Phase0; return; }
            facing.y = 0f; if (facing.sqrMagnitude < 1e-4f) return; facing.Normalize();
            Vector3 right = Vector3.Cross(Vector3.up, facing);
            float w = _idleW;
            int seed = (int)(Style.Phase0 * 100000f);
            // breath: the chest rises and falls, the shoulders follow, the head hardly moves
            float breath = Mathf.Sin(2f * Mathf.PI * (Style.BreathHz * _t + Style.Phase0)) * Style.BreathAmp * 0.5f * w;
            // weight shift: a slow roll of the trunk over one hip and back (two incommensurate periods so it never repeats exactly)
            float sway = (0.72f * Mathf.Sin(2f * Mathf.PI * (_t / Style.ShiftPeriod + Style.ShiftPhase)) + 0.28f * Mathf.Sin(2f * Mathf.PI * (_t / (Style.ShiftPeriod * 0.37f) + Style.ShiftPhase * 3f))) * Style.ShiftAmp * w;
            // glances: look somewhere (up to 34 degrees), hold it, come back
            _gTime -= dt;
            if (_gTime <= 0f)
            {
                _gN++;
                bool back = (_gN & 1) == 0;
                _gTYaw = back ? 0f : (NaturalStyle.U(seed, _gN * 3) - 0.5f) * 68f;
                _gTPitch = back ? 0f : (NaturalStyle.U(seed, _gN * 3 + 1) - 0.4f) * 12f;
                _gTime = back ? Style.GlanceGap * (0.6f + NaturalStyle.U(seed, _gN * 3 + 2)) : 0.9f + NaturalStyle.U(seed, _gN * 3 + 2) * 2.2f;
            }
            _gYaw = Mathf.MoveTowards(_gYaw, Slow(_gYaw, _gTYaw, dt, 0.16f), 330f * dt); _gPitch = Mathf.MoveTowards(_gPitch, Slow(_gPitch, _gTPitch, dt, 0.16f), 120f * dt);
            float gy = _gYaw * w * glanceScale, gp = _gPitch * w * glanceScale;
            if (_spine != null && _chest != null)
            {
                Quaternion q = Quaternion.AngleAxis(breath, right) * Quaternion.AngleAxis(-sway, facing);      // + sway leans the trunk to the right
                _spine.rotation = Quaternion.Slerp(Quaternion.identity, q, 0.4f) * _spine.rotation;
                _chest.rotation = Quaternion.Slerp(Quaternion.identity, q, 0.6f) * _chest.rotation;
                _head.rotation = Quaternion.Inverse(Quaternion.Slerp(Quaternion.identity, q, 0.85f)) * _head.rotation;      // the eyes stay level while the trunk breathes and sways
                _chest.rotation = Yaw(gy * 0.25f) * _chest.rotation;
            }
            if (Mathf.Abs(gy) > 0.05f || Mathf.Abs(gp) > 0.05f)
            {
                float rest = gy * 0.75f;
                if (_neck != null && _neck != _head) { _neck.rotation = Yaw(rest * 0.4f) * _neck.rotation; _head.rotation = Yaw(rest * 0.6f) * _head.rotation; }
                else _head.rotation = Yaw(rest) * _head.rotation;
                _head.rotation = Quaternion.AngleAxis(gp, right) * _head.rotation;
            }
            DbgBreath = breath; DbgSway = sway; DbgGlance = gy;
        }

        // ------------------------------------------------------------------------------------------ the look: limits and rates
        float _pc, _pn; bool _lookHave;
        /// <summary>QA: the corrections of this frame (degrees): chest over pelvis, head over chest.</summary>
        public float DbgLookChest, DbgLookNeck; public bool DbgLookActive, DbgChestOn;

        /// <summary>On the final pose of a free body: the chest stays within <see cref="NaturalLook.ChestMax"/> of the pelvis and the head within <see cref="NaturalLook.NeckMax"/> of the chest, and neither turns faster than a person can.
        /// The chest is corrected first (the head and arms ride with it), the head then keeps the gaze it had (the neck takes what the chest gave back) as far as its own limit allows.</summary>
        public void LookLimit(float dt, bool chestOn, bool neckOn)
        {
            DbgLookChest = DbgLookNeck = 0f; DbgLookActive = (chestOn || neckOn) && Ok && Enabled && !NoLook; DbgChestOn = chestOn && DbgLookActive;
            if (!Ok || !Enabled || NoLook || !(chestOn || neckOn) || dt <= 1e-5f) { _lookHave = false; return; }
            float chest = ChestOverPelvis, neck = HeadOverChest;
            if (!_lookHave) { _pc = chest; _pn = neck; _lookHave = true; }
            // the chest over the pelvis (not for a body whose chest twist is the move: blocking, a stance, a throw)
            float dC = 0f;
            if (chestOn)
            {
                float tc = NaturalLook.SoftLimit(chest, NaturalLook.ChestMax);
                float oc = NaturalLook.Follow(_pc, tc, NaturalLook.ChestRate, dt); if (Mathf.Abs(tc - _pc) <= NaturalLook.ChestRate * dt) oc = tc;
                dC = oc - chest;
                if (Mathf.Abs(dC) > 0.02f) { _chest.rotation = Yaw(dC) * _chest.rotation; DbgLookChest = dC; }
                _pc = oc;
            }
            else _pc = chest;
            // the head over the chest: the gaze it had, inside the neck's range and rate
            float want = neck - dC;
            float tn = NaturalLook.SoftLimit(want, HeadMax);
            float on = NaturalLook.Follow(_pn, tn, HeadRate, dt); if (Mathf.Abs(tn - _pn) <= HeadRate * dt) on = tn;
            float dN = on - neck;
            if (Mathf.Abs(dN) > 0.02f)
            {
                // v0.13.0: about the neck axis (about the vertical only when the v0.12 behaviour is asked for: for a bent-over body that axis lies in the turf)
                Vector3 ax = StanceRules.V012 ? Vector3.up : NeckAxisWorld;
                if (_neck != null && _neck != _head) { _neck.rotation = Quaternion.AngleAxis(dN * 0.4f, ax) * _neck.rotation; _head.rotation = Quaternion.AngleAxis(dN * 0.6f, ax) * _head.rotation; }
                else _head.rotation = Quaternion.AngleAxis(dN, ax) * _head.rotation;
                DbgLookNeck = dN;
            }
            _pn = on;
        }
    }
}
