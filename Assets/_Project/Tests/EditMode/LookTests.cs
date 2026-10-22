using System;
using System.Collections.Generic;
using NUnit.Framework;
using PixFootball.Core;

namespace PixFootball.Tests
{
    /// <summary>v0.6 graphics pass: dirt accumulation model, jersey number atlas mapping and the weather preset table (all pure data).</summary>
    public class LookTests
    {
        // ------------------------------------------------------------------------------------------ dirt model
        [Test]
        public void Dirt_StartsClean()
        {
            var s = new DirtState();
            Assert.IsTrue(s.IsClean);
            Assert.AreEqual(0f, s.Total);
        }

        [Test]
        public void Dirt_EventsAddToTheRightRegions()
        {
            var s = new DirtState();
            DirtModel.Apply(ref s, DirtEvent.Dove, 1f, DirtContext.Dry);
            Assert.Greater(s.Front, s.Back, "a dive soils the front, not the back");
            Assert.Greater(s.Knees, 0f);
            var t = new DirtState();
            DirtModel.Apply(ref t, DirtEvent.Sacked, 1f, DirtContext.Dry);
            Assert.Greater(t.Back, t.Front, "a sacked quarterback is driven onto his back and seat");
            Assert.Greater(t.Seat, t.Knees);
            var u = new DirtState();
            DirtModel.Apply(ref u, DirtEvent.TackledLow, 1f, DirtContext.Dry);
            Assert.Greater(u.Knees, u.Seat, "a shoestring tackle dirties the knees");
        }

        [Test]
        public void Dirt_AccumulatesMonotonicallyAndSaturates()
        {
            var s = new DirtState();
            float prev = 0f;
            for (int i = 0; i < 400; i++)
            {
                DirtModel.Apply(ref s, DirtEvent.Ragdoll, 1f, DirtContext.Dry);
                Assert.GreaterOrEqual(s.Total, prev - 1e-6f);
                prev = s.Total;
                Assert.LessOrEqual(s.Back, 1f); Assert.LessOrEqual(s.Knees, 1f); Assert.LessOrEqual(s.Seat, 1f); Assert.LessOrEqual(s.Front, 1f);
            }
            Assert.Greater(s.Back, 0.95f);
            Assert.AreEqual(400, s.Events);
        }

        [Test]
        public void Dirt_SaturationSlowsTheGain()
        {
            var s = new DirtState();
            DirtModel.Apply(ref s, DirtEvent.TackledHard, 1f, DirtContext.Dry);
            float first = s.Back;
            for (int i = 0; i < 6; i++) DirtModel.Apply(ref s, DirtEvent.TackledHard, 1f, DirtContext.Dry);
            float before = s.Back;
            DirtModel.Apply(ref s, DirtEvent.TackledHard, 1f, DirtContext.Dry);
            Assert.Less(s.Back - before, first, "a dirty region soils slower than a clean one");
        }

        [Test]
        public void Dirt_RainBuildsFasterAndTurnsToMud()
        {
            var dry = new DirtState(); var wet = new DirtState();
            var rain = new DirtContext { Wetness = 1f };
            DirtModel.Apply(ref dry, DirtEvent.TackledWrap, 1f, DirtContext.Dry);
            DirtModel.Apply(ref wet, DirtEvent.TackledWrap, 1f, rain);
            Assert.Greater(wet.Back, dry.Back * 1.5f);
            Assert.Greater(wet.Mud, dry.Mud);
            Assert.Less(wet.GrassFraction, dry.GrassFraction, "rain turns green grass stains into brown mud");
        }

        [Test]
        public void Dirt_ArtificialTurfStainsLessAndNeverMud()
        {
            var nat = new DirtState(); var art = new DirtState();
            var ctx = new DirtContext { Wetness = 0.8f, ArtificialTurf = true };
            DirtModel.Apply(ref nat, DirtEvent.Dove, 1f, new DirtContext { Wetness = 0.8f });
            DirtModel.Apply(ref art, DirtEvent.Dove, 1f, ctx);
            Assert.Less(art.Front, nat.Front);
            Assert.AreEqual(0f, DirtModel.MudFor(ctx));
        }

