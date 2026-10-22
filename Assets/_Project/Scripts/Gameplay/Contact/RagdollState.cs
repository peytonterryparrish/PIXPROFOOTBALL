using UnityEngine;

namespace PixFootball.Gameplay
{
    public enum RagdollPhase
    {
        /// <summary>The animation drives the body.</summary>
        Animated,
        /// <summary>The pose is handed from the animation to the physics (weight 0 to 1).</summary>
        BlendIn,
        /// <summary>The joints and colliders simulate the body (weight 1).</summary>
        Active,
        /// <summary>The settled pose is blended back into a get-up take (weight 1 to 0).</summary>
        Recover
    }

    /// <summary>
    /// Pure state machine of the animation to ragdoll blend: Animated, BlendIn, Active, Recover, Animated. The Unity side (RagdollRig) feeds it the fastest bone speed and whether the body
    /// lies on the ground and applies <see cref="Weight"/> to the pose. Engine-free math only, so the transitions are unit tested.
    /// </summary>
    public sealed class RagdollStateMachine
    {
        public float BlendInTime = 0.12f;
        public float MinActive = 0.4f, MaxActive = 2.0f;      // v0.9.0 CONNECT E-230: was 1.2 s, which froze bodies mid-tumble
        public float SettleSpeed = 0.7f;
        public float RecoverTime = 0.35f;

        public RagdollPhase Phase { get; private set; }
        public float PhaseTime { get; private set; }
        /// <summary>0 = animation, 1 = physics.</summary>
        public float Weight { get; private set; }
        /// <summary>How long the body may simulate in this activation.</summary>
        public float ActiveBudget { get; private set; }
        public float Intensity { get; private set; }
        /// <summary>Raised once when the machine enters Recover (the Unity side picks the get-up take by the face up / down state).</summary>
        public bool RecoverRequested { get; private set; }

        /// <summary>Starts an activation. Intensity 0..1 scales how long the body is left to the physics (0.4 s for a nudge, 1.2 s for a big hit). Ignored unless the body is animated.</summary>
        public bool Trigger(float intensity)
        {
            if (Phase != RagdollPhase.Animated) return false;
            Intensity = Mathf.Clamp01(intensity);
            ActiveBudget = Mathf.Lerp(MinActive, MaxActive, Intensity);
            Phase = RagdollPhase.BlendIn; PhaseTime = 0f; Weight = 0f; RecoverRequested = false;
            return true;
        }

        /// <summary>Back to the animation at once.</summary>
        public void Cancel()
        {
            Phase = RagdollPhase.Animated; PhaseTime = 0f; Weight = 0f; RecoverRequested = false;
        }

        public bool Active { get { return Phase != RagdollPhase.Animated; } }

        public bool ConsumeRecoverRequest() { bool r = RecoverRequested; RecoverRequested = false; return r; }

        /// <summary>Advances the machine. maxBoneSpeed: fastest rigid body speed (m/s); grounded: the pelvis is within a few centimetres of the ground.</summary>
        public void Update(float dt, float maxBoneSpeed, bool grounded)
        {
            if (Phase == RagdollPhase.Animated) return;
            PhaseTime += dt;
            switch (Phase)
            {
                case RagdollPhase.BlendIn:
                    Weight = Mathf.Clamp01(PhaseTime / Mathf.Max(0.01f, BlendInTime));
                    if (Weight >= 1f) { Phase = RagdollPhase.Active; PhaseTime = 0f; }
                    break;
                case RagdollPhase.Active:
                    Weight = 1f;
                    bool settled = grounded && maxBoneSpeed < SettleSpeed && PhaseTime >= MinActive;
                    bool budgetOver = PhaseTime >= ActiveBudget && (maxBoneSpeed < SettleSpeed * 2.5f || PhaseTime >= ActiveBudget + 0.6f);      // v0.9.0 CONNECT E-230: a body still tumbling is given up to 0.6 s more (speed gate)
                    if (settled || budgetOver) { Phase = RagdollPhase.Recover; PhaseTime = 0f; RecoverRequested = true; }
                    break;
                case RagdollPhase.Recover:
                    Weight = 1f - Mathf.Clamp01(PhaseTime / Mathf.Max(0.01f, RecoverTime));
                    if (Weight <= 0f) { Phase = RagdollPhase.Animated; PhaseTime = 0f; Weight = 0f; }
                    break;
            }
        }

        /// <summary>+1 lying face up .. -1 face down, from the chest normal's vertical component (front.y).</summary>
        public static bool IsFaceUp(float chestFrontY) { return chestFrontY > 0f; }

        /// <summary>Clamp for the simulated bodies: no rigid body may exceed this speed (no explosions).</summary>
        public static Vector3 CapVelocity(Vector3 v, float maxSpeed)
        {
            float m = v.magnitude;
            return m > maxSpeed && m > 1e-5f ? v * (maxSpeed / m) : v;
        }
    }
}
