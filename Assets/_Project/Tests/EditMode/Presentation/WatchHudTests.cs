using NUnit.Framework;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.11 WATCH+HUD: the receiver input lock (note 9), the watch-mode defaults, the helper bar modes and the HUD metrics.</summary>
    public class WatchHudTests
    {
        // ------------------------------------------------------------------------------------------ receiver input lock
        [Test]
        public void ReceiverIsLockedWhileThePassIsInTheAir()
        {
            // an offensive receiver (slot 5) the user is "on" (the intended target) without the ball: locked
            Assert.IsTrue(ReceiverInputLock.IsLocked(offense: true, scrimmage: true, slot: 5, hasBall: false));
            Assert.IsFalse(ReceiverInputLock.Allow(true, true, 5, false));
        }

        [Test]
        public void ReceiverGetsControlOnlyWhenTheBallIsInHisHands()
        {
            Assert.IsFalse(ReceiverInputLock.IsLocked(true, true, 5, true), "ball secured in hand: the user steers him");
            Assert.IsTrue(ReceiverInputLock.IsLocked(true, true, 5, false), "one frame earlier (ball still in the air or in the catch) he is locked");
        }

        [Test]
        public void QuarterbackAlwaysKeepsHisControls()
        {
            Assert.IsFalse(ReceiverInputLock.IsLocked(true, true, 0, true), "QB with the ball");
            Assert.IsFalse(ReceiverInputLock.IsLocked(true, true, 0, false), "QB after the hand-off / before the snap");
        }

        [Test]
        public void DefendersAndSpecialTeamsAreNeverLocked()
        {
            Assert.IsFalse(ReceiverInputLock.IsLocked(false, true, 3, false), "the user's defender steers freely");
            Assert.IsFalse(ReceiverInputLock.IsLocked(true, false, 3, false), "a kickoff returner is not a receiver on a pass");
            Assert.IsFalse(ReceiverInputLock.IsLocked(false, false, 3, false));
        }

        [Test]
        public void LockIsAFunctionOfPositionAndBallOnly()
        {
            // every offensive slot 1..10, every ball state: locked exactly when he has no ball
            for (int slot = 1; slot < 11; slot++)
                foreach (bool ball in new[] { false, true })
                    Assert.AreEqual(!ball, ReceiverInputLock.IsLocked(true, true, slot, ball), "slot " + slot + " ball " + ball);
        }

        // ------------------------------------------------------------------------------------------ watch mode settings
        [Test]
        public void WatchModeIsTheDefaultAndUserControlIsAFlag()
        {
            bool had = PlayerPrefs.HasKey("pix.userctl"); int old = PlayerPrefs.GetInt("pix.userctl", 0);
            try
            {
                PlayerPrefs.DeleteKey("pix.userctl");
                Assert.IsTrue(WatchMode.Enabled, "a fresh install watches CPU versus CPU");
                Assert.IsFalse(WatchMode.UserControl);
                WatchMode.UserControl = true;
                Assert.IsFalse(WatchMode.Enabled);
                WatchMode.UserControl = false;
                Assert.IsTrue(WatchMode.Enabled);
            }
            finally { if (had) PlayerPrefs.SetInt("pix.userctl", old); else PlayerPrefs.DeleteKey("pix.userctl"); }
        }

        [Test]
        public void HelperToggleGoesOffThenBackToAuto()
        {
            Assert.AreEqual(HelperBarMode.Off, WatchMode.ToggleHelper(HelperBarMode.Auto));
            Assert.AreEqual(HelperBarMode.Off, WatchMode.ToggleHelper(HelperBarMode.On));
            Assert.AreEqual(HelperBarMode.Auto, WatchMode.ToggleHelper(HelperBarMode.Off));
        }

        [Test]
        public void HelperPreferenceIsPersisted()
        {
            var old = WatchMode.Helper;
            try
            {
                WatchMode.SetHelper(HelperBarMode.Off);
                Assert.AreEqual(HelperBarMode.Off, WatchMode.Helper);
                Assert.AreEqual((int)HelperBarMode.Off, PlayerPrefs.GetInt("pix.helperbar", 0), "stored in the player prefs");
                WatchMode.SetHelper(HelperBarMode.On);
                Assert.AreEqual(HelperBarMode.On, WatchMode.Helper);
            }
            finally { WatchMode.Helper = old; }
        }

        [Test]
        public void GameSpeedCyclesBetweenOneAndTwo()
        {
            WatchMode.SpeedIndex = 0;
            Assert.AreEqual(1f, WatchMode.Speed);
            Assert.AreEqual(2f, WatchMode.NextSpeed());
            Assert.AreEqual("2X", WatchMode.SpeedLabel);
            Assert.AreEqual(1f, WatchMode.NextSpeed());
            WatchMode.SpeedIndex = 0;
        }

        // ------------------------------------------------------------------------------------------ HUD layout rules
        [Test]
        public void BigBannerIsForTheEventsThatMatter()
        {
            foreach (var t in new[] { "TOUCHDOWN", "FIELD GOAL IS GOOD", "SAFETY", "INTERCEPTION", "FUMBLE - TURNOVER", "END OF 1ST QUARTER", "HALFTIME", "EXTRA POINT GOOD", "TWO-POINT CONVERSION" })
                Assert.IsTrue(PixFootball.UI.MatchHud.IsBigBanner(t), t);
            foreach (var t in new[] { "FIRST DOWN", "SACK", "INCOMPLETE", "TACKLED BY #52", "NO PENALTY", "TOUCHBACK", "" })
                Assert.IsFalse(PixFootball.UI.MatchHud.IsBigBanner(t), t);
        }

        [Test]
        public void ScoreBarIsACentredBlockOnEveryCanvasWidth()
        {
            // the canvas is at least 1920 wide (Expand scaler); ultrawide grows it sideways, the bar must not follow
            Assert.AreEqual(PixFootball.UI.HudMetrics.BarMaxWidth, PixFootball.UI.HudMetrics.BarWidth(1920f), 0.01f);
            Assert.AreEqual(PixFootball.UI.HudMetrics.BarMaxWidth, PixFootball.UI.HudMetrics.BarWidth(2580f), 0.01f, "21:9");
            Assert.AreEqual(PixFootball.UI.HudMetrics.BarMaxWidth, PixFootball.UI.HudMetrics.BarWidth(3840f), 0.01f);
            Assert.Less(PixFootball.UI.HudMetrics.BarWidth(1700f), 1700f - 2f * PixFootball.UI.HudMetrics.SafeX + 49f, "a narrow canvas keeps the safe margins");
        }

        [Test]
        public void LaneStackingNeverOverlaps()
        {
            float y = -PixFootball.UI.HudMetrics.PopupTop;
            float[] heights = { 116f, 46f, 40f, 48f };
            float prevBottom = float.MaxValue;
            foreach (float h in heights)
            {
                float top = y, bottom = y - h;
                Assert.Less(top, prevBottom, "each piece starts below the previous one");
                prevBottom = bottom;
                y = PixFootball.UI.HudMetrics.NextLaneY(y, h);
            }
        }

        [Test]
        public void HudBoundsOverlapMath()
        {
            var a = new Rect(0, 0, 100, 100); var b = new Rect(50, 50, 100, 100); var c = new Rect(200, 200, 10, 10);
            Assert.AreEqual(2500f, PixFootball.UI.HudBounds.OverlapArea(a, b), 0.01f);
            Assert.AreEqual(0f, PixFootball.UI.HudBounds.OverlapArea(a, c), 0.01f);
            Assert.IsTrue(PixFootball.UI.HudBounds.OutsideSafe(new Rect(0, 0, 100, 100), 1920, 1080), "touching the screen corner is outside the title-safe area");
            Assert.IsFalse(PixFootball.UI.HudBounds.OutsideSafe(new Rect(100, 100, 400, 100), 1920, 1080));
        }

        [Test]
        public void HudTokensAreTheTileHubValues()
        {
            Assert.AreEqual(0xF2 / 255f, PixFootball.UI.HudStyle.Gold.r, 0.002f);
            Assert.AreEqual(0xA6 / 255f, PixFootball.UI.HudStyle.Gold.g, 0.002f);
            Assert.AreEqual(0x3A / 255f, PixFootball.UI.HudStyle.Gold.b, 0.002f);
            Assert.GreaterOrEqual(PixFootball.UI.HudMetrics.MinFont, 20f, "nothing under 20 canvas units");
        }
    }
}
