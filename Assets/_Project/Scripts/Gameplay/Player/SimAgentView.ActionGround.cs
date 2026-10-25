using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.9.0 LOCOMOTION (LOCO-015): ground contact for ACTION clips. The locomotion gait keeps the feet of runners on the turf; every other clip (throw, pump fake, hand-off, catch, kick approach / plant / follow-through, celebration,
    /// get-up, block, the QB drop poses) used to hover wherever its mocap was authored (a field goal kicker 20 - 40 cm above the turf for the whole follow-through, a quarterback airborne mid-drop). This pass runs LAST in LateUpdate
    /// (animation -> stance / QB pose IK -> arm IK -> this) on the final pose:
    ///  1. the lowest sole point of the two feet is brought to the turf by lowering (or raising) the whole body, smoothly, up to half a metre - never while a genuine jump / dive / hurdle / spin or a contact take owns the body;
    ///  2. with the body nearly still, a foot that is on the turf and slow is planted in world space until the clip lifts it (FootPlant in its action mode: a 10 cm drift or a rise releases), the pelvis is lowered by the reach the legs miss.
    /// </summary>
    public partial class SimAgentView
    {
        float _actOff;
        /// <summary>v0.12.0: largest change (m) of the ground-fit offset in one frame (0.06 = 3.6 m/s).</summary>
        public const float OffsetSlew = 0.06f;
        public static readonly bool NoOffsetSlew = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixSlew") < 0;      // off by default (opt in with -pixSlew): it cut OL hips jumps but slowed the climb out of the turf, FEET_BELOW_TURF_7CM 35 -> 271 in 40 plays
        /// <summary>QA: switch the action ground pass off (the "before" state).</summary>
        public static bool DebugNoActionGround;
        /// <summary>QA: a pair take (ContactDriven) does not get the planted-foot hold (the pre-v0.10.0 behaviour).</summary>
        public static bool NoContactPlant = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-pixNoContactPlant") >= 0;
        /// <summary>Body speed (m/s) below which planted feet of a free clip / of a pair take are held still.</summary>
        public static float ActionPlantSpeed = 1.2f, ContactPlantSpeed = 1.0f;
        /// <summary>QA: how far (m) the action pass has lowered (-) or raised (+) the body this frame.</summary>
        public float ActionGroundOffset { get { return _actOff; } }
        /// <summary>QA: lowest sole height (m above the turf, bind pose = 0) of the final pose before the action pass moved the body; 9 when not measured.</summary>
        public float LastSoleGap { get; private set; } = 9f;

        /// <summary>Actions whose clips are genuinely airborne or owned by the contact director: no ground contact is enforced for them.</summary>
        public static bool AirborneAction(AgentAction a)
        {
            switch (a)
            {
                case AgentAction.Jump: case AgentAction.Dive: case AgentAction.Hurdle: case AgentAction.Spin: case AgentAction.Fallen: case AgentAction.Tackled:
                case AgentAction.TackleWrapUp: case AgentAction.TackleBigHit: case AgentAction.TackleShoestring: case AgentAction.TackleDive: case AgentAction.TackleGang: return true;
                default: return false;
            }
        }

        public static bool AirborneClip(string n)
        {
            if (string.IsNullOrEmpty(n)) return false;
            return n.IndexOf("jump", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("leap", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("dive", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("hurdle", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("flip", System.StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("spike", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void ApplyActionOffset()
        {
            if (Avatar == null || Avatar.Root == null) return;
            var p = Avatar.Root.localPosition;
            float y = _groundOff + _actOff;
            if (Mathf.Abs(p.y - y) > 1e-4f) Avatar.Root.localPosition = new Vector3(p.x, y, p.z);
        }

        void ActionGroundLate(float dt)
        {
            var plant = Motion != null ? Motion.Plant : null;
            if (plant == null || !plant.Ok || Avatar == null || Avatar.Root == null || Avatar.Animator == null || !Avatar.Animator.isHuman) { _actOff = 0f; return; }
            bool gaitOwns = LocoOwns;      // v0.10.0 MOTION: the matcher or the gait player
            bool contactTake = ContactDriven && !NoContactPlant;      // v0.10.0 CHOREO: a pair take (engaged hand fight, get-up, catch) also keeps its planted feet still
            bool ok = !DebugNoActionGround && (!ContactDriven || contactTake) && !(Ragdoll != null && Ragdoll.Active) && Rig != null && Rig.Valid && !AirborneAction(_lastAction) && !AirborneClip(Rig.CurrentClipName) && !gaitOwns;
            if (!ok)
            {
                plant.ActionClaim = false;
                if (Mathf.Abs(_actOff) > 1e-4f) { _actOff = Mathf.MoveTowards(_actOff, 0f, dt / 0.12f * 0.5f); ApplyActionOffset(); }
                LastSoleGap = 9f;
                return;
            }
            // 1. the lowest sole of the final pose (FitGround has already put the root at its own offset: this is the pose the viewer would see without this pass)
            var a = Avatar.Animator;
            Transform lf = a.GetBoneTransform(HumanBodyBones.LeftFoot), rf = a.GetBoneTransform(HumanBodyBones.RightFoot), lt = a.GetBoneTransform(HumanBodyBones.LeftToes), rt = a.GetBoneTransform(HumanBodyBones.RightToes);
            if (lf == null || rf == null) return;
            float gy = transform.position.y;
            float low = Mathf.Min(lf.position.y - gy - plant.RestAnkleL, rf.position.y - gy - plant.RestAnkleR);
            if (lt != null && rt != null) low = Mathf.Min(low, Mathf.Min(lt.position.y - gy - plant.RestToeL, rt.position.y - gy - plant.RestToeR));
            // the pose already contains this frame's offset only if FitGround kept it: it resets the root, so `low` is measured without the action offset
            LastSoleGap = low;
            float gap = low - FootPlant.SoleGoal;
            float target = ContactDriven ? 0f : (Mathf.Abs(gap) < 0.012f ? 0f : Mathf.Clamp(-gap, -0.5f, 0.08f));      // (a pair take keeps the director's own ground fit: only the feet are held)
            float tau = target < _actOff ? 0.03f : 0.06f;      // down fast (the body follows its lowest foot to the turf within two frames), up slower
            { float next = Mathf.Lerp(_actOff, target, 1f - Mathf.Exp(-dt / tau)); _actOff = (EngageStepper.V011 || NoOffsetSlew) ? next : _actOff + Mathf.Clamp(next - _actOff, -OffsetSlew * dt * 60f, OffsetSlew * dt * 60f); }      // v0.12.0 CONTACT: the body never drops or rises more than OffsetSlew in one frame (a hips jump over 10 cm is a pop, not a fit)
            ApplyActionOffset();
            // 2. planted feet of a still body stay planted
            if (plant.Speed < (ContactDriven ? ContactPlantSpeed : ActionPlantSpeed))
            {
                plant.ActionMode = true; plant.ActionClaim = true; plant.HintLeft = plant.HintRight = -1; plant.Warp = 1f; plant.GroundY = gy;
                plant.Late(dt, true);
                plant.ActionMode = false;
            }
            else plant.ActionClaim = false;
        }
    }
}
