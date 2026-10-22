using NUnit.Framework;
using PixFootball.Core;
using PixFootball.Gameplay;
using UnityEngine;

namespace PixFootball.Tests
{
    /// <summary>v0.9.0 GFX track: player level of detail by projected size, kick camera framings, the dirt wipe, the weather grade defaults and the tier lights (pure logic, no scene).</summary>
    public class GfxTests
    {
        // ------------------------------------------------------------------------------------------ GFX-404 / 405 / 406: the LOD follows the projected height
        [Test]
        public void Lod_ClassicCameraShowsLod0AroundTheBall()
        {
            float bias = PlayerLod.Bias;
            try
            {
                PlayerLod.Bias = 1.1f;   // High
                // the Classic camera: 46 degree field of view, 1440 p, at the v0.9.0 release edges (LOD0 from 400 px, LOD1 from 200 px)
                Assert.AreEqual(0, PlayerLod.LevelForPixels(PlayerLod.ScreenPixels(8.5f, 42f, 1440f), -1), "a carrier seen close by the follow camera (8.5 m, 42 degrees) is LOD0");
                Assert.AreEqual(0, PlayerLod.LevelForPixels(PlayerLod.ScreenPixels(8f, 46f, 1440f), -1), "a close-up is LOD0");
                Assert.AreEqual(1, PlayerLod.LevelForPixels(PlayerLod.ScreenPixels(14f, 46f, 1440f), -1), "the quarterback in the pocket (14 m) is LOD1");
                Assert.AreEqual(1, PlayerLod.LevelForPixels(PlayerLod.ScreenPixels(17f, 46f, 1440f), -1), "the near line of scrimmage (17 m) is LOD1");
                Assert.AreEqual(2, PlayerLod.LevelForPixels(PlayerLod.ScreenPixels(38f, 46f, 1440f), -1), "the secondary is LOD2 (the budget: LOD1 only for the near line)");
                Assert.AreEqual(2, PlayerLod.LevelForPixels(PlayerLod.ScreenPixels(90f, 46f, 1440f), -1));
                Assert.AreEqual(3, PlayerLod.LevelForPixels(PlayerLod.ScreenPixels(250f, 46f, 1440f), -1));
                Assert.AreEqual(3, PlayerLod.LevelForPixels(PlayerLod.ScreenPixels(250f, 46f, 1440f), -1));
            }
            finally { PlayerLod.Bias = bias; }
        }

        [Test]
        public void Lod_SizeGrowsWithAZoomedLensAndShrinksWithDistance()
        {
            float bias = PlayerLod.Bias;
            try
            {
                PlayerLod.Bias = 1f;
                float near = PlayerLod.ScreenPixels(20f, 46f, 1440f), far = PlayerLod.ScreenPixels(40f, 46f, 1440f);
                Assert.AreEqual(near / 2f, far, 0.5f, "double the distance, half the height");
                Assert.Greater(PlayerLod.ScreenPixels(40f, 20f, 1440f), far * 2f, "a long lens makes the same player bigger on screen");
                Assert.Greater(PlayerLod.ScreenPixels(40f, 46f, 2160f), far, "a taller screen resolves more detail (1440 p reference)");
            }
            finally { PlayerLod.Bias = bias; }
        }

        [Test]
        public void Lod_HasTwentyPercentHysteresisAroundTheEdges()
        {
            float edge = PlayerLod.Lod0Px;
            Assert.AreEqual(0, PlayerLod.LevelForPixels(edge * 0.9f, 0), "a LOD0 player stays LOD0 until 20 percent below the edge");
            Assert.AreEqual(1, PlayerLod.LevelForPixels(edge * 0.9f, 1), "a LOD1 player does not step up until 20 percent above the edge");
            Assert.AreEqual(1, PlayerLod.LevelForPixels(edge * 0.75f, 0), "past the band it steps down");
            Assert.AreEqual(0, PlayerLod.LevelForPixels(edge * 1.25f, 1), "and steps up again");
            Assert.AreEqual(1, PlayerLod.LevelForPixels(PlayerLod.Lod1Px * 1.5f, -1));
        }

        [Test]
        public void Lod_NoTierBiasCrushesTheSilhouette()
        {
            // GFX-406: the tier bias used to be 0.45 (Low) and 0.7 (Medium): LOD1 below 4 m. The floor is 0.8 and the quarterback at the Classic camera distance keeps LOD1 at least.
            float bias = PlayerLod.Bias;
            try
            {
                PlayerLod.Bias = 0.8f;
                Assert.LessOrEqual(PlayerLod.LevelForPixels(PlayerLod.ScreenPixels(12f, 46f, 1440f), -1), 1, "even on Low a quarterback 12 m from the camera is never worse than LOD1");
            }
            finally { PlayerLod.Bias = bias; }
        }

        // ------------------------------------------------------------------------------------------ GFX-395: kick camera framings
        [Test]
        public void KickCamera_FramingsMatchTheSpec()
        {
            float back = 17f, height = 9f, ahead = 0.5f, fov = 46f;
            UserCameraMath.KickFraming(1, ref back, ref height, ref ahead, ref fov);
            Assert.AreEqual(3.5f, height, 1e-4f); Assert.AreEqual(11f, back, 1e-4f);
            UserCameraMath.KickFraming(2, ref back, ref height, ref ahead, ref fov);
            Assert.AreEqual(2.4f, height, 1e-4f); Assert.AreEqual(8f, back, 1e-4f); Assert.AreEqual(38f, fov, 1e-4f);
            UserCameraMath.KickFraming(3, ref back, ref height, ref ahead, ref fov);
            Assert.Greater(height, 5f, "the punt camera is high behind the punter");
        }

