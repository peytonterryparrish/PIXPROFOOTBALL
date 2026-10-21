using System;
using System.Collections.Generic;

namespace PixFootball.Core
{
    /// <summary>
    /// Deterministic xorshift128+ generator. Identical sequences on every platform (no System.Random).
    /// </summary>
    public sealed class Rng
    {
        private ulong _s0, _s1;
        private double _spare;
        private bool _hasSpare;

        public Rng(ulong seed) { Seed(seed); }
        public Rng(int seed) : this(unchecked((ulong)(uint)seed)) { }

        public void Seed(ulong seed)
        {
            ulong x = seed;
            _s0 = SplitMix(ref x);
            _s1 = SplitMix(ref x);
            if (_s0 == 0UL && _s1 == 0UL) _s1 = 0x9E3779B97F4A7C15UL;
            _hasSpare = false;
        }

        private static ulong SplitMix(ref ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                ulong z = x;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        public ulong NextULong()
        {
            unchecked
            {
                ulong x = _s0;
                ulong y = _s1;
                _s0 = y;
                x ^= x << 23;
                _s1 = x ^ y ^ (x >> 17) ^ (y >> 26);
                return _s1 + y;
            }
        }

        public uint NextUInt() { return (uint)(NextULong() >> 32); }

        /// <summary>Uniform float in [0,1).</summary>
        public float NextFloat() { return (NextUInt() >> 8) * (1f / 16777216f); }

        /// <summary>Uniform double in [0,1).</summary>
        public double NextDouble() { return (NextULong() >> 11) * (1.0 / 9007199254740992.0); }

        /// <summary>Integer in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            ulong span = (ulong)((long)maxExclusive - min);
            return (int)(min + (long)((NextULong() >> 1) % span));
        }

        public float Range(float min, float max) { return min + (max - min) * NextFloat(); }

        /// <summary>Normally distributed value (Marsaglia polar method).</summary>
        public float Gaussian(float mean = 0f, float sd = 1f)
        {
            if (_hasSpare)
            {
                _hasSpare = false;
                return (float)(mean + sd * _spare);
            }
            double u, v, s;
            do
            {
                u = NextDouble() * 2.0 - 1.0;
                v = NextDouble() * 2.0 - 1.0;
                s = u * u + v * v;
            } while (s >= 1.0 || s == 0.0);
            double m = Math.Sqrt(-2.0 * Math.Log(s) / s);
            _spare = v * m;
            _hasSpare = true;
            return (float)(mean + sd * (u * m));
        }

        public bool Chance(float p)
        {
            if (p <= 0f) return false;
            if (p >= 1f) return true;
            return NextFloat() < p;
        }

        public T Pick<T>(IList<T> list)
        {
            if (list == null || list.Count == 0) throw new ArgumentException("Cannot pick from an empty list.");
            return list[Range(0, list.Count)];
        }

        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                T t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }

        /// <summary>Independent sub-stream; advances this generator by two draws.</summary>
        public Rng Fork()
        {
            ulong a = NextULong();
            ulong b = NextULong();
            return new Rng(unchecked(a ^ (b * 0x9E3779B97F4A7C15UL) ^ 0xD1B54A32D192ED03UL));
        }

        public Rng Clone()
        {
            var r = new Rng(0UL);
            r._s0 = _s0; r._s1 = _s1; r._spare = _spare; r._hasSpare = _hasSpare;
            return r;
        }

        public void GetState(out ulong s0, out ulong s1) { s0 = _s0; s1 = _s1; }
        public void SetState(ulong s0, ulong s1) { _s0 = s0; _s1 = s1; _hasSpare = false; }
    }
}
