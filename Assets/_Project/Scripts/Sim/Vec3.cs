using System;
using PixFootball.Core;

namespace PixFootball.Sim
{
    /// <summary>
    /// Engine-free 3D vector for the simulation. X = lateral yards (field coordinates, 0..53.33),
    /// Y = downfield yards in the OFFENSE's frame (own goal line = 0, opponent goal line = 100), Z = height above ground (yards).
    /// </summary>
    public readonly struct Vec3 : IEquatable<Vec3>
    {
        public readonly float X, Y, Z;
        public Vec3(float x, float y, float z) { X = x; Y = y; Z = z; }
        public Vec3(Vec2 xy, float z) { X = xy.X; Y = xy.Y; Z = z; }
        public static readonly Vec3 Zero = new Vec3(0f, 0f, 0f);
        public Vec2 XY { get { return new Vec2(X, Y); } }
        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y + Z * Z); } }
        public float LengthSq { get { return X * X + Y * Y + Z * Z; } }
        public float HorizontalLength { get { return (float)Math.Sqrt(X * X + Y * Y); } }
        public static Vec3 operator +(Vec3 a, Vec3 b) { return new Vec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z); }
        public static Vec3 operator -(Vec3 a, Vec3 b) { return new Vec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z); }
        public static Vec3 operator *(Vec3 a, float s) { return new Vec3(a.X * s, a.Y * s, a.Z * s); }
        public static Vec3 operator *(float s, Vec3 a) { return new Vec3(a.X * s, a.Y * s, a.Z * s); }
        public static float Dot(Vec3 a, Vec3 b) { return a.X * b.X + a.Y * b.Y + a.Z * b.Z; }
        public static float Distance(Vec3 a, Vec3 b) { return (a - b).Length; }
        public Vec3 Normalized
        {
            get { float l = Length; return l > 1e-6f ? new Vec3(X / l, Y / l, Z / l) : Zero; }
        }
        public bool Equals(Vec3 o) { return X == o.X && Y == o.Y && Z == o.Z; }
        public override bool Equals(object obj) { return obj is Vec3 v && Equals(v); }
        public override int GetHashCode() { unchecked { return (((X.GetHashCode() * 397) ^ Y.GetHashCode()) * 397) ^ Z.GetHashCode(); } }
        public override string ToString() { return "(" + X.ToString("0.##") + "," + Y.ToString("0.##") + "," + Z.ToString("0.##") + ")"; }
    }

    /// <summary>Small math helpers (Vec2 extensions, clamping) shared by the simulation.</summary>
    public static class SimMath
    {
        public const float Pi = 3.14159265f;
        public const float Deg2Rad = Pi / 180f;
        public const float Rad2Deg = 180f / Pi;
        /// <summary>Gravity in yards per second squared.</summary>
        public const float Gravity = 10.73f;

        public static float Clamp(float v, float lo, float hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static int Clamp(int v, int lo, int hi) { return v < lo ? lo : (v > hi ? hi : v); }
        public static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static float LerpClamped(float a, float b, float t) { return a + (b - a) * Clamp01(t); }
        public static float InvLerp(float a, float b, float v) { return Math.Abs(b - a) < 1e-6f ? 0f : (v - a) / (b - a); }
        public static float Remap(float v, float a0, float a1, float b0, float b1) { return LerpClamped(b0, b1, InvLerp(a0, a1, v)); }
        public static float Sqr(float v) { return v * v; }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static float Abs(float v) { return v < 0f ? -v : v; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Sign(float v) { return v < 0f ? -1f : 1f; }
        public static float Rating01(int r) { return Clamp01(r / 99f); }
        public static float MoveToward(float cur, float target, float maxDelta)
        {
            if (Math.Abs(target - cur) <= maxDelta) return target;
            return cur + Math.Sign(target - cur) * maxDelta;
        }
        public static float SmoothStep(float a, float b, float v)
        {
            float t = Clamp01(InvLerp(a, b, v));
            return t * t * (3f - 2f * t);
        }

        // --- Vec2 helpers ---
        public static float Dot(this Vec2 a, Vec2 b) { return a.X * b.X + a.Y * b.Y; }
        public static float Cross(this Vec2 a, Vec2 b) { return a.X * b.Y - a.Y * b.X; }
        public static float LenSq(this Vec2 a) { return a.X * a.X + a.Y * a.Y; }
        public static Vec2 Norm(this Vec2 a)
        {
            float l = a.Length;
            return l > 1e-5f ? new Vec2(a.X / l, a.Y / l) : Vec2.Zero;
        }
        /// <summary>Rotates +90 degrees (counter-clockwise).</summary>
        public static Vec2 Perp(this Vec2 a) { return new Vec2(-a.Y, a.X); }
        public static Vec2 Rotate(this Vec2 a, float radians)
        {
            float c = (float)Math.Cos(radians), s = (float)Math.Sin(radians);
            return new Vec2(a.X * c - a.Y * s, a.X * s + a.Y * c);
        }
        public static Vec2 ClampLen(this Vec2 a, float max)
        {
            float l = a.Length;
            return l > max && l > 1e-6f ? a * (max / l) : a;
        }
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) { return new Vec2(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }
        public static float DistSq(Vec2 a, Vec2 b) { float x = a.X - b.X, y = a.Y - b.Y; return x * x + y * y; }
        /// <summary>Unsigned angle in radians between two direction vectors (0 if either is degenerate).</summary>
        public static float AngleBetween(Vec2 a, Vec2 b)
        {
            Vec2 na = a.Norm(), nb = b.Norm();
            if (na.LenSq() < 0.5f || nb.LenSq() < 0.5f) return 0f;
            float d = Clamp(na.Dot(nb), -1f, 1f);
            return (float)Math.Acos(d);
        }
        /// <summary>Rotates <paramref name="from"/> toward <paramref name="to"/> (both unit) by at most maxRadians.</summary>
        public static Vec2 RotateToward(Vec2 from, Vec2 to, float maxRadians)
        {
            if (to.LenSq() < 0.5f) return from;
            float ang = (float)Math.Atan2(from.Cross(to), from.Dot(to));
            if (Math.Abs(ang) <= maxRadians) return to;
            return from.Rotate(Math.Sign(ang) * maxRadians).Norm();
        }
        public static Vec2 Dir(Vec2 from, Vec2 to) { return (to - from).Norm(); }
        public static Vec2 Add(this Vec2 a, float x, float y) { return new Vec2(a.X + x, a.Y + y); }

        /// <summary>Closest point on segment ab to p.</summary>
        public static Vec2 ClosestOnSegment(Vec2 a, Vec2 b, Vec2 p, out float t)
        {
            Vec2 ab = b - a;
            float l2 = ab.LenSq();
            if (l2 < 1e-8f) { t = 0f; return a; }
            t = Clamp01((p - a).Dot(ab) / l2);
            return a + ab * t;
        }
    }
}
namespace PixFootball.Sim
{
    /// <summary>List.Sort is an unstable introsort: ties (mirrored slots at equal X, equal costs) could order differently between runtimes. This sort is stable, so assignments are deterministic everywhere.</summary>
    public static class StableSortExtensions
    {
        public static void SortStable<T>(this System.Collections.Generic.List<T> list, System.Comparison<T> cmp)
        {
            for (int i = 1; i < list.Count; i++)
            {
                T x = list[i]; int j = i - 1;
                while (j >= 0 && cmp(list[j], x) > 0) { list[j + 1] = list[j]; j--; }
                list[j + 1] = x;
            }
        }
    }
}
