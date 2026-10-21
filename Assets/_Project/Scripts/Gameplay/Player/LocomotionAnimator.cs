using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Feeds the Animator controller (built by AnimatorControllerBuilder): a 2D freeform blend tree on local velocity (m/s) for
    /// locomotion and an override action layer driven by triggers. Playback speed is matched to ground speed so feet do not slide.
    /// </summary>
    public class LocomotionAnimator : MonoBehaviour
    {
        public static readonly int VelX = Animator.StringToHash("VelX"), VelZ = Animator.StringToHash("VelZ"), SpeedP = Animator.StringToHash("Speed"),
            Celebrating = Animator.StringToHash("Celebrating"), ProtectP = Animator.StringToHash("Protect"), LocoSpeed = Animator.StringToHash("LocoSpeed");

        public Animator Animator;
        /// <summary>Natural ground speeds (m/s) of the clips in the blend tree along each axis (set from the baked controller data).</summary>
        public float JogSpeed = 3.2f, RunSpeed = 5.2f, SprintSpeed = 8.2f, BackSpeed = 2.2f, SideSpeed = 2.6f;

        Vector2 _smoothed;
        bool _hasController;
        int _actionLayer = -1;
        public bool InAction { get; private set; }

        public void Init(Animator a, RuntimeAnimatorController controller, float[] speeds = null)
        {
            Animator = a;
            if (controller != null) { Animator.runtimeAnimatorController = controller; _hasController = true; _actionLayer = Animator.layerCount > 1 ? 1 : -1; }
            Animator.applyRootMotion = false;
            if (speeds != null && speeds.Length >= 5) { JogSpeed = speeds[0]; RunSpeed = speeds[1]; SprintSpeed = speeds[2]; BackSpeed = speeds[3]; SideSpeed = speeds[4]; }
        }

        /// <summary>worldVelocity in m/s; facing is the body forward direction.</summary>
        public void Drive(Vector3 worldVelocity, Vector3 facing, float dt)
        {
            if (Animator == null || !_hasController) return;
            Vector3 fwd = new Vector3(facing.x, 0f, facing.z).normalized;
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            Vector3 right = Vector3.Cross(Vector3.up, fwd);
            float vx = Vector3.Dot(worldVelocity, right);
            float vz = Vector3.Dot(worldVelocity, fwd);
            var target = new Vector2(vx, vz);
            _smoothed = Vector2.Lerp(_smoothed, target, 1f - Mathf.Exp(-dt * 22f));
            float speed = _smoothed.magnitude;
            Animator.SetFloat(VelX, _smoothed.x);
            Animator.SetFloat(VelZ, _smoothed.y);
            Animator.SetFloat(SpeedP, speed);

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
            Animator.SetFloat(LocoSpeed, ratio);

            if (_actionLayer >= 0)
            {
                var st = Animator.GetCurrentAnimatorStateInfo(_actionLayer);
                InAction = Animator.IsInTransition(_actionLayer) || !st.IsName("None");
            }
        }

        public void Trigger(PlayerMove m)
        {
            if (Animator == null || !_hasController || m == PlayerMove.None) return;
            Animator.SetTrigger(PlayerMoves.Get(m).Trigger);
        }

        public void SetCelebrating(bool on) { if (Animator != null && _hasController) Animator.SetBool(Celebrating, on); }
        public void SetProtect(bool on) { if (Animator != null && _hasController) Animator.SetBool(ProtectP, on); }
    }
}