        [Test]
        public void Dirt_IntensityScalesTheEvent()
        {
            var a = new DirtState(); var b = new DirtState();
            DirtModel.Apply(ref a, DirtEvent.Fell, 0.2f, DirtContext.Dry);
            DirtModel.Apply(ref b, DirtEvent.Fell, 1f, DirtContext.Dry);
            Assert.Greater(b.Total, a.Total * 3f);
            var c = new DirtState();
            DirtModel.Apply(ref c, DirtEvent.None, 1f, DirtContext.Dry);
            Assert.IsTrue(c.IsClean);
        }

        [Test]
        public void Dirt_LineContactIsMildAndScuffsTheHelmet()
        {
            var s = new DirtState();
            for (int i = 0; i < 10; i++) DirtModel.Apply(ref s, DirtEvent.LineContact, 1f, DirtContext.Dry);
            Assert.Less(s.Max, 0.3f, "ten plays of line contact leave a lineman only lightly dirty");
            Assert.Greater(s.HelmetScuff, 0.05f);
        }

        [Test]
        public void DirtBook_KeepsPerPlayerStateForTheGame()
        {
            var book = new DirtBook();
            book.Add(7, DirtEvent.Dove, 1f, DirtContext.Dry);
            book.Add(7, DirtEvent.Fell, 1f, DirtContext.Dry);
            book.Add(9, DirtEvent.Sacked, 1f, DirtContext.Dry);
            Assert.AreEqual(2, book.Count);
            Assert.AreEqual(2, book.Get(7).Events);
            Assert.AreEqual(1, book.Get(9).Events);
            Assert.IsTrue(book.Get(123).IsClean);
            Assert.IsFalse(book.Has(123));
            book.Clear();
            Assert.AreEqual(0, book.Count);
        }

        [Test]
        public void Dirt_ShaderRegionsAreBoundedAndMonotonic()
        {
            float lo = -1f;
            for (float v = 0f; v <= 1.0001f; v += 0.05f)
            {
                var s = new DirtState { Knees = v, Seat = v, Front = v, Back = v };
                float k, st, f, b; DirtModel.ShaderRegions(s, out k, out st, out f, out b);
                Assert.GreaterOrEqual(k, lo); Assert.LessOrEqual(k, 1.0001f); lo = k;
            }
        }

        // ------------------------------------------------------------------------------------------ number atlas
        [Test]
        public void NumberAtlas_CellsTileTheAtlas()
        {
            var seen = new HashSet<int>();
            float area = 0f;
            for (int d = 0; d < 10; d++)
            {
                float u0, v0, u1, v1; NumberAtlas.CellRect(d, out u0, out v0, out u1, out v1);
                Assert.GreaterOrEqual(u0, 0f); Assert.LessOrEqual(u1, 1.0001f); Assert.GreaterOrEqual(v0, 0f); Assert.LessOrEqual(v1, 1.0001f);
                Assert.AreEqual(0.2f, u1 - u0, 1e-5f); Assert.AreEqual(0.5f, v1 - v0, 1e-5f);
                seen.Add((int)Math.Round(u0 * 5) * 10 + (int)Math.Round(v0 * 2));
                area += (u1 - u0) * (v1 - v0);
            }
            Assert.AreEqual(10, seen.Count, "every digit has its own cell");
            Assert.AreEqual(1f, area, 1e-4f);
        }

        [Test]
        public void NumberAtlas_DigitsAndCounts()
        {
            int t, o;
            NumberAtlas.Digits(7, out t, out o); Assert.AreEqual(-1, t); Assert.AreEqual(7, o); Assert.AreEqual(1, NumberAtlas.DigitCount(7));
            NumberAtlas.Digits(10, out t, out o); Assert.AreEqual(1, t); Assert.AreEqual(0, o); Assert.AreEqual(2, NumberAtlas.DigitCount(10));
            NumberAtlas.Digits(99, out t, out o); Assert.AreEqual(9, t); Assert.AreEqual(9, o);
            NumberAtlas.Digits(0, out t, out o); Assert.AreEqual(-1, t); Assert.AreEqual(0, o);
            Assert.AreEqual(99, NumberAtlas.Clamp(250)); Assert.AreEqual(0, NumberAtlas.Clamp(-5));
        }

