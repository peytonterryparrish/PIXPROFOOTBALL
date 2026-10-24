using NUnit.Framework;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.11.0 QB (playtest notes 8 / 10): the keyed throw opens the hips before the shoulders; the stance in motion keeps the ball at the chest.</summary>
    public class QbStanceV11PoseTests
    {
        [Test]
        public void ThrowPoseOpensTheHipsBeforeTheShouldersAndFinishesAcrossTheBody()
        {
            var a = StancePoses.QbThrow11(0.45f, false); var b = StancePoses.QbThrow11(0.7f, false); var c = StancePoses.QbThrow11(1.0f, false); var d = StancePoses.QbThrow11(1.5f, false);
            Assert.Greater(a.Tilt, 20f, "side-on at the load");
            Assert.Greater(a.UpperYaw, 5f, "the shoulders are closed further than the hips");
            float hipsOpened = (a.Tilt - b.Tilt) / a.Tilt, shouldersOpened = ((a.Tilt + a.UpperYaw) - (b.Tilt + b.UpperYaw)) / (a.Tilt + a.UpperYaw);
            Assert.Greater(hipsOpened, shouldersOpened, "the hips open first");
            Assert.AreEqual(0f, c.Tilt + c.UpperYaw, 1f, "square at the release");
            Assert.Less(d.RPos.x, 0f, "the throwing hand finishes across the body");
            Assert.Greater(c.RHeel, 0.05f, "the back heel is up at the release");
        }

        [Test]
        public void ThrowOnTheRunKeepsTheTurnSmall()
        {
            var run = StancePoses.QbThrow11(0.5f, true); var set = StancePoses.QbThrow11(0.5f, false);
            Assert.Less(Mathf.Abs(run.Tilt), Mathf.Abs(set.Tilt) * 0.5f);
        }

        [Test]
        public void PocketPoseWhileMovingKeepsTheBallAtTheChestAndLeansIntoTheTravel()
        {
            var still = StancePoses.QbPocketLive(0.3f, 0f); var moving = StancePoses.QbPocketLive(0.3f, 4.5f);
            Assert.Greater(moving.Lean, still.Lean);
            Assert.AreEqual(HandMode.Chest, moving.RHand); Assert.AreEqual(HandMode.Chest, moving.LHand);
            Assert.Greater(moving.RPos.z, 0.15f, "ball out in front of the chest, not at the ear");
            Assert.Less(moving.RPos.y, 0.15f);
        }
    }
}
