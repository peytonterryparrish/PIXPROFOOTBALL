using NUnit.Framework;
using PixFootball.EditorTools;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.12.0 GAIT: start / stop / cut lean math (pure) and the cut audit on synthetic series.</summary>
    public class GaitLeanTests
    {
        [Test]
        public void AccelLeanIsAFirstStepLean()
        {
            Assert.That(GaitLean.AccelLean(4f), Is.InRange(25f, 40f), "a hard start (4 m/s2) leans 25-40 degrees");
            Assert.That(GaitLean.AccelLean(6f), Is.InRange(30f, 40f));
            Assert.LessOrEqual(GaitLean.AccelLean(30f), 40f + 1e-4f, "never past 40");
            Assert.AreEqual(0f, GaitLean.AccelLean(0f));
            float prev = 0f; for (float a = 0.5f; a <= 12f; a += 0.5f) { float l = GaitLean.AccelLean(a); Assert.GreaterOrEqual(l, prev); prev = l; }
        }

        [Test]
        public void BrakeLeanSitsBack()
        {
            Assert.Less(GaitLean.BrakeLean(-4f), 0f, "a hard stop leans back");
            Assert.GreaterOrEqual(GaitLean.BrakeLean(-30f), -14f - 1e-4f);
            Assert.Greater(GaitLean.BrakeLean(-1f), GaitLean.BrakeLean(-6f));
        }

        [Test]
        public void DeltaAddsOnlyWhatTheClipLacks()
        {
            Assert.AreEqual(0f, GaitLean.Delta(0f, 14f), 1e-5f, "steady running: the clip is left alone");
            Assert.AreEqual(0f, GaitLean.Delta(0.5f, 14f), 1e-5f, "below the threshold");
            Assert.Greater(GaitLean.Delta(5f, 6f), 15f, "a start out of a jog posture");
            Assert.AreEqual(0f, GaitLean.Delta(5f, 45f), 1e-5f, "a clip that already leans further is not leaned more");
            Assert.Less(GaitLean.Delta(-5f, 14f), -15f, "a stop out of a run posture");
            Assert.AreEqual(0f, GaitLean.Delta(-5f, -30f), 1e-5f);
        }

        [Test]
        public void AlignAndSpeedGates()
        {
            Assert.AreEqual(1f, GaitLean.AlignWeight(0f), 1e-5f);
            Assert.AreEqual(0f, GaitLean.AlignWeight(180f), 1e-5f, "a back-pedal gets no start lean");
            Assert.AreEqual(0f, GaitLean.AlignWeight(90f), 1e-5f, "a side shuffle gets none");
            Assert.AreEqual(0f, GaitLean.SpeedWeight(0f), 1e-5f);
            Assert.AreEqual(1f, GaitLean.SpeedWeight(2f), 1e-5f);
        }

        [Test]
        public void StartGrowsThenStraightens()
        {
            var g = new GaitLean(); float dt = 1f / 60f, v = 0f, peak = 0f, last = 0f;
            for (int i = 0; i < 90; i++)      // 5 m/s2 for 1.3 s (6.5 m/s), then steady
            {
                if (v < 6.5f) v = Mathf.Min(6.5f, v + 5f * dt);
                g.Observe(new Vector3(0f, 0f, v), Vector3.forward, dt);
                last = g.Solve(3f, dt, 1f); peak = Mathf.Max(peak, last);
            }
            Assert.Greater(peak, 18f, "the start adds a clear lean on top of the clip's 3 degrees");
            for (int i = 0; i < 120; i++) { g.Observe(new Vector3(0f, 0f, 6.5f), Vector3.forward, dt); last = g.Solve(3f, dt, 1f); }
            Assert.Less(Mathf.Abs(last), 0.5f, "steady speed: the extra lean is gone");
        }

        [Test]
        public void StopLeansBackAndBackpedalDoesNot()
        {
            var g = new GaitLean(); float dt = 1f / 60f, v = 6f, trough = 0f;
            for (int i = 0; i < 40; i++) { v = Mathf.Max(0.5f, v - 8f * dt); g.Observe(new Vector3(0f, 0f, v), Vector3.forward, dt); trough = Mathf.Min(trough, g.Solve(14f, dt, 1f)); }
            Assert.Less(trough, -15f, "a hard stop takes the clip's 14 degrees to leaning back");
            Assert.Greater(g.SinkM, 0.02f, "and sinks");
            var b = new GaitLean(); v = 3f;
            for (int i = 0; i < 40; i++) { v = Mathf.Max(0.5f, v - 6f * dt); b.Observe(new Vector3(0f, 0f, -v), Vector3.forward, dt); Assert.AreEqual(0f, b.Solve(8f, dt, 1f), 0.5f, "a back-pedal is left alone"); }
        }

        [Test]
        public void NoLayerWithoutWeight()
        {
            var g = new GaitLean(); float dt = 1f / 60f, v = 0f;
            for (int i = 0; i < 60; i++) { v = Mathf.Min(7f, v + 6f * dt); g.Observe(new Vector3(0f, 0f, v), Vector3.forward, dt); Assert.AreEqual(0f, g.Solve(0f, dt, 0f), 1e-4f, "weight 0 (quarterback, action owns the body)"); }
        }

        [Test]
        public void CutSeverityNeedsSpeedAndTurn()
        {
            Assert.AreEqual(0f, GaitLean.CutSeverity(0f, 7f), 1e-5f);
            Assert.AreEqual(0f, GaitLean.CutSeverity(400f, 1f), 1e-5f, "a pivot at a walk is no cut");
            Assert.AreEqual(1f, GaitLean.CutSeverity(400f, 6f), 1e-5f);
            Assert.AreEqual(GaitLean.CutSeverity(300f, 6f), GaitLean.CutSeverity(-300f, 6f), 1e-5f, "left and right alike");
            Assert.Greater(GaitLean.Sink(0f, 0f, 1f), GaitLean.Sink(0f, 0f, 0.2f));
            Assert.LessOrEqual(GaitLean.Sink(1f, 1f, 1f), 0.07f);
        }

        // ---- cut audit on a synthetic runner: 6 m/s along +z, a 90 degree turn to +x at frame 120; feet alternate (planted 12 frames each)
        static CutProbe.Fr Frame(int i, bool slide)
        {
            bool turned = i >= 120;
            var f = new CutProbe.Fr { Valid = true, Speed = 6f, Vel = turned ? new Vector2(6f, 0f) : new Vector2(0f, 6f), HipsY = 0.95f };
            int ph = (i / 12) % 2;      // 0 = left planted
            f.SoleL = ph == 0 ? 0.0f : 0.10f; f.SoleR = ph == 1 ? 0.0f : 0.10f; f.FlagL = ph == 0; f.FlagR = ph == 1;
            float x = slide ? i * 0.01f : 0f;      // a planted foot that slides 1 cm per frame
            f.AL = new Vector3(x, 0.1f, 0f); f.AR = new Vector3(0.3f + x, 0.1f, 0f); f.TL = new Vector3(x, 0.02f, 0.2f); f.TR = new Vector3(0.3f + x, 0.02f, 0.2f);
            return f;
        }

        [Test]
        public void CutAuditFindsAPlantedFootAndAHipDrop()
        {
            CutProbe.Reset();
            for (int i = 0; i < 240; i++) { var f = Frame(i, false); if (i >= 114 && i <= 126) f.HipsY = 0.88f; CutProbe.Feed(3, "WR", false, f); }
            CutProbe.EndPlay();
            string r = CutProbe.Report();
            StringAssert.Contains("1 direction changes", r);
            StringAssert.Contains("planted foot (drift < 4 cm) 100 %", r);
            StringAssert.Contains("hip drop median 7.0 cm", r);
            CutProbe.Reset();
        }

        [Test]
        public void CutAuditFlagsASlidingFoot()
        {
            CutProbe.Reset();
            for (int i = 0; i < 240; i++) CutProbe.Feed(3, "WR", false, Frame(i, true));
            CutProbe.EndPlay();
            StringAssert.Contains("planted foot (drift < 4 cm) 0 %", CutProbe.Report());
            CutProbe.Reset();
        }
    }
}
