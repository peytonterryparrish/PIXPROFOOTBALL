using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 QBSTANCE: the stance pose layer of one player (see <see cref="StanceIK"/>). The pose follows <see cref="Stance"/> while the player stands in it (the pre-snap stance and the quarterback's wait for the snap),
    /// the quarterback's pocket / drop poses while his flow says so (feet by the <see cref="ProcGait"/> footfall planner), and the keyed throwing pose around a throw. It fades out when a get-off clip / a locomotion take owns the body
    /// and is switched off for authored contact takes and ragdolls.
    /// </summary>
    public partial class SimAgentView
    {
        /// <summary>The stance pose layer (null until the avatar has a humanoid rig).</summary>
        public StanceIK StanceLayer;
        /// <summary>Inputs that change while he stands (cadence, snap reach, look); set by <see cref="PlayStage"/> each frame.</summary>
        public StanceDyn StanceInput;
        StancePose _pose; StanceSpec _poseSpec; PlayerRole _poseRole; bool _poseHave;
        bool _qbStanceHold, _qbPoseOn; StanceKind _qbPoseKind;
        readonly ProcGait _gait = new ProcGait();
        Vector3 _simVel, _qbDown = Vector3.forward, _qbLook; bool _qbHasLook, _lefty;
        float _upperYaw, _headLookW;
        // keyed throwing pose
        bool _throwToss;
        bool _throwPose, _throwOnRun; AnimationClip _throwClip; float _throwStart, _throwTr, _throwEnd;
        /// <summary>QA: switch the stance pose layer off (the "before" state keeps the mocap stances).</summary>
        public static bool DebugNoStancePose;

        /// <summary>True while the pose layer owns (or is fading out of) the body: the line setback and the foot-hover ground fit step aside.</summary>
        public bool StanceLayerOn { get { return StanceLayer != null && StanceLayer.Ok && !DebugNoStancePose && (_poseHave || StanceLayer.Weight > 0.01f); } }
        /// <summary>QA: the quarterback's pose layer is driving his drop / pocket right now.</summary>
        public bool QbPoseActive { get { return _qbPoseOn; } }
        /// <summary>QA: one line of the pose layer state.</summary>
        public string PoseDebug { get { return "pose:" + (_qbPoseOn ? _qbPoseKind.ToString() : (_poseHave ? Stance.Kind.ToString() : "-")) + " throw=" + _throwPose + " w=" + (StanceLayer != null ? StanceLayer.Weight.ToString("0.00") : "-") + " gait=" + _gait.Valid + " spd=" + _simVel.magnitude.ToString("0.0"); } }
        /// <summary>QA: a throw pose is running (the keyed pose of <see cref="StancePoses.QbThrow"/>).</summary>
        public bool ThrowPoseActive { get { return _throwPose; } }
        /// <summary>QA: the footfall planner of the pose layer (planted flags, step count).</summary>
        public ProcGait FootGait { get { return _gait; } }

        /// <summary>v0.10.0 BODIES: the joint-limit pass over the final pose (see <see cref="BodySanity"/>).</summary>
        public BodySanity Sanity;

        void InitStanceLayer()
        {
            if (Avatar == null || Avatar.Animator == null) return;
            StanceLayer = new StanceIK(Avatar.Animator, name.GetHashCode() & 0xffff);
            if (Sanity == null && Avatar.Animator.isHuman) { Sanity = Avatar.Animator.gameObject.AddComponent<BodySanity>(); Sanity.Init(Avatar.Animator); }
        }

        /// <summary>The solved pose of the current stance (QA / tests).</summary>
        public StancePose CurrentPose { get { return _pose; } }

        /// <summary>A throw take was chosen: key the throwing pose to its release moment (the hips and the stride open before the shoulders, the release is gated by the pose, not by a clip speed).</summary>
        void NoteThrowPose(in ClipPlan plan, in AnimHints h)
        {
            _throwPose = false; _throwToss = false;
            if (!h.IsQb || !plan.Valid || StanceLayer == null || !StanceLayer.Ok) return;
            ClipTimingTable.Entry e;
            float tr = ClipTimingTable.TryEntry(plan.Clip, out e) && e.t > 0.2f ? e.t : ClipTimingTable.Moment(plan.Clip, 0.45f);
            _throwClip = plan.Clip; _throwStart = plan.Start; _throwTr = Mathf.Max(tr, plan.Start + 0.12f); _throwEnd = plan.End > 0f ? plan.End : plan.Clip.length;
            if (h.Pitch) { _throwToss = true; _throwClip = plan.Clip; _throwStart = plan.Start; _throwTr = plan.Start + 0.20f; _throwEnd = plan.Start + 0.9f; _throwOnRun = false; _throwPose = true; return; }      // the pitch: a fixed 0.2 s underhand flip (the ball has already left)
            _throwOnRun = h.PrevScramble || h.Speed > 3.0f || h.Rolling;
            _throwToss = false;
            _throwPose = true;
        }

        /// <summary>Per-frame motion facts from the stage: the simulated velocity (m/s), the direction of the opponent goal, the receiver he looks at and whether he is left-handed.</summary>
        public void SetMotionInputs(Vector3 simVel, Vector3 downfield, Vector3 look, bool hasLook, bool lefty)
        {
            _simVel = simVel; _qbDown = downfield; _qbLook = look; _qbHasLook = hasLook; _lefty = lefty;
        }

        bool ThrowPoseU(out float u)
        {
            u = 0f;
            if (!_throwPose || Rig == null || !Rig.Valid) return false;
            if (Rig.CurrentClip != _throwClip) { _throwPose = false; return false; }
            float t = Rig.ClipTime;
            if (t > _throwEnd + 0.1f) { _throwPose = false; return false; }
            u = (t - _throwStart) / Mathf.Max(0.05f, _throwTr - _throwStart);
            return true;
        }

        static StancePose Smooth(in StancePose a, in StancePose b, float dt, float tau)
        {
            // numbers glide toward the new pose, the hand modes switch at once (a half-way mode switch of a per-frame smoothing would never happen)
            var r = StancePoses.Lerp(a, b, 1f - Mathf.Exp(-dt / Mathf.Max(0.01f, tau)));
            r.RHand = b.RHand; r.LHand = b.LHand;
            return r;
        }

        void LateStancePose(float dt)
        {
            LateStancePoseCore(dt);
            if (Sanity != null) { Sanity.ThrowPose = _throwPose; Sanity.Torso(); }      // v0.10.0 BODIES: spine twist and head limits before the arm IK reaches for the ball
        }

        void LateStancePoseCore(float dt)
        {
            if (StanceLayer == null || !StanceLayer.Ok) return;
            bool free = !DebugNoStancePose && !ContactDriven && !(Ragdoll != null && Ragdoll.Active) && Rig != null && Rig.Valid;
            StancePose target = default(StancePose); bool have = false, gait = false, throwing = false; float tau = 0.09f;
            float u;
            if (free && QbSeqPose(ref target, ref tau, ref gait)) { have = true; }      // v0.10.0 QB: the choreographed sequence poses (survey, crouch ...)
            else if (free && ThrowPoseU(out u)) { target = _throwToss ? StancePoses.QbToss(u) : StancePoses.QbThrow(u, _throwOnRun); if (_lefty) target = target.Mirrored(); have = true; gait = _throwOnRun || QbThrowGait; throwing = true; tau = 0.04f; }
            else if (free && _qbSpecial != 0) { target = _qbSpecial == 1 ? StancePoses.QbKneel(_specialU) : StancePoses.QbSpike(_specialU); have = true; tau = 0.05f; }
            else if (free && _qbPoseOn) { target = StancePoses.For(new StanceSpec { Kind = _qbPoseKind }, Role); if (_lefty) target = target.Mirrored(); have = target.Valid; gait = true; }
            else if (free && (_lastAction == AgentAction.Stance || _qbStanceHold))
            {
                if (_poseSpec.Kind != Stance.Kind || _poseSpec.Mirror != Stance.Mirror || _poseSpec.IsLeftOfBall != Stance.IsLeftOfBall || _poseSpec.Defense != Stance.Defense || _poseSpec.Ol != Stance.Ol
                    || !Mathf.Approximately(_poseSpec.Tell, Stance.Tell) || !Mathf.Approximately(_poseSpec.Tilt, Stance.Tilt) || _poseRole != Role || !_poseHaveSpec)
                { _poseSpec = Stance; _poseRole = Role; _poseHaveSpec = true; _stancePoseCache = StancePoses.For(Stance, Role); }
                target = _stancePoseCache; have = target.Valid;
            }
            if (have)
            {
                if (!_poseHave || !StanceLayer.Active) { _pose = target; _poseHave = true; }
                else _pose = Smooth(_pose, target, dt, tau);
            }
            else if (_poseHave && StanceLayer.Weight <= 0.001f) { _poseHave = false; _poseHaveSpec = false; }
            var dyn = StanceInput;
            if (_poseHave && StanceLayer.Weight > 0.001f && (gait || (have && _qbPoseOn)))
            {
                float S = StanceLayer.Scale;
                Vector3 root = transform.position;
                Vector3 restR = new Vector3(_pose.RX * S, 0f, _pose.RZ * S), restL = new Vector3(_pose.LX * S, 0f, _pose.LZ * S);
                _gait.Ankle = StanceLayer.RestAnkle;
                if (!_gait.Valid) _gait.Reset(root, _yaw, restR, restL, root.y);
                _gait.Update(dt, root, _simVel, _yaw, restR, restL, root.y);
                dyn.GaitOn = true; dyn.RFoot = _gait.R.Pos; dyn.LFoot = _gait.L.Pos; dyn.RFootYaw = _gait.R.Yaw; dyn.LFootYaw = _gait.L.Yaw;
                dyn.RLift = _gait.RLift(root.y); dyn.LLift = _gait.LLift(root.y);
            }
            else _gait.Cancel();
            // the quarterback's upper body: eyes and shoulders to the field while the hips face the way he travels, the head on the read
            if (_poseHave && (_qbPoseOn || throwing))
            {
                Vector3 fwd = Forward; Vector3 down = _qbDown.sqrMagnitude > 0.1f ? _qbDown : Vector3.forward;
                float ang = Vector3.SignedAngle(fwd, down, Vector3.up);
                float want = 0f;
                if (!throwing)
                {
                    if (Mathf.Abs(ang) > 110f) want = -55f * (_lefty ? -1f : 1f);          // facing away: look back over the shoulder
                    else want = Mathf.Clamp(ang, -35f, 35f);
                }
                _upperYaw = Mathf.MoveTowards(_upperYaw, want, 520f * dt);
                dyn.UpperYaw = _upperYaw;
                Vector3 head = transform.position + Vector3.up * 1.6f;
                Vector3 look = _qbHasLook ? _qbLook : head + down * 10f;
                dyn.Look = look; _headLookW = Mathf.MoveTowards(_headLookW, 0.95f, 3f * dt); dyn.LookW = _headLookW;
            }
            else { _upperYaw = Mathf.MoveTowards(_upperYaw, 0f, 520f * dt); _headLookW = 0f; }
            if (_qbPoseOn && _wasFake)
            {
                // the fake hand-off: both hands carry the ball out toward the runner and back (a 0.42 s reach), shoulders and head on him
                float uf = Mathf.Clamp01((_clock - _fakeSince) / 0.42f), sf = Mathf.Sin(Mathf.PI * uf);
                Vector3 toR = _qbHasLook ? _qbLook - transform.position : Forward; toR.y = 0f;
                float side = Vector3.Dot(Vector3.Cross(Vector3.up, Forward), toR.normalized) >= 0f ? 1f : -1f;
                dyn.ChestShift = new Vector3(side * 0.22f * sf, -0.10f * sf, 0.30f * sf);
            }
            QbSeqDyn(ref dyn, have);      // v0.10.0 QB
            StanceLayer.Late(dt, have, _pose, transform.position, _yaw, dyn);
        }

        int _qbSpecial; bool _kneeling; float _kneelSince, _specialU;
        bool _wasFake; float _fakeSince;
        bool _poseHaveSpec; StancePose _stancePoseCache;
    }
}

namespace PixFootball.Gameplay
{
    public partial class SimAgentView
    {
        /// <summary>The hand the quarterback throws with (the left for a left-hander) and the other one.</summary>
        public Transform ThrowHandBone { get { return Avatar == null ? null : (_lefty && Avatar.LeftHand != null ? Avatar.LeftHand : Avatar.RightHand); } }
        public Transform OffHandBone { get { return Avatar == null ? null : (_lefty ? Avatar.RightHand : Avatar.LeftHand); } }
    }
}
