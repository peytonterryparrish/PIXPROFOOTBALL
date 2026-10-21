using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>Coordinate conversion between the Sim (yards, X lateral 0..53.33, Y downfield 0..100) and Unity (metres, X lateral, Z downfield, centred on the pitch). Free roam only; the match uses <see cref="PlayFrame"/>.</summary>
    public static class SimSpace
    {
        public const float YardsToMeters = 0.9144f;
        public const float HalfWidthYd = 26.665f;
        public const float MidfieldYd = 50f;

        public static Vector3 ToUnity(Vec2 p) { return new Vector3((p.X - HalfWidthYd) * YardsToMeters, 0f, (p.Y - MidfieldYd) * YardsToMeters); }
        public static Vector3 ToUnity(Vec3 p) { return new Vector3((p.X - HalfWidthYd) * YardsToMeters, p.Z * YardsToMeters, (p.Y - MidfieldYd) * YardsToMeters); }
        public static Vec2 ToSim(Vector3 u) { return new Vec2(u.x / YardsToMeters + HalfWidthYd, u.z / YardsToMeters + MidfieldYd); }
        public static Vector3 VelToUnity(Vec2 v) { return new Vector3(v.X * YardsToMeters, 0f, v.Y * YardsToMeters); }
        public static Vector3 DirToUnity(Vec2 d) { return new Vector3(d.X, 0f, d.Y).normalized; }
    }

    /// <summary>
    /// Presentation of one simulated player: avatar + locomotion animator driven by position / velocity / facing / AgentAction.
    /// Free roam drives a single view directly; the match drives 22 of them (ApplyMatch) through a PlayableGraph so any mocap clip
    /// can be layered over the locomotion blend tree.
    /// </summary>
    public class SimAgentView : MonoBehaviour
    {
        public PlayerAvatar Avatar;
        public LocomotionAnimator Anim;
        public ActionAnimator Rig;
        public PlayerRole Role;
        public PlayerSize Size;
        public int AgentId = -1;
        AgentAction _lastAction = AgentAction.Idle;
        float _yaw;
        RushMove _lastRush;
        bool _ownsClip;
        float _kickT = -1f;
        /// <summary>The contact director owns this player right now (an authored pair take, a ragdoll or a get-up): ApplyMatch keeps its hands off the clip layer.</summary>
        public bool ContactDriven;
        public RagdollRig Ragdoll;
        /// <summary>Lean, foot lock and stiff-arm IK over the animation (v0.4).</summary>
        public BodyMotion Motion;

        public static SimAgentView Create(Transform parent, PlayerSize size, TeamColors colors, string name = "AgentView")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<SimAgentView>();
            v.Size = size;
            v.Avatar = PlayerAvatar.Spawn(go.transform, size, colors, "Avatar");
            v.Anim = go.AddComponent<LocomotionAnimator>();
            if (v.Avatar.Animator != null)
            {
                var rt = PixRuntimeAssets.Load();
                v.Anim.Init(v.Avatar.Animator, rt != null ? rt.playerController : null, rt != null ? rt.clipSpeeds : null);
            }
            return v;
        }

        /// <summary>Match view: locomotion controller inside an ActionAnimator graph.</summary>
        public static SimAgentView CreateForMatch(Transform parent, PlayerSize size, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<SimAgentView>();
            v.Size = size;
            v.Avatar = PlayerAvatar.Spawn(go.transform, size, TeamColors.Home, "Avatar");
            v.Anim = go.AddComponent<LocomotionAnimator>();
            v.Rig = go.AddComponent<ActionAnimator>();
            if (v.Avatar.Animator != null) v.Avatar.Animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;   // players outside the camera are not posed
            v.Ragdoll = go.AddComponent<RagdollRig>(); v.Ragdoll.Setup(v.Avatar.Animator);
            v.Motion = new BodyMotion(v.Avatar);
            if (v.Avatar.Animator != null)
            {
                var rt = PixRuntimeAssets.Load();
                v.Anim.InitRig(v.Avatar.Animator, v.Rig, rt != null ? rt.playerController : null, rt != null ? rt.clipSpeeds : null);
            }
            return v;
        }

        public void Dress(TeamColors uniform, TeamColors helmet)
        {
            if (Avatar == null) return;
            Avatar.SetColors(uniform); Avatar.SetHelmetColors(helmet);
        }

        /// <summary>Applies a simulation sample: world pose and animation state. lateralHint (-1 left, +1 right) disambiguates handed moves.</summary>
        public void Apply(Vector3 position, Vector3 velocity, Vector3 facing, AgentAction action, float lateralHint, float dt)
        {
            transform.position = position;
            float target = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            _yaw = Mathf.MoveTowardsAngle(_yaw, target, 900f * dt);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 f = new Vector3(Mathf.Sin(_yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(_yaw * Mathf.Deg2Rad));
            Anim.Drive(velocity, f, dt);
            if (action != _lastAction)
            {
                var move = MoveFor(action, lateralHint);
                if (move != PlayerMove.None) Anim.Trigger(move);
                Anim.SetCelebrating(action == AgentAction.Celebrate);
                _lastAction = action;
            }
        }

        /// <summary>Contact director: places the view root and yaw for an authored take (after ApplyMatch has set the simulation pose).</summary>
        public void SetContactPose(Vector3 position, float yaw)
        {
            transform.position = position; _yaw = yaw; transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        /// <summary>Puts the view root under a ragdoll that could not be recovered with a get-up take.</summary>
        public void SnapToGround(Vector2 xz, float yaw)
        {
            transform.position = new Vector3(xz.x, transform.position.y, xz.y); _yaw = yaw; transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        public void SnapYaw(float yaw) { _yaw = yaw; }
        public float Yaw { get { return _yaw; } }
        public Vector3 Forward { get { return new Vector3(Mathf.Sin(_yaw * Mathf.Deg2Rad), 0f, Mathf.Cos(_yaw * Mathf.Deg2Rad)); } }

        /// <summary>Resets animation state when the view is re-used for a new play.</summary>
        public void ResetForPlay(Vector3 position, float yaw)
        {
            transform.position = position; _yaw = yaw; transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            _lastAction = (AgentAction)(-1); _ownsClip = false; _kickT = -1f; _lastRush = RushMove.None; ContactDriven = false;
            if (Ragdoll != null) Ragdoll.Cancel();
            if (Motion != null) Motion.Cancel();
            if (Rig != null) Rig.StopImmediate();
            if (Anim != null) Anim.Drive(Vector3.zero, Forward, 1f);
        }

        // ------------------------------------------------------------------------------------------ match
        public void ApplyMatch(Vector3 position, Vector3 velocity, Vector3 facing, AgentAction action, in AnimHints h, float lateralHint, bool specialTeams, bool shotgunQb, float dt)
        {
            transform.position = position;
            float target = Mathf.Atan2(facing.x, facing.z) * Mathf.Rad2Deg;
            float turnRate = (action == AgentAction.Tackled || action == AgentAction.Fallen) ? 120f : 720f;
            _yaw = Mathf.MoveTowardsAngle(_yaw, target, turnRate * dt);
            transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 f = Forward;
            Anim.Drive(velocity, f, dt);
            if (Rig != null && !Rig.Valid) return;   // the pooled player was just re-activated: wait for his graph
            if (Motion != null)
            {
                Motion.Sample(velocity, f, dt);
                Motion.LocomotionWeight = (ContactDriven || (Ragdoll != null && Ragdoll.Active)) ? 0f : 1f - (Rig != null ? Mathf.Clamp01(Rig.Weight) : 0f);
            }

            if (ContactDriven) { _lastAction = action; _lastRush = h.Rush; return; }   // an authored take / ragdoll / get-up is running
            bool changed = action != _lastAction || (action == AgentAction.Engaged && h.Rush != _lastRush);
            if (!changed) return;
            _lastAction = action; _lastRush = h.Rush;

            ClipPlan plan = default(ClipPlan);
            bool owns = true;
            switch (action)
            {
                case AgentAction.Stance: plan = AnimKit.Stance(Role, shotgunQb, h.Seed, specialTeams); break;
                case AgentAction.Block: plan = AnimKit.Block(h.Seed); break;
                case AgentAction.Engaged: plan = AnimKit.Engaged(h); break;
                case AgentAction.Shed: plan = AnimKit.Shed(h.Seed); break;
                case AgentAction.Catch: plan = AnimKit.Catch(h); break;
                case AgentAction.Throw: plan = AnimKit.Throw(h); break;
                case AgentAction.Handoff: plan = AnimKit.Handoff(h); break;
                case AgentAction.Jump: plan = AnimKit.Jump(h.Seed); break;
                case AgentAction.Spin: plan = AnimKit.Spin(h.Seed); break;
                case AgentAction.TackleWrapUp: case AgentAction.TackleBigHit: case AgentAction.TackleShoestring: case AgentAction.TackleDive: case AgentAction.TackleGang:
                    plan = AnimKit.Tackle(h, false); break;
                case AgentAction.Tackled: plan = h.Tackle != TackleType.None || h.Sack ? AnimKit.Tackle(h, true) : AnimKit.Fall(h.Seed); break;
                case AgentAction.Fallen: plan = AnimKit.Fall(h.Seed); break;
                case AgentAction.Celebrate: plan = AnimKit.Celebrate(h.Seed); break;
                case AgentAction.Kick: _kickT = 0f; owns = false; break;
                default: owns = false; break;
            }
            var mv = MoveFor(action, lateralHint);
            if (mv != PlayerMove.None && !(action == AgentAction.Spin && plan.Valid)) { Anim.Trigger(mv); owns = false; }
            if (plan.Valid && Rig != null) { Rig.Play(plan.Clip, plan.Start, plan.End, plan.Speed, plan.FadeIn, plan.FadeOut, plan.Loop, plan.Hold); _ownsClip = true; }
            else if (_ownsClip && Rig != null) { Rig.Stop(0.18f); _ownsClip = false; }
        }

        /// <summary>
        /// Presentation-only contact correction applied after <see cref="ApplyMatch"/> for paired clips (tackles, engaged blocks): the engine owns the positions, but the
        /// paired takes were authored at a fixed distance, so the view steps toward its partner (offset, scaled by w) and optionally turns to face him.
        /// </summary>
        public void ApplyContact(Vector3 offset, float yawToward, float w, bool turn)
        {
            if (w <= 0f) return;
            transform.position += offset * w;
            if (turn) { _yaw = Mathf.LerpAngle(_yaw, yawToward, Mathf.Clamp01(w)); transform.rotation = Quaternion.Euler(0f, _yaw, 0f); }
        }

        /// <summary>Procedural leg swing for kicks and punts (no kicking mocap exists in the source set): hip and knee of the right leg are driven in LateUpdate over the running animation.</summary>
        int _lodTick;
        void LateUpdate()
        {
            if (Avatar != null && Avatar.Lod != null && ((++_lodTick + AgentId) & 3) == 0) Avatar.Lod.Update(transform.position);
            if (Motion != null) Motion.Late(Time.deltaTime);
            if (_kickT < 0f || Avatar == null || Avatar.RightUpperLeg == null || Avatar.RightLowerLeg == null) return;
            _kickT += Time.deltaTime;
            float t = _kickT;
            if (t > 0.75f) { _kickT = -1f; return; }
            // hip angle about the lateral axis (positive = leg forward), knee flexion (positive = foot back)
            float hip, knee, w = 1f;
            if (t < 0.12f) { float u = t / 0.12f; hip = Mathf.Lerp(-38f, 38f, u * u); knee = Mathf.Lerp(95f, 8f, u); }
            else if (t < 0.38f) { float u = (t - 0.12f) / 0.26f; hip = Mathf.Lerp(38f, 82f, 1f - (1f - u) * (1f - u)); knee = Mathf.Lerp(8f, 30f, u); }
            else { float u = (t - 0.38f) / 0.37f; w = 1f - u * u * (3f - 2f * u); hip = 82f; knee = 30f; }
            if (t < 0.06f) w = Mathf.Clamp01(t / 0.06f);
            Vector3 right = Vector3.Cross(Vector3.up, Forward);
            Avatar.RightUpperLeg.rotation = Quaternion.AngleAxis(-hip * w, right) * Avatar.RightUpperLeg.rotation;
            Avatar.RightLowerLeg.rotation = Quaternion.AngleAxis(knee * w, right) * Avatar.RightLowerLeg.rotation;
            if (Avatar.Chest != null) Avatar.Chest.rotation = Quaternion.AngleAxis(-10f * w, right) * Avatar.Chest.rotation;
        }

        /// <summary>Sim action to ball-carrier move (other actions are covered by locomotion).</summary>
        public static PlayerMove MoveFor(AgentAction a, float lateralHint)
        {
            switch (a)
            {
                case AgentAction.Juke: return lateralHint < 0f ? PlayerMove.JukeLeft : PlayerMove.JukeRight;
                case AgentAction.Spin: return PlayerMove.Spin;
                case AgentAction.StiffArm: return lateralHint < 0f ? PlayerMove.StiffArmLeft : PlayerMove.StiffArmRight;
                case AgentAction.Truck: return PlayerMove.Truck;
                case AgentAction.Hurdle: return PlayerMove.Hurdle;
                case AgentAction.Dive: return PlayerMove.Dive;
                default: return PlayerMove.None;
            }
        }
    }
}

