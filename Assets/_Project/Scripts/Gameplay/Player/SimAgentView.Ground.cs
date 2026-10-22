using UnityEngine;
using PixFootball.Sim;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// v0.7.0 Track A (notes 5, 26, 17: weight, no levitating): the stance, block and idle clips were cleaned on the legacy medium body; on the photoreal bodies their feet hover up to 15 cm above the turf (the 3-point linemen, the tight end,
    /// the 4-point stance) or sink 8 cm into it (the receiver, back and safety stances). While a stationary clip owns the body the avatar is lowered / raised so the lowest sole point (ankle or ball of the foot, measured against the heights
    /// of the same bones in the bind pose) rests on the ground; the offset eases in and out and is zero for everything that moves.
    /// </summary>
    public partial class SimAgentView
    {
        float _restAnkle = -1f, _restToe = -1f, _groundOff;
        /// <summary>QA: switch the ground fit off (the stance test measures the "before" state with it).</summary>
        public static bool DebugNoGroundFit;
        public float GroundOffset { get { return _groundOff; } }

        /// <summary>Heights of the ankle and toe bones above the view root in the bind pose (the avatar was just created: nothing has animated it yet).</summary>
        void CaptureRest()
        {
            if (Avatar == null || Avatar.Animator == null || !Avatar.Animator.isHuman) return;
            var a = Avatar.Animator;
            Transform lf = a.GetBoneTransform(HumanBodyBones.LeftFoot), rf = a.GetBoneTransform(HumanBodyBones.RightFoot), lt = a.GetBoneTransform(HumanBodyBones.LeftToes), rt = a.GetBoneTransform(HumanBodyBones.RightToes);
            if (lf == null || rf == null) return;
            float gy = a.transform.position.y - (Avatar.Root != null ? Avatar.Root.localPosition.y : 0f);
            _restAnkle = Mathf.Min(lf.position.y, rf.position.y) - gy;
            _restToe = lt != null && rt != null ? Mathf.Min(lt.position.y, rt.position.y) - gy : -1f;
        }

        void FitGround(float dt)
        {
            if (DebugNoGroundFit || _restAnkle < 0f || Avatar == null || Avatar.Root == null || Avatar.Animator == null || !Avatar.Animator.isHuman) return;
            bool wants = !ContactDriven && !(Ragdoll != null && Ragdoll.Active) && Rig != null && Rig.Valid
                         && (_lastAction == AgentAction.Stance || _lastAction == AgentAction.Idle || _lastAction == AgentAction.Block);
            float target = 0f;
            if (wants)
            {
                var a = Avatar.Animator;
                Transform lf = a.GetBoneTransform(HumanBodyBones.LeftFoot), rf = a.GetBoneTransform(HumanBodyBones.RightFoot), lt = a.GetBoneTransform(HumanBodyBones.LeftToes), rt = a.GetBoneTransform(HumanBodyBones.RightToes);
                if (lf != null && rf != null)
                {
                    float gy = transform.position.y;
                    // clearance of the lowest foot, whichever bone touches: the ankle (flat foot) or the ball of the foot (heel raised)
                    float gap = Mathf.Min(lf.position.y, rf.position.y) - gy - _restAnkle;
                    if (_restToe >= 0f && lt != null && rt != null) gap = Mathf.Min(gap, Mathf.Min(lt.position.y, rt.position.y) - gy - _restToe);
                    target = Mathf.Clamp(-(gap - _groundOff), -0.25f, 0.25f);
                }
            }
            _groundOff = Mathf.Lerp(_groundOff, target, 1f - Mathf.Exp(-dt / 0.12f));
            var p = Avatar.Root.localPosition;
            if (Mathf.Abs(p.y - _groundOff) > 1e-4f) Avatar.Root.localPosition = new Vector3(p.x, _groundOff, p.z);
        }
    }
}
