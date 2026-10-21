using System;

namespace PixFootball.Core
{
    /// <summary>Minimal engine-free 2D vector. X = lateral yards (+ = offense right), Y = downfield yards.</summary>
    public readonly struct Vec2 : IEquatable<Vec2>
    {
        public readonly float X;
        public readonly float Y;
        public Vec2(float x, float y) { X = x; Y = y; }
        public static readonly Vec2 Zero = new Vec2(0f, 0f);
        public float Length { get { return (float)Math.Sqrt(X * X + Y * Y); } }
        public static Vec2 operator +(Vec2 a, Vec2 b) { return new Vec2(a.X + b.X, a.Y + b.Y); }
        public static Vec2 operator -(Vec2 a, Vec2 b) { return new Vec2(a.X - b.X, a.Y - b.Y); }
        public static Vec2 operator *(Vec2 a, float s) { return new Vec2(a.X * s, a.Y * s); }
        public static float Distance(Vec2 a, Vec2 b) { return (a - b).Length; }
        public Vec2 MirrorX() { return new Vec2(-X, Y); }
        public bool Equals(Vec2 o) { return X == o.X && Y == o.Y; }
        public override bool Equals(object obj) { return obj is Vec2 v && Equals(v); }
        public override int GetHashCode() { unchecked { return (X.GetHashCode() * 397) ^ Y.GetHashCode(); } }
        public override string ToString() { return "(" + X.ToString("0.##") + "," + Y.ToString("0.##") + ")"; }
    }
}
