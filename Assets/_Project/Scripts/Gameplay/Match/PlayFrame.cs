using PixFootball.Core;
using PixFootball.Sim;
using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>
    /// Maps the simulation frame (offense always drives toward +Y, X lateral) onto the stadium (field long axis = Z).
    /// When the offense is driving toward the -Z end zone the whole frame is rotated 180 degrees about the vertical axis
    /// (both X and Z mirrored), which keeps handedness: the offense's right is always the offense's right.
    /// </summary>
    public readonly struct PlayFrame
    {
        public const float YardsToMeters = 0.9144f;
        public const float HalfWidthYd = 26.665f;
        public const float MidfieldYd = 50f;

        /// <summary>True when the offense attacks the +Z end zone.</summary>
        public readonly bool TowardsPlusZ;
        readonly float _s;

        public PlayFrame(bool towardsPlusZ) { TowardsPlusZ = towardsPlusZ; _s = towardsPlusZ ? 1f : -1f; }

        /// <summary>The offense's end-zone direction for a quarter: the home team attacks +Z in odd quarters and -Z in even ones (ends swap every quarter).</summary>
        public static bool HomeAttacksPlusZ(int quarter) { return (quarter & 1) == 1; }

        public static PlayFrame For(Team offense, int quarter)
        {
            bool homePlus = HomeAttacksPlusZ(quarter);
            return new PlayFrame(offense == Team.Home ? homePlus : !homePlus);
        }

        public Vector3 ToUnity(Vec2 p) { return new Vector3(_s * (p.X - HalfWidthYd) * YardsToMeters, 0f, _s * (p.Y - MidfieldYd) * YardsToMeters); }
        public Vector3 ToUnity(Vec3 p) { return new Vector3(_s * (p.X - HalfWidthYd) * YardsToMeters, p.Z * YardsToMeters, _s * (p.Y - MidfieldYd) * YardsToMeters); }
        public Vector3 ToUnity(float simX, float simY) { return new Vector3(_s * (simX - HalfWidthYd) * YardsToMeters, 0f, _s * (simY - MidfieldYd) * YardsToMeters); }
        public Vector3 VelToUnity(Vec2 v) { return new Vector3(_s * v.X * YardsToMeters, 0f, _s * v.Y * YardsToMeters); }
        public Vector3 DirToUnity(Vec2 d) { Vector3 v = new Vector3(_s * d.X, 0f, _s * d.Y); return v.sqrMagnitude < 1e-8f ? Vector3.forward : v.normalized; }

        public Vec2 ToSim(Vector3 u) { return new Vec2(_s * u.x / YardsToMeters + HalfWidthYd, _s * u.z / YardsToMeters + MidfieldYd); }
        /// <summary>World-space XZ direction (x right, y = z forward) to a simulation direction.</summary>
        public Vec2 DirToSim(Vector2 worldXZ) { return new Vec2(_s * worldXZ.x, _s * worldXZ.y); }

        /// <summary>Unity Z of a yard line measured from the offense's own goal line (0..100).</summary>
        public float SpotToZ(float spot) { return _s * (spot - MidfieldYd) * YardsToMeters; }

        /// <summary>Yaw in degrees (about +Y, 0 = +Z) the offense faces when driving downfield.</summary>
        public float DownfieldYaw { get { return TowardsPlusZ ? 0f : 180f; } }
    }
}
