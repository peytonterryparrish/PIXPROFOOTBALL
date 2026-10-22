using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Sim;

namespace PixFootball.Tests
{
    /// <summary>v0.7.0 Track A: the read-only hooks the animation layer uses and the animation-synced quarterback wind-up.</summary>
    public class QbAnimHooksTests
    {
        static float ThrowDelay(float windup)
        {
            PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 801, null, null, new PlaySimOptions { QbHoldsBall = true, Penalties = false, QbWindupSeconds = windup });
            SimTestKit.StepToLive(sim);
            while (sim.Time < 1.6f) sim.Tick(1f / 60f, default(ControlInput), 0);
            int slot = 1; for (int k = 1; k < 11; k++) if (sim.Agents[k].IsReceiverEligible) { slot = k; break; }
            float t0 = sim.Time;
            sim.Tick(1f / 60f, new ControlInput { ThrowToSlot = slot }, 0);
            for (int i = 0; i < 120 && sim.Phase == PlayPhase.Live; i++) sim.Tick(1f / 60f, default(ControlInput), 0);
            var th = sim.Log.First(SimEventType.Throw);
            Assert.IsTrue(th.HasValue, "the user's throw happened");
            return th.Value.Time - t0;
        }

        [Test]
        public void TheDefaultWindupIsUnchangedAndTheConfiguredOneIsHonoured()
        {
            Assert.Less(ThrowDelay(0f), 0.36f, "the calibrated 0.20 - 0.26 s wind-up stays the default");
            float d = ThrowDelay(0.42f);
            Assert.That(d, Is.InRange(0.37f, 0.50f), "a configured wind-up of 0.42 s: the release comes that long after the throw starts (animation sync)");
        }

        [Test]
        public void TheDropBackPointAndStepsAreExposed()
        {
            PlaySimulation sim = SimTestKit.RunNamed("Four Verticals", "Shotgun", "Cover 3 Sky", "4-3 Base", 75, 75, 5, null, null, new PlaySimOptions { Penalties = false, AutoSnap = false });
            SimTestKit.Steps(sim, 5);
            Assert.Less(sim.QbDropPoint.Y, sim.LosY, "the quarterback drops behind the line of scrimmage");
            Assert.GreaterOrEqual(sim.QbDropSteps, 0);
            Assert.IsFalse(sim.QbPlanIsHandoff);
        }

        [Test]
        public void ADesignedHandoffNamesItsRunner()
        {
            PlaySimulation sim = SimTestKit.RunNamed("Inside Zone Right", null, "Cover 3 Sky", "4-3 Base", 75, 75, 5, null, null, new PlaySimOptions { Penalties = false, AutoSnap = false });
            SimTestKit.Steps(sim, 5);
            Assert.IsTrue(sim.QbPlanIsHandoff);
            Assert.Greater(sim.QbHandoffTargetId, 0);
            Assert.Less(sim.QbHandoffTargetId, 11);
        }
    }
}
