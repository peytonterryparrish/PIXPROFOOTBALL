using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    public enum RatingKind
    {
        Speed, Acceleration, Agility, Strength, Awareness, Catching, Carrying,
        ThrowPower, ThrowAccuracy, Tackling, BlockShedding, RunBlocking, PassBlocking,
        ManCoverage, ZoneCoverage, Jumping, Stamina, Injury, KickPower, KickAccuracy,
        BreakTackle, Elusiveness
    }

    /// <summary>Player attributes, each 0-99.</summary>
    [Serializable]
    public struct Ratings
    {
        public int Speed, Acceleration, Agility, Strength, Awareness, Catching, Carrying;
        public int ThrowPower, ThrowAccuracy, Tackling, BlockShedding, RunBlocking, PassBlocking;
        public int ManCoverage, ZoneCoverage, Jumping, Stamina, Injury, KickPower, KickAccuracy;
        public int BreakTackle, Elusiveness;

        public const int Min = 0;
        public const int Max = 99;
        public static readonly int KindCount = Enum.GetValues(typeof(RatingKind)).Length;

        public static Ratings Uniform(int value)
        {
            var r = new Ratings();
            for (int i = 0; i < KindCount; i++) r.Set((RatingKind)i, value);
            return r;
        }

        public int Get(RatingKind k)
        {
            switch (k)
            {
                case RatingKind.Speed: return Speed;
                case RatingKind.Acceleration: return Acceleration;
                case RatingKind.Agility: return Agility;
                case RatingKind.Strength: return Strength;
                case RatingKind.Awareness: return Awareness;
                case RatingKind.Catching: return Catching;
                case RatingKind.Carrying: return Carrying;
                case RatingKind.ThrowPower: return ThrowPower;
                case RatingKind.ThrowAccuracy: return ThrowAccuracy;
                case RatingKind.Tackling: return Tackling;
                case RatingKind.BlockShedding: return BlockShedding;
                case RatingKind.RunBlocking: return RunBlocking;
                case RatingKind.PassBlocking: return PassBlocking;
                case RatingKind.ManCoverage: return ManCoverage;
                case RatingKind.ZoneCoverage: return ZoneCoverage;
                case RatingKind.Jumping: return Jumping;
                case RatingKind.Stamina: return Stamina;
                case RatingKind.Injury: return Injury;
                case RatingKind.KickPower: return KickPower;
                case RatingKind.KickAccuracy: return KickAccuracy;
                case RatingKind.BreakTackle: return BreakTackle;
                case RatingKind.Elusiveness: return Elusiveness;
                default: return 0;
            }
        }

        public void Set(RatingKind k, int v)
        {
            v = Clamp(v);
            switch (k)
            {
                case RatingKind.Speed: Speed = v; break;
                case RatingKind.Acceleration: Acceleration = v; break;
                case RatingKind.Agility: Agility = v; break;
                case RatingKind.Strength: Strength = v; break;
                case RatingKind.Awareness: Awareness = v; break;
                case RatingKind.Catching: Catching = v; break;
                case RatingKind.Carrying: Carrying = v; break;
                case RatingKind.ThrowPower: ThrowPower = v; break;
                case RatingKind.ThrowAccuracy: ThrowAccuracy = v; break;
                case RatingKind.Tackling: Tackling = v; break;
                case RatingKind.BlockShedding: BlockShedding = v; break;
                case RatingKind.RunBlocking: RunBlocking = v; break;
                case RatingKind.PassBlocking: PassBlocking = v; break;
                case RatingKind.ManCoverage: ManCoverage = v; break;
                case RatingKind.ZoneCoverage: ZoneCoverage = v; break;
                case RatingKind.Jumping: Jumping = v; break;
                case RatingKind.Stamina: Stamina = v; break;
                case RatingKind.Injury: Injury = v; break;
                case RatingKind.KickPower: KickPower = v; break;
                case RatingKind.KickAccuracy: KickAccuracy = v; break;
                case RatingKind.BreakTackle: BreakTackle = v; break;
                case RatingKind.Elusiveness: Elusiveness = v; break;
            }
        }

        public static int Clamp(int v) { return v < Min ? Min : (v > Max ? Max : v); }

        public Ratings Clamped()
        {
            var r = this;
            for (int i = 0; i < KindCount; i++) r.Set((RatingKind)i, Get((RatingKind)i));
            return r;
        }

        /// <summary>Position-weighted overall rating, 0-99.</summary>
        public int Overall(Position position)
        {
            RatingWeight[] w = OverallWeights.For(position);
            float sum = 0f, total = 0f;
            for (int i = 0; i < w.Length; i++)
            {
                sum += Clamp(Get(w[i].Kind)) * w[i].Weight;
                total += w[i].Weight;
            }
            if (total <= 0f) return 0;
            return Clamp((int)Math.Round(sum / total));
        }
    }

    public readonly struct RatingWeight
    {
        public readonly RatingKind Kind;
        public readonly float Weight;
        public RatingWeight(RatingKind kind, float weight) { Kind = kind; Weight = weight; }
    }

    /// <summary>Per-position weightings used by <see cref="Ratings.Overall"/>.</summary>
    public static class OverallWeights
    {
        private static readonly Dictionary<Position, RatingWeight[]> Table = Build();

        public static RatingWeight[] For(Position p) { return Table[p]; }

        private static RatingWeight W(RatingKind k, float w) { return new RatingWeight(k, w); }

        private static Dictionary<Position, RatingWeight[]> Build()
        {
            var d = new Dictionary<Position, RatingWeight[]>();
            d[Position.QB] = new[] {
                W(RatingKind.ThrowAccuracy, 30), W(RatingKind.ThrowPower, 20), W(RatingKind.Awareness, 22),
                W(RatingKind.Speed, 4), W(RatingKind.Agility, 4), W(RatingKind.Acceleration, 3),
                W(RatingKind.Elusiveness, 3), W(RatingKind.Carrying, 3), W(RatingKind.Strength, 2), W(RatingKind.Stamina, 2) };
            d[Position.RB] = new[] {
                W(RatingKind.Speed, 17), W(RatingKind.Acceleration, 12), W(RatingKind.Agility, 11),
                W(RatingKind.Carrying, 12), W(RatingKind.BreakTackle, 14), W(RatingKind.Elusiveness, 14),
                W(RatingKind.Strength, 5), W(RatingKind.Awareness, 6), W(RatingKind.Catching, 6), W(RatingKind.Stamina, 3) };
            d[Position.FB] = new[] {
                W(RatingKind.Strength, 15), W(RatingKind.RunBlocking, 20), W(RatingKind.PassBlocking, 10),
                W(RatingKind.Carrying, 10), W(RatingKind.BreakTackle, 12), W(RatingKind.Awareness, 8),
                W(RatingKind.Speed, 6), W(RatingKind.Catching, 6), W(RatingKind.Stamina, 3) };
            d[Position.WR] = new[] {
                W(RatingKind.Catching, 22), W(RatingKind.Speed, 18), W(RatingKind.Acceleration, 10),
                W(RatingKind.Agility, 10), W(RatingKind.Jumping, 8), W(RatingKind.Awareness, 8),
                W(RatingKind.Elusiveness, 8), W(RatingKind.Carrying, 4), W(RatingKind.BreakTackle, 3), W(RatingKind.Stamina, 3) };
            d[Position.TE] = new[] {
                W(RatingKind.Catching, 14), W(RatingKind.RunBlocking, 14), W(RatingKind.PassBlocking, 8),
                W(RatingKind.Strength, 10), W(RatingKind.Speed, 10), W(RatingKind.Awareness, 8),
                W(RatingKind.Agility, 5), W(RatingKind.Jumping, 3), W(RatingKind.Carrying, 3), W(RatingKind.BreakTackle, 4), W(RatingKind.Stamina, 3) };
            d[Position.LT] = Tackle();
            d[Position.RT] = Tackle();
            d[Position.LG] = Guard();
            d[Position.RG] = Guard();
            d[Position.C] = new[] {
                W(RatingKind.Awareness, 20), W(RatingKind.RunBlocking, 22), W(RatingKind.PassBlocking, 22),
                W(RatingKind.Strength, 20), W(RatingKind.Agility, 6), W(RatingKind.Stamina, 4), W(RatingKind.Speed, 2) };
            d[Position.LE] = Edge();
            d[Position.RE] = Edge();
            d[Position.DT] = new[] {
                W(RatingKind.Strength, 24), W(RatingKind.BlockShedding, 24), W(RatingKind.Tackling, 14),
                W(RatingKind.Awareness, 10), W(RatingKind.Acceleration, 8), W(RatingKind.Speed, 5),
                W(RatingKind.Agility, 5), W(RatingKind.Stamina, 4), W(RatingKind.Jumping, 2) };
            var olb = new[] {
                W(RatingKind.Tackling, 18), W(RatingKind.Speed, 14), W(RatingKind.BlockShedding, 12),
                W(RatingKind.Awareness, 14), W(RatingKind.Acceleration, 10), W(RatingKind.Strength, 10),
                W(RatingKind.ZoneCoverage, 8), W(RatingKind.ManCoverage, 6), W(RatingKind.Agility, 5), W(RatingKind.Stamina, 3) };
            d[Position.LOLB] = olb;
            d[Position.ROLB] = olb;
            d[Position.MLB] = new[] {
                W(RatingKind.Tackling, 22), W(RatingKind.Awareness, 22), W(RatingKind.BlockShedding, 12),
                W(RatingKind.Strength, 10), W(RatingKind.Speed, 10), W(RatingKind.ZoneCoverage, 10),
                W(RatingKind.Acceleration, 6), W(RatingKind.ManCoverage, 4), W(RatingKind.Agility, 4) };
            d[Position.CB] = new[] {
                W(RatingKind.ManCoverage, 24), W(RatingKind.Speed, 20), W(RatingKind.ZoneCoverage, 14),
                W(RatingKind.Acceleration, 10), W(RatingKind.Agility, 10), W(RatingKind.Awareness, 8),
                W(RatingKind.Jumping, 6), W(RatingKind.Catching, 4), W(RatingKind.Tackling, 4) };
            d[Position.FS] = new[] {
                W(RatingKind.ZoneCoverage, 22), W(RatingKind.Awareness, 18), W(RatingKind.Speed, 14),
                W(RatingKind.Tackling, 10), W(RatingKind.ManCoverage, 10), W(RatingKind.Acceleration, 8),
                W(RatingKind.Catching, 6), W(RatingKind.Jumping, 6), W(RatingKind.Agility, 6) };
            d[Position.SS] = new[] {
                W(RatingKind.Tackling, 18), W(RatingKind.ZoneCoverage, 16), W(RatingKind.Awareness, 14),
                W(RatingKind.Speed, 12), W(RatingKind.ManCoverage, 10), W(RatingKind.Strength, 8),
                W(RatingKind.BlockShedding, 6), W(RatingKind.Acceleration, 8), W(RatingKind.Jumping, 4), W(RatingKind.Agility, 4) };
            d[Position.K] = new[] { W(RatingKind.KickPower, 50), W(RatingKind.KickAccuracy, 50), W(RatingKind.Awareness, 4) };
            d[Position.P] = new[] { W(RatingKind.KickPower, 45), W(RatingKind.KickAccuracy, 45), W(RatingKind.Awareness, 10) };
            return d;
        }

        private static RatingWeight[] Tackle()
        {
            return new[] {
                W(RatingKind.PassBlocking, 28), W(RatingKind.RunBlocking, 20), W(RatingKind.Strength, 20),
                W(RatingKind.Awareness, 12), W(RatingKind.Agility, 10), W(RatingKind.Speed, 3), W(RatingKind.Stamina, 4), W(RatingKind.Acceleration, 3) };
        }

        private static RatingWeight[] Guard()
        {
            return new[] {
                W(RatingKind.RunBlocking, 28), W(RatingKind.PassBlocking, 20), W(RatingKind.Strength, 24),
                W(RatingKind.Awareness, 12), W(RatingKind.Agility, 6), W(RatingKind.Stamina, 4), W(RatingKind.Acceleration, 3), W(RatingKind.Speed, 3) };
        }

        private static RatingWeight[] Edge()
        {
            return new[] {
                W(RatingKind.BlockShedding, 22), W(RatingKind.Strength, 16), W(RatingKind.Tackling, 14),
                W(RatingKind.Speed, 12), W(RatingKind.Acceleration, 12), W(RatingKind.Awareness, 8),
                W(RatingKind.Agility, 6), W(RatingKind.Stamina, 4), W(RatingKind.Jumping, 3), W(RatingKind.BreakTackle, 3) };
        }
    }
}