        [Test]
        public void NumberAtlas_TwoDigitLayoutIsCenteredAndOrdered()
        {
            Assert.AreEqual(0f, NumberAtlas.SlotCenter(1, 0));
            Assert.AreEqual(-NumberAtlas.SlotCenter(2, 1), NumberAtlas.SlotCenter(2, 0), 1e-6f);
            Assert.Less(NumberAtlas.SlotCenter(2, 0), NumberAtlas.SlotCenter(2, 1), "tens digit left of the ones digit");
            Assert.AreEqual(NumberAtlas.Advance, NumberAtlas.SlotCenter(2, 1) - NumberAtlas.SlotCenter(2, 0), 1e-6f);
            Assert.AreEqual(2f * NumberAtlas.Advance, NumberAtlas.Width(42), 1e-6f);
        }

        [Test]
        public void NumberAtlas_SampleMapsDigitCentresToTheirCells()
        {
            // number 23: the tens digit (2) is left of centre, the ones digit (3) right of it
            float u, v;
            Assert.IsTrue(NumberAtlas.Sample(NumberAtlas.SlotCenter(2, 0), 0f, 23, out u, out v));
            float u0, v0, u1, v1; NumberAtlas.CellRect(2, out u0, out v0, out u1, out v1);
            Assert.AreEqual((u0 + u1) * 0.5f, u, 1e-5f); Assert.AreEqual((v0 + v1) * 0.5f, v, 1e-5f);
            Assert.IsTrue(NumberAtlas.Sample(NumberAtlas.SlotCenter(2, 1), 0f, 23, out u, out v));
            NumberAtlas.CellRect(3, out u0, out v0, out u1, out v1);
            Assert.AreEqual((u0 + u1) * 0.5f, u, 1e-5f);
            // a digit from the bottom row
            Assert.IsTrue(NumberAtlas.Sample(0f, 0f, 8, out u, out v));
            NumberAtlas.CellRect(8, out u0, out v0, out u1, out v1);
            Assert.AreEqual((v0 + v1) * 0.5f, v, 1e-5f); Assert.Less(v, 0.5f);
            // outside the number
            Assert.IsFalse(NumberAtlas.Sample(0.9f, 0f, 8, out u, out v));
            Assert.IsFalse(NumberAtlas.Sample(0f, 0.6f, 8, out u, out v));
        }

        [Test]
        public void NumberAtlas_CellEdgesStayInsideTheCell()
        {
            float u, v, u0, v0, u1, v1;
            for (int d = 0; d < 10; d++)
            {
                NumberAtlas.CellRect(d, out u0, out v0, out u1, out v1);
                Assert.IsTrue(NumberAtlas.Sample(-0.499f * NumberAtlas.Advance, -0.499f, d, out u, out v));
                Assert.GreaterOrEqual(u, u0 - 1e-5f); Assert.LessOrEqual(u, u1 + 1e-5f); Assert.GreaterOrEqual(v, v0 - 1e-5f); Assert.LessOrEqual(v, v1 + 1e-5f);
                Assert.IsTrue(NumberAtlas.Sample(0.499f * NumberAtlas.Advance, 0.499f, d, out u, out v));
                Assert.GreaterOrEqual(u, u0 - 1e-5f); Assert.LessOrEqual(u, u1 + 1e-5f);
            }
        }

        [Test]
        public void NumberAtlas_PlacementTransformRoundTrips()
        {
            var p = new NumberPlacement(0.4f, 0.7f, 0.12f, (float)(Math.PI / 2.0));
            float x, y;
            NumberAtlas.ToNumberSpace(p, 0.4f, 0.7f, out x, out y);
            Assert.AreEqual(0f, x, 1e-5f); Assert.AreEqual(0f, y, 1e-5f);
            // a point one number-height along the placement's rotated "up" axis lands at y = 1
            NumberAtlas.ToNumberSpace(p, 0.4f - 0.12f, 0.7f, out x, out y);   // rotation +90: up in number space points toward -u
            Assert.AreEqual(1f, y, 1e-4f); Assert.AreEqual(0f, x, 1e-4f);
            // a point inside the glyph
            float au, av;
            Assert.IsTrue(NumberAtlas.Map(p, 10, 0.4f, 0.7f, out au, out av));
        }

