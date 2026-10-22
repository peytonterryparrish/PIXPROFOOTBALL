using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Feeds the Animator controller (built by AnimatorControllerBuilder): a 2D freeform blend tree on local velocity (m/s) for
    /// locomotion and an override action layer driven by triggers. Playback speed is matched to ground speed so feet do not slide.
    /// Works either directly on an Animator (free roam) or on an AnimatorControllerPlayable inside an ActionAnimator graph (match).
    /// </summary>
    public class LocomotionAnimator : MonoBehaviour
    {
        public static readonly int VelX = Animator.StringToHash("VelX"), VelZ = Animator.StringToHash("VelZ"), SpeedP = Animator.StringToHash("Speed"),
            Celebrating = Animator.StringToHash("Celebrating"), ProtectP = Animator.StringToHash("Protect"), LocoSpeed = Animator.StringToHash("LocoSpeed"), CycleOffsetP = Animator.StringToHash("CycleOffset");

        static readonly int NoneHash = Animator.StringToHash("None");     // QA-727: no per-frame string hashing
        public Animator Animator;
        public ActionAnimator Rig;
        /// <summary>Natural ground speeds (m/s) of the clips in the blend tree along each axis (set from the baked controller data).</summary>
        public float JogSpeed = 3.2f, RunSpeed = 5.2f, SprintSpeed = 8.2f, BackSpeed = 2.2f, SideSpeed = 2.6f;

        /// <summary>Exponential follow rate (1/s) of the animated velocity.</summary>
        public float SmoothRate = 9f;
        /// <summary>v0.7.0 stride-matching trim of the playback rate (BodyMotion.LocoTrim), set by the view each frame.</summary>
        public float Trim = 1f;
        /// <summary>QA: global multiplier of the playback rate (the treadmill sweeps it to find the stride-matching table).</summary>
        public static float DebugTrim = 1f;

        /// <summary>v0.7.0 stride table (A-008), measured by the treadmill sweep (AnimQbShots): the playback-rate multiplier that keeps the ball of the planted foot standing still on the turf. Plain forward running needs about 20 percent more cadence between 4.5 and 5.5 m/s than the blend tree gives; the ball-carrier cycles and the walk / jog range are already matched.</summary>
        public static float StrideTrim(float speed, bool carrying, bool backward)
        {
            if (carrying || backward) return 1f;
            if (speed <= 3.5f) return 1f;
            if (speed < 4.5f) return Mathf.Lerp(1f, 1.2f, speed - 3.5f);
            if (speed <= 5.6f) return 1.2f;
            if (speed < 6.6f) return Mathf.Lerp(1.2f, 1f, speed - 5.6f);
            return 1f;
        }
        float _ratio = 1f;
        static int _phaseCounter; readonly int _phaseId = ++_phaseCounter;
        Vector2 _smoothed;
        bool _hasController;
        bool _carrying;
        int _actionLayer = -1;
        public bool InAction { get; private set; }
        public Vector2 SmoothedLocalVelocity { get { return _smoothed; } }

        public void Init(Animator a, RuntimeAnimatorController controller, float[] speeds = null)
        {
            Animator = a;
            if (controller != null) { Animator.runtimeAnimatorController = controller; _hasController = true; _actionLayer = Animator.layerCount > 1 ? 1 : -1; }
            Animator.applyRootMotion = false;
            if (speeds != null && speeds.Length >= 5) { JogSpeed = speeds[0]; RunSpeed = speeds[1]; SprintSpeed = speeds[2]; BackSpeed = speeds[3]; SideSpeed = speeds[4]; }
        }

        /// <summary>Match mode: the controller lives in a playable graph owned by the ActionAnimator.</summary>
        public void InitRig(Animator a, ActionAnimator rig, RuntimeAnimatorController controller, float[] speeds = null)
        {
            Animator = a; Rig = rig;
            _hasController = rig != null && rig.Init(a, controller);
            _actionLayer = _hasController && rig.Loco.GetLayerCount() > 1 ? 1 : -1;     // QA-726
            Animator.applyRootMotion = false;
            if (speeds != null && speeds.Length >= 5) { JogSpeed = speeds[0]; RunSpeed = speeds[1]; SprintSpeed = speeds[2]; BackSpeed = speeds[3]; SideSpeed = speeds[4]; }
        }

        bool UseRig { get { return Rig != null && Rig.Valid; } }
        bool Ready { get { return Rig == null || Rig.Valid; } }

        void SetF(int id, float v) { if (UseRig) { var l = Rig.Loco; l.SetFloat(id, v); } else Animator.SetFloat(id, v); }
        void SetB(int id, bool v) { if (UseRig) { var l = Rig.Loco; l.SetBool(id, v); } else Animator.SetBool(id, v); }
        void SetT(string name, bool _) { if (UseRig) { var l = Rig.Loco; l.SetTrigger(name); } else Animator.SetTrigger(name); }

        /// <summary>worldVelocity in m/s; facing is the body forward direction.</summary>
        public void Drive(Vector3 worldVelocity, Vector3 facing, float dt)
        {
            if (Animator == null || !_hasController || !Ready) return;
            Vector3 fwd = new Vector3(facing.x, 0f, facing.z).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float vx = Vector3.Dot(worldVelocity, right);
            float vz = Vector3.Dot(worldVelocity, fwd);
            var target = new Vector2(vx, vz);
            // v0.8 smoothness: unhurried weight - the animated velocity follows the simulation with a ~0.11 s time constant (was 0.045 s: twitchy direction changes)
            // v0.7.0: slowing down follows the simulation faster than speeding up, so the feet stop with the body instead of skating on for a tenth of a second
            float rate = target.sqrMagnitude < _smoothed.sqrMagnitude - 0.04f ? SmoothRate * 1.7f : SmoothRate;
            _smoothed = Vector2.Lerp(_smoothed, target, 1f - Mathf.Exp(-dt * rate));
            float speed = _smoothed.magnitude;
            SetF(CycleOffsetP, (((uint)_phaseId * 2654435761u >> 8) & 0xFFFFu) / 65535f);     // each player starts his cycles at a different phase (no synchronised hops)
            SetF(VelX, _smoothed.x);
            SetF(VelZ, _smoothed.y);
            SetF(SpeedP, speed);

            // foot matching: only above the fastest clip in the travel direction do we need to speed the cycle up
            float maxAlong;
            if (_smoothed.y >= 0f)
            {
                float side = Mathf.Abs(_smoothed.x), fwdc = _smoothed.y;
                float t = side / (side + fwdc + 1e-4f);
                maxAlong = Mathf.Lerp(SprintSpeed, SideSpeed, t);
            }
            else maxAlong = BackSpeed;
            float ratio = speed > 0.5f ? Mathf.Clamp(speed / Mathf.Max(0.1f, maxAlong), 1f, 1.6f) : 1f;
            _ratio = Mathf.Lerp(_ratio, ratio, 1f - Mathf.Exp(-dt * 8f));
            SetF(LocoSpeed, _ratio * Mathf.Clamp(Trim, 0.6f, 2.2f) * DebugTrim * StrideTrim(speed, _carrying, _smoothed.y < 0f));

            if (_actionLayer >= 0)
            {
                if (UseRig)
                {
                    var l = Rig.Loco;
                    var st = l.GetCurrentAnimatorStateInfo(_actionLayer);
                    InAction = l.IsInTransition(_actionLayer) || st.shortNameHash != NoneHash;
                }
                else
                {
                    var st = Animator.GetCurrentAnimatorStateInfo(_actionLayer);
                    InAction = Animator.IsInTransition(_actionLayer) || st.shortNameHash != NoneHash;
                }
            }
        }

        public void Trigger(PlayerMove m)
        {
            if (Animator == null || !_hasController || !Ready || m == PlayerMove.None) return;
            SetT(PlayerMoves.Get(m).Trigger, true);
        }

        /// <summary>v0.9.0 CONNECT E-158: a carrier move trigger that was never consumed (the move layer was busy) is dropped instead of firing late.</summary>
        public void ResetTrigger(PlayerMove m)
        {
            if (Animator == null || !_hasController || !Ready || m == PlayerMove.None) return;
            string n = PlayerMoves.Get(m).Trigger;
            if (UseRig) { var l = Rig.Loco; l.ResetTrigger(n); } else Animator.ResetTrigger(n);
        }

        public void SetCelebrating(bool on) { if (Animator != null && _hasController && Ready) SetB(Celebrating, on); }
        public void SetProtect(bool on) { _carrying = on; if (Animator != null && _hasController && Ready) SetB(ProtectP, on); }
    }
}
