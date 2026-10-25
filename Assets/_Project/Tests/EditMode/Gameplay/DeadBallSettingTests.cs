using NUnit.Framework;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.12.0 (RULES): the dead-ball time (whistle to snap) is a persisted setting, default 25 s (it was about 16).</summary>
    public class DeadBallSettingTests
    {
        [Test]
        public void TheDeadBallTimeDefaultsToTwentyFiveSecondsAndRoundTrips()
        {
            int before = PixSettings.DeadBallSeconds;
            try
            {
                PlayerPrefs.DeleteKey("pix.deadball");
                Assert.AreEqual(25, PixSettings.DeadBallSeconds, "default");
                Assert.AreEqual(2, PixSettings.DeadBallIndex, "the row shows Standard");
                for (int i = 0; i < PixSettings.DeadBallChoices.Length; i++)
                {
                    PixSettings.DeadBallIndex = i;
                    Assert.AreEqual(PixSettings.DeadBallChoices[i], PixSettings.DeadBallSeconds);
                    Assert.AreEqual(i, PixSettings.DeadBallIndex);
                }
                Assert.AreEqual(PixSettings.DeadBallChoices.Length, PixSettings.DeadBallNames.Length);
                PixSettings.DeadBallSeconds = 3; Assert.AreEqual(12, PixSettings.DeadBallSeconds, "clamped");
                PixSettings.DeadBallSeconds = 400; Assert.AreEqual(45, PixSettings.DeadBallSeconds, "clamped");
            }
            finally { PixSettings.DeadBallSeconds = before; }
        }
    }
}
