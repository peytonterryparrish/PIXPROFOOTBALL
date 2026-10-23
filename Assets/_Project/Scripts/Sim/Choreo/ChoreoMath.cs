using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// Rigid transform from a take's frame (metres) to the field (yards): world = T + R(Yaw) * (authored * MetersToYards). Yaw is in degrees and follows the sim heading convention
    /// (0 = +Y downfield, +90 = +X), so an actor that faces yaw 0 in the take faces <see cref="Yaw"/> on the field.
    /// </summary>
    public struct ChoreoRigid
    {
        public Vec2 T;
        public float Yaw;

        public static ChoreoRigid Identity { get { return new ChoreoRigid(); } }
        public Vec2 Apply(Vec2 authoredMeters) { return T + ChoreoMath.Rotate(authoredMeters * ChoreoClip.MetersToYards, Yaw); }
        public Vec2 RotateOnly(Vec2 authoredMeters) { return ChoreoMath.Rotate(authoredMeters * ChoreoClip.MetersToYards, Yaw); }
    }

    /// <summary>Pure math of the choreographer (engine-free, unit tested).</summary>
    public static class ChoreoMath
    {
        public static float DeltaAngle(float from, float to)
        {
            float d = (to - from) % 360f;
            if (d > 180f) d -= 360f; else if (d < -180f) d += 360f;
            return d;
        }

        /// <summary>Rotates a ground vector (x, y) by yawDegrees so that +Y turns toward +X for a positive yaw (the heading convention of the field: atan2(x, y)).</summary>
        public static Vec2 Rotate(Vec2 v, float yawDegrees)
        {
            float r = yawDegrees * SimMath.Deg2Rad;
            float s = (float)Math.Sin(r), c = (float)Math.Cos(r);
            return new Vec2(v.X * c + v.Y * s, -v.X * s + v.Y * c);
        }

        /// <summary>Heading of a ground vector in degrees (+Y = 0, +X = 90).</summary>
        public static float YawOf(Vec2 v) { return (float)Math.Atan2(v.X, v.Y) * SimMath.Rad2Deg; }

        public static Vec2 DirOfYaw(float yawDegrees) { float r = yawDegrees * SimMath.Deg2Rad; return new Vec2((float)Math.Sin(r), (float)Math.Cos(r)); }

        public static float Smooth01(float x) { x = SimMath.Clamp01(x); return x * x * (3f - 2f * x); }

        // ---- turn profile (the defensive back's hip flip): angular rate ramps up, holds at omega, ramps down; the angle is the integral ----
        /// <summary>Seconds a turn of <paramref name="total"/> radians takes when the rate ramps 0 to omega over <paramref name="ramp"/> s, holds, and ramps back down.</summary>
        public static float TurnDuration(float total, float omega, float ramp)
        {
            total = Math.Abs(total); if (total < 1e-5f) return 0f;
            float a = omega / ramp;
            if (total <= omega * ramp) return 2f * (float)Math.Sqrt(total / a);
            return ramp + total / omega;
        }

        /// <summary>Angle (radians, 0..total) turned t seconds into the profile.</summary>
        public static float TurnAngle(float t, float total, float omega, float ramp)
        {
            total = Math.Abs(total); float T = TurnDuration(total, omega, ramp);
            if (t <= 0f) return 0f; if (t >= T) return total;
            float a = omega / ramp;
            if (total <= omega * ramp) { float tp = T * 0.5f; return t <= tp ? 0.5f * a * t * t : total - 0.5f * a * (T - t) * (T - t); }
            if (t < ramp) return 0.5f * a * t * t;
            if (t > T - ramp) { float u = T - t; return total - 0.5f * a * u * u; }
            return 0.5f * omega * ramp + omega * (t - ramp);
        }

        /// <summary>Where a subject is seen from an observer who faces observerYaw: 0 = straight ahead, +90 = to his right, +/-180 = behind him.</summary>
        public static float Bearing(Vec2 subject, Vec2 observer, float observerYaw)
        {
            Vec2 d = subject - observer;
            if (d.LenSq() < 1e-6f) return 0f;
            return DeltaAngle(observerYaw, YawOf(d));
        }

        /// <summary>Transform that puts an authored point (heading authoredYaw) at a field point with a field heading.</summary>
        public static ChoreoRigid Align(Vec2 authoredPointM, float authoredYaw, Vec2 worldPoint, float worldYaw)
        {
            float yaw = DeltaAngle(0f, worldYaw - authoredYaw);
            return new ChoreoRigid { Yaw = yaw, T = worldPoint - Rotate(authoredPointM * ChoreoClip.MetersToYards, yaw) };
        }

        /// <summary>
        /// Aligns an authored pair to the field: actor B is anchored (position exact, heading matched to worldYawB), and the yaw is a blend of "B's heading matches" and "the A to B axis matches"
        /// (axisWeight 0..1), so both bodies keep the authored layout and the second actor comes from where he really came from. Authored points in metres, world points in yards.
        /// </summary>
        public static ChoreoRigid AlignPair(Vec2 authA, Vec2 authB, float authYawB, Vec2 worldA, Vec2 worldB, float worldYawB, float axisWeight)
        {
            float facingYaw = DeltaAngle(0f, worldYawB - authYawB);
            Vec2 ab = authA - authB, wb = worldA - worldB;
            float yaw = facingYaw;
            if (ab.LenSq() > 1e-4f && wb.LenSq() > 1e-4f)
            {
                float axisYaw = DeltaAngle(0f, YawOf(wb) - YawOf(ab));
                yaw = facingYaw + DeltaAngle(facingYaw, axisYaw) * SimMath.Clamp01(axisWeight);
            }
            return new ChoreoRigid { Yaw = yaw, T = worldB - Rotate(authB * ChoreoClip.MetersToYards, yaw) };
        }

        /// <summary>Moves <paramref name="cur"/> toward <paramref name="target"/> by at most maxStep (yards).</summary>
        public static Vec2 MoveToward(Vec2 cur, Vec2 target, float maxStep)
        {
            Vec2 d = target - cur; float l = d.Length;
            if (l <= maxStep || l < 1e-6f) return target;
            return cur + d * (maxStep / l);
        }

        /// <summary>Turns a heading (degrees) toward a target heading by at most maxStep degrees.</summary>
        public static float TurnToward(float cur, float target, float maxStep)
        {
            float d = DeltaAngle(cur, target);
            if (Math.Abs(d) <= maxStep) return target;
            return cur + Math.Sign(d) * maxStep;
        }

        /// <summary>
        /// Time until a pursuer closes to <paramref name="reach"/> yards of a target, from relative position and velocity (both moving, constant velocity); a large number when he never gets there.
        /// rel = target - pursuer, relVel = targetVel - pursuerVel.
        /// </summary>
        public static float TimeToReach(Vec2 rel, Vec2 relVel, float reach)
        {
            float c = rel.LenSq() - reach * reach;
            if (c <= 0f) return 0f;
            float a = relVel.LenSq();
            if (a < 1e-4f) return 99f;
            float b = 2f * rel.Dot(relVel);
            float disc = b * b - 4f * a * c;
            if (disc < 0f) return 99f;
            float sq = (float)Math.Sqrt(disc);
            float t1 = (-b - sq) / (2f * a), t2 = (-b + sq) / (2f * a);
            if (t1 > 0f) return t1;
            return t2 > 0f ? t2 : 99f;
        }

        /// <summary>FNV-1a over ints: the stable per-booking pseudo-random value (variety among near-equal takes; the same on every run).</summary>
        public static uint Hash(int a, int b = 0, int c = 0)
        {
            unchecked
            {
                uint h = 2166136261u;
                h = (h ^ (uint)a) * 16777619u; h = (h ^ (uint)b) * 16777619u; h = (h ^ (uint)c) * 16777619u;
                h ^= h >> 15; h *= 2246822519u; h ^= h >> 13;
                return h;
            }
        }
        public static float Hash01(int a, int b = 0, int c = 0) { return (Hash(a, b, c) & 0xFFFF) / 65535f; }

        /// <summary>Stable FNV-1a of a string (string.GetHashCode is randomized per process on modern runtimes).</summary>
        public static int HashString(string s)
        {
            unchecked { uint h = 2166136261u; if (s != null) for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 16777619u; return (int)(h & 0x7fffffff); }
        }
    }
}