        [Test]
        public void NumberAtlas_MirrorFlipsHorizontally()
        {
            var a = new NumberPlacement(0.5f, 0.5f, 0.1f, 0f, false);
            var b = new NumberPlacement(0.5f, 0.5f, 0.1f, 0f, true);
            float xa, ya, xb, yb;
            NumberAtlas.ToNumberSpace(a, 0.53f, 0.5f, out xa, out ya);
            NumberAtlas.ToNumberSpace(b, 0.53f, 0.5f, out xb, out yb);
            Assert.AreEqual(-xa, xb, 1e-5f); Assert.AreEqual(ya, yb, 1e-5f);
            float pa, pb, pc, pd; b.Pack(out pa, out pb, out pc, out pd);
            Assert.Less(pc, 0f, "mirrored placements pack a negative height for the shader");
        }


        // ------------------------------------------------------------------------------------------ text atlas (names / wordmark)
        [Test]
        public void TextAtlas_IndexMapsLettersAndPunctuation()
        {
            Assert.AreEqual(0, TextAtlas.Index('A')); Assert.AreEqual(25, TextAtlas.Index('Z')); Assert.AreEqual(2, TextAtlas.Index('c'));
            Assert.AreEqual(26, TextAtlas.Index('.')); Assert.AreEqual(27, TextAtlas.Index('-')); Assert.AreEqual(28, TextAtlas.Index('\''));
            Assert.AreEqual(TextAtlas.Blank, TextAtlas.Index(' ')); Assert.AreEqual(TextAtlas.Blank, TextAtlas.Index('7'));
        }

        [Test]
        public void TextAtlas_CleanUpperCasesTrimsAndCuts()
        {
            Assert.AreEqual("J. DOE", TextAtlas.Clean("  j.  doe "));
            Assert.AreEqual("O'NEAL", TextAtlas.Clean("o'neal"));
            Assert.AreEqual("", TextAtlas.Clean(null));
            Assert.AreEqual(TextAtlas.MaxChars, TextAtlas.Clean("ABCDEFGHIJKLMNOPQRSTUVWXYZ").Length);
            Assert.AreEqual("AB", TextAtlas.Clean("A1B"), "digits are not letters: dropped, the name stays readable");
        }

        [Test]
        public void TextAtlas_PackFillsSlotsAndPadsWithMinusOne()
        {
            var slots = new float[TextAtlas.MaxChars];
            int n = TextAtlas.Pack("ab c", slots);
            Assert.AreEqual(4, n);
            Assert.AreEqual(0f, slots[0]); Assert.AreEqual(1f, slots[1]); Assert.AreEqual((float)TextAtlas.Blank, slots[2]); Assert.AreEqual(2f, slots[3]); Assert.AreEqual(-1f, slots[4]);
        }

        [Test]
        public void TextAtlas_CellsStayInsideTheAtlas()
        {
            for (int i = 0; i < TextAtlas.Columns * TextAtlas.Rows; i++)
            {
                float u0, v0, u1, v1; TextAtlas.CellRect(i, out u0, out v0, out u1, out v1);
                Assert.GreaterOrEqual(u0, 0f); Assert.LessOrEqual(u1, 1.0001f); Assert.GreaterOrEqual(v0, -1e-5f); Assert.LessOrEqual(v1, 1.0001f);
                Assert.AreEqual(1f / TextAtlas.Columns, u1 - u0, 1e-5f);
            }
        }

        [Test]
        public void TextAtlas_SqueezeFitsLongNamesAndLeavesShortOnes()
        {
            Assert.AreEqual(1f, TextAtlas.Squeeze(5, 0.1f, 1f));
            float s = TextAtlas.Squeeze(16, 0.1f, 0.5f);
            Assert.Less(s, 1f); Assert.LessOrEqual(TextAtlas.Width(16, s) * 0.1f, 0.5f + 1e-4f);
            Assert.GreaterOrEqual(TextAtlas.Squeeze(40, 1f, 0.1f), 0.35f, "never squeezed to nothing");
        }

