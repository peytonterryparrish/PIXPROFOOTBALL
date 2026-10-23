using PixFootball.Art;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 CONNECT: the connections between animations on the view side.
    ///  - Pop filter (E-250): pelvis and chest jumps of the animated pose (a clip change without a blend, a pair alignment, a yaw offset) are absorbed by an inertializing offset that decays in about a tenth of a second.
    ///  - View offset (E-231): a player who lay somewhere other than where the simulation ran on to catches up at a walking pace instead of gliding or teleporting.
    ///  - Stance hold at the snap (E-248), get-off by the first move (E-251), dwell time (E-259), trigger reset (E-158), catch reach IK (E-125/127), carry tuck IK (E-146).
    /// </summary>
    public partial class SimAgentView
    {
        /// <summary>QA: switch the connection layer off (the "before" state of the audit).</summary>
        public static bool DebugNoConnect;
        public static bool DebugNoPopFilter = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoPopFilter") >= 0;

        // ------------------------------------------------------------------------------------------ view offset
        Vector3 _viewOff;
        /// <summary>Metres per second at which the view offset closes.</summary>
        public float CatchUpSpeed = 3.2f;
        public Vector3 ViewOffset { get { return _viewOff; } }
        public void AddViewOffset(Vector3 o) { o.y = 0f; _viewOff += o; if (_viewOff.magnitude > 6f) _viewOff = _viewOff.normalized * 6f; }
        public void ClearViewOffset() { _viewOff = Vector3.zero; }

        Vector3 ApplyViewOffset(Vector3 p, float dt)
        {
            if (_viewOff.sqrMagnitude < 1e-6f) return p;
            _viewOff = Vector3.MoveTowards(_viewOff, Vector3.zero, CatchUpSpeed * Mathf.Max(dt, 0.0001f));
            return p + _viewOff;
        }

        // ------------------------------------------------------------------------------------------ huddle / break to the line
        /// <summary>The player stands in the huddle: the match animation logic stays off him while the stage walks him to the line (E-252).</summary>
        public bool Huddle { get; private set; }
        public void SetHuddle(bool on, int seed)
        {
            if (on == Huddle) return;
            Huddle = on;
            if (Rig == null || !Rig.Valid) return;
            if (on)
            {
                var plan = AnimKit.HuddlePlan(seed);
                if (plan.Valid) { Rig.Play(plan); _ownsClip = true; }
            }
            else if (_ownsClip) { Rig.Stop(0.3f); _ownsClip = false; _lastAction = (AgentAction)(-1); }
        }

        // ------------------------------------------------------------------------------------------ action filter
        float _liveSince = -1f, _lastChangeAt = -9f; float _lastLocoYaw;
        AnimationClip _engClip; float _engHold; AgentAction _engAction;
        PlayerMove _trigMove; float _trigClearAt;
        Vector3 _fallPos; bool _fell;
        public const float DwellSeconds = 0.40f;

        static bool DwellGroup(AgentAction a)
        {
            return a == AgentAction.Block || a == AgentAction.Engaged || a == AgentAction.Shed || a == AgentAction.Idle || a == AgentAction.Stumble
                   || a == AgentAction.Run || a == AgentAction.Sprint || a == AgentAction.Route || a == AgentAction.Backpedal || a == AgentAction.Shuffle;
        }

        /// <summary>
        /// Filters the simulated action before the view reacts to it: the Stance to Idle the simulation passes through for 0.1 to 0.2 s at the snap keeps the stance (E-248); Block, Engaged, Shed and Idle flips
        /// that follow each other inside 0.3 s do not restart clips (E-259).
        /// </summary>
        AgentAction ConnectAction(AgentAction action, in AnimHints h)
        {
            if (DebugNoConnect) return action;
            if (h.Live) { if (_liveSince < 0f) _liveSince = _clock; } else _liveSince = -1f;
            if (_lastAction == AgentAction.Stance && action == AgentAction.Idle && h.Kind == PixFootball.Sim.PlayKind.Scrimmage && h.Live && !h.Whistled && _liveSince >= 0f && _clock - _liveSince < 0.5f) return AgentAction.Stance;
            if (action != _lastAction && _clock - _lastChangeAt < DwellSeconds && DwellGroup(action) && DwellGroup(_lastAction) && Priority(action) <= Priority(_lastAction)) return _lastAction;
            return action;
        }

        /// <summary>The first-move get-off plan: heading of the move against the facing, the first action, the block style.</summary>
        ClipPlan GetOffByFirst(AgentAction action, in AnimHints h, Vector3 velocity, Vector3 facing)
        {
            float heading = 0f;
            Vector3 vh = new Vector3(velocity.x, 0f, velocity.z);
            if (vh.magnitude > 0.3f) heading = Vector3.SignedAngle(new Vector3(facing.x, 0f, facing.z), vh, Vector3.up);
            if (Role == PlayerRole.DL && Stance.Kind == StanceKind.FourPoint && Mathf.Abs(heading) < 20f && action != AgentAction.Backpedal) { var fp = AnimKit.FourPointGetOff(h.Seed); if (fp.Valid) return ChoreoFitGetOff(fp, facing); }      // v0.10.0 ANIMLIB: the 4-point get-off mocap
            return ChoreoFitGetOff(AnimKit.GetOffFor(Role, h.Seed, action, heading, h.Block), facing);      // v0.10.0 CHOREO: committed only while the planned path agrees
        }

        /// <summary>Plays the hand-fight loop after an engagement take ran through (E-185), instead of holding the last pose.</summary>
        void ServiceEngageLoop(AgentAction action)
        {
            if (_engClip == null || Rig == null || !Rig.Valid) return;
            if (action != _engAction) { _engClip = null; return; }
            if (Rig.CurrentClip == _engClip && !Rig.InWindow)
            {
                var loop = AnimKit.EngageLoop(_engClip, _engHold);
                if (loop.Valid) { loop = ChoreoMask(_engAction, loop); Rig.Play(loop); _ownsClip = true; _engClip = null; }
            }
        }

        void ClearTrigger()
        {
            if (_trigMove != PlayerMove.None && _clock >= _trigClearAt) { if (Anim != null) Anim.ResetTrigger(_trigMove); _trigMove = PlayerMove.None; }
        }

        // ------------------------------------------------------------------------------------------ pop filter (inertialization of pelvis and chest)
        Transform _pfHips, _pfChest;
        Quaternion _pfQ, _pfQoff = Quaternion.identity, _pfCq, _pfCqoff = Quaternion.identity; Vector3 _pfP, _pfPoff; bool _pfHave;
        /// <summary>QA: the largest pelvis rotation / position jump (per frame) absorbed this play.</summary>
        public int PopsAbsorbed { get; private set; }

        void ResetPopFilter() { _pfHave = false; _pfQoff = Quaternion.identity; _pfCqoff = Quaternion.identity; _pfPoff = Vector3.zero; }

        void PopFilterLate(float dt)
        {
            if (DebugNoPopFilter || DebugNoConnect || Avatar == null || Avatar.Animator == null || !Avatar.Animator.isHuman || dt <= 1e-5f) return;
            if (Ragdoll != null && Ragdoll.Active) { _pfHave = false; return; }
            if (_pfHips == null) { _pfHips = Avatar.Animator.GetBoneTransform(HumanBodyBones.Hips); _pfChest = Avatar.Animator.GetBoneTransform(HumanBodyBones.Chest) ?? Avatar.Animator.GetBoneTransform(HumanBodyBones.Spine); }
            if (_pfHips == null) return;
            Transform root = transform;
            Quaternion q = Quaternion.Inverse(root.rotation) * _pfHips.rotation;
            Vector3 p = root.InverseTransformPoint(_pfHips.position);
            Quaternion cq = _pfChest != null ? Quaternion.Inverse(root.rotation) * _pfChest.rotation : Quaternion.identity;
            if (!_pfHave) { _pfQ = q; _pfP = p; _pfCq = cq; _pfHave = true; return; }
            float k = 1f - Mathf.Exp(-dt / 0.09f);
            _pfQoff = Quaternion.Slerp(_pfQoff, Quaternion.identity, k); _pfCqoff = Quaternion.Slerp(_pfCqoff, Quaternion.identity, k); _pfPoff = Vector3.Lerp(_pfPoff, Vector3.zero, k);
            Quaternion qf = _pfQoff * q; Vector3 pf = p + _pfPoff; Quaternion cf = _pfCqoff * cq;
            // pelvis: more than 800 deg/s (a frame's worth, at least 13 degrees) or 9 cm in one frame is a pop; the pose follows at 480 deg/s / 3 m/s and the rest is carried as a decaying offset
            float thrRot = Mathf.Max(13f, 800f * dt), stepRot = Mathf.Max(7f, 480f * dt);
            bool changed = false;
            if (Quaternion.Angle(_pfQ, qf) > thrRot) { Quaternion tgt = Quaternion.RotateTowards(_pfQ, qf, stepRot); _pfQoff = tgt * Quaternion.Inverse(q); qf = tgt; PopsAbsorbed++; changed = true; }
            float dp = Vector3.Distance(_pfP, pf);
            if (dp > 0.09f) { Vector3 tp = Vector3.MoveTowards(_pfP, pf, Mathf.Max(0.04f, 3f * dt)); _pfPoff = tp - p; pf = tp; PopsAbsorbed++; changed = true; }
            // chest: 1400 deg/s
            float thrC = Mathf.Max(22f, 1300f * dt), stepC = Mathf.Max(10f, 700f * dt);
            if (_pfChest != null && Quaternion.Angle(_pfCq, cf) > thrC) { Quaternion tgt = Quaternion.RotateTowards(_pfCq, cf, stepC); _pfCqoff = tgt * Quaternion.Inverse(cq); cf = tgt; changed = true; }
            bool active = changed || Quaternion.Angle(_pfQoff, Quaternion.identity) > 0.4f || _pfPoff.sqrMagnitude > 1e-5f || (_pfChest != null && Quaternion.Angle(_pfCqoff, Quaternion.identity) > 0.4f);
            if (active)
            {
                _pfHips.position = root.TransformPoint(pf);
                _pfHips.rotation = root.rotation * qf;
                if (_pfChest != null && Quaternion.Angle(_pfCqoff, Quaternion.identity) > 0.01f)
                {
                    // the chest is a child of the pelvis: its world rotation has moved with the pelvis; put it where the filtered chest should be
                    _pfChest.rotation = root.rotation * cf;
                }
            }
            _pfQ = qf; _pfP = pf; _pfCq = cf;
        }

        // ------------------------------------------------------------------------------------------ reach IK to the real ball (the catch) and carry tuck
        float _reachW, _reachTarget; Vector3 _reachPos; bool _reachOne;
        float _tuckW, _tuckTarget; int _tuckSide = 1;
        Transform _rUA, _rLA, _rH, _lUA, _lLA, _lH; bool _armsFound;

        /// <summary>Hands reach for the ball at <paramref name="ballWorld"/> with weight <paramref name="weight"/> (0 releases). oneHand: the nearer hand only (a stretch catch).</summary>
        public void ReachBall(Vector3 ballWorld, float weight, bool oneHand) { _reachPos = ballWorld; _reachTarget = Mathf.Clamp01(weight); _reachOne = oneHand; }
        /// <summary>QA: current reach weight.</summary>
        public float ReachWeight { get { return _reachW; } }
        /// <summary>The arm that holds the ball tucks it against the ribs (E-146); weight 0 releases.</summary>
        public void SetTuck(float weight, int side) { _tuckTarget = Mathf.Clamp01(weight); _tuckSide = side >= 0 ? 1 : -1; }

        void FindArms()
        {
            if (_armsFound || Avatar == null || Avatar.Animator == null || !Avatar.Animator.isHuman) return;
            var a = Avatar.Animator;
            _rUA = a.GetBoneTransform(HumanBodyBones.RightUpperArm); _rLA = a.GetBoneTransform(HumanBodyBones.RightLowerArm); _rH = a.GetBoneTransform(HumanBodyBones.RightHand);
            _lUA = a.GetBoneTransform(HumanBodyBones.LeftUpperArm); _lLA = a.GetBoneTransform(HumanBodyBones.LeftLowerArm); _lH = a.GetBoneTransform(HumanBodyBones.LeftHand);
            _armsFound = _rUA != null && _rLA != null && _rH != null && _lUA != null && _lLA != null && _lH != null;
        }

        void ArmIkLate(float dt)
        {
            if (DebugNoConnect) return;
            _reachW = Mathf.MoveTowards(_reachW, _reachTarget, dt / (_reachTarget > _reachW ? 0.10f : 0.22f));
            _tuckW = Mathf.MoveTowards(_tuckW, _tuckTarget, dt / 0.18f);
            if (_reachW < 0.02f && _tuckW < 0.02f) return;
            if (Ragdoll != null && Ragdoll.Active) return;
            FindArms(); if (!_armsFound) return;
            Vector3 fwd = Forward, right = Vector3.Cross(Vector3.up, fwd);
            if (_tuckW > 0.02f && _reachW < 0.5f && !(ContactDriven && !_reaction))
            {
                var chest = Avatar.Chest;
                if (chest != null)
                {
                    bool rightArm = _tuckSide > 0;
                    var u = rightArm ? _rUA : _lUA; var lo = rightArm ? _rLA : _lLA; var hd = rightArm ? _rH : _lH;
                    float w = _tuckW * 0.6f * (1f - Mathf.Clamp01(_reachW * 2f)) * (1f - 0.7f * (Rig != null ? Mathf.Clamp01(Rig.Weight) : 0f));
                    Vector3 target = chest.position + Vector3.down * 0.12f + fwd * 0.17f + right * (_tuckSide * 0.10f);
                    Vector3 pole = right * _tuckSide * 0.7f + Vector3.down * 0.35f - fwd * 0.2f;
                    LimbIK.Apply(u, lo, hd, target, pole, w);
                }
            }
            if (_reachW > 0.02f)
            {
                Vector3 mid = (_rUA.position + _lUA.position) * 0.5f;
                float reach = LimbIK.Reach(_rUA.position, _rLA.position, _rH.position);
                Vector3 to = _reachPos - mid; float d = to.magnitude;
                Vector3 tgt = d > reach * 1.02f ? mid + to / d * reach * 1.02f : _reachPos;
                // the hands cup the ball: one each side of it across the receiver's body; a one-hand catch sends the nearer hand only
                Vector3 side = right * 0.075f;
                Vector3 poleR = right * 0.5f + Vector3.down * 0.9f - fwd * 0.15f, poleL = -right * 0.5f + Vector3.down * 0.9f - fwd * 0.15f;
                if (_reachOne)
                {
                    bool rightNear = Vector3.Dot(_reachPos - mid, right) >= 0f;
                    if (rightNear) LimbIK.Apply(_rUA, _rLA, _rH, tgt, poleR, _reachW); else LimbIK.Apply(_lUA, _lLA, _lH, tgt, poleL, _reachW);
                }
                else
                {
                    LimbIK.Apply(_rUA, _rLA, _rH, tgt + side, poleR, _reachW);
                    LimbIK.Apply(_lUA, _lLA, _lH, tgt - side, poleL, _reachW);
                }
            }
        }
    }
}
