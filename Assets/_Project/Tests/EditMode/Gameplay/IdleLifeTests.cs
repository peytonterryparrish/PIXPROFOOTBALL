using NUnit.Framework;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.13.0 NATURAL-2: the floor of life for bodies that stand still (<see cref="IdleLife"/>).</summary>
    public class IdleLifeTests
    {
        [Test]
        public void ThePoseWantsLifeOnlyWhileItIsStill()
        {
            Assert.AreEqual(1f, IdleLife.WantFor(0f), 1e-5f);
            Assert.AreEqual(1f, IdleLife.WantFor(IdleLife.StillBelow), 1e-5f);
            Assert.AreEqual(0f, IdleLife.WantFor(IdleLife.MovingAbove), 1e-5f);
            Assert.AreEqual(0f, IdleLife.WantFor(300f), 1e-5f);
            float prev = 1f;
            for (float a = 0f; a < 30f; a += 0.5f) { float w = IdleLife.WantFor(a); Assert.LessOrEqual(w, prev + 1e-6f, "monotone"); prev = w; }
        }

        [Test]
        public void TheWaveStaysInRangeAndNeverStops()
        {
            float lo = 9f, hi = -9f, maxStep = 0f, prev = IdleLife.Wave(0f, 0.2f, 0.3f);
            for (float t = 0f; t < 120f; t += 1f / 60f)
            {
                float w = IdleLife.Wave(t, 0.2f, 0.3f); lo = Mathf.Min(lo, w); hi = Mathf.Max(hi, w); maxStep = Mathf.Max(maxStep, Mathf.Abs(w - prev)); prev = w;
            }
            Assert.GreaterOrEqual(lo, -1.001f); Assert.LessOrEqual(hi, 1.001f);
            Assert.Greater(hi - lo, 1.2f, "it uses its range");
            Assert.Less(maxStep, 0.05f, "no jumps between frames");
        }

        [Test]
        public void ABodyWithoutABoneSetIsNotUsed()
        {
            var il = new IdleLife(null);
            Assert.IsFalse(il.Ok);
            il.Late(0.016f, true, true);      // no exception, nothing to write
        }
    }
}
