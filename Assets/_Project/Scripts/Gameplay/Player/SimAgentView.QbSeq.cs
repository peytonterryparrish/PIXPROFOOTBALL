using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.10.0 QB (playtest note 4): the choreographed quarterback sequence on the pose layer. The stage decides the pre-snap segments (walk, survey, step in, crouch) and moves him; this file poses him: the segment poses of
    /// <see cref="StancePoses"/> (QbSurvey, QbCrouch, QbSecure, QbPocketLive ...) are handed to <see cref="LateStancePose"/> through <see cref="QbSeqPose"/> / <see cref="QbSeqDyn"/> (one-line hooks), with the footfall planner seeded from the animated feet so there is never a snap from the walk.
    /// A segment runs for the window the simulation planned (<see cref="QbSeqTiming"/>) and the next one starts only when it is over (commitment); the simulation holds the body for the same windows, so he never glides while a segment plays.
    /// </summary>
    public partial class SimAgentView
    {
        /// <summary>The quarterback's pre-snap segments the stage drives (the stage moves him, the view poses him).</summary>
        public enum QbPre { None, Walk, Survey, Crouch }

        QbPre _qbPre; float _qbPreT, _qbPreLen; Vector3 _qbPreLook; bool _qbPreShotgun; bool _qbSeqPlay;
        float _qbPointW, _qbReadYaw; bool _footLenSet, _qbUcNow, _qbEmpty;
        /// <summary>The pocket-to-scramble transition poses the legs on the pose layer (true) or leaves the legs to the motion matcher while the sim holds his speed down (false, default: the pose layer's 0.22 s fade pinned a planted foot while he accelerated, a metre of slide).</summary>
        public static bool ScrambleStartPose = false;
        // post-snap tracking
        bool _qbMeshOn, _qbCarryOut, _seqHadMesh; float _qbMeshEta = -1f, _qbCarryU;
        bool _qbSecure, _qbScrStart, _seqFromSnap, _seqFromPocket; float _qbScrU; float _qbSecureU, _seqEnter; QbPhase _seqPrevQb = QbPhase.PreSnap;
        Vector3 _seqLookDir = Vector3.forward; bool _seqLookInit;

        /// <summary>Stage input: the pre-snap segment, its time (Crouch: 0..1), the point he surveys, whether the formation is shotgun and the survey length (-1 = a new play begins: reset).</summary>
        public void SetQbPre(QbPre seg, float t, Vector3 look, bool shotgun, float len)
        {
            if (seg != QbPre.None) _qbSeqPlay = true;
            else if (len < 0f) { _qbSeqPlay = false; _seqHadMesh = false; _qbMeshOn = _qbCarryOut = false; _seqPrevQb = QbPhase.PreSnap; _seqFromSnap = false; _seqLookInit = false; }
            _qbPre = seg; _qbPreT = t; _qbPreLook = look; _qbPreShotgun = shotgun; _qbPreLen = len;
        }

        /// <summary>QA: seconds to the simulated exchange as the pose layer sees it (-1 = none).</summary>
        public float QbMeshEtaNow { get { return _qbMeshEta; } }

        /// <summary>QA: name of the quarterback sequence segment on screen.</summary>
        public string QbSeqName
        {
            get
            {
                if (_qmOwn) return "Mocap";      // v0.13.0 QB-MOCAP
                if (_qbPre != QbPre.None) return _qbPre.ToString();
                if (_qtOn) return "Throw";
                if (_qbSecure) return "Secure";
                if (_qbScrStart) return "ScrambleStart";
                if (_qbMeshOn) return "Mesh";
                if (_qbCarryOut) return "CarryOut";
                if (_qbPoseOn && _wasFake) return "Fake";
                if (_qbPoseOn) return _qbPoseKind == StanceKind.QbPocket ? "Pocket" : "Drop";
                return _qbMode.ToString();
            }
        }

        /// <summary>Tracks the simulated quarterback state changes (when a segment began).</summary>
        void QbSeqTrack(in AnimHints h)
        {
            if (h.Qb == _seqPrevQb) return;
            _seqEnter = _clock;
            if (h.Qb == QbPhase.Dropback || h.Qb == QbPhase.Mesh) _seqFromSnap = _seqPrevQb == QbPhase.WaitSnap || _seqPrevQb == QbPhase.PreSnap;
            if (h.Qb == QbPhase.Mesh) _seqHadMesh = true;
            if (h.Qb == QbPhase.Scramble) _seqFromPocket = _seqPrevQb == QbPhase.Set || _seqPrevQb == QbPhase.Dropback || _seqPrevQb == QbPhase.Windup;
            _seqPrevQb = h.Qb;
        }

        /// <summary>The pose layer asks (before the other pose sources): is a sequence pose driving the quarterback now? Fills the pose and its smoothing time; <paramref name="gait"/> = the feet follow the footfall planner.</summary>
        bool QbSeqPose(ref StancePose target, ref float tau, ref bool gait)
        {
            _qbPointW = 0f;
            if (QbThrowPose(ref target, ref tau, ref gait)) return true;      // v0.11.0 QB: the throw
            if (_qbPre == QbPre.Survey)
            {
                float pw; target = StancePoses.QbSurvey(_qbPreT, _qbPreLen, out pw); _qbPointW = pw;
                if (_lefty) target = target.Mirrored();
                SeedGait(); tau = 0.10f; gait = true; return target.Valid;
            }
            if (_qbPre == QbPre.Crouch)
            {
                target = StancePoses.QbCrouch(_qbPreT, _qbPreShotgun);
                if (_lefty) target = target.Mirrored();
                SeedGait(); tau = 0.06f; gait = true; return target.Valid;
            }
            if (_qbSecure)
            {
                target = StancePoses.QbSecure(_qbSecureU);
                if (_lefty) target = target.Mirrored();
                tau = 0.04f; gait = true; return target.Valid;
            }
            if (_qbScrStart)
            {
                target = StancePoses.QbScrambleStart(_qbScrU); if (_lefty) target = target.Mirrored();
                tau = 0.05f; gait = true; return target.Valid;
            }
            if (_qbMeshOn)
            {
                target = StancePoses.QbSecure(1f); target.Lean = 24f; if (_lefty) target = target.Mirrored();
                tau = 0.08f; gait = true; return target.Valid;
            }
            if (_qbCarryOut)
            {
                target = StancePoses.QbCarryOut(_qbCarryU); if (_lefty) target = target.Mirrored();
                tau = 0.07f; gait = true; return target.Valid;
            }
            if (_qbSeqPlay && _qbPre == QbPre.None && !_qbPoseOn && (_lastAction == AgentAction.Stance || _qbStanceHold) && !Huddle)
            {
                // set at the line after the sequence: the stance pose, with the feet where the crouch planted them (no step into the stance rest spots)
                target = StancePoses.For(Stance, Role); if (Stance.Kind == StanceKind.QbUnderCenter) target = StancePoses.QbUcDeep(target); tau = 0.06f; gait = true; return target.Valid;
            }
            if (_qbPoseOn && _qbPoseKind == StanceKind.QbDrop && !_wasFake)
            {
                // the drop: a lower, athletic carriage than the pocket (the legs can only reach so far from under the hips: long drop strides need bent knees)
                target = StancePoses.For(new StanceSpec { Kind = StanceKind.QbDrop }, Role); target.Hips = 0.83f; target.Lean = 18f; if (_lefty) target = target.Mirrored();
                tau = 0.09f; gait = true; return target.Valid;
            }
            if (_qbPoseOn && _qbPoseKind == StanceKind.QbPocket && !_wasFake)
            {
                target = StancePoses.QbPocketLive(_clock, _simVel.magnitude); if (_qbEmpty) { target.RPos = new Vector3(0.20f, -0.30f, 0.12f); target.LPos = new Vector3(-0.20f, -0.30f, 0.12f); } if (_lefty) target = target.Mirrored();
                tau = 0.09f; gait = true; return target.Valid;
            }
            return false;
        }

        /// <summary>Per-frame inputs of the sequence (the head on the surveyed spot / the read, the hands reaching for the centre, the hands leaving the centre).</summary>
        void QbSeqDyn(ref StanceDyn d, bool have)
        {
            // the hand-over to the locomotion: while the pose layer fades out (0.22 s) the legs stay where the animation puts the feet (a planted planner foot pinned to the turf while the body runs off was a metre of slide)
            if (!have && Role == PlayerRole.QB && (Stance.Kind == StanceKind.QbUnderCenter || Stance.Kind == StanceKind.QbShotgun) && StanceLayer != null && StanceLayer.Weight > 0.001f && Avatar != null && Avatar.RightFoot != null && Avatar.LeftFoot != null && Avatar.Animator != null)
            {
                var rtB = Avatar.Animator.GetBoneTransform(HumanBodyBones.RightToes); var ltB = Avatar.Animator.GetBoneTransform(HumanBodyBones.LeftToes);
                d.GaitOn = true; d.RFoot = Avatar.RightFoot.position; d.LFoot = Avatar.LeftFoot.position; d.RLift = d.LLift = 0f;
                Vector3 tr = rtB != null ? rtB.position - Avatar.RightFoot.position : Forward, tl = ltB != null ? ltB.position - Avatar.LeftFoot.position : Forward; tr.y = 0f; tl.y = 0f;
                d.RFootYaw = tr.sqrMagnitude > 1e-4f ? Mathf.Atan2(tr.x, tr.z) * Mathf.Rad2Deg : _yaw; d.LFootYaw = tl.sqrMagnitude > 1e-4f ? Mathf.Atan2(tl.x, tl.z) * Mathf.Rad2Deg : _yaw;
                return;
            }
            bool havePocket = d.ReachW > 0f && d.ReachW < 0.01f; Vector3 pocket = d.Reach;      // the stage hands the runner's pocket point over with a marker weight
            if (havePocket) d.ReachW = 0f;
            if (_qbPre == QbPre.Survey)
            {
                d.Look = _qbPreLook; d.LookW = _qbPointW * 0.95f;
            }
            else if (_qbPre == QbPre.Crouch && !_qbPreShotgun && d.HasAnchor)
            {
                d.Reach = new Vector3(d.Anchor.x, transform.position.y + 0.80f, d.Anchor.z); d.ReachW = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((_qbPreT - 0.30f) / 0.65f));
            }
            else if (_qbSecure && d.HasAnchor && !_qbPreShotgun)
            {
                // the hands leave the centre: they start on the ball and bring it in to the belly
                d.Reach = new Vector3(d.Anchor.x, transform.position.y + 0.80f, d.Anchor.z); d.ReachW = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_qbSecureU * 1.25f));
            }
            // the hand-off: both hands into the runner's pocket for the 0.30 s before the exchange tick, released after it; the play fake carries the ball out toward the runner and back
            Vector3 headPos = Avatar != null && Avatar.Head != null ? Avatar.Head.position : transform.position + Vector3.up * 1.6f;
            if (_qbMeshOn && havePocket && _qbMeshEta >= 0f && _qbMeshEta <= QbSeqTiming.MeshExtendSeconds)
            { d.Reach = pocket; d.ReachW = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((QbSeqTiming.MeshExtendSeconds - _qbMeshEta) / (QbSeqTiming.MeshExtendSeconds - 0.12f))) * 0.95f; }
            else if (_qbCarryOut && havePocket)
            { d.Reach = pocket; d.ReachW = 0.95f * (1f - Mathf.SmoothStep(0f, 1f, _qbCarryU * QbSeqTiming.MeshFollowSeconds / 0.30f)); }
            else if (_qbPoseOn && _wasFake && havePocket)
            { float uf = Mathf.Clamp01((_clock - _fakeSince) / QbSeqTiming.FakeSeconds); d.Reach = pocket; d.ReachW = QbSeqTiming.FakeReach * Mathf.Sin(Mathf.PI * uf); if (uf > QbSeqTiming.FakeEyesBack) { d.Look = headPos + _qbDown * 10f; d.LookW = 0.95f; } }
            if (_qbMeshOn && d.ReachW > 0.01f) d.UpperYaw *= 1f - d.ReachW;      // the shoulders square to the runner for the reach (they were turned back toward the field)
            if (_qbCarryOut) { d.Look = headPos + _qbDown * 10f; d.LookW = 0.95f; }
            // the pocket's live feet: an alternating heel raise on the balls of the feet (the toes stay put, so nothing slides), over a small knee bounce
            if (_qbPoseOn && _qbPoseKind == StanceKind.QbPocket && !_wasFake && !_qbMeshOn && !_qbCarryOut && d.GaitOn && _simVel.magnitude < 0.35f && StanceLayer != null && _gait.Valid && _gait.RPlanted && _gait.LPlanted)
            {
                float sb = Mathf.Sin(_clock * 2f * Mathf.PI * 1.9f), Sc = StanceLayer.Scale;
                float rh = 0.012f * Sc * Mathf.Max(0f, sb), lh = 0.012f * Sc * Mathf.Max(0f, -sb);
                d.RFoot.y += rh; d.LFoot.y += lh; d.RLift += 1.75f * rh / 0.8f; d.LLift += 1.75f * lh / 0.8f;      // the foot pitches up with the heel (the toes stay on the turf)
            }
            // the head scans at a human pace: the look point turns toward the read at a limited rate (no snap from one receiver to the next)
            if (_qbPoseOn && d.LookW > 0.01f && Avatar != null && Avatar.Head != null)
            {
                Vector3 hp = Avatar.Head.position; Vector3 to = d.Look - hp;
                if (to.sqrMagnitude > 0.04f)
                {
                    to.Normalize();
                    if (!_seqLookInit) { _seqLookDir = Forward; _seqLookInit = true; }
                    _seqLookDir = Vector3.RotateTowards(_seqLookDir, to, 520f * Mathf.Deg2Rad * Mathf.Max(Time.deltaTime, 0.0001f), 0f);
                    d.Look = hp + _seqLookDir * 10f;
                }
            }
            // v0.11.0 QB (note 8): the shoulders turn toward the read (a little more than half the way, 30 degrees at most) while the hips and the feet stay on the field: the stance scans with the chest, not only the eyes
            if (!QbV11.Legacy && have && _qbPoseOn && (_qbPoseKind == StanceKind.QbPocket || (_qbPoseKind == StanceKind.QbDrop && !_qbUcNow)) && !_wasFake && !_qbMeshOn && !_qbCarryOut && _qbHasLook)
            {
                Vector3 toR = _qbLook - transform.position; toR.y = 0f;
                float rel = toR.sqrMagnitude > 0.25f ? Vector3.SignedAngle(Forward, toR, Vector3.up) : 0f;
                _qbReadYaw = Mathf.MoveTowards(_qbReadYaw, Mathf.Clamp(rel * 0.65f, -32f, 32f), 220f * Mathf.Max(Time.deltaTime, 0.0001f));
                d.UpperYaw += _qbReadYaw;
            }
            else _qbReadYaw = Mathf.MoveTowards(_qbReadYaw, 0f, 300f * Mathf.Max(Time.deltaTime, 0.0001f));
            QbThrowDyn(ref d);      // v0.11.0 QB: the scripted feet and the eyes of the throw
        }

        /// <summary>The quarterback's set-feet throw keeps the footfall planner (the front foot's stride is a planned step, not a snap to the pose).</summary>
        bool QbThrowGait { get { return Role == PlayerRole.QB; } }

        /// <summary>QA: the passer throws with his left hand.</summary>
        public bool IsLefty { get { return _lefty; } }

        /// <summary>0..1: how far the quarterback's drawn position has stepped toward the runner for the hand-off (see <see cref="PlayStage"/>).</summary>
        public float QbMeshPullWeight
        {
            get
            {
                if (_qmMeshOn && _qbMeshEta >= 0f && _qbMeshEta <= QbSeqTiming.MeshExtendSeconds) return Mathf.SmoothStep(0f, 1f, 1f - _qbMeshEta / QbSeqTiming.MeshExtendSeconds);      // v0.13.0 QB-2: the mocap hand-off take steps him to the runner the same way
                if (_qbMeshOn && _qbMeshEta >= 0f && _qbMeshEta <= QbSeqTiming.MeshExtendSeconds) return Mathf.SmoothStep(0f, 1f, 1f - _qbMeshEta / QbSeqTiming.MeshExtendSeconds);
                if (_qbCarryOut) return 1f - Mathf.SmoothStep(0f, 1f, _qbCarryU * QbSeqTiming.MeshFollowSeconds / 0.25f);
                return 0f;
            }
        }

        void SeedGait()
        {
            if (!_footLenSet && Avatar != null && Avatar.RightFoot != null && Avatar.Animator != null) { var rt = Avatar.Animator.GetBoneTransform(HumanBodyBones.RightToes); if (rt != null) { { float L = Vector3.Distance(Avatar.RightFoot.position, rt.position), dy = Mathf.Max(0.02f, StanceLayer.RestAnkle - 0.013f); _gait.BallOfFoot = Mathf.Sqrt(Mathf.Max(0.0025f, L * L - dy * dy)); _footLenSet = true; } } }
            if (_gait.Valid || Avatar == null || Avatar.RightFoot == null || Avatar.LeftFoot == null) return;
            _gait.Seed(Avatar.RightFoot.position, Avatar.LeftFoot.position);
        }
    }
}
