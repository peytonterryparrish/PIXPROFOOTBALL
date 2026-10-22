using UnityEngine;

namespace PixFootball.Gameplay
{
    /// <summary>v0.9.0 LOCOMOTION (LOCO-007): the pure math of the gait synthesizer (unit tested): which cycles bracket a speed and how they cross-fade, and the piecewise-linear gait phase map that keeps blended cycles on the same foot.</summary>
    public static class GaitMath
    {
        /// <summary>Within +-12 percent of a cycle's native speed only that cycle plays (its playback rate absorbs the difference).</summary>
        public const float RateBand = 0.12f;

        /// <summary>Neighbouring cycles around speed <paramref name="v"/> of an ascending speed ladder: returns the lower index and the weight (0..1) of the upper one. Below the first or above the last cycle the end cycle plays alone.</summary>
        public static void Bracket(float[] speeds, float v, out int lower, out float upperWeight)
        {
            int n = speeds.Length; lower = 0; upperWeight = 0f;
            if (n <= 1 || v <= speeds[0]) return;
            if (v >= speeds[n - 1]) { lower = n - 1; return; }
            int i = 0; while (i < n - 2 && v >= speeds[i + 1]) i++;
            lower = i;
            upperWeight = PairWeight(speeds[i], speeds[i + 1], v);
        }

        /// <summary>Cross-fade weight of the faster of two neighbouring cycles (native speeds sa below sb) at speed v: 0 inside the rate band of the slower one, 1 inside the band of the faster one.</summary>
        public static float PairWeight(float sa, float sb, float v)
        {
            float lo = sa * (1f + RateBand), hi = sb * (1f - RateBand);
            float t = lo >= hi ? Mathf.InverseLerp(sa, sb, v) : Mathf.Clamp01(Mathf.InverseLerp(lo, hi, v));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Maps the shared gait phase to a cycle's own phase: the shared knots (0, k1, k2, k3, 1) are the left lift-off, right plant and right lift-off of the blend, the cycle knots (0, c1, c2, c3, 1) are the same events of one cycle.</summary>
        public static float MapPhase(float phi, float k1, float k2, float k3, float c1, float c2, float c3)
        {
            if (phi < k1) return c1 * (phi / Mathf.Max(1e-4f, k1));
            if (phi < k2) return Mathf.Lerp(c1, c2, (phi - k1) / Mathf.Max(1e-4f, k2 - k1));
            if (phi < k3) return Mathf.Lerp(c2, c3, (phi - k2) / Mathf.Max(1e-4f, k3 - k2));
            return Mathf.Lerp(c3, 1f, (phi - k3) / Mathf.Max(1e-4f, 1f - k3));
        }

        /// <summary>The playback rate of a cycle mixture: target speed over the mixture's native speed, kept inside the rate band (wider only beyond the ladder, where no neighbour exists); the rest is the stride warp factor.</summary>
        public static void RateAndWarp(float v, float mixSpeed, bool lowEnd, bool highEnd, out float rate, out float warp)
        {
            float ratio = v / Mathf.Max(0.2f, mixSpeed);
            float lo = lowEnd ? 0.45f : 1f - RateBand, hi = highEnd ? 1.3f : 1f + RateBand;
            rate = Mathf.Clamp(ratio, lo, hi);
            warp = Mathf.Clamp(ratio / rate, 0.7f, 1.45f);
        }
    }
}
