using PixFootball.Art;
using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>Coordinate conversion between the Sim (yards, X lateral 0..53.33, Y downfield 0..100) and Unity (metres, X lateral, Z downfield, centred on the pitch).</summary>
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
    /// Used by the free-roam PlayerController today and reusable one-per-agent for the 22-player field (v0.2).
    /// </summary>
    public class SimAgentView : MonoBehaviour
    {
        public PlayerAvatar Avatar;
        public LocomotionAnimator Anim;
        AgentAction _lastAction = AgentAction.Idle;
        float _yaw;

        public static SimAgentView Create(Transform parent, PlayerSize size, TeamColors colors, string name = "AgentView")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<SimAgentView>();
            v.Avatar = PlayerAvatar.Spawn(go.transform, size, colors, "Avatar");
            v.Anim = go.AddComponent<LocomotionAnimator>();
            if (v.Avatar.Animator != null)
            {
                var rt = PixRuntimeAssets.Load();
                v.Anim.Init(v.Avatar.Animator, rt != null ? rt.playerController : null, rt != null ? rt.clipSpeeds : null);
            }
            return v;
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

        public void SnapYaw(float yaw) { _yaw = yaw; }

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