        [Test]
        public void TextAtlas_SampleFindsTheRightCharacterCell()
        {
            // "AB": two cells centred on the origin, A on the left half, B on the right
            float u, v, u0, v0, u1, v1;
            float adv = TextAtlas.Advance;
            Assert.IsTrue(TextAtlas.Sample(-0.5f * adv, 0f, "AB", 1f, out u, out v));
            TextAtlas.CellRect(0, out u0, out v0, out u1, out v1);
            Assert.AreEqual((u0 + u1) * 0.5f, u, 1e-4f); Assert.AreEqual((v0 + v1) * 0.5f, v, 1e-4f);
            Assert.IsTrue(TextAtlas.Sample(0.5f * adv, 0f, "AB", 1f, out u, out v));
            TextAtlas.CellRect(1, out u0, out v0, out u1, out v1);
            Assert.AreEqual((u0 + u1) * 0.5f, u, 1e-4f);
            Assert.IsFalse(TextAtlas.Sample(1.2f * adv, 0f, "AB", 1f, out u, out v), "outside the line");
            Assert.IsFalse(TextAtlas.Sample(0f, 0.7f, "AB", 1f, out u, out v), "above the line");
            Assert.IsFalse(TextAtlas.Sample(0f, 0f, "A B", 1f, out u, out v), "a space cell draws nothing");
        }
        // ------------------------------------------------------------------------------------------ weather presets
        [Test]
        public void Weather_AllFifteenCombinationsExistAndAreSane()
        {
            int n = 0;
            foreach (var k in WeatherPresets.Kinds)
                foreach (var t in WeatherPresets.Times)
                {
                    var p = WeatherPresets.Get(k, t);
                    n++;
                    Assert.AreEqual(k, p.Kind); Assert.AreEqual(t, p.Time);
                    Assert.Greater(p.SunLux, 0f); Assert.Greater(p.ExposureEV, 5f); Assert.Less(p.ExposureEV, 16f);
                    Assert.GreaterOrEqual(p.Wetness, 0f); Assert.LessOrEqual(p.Wetness, 1f);
                    Assert.GreaterOrEqual(p.RainIntensity, 0f); Assert.LessOrEqual(p.RainIntensity, 1f);
                    Assert.GreaterOrEqual(p.CloudCover, 0f); Assert.LessOrEqual(p.CloudCover, 1f);
                    Assert.Greater(p.FogMeanFreePath, 20f);
                    Assert.GreaterOrEqual(p.BallSlickness, 0f); Assert.LessOrEqual(p.BallSlickness, 1f);
                    Assert.Greater(p.FieldIlluminance, 100f);
                    Assert.IsFalse(string.IsNullOrEmpty(p.Label));
                }
            Assert.AreEqual(15, n);
        }

        [Test]
        public void Weather_TimeOfDayRules()
        {
            Assert.IsFalse(WeatherPresets.Get(WeatherKind.Clear, TimeOfDay.Day).FloodlightsOn, "a clear day game plays under the sun");
            Assert.IsTrue(WeatherPresets.Get(WeatherKind.Clear, TimeOfDay.Dusk).FloodlightsOn, "dusk: floodlights come on");
            Assert.IsTrue(WeatherPresets.Get(WeatherKind.Clear, TimeOfDay.Night).FloodlightsOn);
            var day = WeatherPresets.Get(WeatherKind.Clear, TimeOfDay.Day);
            var dusk = WeatherPresets.Get(WeatherKind.Clear, TimeOfDay.Dusk);
            Assert.Less(dusk.SunElevation, day.SunElevation / 4f, "dusk sun is low");
            Assert.Greater(dusk.Temperature, day.Temperature, "dusk grade is warmer");
            Assert.IsFalse(WeatherPresets.Get(WeatherKind.Clear, TimeOfDay.Night).PhysicallyBasedSky, "night uses the gradient sky");
        }

