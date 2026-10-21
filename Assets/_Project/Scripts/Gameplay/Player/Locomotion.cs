using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    public struct LocomotionInput
    {
        /// <summary>Desired direction in world XZ (x, z); magnitude 0..1 is stick deflection.</summary>
        public Vector2 Move;
        public bool Sprint;
        public bool Protect;
        public bool HasFace;
        public Vector2 FaceDir;
    }

    /// <summary>Source of ground locomotion for one controllable player. Implemented over the Sim AgentBody; can be swapped.</summary>
    public interface ILocomotionSource
    {
        Vector3 Position { get; }
        /// <summary>Ground velocity, metres per second.</summary>
        Vector3 Velocity { get; }
        /// <summary>Unit horizontal facing.</summary>
        Vector3 Facing { get; }
        float TopSpeed { get; }
        float Energy01 { get; }
        bool IsDown { get; }
        void Teleport(Vector3 position, float yawDegrees);
        void Step(float dt, LocomotionInput input);
        /// <summary>Instant velocity change in m/s (juke kicks, truck bursts).</summary>
        void Kick(Vector3 deltaVelocity);
        void SetSpeedMultiplier(float m);
        void Fall(float seconds);
        void Stumble(float seconds);
        /// <summary>Keeps the body inside an XZ rectangle (metres), killing outward velocity.</summary>
        void ClampPosition(Vector2 min, Vector2 max);
    }

    /// <summary>Momentum locomotion driven by PixFootball.Sim.AgentBody (yards internally, metres at the boundary).</summary>
    public sealed class AgentBodyLocomotion : ILocomotionSource
    {
        public const float YardsToMeters = 0.9144f;
        readonly AgentBody _body;
        public AgentBody Body { get { return _body; } }
        /// <summary>Fraction of top speed used when Sprint is not held (full stick = run, not sprint).</summary>
        public float RunFraction = 0.74f;

        public AgentBodyLocomotion(Ratings ratings, int weightLb = 205, int heightIn = 72)
        {
            _body = new AgentBody(ratings, weightLb, heightIn);
        }

        public static Ratings DefaultRatings()
        {
            var r = Ratings.Uniform(80);
            r.Speed = 80; r.Acceleration = 88; r.Agility = 88; r.Stamina = 90; r.Strength = 72; r.Elusiveness = 90;
            return r;
        }

        public Vector3 Position { get { return SimSpace.ToUnity(_body.Pos); } }
        public Vector3 Velocity { get { return new Vector3(_body.Vel.X * YardsToMeters, 0f, _body.Vel.Y * YardsToMeters); } }
        public Vector3 Facing { get { return new Vector3(_body.Facing.X, 0f, _body.Facing.Y).normalized; } }
        public float TopSpeed { get { return _body.EffectiveVMax * YardsToMeters; } }
        public float Energy01 { get { return _body.Energy; } }
        public bool IsDown { get { return _body.IsDown; } }

        public void Teleport(Vector3 position, float yawDegrees)
        {
            _body.Pos = SimSpace.ToSim(position);
            _body.Vel = Vec2.Zero;
            float r = yawDegrees * Mathf.Deg2Rad;
            _body.Facing = new Vec2(Mathf.Sin(r), Mathf.Cos(r));
            _body.FallenTime = 0f; _body.StumbleTime = 0f;
        }

        public void Step(float dt, LocomotionInput input)
        {
            Vector2 m = input.Move;
            float mag = Mathf.Clamp01(m.magnitude);
            Vec2 want = Vec2.Zero;
            if (mag > 0.001f)
            {
                float vmax = _body.EffectiveVMax;
                float cap = input.Sprint ? vmax : vmax * RunFraction;
                // analog stick: shaped so small deflections walk/jog
                float shaped = Mathf.Pow(mag, 1.25f);
                if (input.Protect) cap *= 0.9f;
                Vec2 dir = new Vec2(m.x / (mag + 1e-6f), m.y / (mag + 1e-6f));
                want = dir * (cap * shaped);
            }
            Vec2 face = input.HasFace ? new Vec2(input.FaceDir.x, input.FaceDir.y) : Vec2.Zero;
            _body.Step(dt, want, input.HasFace, face, input.Sprint && mag > 0.1f ? 1f : 0f);
        }

        public void Kick(Vector3 dv) { _body.Knock(new Vec2(dv.x / YardsToMeters, dv.z / YardsToMeters)); }
        public void SetSpeedMultiplier(float m) { _body.SpeedMultiplier = m; }
        public void Fall(float seconds) { _body.Fall(seconds); }
        public void Stumble(float seconds) { _body.Stumble(seconds); }

        public void ClampPosition(Vector2 min, Vector2 max)
        {
            Vector3 up = SimSpace.ToUnity(_body.Pos); float x = up.x, z = up.z;
            float vx = _body.Vel.X, vz = _body.Vel.Y;
            bool hit = false;
            if (x < min.x) { x = min.x; if (vx < 0f) vx = 0f; hit = true; } else if (x > max.x) { x = max.x; if (vx > 0f) vx = 0f; hit = true; }
            if (z < min.y) { z = min.y; if (vz < 0f) vz = 0f; hit = true; } else if (z > max.y) { z = max.y; if (vz > 0f) vz = 0f; hit = true; }
            if (!hit) return;
            _body.Pos = SimSpace.ToSim(new Vector3(x, 0f, z));
            _body.Vel = new Vec2(vx, vz);
        }
    }
}