        [Test]
        public void KickCamera_ReplacesTheScrimmageFramingBehindTheKicker()
        {
            var d = Vector3.forward; var los = new Vector3(0f, 0f, -10f);
            var scrim = UserCameraMath.Evaluate(new UserCameraInput { ViewDir = d, Los = los, Focus = los, PreSnap = true }, UserCameraPreset.Classic(), 0f, 0f, 0f, 1f);
            var fg = UserCameraMath.Evaluate(new UserCameraInput { ViewDir = d, Los = los, Focus = los, PreSnap = true, KickKind = 2 }, UserCameraPreset.Classic(), 0f, 0f, 0f, 1f);
            Assert.Less(fg.Position.y, scrim.Position.y - 5f, "the field goal camera is low");
            Assert.Less(fg.Fov, scrim.Fov, "and tight");
        }

        // ------------------------------------------------------------------------------------------ GFX-436: dirt wipe and kick plant
        [Test]
        public void Dirt_HalftimeWipeCleansFortyPercent()
        {
            var book = new DirtBook();
            book.Add(7, DirtEvent.TackledHard, 1f, DirtContext.Dry); book.Add(7, DirtEvent.Sacked, 1f, DirtContext.Dry);
            var before = book.Get(7);
            book.Wipe(0.4f);
            var after = book.Get(7);
            Assert.AreEqual(before.Seat * 0.6f, after.Seat, 1e-4f);
            Assert.AreEqual(before.Back * 0.6f, after.Back, 1e-4f);
            Assert.AreEqual(before.HelmetScuff, after.HelmetScuff, 1e-5f, "the helmet scuffs stay");
        }

        [Test]
        public void Dirt_KickPlantTouchesNeitherKneesNorSeat()
        {
            var s = new DirtState();
            DirtModel.Apply(ref s, DirtEvent.KickPlant, 1f, DirtContext.Dry);
            Assert.AreEqual(0f, s.Knees); Assert.AreEqual(0f, s.Seat);
        }

        // ------------------------------------------------------------------------------------------ GFX-410 / 414 / 415 / 441: weather defaults
        [Test]
        public void Weather_DayAndDuskSunsAreReadableAndTheMoonCastsAShadow()
        {
            var day = WeatherPresets.Get(WeatherKind.Clear, TimeOfDay.Day);
            var dusk = WeatherPresets.Get(WeatherKind.Clear, TimeOfDay.Dusk);
            var night = WeatherPresets.Get(WeatherKind.Clear, TimeOfDay.Night);
            Assert.That(day.SunElevation, Is.InRange(36f, 46f), "day: 38-44 degrees, not a 58 degree noon");
            Assert.That(dusk.SunElevation, Is.InRange(12f, 16f), "dusk: 13-15 degrees, not 5.5");
            Assert.GreaterOrEqual(night.SunLux, 250f, "the moon is more than a 90 lux fill");
        }

        [Test]
        public void Weather_LiveGradeIsCleanAndBright()
        {
            foreach (var k in WeatherPresets.Kinds)
                foreach (var t in WeatherPresets.Times)
                {
                    var p = WeatherPresets.Get(k, t);
                    Assert.AreEqual(0f, p.FilmGrain, 1e-5f, "no film grain in live play: " + p.Label);
                    Assert.LessOrEqual(p.Vignette, 0.2f, "vignette: " + p.Label);
                }
        }

        // ------------------------------------------------------------------------------------------ GFX-417: the upscaler default is Auto
        [Test]
        public void Upscaler_AutoIsTheFourthChoice()
        {
            Assert.AreEqual(4, PixSettings.UpscalerNames.Length);
            Assert.AreEqual("Auto", PixSettings.UpscalerNames[3]);
        }

        // ------------------------------------------------------------------------------------------ GFX-409: the tier decides which floodlights cast
        [Test]
        public void Tiers_FloodlightShadowsFollowTheTier()
        {
            try
            {
                QualityTiers.Override(0, -1f); Assert.AreEqual(0, QualityTiers.FloodShadowCount);
                QualityTiers.Override(1, -1f); Assert.AreEqual(0, QualityTiers.FloodShadowCount);
                QualityTiers.Override(2, -1f); Assert.AreEqual(2, QualityTiers.FloodShadowCount, "High: the two far rim lamps");
                QualityTiers.Override(3, -1f); Assert.AreEqual(4, QualityTiers.FloodShadowCount, "Ultra: four");
            }
            finally { QualityTiers.ClearOverride(); QualityTiers.ApplyScene(); }
        }

        [Test]
        public void Tiers_ShadowDistanceIsTheTiersOwn()
        {
            try
            {
                QualityTiers.Override(2, -1f); Assert.AreEqual(120f, QualityTiers.ShadowDistance, 0.01f);
                QualityTiers.Override(0, -1f); Assert.AreEqual(60f, QualityTiers.ShadowDistance, 0.01f);
            }
            finally { QualityTiers.ClearOverride(); QualityTiers.ApplyScene(); }
        }
    }
}