        [Test]
        public void Weather_RainSnowFogDefineTheirSurfaces()
        {
            var rain = WeatherPresets.Get(WeatherKind.Rain, TimeOfDay.Night);
            Assert.Greater(rain.RainIntensity, 0.5f); Assert.Greater(rain.Wetness, 0.8f); Assert.Greater(rain.Puddles, 0.5f); Assert.Greater(rain.BallSlickness, 0.3f);
            Assert.IsTrue(rain.VolumetricFog);
            var snow = WeatherPresets.Get(WeatherKind.Snow, TimeOfDay.Day);
            Assert.Greater(snow.SnowfallIntensity, 0.5f); Assert.Greater(snow.SnowTarget, snow.SnowStart); Assert.AreEqual(0f, snow.RainIntensity); Assert.IsTrue(snow.BreathVapor);
            var fog = WeatherPresets.Get(WeatherKind.Fog, TimeOfDay.Night);
            Assert.Less(fog.FogMeanFreePath, WeatherPresets.Get(WeatherKind.Clear, TimeOfDay.Night).FogMeanFreePath);
            var clear = WeatherPresets.Get(WeatherKind.Clear, TimeOfDay.Day);
            Assert.AreEqual(0f, clear.Wetness); Assert.AreEqual(0f, clear.SnowfallIntensity); Assert.AreEqual(0f, clear.BallSlickness);
            Assert.Less(WeatherPresets.Get(WeatherKind.Overcast, TimeOfDay.Day).SunLux, clear.SunLux);
        }

        [Test]
        public void Weather_ChoiceResolutionIsDeterministicAndCoversAllValues()
        {
            Assert.AreEqual(WeatherKind.Rain, WeatherPresets.Resolve(WeatherChoice.Rain, new Rng(1)));
            Assert.AreEqual(TimeOfDay.Dusk, WeatherPresets.Resolve(TimeChoice.Dusk, new Rng(1)));
            var kinds = new HashSet<WeatherKind>(); var times = new HashSet<TimeOfDay>();
            for (int s = 0; s < 400; s++) { var r = new Rng(s); kinds.Add(WeatherPresets.Resolve(WeatherChoice.Random, r)); times.Add(WeatherPresets.Resolve(TimeChoice.Random, r)); }
            Assert.AreEqual(5, kinds.Count); Assert.AreEqual(3, times.Count);
            Assert.AreEqual(WeatherPresets.Resolve(WeatherChoice.Random, new Rng(77)), WeatherPresets.Resolve(WeatherChoice.Random, new Rng(77)));
            Assert.AreEqual(6, WeatherPresets.WeatherChoiceNames.Length); Assert.AreEqual(4, WeatherPresets.TimeChoiceNames.Length);
        }

        [Test]
        public void Weather_RandomIsMostlyFairWeather()
        {
            int fair = 0, n = 2000;
            for (int s = 0; s < n; s++) { var k = WeatherPresets.Resolve(WeatherChoice.Random, new Rng(s * 7 + 3)); if (k == WeatherKind.Clear || k == WeatherKind.Overcast) fair++; }
            Assert.Greater(fair / (float)n, 0.55f);
            Assert.Less(fair / (float)n, 0.85f);
        }

        [Test]
        public void Climate_SnowBuildsUpOverTheGameAndStopsAtTheTarget()
        {
            var c = new WeatherClimate(WeatherPresets.Get(WeatherKind.Snow, TimeOfDay.Night));
            float start = c.Snow;
            c.Advance(600f);
            Assert.Greater(c.Snow, start);
            for (int i = 0; i < 200; i++) c.Advance(60f);
            Assert.AreEqual(c.Preset.SnowTarget, c.Snow, 1e-5f);
            var clear = new WeatherClimate(WeatherPresets.Get(WeatherKind.Clear, TimeOfDay.Day));
            clear.Advance(3600f);
            Assert.AreEqual(0f, clear.Snow);
        }

        [Test]
        public void Climate_SurfacesDryOutSlowlyWhenTheRainStops()
        {
            var c = new WeatherClimate(WeatherPresets.Get(WeatherKind.Rain, TimeOfDay.Day));
            Assert.AreEqual(1f, c.Wet, 1e-5f);
            var dry = new WeatherPreset(); dry.Wetness = 0f;
            c.Set(dry);
            Assert.AreEqual(0f, c.Wet);
            c.Set(WeatherPresets.Get(WeatherKind.Rain, TimeOfDay.Day));
            c.Advance(5f);
            Assert.AreEqual(1f, c.Wet, 1e-4f);
        }

        [Test]
        public void Weather_PresetIndexIsUnique()
        {
            var set = new HashSet<int>();
            foreach (var k in WeatherPresets.Kinds) foreach (var t in WeatherPresets.Times) Assert.IsTrue(set.Add(WeatherPresets.Index(k, t)));
            Assert.AreEqual(15, set.Count);
        }
    }
}
